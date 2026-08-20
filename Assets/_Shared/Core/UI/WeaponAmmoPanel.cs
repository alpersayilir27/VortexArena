using TMPro;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Silahın ÜSTÜNDEKİ cephane paneli: şarjördeki mermi + yedek şarjör sayısı, silahın kendi
    /// dünya-uzayı canvas'ında (<c>AmmoCanvas</c>).
    /// <para>
    /// <b>Bileşen canvas'ın kendi kökünde durur</b>, <c>WPN_*</c> kökünde değil: <c>AmmoCanvas</c>
    /// TEK bir prefab olarak bütün silahlara iç içe geçmiş örnek olarak giriyor, yani metin bağları
    /// bir kez orada kurulur ve her silah onu hazır alır. Kökte olsaydı bağlar silah başına
    /// kurulurdu (ya da <c>WeaponKitBuilder</c>'a bir adım daha eklenirdi) ve yeni bir silahta
    /// sessizce boş kalırdı. Silahını <see cref="Component.GetComponentInParent{T}(bool)"/> ile
    /// bulur — panelin nereye asıldığını bilmesi gerekmez.
    /// </para>
    /// <para>
    /// ⚠️ <b>Görünüm PREFABTAN gelir</b> (punto, konum, hizalama, ayraç, ikonlar, RENK): bu sınıf
    /// yalnız iki metnin içeriğini yazar ve düşük mermide vurgu rengini sürer. Normal renk bile
    /// koda gömülü DEĞİL, <see cref="Awake"/>'te prefabtan okunur — gömülü olsaydı panelin rengini
    /// Inspector'dan değiştirmek ilk atışta sessizce geri alınırdı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ayraç ("/") ve ikonlar bu sınıfın işi DEĞİLDİR</b> ve kodda karşılıkları yoktur —
    /// sabit metindirler, yeri prefabtır.
    /// </para>
    /// <para>
    /// YALNIZ olaylarla yenilenir (<see cref="Weapon.AmmoChanged"/> ve reload/tutulma olayları),
    /// kare başına iş yapmaz. Metinler <see cref="TMP_Text.SetText(string, float)"/> ile yazılır:
    /// atış başına string üretmez.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class WeaponAmmoPanel : MonoBehaviour
    {
        /// <summary>Bu değer ve altında mermi sayısı vurgu rengine döner.</summary>
        private const int LowAmmoThreshold = 5;

        /// <summary>Düşük mermi/reload vurgusu.</summary>
        private static readonly Color LowAmmoColor = new Color(1f, 0.32f, 0.26f);

        [Tooltip("Şarjördeki mermi. Konumu/puntosu/rengi prefabta ayarlanır.")]
        [SerializeField] private TMP_Text ammoText;

        [Tooltip("Yedek şarjör sayısı (fişek havuzlu silahta kalan fişek). Konumu/puntosu " +
                 "prefabta ayarlanır.")]
        [SerializeField] private TMP_Text magText;

        private Weapon _weapon;
        private Color _normalColor = Color.white;

        private void Awake()
        {
            // ⚠️ includeInactive: panel silahın modeli kapalıyken de (çerçeve klonu gizliyken)
            // silahını bulabilmeli — bulamazsa bir daha hiç aramaz ve panel ölü kalırdı.
            _weapon = GetComponentInParent<Weapon>(true);

            if (ammoText != null)
            {
                _normalColor = ammoText.color;
            }
        }

        private void OnEnable()
        {
            if (_weapon == null)
            {
                return;
            }

            _weapon.AmmoChanged += Refresh;
            _weapon.ReloadStarted += HandleReloadStarted;
            _weapon.ReloadCompleted += Refresh;
            _weapon.HeldChanged += HandleHeldChanged;

            Refresh();
        }

        private void OnDisable()
        {
            if (_weapon == null)
            {
                return;
            }

            _weapon.AmmoChanged -= Refresh;
            _weapon.ReloadStarted -= HandleReloadStarted;
            _weapon.ReloadCompleted -= Refresh;
            _weapon.HeldChanged -= HandleHeldChanged;
        }

        // Reload olayı süre taşıyor, tutulma olayı bayrak: ikisi de yalnız "tazele" demek.
        private void HandleReloadStarted(float duration) => Refresh();

        private void HandleHeldChanged(bool held) => Refresh();

        private void Refresh()
        {
            if (_weapon == null)
            {
                return;
            }

            WriteAmmo();
            WriteReserve();
        }

        /// <summary>Şarjördeki mermi; reload sürerken sayı yerine bekleme işareti.</summary>
        private void WriteAmmo()
        {
            if (ammoText == null)
            {
                return;
            }

            if (_weapon.IsReloading)
            {
                ammoText.color = LowAmmoColor;
                ammoText.SetText("···");
                return;
            }

            ammoText.color = _weapon.CurrentAmmo <= LowAmmoThreshold ? LowAmmoColor : _normalColor;
            ammoText.SetText("{0}", _weapon.CurrentAmmo);
        }

        /// <summary>
        /// Yedek gösterimi silahın rezerv kipine göre değişir: normal silahta kalan ŞARJÖR sayısı,
        /// tek tek fişek dolduran silahta (<see cref="WeaponReserveMode.PoolRounds"/>) kalan FİŞEK
        /// sayısı — havuzlu silahta şarjöre bölmek "0 yedek" derken elde 6 fişek olmasına yol açardı.
        /// </summary>
        private void WriteReserve()
        {
            if (magText == null)
            {
                return;
            }

            WeaponDefinition definition = _weapon.Definition;
            bool pooled = definition != null && definition.ReserveMode == WeaponReserveMode.PoolRounds;

            magText.SetText("{0}", pooled ? _weapon.ReserveRounds : _weapon.SpareMagazineCount);
        }
    }
}
