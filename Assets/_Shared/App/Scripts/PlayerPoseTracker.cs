using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// The local player's pose source (Lobby + arena scenes): finds the BB rig anchors and registers
    /// as an IPoseSource with UdpStateChannel. The world→arena transform happens HERE (ArenaSpace);
    /// Net only receives arena-space poses. The admin role does not send poses.
    /// <para>
    /// <b>No calibration gate:</b> registration happens as soon as the anchors are found. Poses sent
    /// before calibration are offset, but they show the player as connected and moving; once the rig
    /// is aligned the same source lands in the correct space without re-registering.
    /// </para>
    /// <para>
    /// <b>The SINGLE gate for pose + item reporting</b> (§6.2: <c>itemL</c>/<c>itemR</c>/
    /// <c>gripFlags</c> ride the pose packet). ⚠️ Item state is <b>read</b> from
    /// <see cref="HeldItems"/>, never produced here — adding "what is in the hand" discovery would
    /// create a second source of the same information.
    /// </para>
    /// </summary>
    public class PlayerPoseTracker : MonoBehaviour, IPoseSource
    {
        /// <summary>
        /// REST offset used when no valid sample was ever taken (right hand, metres relative to the
        /// head; left mirrors X). The packet is fixed length — there is no "player without hands"
        /// wire state — so some hand pose must exist even if the controller never worked.
        /// <para>⚠️ A zero pose is NOT used: it drops the hand onto the rig origin at the player's
        /// feet. A rough waist-height stance is wrong but <b>readable</b>.</para>
        /// </summary>
        private static readonly Vector3 RestOffsetRight = new Vector3(0.20f, -0.45f, 0.25f);

        private Transform _head;
        private Transform _handL;
        private Transform _handR;

        /// <summary>
        /// Last VALID hand pose, <b>relative to the head</b> (0 = left, 1 = right); rebuilt with the
        /// current frame's head pose while the controller is out.
        /// <para>⚠️ <b>Freezing in arena space is NOT an option:</b> the player keeps walking in
        /// free-roam and the hand would detach and hang in the middle of the room.</para>
        /// </summary>
        private readonly Pose[] _handRelative = new Pose[2];
        private readonly bool[] _hasHandRelative = new bool[2];

        // Logged only on CHANGE: per-frame printing drowns the console and hides the real event
        // (a dying battery).
        private int _loggedStateL = ArenaProtocol.CONTROLLER_UNKNOWN;
        private int _loggedStateR = ArenaProtocol.CONTROLLER_UNKNOWN;

        private void Start()
        {
            if (AppSession.Role != AppSession.RolePlayer)
            {
                enabled = false; // the admin does not send poses
                return;
            }

            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null)
            {
                Debug.LogWarning("[PlayerPoseTracker] OVRCameraRig bulunamadı; poz gönderimi devre dışı.");
                enabled = false;
                return;
            }

            _head = rig.centerEyeAnchor;
            _handL = rig.leftHandAnchor;
            _handR = rig.rightHandAnchor;

            // Calibration is not awaited: once the alignment arrives, the same source lands in the
            // correct space by itself.
            ArenaClient.Instance?.UdpChannel?.SetPoseSource(this);
        }

        private void OnDestroy()
        {
            ArenaClient.Instance?.UdpChannel?.ClearPoseSource(this);
        }

        private void Update()
        {
            // One sample per frame (idempotent) so pose, item flags and the skeleton root guard all
            // see the same hand validity.
            ControllerTracking.Tick();

            ReportControllerState();
        }

        /// <summary>
        /// Converts the anchors' world poses into arena space via ArenaSpace.
        /// <para>⚠️ Order matters: hands are built in world space FIRST, converted last — the hold
        /// computation relies on the head's world pose.</para>
        /// </summary>
        public bool TryGetArenaPoses(out Pose head, out Pose handL, out Pose handR)
        {
            if (_head == null || _handL == null || _handR == null)
            {
                head = Pose.identity;
                handL = Pose.identity;
                handR = Pose.identity;
                return false;
            }

            Vector3 headPos = _head.position;
            Quaternion headRot = _head.rotation;

            head = ArenaSpace.WorldToArena(new Pose(headPos, headRot));
            handL = ArenaSpace.WorldToArena(ResolveHandWorld(0, _handL, headPos, headRot));
            handR = ArenaSpace.WorldToArena(ResolveHandWorld(1, _handR, headPos, headRot));
            return true;
        }

        /// <summary>
        /// A hand's WORLD pose: the live anchor when valid (stored head-relative), otherwise the
        /// stored head-relative pose rebuilt with this frame's head.
        /// <para>⚠️ The anchor must NOT be read unconditionally: the rig writes it even without a
        /// source, and the value it writes is the rig origin (see
        /// <see cref="ControllerTracking"/>).</para>
        /// </summary>
        private Pose ResolveHandWorld(int index, Transform anchor, Vector3 headPos, Quaternion headRot)
        {
            Quaternion headInverse = Quaternion.Inverse(headRot);

            if (ControllerTracking.IsValid(index == 1))
            {
                Vector3 pos = anchor.position;
                Quaternion rot = anchor.rotation;

                _handRelative[index] = new Pose(headInverse * (pos - headPos), headInverse * rot);
                _hasHandRelative[index] = true;
                return new Pose(pos, rot);
            }

            Pose relative = _hasHandRelative[index]
                ? _handRelative[index]
                : new Pose(MirrorForHand(index, RestOffsetRight), Quaternion.identity);

            return new Pose(headPos + headRot * relative.position, headRot * relative.rotation);
        }

        /// <summary>Mirrors the right hand offset to the left hand (X negated).</summary>
        private static Vector3 MirrorForHand(int index, Vector3 rightOffset)
        {
            return index == 1 ? rightOffset : new Vector3(-rightOffset.x, rightOffset.y, rightOffset.z);
        }

        /// <summary>
        /// Pushes the controller state to Net (§5.1). ⚠️ App measures it because
        /// <c>VortexArena.Net</c> does not reference Oculus.VR (the <c>battery</c>/<c>rttMs</c>
        /// pattern).
        /// </summary>
        private void ReportControllerState()
        {
            int stateL = ControllerTracking.GetState(false);
            int stateR = ControllerTracking.GetState(true);

            ArenaClient.Instance?.ReportControllerState(stateL, stateR);

            if (stateL != _loggedStateL)
            {
                _loggedStateL = stateL;
                Debug.LogWarning($"[PlayerPoseTracker] Sol kumanda durumu: {DescribeState(stateL)}");
            }

            if (stateR != _loggedStateR)
            {
                _loggedStateR = stateR;
                Debug.LogWarning($"[PlayerPoseTracker] Sağ kumanda durumu: {DescribeState(stateR)}");
            }
        }

        private static string DescribeState(int state)
        {
            switch (state)
            {
                case ArenaProtocol.CONTROLLER_OK:
                    return "izleniyor (el pozu ölçüm).";
                case ArenaProtocol.CONTROLLER_UNTRACKED:
                    return "bağlı ama pozu geçersiz (görüş dışı/uykuda) — el son geçerli pozunda tutuluyor.";
                case ArenaProtocol.CONTROLLER_LOST:
                    return "bağlı DEĞİL — pil bitmiş olabilir. El son geçerli pozunda tutuluyor, " +
                           "poz bayat işaretleniyor.";
                default:
                    return "bilinmiyor (rig yok ya da henüz örneklenmedi).";
            }
        }

        /// <summary>
        /// §6.2: held item bytes — converts <see cref="HeldItems"/>'s last reported state to the
        /// wire format.
        /// </summary>
        public void GetHeldItems(out byte itemL, out byte itemR, out byte gripFlags)
        {
            itemL = HeldItems.Left;
            itemR = HeldItems.Right;

            // ⚠️ bit0 (FLAG_ALIVE) is NEVER written here: only the server writes it, a client cannot
            // declare itself alive (§6.2/§6.3). The server masks it out, but never writing it keeps
            // the rule alive if the mask is ever loosened.
            gripFlags = 0;
            if (HeldItems.GripLinked)
            {
                gripFlags |= SnapshotEntry.FLAG_GRIP_LINKED;
            }

            if (HeldItems.PrimaryRight)
            {
                gripFlags |= SnapshotEntry.FLAG_PRIMARY_RIGHT;
            }

            // Stale hand: the pose is an estimate, not a measurement. Without this flag the receiver
            // would trust it in aim/contact diagnosis. Same gate as TryGetArenaPoses (same frame's
            // ControllerTracking sample).
            if (!ControllerTracking.IsValid(false))
            {
                gripFlags |= SnapshotEntry.FLAG_HAND_L_STALE;
            }

            if (!ControllerTracking.IsValid(true))
            {
                gripFlags |= SnapshotEntry.FLAG_HAND_R_STALE;
            }

            // §10.9: head inside an interior obstacle. ⚠️ A MEASUREMENT report, not a penalty — the
            // server drains health in its own tick. ObstacleViolationProbe (Core singleton) is only
            // read here, same seam pattern as HeldItems.
            if (ObstacleViolationProbe.IsViolating)
            {
                gripFlags |= SnapshotEntry.FLAG_IN_OBSTACLE;
            }

            // §10.9: head outside the boundary's safe area. ⚠️ Also a measurement and, unlike
            // FLAG_IN_OBSTACLE, PRODUCES NO PENALTY — admin visibility only.
            ArenaBoundary boundary = ArenaBoundary.Active;
            if (boundary != null && boundary.IsOutOfBounds)
            {
                gripFlags |= SnapshotEntry.FLAG_OUT_OF_BOUNDS;
            }
        }
    }
}
