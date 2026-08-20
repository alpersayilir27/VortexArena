namespace VortexArena.Core
{
    /// <summary>
    /// Revive condition (§10.4/2, §10.5 <c>reviveAnchor</c>).
    /// <para>
    /// ⚠ FREE-ROAM: neither of them is a POSITION change — the player physically walks and no
    /// condition moves the rig. The condition only determines "when is <c>revive_request</c> sent".
    /// </para>
    /// <para>⚠ SERIALIZED by <see cref="ModeDefinition"/> — new values are appended at the END.</para>
    /// </summary>
    public enum ModeReviveAnchor
    {
        /// <summary>The player physically enters their own <see cref="Arena.BaseZone"/> (TDM).</summary>
        OwnBase,

        /// <summary>The player stands still without interruption for <c>REVIVE_HOLD_SECONDS</c>
        /// within <c>REVIVE_HOLD_RADIUS</c> (modes without team bases).</summary>
        StandStill,

        /// <summary>
        /// There is NO revive (round-based elimination — <c>tournament</c>). The client never sends
        /// <c>revive_request</c> and the server rejects it if it arrives; since that request is the
        /// only way to revive, a dead player is only brought back by a new round started by the mode.
        /// <para>⚠ Appended at the END of the enum (serialized enum rule) — inserting it in the middle
        /// would have shifted the values in every <see cref="ModeDefinition"/> asset.</para>
        /// </summary>
        None
    }
}
