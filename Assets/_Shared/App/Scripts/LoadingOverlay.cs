using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.UI;

namespace VortexArena.App
{
    /// <summary>
    /// Sahne geçişi sırasında görünen yükleme ekranı. <see cref="SceneRouter"/> asenkron
    /// yüklemeyi başlatırken açar, sahne tamamen ayağa kalkınca kapatır; ilerleme
    /// <c>AsyncOperation.progress</c>'ten gelir.
    ///
    /// **Görünüm prefabtan gelir, SAHNEDEN değil:** iki varyant vardır —
    /// `Resources/UI/LoadingOverlayScreen` (masaüstü) ve `…World` (VR); hangisinin
    /// yükleneceğine <see cref="Bootstrap"/> karar verir. <see cref="ConnectionOverlay"/> ile
    /// birebir aynı desen: prefab sahneye KONMAZ (konsaydı yeni arena eklerken unutulacak bir
    /// adım olurdu), `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` ile kendini önyükler,
    /// prefabı `Resources.Load` ile alır ve `DontDestroyOnLoad` tekil olarak yaşar.
    /// Bu sınıf yalnız **veri yazar ve görünürlüğü sürer**; yerleşim/renk/punto prefabta.
    ///
    /// **Neden açılışta önyüklenir, ilk gösterimde değil:** prefabı tam geçiş anında
    /// `Resources.Load` etmek yüklemenin en kötü karesinde bir takılma üretirdi — bedeli
    /// açılışta bir kez ödenir.
    ///
    /// **VR güvenlik kuralı** (`ConnectionOverlay` ile aynı gerekçe — oyuncu fiziksel alanda
    /// 1:1 yürüyor): world-space varyantta tam ekran scrim YOKTUR, yalnız yarı saydam kart
    /// çizilir. Görüşü karartmak free-roam'da tehlikelidir.
    /// </summary>
    public class LoadingOverlay : MonoBehaviour
    {
        // --------------------------------------------------------------- ayarlar

        /// <summary>Bar dolumunun saniyede kat ettiği oran — sıçramasın, aksın.</summary>
        private const float FillSpeed = 2.5f;

        /// <summary>Nabız periyodu (sn) — accent şerit alfası 0.55 ↔ 1.0.</summary>
        private const float PulsePeriod = 1.2f;

        // --------------------------------------------------------------- durum

        private static LoadingOverlay _instance;

        /// <summary>Prefabın <c>Resources</c> yolları (uzantısız) — VR world-space / masaüstü
        /// screen-space iki ayrı prefabtır.</summary>
        public const string WorldResourcePath = "UI/LoadingOverlayWorld";

        public const string ScreenResourcePath = "UI/LoadingOverlayScreen";

        // ⚠️ Alanlar [SerializeField] — görünüm PREFABTAN gelir. Başlık ve ipucu metinleri de
        // prefabta sabittir (kod onlara dokunmaz), bu yüzden burada alanları yoktur.

        [Tooltip("Bu prefab VR (world-space) varyantı mı? Screen-space varyantta KAPALI olmalı.")]
        [SerializeField] private bool _worldSpace;

        [Header("Kök")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Yalnız world-space varyantta dolu — kartı tembel takiple kameranın önüne taşır.")]
        [SerializeField] private HudFollow _hudFollow;

        [Header("Kart")]
        [SerializeField] private Image _accentStrip;
        [Tooltip("Yüklenen sahnenin adı.")]
        [SerializeField] private TextMeshProUGUI _sceneText;
        [Tooltip("Yüzde göstergesi.")]
        [SerializeField] private TextMeshProUGUI _progressText;
        [Tooltip("İlerleme barının DOLGUSU (UiKit.Bar deseni: anchorMax.x ile sürülür).")]
        [SerializeField] private Image _progressFill;

        private bool _visible;

        /// <summary>Hedef ilerleme (0..1) — <see cref="SetProgress"/> yazar.</summary>
        private float _target;

        /// <summary>Ekranda çizili ilerleme; hedefe yumuşayarak yaklaşır.</summary>
        private float _displayed;

        /// <summary>Ekranda yazılı yüzde — değişmedikçe TMP'ye dokunulmaz (çöp üretmesin).</summary>
        private int _shownPercent = -1;

        // ------------------------------------------------------------ önyükleme

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            // Quest'te (ya da XR aygıtı etkinken) world-space kart, masaüstünde screen-space.
            bool worldSpace = UnityEngine.XR.XRSettings.isDeviceActive ||
                              Application.platform == RuntimePlatform.Android;
            string path = worldSpace ? WorldResourcePath : ScreenResourcePath;

            var prefab = Resources.Load<LoadingOverlay>(path);
            if (prefab == null)
            {
                Debug.LogError($"[LoadingOverlay] '{path}' prefabı bulunamadı — yükleme ekranı " +
                               "çizilemeyecek. (Prefab Resources/UI altından ÇIKARILMAMALIDIR.)");
                return;
            }

            LoadingOverlay overlay = Instantiate(prefab);
            overlay.name = "[LoadingOverlay]";
            DontDestroyOnLoad(overlay.gameObject);
            _instance = overlay;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            ApplyVisible(false);
        }

        // ------------------------------------------------------------ dış yüzey

        /// <summary>
        /// Yükleme ekranını açar ve ilerlemeyi sıfırlar. Prefab bulunamadıysa sessizce hiçbir
        /// şey yapmaz — yükleme ekranı olmaması sahne geçişini ENGELLEMEZ.
        /// </summary>
        public static void Show(string sceneName)
        {
            Bootstrap(); // erken çağrılırsa (ilk sahne yüklemesinden önce) yine de doğsun
            if (_instance == null)
            {
                return;
            }

            _instance.ShowInstance(sceneName);
        }

        /// <summary>İlerleme (0..1). Bar ve yüzde göstergesi bunu izler.</summary>
        public static void SetProgress(float normalized)
        {
            if (_instance == null)
            {
                return;
            }

            _instance._target = Mathf.Clamp01(normalized);
        }

        public static void Hide()
        {
            if (_instance == null)
            {
                return;
            }

            _instance.ApplyVisible(false);
        }

        /// <summary>Ekran şu an görünür mü (teşhis/koruma amaçlı).</summary>
        public static bool IsVisible => _instance != null && _instance._visible;

        // ------------------------------------------------------------- döngü

        private void ShowInstance(string sceneName)
        {
            _target = 0f;
            _displayed = 0f;
            _shownPercent = -1;

            if (_sceneText != null)
            {
                _sceneText.text = sceneName ?? "";
            }

            // VR: kart yeni baştan kameranın önüne otursun — eski sahnedeki konumundan
            // kaymasın (`OnEnable` → HudFollow._initialized sıfırlanır).
            if (_hudFollow != null)
            {
                _hudFollow.enabled = false;
                _hudFollow.enabled = true;
            }

            ApplyVisible(true);
            ApplyProgress();
        }

        private void Update()
        {
            if (_instance != this || !_visible)
            {
                return;
            }

            // Yükleme kareleri düzensizdir; bar ani sıçramasın diye hedefe yumuşayarak gider.
            // Geri gitmek (yeni yükleme) anında olur, ileri gitmek akar.
            _displayed = _target < _displayed
                ? _target
                : Mathf.MoveTowards(_displayed, _target, Time.unscaledDeltaTime * FillSpeed);

            ApplyProgress();
            Pulse();
        }

        private void ApplyProgress()
        {
            UiKit.SetBarFill(_progressFill, _displayed);

            int percent = Mathf.Clamp(Mathf.RoundToInt(_displayed * 100f), 0, 100);
            if (_progressText != null && _shownPercent != percent)
            {
                _shownPercent = percent;
                _progressText.text = $"%{percent}";
            }
        }

        /// <summary>Tek animasyon: "iş sürüyor" hissi veren yumuşak nabız.</summary>
        private void Pulse()
        {
            if (_accentStrip == null)
            {
                return;
            }

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / PulsePeriod));
            Color c = _accentStrip.color;
            c.a = Mathf.Lerp(0.55f, 1f, wave);
            _accentStrip.color = c;
        }

        private void ApplyVisible(bool visible)
        {
            if (_group == null || _canvas == null)
            {
                return;
            }

            _visible = visible;
            _canvas.enabled = visible; // gizliyken hiç çizim maliyeti olmasın
            _group.alpha = visible ? 1f : 0f;

            // Yükleme ekranı hiçbir şeyi tıklanabilir kılmaz; masaüstünde arkadaki admin
            // arayüzüne kazara basılmasın diye yalnız raycast'i keser.
            _group.blocksRaycasts = visible && !_worldSpace;
            _group.interactable = false;
        }
    }
}
