using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core
{
    /// <summary>
    /// Catalog of all mode + map definitions.
    /// The mode/map picker in the admin preferences panel and the mode HUD mapping read this.
    /// ⚠ The asset lives at `Assets/_Shared/Data/Resources/GameCatalog.asset` — since the procedural
    /// admin UI has no `[SerializeField]`, it is loaded via `Resources.Load<GameCatalog>("GameCatalog")`.
    /// If it is moved out of that folder the admin mode/map picker stays empty.
    /// <para>
    /// THE SERVER DOES NOT NEED THE CATALOG: admin only sends <c>start_match{modeId, sceneName}</c>;
    /// the server matches modId against its own IGameMode registrations, rejecting and logging unknown ones.
    /// </para>
    /// All queries are resilient to null/empty input (a missing asset reference must not break the UI).
    /// </summary>
    [CreateAssetMenu(fileName = "GameCatalog", menuName = "VortexArena/Game Catalog")]
    public class GameCatalog : ScriptableObject
    {
        [SerializeField] private ModeDefinition[] modes = Array.Empty<ModeDefinition>();
        [SerializeField] private MapDefinition[] maps = Array.Empty<MapDefinition>();

        /// <summary>Mode definitions in the catalog.</summary>
        public ModeDefinition[] Modes => modes;

        /// <summary>Map definitions in the catalog.</summary>
        public MapDefinition[] Maps => maps;

        /// <summary>Finds a mode definition by modId; null if not found.</summary>
        public ModeDefinition FindMode(string modeId)
        {
            if (string.IsNullOrEmpty(modeId) || modes == null)
            {
                return null;
            }

            for (int i = 0; i < modes.Length; i++)
            {
                ModeDefinition mode = modes[i];
                if (mode != null && string.Equals(mode.ModeId, modeId, StringComparison.OrdinalIgnoreCase))
                {
                    return mode;
                }
            }

            return null;
        }

        /// <summary>Finds a map definition by scene name; null if not found.</summary>
        public MapDefinition FindMap(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || maps == null)
            {
                return null;
            }

            for (int i = 0; i < maps.Length; i++)
            {
                MapDefinition map = maps[i];
                if (map != null && string.Equals(map.SceneName, sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return map;
                }
            }

            return null;
        }

        /// <summary>
        /// Maps playable in the given mode: the mode's own list if it is non-empty, otherwise
        /// every map in the catalog is scanned; each candidate additionally passes through the
        /// <see cref="MapDefinition.SupportsMode"/> filter.
        /// </summary>
        public List<MapDefinition> MapsForMode(string modeId)
        {
            var result = new List<MapDefinition>();
            if (string.IsNullOrEmpty(modeId))
            {
                return result;
            }

            ModeDefinition mode = FindMode(modeId);
            MapDefinition[] pool = mode != null && mode.Maps != null && mode.Maps.Length > 0 ? mode.Maps : maps;
            if (pool == null)
            {
                return result;
            }

            for (int i = 0; i < pool.Length; i++)
            {
                MapDefinition map = pool[i];
                if (map == null || !map.SupportsMode(modeId) || result.Contains(map))
                {
                    continue;
                }

                result.Add(map);
            }

            return result;
        }

        /// <summary>Maps belonging to a game type (§11) — the operator's FIRST pick list.</summary>
        public List<MapDefinition> MapsForGameType(GameType type)
        {
            var result = new List<MapDefinition>();
            if (maps == null)
            {
                return result;
            }

            for (int i = 0; i < maps.Length; i++)
            {
                MapDefinition map = maps[i];
                if (map != null && map.GameType == type && !result.Contains(map))
                {
                    result.Add(map);
                }
            }

            return result;
        }

        /// <summary>Round types startable on <paramref name="map"/>: a startable mode (not a lobby
        /// profile) of the SAME game type that the map also lists as supported.</summary>
        public List<ModeDefinition> ModesForMap(MapDefinition map)
        {
            var result = new List<ModeDefinition>();
            if (map == null || modes == null)
            {
                return result;
            }

            for (int i = 0; i < modes.Length; i++)
            {
                ModeDefinition mode = modes[i];
                if (mode == null || mode.IsLobbyProfile || mode.GameType != map.GameType ||
                    !map.SupportsMode(mode.ModeId) || result.Contains(mode))
                {
                    continue;
                }

                result.Add(mode);
            }

            return result;
        }
    }
}
