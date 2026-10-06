using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// CSS <c>repeating-linear-gradient(θ, C 0 w, transparent w p)</c> as a mesh: diagonal stripes
    /// clipped to the theme's chamfered polygon (team plates, "live violation" bands).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))] // not inherited from Graphic — see UiShape
    [AddComponentMenu("VortexArena/UI/Stripes")]
    public class UiStripes : UiPolygonGraphic
    {
        /// <summary>CSS gradient angle: 0 = up, 90 = right, −55 = toward the top-left.</summary>
        [SerializeField] private float angleDeg = -55f;

        /// <summary>Opaque band width in px (CSS <c>w</c>).</summary>
        [SerializeField] private float stripeWidth = 6f;

        /// <summary>Band-to-band distance in px (CSS <c>p</c>).</summary>
        [SerializeField] private float period = 17f;

        [SerializeField] private Color stripeColor = new Color(1f, 1f, 1f, 0.075f);

        private const int MaxBands = 512;

        private readonly List<Vector2> outline = new List<Vector2>(12);
        private readonly List<Vector2> band = new List<Vector2>(16);
        private readonly List<Vector2> clip = new List<Vector2>(16);

        public override Texture mainTexture => s_WhiteTexture;

        public float AngleDeg
        {
            get => angleDeg;
            set { angleDeg = value; SetVerticesDirty(); }
        }

        public float StripeWidth
        {
            get => stripeWidth;
            set { stripeWidth = value; SetVerticesDirty(); }
        }

        public float Period
        {
            get => period;
            set { period = value; SetVerticesDirty(); }
        }

        public Color StripeColor
        {
            get => stripeColor;
            set { stripeColor = value; SetVerticesDirty(); }
        }

        public UiStripes Stripes(float angle, float width, float stripePeriod, Color c)
        {
            angleDeg = angle;
            stripeWidth = width;
            period = stripePeriod;
            stripeColor = c;
            SetVerticesDirty();
            return this;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect r = GetPixelAdjustedRect();
            BuildPolygon(r, outline);
            if (outline.Count < 3 || period <= 0f || stripeWidth <= 0f || stripeColor.a <= 0f)
            {
                return;
            }

            // CSS screen space has y DOWN, Unity local has y UP — hence (sin, cos), not (sin, −cos).
            float rad = angleDeg * Mathf.Deg2Rad;
            var d = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            var origin = new Vector2(r.xMin, r.yMax); // CSS gradients start at the top-left

            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < outline.Count; i++)
            {
                float t = Vector2.Dot(outline[i] - origin, d);
                min = Mathf.Min(min, t);
                max = Mathf.Max(max, t);
            }

            int first = Mathf.FloorToInt(min / period);
            int last = Mathf.CeilToInt(max / period);
            if (last - first > MaxBands)
            {
                last = first + MaxBands; // guard against a 1 px period on a full-screen rect
            }

            Color32 c32 = Tint(stripeColor);
            float span = (r.width + r.height) * 2f + period; // comfortably covers the polygon
            var side = new Vector2(-d.y, d.x);
            float outwardSign = SignedArea(outline) > 0f ? -1f : 1f;

            for (int k = first; k <= last; k++)
            {
                float lo = k * period;
                float hi = lo + stripeWidth;

                // Oversized quad along the band, then trimmed down to the band and the polygon.
                Vector2 a = origin + d * lo;
                band.Clear();
                band.Add(a - side * span);
                band.Add(a + d * stripeWidth - side * span);
                band.Add(a + d * stripeWidth + side * span);
                band.Add(a + side * span);

                if (!Trim(band, d, hi) || !Trim(band, -d, -lo))
                {
                    continue;
                }

                bool alive = true;
                for (int i = 0, n = outline.Count; i < n && alive; i++)
                {
                    Vector2 p0 = outline[i];
                    Vector2 p1 = outline[(i + 1) % n];
                    Vector2 e = p1 - p0;
                    if (e.sqrMagnitude < 1e-10f)
                    {
                        continue;
                    }

                    // Outward normal of a clockwise outline (y up) is the LEFT normal.
                    Vector2 nrm = new Vector2(-e.y, e.x).normalized * outwardSign;
                    alive = Trim(band, nrm, Vector2.Dot(p0, nrm));
                }

                if (alive)
                {
                    Fan(vh, band, _ => c32);
                }
            }
        }

        // Clips `poly` in place; false when nothing survives.
        private bool Trim(List<Vector2> poly, Vector2 n, float c)
        {
            ClipHalfPlane(poly, n, c, clip);
            poly.Clear();
            for (int i = 0; i < clip.Count; i++)
            {
                poly.Add(clip[i]);
            }

            return poly.Count >= 3;
        }
    }
}
