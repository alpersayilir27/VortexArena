using TMPro;
using UnityEngine;

namespace VortexArena.App
{
    /// <summary>
    /// <c>identify</c> komutunda göz hizasında beliren kimlik kartının görünümü —
    /// <b>prefab</b> (<c>_Shared/App/Resources/UI/IdentifyDisplay.prefab</c>).
    /// <para>
    /// Ayrı bir bileşendir çünkü <see cref="IdentifyOverlay"/> kalıcı bir dinleyicidir
    /// (DontDestroyOnLoad) ama bu kart <b>geçicidir</b>: her identify'da örneklenir, birkaç
    /// saniye sonra yok edilir. İkisi tek prefabta olsaydı dinleyici de kartla birlikte ölürdü.
    /// </para>
    /// Yazı içeriğini <see cref="IdentifyOverlay"/> yazar; punto, renk, boyut ve mesafe hissi
    /// prefabta düzenlenir.
    /// </summary>
    public class IdentifyDisplay : MonoBehaviour
    {
        [Tooltip("Sönme için kullanılır (son saniyede alfa düşer).")]
        [SerializeField] private CanvasGroup group;

        [Tooltip("Kimlik yazısı — içeriği koddan gelir, biçimi buradan.")]
        [SerializeField] private TMP_Text label;

        public CanvasGroup Group => group;

        public void SetText(string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
