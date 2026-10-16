using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Generates the blood splat ATLAS in code: 2×2 cells, each one blob cluster with
    /// droplets.</summary>
    /// <remarks>⚠️ Generated instead of authored on purpose: a splat texture is noise, and an imported
    /// asset would be one more file to keep in sync with the material the decals use. Built ONCE, lazily
    /// (~65k pixels), and uploaded with <c>Apply(false, true)</c> so the CPU copy is dropped.
    /// <para>⚠️ Drawn from a FIXED seed: the splat is placed in the world on every headset
    /// independently, and a texture that differs per visor would read as two different hits.</para></remarks>
    internal static class ZombieBloodTexture
    {
        /// <summary>Atlas side (px). 2×2 cells → 128 px per splat, which is as much as a 0,7 m decal
        /// resolves on a Quest display.</summary>
        private const int Size = 256;

        public const int Cells = 2;

        private const int Seed = 91739;

        private static Texture2D _atlas;

        public static Texture2D Atlas
        {
            get
            {
                if (_atlas == null)
                {
                    _atlas = Build();
                }

                return _atlas;
            }
        }

        private static Texture2D Build()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "ZombieBloodAtlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[Size * Size];
            int cell = Size / Cells;

            Random.State state = Random.state;
            Random.InitState(Seed);

            for (int cy = 0; cy < Cells; cy++)
            {
                for (int cx = 0; cx < Cells; cx++)
                {
                    DrawCell(pixels, cx * cell, cy * cell, cell);
                }
            }

            Random.state = state;

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>One splat: a big central blob, a few satellites and small droplets, all with a
        /// noisy edge.</summary>
        private static void DrawCell(Color32[] pixels, int originX, int originY, int cell)
        {
            int blobs = Random.Range(4, 7);
            var centres = new Vector2[blobs];
            var radii = new float[blobs];

            for (int i = 0; i < blobs; i++)
            {
                // The first blob is the body of the splat, the rest cling to it.
                float spread = i == 0 ? 0.08f : 0.26f;
                centres[i] = new Vector2(0.5f, 0.5f) +
                             Random.insideUnitCircle * spread;
                radii[i] = i == 0 ? Random.Range(0.26f, 0.34f) : Random.Range(0.08f, 0.2f);
            }

            int droplets = Random.Range(6, 12);
            var dropCentres = new Vector2[droplets];
            var dropRadii = new float[droplets];
            for (int i = 0; i < droplets; i++)
            {
                dropCentres[i] = new Vector2(0.5f, 0.5f) + Random.insideUnitCircle * 0.45f;
                dropRadii[i] = Random.Range(0.012f, 0.035f);
            }

            float noiseOffset = Random.Range(0f, 100f);

            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    var uv = new Vector2((x + 0.5f) / cell, (y + 0.5f) / cell);

                    float coverage = 0f;
                    for (int i = 0; i < blobs; i++)
                    {
                        coverage = Mathf.Max(coverage, Blob(uv, centres[i], radii[i], noiseOffset));
                    }

                    for (int i = 0; i < droplets; i++)
                    {
                        coverage = Mathf.Max(coverage, Blob(uv, dropCentres[i], dropRadii[i], noiseOffset));
                    }

                    // Darker in the middle, thinner at the edge: a flat red disc reads as a sticker.
                    float shade = Mathf.Lerp(0.55f, 1f, coverage);
                    var colour = new Color(0.33f * shade, 0.025f * shade, 0.02f * shade,
                        Mathf.Clamp01(coverage));

                    pixels[(originY + y) * Size + originX + x] = colour;
                }
            }
        }

        /// <summary>Soft disc with a noise-perturbed edge; 0 outside, 1 at the centre.</summary>
        private static float Blob(in Vector2 uv, in Vector2 centre, float radius, float noiseOffset)
        {
            float distance = Vector2.Distance(uv, centre);
            float wobble = (Mathf.PerlinNoise(uv.x * 9f + noiseOffset, uv.y * 9f + noiseOffset) - 0.5f) *
                           radius * 0.7f;
            float edge = radius + wobble;
            if (distance >= edge)
            {
                return 0f;
            }

            return Mathf.SmoothStep(0f, 1f, 1f - distance / Mathf.Max(0.0001f, edge));
        }
    }
}
