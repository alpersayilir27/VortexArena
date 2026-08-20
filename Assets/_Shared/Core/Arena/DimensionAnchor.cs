using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>Marks a calibration point on the dimension mesh: is this object the floor tape's
    /// <b>A</b> or <b>B</b>. <c>DimensionMesh'i JSON'a Çevir</c> reads this component to write the
    /// points back into the dimensions file's <c>calibration</c> field.</summary>
    /// <remarks>
    /// ⚠️ No coordinate is stored on the component — the point IS the transform. Storing both would
    /// create a second source that silently drifts from the dragged position (same rationale as
    /// <see cref="DimensionPolygon"/>).
    /// <para>
    /// ⚠️ This marker IS the runtime marker — there is no second marker family.
    /// <see cref="ArenaCalibrator"/> finds its target through this component and its
    /// <see cref="Kind"/>, with a name-based fallback for old scenes without a mesh
    /// (<see cref="ArenaCalibrator.AnchorAName"/> / <see cref="ArenaCalibrator.AnchorBName"/>).
    /// That is why the mesh enters the build and is not tagged <c>EditorOnly</c>; only the
    /// floor/column visual is hidden at runtime.
    /// </para>
    /// <para>
    /// ⚠️ Position contract: the transform IS THE FLOOR POINT (the cube's center sits on the point,
    /// half of it below the floor). Read-back takes the transform raw, so raising the marker to put
    /// its mesh base on the floor would split the written point from the visible one.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public class DimensionAnchor : MonoBehaviour
    {
        /// <summary>The point's place in the calibration order.
        /// <para>⚠️ Serialized enum: new values are appended at the END — Unity stores the numeric
        /// index, so inserting shifts the values in existing scenes.</para></summary>
        public enum AnchorKind
        {
            /// <summary>The first captured point.</summary>
            A,

            /// <summary>The second captured point; the A→B direction gives the arena's orientation.</summary>
            B
        }

        [Tooltip("Bu işaretçi kalibrasyonun A noktası mı yoksa B noktası mı.")]
        [SerializeField] private AnchorKind kind = AnchorKind.A;

        /// <summary>The point's place in the calibration order.</summary>
        public AnchorKind Kind => kind;

        /// <summary>Filled in by the generation tool — nobody writes at runtime.</summary>
        public void SetKind(AnchorKind value)
        {
            kind = value;
        }
    }
}
