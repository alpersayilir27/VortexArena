using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Core.World
{
    /// <summary>Places a FREE (not held) network object: the streamed pose while its owner is flying it,
    /// the resting pose once the stream stops (§6.12/§10.10). It also keeps physics where authority is —
    /// only the OWNER simulates, everyone else stays kinematic.
    /// <para>⚠️ <b>A HELD object is not touched at all</b> — it hangs off the owner's hand and the grab
    /// side places it. Two writers on one transform is a visible jitter, not a compile error.</para>
    /// <para>⚠️ With no resting pose from the server the object is left <b>where the scene put it</b>: a
    /// baked object that never moved has no pose on the wire, and writing one would teleport it to the
    /// origin.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public class NetObjectBody : MonoBehaviour
    {
        private NetObject _net;
        private Rigidbody _body;

        /// <summary>Resting pose already written; kept so the pose is not re-applied every frame (that
        /// would fight anything else nudging the object and burn transform writes).</summary>
        private bool _restApplied;
        private Vector3 _appliedRestPosition;
        private Quaternion _appliedRestRotation;

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _body = GetComponent<Rigidbody>();

            if (_body != null)
            {
                // Kinematic until ownership says otherwise: an object simulated on two headsets is the
                // same knife in two places.
                _body.isKinematic = true;
            }
        }

        private void LateUpdate()
        {
            if (_net == null || _net.NetId <= 0)
            {
                return;
            }

            if (_net.IsHeld)
            {
                // The grab side owns the transform now; re-seat to the rest pose after it lets go.
                _restApplied = false;
                return;
            }

            ApplyPhysicsAuthority(_net.IsMine);

            if (_net.IsMine)
            {
                // We are the source of this object's pose (we stream it out) — nothing to apply.
                _restApplied = false;
                return;
            }

            if (TryApplyStreamedPose())
            {
                return;
            }

            ApplyRestPose();
        }

        /// <summary>Physics runs on the OWNER only (§10.10). A missing Rigidbody is legal — a decorative
        /// or purely server-driven object never needed one.</summary>
        private void ApplyPhysicsAuthority(bool mine)
        {
            bool kinematic = !mine;
            if (_body == null || _body.isKinematic == kinematic)
            {
                return;
            }

            _body.isKinematic = kinematic;
        }

        /// <summary>The owner's live pose (§6.12), arena → world. False when no stream is running.</summary>
        private bool TryApplyStreamedPose()
        {
            ArenaClient client = ArenaClient.Instance;
            RemoteObjectRegistry remotes = client != null ? client.RemoteObjects : null;
            if (remotes == null || !remotes.IsStreaming(_net.NetId))
            {
                return false;
            }

            if (!remotes.TryGetInterpolatedPose(_net.NetId, out Pose arenaPose))
            {
                return false;
            }

            transform.SetPositionAndRotation(
                ArenaSpace.ArenaToWorld(arenaPose.position),
                ArenaSpace.ArenaToWorld(arenaPose.rotation));

            _restApplied = false;
            return true;
        }

        /// <summary>Seats the object on the server's resting pose. ⚠️ The step from the last streamed
        /// frame to this pose can be visible — that correction is accepted (§10.10), the alternative is
        /// two objects at two places.</summary>
        private void ApplyRestPose()
        {
            if (!_net.HasRestPose)
            {
                return;
            }

            if (_restApplied &&
                _appliedRestPosition == _net.RestPosition &&
                _appliedRestRotation == _net.RestRotation)
            {
                return;
            }

            _appliedRestPosition = _net.RestPosition;
            _appliedRestRotation = _net.RestRotation;
            _restApplied = true;

            transform.SetPositionAndRotation(
                ArenaSpace.ArenaToWorld(_appliedRestPosition),
                ArenaSpace.ArenaToWorld(_appliedRestRotation));
        }
    }
}
