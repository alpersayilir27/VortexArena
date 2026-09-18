using UnityEngine;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Makes the HUD panel "lazily follow" the head WITHOUT locking to it: the panel stands in front
    /// of the player, slightly below eye level; it stays PUT while the head turns by small angles and
    /// smoothly drifts to the new direction once the dead zone is exceeded.
    ///
    /// Why: the Meta design guideline explicitly advises against a head-locked HUD
    /// ("Avoid locking HUD style content to the user's head movements. Anchor information
    /// and digital content to a space, or loosely follow the user using smoothing animation"
    /// — developers.meta.com/horizon/design/mr-design-guideline). In free-roam PvP a panel glued to
    /// the head is both tiring and blocks the view while aiming.
    /// The ~1 m distance + slight downward offset is also the object placement recommendation of the
    /// same guideline.
    ///
    /// <para><b>Wall avoidance (opt-in):</b> a world-space panel is depth-tested like any mesh, so a
    /// wall or obstacle between the head and the panel hides it — exactly where the player is
    /// standing at the arena boundary. With <see cref="AvoidWalls"/> the panel is pulled towards the
    /// head along its line of sight and scaled by the same ratio, so it keeps its apparent size.
    /// ⚠️ Off by default: it writes <c>localScale</c>, and a head-locked child
    /// (<see cref="HeadLockedHud"/>) would shrink with its parent.</para>
    /// </summary>
    public class HudFollow : MonoBehaviour
    {
        [Header("Takip")]
        [Tooltip("Kafa transformu (CenterEyeAnchor). Boşsa Camera.main kullanılır.")]
        [SerializeField] private Transform head;
        [Tooltip("Panelin kafadan yatay uzaklığı (m).")]
        [SerializeField] private float distance = 1.1f;
        [Tooltip("Göz hizasına göre dikey ofset (m; negatif = aşağıda).")]
        [SerializeField] private float verticalOffset = -0.32f;
        [Tooltip("Konum yumuşatma süresi (s).")]
        [SerializeField] private float positionSmoothTime = 0.175f;
        [Tooltip("Bu açıdan küçük kafa dönüşlerinde panel yerinde kalır (derece).")]
        [SerializeField] private float yawDeadZoneDegrees = 9f;
        [Tooltip("Ölü bölge aşılınca yöne oturma hızı (derece/sn ölçeği).")]
        [SerializeField] private float yawSmoothTime = 0.175f;

        [Header("Duvar kaçınma")]
        [Tooltip("Kafa ile panel arasına duvar/engel girerse panel öne çekilir ve aynı görünen boyutta kalacak şekilde küçülür. Kafaya kilitli çocuğu olan panelde AÇMA (çocuk da küçülür).")]
        [SerializeField] private bool avoidWalls;
        [Tooltip("Panelin kafaya çekilebileceği en kısa uzaklık (m).")]
        [SerializeField] private float minDistance = 0.55f;
        [Tooltip("Panel ile çarpılan yüzey arasında bırakılan pay (m).")]
        [SerializeField] private float wallClearance = 0.08f;

        /// <summary>Hit buffer for the head→panel ray; a full buffer drops hits, so keep it roomy.</summary>
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private Vector3 _positionVelocity;
        private float _yawVelocity;
        private float _currentYaw;
        private float _targetYaw;
        private bool _initialized;
        private Vector3 _baseScale;
        private float _pull = 1f;
        private float _pullVelocity;

        /// <summary>See the class summary; the serialized field is the prefab's initial value.</summary>
        public bool AvoidWalls
        {
            get => avoidWalls;
            set => avoidWalls = value;
        }

        private void Awake()
        {
            _baseScale = transform.localScale;
        }

        private void OnEnable()
        {
            _initialized = false; // on first entry to the scene/death screen the panel snaps straight into place
        }

        private void LateUpdate()
        {
            Transform reference = ResolveHead();
            if (reference == null)
            {
                return;
            }

            float headYaw = reference.eulerAngles.y;

            if (!_initialized)
            {
                _currentYaw = headYaw;
                _targetYaw = headYaw;
                _pull = avoidWalls ? WallPull(reference, _currentYaw) : 1f;
                _pullVelocity = 0f;
                ApplyPullScale();
                transform.position = TargetPosition(reference, _currentYaw, _pull);
                transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
                _positionVelocity = Vector3.zero;
                _yawVelocity = 0f;
                _initialized = true;
                return;
            }

            // Dead zone: the panel's target direction does not change until the head turns enough.
            if (Mathf.Abs(Mathf.DeltaAngle(_targetYaw, headYaw)) > yawDeadZoneDegrees)
            {
                _targetYaw = headYaw;
            }

            _currentYaw = Mathf.SmoothDampAngle(_currentYaw, _targetYaw, ref _yawVelocity, yawSmoothTime);
            if (avoidWalls)
            {
                // Smoothed like the position, so a wall coming into range slides the panel in
                // instead of popping it.
                _pull = Mathf.SmoothDamp(_pull, WallPull(reference, _currentYaw), ref _pullVelocity,
                    positionSmoothTime);
                ApplyPullScale();
            }

            transform.position = Vector3.SmoothDamp(transform.position,
                TargetPosition(reference, _currentYaw, _pull), ref _positionVelocity, positionSmoothTime);
            // The panel faces the user; no tilt (yaw only) — billboard behaviour for legibility.
            transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
        }

        /// <summary>Head→panel offset at full distance; <paramref name="pull"/> (0..1] shortens it along the line of sight.</summary>
        private Vector3 TargetPosition(Transform reference, float yaw, float pull)
        {
            return reference.position + PanelOffset(yaw) * pull;
        }

        private Vector3 PanelOffset(float yaw)
        {
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            return forward * distance + Vector3.up * verticalOffset;
        }

        /// <summary>
        /// Fraction of the full offset the panel may keep before it would sit behind the nearest
        /// solid surface on the head→panel line (1 = nothing in the way). Triggers, the player's own
        /// rig (weapon, hands, overlays under the same root) and the panel itself are ignored.
        /// </summary>
        private float WallPull(Transform reference, float yaw)
        {
            Vector3 offset = PanelOffset(yaw);
            float length = offset.magnitude;
            if (length <= 0.0001f)
            {
                return 1f;
            }

            float floor = distance > 0f ? Mathf.Clamp01(minDistance / distance) : 1f;
            Transform self = reference.root;
            int count = Physics.RaycastNonAlloc(reference.position, offset / length, _hits, length,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = length;
            for (int i = 0; i < count; i++)
            {
                Transform hit = _hits[i].collider.transform;
                if (hit.IsChildOf(self) || hit.IsChildOf(transform))
                {
                    continue;
                }

                nearest = Mathf.Min(nearest, _hits[i].distance);
            }

            return nearest >= length ? 1f : Mathf.Clamp((nearest - wallClearance) / length, floor, 1f);
        }

        /// <summary>Same ratio as the pull, so the panel keeps its angular size when it comes closer.</summary>
        private void ApplyPullScale()
        {
            transform.localScale = _baseScale * _pull;
        }

        private Transform ResolveHead()
        {
            if (head != null)
            {
                return head;
            }

            Camera main = Camera.main;
            if (main != null)
            {
                head = main.transform;
            }

            return head;
        }
    }
}
