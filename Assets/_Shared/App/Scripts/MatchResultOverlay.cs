using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Maç sonu ekranı: maç bittiğinde (<c>match_end</c>) oyun içi HUD'ları gizleyip önce
    /// <b>sonuç kartını</b> (KAZANDIN / KAYBETTİN / BERABERE), ardından <b>skor tablosunu</b>
    /// gösterir; operatör yeni maç başlatınca ya da lobiye dönülünce kendini kapatır ve HUD'ları
    /// geri verir.
    /// <para>
    /// <b>Faz sözleşmesi (§10.1):</b> maç sonu ekranı kendiliğinden kapanmaz — <c>finished</c>
    /// fazından çıkaran şey operatörün seçimidir (<c>load_match</c> / <c>return_to_lobby</c> /
    /// başka bir <c>match_state</c>). Bu yüzden burada "birkaç saniye sonra kapan" diye bir sayaç
    /// YOKTUR; tek sayaç sonuç kartından skor tablosuna geçiştir.
    /// </para>
    /// <para>
    /// <b>Skor tablosu <c>AdminStatsPanel</c>'in kartıdır</b> — aynı tasarım, aynı kolon sözleşmesi,
    /// yalnız operatöre ait teşhis kolonları (HP · BATARYA · DURUM · SAHNE · PING) olmadan.
    /// Kolonlar tek tek TMP'dir ve satırlar <c>\n</c> ile birleştirilir: TMP varsayılan fontu eşit
    /// genişlikli DEĞİL, tek metin bloğunda boşlukla hizalama kayardı.
    /// </para>
    /// <para>
    /// <b>Mod bilmez.</b> Kazananı <c>match_end</c>'in iki kanalı söyler (takım ya da oyuncu,
    /// §5.3), tablo sırasını <see cref="ModeRuntime.IsTeamless"/> ayırır — burada
    /// <c>if (modeId == "…")</c> zinciri YAZILMAZ, yeni mod bu ekranı bedavaya alır.
    /// </para>
    /// <para>
    /// Kendini önyükleyen kalıcı tekildir (<c>AmmoHud</c> deseni): sahneye KONMAZ, yoksa her yeni
    /// arenaya elle bir kurulum adımı doğardı. Görünüm tümüyle prefabtadır
    /// (<c>Resources/UI/MatchResultOverlay</c>) — bu sınıf yalnız veri yazar.
    /// </para>
    /// <para>
    /// ⚠️ <b>Rol kapısı gösterim anındadır, önyüklemede değil:</b> <c>AppSession.Role</c> Boot
    /// sahnesinde çözülür ve <c>AfterSceneLoad</c> önyüklemesiyle sıralaması garanti değildir.
    /// Admin gözlemcide kazanan ve tablo zaten <c>AdminHud</c>'da çizilir, bu ekran ona hiç
    /// açılmaz.
    /// </para>
    /// </summary>
    public class MatchResultOverlay : MonoBehaviour
    {
        /// <summary>Prefabın <c>Resources</c> içindeki yolu (uzantısız).</summary>
        public const string ResourcePath = "UI/MatchResultOverlay";

        /// <summary>Kolon sırası — soldan sağa; <see cref="CellText"/>'in <c>switch</c> sırasını ve
        /// <see cref="boardColumns"/>'un beklenen uzunluğunu belgeler. Başlık metinleri ve
        /// genişlikler PREFABTA yaşar (<c>AdminStatsPanel</c> ile aynı sözleşme).
        /// <para>⚠️ Buraya kolon eklemek YETMEZ: prefabta da bir TMP objesi açıp diziye bağlamak
        /// gerekir, yoksa yeni kolon sessizce hiç çizilmez.</para>
        /// <para>⚠️ <c>K</c> ve <c>D</c> başlıkları prefabta METİN DEĞİL İKONDUR (crosshair /
        /// skull) — admin kartındaki gibi; ilgili <c>Header</c> objelerinin metni bilerek boştur.</para>
        /// <para>⚠️ Operatöre ait teşhis kolonları (HP · BATARYA · DURUM · SAHNE · PING) burada
        /// YOKTUR ve eklenmez: maç sonunda oyuncuya canlı cihaz durumu gösterilmez, o tablo
        /// admin'in.</para></summary>
        private static readonly string[] ColumnOrder = { "OYUNCU", "TAKIM", "SKOR", "K", "D", "K/D" };

        private enum Stage
        {
            Hidden,
            Result,
            Scoreboard
        }

        private static MatchResultOverlay _instance;

        [Header("Paneller")]
        [Tooltip("Sonuç kartı (KAZANDIN / KAYBETTİN / BERABERE).")]
        [SerializeField] private GameObject resultPanel;
        [Tooltip("Skor tablosu kartı (AdminStatsPanel tasarımı).")]
        [SerializeField] private GameObject scoreboardPanel;

        [Tooltip("Sonuç kartından skor tablosuna geçiş süresi (sn).")]
        [SerializeField] private float resultSeconds = 6f;

        [Header("Sonuç kartı")]
        [SerializeField] private TextMeshProUGUI resultTitleText;
        [Tooltip("Kazananın adı/takımı — berabere bittiğinde boş kalır.")]
        [SerializeField] private TextMeshProUGUI resultWinnerText;
        [SerializeField] private TextMeshProUGUI resultScoreText;

        [Header("Skor tablosu")]
        [Tooltip("Kartın turuncu başlığı: kazanan + skor.")]
        [SerializeField] private TextMeshProUGUI boardHeadlineText;
        [Tooltip("Takım toplamları (FFA'da oyuncu/canlı sayısı).")]
        [SerializeField] private TextMeshProUGUI boardTeamSummaryText;
        [Tooltip("Kartın alt bandı: mod/harita + oyuncunun kendi özeti.")]
        [SerializeField] private TextMeshProUGUI boardMatchSummaryText;
        [Tooltip("Tablo kolonları — ColumnOrder ile AYNI SIRADA ve aynı sayıda olmalı.")]
        [SerializeField] private TextMeshProUGUI[] boardColumns = new TextMeshProUGUI[ColumnOrder.Length];

        private readonly List<PlayerInfo> _ranked = new List<PlayerInfo>();
        private readonly StringBuilder _sb = new StringBuilder();

        private PlayerInfo[] _roster = Array.Empty<PlayerInfo>();
        private MatchEndMsg _lastEnd;
        private Stage _stage = Stage.Hidden;
        private float _scoreboardAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var prefab = Resources.Load<MatchResultOverlay>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"[MatchResultOverlay] '{ResourcePath}' prefabı bulunamadı — maç " +
                               "sonu ekranı çizilemeyecek.");
                return;
            }

            MatchResultOverlay overlay = Instantiate(prefab);
            overlay.name = "[MatchResultOverlay]";
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

            // Domain reload'suz Play girişinden kalan bayat bir "gizli" durumu taşımayalım.
            HideAll();
        }

        private void OnEnable()
        {
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnDisconnected += HandleDisconnected;
        }

        private void OnDisable()
        {
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnDisconnected -= HandleDisconnected;
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            // Ekran yok oluyorsa HUD'ları kapalı bırakma.
            GameplayHudGate.SetHidden(false);
            _instance = null;
        }

        private void Update()
        {
            if (_stage != Stage.Result || Time.unscaledTime < _scoreboardAt)
            {
                return;
            }

            ShowScoreboard();
        }

        // -------------------------------------------------------- ağ olay işleyiciler

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null || AppSession.Role != AppSession.RolePlayer)
            {
                return;
            }

            _lastEnd = msg;
            ShowResult(msg);
        }

        /// <summary>Faz <c>finished</c>'ten çıktıysa ekran kapanır: yeni maç yüklendi, geri sayım
        /// başladı, operatör duraklattı ya da lobiye dönüldü (§10.1).</summary>
        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg != null && msg.phase != ArenaProtocol.PHASE_FINISHED)
            {
                HideAll();
            }
        }

        private void HandleLoadMatch(LoadMatchMsg _)
        {
            HideAll();
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg _)
        {
            HideAll();
        }

        private void HandleDisconnected()
        {
            HideAll();
        }

        /// <summary>Tablo roster'dan beslenir (§10.2): sayaçlar sunucu-otoriterdir ve maç sonu
        /// katılımcıları (<c>left</c> olanlar dahil) <c>finished</c> fazı boyunca listede kalır.
        /// ⚠️ Bağlantı durumuna göre SÜZME YOKTUR — admin tablosuyla aynı gerekçe: oyundan
        /// çıkarılmış satır maç sonu tablosunda görünmeli.</summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            _roster = msg?.players ?? Array.Empty<PlayerInfo>();

            if (_stage == Stage.Scoreboard)
            {
                RefreshScoreboard();
            }
        }

        // ------------------------------------------------------------------ gösterim

        private void ShowResult(MatchEndMsg msg)
        {
            SetPanel(resultPanel, true);
            SetPanel(scoreboardPanel, false);

            bool draw;
            bool won = Won(msg, out draw);

            if (resultTitleText != null)
            {
                resultTitleText.text = draw ? "BERABERE" : won ? "KAZANDIN" : "KAYBETTİN";
                resultTitleText.color = draw ? UiKit.Title : won ? UiKit.Good : UiKit.Bad;
            }

            SetText(resultWinnerText, WinnerLine(msg));
            SetText(resultScoreText, ScoreLine(msg));

            _stage = Stage.Result;
            _scoreboardAt = Time.unscaledTime + Mathf.Max(0f, resultSeconds);
            GameplayHudGate.SetHidden(true);
        }

        private void ShowScoreboard()
        {
            SetPanel(resultPanel, false);
            SetPanel(scoreboardPanel, true);

            _stage = Stage.Scoreboard;
            GameplayHudGate.SetHidden(true);
            RefreshScoreboard();
        }

        private void HideAll()
        {
            SetPanel(resultPanel, false);
            SetPanel(scoreboardPanel, false);

            _stage = Stage.Hidden;
            _lastEnd = null;
            GameplayHudGate.SetHidden(false);
        }

        private static void SetPanel(GameObject panel, bool visible)
        {
            if (panel != null && panel.activeSelf != visible)
            {
                panel.SetActive(visible);
            }
        }

        // --------------------------------------------------------------- skor tablosu

        private void RefreshScoreboard()
        {
            RankPlayers();
            RefreshHeadline();
            RefreshTeamSummary();
            RefreshMatchSummary();
            RefreshTable();
        }

        /// <summary>
        /// Tablo sırası <c>AdminStatsPanel</c> ile aynıdır: takımlı modda roster sırası
        /// (<c>playerId</c>) korunur — oyuncu kendini hep aynı satırda arar; FFA'da tek sıralama
        /// ölçütü skordur, tablo skora göre AZALAN dizilir (eşitlikte <c>playerId</c> ile kararlı).
        /// </summary>
        private void RankPlayers()
        {
            _ranked.Clear();

            for (int i = 0; i < _roster.Length; i++)
            {
                PlayerInfo info = _roster[i];
                if (info != null && info.role != "admin")
                {
                    _ranked.Add(info);
                }
            }

            if (!ModeRuntime.IsTeamless)
            {
                return;
            }

            _ranked.Sort(CompareByScoreDescending);
        }

        private static int CompareByScoreDescending(PlayerInfo a, PlayerInfo b)
        {
            int byScore = b.score.CompareTo(a.score);
            return byScore != 0 ? byScore : a.playerId.CompareTo(b.playerId);
        }

        private void RefreshTable()
        {
            if (boardColumns == null)
            {
                return;
            }

            for (int c = 0; c < boardColumns.Length; c++)
            {
                if (boardColumns[c] == null)
                {
                    continue;
                }

                _sb.Clear();

                for (int i = 0; i < _ranked.Count; i++)
                {
                    if (i > 0)
                    {
                        _sb.AppendLine();
                    }

                    _sb.Append(CellText(_ranked[i], c));
                }

                boardColumns[c].text = _sb.ToString();
            }
        }

        /// <summary>Hücre metinleri <c>AdminStatsPanel.CellText</c> ile birebir aynıdır — aynı
        /// oyuncu iki tabloda farklı görünmemeli.</summary>
        private static string CellText(PlayerInfo info, int column)
        {
            switch (column)
            {
                case 0: return $"{info.name} #{info.playerId}";
                case 1: return info.team == "red" ? "kırmızı" : info.team == "blue" ? "mavi" : "-";
                case 2: return info.score.ToString();
                case 3: return info.kills.ToString();
                case 4: return info.deaths.ToString();
                case 5: return info.deaths > 0
                    ? (info.kills / (float)info.deaths).ToString("0.00")
                    : info.kills.ToString("0.00");
                // Prefabta ColumnOrder'dan FAZLA kolon bağlanmışsa boş kalsın.
                default: return "";
            }
        }

        /// <summary>Kartın turuncu başlığı: kazanan + skor. Skor kısmı <c>AdminStatsPanel</c>'in
        /// başlığıyla aynıdır (takımlı modda takım skoru, FFA'da lider).</summary>
        private void RefreshHeadline()
        {
            string winner = _lastEnd != null ? WinnerLine(_lastEnd) : "";
            string score = SummaryScoreLine();

            if (winner.Length == 0)
            {
                SetText(boardHeadlineText, score);
                return;
            }

            SetText(boardHeadlineText, score.Length == 0 ? winner : $"{winner}   ·   {score}");
        }

        private string SummaryScoreLine()
        {
            if (ModeRuntime.IsTeamless)
            {
                // Takım yok → tek anlamlı başlık lider. Skor hiç yazılmadıysa (maç başlamadı)
                // uydurma yapmayız.
                return _ranked.Count > 0 && _ranked[0].score > 0
                    ? $"LİDER: {_ranked[0].name} {_ranked[0].score}"
                    : "HERKES TEK";
            }

            return _lastEnd != null
                ? $"KIRMIZI {_lastEnd.scoreRed} — {_lastEnd.scoreBlue} MAVİ"
                : "";
        }

        private void RefreshTeamSummary()
        {
            if (boardTeamSummaryText == null)
            {
                return;
            }

            if (ModeRuntime.IsTeamless)
            {
                boardTeamSummaryText.text = $"{_ranked.Count} oyuncu · {AliveCount(null)} canlı";
                return;
            }

            TeamTotals("red", out int redCount, out int redAlive, out int redKills, out int redDeaths);
            TeamTotals("blue", out int blueCount, out int blueAlive, out int blueKills, out int blueDeaths);

            _sb.Clear();
            _sb.AppendLine($"KIRMIZI: {redCount} oyuncu · {redAlive} canlı · {redKills} öldürme · {redDeaths} ölüm");
            _sb.Append($"MAVİ: {blueCount} oyuncu · {blueAlive} canlı · {blueKills} öldürme · {blueDeaths} ölüm");
            boardTeamSummaryText.text = _sb.ToString();
        }

        /// <summary>Kartın alt bandı. Admin kartında burada sunucu teşhisi vardır; oyuncunun
        /// karşılığı maçın kimliği + kendi özetidir.</summary>
        private void RefreshMatchSummary()
        {
            if (boardMatchSummaryText == null)
            {
                return;
            }

            string mode = string.IsNullOrEmpty(ModeRuntime.ModeId) ? "-" : ModeRuntime.ModeId;
            string map = SceneManager.GetActiveScene().name;

            _sb.Clear();
            _sb.AppendLine($"Mod: {mode} · Harita: {map}");

            PlayerInfo self = FindSelf();
            _sb.Append(self == null
                ? ""
                : $"SEN: {self.kills} öldürme · {self.deaths} ölüm · K/D {CellText(self, 5)}");

            boardMatchSummaryText.text = _sb.ToString();
        }

        /// <summary><paramref name="team"/> <c>null</c> ise tüm oyuncular sayılır.</summary>
        private int AliveCount(string team)
        {
            int count = 0;
            for (int i = 0; i < _ranked.Count; i++)
            {
                PlayerInfo info = _ranked[i];
                if (info.alive && info.connection != ArenaProtocol.CONNECTION_LEFT &&
                    (team == null || info.team == team))
                {
                    count++;
                }
            }

            return count;
        }

        private void TeamTotals(string team, out int count, out int alive, out int kills, out int deaths)
        {
            count = 0;
            kills = 0;
            deaths = 0;

            for (int i = 0; i < _ranked.Count; i++)
            {
                PlayerInfo info = _ranked[i];
                if (info.team != team)
                {
                    continue;
                }

                count++;
                kills += info.kills;
                deaths += info.deaths;
            }

            alive = AliveCount(team);
        }

        // ------------------------------------------------------------------- metinler

        /// <summary>Yerel oyuncu kazandı mı. Kazanan iki kanaldan gelir (§5.3): takım skorlu modda
        /// <c>winnerTeam</c>, bireysel skorlu modda <c>winnerPlayerId</c>; ikisi de boşsa berabere.</summary>
        private static bool Won(MatchEndMsg msg, out bool draw)
        {
            if (msg.winnerTeam == "red" || msg.winnerTeam == "blue")
            {
                draw = false;
                Team local = ArenaCombat.LocalTeam;
                return (msg.winnerTeam == "red" && local == Team.Red) ||
                       (msg.winnerTeam == "blue" && local == Team.Blue);
            }

            if (msg.winnerPlayerId > 0)
            {
                draw = false;
                int self = ArenaCombat.LocalPlayerId;
                return self != 0 && msg.winnerPlayerId == self;
            }

            draw = true;
            return false;
        }

        private string WinnerLine(MatchEndMsg msg)
        {
            if (msg.winnerTeam == "red")
            {
                return "KIRMIZI KAZANDI";
            }

            if (msg.winnerTeam == "blue")
            {
                return "MAVİ KAZANDI";
            }

            return msg.winnerPlayerId > 0 ? $"{NameOf(msg.winnerPlayerId)} KAZANDI" : "";
        }

        /// <summary>Sonuç kartının skor satırı: takım skorlu modda takım skoru, bireysel skorlu
        /// modda oyuncunun kendi skoru (<c>scoreRed</c>/<c>scoreBlue</c> o modlarda hep 0'dır,
        /// §10.2).</summary>
        private string ScoreLine(MatchEndMsg msg)
        {
            if (ModeRuntime.Scoring != ModeScoreKind.Player)
            {
                return $"KIRMIZI {msg.scoreRed} — {msg.scoreBlue} MAVİ";
            }

            PlayerInfo self = FindSelf();
            return self != null ? $"SENİN SKORUN {self.score}" : "";
        }

        private PlayerInfo FindSelf()
        {
            int self = ArenaCombat.LocalPlayerId;
            if (self == 0)
            {
                return null;
            }

            for (int i = 0; i < _roster.Length; i++)
            {
                if (_roster[i] != null && _roster[i].playerId == self)
                {
                    return _roster[i];
                }
            }

            return null;
        }

        /// <summary>playerId → ad (roster'dan); bilinmiyorsa "Oyuncu N".</summary>
        private string NameOf(int playerId)
        {
            for (int i = 0; i < _roster.Length; i++)
            {
                if (_roster[i] != null && _roster[i].playerId == playerId &&
                    !string.IsNullOrEmpty(_roster[i].name))
                {
                    return _roster[i].name;
                }
            }

            return $"Oyuncu {playerId}";
        }

        private static void SetText(TextMeshProUGUI target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }
    }
}
