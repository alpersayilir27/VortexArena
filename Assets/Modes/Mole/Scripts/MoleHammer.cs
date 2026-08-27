using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Modes.Mole
{
    /// <summary>Sits on the local player's hammer: a fast enough swing whose HEAD COLLIDER touches a
    /// standing mole raises <c>whack</c> (§10.5).
    /// <para><b>What the developer sets up:</b> a trigger <see cref="SphereCollider"/> on the hammer
    /// head and a collider on the mole. Nothing here measures the mole — "did the hammer touch it"
    /// is answered by the two colliders, so a new mole model only needs its own collider and no code
    /// or number changes.</para>
    /// <para>⚠️ <b>Why a sweep and not <c>OnTriggerEnter</c>:</b> a swung hammer head moves metres per
    /// frame while the head is centimetres wide — between two frames it jumps clean THROUGH the mole
    /// and the trigger never fires. The symptom is "the hammer works sometimes". So each frame the
    /// head's own collider is swept from where it was to where it is; the shape and size of the test
    /// are still the collider's, only the moment of contact is recovered.</para>
    /// <para>⚠️ Only the LOCAL hammer runs this: a remote player's copy is stripped of every
    /// MonoBehaviour and collider when it is drawn from the <c>itemR</c>/<c>itemL</c> byte (§6.6). So
    /// "who reports the hit" resolves to exactly one client and the same pop is never reported
    /// twice.</para></summary>
    [DisallowMultipleComponent]
    public sealed class MoleHammer : MonoBehaviour
    {
        [Tooltip("Balyozun ucundaki vuruş küresi (trigger olmalı). Boşsa çocuklarda aranır — " +
                 "yarıçapı ve konumu Inspector'dan görsel ayarlanır, kodda ölçü yoktur.")]
        [SerializeField] private SphereCollider hitCollider;

        [Tooltip("Vuruş sayılması için gereken en düşük baş hızı (m/s). Dokunarak ezmeyi kapatır.")]
        [SerializeField] private float minSwingSpeed = 1.5f;

        /// <summary>Enough for the moles a single swing can reach; the buffer only has to hold the
        /// contacts of ONE frame, not the scene.</summary>
        private const int MaxContacts = 8;

        private readonly RaycastHit[] _sweepHits = new RaycastHit[MaxContacts];
        private readonly Collider[] _overlaps = new Collider[MaxContacts];

        private Vector3 _lastCenter;
        private bool _hasLastCenter;

        /// <summary>Last pop this hammer already reported. ⚠️ Keyed by (hole, counter), not by time: one
        /// swing spans several frames, and reporting it on each of them would send the same event five
        /// times — every copy passing the server's gates until the first one closes them.</summary>
        private int _reportedNetId;
        private int _reportedNonce = -1;

        private void Awake()
        {
            if (hitCollider == null)
            {
                hitCollider = GetComponentInChildren<SphereCollider>(true);
            }

            if (hitCollider == null)
            {
                Debug.LogError($"[MoleHammer] '{name}' altında vuruş küresi (SphereCollider) yok — " +
                               "bu balyozla köstebek ezilemez.", this);
                enabled = false;
                return;
            }

            // A solid collider on a hand-held object pushes the player's own body around; the contact
            // test does not need it to be solid.
            if (!hitCollider.isTrigger)
            {
                Debug.LogWarning($"[MoleHammer] '{hitCollider.name}' trigger değil — vuruş yine " +
                                 "çalışır ama balyoz sahneye fiziksel olarak çarpar.", this);
            }
        }

        private void LateUpdate()
        {
            Vector3 center = hitCollider.transform.TransformPoint(hitCollider.center);

            if (!_hasLastCenter)
            {
                _lastCenter = center;
                _hasLastCenter = true;
                return;
            }

            Vector3 delta = center - _lastCenter;
            _lastCenter = center;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f || !CalibrationState.IsCalibrated)
            {
                return;
            }

            if (delta.magnitude / deltaTime < minSwingSpeed)
            {
                return;
            }

            MoleHole target = FindContact(center, delta);
            if (target == null || !target.IsUp || target.Nonce < 0)
            {
                return;
            }

            if (target.NetId == _reportedNetId && target.Nonce == _reportedNonce)
            {
                return;
            }

            _reportedNetId = target.NetId;
            _reportedNonce = target.Nonce;
            NetObjectSync.SendEvent(target.NetId, MoleKinds.EventWhack, new[] { target.Nonce });
        }

        /// <summary>The mole the head touched this frame, or null. Overlap first (the head may already be
        /// inside the mole), then the sweep across the distance travelled.</summary>
        private MoleHole FindContact(Vector3 center, Vector3 delta)
        {
            float radius = WorldRadius();

            int overlapCount = Physics.OverlapSphereNonAlloc(center, radius, _overlaps, ~0,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < overlapCount; i++)
            {
                MoleHole hole = Resolve(_overlaps[i]);
                if (hole != null)
                {
                    return hole;
                }
            }

            float distance = delta.magnitude;
            if (distance <= 0.0001f)
            {
                return null;
            }

            // Starts where the head was on the PREVIOUS frame.
            int sweepCount = Physics.SphereCastNonAlloc(center - delta, radius,
                delta / distance, _sweepHits, distance, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < sweepCount; i++)
            {
                MoleHole hole = Resolve(_sweepHits[i].collider);
                if (hole != null)
                {
                    return hole;
                }
            }

            return null;
        }

        /// <summary>The hole a touched collider belongs to; null when it was scenery. Searched UPWARDS so
        /// the mole's collider may sit anywhere under the hole (a real model brings its own hierarchy).</summary>
        private static MoleHole Resolve(Collider collider)
        {
            return collider != null ? collider.GetComponentInParent<MoleHole>() : null;
        }

        /// <summary>Collider radius in world units — the object is scaled in the hand hierarchy.</summary>
        private float WorldRadius()
        {
            Vector3 scale = hitCollider.transform.lossyScale;
            float largest = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            return Mathf.Max(0.001f, hitCollider.radius * largest);
        }
    }
}
