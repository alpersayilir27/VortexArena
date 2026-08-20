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
    /// <c>if (modeId == "…")</c> chain here; a new mode gets this screen for free.
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

        private readonly List<PlayerInfo> _ranked = new List<PlayerInfo>();
        private readonly StringBuilder _sb = new StringBuilder();

        private PlayerInfo[] _roster = Array.Empty<PlayerInfo>();
        private MatchEndMsg _lastEnd;
        private Stage _stage = Stage.Hidden;
        private float _scoreboardAt;

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — "is it needed this session"
        /// is <see cref="AppSingletons"/>'s call (rationale lives there).</summary>
        internal static void Install()
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

            _lastEnd = msg;
            ShowResult(msg);
        }

        /// <summary>Leaving the <c>finished</c> phase closes the screen: new match loaded, countdown
        /// started, operator paused, or returned to lobby (§10.1).</summary>
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

        // ----------------------------------------------------------------- scoreboard

        private void RefreshScoreboard()
        {
            RankPlayers();
            RefreshHeadline();
            RefreshTeamSummary();
            RefreshMatchSummary();
            RefreshTable();
        }

        /// <summary>
        /// Ordering matches <c>AdminStatsPanel</c>: in team modes roster order (<c>playerId</c>) is
        /// kept so a player always finds themselves in the same row; in FFA score is the only
        /// criterion, sorted DESCENDING (stable via <c>playerId</c> on ties).
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

        /// <summary>Cell texts use the same format as their <c>AdminStatsRow</c> counterparts — the
        /// same player must not look different across the two tables. ⚠️ Not extracted into a shared
        /// helper: this class reads <c>PlayerInfo</c> (wire DTO) while the admin row reads
        /// <c>AdminPlayerView</c> (client mirror); a shared signature would cut one of them off from
        /// its natural source.</summary>
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
            if (ModeRuntime.IsTeamless)
            {
                // No teams → the only meaningful headline is the leader. With no score yet
                // (match never started) we invent nothing.
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

        /// <summary>Card's bottom band. The admin card shows server diagnostics here; the player's
        /// counterpart is the match identity + their own summary.</summary>
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
                return "KIRMIZI KAZANDI";
            }

            if (msg.winnerTeam == "blue")
            {
                return "MAVİ KAZANDI";
            }

            return msg.winnerPlayerId > 0 ? $"{NameOf(msg.winnerPlayerId)} KAZANDI" : "";
        }

        /// <summary>Result card's score line: team score in team-scored modes, the player's own
        /// score in player-scored ones (<c>scoreRed</c>/<c>scoreBlue</c> are always 0 there,
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

        /// <summary>playerId → name (from roster); falls back to a generic label.</summary>
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
