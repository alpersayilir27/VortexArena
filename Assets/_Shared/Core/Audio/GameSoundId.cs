namespace VortexArena.Core.Audio
{
    /// <summary>Feedback sounds shared by the whole game, independent of map and mode. The clips live
    /// in <see cref="GameSoundBank"/> and <see cref="GameAudio"/> is the only player.
    /// <para>⚠️ Serialized enum: new values are appended at the END — inserting shifts the mapping in
    /// existing assets.</para></summary>
    public enum GameSoundId
    {
        /// <summary>The local player killed an enemy.</summary>
        EnemyEliminated = 0,

        /// <summary>The local player died.</summary>
        LocalDeath = 1,

        /// <summary>The local player revived.</summary>
        LocalRespawn = 2,

        /// <summary>Phase moved to <c>playing</c> (match started / resumed after a round).</summary>
        MatchStart = 3,

        /// <summary>Match ended, RED team won.</summary>
        TeamRedWon = 4,

        /// <summary>Match ended, BLUE team won.</summary>
        TeamBlueWon = 5,

        /// <summary>Every second of the countdown.</summary>
        CountdownTick = 6,

        /// <summary>A player's physical violation started (§10.9) — plays on the ADMIN PC only; the
        /// player already has the warning on their own screen. The gate and the rate limit live in
        /// <c>AdminRoster</c>; this enum only names the clip.</summary>
        AdminViolation = 7,

        /// <summary>Match ended in a draw — no winning team and no winning player.</summary>
        MatchDraw = 8,

        /// <summary>The local player killed their own TEAMMATE (with friendly fire on).
        /// <para>⚠️ Replaces <see cref="EnemyEliminated"/>, never in addition: one kill event plays
        /// exactly one of the two.</para></summary>
        TeammateEliminated = 9,
    }
}
