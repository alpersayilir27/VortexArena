using System;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Core
{
    /// <summary>
    /// The <b>single read point</b> for the active match's rules (Docs/ArenaNet-Protokol.md §10.5).
    /// <para>
    /// <b>Authority lives on the server:</b> the values arrive via <c>load_match.rules</c> /
    /// <c>welcome.match.rules</c>. There is NO <c>if (modeId == "…")</c> chain on the client — adding
    /// a new mode does not change client code.
    /// </para>
    /// <para>
    /// <b>Why a single point:</b> revive (<c>PlayerCombatState</c>), the score row (<c>ModeHudBase</c>),
    /// the weapon source and the admin team mode (<c>AdminRoster</c>) all want the same information.
    /// If all four listened to <c>load_match</c> separately, all four would go stale separately; one
    /// read point + one feed point makes that structurally impossible.
    /// </para>
    /// <para>
    /// State and event are <b>static</b> (the <c>AdminSelection</c> pattern) so listeners do not have
    /// to know when the feed was set up. The feed is driven by <see cref="ModeRuntimePump"/>, which
    /// bootstraps itself.
    /// </para>
    /// </summary>
    public static class ModeRuntime
    {
        /// <summary>Catalog resource name (without extension), the same one the admin UI reads.</summary>
        private const string CatalogResourceName = "GameCatalog";

        /// <summary>Raised when the rules change (main thread).</summary>
        public static event Action Changed;

        /// <summary>Which mode the rules belong to; empty if no match has ever been loaded.</summary>
        public static string ModeId { get; private set; } = "";

        public static ModeTeamMode Teams { get; private set; } = ModeTeamMode.TwoTeams;

        public static ModeScoreKind Scoring { get; private set; } = ModeScoreKind.Team;

        /// <summary>true = teammates can be hit. The decision lives on the server; the client only displays it.</summary>
        public static bool FriendlyFire { get; private set; }

        public static ModeReviveAnchor Revive { get; private set; } = ModeReviveAnchor.OwnBase;

        public static ModeWeaponSource Weapons { get; private set; } = ModeWeaponSource.WeaponCanvas;

        /// <summary>Death → earliest revive time; the same value as the server's
        /// <c>respawn.delaySeconds</c> (they cannot diverge because both are fed from the mode's rule).
        /// <para><b><c>0</c> is a valid value</b> (instant revive) — it is not snapped back to the
        /// default. If the field never arrives on the wire, the DTO's own initializer
        /// (<c>RESPAWN_DELAY</c>) applies, so "not written" and "written as zero" do not get confused.</para></summary>
        public static float RespawnDelay { get; private set; } = ArenaProtocol.RESPAWN_DELAY;

        /// <summary>
        /// Whether the weapon can be fired while the phase is not <c>playing</c>
        /// (§10.5 <c>fireWhilePaused</c>). <c>true</c> in the lobby type: target practice happens and
        /// the muzzle flash is relayed to everyone — but there is <b>still no damage</b>, the server
        /// shuts that off based on the phase (§10.3).
        /// <para>⚠️ Thanks to this field no <c>if (modeId == "lobby")</c> chain is born on the client;
        /// this is the single answer to "can we fire here".</para>
        /// </summary>
        public static bool FireWhilePaused { get; private set; }

        /// <summary>Teamless-mode shortcut — so callers do not repeat the enum comparison.</summary>
        public static bool IsTeamless => Teams == ModeTeamMode.None;

        /// <summary>Weaponless-mode shortcut (§10.5 <c>weaponSource:"none"</c>): no grant runs, scene
        /// racks stay hidden and the trigger is shut. The single answer to "is there a weapon in this
        /// mode" — read from the RULE, never from <c>modeId</c>.</summary>
        public static bool IsWeaponless => Weapons == ModeWeaponSource.None;

        /// <summary>
        /// Applies the rule shape coming from the server. If <paramref name="info"/> is <c>null</c>
        /// (a server that does not carry rules), the catalog takes over — see <see cref="ApplyFromCatalog"/>.
        /// </summary>
        public static void Apply(string modeId, ModeRulesInfo info)
        {
            if (info == null)
            {
                ApplyFromCatalog(modeId);
                return;
            }

            Set(modeId,
                ParseTeams(info.teamMode),
                ParseScoring(info.scoring),
                info.friendlyFire,
                ParseRevive(info.reviveAnchor),
                ParseWeapons(info.weaponSource),
                info.respawnDelay,
                info.fireWhilePaused);
        }

        /// <summary>
        /// When the rules do not arrive on the wire (a server that does not carry rules), applies the
        /// <see cref="ModeDefinition"/> preview values; falls back to the defaults if the mode is not
        /// in the catalog.
        /// <para>
        /// ⚠ <b>On divergence the SERVER wins.</b> The rule fields in <see cref="ModeDefinition"/> are
        /// for the UI/preview only — the moment a real <c>load_match</c> arrives these values are
        /// overwritten. (The very same contract that already applies to
        /// <c>roundSeconds</c>/<c>scoreLimit</c>.)
        /// </para>
        /// </summary>
        public static void ApplyFromCatalog(string modeId)
        {
            ModeDefinition mode = FindCatalogMode(modeId);
            if (mode == null)
            {
                Reset(modeId);
                return;
            }

            // Free firing is NOT a separate SO field, it is derived from the lobby profile: ticking
            // both fields by hand would make a meaningless combination like "lobby but firing off"
            // possible. Authority is still on the server (rules.fireWhilePaused); this is only the
            // fallback for a wire without rules.
            Set(modeId, mode.TeamMode, mode.Scoring, mode.FriendlyFire,
                mode.Revive, mode.Weapons, mode.RespawnDelay, mode.IsLobbyProfile);
        }

        /// <summary>Returns to the default (team-based TDM) — on returning to the open scene and on disconnect.</summary>
        public static void Reset(string modeId = "")
        {
            Set(modeId, ModeTeamMode.TwoTeams, ModeScoreKind.Team, false,
                ModeReviveAnchor.OwnBase, ModeWeaponSource.WeaponCanvas, ArenaProtocol.RESPAWN_DELAY, false);
        }

        // ---------------------------------------------------------------- internals

        private static void Set(string modeId, ModeTeamMode teams, ModeScoreKind scoring,
            bool friendlyFire, ModeReviveAnchor revive, ModeWeaponSource weapons, float respawnDelay,
            bool fireWhilePaused)
        {
            string id = modeId ?? "";
            // 0 is preserved (instant revive); only a meaningless negative is clamped.
            float delay = Mathf.Max(0f, respawnDelay);

            bool changed = id != ModeId || teams != Teams || scoring != Scoring ||
                           friendlyFire != FriendlyFire || revive != Revive || weapons != Weapons ||
                           fireWhilePaused != FireWhilePaused ||
                           !Mathf.Approximately(delay, RespawnDelay);

            ModeId = id;
            Teams = teams;
            Scoring = scoring;
            FriendlyFire = friendlyFire;
            Revive = revive;
            Weapons = weapons;
            RespawnDelay = delay;
            FireWhilePaused = fireWhilePaused;

            if (changed)
            {
                Changed?.Invoke();
            }
        }

        private static ModeDefinition FindCatalogMode(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
            {
                return null;
            }

            // The catalog is read from the same place as the admin UI (Assets/_Shared/Data/Resources/).
            // If it is not found we silently fall back to the defaults: this path only runs when the
            // rules did not arrive on the wire; in the field the rules always come from the server.
            var catalog = Resources.Load<GameCatalog>(CatalogResourceName);
            return catalog != null ? catalog.FindMode(modeId) : null;
        }

        // Parsing rule (§10.5): AN UNKNOWN/EMPTY VALUE FALLS BACK TO THE DEFAULT. Thanks to this,
        // adding a new rule value does not break an old client and PROTOCOL_VERSION does not bump.

        private static ModeTeamMode ParseTeams(string value)
        {
            return Matches(value, "none") ? ModeTeamMode.None : ModeTeamMode.TwoTeams;
        }

        private static ModeScoreKind ParseScoring(string value)
        {
            if (Matches(value, "shared"))
            {
                return ModeScoreKind.PlayerAndShared;
            }

            return Matches(value, "player") ? ModeScoreKind.Player : ModeScoreKind.Team;
        }

        private static ModeReviveAnchor ParseRevive(string value)
        {
            if (Matches(value, "standstill"))
            {
                return ModeReviveAnchor.StandStill;
            }

            return Matches(value, "none") ? ModeReviveAnchor.None : ModeReviveAnchor.OwnBase;
        }

        private static ModeWeaponSource ParseWeapons(string value)
        {
            if (Matches(value, "none"))
            {
                return ModeWeaponSource.None;
            }

            return Matches(value, "random") ? ModeWeaponSource.RandomGrant : ModeWeaponSource.WeaponCanvas;
        }

        private static bool Matches(string value, string expected)
        {
            return !string.IsNullOrEmpty(value) &&
                   string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
