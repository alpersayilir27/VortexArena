using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.UI;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Generates the "Lokanta" skin's sprites (faces, star, tiles) as PNGs.
    /// <para>
    /// <b>Why code and not art files:</b> they are plain SDF shapes taken straight from
    /// <c>asci.css</c>; a hand-exported PNG drifts from the palette the moment a token changes, and
    /// the files are tiny to rebuild.
    /// </para>
    /// <para>⚠️ An EXISTING png is never overwritten — a sprite is already referenced by generated
    /// prefabs and a fresh file would silently change their art. Delete the png to re-generate.</para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        // Face palette (asci.css .face gradients); the rest comes from Lokanta.
        private static readonly Color FaceHappyA = Girdap.Hex(0xFFE56A);
        private static readonly Color FaceSadA = Girdap.Hex(0xFFB0A6);
        private static readonly Color FaceMehA = Girdap.Hex(0xFFE6A6);
        private static readonly Color FaceMehB = Girdap.Hex(0xF0B860);
        private static readonly Color DotColor = Girdap.Hex(0xFFE9A6);

        private const float FaceDisc = 56f;
        private const float FaceRim = 12f;
        private const float FaceStroke = 7f;

        /// <summary>Writes the missing skin sprites and keeps their importer settings in sync.</summary>
        public static void EnsureLokantaSprites()
        {
            EnsureFolder(LkSpriteDir);

            WriteFace("Lk_FaceHappy", LkMood.Happy, FaceHappyA, Lokanta.Must);
            WriteFace("Lk_FaceMeh", LkMood.Meh, FaceMehA, FaceMehB);
            WriteFace("Lk_FaceSad", LkMood.Sad, FaceSadA, Lokanta.Red);
            WriteStar();
            WriteChecker();
            WriteDot();

            Configure("Lk_FaceHappy", false);
            Configure("Lk_FaceMeh", false);
            Configure("Lk_FaceSad", false);
            Configure("Lk_Star", false);
            Configure("Lk_Checker", true);
            Configure("Lk_Dot", true);
        }

        // --------------------------------------------------------------- drawing

        private static void WriteFace(string fileName, LkMood mood, Color centre, Color edge)
        {
            if (Exists(fileName))
            {
                return;
            }

            var bmp = new LkBitmap(128, 128, 2);

            bmp.Paint(p => Sdf.Circle(p, Vector2.zero, FaceDisc), Lokanta.Ink);

            float fill = FaceDisc - FaceRim;
            bmp.Paint(p => Sdf.Circle(p, Vector2.zero, fill),
                p => Color.Lerp(centre, edge, Mathf.Clamp01(p.magnitude / fill)));

            bmp.Paint(p => Sdf.Circle(p, new Vector2(-19f, 12f), 6f), Lokanta.Ink);
            bmp.Paint(p => Sdf.Circle(p, new Vector2(19f, 12f), 6f), Lokanta.Ink);

            float half = FaceStroke * 0.5f;
            switch (mood)
            {
                case LkMood.Happy:
                    // Bottom 100° of a circle: a smile that keeps its thickness at the ends.
                    PaintArc(bmp, new Vector2(0f, 2f), 26f, -140f, -40f, half, Lokanta.Ink);
                    break;
                case LkMood.Sad:
                    PaintArc(bmp, new Vector2(0f, -32f), 26f, 40f, 140f, half, Lokanta.Ink);
                    break;
                default:
                    bmp.Paint(p => Sdf.Capsule(p, new Vector2(-16f, -14f), new Vector2(16f, -14f),
                        half), Lokanta.Ink);
                    break;
            }

            Write(fileName, bmp);
        }

        // Arc stroke as a capsule chain: round caps and clean AA without an angular clip.
        private static void PaintArc(LkBitmap bmp, Vector2 c, float r, float fromDeg, float toDeg,
            float halfWidth, Color color)
        {
            const int Steps = 24;
            for (int i = 0; i < Steps; i++)
            {
                Vector2 a = OnCircle(c, r, Mathf.Lerp(fromDeg, toDeg, i / (float)Steps));
                Vector2 b = OnCircle(c, r, Mathf.Lerp(fromDeg, toDeg, (i + 1) / (float)Steps));
                bmp.Paint(p => Sdf.Capsule(p, a, b, halfWidth), color);
            }
        }

        private static Vector2 OnCircle(Vector2 c, float r, float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            return c + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * r;
        }

        private static void WriteStar()
        {
            if (Exists("Lk_Star"))
            {
                return;
            }

            var bmp = new LkBitmap(256, 256, 2);

            Vector2[] outer = Star(16, 124f, 90f, 1f);
            bmp.Paint(p => Sdf.Polygon(p, outer), Lokanta.Ink);

            Vector2[] inner = Star(16, 124f, 90f, 0.9f);
            float top = 124f * 0.9f;
            bmp.Paint(p => Sdf.Polygon(p, inner),
                p => Color.Lerp(Lokanta.MustHi, Lokanta.CheeseB,
                    Mathf.Clamp01((top - p.y) / (top * 2f))));

            Write("Lk_Star", bmp);
        }

        private static Vector2[] Star(int points, float outer, float inner, float scale)
        {
            var result = new Vector2[points * 2];
            for (int i = 0; i < result.Length; i++)
            {
                float deg = 90f + i * 360f / result.Length;
                float r = ((i & 1) == 0 ? outer : inner) * scale;
                result[i] = OnCircle(Vector2.zero, r, deg);
            }

            return result;
        }

        private static void WriteChecker()
        {
            if (Exists("Lk_Checker"))
            {
                return;
            }

            const int Size = 28;
            const int Cell = Size / 2;
            var tex = NewTexture(Size, Size);
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool dark = (x / Cell + y / Cell) % 2 == 0;
                    pixels[y * Size + x] = dark ? Lokanta.Red : Lokanta.Light;
                }
            }

            tex.SetPixels(pixels);
            Write("Lk_Checker", tex);
        }

        private static void WriteDot()
        {
            if (Exists("Lk_Dot"))
            {
                return;
            }

            var bmp = new LkBitmap(12, 4, 4);
            bmp.Paint(p => Sdf.Circle(p, Vector2.zero, 1.7f), DotColor);
            Write("Lk_Dot", bmp);
        }

        // ----------------------------------------------------------------- files

        private static bool Exists(string fileName)
        {
            return File.Exists(LkSpriteDir + fileName + ".png");
        }

        private static void Write(string fileName, LkBitmap bmp)
        {
            Write(fileName, bmp.ToTexture());
        }

        private static void Write(string fileName, Texture2D tex)
        {
            string path = LkSpriteDir + fileName + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static Texture2D NewTexture(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        }

        /// <summary>Importer settings for a skin sprite; reimports only when something changed, so a
        /// rebuild of the asset container does not churn every png.</summary>
        private static void Configure(string fileName, bool tile)
        {
            string path = LkSpriteDir + fileName + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[Lokanta] {path} içe aktarılmamış.");
                return;
            }

            TextureWrapMode wrap = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            bool dirty = importer.textureType != TextureImporterType.Sprite
                         || importer.spritePixelsPerUnit != 100f
                         || !importer.alphaIsTransparency
                         || importer.mipmapEnabled
                         || importer.filterMode != FilterMode.Bilinear
                         || importer.textureCompression != TextureImporterCompression.Uncompressed
                         || importer.npotScale != TextureImporterNPOTScale.None
                         || importer.wrapMode != wrap
                         || importer.spriteImportMode != SpriteImportMode.Single
                         || settings.spriteMeshType != SpriteMeshType.FullRect;
            if (!dirty)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = wrap;
            importer.spriteImportMode = SpriteImportMode.Single;

            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- canvas

        /// <summary>
        /// Supersampled premultiplied-alpha scratch canvas. Coordinates are CENTRED and y is UP, in
        /// output pixels — the same space the CSS measurements are given in.
        /// <para>⚠️ Premultiplied on purpose: averaging straight-alpha samples on downsample pulls
        /// the transparent background's black into the edge and leaves a dark fringe.</para>
        /// </summary>
        private sealed class LkBitmap
        {
            private const float AntiAlias = 1.5f;

            private readonly int width;
            private readonly int height;
            private readonly int scale;
            private readonly int sw;
            private readonly int sh;
            private readonly Vector4[] buffer;

            public LkBitmap(int width, int height, int scale)
            {
                this.width = width;
                this.height = height;
                this.scale = Mathf.Max(scale, 1);
                sw = width * this.scale;
                sh = height * this.scale;
                buffer = new Vector4[sw * sh];
            }

            public void Paint(Func<Vector2, float> sdf, Color color)
            {
                Paint(sdf, _ => color);
            }

            public void Paint(Func<Vector2, float> sdf, Func<Vector2, Color> colorAt)
            {
                float step = 1f / scale;
                for (int y = 0; y < sh; y++)
                {
                    float py = (y + 0.5f) * step - height * 0.5f;
                    for (int x = 0; x < sw; x++)
                    {
                        var p = new Vector2((x + 0.5f) * step - width * 0.5f, py);
                        float coverage = Mathf.Clamp01(0.5f - sdf(p) / AntiAlias);
                        if (coverage <= 0f)
                        {
                            continue;
                        }

                        Color c = colorAt(p);
                        float a = c.a * coverage;
                        if (a <= 0f)
                        {
                            continue;
                        }

                        int i = y * sw + x;
                        buffer[i] = buffer[i] * (1f - a)
                                    + new Vector4(c.r * a, c.g * a, c.b * a, a);
                    }
                }
            }

            public Texture2D ToTexture()
            {
                var pixels = new Color[width * height];
                float inv = 1f / (scale * scale);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Vector4 sum = Vector4.zero;
                        for (int sy = 0; sy < scale; sy++)
                        {
                            int row = (y * scale + sy) * sw + x * scale;
                            for (int sx = 0; sx < scale; sx++)
                            {
                                sum += buffer[row + sx];
                            }
                        }

                        sum *= inv;
                        float a = sum.w;
                        pixels[y * width + x] = a > 1e-5f
                            ? new Color(sum.x / a, sum.y / a, sum.z / a, a)
                            : Color.clear;
                    }
                }

                Texture2D tex = NewTexture(width, height);
                tex.SetPixels(pixels);
                return tex;
            }
        }

        /// <summary>Signed distance helpers; negative is inside, units are output pixels.</summary>
        private static class Sdf
        {
            public static float Circle(Vector2 p, Vector2 c, float r)
            {
                return (p - c).magnitude - r;
            }

            public static float RoundBox(Vector2 p, Vector2 c, Vector2 half, float r)
            {
                Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half
                            + new Vector2(r, r);
                float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
                return outside + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
            }

            public static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
            {
                Vector2 pa = p - a;
                Vector2 ba = b - a;
                float denom = Vector2.Dot(ba, ba);
                float h = denom > 1e-6f ? Mathf.Clamp01(Vector2.Dot(pa, ba) / denom) : 0f;
                return (pa - ba * h).magnitude - r;
            }

            /// <summary>Distance to a closed polygon; the sign comes from a crossing-number test.</summary>
            public static float Polygon(Vector2 p, Vector2[] v)
            {
                float d = Vector2.Dot(p - v[0], p - v[0]);
                float sign = 1f;

                for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
                {
                    Vector2 e = v[j] - v[i];
                    Vector2 w = p - v[i];
                    float denom = Vector2.Dot(e, e);
                    float h = denom > 1e-6f ? Mathf.Clamp01(Vector2.Dot(w, e) / denom) : 0f;
                    Vector2 b = w - e * h;
                    d = Mathf.Min(d, Vector2.Dot(b, b));

                    bool c1 = p.y >= v[i].y;
                    bool c2 = p.y < v[j].y;
                    bool c3 = e.x * w.y > e.y * w.x;
                    if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
                    {
                        sign = -sign;
                    }
                }

                return sign * Mathf.Sqrt(d);
            }
        }
    }
}
