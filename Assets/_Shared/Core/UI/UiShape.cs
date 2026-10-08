using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// "Girdap" theme's chamfered/slanted box: outer border ring + gradient fill + outer/inner glow.
    /// One component replaces the CSS trio (<c>clip-path</c> border layer, <c>::before</c> fill,
    /// <c>box-shadow</c>) so a box is a single draw instead of three nested Images.
    /// </summary>
    // ⚠️ RequireComponent is NOT inherited from Graphic: without this, AddComponent<UiShape>() yields
    // a graphic with no CanvasRenderer, which draws nothing and throws on disable.
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("VortexArena/UI/Shape")]
    public class UiShape : UiPolygonGraphic
    {
        [SerializeField] private Color fillA = Color.white;
        [SerializeField] private Color fillB = Color.white;
        [SerializeField] private UiGradientMode gradient = UiGradientMode.None;

        /// <summary>Gradient stop (0..1). CSS <c>linear-gradient(180deg, A, B 48%)</c> → 0.48;
        /// past the stop the color stays flat B.</summary>
        [SerializeField] private float gradientEnd = 1f;

        [SerializeField] private float outlineWidth;
        [SerializeField] private Color outlineA = Color.white;
        [SerializeField] private Color outlineB = Color.white;

        [SerializeField] private float glowWidth;
        [SerializeField] private Color glowColor;
        [SerializeField] private Vector2 glowOffset;

        [SerializeField] private float innerGlowWidth;
        [SerializeField] private Color innerGlowColor;

        /// <summary>0.75 px feather outside the outermost edge. Off only for shapes stacked edge to
        /// edge, where the feather would read as a seam.</summary>
        [SerializeField] private bool antialias = true;

        // Scratch lists: OnPopulateMesh runs on every layout pass — per-frame allocation here shows
        // up as GC churn on Quest.
        private readonly List<Vector2> outer = new List<Vector2>(12);
        private readonly List<Vector2> inner = new List<Vector2>(12);
        private readonly List<Vector2> scratch = new List<Vector2>(16);
        private readonly List<Vector2> piece = new List<Vector2>(16);
        private readonly List<Vector2> glowA = new List<Vector2>(12);
        private readonly List<Vector2> glowB = new List<Vector2>(12);
        private readonly List<Vector2> skirt = new List<Vector2>(12);

        private Rect box;

        public override Texture mainTexture => s_WhiteTexture;

        // ⚠️ raycastTarget stays at Graphic's `true` default: it is a serialized base field, so it
        // cannot be flipped from a field initializer or a constructor (the constructor runs before
        // deserialization and would be overwritten). Setting it in Reset/Awake would then fight
        // prefabs that deliberately raycast. The builder turns it off per shape instead.

        // ------------------------------------------------------------- fluent api

        public UiShape Chamfer(float tl, float tr, float br, float bl)
        {
            ChamferTopLeft = tl;
            ChamferTopRight = tr;
            ChamferBottomRight = br;
            ChamferBottomLeft = bl;
            return this;
        }

        /// <summary>CSS <c>--c</c>: cuts the top-left and bottom-right corners only.</summary>
        public UiShape Chamfer(float c)
        {
            return Chamfer(c, 0f, c, 0f);
        }

        /// <summary>Per-corner rounding in px, TL → TR → BR → BL.</summary>
        public UiShape Radius(float tl, float tr, float br, float bl)
        {
            RadiusTopLeft = tl;
            RadiusTopRight = tr;
            RadiusBottomRight = br;
            RadiusBottomLeft = bl;
            return this;
        }

        /// <summary>CSS <c>border-radius</c>: the same rounding on all four corners.</summary>
        public UiShape Radius(float all)
        {
            return Radius(all, all, all, all);
        }

        public UiShape Slant(float left, float right)
        {
            SlantLeft = left;
            SlantRight = right;
            return this;
        }

        public UiShape Fill(Color c)
        {
            fillA = c;
            fillB = c;
            gradient = UiGradientMode.None;
            gradientEnd = 1f;
            SetVerticesDirty();
            return this;
        }

        public UiShape Fill(Color a, Color b, UiGradientMode m, float end = 1f)
        {
            fillA = a;
            fillB = b;
            gradient = m;
            gradientEnd = Mathf.Clamp01(end);
            SetVerticesDirty();
            return this;
        }

        public UiShape Outline(float w, Color a, Color b)
        {
            outlineWidth = w;
            outlineA = a;
            outlineB = b;
            SetVerticesDirty();
            return this;
        }

        public UiShape Outline(float w, Color c)
        {
            return Outline(w, c, c);
        }

        public UiShape Glow(float w, Color c, Vector2 offset = default)
        {
            glowWidth = w;
            glowColor = c;
            glowOffset = offset;
            SetVerticesDirty();
            return this;
        }

        public UiShape InnerGlow(float w, Color c)
        {
            innerGlowWidth = w;
            innerGlowColor = c;
            SetVerticesDirty();
            return this;
        }

        public UiShape Antialias(bool on)
        {
            antialias = on;
            SetVerticesDirty();
            return this;
        }

        public float OutlineWidth => outlineWidth;

        // ---------------------------------------------------------------- render

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            box = GetPixelAdjustedRect();
            BuildPolygon(box, outer);
            if (outer.Count < 3)
            {
                return;
            }

            DrawOuterGlow(vh);
            DrawBorder(vh);
            DrawFill(vh);
            DrawInnerGlow(vh);
            DrawFeather(vh);
        }

        /// <summary>Outer glow ring count — one Gaussian sample per ring.</summary>
        private const int GlowRings = 4;

        /// <summary>
        /// CSS <c>box-shadow</c>/<c>drop-shadow</c>: the shape's mask blurred by a Gaussian with
        /// σ = blur/2. The profile is sampled outward from the edge, so a big plate gets half its
        /// alpha at the edge and a 2 px rule glows faintly — a flat "0.6 at the edge" ramp read as a
        /// translucent box with a dark rim.
        /// </summary>
        private void DrawOuterGlow(VertexHelper vh)
        {
            if (glowWidth <= 0f || glowColor.a <= 0f)
            {
                return;
            }

            float sigma = Mathf.Max(glowWidth * 0.5f, 0.01f);
            float thickness = Mathf.Max(Mathf.Min(box.width, box.height), 0f);

            // Base is the shape edge, optionally shifted to read as a drop shadow.
            scratch.Clear();
            for (int i = 0; i < outer.Count; i++)
            {
                scratch.Add(outer[i] + glowOffset);
            }

            List<Vector2> innerRing = scratch;
            Color32 innerColor = Tint(GlowShade(0f, thickness, sigma));
            for (int k = 1; k <= GlowRings; k++)
            {
                List<Vector2> outerRing = (k & 1) == 1 ? glowA : glowB;
                float dist = glowWidth * k / GlowRings;
                Offset(scratch, -dist, outerRing);
                Color32 outerColor = k == GlowRings
                    ? Tint(new Color(glowColor.r, glowColor.g, glowColor.b, 0f))
                    : Tint(GlowShade(dist, thickness, sigma));
                Ring(vh, outerRing, innerRing, _ => outerColor, _ => innerColor);
                innerRing = outerRing;
                innerColor = outerColor;
            }
        }

        // Blurred-mask alpha `dist` px outside the edge of a `thickness` px wide shape.
        private Color GlowShade(float dist, float thickness, float sigma)
        {
            float coverage = Phi((dist + thickness) / sigma) - Phi(dist / sigma);
            return new Color(glowColor.r, glowColor.g, glowColor.b, glowColor.a * coverage);
        }

        private static float Phi(float z)
        {
            return 0.5f * (1f + Erf(z * 0.70710678f));
        }

        // Abramowitz–Stegun 7.1.26, |error| < 1.5e-7.
        private static float Erf(float x)
        {
            float sign = x < 0f ? -1f : 1f;
            x = Mathf.Abs(x);
            float t = 1f / (1f + 0.3275911f * x);
            float y = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t
                             - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);
            return sign * y;
        }

        private void DrawBorder(VertexHelper vh)
        {
            if (outlineWidth <= 0f)
            {
                return;
            }

            Offset(outer, outlineWidth, inner);
            Ring(vh, outer, inner, VerticalEdgeColor, VerticalEdgeColor);
        }

        private void DrawFill(VertexHelper vh)
        {
            List<Vector2> area = outlineWidth > 0f ? inner : outer;
            if (area.Count < 3)
            {
                return;
            }

            if (gradient == UiGradientMode.None)
            {
                Color32 flat = Tint(fillA);
                Fan(vh, area, _ => flat);
                return;
            }

            if (gradientEnd >= 1f - 1e-4f)
            {
                Fan(vh, area, FillColor);
                return;
            }

            // Fill color is AFFINE in x/y up to the stop, flat after it — so splitting the polygon
            // on the stop line and interpolating vertex colors is exact, not an approximation.
            Vector2 n = gradient == UiGradientMode.Vertical ? new Vector2(0f, -1f) : new Vector2(1f, 0f);
            float c = gradient == UiGradientMode.Vertical
                ? -(box.yMax - gradientEnd * box.height)
                : box.xMin + gradientEnd * box.width;

            ClipHalfPlane(area, n, c, piece);
            Fan(vh, piece, FillColor);

            ClipHalfPlane(area, -n, -c, piece);
            Color32 tail = Tint(fillB);
            Fan(vh, piece, _ => tail);
        }

        private void DrawInnerGlow(VertexHelper vh)
        {
            if (innerGlowWidth <= 0f || innerGlowColor.a <= 0f)
            {
                return;
            }

            List<Vector2> area = outlineWidth > 0f ? inner : outer;
            if (area.Count < 3)
            {
                return;
            }

            Offset(area, innerGlowWidth, glowA);

            Color32 edge = Tint(innerGlowColor);
            Color32 center = Tint(new Color(innerGlowColor.r, innerGlowColor.g, innerGlowColor.b, 0f));
            Ring(vh, area, glowA, _ => edge, _ => center);
        }

        private void DrawFeather(VertexHelper vh)
        {
            if (!antialias)
            {
                return;
            }

            Offset(outer, -0.75f, skirt);

            // Explicit delegate construction: a `cond ? MethodA : MethodB` ternary has no common
            // type (method groups are typeless) and does not compile.
            Func<Vector2, Color32> solid = outlineWidth > 0f
                ? new Func<Vector2, Color32>(VerticalEdgeColor)
                : new Func<Vector2, Color32>(FillColor);
            Ring(vh, skirt, outer, p => Transparent(solid(p)), solid);
        }

        private static Color32 Transparent(Color32 c)
        {
            c.a = 0;
            return c;
        }

        // Border gradient runs top (outlineA) → bottom (outlineB).
        private Color32 VerticalEdgeColor(Vector2 p)
        {
            float t = box.height > 1e-4f ? Mathf.Clamp01((box.yMax - p.y) / box.height) : 0f;
            return Tint(Color.Lerp(outlineA, outlineB, t));
        }

        private Color32 FillColor(Vector2 p)
        {
            if (gradient == UiGradientMode.None)
            {
                return Tint(fillA);
            }

            float t;
            if (gradient == UiGradientMode.Vertical)
            {
                t = box.height > 1e-4f ? (box.yMax - p.y) / box.height : 0f;
            }
            else
            {
                t = box.width > 1e-4f ? (p.x - box.xMin) / box.width : 0f;
            }

            float stop = Mathf.Max(gradientEnd, 1e-4f);
            return Tint(Color.Lerp(fillA, fillB, Mathf.Clamp01(t / stop)));
        }
    }
}
