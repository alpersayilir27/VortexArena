#if UNITY_EDITOR
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kavrama kalibrasyonunun gözlükteki yazıları: geri sayım, hangi aşamada olunduğu ve yönerge.
    /// <para>
    /// <b>Kodda görsel kurulum YOKTUR</b> (projede bağlayıcı kural): üç <see cref="TextMesh"/> de
    /// sahnede <c>CenterEyeAnchor</c>'ın altında kurulur — yeri, boyu, rengi, hizası orada
    /// ayarlanır. Bu bileşen yalnız <b>veri yazar</b>; yazının nasıl göründüğüne dair hiçbir şeyi
    /// çalışma anında belirlemez.
    /// </para>
    /// <para>
    /// ⚠️ Boş referans <b>sessiz no-op</b>'tur: aracın kendisi HUD'suz da koşabilmeli. Kalibrasyon
    /// sahnesi eksik kurulmuşsa ölçüm yine alınır ve sonuç konsola düşer — HUD bir kolaylıktır,
    /// bir ön koşul değil.
    /// </para>
    /// </summary>
    public class WeaponGripCalibrationHud : MonoBehaviour
    {
        [Tooltip("Tepedeki BÜYÜK yazı: geri sayım rakamı / \"PINCH\" / onay işareti.")]
        [SerializeField] private TextMesh countdownText;

        [Tooltip("Hangi aşamadayız — ör. \"1/4 · ANA KABZA · SAĞ EL\".")]
        [SerializeField] private TextMesh stepText;

        [Tooltip("Yönerge ya da hata satırı (el izlenmiyor, bilek okunamadı…).")]
        [SerializeField] private TextMesh hintText;

        /// <summary>Geri sayım satırını yazar. Renk aşamanın anlamını taşır (bekleme/sayım/onay/hata).</summary>
        public void SetCountdown(string text, Color color)
        {
            if (countdownText == null)
            {
                return;
            }

            countdownText.text = text;
            countdownText.color = color;
        }

        /// <summary>Aşama satırını yazar.</summary>
        public void SetStep(string text)
        {
            if (stepText == null)
            {
                return;
            }

            stepText.text = text;
        }

        /// <summary>Yönerge/hata satırını yazar.</summary>
        public void SetHint(string text)
        {
            if (hintText == null)
            {
                return;
            }

            hintText.text = text;
        }
    }
}
#endif
