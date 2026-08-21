using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// The admin spectator's on-screen management UI — a <b>persistent</b> screen-space canvas that
    /// survives lobby ↔ arena transitions, so the operator never loses the interface.
    /// <para><b>Look comes from the prefab</b>
    /// (<c>Assets/_Shared/App/Resources/UI/AdminHud.prefab</c>); this class is behaviour only (data
    /// binding + refresh). <see cref="AdminSpectator"/> loads and instantiates it via
    /// <c>Resources.Load</c> — it is never placed in a scene, so adding an arena needs no setup
    /// step.</para>
    /// <para><b>sortingOrder = 4000</b> (on the prefab's Canvas): the connection error screen stays
    /// at 5000 and may cover the HUD — without a connection there is no live data to show. Changing
    /// it breaks the order of the two screens.</para>
    /// <para>Refresh is event driven (<see cref="AdminRoster.Changed"/>,
    /// <see cref="AdminSession.Changed"/>); only time-dependent fields (respawn countdown) tick at
    /// <see cref="RefreshInterval"/> (~4 Hz).</para>
    /// </summary>
    public class AdminHud : MonoBehaviour
    {
        /// <summary>Prefab path inside <c>Resources</c> (no extension).</summary>
        public const string ResourcePath = "UI/AdminHud";

        /// <summary>Refresh interval of time-dependent fields (s).</summary>
        private const float RefreshInterval = 0.25f;

        /// <summary>FIXED label of the chip between the scores. The chip is a button, so it says
        /// what it does — not the match phase.</summary>
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

        [Header("Kolonlar")]
        [Tooltip("Kırmızı takım kolonu; FFA'da tek kolon olarak kullanılır.")]
        [SerializeField] private RectTransform redColumn;
        [SerializeField] private RectTransform blueColumn;
        [SerializeField] private TextMeshProUGUI redHeader;
        [SerializeField] private TextMeshProUGUI blueHeader;
        [SerializeField] private TextMeshProUGUI redOverflow;
        [SerializeField] private TextMeshProUGUI blueOverflow;

        [Header("Kamera kipi (sağ üst)")]
        [Tooltip("Kamera kipi düğmelerinin ZEMİNLERİ — sıra AdminCameraMode ile aynı: " +
                 "POV, SERBEST, KUŞ BAKIŞI. ⚠️ POV yuvası (0) BOŞ bırakılır — POV'a oyuncu " +
                 "kartındaki düğmeden (HandleRowPov) ve klavyeden girilir; şeritte POV düğmesi " +
                 "yoktur. Dizi yine de enum indeksli kalır: kaydırmak SERBEST/KUŞ BAKIŞI'nı " +
                 "yanlış kipe bağlardı.")]
        [SerializeField] private Image[] modeButtons = new Image[3];
        [Tooltip("Kamera kipi düğmelerinin ETİKETLERİ — modeButtons ile aynı sırada.")]
        [SerializeField] private TextMeshProUGUI[] modeLabels = new TextMeshProUGUI[3];
        [SerializeField] private Button[] modeButtonTargets = new Button[3];
        [SerializeField] private TextMeshProUGUI killFeedText;

        [Tooltip("İhlal akışı (§10.9) — kill feed'den AYRI bir metin alanı olmalıdır: " +
                 "kill feed maçın hikâyesi, bu operatörün iş listesidir.")]
        [SerializeField] private TextMeshProUGUI violationFeedText;

        [Header("Düğmeler")]
        [SerializeField] private Button preferencesButton;
        [SerializeField] private Button statsChipButton;

        private readonly List<AdminPlayerRow> _redRows = new List<AdminPlayerRow>();
        private readonly List<AdminPlayerRow> _blueRows = new List<AdminPlayerRow>();

        /// <summary>Sort buffer for the FFA leaderboard (avoids a list allocation per refresh).</summary>
        private readonly List<AdminPlayerView> _ranked = new List<AdminPlayerView>();

        private float _nextRefresh;
        private bool _dirty = true;
        private readonly StringBuilder _sb = new StringBuilder();
        private float _rowHeight = -1f;

        /// <summary>
        /// Row height read from the prefab so resizing the row reflows the column; falls back to
        /// the constant when the prefab is missing or its size is meaningless.
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
            // ⚠ Arena scenes have NO EventSystem (only the lobby does) — guarantee one here, or the
            // HUD buttons die silently.
            UiKit.EnsureEventSystem();

            WireButtons();
        }

        /// <summary>
        /// Wires callbacks to the prefab's buttons. ⚠️ <b>No persistent <c>onClick</c> entries in
        /// the prefab:</b> the target is not static (selected player/panel state change) and an
        /// inspector-bound entry would skip the conditions applied here.
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
            AdminSelection.Changed += MarkDirty; // another admin's action/selection
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
            RefreshCameraBar();
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
                // ⚠️ The chip is a BUTTON and says what it does — never the phase name. A varying
                // label made "where are the stats" a per-phase question.
                chipText.text = ChipLabelText;
            }
        }

        /// <summary>
        /// FFA leaderboard: top 3 by score (<c>name · score</c>) — the only meaningful top-band
        /// content in a mode without team scores. Empty while nobody scored: a row of zeroes
        /// carries no information.
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
        /// Appends "· N KALİBRESİZ" to the column header (§10.6) so the operator sees which column
        /// needs attention without opening the preferences panel. Nothing is appended at zero — a
        /// permanent "0 uncalibrated" is noise.
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

        /// <summary>Colours the camera mode buttons. ⚠️ Slot 0 (POV) is empty — that mode is entered
        /// only from a player card or the keyboard; the loop is null-safe, so the empty slot is
        /// skipped.</summary>
        private void RefreshCameraBar()
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
        /// Violation feed (§10.9). ⚠️ Never written INTO the kill feed and never shares its field:
        /// they answer different questions (what happened in the match / what the operator must do
        /// now) and merged into one list neither stays readable.
        /// <para>The field may be unbound in the prefab — then the feed silently draws nothing and
        /// the rest of the HUD keeps working (same pattern as the panels).</para>
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

        // ------------------------------------------------------------- callbacks

        private void HandleRowSelected(int playerId)
        {
            AdminSession.SelectedPlayerId = playerId;
        }

        private void HandleRowPov(int playerId)
        {
            AdminSession.SelectedPlayerId = playerId;
            AdminSession.CameraMode = AdminCameraMode.Pov;
        }
    }
}
