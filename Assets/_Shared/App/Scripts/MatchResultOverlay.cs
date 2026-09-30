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
    /// Match end screen: on <c>match_end</c> hides gameplay HUDs and shows the <b>result card</b>
    /// (won / lost / draw) first, then the <b>scoreboard</b>; closes itself and restores the HUDs
    /// when the operator starts a new match or returns to lobby.
    /// <para>
    /// <b>Phase contract (§10.1):</b> this screen never closes on its own — leaving <c>finished</c>
    /// is the operator's choice (<c>load_match</c> / <c>return_to_lobby</c> / another
    /// <c>match_state</c>). Hence NO "close after a few seconds" timer here; the only timer is the
    /// result card → scoreboard transition.
    /// </para>
    /// <para>
    /// <b>Shares the CARD SHELL with <c>AdminStatsPanel</c>, NOT its layout.</b> This table is
    /// read-only columns; the admin panel is built from per-player action rows
    /// (<c>AdminStatsRow</c>). The split comes from the audience: a player reads a <b>result</b>,
    /// the operator manages a live <b>work list</b> whose buttons would be meaningless (and must be
    /// unpressable) on a player's screen.
    /// Columns are separate TMP objects joined by <c>\n</c>: TMP's default font is NOT monospaced,
    /// so space-aligned columns in one text block would drift.
    /// </para>
    /// <para>
    /// <b>Mode agnostic.</b> The winner arrives on <c>match_end</c>'s two channels (team or player,
    /// §5.3) and table ordering is split by <see cref="ModeRuntime.IsTeamless"/> — no
    /// <c>if (modeId == "…")</c> chain here; a new mode gets this screen for free. The kids' modes drop the combat columns the
    /// same way, off <see cref="ModeRuntime.HidesCombatStats"/>.
    /// </para>
    /// <para>
    /// <b>Per-mode look:</b> <c>ModeDefinition.ResultScreenPrefab</c> may point at a VARIANT of this
    /// prefab; at match end the singleton swaps itself for it (<see cref="SwapTo"/>). The variant
    /// overrides only art and wording — bindings are inherited, so behaviour stays identical.
    /// </para>
    /// <para>
    /// Self-bootstrapping persistent singleton (<c>WeaponGranter</c> pattern): NOT placed in scenes,
    /// else every new arena would gain a manual setup step. Visuals live entirely in the prefab
    /// (<c>Resources/UI/MatchResultOverlay</c>) — this class only writes data.
    /// </para>
    /// <para>
    /// ⚠️ <b>Role gate is at display time, not bootstrap:</b> <c>AppSession.Role</c> resolves in the
    /// Boot scene and its ordering against <c>AfterSceneLoad</c> bootstrap is not guaranteed. On the
    /// admin spectator the winner and table are already drawn by <c>AdminHud</c>, so this screen
    /// never opens there.
    /// </para>
    /// </summary>
    public class MatchResultOverlay : MonoBehaviour
    {
        /// <summary>Prefab path inside <c>Resources</c> (no extension).</summary>
        public const string ResourcePath = "UI/MatchResultOverlay";

        /// <summary>Column order, left to right — documents <see cref="CellText"/>'s <c>switch</c>
        /// order and the expected length of <see cref="boardColumns"/>. Header texts and widths live
        /// IN THE PREFAB (repo-wide UI contract: code only writes data).
        /// <para>⚠️ Adding a column here is NOT enough: a TMP object must also be created in the
        /// prefab and wired into the array, else the new column is silently never drawn.</para>
        /// <para>⚠️ The K and D headers are ICONS, not text, in the prefab (crosshair / skull) —
        /// like the admin card; those <c>Header</c> objects are intentionally blank.</para>
        /// <para>⚠️ Operator diagnostics (battery · controller · ping · status) are NOT here and are
        /// not added: players are never shown live device state at match end — that is admin
        /// information.</para></summary>
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

        [Tooltip("Kolon kökleri (başlık + değerler) — ColumnOrder sırasında. Bağlanmazsa kolon " +
                 "gizlenirken yalnız değer metni kapanır.")]
        [SerializeField] private GameObject[] boardColumnRoots = new GameObject[ColumnOrder.Length];

        [Tooltip("Kolon başlık metinleri — ColumnOrder sırasında. Bağlanmazsa başlık yeniden " +
                 "adlandırılamaz (K/D başlıkları ikon olduğu için boş bırakılabilir).")]
        [SerializeField] private TextMeshProUGUI[] boardColumnHeaders = new TextMeshProUGUI[ColumnOrder.Length];

        [Header("Kelimeler ve renkler (mod varyantı ezer)")]
        [SerializeField] private string wonTitle = "KAZANDIN";
        [SerializeField] private string lostTitle = "KAYBETTİN";
        [SerializeField] private string drawTitle = "BERABERE";
        [Tooltip("Ko-op sonuç başlığı — rakip yok, kazandın/kaybettin okunmaz.")]
        [SerializeField] private string coopTitle = "OYUN BİTTİ";
        [Tooltip("Skor kolonunun başlığı.")]
        [SerializeField] private string scoreHeader = "SKOR";
        [Tooltip("Ko-op'ta skor kolonunun başlığı (ortak toplama katkı).")]
        [SerializeField] private string coopScoreHeader = "KATKI";
        [SerializeField] private Color wonColor = UiKit.Good;
        [SerializeField] private Color lostColor = UiKit.Bad;
        [SerializeField] private Color drawColor = UiKit.Title;
        [SerializeField] private Color coopColor = UiKit.Title;
        [Tooltip("Kırmızı takım adının yazı rengi (sonuç kartı + skor tablosu).")]
        [SerializeField] private Color redTeamColor = UiKit.TeamRed;
        [Tooltip("Mavi takım adının yazı rengi (sonuç kartı + skor tablosu).")]
        [SerializeField] private Color blueTeamColor = UiKit.TeamBlue;

        private readonly List<PlayerInfo> _ranked = new List<PlayerInfo>();
        private readonly StringBuilder _sb = new StringBuilder();

        private PlayerInfo[] _roster = Array.Empty<PlayerInfo>();
        private MatchEndMsg _lastEnd;

        /// <summary>Last <c>modeState</c> line (§10.1) — the co-op customer counters live there and the
        /// server keeps publishing it through the <c>finished</c> phase.</summary>
        private string _modeState = "";

        private Stage _stage = Stage.Hidden;
        private float _scoreboardAt;

        /// <summary>Prefab this instance was spawned from; compared against the running mode's screen
        /// at match end.</summary>
        private MatchResultOverlay _source;

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — "is it needed this session"
        /// is <see cref="AppSingletons"/>'s call (rationale lives there).</summary>
        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            MatchResultOverlay prefab = LoadGeneric();
            if (prefab == null)
            {
                Debug.LogError($"[MatchResultOverlay] '{ResourcePath}' prefabı bulunamadı — maç " +
                               "sonu ekranı çizilemeyecek.");
                return;
            }

            Spawn(prefab);
        }

        private static MatchResultOverlay LoadGeneric()
        {
            return Resources.Load<MatchResultOverlay>(ResourcePath);
        }

        private static MatchResultOverlay Spawn(MatchResultOverlay prefab)
        {
            MatchResultOverlay overlay = Instantiate(prefab);
            overlay.name = "[MatchResultOverlay]";
            overlay._source = prefab;
            DontDestroyOnLoad(overlay.gameObject);
            _instance = overlay;
            return overlay;
        }

        /// <summary>The running mode's own screen (<c>ModeDefinition.ResultScreenPrefab</c>), else the
        /// generic one. Resolved at match end, when the mode is certain.</summary>
        private static MatchResultOverlay ScreenForRunningMode()
        {
            GameCatalog catalog = Admin.AdminContent.Catalog;
            ModeDefinition mode = catalog != null ? catalog.FindMode(ModeRuntime.ModeId) : null;
            GameObject custom = mode != null ? mode.ResultScreenPrefab : null;
            if (custom == null)
            {
                return LoadGeneric();
            }

            MatchResultOverlay screen = custom.GetComponent<MatchResultOverlay>();
            if (screen == null)
            {
                Debug.LogError($"[MatchResultOverlay] '{mode.ModeId}' modunun maç sonu ekranında " +
                               "MatchResultOverlay bileşeni yok — genel ekran çiziliyor.");
                return LoadGeneric();
            }

            return screen;
        }

        /// <summary>Replaces this instance with one spawned from <paramref name="prefab"/>. Roster and
        /// modeState are carried over: both arrive only on change, so the new instance would not hear
        /// them again before its screen opens.</summary>
        private MatchResultOverlay SwapTo(MatchResultOverlay prefab)
        {
            // Frees the slot so the new instance's Awake keeps itself instead of self-destructing.
            _instance = null;
            MatchResultOverlay next = Spawn(prefab);
            next._roster = _roster;
            next._modeState = _modeState;

            // Deactivate first: OnDisable unsubscribes now, not at end of frame, so this instance
            // cannot act on a later event (e.g. HideAll releasing the HUD gate) before it is gone.
            gameObject.SetActive(false);
            Destroy(gameObject);
            return next;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Don't carry a stale "hidden" state over from a Play entry without domain reload.
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

            // Don't leave HUDs hidden if this screen is being destroyed.
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

        // -------------------------------------------------------- net event handlers

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null || AppSession.Role != AppSession.RolePlayer)
            {
                return;
            }

            MatchResultOverlay screen = ScreenForRunningMode();
            if (screen != null && screen != _source)
            {
                SwapTo(screen).HandleMatchEnd(msg);
                return;
            }

            _lastEnd = msg;
            ShowResult(msg);
        }

        /// <summary>Leaving the <c>finished</c> phase closes the screen: new match loaded, countdown
        /// started, operator paused, or returned to lobby (§10.1).</summary>
        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (msg.phase != ArenaProtocol.PHASE_FINISHED)
            {
                HideAll();
            }

            // AFTER HideAll: the line must survive into the result screen, and HideAll clears it.
            _modeState = msg.modeState ?? "";

            if (_stage == Stage.Scoreboard)
            {
                RefreshScoreboard();
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

        /// <summary>Table is fed by the roster (§10.2): counters are server-authoritative and
        /// participants (including <c>left</c> ones) stay listed for the whole <c>finished</c> phase.
        /// ⚠️ NO filtering by connection state — same rationale as the admin table: a removed player
        /// must still appear in the final table.</summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            _roster = msg?.players ?? Array.Empty<PlayerInfo>();

            if (_stage == Stage.Scoreboard)
            {
                RefreshScoreboard();
            }
        }

        // ------------------------------------------------------------------ display

        private void ShowResult(MatchEndMsg msg)
        {
            SetPanel(resultPanel, true);
            SetPanel(scoreboardPanel, false);

            if (ModeRuntime.IsCoop)
            {
                // ⚠️ Co-op has no opponent: the server ends it with no winner, which the generic
                // branch would read as "BERABERE" — a draw against nobody. The shift simply ends.
                if (resultTitleText != null)
                {
                    resultTitleText.text = coopTitle;
                    resultTitleText.color = coopColor;
                }

                SetText(resultWinnerText, $"EKİP SKORU {msg.scoreRed}");
                SetText(resultScoreText, CoopResultLines());
            }
            else
            {
                bool draw;
                bool won = Won(msg, out draw);

                if (resultTitleText != null)
                {
                    resultTitleText.text = draw ? drawTitle : won ? wonTitle : lostTitle;
                    resultTitleText.color = draw ? drawColor : won ? wonColor : lostColor;
                }

                SetText(resultWinnerText, WinnerLine(msg));
                SetText(resultScoreText, ScoreLine(msg));
            }

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
            _modeState = "";
            GameplayHudGate.SetHidden(false);
        }

        private static void SetPanel(GameObject panel, bool visible)
        {
            if (panel != null && panel.activeSelf != visible)
            {
                panel.SetActive(visible);
            }
        }

        // ----------------------------------------------------------------- scoreboard

        private void RefreshScoreboard()
        {
            RankPlayers();
            RefreshColumnLayout();
            RefreshHeadline();
            RefreshTeamSummary();
            RefreshMatchSummary();
            RefreshTable();
        }

        /// <summary>
        /// Ordering matches <c>AdminStatsPanel</c>: in team modes roster order (<c>playerId</c>) is
        /// kept so a player always finds themselves in the same row; in FFA score is the only
        /// criterion, sorted DESCENDING (stable via <c>playerId</c> on ties).
        /// <para>Departed (<c>left</c>) rows follow the Counter-Strike rule (§10.2): in a
        /// player-scored mode they leave the table (deathmatch), in a team mode they stay, dimmed
        /// (competitive). The admin table is not filtered — the operator must see everyone.</para>
        /// </summary>
        private void RankPlayers()
        {
            _ranked.Clear();

            for (int i = 0; i < _roster.Length; i++)
            {
                PlayerInfo info = _roster[i];
                if (info == null || info.role == "admin")
                {
                    continue;
                }

                if (ModeRuntime.IsTeamless && info.connection == ArenaProtocol.CONNECTION_LEFT)
                {
                    continue;
                }

                _ranked.Add(info);
            }

            if (!ModeRuntime.IsTeamless && ModeRuntime.IsWeaponless)
            {
                // Weaponless team mode: teams stay grouped, but inside a team score is the only
                // ranking left — there are no kills to read the row order from.
                _ranked.Sort(CompareByTeamThenScore);
                return;
            }

            // Co-op has no teams to hold a row in place either: the list IS the contribution ranking.
            if (!ModeRuntime.IsTeamless && !ModeRuntime.IsCoop)
            {
                return;
            }

            _ranked.Sort(CompareByScoreDescending);
        }

        /// <summary>
        /// Which columns this mode's table carries. Kill/death columns are dropped where they can only
        /// print zeros (co-op, weaponless), and co-op's score column is the shared total's
        /// contribution, so it is renamed.
        /// <para>⚠️ Visibility is restored on every refresh, never only switched off: the overlay is a
        /// persistent singleton, so a column hidden by the kids' mode would stay hidden for the next
        /// shooter match.</para>
        /// </summary>
        private void RefreshColumnLayout()
        {
            bool coop = ModeRuntime.IsCoop;
            bool hideCombat = ModeRuntime.HidesCombatStats;

            for (int c = 0; c < ColumnOrder.Length; c++)
            {
                bool visible = true;
                if (c == 1)
                {
                    visible = !coop;
                }
                else if (c >= 3)
                {
                    visible = !hideCombat;
                }

                SetColumnVisible(c, visible);
            }

            SetColumnHeader(2, coop ? coopScoreHeader : scoreHeader);
        }

        private void SetColumnVisible(int column, bool visible)
        {
            GameObject root = boardColumnRoots != null && column < boardColumnRoots.Length
                ? boardColumnRoots[column]
                : null;

            if (root != null)
            {
                SetPanel(root, visible);
                return;
            }

            // No column root wired: hide what we can reach — the values and, if it is a text header,
            // the header. An icon header left over is a cosmetic leftover, not a wrong number.
            if (boardColumns != null && column < boardColumns.Length && boardColumns[column] != null)
            {
                SetPanel(boardColumns[column].gameObject, visible);
            }

            TextMeshProUGUI header = HeaderOf(column);
            if (header != null)
            {
                SetPanel(header.gameObject, visible);
            }
        }

        private void SetColumnHeader(int column, string label)
        {
            TextMeshProUGUI header = HeaderOf(column);
            if (header != null && header.text != label)
            {
                header.text = label;
            }
        }

        private TextMeshProUGUI HeaderOf(int column)
        {
            return boardColumnHeaders != null && column < boardColumnHeaders.Length
                ? boardColumnHeaders[column]
                : null;
        }

        private static int CompareByTeamThenScore(PlayerInfo a, PlayerInfo b)
        {
            int byTeam = TeamOrder(a.team).CompareTo(TeamOrder(b.team));
            return byTeam != 0 ? byTeam : CompareByScoreDescending(a, b);
        }

        /// <summary>Red · blue · unassigned — the same left-to-right order the whole UI uses.</summary>
        private static int TeamOrder(string team)
        {
            return team == "red" ? 0 : team == "blue" ? 1 : 2;
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

        /// <summary>Cell texts use the same format as their <c>AdminStatsRow</c> counterparts — the
        /// same player must not look different across the two tables. ⚠️ Not extracted into a shared
        /// helper: this class reads <c>PlayerInfo</c> (wire DTO) while the admin row reads
        /// <c>AdminPlayerView</c> (client mirror); a shared signature would cut one of them off from
        /// its natural source.</summary>
        private string CellText(PlayerInfo info, int column)
        {
            string text = RawCellText(info, column);
            if (info.connection != ArenaProtocol.CONNECTION_LEFT)
            {
                return text;
            }

            // Departed row (only team modes get here, see RankPlayers): dimmed, and the name carries
            // the marker — the text stand-in for Counter-Strike's disconnected icon.
            return column == 0
                ? $"<alpha=#66>{text} · ayrıldı<alpha=#FF>"
                : $"<alpha=#66>{text}<alpha=#FF>";
        }

        private string RawCellText(PlayerInfo info, int column)
        {
            switch (column)
            {
                // Rich text is ON for the columns (departed-row alpha tags) → a name must never open
                // a tag; '<' becomes a look-alike instead of being parsed.
                case 0: return $"{SafeName(info.name)} #{info.playerId}";
                // ⚠️ Column is NoWrap: a wrapped cell would desync the \n-joined rows.
                case 1: return info.team == "red" ? $"{Paint("Kırmızı", redTeamColor)} Takım"
                             : info.team == "blue" ? $"{Paint("Mavi", blueTeamColor)} Takım" : "-";
                case 2: return info.score.ToString();
                case 3: return info.kills.ToString();
                case 4: return info.deaths.ToString();
                case 5: return info.deaths > 0
                    ? (info.kills / (float)info.deaths).ToString("0.00")
                    : info.kills.ToString("0.00");
                // Columns wired in the prefab beyond ColumnOrder stay blank.
                default: return "";
            }
        }

        /// <summary>Card's orange headline: winner + score. The score part matches
        /// <c>AdminStatsPanel</c>'s headline (team score in team modes, leader in FFA).</summary>
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
            // ⚠️ BEFORE the teamless branch: co-op is teamless too, but it has no leader — the shared
            // total is the only score, and a "LİDER" line would turn a co-op shift into a contest.
            if (ModeRuntime.IsCoop)
            {
                return _lastEnd != null ? $"EKİP SKORU {_lastEnd.scoreRed}" : "EKİP SKORU";
            }

            if (ModeRuntime.IsTeamless)
            {
                // No teams → the only meaningful headline is the leader. With no score yet
                // (match never started) we invent nothing.
                return _ranked.Count > 0 && _ranked[0].score > 0
                    ? $"LİDER: {SafeName(_ranked[0].name)} {_ranked[0].score}"
                    : "HERKES TEK";
            }

            return _lastEnd != null ? TeamScoreLine(_lastEnd) : "";
        }

        private void RefreshTeamSummary()
        {
            if (boardTeamSummaryText == null)
            {
                return;
            }

            if (ModeRuntime.IsCoop)
            {
                // Neither "canlı" nor kills belong here: nobody dies and nobody shoots in a co-op
                // shift — the customers are the only thing that happened.
                string customers = CustomerLine();
                boardTeamSummaryText.text = customers.Length > 0
                    ? $"{_ranked.Count} oyuncu · {customers}"
                    : $"{_ranked.Count} oyuncu";
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

            if (ModeRuntime.IsWeaponless)
            {
                // Weaponless team mode: the team line is players + team points; "canlı"/kill totals
                // would be three zeros the players have to learn to ignore.
                int redScore = _lastEnd != null ? _lastEnd.scoreRed : 0;
                int blueScore = _lastEnd != null ? _lastEnd.scoreBlue : 0;
                _sb.AppendLine($"{RedTeam}: {redCount} oyuncu · TAKIM SKORU: {redScore}");
                _sb.Append($"{BlueTeam}: {blueCount} oyuncu · TAKIM SKORU: {blueScore}");
                boardTeamSummaryText.text = _sb.ToString();
                return;
            }

            _sb.AppendLine($"{RedTeam}: {redCount} oyuncu · {redAlive} canlı · {redKills} öldürme · {redDeaths} ölüm");
            _sb.Append($"{BlueTeam}: {blueCount} oyuncu · {blueAlive} canlı · {blueKills} öldürme · {blueDeaths} ölüm");
            boardTeamSummaryText.text = _sb.ToString();
        }

        /// <summary>Card's bottom band. The admin card shows server diagnostics here; the player's
        /// counterpart is the match identity + their own summary.</summary>
        private void RefreshMatchSummary()
        {
            if (boardMatchSummaryText == null)
            {
                return;
            }

            string mode = Admin.AdminContent.ModeDisplayName(ModeRuntime.ModeId);
            string map = SceneManager.GetActiveScene().name;

            _sb.Clear();
            _sb.AppendLine($"Mod: {mode} · Harita: {map}");

            PlayerInfo self = FindSelf();
            _sb.Append(self == null
                ? ""
                : ModeRuntime.IsCoop
                    ? $"SKOR: {self.score}"
                    : ModeRuntime.IsWeaponless
                        ? $"BİREYSEL SKOR: {self.score}"
                        : $"SEN: {self.kills} öldürme · {self.deaths} ölüm · K/D {CellText(self, 5)}");

            boardMatchSummaryText.text = _sb.ToString();
        }

        /// <summary>Counts all players when <paramref name="team"/> is <c>null</c>.</summary>
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

        // ----------------------------------------------------------------------- text

        /// <summary>Did the local player win. The winner arrives on two channels (§5.3):
        /// <c>winnerTeam</c> for team-scored modes, <c>winnerPlayerId</c> for player-scored ones;
        /// both empty = draw.</summary>
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
                return $"{RedTeam} KAZANDI";
            }

            if (msg.winnerTeam == "blue")
            {
                return $"{BlueTeam} KAZANDI";
            }

            return msg.winnerPlayerId > 0 ? $"{NameOf(msg.winnerPlayerId)} KAZANDI" : "";
        }

        /// <summary>Result card's score line: team score in team-scored modes, the player's own
        /// score in player-scored ones (<c>scoreRed</c>/<c>scoreBlue</c> are always 0 there,
        /// §10.2). ⚠️ Co-op never reaches here — it has its own card
        /// (<see cref="CoopResultLines"/>), because the team line would read "KIRMIZI TAKIM n — 0 MAVİ TAKIM".</summary>
        private string ScoreLine(MatchEndMsg msg)
        {
            if (ModeRuntime.Scoring != ModeScoreKind.Player)
            {
                return TeamScoreLine(msg);
            }

            PlayerInfo self = FindSelf();
            return self != null ? $"SENİN SKORUN {self.score}" : "";
        }

        private string TeamScoreLine(MatchEndMsg msg) => $"{RedTeam} {msg.scoreRed} — {msg.scoreBlue} {BlueTeam}";

        // Only the colour word is painted; "TAKIM" keeps the text's own colour.
        private string RedTeam => $"{Paint("KIRMIZI", redTeamColor)} TAKIM";
        private string BlueTeam => $"{Paint("MAVİ", blueTeamColor)} TAKIM";

        /// <summary>Rich text is on for the texts carrying team colours → a player name must never
        /// open a tag; '&lt;' becomes a look-alike instead of being parsed.</summary>
        private static string SafeName(string name) => name?.Replace('<', '‹');

        /// <summary>Team-coloured span; the prefab enables rich text on the texts that carry it.</summary>
        private static string Paint(string text, Color color) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

        /// <summary>Co-op result card body: customer counters (when the mode publishes them) + the
        /// player's own contribution. Same wording as the in-match HUD — the numbers must not be
        /// renamed between the match and its result screen.</summary>
        private string CoopResultLines()
        {
            _sb.Clear();

            string customers = CustomerLine();
            if (customers.Length > 0)
            {
                _sb.AppendLine(customers);
            }

            PlayerInfo self = FindSelf();
            if (self != null)
            {
                _sb.Append($"Skor: {self.score}");
            }

            return _sb.ToString();
        }

        /// <summary>"Mutlu müşteri h · Mutsuz müşteri u", or empty when the running mode publishes no counters.
        /// ⚠️ Parsing is NOT duplicated here: <see cref="Admin.AdminModeState"/> is the single parser,
        /// so the operator and the player can never read two different numbers.</summary>
        private string CustomerLine()
        {
            return Admin.AdminModeState.TryCustomerCounts(_modeState, out int happy, out int unhappy)
                ? $"Mutlu müşteri {happy} · Mutsuz müşteri {unhappy}"
                : "";
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

        /// <summary>playerId → name (from roster); falls back to a generic label.</summary>
        private string NameOf(int playerId)
        {
            for (int i = 0; i < _roster.Length; i++)
            {
                if (_roster[i] != null && _roster[i].playerId == playerId &&
                    !string.IsNullOrEmpty(_roster[i].name))
                {
                    return SafeName(_roster[i].name);
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
