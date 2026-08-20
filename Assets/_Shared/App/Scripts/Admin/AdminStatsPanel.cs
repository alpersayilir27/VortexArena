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
    /// <para><b>No invented metrics:</b> only data actually on the wire (K/D/score from §5.3
    /// <c>lobby_state</c>; battery/controllers from <c>status</c>; ping = client-measured RTT,
    /// §6.7 <c>net_stats</c>). ⚠️ <b>Damage and accuracy are not in the protocol, so not here
    /// either.</b> Jitter and packet loss are measured but not shown: ping is the only number the
    /// operator can act on.</para>
    /// <para>⚠️ <b>HP, scene and violations are deliberately absent from the row</b> — reasoning in
    /// the <see cref="AdminStatsRow"/> class doc; do not "restore" them.</para>
    /// <para><b>The THREE calibration buttons on the bottom bar are different jobs:</b>
    /// <c>TÜMÜNÜ KALİBRE ET</c> <i>reloads</i> alignment from the headset record (tries to put the
    /// player back in) · <c>TÜM HİZALAMALARI SIFIRLA</c> benches everyone but KEEPS the device
    /// record, so reload still works afterwards · <c>CİHAZ KAYITLARINI SİL</c> destroys the record
    /// too, so <b>reload no longer works</b> and players must redo the A/B sequence by hand.
    /// Standing side by side, the distinction is carried by <b>look and friction</b>: both
    /// destructive buttons are red and each asks for its own two-step confirm; the reload has none
    /// (it is undoable). Per-player invalidation lives on the side panel card
    /// (<see cref="AdminPlayerRow"/>, KAL) and is soft-only there.</para>
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

        /// <summary>Confirm window of the bulk reset (s); without a second press the button goes
        /// back to idle.</summary>
        private const float ClearAllConfirmSeconds = 3f;

        /// <summary>Shared idle fill of the bottom bar buttons; a reset button switches to
        /// <see cref="UiKit.Bad"/> only while awaiting confirmation.</summary>
        private static readonly Color ClearAllIdleFill = UiKit.Hex(0x334557, 0xF2);

        /// <summary>FFA sort buffer (avoids a list allocation per refresh).</summary>
        private readonly List<AdminPlayerView> _sorted = new List<AdminPlayerView>();

        /// <summary>Row pool: instances are hidden, never destroyed (same pattern as
        /// <see cref="AdminHud"/>) — Instantiate/Destroy per refresh causes GC and layout jitter.</summary>
        private readonly List<AdminStatsRow> _rows = new List<AdminStatsRow>();

        // ⚠️ All fields are [SerializeField] — the look comes from the PREFAB, this class only
        // writes data.

        [Tooltip("Açılıp kapanan kart — panel kapalıyken bu obje devre dışı bırakılır.")]
        [SerializeField] private GameObject _root;
        [SerializeField] private Button _closeButton;
        [SerializeField] private TextMeshProUGUI _headline;
        [SerializeField] private TextMeshProUGUI _teamSummary;
        [SerializeField] private TextMeshProUGUI _matchSummary;

        [Header("Oyuncu listesi")]
        [SerializeField] private AdminStatsRow _rowPrefab;
        [Tooltip("ScrollRect'in content'i — satırlar buranın altına kurulur, yüksekliği koddan sürülür.")]
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private float _rowGap = 6f;

        [Header("Toplu eylemler")]
        [SerializeField] private Button _calibrateAllButton;
        [SerializeField] private Button _measureAllButton;
        [SerializeField] private Button _clearAllButton;
        [SerializeField] private TextMeshProUGUI _clearAllLabel;
        [Tooltip("SERT kip: cihazdaki kayıtlı çapayı da siler — ardından KALİBRE ET çalışmaz.")]
        [SerializeField] private Button _purgeAllButton;
        [SerializeField] private TextMeshProUGUI _purgeAllLabel;

        [Header("Uyarı penceresi")]
        [SerializeField] private GameObject _popupRoot;
        [SerializeField] private TextMeshProUGUI _popupText;
        [SerializeField] private Button _popupCloseButton;

        private readonly StringBuilder _sb = new StringBuilder();

        private float _nextRefresh;
        private bool _dirty = true;

        /// <summary>Visible (bound) row count — <see cref="Tick"/> and bulk actions only touch
        /// these.</summary>
        private int _visibleRows;

        /// <summary>Row height read from the prefab (fallback <see cref="AdminStatsRow.Height"/>),
        /// so resizing the row reflows the list.</summary>
        private float _rowHeight = -1f;

        /// <summary>When the popup closes (<c>Time.unscaledTime</c>); &lt; 0 = closed.</summary>
        private float _popupUntil = -1f;

        /// <summary>First press of the bulk reset (<c>Time.unscaledTime</c>); &lt; 0 = not
        /// awaiting confirmation.</summary>
        private float _clearAllArmedAt = -1f;

        /// <summary>First press of the device-record purge (<c>Time.unscaledTime</c>); &lt; 0 = not
        /// awaiting confirmation. ⚠️ INDEPENDENT of <see cref="_clearAllArmedAt"/>: arming one must
        /// not make the other destructive on a single click.</summary>
        private float _purgeAllArmedAt = -1f;

        private float RowHeight
        {
            get
            {
                if (_rowHeight > 1f)
                {
                    return _rowHeight;
                }

                float fromPrefab = _rowPrefab != null
                    ? ((RectTransform)_rowPrefab.transform).rect.height
                    : 0f;
                _rowHeight = fromPrefab > 1f ? fromPrefab : AdminStatsRow.Height;
                return _rowHeight;
            }
        }

        private void Start()
        {
            // ⚠️ No persistent onClick entries in the prefab (see AdminPreferencesPanel.WireButtons).
            Wire(_closeButton, AdminSession.ClosePanel);
            Wire(_calibrateAllButton, ReloadAllCalibrations);
            Wire(_measureAllButton, () => AdminCommands.MeasureBodyScale(0));
            Wire(_clearAllButton, ArmClearAllCalibration);
            Wire(_purgeAllButton, ArmPurgeAllCalibration);
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
            for (int i = 0; i < _visibleRows && i < _rows.Count; i++)
            {
                _rows[i].Tick();
            }

            if (_popupUntil >= 0f && Time.unscaledTime >= _popupUntil)
            {
                HidePopup();
            }

            if (_clearAllArmedAt >= 0f && Time.unscaledTime - _clearAllArmedAt > ClearAllConfirmSeconds)
            {
                _clearAllArmedAt = -1f;
                _dirty = true;
            }

            if (_purgeAllArmedAt >= 0f && Time.unscaledTime - _purgeAllArmedAt > ClearAllConfirmSeconds)
            {
                _purgeAllArmedAt = -1f;
                _dirty = true;
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

            // ⚠️ A half-armed confirm never survives closing: reopening onto an "EMİN?" button
            // would wipe everyone's calibration on a single click.
            if (!open)
            {
                _clearAllArmedAt = -1f;
                _purgeAllArmedAt = -1f;
            }

            // BEFORE the roster: the destructive buttons must look right even with no player list.
            ApplyClearAllButton();
            ApplyPurgeAllButton();

            AdminRoster roster = AdminRoster.Instance;
            if (!open || roster == null)
            {
                return;
            }

            RefreshSummary(roster);
            RefreshRows(OrderedPlayers(roster));
            RefreshMatchInfo(roster);
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
        /// Fits the row pool to the list and places the rows.
        /// <para>⚠️ No filtering by connection state, ever (§10.2): a <c>left</c> row must appear in
        /// the end-of-match table — the server keeps it in the roster exactly for that.</para>
        /// <para>⚠️ No row COUNT cap either: unlike the side columns nothing is clipped here, the
        /// content height is driven and the ScrollRect scrolls the rest. A clipped stats table
        /// would hide the player the operator is looking for.</para>
        /// </summary>
        private void RefreshRows(IReadOnlyList<AdminPlayerView> players)
        {
            if (_rowContainer == null)
            {
                return;
            }

            int count = players != null ? players.Count : 0;

            while (_rows.Count < count)
            {
                if (_rowPrefab == null)
                {
                    Debug.LogWarning("[AdminStatsPanel] _rowPrefab atanmadı; oyuncu satırları çizilemiyor.");
                    break;
                }

                AdminStatsRow row = Instantiate(_rowPrefab, _rowContainer);
                row.Initialize(HandleRowSelected, ShowPopup);
                _rows.Add(row);
            }

            count = Mathf.Min(count, _rows.Count);
            _visibleRows = count;

            float height = RowHeight;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= count)
                {
                    _rows[i].SetVisible(false);
                    continue;
                }

                _rows[i].SetVisible(true);
                _rows[i].Place(i * (height + _rowGap), height);
                _rows[i].Bind(players[i], players[i].playerId == AdminSession.SelectedPlayerId);
            }

            // Content height is driven from code: no ContentSizeFitter/Layout Group (UiKit layout
            // rule) — the scrollbar range comes from here alone.
            _rowContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                count > 0 ? count * (height + _rowGap) : 0f);
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
                           (roster.RoundSeconds > 0 ? $" · raund {AdminCommands.FormatDuration(roster.RoundSeconds)}" : "") +
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
            for (int i = 0; i < _visibleRows && i < _rows.Count; i++)
            {
                _rows[i].BeginCalibrationLoad();
            }

            AdminCommands.ReloadCalibration(0);
        }

        /// <summary>
        /// Invalidates everyone's alignment (SOFT mode) — <b>two-step</b>, same contract as the
        /// destructive buttons of <see cref="AdminPlayerRow"/>.
        /// <para>The friction exists because of the neighbouring button: reload is an undoable
        /// attempt, this one benches everyone on the floor in a single click (§10.6).</para>
        /// <para>The headset RECORD is kept (<c>keepSaved: true</c>), so <c>TÜMÜNÜ KALİBRE ET</c>
        /// afterwards puts everyone back in one click — that is the daily action.</para>
        /// </summary>
        private void ArmClearAllCalibration()
        {
            if (_clearAllArmedAt < 0f)
            {
                _clearAllArmedAt = Time.unscaledTime;
                _dirty = true;
                return;
            }

            _clearAllArmedAt = -1f;
            AdminCommands.ClearCalibration(0, keepSaved: true);
            _dirty = true;
        }

        /// <summary>
        /// Wipes the SAVED anchors on the headsets too (HARD mode) — same two-step contract as
        /// <see cref="ArmClearAllCalibration"/>, but with its <b>own</b> confirm window.
        /// <para>⚠️ Afterwards <c>TÜMÜNÜ KALİBRE ET</c> no longer works (nothing left to read) and
        /// players must redo the A/B sequence by hand — venue maintenance, done when the floor
        /// markers move.</para>
        /// </summary>
        private void ArmPurgeAllCalibration()
        {
            if (_purgeAllArmedAt < 0f)
            {
                _purgeAllArmedAt = Time.unscaledTime;
                _dirty = true;
                return;
            }

            _purgeAllArmedAt = -1f;
            AdminCommands.ClearCalibration(0, keepSaved: false);
            _dirty = true;
        }

        /// <summary>Paints the invalidate button by confirm state: idle = red text on the shared
        /// fill, armed = inverted (red fill) — the operator reads what the second press will do off
        /// the button.</summary>
        private void ApplyClearAllButton()
        {
            ApplyDestructiveButton(_clearAllButton, _clearAllLabel, _clearAllArmedAt >= 0f,
                "EMİN? HİZALAMALARI SIFIRLA", "TÜM HİZALAMALARI SIFIRLA");
        }

        /// <summary>Device-record purge button — same look contract as
        /// <see cref="ApplyClearAllButton"/>, only the text and its own confirm state differ.</summary>
        private void ApplyPurgeAllButton()
        {
            ApplyDestructiveButton(_purgeAllButton, _purgeAllLabel, _purgeAllArmedAt >= 0f,
                "EMİN? KAYITLARI SİL", "CİHAZ KAYITLARINI SİL");
        }

        private static void ApplyDestructiveButton(Button button, TextMeshProUGUI label, bool armed,
            string armedText, string idleText)
        {
            if (label != null)
            {
                label.text = armed ? armedText : idleText;
                label.color = armed ? UiKit.OnAccent : UiKit.Bad;
            }

            if (button != null && button.targetGraphic is Image image)
            {
                image.color = armed ? UiKit.Bad : ClearAllIdleFill;
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

            for (int i = 0; i < _visibleRows && i < _rows.Count; i++)
            {
                if (_rows[i].PlayerId == msg.playerId)
                {
                    _rows[i].ApplyCalibrationResult(msg.ok, msg.error);
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
