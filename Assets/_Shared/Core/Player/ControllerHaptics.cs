using System.Collections;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Kısa kumanda titreşim kalıpları (XR politikası: <c>OVRInput.SetControllerVibration</c>,
    /// ayrı haptics paketi yok). OVRInput titreşimi "aç/kapa" modelidir — süre parametresi yok;
    /// kalıp bu yüzden bir coroutine ister ve statik sınıf onu ÇAĞIRANIN üstünde koşturur:
    /// ayrı bir tekil GameObject açmaya değmez, çağıran zaten sahnede yaşayan bir bileşendir.
    /// <para>
    /// ⚠️ Silah geri tepmesi gibi SÜREKLİ titreşim kaynaklarıyla yarışmaz: OVRInput el başına tek
    /// kanaldır, aynı ele aynı anda iki kaynak yazarsa son yazan kazanır. Kalıbı ateşin kapalı
    /// olduğu bağlamlarda çağır (toplanma, tur içi ölüm — <c>CanFire</c> zaten kapalı).
    /// </para>
    /// </summary>
    public static class ControllerHaptics
    {
        private const float PulseSeconds = 0.12f;
        private const float GapSeconds = 0.08f;
        private const float Amplitude = 0.8f;

        /// <summary>
        /// İki kumandaya birden <paramref name="pulses"/> kısa darbe. Coroutine
        /// <paramref name="host"/> üstünde koşar; host yok/kapalıysa sessizce hiçbir şey yapmaz —
        /// titreşim kritik bir bildirim değil, geri bildirimdir.
        /// </summary>
        public static void PulseBoth(MonoBehaviour host, int pulses = 3)
        {
            if (host == null || !host.isActiveAndEnabled)
            {
                return;
            }

            host.StartCoroutine(PulseRoutine(pulses));
        }

        private static IEnumerator PulseRoutine(int pulses)
        {
            for (int i = 0; i < pulses; i++)
            {
                SetBoth(1f, Amplitude);
                yield return new WaitForSecondsRealtime(PulseSeconds);
                SetBoth(0f, 0f);

                if (i < pulses - 1)
                {
                    yield return new WaitForSecondsRealtime(GapSeconds);
                }
            }
        }

        private static void SetBoth(float frequency, float amplitude)
        {
            OVRInput.SetControllerVibration(frequency, amplitude, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(frequency, amplitude, OVRInput.Controller.RTouch);
        }
    }
}
