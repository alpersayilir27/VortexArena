using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// An obstacle placed BY HAND in the scene (column, crate, post, pillar) — a rectangle marked
    /// so that it enters the boundary computation. Position and rotation come from the object's own
    /// transform; only its footprint size (<see cref="Size"/>) is written on the component.
    /// <para>
    /// <b>Why a separate component:</b> the columns in <see cref="ArenaDimensions"/> are part of the
    /// arena PLAN (the editor tool generates the geometry from them). For decor that is not in the
    /// plan and is scattered around the scene, the only source of truth is the object itself — this
    /// component introduces that object to <see cref="ArenaBoundary"/> as "do not walk here".
    /// </para>
    /// <para>
    /// ⚠️ <b>It ADDS NO collider and does NO physics.</b> Since the player physically walks in
    /// free-roam, what stops them is the real-world object, not a collision; the only job of this
    /// component is to make the boundary (wall alpha + fade + warning) warn the player as they
    /// approach the obstacle. The visual/physical body are separate objects and this component does
    /// not touch them.
    /// </para>
    /// <para>
    /// Registration happens in <see cref="OnEnable"/>/<see cref="OnDisable"/>; the static list
    /// empties itself on scene change (no leak).
    /// </para>
    /// </summary>
    public class ArenaObstacle : MonoBehaviour
    {
        private static readonly List<ArenaObstacle> Registry = new List<ArenaObstacle>();

        [Tooltip("Zemindeki ölçü: X = genişlik, Y = derinlik (metre). Yükseklik önemsizdir — " +
                 "muhafaza 2B çalışır.")]
        [SerializeField] private Vector2 size = new Vector2(1f, 1f);

        /// <summary>All obstacles active in Play mode (read only).
        /// <para>⚠️ The registry fills in <c>OnEnable</c>, so it is only valid <b>in Play mode</b> —
        /// when writing an editor tool, scan the scene with <c>FindObjectsByType</c>.</para></summary>
        public static IReadOnlyList<ArenaObstacle> All => Registry;

        /// <summary>Footprint size (X = width, Z = depth; meters).</summary>
        public Vector2 Size => size;

        private void OnEnable()
        {
            if (!Registry.Contains(this))
            {
                Registry.Add(this);
            }
        }

        private void OnDisable()
        {
            Registry.Remove(this);
        }

        /// <summary>
        /// Returns the obstacle's rectangle in the LOCAL XZ space of the
        /// <paramref name="arenaLocal"/> transform (that is where the
        /// <see cref="ArenaBoundary"/> computation happens).
        /// <para>
        /// The conversion is gathered here because it needs to know the relation between the two
        /// transforms: the center comes from a point transform and <paramref name="yaw"/> from the
        /// difference of the two objects' Y rotations. The caller only consumes the result.
        /// </para>
        /// ⚠️ Scale is ignored: the obstacle size comes from <see cref="Size"/>, not from the
        /// transform scale — the field is the single source of truth so that differently scaled
        /// copies of the same prefab do not silently produce different boundaries.
        /// </summary>
        /// <param name="arenaLocal">Transform of the space the computation runs in (the boundary object).</param>
        /// <param name="center">Obstacle center (local XZ, meters).</param>
        /// <param name="size">Obstacle size (X = width, Y = depth; meters).</param>
        /// <param name="yaw">The obstacle's Y rotation in local space (degrees).</param>
        public void GetLocalRect(Transform arenaLocal, out Vector2 center, out Vector2 size, out float yaw)
        {
            size = this.size;

            if (arenaLocal == null)
            {
                center = new Vector2(transform.position.x, transform.position.z);
                yaw = transform.eulerAngles.y;
                return;
            }

            Vector3 local = arenaLocal.InverseTransformPoint(transform.position);
            center = new Vector2(local.x, local.z);

            // Direction difference: move the obstacle's forward vector into local space and take its
            // angle in the XZ plane. Taking an Euler difference gets stuck on gimbal (the obstacle
            // may be tilted), a direction vector does not.
            Vector3 forward = arenaLocal.InverseTransformDirection(transform.forward);
            yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------------ gizmo

        /// <summary>Makes placement easier in the editor: the footprint rectangle + a light volume.</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.95f, 0.55f, 0.15f, 0.9f);

            // The scale is deliberately 1: the box represents Size, not the transform scale
            // (GetLocalRect ignores scale too — the gizmo and the computation must show the same
            // thing).
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0.02f, size.y));
            Gizmos.DrawWireCube(new Vector3(0f, 1f, 0f), new Vector3(size.x, 2f, size.y));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
