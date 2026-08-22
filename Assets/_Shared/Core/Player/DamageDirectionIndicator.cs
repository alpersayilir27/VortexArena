using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>Directional half of the damage feedback: an arc that lights up on the edge of the view
    /// the hit came from, plus a short buzz in the controller on that side.</summary>
    /// <remarks>
    /// <b>Costs NOTHING on the wire.</b> <c>health_update</c> already carries <c>attackerId</c>
    /// (Docs/ArenaNet-Protokol.md §10.3) and the attacker's pose is already in
    /// <see cref="RemotePlayerRegistry"/> from the snapshot channel — the bearing is derived locally,
    /// so no message, field or byte was added for this.
    /// <para>⚠️ <b>Subscribed, not sampled</b> — the opposite of its sibling
    /// <see cref="DamageVignette"/>, and deliberately so: a per-frame health diff can tell you that you
    /// were hit but never by WHOM, and <c>attackerId</c> exists only on the message.</para>
    /// <para>⚠️ The bearing is computed in YAW ONLY (both vectors flattened to XZ). Head pitch and roll
    /// must not move the arc: tilting the head would otherwise spin it around the view, which is the
    /// exact motion this effect exists to avoid.</para>
    /// <para>Owned by the <c>VA_CameraRig</c> prefab, at the SAME local depth as the vignette quad —
    /// a different depth would give the two overlays different stereo disparity and read as double
    /// vision. Layering is by render queue instead (<c>Overlay+1</c>).</para>
    /// </remarks>
    [DefaultExecutionOrder(30201)]
    public class DamageDirectionIndicator : MonoBehaviour
    {
        [Header("Genel")]
        [Tooltip("Ana anahtar — kapalıyken ne gösterge çizilir ne haptik tetiklenir.")]
        [SerializeField] private bool effectEnabled = true;

        [Tooltip("Gösterge quad'ının renderer'ı (bu objenin kendi MeshRenderer'ı).")]
        [SerializeField] private Renderer indicatorRenderer;

        [Tooltip("Bakış transform'u (CenterEyeAnchor). Boşsa bu objenin parent'ı kullanılır.")]
        [SerializeField] private Transform head;

        [Header("Görünüm")]
        [Tooltip("Gösterge rengi (#8E1F1F) — vinyetle aynı dili konuşur.")]
        [SerializeField] private Color indicatorColor = new Color(0.5569f, 0.1216f, 0.1216f);

        [Tooltip("Yayın en yüksek opaklığı.")]
        [Range(0f, 1f)]
        [SerializeField] private float maxAlpha = 0.40f;

        [Header("Zamanlama")]
        [Tooltip("Vuruştan sonra tepe opaklığa çıkma süresi (sn).")]
        [Range(0f, 1f)]
        [SerializeField] private float attackSeconds = 0.06f;

        [Tooltip("Tepeden sıfıra sönme süresi (sn). Eğri sona doğru yavaşlar.")]
        [Range(0.05f, 3f)]
        [SerializeField] private float decaySeconds = 0.40f;

        [Tooltip("Tek pakette tam yoğunluk sayılan hasar (HP).")]
        [SerializeField] private float fullIntensityDamage = 25f;

        [Header("Haptik")]
        [Tooltip("Vuruşun geldiği taraftaki kumandayı titret.")]
        [SerializeField] private bool hapticEnabled = true;

        [Tooltip("Titreşim süresi (sn).")]
        [Range(0f, 1f)]
        [SerializeField] private float hapticSeconds = 0.15f;

        [Tooltip("Titreşim genliği (0..1).")]
        [Range(0f, 1f)]
        [SerializeField] private float hapticAmplitude = 1f;

        /// <summary><see cref="ControllerHaptics"/> source id.</summary>
        private const string HapticSourceId = "damage";

        private const int ChannelNone = 0;
        private const int ChannelLeft = 1;
        private const int ChannelRight = 2;

        /// <summary>Both controllers — used when the hit is real but its direction is unknown.</summary>
        private const int ChannelBoth = 3;

        /// <summary>Below this a flattened vector has no usable bearing (attacker standing on our head,
        /// or a head looking straight down).</summary>
        private const float MinPlanarSqrMagnitude = 1e-4f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _propertyBlock;
        private float _lastHp = ArenaProtocol.PLAYER_MAX_HP;

        // Hit envelope — refreshed, never stacked (see DamageVignette for the rationale).
        private float _hitTime = float.NegativeInfinity;
        private float _hitPeak;
        private float _hitFrom;

        // Haptic window. Driven from LateUpdate rather than a coroutine: the arbiter wants a report
        // every frame anyway, and this component already has a per-frame loop — so the buzz costs no
        // allocation per hit.
        private float _hapticUntil;
        private int _hapticChannel;

        private void Awake()
        {
            if (indicatorRenderer == null)
            {
                indicatorRenderer = GetComponent<Renderer>();
            }

            if (head == null)
            {
                head = transform.parent;
            }

            _propertyBlock = new MaterialPropertyBlock();
            Draw(0f);
        }

        private void OnEnable()
        {
            // Reseeded here, not in Awake: a stale value across a disable would read as a phantom hit.
            _lastHp = ArenaCombat.LocalHp;
            NetEvents.OnHealthUpdate += HandleHealthUpdate;
        }

        private void OnDisable()
        {
            NetEvents.OnHealthUpdate -= HandleHealthUpdate;

            if (_hapticChannel != ChannelNone)
            {
                // Released explicitly: without a report the arbiter only forgets us after its timeout,
                // and a component disabled mid-buzz would keep the controller running until then.
                WriteHaptic(_hapticChannel, 0f);
                _hapticChannel = ChannelNone;
            }

            _hitPeak = 0f;
            Draw(0f);
        }

        private void HandleHealthUpdate(HealthUpdateMsg msg)
        {
            if (!effectEnabled || msg == null)
            {
                return;
            }

            int localId = ArenaCombat.LocalPlayerId;
            if (localId == 0 || msg.playerId != localId)
            {
                return;
            }

            float previous = _lastHp;
            _lastHp = msg.hp;

            // attackerId 0 = environmental damage or a revive (§10.3): there is no direction to point
            // at, and a guessed one is worse than none. The vignette still fires — it samples health.
            if (msg.attackerId == 0 || msg.hp >= previous)
            {
                return;
            }

            float bearing = 0f;
            bool hasBearing = TryGetBearing(msg.attackerId, out bearing);

            // ⚠️ The buzz does NOT wait for the bearing. An attacker missing from the snapshot (just
            // joined, packet loss, shot from outside the interpolation window) is exactly the case where
            // the player is being hurt with no visual explanation — going silent there would drop the
            // one piece of feedback that never fails.
            if (hapticEnabled && hapticSeconds > 0f)
            {
                BeginHaptic(hasBearing ? (bearing >= 0f ? ChannelRight : ChannelLeft) : ChannelBoth);
            }

            if (!hasBearing)
            {
                return; // no arc without a direction — a guessed one is worse than none
            }

            // Screen angle: the arc is authored at the TOP of the quad (bearing 0 = straight ahead).
            // Rotating about local Z by -bearing carries the top toward +X, i.e. screen right, so
            // +90 lands on the right edge.
            transform.localRotation = Quaternion.Euler(0f, 0f, -bearing);

            float intensity = fullIntensityDamage > 0f
                ? Mathf.Clamp01((previous - msg.hp) / fullIntensityDamage)
                : 1f;

            float current = HitEnvelope();
            _hitFrom = current;
            _hitPeak = Mathf.Max(current, intensity);
            _hitTime = Time.unscaledTime;
        }

        /// <summary>Opens the buzz window on one channel. A hit on a DIFFERENT channel while one is
        /// running releases the previous one explicitly — left to time out on its own it would keep
        /// buzzing for the arbiter's whole timeout and read as "both controllers".</summary>
        private void BeginHaptic(int channel)
        {
            if (_hapticChannel != ChannelNone && _hapticChannel != channel)
            {
                WriteHaptic(_hapticChannel, 0f);
            }

            _hapticChannel = channel;
            _hapticUntil = Time.unscaledTime + hapticSeconds;
        }

        /// <summary>⚠️ Through the arbiter, never straight to OVRInput: a vibration written behind its
        /// back is either swallowed by its "already wrote that amplitude" check or left switched on.</summary>
        private void WriteHaptic(int channel, float amplitude)
        {
            switch (channel)
            {
                case ChannelLeft:
                    ControllerHaptics.ReportHand(HapticSourceId, false, amplitude);
                    break;
                case ChannelRight:
                    ControllerHaptics.ReportHand(HapticSourceId, true, amplitude);
                    break;
                case ChannelBoth:
                    ControllerHaptics.Report(HapticSourceId, amplitude);
                    break;
            }
        }

        private void TickHaptic()
        {
            if (_hapticChannel == ChannelNone)
            {
                return;
            }

            if (Time.unscaledTime >= _hapticUntil)
            {
                WriteHaptic(_hapticChannel, 0f);
                _hapticChannel = ChannelNone;
                return;
            }

            WriteHaptic(_hapticChannel, hapticAmplitude);
        }

        /// <summary>Signed YAW bearing to the attacker in degrees: 0 straight ahead, +90 hard right,
        /// ±180 behind. False when there is no usable pose.</summary>
        private bool TryGetBearing(int attackerId, out float bearing)
        {
            bearing = 0f;

            Transform view = head;
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (view == null || registry == null)
            {
                return false;
            }

            if (!registry.GetInterpolatedPose(attackerId, out Pose attackerHead, out _, out _))
            {
                return false;
            }

            Vector3 toAttacker = ArenaSpace.ArenaToWorld(attackerHead.position) - view.position;
            toAttacker.y = 0f;

            Vector3 forward = view.forward;
            forward.y = 0f;

            if (toAttacker.sqrMagnitude < MinPlanarSqrMagnitude ||
                forward.sqrMagnitude < MinPlanarSqrMagnitude)
            {
                return false;
            }

            // ⚠️ Flattened BEFORE the angle — this is the yaw-only rule, not an optimisation.
            bearing = Vector3.SignedAngle(forward, toAttacker, Vector3.up);
            return true;
        }

        private void LateUpdate()
        {
            TickHaptic();

            if (!effectEnabled)
            {
                Draw(0f);
                return;
            }

            if (!ArenaCombat.IsAlive)
            {
                // Same gate as the vignette: the death overlay presents itself.
                _hitPeak = 0f;
                Draw(0f);
                return;
            }

            Draw(HitEnvelope() * maxAlpha);
        }

        /// <summary>Attack then ease-out decay, 0..1. unscaledTime for the same reason as everywhere
        /// else in this layer: presentation must not follow Time.timeScale.</summary>
        private float HitEnvelope()
        {
            if (_hitPeak <= 0f)
            {
                return 0f;
            }

            float elapsed = Time.unscaledTime - _hitTime;
            if (elapsed <= 0f)
            {
                return _hitFrom;
            }

            if (elapsed < attackSeconds)
            {
                return Mathf.Lerp(_hitFrom, _hitPeak, elapsed / attackSeconds);
            }

            if (decaySeconds <= 0f)
            {
                return 0f;
            }

            float u = (elapsed - attackSeconds) / decaySeconds;
            if (u >= 1f)
            {
                return 0f;
            }

            float remaining = 1f - u;
            return _hitPeak * remaining * remaining;
        }

        private void Draw(float alpha)
        {
            if (indicatorRenderer == null)
            {
                return;
            }

            bool visible = alpha > 0.001f;
            indicatorRenderer.enabled = visible;
            if (!visible)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            indicatorRenderer.GetPropertyBlock(_propertyBlock);

            // .linear: the project renders Linear but a serialized Color holds what the picker showed
            // (sRGB) and SetColor converts nothing — see DamageVignette.
            Color color = indicatorColor.linear;
            color.a = alpha;
            _propertyBlock.SetColor(BaseColorId, color);
            indicatorRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
