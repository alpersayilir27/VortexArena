using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// <b>Proximity grab socket</b>: where an item is taken FROM. Sits on the prefab, says where the
    /// socket is and how wide it accepts, draws a gizmo in the editor and an indicator sphere in game
    /// once a hand approaches.
    /// <para>⚠️ <b>A socket is NOT a grip record.</b> The socket answers "from where can it be taken"
    /// (position + accept radius, in the prefab); the grip record answers "how does the hand sit once
    /// it is taken" (authored in the studio, stored in <see cref="ItemDefinition"/>). Mixing the two
    /// makes an item that is grabbable where the hand does not fit, or vice versa.</para>
    /// <para>⚠️ <b>The sphere the player sees IS the acceptance volume</b> — the indicator prefab is
    /// authored at 1 m diameter and scaled here to twice <see cref="acceptRadius"/>. Separate numbers
    /// produce "I am inside but it will not take", which reads as a broken grab.</para>
    /// <para>⚠️ The measured point is the controller ANCHOR, not the wrist: the hand visual sits
    /// centimetres away from it, so measuring the wrist would refuse a grab where the player is told
    /// "you are in".</para>
    /// <para>This is the ONE proximity implementation — <see cref="WristHolster"/> drives it through
    /// <see cref="Configure"/> instead of carrying its own copy.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GripSocket : MonoBehaviour
    {
        /// <summary>Distance at which the socket becomes VISIBLE (m, controller anchor to socket) — the
        /// same figure as the weapon's front-grip socket (<c>Weapon.SecondaryGripHoverRadius</c>): two
        /// sockets appearing at different distances read as a bug, not as a feature.</summary>
        private const float HoverRadius = 0.30f;

        /// <summary>Alpha while approaching, and while the controller is INSIDE (slightly more solid to
        /// read as "you are in, press"; colour and size never change).</summary>
        private const float IndicatorHoverAlpha = 0.30f;
        private const float IndicatorReadyAlpha = 0.50f;

        private static readonly Color IndicatorColor = new Color(0.55f, 0.82f, 1f, 1f);

        [Tooltip("Kabul küresinin prefabı — 1 m ÇAPINDA tasarlanır, ölçeği koddan verilir. Boşsa " +
                 "gösterge çizilmez (eşyayı almak yine çalışır).")]
        [SerializeField] private GameObject socketIndicatorPrefab;

        [Tooltip("Kabul yarıçapı (m): kumanda anchor'ı bu kürenin içindeyken kavramaya basılınca eşya " +
                 "ele gelir. Oyuncunun gördüğü küre de tam bu yarıçapla çizilir (0.12 = 24 cm çap).")]
        [SerializeField] private float acceptRadius = 0.12f;

        [Tooltip("SOL el bu soketten alabilir mi.")]
        [SerializeField] private bool acceptsLeftHand = true;

        [Tooltip("SAĞ el bu soketten alabilir mi.")]
        [SerializeField] private bool acceptsRightHand = true;

        private Transform _indicator;
        private LineRenderer _indicatorLine;
        private Material _indicatorMaterial;
        private bool _indicatorPrefabWarned;

        /// <summary>Accept radius (m). ⚠️ The 1 cm floor must stay: a zero radius makes the socket
        /// mathematically unreachable, and in the field that shows up NOT as an error but as "the item
        /// cannot be picked up", which is expensive to diagnose.</summary>
        public float AcceptRadius => Mathf.Max(0.01f, acceptRadius);

        /// <summary>Configures a socket created/driven by CODE (the wrist holster keeps its own
        /// serialized fields on the holster and pushes them here) — so there is exactly one proximity
        /// implementation and no prefab has to be re-wired.</summary>
        public void Configure(GameObject indicatorPrefab, float radius, bool acceptsLeft, bool acceptsRight)
        {
            // Only while the indicator has not been built yet: swapping the prefab under a live
            // instance would leave the old sphere in the scene.
            if (_indicator == null)
            {
                socketIndicatorPrefab = indicatorPrefab;
            }

            acceptRadius = radius;
            acceptsLeftHand = acceptsLeft;
            acceptsRightHand = acceptsRight;
        }

        /// <summary>Does this socket accept that hand at all (prefab decision).</summary>
        public bool Accepts(bool rightHand)
        {
            return rightHand ? acceptsRightHand : acceptsLeftHand;
        }

        /// <summary>Distance from a controller ANCHOR to the socket; <c>false</c> with no rig
        /// (spectator, editor session) or a hand this socket does not accept.
        /// <para>The single measurement behind both the indicator and the take gate, so the two can
        /// never disagree.</para></summary>
        public bool TryMeasure(OVRInput.Controller hand, out float distance)
        {
            distance = 0f;

            bool rightHand = hand == OVRInput.Controller.RTouch;
            if (!Accepts(rightHand))
            {
                return false;
            }

            Transform anchor = WeaponGranter.ResolveHandAnchor(hand);
            if (anchor == null)
            {
                return false;
            }

            distance = Vector3.Distance(anchor.position, transform.position);
            return true;
        }

        /// <summary>Is that controller INSIDE the accept radius right now.</summary>
        public bool IsInside(OVRInput.Controller hand)
        {
            return TryMeasure(hand, out float distance) && distance <= AcceptRadius;
        }

        /// <summary>The accepted hand inside the radius, NEAREST first; <c>false</c> when neither is in.
        /// <para>Nearest wins so that two hands in one socket produce a defined answer instead of a
        /// left-hand bias.</para></summary>
        public bool TryResolveHand(out OVRInput.Controller hand, out bool rightHand)
        {
            hand = OVRInput.Controller.None;
            rightHand = false;

            float best = float.MaxValue;

            if (TryMeasure(OVRInput.Controller.LTouch, out float left) && left <= AcceptRadius)
            {
                hand = OVRInput.Controller.LTouch;
                best = left;
            }

            if (TryMeasure(OVRInput.Controller.RTouch, out float right) && right <= AcceptRadius && right < best)
            {
                hand = OVRInput.Controller.RTouch;
                rightHand = true;
            }

            return hand != OVRInput.Controller.None;
        }

        /// <summary>One frame of the socket: the sphere appears as an accepted controller approaches and
        /// gets slightly more solid once the anchor is INSIDE ("press").</summary>
        /// <param name="available">Is the item takeable right now (the caller's rule: already in a hand,
        /// held by someone else, not calibrated…). <c>false</c> hides the socket.</param>
        public void Tick(bool available)
        {
            if (!available || !TryMeasureNearest(out float distance))
            {
                Hide();
                return;
            }

            float radius = AcceptRadius;
            if (distance > Mathf.Max(HoverRadius, radius))
            {
                Hide();
                return;
            }

            if (!EnsureIndicator())
            {
                return;
            }

            _indicator.gameObject.SetActive(true);
            _indicator.SetPositionAndRotation(transform.position, transform.rotation);

            // ⚠️ A WORLD measurement: the prefab ships at 1 m diameter, so diameter = 2 × radius. The
            // parent scale is undone — a raw local scale would size the sphere by the prop's scale.
            float parentScale = Mathf.Max(1e-4f, transform.lossyScale.x);
            _indicator.localScale = Vector3.one * (2f * radius / parentScale);

            Color color = IndicatorColor;
            color.a = distance <= radius ? IndicatorReadyAlpha : IndicatorHoverAlpha;

            if (_indicatorLine != null)
            {
                _indicatorLine.startColor = color;
                _indicatorLine.endColor = color;
            }
            else if (_indicatorMaterial != null)
            {
                _indicatorMaterial.color = color;
            }
        }

        /// <summary>Hides the indicator. Safe before it was ever built.</summary>
        public void Hide()
        {
            if (_indicator != null && _indicator.gameObject.activeSelf)
            {
                _indicator.gameObject.SetActive(false);
            }
        }

        private bool TryMeasureNearest(out float distance)
        {
            bool hasLeft = TryMeasure(OVRInput.Controller.LTouch, out float left);
            bool hasRight = TryMeasure(OVRInput.Controller.RTouch, out float right);

            if (hasLeft && hasRight)
            {
                distance = Mathf.Min(left, right);
                return true;
            }

            distance = hasLeft ? left : right;
            return hasLeft || hasRight;
        }

        /// <summary>Instantiates the indicator once, under the socket. Physics is stripped: a leftover
        /// collider would catch the shot ray and the grab system, so the thing meant to help the player
        /// would ruin their aim.</summary>
        private bool EnsureIndicator()
        {
            if (_indicator != null)
            {
                return true;
            }

            if (socketIndicatorPrefab == null)
            {
                if (!_indicatorPrefabWarned)
                {
                    _indicatorPrefabWarned = true;
                    Debug.LogWarning($"[GripSocket] '{name}' üzerinde gösterge prefabı boş — soket " +
                                     "küresi çizilmeyecek (eşyayı almak yine çalışır).", this);
                }

                return false;
            }

            GameObject instance = Instantiate(socketIndicatorPrefab, transform);
            instance.name = "[GripSocketIndicator]";

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }

            Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Destroy(bodies[i]);
            }

            _indicator = instance.transform;
            _indicatorLine = instance.GetComponentInChildren<LineRenderer>(true);

            if (_indicatorLine == null)
            {
                // Taken ONCE: .material allocates a new instance per call.
                var renderer = instance.GetComponentInChildren<Renderer>(true);
                if (renderer != null)
                {
                    Material material = renderer.material;
                    if (material != null &&
                        (material.HasProperty("_BaseColor") || material.HasProperty("_Color")))
                    {
                        _indicatorMaterial = material;
                    }
                }
            }

            return true;
        }

#if UNITY_EDITOR
        /// <summary>Editor gizmo: the acceptance volume at its real size, so the socket can be placed
        /// without entering play mode. Drawn only when SELECTED — a scene full of props would otherwise
        /// be a wall of spheres.</summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IndicatorColor;
            Gizmos.DrawWireSphere(transform.position, AcceptRadius);
        }
#endif
    }
}
