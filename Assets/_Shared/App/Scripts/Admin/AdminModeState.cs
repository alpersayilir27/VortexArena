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
    }
}
