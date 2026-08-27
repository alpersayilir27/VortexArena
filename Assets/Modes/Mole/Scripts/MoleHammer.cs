using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Modes.Mole
{
    /// <summary>Sits on the hammer HEAD of the local player's hammer: a fast enough swing that reaches a
    /// standing mole raises <c>whack</c> (§10.5).
    /// <para>⚠️ Distance, not physics: a trigger would need a kinematic rigidbody on a hand-driven
    /// object, and a hand that teleports between frames passes THROUGH a thin trigger without ever
    /// touching it. The server does not judge the geometry either way (§10.3) — it only checks whether a
    /// mole is standing there.</para>
    /// <para>⚠️ Only the LOCAL hammer carries this component: it is spawned by
    /// <see cref="MoleHammerGranter"/> into the local hand, while a remote player's hammer is drawn from
    /// the <c>itemR</c>/<c>itemL</c> byte (§6.6). So "who reports the hit" resolves to exactly one
    /// client and the same pop is never reported twice.</para></summary>
    [DisallowMultipleComponent]
    public sealed class MoleHammer : MonoBehaviour
    {
        [Tooltip("Vuruş noktası (balyoz başı). Boşsa bu objenin kendisi kullanılır.")]
        [SerializeField] private Transform head;

        [Tooltip("Köstebeği yakalama yarıçapı (m) — baş merkezinden köstebeğe olan mesafe.")]
        [SerializeField] private float hitRadius = 0.35f;

        [Tooltip("Vuruş sayılması için gereken en düşük baş hızı (m/s). Dokunarak ezmeyi kapatır.")]
        [SerializeField] private float minSwingSpeed = 1.5f;

        private Vector3 _lastPosition;
        private bool _hasLastPosition;

        /// <summary>Last pop this hammer already reported. ⚠️ Keyed by (hole, counter), not by time: one
        /// swing spans several frames, and reporting it on each of them would send the same event five
        /// times — every copy passing the server's gates until the first one closes them.</summary>
        private int _reportedNetId;
        private int _reportedNonce = -1;

        private void LateUpdate()
        {
            Transform point = head != null ? head : transform;
            Vector3 position = point.position;

            if (!_hasLastPosition)
            {
                _lastPosition = position;
                _hasLastPosition = true;
                return;
            }

            Vector3 delta = position - _lastPosition;
            _lastPosition = position;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f || !CalibrationState.IsCalibrated)
            {
                return;
            }

            if (delta.magnitude / deltaTime < minSwingSpeed)
            {
                return;
            }

            MoleHole target = FindTarget(position);
            if (target == null || target.Nonce < 0)
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

        /// <summary>Closest STANDING mole within the radius; null when the swing hit nothing.</summary>
        private MoleHole FindTarget(Vector3 position)
        {
            MoleHole best = null;
            float bestDistance = hitRadius * hitRadius;

            var holes = MoleHole.All;
            for (int i = 0; i < holes.Count; i++)
            {
                MoleHole hole = holes[i];
                if (hole == null || !hole.IsUp)
                {
                    continue;
                }

                float distance = (hole.HitPoint - position).sqrMagnitude;
                if (distance > bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = hole;
            }

            return best;
        }
    }
}
