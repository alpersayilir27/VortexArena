using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Kabuk <c>Lobby</c> sahnesinin denetleyicisi. <b>Tek işi bağlantıdır:</b> durum metni ve
    /// **gizli** IP paneli (numpad ile elle adres girme).
    ///
    /// <para>
    /// <b>Bu sahne bir oyun alanı DEĞİL, bir bekleme odasıdır.</b> Oyuncu burada yalnız sunucuya
    /// bağlanmayı bekler; bağlanır bağlanmaz sunucunun <b>açık sahnesine</b> geçer
    /// (<c>SceneRouter</c>, §10.7) ve gerçek lobi orasıdır. Bu yüzden burada roster, "hazır"
    /// düğmesi ve takım seçimi <b>YOKTUR</b>: takımı yalnız admin atar (§5.2) ve <c>set_ready</c>
    /// bir yükleme kapısıdır, <c>SceneRouter</c> gönderir. Buraya oyun arayüzü eklenirse iki
    /// lobi doğar ve sahada hangisinin geçerli olduğu belirsizleşir.
    /// </para>
    /// <para>
    /// <b>Normal akış oyuncuya hiçbir şey sormaz:</b> adres öncelik zinciriyle
    /// (komut satırı <c>--server-ip</c> &gt; PlayerPrefs &gt; beacon &gt;
    /// StreamingAssets/arena.json) bulunur ve <b>otomatik bağlanılır</b>. IP paneli
    /// başlangıçta KAPALIDIR. Zincirin başındaki komut satırı adresini
    /// <see cref="AppBoot"/> yazar (editörde <c>Tools &gt; VortexArena &gt; Development &gt; Dev</c>
    /// penceresinin seçtiği hedef de bu yoldan gelir) — açıkça verilen adres kazanır.
    /// </para>
    /// <para>
    /// <b>Kurtarma yolu:</b> beacon'ı kesen/izole eden ağlarda sunucu bulunamazsa
    /// sağ kumandada <b>joystick 1 saniye basılı tutularak</b> IP paneli açılır ve adres
    /// elle girilir (girilen adres <c>PlayerPrefs</c>'e kalıcı yazılır, beacon'ı ezer).
    /// Aynı jest paneli tekrar kapatır; tetiklendiğinde kumanda titrer. Kalibrasyon
    /// jestiyle (A basılıyken B'ye çift basış) çakışmaz — ortak tuş yoktur.
    /// </para>
    /// Tüm sahne bağları [SerializeField] ve null olabilir; buton onClick'leri public
    /// metotlara bağlanır.
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        private const int MaxIpTextLength = 21; // "255.255.255.255:65535"

        /// <summary>Joystick bu süre kesintisiz basılı tutulursa IP paneli aç/kapat tetiklenir.</summary>
        private const float IpPanelHoldDuration = 1f;

        /// <summary>Bu süre boyunca hiç adres bulunamazsa kurtarma ipucu gösterilir.</summary>
        private const float DiscoveryHintDelay = 8f;

        /// <summary>
        /// IP paneli canvas düzleminden bu kadar (m) saparsa hata basılır — bkz.
        /// <see cref="WarnIfPanelOffCanvasPlane"/>. Sapma sıfır olmalıdır; tolerans yalnız
        /// kayan nokta yuvarlamasını yutar.
        /// </summary>
        private const float PanelPlaneTolerance = 0.01f;

        private static readonly OVRInput.Controller Hand = OVRInput.Controller.RTouch;

        [Header("Durum")]
        [SerializeField] private TMP_Text statusText;

        [Header("IP paneli (gizli — sağ kumandada joystick 1 sn basılı tutularak açılır)")]
        [SerializeField] private GameObject ipPanel;
        [SerializeField] private TMP_Text ipText;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;

        private string _ipBuffer = "";
        private bool _manualEntry; // elle giriş (veya kayıtlı IP) beacon'ı ezer
        private bool _beaconSubscribed;

        private bool _ipPanelVisible;
        private float _joystickHoldTimer;
        private bool _joystickHoldFired; // basılı tutmaya devam ederken ikinci kez tetiklenmesin
        private float _discoveryTimer;
        private bool _autoConnectDone;
        private bool _hintShown;
        private bool _planeChecked;

        private void Awake()
        {
            if (!AppSession.RoleResolved)
            {
                // Lobby sahnesi Boot'suz oynatıldı (Editor testi) — bu sahne player kabuğudur.
                AppSession.Role = AppSession.RolePlayer;
                AppSession.RoleResolved = true;
            }
        }

        private void OnEnable()
        {
            NetEvents.OnConnectionStateChanged += HandleConnectionStateChanged;
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnKicked += HandleKicked;
            TrySubscribeBeacon();
        }

        private void OnDisable()
        {
            NetEvents.OnConnectionStateChanged -= HandleConnectionStateChanged;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnKicked -= HandleKicked;

            if (_beaconSubscribed && ServerDiscovery.Instance != null)
            {
                ServerDiscovery.Instance.OnBeacon -= HandleBeacon;
            }

            _beaconSubscribed = false;
        }

        private void Start()
        {
            // Kalıcı singleton'lar sahne objelerinden sonra önyüklenebilir — burada tekrar dene.
            TrySubscribeBeacon();

            SetIpPanelVisible(false); // oyuncuya adres sorulmaz; kurtarma joystick basılı tutarak açılır

            if (AppSession.HasServerEndpoint)
            {
                // Komut satırından (veya dev penceresinden) açıkça verilmiş adres: zincirin
                // en üstü. _manualEntry ile işaretlenir ki beacon bunu EZMESİN.
                _ipBuffer = FormatEndpoint(AppSession.ServerIp, AppSession.ServerPort);
                _manualEntry = true;
            }
            else if (ServerDiscovery.TryGetSavedEndpoint(out string ip, out int port))
            {
                _ipBuffer = FormatEndpoint(ip, port);
                _manualEntry = true;
            }
            else if (ServerDiscovery.Instance != null &&
                     ServerDiscovery.Instance.TryGetPreferredEndpoint(out ip, out port))
            {
                _ipBuffer = FormatEndpoint(ip, port);
            }

            RefreshIpText();
            TryAutoConnect(); // adres varsa hemen bağlan; yoksa beacon'ı bekle
            RefreshStatus();
        }

        private void Update()
        {
            DetectIpPanelCombo();

            // `ArenaClient`/`ServerDiscovery` kalıcı tekilleri AfterSceneLoad'da doğar ve
            // Start()'ta henüz var olmayabilir. Kayıtlı adres (PlayerPrefs) varken hiç beacon
            // gelmezse tek deneme kaçardı — hazır olan ilk karede yakalıyoruz.
            TryAutoConnect();
            TrySubscribeBeacon();

            // Adres hâlâ yoksa bir süre sonra kurtarma yolunu yaz (beacon dinlemeye devam).
            if (_autoConnectDone || _hintShown || _ipPanelVisible)
            {
                return;
            }

            _discoveryTimer += Time.unscaledDeltaTime;
            if (_discoveryTimer >= DiscoveryHintDelay)
            {
                _hintShown = true;
                SetStatus("Sunucu bulunamadı. Adresi elle girmek için sağ kumandada joystick'e 1 sn basılı tut.");
            }
        }

        /// <summary>
        /// Sağ kumandada joystick 1 sn basılı → IP panelini aç/kapat (gizli kurtarma yolu).
        /// Basış kesilirse sayaç sıfırlanır; tetikleme başına tek titreşim verilir.
        /// </summary>
        private void DetectIpPanelCombo()
        {
            if (!OVRInput.Get(OVRInput.Button.PrimaryThumbstick, Hand))
            {
                _joystickHoldTimer = 0f;
                _joystickHoldFired = false;
                return;
            }

            if (_joystickHoldFired)
            {
                return; // hâlâ basılı — bırakılmadan ikinci kez tetiklenmez
            }

            _joystickHoldTimer += Time.unscaledDeltaTime;
            if (_joystickHoldTimer < IpPanelHoldDuration)
            {
                return;
            }

            _joystickHoldFired = true;
            SetIpPanelVisible(!_ipPanelVisible);
            OVRInput.SetControllerVibration(0.5f, 0.3f, Hand);
        }

        private void SetIpPanelVisible(bool visible)
        {
            _ipPanelVisible = visible;

            if (ipPanel != null)
            {
                ipPanel.SetActive(visible);
            }

            if (visible)
            {
                _hintShown = true; // panel açıkken ipucu metnini tekrar yazma
                WarnIfPanelOffCanvasPlane();
                RefreshIpText();
                RefreshStatus();
            }
        }

        /// <summary>
        /// Panel canvas düzleminin ÜSTÜNDE mi diye bakar; değilse bir kez hata basar.
        /// <para>
        /// ⚠️ <b>Neden ayrı bir denetim:</b> world-space canvas'ta düzlemden sapmış bir çocuk
        /// <b>çizilmeye devam eder ama tıklanamaz</b> — ne ISDK ışını ne fare ulaşır. Sebebi
        /// grafik raycast'inin canvas düzleminde kurulan bir kameradan yapılmasıdır: düzlemin
        /// önünde/arkasında kalan öge kameranın arkasına düşer ve
        /// <c>RectangleContainsScreenPoint</c> false döner. Konsolda tek satır olmadan bu
        /// "buton çalışmıyor" diye görünür ve saatler yer.
        /// </para>
        /// <para>
        /// Kolayca olur: canvas ölçeği 0.0012 olduğu için sahne görünümünde panelin z'sini
        /// yanlışlıkla 1 m kaydırmak yerel uzayda ~830 birimlik bir sapmadır.
        /// </para>
        /// Denetim <b>yalnız okur</b> — konumu düzeltmez: düzeltseydi sahnedeki değerle koddaki
        /// değer iki ayrı doğruluk kaynağı olurdu.
        /// </summary>
        private void WarnIfPanelOffCanvasPlane()
        {
            if (_planeChecked || ipPanel == null)
            {
                return;
            }

            _planeChecked = true;

            Canvas canvas = ipPanel.GetComponentInParent<Canvas>(true);
            if (canvas == null)
            {
                return;
            }

            Transform plane = canvas.rootCanvas.transform;
            float offset = Vector3.Dot(ipPanel.transform.position - plane.position, plane.forward);
            if (Mathf.Abs(offset) <= PanelPlaneTolerance)
            {
                return;
            }

            Debug.LogError($"[LobbyController] '{ipPanel.name}' canvas düzleminden {offset:0.###} m " +
                           "sapmış — panel çizilir ama hiçbir tuşuna basılamaz. RectTransform'un " +
                           "Pos Z'sini 0 yap.", ipPanel);
        }

        /// <summary>
        /// Bilinen adrese bir kez otomatik bağlanır. Tekrar çağrılması zararsızdır:
        /// bağlantı koparsa yeniden denemeyi <c>ArenaClient</c>'ın backoff döngüsü yapar,
        /// bu yüzden beacon her geldiğinde Connect çağırıp döngüyü baştan kurmayız.
        /// </summary>
        private void TryAutoConnect()
        {
            if (_autoConnectDone || ArenaClient.Instance == null)
            {
                return;
            }

            // Adres henüz yoksa öncelik zincirini tekrar dene: `ServerDiscovery` tekili
            // Start()'ta null olabilir ve o durumda arena.json fallback'i kaçardı
            // (PlayerPrefs statik okunduğu için hiç kaçmaz).
            if (string.IsNullOrEmpty(_ipBuffer) && ServerDiscovery.Instance != null &&
                ServerDiscovery.Instance.TryGetPreferredEndpoint(out string chainIp, out int chainPort))
            {
                _ipBuffer = FormatEndpoint(chainIp, chainPort);
                RefreshIpText();
            }

            if (!ServerDiscovery.TryParseEndpoint(_ipBuffer, out string ip, out int port))
            {
                return;
            }

            _autoConnectDone = true;
            ArenaClient.Instance.Connect(ip, port, AppSession.Role);
        }

        private void TrySubscribeBeacon()
        {
            if (_beaconSubscribed || ServerDiscovery.Instance == null)
            {
                return;
            }

            ServerDiscovery.Instance.OnBeacon += HandleBeacon;
            _beaconSubscribed = true;
        }

        // ------------------------------------------------------ UI buton metotları

        /// <summary>Numpad girişi: "0".."9", "." (buton parametresi olarak verilir).</summary>
        public void AppendChar(string c)
        {
            if (string.IsNullOrEmpty(c) || c.Length != 1 || "0123456789.:".IndexOf(c[0]) < 0)
            {
                return;
            }

            if (_ipBuffer.Length >= MaxIpTextLength)
            {
                return;
            }

            _ipBuffer += c;
            _manualEntry = true;
            RefreshIpText();
        }

        public void Backspace()
        {
            if (_ipBuffer.Length == 0)
            {
                return;
            }

            _ipBuffer = _ipBuffer.Substring(0, _ipBuffer.Length - 1);
            _manualEntry = true;
            RefreshIpText();
        }

        public void ClearIp()
        {
            _ipBuffer = "";
            _manualEntry = true;
            RefreshIpText();
        }

        public void ConnectPressed()
        {
            if (!ServerDiscovery.TryParseEndpoint(_ipBuffer, out string ip, out int port))
            {
                SetStatus($"Geçersiz adres: '{_ipBuffer}'");
                return;
            }

            ServerDiscovery.SaveManualEndpoint(ip, port);
            _manualEntry = true;

            if (ArenaClient.Instance == null)
            {
                SetStatus("İstemci hazır değil.");
                return;
            }

            _autoConnectDone = true; // elle bağlandık; beacon artık devralmasın
            ArenaClient.Instance.Connect(ip, port, AppSession.Role);
        }

        public void DisconnectPressed()
        {
            if (ArenaClient.Instance != null)
            {
                ArenaClient.Instance.Disconnect();
            }
        }

        // -------------------------------------------------------- olay işleyiciler

        private void HandleConnectionStateChanged(ArenaConnectionState state)
        {
            RefreshStatus();
        }

        private void HandleConnected(WelcomeMsg msg)
        {
            RefreshStatus();
        }

        private void HandleKicked(KickedMsg msg)
        {
            string reason = msg != null && !string.IsNullOrEmpty(msg.reason) ? $" ({msg.reason})" : "";
            SetStatus($"Sunucudan atıldınız{reason}.");
        }

        private void HandleBeacon(BeaconMsg beacon, string ip)
        {
            // Elle girilmiş/kayıtlı adres varken beacon alanı EZMEZ.
            if (_manualEntry && !string.IsNullOrEmpty(_ipBuffer))
            {
                return;
            }

            int port = beacon != null && beacon.controlPort > 0 ? beacon.controlPort : ArenaProtocol.CONTROL_PORT;
            _ipBuffer = FormatEndpoint(ip, port);
            RefreshIpText();
            TryAutoConnect(); // beacon'la bulunan sunucuya kendiliğinden bağlan
        }

        // ---------------------------------------------------------------- çizim

        private void RefreshIpText()
        {
            if (ipText != null)
            {
                ipText.text = _ipBuffer;
            }

            // ⚠️ "Bağlan" bağlantı DURUMUNA değil YAZILAN ADRESE bakar. Bu panel tam da istemci
            // eski/yanlış adrese boşuna deneyip dururken açılır ve `ArenaClient` o sırada
            // saniyelerce `Connecting`de kalır (WS zaman aşımı) — duruma bağlansaydı düğme tam
            // gerektiği anda gri olurdu. `Connect` koşan döngüyü zaten iptal edip yenisini kurar,
            // yani deneme ortasında basmak güvenlidir. Yan fayda: adres tamamlanır tamamlanmaz
            // düğme yanar, eksik yazımda sönük kalır.
            if (connectButton != null)
            {
                connectButton.interactable = ServerDiscovery.TryParseEndpoint(_ipBuffer, out _, out _);
            }
        }

        private void RefreshStatus()
        {
            ArenaConnectionState state = ArenaClient.Instance != null
                ? ArenaClient.Instance.State
                : ArenaConnectionState.Disconnected;

            switch (state)
            {
                case ArenaConnectionState.Connected:
                    SetStatus($"Bağlı — oyuncu {ArenaClient.Instance.PlayerId} ({ArenaClient.Instance.ServerIp}:{ArenaClient.Instance.ServerPort})");
                    break;
                case ArenaConnectionState.Connecting:
                    SetStatus($"Bağlanılıyor... ({_ipBuffer})");
                    break;
                default:
                    SetStatus(string.IsNullOrEmpty(_ipBuffer)
                        ? "Sunucu aranıyor..."
                        : $"Bağlı değil ({_ipBuffer})");
                    break;
            }

            // `connectButton` burada DEĞİL `RefreshIpText`'te sürülür (gerekçe orada).

            if (disconnectButton != null)
            {
                disconnectButton.interactable = state != ArenaConnectionState.Disconnected;
            }
        }

        private void SetStatus(string text)
        {
            if (statusText != null)
            {
                statusText.text = text;
            }
        }

        private static string FormatEndpoint(string ip, int port)
        {
            // Varsayılan portta yalnız IP göster (numpad'de ':' tuşu zorunlu olmasın).
            return port == ArenaProtocol.CONTROL_PORT ? ip : $"{ip}:{port}";
        }
    }
}
