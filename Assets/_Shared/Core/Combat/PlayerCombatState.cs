using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

// Alias: the class has a property named Team, so enum members are reached through this.
using CoreTeam = VortexArena.Core.Team;

namespace VortexArena.Core.Combat
{
    /// <summary>LOCAL player's match/combat state (persistent singleton): team, health, alive, fire
    /// permission and the free-roam revive flow.
    /// <para>Server-authoritative: health only from <c>health_update</c>, phase only from
    /// <c>match_state</c>/<c>countdown</c>/<c>load_match</c>. This class applies no damage, keeps
    /// no score and never changes phase (Docs/ArenaNet-Protokol.md §10).</para>
    /// <para>⚠️ FREE-ROAM RULE: a physical player is never teleported. Revive is a STATE change,
    /// not a position change — this class never moves the rig/camera. Same for map change:
    /// <c>load_match</c> respawns nobody and does not reset calibration (§10.4). The place to
    /// return to after death is a <see cref="BaseZone"/>.</para>
    /// <para>Never placed in a scene: <c>load_match</c> arrives BEFORE the scene loads, so this
    /// self-bootstraps (ArenaClient/SceneRouter pattern) and goes
    /// DontDestroyOnLoad.</para></summary>
    public class PlayerCombatState : MonoBehaviour
    {
        // Phase names travel as strings (§10.1). Constants come from ArenaProtocol, the only
        // place the wire value is written.
        private const string PhasePaused = ArenaProtocol.PHASE_PAUSED;
        private const string PhasePlaying = ArenaProtocol.PHASE_PLAYING;
        private const string PhaseFinished = ArenaProtocol.PHASE_FINISHED;

        /// <summary>Revive request retry interval until the server confirms (§10.4).</summary>
        private const float ReviveRepeatSeconds = 1f;

        private const float ZoneRescanSeconds = 1f;

        public static PlayerCombatState Instance { get; private set; }

        public int PlayerId { get; private set; }

        /// <summary>Team from load_match.yourTeam (also refreshed by lobby_state in the lobby).
        /// Starts <see cref="CoreTeam.Neutral"/> so a teamless mode does not send the player to the
        /// wrong base (§10.5).</summary>
        public Team Team
        {
            get => _team;
            private set
            {
                if (_team == value)
                {
                    return;
                }

                _team = value;
                LocalTeamChanged?.Invoke(value);
            }
        }

        private CoreTeam _team = CoreTeam.Neutral;

        public string ModeId { get; private set; } = "";

        /// <summary>Last phase from the server: <c>paused</c> | <c>playing</c> | <c>finished</c>
        /// (§10.1). An unknown value counts as paused so the damage/fire gate fails safe.</summary>
        public string Phase { get; private set; } = PhasePaused;

        /// <summary>Pause reason (§10.1 <c>phaseReason</c>); empty while not paused. PRESENTATION
        /// only — no combat gate reads it.</summary>
        public string PhaseReason { get; private set; } = ArenaProtocol.PAUSE_REASON_LOBBY;

        /// <summary>Mode's own sub-state (§10.1 <c>modeState</c>); the core never interprets it.</summary>
        public string ModeState { get; private set; } = "";

        /// <summary>Local health — set ONLY from health_update.</summary>
        public float Hp { get; private set; } = ArenaProtocol.PLAYER_MAX_HP;

        /// <summary>Alive (hp &gt; 0; also false on a respawn message).</summary>
        public bool IsAlive { get; private set; } = true;

        public string StatusText { get; private set; } = "";

        /// <summary>§10.4: is the local player spawn-protected — the server's snapshot bit, NOT a
        /// local timer (source: <see cref="RemotePlayerRegistry.IsLocalSpawnProtected"/>).
        /// <para>⚠️ An INDICATOR, not a permission: the damage gate is on the server and no
        /// fire/damage decision reads this. Since the player's own shield is never drawn, this flag
        /// and its status text are the ONLY way the protection reaches the player.</para></summary>
        public bool IsSpawnProtected =>
            RemotePlayerRegistry.Instance != null && RemotePlayerRegistry.Instance.IsLocalSpawnProtected;

        /// <summary>Is there at least one base zone OPEN to the player (own team or
        /// <c>Neutral</c>) in the scene. Meaningful only while tracking is on —
        /// see <see cref="RequestBaseTracking"/>.</summary>
        public bool HasOpenBaseZone { get; private set; }

        /// <summary>Is the player INSIDE a base zone open to them.
        /// <para>⚠️ Independent of revive. Two consumers with different conditions: revive (only in
        /// <see cref="ModeReviveAnchor.OwnBase"/>, only while dead) and a round-based mode's muster
        /// gate (alive or dead, even with <see cref="ModeReviveAnchor.None"/>). The zone matching
        /// rule (§10.4) lives in ONE place in this class and is never copied.</para></summary>
        public bool IsInsideOwnBase { get; private set; }

        public event Action<float> HpChanged;
        public event Action<bool> AliveChanged;
        public event Action<string> StatusChanged;

        /// <summary>Local team changed (<c>load_match</c> / <c>lobby_state</c>); fires only on a
        /// real value change.
        /// <para>⚠️ STATIC unlike the others: its listener (<c>BaseZoneVisibility</c>) is a
        /// self-bootstrapping singleton that can be born BEFORE <see cref="Instance"/>, so an
        /// instance event would force it to wait. Same pattern as <c>ModeRuntime.Changed</c>.</para>
        /// </summary>
        public static event Action<CoreTeam> LocalTeamChanged;

        /// <summary>Local alive state changed; fires only on a real value change. ⚠️ Raised with
        /// <see cref="AliveChanged"/> but STATIC, for the same reason as
        /// <see cref="LocalTeamChanged"/>.</summary>
        public static event Action<bool> LocalAliveChanged;

        /// <summary>Can the trigger be pulled: alive + (phase <c>playing</c> OR the mode allows
        /// free fire — <see cref="ModeRuntime.FireWhilePaused"/>, §10.5).
        /// <para>⚠️ A MODE rule, not a phase rule. Never write <c>if (modeId == "lobby")</c> here —
        /// a mode that wants this behaviour declares it in its own rules. Damage stays off anyway:
        /// the server gates it on <c>playing</c> (§10.3).</para>
        /// <para>The trigger also dies while the PLAYER's own body is inside an obstacle (§10.9,
        /// <see cref="ObstacleViolationProbe.IsBodyBlocked"/>). The weapon's own obstacle gate is
        /// ADDITIONAL: one asks "am I exposing my body", the other "where is my muzzle".</para>
        /// <para>Connection: once connected, fire locks while the link is down. If we never
        /// connected (server-less Editor test) the weapon keeps working.</para></summary>
        public bool CanFire
        {
            get
            {
                if (!IsAlive)
                {
                    return false;
                }

                // §10.6: an uncalibrated player cannot fire. The server already rejects the
                // hit_report; gating here avoids the "pulled the trigger, nothing happened" feel.
                if (!CalibrationState.IsCalibrated)
                {
                    return false;
                }

                // §10.9: a player whose head or hand is INSIDE an obstacle cannot fire.
                // ⚠️ The gate lives HERE, not on the weapon: someone standing inside a block and
                // reaching the gun out has a perfectly clear weapon. One gate means a future
                // grenade/arrow inherits the rule for free.
                if (ObstacleViolationProbe.IsBodyBlocked)
                {
                    return false;
                }

                if (Phase != PhasePlaying && !ModeRuntime.FireWhilePaused)
                {
                    return false;
                }

                if (!_hasEverConnected)
                {
                    return true;
                }

                ArenaClient client = ArenaClient.Instance;
                return client != null && client.IsConnected;
            }
        }

        // Single instance (field-less message): no per-frame DTO allocation.
        private readonly ReviveRequestMsg _reviveMsg = new ReviveRequestMsg();

        private bool _hasEverConnected;

        /// <summary>Has health/alive been learned once from the authoritative flow
        /// (<c>health_update</c>/<c>respawn</c>) or the roster. Reset on every connection.</summary>
        private bool _healthConfirmed;

        private bool _awaitingRevive;
        private float _reviveAt;
        private float _nextReviveSendAt;

        private BaseZone[] _zones = Array.Empty<BaseZone>();
        private float _nextZoneScanAt;

        /// <summary>Base tracking validity deadline (<see cref="RequestBaseTracking"/>). Timed on
        /// purpose: when the requesting component dies, tracking closes itself.</summary>
        private float _baseTrackingUntil;

        private string _modePrompt = "";

        // StandStill revive anchor (§10.4/2). _hasHoldAnchor also means "is the head trackable":
        // with no camera there is no anchor and the stand-still condition is never required.
        private Vector3 _holdAnchor;
        private bool _hasHoldAnchor;
        private float _holdSince;
        private Transform _head;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[PlayerCombatState]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PlayerCombatState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Persistent singleton: subscribe in Awake/OnDestroy, not OnEnable/OnDisable, so
            // server events are not missed while the object is disabled.
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnDisconnected += HandleDisconnected;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnHealthUpdate += HandleHealthUpdate;
            NetEvents.OnRespawn += HandleRespawn;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            ScanZones();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnDisconnected -= HandleDisconnected;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnHealthUpdate -= HandleHealthUpdate;
            NetEvents.OnRespawn -= HandleRespawn;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;

            SceneManager.sceneLoaded -= HandleSceneLoaded;

            Instance = null;
        }

        private void Update()
        {
            if (Instance != this)
            {
                return;
            }

            TickHoldAnchor();
            RefreshZoneState();
            TickRevive();
            RefreshStatusText();
        }

        // --------------------------------------------------------- free-roam revive

        /// <summary>§10.4: sends <c>revive_request</c> once the delay expired AND the mode's
        /// revive condition is met, repeating every second until the server revives us. The
        /// condition comes from <see cref="ModeRuntime.Revive"/> — no branch on mode name.</summary>
        private void TickRevive()
        {
            if (IsAlive || !_awaitingRevive || Time.time < _reviveAt)
            {
                return;
            }

            if (!IsReviveConditionMet())
            {
                return;
            }

            if (Time.time < _nextReviveSendAt)
            {
                return;
            }

            _nextReviveSendAt = Time.time + ReviveRepeatSeconds;
            ArenaClient.Instance?.Send(_reviveMsg);
        }

        /// <summary>In StandStill mode, anchors the HMD position at death and resets anchor and
        /// timer on any move beyond <see cref="ArenaProtocol.REVIVE_HOLD_RADIUS"/>. Runs during the
        /// death delay too, so standing still while waiting costs no extra wait.
        /// <para>⚠️ The timer does NOT run inside an obstacle (§10.9): otherwise waiting inside one
        /// would revive the player on exit and obstacles would become shelters.</para></summary>
        private void TickHoldAnchor()
        {
            if (IsAlive || ModeRuntime.Revive != ModeReviveAnchor.StandStill)
            {
                _hasHoldAnchor = false;
                return;
            }

            Transform head = ResolveHead();
            if (head == null)
            {
                _hasHoldAnchor = false; // untrackable → condition not required (IsReviveConditionMet)
                return;
            }

            Vector3 position = head.position;

            // §10.9: inside an obstacle the timer does NOT advance — anchor and time refresh
            // every frame, so the stand-still condition restarts from zero after leaving.
            // ⚠️ Do NOT set _hasHoldAnchor false here: false means "condition unmeasurable" and
            // IsReviveConditionMet reads that as CONDITION MET (fail-open) — the exact opposite.
            if (ObstacleViolationProbe.IsViolating)
            {
                _holdAnchor = position;
                _holdSince = Time.time;
                _hasHoldAnchor = true;
                return;
            }

            if (_hasHoldAnchor &&
                Vector3.Distance(position, _holdAnchor) <= ArenaProtocol.REVIVE_HOLD_RADIUS)
            {
                return;
            }

            _holdAnchor = position;
            _holdSince = Time.time;
            _hasHoldAnchor = true;
        }

        /// <summary>Is the mode's revive condition met (§10.5 <c>reviveAnchor</c>). An
        /// unmeasurable condition counts as MET. ⚠️ This fail-open is the only safety net: the
        /// server has no timer-based revive, so a dead player is raised only by the
        /// <c>revive_request</c> sent from here — staying silent would leave them dead for the rest
        /// of the match.</summary>
        private bool IsReviveConditionMet()
        {
            // §10.5 reviveAnchor:"none" — no revive in round-based elimination. The server
            // rejects it anyway; gating here avoids a pointless request every second and a
            // "reviving" text that will never come true.
            if (ModeRuntime.Revive == ModeReviveAnchor.None)
            {
                return false;
            }

            // §10.9: no revive inside an obstacle; the server rejects it. ⚠️ Authority stays on
            // the server — deleting this line keeps the rule, it only makes the feedback lie.
            if (ObstacleViolationProbe.IsViolating)
            {
                return false;
            }

            if (ModeRuntime.Revive == ModeReviveAnchor.StandStill)
            {
                return !_hasHoldAnchor || HoldRemaining <= 0f;
            }

            return !HasOpenBaseZone || IsInsideOwnBase;
        }

        private float HoldRemaining =>
            !_hasHoldAnchor
                ? 0f
                : Mathf.Max(0f, ArenaProtocol.REVIVE_HOLD_SECONDS - (Time.time - _holdSince));

        private Transform ResolveHead()
        {
            if (_head != null)
            {
                return _head;
            }

            Camera cam = Camera.main;
            _head = cam != null ? cam.transform : null;
            return _head;
        }

        private void RefreshStatusText()
        {
            string text = "";

            // §10.6 — comes BEFORE the death text: an uncalibrated player cannot revive anyway,
            // so telling them to run to their base would be a wasted trip.
            if (!CalibrationState.IsCalibrated)
            {
                text = "Kalibrasyon gerekli — sağ kumandada A basılıyken B×2";
            }
            else if (!string.IsNullOrEmpty(_modePrompt))
            {
                // The mode's own prompt (e.g. muster between rounds). OVERRIDES the death text:
                // during a mode pause there is no revive, this is the only thing to do.
                text = _modePrompt;
            }
            else if (!IsAlive)
            {
                // §10.5 reviveAnchor:"none" — no revive, so a countdown would lie: the player is
                // waiting for the round, not for a timer. A generic FALLBACK; the round-based mode
                // writes its own prompt (which overrides this). No mode-specific text here
                // (see the SetModePrompt contract below).
                if (ModeRuntime.Revive == ModeReviveAnchor.None)
                {
                    text = "Öldün — tur bitene kadar bekle";
                }
                else if (ObstacleViolationProbe.IsViolating)
                {
                    // §10.9: no revive before leaving the obstacle; a countdown would lie, what
                    // is awaited is the player's own move.
                    text = "Engelden çık ve canlan";
                }
                else
                {
                    float remaining = _reviveAt - Time.time;
                    if (remaining > 0f)
                    {
                        text = $"Öldün — canlanmaya {Mathf.CeilToInt(remaining)} sn";
                    }
                    else if (ModeRuntime.Revive == ModeReviveAnchor.StandStill)
                    {
                        float hold = HoldRemaining;
                        text = hold > 0f
                            ? $"Canlanmak için sabit dur — {Mathf.CeilToInt(hold)} sn"
                            : "Canlanılıyor...";
                    }
                    else
                    {
                        text = !HasOpenBaseZone || IsInsideOwnBase ? "Canlanılıyor..." : "Tabanına dön ve canlan";
                    }
                }
            }
            else if (IsSpawnProtected)
            {
                // §10.4: protection applies only while alive, right after a revive — hence it
                // comes AFTER the death branch and never overrides it.
                text = "Yeniden doğma koruması — hasar almıyorsun";
            }

            if (text == StatusText)
            {
                return;
            }

            StatusText = text;
            StatusChanged?.Invoke(text);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ScanZones();
            _head = null; // the new scene has its own rig; resolve the camera again
        }

        private void ScanZones()
        {
            _zones = FindObjectsByType<BaseZone>(FindObjectsSortMode.None);
            _nextZoneScanAt = Time.time + ZoneRescanSeconds;
        }

        /// <summary>Refreshes base zone state once per frame — while DEAD (revive condition) or
        /// when someone asks (<see cref="RequestBaseTracking"/>); otherwise the flags are cleared.
        /// If no open zone is found the scene is rescanned once a second: a zone may have been
        /// added late, or our team may have just arrived with <c>load_match</c>.</summary>
        private void RefreshZoneState()
        {
            // ⚠️ Tracking is NOT tied to the revive condition: a round-based muster gate needs the
            // answer for LIVE players too, with reviveAnchor "none". The driver is a REQUEST — if
            // nobody asks (lobby, FFA, live player) nothing is computed.
            if (IsAlive && Time.time >= _baseTrackingUntil)
            {
                HasOpenBaseZone = false;
                IsInsideOwnBase = false;
                return;
            }

            EvaluateZonesNow();
        }

        /// <summary>Evaluates zones; if none is open (and the rescan interval elapsed) rescans the
        /// scene once and retries.</summary>
        private void EvaluateZonesNow()
        {
            EvaluateZones();
            if (HasOpenBaseZone || Time.time < _nextZoneScanAt)
            {
                return;
            }

            ScanZones();
            EvaluateZones();
        }

        /// <summary>Opens base zone tracking for the next half second; expected to be called every
        /// frame (heartbeat pattern). Used by the round-based muster reporter, which needs the
        /// "am I in my base" answer while the player is ALIVE too.
        /// <para>Why an explicit request: the computation walks every <see cref="BaseZone"/> and
        /// rescans the scene each second when none is found — unconditional work nobody reads in
        /// the lobby and in base-less modes.</para></summary>
        public void RequestBaseTracking()
        {
            _baseTrackingUntil = Time.time + 0.5f;

            // ⚠️ Evaluate NOW, not on the next Update: on the frame tracking opens the flags would
            // still hold the cleared state (HasOpenBaseZone=false), which the caller reads as "no
            // base in the scene" and counts a player metres away as ready.
            EvaluateZonesNow();
        }

        /// <summary>Writes the mode's own status prompt (empty string clears). OVERRIDES the
        /// death/revive text, never the calibration warning.
        /// <para>⚠️ NEVER write mode-specific text in this class — no <c>if (modeId == …)</c> chain
        /// on the client (§10.5). This class states only what the RULE (<c>reviveAnchor</c>) says;
        /// what a <c>modeState</c> means is known only to the mode, which writes it here.</para>
        /// </summary>
        public void SetModePrompt(string prompt)
        {
            _modePrompt = prompt ?? "";
        }

        /// <summary>A base zone is OPEN to the player if its team matches, if it is
        /// <see cref="CoreTeam.Neutral"/> (open to all) or if the player has no team (teamless mode,
        /// §10.5). With several zones of the same team, ANY one of them counts.
        /// <para>A disabled component never counts as open: <c>BaseZone.Update</c> does not run so
        /// <c>IsPlayerInside</c> freezes, and the player could never revive — the server has no
        /// timer to compensate.</para></summary>
        private void EvaluateZones()
        {
            HasOpenBaseZone = false;
            IsInsideOwnBase = false;

            for (int i = 0; i < _zones.Length; i++)
            {
                BaseZone zone = _zones[i];
                if (zone == null || !zone.isActiveAndEnabled)
                {
                    continue;
                }

                if (zone.Team != this.Team && zone.Team != CoreTeam.Neutral && this.Team != CoreTeam.Neutral)
                {
                    continue;
                }

                HasOpenBaseZone = true;
                if (zone.IsPlayerInside)
                {
                    IsInsideOwnBase = true;
                    return;
                }
            }
        }

        // ------------------------------------------------------------ event handlers

        private void HandleConnected(WelcomeMsg msg)
        {
            _hasEverConnected = true;

            if (msg == null)
            {
                return;
            }

            PlayerId = msg.playerId;

            // Late join: sync from the state carried in welcome.
            string phase = msg.match != null && !string.IsNullOrEmpty(msg.match.phase) ? msg.match.phase : PhasePaused;
            if (msg.match != null)
            {
                PhaseReason = msg.match.phaseReason ?? "";
                ModeState = msg.match.modeState ?? "";
                if (!string.IsNullOrEmpty(msg.match.modeId))
                {
                    ModeId = msg.match.modeId;
                }
            }

            // ⚠️ Do NOT reset health/alive: welcome does not carry them, the roster will.
            ResetCombat(phase, resetHealth: false);
        }

        private void HandleDisconnected()
        {
            PlayerId = 0;
            PhaseReason = ArenaProtocol.PAUSE_REASON_LOBBY;
            ModeState = "";
            ResetCombat(PhasePaused);
        }

        /// <summary>OUR row in the roster: team + authoritative health/alive.
        /// <para>⚠️ Applying health/alive here is MANDATORY, not a preference: <c>welcome</c> does
        /// not carry them (§5.3), so a reconnecting client has no other source. Without it a player
        /// who dropped while dead comes back believing they are alive at full health — trigger
        /// works, rounds drain — while the server discards every <c>hit_report</c> as "shooter
        /// dead". The symptom is an undiagnosable "I hit him but he never dies".</para>
        /// <para>The roster is broadcast on every change and <c>status</c> reconciliation (§5.1)
        /// sends a full copy, so this is a REGULAR sync point, not a one-off repair.</para>
        /// </summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg == null || msg.players == null || PlayerId == 0)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info == null || info.playerId != PlayerId)
                {
                    continue;
                }

                // An empty team is applied too: in a teamless mode the server CLEARS teams
                // (§10.5), and keeping the old value would point the player at the wrong base.
                Team = ParseTeam(info.team);
                ApplyAuthoritativeState(info.hp, info.alive);
                return;
            }
        }

        /// <summary>Applies the health/alive the server reports. The local side of a death/revive
        /// decision goes through ONE path (<see cref="BeginDeath"/> / <see cref="SetAlive"/>); this
        /// method writes no second death logic, it only feeds that path.</summary>
        private void ApplyAuthoritativeState(float hp, bool alive)
        {
            // ⚠️ The roster fills a GAP, it never overrides the flow. Once health_update/respawn
            // has arrived that flow is authoritative: the two messages travel different paths
            // (tick outbox vs roster broadcast) so their order is not guaranteed, and a late roster
            // copy could undo a fresh death. The flag resets on every connection.
            if (_healthConfirmed)
            {
                return;
            }

            _healthConfirmed = true;
            SetHp(hp);

            if (alive)
            {
                if (!IsAlive)
                {
                    _awaitingRevive = false;
                    SetAlive(true);
                }

                return;
            }

            // Server says dead: the roster carries no delay, so the mode default is used. If we
            // are already dead BeginDeath does not overwrite the timer (non-authoritative call).
            BeginDeath(ModeRuntime.RespawnDelay, false);
        }

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            Team = ParseTeam(msg.yourTeam);
            ModeId = msg.modeId ?? "";

            // Scene loading: the match has NOT started (§5.3) — phase paused/loading. Fire off,
            // health full, death state clean.
            PhaseReason = ArenaProtocol.PAUSE_REASON_LOADING;
            ModeState = "";
            ResetCombat(PhasePaused);
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            Phase = PhasePaused;
            PhaseReason = ArenaProtocol.PAUSE_REASON_COUNTDOWN;
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.phase))
            {
                return;
            }

            Phase = msg.phase;
            PhaseReason = msg.phaseReason ?? "";
            ModeState = msg.modeState ?? "";
        }

        private void HandleHealthUpdate(HealthUpdateMsg msg)
        {
            if (msg == null || PlayerId == 0 || msg.playerId != PlayerId)
            {
                return;
            }

            // Authoritative flow started: the roster copy no longer touches health/alive
            // (rationale in ApplyAuthoritativeState).
            _healthConfirmed = true;
            SetHp(msg.hp);

            if (msg.hp > 0f)
            {
                if (!IsAlive)
                {
                    _awaitingRevive = false;
                    SetAlive(true);
                }

                return;
            }

            // hp ≤ 0: if no respawn message arrived yet, start death with the mode delay (the
            // server sends the same value in respawn.delaySeconds, so the two agree).
            BeginDeath(ModeRuntime.RespawnDelay, false);
        }

        private void HandleRespawn(RespawnMsg msg)
        {
            if (msg == null || PlayerId == 0 || msg.playerId != PlayerId)
            {
                return;
            }

            // No position change: respawn is only a STATE + delay notice (§10.4).
            _healthConfirmed = true;
            BeginDeath(msg.delaySeconds, true);
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            Phase = PhaseFinished;
            PhaseReason = "";
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg _)
        {
            PhaseReason = ArenaProtocol.PAUSE_REASON_LOBBY;
            ModeState = "";
            ResetCombat(PhasePaused);
        }

        // ----------------------------------------------------------------- helpers

        private void BeginDeath(float delaySeconds, bool authoritative)
        {
            float delay = Mathf.Max(0f, delaySeconds);

            if (IsAlive)
            {
                _reviveAt = Time.time + delay;
                _nextReviveSendAt = 0f;
                _awaitingRevive = true;
                _hasHoldAnchor = false; // the StandStill timer starts at death
                SetAlive(false);
                return;
            }

            // Already dead: only the server's respawn message may update the timer.
            if (authoritative)
            {
                _reviveAt = Time.time + delay;
                _nextReviveSendAt = 0f;
                _awaitingRevive = true;
            }
        }

        /// <summary>Local reset for match/scene transitions.
        /// <para>⚠️ <paramref name="resetHealth"/> is <c>true</c> ONLY on transitions the server
        /// also resets (<c>load_match</c>, <c>return_to_lobby</c>, disconnect). It is <c>false</c>
        /// on <c>welcome</c>, and that is critical: that message carries no health/alive (§5.3), so
        /// assuming "alive at full health" there is a GUESS that resurrects a player who dropped
        /// while dead. Unknown state is never guessed; it is learned from the roster
        /// (<see cref="ApplyAuthoritativeState"/>).</para></summary>
        private void ResetCombat(string phase, bool resetHealth = true)
        {
            Phase = string.IsNullOrEmpty(phase) ? PhasePaused : phase;
            _awaitingRevive = false;
            _nextReviveSendAt = 0f;
            _hasHoldAnchor = false;
            // Match/scene changed: the mode prompt is stale and the component that wrote it may
            // already be gone, so we do not leave the cleanup to it.
            _modePrompt = "";

            _healthConfirmed = resetHealth;
            if (resetHealth)
            {
                SetHp(ArenaProtocol.PLAYER_MAX_HP);
                SetAlive(true);
            }

            RefreshStatusText();
        }

        private void SetHp(float hp)
        {
            float clamped = Mathf.Clamp(hp, 0f, ArenaProtocol.PLAYER_MAX_HP);
            if (Mathf.Approximately(clamped, Hp))
            {
                return;
            }

            Hp = clamped;
            HpChanged?.Invoke(Hp);
        }

        private void SetAlive(bool alive)
        {
            if (IsAlive == alive)
            {
                return;
            }

            IsAlive = alive;
            AliveChanged?.Invoke(alive);
            LocalAliveChanged?.Invoke(alive);
        }

        /// <summary>Parses the protocol "red"/"blue" value; empty/unknown returns
        /// <see cref="CoreTeam.Neutral"/> so a teamless mode (§10.5) does not make the player think
        /// they are red.</summary>
        private static Team ParseTeam(string team)
        {
            if (string.Equals(team, "red", StringComparison.OrdinalIgnoreCase))
            {
                return CoreTeam.Red;
            }

            if (string.Equals(team, "blue", StringComparison.OrdinalIgnoreCase))
            {
                return CoreTeam.Blue;
            }

            return CoreTeam.Neutral;
        }
    }
}
