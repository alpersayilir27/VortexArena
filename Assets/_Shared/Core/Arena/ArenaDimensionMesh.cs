using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// The marker at the root of a venue's <b>dimension mesh</b>: the <c>Plane</c> and
    /// <c>Columns</c> geometry under this branch was generated from the dimensions file in
    /// <see cref="SourceJson"/>.
    /// <para>
    /// <b>What it is for:</b> the dimension mesh makes the venue's physical area visible in the
    /// scene — the arena art is built on top of it. If the measurement was taken wrong, the corners
    /// are fixed in place with ProBuilder and
    /// <c>Tools &gt; VortexArena &gt; Arena &gt; DimensionMesh'i JSON'a Çevir</c> reads the mesh back
    /// and OVERWRITES <see cref="SourceJson"/>. This component is the answer to that tool's
    /// question "what am I supposed to convert".
    /// </para>
    /// <para>
    /// ⚠️ <b>The dimension mesh is NOT played geometry but it DOES go into the build:</b> the
    /// calibration anchors (<c>anchor_a</c> / <c>anchor_b</c>) sit under the mesh and
    /// <see cref="ArenaCalibrator"/> looks for them at runtime — a mesh dropped from the build
    /// means an arena that cannot be aligned. What keeps it invisible is behaviour, not a tag:
    /// <see cref="Awake"/> only disables the measurement visual (<see cref="PlaneName"/> +
    /// <see cref="ColumnsGroupName"/>). The floor/walls the player sees come from the environment
    /// art.
    /// </para>
    /// <para>
    /// ⚠️ In a real build that visual branch <b>never enters at all</b>:
    /// <c>DimensionMeshBuildStripper</c> deletes it from the temporary scene copy that goes into
    /// the build, because the <c>Plane</c>/columns carry a <c>ProBuilderMesh</c> and that would
    /// pull <c>Unity.ProBuilder</c> into the runtime. So the hiding in <see cref="Awake"/> is for
    /// <b>editor Play mode</b> — the two mechanisms give the same result in two different
    /// contexts, neither is a fallback for the other.
    /// </para>
    /// <para>
    /// ⚠️ <b>The dimension mesh is INDEPENDENT of the scene</b> — the generation tool creates it at
    /// the scene root, at the world origin and without rotation, so the measurement in the file can
    /// be read one-to-one in the scene. It may be moved and rotated by hand if desired: since the
    /// measurement extraction is done relative to the local space of this <b>root</b>, a moved mesh
    /// is still converted correctly. Just <b>do not change its scale</b> — the plan is in meters.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaDimensionMesh : MonoBehaviour
    {
        /// <summary>Suffix of the root object name: <c>&lt;Venue&gt;_DimensionMesh</c>.</summary>
        public const string RootSuffix = "_DimensionMesh";

        /// <summary>Name of the floor polygon object.</summary>
        public const string PlaneName = "Plane";

        /// <summary>Name of the group object the columns are collected under.</summary>
        public const string ColumnsGroupName = "Columns";

        [Tooltip("Mekan (işletme) klasör adı — kök obje adı ve raporlar bunu kullanır.")]
        [SerializeField] private string venueName = string.Empty;

        [Tooltip("Maketin üretildiği ve geri yazılacağı boyut dosyası (ArenaDimensions JSON'u).")]
        [SerializeField] private TextAsset sourceJson;

        [Tooltip("Kolonun kendi 'height' değeri 0 ise kullanılan yükseklik (metre).")]
        [SerializeField] private float defaultColumnHeight = 3f;

        /// <summary>Venue (business) folder name.</summary>
        public string VenueName => venueName;

        /// <summary>The dimensions file that is the mesh's source and write-back target.</summary>
        public TextAsset SourceJson => sourceJson;

        /// <summary>
        /// Carrier field preserved during write-back (see <see cref="Configure"/>).
        /// </summary>
        public float DefaultColumnHeight => defaultColumnHeight;

        /// <summary>
        /// Hides the mesh's MEASUREMENT VISUAL in game: the floor and column prisms are not
        /// geometry the player is meant to see, they are an editor reference.
        /// <para>
        /// ⚠️ The calibration anchors (<c>anchor_a</c> / <c>anchor_b</c>) are NOT TOUCHED:
        /// <see cref="ArenaCalibrator"/> manages their visibility (lights them during capture,
        /// hides them once the alignment is confirmed). Disabling them here would silently kill
        /// that feedback.
        /// </para>
        /// <para>
        /// ⚠️ What gets disabled is <see cref="Renderer.enabled"/>, NOT the object: if the mesh's
        /// branch is deactivated the calibrator cannot find the anchors and cannot place them on
        /// the points from the dimensions file.
        /// </para>
        /// <para>
        /// It must stay visible in the editor (the mesh is a setup tool), which is why there is NO
        /// <c>[ExecuteAlways]</c> — <c>Awake</c> only runs in Play/at runtime.
        /// </para>
        /// </summary>
        private void Awake()
        {
            HideMeasurementVisual(PlaneName);
            HideMeasurementVisual(ColumnsGroupName);
        }

        private void HideMeasurementVisual(string childName)
        {
            Transform child = transform.Find(childName);
            if (child == null)
                return;

            Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }
        }

        /// <summary>
        /// Filled in by the generation tool. <b>Only editor tools call it</b>, nobody writes at
        /// runtime.
        /// <para>
        /// ⚠️ <paramref name="defaultColumnHeight"/> is held here as a <b>round-trip carrier</b>,
        /// not as a second source of truth: since it does not become geometry it cannot be read
        /// back from the mesh, and if it were not stored it would be lost on every write-back and
        /// the value in the file would silently fall back to the default.
        /// </para>
        /// </summary>
        public void Configure(string venue, TextAsset json, float defaultColumnHeight)
        {
            venueName = venue;
            sourceJson = json;
            this.defaultColumnHeight = defaultColumnHeight;
        }

        /// <summary>The expected root object name for a given venue name.</summary>
        public static string RootNameFor(string venue)
        {
            return (string.IsNullOrWhiteSpace(venue) ? "Arena" : venue.Trim()) + RootSuffix;
        }
    }
}
