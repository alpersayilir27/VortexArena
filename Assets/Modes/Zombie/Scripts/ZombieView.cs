using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie's PRESENTATION on every headset: applies the owner's streamed pose and animates
    /// the body procedurally (walk, attack lunge, collapse). No rules, no authority.</summary>
    /// <remarks>⚠️ <b>No Animator and no clips:</b> the prototype's model has none, and an Animator would
    /// need its own state machine on a prefab that is still changing. Everything here comes from the
    /// MEASURED pose: walking is read from the pose delta, so it is in step with the real movement on
    /// every headset and needs no extra network field.
    /// <para>⚠️ The attack is played from the payload's attack counter (<see cref="ZombieKinds.PayloadAttack"/>)
    /// and only on a <see cref="NetStateOrigin.Live"/> state: a late joiner's snapshot carries the same
    /// counter, and reacting to it would make every zombie lunge at the moment they walk in.</para>
    /// <para>⚠️ When the stream stops the LAST pose is kept, never the origin: a dead or
    /// temporarily brainless zombie must stay where it was, not jump to the arena's centre.</para></remarks>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public class ZombieView : MonoBehaviour
    {
        [Header("Gövde parçaları (prosedürel animasyon)")]
        [Tooltip("Yürüyüş salınımı ve çökme bu transform'a uygulanır — KÖK değil, kökü ağ akışı sürüyor.")]
        [SerializeField] private Transform body;
        [Tooltip("Opsiyonel: hafif baş salınımı.")]
        [SerializeField] private Transform head;
        [Tooltip("Opsiyonel: öne uzanan sol kol.")]
        [SerializeField] private Transform leftArm;
        [Tooltip("Opsiyonel: öne uzanan sağ kol.")]
        [SerializeField] private Transform rightArm;

        [Header("Görünürlük")]
        [Tooltip("Boş bırakılırsa alt hiyerarşideki tüm renderer'lar kullanılır.")]
        [SerializeField] private Renderer[] renderers;
        [Tooltip("Vuruş kutularının kökü — zombi kırılınca kapatılır. Opsiyonel.")]
        [SerializeField] private GameObject hitboxRoot;

        [Header("Hareket")]
        [Tooltip("Yürüyüş salınımının genliği (m).")]
        [SerializeField] private float bobHeight = 0.05f;
        [Tooltip("Yürüyüş yan yatma genliği (derece).")]
        [SerializeField] private float swayDegrees = 7f;
        [Tooltip("Vuruş hamlesinin süresi (sn).")]
        [SerializeField] private float lungeSeconds = 0.35f;
        [Tooltip("Vuruş hamlesinde gövdenin öne gidişi (m).")]
        [SerializeField] private float lungeReach = 0.22f;
        [Tooltip("Çökmenin süresi (sn) — sunucunun despawn gecikmesinden kısa olmalı.")]
        [SerializeField] private float collapseSeconds = 1.2f;

        private NetObject _net;

        // Local baselines of the animated parts; everything is written as an OFFSET from these, so a
        // model whose bones move only needs re-authoring, not code changes.
        private Vector3 _bodyPosition;
        private Quaternion _bodyRotation;
        private Quaternion _headRotation;
        private Quaternion _leftArmRotation;
        private Quaternion _rightArmRotation;

        private Vector3 _lastPosition;
        private float _speed;
        private float _phase;

        private float _lungeLeft;
        private float _collapsed;
        private int _attackCounter = -1;

        /// <summary>Have we ever had a pose for this zombie? Until then it is INVISIBLE: the server spawns
        /// it with a meaningless pose (§10.5), and the prototype has no spawn animation to cover the jump
        /// from the origin.</summary>
        private bool _posed;

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
            }

            if (body != null)
            {
                body.GetLocalPositionAndRotation(out _bodyPosition, out _bodyRotation);
            }

            if (head != null)
            {
                _headRotation = head.localRotation;
            }

            if (leftArm != null)
            {
                _leftArmRotation = leftArm.localRotation;
            }

            if (rightArm != null)
            {
                _rightArmRotation = rightArm.localRotation;
            }

            _lastPosition = transform.position;
            SetVisible(false);
        }

        private void OnEnable()
        {
            _net.StateChanged += HandleStateChanged;
            _attackCounter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadAttack, -1);
        }

        private void OnDisable()
        {
            _net.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(NetObject netObject, NetStateOrigin origin)
        {
            int counter = ZombieKinds.PayloadInt(_net, ZombieKinds.PayloadAttack, _attackCounter);

            if (origin == NetStateOrigin.Live && counter > _attackCounter && !_net.IsBroken)
            {
                _lungeLeft = lungeSeconds;
            }

            _attackCounter = counter;

            if (_net.IsBroken && hitboxRoot != null && hitboxRoot.activeSelf)
            {
                // Hit boxes go down with the first broken state: a corpse that still reports hits lets the
                // squad farm score off it for the whole despawn delay.
                hitboxRoot.SetActive(false);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            ApplyStream();
            MeasureSpeed(dt);

            if (_net.IsBroken)
            {
                Collapse(dt);
                return;
            }

            Animate(dt);
        }

        /// <summary>Puts the body on the owner's interpolated pose. ⚠️ Not applied on the OWNER's machine:
        /// there the brain's agent drives the transform and the registry refuses the pose anyway
        /// (<see cref="RemoteObjectRegistry.TryGetInterpolatedPose"/>).</summary>
        private void ApplyStream()
        {
            if (_net.NetId <= 0)
            {
                return;
            }

            if (_net.IsMine)
            {
                SetVisible(true);
                _posed = true;
                return;
            }

            RemoteObjectRegistry streams = ArenaClient.Instance != null
                ? ArenaClient.Instance.RemoteObjects
                : null;
            if (streams == null || !streams.TryGetInterpolatedPose(_net.NetId, out Pose arenaPose))
            {
                return;
            }

            Pose worldPose = ArenaSpace.ArenaToWorld(arenaPose);
            transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);

            if (!_posed)
            {
                _posed = true;
                _lastPosition = worldPose.position;
                SetVisible(true);
            }
        }

        /// <summary>Walking speed from the pose DELTA — the one measure that exists on every headset
        /// (the agent lives only on the owner's).</summary>
        private void MeasureSpeed(float dt)
        {
            Vector3 position = transform.position;
            float raw = dt > 0f ? Vector3.Distance(position, _lastPosition) / dt : 0f;
            _lastPosition = position;

            // Smoothed: the stream arrives at OBJECT_POSE_RATE_HZ while this runs per frame, so the raw
            // delta is zero on most frames and the gait would stutter.
            _speed = Mathf.Lerp(_speed, raw, 1f - Mathf.Exp(-8f * dt));
        }

        private void Animate(float dt)
        {
            if (body == null)
            {
                return;
            }

            float stride = Mathf.Clamp01(_speed / ZombieKinds.SpeedCap);
            _phase += dt * Mathf.Lerp(1.5f, 7f, stride);

            float bob = Mathf.Abs(Mathf.Sin(_phase)) * bobHeight * stride;
            float sway = Mathf.Sin(_phase) * swayDegrees * stride;

            float lunge = 0f;
            if (_lungeLeft > 0f)
            {
                _lungeLeft = Mathf.Max(0f, _lungeLeft - dt);
                // One in-out arc over the whole lunge.
                lunge = Mathf.Sin((1f - _lungeLeft / lungeSeconds) * Mathf.PI);
            }

            body.SetLocalPositionAndRotation(
                _bodyPosition + new Vector3(0f, bob, lunge * lungeReach),
                _bodyRotation * Quaternion.Euler(lunge * 18f, 0f, sway));

            if (head != null)
            {
                head.localRotation = _headRotation * Quaternion.Euler(0f, sway * 0.5f, -sway * 0.5f);
            }

            // Arms hang forward and swing opposite each other; the lunge throws both of them up.
            float swing = Mathf.Sin(_phase) * 12f * stride;
            if (leftArm != null)
            {
                leftArm.localRotation = _leftArmRotation * Quaternion.Euler(-swing - lunge * 35f, 0f, 0f);
            }

            if (rightArm != null)
            {
                rightArm.localRotation = _rightArmRotation * Quaternion.Euler(swing - lunge * 35f, 0f, 0f);
            }
        }

        /// <summary>Death: tips over and sinks. The server despawns it after its own delay (§10.5), so this
        /// only has to be SHORTER than that — it is never responsible for removing the object.</summary>
        private void Collapse(float dt)
        {
            if (body == null)
            {
                return;
            }

            _collapsed = Mathf.Min(1f, _collapsed + dt / Mathf.Max(0.1f, collapseSeconds));
            float fall = Mathf.SmoothStep(0f, 1f, _collapsed);

            body.SetLocalPositionAndRotation(
                _bodyPosition + new Vector3(0f, -0.35f * fall, 0.25f * fall),
                _bodyRotation * Quaternion.Euler(85f * fall, 0f, 12f * fall));
        }

        private void SetVisible(bool visible)
        {
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled != visible)
                {
                    renderers[i].enabled = visible;
                }
            }
        }
    }
}
