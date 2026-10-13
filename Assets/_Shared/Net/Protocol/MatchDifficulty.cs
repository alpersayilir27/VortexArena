namespace VortexArena.Protocol
{
    /// <summary>Operator-picked match difficulty (§5.2 <c>start_match.difficulty</c>).</summary>
    /// <remarks>⚠️ The name is GENERIC but only <c>zombie</c> reads it today (§10.5); a mode-specific
    /// field would add a new wire field per mode.
    /// <para>Named <c>MatchDifficulty</c>, not <c>Difficulty</c>: the server's
    /// <c>MatchDirector.Difficulty</c> property would shadow the type inside that class.</para></remarks>
    public static class MatchDifficulty
    {
        public const string Easy = "easy";
        public const string Normal = "normal";
        public const string Hard = "hard";

        /// <summary>Missing, empty or unknown reads as <see cref="Normal"/> — the wire carries one
        /// spelling, so a selection gate can compare strings directly.</summary>
        public static string Normalize(string difficulty)
        {
            if (string.IsNullOrEmpty(difficulty)) return Normal;

            return difficulty == Easy ? Easy
                : difficulty == Hard ? Hard
                : Normal;
        }
    }
}
