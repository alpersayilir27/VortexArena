using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Admin gözlemcinin oyuncu başına çizdiği <b>zemin halkası + ad etiketi</b> —
    /// <b>prefab</b> (<c>_Shared/App/Resources/UI/AdminPlayerMarker.prefab</c>).
    /// <para>
    /// Dünya-uzayı canvas'larıdır: halka zemine yatar, etiket her karede kameraya döner.
    /// Konumlandırma ve renklendirme <see cref="AdminPlayerMarkers"/>'ta kalır (oyuncu pozuna
    /// bağlı); bu bileşen yalnız <b>görünümün</b> tutamağıdır — halka kalınlığı, etiket puntosu,
    /// boyutlar prefabta düzenlenir.
    /// </para>
    /// </summary>
    public class AdminPlayerMarker : MonoBehaviour
    {
        /// <summary>Prefabın <c>Resources</c> içindeki yolu (uzantısız).</summary>
        public const string ResourcePath = "UI/AdminPlayerMarker";

        [Tooltip("Zemine yatan halka canvas'ı — konumu her karede koddan sürülür.")]
        [SerializeField] private Transform ring;

        [Tooltip("Halkanın görseli; rengi takım/durum'a göre koddan sürülür.")]
        [SerializeField] private Image ringImage;

        [Tooltip("Ad etiketi canvas'ı — kameraya döndürülür.")]
        [SerializeField] private Transform label;

        [SerializeField] private TextMeshProUGUI labelText;

        [Header("Halka görselleri")]
        [Tooltip("Normal (seçili olmayan) oyuncunun halkası.")]
        [SerializeField] private Sprite ringNormal;

        [Tooltip("Seçili oyuncunun halkası. Daha KALIN bir halka koymak seçimi belirginleştirir; " +
                 "aynı sprite bırakılırsa seçim yalnız boyut artışıyla anlatılır.")]
        [SerializeField] private Sprite ringSelected;

        public Transform Ring => ring;
        public Image RingImage => ringImage;
        public Transform Label => label;
        public TextMeshProUGUI LabelText => labelText;

        /// <summary>
        /// Seçim görselini uygular. ⚠️ Halka sprite'ı çalışırken ÜRETİLMEZ — iki varyant da
        /// prefabtan gelir; üretilseydi sanatçının seçtiği görsel seçim anında ezilirdi.
        /// </summary>
        public void SetSelected(bool selected)
        {
            if (ringImage == null)
            {
                return;
            }

            Sprite wanted = selected ? ringSelected : ringNormal;
            if (wanted != null && ringImage.sprite != wanted)
            {
                ringImage.sprite = wanted;
            }
        }
    }
}
