using System;

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
    /// moment is the lobby profile, this information cannot be read from anywhere else. Its only
    /// consumer is <c>Arena.BaseZoneVisibility</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>No field without a consumer is added here</b>: the rest of the selection (map, duration,
    /// limit) belongs to the operator and goes only to admins via <c>admin_state</c>.
    /// </para>
    /// </summary>
    public static class ModeSelection
    {
        /// <summary>Whether the server ever reported a selection. <c>false</c> = old server (or no
        /// connection): the consumer falls back to the active rule, because "unknown" and "teamless"
        /// are not the same thing.</summary>
        public static bool HasValue { get; private set; }

        /// <summary>Selected mode id (<c>"lobby"</c> at startup). For diagnostics/logging only —
        /// behaviour is read from <see cref="IsTeamless"/> (no <c>if (modeId == …)</c> chain is
        /// written on the client, §10.5).</summary>
        public static string ModeId { get; private set; } = "";

        /// <summary>Whether the selected mode is teamless (<c>teamMode:"none"</c>).</summary>
        public static bool IsTeamless { get; private set; }

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

        /// <summary>Connection lost / session ended: returns to "unknown" so the consumer falls back
        /// to the active rule again.</summary>
        public static void Reset()
        {
            Set(false, "", false);
        }

        private static void Set(bool hasValue, string modeId, bool teamless)
        {
            bool changed = hasValue != HasValue || modeId != ModeId || teamless != IsTeamless;

            HasValue = hasValue;
            ModeId = modeId;
            IsTeamless = teamless;

            if (changed)
            {
                Changed?.Invoke();
            }
        }
    }
}
