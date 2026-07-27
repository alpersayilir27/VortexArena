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

        /// <summary>Kolon başlıkları ve genişlikleri (px) — sırayla soldan sağa.</summary>
        private static readonly string[] ColumnTitles =
            { "OYUNCU", "TAKIM", "K", "D", "K/D", "HP", "BATARYA", "DURUM", "SAHNE" };

        private static readonly float[] ColumnWidths =
            { 280f, 90f, 60f, 60f, 80f, 80f, 100f, 170f, 180f };

        private GameObject _root;
        private TextMeshProUGUI _headline;
        private TextMeshProUGUI _teamSummary;
        private TextMeshProUGUI _matchSummary;
        private readonly TextMeshProUGUI[] _columns = new TextMeshProUGUI[ColumnTitles.Length];
        private readonly StringBuilder _sb = new StringBuilder();

        private float _nextRefresh;
        private bool _dirty = true;

        public void Initialize(RectTransform parent)
        {
            Build(parent);
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

        // ------------------------------------------------------------------ kurulum

        private void Build(RectTransform parent)
        {
            Image card = UiKit.Panel(parent, "StatsPanel", UiKit.CardTranslucent, UiKit.Border);
            _root = card.transform.parent.gameObject;
            card.raycastTarget = true;
            UiKit.Center((RectTransform)_root.transform, new Vector2(PanelWidth, PanelHeight));

            Transform body = card.transform;

            TextMeshProUGUI title = UiKit.Text(body, "Title", 30f, UiKit.Title, FontStyles.Bold,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(title.rectTransform, 28f, 22f, 160f, 38f);
            title.text = "İSTATİSTİKLER";
            title.characterSpacing = 3f;

            Button close = UiKit.Button(body, "Close", "KAPAT", 18f, UiKit.Hex(0x2A303B, 0xFF),
                UiKit.Muted, AdminSession.ClosePanel, out _);
            UiKit.Corner((RectTransform)close.transform, new Vector2(1f, 1f),
                new Vector2(-24f, -24f), new Vector2(110f, 34f));

            _headline = UiKit.Text(body, "Headline", 34f, UiKit.Accent, FontStyles.Bold,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_headline.rectTransform, 28f, 66f, 160f, 42f);

            _teamSummary = UiKit.Text(body, "TeamSummary", 20f, UiKit.Muted, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            UiKit.Block(_teamSummary.rectTransform, 28f, 112f, 28f, 52f);
            _teamSummary.lineSpacing = 8f;

            Image divider = UiKit.Solid(body, "Divider", UiKit.Border);
            UiKit.Block(divider.rectTransform, 28f, TableTop - 26f, 28f, 1f);

            // Kolonlar: her biri kendi TMP'si (hizalama font metriğinden bağımsız kalsın).
            float x = 28f;
            for (int i = 0; i < ColumnTitles.Length; i++)
            {
                TextMeshProUGUI header = UiKit.Text(body, $"Header{i}", 16f, UiKit.Faint,
                    FontStyles.Bold, TextAlignmentOptions.TopLeft);
                UiKit.Corner(header.rectTransform, new Vector2(0f, 1f), new Vector2(x, -(TableTop - 22f)),
                    new Vector2(ColumnWidths[i], 20f));
                header.text = ColumnTitles[i];

                _columns[i] = UiKit.Text(body, $"Column{i}", 18f, UiKit.Title, FontStyles.Normal,
                    TextAlignmentOptions.TopLeft);
                UiKit.Corner(_columns[i].rectTransform, new Vector2(0f, 1f), new Vector2(x, -TableTop),
                    new Vector2(ColumnWidths[i], 380f));
                _columns[i].lineSpacing = 14f;
                _columns[i].textWrappingMode = TextWrappingModes.NoWrap;
                _columns[i].overflowMode = TextOverflowModes.Ellipsis;

                x += ColumnWidths[i];
            }

            _matchSummary = UiKit.Text(body, "MatchSummary", 18f, UiKit.Muted, FontStyles.Normal,
                TextAlignmentOptions.BottomLeft);
            UiKit.Corner(_matchSummary.rectTransform, new Vector2(0f, 0f), new Vector2(28f, 24f),
                new Vector2(PanelWidth - 80f, 76f));
            _matchSummary.lineSpacing = 8f;

            _root.SetActive(false);
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
            RefreshTable(roster.Players);
            RefreshMatchInfo(roster);
        }

        private void RefreshSummary(AdminRoster roster)
        {
            if (roster.IsFfa)
            {
                _headline.text = "HERKES TEK";
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
                case 2: return view.kills.ToString();
                case 3: return view.deaths.ToString();
                case 4: return view.deaths > 0
                    ? (view.kills / (float)view.deaths).ToString("0.00")
                    : view.kills.ToString("0.00");
                case 5: return Mathf.RoundToInt(view.hp).ToString();
                case 6: return view.battery < 0f
                    ? "-"
                    : $"%{Mathf.RoundToInt(Mathf.Clamp01(view.battery) * 100f)}";
                case 7: return StateText(view);
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
