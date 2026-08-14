using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Kumanda titreşiminin <b>hakemi</b>: birden çok kaynak kendi titreşimini bildirir, en yüksek
    /// genliği isteyen kazanır ve <b>tek</b> uygulama yapılır.
    /// <para>
    /// <b>Neden var:</b> titreşim motoru tektir ama onu isteyen birden fazla sistem vardır (engel
    /// ihlali · arena sınırının dışına çıkma) ve ikisi aynı anda doğru olabilir — muhafaza,
    /// sahnedeki <c>ArenaObstacle</c>'ları da "alan dışı" sayıyor. İkisi de
    /// <see cref="OVRInput.SetControllerVibration"/>'ı doğrudan çağırsaydı biri sustuğu anda
    /// ötekinin titreşimini kapatırdı; belirtisi "duvarda dururken titreşim kesik kesik geliyor"
    /// olur ve sebebi iki ayrı bileşene dağılmış olurdu. <see cref="ScreenFade"/> aynı sorunu aynı
    /// biçimde çözüyor — bu sınıf onun titreşim ikizidir.
    /// </para>
    /// <para>
    /// <b>Kalp atışı sözleşmesi:</b> kaynak titreşimini <b>her karede</b> bildirir; bildirmeyi
    /// bırakan kaynak <see cref="EntryTimeoutSeconds"/> sonra kendiliğinden düşer. "Kapat" demeyi
    /// unutmak mümkün değildir — kapatmayı unutan bir kaynak kumandayı sonsuza kadar titretirdi.
    /// </para>
    /// <para>
    /// ⚠️ <b>Hakem yalnız <see cref="Report"/> içinde yeniden hesaplar</b>, kendi döngüsü YOKTUR
    /// (bir GameObject önyüklemek için sebep değil). Yani en az bir kaynağın her karede
    /// bildiriyor olması gerekir ve bu garanti edilmiştir: <see cref="ObstacleViolationProbe"/>
    /// kendini önyükleyen kalıcı bir tekildir ve koşulsuz bildirir. Susan bir hakem son yazdığı
    /// titreşimi açık bırakırdı.
    /// </para>
    /// </summary>
    public static class ControllerHaptics
    {
        /// <summary>Bildirim yaşı bunu aşarsa kaynak susmuş sayılır (sn).</summary>
        private const float EntryTimeoutSeconds = 0.25f;

        /// <summary>Nabzın frekansı (Hz). ⚠️ Sürekli titreşim uyarı olmaktan çıkar, bu yüzden
        /// nabızdır; kaynaklar aynı sayıyı kullanır ki üst üste binince faz kaymasın.</summary>
        private const float PulseHz = 2f;

        /// <summary>Nabzın genliği (0..1).</summary>
        private const float PulseAmplitude = 0.5f;

        /// <summary>Titreşimin frekans parametresi — cihaz ayarı, kaynağın kararı değil.</summary>
        private const float VibrationFrequency = 0.6f;

        private struct Entry
        {
            public float Amplitude;
            public float Time;
        }

        // Kaynak sayısı tek haneli; sözlük kurulum kolaylığı için (kaynaklar birbirini tanımıyor).
        private static readonly Dictionary<string, Entry> Sources = new Dictionary<string, Entry>();

        /// <summary>En son uygulanan genlik — aynı değeri tekrar tekrar yazmamak için.</summary>
        private static float _applied;

        /// <summary>
        /// <b>Nabız</b> bildirimi: <paramref name="active"/> true iken kaynak
        /// <see cref="PulseHz"/> frekansında titreşim ister. Faz <see cref="Time.unscaledTime"/>'dan
        /// türediği için tüm kaynaklarda aynıdır.
        /// </summary>
        /// <param name="sourceId">Kaynağın sabit kimliği (ör. "obstacle", "boundary").</param>
        /// <param name="active">Kaynak şu an titreşim istiyor mu.</param>
        public static void ReportPulse(string sourceId, bool active)
        {
            bool on = active && Mathf.Repeat(Time.unscaledTime * PulseHz, 1f) < 0.5f;
            Report(sourceId, on ? PulseAmplitude : 0f);
        }

        /// <summary>
        /// Bir kaynağın o karedeki titreşim isteği. <paramref name="amplitude"/> <c>0</c> ise kaynak
        /// titreşim istemiyor demektir (bildirmemekle aynı sonuç, ama açık).
        /// </summary>
        /// <param name="sourceId">Kaynağın sabit kimliği.</param>
        /// <param name="amplitude">0..1 titreşim genliği.</param>
        public static void Report(string sourceId, float amplitude)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            Sources[sourceId] = new Entry
            {
                Amplitude = Mathf.Clamp01(amplitude),
                // ⚠️ unscaledTime: titreşim bir SUNUM katmanıdır ve Time.timeScale ile oynanırsa da
                // tazeliğini korumalıdır (ScreenFade'deki gerekçenin aynısı).
                Time = UnityEngine.Time.unscaledTime
            };

            Apply(Resolve());
        }

        /// <summary>
        /// Kazanan genlik: <b>en yüksek</b> genliği isteyen taze kaynak. Karışım (toplama/çarpma)
        /// bilinçli olarak YOK — iki kaynağın genliği toplanınca sonuç ikisinden de sert olur ve
        /// "neden bu kadar titriyor" sorusunun cevabı hiçbir kaynakta bulunmaz.
        /// </summary>
        private static float Resolve()
        {
            float now = UnityEngine.Time.unscaledTime;
            float amplitude = 0f;

            foreach (KeyValuePair<string, Entry> kv in Sources)
            {
                Entry entry = kv.Value;
                if (now - entry.Time > EntryTimeoutSeconds)
                {
                    continue; // susmuş kaynak
                }

                if (entry.Amplitude > amplitude)
                {
                    amplitude = entry.Amplitude;
                }
            }

            return amplitude;
        }

        private static void Apply(float amplitude)
        {
            if (Mathf.Approximately(amplitude, _applied))
            {
                return;
            }

            _applied = amplitude;
            OVRInput.SetControllerVibration(VibrationFrequency, amplitude, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(VibrationFrequency, amplitude, OVRInput.Controller.RTouch);
        }
    }
}
