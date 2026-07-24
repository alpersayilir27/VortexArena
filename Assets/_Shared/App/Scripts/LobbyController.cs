using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Lobby (VR) world-space panelini sürer: numpad ile IP kurma, bağlan/kes,
    /// canlı roster, ready/takım. Tüm sahne bağları [SerializeField] ve null
    /// olabilir (sahne kurulumu sonra Unity MCP ile yapılacak). Buton onClick'leri
    /// public metotlara bağlanır. Başlangıç IP: PlayerPrefs > arena.json;
    /// beacon gelirse alanı otomatik doldurur ama elle giriş her zaman ezer.
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        private const int MaxIpTextLength = 21; // "255.255.255.255:65535"

        [Header("Durum")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text rosterText;

        [Header("IP paneli")]
        [SerializeField] private TMP_Text ipText;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;

        [Header("Hazır/Takım")]
        [SerializeField] private TMP_Text readyButtonText;

        private string _ipBuffer = "";
        private bool _manualEntry; // elle giriş (veya kayıtlı IP) beacon'ı ezer
        private bool _ready;
        private bool _beaconSubscribed;

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

            if (ServerDiscovery.TryGetSavedEndpoint(out string ip, out int port))
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
            RefreshStatus();
            RedrawRoster(null);
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
                    SetStatus("Bağlanılıyor...");
                    break;
                default:
                    SetStatus("Bağlı değil");
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
