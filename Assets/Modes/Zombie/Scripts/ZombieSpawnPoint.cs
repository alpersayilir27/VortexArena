using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Editor marker for a <c>zombie_spawn</c> object: draws the gate the zombies come through so
    /// it can be placed and aimed in the scene view.</summary>
    /// <remarks>NO runtime logic — the spawn point's whole job on the wire is its <c>netId</c>
    /// (§10.5): the server picks one, the brain resolves the transform. The network side is
    /// <c>NetIdentity</c> + <c>NetObject</c> with the <c>zombie_spawn</c> kind; this component only makes
    /// it visible to the person placing it.
    /// <para>⚠️ Zombies arrive from OUTSIDE the play area, so the point stands outside the boundary and
    /// the NavMesh must reach from it to the gate — a point with no NavMesh under it produces zombies
    /// that stand still at the fence.</para></remarks>
    [DisallowMultipleComponent]
    public class ZombieSpawnPoint : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.45f, 0.85f, 0.35f, 0.9f);

        [Tooltip("Gizmo yarıçapı (m) — yalnız editörde görünür, oyunu etkilemez.")]
        [SerializeField] private float gizmoRadius = 0.4f;

        private void OnDrawGizmos()
        {
            Gizmos.color = Ink;
            Gizmos.DrawWireSphere(transform.position, gizmoRadius);

            // Forward arrow: zombies walk out this way, so the point's rotation is not decoration.
            Vector3 tip = transform.position + transform.forward * (gizmoRadius * 3f);
            Gizmos.DrawLine(transform.position, tip);
            Gizmos.DrawLine(tip, tip - transform.forward * gizmoRadius + transform.right * gizmoRadius * 0.5f);
            Gizmos.DrawLine(tip, tip - transform.forward * gizmoRadius - transform.right * gizmoRadius * 0.5f);
        }
    }
}
