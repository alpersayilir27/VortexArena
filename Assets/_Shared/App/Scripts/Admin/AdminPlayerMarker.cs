using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// <b>Ground ring + name label</b> drawn per player by the admin spectator —
    /// <b>prefab</b> (<c>_Shared/App/Resources/UI/AdminPlayerMarker.prefab</c>).
    /// <para>
    /// World-space canvases. Positioning/coloring stay in <see cref="AdminPlayerMarkers"/> (pose
    /// dependent); this component is only the handle for the <b>visuals</b>, which are edited in
    /// the prefab.
    /// </para>
    /// </summary>
    public class AdminPlayerMarker : MonoBehaviour
    {
        /// <summary>The prefab's path inside <c>Resources</c> (without extension).</summary>
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
        /// Applies the selection visual. ⚠️ Ring sprites are NOT generated at runtime — both
        /// variants come from the prefab, else the artist's visual is overwritten on selection.
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
