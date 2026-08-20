using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// The source that feeds poses into UdpStateChannel's 20 Hz send loop.
    /// The world→arena transform is the SOURCE's responsibility (PlayerPoseTracker in App does it) —
    /// the Net layer only receives ready arena-space poses.
    /// </summary>
    public interface IPoseSource
    {
        /// <summary>Returns the arena-space poses; false when tracking is not ready.</summary>
        bool TryGetArenaPoses(out Pose head, out Pose handL, out Pose handR);

        /// <summary>
        /// §6.2: the item bytes currently held in hand. FIVE client-owned bits are meaningful in
        /// <c>gripFlags</c> — <c>FLAG_GRIP_LINKED</c>/<c>FLAG_PRIMARY_RIGHT</c> (grip),
        /// <c>FLAG_HAND_L_STALE</c>/<c>FLAG_HAND_R_STALE</c> (that hand's pose is the last valid one,
        /// not a measurement) and <c>FLAG_IN_OBSTACLE</c> (body inside an inner obstacle, §10.9).
        /// The filter is <see cref="VortexArena.Protocol.SnapshotEntry.GRIP_FLAG_MASK"/>; bit0 is the
        /// server's (<c>FLAG_ALIVE</c>). With no item all three return 0.
        /// <para>⚠️ Named "grip" but carrying <b>all of the client's hand-related presentation
        /// bits</b>. Adding a bit means growing the mask too, or the server drops it silently.</para>
        /// <para><b>Why through the pose seam:</b> (1) <b>asmdef direction</b> — Protocol ← Net ← Core ←
        /// App, so this layer cannot see Core types (<c>HeldItems</c>/<c>ItemDefinition</c>/
        /// <c>NetItemCatalog</c>); (2) <b>same authority</b> — "what is in my hand" is
        /// client-authoritative presentation info, like the pose, and rides the same packet.</para>
        /// </summary>
        void GetHeldItems(out byte itemL, out byte itemR, out byte gripFlags);
    }
}
