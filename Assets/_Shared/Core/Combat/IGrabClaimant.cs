namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Something that answers a proximity grip press by filling a hand: the bridge on a grabbable
    /// object, a dispenser that has the server spawn one into the palm.
    /// <para>⚠️ An implementer never acts on the press itself — it offers itself to
    /// <see cref="GrabArbiter"/> and waits to be called back. Sockets overlap (a dispenser stands among
    /// the ingredients it hands out, a board carries a stack), and each claimant sees only its own
    /// socket, so acting directly fills one hand several times over.</para>
    /// </summary>
    public interface IGrabClaimant
    {
        /// <summary>The arbiter's callback: this claimant won that hand this frame.</summary>
        void CommitGrab(bool rightHand);
    }
}
