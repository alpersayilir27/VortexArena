using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// İstatistikler paneli (skor bandının ortasındaki chip ya da <c>I</c> tuşu açar).
    /// Kart yarı saydamdır, arkasına scrim koyulmaz — canlı sahne izlenmeye devam eder.
    ///
    /// <para><b>Tablo neden kolon kolon çiziliyor:</b> TMP varsayılan fontu eşit genişlikli
    /// DEĞİL, tek metin bloğunda boşlukla hizalama kayar. Bu yüzden her kolon kendi TMP'sidir ve
    /// satırlar <c>\n</c> ile birleştirilir — hizalama font metriğinden bağımsız kalır.</para>
    ///
    /// <para><b>Uydurma metrik yok:</b> yalnız protokolde gerçekten taşınan veriler gösterilir
    /// (K/D/HP sunucudan — §5.3 <c>lobby_state</c>; batarya/sahne <c>status</c>'tan). Hasar,
    /// isabet oranı ve ping protokolde YOK, bu yüzden tabloda da yok.</para>
    /// </summary>
    public class AdminStatsPanel : MonoBehaviour
    {
        private const float PanelWidth = 1180f;
        private const float PanelHeight = 660f;
        private const float TableTop = 190f;
        private const float RefreshInterval = 0.5f;

        /// <summary>Kolon başlıkları ve genişlikleri (px) — sırayla soldan sağa.
        /// <c>SKOR</c> bireysel maç skorudur (§10.2) ve <c>K</c> ile aynı şey DEĞİLDİR: skoru mod
        /// yazar, öldürme başına 1 olmak zorunda değil.</summary>
        private static readonly string[] ColumnTitles =
            { "OYUNCU", "TAKIM", "SKOR", "K", "D", "K/D", "HP", "BATARYA", "DURUM", "SAHNE" };

        private static readonly float[] ColumnWidths =
            { 260f, 80f, 70f, 55f, 55f, 75f, 70f, 95f, 160f, 160f };

        /// <summary>FFA sıralaması için tampon — her tazelemede yeni liste ayırmamak adına.</summary>
        private readonly List<AdminPlayerView> _sorted = new List<AdminPlayerView>();

        // ⚠️ Alanlar [SerializeField] — görünüm PREFABTAN gelir
        // (`_Shared/App/Resources/UI/AdminStatsPanel.prefab`). Bu sınıf yalnız veri yazar.

        [Tooltip("Açılıp kapanan kart — panel kapalıyken bu obje devre dışı bırakılır.")]
        [SerializeField] private GameObject _root;
        [SerializeField] private Button _closeButton;
        [SerializeField] private TextMeshProUGUI _headline;
        [SerializeField] private TextMeshProUGUI _teamSummary;
        [SerializeField] private TextMeshProUGUI _matchSummary;

        [Tooltip("Tablo kolonları — ColumnTitles ile AYNI SIRADA ve aynı sayıda olmalı.")]
        [SerializeField] private TextMeshProUGUI[] _columns = new TextMeshProUGUI[ColumnTitles.Length];

        private readonly StringBuilder _sb = new StringBuilder();

        private float _nextRefresh;
        private bool _dirty = true;

        private void Start()
        {
            if (_closeButton != null)
            {
                // Prefabta kalıcı onClick kaydı YOKTUR (bkz. AdminPreferencesPanel.WireButtons).
                _closeButton.onClick.RemoveAllListeners();
                _closeButton.onClick.AddListener(AdminSession.ClosePanel);
            }

            if (_root != null)
            {
                _root.SetActive(false); // görünürlüğü Apply() belirler
            }

            Apply();
        }

        private void OnEnable()
        {
            AdminSession.Changed += MarkDirty;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed += MarkDirty;
            }
        }

        private void OnDisable()
        {
            AdminSession.Changed -= MarkDirty;

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
                Apply();
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        // ------------------------------------------------------------------ tazeleme

        private void Apply()
        {
            if (_root == null)
            {
                return;
            }

            bool open = AdminSession.OpenPanel == AdminPanelKind.Stats;
            if (_root.activeSelf != open)
            {
                _root.SetActive(open);
            }

            AdminRoster roster = AdminRoster.Instance;
            if (!open || roster == null)
            {
                return;
            }

            RefreshSummary(roster);
            RefreshTable(OrderedPlayers(roster));
            RefreshMatchInfo(roster);
        }

        /// <summary>
        /// Tablo sırası: takımlı modda roster sırası (playerId) korunur — operatör oyuncuyu hep
        /// aynı satırda arar. FFA'da tek sıralama ölçütü skordur, bu yüzden tablo skora göre
        /// AZALAN dizilir (eşitlikte playerId ile kararlı kalır).
        /// </summary>
        private IReadOnlyList<AdminPlayerView> OrderedPlayers(AdminRoster roster)
        {
            if (!roster.IsFfa)
            {
                return roster.Players;
            }

            _sorted.Clear();
            for (int i = 0; i < roster.Players.Count; i++)
            {
                _sorted.Add(roster.Players[i]);
            }

            _sorted.Sort(CompareByScoreDescending);
            return _sorted;
        }

        private static int CompareByScoreDescending(AdminPlayerView a, AdminPlayerView b)
        {
            int byScore = b.score.CompareTo(a.score);
            return byScore != 0 ? byScore : a.playerId.CompareTo(b.playerId);
        }

        private void RefreshSummary(AdminRoster roster)
        {
            if (roster.IsFfa)
            {
                // Takım yok → tek anlamlı başlık lider. Skor hiç yazılmadıysa (maç başlamadı)
                // uydurma yapmayız, "herkes tek" der geçeriz.
                IReadOnlyList<AdminPlayerView> ranked = OrderedPlayers(roster);
                _headline.text = ranked.Count > 0 && ranked[0].score > 0
                    ? $"LİDER: {ranked[0].name} {ranked[0].score}"
                    : "HERKES TEK";
                _teamSummary.text = $"{roster.Players.Count} oyuncu · {AliveCount(roster.Players)} canlı";
                return;
            }

            _headline.text = $"KIRMIZI {roster.ScoreRed} — {roster.ScoreBlue} MAVİ";

            roster.TeamTotals("red", out int redKills, out int redDeaths, out int redAlive);
            roster.TeamTotals("blue", out int blueKills, out int blueDeaths, out int blueAlive);

            _sb.Clear();
            _sb.AppendLine($"KIRMIZI: {roster.Red.Count} oyuncu · {redAlive} canlı · {redKills} öldürme · {redDeaths} ölüm");
            _sb.Append($"MAVİ: {roster.Blue.Count} oyuncu · {blueAlive} canlı · {blueKills} öldürme · {blueDeaths} ölüm");
            _teamSummary.text = _sb.ToString();
        }

        private void RefreshTable(IReadOnlyList<AdminPlayerView> players)
        {
            for (int c = 0; c < _columns.Length; c++)
            {
                _sb.Clear();

                for (int i = 0; i < players.Count; i++)
                {
                    if (i > 0)
                    {
                        _sb.AppendLine();
                    }

                    _sb.Append(CellText(players[i], c));
                }

                _columns[c].text = _sb.ToString();
            }
        }

        private static string CellText(AdminPlayerView view, int column)
        {
            switch (column)
            {
                case 0: return $"{view.name} #{view.playerId}";
                case 1: return view.team == "red" ? "kırmızı" : view.team == "blue" ? "mavi" : "-";
                case 2: return view.score.ToString();
                case 3: return view.kills.ToString();
                case 4: return view.deaths.ToString();
                case 5: return view.deaths > 0
                    ? (view.kills / (float)view.deaths).ToString("0.00")
                    : view.kills.ToString("0.00");
                case 6: return Mathf.RoundToInt(view.hp).ToString();
                case 7: return view.battery < 0f
                    ? "-"
                    : $"%{Mathf.RoundToInt(Mathf.Clamp01(view.battery) * 100f)}";
                case 8: return StateText(view);
                default: return string.IsNullOrEmpty(view.scene) ? "-" : view.scene;
            }
        }

        private static string StateText(AdminPlayerView view)
        {
            if (!view.online)
            {
                return "çevrimdışı";
            }

            if (!view.alive)
            {
                float remaining = view.RespawnRemaining;
                return remaining > 0.1f ? $"ölü ({Mathf.CeilToInt(remaining)} sn)" : "tabanda bekliyor";
            }

            return view.ready ? "hazır" : "bekliyor";
        }

        private void RefreshMatchInfo(AdminRoster roster)
        {
            ArenaClient client = ArenaClient.Instance;
            string endpoint = client != null && client.IsConnected
                ? $"{client.ServerIp}:{client.ServerPort}"
                : "bağlı değil";
            float age = roster.SnapshotAge;
            string mode = string.IsNullOrEmpty(roster.ModeId) ? "-" : AdminContent.ModeDisplayName(roster.ModeId);
            string map = string.IsNullOrEmpty(roster.SceneName) ? "-" : roster.SceneName;

            _sb.Clear();
            _sb.AppendLine($"Faz: {roster.Phase} · kalan {FormatTime(roster.TimeRemaining)} · " +
                           $"mod {mode} · harita {map}" +
                           // Süre/limit o maça özel olabildiği için (§5.2) operatör seçtiği
                           // değerin gerçekten uygulandığını buradan doğrular.
                           (roster.RoundSeconds > 0 ? $" · raund {AdminCommands.FormatDuration(roster.RoundSeconds)}" : "") +
                           (roster.ScoreLimit > 0 ? $" · skor limiti {roster.ScoreLimit}" : ""));
            _sb.Append($"Sunucu: {endpoint} · poz akışı " +
                       (age >= 0f ? $"{age:0.0} sn önce" : "yok") +
                       $" · bağlı admin {roster.AdminCount}");
            _matchSummary.text = _sb.ToString();
        }

        private static int AliveCount(IReadOnlyList<AdminPlayerView> players)
        {
            int count = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].online && players[i].alive)
                {
                    count++;
                }
            }

            return count;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
