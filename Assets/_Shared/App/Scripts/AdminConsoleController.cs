using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// AdminConsole (masaüstü, screen-space UGUI) controller'ı. İki panel:
    /// (1) launcher — sunucu exe'sini başlat (yalnız Windows) veya IP'ye bağlan;
    /// (2) dashboard — canlı roster + oyuncu başına set_team / kick / identify.
    /// Başlatılan server Process'i bağımsız yaşar: kapanışta ÖLDÜRÜLMEZ, yalnız
    /// "Sunucuyu Durdur" butonu sonlandırır. Tüm UI bağları null olabilir.
    /// </summary>
    public class AdminConsoleController : MonoBehaviour
    {
        private const string PrefKeyServerExePath = "arena.serverExePath";
        private const string DefaultServerExeRelativePath =
            "Server/VortexArena.Server.App/bin/Release/net8.0/VortexArena.Server.App.exe";

        [Header("Paneller")]
        [SerializeField] private GameObject launcherPanel;
        [SerializeField] private GameObject dashboardPanel;

        [Header("Launcher")]
        [SerializeField] private TMP_InputField serverExePathField;
        [SerializeField] private TMP_InputField ipField;
        [SerializeField] private TMP_Text launcherStatusText;

        [Header("Dashboard")]
        [SerializeField] private TMP_Text rosterText;
        [SerializeField] private TMP_InputField playerIdField;
        [SerializeField] private TMP_Text dashboardStatusText;

        private System.Diagnostics.Process _serverProcess;

        private void Awake()
        {
            if (!AppSession.RoleResolved)
            {
                // AdminConsole sahnesi Boot'suz oynatıldı (Editor testi) — bu sahne admin kabuğudur.
                AppSession.Role = AppSession.RoleAdmin;
                AppSession.RoleResolved = true;
            }
        }

        private void OnEnable()
        {
            NetEvents.OnConnectionStateChanged += HandleConnectionStateChanged;
            NetEvents.OnLobbyState += HandleLobbyState;
        }

        private void OnDisable()
        {
            NetEvents.OnConnectionStateChanged -= HandleConnectionStateChanged;
            NetEvents.OnLobbyState -= HandleLobbyState;
        }

        private void Start()
        {
            if (serverExePathField != null && string.IsNullOrEmpty(serverExePathField.text))
            {
                serverExePathField.text = PlayerPrefs.GetString(PrefKeyServerExePath, DefaultServerExeRelativePath);
            }

            if (ipField != null && string.IsNullOrEmpty(ipField.text))
            {
                ipField.text = ServerDiscovery.TryGetSavedEndpoint(out string ip, out int port)
                    ? (port == ArenaProtocol.CONTROL_PORT ? ip : $"{ip}:{port}")
                    : "127.0.0.1";
            }

            bool connected = ArenaClient.Instance != null && ArenaClient.Instance.State == ArenaConnectionState.Connected;
            ApplyPanels(connected);
            RefreshStatusTexts();
        }

        // ------------------------------------------------------------- launcher

        public void StartServerPressed()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string path = ResolveServerExePath();
            if (!File.Exists(path))
            {
                SetLauncherStatus($"Sunucu exe bulunamadı: {path}");
                return;
            }

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path),
                    UseShellExecute = true // ayrı konsol penceresi — server logları görünür
                };
                _serverProcess = System.Diagnostics.Process.Start(startInfo);
            }
            catch (System.Exception e)
            {
                SetLauncherStatus($"Sunucu başlatılamadı: {e.Message}");
                return;
            }

            if (serverExePathField != null && !string.IsNullOrWhiteSpace(serverExePathField.text))
            {
                PlayerPrefs.SetString(PrefKeyServerExePath, serverExePathField.text.Trim());
                PlayerPrefs.Save();
            }

            SetLauncherStatus("Sunucu başlatıldı; 127.0.0.1'e bağlanılıyor...");
            Connect("127.0.0.1", ArenaProtocol.CONTROL_PORT); // backoff bağlanana dek dener
#else
            SetLauncherStatus("Sunucu başlatma yalnız Windows'ta desteklenir.");
#endif
        }

        /// <summary>Yalnız bu konsoldan başlatılan server process'ini sonlandırır.</summary>
        public void StopServerPressed()
        {
            if (_serverProcess == null)
            {
                SetLauncherStatus("Bu konsoldan başlatılmış bir sunucu yok.");
                return;
            }

            try
            {
                if (!_serverProcess.HasExited)
                {
                    _serverProcess.Kill();
                }

                SetLauncherStatus("Sunucu durduruldu.");
            }
            catch (System.Exception e)
            {
                SetLauncherStatus($"Sunucu durdurulamadı: {e.Message}");
            }
            finally
            {
                _serverProcess.Dispose();
                _serverProcess = null;
            }
        }

        public void ConnectPressed()
        {
            string text = ipField != null ? ipField.text : "";
            if (!ServerDiscovery.TryParseEndpoint(text, out string ip, out int port))
            {
                SetLauncherStatus($"Geçersiz adres: '{text}'");
                return;
            }

            ServerDiscovery.SaveManualEndpoint(ip, port);
            Connect(ip, port);
        }

        public void DisconnectPressed()
        {
            if (ArenaClient.Instance != null)
            {
                ArenaClient.Instance.Disconnect();
            }
        }

        private void Connect(string ip, int port)
        {
            if (ArenaClient.Instance == null)
            {
                SetLauncherStatus("İstemci hazır değil.");
                return;
            }

            ArenaClient.Instance.Connect(ip, port, AppSession.RoleAdmin);
        }

        private string ResolveServerExePath()
        {
            string path = serverExePathField != null && !string.IsNullOrWhiteSpace(serverExePathField.text)
                ? serverExePathField.text.Trim()
                : PlayerPrefs.GetString(PrefKeyServerExePath, DefaultServerExeRelativePath);

            if (string.IsNullOrWhiteSpace(path))
            {
                path = DefaultServerExeRelativePath;
            }

            if (!Path.IsPathRooted(path))
            {
                // Göreli yol proje/build köküne göredir (Editor: proje kökü; build: exe klasörü).
                path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            }

            return path;
        }

        // ------------------------------------------------------------ dashboard

        public void SetTeamRedPressed() { SetTeamForField("red"); }
        public void SetTeamBluePressed() { SetTeamForField("blue"); }

        public void KickPressed()
        {
            int playerId = ReadPlayerIdField();
            if (playerId > 0)
            {
                Kick(playerId);
            }
        }

        public void IdentifyPressed()
        {
            int playerId = ReadPlayerIdField();
            if (playerId > 0)
            {
                Identify(playerId);
            }
        }

        public void SetTeam(int playerId, string team)
        {
            if (team != "red" && team != "blue")
            {
                return;
            }

            SendAdmin(new SetTeamMsg { playerId = playerId, team = team });
        }

        public void Kick(int playerId)
        {
            SendAdmin(new KickMsg { playerId = playerId });
        }

        public void Identify(int playerId)
        {
            SendAdmin(new IdentifyMsg { playerId = playerId });
        }

        private void SetTeamForField(string team)
        {
            int playerId = ReadPlayerIdField();
            if (playerId > 0)
            {
                SetTeam(playerId, team);
            }
        }

        private int ReadPlayerIdField()
        {
            string text = playerIdField != null ? playerIdField.text : "";
            if (int.TryParse(text, out int playerId) && playerId > 0)
            {
                return playerId;
            }

            SetDashboardStatus($"Geçersiz oyuncu no: '{text}'");
            return -1;
        }

        private void SendAdmin<T>(T msg) where T : class
        {
            if (ArenaClient.Instance == null || !ArenaClient.Instance.IsConnected)
            {
                SetDashboardStatus("Bağlantı yok; komut gönderilemedi.");
                return;
            }

            ArenaClient.Instance.Send(msg);
        }

        // -------------------------------------------------------- olay işleyiciler

        private void HandleConnectionStateChanged(ArenaConnectionState state)
        {
            ApplyPanels(state == ArenaConnectionState.Connected);
            RefreshStatusTexts();

            if (state != ArenaConnectionState.Connected && rosterText != null)
            {
                rosterText.text = "";
            }
        }

        private void HandleLobbyState(LobbyStateMsg msg)
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
            sb.AppendLine("No  Ad  Rol  Takım  Hazır  Batarya  Sahne");
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
                  .Append(p.name).Append("  ")
                  .Append(p.role).Append("  ")
                  .Append(team).Append("  ")
                  .Append(p.ready ? "evet" : "hayır").Append("  ")
                  .Append(battery).Append("  ")
                  .Append(string.IsNullOrEmpty(p.scene) ? "-" : p.scene);

                if (!p.online)
                {
                    sb.Append("  (çevrimdışı)");
                }

                sb.AppendLine();
            }

            rosterText.text = sb.ToString();
        }

        // ---------------------------------------------------------------- çizim

        private void ApplyPanels(bool connected)
        {
            if (launcherPanel != null)
            {
                launcherPanel.SetActive(!connected);
            }

            if (dashboardPanel != null)
            {
                dashboardPanel.SetActive(connected);
            }
        }

        private void RefreshStatusTexts()
        {
            ArenaConnectionState state = ArenaClient.Instance != null
                ? ArenaClient.Instance.State
                : ArenaConnectionState.Disconnected;

            switch (state)
            {
                case ArenaConnectionState.Connected:
                    SetDashboardStatus($"Bağlı — {ArenaClient.Instance.ServerIp}:{ArenaClient.Instance.ServerPort}");
                    break;
                case ArenaConnectionState.Connecting:
                    SetLauncherStatus("Bağlanılıyor...");
                    break;
                default:
                    SetLauncherStatus("Bağlı değil");
                    break;
            }
        }

        private void SetLauncherStatus(string text)
        {
            if (launcherStatusText != null)
            {
                launcherStatusText.text = text;
            }
        }

        private void SetDashboardStatus(string text)
        {
            if (dashboardStatusText != null)
            {
                dashboardStatusText.text = text;
            }
        }
    }
}
