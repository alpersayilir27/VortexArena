using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// The <b>single</b> answer to "can this hand's anchor pose be trusted right now" + the controller
    /// state to report to the operator (<c>ArenaProtocol.CONTROLLER_*</c>).
    /// <para>
    /// ⚠️ The reason it exists is that <c>OVRCameraRig.UpdateAnchors</c> writes the hand anchors
    /// <b>unconditionally</b>: unlike the eye anchors, validity is not asked and
    /// <c>OVRInput.GetLocalControllerPosition(active controller)</c> is written directly. When a
    /// controller's battery dies the active controller becomes <see cref="OVRInput.Controller.None"/>,
    /// the read returns <c>(0,0,0)</c> and the hand anchor jumps to the rig origin — right at the
    /// player's feet. That zero poisons two channels at once: <c>0x01 PoseUpdate</c> reads the anchor
    /// directly, and the body tracking solve targets that hand and collapses the skeleton.
    /// </para>
    /// <para>
    /// ⚠️ <b>The validity criterion MIRRORS what the rig does</b> and is not based on any other
    /// criterion (battery, connected controller list, pose jump): whichever source writes the anchor
    /// also tells its validity. A separate criterion would become a second source of truth, and when
    /// the two diverge the hand is either frozen for nothing or a zero pose leaks through. For the same
    /// reason <b>hand tracking (LHand/RHand) is a legitimate source</b> — it is not treated as invalid
    /// just because there is no controller; whatever the rig picks is what counts.
    /// </para>
    /// </summary>
    public static class ControllerTracking
    {
        /// <summary>
        /// The settle time <see cref="GetState"/> waits before reporting a change. The state goes to the
        /// operator's indicator: since tracking can flicker several times a second, an unfiltered field
        /// would keep triggering roster broadcasts (a flickering field = a broadcast trigger).
        /// </summary>
        private const float StateSettleSeconds = 1f;

        /// <summary>Left hand = 0, right hand = 1 (indexed as <c>right ? 1 : 0</c>).</summary>
        private static readonly HandTrack[] Hands = { new HandTrack(), new HandTrack() };

        private static int _lastTickFrame = -1;

        /// <summary>
        /// Samples once per frame; it is <b>idempotent</b> — however many times it is called in the same
        /// frame, only a single sample is taken.
        /// <para>Every getter also calls it first: even if a caller forgets <see cref="Tick"/> the
        /// values do not go stale — a safety net that silently answers wrong because it was not called
        /// is not a safety net.</para>
        /// </summary>
        public static void Tick()
        {
            if (_lastTickFrame == Time.frameCount)
            {
                return;
            }

            _lastTickFrame = Time.frameCount;

            Sample(Hands[0], OVRInput.Handedness.LeftHanded);
            Sample(Hands[1], OVRInput.Handedness.RightHanded);
        }

        /// <summary>
        /// Is this hand's anchor pose a measurement <b>THIS FRAME</b> (i.e. can it be read).
        /// <para>⚠️ <b>There is NO debounce and none is added:</b> the anchor going to zero and the hold
        /// kicking in must happen in the SAME frame — even a one-frame delay leaks a hand at the
        /// player's feet onto the network (and into body tracking). Smoothing is only
        /// <see cref="GetState"/>'s job; that one is an indicator, this one is a gate.</para>
        /// </summary>
        public static bool IsValid(bool right)
        {
            Tick();
            return Hands[right ? 1 : 0].Valid;
        }

        /// <summary>
        /// The state to report to the operator: <see cref="ArenaProtocol.CONTROLLER_UNKNOWN"/> /
        /// <c>_OK</c> / <c>_UNTRACKED</c> / <c>_LOST</c>. Changes are reported after they have stayed
        /// stable for <see cref="StateSettleSeconds"/>.
        /// </summary>
        public static int GetState(bool right)
        {
            Tick();
            return Hands[right ? 1 : 0].Reported;
        }

        /// <summary>
        /// ⚠️ With domain reload disabled, static state SURVIVES between Play sessions: if it is not
        /// reset, the previous session's "was never valid" stamp is carried into the new one.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _lastTickFrame = -1;
            Hands[0].Reset();
            Hands[1].Reset();
        }

        private static void Sample(HandTrack hand, OVRInput.Handedness handedness)
        {
            // With no rig (admin observer, editor without a headset) there is no measurement either:
            // OVRInput's active controller table is driven by OVRManager, and without it every hand
            // would look "lost".
            // ⚠️ In the unknown state the hand counts as VALID — holding produces a guess, and where no
            // measurement happens there is nothing to be gained by feeding a guess into the stream.
            if (OVRManager.instance == null)
            {
                hand.Valid = true;
                hand.Raw = ArenaProtocol.CONTROLLER_UNKNOWN;
                hand.Reported = ArenaProtocol.CONTROLLER_UNKNOWN;
                hand.RawSince = Time.unscaledTime;
                return;
            }

            // A mirror of which source the rig writes the anchor from (see the class note).
            OVRInput.Controller active = OVRInput.GetActiveControllerForHand(handedness);

            int raw;
            if (active == OVRInput.Controller.None)
            {
                raw = ArenaProtocol.CONTROLLER_LOST;
            }
            else if (!OVRInput.GetControllerPositionValid(active))
            {
                raw = ArenaProtocol.CONTROLLER_UNTRACKED;
            }
            else
            {
                raw = ArenaProtocol.CONTROLLER_OK;
            }

            hand.Valid = raw == ArenaProtocol.CONTROLLER_OK;

            if (raw != hand.Raw)
            {
                hand.Raw = raw;
                hand.RawSince = Time.unscaledTime;
            }

            if (hand.Reported != raw && Time.unscaledTime - hand.RawSince >= StateSettleSeconds)
            {
                hand.Reported = raw;
            }
        }

        /// <summary>Sampling state of a single hand. A class (not a struct) because it is updated in
        /// place inside the array.</summary>
        private sealed class HandTrack
        {
            /// <summary>This frame's raw validity — <see cref="IsValid"/> returns this.</summary>
            public bool Valid = true;

            /// <summary>This frame's raw state (before debouncing).</summary>
            public int Raw = ArenaProtocol.CONTROLLER_UNKNOWN;

            /// <summary>Since when <see cref="Raw"/> has not changed.</summary>
            public float RawSince;

            /// <summary>The debounced state reported to the outside.</summary>
            public int Reported = ArenaProtocol.CONTROLLER_UNKNOWN;

            public void Reset()
            {
                Valid = true;
                Raw = ArenaProtocol.CONTROLLER_UNKNOWN;
                RawSince = 0f;
                Reported = ArenaProtocol.CONTROLLER_UNKNOWN;
            }
        }
    }
}
