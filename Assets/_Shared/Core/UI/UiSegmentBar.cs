using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Segmented, skewed health bar (CSS <c>.hp</c>: 12 px tall, 9 px tick + 3 px gap,
    /// <c>skewX(-24deg)</c>). Mesh rather than a masked Image pair: the CSS version needs a
    /// repeating mask + a skew transform, and a skewed RectTransform does not exist in uGUI.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))] // not inherited from Graphic — see UiShape
    [AddComponentMenu("VortexArena/UI/Segment Bar")]
    public class UiSegmentBar : MaskableGraphic
    {
        [SerializeField] private float segment = 9f;
        [SerializeField] private float gap = 3f;

        /// <summary>Skew in degrees; the TOP edge slides right (a "/" lean), as CSS
        /// <c>skewX(-24deg)</c> does on a y-down canvas.</summary>
        [SerializeField] private float skewDeg = 24f;

        [SerializeField, Range(0f, 1f)] private float fill = 1f;
        [SerializeField] private Color trackColor = new Color(0.094f, 0.137f, 0.239f, 1f);
        [SerializeField] private Color fillA = Color.white;
        [SerializeField] private Color fillB = Color.white;

        private const int MaxSegments = 256;

        private readonly List<Vector2> quad = new List<Vector2>(4);

        private Rect box;

        public override Texture mainTexture => s_WhiteTexture;

        public float Fill => fill;

        public void SetFill(float value)
        {
            float next = Mathf.Clamp01(value);
            if (Mathf.Approximately(next, fill))
            {
                return;
            }

            fill = next;
            SetVerticesDirty();
        }

        public void SetFillColors(Color a, Color b)
        {
            fillA = a;
            fillB = b;
            SetVerticesDirty();
        }

        public void SetTrack(Color c)
        {
            trackColor = c;
            SetVerticesDirty();
        }

        public void SetMetrics(float segmentWidth, float gapWidth, float skewDegrees)
        {
            segment = segmentWidth;
            gap = gapWidth;
            skewDeg = skewDegrees;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            box = GetPixelAdjustedRect();
            if (box.width <= 0f || box.height <= 0f || segment <= 0f)
            {
                return;
            }

            float step = segment + Mathf.Max(0f, gap);
            float cutoff = box.xMin + box.width * fill;
            Color32 track = Tint(trackColor);

            int count = Mathf.Min(MaxSegments, Mathf.CeilToInt(box.width / step));
            for (int i = 0; i < count; i++)
            {
                float x0 = box.xMin + i * step;
                float x1 = Mathf.Min(x0 + segment, box.xMax);
                if (x1 <= x0)
                {
                    break;
                }

                SetQuad(x0, x1);
                UiPolygonGraphic.Fan(vh, quad, _ => track);

                if (fill <= 0f || x0 >= cutoff)
                {
                    continue;
                }

                // Fill is clipped by the bar's progress, not by segment boundaries — a partially
                // filled tick must read as partial.
                SetQuad(x0, Mathf.Min(x1, cutoff));
                UiPolygonGraphic.Fan(vh, quad, FillColor);
            }
        }

        // Builds one tick and applies the skew; shear is around the vertical center so the bar does
        // not drift out of its rect.
        private void SetQuad(float x0, float x1)
        {
            float tan = Mathf.Tan(skewDeg * Mathf.Deg2Rad);
            float cy = box.center.y;

            quad.Clear();
            quad.Add(Skew(new Vector2(x0, box.yMax), cy, tan));
            quad.Add(Skew(new Vector2(x1, box.yMax), cy, tan));
            quad.Add(Skew(new Vector2(x1, box.yMin), cy, tan));
            quad.Add(Skew(new Vector2(x0, box.yMin), cy, tan));
        }

        private static Vector2 Skew(Vector2 p, float cy, float tan)
        {
            p.x += (p.y - cy) * tan;
            return p;
        }

        private Color32 FillColor(Vector2 p)
        {
            float t = box.height > 1e-4f ? Mathf.Clamp01((box.yMax - p.y) / box.height) : 0f;
            return Tint(Color.Lerp(fillA, fillB, t));
        }

        private Color32 Tint(Color c)
        {
            Color tint = color;
            return new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a * tint.a);
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
