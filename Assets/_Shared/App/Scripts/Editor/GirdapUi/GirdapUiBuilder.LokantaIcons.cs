using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Generates the two theme icons the Lokanta screens need and the hand-exported set is missing
    /// (<c>Ic_Flame</c>, <c>Ic_Check</c>).
    /// <para>
    /// ⚠️ Drawn WHITE on transparent like the rest of <c>Ic_*</c>: every call site tints the sprite
    /// through <see cref="UnityEngine.UI.Image.color"/>, and a baked-in colour would ignore the tint.
    /// </para>
    /// <para>
    /// Geometry mirrors <c>Docs/Gelistirici/Arayuz/tema.js</c> map <c>P</c> (24×24 viewBox, stroke 2,
    /// round caps) scaled to the 64 px the existing icons use.
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        private const int IcSize = 64;

        /// <summary>viewBox → output pixels.</summary>
        private const float IcScale = IcSize / 24f;

        /// <summary>CSS <c>stroke-width: 2</c> in output pixels, halved for the SDF.</summary>
        private const float IcStroke = IcScale;

        /// <summary>Writes the missing theme icons. ⚠️ Must run BEFORE the asset container is built —
        /// the icon array is collected from the folder, so a png that appears later is not in it.</summary>
        public static void EnsureGirdapIcons()
        {
            EnsureFolder(IconDir);

            WriteFlameIcon();
            WriteCheckIcon();

            ConfigureIcon("Ic_Flame");
            ConfigureIcon("Ic_Check");
        }

        private static void WriteFlameIcon()
        {
            if (IconExists("Ic_Flame"))
            {
                return;
            }

            var bmp = new LkBitmap(IcSize, IcSize, 2);

            // Teardrop body + a second, smaller teardrop as the inner tongue.
            StrokeLoop(bmp, Teardrop(new Vector2(0f, -2.5f * IcScale), 6.8f * IcScale,
                9.5f * IcScale));
            StrokeLoop(bmp, Teardrop(new Vector2(0f, -3.2f * IcScale), 3.4f * IcScale,
                3.0f * IcScale));

            WriteIcon("Ic_Flame", bmp);
        }

        private static void WriteCheckIcon()
        {
            if (IconExists("Ic_Check"))
            {
                return;
            }

            var bmp = new LkBitmap(IcSize, IcSize, 2);

            // tema.js: M5 12l5 5L20 7 — viewBox coords, y down.
            StrokePath(bmp, new List<Vector2>
            {
                Ic(5f, 12f),
                Ic(10f, 17f),
                Ic(20f, 7f)
            });

            WriteIcon("Ic_Check", bmp);
        }

        // --------------------------------------------------------------- geometry

        /// <summary>viewBox point (y down, origin top-left) → canvas point (y up, origin centre).</summary>
        private static Vector2 Ic(float x, float y)
        {
            return new Vector2((x - 12f) * IcScale, (12f - y) * IcScale);
        }

        /// <summary>
        /// Flame silhouette: the lower arc of a circle closed by its two TANGENTS to a tip above it,
        /// so the outline has no corner where the sides meet the body.
        /// </summary>
        private static List<Vector2> Teardrop(Vector2 c, float r, float tipY)
        {
            const int Steps = 28;

            var tip = new Vector2(c.x, tipY);
            float d = Vector2.Distance(tip, c);
            // Degenerate tip (inside the circle): fall back to a plain circle.
            float alpha = d > r ? Mathf.Acos(r / d) * Mathf.Rad2Deg : 90f;

            float from = 90f + alpha;
            float to = 90f - alpha - 360f;

            var points = new List<Vector2>(Steps + 2);
            for (int i = 0; i <= Steps; i++)
            {
                points.Add(OnCircle(c, r, Mathf.Lerp(from, to, i / (float)Steps)));
            }

            points.Add(tip);
            return points;
        }

        private static void StrokeLoop(LkBitmap bmp, List<Vector2> points)
        {
            points.Add(points[0]);
            StrokePath(bmp, points);
        }

        // Round-capped capsule chain: matches SVG stroke-linecap/linejoin "round".
        private static void StrokePath(LkBitmap bmp, List<Vector2> points)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];
                bmp.Paint(p => Sdf.Capsule(p, a, b, IcStroke), Color.white);
            }
        }

        // ------------------------------------------------------------------ files

        private static bool IconExists(string fileName)
        {
            return File.Exists(IconDir + fileName + ".png");
        }

        private static void WriteIcon(string fileName, LkBitmap bmp)
        {
            string path = IconDir + fileName + ".png";
            Texture2D tex = bmp.ToTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>Importer settings of a theme icon — matched to the hand-exported ones (mipmaps ON,
        /// they are drawn at many sizes), so the set stays uniform.</summary>
        private static void ConfigureIcon(string fileName)
        {
            string path = IconDir + fileName + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[Girdap] {path} içe aktarılmamış.");
                return;
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            bool dirty = importer.textureType != TextureImporterType.Sprite
                         || importer.spritePixelsPerUnit != 100f
                         || !importer.alphaIsTransparency
                         || !importer.mipmapEnabled
                         || importer.spriteImportMode != SpriteImportMode.Single
                         || settings.spriteMeshType != SpriteMeshType.FullRect;
            if (!dirty)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.spriteImportMode = SpriteImportMode.Single;

            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
    }
}
