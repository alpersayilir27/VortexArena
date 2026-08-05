using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// HMD karartma quad'ının <b>hakemi</b>: birden çok kaynak kendi karartmasını bildirir, en
    /// yüksek alfayı isteyen kazanır ve <b>tek</b> çizim yapılır.
    /// <para>
    /// <b>Neden var:</b> karartma quad'ı tektir ama onu isteyen birden fazla sistem vardır (arena
    /// sınırına yaklaşma · engel ihlali). İkisi de <c>MaterialPropertyBlock</c>'a doğrudan yazsaydı
    /// kare başına birbirini ezerlerdi — belirtisi "sınıra yaklaşırken engele girince ekran
    /// titriyor" olurdu ve sebebi iki ayrı bileşene dağılmış olurdu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bu sınıf hiçbir şey ÇİZMEZ.</b> Renderer'ın sahibi <see cref="VortexArena.Core.Arena.ArenaBoundary"/>'dir
    /// (quad onun serialize alanı, rig prefabında bağlı) ve kazananı o uygular. Buraya çizim
    /// eklemek ikinci bir renderer sahibi üretirdi.
    /// </para>
    /// <para>
    /// <b>Kalp atışı sözleşmesi:</b> kaynak karartmasını <b>her karede</b> bildirir; bildirmeyi
    /// bırakan kaynak <see cref="EntryTimeoutSeconds"/> sonra kendiliğinden düşer. "Kapat" demeyi
    /// unutmak mümkün değildir — kapatmayı unutan bir kaynak ekranı kalıcı siyah bırakırdı.
    /// (Aynı desen <c>PlayerCombatState.RequestBaseTracking</c>'de de kullanılıyor.)
    /// </para>
    /// </summary>
    public static class ScreenFade
    {
        /// <summary>Bildirim yaşı bunu aşarsa kaynak susmuş sayılır (sn).</summary>
        private const float EntryTimeoutSeconds = 0.25f;

        private struct Entry
        {
            public float Alpha;
            public Color Color;
            public float Time;
        }

        // Kaynak sayısı tek haneli; sözlük kurulum kolaylığı için (kaynaklar birbirini tanımıyor).
        private static readonly Dictionary<string, Entry> Sources = new Dictionary<string, Entry>();

        /// <summary>
        /// Bir kaynağın o karedeki karartma isteği. <paramref name="alpha"/> <c>0</c> ise kaynak
        /// karartma istemiyor demektir (bildirmemekle aynı sonuç, ama açık).
        /// </summary>
        /// <param name="sourceId">Kaynağın sabit kimliği (ör. "boundary", "obstacle").</param>
        /// <param name="alpha">0..1 karartma yoğunluğu.</param>
        /// <param name="color">Karartmanın RGB'si — alfa alanı YOK SAYILIR.</param>
        public static void Report(string sourceId, float alpha, Color color)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            Sources[sourceId] = new Entry
            {
                Alpha = Mathf.Clamp01(alpha),
                Color = color,
                // ⚠️ unscaledTime: karartma bir SUNUM katmanıdır ve maç duraklatılınca da
                // (Time.timeScale ile oynanırsa) tazeliğini korumalıdır.
                Time = UnityEngine.Time.unscaledTime
            };
        }

        /// <summary>
        /// Kazanan karartmayı verir: <b>en yüksek alfayı</b> isteyen kaynak. <c>false</c> dönerse
        /// hiçbir taze kaynak karartma istemiyor demektir (çizen taraf quad'ı kapatır).
        /// <para>Karışım (alfa toplama/çarpma) bilinçli olarak YOK: iki yarı saydam katman
        /// üst üste binince sonuç ikisinden de koyu olur ve "neden bu kadar karardı" sorusunun
        /// cevabı hiçbir kaynakta bulunmaz. En yükseği almak her zaman bir kaynağın istediği
        /// değerdir.</para>
        /// </summary>
        public static bool Resolve(out float alpha, out Color color)
        {
            alpha = 0f;
            color = Color.black;

            float now = UnityEngine.Time.unscaledTime;
            bool found = false;

            foreach (KeyValuePair<string, Entry> kv in Sources)
            {
                Entry entry = kv.Value;
                if (now - entry.Time > EntryTimeoutSeconds)
                {
                    continue; // susmuş kaynak
                }

                if (entry.Alpha <= alpha)
                {
                    continue;
                }

                alpha = entry.Alpha;
                color = entry.Color;
                found = true;
            }

            return found && alpha > 0.001f;
        }
    }
}
