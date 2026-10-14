using System;

namespace VortexArena.App.Admin
{
    /// <summary>Reads the tokens a mode publishes in <c>modeState</c> (§10.1). The ONE parser for the
    /// admin side, so the top band and the stats panel can never disagree.
    /// <para>⚠️ Keyed on the TOKENS, never on <c>modeId</c>: the vocabulary belongs to the mode, so a
    /// second co-op mode publishing the same counters gets the same readout for free. An unknown token
    /// is skipped rather than treated as an error.</para></summary>
    internal static class AdminModeState
    {
        /// <summary>How a wave run ended (<c>over:</c>); <see cref="None"/> = no outcome token, so the
        /// caller keeps its own wording (an aborted match says nothing about winning).</summary>
        public enum RunOutcome
        {
            None,
            Survived,
            Rescued,
            Lost
        }

        // Wave-run tokens (§10.5). ⚠️ Token names, not a mode name: the readout belongs to whoever
        // publishes them, which is why there is no modeId anywhere in this file.
        private const string WaveKey = "w";
        private const string OverKey = "over";
        private const string OverSurvived = "survived";
        private const string OverRescued = "rescued";
        private const string OverLost = "lost";

        /// <summary>Per-player final tallies: <c>zs&lt;playerId&gt;:&lt;headshots&gt;/&lt;revives&gt;</c>.
        /// The id rides the KEY, so the match is on this prefix.</summary>
        private const string PlayerStatsPrefix = "zs";

        /// <summary>Co-op customer counters (<c>h:</c> happy, <c>u:</c> unhappy); <c>false</c> = the
        /// running mode publishes neither, so the caller draws nothing.</summary>
        public static bool TryCustomerCounts(string modeState, out int happy, out int unhappy)
        {
            happy = 0;
            unhappy = 0;

            if (string.IsNullOrEmpty(modeState))
            {
                return false;
            }

            bool any = false;
            string[] tokens = modeState.Split(';');

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int sep = token.IndexOf(':');
                if (sep <= 0 || sep == token.Length - 1)
                {
                    continue;
                }

                string key = token.Substring(0, sep).Trim();
                if (!int.TryParse(token.Substring(sep + 1).Trim(), out int value) || value < 0)
                {
                    continue;
                }

                if (string.Equals(key, "h", StringComparison.Ordinal))
                {
                    happy = value;
                    any = true;
                }
                else if (string.Equals(key, "u", StringComparison.Ordinal))
                {
                    unhappy = value;
                    any = true;
                }
            }

            return any;
        }

        /// <summary>Wave reached (<c>w:</c>); <c>false</c> = the running mode has no waves.</summary>
        public static bool TryWave(string modeState, out int wave)
        {
            wave = 0;
            if (string.IsNullOrEmpty(modeState))
            {
                return false;
            }

            string[] tokens = modeState.Split(';');
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!Split(tokens[i], out string key, out string raw) ||
                    !string.Equals(key, WaveKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (int.TryParse(raw, out int value) && value >= 0)
                {
                    wave = value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Outcome of a wave run (<c>over:</c>). An unknown value reads as
        /// <see cref="RunOutcome.None"/>: inventing a win or a loss is worse than saying nothing.</summary>
        public static RunOutcome ReadOutcome(string modeState)
        {
            if (string.IsNullOrEmpty(modeState))
            {
                return RunOutcome.None;
            }

            string[] tokens = modeState.Split(';');
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!Split(tokens[i], out string key, out string raw) ||
                    !string.Equals(key, OverKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(raw, OverSurvived, StringComparison.Ordinal))
                {
                    return RunOutcome.Survived;
                }

                if (string.Equals(raw, OverRescued, StringComparison.Ordinal))
                {
                    return RunOutcome.Rescued;
                }

                if (string.Equals(raw, OverLost, StringComparison.Ordinal))
                {
                    return RunOutcome.Lost;
                }
            }

            return RunOutcome.None;
        }

        /// <summary>Does the state carry per-player tallies at all? That is what tells a screen it may
        /// draw the extra columns — the mode publishes them only in the FINAL state.</summary>
        public static bool HasPlayerRunStats(string modeState)
        {
            if (string.IsNullOrEmpty(modeState))
            {
                return false;
            }

            string[] tokens = modeState.Split(';');
            for (int i = 0; i < tokens.Length; i++)
            {
                if (Split(tokens[i], out string key, out string _) && IsPlayerStatsKey(key, out int _))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>One player's tallies; <c>false</c> = no token for that id (nothing is drawn rather
        /// than a zero, which would read as "I never hit a head").</summary>
        public static bool TryPlayerRunStats(string modeState, int playerId, out int headshots,
            out int revives)
        {
            headshots = 0;
            revives = 0;

            if (string.IsNullOrEmpty(modeState) || playerId == 0)
            {
                return false;
            }

            string[] tokens = modeState.Split(';');
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!Split(tokens[i], out string key, out string raw) ||
                    !IsPlayerStatsKey(key, out int id) || id != playerId)
                {
                    continue;
                }

                int slash = raw.IndexOf('/');
                if (slash <= 0 || slash == raw.Length - 1)
                {
                    continue;
                }

                if (!int.TryParse(raw.Substring(0, slash), out int head) ||
                    !int.TryParse(raw.Substring(slash + 1), out int rev) || head < 0 || rev < 0)
                {
                    continue;
                }

                headshots = head;
                revives = rev;
                return true;
            }

            return false;
        }

        private static bool IsPlayerStatsKey(string key, out int playerId)
        {
            playerId = 0;
            return key.Length > PlayerStatsPrefix.Length
                   && key.StartsWith(PlayerStatsPrefix, StringComparison.Ordinal)
                   && int.TryParse(key.Substring(PlayerStatsPrefix.Length), out playerId)
                   && playerId > 0;
        }

        /// <summary><c>"key:value"</c> → its two halves; an empty half is not a token.</summary>
        private static bool Split(string token, out string key, out string value)
        {
            key = null;
            value = null;

            int sep = token.IndexOf(':');
            if (sep <= 0 || sep == token.Length - 1)
            {
                return false;
            }

            key = token.Substring(0, sep).Trim();
            value = token.Substring(sep + 1).Trim();
            return key.Length > 0 && value.Length > 0;
        }
    }
}
