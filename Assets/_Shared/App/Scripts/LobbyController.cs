using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Lobby (VR) world-space panelini sürer: canlı roster, ready/takım ve
    /// **gizli** IP paneli (numpad ile elle adres girme).
    ///
    /// <para>
    /// <b>Normal akış oyuncuya hiçbir şey sormaz:</b> adres öncelik zinciriyle
    /// (komut satırı <c>--server-ip</c> &gt; PlayerPrefs &gt; beacon &gt;
    /// StreamingAssets/arena.json) bulunur ve <b>otomatik bağlanılır</b>. IP paneli
    /// başlangıçta KAPALIDIR. Zincirin başındaki komut satırı adresini
    /// <see cref="AppBoot"/> yazar (editörde <c>Tools &gt; VortexArena &gt; Dev</c>
    /// penceresinin seçtiği hedef de bu yoldan gelir) — açıkça verilen adres kazanır.
    /// </para>
    /// <para>
    /// <b>Kurtarma yolu:</b> beacon'ı kesen/izole eden ağlarda sunucu bulunamazsa
    /// sağ kumandada <b>A tuşuna iki kez</b> basılarak IP paneli açılır ve adres elle
    /// girilir (girilen adres <c>PlayerPrefs</c>'e kalıcı yazılır, beacon'ı ezer).
    /// Aynı kombinasyon paneli tekrar kapatır. Kalibrasyondaki A+B basılı tutma
    /// kombinasyonu yalnız arena sahnelerinde olduğu için çakışma yoktur.
    /// </para>
    /// Tüm sahne bağları [SerializeField] ve null olabilir; buton onClick'leri public
    /// metotlara bağlanır.
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        private const int MaxIpTextLength = 21; // "255.255.255.255:65535"

        /// <summary>İki A basışı arası bu süreden kısaysa kombinasyon sayılır.</summary>
        private const float DoubleTapWindow = 0.6f;

        /// <summary>Bu süre boyunca hiç adres bulunamazsa kurtarma ipucu gösterilir.</summary>
        private const float DiscoveryHintDelay = 8f;

        private static readonly OVRInput.Controller Hand = OVRInput.Controller.RTouch;

        [Header("Durum")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text rosterText;

        [Header("IP paneli (gizli — sağ kumandada A×2 ile açılır)")]
        [SerializeField] private GameObject ipPanel;
        [SerializeField] private TMP_Text ipText;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;

        [Header("Hazır/Takım")]
        [SerializeField] private TMP_Text readyButtonText;

        private string _ipBuffer = "";
        private bool _manualEntry; // elle giriş (veya kayıtlı IP) beacon'ı ezer
        private bool _ready;
        private bool _beaconSubscribed;

        private bool _ipPanelVisible;
        private float _lastATapTime = float.NegativeInfinity;
        private float _discoveryTimer;
        private bool _autoConnectDone;
        private bool _hintShown;

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
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnKicked += HandleKicked;
            TrySubscribeBeacon();
        }

        private void OnDisable()
        {
            NetEvents.OnConnectionStateChanged -= HandleConnectionStateChanged;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLobbyState -= HandleLobbyState;
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

            SetIpPanelVisible(false); // oyuncuya adres sorulmaz; kurtarma A×2 ile açılır

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
            RefreshReadyLabel();
            RedrawRoster(null);

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
                SetStatus("Sunucu bulunamadı. Adresi elle girmek için sağ kumandada A'ya İKİ KEZ bas.");
            }
        }

        /// <summary>Sağ kumandada A×2 → IP panelini aç/kapat (gizli kurtarma yolu).</summary>
        private void DetectIpPanelCombo()
        {
            if (!OVRInput.GetDown(OVRInput.Button.One, Hand))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastATapTime <= DoubleTapWindow)
            {
                _lastATapTime = float.NegativeInfinity; // üçüncü basış yeni çift saymasın
                SetIpPanelVisible(!_ipPanelVisible);
                OVRInput.SetControllerVibration(0.5f, 0.3f, Hand);
            }
            else
            {
                _lastATapTime = now;
            }
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
                RefreshIpText();
                RefreshStatus();
            }
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

        public void ToggleReady()
        {
            if (ArenaClient.Instance == null || !ArenaClient.Instance.IsConnected)
            {
                return;
            }

            _ready = !_ready;
            ArenaClient.Instance.Send(new SetReadyMsg { ready = _ready });
            RefreshReadyLabel();
        }

        /// <summary>Kendi takımını seçer ("red"|"blue"). Not: protokolde set_team admin
        /// komutudur; sunucu oyuncudan kabul etmiyorsa loglayıp yok sayar.</summary>
        public void SetTeam(string team)
        {
            if (ArenaClient.Instance == null || !ArenaClient.Instance.IsConnected)
            {
                return;
            }

            if (team != "red" && team != "blue")
            {
                return;
            }

            ArenaClient.Instance.Send(new SetTeamMsg
            {
                playerId = ArenaClient.Instance.PlayerId,
                team = team
            });
        }

        // -------------------------------------------------------- olay işleyiciler

        private void HandleConnectionStateChanged(ArenaConnectionState state)
        {
            if (state != ArenaConnectionState.Connected)
            {
                _ready = false;
                RefreshReadyLabel();
                RedrawRoster(null);
            }

            RefreshStatus();
        }

        private void HandleConnected(WelcomeMsg msg)
        {
            _ready = false;
            RefreshReadyLabel();
            RefreshStatus();
        }

        private void HandleLobbyState(LobbyStateMsg msg)
        {
            RedrawRoster(msg);
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
        }

        private void RefreshReadyLabel()
        {
            if (readyButtonText != null)
            {
                readyButtonText.text = _ready ? "HAZIR (vazgeç)" : "HAZIR OL";
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

            if (connectButton != null)
            {
                connectButton.interactable = state == ArenaConnectionState.Disconnected;
            }

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

        private void RedrawRoster(LobbyStateMsg msg)
        {
            if (rosterText == null)
            {
                return;
            }

            if (msg == null || msg.players == null || msg.players.Length == 0)
            {
                rosterText.text = "";
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo p = msg.players[i];
                if (p == null)
                {
                    continue;
                }

                string team = string.IsNullOrEmpty(p.team) ? "-" : p.team;
                string battery = p.battery < 0f ? "-" : $"%{Mathf.RoundToInt(Mathf.Clamp01(p.battery) * 100f)}";

                sb.Append(p.playerId).Append("  ")
                  .Append(p.name).Append("  [")
                  .Append(p.role).Append("]  ")
                  .Append(team).Append("  ")
                  .Append(p.ready ? "HAZIR" : "bekliyor").Append("  ")
                  .Append(battery);

                if (!p.online)
                {
                    sb.Append("  (çevrimdışı)");
                }

                sb.AppendLine();
            }

            rosterText.text = sb.ToString();
        }

        private static string FormatEndpoint(string ip, int port)
        {
            // Varsayılan portta yalnız IP göster (numpad'de ':' tuşu zorunlu olmasın).
            return port == ArenaProtocol.CONTROL_PORT ? ip : $"{ip}:{port}";
        }
    }
}
