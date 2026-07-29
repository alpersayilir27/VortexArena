using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VortexArena.Core.Arena;
using VortexArena.Core.UI;
using VortexArena.Net;

namespace VortexArena.App
{
    /// <summary>
    /// Sunucuya bağlanılamadığında **oyun ekranında** görünen tasarımlı hata ekranı.
    /// Sunucuyu ASLA başlatmaz; yalnız durumu bildirir (adres, geçen süre, deneme sayısı,
    /// son hata) ve masaüstünde elle "Yeniden Bağlan" sunar. Aynı bilgi hiyerarşisi hem
    /// masaüstü admin build'inde (screen-space + scrim + buton) hem Quest'te (world-space
    /// kart, lazy-follow, butonsuz) gösterilir.
    ///
    /// **Görünüm prefabtan gelir, SAHNEDEN değil:** iki varyant vardır —
    /// `Resources/UI/ConnectionOverlayScreen` (masaüstü) ve `…World` (VR); hangisinin
    /// yükleneceğine <see cref="Bootstrap"/> karar verir. Prefab sahneye KONMAZ: konsaydı
    /// yeni arena eklerken unutulacak bir adım olurdu (arena sahneleri kendine yeten
    /// kutulardır). Bu yüzden `ArenaClient` deseni korunur —
    /// `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` ile kendini önyükler, prefabı
    /// `Resources.Load` ile alır ve `DontDestroyOnLoad` tekil olarak yaşar.
    /// Bu sınıf yalnız **veri yazar ve görünürlüğü sürer**; yerleşim/renk/punto prefabta.
    ///
    /// **Neden grace süresi:** kopuş anlıksa (WS yeniden bağlanma backoff'u 1→2→5 sn) ekranın
    /// yanıp sönmesi hem çirkin hem maç ortasında dikkat dağıtıcı. Bağlı olmayan durum
    /// <see cref="GraceSeconds"/> kadar sürerse gösterilir; `Connected` olunca derhal kaybolur
    /// ve sayaç sıfırlanır. Aynı mantık açılışı ve maç ortasındaki kopmayı birlikte kapsar.
    ///
    /// **VR güvenlik kuralı:** oyuncu fiziksel alanda 1:1 yürüyor. (a) Tam ekran scrim YOK —
    /// yalnız yarı saydam kart çizilir, görüşü karartmak tehlikeli. (b) `ArenaBoundary`
    /// alan-dışı bildirdiği sürece overlay kendini TAMAMEN gizler: alan-dışı karartması ve
    /// uyarısı her zaman baskın kalmalı, bir bağlantı hatası ekranı oyuncunun duvara
    /// yürümesine sebep OLAMAZ.
    /// </summary>
    public class ConnectionOverlay : MonoBehaviour
    {
        // --------------------------------------------------------------- ayarlar

        /// <summary>Bağlantısız geçen bu süreden sonra ekran gösterilir (sn).</summary>
        private const float GraceSeconds = 3f;

        /// <summary>Metin tazeleme aralığı (sn) — ~4 Hz, gereksiz TMP yeniden çizimi olmasın.</summary>
        private const float RefreshInterval = 0.25f;

        /// <summary>Nabız periyodu (sn) — accent şerit + badge alfası 0.55 ↔ 1.0.</summary>
        private const float PulsePeriod = 1.2f;

        /// <summary>`LastError` en fazla bu kadar karakter gösterilir.</summary>
        private const int MaxErrorChars = 120;

        private const float CardWidth = 900f;
        private const float CardHeightVr = 520f;
        private const float CardHeightDesktop = 600f;

        /// <summary>World-space kip: 900 px → ~0.9 m.</summary>
        private const float WorldScale = 0.001f;

        // Palet + prosedürel öge fabrikaları `UiKit`'te (admin HUD'ı ile aynı görsel dil).
        private static readonly Color ColorScrim = UiKit.Scrim;
        private static readonly Color ColorCard = UiKit.Card;
        private static readonly Color ColorCardWorld = UiKit.CardTranslucent; // alfa ≈ 0.88 (VR)
        private static readonly Color ColorBorder = UiKit.Border;
        private static readonly Color ColorAccent = UiKit.Accent;
        private static readonly Color ColorTitle = UiKit.Title;
        private static readonly Color ColorMuted = UiKit.Muted;
        private static readonly Color ColorFaint = UiKit.Faint;
        private static readonly Color ColorOnAccent = UiKit.OnAccent;

        /// <summary>Kart köşe yarıçapı (px) — bu ekranın kendi ölçüsü, panel varsayılanından iri.</summary>
        private const float CardRadius = 20f;

        // --------------------------------------------------------------- durum

        private static ConnectionOverlay _instance;

        /// <summary>Prefabın <c>Resources</c> yolları (uzantısız) — VR world-space / masaüstü
        /// screen-space iki ayrı prefabtır, hangisinin yükleneceğine <see cref="Bootstrap"/>
        /// karar verir.</summary>
        public const string WorldResourcePath = "UI/ConnectionOverlayWorld";

        public const string ScreenResourcePath = "UI/ConnectionOverlayScreen";

        // ⚠️ Alanlar [SerializeField] — görünüm PREFABTAN gelir. Bu sınıf yalnız veri yazar
        // ve görünürlüğü sürer; yerleşim/renk/punto prefabta düzenlenir.

        [Tooltip("Bu prefab VR (world-space) varyantı mı? Screen-space varyantta KAPALI olmalı.")]
        [SerializeField] private bool _worldSpace;

        [Header("Kök")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Yalnız world-space varyantta dolu — kartı tembel takiple kameranın önüne taşır.")]
        [SerializeField] private HudFollow _hudFollow;

        [Header("Kart")]
        [SerializeField] private Image _accentStrip;
        [SerializeField] private Image _badge;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _addressText;
        [SerializeField] private TextMeshProUGUI _metaText;
        [SerializeField] private TextMeshProUGUI _hintText;
        [SerializeField] private TextMeshProUGUI _errorText;

        [Tooltip("Yalnız masaüstü (screen-space) varyantta dolu — VR'da yeniden bağlanma düğmesi yok.")]
        [SerializeField] private Button _reconnectButton;
        [SerializeField] private TextMeshProUGUI _reconnectLabel;

        /// <summary>Bağlantısız duruma girdiğimiz an (unscaled); bağlıyken -1.</summary>
        private float _disconnectedSince = -1f;

        private float _nextRefresh;
        private bool _forceRefresh = true;
        private bool _visible;

        /// <summary>`ArenaBoundary` önbelleği — her karede sahne taraması yapılmaz.</summary>
        private ArenaBoundary _boundary;
        private bool _boundarySearched;

        // Ekranda yazılı olan değerler (değişmedikçe TMP'ye dokunulmaz → çöp üretilmez).
        private bool _shownKnown;
        private string _shownIp = null;
        private int _shownPort = -1;
        private int _shownSeconds = -1;
        private int _shownAttempts = -1;
        private string _shownError = null;

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

            var prefab = Resources.Load<ConnectionOverlay>(path);
            if (prefab == null)
            {
                Debug.LogError($"[ConnectionOverlay] '{path}' prefabı bulunamadı — bağlantı hata " +
                               "ekranı çizilemeyecek.");
                return;
            }

            ConnectionOverlay overlay = Instantiate(prefab);
            overlay.name = "[ConnectionOverlay]";
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

            if (_reconnectButton != null)
            {
                // Prefabta kalıcı onClick kaydı YOKTUR: düğme yalnız adres bilinirken
                // etkindir (RefreshTexts) ve komut AdminCommands üzerinden gider.
                _reconnectButton.onClick.RemoveAllListeners();
                _reconnectButton.onClick.AddListener(HandleReconnectPressed);
            }
        }

        private void OnEnable()
        {
            NetEvents.OnConnectionStateChanged += HandleConnectionStateChanged;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        }

        private void OnDisable()
        {
            NetEvents.OnConnectionStateChanged -= HandleConnectionStateChanged;
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

        // ------------------------------------------------------------- döngü

        private void Update()
        {
            if (_instance != this)
            {
                return;
            }

            // Olayı kaçırmış olabiliriz: bu tekil `ArenaClient`'tan ÖNCE doğabilir, ilk
            // durum değişimi abonelikten önce yayınlanmış olabilir → durumu ayrıca yokla.
            ArenaClient client = ArenaClient.Instance;
            TrackState(client != null ? client.State : ArenaConnectionState.Disconnected);

            if (!ShouldShow())
            {
                SetVisible(false);
                return;
            }

            // GÜVENLİK: alan-dışıyken `ArenaBoundary`'nin karartma + uyarısı baskın kalır.
            if (IsOutOfBounds())
            {
                SetVisible(false);
                return;
            }

            // VR'da kart kameranın önüne HudFollow ile yerleşir; kamera henüz yoksa (Boot gibi
            // erken/kamerasız sahneler) göstermeyi ertele — panel origin'de asılı kalmasın.
            if (_group == null || (_worldSpace && Camera.main == null))
            {
                return;
            }

            SetVisible(true);
            Pulse();

            if (_forceRefresh || Time.unscaledTime >= _nextRefresh)
            {
                _forceRefresh = false;
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                RefreshTexts();
            }
        }

        private void HandleConnectionStateChanged(ArenaConnectionState state)
        {
            TrackState(state);
            _forceRefresh = true;
        }

        /// <summary>
        /// Sahne değişiminde: `ArenaBoundary` önbelleği düşer (overlay sahnelerden önce doğuyor,
        /// her sahnede yeniden bulunmalı) ve `HudFollow` yeniden başlar (yeni sahnenin kamerası
        /// bulunup panel doğrudan yerine otursun, eski konumdan kaymasın).
        /// </summary>
        private void HandleActiveSceneChanged(Scene previous, Scene current)
        {
            _boundary = null;
            _boundarySearched = false;

            if (_hudFollow != null)
            {
                _hudFollow.enabled = false;
                _hudFollow.enabled = true; // OnEnable → _initialized sıfırlanır
            }
        }

        /// <summary>Bağlı değilken sayaç işler; `Connected` olunca sıfırlanır.</summary>
        private void TrackState(ArenaConnectionState state)
        {
            if (state == ArenaConnectionState.Connected)
            {
                _disconnectedSince = -1f;
                return;
            }

            if (_disconnectedSince < 0f)
            {
                _disconnectedSince = Time.unscaledTime;
            }
        }

        private bool ShouldShow()
        {
            return _disconnectedSince >= 0f &&
                   Time.unscaledTime - _disconnectedSince >= GraceSeconds;
        }

        private bool IsOutOfBounds()
        {
            if (!_boundarySearched)
            {
                _boundary = FindFirstObjectByType<ArenaBoundary>();
                _boundarySearched = true; // sahne değişimine dek tekrar aranmaz
            }

            return _boundary != null && _boundary.IsOutOfBounds;
        }

        private void SetVisible(bool visible)
        {
            if (_group == null || _canvas == null || _visible == visible)
            {
                return; // her karede aynı değeri yazıp canvas'ı kirletmeyelim
            }

            _visible = visible;
            _canvas.enabled = visible; // gizliyken hiç çizim maliyeti olmasın
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible && !_worldSpace;
            _group.interactable = visible && !_worldSpace;

            if (visible)
            {
                _forceRefresh = true; // görünür olurken metinler taze olsun
                EnsureClickableOnDesktop();
            }
        }

        /// <summary>Tek animasyon: "hâlâ deniyor" hissi veren yumuşak nabız.</summary>
        private void Pulse()
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / PulsePeriod));
            float alpha = Mathf.Lerp(0.55f, 1f, wave);

            if (_accentStrip != null)
            {
                Color c = _accentStrip.color;
                c.a = alpha;
                _accentStrip.color = c;
            }

            if (_badge != null)
            {
                Color c = _badge.color;
                c.a = alpha;
                _badge.color = c;
            }
        }

        // ------------------------------------------------------------- metinler

        /// <summary>
        /// Adres kaynağı: önce fiilen denenen adres (`ArenaClient`), yoksa launcher'ın
        /// geçtiği `AppSession` adresi. Hiçbiri yoksa "adres yok" durumu.
        /// </summary>
        private static bool ResolveEndpoint(out string ip, out int port)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client != null && !string.IsNullOrEmpty(client.ServerIp) && client.ServerPort > 0)
            {
                ip = client.ServerIp;
                port = client.ServerPort;
                return true;
            }

            if (AppSession.HasServerEndpoint)
            {
                ip = AppSession.ServerIp;
                port = AppSession.ServerPort;
                return true;
            }

            ip = "";
            port = 0;
            return false;
        }

        private void RefreshTexts()
        {
            ArenaClient client = ArenaClient.Instance;
            bool known = ResolveEndpoint(out string ip, out int port);
            int seconds = _disconnectedSince >= 0f
                ? Mathf.Max(0, Mathf.FloorToInt(Time.unscaledTime - _disconnectedSince))
                : 0;
            int attempts = client != null ? client.ConnectAttempts : 0;
            string error = client != null ? client.LastError : "";

            // Başlık / adres / ipucu: yalnız adres durumu değişince yazılır.
            if (_shownIp == null || _shownKnown != known || _shownPort != port ||
                !string.Equals(_shownIp, ip, StringComparison.Ordinal))
            {
                _shownKnown = known;
                _shownIp = ip;
                _shownPort = port;

                _titleText.text = known ? "SUNUCUYA BAĞLANILAMIYOR" : "SUNUCU BULUNAMADI";
                _addressText.text = known ? $"{ip}:{port}" : "adres yok";
                _addressText.color = known ? ColorAccent : ColorFaint;
                _hintText.text = BuildHint(known);

                ApplyButtonState(known);
                _shownAttempts = -1; // meta satırı da tazelensin (deneme sayacı görünürlüğü değişti)
            }

            // Meta: geçen süre (sn çözünürlüğü) + deneme sayacı (yalnız adres varken).
            if (_shownSeconds != seconds || _shownAttempts != attempts)
            {
                _shownSeconds = seconds;
                _shownAttempts = attempts;

                _metaText.text = known && attempts > 0
                    ? $"{seconds} sn · {attempts}. deneme"
                    : $"{seconds} sn";
            }

            // Son hata: küçük punto, soluk, en altta.
            if (!string.Equals(_shownError, error, StringComparison.Ordinal))
            {
                _shownError = error;

                if (string.IsNullOrEmpty(error))
                {
                    _errorText.text = "";
                }
                else
                {
                    // "…" gibi tek karakterli semboller TMP varsayılan fontunda eksik olabilir
                    // (eksik glif □ çizilir) → düz üç nokta.
                    string clipped = error.Length > MaxErrorChars
                        ? error.Substring(0, MaxErrorChars) + "..."
                        : error;
                    _errorText.text = $"Son hata: {clipped}";
                }
            }
        }

        private static string BuildHint(bool known)
        {
            if (AppSession.Role == AppSession.RoleAdmin)
            {
                return known
                    ? "Sunucu uygulamasını başlatın, sonra Yeniden Bağlan'a basın."
                    : $"Bu uygulama launcher'dan başlatılmalıdır ({AppBoot.ArgServerIp} <ip>).";
            }

            return "Sunucunun açık olduğundan emin olun.\n" +
                   "Adresi elle girmek için sağ kumandada A'ya İKİ KEZ basın.";
        }

        private void ApplyButtonState(bool addressKnown)
        {
            if (_reconnectButton == null)
            {
                return;
            }

            // Adres hiç yoksa basmak hiçbir şeyi değiştirmez → yalancı umut vermeyelim.
            _reconnectButton.interactable = addressKnown;

            if (_reconnectLabel != null)
            {
                _reconnectLabel.color = addressKnown ? ColorOnAccent : ColorFaint;
            }
        }

        /// <summary>
        /// Elle yeniden bağlanma. Bu buton olmadan geri dönüş yolu YOK: `ArenaClient.Disconnect()`
        /// `_userDisconnect = true` yapıp otomatik yeniden deneme döngüsünü durdurur — tekrar
        /// bağlanmanın tek yolu açıkça `Connect(...)` çağırmaktır.
        /// </summary>
        private void HandleReconnectPressed()
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !ResolveEndpoint(out string ip, out int port))
            {
                return;
            }

            client.Connect(ip, port, AppSession.Role);
            _forceRefresh = true;
        }

        // ------------------------------------------------------------ UI kurulumu

        /// <summary>
        /// "Yeniden Bağlan" tıklanabilir olsun diye EventSystem garantisi (yalnız masaüstü).
        /// Admin `Lobby`'de kalmaz, arena sahnelerine girer ve <b>arena sahnelerinde EventSystem
        /// YOK</b> — garanti edilmezse buton orada sessizce ölür.
        /// <see cref="UiKit.EnsureEventSystem"/> kalıcı bir tane kurar (Input System paketiyle
        /// derlendiğimiz için modül `InputSystemUIInputModule`'dür; `StandaloneInputModule`
        /// runtime'da `UnityEngine.Input`'a dokunup patlar).
        /// </summary>
        private void EnsureClickableOnDesktop()
        {
            if (_worldSpace)
            {
                return; // VR kipinde buton yok
            }

            UiKit.EnsureEventSystem();
        }

        // Prosedürel öge fabrikaları, yerleşim yardımcıları ve yuvarlak köşe sprite'ı
        // `UiKit`'e taşındı (admin gözlemci HUD'ı ile tek görsel dil, tek uygulama).
    }
}
