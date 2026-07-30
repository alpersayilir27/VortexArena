using System;

namespace VortexArena.Protocol
{
    /// <summary>
    /// Birim vektör ↔ 2×i16 sıkıştırma (klasik oktahedral eşleme, 4 B).
    /// <para><b>Neden 3×f32 değil:</b> atış olayı (§6.4) tel üzerinde 12 B'de tutuluyor; ham yön
    /// tek başına 12 B alırdı. Oktahedral eşleme aynı bilgiyi <b>4 B</b>'ye indirir ve hata bütçesi
    /// ~0.01° kalır — 60 m mesafede 1 cm'den iyi sapma, yani nişanın gözle görülür bir kaybı yok.
    /// Yön <b>birim vektördür</b>: uzunluk taşımaz (mesafe/hız ayrı alan, <c>magnitude</c>).</para>
    /// <para>Saf C#: <see cref="System.Math"/> kullanılır (bu klasörde UnityEngine YASAK).</para>
    /// </summary>
    public static class OctahedralDirection
    {
        /// <summary>i16 tam ölçek; ±1 aralığı buna eşlenir.</summary>
        private const float SCALE = 32767f;

        /// <summary>
        /// Kat işlemlerinde kullanılan işaret. ⚠️ <see cref="Math.Sign(float)"/> KULLANILMAZ:
        /// 0'da 0 döner ve kat çarpımını sıfırlar → eksen üstündeki yönler (ör. (0,0,-1)) bozulur.
        /// 0 bilinçli olarak <b>pozitif</b> sayılır; kodlama ve kod çözme aynı sözleşmeyi paylaştığı
        /// için round-trip birebir kapanır.
        /// </summary>
        private static float SignOrPositive(float v) => v >= 0f ? 1f : -1f;

        /// <summary>
        /// Birim vektörü 2×i16'ya sıkıştırır. Girdi birim olmak zorunda değildir (burada birimlenir);
        /// sıfır vektörde (0,0,1) varsayılanına düşülür — telde "yön yok" diye bir değer yoktur.
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

            // L1 normalize: oktahedronun yüzeyine izdüşüm.
            float l1 = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
            float px = x / l1;
            float py = y / l1;
            float pz = z / l1;

            if (pz < 0f)
            {
                // Alt yarıküreyi üst yarıkürenin dışına katla (kare düzleme yayılır).
                float fx = (1f - Math.Abs(py)) * SignOrPositive(px);
                float fy = (1f - Math.Abs(px)) * SignOrPositive(py);
                px = fx; py = fy;
            }

            ox = Quantize(px);
            oy = Quantize(py);
        }

        /// <summary>2×i16'yı birim vektöre açar (<see cref="Encode"/>'un tersi).</summary>
        public static void Decode(short ox, short oy, out float x, out float y, out float z)
        {
            float px = ox / SCALE;
            float py = oy / SCALE;

            float ex = px;
            float ey = py;
            float ez = 1f - Math.Abs(px) - Math.Abs(py);

            if (ez < 0f)
            {
                // Katı geri al — ex/ey'yi ezmeden ÖNCE ikisini de orijinalinden hesapla.
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
