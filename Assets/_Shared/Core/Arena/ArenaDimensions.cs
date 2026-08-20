using System;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// The 2D plan of a venue (business): the floor boundary (an ordered corner ring) + the columns
    /// inside it. <b>It is the ONLY source of truth for the arena measurements</b> — it lives as a
    /// hand-editable JSON file; the venue's physical measurements are taken with a tape measure and
    /// entered directly here (no need to open Unity). The measurements are not written to a second
    /// place (component field, prefab, scene).
    /// <para>
    /// <b>The file is PER VENUE</b> (<c>Venues/&lt;Venue&gt;/Data/&lt;Venue&gt;_dimensions.json</c>):
    /// since the same physical area is always played in a venue, all of that venue's scenes (arenas
    /// + lobby) point at the SAME file in their <see cref="ArenaBoundary.dimensionsJson"/> field.
    /// Making a copy per scene inevitably produces two measurements that drift apart.
    /// </para>
    /// <para>
    /// <b>It is read at runtime:</b> <see cref="ArenaBoundary"/> resolves the <c>TextAsset</c>
    /// assigned to it with <see cref="Parse"/>. That is why the JSON file must be referenced from a
    /// scene (a referenced TextAsset goes into the build); a JSON that just sits under
    /// <c>Assets/</c> with nobody referencing it does NOT enter the build.
    /// </para>
    /// <para>
    /// ⚠️ <b>Coordinate system:</b> all points are in meters and lie in the LOCAL XZ plane of the
    /// transform carrying <see cref="ArenaBoundary"/> (the <c>y</c> field in the JSON = world Z). If
    /// you take the measurements from a corner, that corner becomes (0,0); the plan's zero does NOT
    /// have to be the arena origin — the zero of network coordinates is the world origin
    /// (<see cref="ArenaSpace"/>) and the boundary's measurements are defined in this local plane
    /// independently of it.
    /// </para>
    /// <para>
    /// ⚠️ Rings are assumed to be <b>closed</b>: the last corner connects to the first one
    /// automatically, do not repeat the same point at the end. Winding order (clockwise or not)
    /// does not matter.
    /// </para>
    /// <para>
    /// ⚠️ <b>The floor is a SINGLE ring and is not assembled from pieces.</b> Nothing extra is
    /// needed for concavity: an L shape, a trapezoid, an indented wall are all a single ordered
    /// corner ring. The same holds for columns — rationale in the <see cref="Polygon2D"/> docs.
    /// </para>
    /// <para>
    /// ⚠️ <b>There is NO wall height field and it is not added back:</b> both wall generation and
    /// the boundary's semi-transparent wall indicator were removed, so it would be a number with no
    /// reader — an unread measurement goes stale. The arena's walls come from the environment art.
    /// </para>
    /// <para>
    /// <b>The calibration points live here too</b> (<see cref="calibration"/>): the position of the
    /// A/B tapes stuck to the floor is also a MEASUREMENT and is per venue — all scenes played in
    /// the same room use the same two physical marks. The <c>anchor_a</c>/<c>anchor_b</c> objects
    /// in the scene are positioned from here, so the measurement is not copied into the scene by
    /// hand.
    /// </para>
    /// <example>
    /// Example file: <c>Assets/Arenas/Venues/VortexAntep/Data/VortexAntep_dimensions.json</c>
    /// <code>
    /// {
    ///   "name": "VortexAntep",
    ///   "plane": [ { "x": 0, "y": 0 }, { "x": 8.32, "y": 0 }, { "x": 8.32, "y": 13.23 } ],
    ///   "columns": [
    ///     { "name": "Kolon_Orta", "height": 0,
    ///       "points": [ { "x": 3.27, "y": 7.19 }, { "x": 3.94, "y": 7.19 },
    ///                   { "x": 3.94, "y": 7.57 }, { "x": 3.27, "y": 7.57 } ] }
    ///   ],
    ///   "calibration": { "a": { "x": 3.17, "y": 1.82 }, "b": { "x": 3.17, "y": 7.19 } },
    ///   "defaultColumnHeight": 3.0
    /// }
    /// </code>
    /// </example>
    /// </summary>
    [Serializable]
    public class ArenaDimensions
    {
        /// <summary>Minimum number of corners required for a valid polygon.</summary>
        public const int MinOutlinePoints = Polygon2D.MinPoints;

        /// <summary>
        /// A column inside the arena — as its own ordered corner ring. It is drawn as a prism and
        /// <b>always</b> enters the boundary computation as an obstacle: a column is the building's
        /// load-bearing structure, the player bumps into it. There is NO "let this column not
        /// block" switch and none is added.
        /// <para>
        /// ⚠️ <b>The reason it is a wrapper object is technical:</b> <see cref="JsonUtility"/> does
        /// not serialize nested arrays (<c>Vector2[][]</c>). In return <see cref="name"/> and
        /// <see cref="height"/> come for free — if they were kept in parallel arrays it would be a
        /// structure whose indices are kept aligned by hand and can silently drift.
        /// </para>
        /// <para>
        /// ⚠️ The center + size + rotation (<c>center</c>/<c>size</c>/<c>yaw</c>) representation was
        /// deliberately removed: real columns are not always axis-aligned rectangles, and a skewed
        /// pillar could only be represented approximately in that form.
        /// </para>
        /// </summary>
        [Serializable]
        public struct Column
        {
            /// <summary>Name of the generated object. When empty, <c>Kolon_&lt;index&gt;</c> is used.</summary>
            public string name;

            /// <summary>Height (meters). When left at 0, <see cref="defaultColumnHeight"/> is used.</summary>
            public float height;

            /// <summary>
            /// Ordered corners of the footprint (meters, arena-local XZ — <c>y</c> = world Z). It is
            /// assumed to be closed.
            /// </summary>
            public Vector2[] points;
        }

        /// <summary>
        /// The venue's two calibration points (the A and B tapes stuck to the floor), in meters and
        /// in plan space. The alignment order is <b>always A → B</b>: the yaw comes out of that
        /// direction, so swapping them flips the arena by 180°.
        /// <para>
        /// ⚠️ <b>The reason it is a separate object is technical:</b> <see cref="JsonUtility"/>
        /// matches field names one-to-one and a name like <c>anchor_a</c> cannot be written as a C#
        /// field name. Wrapping it as an object keeps
        /// <c>"calibration": { "a": …, "b": … }</c> readable in the file.
        /// </para>
        /// </summary>
        [Serializable]
        public struct CalibrationMarks
        {
            /// <summary>The first captured point (<c>anchor_a</c> in the scene).</summary>
            public Vector2 a;

            /// <summary>The second captured point (<c>anchor_b</c> in the scene).</summary>
            public Vector2 b;
        }

        /// <summary>
        /// The shortest distance the two points must have between them (meters). A pair closer than
        /// this does not define a direction: since the yaw error grows inversely with distance, a
        /// 20 cm span would turn a few millimeters of measurement error into meters at the other
        /// end of the arena.
        /// </summary>
        public const float MinCalibrationSpan = 0.5f;

        /// <summary>Informational name (labels the generated geometry and the error messages).</summary>
        public string name = string.Empty;

        /// <summary>
        /// Ordered corners of the floor (meters, arena-local XZ). Assumed to be closed — do not
        /// repeat the first point at the end.
        /// </summary>
        public Vector2[] plane = Array.Empty<Vector2>();

        /// <summary>Columns/obstacles inside the arena. May be left empty.</summary>
        public Column[] columns = Array.Empty<Column>();

        /// <summary>
        /// Position of the A/B floor-tape marks (meters, plan space). If not written, both points
        /// stay at (0,0) and <see cref="HasCalibration"/> returns false — in that case the
        /// calibrator does not touch the scene's anchors and logs a warning.
        /// </summary>
        public CalibrationMarks calibration;

        /// <summary>Height used when a column's <c>height</c> field is left at 0 (meters).</summary>
        public float defaultColumnHeight = 3f;

        /// <summary>
        /// Height of the admin top-down camera above the floor (meters). 0 or negative = the
        /// camera's own default is used.
        /// <para>
        /// ⚠️ Since the camera is <b>orthographic</b>, this value does not change the FRAMING (the
        /// framing comes from the bounding box of the floor ring); its only effect is at which
        /// height the camera sits, i.e. whether it stays ABOVE the roof and tall objects. If the
        /// default is not enough in a venue with a high ceiling, this is raised.
        /// </para>
        /// </summary>
        public float topViewHeight;

        /// <summary>Is this a usable plan (at least a triangle).</summary>
        public bool IsValid => Polygon2D.IsValid(plane);

        /// <summary>
        /// Are the calibration points usable: have they been written and are they at least
        /// <see cref="MinCalibrationSpan"/> meters apart.
        /// <para>
        /// ⚠️ This is <b>NOT part of plan validity</b> (<see cref="IsValid"/>): a file without
        /// points is enough to run the boundary, only the calibration anchors do not place
        /// themselves. The line that separates a measurement from no measurement is the floor ring.
        /// </para>
        /// </summary>
        public bool HasCalibration =>
            (calibration.b - calibration.a).sqrMagnitude >= MinCalibrationSpan * MinCalibrationSpan;

        /// <summary>A column's effective height (the default when its own value is 0).</summary>
        public float HeightOf(in Column column)
        {
            return column.height > 0f ? column.height : defaultColumnHeight;
        }

        /// <summary>
        /// The XZ bounding box of the floor ring (arena-local). <see cref="ArenaBoundary"/> derives
        /// the boundary extents and the admin top-down framing from it. Returns a zero box when the
        /// plan is invalid.
        /// </summary>
        public Rect LocalBounds()
        {
            return Polygon2D.Bounds(plane);
        }

        // -------------------------------------------------------------- parsing

        /// <summary>
        /// Converts JSON text into a plan. <b>It does NOT THROW</b> — instead of blowing up on scene
        /// load because of a corrupt file it returns <c>null</c> and writes the reason into
        /// <paramref name="error"/> (the caller logs the error loudly).
        /// <para>
        /// ⚠️ <see cref="JsonUtility.FromJsonOverwrite"/> is used so that fields NOT written in the
        /// JSON keep the defaults from this class (<c>defaultColumnHeight</c> 3). With
        /// <c>FromJson</c> a missing field would silently become 0 — meaning columns without height,
        /// i.e. columns that are never drawn.
        /// </para>
        /// </summary>
        /// <param name="json">File contents.</param>
        /// <param name="error">The reason on failure; <c>null</c> on success.</param>
        /// <returns>A valid plan or <c>null</c>.</returns>
        public static ArenaDimensions Parse(string json, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Boyut dosyası boş.";
                return null;
            }

            var dimensions = new ArenaDimensions();
            try
            {
                JsonUtility.FromJsonOverwrite(json, dimensions);
            }
            catch (Exception exception)
            {
                error = "Boyut dosyası ayrıştırılamadı: " + exception.Message;
                return null;
            }

            if (!dimensions.IsValid)
            {
                int count = dimensions.plane?.Length ?? 0;
                error = $"Geçersiz plan: 'plane' en az {MinOutlinePoints} köşe içermeli (bulunan: {count}).";
                return null;
            }

            dimensions.columns ??= Array.Empty<Column>();

            // A column without points produces no geometry and cannot enter the boundary test:
            // instead of carrying it along silently it is filtered out here so consumers can trust
            // that every element is usable.
            int usable = 0;
            for (int i = 0; i < dimensions.columns.Length; i++)
            {
                if (Polygon2D.IsValid(dimensions.columns[i].points))
                {
                    dimensions.columns[usable++] = dimensions.columns[i];
                }
            }

            if (usable != dimensions.columns.Length)
            {
                Array.Resize(ref dimensions.columns, usable);
            }

            return dimensions;
        }

        /// <summary>
        /// Reads a plan from a <c>TextAsset</c> (see <see cref="Parse"/>). Returns <c>null</c>
        /// silently when the asset is null — "no plan given" is not an error.
        /// </summary>
        public static ArenaDimensions FromTextAsset(TextAsset asset, out string error)
        {
            error = null;
            return asset == null ? null : Parse(asset.text, out error);
        }

        /// <summary>Converts the plan into JSON text (used by editor tools when writing the file).</summary>
        public string ToJson(bool pretty = true)
        {
            return JsonUtility.ToJson(this, pretty);
        }
    }
}
