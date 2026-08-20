using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>2D polygon math over an <b>ordered corner ring</b>: containment, distance, bounds and
    /// validation. Pure computation — knows nothing of scenes, assets or components.</summary>
    /// <remarks>
    /// Used both at runtime (<see cref="ArenaBoundary"/> distances) and in the editor (dimension mesh
    /// generation/read-back). Since the rings in <see cref="ArenaDimensions"/> are the only
    /// representation of the arena measurements, every geometric question about them is answered
    /// here.
    /// <para>⚠️ The ring is CLOSED: the last corner connects to the first automatically — do not
    /// repeat the first point. Winding order is irrelevant in every method.</para>
    /// <para>⚠️ Polygon UNION is deliberately absent and is not added. Neither floor nor column is
    /// assembled from pieces; both are a single ring. Concavity is no obstacle to that (an L shape,
    /// a trapezoid, an indented wall are one ring, and the tests here are correct on concave rings).
    /// Union would only buy the convenience of "write the area as overlapping rectangles", at the
    /// cost of edge-case-heavy planar-arrangement code that <see cref="ArenaBoundary"/> would run on
    /// scene load.</para>
    /// <para>⚠️ Methods allocate nothing (<see cref="Bounds"/> included): the boundary calls them
    /// every frame and a temporary array means GC pressure.</para>
    /// </remarks>
    public static class Polygon2D
    {
        /// <summary>Minimum number of corners required for a valid polygon.</summary>
        public const int MinPoints = 3;

        /// <summary>Is the ring usable (non-null and at least a triangle).</summary>
        public static bool IsValid(Vector2[] ring)
        {
            return ring != null && ring.Length >= MinPoints;
        }

        // ------------------------------------------------------------- containment

        /// <summary>Is the point inside the ring (ray casting along +X, odd crossings = inside).
        /// Correct on concave polygons too.
        /// <para>For a point exactly on the boundary the result is undefined (floating point) —
        /// harmless, since boundary decisions go through <see cref="SignedDistance"/>, where the edge
        /// is 0 and equidistant from both sides.</para></summary>
        public static bool Contains(Vector2[] ring, Vector2 point)
        {
            if (!IsValid(ring))
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                Vector2 a = ring[i];
                Vector2 b = ring[j];

                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        // ---------------------------------------------------------------- distance

        /// <summary>Distance to the nearest EDGE SEGMENT (unsigned, always ≥ 0). Segments, not
        /// infinite lines, so results near corners are correct. Returns
        /// <see cref="float.MaxValue"/> for an invalid ring, so it stays neutral in a
        /// <c>Mathf.Min</c> chain.</summary>
        public static float DistanceToRing(Vector2[] ring, Vector2 point)
        {
            if (!IsValid(ring))
            {
                return float.MaxValue;
            }

            float minDistance = float.MaxValue;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                float distance = DistanceToSegment(point, ring[i], ring[j]);
                if (distance < minDistance)
                {
                    minDistance = distance;
                }
            }

            return minDistance;
        }

        /// <summary>Boundary contract (area): <b>+</b> inside (that much margin to the edge),
        /// <b>−</b> outside. The magnitude is the distance to the nearest edge segment either
        /// way.</summary>
        public static float SignedDistance(Vector2[] ring, Vector2 point)
        {
            if (!IsValid(ring))
            {
                return float.MaxValue;
            }

            float distance = DistanceToRing(ring, point);
            return Contains(ring, point) ? distance : -distance;
        }

        /// <summary>Boundary contract (obstacle): the sign inverse of <see cref="SignedDistance"/> —
        /// <b>+</b> outside the obstacle, <b>−</b> inside.
        /// <para>Two contracts exist so the boundary can merge them with a single <c>Mathf.Min</c>:
        /// in both, "positive = safe margin". Safe means inside for the area and outside for an
        /// obstacle.</para></summary>
        public static float ObstacleDistance(Vector2[] ring, Vector2 point)
        {
            if (!IsValid(ring))
            {
                return float.MaxValue;
            }

            float distance = DistanceToRing(ring, point);
            return Contains(ring, point) ? -distance : distance;
        }

        /// <summary>Distance from the point to the <c>a</c>–<c>b</c> segment.</summary>
        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-8f)
            {
                return Vector2.Distance(point, a); // degenerate edge (two coincident corners)
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq);
            return Vector2.Distance(point, a + ab * t);
        }

        // ------------------------------------------------------------ measurements

        /// <summary>XZ bounding box of the ring; a zero box for an invalid ring.
        /// <para>⚠️ Framing also needs the box CENTER: measurements are usually taken from a corner,
        /// so the box is NOT centered on the transform.</para></summary>
        public static Rect Bounds(Vector2[] ring)
        {
            if (!IsValid(ring))
            {
                return new Rect(0f, 0f, 0f, 0f);
            }

            float minX = ring[0].x, maxX = ring[0].x;
            float minY = ring[0].y, maxY = ring[0].y;

            for (int i = 1; i < ring.Length; i++)
            {
                minX = Mathf.Min(minX, ring[i].x);
                maxX = Mathf.Max(maxX, ring[i].x);
                minY = Mathf.Min(minY, ring[i].y);
                maxY = Mathf.Max(maxY, ring[i].y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        /// <summary>Signed area (+ / − by winding). Its absolute value is the real area.</summary>
        public static float SignedArea(Vector2[] ring)
        {
            if (!IsValid(ring))
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                sum += (ring[j].x * ring[i].y) - (ring[i].x * ring[j].y);
            }

            return sum * 0.5f;
        }

        /// <summary>Centroid of the ring — becomes the column prism's pivot during mesh generation,
        /// so dragging a column with the Move tool feels natural.
        /// <para>Falls back to the arithmetic mean of the corners when the area is near zero
        /// (degenerate ring), where the centroid formula would divide by zero.</para></summary>
        public static Vector2 Centroid(Vector2[] ring)
        {
            if (!IsValid(ring))
            {
                return Vector2.zero;
            }

            float area = SignedArea(ring);
            if (Mathf.Abs(area) < 1e-6f)
            {
                Vector2 sum = Vector2.zero;
                for (int i = 0; i < ring.Length; i++)
                {
                    sum += ring[i];
                }

                return sum / ring.Length;
            }

            float cx = 0f, cy = 0f;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                float cross = (ring[j].x * ring[i].y) - (ring[i].x * ring[j].y);
                cx += (ring[j].x + ring[i].x) * cross;
                cy += (ring[j].y + ring[i].y) * cross;
            }

            float scale = 1f / (6f * area);
            return new Vector2(cx * scale, cy * scale);
        }

        // -------------------------------------------------------------- validation

        /// <summary>Does the ring self-intersect — VALIDATION ONLY, the boundary never calls it.
        /// <para>An area whose corners are written in the wrong order (hourglass shape) silently
        /// defines a wrong region: <see cref="Contains"/> still answers, but the answer does not
        /// represent the measured room. Generation tools catch and warn about it.</para>
        /// <para>O(n²) — fine in the editor, since corner counts are on the order of a room's
        /// corners.</para></summary>
        public static bool IsSelfIntersecting(Vector2[] ring)
        {
            if (!IsValid(ring))
            {
                return false;
            }

            int count = ring.Length;
            for (int i = 0; i < count; i++)
            {
                Vector2 a1 = ring[i];
                Vector2 a2 = ring[(i + 1) % count];

                for (int j = i + 1; j < count; j++)
                {
                    // Adjacent edges already "intersect" at their shared corner — skipped. The
                    // (0, n-1) pair is adjacent too: the ring is closed, so the last edge joins the
                    // first.
                    if (j == i + 1 || (i == 0 && j == count - 1))
                    {
                        continue;
                    }

                    if (SegmentsIntersect(a1, a2, ring[j], ring[(j + 1) % count]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Do two segments intersect (endpoints included, via orientation signs).</summary>
        private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float d1 = Cross(q1, q2, p1);
            float d2 = Cross(q1, q2, p2);
            float d3 = Cross(p1, p2, q1);
            float d4 = Cross(p1, p2, q2);

            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
            {
                return true;
            }

            // Collinear cases: one endpoint lying on the other segment.
            if (Mathf.Approximately(d1, 0f) && OnSegment(q1, q2, p1)) return true;
            if (Mathf.Approximately(d2, 0f) && OnSegment(q1, q2, p2)) return true;
            if (Mathf.Approximately(d3, 0f) && OnSegment(p1, p2, q1)) return true;
            if (Mathf.Approximately(d4, 0f) && OnSegment(p1, p2, q2)) return true;

            return false;
        }

        /// <summary>Cross product of <c>a</c>→<c>b</c> and <c>a</c>→<c>p</c> (sign = turn direction).</summary>
        private static float Cross(Vector2 a, Vector2 b, Vector2 p)
        {
            return ((b.x - a.x) * (p.y - a.y)) - ((b.y - a.y) * (p.x - a.x));
        }

        /// <summary>Is <c>p</c>, known to be collinear, within the bounds of the <c>a</c>–<c>b</c>
        /// segment.</summary>
        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            return p.x >= Mathf.Min(a.x, b.x) && p.x <= Mathf.Max(a.x, b.x) &&
                   p.y >= Mathf.Min(a.y, b.y) && p.y <= Mathf.Max(a.y, b.y);
        }
    }
}
