using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.World
{
    /// <summary>Streams the pose of an object we OWN (<c>0x09</c>, §6.12) and closes ownership when it
    /// stops (<c>object_rest</c>, §5.1). The gate is all three at once: ours, awake and NOT held — the
    /// flight window between <c>object_release</c> and <c>object_rest</c>.
    /// <para>⚠️ <b>Stopping is measured here</b>, on the client: the server has no physics and no metres,
    /// so nobody else can tell when a thrown object came to rest.</para>
    /// <para>An object in a LOCAL hand closes the gate as well (<see cref="HeldItems.Holds"/>): a thrower
    /// who catches their own object back is still the owner while the <c>Held</c> confirmation travels,
    /// and an object resting in a palm must not send <c>object_rest</c> and free itself.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public class NetObjectPoseSender : MonoBehaviour
    {
        private static readonly float SendInterval = 1f / ArenaProtocol.OBJECT_POSE_RATE_HZ;

        /// <summary><c>object_rest</c> just went out for this object: presentation components learn from
        /// here that <b>WE</b> brought it to rest.
        /// <para>⚠️ Raised only on the SENDING client, which makes it the natural selector whenever a
        /// report about an object must be sent by exactly ONE client (the grill's cook timer): everyone
        /// reporting would start the same server-side counter N times.</para></summary>
        public event System.Action<NetObject> RestSent;

        private NetObject _net;
        private Rigidbody _body;

        /// <summary>How long a "standing on something that can still move" contact keeps counting after
        /// the physics callback that saw it. ⚠️ Not a per-frame flag: physics does not step every frame,
        /// so a flag cleared each Update would read "stable ground" on the frames in between.</summary>
        private const float UnstableSupportMemory = 0.25f;

        private float _sendTimer;
        private float _stillSeconds;

        /// <summary>Seconds left of the last unstable-support contact (see the constant).</summary>
        private float _unstableSupportLeft;

        /// <summary>⚠️ <c>object_rest</c> goes out ONCE per flight: the gate stays open until the server
        /// answers by dropping <c>Awake</c>, and a per-frame resend would spam the reliable queue.</summary>
        private bool _restSent;

        private Vector3 _lastPosition;

        private void Awake()
        {
            _net = GetComponent<NetObject>();
            _body = GetComponent<Rigidbody>();
            _lastPosition = transform.position;
        }

        private void OnEnable()
        {
            ResetFlight();
            _lastPosition = transform.position;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 position = transform.position;
            float speed = MeasureSpeed(position, dt);
            _lastPosition = position;
            _unstableSupportLeft = Mathf.Max(0f, _unstableSupportLeft - dt);

            if (_net == null || _net.NetId <= 0 || !_net.IsMine || !_net.IsAwake || _net.IsHeld ||
                HeldItems.Holds(transform))
            {
                ResetFlight();
                return;
            }

            StreamPose(dt);
            TrackRest(speed, dt);
        }

        /// <summary>Rigidbody speed while physics runs here; otherwise the transform delta — an object
        /// moved by something other than physics must still be able to come to rest.</summary>
        private float MeasureSpeed(Vector3 position, float dt)
        {
            if (_body != null && !_body.isKinematic)
            {
                return _body.linearVelocity.magnitude;
            }

            return dt > 0f ? (position - _lastPosition).magnitude / dt : 0f;
        }

        private void StreamPose(float dt)
        {
            _sendTimer -= dt;
            if (_sendTimer > 0f)
            {
                return;
            }

            _sendTimer = SendInterval;

            ArenaClient client = ArenaClient.Instance;
            UdpStateChannel channel = client != null ? client.UdpChannel : null;
            if (channel == null)
            {
                return;
            }

            channel.SendObjectPose(_net.NetId, ArenaSpace.WorldToArena(CurrentWorldPose()));
        }

        /// <summary>⚠️ A single frame under the threshold is NOT "stopped": speed hits zero at the apex of
        /// a bounce, and the object would freeze in mid-air (<c>OBJECT_REST_SECONDS</c>).</summary>
        private void TrackRest(float speed, float dt)
        {
            // ⚠️ Standing still ON A CARRIED OBJECT is not resting: rest ends ownership and the server
            // freezes the object at that pose, so a knife laid on a held plate stays hanging in the air
            // once the plate moves out from under it. Only ground that cannot walk away counts.
            if (speed > ArenaProtocol.OBJECT_REST_SPEED || _unstableSupportLeft > 0f)
            {
                _stillSeconds = 0f;
                return;
            }

            _stillSeconds += dt;

            if (_restSent || _stillSeconds < ArenaProtocol.OBJECT_REST_SECONDS)
            {
                return;
            }

            _restSent = true;

            Pose arenaPose = ArenaSpace.WorldToArena(CurrentWorldPose());
            NetObjectSync.SendRest(_net.NetId, arenaPose.position, arenaPose.rotation);
            RestSent?.Invoke(_net);
        }

        /// <remarks>Contacts arrive only while the body is dynamic, which is exactly the flight window
        /// this component measures.</remarks>
        private void OnCollisionStay(Collision collision)
        {
            if (IsUnstableSupport(collision.collider))
            {
                _unstableSupportLeft = UnstableSupportMemory;
            }
        }

        /// <summary>Can this contact walk away from under us — a net object in someone's hand, or one
        /// already in a LOCAL hand while its <c>Held</c> confirmation is still travelling?</summary>
        private static bool IsUnstableSupport(Collider other)
        {
            if (other == null)
            {
                return false;
            }

            NetObject support = other.GetComponentInParent<NetObject>();
            return support != null && (support.IsHeld || HeldItems.Holds(support.transform));
        }

        private Pose CurrentWorldPose()
        {
            transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            return new Pose(position, rotation);
        }

        /// <summary>Gate closed: the next flight starts with an immediate pose and a fresh rest
        /// measurement.</summary>
        private void ResetFlight()
        {
            _sendTimer = 0f;
            _stillSeconds = 0f;
            _restSent = false;
            _unstableSupportLeft = 0f;
        }
    }
}
