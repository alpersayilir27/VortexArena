namespace VortexArena.Core
{
    /// <summary>
    /// Skorun hangi kanaldan taşındığı (§10.5 <c>scoring</c>).
    /// <para>⚠ <see cref="ModeDefinition"/> tarafından SERIALIZE edilir — yeni değer SONA eklenir.</para>
    /// </summary>
    public enum ModeScoreKind
    {
        /// <summary><c>match_state.scoreRed</c> / <c>scoreBlue</c>.</summary>
        Team,

        /// <summary><c>lobby_state → PlayerInfo.score</c> (§10.2).</summary>
        Player
    }
}
