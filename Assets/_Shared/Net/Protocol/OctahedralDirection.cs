using System;

namespace VortexArena.Protocol
{
    /// <summary>
    /// Unit vector ↔ 2×i16 compression (classic octahedral mapping, 4 B).
    /// <para><b>Why not 3×f32:</b> the shot event (§6.4) is 12 B on the wire and the raw direction
    /// alone would eat all of it. Octahedral mapping fits the same information in <b>4 B</b> at
    /// ~0.01° error — under 1 cm deviation at 60 m, no visible loss of aim. A <b>unit vector</b>:
    /// length lives in the separate <c>magnitude</c> field.</para>
    /// <para>Pure C#: <see cref="System.Math"/> (UnityEngine is FORBIDDEN in this folder).</para>
    /// </summary>
    public static class OctahedralDirection
    {
        /// <summary>i16 full scale; the ±1 range maps onto it.</summary>
        private const float SCALE = 32767f;

        /// <summary>
        /// Sign used in the fold. ⚠️ NOT <see cref="Math.Sign(float)"/>: it returns 0 at 0 and zeroes
        /// the fold product → axis directions (e.g. (0,0,-1)) break. 0 counts as <b>positive</b>;
        /// encode and decode share the convention, so the round-trip closes exactly.
        /// </summary>
        private static float SignOrPositive(float v) => v >= 0f ? 1f : -1f;

        /// <summary>
        /// Compresses a unit vector into 2×i16. The input need not be normalised (done here); a zero
        /// vector falls back to (0,0,1) — the wire has no "no direction" value.
        /// </summary>
        public static void Encode(float x, float y, float z, out short ox, out short oy)
        {
            double len = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
            if (len < 1e-6)
            {
                x = 0f; y = 0f; z = 1f;
            }
            else
            {
                x = (float)(x / len); y = (float)(y / len); z = (float)(z / len);
            }

            // L1 normalize: projection onto the surface of the octahedron.
            float l1 = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
            float px = x / l1;
            float py = y / l1;
            float pz = z / l1;

            if (pz < 0f)
            {
                // Fold the lower hemisphere outside the upper one (it spreads over the square plane).
                float fx = (1f - Math.Abs(py)) * SignOrPositive(px);
                float fy = (1f - Math.Abs(px)) * SignOrPositive(py);
                px = fx; py = fy;
            }

            ox = Quantize(px);
            oy = Quantize(py);
        }

        /// <summary>Expands 2×i16 back into a unit vector (the inverse of <see cref="Encode"/>).</summary>
        public static void Decode(short ox, short oy, out float x, out float y, out float z)
        {
            float px = ox / SCALE;
            float py = oy / SCALE;

            float ex = px;
            float ey = py;
            float ez = 1f - Math.Abs(px) - Math.Abs(py);

            if (ez < 0f)
            {
                // Undo the fold — compute both from the originals BEFORE overwriting ex/ey.
                float fx = (1f - Math.Abs(py)) * SignOrPositive(px);
                float fy = (1f - Math.Abs(px)) * SignOrPositive(py);
                ex = fx; ey = fy;
            }

            double len = Math.Sqrt((double)ex * ex + (double)ey * ey + (double)ez * ez);
            if (len < 1e-6)
            {
                x = 0f; y = 0f; z = 1f;
                return;
            }

            x = (float)(ex / len);
            y = (float)(ey / len);
            z = (float)(ez / len);
        }

        private static short Quantize(float p)
        {
            if (p < -1f) p = -1f;
            else if (p > 1f) p = 1f;
            return (short)Math.Round(p * SCALE, MidpointRounding.AwayFromZero);
        }
    }
}
