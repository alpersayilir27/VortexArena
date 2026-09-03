#nullable enable
using System.Collections.Concurrent;
using VortexArena.Protocol;
using VortexArena.Server.Core.Modes;
using VortexArena.Server.Core.World;

namespace VortexArena.Server.Core;

/// <summary>Overall match state (§10.1) — identical to the wire format, three values, no more.</summary>
/// <remarks>⚠️ Its ONLY authority is the damage gate: <c>hit_report</c> is processed only in
/// <see cref="Playing"/>. "Can I fire", "where does my weapon come from", "which HUD" are answered by
/// the MODE (<see cref="Modes.ModeRules"/>), not here. A mode needing an intermediate state does not
/// grow this enum — it uses <see cref="PauseReason.Mode"/> + <c>modeState</c>.</remarks>
public enum Phase
{
    /// <summary>Match not running: lobby, loading, countdown, pause. Damage OFF.</summary>
    Paused,

    /// <summary>Match running. The ONLY phase where damage is processed.</summary>
    Playing,

    /// <summary>Match over, scores final. Damage OFF.</summary>
    Finished
}

/// <summary>Reason for <see cref="Phase.Paused"/> (§10.1 <c>phaseReason</c>), and the core's own
/// sub-state: the tick loop decides its work (loading gate vs countdown) from here.</summary>
/// <remarks>Separate field so a mode pause (<see cref="Mode"/>, e.g. "everyone back to base") and an
/// operator pause (<see cref="Operator"/>) cannot be confused — the mode keeps its own state in
/// <c>modeState</c>, the core keeps the reason here.</remarks>
public enum PauseReason
{
    /// <summary>Not paused (<see cref="Phase.Playing"/> / <see cref="Phase.Finished"/>).</summary>
    None,

    /// <summary>Lobby staged, no match set up.</summary>
    Lobby,

    /// <summary>Scene loading gate: waiting for players' set_ready.</summary>
    Loading,

    /// <summary>Countdown.</summary>
    Countdown,

    /// <summary>Operator paused the running match.</summary>
    Operator,

    /// <summary>Mode requested the pause; its reason lives in <c>modeState</c>.</summary>
    Mode
}

/// <summary>Result of lobby staging (§10.7) — the operator's announcement is built from it.</summary>
public enum StageOutcome
{
    /// <summary>Scene changed; <c>return_to_lobby</c> sent to all clients.</summary>
    Staged,

    /// <summary>Already on that scene or the requested scene was empty — nobody reloaded.</summary>
    Unchanged,

    /// <summary>Not done; reason in <see cref="StageSceneResult.Reason"/>.</summary>
    Rejected
}

/// <summary>Staging result + human-readable reason when rejected (goes into the admin announcement).</summary>
public readonly record struct StageSceneResult(StageOutcome Outcome, string Reason = "");

/// <summary>One object's ownership as the lock-free mirror carries it (§6.12): who owns it and whether it
/// is still IN a hand.</summary>
/// <remarks><c>Held</c> travels along because the <c>0x09</c> gate needs BOTH facts: between
/// <c>object_release</c> and <c>object_rest</c> the object is owned but NOT held, and that is the only
/// window where a pose packet is accepted (a held object hangs off the hand, which already streams).</remarks>
internal readonly record struct ObjectOwnership(int PlayerId, bool Held);

/// <summary>Server-authoritative match flow (§10): phase machine, per-player load_match, countdown,
/// match_state broadcast, hit validation, scoring and free-roam revive. The client is
/// presentation+input; it applies no damage, keeps no score, changes no phase.</summary>
/// <remarks><b>Lock contract:</b> all match state (phase, score, time + PlayerState's match fields) is
/// read/written under <c>_gate</c>. NEVER await and NEVER send under the lock: messages are built under
/// it into <c>outbox</c> and sent after release. Same reason, <c>IGameMode</c> hooks and event-raising
/// registry methods (SetTeam/SetReady) are called OUTSIDE the lock — the public API modes use (ScoreRed,
/// AddScore, ConnectedPlayers…) takes its own lock, so no re-entry.
/// PlayerRegistry.Snapshot()/TryGetByPlayerId do NOT take the registry lock (ConcurrentDictionary), so
/// calling them under _gate is safe.</remarks>
public sealed class MatchDirector
{
    /// <summary>Match tick at 10 Hz: enough resolution for countdown/time/obstacle penalty, and
    /// independent of the 20 Hz snapshot loop.</summary>
    private const int TickIntervalMs = 100;

    /// <summary>One send, built under the lock and dispatched outside it.</summary>
    private readonly record struct Outgoing(ClientConnection Connection, string Json, string Who);

    private readonly object _gate = new();
    private readonly PlayerRegistry _registry;

    /// <summary>Map catalog (config/maps.json — Unity export). May be EMPTY: then map validation and
    /// the spawn slot limit are disabled (§10.1).</summary>
    private readonly MapTable _maps;

    private readonly Dictionary<string, IGameMode> _modes = new(StringComparer.Ordinal);

    /// <summary>Network object state of the staged scene (§10.10); guarded by <see cref="_gate"/> — it
    /// has no lock of its own, so every touch happens under the lock.</summary>
    private readonly WorldObjectTable _objects = new();

    /// <summary>netId → ownership, kept in step with <see cref="_objects"/> but readable WITHOUT the
    /// lock: StateHost validates <c>0x09</c> ownership on the recv thread (§6.12) and must never stall
    /// the 20 Hz pose intake behind the match lock (the <c>Alive</c> pattern).</summary>
    private readonly ConcurrentDictionary<int, ObjectOwnership> _objectOwners = new();

    /// <summary>Last pose seen on <c>0x09</c> per object (§6.12). The server does NOT write it into the
    /// table as state — it is the pose an object is FREED at when its owner drops or dies; without it
    /// the object would teleport back to where it was picked up.</summary>
    private readonly ConcurrentDictionary<int, PoseData> _lastObjectPoses = new();

    /// <summary>"Clear ready flag" work collected under the lock; applied OUTSIDE it because
    /// registry.SetReady raises an event (lobby_state broadcast).</summary>
    private readonly List<string> _readyClearQueue = new();

    /// <summary>Sends produced by mode hooks (synchronous, outside the lock). Hooks cannot
    /// <c>await</c> (<see cref="IGameMode.OnTick"/> is <c>void</c>) and must not send directly (lock
    /// contract + ordering). So messages land here and the tick loop dispatches them via
    /// <see cref="FlushPendingAsync"/> after each hook group: one sender, preserved order, no send
    /// under the lock.</summary>
    private readonly List<Outgoing> _pendingOutbox = new();

    /// <summary>Did this match start with at least one player? Decides how "no players left" is read
    /// in Loading (§10.1): a match started with none is an admin map preview and does NOT return to
    /// the lobby on its own.</summary>
    private bool _startedWithPlayers;

    /// <summary>"Refresh the roster" request flagged under the lock. hp/alive/kills/deaths travel in
    /// lobby_state (§5.3) and are the admin statistics table's source. Applied OUTSIDE the lock
    /// (FlushRosterRefresh) since registry.Announce raises an event; the last changed player is kept
    /// instead of a bool because Announce's signature wants a PlayerState.</summary>
    private PlayerState? _rosterRefreshFor;

    /// <summary>"Close the match ledger" request flagged under the lock (§10.2): dropping <c>left</c>
    /// records + clearing <c>inMatch</c> flags. Applied OUTSIDE the lock
    /// (<see cref="FlushParticipantCleanup"/>) since the registry raises events.</summary>
    private bool _participantCleanupPending;

    /// <summary>Per-shooter throttle interval for hit rejection logs: keeps the console from drowning
    /// while someone keeps firing at a dead target (real players do this).</summary>
    private const double RejectLogIntervalSeconds = 2.0;

    /// <summary>§10.3 gate 2: how long a DEAD shooter's damage still counts. A thrown bomb leaves the
    /// hand before its owner dies; dropping the blast would cancel a throw for a death that happened
    /// after it. Covers fuse + flight with room to spare — and the window is the reason the gate
    /// exists at all: outside it, a reconnecting client that still thinks it is alive damages nobody.
    /// <para>⚠️ Server-side only, never on the wire. A timed effect (damage pool) must not outlive
    /// it — its last ticks would be rejected silently.</para></summary>
    private const double PosthumousDamageSeconds = 10.0;

    // Log state — NOT match state, hence kept here (not in PlayerState) under its own small lock.
    // _rejectLogGate NEVER takes _gate (only the _gate → _rejectLogGate direction exists).
    private readonly object _rejectLogGate = new();
    private readonly Dictionary<int, DateTime> _lastRejectLogAt = new();
    private readonly Dictionary<int, int> _suppressedRejects = new();

    private Phase _phase = Phase.Paused;
    private PauseReason _pauseReason = PauseReason.Lobby;

    /// <summary>The mode's own sub-state (§10.1 <c>modeState</c>). The core does NOT interpret it, only
    /// carries it; the client HUD reads it. Preserved across pauses so the mode can resume.</summary>
    private string _modeState = "";

    /// <summary>An operator <c>mode_continue</c> waiting to be picked up (§5.2).</summary>
    /// <remarks>⚠️ The core does NOT act on it — it only carries the press to the mode's next tick,
    /// which CONSUMES it (<see cref="ConsumeModeContinue"/>). Handling it here would make the core the
    /// second owner of a pause the mode set (§10.1).</remarks>
    private bool _modeContinuePending;

    private string _modeId = "";
    private string _sceneName = "";

    /// <summary>When the current scene was staged (§5.3 <c>sceneElapsed</c>). Refreshed ONLY when the
    /// scene CHANGES — match start/end do not touch it, since it measures the scene, not the match:
    /// client ambience must not restart while the map stays the same.</summary>
    private DateTime _sceneStagedUtc = DateTime.UtcNow;

    private float _timeRemaining;
    private int _scoreRed;
    private int _scoreBlue;
    private int _roundSeconds;
    private int _scoreLimit;

    /// <summary>Length of every countdown in this match (§5.2 <c>start_match.countdownSeconds</c>);
    /// <see cref="ArenaProtocol.COUNTDOWN_SECONDS"/> when the admin gives none. Round-based modes use it
    /// between rounds too.</summary>
    private int _countdownSeconds = ArenaProtocol.COUNTDOWN_SECONDS;

    private IGameMode? _mode;

    /// <summary>Rule shape of the running match (§10.5); the TDM default when there is no match, so the
    /// lobby also has a meaningful answer and no reader needs a null check.</summary>
    /// <remarks>⚠️ Its ONLY writer is <see cref="ApplyRulesLocked"/>: the friendly-fire switch overrides
    /// the mode's rule, so every assignment must pass through that gate.</remarks>
    private ModeRules _rules = ModeRules.TeamDefault;

    /// <summary>Friendly-fire switch (§5.2) — a SERVER SESSION setting, not a mode rule.</summary>
    /// <remarks><c>false</c> at startup; only <see cref="SetFriendlyFireAsync"/> changes it. Match start,
    /// map staging and returning to the lobby do NOT reset it — the operator's setting lives until the
    /// server restarts (same contract as the time/limit selection).
    /// <para>⚠️ Modes do NOT declare this field. <see cref="ModeRules.FriendlyFire"/> only carries the
    /// currently effective value on the wire; two writers would give "which one is right" two
    /// answers.</para></remarks>
    private bool _friendlyFire;

    /// <summary>Lock-free publication of the shot event relay gate (§6.5) — phase
    /// <see cref="Phase.Playing"/> OR <c>rules.fireWhilePaused</c>. Its ONLY writer is
    /// <see cref="RefreshShotRelayLocked"/>.</summary>
    private volatile bool _shotRelayOpen;

    private DateTime _phaseEnteredAt = DateTime.UtcNow;

    /// <summary>Next deadline for 1 Hz work (countdown ticks + match_state while Live).</summary>
    private DateTime _nextSecondAt = DateTime.UtcNow;

    /// <summary><c>health_update</c> cadence for obstacle damage (ms) — NOT the tick cadence (§10.9,
    /// rationale in <see cref="TickObstacleLocked"/>). 4 Hz: three-HP steps at 12 HP/s.</summary>
    private const int ObstacleHealthIntervalMs = 250;

    /// <summary>Next health announcement deadline for obstacle damage. Belongs to the tick loop, NOT to
    /// a player: everyone draining at once is announced on the same cadence.</summary>
    private DateTime _nextObstacleHealthAt = DateTime.UtcNow;

    private int _countdownRemaining;

    /// <summary>Entered Live; IGameMode.OnMatchStart to be called outside the lock.</summary>
    private bool _matchStartPending;

    /// <summary>Entered Live; IGameMode.OnRoundStart to be called outside the lock. Set on EVERY Live
    /// entry — that is the difference from <see cref="_matchStartPending"/>.</summary>
    private bool _roundStartPending;

    /// <summary>Has this match entered Live at least once. Keeps <c>OnMatchStart</c> to once per match:
    /// a round-based mode re-enters Live every round, and announcing "match started" each time would make
    /// the mode reset its match state every round.</summary>
    private bool _matchStarted;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>This venue's lobby scene (§10.7, <c>server.json → lobbyScene</c>).</summary>
    /// <remarks>⚠️ Cannot be empty: if the server cannot resolve it at startup it does not start at all
    /// (§11 fail-fast, <c>Program.cs</c>). The server's staged scene is the client's ONLY routing source.
    /// <para>⚠️ The lobby is not a MATCH but a KIND — this field fills <c>_sceneName</c>/<c>_modeId</c>
    /// outside a match. The damage gate (§10.3) still reads the phase.</para></remarks>
    private readonly string _lobbyScene;

    /// <summary>Hamburgerci balance (<c>server.json → burger</c>), already sanitized.</summary>
    private readonly BurgerSettings _burger;

    public MatchDirector(PlayerRegistry registry, MapTable maps, string lobbyScene = "",
        BurgerSettings? burger = null)
    {
        _registry = registry;
        _maps = maps;
        // Sanitized once here, not per match: a bad file must be reported at startup, not on the
        // operator's first shift.
        _burger = (burger ?? new BurgerSettings()).Sanitized();
        // Left empty, the venue's own lobby map takes over (§11) — so this is not a field every install
        // must fill in by hand; config only covers the exception.
        _lobbyScene = string.IsNullOrWhiteSpace(lobbyScene) ? maps.ResolveLobbyScene() : lobbyScene.Trim();
        RegisterModes();
        // State already starts as Paused/Lobby; write the lobby profile up front so the first welcome
        // (before any EnterLobbyLocked ran) carries the right scene/modId/rules.
        _modeId = _lobbyScene.Length > 0 ? ArenaProtocol.LOBBY_MODE_ID : "";
        SetSceneLocked(_lobbyScene);
        // The startup lobby is a staging too (§10.10): without it the first welcome would carry no
        // world_state and the lobby's objects would stay unknown until the first map change.
        RebuildObjectsLocked(_lobbyScene);
        ApplyRulesLocked(LobbyProfileForSceneLocked(_lobbyScene));
        RefreshShotRelayLocked();
    }

    /// <summary>The ONLY writer of the staged scene: refreshes the staging stamp
    /// (<see cref="_sceneStagedUtc"/>) only on a real change.</summary>
    /// <remarks>Rewriting the same scene PRESERVES the stamp — a second match on the same map must not
    /// restart the client's ambience.</remarks>
    private void SetSceneLocked(string sceneName)
    {
        if (string.Equals(_sceneName, sceneName, StringComparison.Ordinal)) return;

        _sceneName = sceneName;
        _sceneStagedUtc = DateTime.UtcNow;
    }

    /// <summary>Seconds since the current scene was staged (§5.3 <c>sceneElapsed</c>).</summary>
    private float SceneElapsedLocked =>
        (float)Math.Max(0d, (DateTime.UtcNow - _sceneStagedUtc).TotalSeconds);

    /// <summary>The ONLY writer of the rule shape: stamps the operator's friendly-fire switch onto the
    /// mode's/lobby's rules (§10.5).</summary>
    /// <remarks>⚠️ Functionally only the <c>start_match</c> call carries weight — damage is processed
    /// only in <see cref="Phase.Playing"/>, reachable only from there. Lobby-profile calls go through
    /// here so <c>welcome.match.rules</c> does not lie: with the switch on, sending
    /// <c>friendlyFire:false</c> to a client connecting in the lobby would make the first client that
    /// reads the field draw the feature wrong.</remarks>
    private void ApplyRulesLocked(ModeRules baseRules) =>
        _rules = baseRules with { FriendlyFire = _friendlyFire };

    /// <summary>Which lobby profile a scene runs with (§10.7): the OPEN SCENE's game family decides,
    /// not the selected mode.</summary>
    /// <remarks>⚠️ The single decision point — the normal profile grants a random weapon on grip, which
    /// on a kids map would arm a child while the match is still being set up. Any new path that writes
    /// scene + rules must come through here.</remarks>
    private ModeRules LobbyProfileForSceneLocked(string sceneName) =>
        _maps.TryGet(sceneName, out var entry) && MapTable.IsKids(entry)
            ? ModeRules.KidsLobbyProfile
            : ModeRules.LobbyProfile;

    /// <summary>Whether shot events (<c>0x03</c>/<c>0x04</c>, §6.4/6.5) are relayed: phase
    /// <c>playing</c> OR <c>rules.fireWhilePaused</c>. The shooter's own conditions (online + player +
    /// alive + calibrated) are not part of it; <see cref="StateHost"/> reads those per player.</summary>
    /// <remarks>⚠️ Why a flag and not a locked read (§10.3): this gate is read on the UDP recv thread,
    /// and that thread MUST NOT enter <c>_gate</c> — doing so would stall the 20 Hz pose intake behind
    /// the match lock. Same lock-free pattern as <see cref="PlayerState.Alive"/>: the flag is published
    /// volatile on phase/rule changes and the event path only reads it. One tick of lag is irrelevant for
    /// PRESENTATION (this is not the damage gate; that one is on WS and locked).</remarks>
    public bool ShotRelayOpen => _shotRelayOpen;

    /// <summary>The server's configured lobby scene — for the startup log and validation.</summary>
    public string LobbyScene => _lobbyScene;

    /// <summary>Venue played in this session (§11); empty when there is no venue split.</summary>
    public string VenueId => _maps.Venue;

    /// <summary>This venue's map names — sent to admins in <c>admin_state</c> so their map pickers show
    /// only playable arenas.</summary>
    public IReadOnlyList<string> VenueScenes => _maps.SceneNames;

    /// <summary>The ONLY registration point for modes the server knows — a new mode adds one line here
    /// (CLAUDE.md "Yeni mod" recipe). start_match with an unregistered modId is rejected.</summary>
    private void RegisterModes()
    {
        Register(new TdmMode());
        Register(new FfaMode());
        Register(new TournamentMode());
        Register(new BurgerMode(_burger));
        Register(new MoleMode());
    }

    private void Register(IGameMode mode) => _modes[mode.ModeId] = mode;

    /// <summary>Registered mode ids (for the startup summary / rejection messages).</summary>
    public IReadOnlyCollection<string> ModeIds => _modes.Keys;

    /// <summary>A mode's team mode — the source of <c>selection_state.teamMode</c> (§5.3). An
    /// unregistered <paramref name="modeId"/> (including the lobby, which is not an
    /// <see cref="IGameMode"/>) falls back to the lobby profile.</summary>
    /// <remarks>⚠️ Carries NO rules: only the single field needed for PRESENTATION passes here.
    /// Publishing the whole mode would make a not-yet-started match's rules readable as "active" on the
    /// client (§10.5 authority is <c>load_match.rules</c>).</remarks>
    public TeamMode TeamModeOf(string modeId)
    {
        if (!string.IsNullOrEmpty(modeId) && _modes.TryGetValue(modeId, out var mode))
            return mode.Rules.Teams;

        // Both lobby profiles carry the same Teams, so the scene family is irrelevant here.
        return ModeRules.LobbyProfile.Teams;
    }

    public Phase CurrentPhase
    {
        get { lock (_gate) return _phase; }
    }

    /// <summary>Is mode/map selection and staging (§10.7) currently allowed? Exactly two states qualify:
    /// <see cref="Phase.Finished"/>, and <see cref="Phase.Paused"/> + <see cref="PauseReason.Lobby"/>.</summary>
    /// <remarks>The criterion is NOT "is a match running" but "is a match SET UP": picking a map is a
    /// scene command sent to everyone (§10.7), and the scene cannot be pulled out from under a match that
    /// is being set up or is frozen. Hence <c>loading</c>/<c>countdown</c> (setting up) and
    /// <c>operator</c>/<c>mode</c> (frozen) are CLOSED too — a frozen match is still a set-up match, and
    /// its scene must not have changed when the operator resumes. A phase-only gate would leave these
    /// four open: the intermediate state lives in <see cref="PauseReason"/>, not <see cref="Phase"/>.
    /// <para><see cref="Phase.Finished"/> is deliberately OPEN: the match is over, the operator must be
    /// able to pick the next one without waiting for the lobby return.</para></remarks>
    public bool CanChangeSelection
    {
        get { lock (_gate) return CanChangeSelectionLocked; }
    }

    /// <summary>In-lock form — the lock contract forbids calling public properties under the lock
    /// (<see cref="_gate"/> is reentrant, but the single pattern is kept).</summary>
    private bool CanChangeSelectionLocked =>
        _phase == Phase.Finished || (_phase == Phase.Paused && _pauseReason == PauseReason.Lobby);

    /// <summary>Staging rejection reason shown to the operator. INCLUDES the state: a fixed "match in
    /// progress" text would lie about a paused match and hide what is blocking.</summary>
    private string RejectReasonLocked() =>
        $"maç kurulu ({Describe(_phase, _pauseReason)}) — önce İPTAL edin";

    // ---- Public API used by IGameModes (all lock-safe, called from OUTSIDE the lock) ----

    public int ScoreRed
    {
        get { lock (_gate) return _scoreRed; }
    }

    public int ScoreBlue
    {
        get { lock (_gate) return _scoreBlue; }
    }

    public float TimeRemaining
    {
        get { lock (_gate) return _timeRemaining; }
    }

    public int RoundSeconds
    {
        get { lock (_gate) return _roundSeconds; }
    }

    /// <summary>Score/round limit of the running match. <c>&gt; 0</c> = limit; <c>&lt;= 0</c> = NO limit
    /// (<see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/> is the operator's pick, <c>0</c> is the reset
    /// state with no match).</summary>
    /// <remarks>⚠️ Modes always read it behind a <c>limit &gt; 0</c> gate so no limit branch runs in an
    /// unlimited match. A comparison skipping the gate (<c>score &gt;= limit</c>) would end an unlimited
    /// match on the first point.</remarks>
    public int ScoreLimit
    {
        get { lock (_gate) return _scoreLimit; }
    }

    /// <summary>Console/announcement text of the limit: <c>sınırsız</c> · <c>mod limiti</c> · a number.
    /// The single wording the operator sees lives here so the server window and the admin panel do not
    /// drift into two phrasings.</summary>
    public static string DescribeScoreLimit(int scoreLimit) =>
        scoreLimit > 0 ? scoreLimit.ToString()
        : scoreLimit < 0 ? "sınırsız"
        : "mod limiti";


    /// <summary>Rule shape of the running match (§10.5); the TDM default when there is no match.</summary>
    public ModeRules Rules
    {
        get { lock (_gate) return _rules; }
    }

    // ---- Score ledger (§10.2) ----
    // Modes read/write score ONLY through here; two channels exist and ModeRules.Scoring picks which:
    // team score (match_state) or individual score (lobby_state). They sit in one section so extracting
    // a separate Scoreboard class later is a mechanical move — that is why the phase machine was not
    // grown instead.

    /// <summary>Adds to the team score (kill/objective rules come from the modes).</summary>
    public void AddScore(string team, int amount)
    {
        lock (_gate)
        {
            if (team == "red") _scoreRed += amount;
            else if (team == "blue") _scoreBlue += amount;
        }
    }

    /// <summary>Adds to the individual score (§10.2 <c>score</c>). Only raises the "refresh roster" flag,
    /// never broadcasts itself: this is called from a mode hook (outside the lock) and broadcasting raises
    /// a registry event. The flag is drained on the next tick (≤100 ms) — no extra message type or
    /// broadcast loop is needed to get the score into lobby_state.</summary>
    public void AddPlayerScore(int playerId, int amount)
    {
        if (playerId <= 0 || amount == 0) return;
        lock (_gate)
        {
            if (!_registry.TryGetByPlayerId(playerId, out var player)) return;
            if (player.Role != "player") return;
            player.Score += amount;
            _rosterRefreshFor = player;
        }
    }

    /// <summary>Adds to BOTH the player's individual score and the shared total in one call
    /// (<c>scoring:"shared"</c>, §10.5/§10.2) — two writes that can never drift apart because there is no
    /// way to do one without the other.</summary>
    /// <remarks>⚠️ The shared total rides on <c>scoreRed</c> and <c>scoreBlue</c> STAYS 0: with
    /// <c>teams:"none"</c> there is no second side, so nothing may read this pair as a team score.</remarks>
    public void AddSharedScore(int playerId, int amount)
    {
        if (playerId <= 0 || amount == 0) return;
        lock (_gate)
        {
            if (!_registry.TryGetByPlayerId(playerId, out var player)) return;
            if (player.Role != "player") return;
            player.Score += amount;
            _rosterRefreshFor = player;
            _scoreRed += amount;
        }
    }

    /// <summary>A player's individual score; 0 when the player is unknown.</summary>
    public int ScoreOf(int playerId)
    {
        lock (_gate)
        {
            return _registry.TryGetByPlayerId(playerId, out var player) ? player.Score : 0;
        }
    }

    /// <summary>Leader of the individual score. Returns false on a TIE (no single winner) — the calling
    /// mode reads that as a draw; silently picking the first player would declare a wrong winner. Also
    /// false when no player is connected.</summary>
    public bool TryGetLeader(out int playerId, out int score)
    {
        playerId = 0;
        score = 0;

        lock (_gate)
        {
            var tied = false;
            foreach (var player in ConnectedPlayersLocked())
            {
                if (playerId == 0 || player.Score > score)
                {
                    playerId = player.PlayerId;
                    score = player.Score;
                    tied = false;
                    continue;
                }

                if (player.Score == score) tied = true;
            }

            if (playerId != 0 && !tied) return true;
        }

        playerId = 0;
        score = 0;
        return false;
    }

    /// <summary>Snapshot of CONNECTED players (role=player) — modes iterate it outside the lock;
    /// PlayerState fields may change during the read (int/string reads are atomic).</summary>
    /// <remarks>Dropped (<c>reconnecting</c>) and removed (<c>left</c>) records are NOT in the list
    /// (§2) — modes never see them.</remarks>
    public IEnumerable<PlayerState> ConnectedPlayers()
    {
        lock (_gate) return ConnectedPlayersLocked();
    }

    /// <summary>Writes a late-joining player into the match ledger (§10.2) — called on the <c>hello</c>
    /// path, after welcome is sent.</summary>
    /// <remarks>⚠️ Does work only while a match is SET UP: in the lobby (phase <c>paused</c> + lobby
    /// profile) there is no such thing as a participant, and in <c>finished</c> the ledger is already
    /// closed — writing there would add a row to the end-of-match table for someone who never played.
    /// <para>Admins are never written: they do not play and have no statistics.</para></remarks>
    public void MarkParticipantIfMatchRunning(PlayerState player)
    {
        if (player.Role != "player") return;
        bool running;
        lock (_gate) running = _mode != null && _phase != Phase.Finished;
        if (running) _registry.SetMatchParticipant(player.PlayerId, true);
    }

    /// <summary>welcome.match snapshot — used by the late-join sync (§5.3).</summary>
    public MatchInfo CurrentMatchInfo()
    {
        lock (_gate) return BuildMatchInfoLocked();
    }

    /// <summary>Open (announced) violations as <c>violation active:true</c> messages, for an admin that
    /// connects mid-violation (§5.3): the start edge went out before this admin existed, and without a
    /// replay its feed would show only the end line.</summary>
    public List<string> BuildOpenViolationJsons()
    {
        var result = new List<string>();
        lock (_gate)
        {
            foreach (var player in _registry.Snapshot())
            {
                if (player.Role != "player" || !player.IsConnected) continue;
                AppendOpenViolationLocked(result, player, player.ObstacleTally,
                    ArenaProtocol.VIOLATION_KIND_OBSTACLE);
                AppendOpenViolationLocked(result, player, player.OutOfBoundsTally,
                    ArenaProtocol.VIOLATION_KIND_OUT_OF_BOUNDS);
            }
        }
        return result;
    }

    private static void AppendOpenViolationLocked(List<string> sink, PlayerState player,
        ViolationTally tally, string kind)
    {
        if (!tally.Announced) return;
        sink.Add(JsonUtil.Serialize(new ViolationMsg
        {
            playerId = player.PlayerId,
            kind = kind,
            active = true,
            seconds = 0f,
            count = tally.Count,
            totalSeconds = tally.TotalSeconds
        }));
    }

    // ---- Tick loop ----

    public void Start()
    {
        if (_loop is { IsCompleted: false }) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => TickLoopAsync(token));
    }

    /// <summary>Cancel → drain the tick → drop the pending outbox. Idempotent.</summary>
    /// <remarks>⚠️ The outbox is dropped, not flushed: whatever the last tick queued would go out
    /// while the control host is already closing the sockets. Cleared under <c>_gate</c> (lock
    /// contract) and nothing is sent while holding it.</remarks>
    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;
        if (cts == null && loop == null) return;

        cts?.Cancel();
        await ServiceShutdown.DrainAsync("match", loop);
        lock (_gate) _pendingOutbox.Clear();
        cts?.Dispose();
    }

    private async Task TickLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));
        var last = DateTime.UtcNow;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token)) break;
            }
            catch (OperationCanceledException) { break; }

            var now = DateTime.UtcNow;
            var delta = (float)(now - last).TotalSeconds;
            last = now;
            try
            {
                await TickAsync(now, delta);
            }
            catch (Exception ex)
            {
                // One bad tick must not kill the match loop.
                Console.WriteLine($"[match] tick hatası: {ex.Message}");
            }
        }
    }

    private async Task TickAsync(DateTime now, float deltaSeconds)
    {
        var outbox = new List<Outgoing>();
        IGameMode? modeToStart;
        IGameMode? modeToRoundStart;
        IGameMode? modeToTick = null;

        lock (_gate)
        {
            // Dispatch on the (phase, reason) pair: a single `paused` on the wire can be three different
            // jobs in the core (loading gate, countdown, plain waiting).
            switch (_phase)
            {
                case Phase.Paused when _pauseReason == PauseReason.Loading:
                    TickLoadingLocked(outbox, now);
                    break;
                // ⚠️ The mode DOES tick during the countdown. The core runs the counter, but in a
                // round-based mode the countdown is a REVERSIBLE decision (cancelled if one of the
                // gathered players leaves their base, see TryCancelCountdownForMode) — it needs ticks to
                // poll that. If the counter hits zero on this tick the phase becomes Playing and the mode
                // gets OnRoundStart + OnTick in the same tick; ordering holds.
                case Phase.Paused when _pauseReason == PauseReason.Countdown:
                    TickCountdownLocked(outbox, now);
                    modeToTick = _mode;
                    break;
                // ⚠️ The MODE set this pause (§10.1) → it also owns lifting it, so it must tick; a mode
                // without ticks could never poll its gathering gate. Time does NOT advance
                // (TickLiveLocked is not called) and damage is off (phase paused). On an operator pause
                // the mode does NOT tick: a frozen match stays frozen.
                case Phase.Paused when _pauseReason == PauseReason.Mode:
                    modeToTick = _mode;
                    break;
                // Lobby/Operator: nothing to wait for, no counter runs.
                case Phase.Playing:
                    modeToTick = TickLiveLocked(outbox, now, deltaSeconds);
                    break;
                case Phase.Finished:
                    TickEndLocked(outbox, now);
                    break;
            }

            // ⚠️ The violation ledger sits OUTSIDE the switch, i.e. runs in every phase (rationale in
            // TickViolationFeedLocked): the penalty applies only in `playing`, but the operator must also
            // see a player leaving the arena in the lobby and during the countdown.
            TickViolationFeedLocked(outbox, now);
            // Also outside the switch, for the same reason: an object held in the lobby must be freed
            // when its owner drops, not only during a match.
            TickObjectOwnersLocked(outbox);

            modeToStart = _matchStartPending ? _mode : null;
            modeToRoundStart = _roundStartPending ? _mode : null;
            _matchStartPending = false;
            _roundStartPending = false;
        }

        await FlushAsync(outbox);
        FlushReadyClear();
        FlushRosterRefresh();
        FlushParticipantCleanup(); // close the ledger if we returned to the lobby (§10.2 — AFTER messages)

        // Mode hooks run OUTSIDE the lock (lock contract above). Sends they produce collect in
        // _pendingOutbox and are dispatched after each hook group.
        modeToStart?.OnMatchStart(this);
        modeToRoundStart?.OnRoundStart(this);

        if (modeToTick == null)
        {
            await FlushPendingAsync();
            return;
        }

        modeToTick.OnTick(this, deltaSeconds);
        await FlushPendingAsync();

        // IsMatchOver is asked during a mode pause too (a mode may end the round and close the match
        // there); EnterEndAsync only proceeds from Playing, so the phase machine stays intact.
        if (modeToTick.IsMatchOver(this, out var outcome))
            await EnterEndAsync(outcome);
    }

    /// <summary>Moves to Countdown once all CONNECTED players report "scene loaded" (set_ready) or
    /// LOADING_TIMEOUT expires. Dropped players are not waited for.</summary>
    private void TickLoadingLocked(List<Outgoing> outbox, DateTime now)
    {
        var players = ConnectedPlayersLocked();
        if (players.Count == 0)
        {
            // The distinction matters (§10.1): a match that STARTED with players and lost its last one
            // should be abandoned for the lobby. A match STARTED without players (admin map preview) has
            // nobody to wait for and goes straight to Countdown; its exit is the operator's
            // abort_match/return_to_lobby.
            if (_startedWithPlayers)
            {
                Console.WriteLine("[match] loading: bağlı oyuncu kalmadı — lobiye dönülüyor.");
                EnterLobbyLocked(outbox, now);
                return;
            }

            EnterCountdownLocked(outbox, now);
            return;
        }

        var notReady = players.Where(p => !p.Ready).ToList();
        if (notReady.Count == 0)
        {
            EnterCountdownLocked(outbox, now);
            return;
        }

        if ((now - _phaseEnteredAt).TotalSeconds >= ArenaProtocol.LOADING_TIMEOUT)
        {
            Console.WriteLine($"[match] loading zaman aşımı ({ArenaProtocol.LOADING_TIMEOUT:0} sn) — " +
                              $"hazır olmayanlar: {string.Join(", ", notReady.Select(p => p.Name))}");
            EnterCountdownLocked(outbox, now);
        }
    }

    private void TickCountdownLocked(List<Outgoing> outbox, DateTime now)
    {
        if (now < _nextSecondAt) return;
        _nextSecondAt = now.AddSeconds(1);
        _countdownRemaining--;
        if (_countdownRemaining > 0)
        {
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(new CountdownMsg { seconds = _countdownRemaining }));
            return;
        }
        EnterLiveLocked(outbox, now);
    }

    /// <summary>Advances the clock and broadcasts match_state at 1 Hz. Returns the mode to use outside
    /// the lock for OnTick/IsMatchOver (null when there is none).</summary>
    private IGameMode? TickLiveLocked(List<Outgoing> outbox, DateTime now, float deltaSeconds)
    {
        _timeRemaining = MathF.Max(0f, _timeRemaining - deltaSeconds);

        TickObstacleLocked(outbox, now, deltaSeconds);

        if (now >= _nextSecondAt)
        {
            _nextSecondAt = now.AddSeconds(1);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }
        return _mode;
    }

    /// <summary>§10.9: drains health of players reporting they are INSIDE an interior obstacle
    /// (<see cref="ArenaProtocol.OBSTACLE_DAMAGE_PER_SECOND"/> · seconds).</summary>
    /// <remarks>Authority split is the same as <c>hit_report</c>: the client MEASURES (is my head inside
    /// the obstacle), the server WRITES the result. The server cannot VALIDATE the violation — arena
    /// geometry is not here and is not brought in (it would be a second source of truth). What it does is
    /// BOUND the result: it times the penalty on its own clock, drops stale flags, and applies the
    /// phase/alive/calibration gates itself.
    /// <para>⚠️ The calibration gate is mandatory (§10.6): on a misaligned headset the virtual obstacle
    /// drifts from the real one and detection turns false-positive — the player would die for nothing.</para>
    /// <para>⚠️ The flag is NOT reset on death and must not be: the player is still inside the obstacle
    /// and the client keeps reporting it at 20 Hz. Clearing it on revive would give the player a
    /// permanent shelter inside the obstacle.</para>
    /// <para>The penalty has two stages (§10.9): first
    /// <see cref="ArenaProtocol.OBSTACLE_GRACE_SECONDS"/> of grace (no health loss at all), then a drain
    /// from full health to death within <see cref="ArenaProtocol.OBSTACLE_DRAIN_SECONDS"/>. The grace
    /// clock is the SERVER's (<see cref="PlayerState.ObstacleSince"/>) — a duration from the client would
    /// hand it the penalty.</para>
    /// <para>⚠️ NO penalty in a weaponless mode (<see cref="WeaponSource.None"/>): those modes have no
    /// revive either, so an obstacle death would last the whole shift. The darkening, the warning and the
    /// admin ring stay — only the health loss is off.</para></remarks>
    private void TickObstacleLocked(List<Outgoing> outbox, DateTime now, float deltaSeconds)
    {
        var damage = ArenaProtocol.OBSTACLE_DAMAGE_PER_SECOND * deltaSeconds;
        if (damage <= 0f) return;

        // Health announcements go out on this interval, NOT the tick cadence (rationale below).
        var announce = now >= _nextObstacleHealthAt;
        if (announce) _nextObstacleHealthAt = now.AddMilliseconds(ObstacleHealthIntervalMs);

        foreach (var player in ConnectedPlayersLocked())
        {
            // ⚠️ ONE gate: the grace clock resets the moment any penalty condition drops. With separate
            // `continue`s, a player who died and revived would find the grace already spent and start
            // draining the instant they respawn.
            if (!IsObstacleFlagLiveLocked(player, now) || !player.Alive || !player.Calibrated ||
                _rules.Weapons == WeaponSource.None)
            {
                player.ObstacleSince = null;
                continue;
            }

            player.ObstacleSince ??= now;
            if ((now - player.ObstacleSince.Value).TotalSeconds < ArenaProtocol.OBSTACLE_GRACE_SECONDS)
            {
                // Grace: no health loss, no announcement. The player is blind meanwhile (the client
                // darkens the screen) but unpunished — enough time to step out of the obstacle.
                continue;
            }

            player.Hp = MathF.Max(0f, player.Hp - damage);

            // ⚠️ Announcements are THROTTLED, and this is not a micro-optimisation: this damage is
            // CONTINUOUS, not event-based. Broadcast every tick, a single obstacle death (≈8 s × tickHz)
            // would produce hundreds of WS messages, each going to the victim + EVERY ADMIN. The HUD
            // draws a rounded integer anyway, so this cadence loses no information. The death packet is
            // NEVER throttled.
            if (announce || player.Hp <= 0f)
            {
                // attackerId = 0: environmental damage, not an attack (§10.9).
                QueueHealthUpdateLocked(outbox, player, JsonUtil.Serialize(new HealthUpdateMsg
                {
                    playerId = player.PlayerId,
                    hp = player.Hp,
                    attackerId = 0
                }));
            }

            if (player.Hp > 0f) continue;

            // ⚠️ The ONLY writer of death. killer = null → killerId 0 and IGameMode.OnKill is not called:
            // no killer means no score (same rule as a teamkill, §10.2 — the event happens, the reward
            // does not).
            KillPlayerLocked(outbox, player, null, ArenaProtocol.WEAPON_ID_OBSTACLE, now);
            Console.WriteLine($"[match] engel ölümü: {player.Name}");
        }
    }

    /// <summary>Are the player's reported state bits still FRESH (§10.9).</summary>
    /// <remarks>A stale flag means a silent/frozen client. The bits carry state, so the last packet would
    /// keep saying "I am in an obstacle"/"I am out of bounds" forever. The penalty, the revive gate AND
    /// the violation ledger all ask the same freshness question, so it lives in ONE place.</remarks>
    private static bool IsPoseFreshLocked(PlayerState player, DateTime now) =>
        (now - player.LastPoseAt).TotalMilliseconds < ArenaProtocol.OBSTACLE_FLAG_STALE_MS;

    /// <summary>Is the player inside an obstacle RIGHT NOW: flag set AND fresh (§10.9).</summary>
    private static bool IsObstacleFlagLiveLocked(PlayerState player, DateTime now) =>
        player.InObstacle && IsPoseFreshLocked(player, now);

    /// <summary>Should the revive be deferred because the player is inside an obstacle (§10.9). Today
    /// there is ONE revive path (<c>revive_request</c>); ⚠️ any second one must call this gate too — a ban
    /// does not exist until every path changing that state is closed.</summary>
    /// <remarks>⚠️ The deferral is NOT unbounded
    /// (<see cref="ArenaProtocol.OBSTACLE_REVIVE_BLOCK_SECONDS"/>): the gate reads a client-reported flag,
    /// and a lying client could leave the player permanently dead. At the cap the player is revived — if
    /// they did not leave the obstacle the penalty simply restarts.</remarks>
    private static bool IsObstacleReviveBlockedLocked(PlayerState player, DateTime now) =>
        IsObstacleFlagLiveLocked(player, now) &&
        (now - player.DiedAt).TotalSeconds < ArenaProtocol.OBSTACLE_REVIVE_BLOCK_SECONDS;

    /// <summary>Runs the violation ledger and pushes its edges to admins (§10.9): both kinds (obstacle ·
    /// out of bounds) go through the SAME code path — no per-kind <c>if</c> chain, the only difference is
    /// the flag feeding the "is it live" question.</summary>
    /// <remarks>⚠️ The ledger is PHASE-INDEPENDENT, hence it sits outside <c>TickLiveLocked</c>, in the
    /// phase-independent part of the tick loop: the operator must also see a player leaving the arena in
    /// the lobby and during the countdown. The penalty (<see cref="TickObstacleLocked"/>) runs only in
    /// <c>playing</c> — the ledger is not a penalty.
    /// <para>⚠️ <c>Calibrated</c> is NOT asked here: the penalty has it because it drains health; the ledger
    /// only records, and an uncalibrated player going out of bounds is exactly what the operator needs to
    /// see. <c>Alive</c> IS asked: death ends the violation for the operator (the ring goes dark, the end
    /// line drops) — a dead body inside a block is nothing to act on.</para></remarks>
    private void TickViolationFeedLocked(List<Outgoing> outbox, DateTime now)
    {
        // ⚠️ The early exit is PARTIAL: with no admin connected no message is serialised (no packets for
        // nobody — same rationale as the net_stats loop), but the edge state STILL advances. Otherwise an
        // admin connecting later would find a half-finished violation open forever, and the ledger's
        // duration would swallow that gap.
        var anyAdmin = HasConnectedAdminLocked();

        foreach (var player in _registry.Snapshot())
        {
            if (player.Role != "player") continue;

            if (!player.IsConnected)
            {
                // A dropped/removed player's open violation closes SILENTLY: an end line tells the
                // operator nothing, and the disconnected gap would be written into the duration.
                CloseViolationLocked(player.ObstacleTally);
                CloseViolationLocked(player.OutOfBoundsTally);
                continue;
            }

            // Freshness is the same question for both kinds; asked once (see IsPoseFreshLocked).
            var fresh = IsPoseFreshLocked(player, now);
            TickViolationKindLocked(outbox, player, player.ObstacleTally, player.Alive && player.InObstacle && fresh,
                ArenaProtocol.VIOLATION_KIND_OBSTACLE, now, anyAdmin);
            TickViolationKindLocked(outbox, player, player.OutOfBoundsTally, player.Alive && player.OutOfBounds && fresh,
                ArenaProtocol.VIOLATION_KIND_OUT_OF_BOUNDS, now, anyAdmin);
        }
    }

    /// <summary>Edge logic for ONE violation kind of ONE player (§10.9). <paramref name="live"/> is that
    /// kind's "is it violating now" answer — the caller knows which flag it watched, this method does
    /// not.</summary>
    /// <remarks>⚠️ Contact below <see cref="ArenaProtocol.VIOLATION_MIN_SECONDS"/> never enters the
    /// ledger: no message, no <c>Count</c> increment. A player oscillating on the boundary line would
    /// otherwise produce three lines per second and make the feed unreadable. The threshold is for the
    /// feed only — the ring is driven by the snapshot bit and lights up from the first frame.</remarks>
    private void TickViolationKindLocked(List<Outgoing> outbox, PlayerState player, ViolationTally tally,
        bool live, string kind, DateTime now, bool anyAdmin)
    {
        if (live)
        {
            tally.Since ??= now;
            if (tally.Announced) return;
            if ((now - tally.Since.Value).TotalSeconds < ArenaProtocol.VIOLATION_MIN_SECONDS) return;

            tally.Announced = true;
            tally.Count++;
            if (anyAdmin)
            {
                QueueAdminBroadcastLocked(outbox, JsonUtil.Serialize(new ViolationMsg
                {
                    playerId = player.PlayerId,
                    kind = kind,
                    active = true,
                    // Duration unknown yet; the start line only says "started" (§5.3).
                    seconds = 0f,
                    count = tally.Count,
                    totalSeconds = tally.TotalSeconds
                }));
            }
            return;
        }

        if (tally.Since == null) return;

        if (tally.Announced)
        {
            var seconds = (float)(now - tally.Since.Value).TotalSeconds;
            tally.TotalSeconds += seconds;
            if (anyAdmin)
            {
                QueueAdminBroadcastLocked(outbox, JsonUtil.Serialize(new ViolationMsg
                {
                    playerId = player.PlayerId,
                    kind = kind,
                    active = false,
                    seconds = seconds,
                    count = tally.Count,
                    totalSeconds = tally.TotalSeconds
                }));
            }
        }

        CloseViolationLocked(tally);
    }

    /// <summary>Closes an open violation without a message. Does NOT touch the counters: the ledger's
    /// accumulated count/duration lives for the whole match, only the current violation closes.</summary>
    private static void CloseViolationLocked(ViolationTally tally)
    {
        tally.Since = null;
        tally.Announced = false;
    }

    /// <summary>The ONLY writer of death (§10.2/§10.9): <c>Alive = false</c> is written nowhere
    /// else.</summary>
    /// <remarks>Why one gate: death arrives on two paths — a hit (<c>hit_report</c>) and environmental
    /// damage (obstacle). If both wrote <c>Alive</c>, <c>DiedAt</c>, the counters, <c>kill_event</c> and
    /// <c>respawn</c> themselves, every new step added to one (clearing a flag, calling a hook, sending a
    /// message) would go SILENTLY MISSING in the other. "Dying" is one sentence, written in one place.
    /// <para>With <paramref name="killer"/> <c>null</c> the death is environmental: <c>killerId</c> 0 and
    /// nobody's <c>kills</c> increases. ⚠️ This method does NOT write REWARD score — <c>IGameMode.OnKill</c>
    /// is raised at the CALL SITE, outside the lock (§10.2: teamkills and environmental deaths reward
    /// nothing for the same reason). The teamkill PENALTY is written here: it is a counter correction,
    /// not a reward, so it belongs with the counters.</para></remarks>
    private void KillPlayerLocked(List<Outgoing> outbox, PlayerState victim, PlayerState? killer,
        string weaponId, DateTime now, bool teamKill = false)
    {
        victim.Alive = false;
        victim.DiedAt = now;
        // A dead player showing as protected makes no sense: if snapshot bit6 stayed on the wire the
        // client would draw the shield on a ghost (§10.4).
        victim.SpawnProtectedUntil = DateTime.MinValue;
        victim.Deaths++;
        // §10.10: a dead owner never sends object_release — freeing here is what keeps a held object from
        // being locked away until the round resets.
        ReleaseObjectsOfLocked(outbox, victim.PlayerId);
        // A suicide adds a death but NO kill (§10.2): killer and victim are the same record, and
        // crediting it would inflate K/D. The kill_event still goes out with killerId == victimId.
        if (killer != null && !ReferenceEquals(killer, victim))
        {
            if (teamKill)
            {
                // Counter-Strike rule (§10.2): a teamkill is a kill taken AWAY — kills −1 and score −1 on
                // the killer, both may go negative. Team score is untouched: its only writer is OnKill,
                // which the call site does not raise for a teamkill.
                killer.Kills--;
                killer.Score--;
            }
            else
            {
                killer.Kills++;
            }
        }

        _rosterRefreshFor = victim; // deaths + alive changed → refresh lobby_state (§5.3)

        QueueBroadcastLocked(outbox, JsonUtil.Serialize(new KillEventMsg
        {
            killerId = killer?.PlayerId ?? 0,
            victimId = victim.PlayerId,
            weaponId = weaponId ?? "" // unvalidated free label (kill feed / statistics)
        }));

        var victimConnection = victim.Socket;
        if (victimConnection == null) return;

        outbox.Add(new Outgoing(victimConnection, JsonUtil.Serialize(new RespawnMsg
        {
            playerId = victim.PlayerId,
            delaySeconds = _rules.RespawnDelay
        }), victim.Name));
    }

    /// <summary>The <c>finished</c> safety valve: back to the lobby after <c>MATCH_END_SECONDS</c> if the
    /// operator chose nothing (§10.1).</summary>
    /// <remarks>⚠️ Switched OFF for a mode that holds its result
    /// (<see cref="IGameMode.HoldsResultForOperator"/>): there the scoreboard is the match's product and
    /// a timer would wipe it exactly as it is being read out. The exit stays
    /// <c>return_to_lobby</c>/<c>abort_match</c>/a new <c>start_match</c>.</remarks>
    private void TickEndLocked(List<Outgoing> outbox, DateTime now)
    {
        if (_mode is { HoldsResultForOperator: true }) return;

        if ((now - _phaseEnteredAt).TotalSeconds < ArenaProtocol.MATCH_END_SECONDS) return;
        EnterLobbyLocked(outbox, now);
    }

    // ---- Admin commands ----

    /// <summary>start_match validation + per-player load_match broadcast (§10.1). On failed validation
    /// the phase does NOT change and the reason is logged.</summary>
    /// <remarks><paramref name="roundSeconds"/>/<paramref name="scoreLimit"/>/
    /// <paramref name="countdownSeconds"/> are per-match: <c>0</c> falls back to the mode's (for the
    /// countdown, the protocol's) default (§5.2). The numbers on <see cref="IGameMode"/> are defaults,
    /// not locks, so the operator can shorten or extend the round.
    /// <para><paramref name="scoreLimit"/> may also be
    /// <see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/>: an UNLIMITED match — no limit branch runs, the
    /// end is decided by the clock or the operator's <c>abort_match</c> (in round-based modes the round
    /// cap lifts too).</para></remarks>
    public async Task StartMatchAsync(string? modeId, string? sceneName, int roundSeconds = 0,
        int scoreLimit = 0, int countdownSeconds = 0)
    {
        modeId ??= "";
        sceneName ??= "";

        if (!_modes.TryGetValue(modeId, out var mode))
        {
            Console.WriteLine($"[match] start_match reddedildi: '{modeId}' modu kayıtlı değil (kayıtlı: {string.Join(", ", _modes.Keys)}).");
            return;
        }
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Console.WriteLine("[match] start_match reddedildi: sceneName boş.");
            return;
        }

        // With a non-empty map table (config/maps.json — Unity export) scene + mode compatibility is
        // validated. An empty table (no file) skips this step entirely.
        if (!_maps.IsEmpty)
        {
            if (!_maps.TryGet(sceneName, out var known))
            {
                Console.WriteLine($"[match] start_match reddedildi: '{sceneName}' harita tablosunda yok (bilinen: {string.Join(", ", _maps.SceneNames)}).");
                return;
            }
            if (!MapTable.SupportsMode(known, modeId))
            {
                Console.WriteLine($"[match] start_match reddedildi: '{sceneName}' haritası '{modeId}' modunu desteklemiyor (desteklenen: {string.Join(", ", known.modes)}).");
                return;
            }
            if (!MapTable.MatchesGameType(known, mode.GameType))
            {
                var mapGameType = string.IsNullOrWhiteSpace(known.gameType) ? MapTable.DefaultGameType : known.gameType;
                Console.WriteLine($"[match] start_match reddedildi: '{sceneName}' haritası '{mode.GameType}' oyun tipinde değil (haritanın tipi: {mapGameType}).");
                return;
            }
        }

        var players = _registry.Snapshot()
            .Where(p => p.IsConnected && p.Role == "player")
            .OrderBy(p => p.PlayerId)
            .ToList();

        var missing = players.Where(p => !p.Scenes.Contains(sceneName)).Select(p => p.Name).ToList();
        if (missing.Count > 0)
        {
            Console.WriteLine($"[match] start_match reddedildi: '{sceneName}' sahnesi şu istemcilerin build listesinde yok — {string.Join(", ", missing)}.");
            return;
        }

        // Starting with no players is ALLOWED: the admin observer may open the map in an empty arena
        // (§10.1).
        if (players.Count == 0)
            Console.WriteLine("[match] uyarı: hiç oyuncu yok — maç yalnız admin gözlemci için başlatılıyor (harita önizleme).");
        else if (players.Count == 1)
            Console.WriteLine("[match] uyarı: tek oyuncuyla maç başlatılıyor (yalnız test amaçlı).");

        // Team setup comes from the mode's shape (§10.5). Both are called OUTSIDE the lock since
        // registry.SetTeam raises events. Balancing only makes sense with 2+ players.
        var rules = mode.Rules;
        if (rules.Teams == TeamMode.None)
            ClearTeams(players);
        else if (players.Count > 1)
            BalanceTeams(players);

        // Lobby ready flags are cleared BEFORE entering Loading: there the same flag means "scene
        // loaded", and a stale true would start the countdown instantly (§10.1).
        foreach (var player in players.Where(p => p.Ready).ToList())
            _registry.SetReady(player.DeviceId, false);

        // Match participant ledger (§10.2): every currently connected player is written in. OUTSIDE the
        // lock since the registry raises events — same contract as SetTeam/SetReady.
        _registry.MarkConnectedPlayersAsParticipants();

        var outbox = new List<Outgoing>();
        var teamless = rules.Teams == TeamMode.None;
        // Per-match value if the admin gave one, otherwise the mode's default (§5.2).
        var appliedRound = roundSeconds > 0 ? roundSeconds : mode.DefaultRoundSeconds;
        // The limit is THREE-valued (§5.2): a positive value is per-match, 0 falls back to the mode's
        // default, and SCORE_LIMIT_UNLIMITED means "no limit" and does NOT trigger the default. The
        // sentinel travels as-is (not turned into 0): load_match/admin_state echo it back, and "mode
        // default" must stay distinguishable from "unlimited" on the operator's panel.
        var appliedLimit = scoreLimit > 0 ? scoreLimit
            : scoreLimit < 0 ? ArenaProtocol.SCORE_LIMIT_UNLIMITED
            : mode.DefaultScoreLimit;
        // The countdown range is a server constraint, not a UI list (§5.2): 0 = default, a given value is
        // clamped. Clamped here so there is a single writer.
        var appliedCountdown = countdownSeconds > 0
            ? Math.Clamp(countdownSeconds, ArenaProtocol.COUNTDOWN_SECONDS_MIN, ArenaProtocol.COUNTDOWN_SECONDS_MAX)
            : ArenaProtocol.COUNTDOWN_SECONDS;

        lock (_gate)
        {
            if (_phase != Phase.Paused || _pauseReason != PauseReason.Lobby)
                Console.WriteLine($"[match] start_match: durum {PhaseWire(_phase)} — mevcut maç iptal edilip yenisi kuruluyor.");

            _mode = mode;
            ApplyRulesLocked(rules);
            RefreshShotRelayLocked();
            _modeId = mode.ModeId;
            SetSceneLocked(sceneName);
            _roundSeconds = appliedRound;
            _scoreLimit = appliedLimit;
            _countdownSeconds = appliedCountdown;
            _scoreRed = 0;
            _scoreBlue = 0;
            _timeRemaining = _roundSeconds;
            _modeState = "";
            _matchStartPending = false;
            _roundStartPending = false;
            _matchStarted = false;
            _startedWithPlayers = players.Count > 0;

            var rulesInfo = _rules.ToInfo();

            // §10.10: staging the scene resets the world objects — before load_match goes out, so the
            // world_state that follows describes the scene the clients are about to load.
            RebuildObjectsLocked(_sceneName);

            foreach (var player in players)
            {
                ResetMatchStateLocked(player);

                var connection = player.Socket;
                if (connection == null) continue;
                // load_match is personalised: each player gets their own team (§10.1). No position/slot
                // travels — the player stays wherever they physically stand (§10.4).
                var load = new LoadMatchMsg
                {
                    modeId = _modeId,
                    sceneName = _sceneName,
                    roundSeconds = _roundSeconds,
                    scoreLimit = _scoreLimit,
                    yourTeam = player.Team,
                    sceneElapsed = SceneElapsedLocked,
                    rules = rulesInfo
                };
                outbox.Add(new Outgoing(connection, JsonUtil.Serialize(load), player.Name));
            }

            // Admins load the same scene (observer view, §2): team is meaningless so it goes empty, and
            // admins send no set_ready — the Loading gate counts only role=player connections
            // (ConnectedPlayersLocked). Rules go to admins too: team mode drives the admin UI's
            // one-column/two-column decision.
            var adminLoad = JsonUtil.Serialize(new LoadMatchMsg
            {
                modeId = _modeId,
                sceneName = _sceneName,
                roundSeconds = _roundSeconds,
                scoreLimit = _scoreLimit,
                yourTeam = "",
                sceneElapsed = SceneElapsedLocked,
                rules = rulesInfo
            });
            foreach (var admin in _registry.Snapshot())
            {
                if (!admin.IsConnected || admin.Role != "admin") continue;
                var adminConnection = admin.Socket;
                if (adminConnection == null) continue;
                outbox.Add(new Outgoing(adminConnection, adminLoad, admin.Name));
            }

            QueueWorldStateLocked(outbox);
            SetPhaseLocked(Phase.Paused, PauseReason.Loading, DateTime.UtcNow);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }

        // Team split is counted from the REAL state after BalanceTeams/ClearTeams (the players list holds
        // PlayerState references and SetTeam updates them in place).
        var blueCount = players.Count(p => p.Team == "blue");
        var teamInfo = teamless ? "takımsız" : $"kırmızı {players.Count - blueCount} / mavi {blueCount}";
        // Friendly fire is logged on purpose: the switch lives for the whole session and can change
        // mid-match, so this line is the only later record of which match ran under which rule.
        Console.WriteLine($"[match] start_match: mod '{mode.ModeId}', sahne '{sceneName}', " +
                          $"{appliedRound} sn / limit {DescribeScoreLimit(appliedLimit)} / " +
                          $"geri sayım {appliedCountdown} sn, " +
                          $"dost ateşi {(FriendlyFire ? "AÇIK" : "kapalı")}, " +
                          $"{players.Count} oyuncu ({teamInfo}).");
        await FlushAsync(outbox);
    }

    /// <summary><c>pause_match</c> (§5.2) — freezes the running match: <see cref="Phase.Playing"/> →
    /// <see cref="Phase.Paused"/> + <see cref="PauseReason.Operator"/>.</summary>
    /// <remarks>The clock stops by itself: it only decrements inside <see cref="TickLiveLocked"/>, which
    /// runs only in <see cref="Phase.Playing"/>. Scores, health and <c>modeState</c> are untouched —
    /// pausing is not leaving the match (that is <c>abort_match</c>).
    /// <para>A match that is not running is not paused; returns <c>false</c> with no state change.</para></remarks>
    public async Task<bool> PauseMatchAsync()
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Playing)
            {
                Console.WriteLine($"[match] pause_match yok sayıldı: faz {PhaseWire(_phase)} (yalnız koşan maç duraklatılır).");
                return false;
            }

            SetPhaseLocked(Phase.Paused, PauseReason.Operator, DateTime.UtcNow);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }
        await FlushAsync(outbox);
        return true;
    }

    /// <summary><c>end_match</c> (§5.2) — ends the match NORMALLY: <see cref="Phase.Finished"/> +
    /// <c>match_end</c> + the result screen, then the usual return to the lobby.</summary>
    /// <remarks>⚠️ NOT <c>abort_match</c>: that one skips the result entirely and drops to the lobby.
    /// This exists because a mode's own end condition may never fire — an unlimited tournament has no
    /// win limit and no round cap (§10.5) — and "there is no way out but abort" costs the operators the
    /// scoreboard their players just played for.
    /// <para>The winner is decided HERE and the mode's <see cref="IGameMode.IsMatchOver"/> is NOT asked:
    /// the operator is ending it precisely because the mode's condition is not met, so a mode answer
    /// would be "not over" and there would be nobody to declare.</para>
    /// <para>Works from a pause too (the between-rounds regroup is one) — otherwise the command would be
    /// unusable in exactly the mode that needs it most. Lobby and an already finished match are logged
    /// and ignored.</para></remarks>
    public async Task<bool> EndMatchAsync()
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            bool inMatch = _phase == Phase.Playing ||
                           (_phase == Phase.Paused && _pauseReason != PauseReason.Lobby &&
                            _pauseReason != PauseReason.None);
            if (!inMatch)
            {
                Console.WriteLine($"[match] end_match yok sayıldı: faz {PhaseWire(_phase)} (bitirilecek maç yok).");
                return false;
            }

            EnterEndLocked(outbox, DateTime.UtcNow, DecideOutcomeLocked());
        }
        await FlushAsync(outbox);
        FlushRosterRefresh();
        return true;
    }

    /// <summary>Who is ahead RIGHT NOW, on the channel the mode's rules use (§10.2). Draw when nobody
    /// leads — inventing a winner from a 0-0 board would be worse than saying "berabere".</summary>
    private MatchOutcome DecideOutcomeLocked() => _rules.Scoring switch
    {
        ScoreKind.Player => DecidePlayerOutcomeLocked(),
        // Shared score: the common total rides on scoreRed but is NOT a team score — naming a winner
        // from it would turn it into one (§10.5: no winner in co-op).
        ScoreKind.PlayerAndShared => MatchOutcome.Draw,
        _ => DecideTeamOutcomeLocked()
    };

    private MatchOutcome DecidePlayerOutcomeLocked()
    {
        var leaderId = 0;
        var best = 0;
        foreach (var player in ConnectedPlayersLocked())
        {
            if (player.Score <= best) continue;
            best = player.Score;
            leaderId = player.PlayerId;
        }

        return leaderId > 0 ? MatchOutcome.Player(leaderId) : MatchOutcome.Draw;
    }

    private MatchOutcome DecideTeamOutcomeLocked()
    {
        if (_scoreRed > _scoreBlue) return MatchOutcome.Team("red");
        return _scoreBlue > _scoreRed ? MatchOutcome.Team("blue") : MatchOutcome.Draw;
    }

    /// <summary><c>resume_match</c> (§5.2) — resumes the match the operator paused.</summary>
    /// <remarks>⚠️ Only <see cref="PauseReason.Operator"/> is lifted. Every pause is lifted by its owner:
    /// lifting <see cref="PauseReason.Mode"/> would break the mode's sub-state,
    /// <see cref="PauseReason.Loading"/>/<see cref="PauseReason.Countdown"/> end on their own conditions,
    /// and in <see cref="PauseReason.Lobby"/> there is no match to resume.
    /// <para><see cref="EnterLiveLocked"/> is NOT used: it sets the match up from scratch (full round
    /// time, full health). Resuming means continuing where it stopped.</para></remarks>
    public async Task<bool> ResumeMatchAsync()
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Paused || _pauseReason != PauseReason.Operator)
            {
                Console.WriteLine($"[match] resume_match reddedildi: durum {PhaseWire(_phase)}" +
                                  $"{(_pauseReason != PauseReason.None ? "/" + ReasonWire(_pauseReason) : "")} " +
                                  "(yalnız operatörün duraklattığı maç sürdürülür).");
                return false;
            }

            var now = DateTime.UtcNow;
            SetPhaseLocked(Phase.Playing, now);
            // The 1 Hz match_state rhythm drifted during the pause, so it is re-anchored; time and scores
            // stay as they were.
            _nextSecondAt = now.AddSeconds(1);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }
        await FlushAsync(outbox);
        return true;
    }

    /// <summary><c>mode_continue</c> (§5.2) — the operator's "carry on" for a mode that parked the round
    /// flow (the tournament's round review, §10.5).</summary>
    /// <remarks>⚠️ Deliberately NOT a phase transition: it only queues a flag. The mode reads it on its
    /// next tick and lifts its OWN pause — so "every pause is lifted by its owner" still holds, which is
    /// exactly why <c>resume_match</c> was not widened to cover this instead.
    /// <para>Accepted only in <see cref="PauseReason.Mode"/>: in any other phase there is no parked flow
    /// and a flag left standing would fire the NEXT hold.</para></remarks>
    public bool ModeContinue()
    {
        lock (_gate)
        {
            if (_phase != Phase.Paused || _pauseReason != PauseReason.Mode)
            {
                Console.WriteLine($"[match] mode_continue reddedildi: durum {PhaseWire(_phase)}" +
                                  $"{(_pauseReason != PauseReason.None ? "/" + ReasonWire(_pauseReason) : "")} " +
                                  "(bekleyen bir mod akışı yok).");
                return false;
            }

            _modeContinuePending = true;
            Console.WriteLine("[match] mode_continue — mod akışı sürdürülecek.");
            return true;
        }
    }

    // ---- Mode commands (§10.1 "round-based modes") ----
    //
    // All three are called from IGameMode hooks: OUTSIDE the lock and SYNCHRONOUSLY (OnTick is void, a
    // mode cannot await). Messages are built under the lock into _pendingOutbox and dispatched by the
    // tick loop when the hook returns.
    //
    // ⚠️ The core knows NOTHING about rounds. "Round" here is only in the names; the meaning lives in the
    // mode. Without these three a mode would have to either send its own messages (a second sender) or
    // write the phase directly (a second authority).

    /// <summary>Reads AND clears a pending operator <c>mode_continue</c> (§5.2); <c>false</c> when none
    /// is waiting.</summary>
    /// <remarks>⚠️ Consuming, not peeking: the press is a single event. A flag left standing would be
    /// read again at the mode's next hold and skip it without anyone touching a button.
    /// <para>Called from a mode hook, so a mode that parks its flow in more than one stage must consume
    /// on every tick and act only where the press means something — otherwise a press landing in the
    /// wrong stage still queues up for the next one.</para></remarks>
    public bool ConsumeModeContinue()
    {
        lock (_gate)
        {
            if (!_modeContinuePending) return false;

            _modeContinuePending = false;
            return true;
        }
    }

    /// <summary>Requests a mode pause (§10.1): <see cref="Phase.Playing"/> → <see cref="Phase.Paused"/> +
    /// <see cref="PauseReason.Mode"/>, with the reason written into <paramref name="modeState"/>.</summary>
    /// <remarks>Time is ZEROED: the round is over, and showing a frozen counter would be a lie on the
    /// HUD. Scores and health are untouched. All <c>ready</c> flags are cleared — the gathering gate uses
    /// that flag (§10.1) and a stale <c>true</c> would open it instantly.
    /// <para>Does nothing and returns <c>false</c> outside <see cref="Phase.Playing"/> (an
    /// abort/operator pause may have slipped in).</para></remarks>
    public bool TryPauseForMode(string? modeState)
    {
        lock (_gate)
        {
            if (_phase != Phase.Playing) return false;

            _modeState = modeState ?? "";
            _timeRemaining = 0f;
            foreach (var player in ConnectedPlayersLocked()) QueueReadyClearLocked(player);

            SetPhaseLocked(Phase.Paused, PauseReason.Mode, DateTime.UtcNow);
            QueueBroadcastLocked(_pendingOutbox, JsonUtil.Serialize(BuildMatchStateLocked()));
            return true;
        }
    }

    /// <summary>Round boundary: pulls the whole roster to full health AT ONCE, without waiting for the
    /// next round's countdown (§10.1). Called by a round-based mode right after it opens the mode
    /// pause.</summary>
    /// <remarks>Why here and not only at the <c>playing</c> gate: between the two lie the regroup and the
    /// countdown — minutes, in which the player walks to their base. Refreshing only at the gate would
    /// leave them staring at the death screen (or at the previous round's health bar) for that whole walk,
    /// with no way to tell "the round is over" from "I am still dead".
    /// <para>⚠️ Health cannot change again before the round starts: damage needs phase <c>playing</c>
    /// (§10.3) and the obstacle clock only advances on <c>playing</c> ticks (§10.9) — so the early refresh
    /// cannot be undone while the mode holds the pause.</para>
    /// <para>Works only from a mode pause and returns <c>false</c> otherwise (an <c>abort_match</c> may
    /// have landed in between). Failure needs no handling: <see cref="EnterLiveLocked"/> refreshes the
    /// roster unconditionally, so the guarantee holds either way — only its timing slips back to the end
    /// of the countdown.</para></remarks>
    public bool TryReviveRosterForMode()
    {
        lock (_gate)
        {
            if (_phase != Phase.Paused || _pauseReason != PauseReason.Mode) return false;

            ReviveRosterLocked(_pendingOutbox);
            return true;
        }
    }

    /// <summary>Round boundary: pulls every network object back to full health and no flags, then
    /// broadcasts <c>world_state</c> (§10.10); <c>false</c> when nothing needed resetting.</summary>
    /// <remarks>Its consumer is <c>TournamentMode.OnRoundStart</c>: the second round of a tournament must
    /// not start behind covers the first round broke. Called from a mode hook, i.e. OUTSIDE the lock —
    /// the message therefore goes into <see cref="_pendingOutbox"/> (a mode cannot await).</remarks>
    public bool TryResetObjectsForMode()
    {
        lock (_gate)
        {
            if (!_objects.ResetLocked()) return false;

            SyncAllOwnersLocked();
            _lastObjectPoses.Clear();
            QueueBroadcastLocked(_pendingOutbox, BuildWorldStateJsonLocked());
            return true;
        }
    }

    /// <summary>Full <c>world_state</c> JSON for a single late joiner (§10.10, right after
    /// <c>welcome</c>); <c>null</c> when the staged scene has no network objects.</summary>
    public string? BuildWorldStateJson()
    {
        lock (_gate)
        {
            return _objects.Count == 0 ? null : BuildWorldStateJsonLocked();
        }
    }

    /// <summary>Updates the mode's sub-state (§10.1 <c>modeState</c>) and broadcasts <c>match_state</c>
    /// ONLY on a real change. Staying silent otherwise is deliberate: this is called from a 10 Hz hook, so
    /// an unconditional broadcast would produce 10 broadcasts per second.</summary>
    public void SetModeState(string? modeState)
    {
        lock (_gate)
        {
            var next = modeState ?? "";
            if (_modeState == next) return;

            _modeState = next;
            QueueBroadcastLocked(_pendingOutbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }
    }

    /// <summary>Overwrites the match clock (§10.1) and broadcasts <c>match_state</c>.</summary>
    /// <remarks>The core OWNS the clock — it starts it at <see cref="EnterLiveLocked"/> and decrements it
    /// in <see cref="TickLiveLocked"/> — but it does not own its MEANING. A round-based mode restarts
    /// <c>playing</c> once per round while its clock is the whole MATCH's, so it writes the carried-over
    /// budget back here. Writing it is the mode's single lever over the clock; the core still runs it.
    /// <para>⚠️ Deliberately NOT clamped to <c>roundSeconds</c>: the value a mode writes here is by
    /// definition a remainder, not the configured duration.</para>
    /// <para>Does nothing outside a live/paused match, so a stale mode hook cannot resurrect a clock in
    /// the lobby.</para></remarks>
    public void SetTimeRemaining(float seconds)
    {
        lock (_gate)
        {
            if (_phase == Phase.Finished || (_phase == Phase.Paused && _pauseReason == PauseReason.Lobby))
            {
                return;
            }

            var next = MathF.Max(0f, seconds);
            if (MathF.Abs(_timeRemaining - next) < 0.001f) return;

            _timeRemaining = next;
            QueueBroadcastLocked(_pendingOutbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }
    }

    /// <summary>Ends the mode pause and opens a new round: <see cref="Phase.Paused"/>/
    /// <see cref="PauseReason.Mode"/> → countdown (<see cref="_countdownSeconds"/>) → on through the
    /// core's normal path to <see cref="Phase.Playing"/>.</summary>
    /// <remarks>Health/dead players are NOT fixed up here — <see cref="EnterLiveLocked"/> already pulls
    /// the whole roster to full health and sends each of them a <c>health_update</c>. Doing it in two
    /// places would open a second revive path.
    /// <para>⚠️ <c>ready</c> flags are NOT cleared. At the gathering gate that flag means "I am in my base
    /// right now" and must stay alive through the countdown — it is the only basis for the mode to cancel
    /// the countdown (<see cref="TryCancelCountdownForMode"/>) and return to gathering. Clearing happens
    /// in <see cref="TryPauseForMode"/> (once, at the START of gathering).</para>
    /// <para>Works only from a mode pause; returns <c>false</c> otherwise.</para></remarks>
    public bool TryStartRound()
    {
        lock (_gate)
        {
            if (_phase != Phase.Paused || _pauseReason != PauseReason.Mode) return false;

            _modeState = "";
            EnterCountdownLocked(_pendingOutbox, DateTime.UtcNow);
            return true;
        }
    }

    /// <summary>Reverses the countdown the mode opened: <see cref="PauseReason.Countdown"/> →
    /// <see cref="PauseReason.Mode"/>. In a round-based mode the gathering condition can break during the
    /// countdown (a player leaves their base) — then the round does not start and gathering resumes.</summary>
    /// <remarks>There is no separate "countdown cancelled" message and none is needed: the client already
    /// clears the countdown when it sees <c>phaseReason != countdown</c>, so the broadcast
    /// <c>match_state</c> suffices.
    /// <para>Works only from the countdown; returns <c>false</c> if the counter hit zero on that tick and
    /// the phase went Playing — the round has started and is not reversed.</para></remarks>
    public bool TryCancelCountdownForMode(string? modeState)
    {
        lock (_gate)
        {
            if (_phase != Phase.Paused || _pauseReason != PauseReason.Countdown) return false;

            _modeState = modeState ?? "";
            _countdownRemaining = 0;
            SetPhaseLocked(Phase.Paused, PauseReason.Mode, DateTime.UtcNow);
            QueueBroadcastLocked(_pendingOutbox, JsonUtil.Serialize(BuildMatchStateLocked()));
            return true;
        }
    }

    /// <summary><c>set_friendly_fire</c> (§5.2) — the friendly-fire switch. There is NO phase gate: it
    /// applies during a running match too, effective from the next <c>hit_report</c> (the damage gate
    /// already reads <c>_rules.FriendlyFire</c>, §10.3).</summary>
    /// <remarks>Announces the change to all clients via <c>rules_update</c>: rules normally arrive with
    /// <c>welcome</c>/<c>load_match</c>, so there is no other channel for a rule changing MID-match. Late
    /// joiners still get the right value from <c>welcome.match.rules</c>.
    /// <para>No work and <c>false</c> when the value is unchanged (no broadcast either) — the panel may
    /// optimistically resend the same value.</para></remarks>
    public async Task<bool> SetFriendlyFireAsync(bool enabled)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_friendlyFire == enabled) return false;

            _friendlyFire = enabled;
            ApplyRulesLocked(_rules); // keep the current shape, only re-stamp the switch
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(new RulesUpdateMsg
            {
                modeId = _modeId,
                rules = _rules.ToInfo()
            }));

            Console.WriteLine($"[match] dost ateşi {(enabled ? "AÇILDI" : "KAPATILDI")} " +
                              $"(faz {PhaseWire(_phase)}).");
        }

        await FlushAsync(outbox);
        return true;
    }

    /// <summary>Current value of the friendly-fire switch (§5.2) — broadcast by <c>admin_state</c>.</summary>
    public bool FriendlyFire
    {
        get { lock (_gate) return _friendlyFire; }
    }

    /// <summary>abort_match — to Lobby from any phase (§10.1).</summary>
    public Task AbortMatchAsync() => BackToLobbyAsync("abort_match");

    /// <summary>return_to_lobby — same work as abort (§10.1).</summary>
    public Task ReturnToLobbyAsync() => BackToLobbyAsync("return_to_lobby");

    private async Task BackToLobbyAsync(string reason)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            Console.WriteLine($"[match] {reason} → lobiye dönülüyor (faz {_phase}).");
            EnterLobbyLocked(outbox, DateTime.UtcNow);
        }
        await FlushAsync(outbox);
        FlushReadyClear();
        FlushRosterRefresh();
        FlushParticipantCleanup(); // §10.2: the ledger closes after return_to_lobby / abort_match
    }

    /// <summary>Lobby staging (§10.7): makes ALL clients load the map the operator picked while in the
    /// lobby — changing the map on the admin panel means the players move to that arena too.</summary>
    /// <remarks>This is NOT a match: the phase stays <see cref="Phase.Paused"/>, the damage gate (§10.3)
    /// stays closed, nobody sends <c>set_ready</c>, and no time/score runs. The carrier message is
    /// <c>return_to_lobby</c> (lobby profile + new scene) — "we are in the lobby, load this scene" is
    /// already that message's meaning on the client, so no second message type is needed.
    /// <para>⚠️ Works only while NO match is set up (<see cref="CanChangeSelection"/>): during the lobby
    /// wait and in <c>finished</c>. Pulling the scene out from under a set-up match — running, being set
    /// up or frozen — would break it; there is no such thing as a map change, a new map comes via
    /// <c>start_match</c> after <c>abort_match</c>.</para>
    /// <para>Validation matches <c>start_match</c> (§10.1): the scene must be in the map table (skipped
    /// when the table is empty) and in EVERY connected player's build list — otherwise some would stay in
    /// the lobby and the operator could not see it on screen.</para></remarks>
    public async Task<StageSceneResult> StageSceneAsync(string? sceneName)
    {
        var target = (sceneName ?? "").Trim();
        if (target.Length == 0) return new StageSceneResult(StageOutcome.Unchanged);

        // Early exit so the validation (registry scan) is not done for nothing.
        lock (_gate)
        {
            // The gate asks "is a match set up" (§10.7): `finished` and the lobby wait are open,
            // everything else — loading/countdown/pause — is closed.
            if (!CanChangeSelectionLocked)
                return new StageSceneResult(StageOutcome.Rejected, RejectReasonLocked());
            if (_sceneName == target) return new StageSceneResult(StageOutcome.Unchanged);
        }

        if (!_maps.IsEmpty && !_maps.TryGet(target, out _))
        {
            return new StageSceneResult(StageOutcome.Rejected,
                $"'{target}' harita tablosunda yok (bilinen: {string.Join(", ", _maps.SceneNames)})");
        }

        var missing = _registry.Snapshot()
            .Where(p => p.IsConnected && p.Role == "player" && !p.Scenes.Contains(target))
            .Select(p => p.Name)
            .ToList();
        if (missing.Count > 0)
        {
            return new StageSceneResult(StageOutcome.Rejected,
                $"'{target}' şu istemcilerin build listesinde yok: {string.Join(", ", missing)}");
        }

        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            // Lock retaken (validation ran OUTSIDE it per the lock contract): a start_match may have
            // slipped in meanwhile, so the gate is re-checked here.
            if (!CanChangeSelectionLocked)
                return new StageSceneResult(StageOutcome.Rejected, RejectReasonLocked());
            if (_sceneName == target) return new StageSceneResult(StageOutcome.Unchanged);

            SetSceneLocked(target);
            // The KIND stays lobby: even if the scene is an arena there is no match yet (§10.7). Writing
            // the selected match mode here would open the match HUD and match loadout before the match
            // starts. The kind changes only via start_match. Rules stay on a LOBBY profile, but WHICH one
            // follows the staged scene's family (§10.7); damage is closed by the phase anyway.
            _modeId = ArenaProtocol.LOBBY_MODE_ID;
            ApplyRulesLocked(LobbyProfileForSceneLocked(_sceneName));
            RefreshShotRelayLocked();

            // Staging changes the wait, not a running match: if it happened from `finished`, we are now
            // in the lobby wait.
            SetPhaseLocked(Phase.Paused, PauseReason.Lobby, DateTime.UtcNow);

            RebuildObjectsLocked(_sceneName); // §10.10: every staging resets
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(new ReturnToLobbyMsg
            {
                modeId = _modeId,
                sceneName = _sceneName,
                sceneElapsed = SceneElapsedLocked,
                rules = _rules.ToInfo()
            }));
            QueueWorldStateLocked(outbox);
        }

        Console.WriteLine($"[match] lobi sahnesi -> '{target}' (tüm istemciler yüklüyor).");
        await FlushAsync(outbox);
        return new StageSceneResult(StageOutcome.Staged);
    }

    // ---- Combat path (§10.3) ----

    // Shot events (0x03/0x04) are not handled here: they moved to UDP and StateHost does the relay
    // (§6.4/6.5). The only share here is the ShotRelayOpen flag — 10 shots/s/player was drowning the
    // authoritative WS channel; damage (hit_report) deliberately STAYED on WS (§10.3).

    /// <summary>hit_report path (§10.3, in order): phase → shooter → target → spawn protection →
    /// self/team → damage number. Any failure means one log line + silent rejection (no reply to the
    /// client).</summary>
    /// <remarks>These are state-consistency checks, NOT anti-cheat — the product runs in a supervised
    /// private space, so cheat protection is deliberately absent (§10.3). The client computes damage and
    /// the server applies it as-is; there is no weapon table, no weaponId whitelist and no fire-rate check
    /// (they would silently drop legitimate pellet/explosion/burst hits).</remarks>
    public async Task HandleHitReportAsync(PlayerState shooter, HitReportMsg msg)
    {
        // §10.10: a network object target takes a separate path and targetPlayerId is not read. Spawn
        // protection, friendly fire, score, kill feed and IGameMode.OnKill do NOT run there — an object
        // has no team and breaking one is a world state, not a game event.
        if (msg.targetNetId != 0)
        {
            await HandleObjectHitAsync(shooter, msg);
            return;
        }

        // Registry lookup is lock-free (ConcurrentDictionary) — done before taking the lock.
        if (!_registry.TryGetByPlayerId(msg.targetPlayerId, out var target))
        {
            RejectHit(shooter, msg.targetPlayerId, $"hedef {msg.targetPlayerId} bulunamadı");
            return;
        }

        var outbox = new List<Outgoing>();
        IGameMode? mode;
        float appliedDamage;
        var killed = false;
        var teamKill = false;
        var selfHit = false;
        string weaponId;

        lock (_gate)
        {
            // ⚠️ THE single damage gate (§10.1/§10.3). Lobby, loading, countdown, pause and match end are
            // all not `playing`, so none of them process damage.
            if (_phase != Phase.Playing)
            {
                RejectHit(shooter, msg.targetPlayerId, $"faz {PhaseWire(_phase)}");
                return;
            }
            if (!ShooterGateOkLocked(shooter, out var shooterReason))
            {
                RejectHit(shooter, msg.targetPlayerId, shooterReason);
                return;
            }
            if (!target.IsConnected || target.Role != "player" || !target.Alive)
            {
                RejectHit(shooter, msg.targetPlayerId, "hedef ölü/bağlantısız");
                return;
            }
            // §10.6: an uncalibrated player takes NO damage. Their avatar is offset from their physical
            // position, so neither aiming at nor hitting them is meaningful.
            if (!target.Calibrated)
            {
                RejectHit(shooter, msg.targetPlayerId, "hedef kalibresiz");
                return;
            }
            // §10.4: spawn protection — a revived player takes no damage for SpawnProtectionSeconds.
            // ⚠️ The gate is on the SERVER: the client only draws the protection (snapshot bit6) and does
            // not base its fire decision on it — even if the shield fades a frame late on the shooter's
            // screen, the hit is dropped here.
            if (DateTime.UtcNow < target.SpawnProtectedUntil)
            {
                RejectHit(shooter, msg.targetPlayerId, "hedef doğma koruması altında");
                return;
            }
            // §10.3 gate 5. TWO separate tests on purpose: an empty team is never a teammate, so in a
            // team-less mode a self-hit would slip past the teammate test — and then score.
            selfHit = target.PlayerId == shooter.PlayerId;
            teamKill = AreTeammates(shooter, target);
            if (!_rules.FriendlyFire && (selfHit || teamKill))
            {
                RejectHit(shooter, msg.targetPlayerId, selfHit ? "kendine vuruş — dost ateşi yok" : "dost ateşi yok");
                return;
            }
            // The CLIENT computes damage (distance falloff, bow draw strength, headshot…) and the server
            // applies it as-is. The only check is that the number is usable: NaN/∞ corrupts health
            // permanently (hp turned NaN can never drop below 0 → an immortal player) and negative damage
            // heals. This is a number check, not an anti-cheat check.
            if (!float.IsFinite(msg.damage) || msg.damage <= 0f)
            {
                RejectHit(shooter, msg.targetPlayerId, $"geçersiz hasar {msg.damage}");
                return;
            }

            var now = DateTime.UtcNow;
            weaponId = msg.weaponId ?? "";
            appliedDamage = msg.damage;
            target.Hp = MathF.Max(0f, target.Hp - appliedDamage);
            // Targeted send (§10.3): victim + admins. Other players were discarding this message anyway.
            QueueHealthUpdateLocked(outbox, target, JsonUtil.Serialize(new HealthUpdateMsg
            {
                playerId = target.PlayerId,
                hp = target.Hp,
                attackerId = shooter.PlayerId
            }));

            if (target.Hp <= 0f)
            {
                killed = true;
                // ⚠️ The ONLY writer of death (§10.2): counters, kill_event and respawn live there.
                KillPlayerLocked(outbox, target, shooter, weaponId, now, teamKill);
            }

            mode = _mode;
        }

        await FlushAsync(outbox);
        FlushRosterRefresh(); // on a death, pushes K/D + alive to the roster (no-op for plain damage)

        // Accepted damage is NOT logged (dozens of lines per second) — only kills + rejections.
        mode?.OnHitApplied(this, shooter.PlayerId, target.PlayerId, appliedDamage, killed);
        if (!killed) return;

        // ⚠️ A TEAMKILL REWARDS NOTHING (§10.2): with friendly fire on, a hit that thins your own team must
        // not award that team points. The gate sits at the CALL SITE, not inside the mode — IGameMode is
        // the only reward writer, so the rule stays in one place and every new mode obeys it for free.
        // The kill feed line still runs; the Counter-Strike penalty (kills −1, score −1) is already
        // written by KillPlayerLocked as a counter correction.
        // Blowing yourself up scores nothing either, and for the same reason — plus it is a SEPARATE
        // test: with no teams, `teamKill` is false for a suicide (§10.2).
        if (!teamKill && !selfHit) mode?.OnKill(this, shooter.PlayerId, target.PlayerId, weaponId);
        var scorelessNote = selfHit ? " (KENDİNİ — skor yazılmadı)"
            : teamKill ? " (TAKIMDAŞ — öldürene −1, takım skoru yazılmadı)" : "";
        Console.WriteLine($"[match] öldürme{scorelessNote}: " +
                          $"{shooter.Name} → {target.Name} ({weaponId}) — skor kırmızı {ScoreRed} : mavi {ScoreBlue}");
        // The match-end check runs in the tick loop (≤100 ms); no phase change here.
    }

    /// <summary>§10.3 gate 2 / §10.10 gate 2 — the SHOOTER side, shared by both hit paths so the two
    /// cannot drift apart: connected player, inside the posthumous window, calibrated.</summary>
    /// <remarks>The shooter need NOT be alive: damage that already left the hand (a bomb in the air)
    /// still lands, but only inside <see cref="PosthumousDamageSeconds"/> — see that constant.
    /// <para>§10.6: an uncalibrated player cannot fire. With a broken alignment, where they aim and where
    /// they actually point differ.</para></remarks>
    private bool ShooterGateOkLocked(PlayerState shooter, out string reason)
    {
        if (!shooter.IsConnected || shooter.Role != "player")
        {
            reason = "atıcı bağlantısız/oyuncu değil";
            return false;
        }
        if (!shooter.Alive && (DateTime.UtcNow - shooter.DiedAt).TotalSeconds > PosthumousDamageSeconds)
        {
            reason = "atıcı ölü";
            return false;
        }
        if (!shooter.Calibrated)
        {
            reason = "atıcı kalibresiz";
            return false;
        }
        reason = "";
        return true;
    }

    /// <summary>hit_report against a network object (§10.10, in order): phase → shooter → object →
    /// damage number. A rejection is one console line, no reply.</summary>
    /// <remarks>⚠️ Spawn protection and friendly fire are deliberately ABSENT: both judge a relation
    /// between players and an object has no team — breaking your own team's cover with friendly fire off
    /// is the RULE, not a leak. Score, kill feed and <see cref="IGameMode.OnKill"/> stay out for the same
    /// reason.</remarks>
    private async Task HandleObjectHitAsync(PlayerState shooter, HitReportMsg msg)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            // ⚠️ WIDER than the player path's "playing only" gate (§10.3): with fireWhilePaused a target
            // board in the lobby is hittable while a player still is not.
            if (_phase != Phase.Playing && !_rules.FireWhilePaused)
            {
                RejectObjectHit(shooter, msg.targetNetId, $"faz {PhaseWire(_phase)}");
                return;
            }
            if (!ShooterGateOkLocked(shooter, out var shooterReason))
            {
                RejectObjectHit(shooter, msg.targetNetId, shooterReason);
                return;
            }
            if (!_objects.ApplyDamageLocked(msg.targetNetId, msg.damage, out var entry, out var reason))
            {
                RejectObjectHit(shooter, msg.targetNetId, reason);
                return;
            }
            // To EVERYONE (§10.10): a broken cover is everyone's cover.
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(WorldObjectTable.ToStateMsg(entry)));
        }
        await FlushAsync(outbox);
    }

    // ---- Object ownership, events, dynamic spawn (§10.10) ----

    /// <summary>object_grab (§10.10): the object exists → its kind is grabbable → it is free. On success
    /// the owner is written and <c>object_state</c> goes to EVERYONE.</summary>
    /// <remarks>⚠️ A rejection is SILENT toward the client and always will be: the result is
    /// <c>object_state.owner</c>, and a separate denial would carry the same fact on a second channel
    /// whose ordering against the broadcast is not guaranteed.</remarks>
    public async Task HandleObjectGrabAsync(PlayerState player, ObjectGrabMsg msg)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (!ObjectSenderOkLocked(player, MessageTypes.ObjectGrab, msg.netId)) return;
            if (!_objects.TryGrabLocked(msg.netId, player.PlayerId, msg.hand == 1, out var entry, out var reason))
            {
                RejectObject(player, MessageTypes.ObjectGrab, msg.netId, reason);
                return;
            }

            SyncOwnerLocked(entry);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(WorldObjectTable.ToStateMsg(entry)));
        }
        await FlushAsync(outbox);
    }

    /// <summary>object_release (§10.10): the object leaves the OWNER's hand. Ownership is KEPT — the
    /// flight is the thrower's to stream (§6.12) — and <c>object_state</c> goes to everyone.</summary>
    /// <remarks>⚠️ This does not end ownership; <c>object_rest</c> does. Ending it here would leave the
    /// flying object without a pose source, and nobody would see where it landed.</remarks>
    public async Task HandleObjectReleaseAsync(PlayerState player, ObjectReleaseMsg msg)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (!ObjectSenderOkLocked(player, MessageTypes.ObjectRelease, msg.netId)) return;
            if (!_objects.TryReleaseLocked(msg.netId, player.PlayerId, msg.pos, msg.rot,
                    out var entry, out var reason))
            {
                RejectObject(player, MessageTypes.ObjectRelease, msg.netId, reason);
                return;
            }

            // The Held bit changed, so the lock-free mirror must follow: the 0x09 gate opens exactly
            // here (owned but no longer held) and the pose stream starts.
            SyncOwnerLocked(entry);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(WorldObjectTable.ToStateMsg(entry)));
        }
        await FlushAsync(outbox);
    }

    /// <summary>object_rest (§10.10): the object STOPPED, so the OWNER's part is over. <c>Awake</c> drops,
    /// the reported pose becomes the resting pose and <c>object_state</c> goes to everyone.</summary>
    public async Task HandleObjectRestAsync(PlayerState player, ObjectRestMsg msg)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (!ObjectSenderOkLocked(player, MessageTypes.ObjectRest, msg.netId)) return;
            if (!_objects.TryRestLocked(msg.netId, player.PlayerId, msg.pos, msg.rot,
                    out var entry, out var reason))
            {
                RejectObject(player, MessageTypes.ObjectRest, msg.netId, reason);
                return;
            }

            SyncOwnerLocked(entry);
            _lastObjectPoses.TryRemove(entry.NetId, out _);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(WorldObjectTable.ToStateMsg(entry)));
        }
        await FlushAsync(outbox);
    }

    /// <summary>object_event (§10.10, in order): the object exists → the kind accepts this
    /// <c>name</c> → the policy's owner requirement → the phase gate. Then the MODE interprets it and its
    /// answer decides ONE thing — whether the same <c>object_event</c> is relayed to everyone.</summary>
    /// <remarks>⚠️ Any <c>object_state</c> was already published by the writing call
    /// (<c>SetObjectStage</c>/<c>SetObjectFlags</c>/<c>SetObjectPayload</c>/<c>SpawnObject</c>/
    /// <c>DespawnObject</c>): one event may change SEVERAL objects, so publishing "the event's object"
    /// here would announce only one of them. Relaying on top of that would announce one fact twice and
    /// the client would play the presentation twice.
    /// <para>The mode hook runs OUTSIDE the lock (hook contract) and may spawn/despawn while it runs, so
    /// the outcome message is queued into <see cref="_pendingOutbox"/> as well — that keeps it BEHIND
    /// whatever the hook queued, instead of racing ahead of it on a second sender. That queue is also why
    /// this handler is SYNCHRONOUS: it sends nothing itself, the tick loop dispatches.</para></remarks>
    public void HandleObjectEvent(PlayerState player, ObjectEventMsg msg)
    {
        IGameMode? mode;
        string kind;
        lock (_gate)
        {
            if (!ObjectSenderOkLocked(player, MessageTypes.ObjectEvent, msg.netId)) return;
            if (!_objects.TryGetLocked(msg.netId, out var entry))
            {
                RejectObject(player, MessageTypes.ObjectEvent, msg.netId, $"netId {msg.netId} bu sahnede yok");
                return;
            }
            // Free text on the wire, none on the server (§10.10 gate 2): an unlisted name is rejected.
            if (!_maps.Kinds.TryGetEvent(entry.Kind, msg.name, out var rule))
            {
                RejectObject(player, MessageTypes.ObjectEvent, msg.netId,
                    $"'{entry.Kind}' türü '{msg.name}' olayını kabul etmiyor");
                return;
            }
            if (string.Equals(rule.policy, ArenaProtocol.OBJECT_EVENT_POLICY_OWNER, StringComparison.OrdinalIgnoreCase)
                && entry.Owner != player.PlayerId)
            {
                RejectObject(player, MessageTypes.ObjectEvent, msg.netId,
                    $"'{msg.name}' yalnız sahibine açık (owner {entry.Owner})");
                return;
            }
            if (!string.Equals(rule.phaseGate, ArenaProtocol.OBJECT_PHASE_GATE_ANY, StringComparison.OrdinalIgnoreCase)
                && _phase != Phase.Playing)
            {
                RejectObject(player, MessageTypes.ObjectEvent, msg.netId, $"faz {PhaseWire(_phase)}");
                return;
            }

            kind = entry.Kind;
            mode = _mode;
        }

        var stateChanged = mode?.OnObjectEvent(this, player.PlayerId, msg.netId, kind, msg) ?? false;

        // The mode already broadcast every state it wrote; nothing left to announce.
        if (stateChanged) return;

        lock (_gate)
        {
            // Cosmetic: the same body goes back out to everyone, sender included (it ignores its own).
            QueueBroadcastLocked(_pendingOutbox, JsonUtil.Serialize(msg));
        }
    }

    /// <summary>Common sender gate of the three object messages: a connected PLAYER. Admins and
    /// pre-hello connections have no hands.</summary>
    private bool ObjectSenderOkLocked(PlayerState player, string type, int netId)
    {
        if (player.IsConnected && player.Role == "player") return true;

        RejectObject(player, type, netId, "gönderen bağlantısız/oyuncu değil");
        return false;
    }

    /// <summary>Mirrors one entry's ownership into the lock-free map read by StateHost (§6.12).</summary>
    private void SyncOwnerLocked(NetObjectEntry entry)
    {
        if (entry.Owner == 0) _objectOwners.TryRemove(entry.NetId, out _);
        else _objectOwners[entry.NetId] = new ObjectOwnership(entry.Owner,
            (entry.Flags & ArenaProtocol.OBJECT_FLAG_HELD) != 0);
    }

    /// <summary>Rebuilds the lock-free owner map from the table; used after a rebuild/reset, where owners
    /// disappear wholesale.</summary>
    private void SyncAllOwnersLocked()
    {
        _objectOwners.Clear();
        foreach (var entry in _objects.OwnedLocked()) SyncOwnerLocked(entry);
    }

    /// <summary>Frees every object held by this player and broadcasts one <c>object_state</c> each
    /// (§10.10): the owner died, dropped or was kicked and will never send <c>object_release</c>.</summary>
    private void ReleaseObjectsOfLocked(List<Outgoing> outbox, int playerId)
    {
        var released = _objects.ReleaseOwnedByLocked(playerId, LastObjectPose);
        foreach (var entry in released)
        {
            SyncOwnerLocked(entry);
            _lastObjectPoses.TryRemove(entry.NetId, out _);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(WorldObjectTable.ToStateMsg(entry)));
        }
    }

    private PoseData? LastObjectPose(int netId) =>
        _lastObjectPoses.TryGetValue(netId, out var pose) ? pose : null;

    /// <summary>Sweep for owners the server will never hear from again (§10.10): a record that dropped to
    /// <c>left</c> or was removed entirely (kick).</summary>
    /// <remarks>⚠️ A sweep and not an event hook because the two ways out differ: <c>left</c> is raised by
    /// the registry, but a KICKED player's record is gone, so no event could name the owner any more —
    /// and the object would stay locked until the round reset. Death is handled at its own single writer
    /// (<see cref="KillPlayerLocked"/>), so it never reaches this sweep.</remarks>
    private void TickObjectOwnersLocked(List<Outgoing> outbox)
    {
        if (_objectOwners.IsEmpty) return;

        List<int>? lost = null;
        foreach (var pair in _objectOwners)
        {
            var playerId = pair.Value.PlayerId;
            var gone = !_registry.TryGetByPlayerId(playerId, out var owner)
                       || owner.Connection == PlayerConnection.Left;
            if (!gone) continue;
            (lost ??= new List<int>()).Add(playerId);
        }
        if (lost == null) return;

        foreach (var playerId in lost.Distinct()) ReleaseObjectsOfLocked(outbox, playerId);
    }

    /// <summary>May this player stream the object's pose (§6.12)? Owner, and the object is NOT in a hand
    /// — the flight window between <c>object_release</c> and <c>object_rest</c>. Read LOCK-FREE from
    /// StateHost's recv thread: the pose gate must not queue behind the match lock.</summary>
    /// <remarks>⚠️ A HELD object is rejected on purpose: its pose comes from the owner's hand, which
    /// already streams at 20 Hz (<c>0x01</c>). Carrying it on both channels drifts the hand and the
    /// object apart — the symptom is "the knife floats next to the hand".</remarks>
    public bool IsObjectOwner(int netId, int playerId) =>
        playerId != 0 && _objectOwners.TryGetValue(netId, out var owner)
                      && owner.PlayerId == playerId && !owner.Held;

    /// <summary>Records the pose last seen on <c>0x09</c> (§6.12). Written from the recv thread, so the
    /// map is concurrent and the match lock is never taken here.</summary>
    /// <remarks>⚠️ This is NOT the object's state: <c>world_state</c> must carry a RESTING pose, and this
    /// one is a frame from mid-flight. Its only reader is the "owner dropped" release path.</remarks>
    public void RecordObjectPose(int netId, PoseData pose) => _lastObjectPoses[netId] = pose;

    /// <summary>Creates a runtime network object and announces it with <c>object_spawn</c> (§10.10);
    /// returns the allocated <c>netId</c>, or <c>0</c> when rejected (unknown kind / range exhausted).</summary>
    /// <remarks>Called from a mode hook, i.e. OUTSIDE the lock — the message therefore goes into
    /// <see cref="_pendingOutbox"/> (a mode cannot await), like <see cref="TryResetObjectsForMode"/>.
    /// <para>⚠️ There is no client-side spawn request and there will not be: the two sources of a spawn
    /// are the mode and the kind's own rule.</para>
    /// <para>With <paramref name="owner"/> set the object is born DIRECTLY IN A HAND; there is no separate
    /// "hand it over" message, because the <c>object_spawn</c> body already carries <c>owner</c> +
    /// <c>flags</c> — a second message would leave a frame showing the object on the floor.</para></remarks>
    public int SpawnObject(string kind, PoseData pose, int owner = 0, bool rightHand = false,
        string? payload = null)
    {
        lock (_gate)
        {
            if (!_objects.TrySpawnLocked(kind, pose, owner, rightHand, payload, out var entry, out var reason))
            {
                Console.WriteLine($"[world] object_spawn reddedildi ('{kind}'): {reason}.");
                return 0;
            }

            if (owner != 0) SyncOwnerLocked(entry);

            // Same body as object_state, only the type differs (§5.3): an unknown netId is a drift
            // warning there and the truth itself here.
            var spawn = WorldObjectTable.ToStateMsg(entry);
            spawn.type = MessageTypes.ObjectSpawn;
            QueueBroadcastLocked(_pendingOutbox, JsonUtil.Serialize(spawn));
            return entry.NetId;
        }
    }

    /// <summary>Removes a runtime object and announces <c>object_despawn</c> (§10.10); false = unknown id
    /// or a scene object (whose id is baked into the scene and cannot be removed).</summary>
    public bool DespawnObject(int netId)
    {
        lock (_gate)
        {
            if (!_objects.TryDespawnLocked(netId, out _, out var reason))
            {
                Console.WriteLine($"[world] object_despawn reddedildi (netId {netId}): {reason}.");
                return false;
            }

            _objectOwners.TryRemove(netId, out _);
            _lastObjectPoses.TryRemove(netId, out _);
            QueueBroadcastLocked(_pendingOutbox, JsonUtil.Serialize(new ObjectDespawnMsg { netId = netId }));
            return true;
        }
    }

    /// <summary>Writes an object's per-kind stage (§10.10) and broadcasts the resulting
    /// <c>object_state</c>; false = unknown id or unchanged value (then nothing is published).</summary>
    /// <remarks>⚠️ The WRITER announces: one event may change several objects (a valid serve touches the
    /// customer, the ingredients and the score at once), so "publish the event's object" would announce
    /// only one of them.</remarks>
    public bool SetObjectStage(int netId, int stage)
    {
        lock (_gate)
        {
            if (!_objects.SetStageLocked(netId, stage, out var entry)) return false;
            QueueBroadcastLocked(_pendingOutbox, JsonUtil.Serialize(WorldObjectTable.ToStateMsg(entry)));
            return true;
        }
    }

    /// <summary>Sets/clears object flag bits (§10.10) and broadcasts the resulting <c>object_state</c>;
    /// false = unknown id or nothing changed. Same writer-announces rule as
    /// <see cref="SetObjectStage"/>.</summary>
    public bool SetObjectFlags(int netId, int setMask, int clearMask)
    {
        lock (_gate)
        {
            if (!_objects.SetFlagsLocked(netId, setMask, clearMask, out var entry)) return false;
            QueueBroadcastLocked(_pendingOutbox, JsonUtil.Serialize(WorldObjectTable.ToStateMsg(entry)));
            return true;
        }
    }

    /// <summary>Writes an object's mode-defined per-instance text (§10.10) and broadcasts the resulting
    /// <c>object_state</c>; false = unknown id or unchanged value. Same writer-announces rule as
    /// <see cref="SetObjectStage"/>.</summary>
    public bool SetObjectPayload(int netId, string payload)
    {
        lock (_gate)
        {
            if (!_objects.SetPayloadLocked(netId, payload, out var entry)) return false;
            QueueBroadcastLocked(_pendingOutbox, JsonUtil.Serialize(WorldObjectTable.ToStateMsg(entry)));
            return true;
        }
    }

    /// <summary>Reads one object's state for a mode; false = no such object (outputs stay default).</summary>
    /// <remarks>⚠️ Returns COPIES, never the <see cref="NetObjectEntry"/>: mode hooks run OUTSIDE the
    /// lock, so a leaked reference would be read while another thread writes it.</remarks>
    public bool TryReadObject(int netId, out string kind, out int stage, out int owner, out int flags,
        out PoseData pose, out bool hasPose)
    {
        lock (_gate)
        {
            if (!_objects.TryGetLocked(netId, out var entry))
            {
                kind = "";
                stage = 0;
                owner = 0;
                flags = 0;
                pose = default;
                hasPose = false;
                return false;
            }

            kind = entry.Kind;
            stage = entry.Stage;
            owner = entry.Owner;
            flags = entry.Flags;
            pose = entry.Pose;
            hasPose = entry.HasPose;
            return true;
        }
    }

    /// <summary>netIds of the loaded scene's objects of one kind, in ascending order; empty when the
    /// scene has none. The way a mode learns which objects it may drive.</summary>
    /// <remarks>⚠️ Returns a COPY for the same reason as <see cref="TryReadObject"/>: mode hooks run
    /// outside the lock.
    /// <para>Meant for the SETUP path (<c>OnMatchStart</c>) — it walks the whole table, so calling it
    /// from the 10 Hz tick would rescan every object ten times a second for a list that only changes
    /// when the scene does.</para></remarks>
    public IReadOnlyList<int> ObjectIdsOfKind(string kind)
    {
        lock (_gate) return _objects.ListByKindLocked(kind);
    }

    /// <summary>Rejected object message: one console line, nothing sent back (§10.10).</summary>
    /// <remarks>NOT throttled like the hit path: grabs and interactions are hand-driven, a few per
    /// second at most — and a silently dropped grab with no trace is the harder bug to find.</remarks>
    private void RejectObject(PlayerState player, string type, int netId, string reason) =>
        Console.WriteLine($"[world] {type} reddedildi ({player.Name} → netId {netId}): {reason}.");

    /// <summary>revive_request (§10.4): revives when phase is Live + the player is dead + the delay has
    /// elapsed. Failing conditions are ignored SILENTLY — the client retries about once a second until
    /// revived, and logging that would flood the console.</summary>
    /// <remarks><see cref="ReviveAnchor"/> is NOT validated here (§10.4 note): "am I in my base / did I
    /// hold still" is the client's call — the server keeps books, it does not referee (§10.3 philosophy).
    /// <para>⚠️ This is the ONLY revive path and it carries all the bans (calibration §10.6,
    /// <c>reviveAnchor:"none"</c> §10.5, delay, obstacle §10.9). There is deliberately no operator
    /// override: a ban does not exist until every path changing that state is closed, so any second
    /// revive path would have to carry those gates along.</para></remarks>
    public async Task HandleReviveRequestAsync(PlayerState player)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Playing || player.Role != "player" || player.Alive) return;
            // §10.5: reviveAnchor "none" → no revive within the round. The client does not send it
            // anyway; closing it here also answers "what if an old client does".
            if (_rules.Revive == ReviveAnchor.None) return;
            if (!player.Calibrated) return; // §10.6: an uncalibrated player cannot revive
            var now = DateTime.UtcNow;
            if ((now - player.DiedAt).TotalSeconds < _rules.RespawnDelay) return;
            // §10.9: no revive INSIDE an obstacle — the player must step out first. The client does not
            // send the request anyway; closing it here keeps the rule off the client's trust.
            if (IsObstacleReviveBlockedLocked(player, now)) return;
            RevivePlayerLocked(outbox, player);
            Console.WriteLine($"[match] canlandı: {player.Name}");
        }
        await FlushAsync(outbox);
        FlushRosterRefresh(); // hp/alive changed → refresh the roster for the admin table (§5.3)
    }

    // ---- Phase transitions (all called under _gate) ----

    private void SetPhaseLocked(Phase next, DateTime now)
    {
        SetPhaseLocked(next, PauseReason.None, now);
    }

    /// <summary>Writes phase + reason together. Both change in one place so an inconsistent pair (e.g.
    /// <c>playing</c> + <c>loading</c>) can never appear on the wire.</summary>
    private void SetPhaseLocked(Phase next, PauseReason reason, DateTime now)
    {
        // The reason is meaningful only in Paused; other phases force-clear it.
        if (next != Phase.Paused) reason = PauseReason.None;

        if (_phase != next || _pauseReason != reason)
        {
            var from = Describe(_phase, _pauseReason);
            var to = Describe(next, reason);
            Console.WriteLine($"[match] durum {from} → {to}");
        }

        _phase = next;
        _pauseReason = reason;
        _phaseEnteredAt = now;
        RefreshShotRelayLocked();
    }

    /// <summary>Single human/log readable form of phase + reason (like <c>paused/loading</c>). Also used
    /// in rejection reasons: on the operator's status line the answer to "why was it rejected" is usually
    /// the phase itself.</summary>
    private static string Describe(Phase phase, PauseReason reason) =>
        phase == Phase.Paused && reason != PauseReason.None
            ? $"{PhaseWire(phase)}/{ReasonWire(reason)}"
            : PhaseWire(phase);

    /// <summary>Recomputes the shot relay gate (<see cref="ShotRelayOpen"/>) from phase + rules. This is
    /// the ONLY writer and is called EVERYWHERE the phase or <c>_rules</c> changes
    /// (<see cref="SetPhaseLocked"/> + every <c>_rules</c> assignment). Scattered assignments are avoided
    /// on purpose: the gate derives from two separate states, and with two writers one would go stale and
    /// surface as a silent field bug ("nobody's muzzle flash shows in the lobby").</summary>
    private void RefreshShotRelayLocked() =>
        _shotRelayOpen = _phase == Phase.Playing || _rules.FireWhilePaused;

    /// <summary>Phase → wire value (§10.1). Enum name and wire value are kept separate on purpose: the
    /// wire follows the lowercase convention, the C# name follows C#'s.</summary>
    private static string PhaseWire(Phase phase) => phase switch
    {
        Phase.Playing => ArenaProtocol.PHASE_PLAYING,
        Phase.Finished => ArenaProtocol.PHASE_FINISHED,
        _ => ArenaProtocol.PHASE_PAUSED
    };

    /// <summary>Pause reason → wire value; <see cref="PauseReason.None"/> is an empty string.</summary>
    private static string ReasonWire(PauseReason reason) => reason switch
    {
        PauseReason.Lobby => ArenaProtocol.PAUSE_REASON_LOBBY,
        PauseReason.Loading => ArenaProtocol.PAUSE_REASON_LOADING,
        PauseReason.Countdown => ArenaProtocol.PAUSE_REASON_COUNTDOWN,
        PauseReason.Operator => ArenaProtocol.PAUSE_REASON_OPERATOR,
        PauseReason.Mode => ArenaProtocol.PAUSE_REASON_MODE,
        _ => ""
    };

    /// <summary>Enters the countdown. Its length is the match's <see cref="_countdownSeconds"/> (§5.2) —
    /// the first round and later rounds are treated the same.</summary>
    private void EnterCountdownLocked(List<Outgoing> outbox, DateTime now)
    {
        SetPhaseLocked(Phase.Paused, PauseReason.Countdown, now);
        _countdownRemaining = _countdownSeconds;
        _nextSecondAt = now.AddSeconds(1);
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(new CountdownMsg { seconds = _countdownRemaining }));
    }

    private void EnterLiveLocked(List<Outgoing> outbox, DateTime now)
    {
        SetPhaseLocked(Phase.Playing, now);
        _timeRemaining = _roundSeconds;
        _nextSecondAt = now.AddSeconds(1);

        // §10.2: entering playing means full health + alive for everyone. In a round-based mode the
        // roster is usually ALREADY full here (the mode refreshed it when the round ended,
        // TryReviveRosterForMode) — repeating it is deliberate and idempotent: this is the gate every
        // match/round passes through, so the guarantee cannot depend on a mode remembering to ask.
        ReviveRosterLocked(outbox);

        // OnMatchStart ONCE per match, OnRoundStart on every Live entry (§ IGameMode).
        _roundStartPending = _mode != null;
        _matchStartPending = _mode != null && !_matchStarted;
        if (_mode != null) _matchStarted = true;

        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
    }

    private void EnterEndLocked(List<Outgoing> outbox, DateTime now, MatchOutcome outcome)
    {
        SetPhaseLocked(Phase.Finished, now);
        Console.WriteLine($"[match] maç sonu — kazanan: {DescribeOutcomeLocked(outcome)} " +
                          $"(kırmızı {_scoreRed} : mavi {_scoreBlue})");
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(new MatchEndMsg
        {
            winnerTeam = outcome.WinnerTeam,
            winnerPlayerId = outcome.WinnerPlayerId,
            scoreRed = _scoreRed,
            scoreBlue = _scoreBlue
        }));
    }

    /// <summary>Readable winner for the console line (team name / player name / draw).</summary>
    private string DescribeOutcomeLocked(MatchOutcome outcome)
    {
        if (!string.IsNullOrEmpty(outcome.WinnerTeam)) return outcome.WinnerTeam;
        if (outcome.WinnerPlayerId <= 0) return "berabere";
        return _registry.TryGetByPlayerId(outcome.WinnerPlayerId, out var winner)
            ? $"{winner.Name} (#{winner.PlayerId}, {winner.Score} puan)"
            : $"oyuncu {outcome.WinnerPlayerId}";
    }

    private void EnterLobbyLocked(List<Outgoing> outbox, DateTime now)
    {
        SetPhaseLocked(Phase.Paused, PauseReason.Lobby, now);
        _mode = null;
        _modeState = "";
        // The lobby KIND (§10.7): its rule shape differs from the default only by free fire. Damage is
        // still closed by the phase (hit_report only in playing) — this flag merely makes the muzzle flash
        // visible. modId is filled too, since the client resolves weapon loadout/HUD by that key.
        // The profile follows the scene we are RETURNING to, which is set a few lines below.
        ApplyRulesLocked(LobbyProfileForSceneLocked(_lobbyScene));
        // ⚠️ SetPhaseLocked ran above, so the gate was computed with the OLD _rules — which is exactly why
        // every place that changes the rules must refresh it itself.
        RefreshShotRelayLocked();
        _modeId = _lobbyScene.Length > 0 ? ArenaProtocol.LOBBY_MODE_ID : "";
        SetSceneLocked(_lobbyScene);
        _timeRemaining = 0f;
        _scoreRed = 0;
        _scoreBlue = 0;
        _roundSeconds = 0;
        _scoreLimit = 0;
        _countdownSeconds = ArenaProtocol.COUNTDOWN_SECONDS;
        _countdownRemaining = 0;
        _matchStartPending = false;
        _roundStartPending = false;
        _matchStarted = false;
        // The mode that would have consumed it is gone; carrying the press into the NEXT match would
        // skip that match's first hold.
        _modeContinuePending = false;

        foreach (var player in _registry.Snapshot())
        {
            if (player.Role != "player") continue;
            ResetMatchStateLocked(player);
            QueueReadyClearLocked(player);
        }

        RebuildObjectsLocked(_sceneName); // §10.10: every staging resets

        var returnMsg = new ReturnToLobbyMsg
        {
            modeId = _modeId,
            sceneName = _sceneName,
            sceneElapsed = SceneElapsedLocked,
            rules = _rules.ToInfo()
        };
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(returnMsg));
        QueueWorldStateLocked(outbox);
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));

        // ⚠️ The match ledger does NOT close here, it is only flagged (§10.2): the cleanup runs AFTER this
        // `return_to_lobby` broadcast, outside the lock (the registry raises events). Keeping the ledger
        // through the WHOLE `finished` phase is deliberate — players who left must appear in the
        // end-of-match table; clearing it on `match_end` would empty the table exactly as it is read.
        _participantCleanupPending = true;
    }

    /// <summary>Called from outside the tick (mode IsMatchOver); a no-op if an abort slipped in.</summary>
    private async Task EnterEndAsync(MatchOutcome outcome)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Playing) return;
            EnterEndLocked(outbox, DateTime.UtcNow, outcome);
        }
        await FlushAsync(outbox);
        FlushRosterRefresh();
    }

    // ---- Helpers ----

    /// <summary>Pulls the WHOLE roster to full health and tells each of them (<c>health_update</c>,
    /// §10.1) — the single implementation behind both round-boundary triggers.</summary>
    /// <remarks>⚠️ Everyone goes through <see cref="RevivePlayerLocked"/>, alive players too, never
    /// <see cref="ResetMatchStateLocked"/>: the latter writes the server fields but SENDS THE CLIENT
    /// NOTHING, and a round transition has no <c>load_match</c> for the client to reset itself on. Without
    /// the message a player who died mid-round FREEZES on the death screen, and one who survived wounded
    /// keeps the previous round's HP on their HUD while the server already reads MAX — the two drift apart
    /// until the next hit. Skipping the alive ones is exactly that trap and is not an optimisation.
    /// <para>Kills/deaths/score and the violation ledger are MATCH ledgers and survive the round: reviving
    /// does not touch them.</para>
    /// <para>Idempotent: calling it twice costs one duplicate <c>health_update</c> carrying the same
    /// value, which is why the <c>playing</c> gate can repeat it unconditionally.</para></remarks>
    private void ReviveRosterLocked(List<Outgoing> outbox)
    {
        foreach (var player in ConnectedPlayersLocked())
        {
            // ⚠️ The obstacle grace is reset too (§10.9): its clock only advances on `playing` ticks, so
            // time spent paused would silently consume it and the player would start losing health on the
            // first tick after the resume — having burned their three seconds during the pause.
            player.ObstacleSince = null;

            // ⚠️ Starting a match/round grants NO spawn protection (§10.4), deliberately: protection
            // exists to keep a respawning player from being shot on their first frame, whereas at match
            // start everyone begins together after a countdown — there it would only make the match's
            // first seconds damage-free. One gate for the whole roster: protecting some and not others
            // would mean two rules in one match.
            RevivePlayerLocked(outbox, player, spawnProtect: false); // hp = MAX, alive = 1, health_update
            player.DiedAt = DateTime.MinValue;
        }
    }

    /// <summary>Wipes a player's match state for a MATCH boundary (match setup / lobby return).</summary>
    /// <remarks>⚠️ NOT for a round boundary: it writes the server fields and sends the client nothing —
    /// round start revives instead (see <see cref="EnterLiveLocked"/>).</remarks>
    private void ResetMatchStateLocked(PlayerState player)
    {
        player.Hp = ArenaProtocol.PLAYER_MAX_HP;
        player.Alive = true;
        player.DiedAt = DateTime.MinValue;
        // No stale stamp is left (§10.4): this method is also reached from match setup and the lobby
        // return, so a player with unexpired protection would carry their shield into the lobby.
        // Protection is granted only to a player revived after DYING (StampSpawnProtectionLocked) and none
        // of these paths is such a revive — the stamp cleared here is not put back.
        player.SpawnProtectedUntil = DateTime.MinValue;
        _rosterRefreshFor = player;
        player.Kills = 0;
        player.Deaths = 0;
        player.Score = 0;
        // The violation ledger is a match ledger: reset alongside kills/deaths — the number the operator
        // sees spans the match, not the round.
        player.ObstacleTally.Reset();
        player.OutOfBoundsTally.Reset();
    }

    /// <summary>The ONLY place the friendly-fire decision is made. An empty team is never a teammate: in
    /// teamless modes everyone's team is <c>""</c>, so a plain <c>a.Team == b.Team</c> would reject ALL
    /// hits via "" == "" (§10.3/4).</summary>
    private static bool AreTeammates(PlayerState a, PlayerState b) =>
        !string.IsNullOrEmpty(a.Team) && a.Team == b.Team;

    /// <summary>The ONLY writer of spawn protection (§10.4); called only by
    /// <see cref="RevivePlayerLocked"/>, so protection is the answer to a DEATH — match/round start grants
    /// none.</summary>
    /// <remarks>⚠️ When the rules grant no protection (or <paramref name="protect"/> is <c>false</c>) the
    /// stamp is PULLED to <see cref="DateTime.MinValue"/>, not left alone: switching from a match with
    /// protection to one without would leave a stale stamp making the player untouchable for no
    /// reason.</remarks>
    private void StampSpawnProtectionLocked(PlayerState player, bool protect = true) =>
        player.SpawnProtectedUntil = protect && _rules.SpawnProtectionSeconds > 0f
            ? DateTime.UtcNow.AddSeconds(_rules.SpawnProtectionSeconds)
            : DateTime.MinValue;

    /// <summary><paramref name="spawnProtect"/> is passed <c>false</c> only at match/round start (§10.4):
    /// there the revive answers no DEATH, it is the match being set up.</summary>
    private void RevivePlayerLocked(List<Outgoing> outbox, PlayerState player, bool spawnProtect = true)
    {
        player.Hp = ArenaProtocol.PLAYER_MAX_HP;
        player.Alive = true;
        StampSpawnProtectionLocked(player, spawnProtect);
        _rosterRefreshFor = player;
        // attackerId=0: a revive is not the result of an attack (§10.4/3).
        // Targeted send (§10.3): the revived player + admins.
        QueueHealthUpdateLocked(outbox, player, JsonUtil.Serialize(new HealthUpdateMsg
        {
            playerId = player.PlayerId,
            hp = player.Hp,
            attackerId = 0
        }));
    }

    /// <summary>Moves half of the crowded side over so no team stays empty, and puts teamless players on
    /// the smaller side (§10.1). Called ONLY outside the lock, since registry.SetTeam raises events.</summary>
    private void BalanceTeams(List<PlayerState> players)
    {
        var red = players.Where(p => p.Team == "red").ToList();
        var blue = players.Where(p => p.Team == "blue").ToList();

        foreach (var player in players.Where(p => p.Team != "red" && p.Team != "blue").ToList())
        {
            var team = red.Count <= blue.Count ? "red" : "blue";
            _registry.SetTeam(player.PlayerId, team);
            (team == "red" ? red : blue).Add(player);
        }

        if (red.Count != 0 && blue.Count != 0) return;

        var full = red.Count == 0 ? blue : red;
        var emptyTeam = red.Count == 0 ? "red" : "blue";
        var moveCount = full.Count / 2;
        for (var i = 0; i < moveCount; i++)
        {
            var player = full[full.Count - 1];
            full.RemoveAt(full.Count - 1);
            _registry.SetTeam(player.PlayerId, emptyTeam);
        }
        Console.WriteLine($"[match] takım dengeleme: {moveCount} oyuncu '{emptyTeam}' takımına taşındı.");
    }

    /// <summary>Teamless mode (§10.5 <c>teamMode:"none"</c>): clears teams assigned in the lobby, nobody
    /// is split into red/blue. Like <see cref="BalanceTeams"/>, called ONLY outside the lock since
    /// registry.SetTeam raises events.</summary>
    private void ClearTeams(List<PlayerState> players)
    {
        var cleared = 0;
        foreach (var player in players)
        {
            if (string.IsNullOrEmpty(player.Team)) continue;
            _registry.SetTeam(player.PlayerId, "");
            cleared++;
        }

        if (cleared > 0)
            Console.WriteLine($"[match] takımsız mod: {cleared} oyuncunun takımı temizlendi.");
    }

    /// <summary>The single player list used by the match gates: only CONNECTED (§2 <c>connected</c>)
    /// players. <c>reconnecting</c>/<c>left</c> records do not appear — the loading gate does not wait for
    /// them, they cannot be hit or revived, and they do not count towards the winner.</summary>
    private List<PlayerState> ConnectedPlayersLocked() =>
        _registry.Snapshot().Where(p => p.IsConnected && p.Role == "player").OrderBy(p => p.PlayerId).ToList();

    private MatchInfo BuildMatchInfoLocked() => new()
    {
        phase = PhaseWire(_phase),
        phaseReason = ReasonWire(_pauseReason),
        modeId = _modeId,
        modeState = _modeState,
        sceneName = _sceneName,
        sceneElapsed = SceneElapsedLocked,
        timeRemaining = _timeRemaining,
        scoreRed = _scoreRed,
        scoreBlue = _scoreBlue,
        rules = _rules.ToInfo()
    };

    private MatchStateMsg BuildMatchStateLocked() => new()
    {
        phase = PhaseWire(_phase),
        phaseReason = ReasonWire(_pauseReason),
        modeState = _modeState,
        timeRemaining = _timeRemaining,
        scoreRed = _scoreRed,
        scoreBlue = _scoreBlue
    };

    /// <summary>Queues to every connected socket (admins included).</summary>
    /// <remarks>There is no "skip this player" parameter: its only user was the shot relay, which moved to
    /// UDP. That channel does no filtering — the shooter gets its own event back and ignores it (§6.5). WS
    /// messages go to everyone by definition.</remarks>
    private void QueueBroadcastLocked(List<Outgoing> outbox, string json)
    {
        foreach (var player in _registry.Snapshot())
        {
            if (!player.IsConnected) continue;
            var connection = player.Socket;
            if (connection == null) continue;
            outbox.Add(new Outgoing(connection, json, player.Name));
        }
    }

    /// <summary>Rebuilds the network object table for the scene being staged (§10.10): every object at
    /// full health, no flags.</summary>
    /// <remarks>⚠️ Deliberately NOT hooked to <see cref="SetSceneLocked"/>, which returns early when the
    /// same scene is staged again: a SECOND match on the same map must reset too. The client reloads the
    /// scene and draws its objects intact either way, so a stale table would leave the server holding
    /// covers the players see standing.
    /// <para>An unknown scene empties the table — the server invents no objects.</para></remarks>
    private void RebuildObjectsLocked(string sceneName)
    {
        var map = _maps.TryGet(sceneName, out var found) ? found : null;
        _objects.RebuildLocked(map, _maps.Kinds);
        // The lock-free mirrors describe the OLD scene now; a stale owner would let a dropped 0x09
        // packet pass ownership validation for an object that no longer exists.
        _objectOwners.Clear();
        _lastObjectPoses.Clear();
    }

    /// <summary>Queues a full <c>world_state</c> to everyone; silent when the scene has no network
    /// objects (§10.10) — an empty snapshot tells the client nothing.</summary>
    private void QueueWorldStateLocked(List<Outgoing> outbox)
    {
        if (_objects.Count == 0) return;

        QueueBroadcastLocked(outbox, BuildWorldStateJsonLocked());
    }

    private string BuildWorldStateJsonLocked() => JsonUtil.Serialize(new WorldStateMsg
    {
        sceneName = _sceneName,
        objects = _objects.BuildStatesLocked()
    });

    /// <summary>Queues <c>health_update</c> ONLY to the subject player + admins (§10.3).</summary>
    /// <remarks>Why not a broadcast: the message has two consumers and both are narrow —
    /// <c>PlayerCombatState</c> already discards every message outside its own <c>playerId</c>, and the
    /// admin table draws everyone's health. In a 10-player match each hit sent 11 TCP messages of which 9
    /// were thrown away. Cutting the fan-out changes no behaviour; it just stops producing packets nobody
    /// reads.
    /// <para>⚠️ This is the deliberate EXCEPTION to "WS messages go to everyone by definition": produced
    /// per hit, it is the one message whose fan-out grows with the SQUARE of the player count. When adding
    /// a new "tell everyone per event" message the question is not "how many bytes" but "how many
    /// datagrams" (Docs/Sistem-Ozeti.md §3.12).</para></remarks>
    private void QueueHealthUpdateLocked(List<Outgoing> outbox, PlayerState subject, string json)
    {
        foreach (var player in _registry.Snapshot())
        {
            if (!player.IsConnected) continue;
            var connection = player.Socket;
            if (connection == null) continue;
            if (!ReferenceEquals(player, subject) && player.Role != "admin") continue;
            outbox.Add(new Outgoing(connection, json, player.Name));
        }
    }

    /// <summary>Queues only to connected admins (§5.3). Same class of narrow broadcast as
    /// <see cref="QueueHealthUpdateLocked"/>: the sole consumer is the operator screen, and sending it to
    /// players would multiply unread packets by the player count.</summary>
    private void QueueAdminBroadcastLocked(List<Outgoing> outbox, string json)
    {
        foreach (var player in _registry.Snapshot())
        {
            if (!player.IsConnected || player.Role != "admin") continue;
            var connection = player.Socket;
            if (connection == null) continue;
            outbox.Add(new Outgoing(connection, json, player.Name));
        }
    }

    /// <summary>Is any admin connected — asked BEFORE serialising: producing JSON nobody looks at is
    /// wasted work (see <see cref="TickViolationFeedLocked"/>).</summary>
    private bool HasConnectedAdminLocked() =>
        _registry.Snapshot().Any(p => p.IsConnected && p.Role == "admin" && p.Socket != null);

    private void QueueReadyClearLocked(PlayerState player)
    {
        if (player.Ready) _readyClearQueue.Add(player.DeviceId);
    }

    /// <summary>Outside the lock: registry.SetReady → Changed → lobby_state broadcast.</summary>
    /// <summary>Outside the lock: registry.Announce → Changed → lobby_state broadcast. The `Updated` kind
    /// logs NO console line (Program.cs), it only refreshes the roster.</summary>
    private void FlushRosterRefresh()
    {
        PlayerState? player;
        lock (_gate)
        {
            player = _rosterRefreshFor;
            _rosterRefreshFor = null;
        }

        if (player != null) _registry.Announce(player, PlayerChangeKind.Updated);
    }

    /// <summary>Closes the match ledger (§10.2): <c>left</c> records are dropped (their playerIds return
    /// to the pool) and remaining <c>inMatch</c> flags are cleared.</summary>
    /// <remarks>⚠️ The ordering is binding: runs only on the lobby return and AFTER <c>match_end</c> + the
    /// final <c>lobby_state</c> — the client's end-of-match table is drawn from the roster, so early
    /// cleanup would cost it rows. It must also be outside the lock since the registry raises
    /// events.</remarks>
    private void FlushParticipantCleanup()
    {
        lock (_gate)
        {
            if (!_participantCleanupPending) return;
            _participantCleanupPending = false;
        }

        var purged = _registry.PurgeLeftParticipants();
        _registry.ClearMatchParticipants();
        if (purged > 0)
            Console.WriteLine($"[match] maç defteri kapandı: ayrılmış {purged} kayıt silindi (playerId'leri havuza döndü).");
    }

    /// <summary>Dispatches the sends mode hooks collected under the lock (see
    /// <see cref="_pendingOutbox"/>). Called from the tick loop — a single sender, so order is
    /// preserved.</summary>
    private async Task FlushPendingAsync()
    {
        List<Outgoing> pending;
        lock (_gate)
        {
            if (_pendingOutbox.Count == 0) return;
            pending = new List<Outgoing>(_pendingOutbox);
            _pendingOutbox.Clear();
        }

        await FlushAsync(pending);
        FlushReadyClear();
        FlushRosterRefresh();
    }

    private void FlushReadyClear()
    {
        string[] devices;
        lock (_gate)
        {
            if (_readyClearQueue.Count == 0) return;
            devices = _readyClearQueue.ToArray();
            _readyClearQueue.Clear();
        }
        foreach (var deviceId in devices) _registry.SetReady(deviceId, false);
    }

    private static async Task FlushAsync(List<Outgoing> outbox)
    {
        foreach (var item in outbox)
        {
            try
            {
                await item.Connection.SendTextAsync(item.Json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[match] gönderim başarısız ({item.Who}): {ex.Message}");
            }
        }
    }

    /// <summary>Rejected hit_report: the reason is kept verbatim, but at most one line per shooter every
    /// RejectLogIntervalSeconds (so the console does not drown while someone keeps firing at a dead
    /// target). Suppressed lines are not swallowed: their count is appended to the next printed line.
    /// Phase/kill/match-end/revive lines are NOT throttled (they are rare).</summary>
    private void RejectHit(PlayerState shooter, int targetPlayerId, string reason) =>
        RejectHitLog(shooter, targetPlayerId.ToString(), reason);

    /// <summary>Network object target (§10.10): the label is a netId, the throttle is the shooter's
    /// same one — one player spraying a broken cover must not drown the console either.</summary>
    private void RejectObjectHit(PlayerState shooter, int netId, string reason) =>
        RejectHitLog(shooter, $"netId {netId}", reason);

    private void RejectHitLog(PlayerState shooter, string target, string reason)
    {
        int suppressed;
        lock (_rejectLogGate)
        {
            var now = DateTime.UtcNow;
            if (_lastRejectLogAt.TryGetValue(shooter.PlayerId, out var last) &&
                (now - last).TotalSeconds < RejectLogIntervalSeconds)
            {
                _suppressedRejects[shooter.PlayerId] = _suppressedRejects.GetValueOrDefault(shooter.PlayerId) + 1;
                return;
            }
            _lastRejectLogAt[shooter.PlayerId] = now;
            suppressed = _suppressedRejects.GetValueOrDefault(shooter.PlayerId);
            _suppressedRejects.Remove(shooter.PlayerId);
        }
        var tail = suppressed > 0 ? $" (+{suppressed} bastırıldı)" : "";
        Console.WriteLine($"[match] hit_report reddedildi ({shooter.Name} → {target}): {reason}.{tail}");
    }
}
