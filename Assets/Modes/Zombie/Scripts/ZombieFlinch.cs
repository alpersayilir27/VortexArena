using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Procedural hit reaction: a damped spring that bends the bone chain nearest the shot
    /// away from it, on top of whatever the Animator is playing.</summary>
    /// <remarks>⚠️ Bone references are written by <c>ZombieLookBuilder</c>; a hand edit is lost on the
    /// next run of the tool.
    /// <para>⚠️ Runs in <c>LateUpdate</c>, i.e. AFTER the Animator has written the pose — in
    /// <c>Update</c> the animation would overwrite the bend in the same frame and nothing would move.
    /// </para>
    /// <para>⚠️ Why a spring and not a clip: the clip layer takes 0.15 s to blend in and cannot play
    /// on a crawler or a boss at all, while a hit must read on EVERY body and on every round of a
    /// burst. Allocation-free on purpose — thirty bodies tick this per frame.</para></remarks>
    [DisallowMultipleComponent]
    public class ZombieFlinch : MonoBehaviour
    {
        private const float Stiffness = 140f;

        private const float Damping = 16f;

        /// <summary>Below this (deg / deg per s) the chain is parked: an endlessly ticking spring
        /// would keep writing bone rotations for the body's whole life.</summary>
        private const float RestAngle = 0.05f;

        private const float RestVelocity = 0.5f;

        /// <summary>Hard cap (deg) on the total bend: a burst stacks kicks, and without this the neck
        /// folds backwards.</summary>
        private const float MaxAngle = 26f;

        // Amplitudes (deg) per zone.
        private const float HeadAmplitude = 18f;
        private const float BodyAmplitude = 11f;
        private const float StomachAmplitude = 9f;
        private const float LegAmplitude = 7f;

        /// <summary>A kick is given as VELOCITY, not as an instant angle: an instant one pops on the
        /// first frame. Scaled so the damped peak lands on the requested amplitude.</summary>
        private static readonly float ImpulseGain = Mathf.Sqrt(Stiffness) * 1.8f;

        private const int ChainCount = 4;
        private const int ChainTorso = 0;
        private const int ChainArmLeft = 1;
        private const int ChainArmRight = 2;
        private const int ChainLower = 3;

        [Header("Gövde zinciri")]
        [SerializeField] private Transform spine1;
        [SerializeField] private Transform spine2;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform head;

        [Header("Kollar")]
        [SerializeField] private Transform shoulderLeft;
        [SerializeField] private Transform upperArmLeft;
        [SerializeField] private Transform shoulderRight;
        [SerializeField] private Transform upperArmRight;

        [Header("Alt gövde")]
        [SerializeField] private Transform hips;
        [SerializeField] private Transform upperLegLeft;
        [SerializeField] private Transform upperLegRight;

        private readonly Transform[][] _bones = new Transform[ChainCount][];
        private readonly float[][] _weights = new float[ChainCount][];
        private readonly float[] _angle = new float[ChainCount];
        private readonly float[] _velocity = new float[ChainCount];
        private readonly Vector3[] _axis = new Vector3[ChainCount];

        /// <summary>Per-kind amplitude scale — a boss shrugged by every round reads as stunned.</summary>
        private float _scale = 1f;

        /// <summary>A crawler has no standing chain to bend: everything goes into spine + head.</summary>
        private bool _crawler;

        private void Awake()
        {
            // Allocated once, here: the per-chain arrays are walked every frame.
            _bones[ChainTorso] = new[] { spine1, spine2, neck, head };
            _weights[ChainTorso] = new[] { 0.30f, 0.30f, 0.25f, 0.15f };

            _bones[ChainArmLeft] = new[] { shoulderLeft, upperArmLeft };
            _weights[ChainArmLeft] = new[] { 0.40f, 0.60f };

            _bones[ChainArmRight] = new[] { shoulderRight, upperArmRight };
            _weights[ChainArmRight] = new[] { 0.40f, 0.60f };

            _bones[ChainLower] = new[] { hips, upperLegLeft, upperLegRight };
            _weights[ChainLower] = new[] { 0.50f, 0.25f, 0.25f };
        }

        /// <summary>Kind-dependent behaviour; called once the body is known.</summary>
        public void Configure(bool crawler, float amplitudeScale)
        {
            _crawler = crawler;
            _scale = Mathf.Max(0f, amplitudeScale);
        }

        /// <summary>A round landed: kicks the chain nearest the hit.</summary>
        /// <param name="zone">Hit zone — amplitude and chain come from it.</param>
        /// <param name="directionWorld">Shot direction (need not be normalized); zero = no push.</param>
        /// <param name="pointWorld">World hit point — picks the arm side.</param>
        public void Hit(HitZone zone, Vector3 directionWorld, Vector3 pointWorld)
        {
            float amplitude = AmplitudeOf(zone) * _scale;
            if (amplitude < 0.5f)
            {
                return;
            }

            Vector3 direction = directionWorld;
            if (direction.sqrMagnitude < 1e-6f)
            {
                direction = -transform.forward;
            }

            // Rotating around (up × shot) tips the body ALONG the shot — the one axis that reads
            // correctly whatever side the round came from.
            Vector3 axis = Vector3.Cross(Vector3.up, direction.normalized);
            if (axis.sqrMagnitude < 1e-6f)
            {
                // Straight up or down: any horizontal axis will do.
                axis = transform.right;
            }

            axis.Normalize();

            int chain = PickChain(zone, pointWorld);
            Kick(chain, axis, amplitude);

            if (chain == ChainArmLeft || chain == ChainArmRight)
            {
                // The torso follows an arm hit, faintly: an arm moving alone reads as detached.
                Kick(ChainTorso, axis, amplitude * 0.4f);
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            for (int chain = 0; chain < ChainCount; chain++)
            {
                float angle = _angle[chain];
                float velocity = _velocity[chain];

                if (angle == 0f && velocity == 0f)
                {
                    continue;
                }

                // Semi-implicit Euler: stable at this stiffness and one multiply cheaper than RK.
                velocity += (-Stiffness * angle - Damping * velocity) * dt;
                angle = Mathf.Clamp(angle + velocity * dt, -MaxAngle, MaxAngle);

                if (Mathf.Abs(angle) < RestAngle && Mathf.Abs(velocity) < RestVelocity)
                {
                    _angle[chain] = 0f;
                    _velocity[chain] = 0f;
                    continue;
                }

                _angle[chain] = angle;
                _velocity[chain] = velocity;
                Apply(chain, angle);
            }
        }

        /// <summary>Bends one chain. ⚠️ Parent first: each bone is rotated on top of the pose its
        /// parent already carries, which is what makes the bend accumulate down the chain instead of
        /// snapping at one joint.</summary>
        private void Apply(int chain, float angle)
        {
            Transform[] bones = _bones[chain];
            float[] weights = _weights[chain];
            Vector3 axis = _axis[chain];

            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                if (bone == null)
                {
                    continue;
                }

                bone.rotation = Quaternion.AngleAxis(angle * weights[i], axis) * bone.rotation;
            }
        }

        private void Kick(int chain, Vector3 axis, float amplitude)
        {
            // The newest hit owns the axis: averaging two directions cancels a pair of crossfire
            // rounds into no reaction at all.
            _axis[chain] = axis;
            _velocity[chain] += amplitude * ImpulseGain;
        }

        /// <summary>Which chain a hit bends. Arms are <see cref="HitZone.Body"/> like the chest, so the
        /// side is decided by DISTANCE, not by the zone.</summary>
        private int PickChain(HitZone zone, Vector3 pointWorld)
        {
            if (_crawler || zone == HitZone.Head)
            {
                return ChainTorso;
            }

            if (zone == HitZone.Stomach || zone == HitZone.Leg)
            {
                return ChainLower;
            }

            Transform torso = spine2 != null ? spine2 : hips;
            float torsoDistance = torso != null
                ? (pointWorld - torso.position).sqrMagnitude
                : float.MaxValue;

            float leftDistance = upperArmLeft != null
                ? (pointWorld - upperArmLeft.position).sqrMagnitude
                : float.MaxValue;
            float rightDistance = upperArmRight != null
                ? (pointWorld - upperArmRight.position).sqrMagnitude
                : float.MaxValue;

            if (Mathf.Min(leftDistance, rightDistance) >= torsoDistance)
            {
                return ChainTorso;
            }

            return leftDistance <= rightDistance ? ChainArmLeft : ChainArmRight;
        }

        private static float AmplitudeOf(HitZone zone) => zone switch
        {
            HitZone.Head => HeadAmplitude,
            HitZone.Stomach => StomachAmplitude,
            HitZone.Leg => LegAmplitude,
            _ => BodyAmplitude,
        };
    }
}
