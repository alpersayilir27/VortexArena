using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>Marks what a polygon on the dimension mesh is: the floor itself or a column.
    /// <c>DimensionMesh'i JSON'a Çevir</c> walks the mesh by this component.</summary>
    /// <remarks>
    /// ⚠️ No points, name or height are stored on the component. The single source for the points is
    /// the mesh itself (otherwise ProBuilder edits would be silently ignored), the name is the
    /// <c>GameObject</c>'s name and the height is the mesh's Y range — copying any of them here
    /// would create a second source that drifts from what is edited in the scene.
    /// <para>
    /// ⚠️ A component is used instead of checking the parent name (<c>Columns</c>) because the
    /// hierarchy can be rearranged by hand while the component travels with the object.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public class DimensionPolygon : MonoBehaviour
    {
        /// <summary>The polygon's role on the dimension mesh.
        /// <para>⚠️ Serialized enum: new values are appended at the END — Unity stores the numeric
        /// index, so inserting shifts the values in existing scenes.</para></summary>
        public enum PolygonKind
        {
            /// <summary>The floor itself (one per mesh).</summary>
            Plane,

            /// <summary>A column/obstacle inside the area (a prism).</summary>
            Column
        }

        [Tooltip("Bu çokgen tabanı mı yoksa bir kolonu mu temsil ediyor.")]
        [SerializeField] private PolygonKind kind = PolygonKind.Plane;

        /// <summary>The polygon's role on the dimension mesh.</summary>
        public PolygonKind Kind => kind;

        /// <summary>Filled in by the generation tool — nobody writes at runtime.</summary>
        public void SetKind(PolygonKind value)
        {
            kind = value;
        }
    }
}
