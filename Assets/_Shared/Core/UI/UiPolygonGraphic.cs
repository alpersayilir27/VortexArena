using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Base for the "Girdap" theme's convex polygon graphics: chamfered corners + slanted sides.
    /// <para>
    /// <b>Why a mesh instead of a sprite:</b> the theme's boxes are CSS <c>clip-path: polygon(...)</c>
    /// — the cut is a fixed px size at any box size, which 9-slice cannot express (a sliced corner
    /// stretches) and a per-size sprite would mean one texture per box.
    /// </para>
    /// <para>
    /// ⚠️ Vertex colors carry every gradient/glow here; <see cref="Graphic.color"/> only multiplies
    /// on top (so a CanvasGroup/tween still works). UV is 0 and the texture is white.
    /// </para>
    /// </summary>
    public abstract class UiPolygonGraphic : MaskableGraphic
    {
        // 45° corner cut length in px (0 = square corner).
        [SerializeField] private float chamferTopLeft;
        [SerializeField] private float chamferTopRight;
        [SerializeField] private float chamferBottomRight;
        [SerializeField] private float chamferBottomLeft;

        // Corner ROUNDING radius in px (0 = no arc). ⚠️ Ignored when the same corner has a chamfer:
        // a 45° cut and an arc on one corner cannot both be the outline there.
        [SerializeField] private float radiusTopLeft;
        [SerializeField] private float radiusTopRight;
        [SerializeField] private float radiusBottomRight;
        [SerializeField] private float radiusBottomLeft;

        /// <summary>Side slant in px. ⚠️ Sign carries the direction: positive pulls the BOTTOM corner
        /// inward, negative pulls the TOP corner inward.</summary>
        [SerializeField] private float slantLeft;

        [SerializeField] private float slantRight;

        private const float Eps = 1e-5f;

        public float ChamferTopLeft
        {
            get => chamferTopLeft;
            set { chamferTopLeft = value; SetVerticesDirty(); }
        }

        public float ChamferTopRight
        {
            get => chamferTopRight;
            set { chamferTopRight = value; SetVerticesDirty(); }
        }

        public float ChamferBottomRight
        {
            get => chamferBottomRight;
            set { chamferBottomRight = value; SetVerticesDirty(); }
        }

        public float ChamferBottomLeft
        {
            get => chamferBottomLeft;
            set { chamferBottomLeft = value; SetVerticesDirty(); }
        }

        public float RadiusTopLeft
        {
            get => radiusTopLeft;
            set { radiusTopLeft = value; SetVerticesDirty(); }
        }

        public float RadiusTopRight
        {
            get => radiusTopRight;
            set { radiusTopRight = value; SetVerticesDirty(); }
        }

        public float RadiusBottomRight
        {
            get => radiusBottomRight;
            set { radiusBottomRight = value; SetVerticesDirty(); }
        }

        public float RadiusBottomLeft
        {
            get => radiusBottomLeft;
            set { radiusBottomLeft = value; SetVerticesDirty(); }
        }

        public float SlantLeft
        {
            get => slantLeft;
            set { slantLeft = value; SetVerticesDirty(); }
        }

        public float SlantRight
        {
            get => slantRight;
            set { slantRight = value; SetVerticesDirty(); }
        }

        /// <summary>Multiplies a vertex color by <see cref="Graphic.color"/>.</summary>
        protected Color32 Tint(Color c)
        {
            Color tint = color;
            return new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a * tint.a);
        }

        /// <summary>
        /// Builds the outline in local space: slant first, then corner chamfers/arcs.
        /// Order is TL → TR → BR → BL (clockwise on screen).
        /// </summary>
        protected void BuildPolygon(Rect r, List<Vector2> result)
        {
            result.Clear();

            var tl = new Vector2(r.xMin, r.yMax);
            var tr = new Vector2(r.xMax, r.yMax);
            var br = new Vector2(r.xMax, r.yMin);
            var bl = new Vector2(r.xMin, r.yMin);

            if (slantLeft > 0f)
            {
                bl.x += slantLeft;
            }
            else if (slantLeft < 0f)
            {
                tl.x -= slantLeft;
            }

            if (slantRight > 0f)
            {
                br.x -= slantRight;
            }
            else if (slantRight < 0f)
            {
                tr.x += slantRight;
            }

            // Two radii on one edge may not add up past the edge: scale the pair down instead of
            // letting the arcs cross (a crossed outline inverts the fill's winding).
            float rTl = Mathf.Max(radiusTopLeft, 0f);
            float rTr = Mathf.Max(radiusTopRight, 0f);
            float rBr = Mathf.Max(radiusBottomRight, 0f);
            float rBl = Mathf.Max(radiusBottomLeft, 0f);
            ClampPair(ref rTl, ref rTr, r.width);
            ClampPair(ref rBl, ref rBr, r.width);
            ClampPair(ref rTl, ref rBl, r.height);
            ClampPair(ref rTr, ref rBr, r.height);

            AddCorner(result, tl, bl, tr, chamferTopLeft, rTl);
            AddCorner(result, tr, tl, br, chamferTopRight, rTr);
            AddCorner(result, br, tr, bl, chamferBottomRight, rBr);
            AddCorner(result, bl, br, tl, chamferBottomLeft, rBl);
            RemoveDuplicateVertices(result);
        }

        // A pill (radius = half the short edge) lands both arcs on the same side midpoint; a
        // zero-length edge has no normal, so Offset would leave a spike in the border there.
        private static void RemoveDuplicateVertices(List<Vector2> poly)
        {
            for (int i = poly.Count - 1; i >= 0 && poly.Count > 3; i--)
            {
                int prev = (i - 1 + poly.Count) % poly.Count;
                if ((poly[i] - poly[prev]).sqrMagnitude < 1e-4f) // < 0.01 px apart
                {
                    poly.RemoveAt(i);
                }
            }
        }

        private static void ClampPair(ref float a, ref float b, float length)
        {
            float sum = a + b;
            if (sum <= length || sum < Eps)
            {
                return;
            }

            float k = Mathf.Max(length, 0f) / sum;
            a *= k;
            b *= k;
        }

        /// <summary>Allocating overload — prefer the <see cref="List{T}"/> one on the render path.</summary>
        protected List<Vector2> BuildPolygon(Rect r)
        {
            var list = new List<Vector2>(8);
            BuildPolygon(r, list);
            return list;
        }

        // Replaces a corner with two points `cut` px along each adjacent edge (capped at half the
        // edge so two chamfers on one short edge cannot cross), or with an arc when rounded.
        private static void AddCorner(List<Vector2> result, Vector2 v, Vector2 prev, Vector2 next,
            float cut, float radius)
        {
            if (cut > 0f)
            {
                result.Add(v + Along(v, prev, cut));
                result.Add(v + Along(v, next, cut));
                return;
            }

            if (radius > Eps && AddArc(result, v, prev, next, radius))
            {
                return;
            }

            result.Add(v);
        }

        /// <summary>
        /// Quarter-ish arc inscribed in the corner, tangent to both edges. False (nothing emitted)
        /// for a degenerate corner so the caller can fall back to the sharp vertex.
        /// </summary>
        private static bool AddArc(List<Vector2> result, Vector2 v, Vector2 prev, Vector2 next,
            float radius)
        {
            Vector2 u = prev - v;
            Vector2 w = next - v;
            float lu = u.magnitude;
            float lw = w.magnitude;
            if (lu < Eps || lw < Eps)
            {
                return false;
            }

            u /= lu;
            w /= lw;

            Vector2 bisector = u + w;
            float lb = bisector.magnitude;
            if (lb < Eps)
            {
                return false; // 180°: no corner to round
            }

            bisector /= lb;

            float half = Mathf.Acos(Mathf.Clamp(Vector2.Dot(u, w), -1f, 1f)) * 0.5f;
            float sinHalf = Mathf.Sin(half);
            float tanHalf = Mathf.Tan(half);
            if (sinHalf < Eps || tanHalf < Eps)
            {
                return false;
            }

            // Tangent distance for radius, capped at half of the shorter edge (same guard as the
            // chamfer path), then the radius is recomputed from the capped distance.
            float tangent = Mathf.Min(radius / tanHalf, Mathf.Min(lu, lw) * 0.5f);
            float rr = tangent * tanHalf;
            if (rr < Eps)
            {
                return false;
            }

            Vector2 center = v + bisector * (rr / sinHalf);
            Vector2 a0 = v + u * tangent - center;
            Vector2 a1 = v + w * tangent - center;
            float start = Mathf.Atan2(a0.y, a0.x);
            float end = Mathf.Atan2(a1.y, a1.x);

            // Shortest sweep: a convex corner is always < 180°, so this picks the inside arc and
            // keeps the polygon's winding direction.
            float delta = Mathf.DeltaAngle(start * Mathf.Rad2Deg, end * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            int segments = Mathf.Clamp(Mathf.CeilToInt(radius / 3f), 4, 16);
            for (int i = 0; i <= segments; i++)
            {
                float angle = start + delta * i / segments;
                result.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * rr);
            }

            return true;
        }

        private static Vector2 Along(Vector2 from, Vector2 to, float cut)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < Eps)
            {
                return Vector2.zero;
            }

            return d * (Mathf.Min(cut, len * 0.5f) / len);
        }

        // ------------------------------------------------------------- geometry

        /// <summary>Signed area; positive = counter-clockwise.</summary>
        public static float SignedArea(List<Vector2> poly)
        {
            if (poly == null || poly.Count < 3)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0, n = poly.Count; i < n; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % n];
                sum += a.x * b.y - b.x * a.y;
            }

            return sum * 0.5f;
        }

        /// <summary>
        /// Shrinks a convex polygon INWARD by <paramref name="d"/> px (negative grows it): every edge
        /// slides along its inward normal and consecutive edge lines are re-intersected, so corner
        /// angles survive (a naive "scale toward centroid" would not).
        /// <para>⚠️ Collapses to a zero-area polygon when <paramref name="d"/> eats the shape — the
        /// caller gets degenerate triangles instead of NaN/inside-out geometry.</para>
        /// </summary>
        public static void Offset(List<Vector2> poly, float d, List<Vector2> result)
        {
            result.Clear();
            if (poly == null || poly.Count < 3)
            {
                return;
            }

            int n = poly.Count;
            if (Mathf.Abs(d) < Eps)
            {
                for (int i = 0; i < n; i++)
                {
                    result.Add(poly[i]);
                }

                return;
            }

            float area = SignedArea(poly);
            float inward = area >= 0f ? 1f : -1f; // CCW: inward normal is the left normal

            for (int i = 0; i < n; i++)
            {
                // Vertex i is shared by edge (i-1 -> i) and edge (i -> i+1).
                if (!EdgeLine(poly, (i - 1 + n) % n, d, inward, out Vector2 q0, out Vector2 e0) ||
                    !EdgeLine(poly, i, d, inward, out Vector2 q1, out Vector2 e1))
                {
                    result.Add(poly[i]);
                    continue;
                }

                float cross = e0.x * e1.y - e0.y * e1.x;
                if (Mathf.Abs(cross) < Eps)
                {
                    result.Add(q1); // collinear edges: no corner to rebuild
                    continue;
                }

                Vector2 w = q1 - q0;
                float t = (w.x * e1.y - w.y * e1.x) / cross;
                Vector2 p = q0 + e0 * t;
                result.Add(float.IsNaN(p.x) || float.IsNaN(p.y) ? q1 : p);
            }

            // Sign flip (or near-zero area) means the offset crossed the shape's medial axis.
            float newArea = SignedArea(result);
            if (d > 0f && (newArea * area <= 0f || Mathf.Abs(newArea) < Eps))
            {
                Vector2 c = Centroid(poly);
                for (int i = 0; i < result.Count; i++)
                {
                    result[i] = c;
                }
            }
        }

        /// <summary>Allocating overload of <see cref="Offset(List{Vector2},float,List{Vector2})"/>.</summary>
        public static List<Vector2> Offset(List<Vector2> poly, float d)
        {
            var list = new List<Vector2>(poly != null ? poly.Count : 4);
            Offset(poly, d, list);
            return list;
        }

        private static bool EdgeLine(List<Vector2> poly, int i, float d, float inward,
            out Vector2 point, out Vector2 dir)
        {
            int n = poly.Count;
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];
            Vector2 e = b - a;
            float len = e.magnitude;
            if (len < Eps)
            {
                point = a;
                dir = Vector2.zero;
                return false;
            }

            dir = e / len;
            Vector2 normal = new Vector2(-dir.y, dir.x) * inward;
            point = a + normal * d;
            return true;
        }

        private static Vector2 Centroid(List<Vector2> poly)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < poly.Count; i++)
            {
                sum += poly[i];
            }

            return poly.Count > 0 ? sum / poly.Count : Vector2.zero;
        }

        /// <summary>
        /// Sutherland–Hodgman clip; the <c>dot(p, n) &lt;= c</c> side survives.
        /// ⚠️ <paramref name="result"/> must not be <paramref name="poly"/>.
        /// </summary>
        public static void ClipHalfPlane(List<Vector2> poly, Vector2 n, float c, List<Vector2> result)
        {
            result.Clear();
            if (poly == null || poly.Count < 3)
            {
                return;
            }

            int count = poly.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % count];
                float da = Vector2.Dot(a, n) - c;
                float db = Vector2.Dot(b, n) - c;

                if (da <= 0f)
                {
                    result.Add(a);
                }

                if ((da < 0f && db > 0f) || (da > 0f && db < 0f))
                {
                    float t = da / (da - db);
                    result.Add(a + (b - a) * t);
                }
            }
        }

        /// <summary>Allocating overload of <see cref="ClipHalfPlane(List{Vector2},Vector2,float,List{Vector2})"/>.</summary>
        public static List<Vector2> ClipHalfPlane(List<Vector2> poly, Vector2 n, float c)
        {
            var list = new List<Vector2>(poly != null ? poly.Count + 2 : 4);
            ClipHalfPlane(poly, n, c, list);
            return list;
        }

        /// <summary>Triangle fan over a convex polygon. UV is 0 (white texture).</summary>
        public static void Fan(VertexHelper vh, List<Vector2> poly, Func<Vector2, Color32> colorAt)
        {
            if (vh == null || poly == null || poly.Count < 3 || colorAt == null)
            {
                return;
            }

            int start = vh.currentVertCount;
            for (int i = 0; i < poly.Count; i++)
            {
                vh.AddVert(poly[i], colorAt(poly[i]), Vector4.zero);
            }

            for (int i = 1; i < poly.Count - 1; i++)
            {
                vh.AddTriangle(start, start + i, start + i + 1);
            }
        }

        /// <summary>
        /// Quad strip between two polygons of the SAME vertex count (border, glow band).
        /// Nothing is drawn when the counts differ — a mismatched pair would weld wrong corners.
        /// </summary>
        public static void Ring(VertexHelper vh, List<Vector2> outer, List<Vector2> inner,
            Func<Vector2, Color32> colorOuter, Func<Vector2, Color32> colorInner)
        {
            if (vh == null || outer == null || inner == null || outer.Count != inner.Count ||
                outer.Count < 3 || colorOuter == null || colorInner == null)
            {
                return;
            }

            int n = outer.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 o0 = outer[i];
                Vector2 o1 = outer[j];
                Vector2 i1 = inner[j];
                Vector2 i0 = inner[i];

                int s = vh.currentVertCount;
                vh.AddVert(o0, colorOuter(o0), Vector4.zero);
                vh.AddVert(o1, colorOuter(o1), Vector4.zero);
                vh.AddVert(i1, colorInner(i1), Vector4.zero);
                vh.AddVert(i0, colorInner(i0), Vector4.zero);
                vh.AddTriangle(s, s + 1, s + 2);
                vh.AddTriangle(s, s + 2, s + 3);
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetVerticesDirty();
        }
#endif
    }
}
