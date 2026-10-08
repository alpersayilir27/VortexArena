using System;
using UnityEditor;
using UnityEngine;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Arena &gt; Bulut Dokusu Üret</c> — bakes the tileable noise
    /// texture that <c>VortexArena/AnimatedSkybox</c> reads as <c>_CloudTex</c>.
    /// </summary>
    public static class CloudNoiseGenerator
    {
        private const string MenuPath = "Tools/VortexArena/Arena/Bulut Dokusu Üret";

        private const string ParentFolder = "Assets/_Shared/World";
        private const string FolderName = "Sky";
        private const string Path = ParentFolder + "/" + FolderName + "/T_CloudNoise.asset";

        private const int Size = 512;

        // Fixed seed: the same texture must come out on every machine, otherwise the authored cloud
        // coverage threshold of each sky material means something else after a re-bake.
        private const int Seed = 1337;

        // Base period in cells; it DOUBLES per octave, so every octave still tiles at the texture
        // edge (a non-integer lacunarity would break the seam).
        private const int BasePeriod = 4;
        private const int Octaves = 5;
        private const float Gain = 0.5f;

        // Unit gradients, 8 directions — enough for cloud cover and keeps the table tiny.
        private static readonly Vector2[] Gradients =
        {
            new Vector2(1f, 0f), new Vector2(-1f, 0f),
            new Vector2(0f, 1f), new Vector2(0f, -1f),
            new Vector2(0.7071068f, 0.7071068f), new Vector2(-0.7071068f, 0.7071068f),
            new Vector2(0.7071068f, -0.7071068f), new Vector2(-0.7071068f, -0.7071068f)
        };

        [MenuItem(MenuPath, false, 30)]
        private static void Generate()
        {
            EnsureFolder();

            int[] perm = BuildPermutation();
            float[] values = new float[Size * Size];
            float min = float.MaxValue;
            float max = float.MinValue;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Normalized coords: the noise is evaluated in period space, so the texture
                    // wraps exactly on both axes.
                    float v = Fbm(perm, x / (float)Size, y / (float)Size);
                    values[y * Size + x] = v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }

            float range = Mathf.Max(max - min, 1e-6f);
            byte[] data = new byte[Size * Size];
            for (int i = 0; i < values.Length; i++)
            {
                float v = (values[i] - min) / range;
                // Mild contrast so the shader's coverage threshold has a usable range across 0..1
                // instead of everything bunching around 0.5.
                v = v * v * (3f - 2f * v);
                data[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
            }

            var tex = new Texture2D(Size, Size, TextureFormat.R8, true, true)
            {
                name = "T_CloudNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2
            };
            tex.SetPixelData(data, 0);
            tex.Apply(true, false);

            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
            if (existing != null)
            {
                // Overwrite IN PLACE: the GUID survives, so every sky material keeps its reference.
                EditorUtility.CopySerialized(tex, existing);
                UnityEngine.Object.DestroyImmediate(tex);
                tex = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(tex, Path);
            }

            EditorUtility.SetDirty(tex);
            AssetDatabase.SaveAssets();

            Selection.activeObject = tex;
            EditorGUIUtility.PingObject(tex);
            Debug.Log($"[Bulut] Bulut dokusu üretildi: {Path}");
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(ParentFolder + "/" + FolderName))
            {
                AssetDatabase.CreateFolder(ParentFolder, FolderName);
            }
        }

        private static int[] BuildPermutation()
        {
            var rng = new System.Random(Seed);
            int[] source = new int[256];
            for (int i = 0; i < 256; i++)
            {
                source[i] = i;
            }

            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = source[i];
                source[i] = source[j];
                source[j] = tmp;
            }

            // Doubled table so lookups can add without a second wrap.
            int[] perm = new int[512];
            for (int i = 0; i < 512; i++)
            {
                perm[i] = source[i & 255];
            }
            return perm;
        }

        private static float Fbm(int[] perm, float u, float v)
        {
            float sum = 0f;
            float amp = 1f;
            int period = BasePeriod;

            for (int o = 0; o < Octaves; o++)
            {
                sum += amp * PeriodicNoise(perm, u * period, v * period, period);
                amp *= Gain;
                period *= 2;
            }
            return sum;
        }

        /// <summary>Gradient (Perlin) noise whose lattice wraps at <paramref name="period"/>.</summary>
        private static float PeriodicNoise(int[] perm, float x, float y, int period)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;

            int x0 = Wrap(xi, period);
            int y0 = Wrap(yi, period);
            int x1 = Wrap(xi + 1, period);
            int y1 = Wrap(yi + 1, period);

            float n00 = Dot(perm, x0, y0, xf, yf);
            float n10 = Dot(perm, x1, y0, xf - 1f, yf);
            float n01 = Dot(perm, x0, y1, xf, yf - 1f);
            float n11 = Dot(perm, x1, y1, xf - 1f, yf - 1f);

            float fx = Fade(xf);
            float fy = Fade(yf);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, fx), Mathf.Lerp(n01, n11, fx), fy);
        }

        private static float Dot(int[] perm, int ix, int iy, float dx, float dy)
        {
            Vector2 g = Gradients[perm[perm[ix & 255] + (iy & 255)] & 7];
            return g.x * dx + g.y * dy;
        }

        private static int Wrap(int v, int period)
        {
            return ((v % period) + period) % period;
        }

        private static float Fade(float t)
        {
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }
    }
}
