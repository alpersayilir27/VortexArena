using System;
using UnityEngine;

namespace VortexArena.Core
{
    /// <summary>
    /// The selected mode of a <b>not yet started</b> match (§5.3 <c>selection_state</c>) —
    /// PRESENTATION only.
    /// <para>
    /// ⚠️ Not to be confused with <see cref="ModeRuntime"/>: that one holds the rules of the
    /// <b>running</b> match and its authority is <c>load_match.rules</c>. This one is the "what is
    /// selected in the admin panel" information; it changes no rule, HUD or loadout — the match type
    /// only changes via <c>start_match</c>.
    /// </para>
    /// <para>
    /// <b>Why it exists:</b> while waiting in the lobby (and when admin stages an arena) whether the
    /// base strips are visible depends on the selected mode — in a team mode (TDM/tournament) the
    /// strips are needed, in a teamless one (FFA) they are misleading. Since the active rule at that
    /// moment is the lobby profile, this information cannot be read from anywhere else. Consumers:
    /// <c>Arena.BaseZoneVisibility</c> (team mode), the weapon gate (<see cref="IsKidsGame"/>,
    /// <see cref="GrantsRandomWeapon"/>) and <c>RemoteAvatar</c> (the selected mode's player bodies,
    /// <see cref="FindDefinition"/>).
    /// </para>
    /// <para>
    /// ⚠️ <b>No field without a consumer is added here</b>: the rest of the selection (map, duration,
    /// limit) belongs to the operator and goes only to admins via <c>admin_state</c>.
    /// </para>
    /// </summary>
    public static class ModeSelection
    {
        /// <summary>Catalog resource name (without extension) — the same asset <see cref="ModeRuntime"/>
        /// and the admin UI read.</summary>
        private const string CatalogResourceName = "GameCatalog";

        /// <summary>Whether the server ever reported a selection. <c>false</c> = old server (or no
        /// connection): the consumer falls back to the active rule, because "unknown" and "teamless"
        /// are not the same thing.</summary>
        public static bool HasValue { get; private set; }

        /// <summary>Selected mode id (<c>"lobby"</c> at startup). Only a catalog key
        /// (<see cref="FindDefinition"/>) — no <c>if (modeId == …)</c> chain is written on the client
        /// (§10.5).</summary>
        public static string ModeId { get; private set; } = "";

        /// <summary>Whether the selected mode is teamless (<c>teamMode:"none"</c>).</summary>
        public static bool IsTeamless { get; private set; }

        /// <summary>Whether the selected mode belongs to the CHILDREN's family
        /// (<see cref="GameType.Kids"/>, §11). Consumer: the weapon gate — a kids session must arm
        /// nobody, even while waiting in a military lobby whose own profile hands out weapons.
        /// <para>⚠️ Resolved from the CATALOG, not from the wire: the family is authored data
        /// (<c>ModeDefinition.gameType</c>) and the selection carries only the id. An unknown mode
        /// answers <c>false</c> — "unknown" must not disarm a competitive lobby.</para></summary>
        public static bool IsKidsGame { get; private set; }

        /// <summary>Whether the selected mode hands out a random weapon
        /// (<see cref="ModeWeaponSource.RandomGrant"/>, e.g. FFA). Consumer: the weapon gate — the
        /// racks go while the arena is staged, not only at <c>start_match</c>.
        /// <para>⚠️ Catalog-resolved like <see cref="IsKidsGame"/>; an unknown mode answers
        /// <c>false</c> and keeps the racks.</para></summary>
        public static bool GrantsRandomWeapon { get; private set; }

        /// <summary>Raised when the selection actually changes (SILENT on a repeat of the same value —
        /// the message can arrive on every connection and on every selection command).</summary>
        public static event Action Changed;

        /// <summary>Applies the selection coming from the wire. <paramref name="teamMode"/> is the
        /// §10.5 vocabulary; every value other than <c>"none"</c> (including empty) counts as
        /// team-based — the rule that an unknown value falls back to the default.</summary>
        public static void Apply(string modeId, string teamMode)
        {
            Set(true, modeId ?? "", string.Equals(teamMode, "none", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Catalog definition of the SELECTED mode; null when nothing was reported or the mode
        /// is unknown. Content only (e.g. player bodies) — never a rule.</summary>
        public static ModeDefinition FindDefinition()
        {
            return HasValue ? FindMode(ModeId) : null;
        }

        /// <summary>Connection lost / session ended: returns to "unknown" so the consumer falls back
        /// to the active rule again.</summary>
        public static void Reset()
        {
            Set(false, "", false);
        }

        private static void Set(bool hasValue, string modeId, bool teamless)
        {
            // The catalog is consulted only on an id CHANGE: selection_state repeats on every
            // connection and every operator command.
            bool sameMode = HasValue && modeId == ModeId;
            bool kids = IsKidsGame;
            bool grants = GrantsRandomWeapon;
            if (!sameMode)
            {
                ModeDefinition mode = hasValue ? FindMode(modeId) : null;
                kids = mode != null && mode.GameType == GameType.Kids;
                grants = mode != null && mode.Weapons == ModeWeaponSource.RandomGrant;
            }

            bool changed = hasValue != HasValue || modeId != ModeId || teamless != IsTeamless ||
                           kids != IsKidsGame || grants != GrantsRandomWeapon;

            HasValue = hasValue;
            ModeId = modeId;
            IsTeamless = teamless;
            IsKidsGame = kids;
            GrantsRandomWeapon = grants;

            if (changed)
            {
                Changed?.Invoke();
            }
        }

        /// <summary>Catalog definition of a mode id (same asset the admin UI reads).</summary>
        private static ModeDefinition FindMode(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
            {
                return null;
            }

            GameCatalog catalog = Resources.Load<GameCatalog>(CatalogResourceName);
            return catalog != null ? catalog.FindMode(modeId) : null;
        }
    }
}
