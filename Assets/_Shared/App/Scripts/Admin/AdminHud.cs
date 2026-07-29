using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Admin gözlemcinin sahne üstü yönetim arayüzü — <b>kalıcı</b> ekran-uzayı canvas'ı.
    /// Lobby ↔ arena geçişlerinde yeniden kurulmaz; operatör için arayüz kesintisizdir.
    ///
    /// <para><b>Yerleşim</b> (kullanıcı isteğiyle birebir):
    /// <list type="bullet">
    /// <item>En tepe orta: takım skorları; <b>skorların ortasındaki chip istatistikler düğmesi</b>
    /// (aynı zamanda faz + kalan süre göstergesi).</item>
    /// <item>Sol üst: tercihler düğmesi. Sağ üst: mod · harita + bağlantı durumu.</item>
    /// <item>Yan paneller: takım oyuncuları — takımlıda sol kırmızı / sağ mavi, <b>FFA'da tek
    /// kolon</b> (karar veriden gelir: hiçbir çevrimiçi oyuncunun takımı yoksa FFA).</item>
    /// <item>Alt orta: kamera kipi şeridi + seçili oyuncu. Alt sağ: ölüm akışı.</item>
    /// </list></para>
    ///
    /// <para><b>sortingOrder = 4000:</b> bağlantı hata ekranı 5000'de kalır ve gerektiğinde HUD'ın
    /// üstünü kaplar — bağlantı yoksa gösterilecek canlı veri de yoktur.</para>
    ///
    /// <para>Tazeleme olay güdümlüdür (<see cref="AdminRoster.Changed"/>,
    /// <see cref="AdminSession.Changed"/>); yalnız zamana bağlı alanlar (süre, ölüm geri sayımı,
    /// snapshot yaşı) <see cref="RefreshInterval"/> ile ~4 Hz tazelenir.</para>
    /// </summary>
    public class AdminHud : MonoBehaviour
    {
        /// <summary>Zamana bağlı alanların tazeleme aralığı (sn).</summary>
        private const float RefreshInterval = 0.25f;

        /// <summary>Kolon başına gösterilen en fazla satır (fazlası "+N daha" ile özetlenir).</summary>
        private const int MaxRowsPerColumn = 6;

        private const float Margin = 24f;
        private const float ColumnWidth = 380f;
        private const float ColumnTop = 118f;
        private const float RowGap = 8f;
        private const float HeaderHeight = 28f;

        private Canvas _canvas;

        // Üst bant
        private TextMeshProUGUI _scoreRedText;
        private TextMeshProUGUI _scoreBlueText;
        private TextMeshProUGUI _leaderboardText;
        private TextMeshProUGUI _chipText;
        private TextMeshProUGUI _matchInfoText;
        private TextMeshProUGUI _connectionText;
        private TextMeshProUGUI _adminNoticeText;
        private Image _connectionDot;

        // Kolonlar
        private RectTransform _redColumn;
        private RectTransform _blueColumn;
        private TextMeshProUGUI _redHeader;
        private TextMeshProUGUI _blueHeader;
        private TextMeshProUGUI _redOverflow;
        private TextMeshProUGUI _blueOverflow;
        private readonly List<AdminPlayerRow> _redRows = new List<AdminPlayerRow>();
        private readonly List<AdminPlayerRow> _blueRows = new List<AdminPlayerRow>();

        /// <summary>FFA lider tablosu sıralama tamponu (her tazelemede liste ayırmamak için).</summary>
        private readonly List<AdminPlayerView> _ranked = new List<AdminPlayerView>();

        // Alt şerit
        private readonly Image[] _modeButtons = new Image[3];
        private readonly TextMeshProUGUI[] _modeLabels = new TextMeshProUGUI[3];
        private TextMeshProUGUI _selectedText;
        private TextMeshProUGUI _hintText;
        private TextMeshProUGUI _killFeedText;

        private AdminPreferencesPanel _preferences;
        private AdminStatsPanel _stats;

        private float _nextRefresh;
        private bool _dirty = true;
        private readonly StringBuilder _sb = new StringBuilder();

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            AdminSession.Changed += MarkDirty;
            AdminSelection.Changed += MarkDirty; // başka bir admin'in eylemi/seçimi
            NetEvents.OnConnectionStateChanged += HandleConnectionState;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed += MarkDirty;
            }
        }

        private void OnDisable()
        {
            AdminSession.Changed -= MarkDirty;
            AdminSelection.Changed -= MarkDirty;
            NetEvents.OnConnectionStateChanged -= HandleConnectionState;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed -= MarkDirty;
            }
        }

        private void Update()
        {
            bool tick = Time.unscaledTime >= _nextRefresh;
            if (tick)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
            }

            if (_dirty || tick)
            {
                _dirty = false;
                Refresh();
            }

            TickRows(_redRows);
            TickRows(_blueRows);
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void HandleConnectionState(ArenaConnectionState state)
        {
            _dirty = true;
        }

        // ------------------------------------------------------------ UI kurulumu

        private void Build()
        {
            var root = new GameObject("[AdminHudCanvas]");
            root.transform.SetParent(transform, false);
            _canvas = UiKit.ScreenCanvas(root, 4000);
            UiKit.EnsureEventSystem();

            var rootRect = root.GetComponent<RectTransform>();

            BuildTopBar(rootRect);
            BuildColumns(rootRect);
            BuildBottomBar(rootRect);
            BuildKillFeed(rootRect);

            _preferences = gameObject.AddComponent<AdminPreferencesPanel>();
            _preferences.Initialize(rootRect);

            _stats = gameObject.AddComponent<AdminStatsPanel>();
            _stats.Initialize(rootRect);
        }

        private void BuildTopBar(RectTransform parent)
        {
            // Sol üst: tercihler.
            Button preferences = UiKit.Button(parent, "PreferencesButton", "TERCİHLER", 20f,
                UiKit.CardTranslucent, UiKit.Title,
                () => AdminSession.TogglePanel(AdminPanelKind.Preferences), out _);
            UiKit.Corner((RectTransform)preferences.transform, new Vector2(0f, 1f),
                new Vector2(Margin, -Margin), new Vector2(190f, 44f));

            // Orta: skorlar + chip.
            RectTransform center = UiKit.Node(parent, "ScoreBand");
            UiKit.Corner(center, new Vector2(0.5f, 1f), new Vector2(0f, -Margin), new Vector2(760f, 96f));

            _scoreRedText = UiKit.Text(center, "ScoreRed", 56f, UiKit.TeamRed, FontStyles.Bold,
                TextAlignmentOptions.TopRight);
            UiKit.Block(_scoreRedText.rectTransform, 0f, 0f, 500f, 64f);

            _scoreBlueText = UiKit.Text(center, "ScoreBlue", 56f, UiKit.TeamBlue, FontStyles.Bold,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_scoreBlueText.rectTransform, 500f, 0f, 0f, 64f);

            // Chip = faz/süre göstergesi VE istatistikler düğmesi (kullanıcı isteği: skorların ortası).
            Button chip = UiKit.Button(center, "StatsChip", "", 22f, UiKit.CardTranslucent, UiKit.Title,
                () => AdminSession.TogglePanel(AdminPanelKind.Stats), out _chipText);
            UiKit.Corner((RectTransform)chip.transform, new Vector2(0.5f, 1f), new Vector2(0f, 0f),
                new Vector2(260f, 76f));
            _chipText.alignment = TextAlignmentOptions.Center;

            // Bandın hemen altı: FFA lider tablosu (takım skoru olmayan modlarda üst bandın tek
            // anlamlı içeriği). Takımlı modda boş kalır, yer kaplamaz. Chip'in ALTINDA durur —
            // band içine koyulsa 56 punto skorlarla ve chip'le çakışırdı.
            _leaderboardText = UiKit.Text(parent, "Leaderboard", 22f, UiKit.Title, FontStyles.Bold,
                TextAlignmentOptions.Top);
            UiKit.Corner(_leaderboardText.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -(Margin + 100f)), new Vector2(760f, 28f));
            _leaderboardText.textWrappingMode = TextWrappingModes.NoWrap;
            _leaderboardText.overflowMode = TextOverflowModes.Ellipsis;

            // Sağ üst: maç kimliği + bağlantı.
            _matchInfoText = UiKit.Text(parent, "MatchInfo", 20f, UiKit.Muted, FontStyles.Normal,
                TextAlignmentOptions.TopRight);
            UiKit.Block(_matchInfoText.rectTransform, 0f, Margin, Margin, 26f);

            RectTransform connection = UiKit.Node(parent, "Connection");
            UiKit.Corner(connection, new Vector2(1f, 1f), new Vector2(-Margin, -(Margin + 30f)),
                new Vector2(320f, 24f));

            _connectionDot = UiKit.Solid(connection, "Dot", UiKit.Bad, true);
            UiKit.Corner(_connectionDot.rectTransform, new Vector2(1f, 1f), new Vector2(0f, -4f),
                new Vector2(12f, 12f));

            _connectionText = UiKit.Text(connection, "Text", 18f, UiKit.Faint, FontStyles.Normal,
                TextAlignmentOptions.TopRight);
            UiKit.Block(_connectionText.rectTransform, 0f, 0f, 20f, 24f);

            // Sağ üst, bağlantının altı: BAŞKA bir admin ne yaptı (§5.3 admin_state.notice).
            // Tercihler paneli kapalıyken de görünmeli — çoklu operatörde "harita neden değişti?"
            // sorusunun cevabı burada durur. Tek admin varken satır boş kalır, yer kaplamaz.
            _adminNoticeText = UiKit.Text(parent, "AdminNotice", 18f, UiKit.Accent, FontStyles.Normal,
                TextAlignmentOptions.TopRight);
            UiKit.Corner(_adminNoticeText.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Margin, -(Margin + 58f)), new Vector2(520f, 24f));
            _adminNoticeText.textWrappingMode = TextWrappingModes.NoWrap;
            _adminNoticeText.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void BuildColumns(RectTransform parent)
        {
            _redColumn = UiKit.Node(parent, "RedColumn");
            UiKit.Corner(_redColumn, new Vector2(0f, 1f), new Vector2(Margin, -ColumnTop),
                new Vector2(ColumnWidth, 800f));

            _redHeader = UiKit.Text(_redColumn, "Header", 22f, UiKit.TeamRed, FontStyles.Bold,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_redHeader.rectTransform, 4f, 0f, 4f, HeaderHeight);
            FitHeader(_redHeader);

            _redOverflow = UiKit.Text(_redColumn, "Overflow", 18f, UiKit.Faint, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);

            _blueColumn = UiKit.Node(parent, "BlueColumn");
            UiKit.Corner(_blueColumn, new Vector2(1f, 1f), new Vector2(-Margin, -ColumnTop),
                new Vector2(ColumnWidth, 800f));

            _blueHeader = UiKit.Text(_blueColumn, "Header", 22f, UiKit.TeamBlue, FontStyles.Bold,
                TextAlignmentOptions.TopRight);
            UiKit.Block(_blueHeader.rectTransform, 4f, 0f, 4f, HeaderHeight);
            FitHeader(_blueHeader);

            _blueOverflow = UiKit.Text(_blueColumn, "Overflow", 18f, UiKit.Faint, FontStyles.Normal,
                TextAlignmentOptions.TopRight);
        }

        /// <summary>
        /// Kolon başlığı tek satırda kalsın: "KIRMIZI (3)" kısa, ama sonuna kalibrasyon sayacı
        /// eklenince ("· 2 KALİBRESİZ") satır uzuyor. Sarmaya izin verilse ikinci satır
        /// <see cref="HeaderHeight"/> kutusunun dışında kalıp kırpılırdı; onun yerine punto
        /// küçülür, o da yetmezse kırpılır.
        /// </summary>
        private static void FitHeader(TextMeshProUGUI header)
        {
            // ⚠️ Tavan punto autosize AÇILMADAN ÖNCE okunur: açıldıktan sonra `fontSize` artık
            // istenen değil TMP'nin HESAPLADIĞI puntodur, oradan okumak tavanı kaydırır.
            float requested = header.fontSize;
            header.textWrappingMode = TextWrappingModes.NoWrap;
            header.overflowMode = TextOverflowModes.Ellipsis;
            header.enableAutoSizing = true;
            header.fontSizeMax = requested;
            header.fontSizeMin = Mathf.Max(8f, requested * 0.7f);
        }

        private void BuildBottomBar(RectTransform parent)
        {
            RectTransform bar = UiKit.Node(parent, "CameraBar");
            UiKit.Corner(bar, new Vector2(0.5f, 0f), new Vector2(0f, Margin), new Vector2(720f, 84f));

            string[] labels = { "1 POV", "2 SERBEST", "3 KUŞ BAKIŞI" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Button button = UiKit.Button(bar, $"Mode{i}", labels[i], 20f,
                    UiKit.CardTranslucent, UiKit.Title,
                    () => AdminSession.CameraMode = (AdminCameraMode)index, out _modeLabels[i]);

                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(i / 3f, 1f);
                rect.anchorMax = new Vector2((i + 1) / 3f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(i == 0 ? 0f : 4f, -48f);
                rect.offsetMax = new Vector2(i == 2 ? 0f : -4f, 0f);

                _modeButtons[i] = button.targetGraphic as Image;
            }

            _selectedText = UiKit.Text(bar, "Selected", 20f, UiKit.Accent, FontStyles.Bold,
                TextAlignmentOptions.Center);
            UiKit.Block(_selectedText.rectTransform, 0f, 52f, 0f, 26f);

            _hintText = UiKit.Text(parent, "Hint", 16f, UiKit.Faint, FontStyles.Normal,
                TextAlignmentOptions.BottomLeft);
            UiKit.Corner(_hintText.rectTransform, new Vector2(0f, 0f), new Vector2(Margin, Margin),
                new Vector2(560f, 44f));
            _hintText.text =
                "WASD/QE gez · sağ tuşu basılı tutup bak · tekerlek hız/zoom\n" +
                "1/2/3 kamera · Tab sonraki oyuncu · F seçiliye POV · P tercihler · I istatistik";
        }

        private void BuildKillFeed(RectTransform parent)
        {
            _killFeedText = UiKit.Text(parent, "KillFeed", 18f, UiKit.Muted, FontStyles.Normal,
                TextAlignmentOptions.BottomRight);
            UiKit.Corner(_killFeedText.rectTransform, new Vector2(1f, 0f),
                new Vector2(-Margin, Margin + 130f), new Vector2(420f, 200f));
        }

        // ---------------------------------------------------------------- tazeleme

        private void Refresh()
        {
            AdminRoster roster = AdminRoster.Instance;
            if (roster == null)
            {
                return;
            }

            RefreshTopBar(roster);
            RefreshColumns(roster);
            RefreshBottomBar(roster);
            RefreshKillFeed(roster);
        }

        private void RefreshTopBar(AdminRoster roster)
        {
            bool ffa = roster.IsFfa;

            _scoreRedText.text = ffa ? "" : roster.ScoreRed.ToString();
            _scoreBlueText.text = ffa ? "" : roster.ScoreBlue.ToString();
            _leaderboardText.text = ffa ? LeaderboardLine(roster) : "";

            if (roster.PhaseReason == ArenaProtocol.PAUSE_REASON_COUNTDOWN && roster.CountdownSeconds > 0)
            {
                _chipText.text = $"BAŞLIYOR {roster.CountdownSeconds}";
            }
            else if (roster.Phase == ArenaProtocol.PHASE_FINISHED)
            {
                _chipText.text = WinnerLabel(roster);
            }
            else if (roster.Phase == ArenaProtocol.PHASE_PLAYING)
            {
                _chipText.text = $"{FormatTime(roster.TimeRemaining)} · LIVE";
            }
            else
            {
                _chipText.text = PhaseLabel(roster.Phase, roster.PhaseReason);
            }

            // Lobi bekleyişinde admin bir arenayı yerel olarak ÖNİZLİYOR olabilir → sunucunun
            // bildirdiği sahne yerine gerçekten baktığımız sahneyi yaz.
            string activeScene = SceneManager.GetActiveScene().name;
            bool previewing = roster.PhaseReason == ArenaProtocol.PAUSE_REASON_LOBBY &&
                              activeScene != roster.SceneName &&
                              activeScene != AppSession.SceneLobby;
            string map = previewing
                ? $"{activeScene} (önizleme)"
                : string.IsNullOrEmpty(roster.SceneName) ? "-" : roster.SceneName;
            string mode = string.IsNullOrEmpty(roster.ModeId) ? "-" : AdminContent.ModeDisplayName(roster.ModeId);
            _matchInfoText.text = ffa ? $"{mode} · {map} · herkes tek" : $"{mode} · {map}";

            ArenaClient client = ArenaClient.Instance;
            bool connected = client != null && client.IsConnected;
            float age = roster.SnapshotAge;

            if (!connected)
            {
                _connectionDot.color = UiKit.Bad;
                _connectionText.text = AppSession.HasServerEndpoint
                    ? $"bağlı değil — {AppSession.ServerIp}:{AppSession.ServerPort}"
                    : "bağlı değil (adres yok)";
            }
            else
            {
                // Snapshot 1 sn'den eski ise poz akışı duruyor demektir (oyuncu yok ya da ağ sorunu).
                _connectionDot.color = age >= 0f && age <= 1f ? UiKit.Good : UiKit.Accent;
                _connectionText.text = $"{client.ServerIp}:{client.ServerPort}" +
                                       (age >= 0f ? $" · poz {age:0.0} sn" : " · poz yok");
            }

            RefreshAdminNotice(connected);
        }

        /// <summary>
        /// FFA lider tablosu: skora göre azalan ilk 3 (<c>ad · skor</c>). Takım skoru olmayan
        /// modda üst bandın tek anlamlı içeriği budur. Kimse puan almadıysa boş döner — sıfırlar
        /// dizisi bilgi taşımaz, yalnız yer kaplar.
        /// </summary>
        private string LeaderboardLine(AdminRoster roster)
        {
            _ranked.Clear();
            for (int i = 0; i < roster.Players.Count; i++)
            {
                _ranked.Add(roster.Players[i]);
            }

            if (_ranked.Count == 0)
            {
                return "";
            }

            _ranked.Sort(CompareByScoreDescending);
            if (_ranked[0].score <= 0)
            {
                return "";
            }

            _sb.Clear();
            int shown = Mathf.Min(3, _ranked.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                {
                    _sb.Append("   ");
                }

                _sb.Append($"{i + 1}. {_ranked[i].name} {_ranked[i].score}");
            }

            return _sb.ToString();
        }

        private static int CompareByScoreDescending(AdminPlayerView a, AdminPlayerView b)
        {
            int byScore = b.score.CompareTo(a.score);
            return byScore != 0 ? byScore : a.playerId.CompareTo(b.playerId);
        }

        /// <summary>Maç sonu başlığı: takım skorlu modda kazanan takım, bireysel skorlu modda
        /// kazanan oyuncu (§5.3 <c>match_end</c> iki kanalı).</summary>
        private static string WinnerLabel(AdminRoster roster)
        {
            if (roster.WinnerTeam == "red") return "KIRMIZI KAZANDI";
            if (roster.WinnerTeam == "blue") return "MAVİ KAZANDI";
            if (roster.WinnerPlayerId > 0) return $"{roster.NameOf(roster.WinnerPlayerId)} KAZANDI";
            return "BERABERE";
        }

        /// <summary>
        /// Çoklu operatör satırı: kaç admin bağlı + son admin eylemi (§5.3 <c>admin_state</c>).
        /// Tek admin varken ve duyuru yokken boş kalır — normal kullanımda hiç görünmez.
        /// </summary>
        private void RefreshAdminNotice(bool connected)
        {
            if (_adminNoticeText == null)
            {
                return;
            }

            if (!connected || (AdminSelection.AdminCount <= 1 && string.IsNullOrEmpty(AdminSelection.LastNotice)))
            {
                _adminNoticeText.text = "";
                return;
            }

            string peers = AdminSelection.AdminCount > 1 ? $"{AdminSelection.AdminCount} admin" : "";
            string notice = AdminSelection.LastNotice;
            _adminNoticeText.text = string.IsNullOrEmpty(notice)
                ? peers
                : string.IsNullOrEmpty(peers) ? notice : $"{peers} · {notice}";
        }

        private void RefreshColumns(AdminRoster roster)
        {
            if (roster.IsFfa)
            {
                _redHeader.text = $"OYUNCULAR ({roster.Players.Count}){CalibrationSuffix(roster.Players)}";
                _redHeader.color = UiKit.Title;
                BindColumn(_redRows, _redColumn, _redOverflow, roster.Players, false);

                _blueHeader.text = "";
                BindColumn(_blueRows, _blueColumn, _blueOverflow, null, true);
                return;
            }

            _redHeader.color = UiKit.TeamRed;
            _redHeader.text = $"KIRMIZI ({roster.Red.Count}){CalibrationSuffix(roster.Red)}";
            BindColumn(_redRows, _redColumn, _redOverflow, roster.Red, false);

            _blueHeader.text = $"MAVİ ({roster.Blue.Count}){CalibrationSuffix(roster.Blue)}";
            BindColumn(_blueRows, _blueColumn, _blueOverflow, roster.Blue, true);
        }

        /// <summary>
        /// Kolon başlığına "· N KALİBRESİZ" ekler (§10.6) — operatör tercihler panelini açmadan,
        /// hangi kolona bakması gerektiğini görsün. Kimse kalibresiz değilse hiçbir şey eklenmez:
        /// sürekli duran bir "0 kalibresiz" yazısı gürültüdür.
        /// </summary>
        private static string CalibrationSuffix(IReadOnlyList<AdminPlayerView> players)
        {
            if (players == null)
            {
                return "";
            }

            int count = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].NeedsCalibration)
                {
                    count++;
                }
            }

            return count > 0 ? $"  ·  {count} KALİBRESİZ" : "";
        }

        private void BindColumn(List<AdminPlayerRow> rows, RectTransform column,
            TextMeshProUGUI overflow, IReadOnlyList<AdminPlayerView> players, bool rightAligned)
        {
            int count = players != null ? Mathf.Min(players.Count, MaxRowsPerColumn) : 0;

            while (rows.Count < count)
            {
                var row = new AdminPlayerRow(column, HandleRowSelected, HandleRowPov);
                rows.Add(row);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (i >= count)
                {
                    rows[i].SetVisible(false);
                    continue;
                }

                rows[i].SetVisible(true);
                rows[i].Place(HeaderHeight + 6f + i * (AdminPlayerRow.Height + RowGap));
                rows[i].Bind(players[i], players[i].playerId == AdminSession.SelectedPlayerId);
            }

            int hidden = players != null ? players.Count - count : 0;
            overflow.text = hidden > 0 ? $"+{hidden} oyuncu daha (istatistiklerde)" : "";
            UiKit.Block(overflow.rectTransform,
                rightAligned ? 4f : 4f,
                HeaderHeight + 6f + count * (AdminPlayerRow.Height + RowGap),
                4f, 24f);
        }

        private void RefreshBottomBar(AdminRoster roster)
        {
            var active = (int)AdminSession.CameraMode;
            for (int i = 0; i < _modeButtons.Length; i++)
            {
                if (_modeButtons[i] != null)
                {
                    _modeButtons[i].color = i == active ? UiKit.Accent : UiKit.CardTranslucent;
                }

                if (_modeLabels[i] != null)
                {
                    _modeLabels[i].color = i == active ? UiKit.OnAccent : UiKit.Title;
                }
            }

            int selectedId = AdminSession.SelectedPlayerId;
            if (selectedId == 0)
            {
                _selectedText.text = roster.Players.Count == 0 ? "oyuncu bekleniyor" : "oyuncu seçilmedi";
                return;
            }

            AdminPlayerView view = roster.Find(selectedId);
            string name = view != null ? view.name : $"Oyuncu {selectedId}";

            bool hasPose = RemotePlayerRegistry.Instance != null &&
                           RemotePlayerRegistry.Instance.GetInterpolatedPose(selectedId, out _, out _, out _);
            _selectedText.text = AdminSession.CameraMode == AdminCameraMode.Pov && !hasPose
                ? $"{name} — poz yok"
                : name;
        }

        private void RefreshKillFeed(AdminRoster roster)
        {
            IReadOnlyList<string> feed = roster.KillFeed;
            if (feed.Count == 0)
            {
                _killFeedText.text = "";
                return;
            }

            _sb.Clear();
            for (int i = 0; i < feed.Count; i++)
            {
                _sb.AppendLine(feed[i]);
            }

            _killFeedText.text = _sb.ToString();
        }

        private static void TickRows(List<AdminPlayerRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Tick();
            }
        }

        // ------------------------------------------------------------- geri çağrı

        private void HandleRowSelected(int playerId)
        {
            AdminSession.SelectedPlayerId = playerId;
        }

        private void HandleRowPov(int playerId)
        {
            AdminSession.SelectedPlayerId = playerId;
            AdminSession.CameraMode = AdminCameraMode.Pov;
        }

        // ------------------------------------------------------------- biçimleme

        /// <summary>Durumu operatör metnine çevirir (§10.1). Faz tek başına yetmez: telde tek bir
        /// <c>paused</c> lobi de olabilir, yükleme/geri sayım/duraklatma da — gerekçe ayırır.</summary>
        private static string PhaseLabel(string phase, string phaseReason)
        {
            if (phase == ArenaProtocol.PHASE_PLAYING) return "LIVE";
            if (phase == ArenaProtocol.PHASE_FINISHED) return "MAÇ BİTTİ";

            switch (phaseReason)
            {
                case ArenaProtocol.PAUSE_REASON_LOADING: return "SAHNE YÜKLENİYOR";
                case ArenaProtocol.PAUSE_REASON_COUNTDOWN: return "BAŞLIYOR";
                case ArenaProtocol.PAUSE_REASON_OPERATOR: return "DURAKLATILDI";
                case ArenaProtocol.PAUSE_REASON_MODE: return "MOD BEKLİYOR";
                default: return "LOBİ";
            }
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
