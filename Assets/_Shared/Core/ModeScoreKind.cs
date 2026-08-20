namespace VortexArena.Core
{
    /// <summary>
    /// Which channel carries the score (§10.5 <c>scoring</c>).
    /// <para>⚠ SERIALIZED by <see cref="ModeDefinition"/> — new values are appended at the END.</para>
    /// </summary>
    public enum ModeScoreKind
    {
        /// <summary><c>match_state.scoreRed</c> / <c>scoreBlue</c>.</summary>
        Team,

        /// <summary><c>lobby_state → PlayerInfo.score</c> (§10.2).</summary>
        Player
    }
}
