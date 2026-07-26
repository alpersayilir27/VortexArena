using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// AdminConsole (masaüstü, screen-space UGUI) controller'ı. İki panel:
    /// (1) connecting — yalnız durum metni + "Yeniden Bağlan"; **IP SORULMAZ**;
    /// (2) dashboard — canlı roster + oyuncu başına set_team / kick / identify +
    ///     maç paneli (mod/harita seçimi, start/abort/lobiye dön, canlı skor, kill-feed).
    /// Adres `AppSession.ServerIp/ServerPort`'tan gelir; onu Flutter launcher'ın geçtiği
    /// `--server-ip` argümanından `AppBoot` doldurur → sahne açılır açılmaz otomatik bağlanılır.
    /// Sunucu bu ekrandan BAŞLATILMAZ — `Server/VortexArena.Server.App` her zaman elle
    /// çalıştırılır (bkz. Server/README.md). Tüm UI bağları null olabilir.
    /// Maç otoritesi SUNUCUDADIR — buradaki panel yalnız komut yollar ve gösterir.
    /// </summary>
    public class AdminConsoleController : MonoBehaviour
    {
        [Header("Paneller")]
        [SerializeField] private GameObject connectingPanel;
        [SerializeField] private GameObject dashboardPanel;

        [Header("Bağlanma ekranı")]
        [SerializeField] private TMP_Text connectingStatusText;

        [Header("Dashboard")]
        [SerializeField] private TMP_Text rosterText;
        [SerializeField] private TMP_InputField playerIdField;
        [SerializeField] private TMP_Text dashboardStatusText;

        [Header("Maç paneli")]
        [SerializeField] private GameCatalog catalog;
        [SerializeField] private TMP_Dropdown modeDropdown;
        [SerializeField] private TMP_Dropdown mapDropdown;
        [SerializeField] private TMP_Text matchStateText;
        [SerializeField] private TMP_Text killFeedText;
        [SerializeField] private TacticalView tacticalView;

        private const int AdminKillFeedMaxLines = 8;

        private readonly List<ModeDefinition> _modes = new List<ModeDefinition>();
        private readonly List<MapDefinition> _maps = new List<MapDefinition>();
        private readonly List<string> _dropdownScratch = new List<string>();
        private readonly Dictionary<int, string> _playerNames = new Dictionary<int, string>();
        private readonly List<string> _killFeed = new List<string>();

        /// <summary>Bağlanma bir kez istendi mi? (Update'in sonsuz Connect çağırmasını keser.)</summary>
        private bool _connectRequested;

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
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnKillEvent += HandleKillEvent;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
        }

        private void OnDisable()
        {
            NetEvents.OnConnectionStateChanged -= HandleConnectionStateChanged;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnKillEvent -= HandleKillEvent;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
        }

        private void Start()
        {
            BuildModeDropdown();

            bool connected = ArenaClient.Instance != null && ArenaClient.Instance.State == ArenaConnectionState.Connected;
            ApplyPanels(connected);
            RefreshStatusTexts();
            _connectRequested = connected;
        }

        /// <summary>
        /// Otomatik bağlanmayı Start() DEĞİL burası yapar: `ArenaClient` kalıcı tekili
        /// `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` ile doğuyor ve bu sahnenin
        /// `Start()`'ında henüz var olmayabilir. Hazır olduğu ilk karede bağlanırız.
        /// </summary>
        private void Update()
        {
            if (_connectRequested || ArenaClient.Instance == null)
            {
                return;
            }

            Connect(); // adres launcher'dan geldi — kullanıcıya sorulmaz
        }

        // -------------------------------------------------------- bağlanma ekranı

        /// <summary>"Yeniden Bağlan" — elle Disconnect sonrası tek geri dönüş yolu.</summary>
        public void ReconnectPressed()
        {
            _connectRequested = false;
            Connect();
        }

        public void DisconnectPressed()
        {
            if (ArenaClient.Instance != null)
            {
                ArenaClient.Instance.Disconnect();
            }
        }

        /// <summary>
        /// `AppSession`'daki adrese bağlanır. Adres yoksa (build launcher'sız açıldı)
        /// ekranda sebebi yazar — burada IP sorulmaz, çözüm launcher'dan başlatmaktır.
        /// </summary>
        private void Connect()
        {
            if (ArenaClient.Instance == null)
            {
                // Geçici: tekil henüz doğmadı → _connectRequested set EDİLMEZ, Update tekrar dener.
                SetConnectingStatus("İstemci hazır değil.");
                return;
            }

            if (!AppSession.HasServerEndpoint)
            {
                // Kalıcı: argüman gelmemiş. Tekrar denemek durumu değiştirmez.
                _connectRequested = true;
                SetConnectingStatus(
                    $"Sunucu adresi yok. Bu uygulama launcher'dan başlatılmalı " +
                    $"({AppBoot.ArgServerIp} <ip>).");
                return;
            }

            _connectRequested = true;
            SetConnectingStatus($"Bağlanılıyor: {AppSession.ServerIp}:{AppSession.ServerPort}");
            ArenaClient.Instance.Connect(AppSession.ServerIp, AppSession.ServerPort, AppSession.RoleAdmin);
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

        // ------------------------------------------------------------- maç paneli

        /// <summary>Katalogdaki modları dropdown'a yazar ve ilk modun haritalarını doldurur.</summary>
        private void BuildModeDropdown()
        {
            _modes.Clear();

            if (catalog != null && catalog.Modes != null)
            {
                for (int i = 0; i < catalog.Modes.Length; i++)
                {
                    ModeDefinition mode = catalog.Modes[i];
                    if (mode != null && !string.IsNullOrEmpty(mode.ModeId))
                    {
                        _modes.Add(mode);
                    }
                }
            }

            if (modeDropdown != null)
            {
                _dropdownScratch.Clear();
                for (int i = 0; i < _modes.Count; i++)
                {
                    _dropdownScratch.Add(DisplayOf(_modes[i].DisplayName, _modes[i].ModeId));
                }

                modeDropdown.onValueChanged.RemoveListener(HandleModeDropdownChanged);
                modeDropdown.ClearOptions();
                modeDropdown.AddOptions(_dropdownScratch);
                modeDropdown.SetValueWithoutNotify(0);
                modeDropdown.RefreshShownValue();
                modeDropdown.onValueChanged.AddListener(HandleModeDropdownChanged);
            }

            if (_modes.Count == 0)
            {
                SetDashboardStatus("GameCatalog atanmadı veya boş; maç başlatılamaz.");
            }

            BuildMapDropdown();
        }

        /// <summary>Seçili modun uyumlu haritalarını harita dropdown'una yazar.</summary>
        private void BuildMapDropdown()
        {
            _maps.Clear();

            ModeDefinition mode = SelectedMode();
            if (catalog != null && mode != null)
            {
                List<MapDefinition> maps = catalog.MapsForMode(mode.ModeId);
                if (maps != null)
                {
                    for (int i = 0; i < maps.Count; i++)
                    {
                        if (maps[i] != null && !string.IsNullOrEmpty(maps[i].SceneName))
                        {
                            _maps.Add(maps[i]);
                        }
                    }
                }
            }

            if (mapDropdown != null)
            {
                _dropdownScratch.Clear();
                for (int i = 0; i < _maps.Count; i++)
                {
                    _dropdownScratch.Add(DisplayOf(_maps[i].DisplayName, _maps[i].SceneName));
                }

                mapDropdown.onValueChanged.RemoveListener(HandleMapDropdownChanged);
                mapDropdown.ClearOptions();
                mapDropdown.AddOptions(_dropdownScratch);
                mapDropdown.SetValueWithoutNotify(0);
                mapDropdown.RefreshShownValue();
                mapDropdown.onValueChanged.AddListener(HandleMapDropdownChanged);
            }

            ApplySelectedMapToTacticalView();
        }

        private void HandleModeDropdownChanged(int index)
        {
            BuildMapDropdown();
        }

        private void HandleMapDropdownChanged(int index)
        {
            ApplySelectedMapToTacticalView();
        }

        /// <summary>Taktik görünüm ölçeğini seçili haritanın metre boyutuna eşitler.</summary>
        private void ApplySelectedMapToTacticalView()
        {
            MapDefinition map = SelectedMap();
            if (map != null && tacticalView != null)
            {
                tacticalView.SetArenaSize(map.Size.x, map.Size.y);
            }
        }

        public void StartMatchPressed()
        {
            ModeDefinition mode = SelectedMode();
            MapDefinition map = SelectedMap();

            if (mode == null || map == null)
            {
                SetDashboardStatus("Mod/harita seçilmedi; maç başlatılamadı.");
                return;
            }

            SendAdmin(new StartMatchMsg { modeId = mode.ModeId, sceneName = map.SceneName });
            SetDashboardStatus($"Maç isteği gönderildi: {mode.ModeId} · {map.SceneName}");
        }

        public void AbortMatchPressed()
        {
            SendAdmin(new AbortMatchMsg());
        }

        public void ReturnToLobbyPressed()
        {
            SendAdmin(new ReturnToLobbyMsg());
        }

        private ModeDefinition SelectedMode()
        {
            int index = modeDropdown != null ? modeDropdown.value : 0;
            return index >= 0 && index < _modes.Count ? _modes[index] : null;
        }

        private MapDefinition SelectedMap()
        {
            int index = mapDropdown != null ? mapDropdown.value : 0;
            return index >= 0 && index < _maps.Count ? _maps[index] : null;
        }

        private static string DisplayOf(string displayName, string fallback)
        {
            return string.IsNullOrEmpty(displayName) ? fallback : displayName;
        }

        // -------------------------------------------------------- olay işleyiciler

        private void HandleConnectionStateChanged(ArenaConnectionState state)
        {
            ApplyPanels(state == ArenaConnectionState.Connected);
            RefreshStatusTexts();

            if (state != ArenaConnectionState.Connected)
            {
                if (rosterText != null)
                {
                    rosterText.text = "";
                }

                _killFeed.Clear();
                RedrawKillFeed();
            }
        }

        private void HandleLobbyState(LobbyStateMsg msg)
        {
            // Kill-feed'de id yerine ad gösterebilmek için sözlüğü tazele.
            if (msg != null && msg.players != null)
            {
                for (int i = 0; i < msg.players.Length; i++)
                {
                    PlayerInfo info = msg.players[i];
                    if (info != null && !string.IsNullOrEmpty(info.name))
                    {
                        _playerNames[info.playerId] = info.name;
                    }
                }
            }

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

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null || matchStateText == null)
            {
                return;
            }

            matchStateText.text =
                $"Faz: {msg.phase} · Süre {FormatTime(msg.timeRemaining)} · {ScoreLine(msg.scoreRed, msg.scoreBlue)}";
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            if (msg == null || matchStateText == null)
            {
                return;
            }

            matchStateText.text = $"Geri sayım: {msg.seconds}";
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null || matchStateText == null)
            {
                return;
            }

            string winner = msg.winnerTeam == "red" ? "KIRMIZI KAZANDI"
                : msg.winnerTeam == "blue" ? "MAVİ KAZANDI"
                : "BERABERE";

            matchStateText.text = $"{winner} · {ScoreLine(msg.scoreRed, msg.scoreBlue)}";
        }

        private void HandleKillEvent(KillEventMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            string weapon = string.IsNullOrEmpty(msg.weaponId) ? "" : $" [{msg.weaponId}]";
            string victim = NameOf(msg.victimId);
            string line = msg.killerId > 0 && msg.killerId != msg.victimId
                ? $"{NameOf(msg.killerId)} ⇒ {victim}{weapon}"
                : $"☠ {victim}{weapon}";

            _killFeed.Add(line);
            while (_killFeed.Count > AdminKillFeedMaxLines)
            {
                _killFeed.RemoveAt(0);
            }

            RedrawKillFeed();
        }

        private void HandleReturnToLobby()
        {
            _killFeed.Clear();
            RedrawKillFeed();

            if (matchStateText != null)
            {
                matchStateText.text = "Faz: Lobby";
            }
        }

        // ---------------------------------------------------------------- çizim

        private void RedrawKillFeed()
        {
            if (killFeedText == null)
            {
                return;
            }

            if (_killFeed.Count == 0)
            {
                killFeedText.text = "";
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < _killFeed.Count; i++)
            {
                sb.AppendLine(_killFeed[i]);
            }

            killFeedText.text = sb.ToString();
        }

        private string NameOf(int playerId)
        {
            return _playerNames.TryGetValue(playerId, out string name) && !string.IsNullOrEmpty(name)
                ? name
                : $"Oyuncu {playerId}";
        }

        private static string ScoreLine(int scoreRed, int scoreBlue)
        {
            return $"KIRMIZI {scoreRed} — {scoreBlue} MAVİ";
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        private void ApplyPanels(bool connected)
        {
            if (connectingPanel != null)
            {
                connectingPanel.SetActive(!connected);
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
                    SetConnectingStatus(AppSession.HasServerEndpoint
                        ? $"Bağlanılıyor: {AppSession.ServerIp}:{AppSession.ServerPort}"
                        : "Bağlanılıyor...");
                    break;
                default:
                    SetConnectingStatus(AppSession.HasServerEndpoint
                        ? $"Bağlı değil — {AppSession.ServerIp}:{AppSession.ServerPort}"
                        : "Bağlı değil (sunucu adresi yok)");
                    break;
            }
        }

        private void SetConnectingStatus(string text)
        {
            if (connectingStatusText != null)
            {
                connectingStatusText.text = text;
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
