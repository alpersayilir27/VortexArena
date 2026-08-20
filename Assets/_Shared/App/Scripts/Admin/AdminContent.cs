using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Arena;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// The admin UI's access to the content catalog (modes/maps).
    /// <para>
    /// The fully procedural admin UI cannot wire the catalog with <c>[SerializeField]</c>, so
    /// <c>Assets/_Shared/Data/Resources/GameCatalog.asset</c> is loaded once via
    /// <see cref="Resources.Load{T}(string)"/>. ⚠️ Moving that asset leaves the mode/map picker
    /// empty (with a warning) and matches can only be started from the server console.
    /// </para>
    /// </summary>
    public static class AdminContent
    {
        /// <summary>The catalog's name under Resources (without extension).</summary>
        public const string CatalogResourceName = "GameCatalog";

        private static GameCatalog _catalog;
        private static bool _loadAttempted;

        /// <summary>The content catalog; null when not found (a warning is printed once).</summary>
        public static GameCatalog Catalog
        {
            get
            {
                if (_catalog != null || _loadAttempted)
                {
                    return _catalog;
                }

                _loadAttempted = true;
                _catalog = Resources.Load<GameCatalog>(CatalogResourceName);
                if (_catalog == null)
                {
                    Debug.LogWarning(
                        $"[AdminContent] '{CatalogResourceName}' Resources altında bulunamadı " +
                        "(beklenen yer: Assets/_Shared/Data/Resources/GameCatalog.asset); " +
                        "mod/harita seçicisi boş kalacak.");
                }

                return _catalog;
            }
        }

        /// <summary>The <b>startable</b> catalog modes: non-empty modId, not the lobby profile. The
        /// lobby profile (§10.7) must exist in the catalog (the client resolves its loadout from it)
        /// but has no server-side <c>IGameMode</c>, so in the picker it would be a button that is
        /// silently rejected every time.</summary>
        public static void CollectModes(List<ModeDefinition> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();
            ModeDefinition[] modes = Catalog != null ? Catalog.Modes : null;
            if (modes == null)
            {
                return;
            }

            for (int i = 0; i < modes.Length; i++)
            {
                if (modes[i] != null && !string.IsNullOrEmpty(modes[i].ModeId) && !modes[i].IsLobbyProfile)
                {
                    buffer.Add(modes[i]);
                }
            }
        }

        /// <summary>
        /// Maps on which the given mode can be played <b>in this venue</b> (scene name non-empty).
        /// <para>Two filters: mode compatibility from the catalog, then the venue via
        /// <see cref="AdminSelection.IsInVenue"/> (§11). The catalog knows the whole project, but
        /// the server decides which arenas are playable, so the venue filter is never produced
        /// locally — it arrives with <c>admin_state</c>.</para>
        /// </summary>
        public static void CollectMaps(string modeId, List<MapDefinition> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();
            if (Catalog == null || string.IsNullOrEmpty(modeId))
            {
                return;
            }

            List<MapDefinition> maps = Catalog.MapsForMode(modeId);
            if (maps == null)
            {
                return;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i] != null && !string.IsNullOrEmpty(maps[i].SceneName) &&
                    AdminSelection.IsInVenue(maps[i].SceneName))
                {
                    buffer.Add(maps[i]);
                }
            }
        }

        /// <summary>
        /// This venue's <b>lobby</b> map — the single map with <c>supportedModeIds == ["lobby"]</c>
        /// (§10.7), or null.
        /// <para>
        /// Same criterion as the server's <c>MapTable.ResolveLobbyScene</c>, alphabetically first
        /// among candidates, so both sides pick the same scene. The venue filter applies here too;
        /// until the first <c>admin_state</c> every business' lobby looks like a candidate and the
        /// list is rebuilt when the filter arrives.
        /// </para>
        /// <para>
        /// ⚠️ The lobby is NOT in <see cref="CollectMaps"/>: no match is played there, and the
        /// lobby row in the panel is the <c>return_to_lobby</c> command, not a map selection.
        /// </para>
        /// </summary>
        public static MapDefinition ResolveLobbyMap()
        {
            MapDefinition[] maps = Catalog != null ? Catalog.Maps : null;
            if (maps == null)
            {
                return null;
            }

            MapDefinition best = null;
            for (int i = 0; i < maps.Length; i++)
            {
                MapDefinition map = maps[i];
                if (map == null || string.IsNullOrEmpty(map.SceneName) || !IsLobbyMap(map) ||
                    !AdminSelection.IsInVenue(map.SceneName))
                {
                    continue;
                }

                if (best == null || string.CompareOrdinal(map.SceneName, best.SceneName) < 0)
                {
                    best = map;
                }
            }

            return best;
        }

        /// <summary>
        /// Is this scene a <b>lobby</b> scene (asked from the catalog).
        /// <para>
        /// ⚠️ Deliberately NOT a comparison with <see cref="ResolveLobbyMap"/>'s scene: that depends
        /// on the venue filter and may point at another business' lobby before the first
        /// <c>admin_state</c>. Reading the catalog definition keeps the answer correct regardless of
        /// the connection state.
        /// </para>
        /// </summary>
        public static bool IsLobbyScene(string sceneName)
        {
            MapDefinition map = Catalog != null ? Catalog.FindMap(sceneName) : null;
            return map != null && IsLobbyMap(map);
        }

        /// <summary>Lobby map = the ONLY supported mode is <c>lobby</c>. ⚠️ An empty list means
        /// "unrestricted" (<see cref="MapDefinition.SupportsMode"/>), which is the opposite of a
        /// lobby: unrestricted maps play in every mode, a lobby in none.</summary>
        public static bool IsLobbyMap(MapDefinition map)
        {
            if (map == null)
            {
                return false;
            }

            string[] modes = map.SupportedModeIds;
            return modes != null && modes.Length == 1 &&
                   string.Equals(modes[0], ArenaProtocol.LOBBY_MODE_ID,
                       System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The mode's display name, or the modId when it is not in the catalog.</summary>
        public static string ModeDisplayName(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
            {
                return "-";
            }

            ModeDefinition mode = Catalog != null ? Catalog.FindMode(modeId) : null;
            return mode != null && !string.IsNullOrEmpty(mode.DisplayName) ? mode.DisplayName : modeId;
        }
    }
}
