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
    /// <para><b>Görünüm prefabtan gelir:</b>
    /// <c>Assets/_Shared/App/Resources/UI/AdminHud.prefab</c>. Bu sınıf yalnız <b>davranış</b>tır
    /// (veri bağlama + tazeleme); yerleşim, renk, punto ve hangi öge nerede duracağı prefabta
    /// düzenlenir. <see cref="AdminSpectator"/> prefabı <c>Resources.Load</c> ile yükleyip
    /// örnekler — sahneye KONMAZ, böylece yeni arena eklerken hiçbir kurulum adımı doğmaz.</para>
    ///
    /// <para><b>Yerleşim</b> (prefabın taşıdığı tasarım):
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
    /// üstünü kaplar — bağlantı yoksa gösterilecek canlı veri de yoktur. (Prefabın Canvas
    /// bileşeninde durur; değiştirilirse iki ekranın sırası bozulur.)</para>
    ///
    /// <para>Tazeleme olay güdümlüdür (<see cref="AdminRoster.Changed"/>,
    /// <see cref="AdminSession.Changed"/>); yalnız zamana bağlı alanlar (süre, ölüm geri sayımı,
    /// snapshot yaşı) <see cref="RefreshInterval"/> ile ~4 Hz tazelenir.</para>
    /// </summary>
    public class AdminHud : MonoBehaviour
    {
        /// <summary>Prefabın <c>Resources</c> içindeki yolu (uzantısız).</summary>
        public const string ResourcePath = "UI/AdminHud";

        /// <summary>Zamana bağlı alanların tazeleme aralığı (sn).</summary>
        private const float RefreshInterval = 0.25f;

        /// <summary>Skorların ortasındaki chip'in SABİT etiketi. Chip bir düğmedir; üstünde ne
        /// yaptığı yazar, maçın fazı değil.</summary>
        private const string ChipLabelText = "İSTATİSTİK";

        [Header("Oyuncu satırı")]
        [Tooltip("Kolonlara örneklenecek satır prefabı (Resources/UI/AdminPlayerRow).")]
        [SerializeField] private AdminPlayerRow rowPrefab;

        [Tooltip("Kolon başına gösterilen en fazla satır; fazlası \"+N daha\" ile özetlenir.")]
        [SerializeField] private int maxRowsPerColumn = 6;

        [Tooltip("İki satır arasındaki boşluk (px).")]
        [SerializeField] private float rowGap = 8f;

        [Tooltip("Kolon başlığının yüksekliği (px) — ilk satır bunun altından başlar.")]
        [SerializeField] private float headerHeight = 28f;

        [Header("Üst bant")]
        [SerializeField] private TextMeshProUGUI scoreRedText;
        [SerializeField] private TextMeshProUGUI scoreBlueText;
        [Tooltip("FFA lider tablosu satırı; takımlı modda boş kalır.")]
        [SerializeField] private TextMeshProUGUI leaderboardText;
        [Tooltip("Skorların ortasındaki chip: faz/süre yazar, tıklanınca istatistikleri açar.")]
        [SerializeField] private TextMeshProUGUI chipText;
        [SerializeField] private TextMeshProUGUI matchInfoText;
        [SerializeField] private TextMeshProUGUI connectionText;
        [Tooltip("Başka bir adminin son eylemi (admin_state.notice).")]
        [SerializeField] private TextMeshProUGUI adminNoticeText;
        [SerializeField] private Image connectionDot;

        [Header("Kolonlar")]
        [Tooltip("Kırmızı takım kolonu; FFA'da tek kolon olarak kullanılır.")]
        [SerializeField] private RectTransform redColumn;
        [SerializeField] private RectTransform blueColumn;
        [SerializeField] private TextMeshProUGUI redHeader;
        [SerializeField] private TextMeshProUGUI blueHeader;
        [SerializeField] private TextMeshProUGUI redOverflow;
        [SerializeField] private TextMeshProUGUI blueOverflow;

        [Header("Alt şerit")]
        [Tooltip("Kamera kipi düğmelerinin ZEMİNLERİ — sıra: POV, SERBEST, KUŞ BAKIŞI.")]
        [SerializeField] private Image[] modeButtons = new Image[3];
        [Tooltip("Kamera kipi düğmelerinin ETİKETLERİ — modeButtons ile aynı sırada.")]
        [SerializeField] private TextMeshProUGUI[] modeLabels = new TextMeshProUGUI[3];
        [SerializeField] private Button[] modeButtonTargets = new Button[3];
        [SerializeField] private TextMeshProUGUI selectedText;
        [SerializeField] private TextMeshProUGUI killFeedText;

        [Tooltip("İhlal akışı (§10.9) — kill feed'den AYRI bir metin alanı olmalıdır: " +
                 "kill feed maçın hikâyesi, bu operatörün iş listesidir.")]
        [SerializeField] private TextMeshProUGUI violationFeedText;

        [Header("Düğmeler")]
        [SerializeField] private Button preferencesButton;
        [SerializeField] private Button statsChipButton;

        private readonly List<AdminPlayerRow> _redRows = new List<AdminPlayerRow>();
        private readonly List<AdminPlayerRow> _blueRows = new List<AdminPlayerRow>();

        /// <summary>FFA lider tablosu sıralama tamponu (her tazelemede liste ayırmamak için).</summary>
        private readonly List<AdminPlayerView> _ranked = new List<AdminPlayerView>();

        private float _nextRefresh;
        private bool _dirty = true;
        private readonly StringBuilder _sb = new StringBuilder();
        private float _rowHeight = -1f;

        /// <summary>
        /// Satır yüksekliği prefabtan okunur — sanatçı satırı büyütürse kolon yerleşimi uyar.
        /// Prefab yoksa/ölçüsü anlamsızsa sabit yedeğe düşer.
        /// </summary>
        private float RowHeight
        {
            get
            {
                if (_rowHeight > 1f)
                {
                    return _rowHeight;
                }

                float fromPrefab = rowPrefab != null
                    ? ((RectTransform)rowPrefab.transform).rect.height
                    : 0f;
                _rowHeight = fromPrefab > 1f ? fromPrefab : AdminPlayerRow.Height;
                return _rowHeight;
            }
        }

        private void Awake()
        {
            // ⚠ Arena sahnelerinde EventSystem HİÇ YOK (yalnız Lobby'de var) — garanti altına al,
            // yoksa HUD düğmeleri sessizce ölür.
            UiKit.EnsureEventSystem();

            WireButtons();
        }

        /// <summary>
        /// Prefabtaki düğmelere geri çağrıları bağlar. <b>Prefabta kalıcı (persistent) onClick
        /// kaydı YOKTUR:</b> hedef statik değil (seçili oyuncu/panel durumu değişiyor) ve
        /// inspector'dan bağlanan bir kayıt kod tarafındaki koşulları atlardı.
        /// </summary>
        private void WireButtons()
        {
            if (preferencesButton != null)
            {
                preferencesButton.onClick.RemoveAllListeners();
                preferencesButton.onClick.AddListener(() => AdminSession.TogglePanel(AdminPanelKind.Preferences));
            }

            if (statsChipButton != null)
            {
                statsChipButton.onClick.RemoveAllListeners();
                statsChipButton.onClick.AddListener(() => AdminSession.TogglePanel(AdminPanelKind.Stats));
            }

            for (int i = 0; i < modeButtonTargets.Length; i++)
            {
                if (modeButtonTargets[i] == null)
                {
                    continue;
                }

                int index = i;
                modeButtonTargets[i].onClick.RemoveAllListeners();
                modeButtonTargets[i].onClick.AddListener(
                    () => AdminSession.CameraMode = (AdminCameraMode)index);
            }
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
            RefreshViolationFeed(roster);
        }

        private void RefreshTopBar(AdminRoster roster)
        {
            bool ffa = roster.IsFfa;

            if (scoreRedText != null)
            {
                scoreRedText.text = ffa ? "" : roster.ScoreRed.ToString();
            }

            if (scoreBlueText != null)
            {
                scoreBlueText.text = ffa ? "" : roster.ScoreBlue.ToString();
            }

            if (leaderboardText != null)
            {
                leaderboardText.text = ffa ? LeaderboardLine(roster) : "";
            }

            if (chipText != null)
            {
                // ⚠️ Chip bir DÜĞMEDİR ve üstünde ne yaptığı yazar — faz adı DEĞİL.
                // Faz metni ("LOBİ", kazanan…) buradan kaldırıldı: operatör hangi haritanın açık
                // olduğunu zaten sahneden görüyor ve düğmenin etiketi değişken olduğunda
                // "istatistikler nerede" sorusu her fazda yeniden soruluyordu. Kaybolmaması gereken
                // tek şey SÜRE/GERİ SAYIMdır; o da sağ üst maç satırına taşındı (RefreshMatchInfo).
                chipText.text = ChipLabelText;
            }

            RefreshMatchInfo(roster, ffa);
            RefreshConnection(roster);
        }

        /// <summary>
        /// Maç satırının sonuna eklenen ZAMAN bilgisi — geri sayım, kalan süre ya da kazanan.
        /// <para>Faz ADI bilinçli olarak yazılmaz (lobide boş döner): operatör hangi haritanın açık
        /// olduğunu sahneden görüyor, "LOBİ" yazısı yalnız yer kaplıyordu. Yazılan tek şey
        /// <b>kendiliğinden bilinemeyecek</b> olandır — kaç saniye kaldığı.</para>
        /// </summary>
        private static string StatusSuffix(AdminRoster roster)
        {
            if (roster.PhaseReason == ArenaProtocol.PAUSE_REASON_COUNTDOWN && roster.CountdownSeconds > 0)
            {
                return $"BAŞLIYOR {roster.CountdownSeconds}";
            }

            if (roster.Phase == ArenaProtocol.PHASE_FINISHED)
            {
                return WinnerLabel(roster);
            }

            if (roster.Phase == ArenaProtocol.PHASE_PLAYING)
            {
                return $"{FormatTime(roster.TimeRemaining)} · LIVE";
            }

            // Duraklatma bir OPERATÖR kararıdır ve maçın koştuğunu sanmak pahalıdır → yazılır.
            // Lobi ve yükleme yazılmaz: ikisi de sahneden zaten görülüyor.
            return PhaseLabel(roster.Phase, roster.PhaseReason) is { Length: > 0 } label &&
                   roster.Phase == ArenaProtocol.PHASE_PAUSED &&
                   roster.PhaseReason != ArenaProtocol.PAUSE_REASON_LOBBY
                ? label
                : "";
        }

        private void RefreshMatchInfo(AdminRoster roster, bool ffa)
        {
            if (matchInfoText == null)
            {
                return;
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
            string line = ffa ? $"{mode} · {map} · herkes tek" : $"{mode} · {map}";

            // Süre/geri sayım chip'ten buraya taşındı (bkz. StatusSuffix): chip artık sabit bir
            // düğme etiketi taşıyor ve zaman bilgisinin kaybolmaması gerekiyor.
            string status = StatusSuffix(roster);
            matchInfoText.text = status.Length > 0 ? $"{line} · {status}" : line;
        }

        private void RefreshConnection(AdminRoster roster)
        {
            ArenaClient client = ArenaClient.Instance;
            bool connected = client != null && client.IsConnected;
            float age = roster.SnapshotAge;

            if (!connected)
            {
                if (connectionDot != null)
                {
                    connectionDot.color = UiKit.Bad;
                }

                if (connectionText != null)
                {
                    connectionText.text = AppSession.HasServerEndpoint
                        ? $"bağlı değil — {AppSession.ServerIp}:{AppSession.ServerPort}"
                        : "bağlı değil (adres yok)";
                }
            }
            else
            {
                if (connectionDot != null)
                {
                    // Snapshot 1 sn'den eski ise poz akışı duruyor demektir (oyuncu yok ya da ağ sorunu).
                    connectionDot.color = age >= 0f && age <= 1f ? UiKit.Good : UiKit.Accent;
                }

                if (connectionText != null)
                {
                    connectionText.text = $"{client.ServerIp}:{client.ServerPort}" +
                                          (age >= 0f ? $" · poz {age:0.0} sn" : " · poz yok");
                }
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
            if (adminNoticeText == null)
            {
                return;
            }

            if (!connected || (AdminSelection.AdminCount <= 1 && string.IsNullOrEmpty(AdminSelection.LastNotice)))
            {
                adminNoticeText.text = "";
                return;
            }

            string peers = AdminSelection.AdminCount > 1 ? $"{AdminSelection.AdminCount} admin" : "";
            string notice = AdminSelection.LastNotice;
            adminNoticeText.text = string.IsNullOrEmpty(notice)
                ? peers
                : string.IsNullOrEmpty(peers) ? notice : $"{peers} · {notice}";
        }

        private void RefreshColumns(AdminRoster roster)
        {
            if (roster.IsFfa)
            {
                if (redHeader != null)
                {
                    redHeader.text = $"OYUNCULAR ({roster.Players.Count}){CalibrationSuffix(roster.Players)}";
                    redHeader.color = UiKit.Title;
                }

                BindColumn(_redRows, redColumn, redOverflow, roster.Players);

                if (blueHeader != null)
                {
                    blueHeader.text = "";
                }

                BindColumn(_blueRows, blueColumn, blueOverflow, null);
                return;
            }

            if (redHeader != null)
            {
                redHeader.color = UiKit.TeamRed;
                redHeader.text = $"KIRMIZI ({roster.Red.Count}){CalibrationSuffix(roster.Red)}";
            }

            BindColumn(_redRows, redColumn, redOverflow, roster.Red);

            if (blueHeader != null)
            {
                blueHeader.text = $"MAVİ ({roster.Blue.Count}){CalibrationSuffix(roster.Blue)}";
            }

            BindColumn(_blueRows, blueColumn, blueOverflow, roster.Blue);
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
            TextMeshProUGUI overflow, IReadOnlyList<AdminPlayerView> players)
        {
            if (column == null)
            {
                return;
            }

            int count = players != null ? Mathf.Min(players.Count, maxRowsPerColumn) : 0;

            while (rows.Count < count)
            {
                if (rowPrefab == null)
                {
                    Debug.LogWarning("[AdminHud] rowPrefab atanmadı; oyuncu satırları çizilemiyor.");
                    break;
                }

                AdminPlayerRow row = Instantiate(rowPrefab, column);
                row.Initialize(HandleRowSelected, HandleRowPov);
                rows.Add(row);
            }

            float height = RowHeight;
            for (int i = 0; i < rows.Count; i++)
            {
                if (i >= count)
                {
                    rows[i].SetVisible(false);
                    continue;
                }

                rows[i].SetVisible(true);
                rows[i].Place(headerHeight + 6f + i * (height + rowGap), height);
                rows[i].Bind(players[i], players[i].playerId == AdminSession.SelectedPlayerId);
            }

            if (overflow == null)
            {
                return;
            }

            int hidden = players != null ? players.Count - count : 0;
            overflow.text = hidden > 0 ? $"+{hidden} oyuncu daha (istatistiklerde)" : "";
            UiKit.Block(overflow.rectTransform, 4f,
                headerHeight + 6f + count * (height + rowGap), 4f, 24f);
        }

        private void RefreshBottomBar(AdminRoster roster)
        {
            var active = (int)AdminSession.CameraMode;
            for (int i = 0; i < modeButtons.Length; i++)
            {
                if (modeButtons[i] != null)
                {
                    modeButtons[i].color = i == active ? UiKit.Accent : UiKit.CardTranslucent;
                }

                if (i < modeLabels.Length && modeLabels[i] != null)
                {
                    modeLabels[i].color = i == active ? UiKit.OnAccent : UiKit.Title;
                }
            }

            if (selectedText == null)
            {
                return;
            }

            int selectedId = AdminSession.SelectedPlayerId;
            if (selectedId == 0)
            {
                selectedText.text = roster.Players.Count == 0 ? "oyuncu bekleniyor" : "oyuncu seçilmedi";
                return;
            }

            AdminPlayerView view = roster.Find(selectedId);
            string name = view != null ? view.name : $"Oyuncu {selectedId}";

            bool hasPose = RemotePlayerRegistry.Instance != null &&
                           RemotePlayerRegistry.Instance.GetInterpolatedPose(selectedId, out _, out _, out _);
            selectedText.text = AdminSession.CameraMode == AdminCameraMode.Pov && !hasPose
                ? $"{name} — poz yok"
                : name;
        }

        private void RefreshKillFeed(AdminRoster roster)
        {
            if (killFeedText == null)
            {
                return;
            }

            IReadOnlyList<string> feed = roster.KillFeed;
            if (feed.Count == 0)
            {
                killFeedText.text = "";
                return;
            }

            _sb.Clear();
            for (int i = 0; i < feed.Count; i++)
            {
                _sb.AppendLine(feed[i]);
            }

            killFeedText.text = _sb.ToString();
        }

        /// <summary>
        /// İhlal akışı (§10.9). ⚠️ Kill feed'in İÇİNE yazılmaz ve alanı onunla paylaşmaz: ikisi
        /// farklı sorulara cevap veriyor (maçta ne oldu / operatörün şimdi ne yapması gerekiyor)
        /// ve tek bir listede ikisi de okunmaz olur.
        /// <para>Alan prefabta bağlanmamış olabilir — o durumda akış sessizce çizilmez, HUD'ın
        /// geri kalanı çalışmaya devam eder (panellerdeki eksik bağ deseninin aynısı).</para>
        /// </summary>
        private void RefreshViolationFeed(AdminRoster roster)
        {
            if (violationFeedText == null)
            {
                return;
            }

            IReadOnlyList<string> feed = roster.ViolationFeed;
            if (feed.Count == 0)
            {
                violationFeedText.text = "";
                return;
            }

            _sb.Clear();
            for (int i = 0; i < feed.Count; i++)
            {
                _sb.AppendLine(feed[i]);
            }

            violationFeedText.text = _sb.ToString();
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
