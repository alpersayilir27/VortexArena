namespace VortexArena.Modes.Zombie
{
    /// <summary>One severable body SEGMENT — the hit box the round struck, not the limb it belongs to.
    /// <para>⚠️ The cut is at the segment's ROOT joint: a forearm hit takes the arm off at the elbow,
    /// an upper-arm hit at the shoulder. That is why this is finer than
    /// <c>ZombieFeelEvents.ZombieLimb</c>, which only tells the player side what came off.</para></summary>
    public enum ZombieSegment
    {
        None = 0,
        Neck = 1,
        UpperArm = 2,
        ForeArm = 3,
        Thigh = 4,
        Shin = 5,
        Torso = 6,
    }
}
