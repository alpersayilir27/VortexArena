using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Audio;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>Everything the admin UI needs for a single player row.</summary>
    public class AdminPlayerView
    {
        public int playerId;
        public string name = "";

        /// <summary>Jersey number 1..99 (§2); 0 = unassigned, always 0 for admins. Names are not
        /// unique, so this is the operator's distinguishing field.</summary>
        public int number;

        public string role = AppSession.RolePlayer;
        public string team = "";
        public bool ready;

        /// <summary>Connection state (§2/§5.3): <c>connected</c> | <c>reconnecting</c> |
        /// <c>left</c>. ⚠️ Never compare this string elsewhere — read it only through the three
        /// shortcuts below, or a scattered <c>== "reconnecting"</c> chain will eventually miss the
        /// "unknown counts as connected" rule.</summary>
        public string connection = ArenaProtocol.CONNECTION_CONNECTED;

        /// <summary>"Seconds left before removal" from the last <c>lobby_state</c>, plus when that
        /// message arrived. Both are kept because roster broadcasts are EVENT based — the server
        /// does not tick per second, so we advance the counter locally
        /// (<see cref="ReconnectSecondsLeft"/>).</summary>
        public int reconnectSeconds;

        /// <inheritdoc cref="reconnectSeconds"/>
        public float reconnectStampedAt = -1f;

        /// <summary>Is a participant of the running match (§10.2) — why a <c>left</c> row still
        /// shows in the end-of-match table.</summary>
        public bool inMatch;

        /// <summary>Is the socket live. ⚠️ An unknown/empty value counts as connected (§5.3), so a
        /// mixed server/client version does not blank the whole roster.</summary>
        public bool IsConnected => !IsReconnecting && !HasLeft;

        /// <summary>Connection dropped, the device is expected back.</summary>
        public bool IsReconnecting => connection == ArenaProtocol.CONNECTION_RECONNECTING;

        /// <summary>Timed out and removed; the row stays only for match statistics.</summary>
        public bool HasLeft => connection == ArenaProtocol.CONNECTION_LEFT;

        /// <summary>Seconds until the player is removed (0 = none). The server value is decremented
        /// by locally elapsed time; broadcasts are event based, so otherwise the counter would only
        /// move on some unrelated roster change.</summary>
        public int ReconnectSecondsLeft
        {
            get
            {
                if (!IsReconnecting || reconnectSeconds <= 0)
                {
                    return 0;
                }

                float elapsed = reconnectStampedAt < 0f ? 0f : Time.unscaledTime - reconnectStampedAt;
                return Mathf.Max(0, reconnectSeconds - Mathf.FloorToInt(elapsed));
            }
        }

        public bool alive = true;

        /// <summary>HEADSET battery (0..1); -1 = unknown.</summary>
        public float battery = -1f;

        /// <summary>Left/right controller STATE (§5.1 <c>ArenaProtocol.CONTROLLER_*</c>).
        /// <para>⚠️ A state, not a percentage — controller charge is unreadable on Quest under
        /// OpenXR; <see cref="battery"/> is the HEADSET's.</para>
        /// <para>Default <c>0</c> = <c>CONTROLLER_UNKNOWN</c> ("not reported"), the same pattern as
        /// <c>battery = -1f</c>: the unknown value sits outside the valid range and is never counted
        /// as healthy. Admin records stay at <c>0</c>.</para></summary>
        public int ctrlL;

        /// <inheritdoc cref="ctrlL"/>
        public int ctrlR;

        public float hp = ArenaProtocol.PLAYER_MAX_HP;
        public int kills;
        public int deaths;

        /// <summary>Individual match score (§10.2) — NOT kills; its meaning is mode-specific.</summary>
        public int score;

        /// <summary>Is the headset aligned with the arena (§10.6). Defaults to <b>true</b>: showing
        /// unknown as an alarm is noise, and the admin's own row must never read "uncalibrated"
        /// (the server always sends false for admins).</summary>
        public bool calibrated = true;

        /// <summary>"manual" | "anchor" | "cloud" | "" — a free label, not validated.</summary>
        public string calibrationSource = "";

        /// <summary>Floor offset from the last manual calibration (signed meters, §10.6);
        /// <c>0</c> = no measurement or clean. Rows above
        /// <c>ArenaProtocol.CALIB_FLOOR_WARN_METERS</c> get a ⚠ on the KAL button — the cause is
        /// stale headset space data.</summary>
        public float floorOffset;

        /// <summary>Body scale (§10.8); <b>0 = not measured</b>. The row's ÖLÇ button shows it, so
        /// the operator can see who has been measured from the list.</summary>
        public float bodyScale;

        /// <summary>Failure reason of the last body measurement; empty = fine (§10.8). When set the
        /// ÖLÇ button shows an error instead of the scale; a successful measurement clears it.</summary>
        public string scaleError = "";

        /// <summary>Failure reason of the last calibration RELOAD attempt; empty = fine (§5.3). A
        /// successful calibration clears it; while set the row can explain why the reload failed
        /// (the narrow button only carries "HATA").</summary>
        public string calibrationError = "";

        /// <summary>Does the row need operator attention: a PLAYER and uncalibrated.</summary>
        public bool NeedsCalibration => IsPlayer && !calibrated;

        public string scene = "";

        // ---- Network telemetry (§6.7): the CLIENT measures, the server relays via net_stats ----
        // ⚠️ Default -1 = "unknown" and must not be confused with 0, which reads as a real 0 ms
        // measurement. Same pattern as battery = -1f.

        /// <summary>Measured round-trip time (ms); -1 = unknown. The PING column.</summary>
        public int rttMs = -1;

        /// <summary>Downlink snapshot jitter (ms); -1 = unknown. Not shown in the panel — ping is
        /// the number the operator can act on; this is diagnostics.</summary>
        public float jitterMs = -1f;

        /// <summary>Downlink snapshot loss (%); -1 = unknown. Not shown (same reason as jitterMs).</summary>
        public float lossPct = -1f;

        /// <summary>Time of death (<c>Time.unscaledTime</c>); -1 = alive/unknown.</summary>
        public float diedAt = -1f;

        // ---- Violation ledger (§10.9): the SERVER counts, the admin only displays ----
        // ⚠️ Never increment these locally. The server is authoritative, so one clock measures the
        // time and both operators see the same number. The edge-triggered `violation` message
        // already carries the current total, so a lost message self-heals on the next one; counting
        // locally would turn it into a permanent drift.

        /// <summary>Obstacle violations this match (head inside an inner obstacle).</summary>
        public int obstacleCount;

        /// <summary>Total time spent inside an obstacle (s).</summary>
        public float obstacleSeconds;

        /// <summary>Out-of-bounds violations this match.</summary>
        public int outOfBoundsCount;

        /// <summary>Total time spent out of bounds (s).</summary>
        public float outOfBoundsSeconds;

        /// <summary>Sum of both kinds — for the single stats-table cell.</summary>
        public int ViolationCount => obstacleCount + outOfBoundsCount;

        /// <inheritdoc cref="ViolationCount"/>
        public float ViolationSeconds => obstacleSeconds + outOfBoundsSeconds;

        public bool IsPlayer => role == AppSession.RolePlayer;

        public float HpNormalized => Mathf.Clamp01(hp / ArenaProtocol.PLAYER_MAX_HP);

        /// <summary>Seconds until respawn (0 = not waiting). See the class doc.</summary>
        public float RespawnRemaining =>
            alive || diedAt < 0f
                ? 0f
                : Mathf.Max(0f, ArenaProtocol.RESPAWN_DELAY - (Time.unscaledTime - diedAt));
    }

    /// <summary>Unified live model of everything from the server — the admin UI's data layer. It
    /// touches no UI types; HUD and panels only read from here.
    /// <para>Authority: <c>lobby_state</c> is the FULL authoritative snapshot (name/role/team/ready/
    /// connection/battery/scene + <c>kills/deaths/hp/alive</c>). Between snapshots it is advanced
    /// locally from <c>health_update</c> and <c>kill_event</c> so bars and counters react at once;
    /// on drift the next <c>lobby_state</c> wins.</para>
    /// <para>⚠️ <c>respawn</c> never reaches the admin — the server sends it only to the dead
    /// player's connection (§10.4). The respawn countdown is therefore computed LOCALLY from
    /// <c>kill_event</c> time + <see cref="ArenaProtocol.RESPAWN_DELAY"/>. If the player never
    /// enters their base the real respawn is delayed with no upper bound, so at 0 the row falls back
    /// to "waiting in base" instead of falsely claiming a respawn.</para>
    /// </summary>
    public class AdminRoster : MonoBehaviour
    {
        /// <summary>Max rows kept in the kill feed.</summary>
        public const int KillFeedMaxLines = 8;

        /// <summary>Max rows kept in the violation feed.</summary>
        public const int ViolationFeedMaxLines = 8;

        /// <summary>Minimum gap between two alert sounds (s).
        /// <para>⚠️ The throttle is SHARED across all players, not per player: with three people
        /// hovering at the boundary a per-player counter turns the sound into a siren and the
        /// operator mutes it within a minute. The policy lives here — <see cref="GameAudio"/> only
        /// plays, it does not decide when.</para></summary>
        private const float ViolationSoundCooldown = 3f;

        public static AdminRoster Instance { get; private set; }

        /// <summary>Raised on the main thread when roster/score/phase data changes.</summary>
        public event Action Changed;

        /// <summary>Result of a player's calibration reload attempt (§5.3). An EVENT, not state:
        /// only the row currently awaiting a reply cares, and it shows the result briefly.
        /// <para>⚠️ <see cref="Changed"/> is not raised — no roster data changed, and a refresh
        /// would redraw the whole list for nothing.</para>
        /// <para>Static because its listener (the stats panel) opens and closes independently of the
        /// roster; instance subscription would tie it to <see cref="Instance"/>'s creation
        /// order.</para></summary>
        public static event Action<CalibrationResultMsg> CalibrationResult;

        private readonly Dictionary<int, AdminPlayerView> _players = new Dictionary<int, AdminPlayerView>();
        private readonly List<AdminPlayerView> _red = new List<AdminPlayerView>();
        private readonly List<AdminPlayerView> _blue = new List<AdminPlayerView>();
        private readonly List<AdminPlayerView> _all = new List<AdminPlayerView>();
        private readonly List<string> _killFeed = new List<string>();
        private readonly List<string> _violationFeed = new List<string>();
        private readonly List<int> _removeScratch = new List<int>();

        /// <summary>Time of the last violation alert (<c>Time.unscaledTime</c>); see
        /// <see cref="ViolationSoundCooldown"/>.</summary>
        private float _lastViolationSoundAt = float.NegativeInfinity;

        /// <summary>Red team (role=player only, ordered by playerId).</summary>
        public IReadOnlyList<AdminPlayerView> Red => _red;

        /// <summary>Blue team (role=player only, ordered by playerId).</summary>
        public IReadOnlyList<AdminPlayerView> Blue => _blue;

        /// <summary>All players (role=player only, ordered by playerId).</summary>
        public IReadOnlyList<AdminPlayerView> Players => _all;

        public IReadOnlyList<string> KillFeed => _killFeed;

        /// <summary>Violation feed (§10.9). ⚠️ Kept separate from the kill feed: that one is the
        /// match story, this one is the operator's to-do list; merged, neither is readable.</summary>
        public IReadOnlyList<string> ViolationFeed => _violationFeed;

        /// <summary>Connected admin count (including us) — shown in the stats panel.</summary>
        public int AdminCount { get; private set; }

        /// <summary>Should the UI collapse to one column (team-less mode)? The server has authority:
        /// the decision comes from <see cref="ModeRuntime.Teams"/>, i.e. <c>load_match.rules</c>
        /// (§10.5).
        /// <para>With no match (Lobby phase) no rules have been published, so the shared selection's
        /// mode is read from the catalog. Without a catalog, or while the selection is still the
        /// startup lobby profile (§5.3 — the lobby is not a match mode), it falls back to the
        /// HEURISTIC "no online player has a team". ⚠️ That fallback is misleading on its own — it
        /// shows a TDM match whose teams are not yet assigned as FFA — and exists only as a last
        /// resort so the UI is not blank.</para>
        /// <para>A computed property, not a field: its inputs (phase, server rules, shared
        /// selection) change independently of the roster and a cache would go stale.</para>
        /// </summary>
        public bool IsFfa => ResolveIsFfa();

        /// <summary>Input of the heuristic fallback: does at least one CONNECTED player have a team
        /// (computed by <see cref="Rebuild"/>).</summary>
        private bool _anyConnectedTeam;

        /// <summary>Phase from the server (§10.1): <c>paused</c> | <c>playing</c> | <c>finished</c>.</summary>
        public string Phase { get; private set; } = ArenaProtocol.PHASE_PAUSED;

        /// <summary>Pause reason (§10.1); empty when not paused.</summary>
        public string PhaseReason { get; private set; } = ArenaProtocol.PAUSE_REASON_LOBBY;

        /// <summary>The mode's own intermediate state (§10.1); the core does not interpret it.</summary>
        public string ModeState { get; private set; } = "";

        /// <summary>Can mode/map selection change now? Picking a map loads a scene on ALL clients
        /// (§10.7 staging), so the test is "is a match SET UP", not "is it running". Exactly two
        /// states allow it: <c>finished</c> (the operator must be able to pick the next one) and the
        /// lobby (no match ever set up).
        /// <para>⚠️ Loading, countdown and pause are CLOSED too. All three look like the
        /// <c>paused</c> phase but the match is already set up: changing scene while loading cuts it
        /// in half, and a paused match is a frozen <i>live</i> match. The way out of both is İPTAL
        /// (<c>abort_match</c>).</para>
        /// <para>The server enforces the same rule; this copy only avoids pointless clicks.</para>
        /// </summary>
        public bool CanChangeSelection
        {
            get
            {
                if (Phase == ArenaProtocol.PHASE_PLAYING) return false;
                if (Phase == ArenaProtocol.PHASE_FINISHED) return true;

                // paused: only the lobby is free. An empty reason counts as lobby — the server
                // always fills it, but a gap must not lock the selector permanently.
                return string.IsNullOrEmpty(PhaseReason) ||
                       PhaseReason == ArenaProtocol.PAUSE_REASON_LOBBY;
            }
        }

        public float TimeRemaining { get; private set; }
        public int ScoreRed { get; private set; }
        public int ScoreBlue { get; private set; }

        /// <summary>Countdown seconds (0 outside the Countdown phase).</summary>
        public int CountdownSeconds { get; private set; }

        /// <summary>Winning TEAM from match_end ("red"/"blue"/"" = none); meaningful in phase End.</summary>
        public string WinnerTeam { get; private set; } = "";

        /// <summary>Winning PLAYER from match_end (individually scored modes); 0 = none. Never set
        /// together with the team — the UI reads whichever is filled (§5.3).</summary>
        public int WinnerPlayerId { get; private set; }

        public string ModeId { get; private set; } = "";
        public string SceneName { get; private set; } = "";
        /// <summary>Running match's score/round limit (<c>load_match</c>): <c>&gt; 0</c> = limit,
        /// <c>ArenaProtocol.SCORE_LIMIT_UNLIMITED</c> = unlimited, <c>0</c> = no match.</summary>
        public int ScoreLimit { get; private set; }

        public int RoundSeconds { get; private set; }

        /// <summary>Seconds since the last snapshot; -1 if none received.</summary>
        public float SnapshotAge
        {
            get
            {
                RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
                if (registry == null || registry.LastSnapshotMs == 0)
                {
                    return -1f;
                }

                return Mathf.Max(0f, (Environment.TickCount - registry.LastSnapshotMs) / 1000f);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnEnable()
        {
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnDisconnected += HandleDisconnected;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            NetEvents.OnHealthUpdate += HandleHealthUpdate;
            NetEvents.OnKillEvent += HandleKillEvent;
            NetEvents.OnNetStats += HandleNetStats;
            NetEvents.OnViolation += HandleViolation;
            NetEvents.OnCalibrationResult += HandleCalibrationResult;
        }

        private void OnDisable()
        {
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnDisconnected -= HandleDisconnected;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            NetEvents.OnHealthUpdate -= HandleHealthUpdate;
            NetEvents.OnKillEvent -= HandleKillEvent;
            NetEvents.OnNetStats -= HandleNetStats;
            NetEvents.OnViolation -= HandleViolation;
            NetEvents.OnCalibrationResult -= HandleCalibrationResult;
        }

        // --------------------------------------------------------------- queries

        public AdminPlayerView Find(int playerId)
        {
            return _players.TryGetValue(playerId, out AdminPlayerView view) ? view : null;
        }

        public string NameOf(int playerId)
        {
            AdminPlayerView view = Find(playerId);
            return view != null && !string.IsNullOrEmpty(view.name) ? view.name : $"Oyuncu {playerId}";
        }

        /// <summary>Next eligible player for POV (Tab): cycles CONNECTED players by playerId, since
        /// a disconnected or departed player has no camera. 0 if there are none.</summary>
        public int NextPlayerId(int currentId)
        {
            if (_all.Count == 0)
            {
                return 0;
            }

            int index = -1;
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].playerId == currentId)
                {
                    index = i;
                    break;
                }
            }

            for (int step = 1; step <= _all.Count; step++)
            {
                AdminPlayerView candidate = _all[(index + step + _all.Count) % _all.Count];
                if (candidate.IsConnected)
                {
                    return candidate.playerId;
                }
            }

            return _all[0].playerId;
        }

        /// <summary>Team totals (kills/deaths/alive count).</summary>
        public void TeamTotals(string team, out int kills, out int deaths, out int aliveCount)
        {
            kills = 0;
            deaths = 0;
            aliveCount = 0;

            for (int i = 0; i < _all.Count; i++)
            {
                AdminPlayerView view = _all[i];
                if (view.team != team)
                {
                    continue;
                }

                kills += view.kills;
                deaths += view.deaths;
                if (view.IsConnected && view.alive)
                {
                    aliveCount++;
                }
            }
        }

        // ---------------------------------------------------------- event handlers

        private void HandleConnected(WelcomeMsg msg)
        {
            if (msg?.match == null)
            {
                return;
            }

            Phase = string.IsNullOrEmpty(msg.match.phase) ? ArenaProtocol.PHASE_PAUSED : msg.match.phase;
            PhaseReason = msg.match.phaseReason ?? "";
            ModeState = msg.match.modeState ?? "";
            TimeRemaining = msg.match.timeRemaining;
            ScoreRed = msg.match.scoreRed;
            ScoreBlue = msg.match.scoreBlue;
            ModeId = msg.match.modeId ?? "";
            SceneName = msg.match.sceneName ?? "";
            Raise();
        }

        private void HandleDisconnected()
        {
            _players.Clear();
            _killFeed.Clear();
            _violationFeed.Clear();
            AdminCount = 0;
            Rebuild();
        }

        /// <summary>The server's FULL snapshot: add, update and drop departed rows.</summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg?.players == null)
            {
                return;
            }

            _removeScratch.Clear();
            foreach (KeyValuePair<int, AdminPlayerView> kv in _players)
            {
                _removeScratch.Add(kv.Key);
            }

            AdminCount = 0;

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info == null || info.playerId <= 0)
                {
                    continue;
                }

                _removeScratch.Remove(info.playerId);

                if (!_players.TryGetValue(info.playerId, out AdminPlayerView view))
                {
                    view = new AdminPlayerView { playerId = info.playerId };
                    _players.Add(info.playerId, view);
                }

                view.name = string.IsNullOrEmpty(info.name) ? $"Oyuncu {info.playerId}" : info.name;
                view.number = info.number;
                view.role = string.IsNullOrEmpty(info.role) ? AppSession.RolePlayer : info.role;
                view.team = info.team ?? "";
                view.ready = info.ready;
                // Empty/unknown counts as connected (§5.3) — the shortcuts' contract.
                view.connection = string.IsNullOrEmpty(info.connection)
                    ? ArenaProtocol.CONNECTION_CONNECTED
                    : info.connection;
                view.reconnectSeconds = info.reconnectSeconds;
                view.reconnectStampedAt = Time.unscaledTime;
                view.inMatch = info.inMatch;
                view.battery = info.battery;

                // Controller state is announced BEFORE assignment: the only input to the comparison
                // is the view's previous value, which the assignment destroys.
                if (view.IsPlayer)
                {
                    AnnounceControllerChange(view, "SOL", view.ctrlL, info.ctrlL);
                    AnnounceControllerChange(view, "SAĞ", view.ctrlR, info.ctrlR);
                }

                view.ctrlL = info.ctrlL;
                view.ctrlR = info.ctrlR;
                view.scene = info.scene ?? "";

                // Server counters OVERWRITE local ones (§5.3) — drift closes here.
                view.kills = info.kills;
                view.deaths = info.deaths;
                view.score = info.score;
                view.hp = info.hp;

                // §10.6 calibration state. The server always sends false for admin records, so it is
                // pinned to true here to keep the admin out of "uncalibrated" (see NeedsCalibration).
                view.calibrated = view.IsPlayer ? info.calibrated : true;
                view.calibrationSource = info.calibrationSource ?? "";
                view.floorOffset = view.IsPlayer ? info.floorOffset : 0f;
                view.bodyScale = view.IsPlayer ? info.bodyScale : 0f;
                view.scaleError = view.IsPlayer ? info.scaleError ?? "" : "";
                view.calibrationError = view.IsPlayer ? info.calibrationError ?? "" : "";

                if (view.alive != info.alive)
                {
                    view.alive = info.alive;
                    view.diedAt = info.alive ? -1f : Time.unscaledTime;
                }

                if (!view.IsPlayer)
                {
                    AdminCount++;
                }
            }

            for (int i = 0; i < _removeScratch.Count; i++)
            {
                _players.Remove(_removeScratch[i]);
            }

            Rebuild();
        }

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            ModeId = msg.modeId ?? "";
            SceneName = msg.sceneName ?? "";
            RoundSeconds = msg.roundSeconds;
            ScoreLimit = msg.scoreLimit;
            WinnerTeam = "";
            WinnerPlayerId = 0;
            Raise();
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            Phase = string.IsNullOrEmpty(msg.phase) ? Phase : msg.phase;
            PhaseReason = msg.phaseReason ?? "";
            ModeState = msg.modeState ?? "";
            TimeRemaining = msg.timeRemaining;
            ScoreRed = msg.scoreRed;
            ScoreBlue = msg.scoreBlue;

            if (PhaseReason != ArenaProtocol.PAUSE_REASON_COUNTDOWN)
            {
                CountdownSeconds = 0;
            }

            Raise();
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            CountdownSeconds = msg.seconds;
            Raise();
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            WinnerTeam = msg.winnerTeam ?? "";
            WinnerPlayerId = msg.winnerPlayerId;
            ScoreRed = msg.scoreRed;
            ScoreBlue = msg.scoreBlue;
            Phase = ArenaProtocol.PHASE_FINISHED;
            PhaseReason = "";
            Raise();
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg _)
        {
            Phase = ArenaProtocol.PHASE_PAUSED;
            PhaseReason = ArenaProtocol.PAUSE_REASON_LOBBY;
            ModeState = "";
            CountdownSeconds = 0;
            WinnerTeam = "";
            WinnerPlayerId = 0;
            TimeRemaining = 0f;
            _killFeed.Clear();
            _violationFeed.Clear();

            foreach (KeyValuePair<int, AdminPlayerView> kv in _players)
            {
                kv.Value.hp = ArenaProtocol.PLAYER_MAX_HP;
                kv.Value.alive = true;
                kv.Value.diedAt = -1f;
                kv.Value.score = 0; // sunucu da lobiye dönerken sıfırlıyor (§10.2)

                // The violation ledger resets with the score, mirroring the server's (§10.9): left
                // alone, the old match's count would linger until the new match's first `violation`
                // message and read as the new match's.
                kv.Value.obstacleCount = 0;
                kv.Value.obstacleSeconds = 0f;
                kv.Value.outOfBoundsCount = 0;
                kv.Value.outOfBoundsSeconds = 0f;
            }

            Raise();
        }

        /// <summary>§6.7 — per-player ping/jitter/loss, sent only to admin connections. Values for
        /// players missing from the message are KEPT, not cleared: the server only writes online
        /// players, and blanking a missing entry would make the row flicker.</summary>
        private void HandleNetStats(NetStatsMsg msg)
        {
            if (msg?.players == null)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                NetStatsEntry entry = msg.players[i];
                if (entry == null)
                {
                    continue;
                }

                AdminPlayerView view = Find(entry.playerId);
                if (view == null)
                {
                    continue;
                }

                view.rttMs = entry.rttMs;
                view.jitterMs = entry.jitterMs;
                view.lossPct = entry.lossPct;
            }

            Raise();
        }

        private void HandleHealthUpdate(HealthUpdateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            AdminPlayerView view = Find(msg.playerId);
            if (view == null)
            {
                return;
            }

            view.hp = msg.hp;

            // hp>0 ⇒ alive: the server broadcasts 0 on death and full hp on respawn (§10.4/3).
            bool alive = msg.hp > 0f;
            if (view.alive != alive)
            {
                view.alive = alive;
                view.diedAt = alive ? -1f : Time.unscaledTime;
            }

            Raise();
        }

        private void HandleKillEvent(KillEventMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            AdminPlayerView victim = Find(msg.victimId);
            if (victim != null)
            {
                victim.deaths++;
                victim.alive = false;
                victim.hp = 0f;
                victim.diedAt = Time.unscaledTime;
            }

            if (msg.killerId > 0 && msg.killerId != msg.victimId)
            {
                AdminPlayerView killer = Find(msg.killerId);
                if (killer != null)
                {
                    killer.kills++;

                    // Let the table react at once in individually scored modes. This is a GUESS (the
                    // mode writes the score and it need not be 1 per kill) — the next lobby_state
                    // overwrites it, same pattern as `kills`.
                    if (ModeRuntime.Scoring == ModeScoreKind.Player)
                    {
                        killer.score++;
                    }
                }
            }

            // ⚠️ No glyph that the default TMP font may lack; use "->".
            string weapon = string.IsNullOrEmpty(msg.weaponId) ? "" : $" [{msg.weaponId}]";
            string line = msg.killerId > 0 && msg.killerId != msg.victimId
                ? $"{NameOf(msg.killerId)} -> {NameOf(msg.victimId)}{weapon}"
                : $"{NameOf(msg.victimId)} öldü{weapon}";

            _killFeed.Add(line);
            while (_killFeed.Count > KillFeedMaxLines)
            {
                _killFeed.RemoveAt(0);
            }

            Raise();
        }

        /// <summary>Start/end of a physical violation (§10.9). The SERVER is the source — this class
        /// derives no edges, measures no time and counts nothing; it writes the incoming ledger.
        /// <para>⚠️ The message is edge triggered: a lost line is only a log loss, since the ring is
        /// fed from the snapshot bit (<c>AdminViolations.Of</c>). Short contacts (below
        /// <see cref="ArenaProtocol.VIOLATION_MIN_SECONDS"/>) are already filtered on the server —
        /// there is NO second threshold here, or the two ends would silently diverge.</para>
        /// </summary>
        private void HandleViolation(ViolationMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            AdminPlayerView view = Find(msg.playerId);
            if (view != null)
            {
                // Counters are the server's totals; no local increment (see the field docs).
                if (msg.kind == ArenaProtocol.VIOLATION_KIND_OBSTACLE)
                {
                    view.obstacleCount = msg.count;
                    view.obstacleSeconds = msg.totalSeconds;
                }
                else if (msg.kind == ArenaProtocol.VIOLATION_KIND_OUT_OF_BOUNDS)
                {
                    view.outOfBoundsCount = msg.count;
                    view.outOfBoundsSeconds = msg.totalSeconds;
                }
            }

            // Unknown kinds are written with their raw label (AdminViolations.Label): swallowing the
            // row would hide a real event from the operator.
            string label = AdminViolations.Label(msg.kind);
            string line = msg.active
                ? $"{NameOf(msg.playerId)} — {label}"
                : $"{NameOf(msg.playerId)} — {label} bitti ({msg.seconds:0.0} sn)";

            _violationFeed.Add(line);
            while (_violationFeed.Count > ViolationFeedMaxLines)
            {
                _violationFeed.RemoveAt(0);
            }

            PlayViolationSound(msg);
            Raise();
        }

        /// <summary>Forwards a calibration reload result (§5.3) to listeners.
        /// <para>⚠️ <see cref="Raise"/> is NOT called: no roster data changed — the persistent state
        /// (<see cref="AdminPlayerView.calibrated"/>, <see cref="AdminPlayerView.calibrationError"/>)
        /// is written by the next <c>lobby_state</c>. This message only answers "what happened to my
        /// attempt" for the row that is waiting.</para></summary>
        private void HandleCalibrationResult(CalibrationResultMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            CalibrationResult?.Invoke(msg);
        }

        /// <summary>Violation alert — only on the START of a violation. The end needs no operator
        /// action, and sounding it too would announce every violation twice. The gate is a screen
        /// preference (<see cref="AdminSession.ViolationSound"/>), so one operator muting it leaves
        /// the other's sound on.</summary>
        private void PlayViolationSound(ViolationMsg msg)
        {
            if (!msg.active || !AdminSession.ViolationSound)
            {
                return;
            }

            if (Time.unscaledTime - _lastViolationSoundAt < ViolationSoundCooldown)
            {
                return;
            }

            _lastViolationSoundAt = Time.unscaledTime;
            GameAudio.Play(GameSoundId.AdminViolation);
        }

        // -------------------------------------------------------------- internals

        /// <summary>Notifies the operator ONCE when a hand's controller state changes (§5.1). A lost
        /// controller staleness the hand pose and the player cannot see it themselves.
        /// <para>⚠️ The gate is "did the state CHANGE", not "is it bad": <c>lobby_state</c> is a full
        /// snapshot repeated on every broadcast, so an unconditional notice would print a dead
        /// controller to the operator once per second.</para>
        /// <para>⚠️ Transitions out of <see cref="ArenaProtocol.CONTROLLER_UNKNOWN"/> are not
        /// reported, or a client's first report would produce a "changed" per hand. For the same
        /// reason only <c>role=player</c> records are inspected — admins never report controllers.</para>
        /// <para>⚠️ The only reported event is the loss itself: entering
        /// <see cref="ArenaProtocol.CONTROLLER_LOST"/> is announced and only LEAVING it closes the
        /// notice. <see cref="ArenaProtocol.CONTROLLER_UNTRACKED"/> is normal (hand behind the back,
        /// controller out of view) and treating it as news would make the status line unusable.</para>
        /// </summary>
        private static void AnnounceControllerChange(AdminPlayerView view, string hand,
            int previous, int current)
        {
            if (previous == current || previous == ArenaProtocol.CONTROLLER_UNKNOWN)
            {
                return;
            }

            if (current == ArenaProtocol.CONTROLLER_LOST)
            {
                string lost = $"{view.name} (#{view.playerId}) {hand} kumanda düştü — " +
                              "pil bitmiş olabilir; o elin pozu bayat çizilir.";
                AdminCommands.Note(lost);
                Debug.LogWarning(lost);
                return;
            }

            // ⚠️ Recovery is reported ONLY for a controller that was LOST. UNTRACKED happens
            // constantly in the field, and calling that "it's back" would turn the status line into
            // a never-quiet stream. The symmetry therefore keys on LOST, not OK: only an announced
            // loss gets closed.
            if (previous == ArenaProtocol.CONTROLLER_LOST)
            {
                string back = $"{view.name} (#{view.playerId}) {hand} kumanda geri bağlandı.";
                AdminCommands.Note(back);
                Debug.Log(back);
            }
        }

        /// <summary>Rebuilds the team lists and the FFA decision.</summary>
        private void Rebuild()
        {
            _all.Clear();
            _red.Clear();
            _blue.Clear();

            foreach (KeyValuePair<int, AdminPlayerView> kv in _players)
            {
                if (kv.Value.IsPlayer)
                {
                    _all.Add(kv.Value);
                }
            }

            _all.Sort(ComparePlayerId);

            bool anyTeam = false;
            for (int i = 0; i < _all.Count; i++)
            {
                AdminPlayerView view = _all[i];
                if (view.team == "red")
                {
                    _red.Add(view);
                }
                else if (view.team == "blue")
                {
                    _blue.Add(view);
                }

                if (view.IsConnected && !string.IsNullOrEmpty(view.team))
                {
                    anyTeam = true;
                }
            }

            _anyConnectedTeam = anyTeam;

            // If the selected player left, move the selection to the first eligible one so POV is
            // never empty.
            if (AdminSession.SelectedPlayerId != 0 && Find(AdminSession.SelectedPlayerId) == null)
            {
                AdminSession.SelectedPlayerId = _all.Count > 0 ? _all[0].playerId : 0;
            }
            else if (AdminSession.SelectedPlayerId == 0 && _all.Count > 0)
            {
                AdminSession.SelectedPlayerId = _all[0].playerId;
            }

            Raise();
        }

        /// <summary>Team-mode decision from three sources in order (see <see cref="IsFfa"/>):
        /// (1) the running match's server rules, (2) in the lobby, the shared selection's catalog
        /// mode, (3) the heuristic fallback.</summary>
        private bool ResolveIsFfa()
        {
            // (1) If a match is loaded the rules came from the server. "Loaded" = we are not waiting
            // in the lobby; the rules are real in loading/countdown/pause/running/finished.
            if (PhaseReason != ArenaProtocol.PAUSE_REASON_LOBBY)
            {
                return ModeRuntime.Teams == ModeTeamMode.None;
            }

            // (2) No rules in the lobby: what does the operator's selected mode say?
            // ⚠️ The lobby profile is SKIPPED: the server seeds the shared selection with the venue's
            // lobby map at startup (§5.3), so ModeId is "lobby" before anyone picks. The lobby is not
            // a MATCH mode and cannot answer "will the next match have teams".
            ModeDefinition selected = AdminContent.Catalog != null
                ? AdminContent.Catalog.FindMode(AdminSelection.ModeId)
                : null;
            if (selected != null && !selected.IsLobbyProfile)
            {
                return selected.TeamMode == ModeTeamMode.None;
            }

            // (3) No catalog/selection — keep the UI from going blank.
            return !_anyConnectedTeam;
        }

        private static int ComparePlayerId(AdminPlayerView a, AdminPlayerView b)
        {
            return a.playerId.CompareTo(b.playerId);
        }

        private void Raise()
        {
            Changed?.Invoke();
        }
    }
}
