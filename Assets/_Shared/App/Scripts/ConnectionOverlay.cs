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
    /// **Neden tamamen prosedürel (prefab/Resources/sahne bağı YOK):** overlay her sahnede
    /// gerekli. Sahneye elle bağlanan bir prefab, yeni arena eklerken unutulacak bir adım
    /// olurdu (arena sahneleri kendine yeten kutulardır). Bu yüzden `ArenaClient` /
    /// `IdentifyOverlay` deseni tekrarlanır: `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`
    /// ile kendini önyükler, `DontDestroyOnLoad` tekil olarak yaşar, tüm UI koddan kurulur.
    /// Yuvarlatılmış köşe sprite'ı da runtime'da üretilir (tek statik, önbellekli).
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

        private bool _worldSpace;

        private Canvas _canvas;
        private CanvasGroup _group;
        private Image _accentStrip;
        private Image _badge;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _addressText;
        private TextMeshProUGUI _metaText;
        private TextMeshProUGUI _hintText;
        private TextMeshProUGUI _errorText;
        private Button _reconnectButton;
        private TextMeshProUGUI _reconnectLabel;
        private HudFollow _hudFollow;

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

            var go = new GameObject("[ConnectionOverlay]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ConnectionOverlay>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Quest'te (ya da XR aygıtı etkinken) world-space kart, masaüstünde screen-space.
            _worldSpace = UnityEngine.XR.XRSettings.isDeviceActive ||
                          Application.platform == RuntimePlatform.Android;
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

            EnsureUi();
            if (_group == null)
            {
                return; // VR'da Camera.main henüz yok — kart kurulumu ertelendi.
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

        private void EnsureUi()
        {
            if (_canvas != null)
            {
                return;
            }

            // VR'da kart kameranın önüne yerleşir; Camera.main yoksa (Boot gibi erken/kamerasız
            // sahneler) kurulumu erteleriz — böylece panel origin'de asılı kalmaz.
            if (_worldSpace && Camera.main == null)
            {
                return;
            }

            float cardHeight = _worldSpace ? CardHeightVr : CardHeightDesktop;

            var root = new GameObject(_worldSpace ? "[ConnectionCardWorld]" : "[ConnectionCardScreen]");
            root.transform.SetParent(transform, false);

            _canvas = root.AddComponent<Canvas>();
            _canvas.sortingOrder = 5000; // her şeyin üstünde
            _group = root.AddComponent<CanvasGroup>();

            var rootRect = root.GetComponent<RectTransform>();

            if (_worldSpace)
            {
                _canvas.renderMode = RenderMode.WorldSpace;
                rootRect.sizeDelta = new Vector2(CardWidth, cardHeight);
                root.transform.localScale = Vector3.one * WorldScale;

                // Kafaya KİLİTLEME yok: mevcut tembel takip bileşeni yeniden kullanılır.
                _hudFollow = root.AddComponent<HudFollow>();
            }
            else
            {
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                root.AddComponent<GraphicRaycaster>();

                // Tam ekran scrim: dashboard'ın okunmasını engellemesi İSTENEN şey.
                Image scrim = UiKit.Image(rootRect, "Scrim", null, ColorScrim);
                UiKit.Stretch(scrim.rectTransform);
            }

            BuildCard(rootRect, cardHeight);

            // Kurulum gizli durumla başlar (_visible ile birebir tutarlı olsun); aynı karede
            // SetVisible(true) çağrılıp açılır.
            _canvas.enabled = false;
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        private void BuildCard(RectTransform parent, float cardHeight)
        {
            // Kart = kenar rengindeki yuvarlatılmış zemin + 2 px içeri kaçmış dolgu.
            Image border = UiKit.Image(parent, "CardBorder", UiKit.RoundedSprite(CardRadius), ColorBorder);
            RectTransform borderRect = border.rectTransform;

            if (_worldSpace)
            {
                UiKit.Stretch(borderRect); // world-space canvas'ın kendisi kart boyutunda
            }
            else
            {
                borderRect.anchorMin = new Vector2(0.5f, 0.5f);
                borderRect.anchorMax = new Vector2(0.5f, 0.5f);
                borderRect.pivot = new Vector2(0.5f, 0.5f);
                borderRect.anchoredPosition = Vector2.zero;
                borderRect.sizeDelta = new Vector2(CardWidth, cardHeight);
            }

            Image fill = UiKit.Image(borderRect, "CardFill", UiKit.RoundedSprite(CardRadius),
                _worldSpace ? ColorCardWorld : ColorCard);
            UiKit.Stretch(fill.rectTransform, 2f);
            RectTransform card = fill.rectTransform;

            // Üstte 4 px accent şerit (yuvarlak köşelerden taşmaması için yatayda 20 px içeri).
            _accentStrip = UiKit.Image(card, "AccentStrip", null, ColorAccent);
            RectTransform strip = _accentStrip.rectTransform;
            strip.anchorMin = new Vector2(0f, 1f);
            strip.anchorMax = new Vector2(1f, 1f);
            strip.pivot = new Vector2(0.5f, 1f);
            strip.offsetMin = new Vector2(20f, -4f);
            strip.offsetMax = new Vector2(-20f, 0f);

            // Sol üstte "!" badge'i (⚠ yerine "!" — TMP varsayılan fontunda glif garantisi yok).
            _badge = UiKit.Image(card, "Badge", UiKit.RoundedSprite(CardRadius), ColorAccent);
            RectTransform badgeRect = _badge.rectTransform;
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.anchoredPosition = new Vector2(44f, -48f);
            badgeRect.sizeDelta = new Vector2(80f, 80f);

            TextMeshProUGUI badgeText = UiKit.Text(badgeRect, "BadgeText", 52f, ColorOnAccent,
                FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Stretch(badgeText.rectTransform);
            badgeText.text = "!";

            _titleText = UiKit.Text(card, "Title", 44f, ColorTitle, FontStyles.Bold,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_titleText.rectTransform, 148f, 52f, 44f, 56f);
            _titleText.characterSpacing = 3f; // hafif letter-spacing (TMP font birimi)

            _addressText = UiKit.Text(card, "Address", 30f, ColorAccent, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_addressText.rectTransform, 148f, 116f, 44f, 40f);

            _metaText = UiKit.Text(card, "Meta", 24f, ColorMuted, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_metaText.rectTransform, 148f, 160f, 44f, 34f);

            Image divider = UiKit.Image(card, "Divider", null, ColorBorder);
            UiKit.Block(divider.rectTransform, 44f, 232f, 44f, 2f);

            _hintText = UiKit.Text(card, "Hint", 24f, ColorMuted, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_hintText.rectTransform, 44f, 258f, 44f, 108f);
            _hintText.lineSpacing = 18f; // rahat satır aralığı

            _errorText = UiKit.Text(card, "Error", 20f, ColorFaint, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_errorText.rectTransform, 44f, 386f, 44f, 60f);

            if (!_worldSpace)
            {
                BuildReconnectButton(card);
            }
        }

        /// <summary>VR'da buton YOK: prosedürel canvas'ın işaretçisi olmaz, yerine A×2 ipucu var.</summary>
        private void BuildReconnectButton(RectTransform card)
        {
            Image background = UiKit.Image(card, "ReconnectButton", UiKit.RoundedSprite(CardRadius), ColorAccent);
            background.raycastTarget = true; // tek tıklanabilir öge (UiKit.Image varsayılanı kapalı)
            RectTransform rect = background.rectTransform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-44f, 44f);
            rect.sizeDelta = new Vector2(320f, 64f);

            _reconnectLabel = UiKit.Text(rect, "Label", 26f, ColorOnAccent, FontStyles.Bold,
                TextAlignmentOptions.Center);
            UiKit.Stretch(_reconnectLabel.rectTransform);
            _reconnectLabel.text = "YENİDEN BAĞLAN";

            _reconnectButton = background.gameObject.AddComponent<Button>();
            _reconnectButton.targetGraphic = background;

            ColorBlock colors = _reconnectButton.colors;
            colors.normalColor = Color.white;                    // Image rengi accent, tint 1
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.42f, 0.45f, 0.52f, 1f);
            colors.fadeDuration = 0.08f;
            _reconnectButton.colors = colors;

            _reconnectButton.onClick.AddListener(HandleReconnectPressed);
        }

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
