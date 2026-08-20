using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Player
{
    /// <summary>A hittable part of a remote player avatar (head / body / stomach / leg collider).</summary>
    /// <remarks>
    /// If a Weapon raycast hits this component the target is a NETWORK PLAYER: damage is not applied
    /// locally, a <c>hit_report</c> is sent to the server and a <c>health_update</c> is awaited
    /// (Docs/ArenaNet-Protokol.md §10.3).
    /// <para>The zone multiplier is applied client-side by <see cref="VortexArena.Core.Combat.Weapon"/>
    /// (damage is client-authoritative, <c>hit_report.damage</c>; §10.3) — <see cref="Zone"/> is that
    /// multiplier's source and its number comes from <c>WeaponDefinition.GetZoneMultiplier</c>.</para>
    /// </remarks>
    public class RemoteHitBox : MonoBehaviour
    {
        /// <summary>Head — the most striking colour, being the highest-multiplier zone.</summary>
        private static readonly Color HeadColor = new Color(1f, 0.25f, 0.2f, 0.9f);

        /// <summary>Stomach/pelvis.</summary>
        private static readonly Color StomachColor = new Color(1f, 0.6f, 0.1f, 0.9f);

        /// <summary>Legs.</summary>
        private static readonly Color LegColor = new Color(1f, 0.95f, 0.25f, 0.9f);

        /// <summary>Chest and arms (reference damage).</summary>
        private static readonly Color BodyColor = new Color(0.35f, 1f, 0.45f, 0.9f);

        [Tooltip("Boş bırakılırsa üst hiyerarşiden otomatik bulunur.")]
        [SerializeField] private RemoteAvatar avatar;

        // ⚠️ Boxes are maintained BY HAND (no generator tool): whoever hangs a new box on a bone must add
        // this component and PICK its zone. The default is Body, so a forgotten head box silently deals
        // 1× instead of 4× — in the field that reads as "I hit the head but they did not die" and is
        // expensive to diagnose.
        [Tooltip("Vuruş bölgesi — hasar çarpanının kaynağı (kafa 4×, karın 1.25×, bacak 0.75×).")]
        [SerializeField] private HitZone zone = HitZone.Body;

        /// <summary>The avatar this hitbox belongs to.</summary>
        public RemoteAvatar Avatar => avatar;

        /// <summary>The avatar's player id; 0 if there is no avatar (invalid target).</summary>
        public int PlayerId => avatar != null ? avatar.PlayerId : 0;

        /// <summary>Hit zone; APPLYING the multiplier is the caller's job (damage is client-authoritative).</summary>
        public HitZone Zone => zone;

        /// <summary>Is this the head zone — derived from <see cref="Zone"/>.</summary>
        public bool IsHead => zone == HitZone.Head;

        private void Reset()
        {
            if (avatar == null)
            {
                avatar = GetComponentInParent<RemoteAvatar>(true);
            }
        }

        private void Awake()
        {
            if (avatar == null)
            {
                avatar = GetComponentInParent<RemoteAvatar>(true);
            }

            if (avatar == null)
            {
                Debug.LogWarning($"[RemoteHitBox] '{name}' bir RemoteAvatar altında değil; vuruşlar raporlanamaz.", this);
            }
        }

        // ------------------------------------------------------------------ gizmo

        /// <summary>Real wireframe of the box (read from the collider). ⚠️ NOT
        /// <c>OnDrawGizmosSelected</c>: while adjusting a box the selection is usually something else
        /// (a bone, the character root) and the box's location must be visible then too.</summary>
        private void OnDrawGizmos()
        {
            var collider = GetComponent<Collider>();
            if (collider == null)
            {
                // The box may not be set up yet; stay silent — a gizmo is not a warning channel.
                return;
            }

            // ⚠️ Gizmos.matrix IS used here, the OPPOSITE of the "do not use the matrix" rule in the grip
            // gizmos — both are right in their own place: grip offsets are never scaled (they must read in
            // metres), whereas collider dimensions REALLY do scale with the transform (the bone root
            // scales with the player's height). A wireframe drawn without the matrix would misrepresent
            // the real collider and send manual tuning to the wrong place.
            Matrix4x4 previous = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = ZoneColor(zone);

            if (collider is SphereCollider sphere)
            {
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else if (collider is CapsuleCollider capsule)
            {
                DrawWireCapsule(capsule);
            }

            Gizmos.matrix = previous;
            Gizmos.color = previousColor;
        }

        private static Color ZoneColor(HitZone value)
        {
            switch (value)
            {
                case HitZone.Head: return HeadColor;
                case HitZone.Stomach: return StomachColor;
                case HitZone.Leg: return LegColor;
                default: return BodyColor;
            }
        }

        /// <summary>Unity has NO built-in wire capsule gizmo: built from two end spheres + four connecting
        /// lines. The ends sit <c>height/2 - radius</c> from the centre (capsule height includes the end
        /// spheres); the axis comes from the collider's <c>direction</c> field.</summary>
        private static void DrawWireCapsule(CapsuleCollider capsule)
        {
            float radius = capsule.radius;
            Vector3 axis = AxisVector(capsule.direction);
            Vector3 perpA = AxisVector((capsule.direction + 1) % 3);
            Vector3 perpB = AxisVector((capsule.direction + 2) % 3);

            // If the height is below twice the radius the capsule is already a sphere — clamped so the
            // distance cannot go negative, which would put the end spheres on the wrong sides.
            float half = Mathf.Max(0f, capsule.height * 0.5f - radius);
            Vector3 top = capsule.center + axis * half;
            Vector3 bottom = capsule.center - axis * half;

            Gizmos.DrawWireSphere(top, radius);
            Gizmos.DrawWireSphere(bottom, radius);

            Gizmos.DrawLine(top + perpA * radius, bottom + perpA * radius);
            Gizmos.DrawLine(top - perpA * radius, bottom - perpA * radius);
            Gizmos.DrawLine(top + perpB * radius, bottom + perpB * radius);
            Gizmos.DrawLine(top - perpB * radius, bottom - perpB * radius);
        }

        /// <summary>The CapsuleCollider.direction contract: 0=X, 1=Y, 2=Z.</summary>
        private static Vector3 AxisVector(int direction)
        {
            switch (direction)
            {
                case 0: return Vector3.right;
                case 2: return Vector3.forward;
                default: return Vector3.up;
            }
        }
    }
}
