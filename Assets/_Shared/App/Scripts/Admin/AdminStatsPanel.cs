using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Stats panel (opened by the chip between the scores or the <c>I</c> key). The card is
    /// translucent with no scrim behind it — the live scene stays watchable.
    /// <para><b>Why row based:</b> the panel is not a read-only table but the screen where the
    /// operator does <i>record keeping</i> (fixing names, body measurement, calibration reload).
    /// Each player is a row (<see cref="AdminStatsRow"/>): alignment comes from layout, actions sit
    /// on the row and the list scrolls — <b>no cap</b>, everyone is drawn.</para>
    /// <para><b>Column count follows the mode</b> (<see cref="AdminRoster.IsFfa"/>): a team mode
    /// splits the list in two — KIRMIZI left, MAVİ right, each with the full K/D/score/ping/device
    /// set — while HERKES TEK collapses to one full-width column ranked by score. The split column
    /// is half as wide, so it takes the <b>narrow row variant</b> (icon buttons, stacked lines);
    /// the wide row does not fit there and would draw its halves on top of each other.</para>
    /// <para>⚠️ <b>Team-less players stay visible</b> in a team mode — see
    /// <see cref="BuildLeftColumn"/>; they are the whole roster during Lobby.</para>
    /// <para><b>No invented metrics:</b> only data actually on the wire (K/D/score from §5.3
    /// <c>lobby_state</c>; battery/controllers from <c>status</c>; ping = client-measured RTT,
    /// §6.7 <c>net_stats</c>). ⚠️ <b>Damage and accuracy are not in the protocol, so not here
    /// either.</b> Jitter and packet loss are measured but not shown: ping is the only number the
    /// operator can act on.</para>
    /// <para>⚠️ <b>HP, scene and violations are deliberately absent from the row</b> — reasoning in
    /// the <see cref="AdminStatsRow"/> class doc; do not "restore" them.</para>
    /// <para><b>The TWO calibration buttons on the bottom bar are opposite jobs:</b>
    /// <c>TÜMÜNÜ KALİBRE ET</c> <i>reloads</i> alignment from the headset record (undoable, benches
    /// nobody, no friction) · <c>HİZALAMALARI SIFIRLA</c> benches everyone. ⚠️ The reset is ONE
    /// button carrying BOTH modes (<see cref="HoldButton"/>): a tap KEEPS the device record so
    /// reload still works afterwards, a 1 s hold destroys it too — then <b>reload no longer
    /// works</b> and players must redo the A/B sequence by hand. Severity comes from press
    /// DURATION, never from picking the right neighbour. Its friction is threefold: the button is
    /// small and red-lettered, the hard mode needs a full uninterrupted second, and the fill walks to
    /// red for that whole time (sliding off aborts). The same one-button rule holds per player
    /// (<see cref="AdminPlayerRow"/> KAL, <see cref="AdminStatsRow"/> SIFIRLA).</para>
    /// <para><b>Look comes from the prefab</b>
    /// (<c>_Shared/App/Resources/UI/AdminStatsPanel.prefab</c>); this class only writes data and
    /// places rows with <see cref="UiKit.Block"/>.</para>
    /// </summary>
    public class AdminStatsPanel : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;

        /// <summary>Auto-close time of the popup (s), so a forgotten one does not sit on the list
        /// forever.</summary>
        private const float PopupSeconds = 6f;

        /// <summary>Shared idle fill of the bottom bar buttons; the reset button walks from here
        /// to <see cref="UiKit.Bad"/> while its hard-reset hold runs.</summary>
        private static readonly Color ClearAllIdleFill = UiKit.Hex(0x334557, 0xF2);

        /// <summary>FFA sort buffer (avoids a list allocation per refresh).</summary>
        private readonly List<AdminPlayerView> _sorted = new List<AdminPlayerView>();

        /// <summary>Row pools: instances are hidden, never destroyed (same pattern as
        /// <see cref="AdminHud"/>) — Instantiate/Destroy per refresh causes GC and layout jitter.
        /// <para>Three pools because two prefabs are in play: the wide row fills the single FFA
        /// column, the narrow variant fills each team column. ⚠️ A pool is <b>never</b> reparented
        /// between columns — a row carries a half-typed name and armed confirm windows, and moving
        /// it would hand them to another player.</para></summary>
        private readonly List<AdminStatsRow> _wideRows = new List<AdminStatsRow>();

        /// <inheritdoc cref="_wideRows"/>
        private readonly List<AdminStatsRow> _redRows = new List<AdminStatsRow>();

        /// <inheritdoc cref="_wideRows"/>
        private readonly List<AdminStatsRow> _blueRows = new List<AdminStatsRow>();

        /// <summary>Rows bound in the last refresh, across every column. <see cref="Tick"/>, the
        /// bulk actions and the calibration result walk THIS, not a pool: which pool is live
        /// depends on the mode.</summary>
        private readonly List<AdminStatsRow> _live = new List<AdminStatsRow>();

        /// <summary>Left column buffer: red team + the team-less. See <see cref="BuildLeftColumn"/>.</summary>
        private readonly List<AdminPlayerView> _leftColumn = new List<AdminPlayerView>();

        // ⚠️ All fields are [SerializeField] — the look comes from the PREFAB, this class only
        // writes data.

        [Tooltip("Açılıp kapanan kart — panel kapalıyken bu obje devre dışı bırakılır.")]
        [SerializeField] private GameObject _root;
        [SerializeField] private Button _closeButton;
        [SerializeField] private TextMeshProUGUI _headline;
        [SerializeField] private TextMeshProUGUI _teamSummary;
        [SerializeField] private TextMeshProUGUI _matchSummary;

        [Header("Oyuncu listesi")]
        [Tooltip("HERKES TEK kipinin tam genişlik satırı.")]
        [SerializeField] private AdminStatsRow _rowPrefab;
        [Tooltip("Takımlı kipin dar sütun satırı (AdminStatsRow'un ikon düğmeli varyantı). " +
                 "Boşsa panel tek sütuna düşer.")]
        [SerializeField] private AdminStatsRow _narrowRowPrefab;
        [Tooltip("ScrollRect'in content'i — sütunlar buranın altındadır, yüksekliği koddan sürülür.")]
        [SerializeField] private RectTransform _rowContainer;
        [Tooltip("Sol sütun: takımlı kipte KIRMIZI, HERKES TEK kipinde tüm panel genişliği.")]
        [SerializeField] private RectTransform _redColumn;
        [Tooltip("Sağ sütun: yalnız takımlı kipte açılır (MAVİ).")]
        [SerializeField] private RectTransform _blueColumn;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private float _rowGap = 6f;
        [Tooltip("İki sütun arasındaki boşluk (px) — sütun başlıklarına da aynısı uygulanır.")]
        [SerializeField] private float _columnGap = 24f;

        [Header("Sütun başlıkları")]
        [Tooltip("HERKES TEK kipinin tek başlık şeridi (OYUNCU · K/D).")]
        [SerializeField] private RectTransform _wideHeader;
        [Tooltip("Takımlı kipin başlık çifti; iki çocuğu sütunlarla aynı genişliğe sürülür.")]
        [SerializeField] private RectTransform _teamHeaders;
        [SerializeField] private RectTransform _redHeader;
        [SerializeField] private RectTransform _blueHeader;
        [Tooltip("Sol sütunun adı — takımsız oyuncu varsa sayısını da yazar.")]
        [SerializeField] private TextMeshProUGUI _redHeaderLabel;
        [SerializeField] private TextMeshProUGUI _blueHeaderLabel;

        [Header("Toplu eylemler")]
        [SerializeField] private Button _calibrateAllButton;
        [SerializeField] private Button _measureAllButton;
        [Tooltip("Herkesin kalibrasyonunu sıfırlar — TEK düğme: kısa basış o anki hizalamaları " +
                 "düşürür, 1 sn basılı tutmak cihazlardaki KAYITLI çapaları da sildirir " +
                 "(ardından TÜMÜNÜ KALİBRE ET çalışmaz).")]
        [SerializeField] private Button _clearAllButton;
        [SerializeField] private TextMeshProUGUI _clearAllLabel;

        [Header("Uyarı penceresi")]
        [SerializeField] private GameObject _popupRoot;
        [SerializeField] private TextMeshProUGUI _popupText;
        [SerializeField] private Button _popupCloseButton;

        private readonly StringBuilder _sb = new StringBuilder();

        private float _nextRefresh;
        private bool _dirty = true;

        /// <summary>Row heights read from the prefabs (fallback <see cref="AdminStatsRow.Height"/>),
        /// so resizing a row reflows its list.</summary>
        private float _wideRowHeight = -1f;

        /// <inheritdoc cref="_wideRowHeight"/>
        private float _narrowRowHeight = -1f;

        /// <summary>When the popup closes (<c>Time.unscaledTime</c>); &lt; 0 = closed.</summary>
        private float _popupUntil = -1f;

        /// <summary>How long the bulk wipe confirmation stays up (s) — same as the stats row's
        /// TAMAM/HATA hold, so a result reads the same on both screens.</summary>
        private const float PurgedHoldSeconds = 2f;

        /// <summary>Press-duration gate of the bulk reset; null when the button is unwired.</summary>
        private HoldButton _clearAllHold;

        /// <summary>When the device records were wiped (<c>Time.unscaledTime</c>); &lt; 0 = no
        /// confirmation showing. ⚠️ <b>Required:</b> the hold has no other ending — without it the
        /// button snaps back and a completed wipe reads like one aborted by sliding off.</summary>
        private float _purgedAllAt = -1f;

        /// <summary>Was the bulk reset painted in its "holding" look last frame — without it the
        /// button would stay red after the press ends.</summary>
        private bool _clearAllHoldPainted;

        private float WideRowHeight => HeightOf(ref _wideRowHeight, _rowPrefab);

        private float NarrowRowHeight => HeightOf(ref _narrowRowHeight, NarrowPrefab);

        /// <summary>Narrow prefab with a fallback to the wide one: an unassigned field must split
        /// the list into columns that are merely cramped, not empty.</summary>
        private AdminStatsRow NarrowPrefab => _narrowRowPrefab != null ? _narrowRowPrefab : _rowPrefab;

        /// <summary>Two columns need two teams to fill them; without the narrow prefab the row
        /// would not fit half the width either, so the panel stays single column.</summary>
        private bool SplitByTeam => _blueColumn != null && _narrowRowPrefab != null;

        private static float HeightOf(ref float cache, AdminStatsRow prefab)
        {
            if (cache > 1f)
            {
                return cache;
            }

            float fromPrefab = prefab != null ? ((RectTransform)prefab.transform).rect.height : 0f;
            cache = fromPrefab > 1f ? fromPrefab : AdminStatsRow.Height;
            return cache;
        }

        private void Start()
        {
            // ⚠️ No persistent onClick entries in the prefab (see AdminPreferencesPanel.WireButtons).
            Wire(_closeButton, AdminSession.ClosePanel);
            Wire(_calibrateAllButton, ReloadAllCalibrations);
            Wire(_measureAllButton, () => AdminCommands.MeasureBodyScale(0));
            // ⚠️ NOT Wire(): onClick fires on pointer-up whatever the duration, so a completed
            // hold would also send the soft reset — everyone would be reset twice.
            _clearAllHold = HoldButton.Attach(_clearAllButton, ClearAllCalibration, PurgeAllCalibration);
            Wire(_popupCloseButton, HidePopup);

            if (_root != null)
            {
                _root.SetActive(false); // Apply() decides visibility
            }

            HidePopup();
            Apply();
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private void OnEnable()
        {
            AdminSession.Changed += MarkDirty;
            AdminRoster.CalibrationResult += HandleCalibrationResult;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed += MarkDirty;
            }
        }

        private void OnDisable()
        {
            AdminSession.Changed -= MarkDirty;
            AdminRoster.CalibrationResult -= HandleCalibrationResult;

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

            // Confirm windows and the calibration timeout advance PER FRAME: on the refresh
            // interval the countdown would twitch in half-second steps.
            for (int i = 0; i < _live.Count; i++)
            {
                _live[i].Tick();
            }

            if (_popupUntil >= 0f && Time.unscaledTime >= _popupUntil)
            {
                HidePopup();
            }

            // Repaints every frame WHILE HELD: the fill ramp is the progress bar and the panel's
            // own refresh runs at 4 Hz, which would make it step.
            if (_clearAllHold != null && (_clearAllHold.IsPressed || _clearAllHoldPainted))
            {
                ApplyClearAllButton();
            }

            if (_purgedAllAt >= 0f)
            {
                // ⚠️ The window starts when the FINGER LIFTS — see AdminPlayerRow.Tick for why.
                if (_clearAllHold != null && _clearAllHold.IsPressed)
                {
                    _purgedAllAt = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _purgedAllAt > PurgedHoldSeconds)
                {
                    _purgedAllAt = -1f;
                    ApplyClearAllButton();
                }
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        // ------------------------------------------------------------------ refresh

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

                // Always opens at the TOP of the list: a carried-over scroll position could land
                // the operator in empty space after the list shrank.
                if (open && _scroll != null)
                {
                    _scroll.verticalNormalizedPosition = 1f;
                }
            }

            // ⚠️ A press in flight never survives closing: reopening mid-hold would let the wipe
            // land on a panel the operator is no longer looking at.
            if (!open)
            {
                _clearAllHold?.Cancel();
                _purgedAllAt = -1f;
            }

            // BEFORE the roster: the destructive button must look right even with no player list.
            ApplyClearAllButton();

            AdminRoster roster = AdminRoster.Instance;
            if (!open || roster == null)
            {
                return;
            }

            // ⚠️ Layout BEFORE the rows: the rows stretch to their column, so a column still at
            // last frame's width would place this frame's rows over the wrong half.
            bool split = SplitByTeam && !roster.IsFfa;
            ApplyColumnLayout(split);

            RefreshSummary(roster);
            RefreshRows(roster, split);
            RefreshColumnHeaders(roster, split);
            RefreshMatchInfo(roster);
        }

        /// <summary>Names the two columns. Runs AFTER the rows: the team-less count comes from the
        /// left column they were just placed in.</summary>
        private void RefreshColumnHeaders(AdminRoster roster, bool split)
        {
            if (!split)
            {
                return;
            }

            if (_redHeaderLabel != null)
            {
                int loose = _leftColumn.Count - roster.Red.Count;
                _redHeaderLabel.text = loose > 0 ? $"KIRMIZI · +{loose} TAKIMSIZ" : "KIRMIZI";
                _redHeaderLabel.color = UiKit.TeamRed;
            }

            if (_blueHeaderLabel != null)
            {
                _blueHeaderLabel.text = "MAVİ";
                _blueHeaderLabel.color = UiKit.TeamBlue;
            }
        }

        /// <summary>
        /// Splits the list into two columns (KIRMIZI left, MAVİ right) or collapses it back to one.
        /// <para>The column extents are driven from here rather than authored, so the gutter is one
        /// number (<see cref="_columnGap"/>) and the header pair can never drift away from the
        /// columns it labels.</para>
        /// </summary>
        private void ApplyColumnLayout(bool split)
        {
            float gutter = Mathf.Max(0f, _columnGap) * 0.5f;

            SetActive(_blueColumn, split);
            SetActive(_teamHeaders, split);
            SetActive(_wideHeader, !split);

            if (split)
            {
                SpanHorizontal(_redColumn, 0f, 0.5f, 0f, gutter);
                SpanHorizontal(_redHeader, 0f, 0.5f, 0f, gutter);
                SpanHorizontal(_blueColumn, 0.5f, 1f, gutter, 0f);
                SpanHorizontal(_blueHeader, 0.5f, 1f, gutter, 0f);
                return;
            }

            SpanHorizontal(_redColumn, 0f, 1f, 0f, 0f);
        }

        private static void SetActive(Component target, bool active)
        {
            if (target != null && target.gameObject.activeSelf != active)
            {
                target.gameObject.SetActive(active);
            }
        }

        /// <summary>Horizontal extent inside the parent; the vertical anchors are left alone so a
        /// column keeps stretching over the scroll content and a header keeps its own band.</summary>
        private static void SpanHorizontal(RectTransform rect, float anchorLeft, float anchorRight,
            float padLeft, float padRight)
        {
            if (rect == null)
            {
                return;
            }

            Vector2 anchorMin = rect.anchorMin;
            anchorMin.x = anchorLeft;
            rect.anchorMin = anchorMin;

            Vector2 anchorMax = rect.anchorMax;
            anchorMax.x = anchorRight;
            rect.anchorMax = anchorMax;

            Vector2 offsetMin = rect.offsetMin;
            offsetMin.x = padLeft;
            rect.offsetMin = offsetMin;

            Vector2 offsetMax = rect.offsetMax;
            offsetMax.x = -padRight;
            rect.offsetMax = offsetMax;
        }

        /// <summary>
        /// List order: team modes keep the roster order (playerId) so a player is always on the
        /// same row; in FFA the only meaningful order is score, descending (stable on ties by
        /// playerId).
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
                // No teams → the leader is the only meaningful headline; with no score yet nothing
                // is invented.
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

        /// <summary>
        /// Fills the live column(s) and drives the scroll content height.
        /// <para>The pools that belong to the other mode are emptied in the same pass — a row left
        /// bound in a hidden column would keep ticking a confirm window nobody can see.</para>
        /// <para>⚠️ No filtering by connection state, ever (§10.2): a <c>left</c> row must appear in
        /// the end-of-match table — the server keeps it in the roster exactly for that.</para>
        /// <para>⚠️ No row COUNT cap either: unlike the side columns nothing is clipped here, the
        /// content height is driven and the ScrollRect scrolls the rest. A clipped stats table
        /// would hide the player the operator is looking for.</para>
        /// </summary>
        private void RefreshRows(AdminRoster roster, bool split)
        {
            if (_rowContainer == null || _redColumn == null)
            {
                return;
            }

            _live.Clear();

            float height;
            int rows;

            if (split)
            {
                BuildLeftColumn(roster);
                height = NarrowRowHeight;

                // ⚠️ The taller column drives the height: sizing on one team would clip the other
                // one's tail out of the scroll range.
                rows = Mathf.Max(
                    FillColumn(_redRows, NarrowPrefab, _redColumn, _leftColumn, height),
                    FillColumn(_blueRows, NarrowPrefab, _blueColumn, roster.Blue, height));

                FillColumn(_wideRows, _rowPrefab, _redColumn, null, WideRowHeight);
            }
            else
            {
                height = WideRowHeight;
                rows = FillColumn(_wideRows, _rowPrefab, _redColumn, OrderedPlayers(roster), height);

                FillColumn(_redRows, NarrowPrefab, _redColumn, null, NarrowRowHeight);
                FillColumn(_blueRows, NarrowPrefab, _blueColumn, null, NarrowRowHeight);
            }

            // Content height is driven from code: no ContentSizeFitter/Layout Group (UiKit layout
            // rule) — the scrollbar range comes from here alone.
            _rowContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                rows > 0 ? rows * (height + _rowGap) : 0f);
        }

        /// <summary>
        /// Grows a pool to the list, places and binds it, hides the rest; returns the bound count.
        /// <para>⚠️ Every bound row is appended to <see cref="_live"/> — a row left out of it stops
        /// ticking, so its armed confirm window would never expire.</para>
        /// </summary>
        private int FillColumn(List<AdminStatsRow> pool, AdminStatsRow prefab, RectTransform parent,
            IReadOnlyList<AdminPlayerView> players, float height)
        {
            int count = players != null ? players.Count : 0;

            while (pool.Count < count)
            {
                if (prefab == null)
                {
                    Debug.LogWarning("[AdminStatsPanel] Satır prefabı atanmadı; oyuncu satırları çizilemiyor.");
                    break;
                }

                AdminStatsRow row = Instantiate(prefab, parent);
                row.Initialize(HandleRowSelected, ShowPopup);
                pool.Add(row);
            }

            count = Mathf.Min(count, pool.Count);

            for (int i = 0; i < pool.Count; i++)
            {
                if (i >= count)
                {
                    pool[i].SetVisible(false);
                    continue;
                }

                pool[i].SetVisible(true);
                pool[i].Place(i * (height + _rowGap), height);
                pool[i].Bind(players[i], players[i].playerId == AdminSession.SelectedPlayerId);
                _live.Add(pool[i]);
            }

            return count;
        }

        /// <summary>
        /// Left column of a team mode: red first, then the team-less.
        /// <para>⚠️ The team-less are <b>not</b> dropped, unlike the HUD side columns: this is the
        /// screen where calibration and naming happen, and in Lobby — before the server hands out
        /// teams — every player is team-less, so filtering by team alone would leave the operator
        /// with two empty columns exactly when the work has to be done. Their row keeps its neutral
        /// stripe and the header counts them, so the column never claims they are red.</para>
        /// </summary>
        private void BuildLeftColumn(AdminRoster roster)
        {
            _leftColumn.Clear();

            for (int i = 0; i < roster.Red.Count; i++)
            {
                _leftColumn.Add(roster.Red[i]);
            }

            for (int i = 0; i < roster.Players.Count; i++)
            {
                AdminPlayerView view = roster.Players[i];
                if (view.team != "red" && view.team != "blue")
                {
                    _leftColumn.Add(view);
                }
            }
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
                           // Duration/limit can be per match (§5.2), so this is where the operator
                           // verifies the chosen value was really applied.
                           (roster.RoundSeconds > 0 ? $" · süre {AdminCommands.FormatDuration(roster.RoundSeconds)}" : "") +
                           // The limit is three-valued (§5.2) and is written for unlimited too:
                           // "no limit line" and "limit is unlimited" must be distinguishable.
                           (roster.ScoreLimit != 0
                               ? $" · skor limiti {AdminCommands.FormatScoreLimit(roster.ScoreLimit)}"
                               : ""));
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
                if (players[i].IsConnected && players[i].alive)
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

        // ------------------------------------------------------------- bulk actions

        /// <summary>
        /// Reloads everyone's calibration from the headset record.
        /// <para>⚠️ <b>Order matters:</b> rows go into loading state FIRST, the command second. The
        /// other way round, a headset answering instantly would reply before the row is in loading
        /// state and the result would be swallowed unseen.</para>
        /// <para>No confirm window: reloading benches nobody and leaves the player as before if it
        /// fails — an undoable attempt, unlike a reset.</para>
        /// </summary>
        private void ReloadAllCalibrations()
        {
            for (int i = 0; i < _live.Count; i++)
            {
                _live[i].BeginCalibrationLoad();
            }

            AdminCommands.ReloadCalibration(0);
        }

        /// <summary>
        /// TAP: invalidates everyone's alignment and KEEPS the headsets' saved anchors
        /// (<c>keepSaved: true</c>), so <c>TÜMÜNÜ KALİBRE ET</c> afterwards puts everyone back in
        /// one click. That is the daily action, so it fires on the first press.
        /// </summary>
        private void ClearAllCalibration()
        {
            // A soft reset clears a standing confirmation: the newer, weaker command is what happened.
            _purgedAllAt = -1f;
            AdminCommands.ClearCalibration(0, keepSaved: true);
            ApplyClearAllButton();
        }

        /// <summary>
        /// HOLD (1 s): wipes the SAVED anchors on the headsets too (hard mode).
        /// <para>⚠️ Afterwards <c>TÜMÜNÜ KALİBRE ET</c> no longer works (nothing left to read) and
        /// every player must redo the A/B sequence by hand — venue maintenance, done when the floor
        /// markers move. The hold IS the confirmation (<see cref="HoldButton"/>); no two-step
        /// window sits on top of it.</para>
        /// </summary>
        private void PurgeAllCalibration()
        {
            _purgedAllAt = Time.unscaledTime;
            AdminCommands.ClearCalibration(0, keepSaved: false);
            ApplyClearAllButton();
        }

        /// <summary>Paints the bulk reset. Idle = red text on the shared fill (it still benches
        /// everyone on the floor). While held the fill walks to solid red — that ramp IS the
        /// progress bar, so the operator can read how much of the device wipe is left and slide off
        /// the button to abort.</summary>
        private void ApplyClearAllButton()
        {
            // ⚠️ The RESULT outranks the press: the wipe lands at the threshold, so from that
            // moment the button says SİLİNDİ even though the finger is still down.
            bool purged = _purgedAllAt >= 0f;
            bool holding = !purged && _clearAllHold != null && _clearAllHold.IsPressed;
            _clearAllHoldPainted = holding || purged;

            if (_clearAllLabel != null)
            {
                _clearAllLabel.text = holding ? "CİHAZ KAYITLARI SİLİNİYOR"
                    : purged ? "CİHAZ KAYITLARI SİLİNDİ"
                    : "HİZALAMALARI SIFIRLA";
                // ⚠️ GREEN even though the command was destructive: it reports "the thing you asked
                // for happened", the same grammar as the rows' TAMAM.
                _clearAllLabel.color = holding ? UiKit.OnAccent : purged ? UiKit.Good : UiKit.Bad;
            }

            if (_clearAllButton != null && _clearAllButton.targetGraphic is Image image)
            {
                image.color = holding
                    ? Color.Lerp(ClearAllIdleFill, UiKit.Bad, _clearAllHold.HoldProgress)
                    : ClearAllIdleFill;
            }
        }

        // ------------------------------------------------------------- callbacks

        private void HandleRowSelected(int playerId)
        {
            AdminSession.SelectedPlayerId = playerId;
        }

        /// <summary>Hands the result to <b>that row only</b>: the message is an event, no relayout
        /// needed.</summary>
        private void HandleCalibrationResult(CalibrationResultMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i].PlayerId == msg.playerId)
                {
                    _live[i].ApplyCalibrationResult(msg.ok, msg.error);
                    return;
                }
            }
        }

        /// <summary>
        /// Shows why a calibration reload failed (the narrow button can only carry "HATA").
        /// <para>⚠️ No scrim behind it and the panel is not locked — the panel's general rule: the
        /// live scene stays watchable and the operator can keep working.</para>
        /// </summary>
        private void ShowPopup(int playerId, string error)
        {
            if (_popupRoot == null)
            {
                return;
            }

            if (_popupText != null)
            {
                AdminRoster roster = AdminRoster.Instance;
                string who = roster != null ? roster.NameOf(playerId) : $"Oyuncu {playerId}";
                _popupText.text = $"{who} · {error}";
                _popupText.color = UiKit.Bad;
            }

            _popupRoot.SetActive(true);
            _popupUntil = Time.unscaledTime + PopupSeconds;
        }

        private void HidePopup()
        {
            _popupUntil = -1f;

            if (_popupRoot != null && _popupRoot.activeSelf)
            {
                _popupRoot.SetActive(false);
            }
        }
    }
}
