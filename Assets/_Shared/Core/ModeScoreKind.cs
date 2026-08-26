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
        Player,

        /// <summary>Co-op score: everyone's contribution goes to <c>PlayerInfo.score</c> AND the
        /// shared total to <c>match_state.scoreRed</c>, with <c>scoreBlue</c> always <c>0</c>. There
        /// is no winner. Wire name <c>"shared"</c> (§10.5).
        /// <para>⚠ Appended at the END — inserting it above would shift the stored index of every
        /// <see cref="ModeDefinition"/> asset.</para></summary>
        PlayerAndShared
    }
}
