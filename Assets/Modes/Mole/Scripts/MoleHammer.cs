using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Modes.Mole
{
    /// <summary>Local hammer: head collider touching a standing mole during a fast swing raises <c>whack</c> (§10.5).
    /// <para>Setup: trigger <see cref="SphereCollider"/> on the head, any collider on the mole — no
    /// mole measurements in code. "Fast" = peak head speed within <c>speedWindowSeconds</c>, so a swing
    /// that decelerates on impact still counts; a slow touch/push does not.</para>
    /// <para>⚠️ Sweep, not <c>OnTriggerEnter</c>: the head jumps clean through the mole between frames
    /// ("works sometimes"), so each frame the head sphere is swept from its previous position.</para>
    /// <para>⚠️ Only the LOCAL hammer runs this: remote copies are stripped of behaviours/colliders
    /// (§6.6), so exactly one client reports a pop.</para>
    /// <para>⚠️ The mole capsule is static and covers the whole standing envelope, so a resting head is
    /// already "in contact" the moment a mole pops. A pop whose FIRST contact is slow is latched as
    /// embedded: until the head leaves it (radius + <c>embeddedExitMargin</c>) it only counts on an
    /// <c>embeddedSwingSpeed</c> swing, so a wiggle or tracking jitter inside the capsule is no hit.</para></summary>
    [DisallowMultipleComponent]
    public sealed class MoleHammer : MonoBehaviour
    {
        [Tooltip("Balyozun ucundaki vuruş küresi (trigger olmalı). Boşsa çocuklarda aranır — " +
                 "yarıçapı ve konumu Inspector'dan görsel ayarlanır, kodda ölçü yoktur.")]
        [SerializeField] private SphereCollider hitCollider;

        [Tooltip("Vuruş sayılması için gereken en düşük baş hızı (m/s) — Speed Window Seconds süresi içindeki " +
                 "en yüksek hıza bakılır. Yavaş dokunma/itme vuruş sayılmaz.")]
        [SerializeField] private float minSwingSpeed = 1.5f;

        [Tooltip("Vuruş hızı bu süre içindeki en yüksek baş hızıdır (sn). Çarpma anında yavaşlayan " +
                 "salınım da sayılır.")]
        [SerializeField] private float speedWindowSeconds = 0.1f;

        [Tooltip("Köstebek balyozun içine doğru çıktıysa ya da balyoz yavaşça bastırıldıysa, balyoz dışarı " +
                 "çıkmadan vuruş ancak bu hızda sert bir salınımla sayılır (m/s). İçerideki küçük kıpırdama " +
                 "vuruş sayılmaz.")]
        [SerializeField] private float embeddedSwingSpeed = 3f;

        [Tooltip("İçine gömülmüş balyozun köstebeği terk etmiş sayılması için vuruş küresinin köstebekten " +
                 "bu kadar uzaklaşması gerekir (m). Sınırda titreyen balyoz her girişte yeniden vuruş " +
                 "denemesi saymasın diye.")]
        [SerializeField] private float embeddedExitMargin = 0.05f;

        /// <summary>Per-frame contact buffer; sized for scenery + own colliders so moles are not pushed out.</summary>
        private const int MaxContacts = 32;

        /// <summary>Covers the speed window at 120+ Hz.</summary>
        private const int SpeedSamples = 32;

        private readonly RaycastHit[] _sweepHits = new RaycastHit[MaxContacts];
        private readonly Collider[] _overlaps = new Collider[MaxContacts];

        private readonly float[] _speedValues = new float[SpeedSamples];
        private readonly float[] _speedTimes = new float[SpeedSamples];
        private int _speedHead;
        private int _speedCount;

        private Vector3 _lastCenter;
        private bool _hasLastCenter;

        /// <summary>Last reported pop. ⚠️ Keyed by (hole, counter), not time: a swing spans several frames.</summary>
        private int _reportedNetId;
        private int _reportedNonce = -1;

        /// <summary>Pop whose first contact was too slow; counts only on a hard swing until the head leaves it.</summary>
        private MoleHole _embeddedHole;
        private int _embeddedNonce = -1;

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

            // A solid hand-held collider pushes the player's own body around.
            if (!hitCollider.isTrigger)
            {
                Debug.LogWarning($"[MoleHammer] '{hitCollider.name}' trigger değil — vuruş yine " +
                                 "çalışır ama balyoz sahneye fiziksel olarak çarpar.", this);
            }

            if (embeddedSwingSpeed < minSwingSpeed)
            {
                Debug.LogWarning($"[MoleHammer] '{name}' üzerinde Embedded Swing Speed, Min Swing Speed'in " +
                                 "altında — gömülü balyozun eşiği normal vuruştan kolay olur.", this);
            }
        }

        // Re-enable/re-grant may teleport the head; without a reset that jump reads as a huge speed.
        private void OnEnable() => ResetTracking();

        private void OnDisable() => ResetTracking();

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
            if (deltaTime <= 0f)
            {
                return;
            }

            if (!CalibrationState.IsCalibrated)
            {
                // Calibration moves the rig; re-seed so that jump is not read as a swing.
                ResetTracking();
                return;
            }

            float now = Time.time;
            RecordSpeed(now, delta.magnitude / deltaTime);

            // Always tested: a decelerating impact frame must still find the contact.
            MoleHole target = FindContact(center, delta);
            if (target == null || !target.IsHittable || target.Nonce < 0)
            {
                UpdateEmbeddedExit(center);
                return;
            }

            bool embedded = target == _embeddedHole && target.Nonce == _embeddedNonce;
            float required = embedded ? embeddedSwingSpeed : minSwingSpeed;
            if (PeakSpeed(now) < required)
            {
                if (!embedded)
                {
                    // First contact of this pop was slow: the mole rose into a resting head, or a slow press.
                    _embeddedHole = target;
                    _embeddedNonce = target.Nonce;
                }

                return;
            }

            if (target.NetId == _reportedNetId && target.Nonce == _reportedNonce)
            {
                return;
            }

            _embeddedHole = null;
            _embeddedNonce = -1;
            _reportedNetId = target.NetId;
            _reportedNonce = target.Nonce;
            NetObjectSync.SendEvent(target.NetId, MoleKinds.EventWhack, new[] { target.Nonce });
        }

        private void ResetTracking()
        {
            _hasLastCenter = false;
            _speedHead = 0;
            _speedCount = 0;
            _embeddedHole = null;
            _embeddedNonce = -1;
        }

        /// <summary>Clears the embedded latch once that pop ended or the head is clearly out of it.</summary>
        private void UpdateEmbeddedExit(Vector3 center)
        {
            if (_embeddedHole == null)
            {
                return;
            }

            if (!_embeddedHole.IsHittable || _embeddedHole.Nonce != _embeddedNonce)
            {
                _embeddedHole = null;
                _embeddedNonce = -1;
                return;
            }

            // Hysteresis: a wider sphere than the hit one, so jitter on the boundary does not re-arm.
            int count = Physics.OverlapSphereNonAlloc(center, WorldRadius() + embeddedExitMargin, _overlaps,
                ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                if (Resolve(_overlaps[i]) == _embeddedHole)
                {
                    return;
                }
            }

            _embeddedHole = null;
            _embeddedNonce = -1;
        }

        private void RecordSpeed(float time, float speed)
        {
            _speedValues[_speedHead] = speed;
            _speedTimes[_speedHead] = time;
            _speedHead = (_speedHead + 1) % SpeedSamples;
            if (_speedCount < SpeedSamples)
            {
                _speedCount++;
            }
        }

        /// <summary>Highest recorded speed within the window ending at <paramref name="now"/>.</summary>
        private float PeakSpeed(float now)
        {
            float peak = 0f;
            for (int i = 0; i < _speedCount; i++)
            {
                if (now - _speedTimes[i] <= speedWindowSeconds && _speedValues[i] > peak)
                {
                    peak = _speedValues[i];
                }
            }

            return peak;
        }

        /// <summary>The mole the head touched this frame, or null. Overlap first (head may already be inside), then sweep.</summary>
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

        /// <summary>Hole owning a touched collider, or null. Searched upwards: a mole model brings its own hierarchy.</summary>
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
