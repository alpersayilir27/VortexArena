using System;

namespace VortexArena.Protocol
{
    // All WS control DTOs (Docs/ArenaNet-Protokol.md §5) + the UDP beacon DTO (§4).
    // Rules: [Serializable], public fields only, no Dictionary/polymorphism, field names match the
    // protocol document's camelCase exactly.

    /// Envelope: the receiver parses only `type` first, then the full DTO.
    [Serializable]
    public class MsgEnvelope
    {
        public string type;
    }

    // ---- Client → Server ----

    [Serializable]
    public class HelloMsg
    {
        public string type = MessageTypes.Hello;
        public int protocolVersion;
        public string role;
        public string deviceId;
        public string deviceName;
        public string appVersion;
        public string currentScene;
        public string[] scenes;
    }

    [Serializable]
    public class StatusMsg
    {
        public string type = MessageTypes.Status;
        public string scene;
        public float battery;

        /// <summary>Left/right controller state (§5.1); <c>0</c> = not reported. ⚠️ A STATE, not a
        /// battery percentage (unreadable on Quest under OpenXR); <see cref="battery"/> is the HEADSET's.
        /// ⚠️ Unlike telemetry these DO trigger a roster broadcast, so they also live on
        /// <see cref="PlayerInfo"/>.</summary>
        public int ctrlL;

        /// <inheritdoc cref="ctrlL"/>
        public int ctrlR;

        public float fps;

        /// <summary>Last <see cref="LobbyStateMsg.version"/> the client APPLIED (§5.1); 0 = never → full
        /// snapshot. A backup net only: the control channel is TCP, so this covers windows where the
        /// client could not apply a broadcast (scene change, drop).</summary>
        public int rosterVersion;

        // ---- Net telemetry: the CLIENT measures (free there: RTT from the 0x06 echo, jitter/loss from
        // the incoming snapshot stream), the server only carries.
        // ⚠️ These three must NOT be added to PlayerInfo: they change constantly and would open
        // PlayerRegistry.UpdateStatus's "did a visible field change" gate on every status → dozens of
        // roster JSONs per second (fps is kept out for the same reason). Admins get net_stats.

        /// <summary>Round-trip time (ms) over the UDP state channel; <b>-1 = unknown</b> (0 would read
        /// as a real measurement).</summary>
        public int rttMs = -1;

        /// <summary>Mean deviation of snapshot arrival spacing from nominal (ms); -1 = unknown.</summary>
        public float jitterMs = -1f;

        /// <summary>Downlink snapshot loss percentage (from <c>serverTick</c> gaps); -1 = unknown.</summary>
        public float lossPct = -1f;
    }

    /// Player name and/or jersey number (§5.1). An empty string / 0 KEEPS the current value, so
    /// "change only the number" is a single message. Authority matches set_team: a player may only
    /// target their OWN playerId, an admin anyone's.
    [Serializable]
    public class SetIdentityMsg
    {
        public string type = MessageTypes.SetIdentity;
        public int playerId;
        public string name;
        public int number;
    }

    [Serializable]
    public class SetReadyMsg
    {
        public string type = MessageTypes.SetReady;
        public bool ready;
    }

    // ShotFiredMsg was REMOVED in v4 → UDP 0x03/0x04 (§6.4/6.5); 10 shots/s/player flooded the
    // authoritative WS channel.

    [Serializable]
    public class HitReportMsg
    {
        public string type = MessageTypes.HitReport;
        public int seq;
        public int targetPlayerId;
        public string weaponId;
        public float damage;
        public float[] hitPos;
    }

    /// A dead player's revive request (§10.4): sent once respawn.delaySeconds elapsed AND the player is
    /// in their own base; the server verifies. Carries no fields.
    [Serializable]
    public class ReviveRequestMsg
    {
        public string type = MessageTypes.ReviveRequest;
    }

    /// The headset reporting its OWN alignment state (§10.6). Players only. <c>source</c> ∈ "manual" |
    /// "anchor" | "cloud": a free, unvalidated label like weaponId, hence a string and not an enum.
    [Serializable]
    public class SetCalibrationMsg
    {
        public string type = MessageTypes.SetCalibration;
        public bool calibrated;
        public string source = "";

        /// <summary>Manual calibration only: how far the system's floor guess is from the real floor
        /// (m, signed); <c>0</c> when restored from an anchor. The server warns past
        /// <see cref="ArenaProtocol.CALIB_FLOOR_WARN_METERS"/> (§10.6). ⚠️ Not a gate — calibration is
        /// accepted whatever the offset.</summary>
        public float floorOffset;

        /// <summary>Why a saved alignment could NOT be reloaded; empty = fine. ⚠️ When set, the other
        /// three fields are IGNORED and the stored calibration is untouched — the reason only reaches
        /// admins and the roster (<see cref="PlayerInfo.calibrationError"/>). ⚠️ Free, unvalidated text:
        /// no error code list, a new failure kind must not create server work.</summary>
        public string error = "";
    }

    /// The headset reporting its OWN body scale (§10.8). Players only; <c>playerId</c> comes from the
    /// connection. The CLIENT measures; the server only clamps and broadcasts.
    [Serializable]
    public class SetBodyScaleMsg
    {
        public string type = MessageTypes.SetBodyScale;
        public float scale;

        /// <summary>Why the measurement failed; empty = success. ⚠️ When set, <see cref="scale"/> is
        /// IGNORED and the stored scale unchanged (§10.8). Free, unvalidated text; no error code
        /// list.</summary>
        public string error = "";
    }

    // ---- Admin → Server only ----

    /// start_match (§5.2). roundSeconds/scoreLimit/countdownSeconds apply to THAT match only: 0 or
    /// missing falls back to the mode default (or the protocol default for the countdown).
    [Serializable]
    public class StartMatchMsg
    {
        public string type = MessageTypes.StartMatch;
        public string modeId;
        public string sceneName;
        public int roundSeconds;

        /// <summary>Score/round limit: <c>&gt; 0</c> = that value, <c>0</c> = mode default,
        /// <see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/> = unlimited. ⚠️ Three-valued, so never
        /// test it with <c>≤ 0</c>.</summary>
        public int scoreLimit;

        /// <summary>Countdown length (s), clamped server-side; <c>0</c> = protocol default. Used for
        /// every countdown, including between rounds.</summary>
        public int countdownSeconds;
    }

    [Serializable]
    public class AbortMatchMsg
    {
        public string type = MessageTypes.AbortMatch;
    }

    /// <summary>Ends the match NORMALLY (§5.2): <c>finished</c> + <c>match_end</c> + result screen.
    /// ⚠️ Not <see cref="AbortMatchMsg"/>: abort skips both and drops straight to the lobby. The winner
    /// is decided by the CORE from the current scores, not by the mode.</summary>
    [Serializable]
    public class EndMatchMsg
    {
        public string type = MessageTypes.EndMatch;
    }

    /// <summary>Freezes a running match: <c>playing</c> → <c>paused</c>/<c>operator</c> (§5.2). Only
    /// acts while <c>playing</c>.</summary>
    [Serializable]
    public class PauseMatchMsg
    {
        public string type = MessageTypes.PauseMatch;
    }

    /// <summary>Resumes an operator-paused match (§5.2). Accepted only while
    /// <c>phaseReason == "operator"</c> — every pause is lifted by its own owner.</summary>
    [Serializable]
    public class ResumeMatchMsg
    {
        public string type = MessageTypes.ResumeMatch;
    }

    [Serializable]
    public class SetTeamMsg
    {
        public string type = MessageTypes.SetTeam;
        public int playerId;
        public string team;
    }

    [Serializable]
    public class KickMsg
    {
        public string type = MessageTypes.Kick;
        public int playerId;
    }

    /// An admin RESETS a player's calibration (§10.6); admins may only reset, never mark someone
    /// calibrated. Admin → server carries <c>playerId</c> (<c>0</c> = ALL); server → client forwards only
    /// <see cref="keepSaved"/>.
    /// <para>⚠️ Forwarded unconditionally: a half-finished manual calibration lives only on the headset
    /// and leaves no trace in the roster. ⚠️ The roster side is identical in both modes.</para>
    [Serializable]
    public class ClearCalibrationMsg
    {
        public string type = MessageTypes.ClearCalibration;
        public int playerId;

        /// <summary><b>Soft</b> (<c>true</c>): alignment invalidated, the device anchor + UUID KEPT so a
        /// following <c>reload_calibration</c> works. <b>Hard</b> (<c>false</c>): anchor deleted too.
        /// ⚠️ The default is HARD (a missing field reads <c>false</c>) so an old headset keeps today's
        /// behaviour.</summary>
        public bool keepSaved;
    }

    /// STARTS a reload of alignment from the saved anchor (§10.6). Admin → server carries
    /// <c>playerId</c> (<c>0</c> = ALL), server → client goes fieldless; the headset answers with
    /// <c>set_calibration</c>, relayed as <see cref="CalibrationResultMsg"/>.
    /// <para>⚠️ Unlike <see cref="MeasureBodyScaleMsg"/> an uncalibrated target is NOT skipped — that
    /// player is the point. ⚠️ It never sets <c>calibrated</c>; the headset writes the flag.</para>
    [Serializable]
    public class ReloadCalibrationMsg
    {
        public string type = MessageTypes.ReloadCalibration;
        public int playerId;
    }

    /// Starts a body measurement (§10.8). Admin → server carries <c>playerId</c> (<c>0</c> = ALL),
    /// server → client goes fieldless. The server only forwards; ⚠️ it does NOT forward to an
    /// uncalibrated player (the measurement is relative to the arena floor).
    [Serializable]
    public class MeasureBodyScaleMsg
    {
        public string type = MessageTypes.MeasureBodyScale;
        public int playerId;
    }

    /// Restarts body tracking on the headset (§6.11). Admin → server carries <c>playerId</c>
    /// (<c>0</c> = ALL), server → client goes fieldless. The server only forwards.
    /// ⚠️ Unlike <see cref="MeasureBodyScaleMsg"/> an UNCALIBRATED target is NOT skipped: the repair has
    /// nothing to do with arena alignment, and a calibration gate would disable the command exactly
    /// where it is needed.
    /// ⚠️ There is deliberately no reply message — see the type constant.
    [Serializable]
    public class RestartBodyTrackingMsg
    {
        public string type = MessageTypes.RestartBodyTracking;
        public int playerId;
    }

    /// The SHARED mode/map/duration/limit selection for the next match (§5.2). Does not start a match;
    /// broadcast to all admins via admin_state. Empty string or 0 keeps the current value.
    [Serializable]
    public class SetSelectionMsg
    {
        public string type = MessageTypes.SetSelection;
        public string modeId;
        public string sceneName;
        public int roundSeconds;

        /// <summary>Score/round limit: <c>&gt; 0</c> = that value, <c>0</c> = keep current,
        /// <see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/> = unlimited was selected. ⚠️ "Unlimited"
        /// is a choice, not an absence of one — hence the server gate asks <c>!= 0</c>.</summary>
        public int scoreLimit;

        /// <summary>Countdown length (s); <c>0</c> = keep current.</summary>
        public int countdownSeconds;
    }

    /// <summary>Friendly-fire switch (§5.2) — a <b>server session setting</b>, not a match parameter.
    /// ⚠️ No phase gate: it applies to a running match immediately (the operator must be able to fix the
    /// floor without aborting), starts <c>false</c> and is reset by nothing but the operator. Not a
    /// <c>set_selection</c> field because a <c>bool</c> cannot express "untouched".</summary>
    [Serializable]
    public class SetFriendlyFireMsg
    {
        public string type = MessageTypes.SetFriendlyFire;
        public bool enabled;
    }

    /// <summary>How headsets align AT STARTUP (§5.2/§10.6) — like friendly fire an immediate command,
    /// outside <c>set_selection</c> and the selection lock. Value is <c>ArenaProtocol.CALIB_MODE_*</c>.
    /// ⚠️ Unknown/empty is REJECTED (an operator decision, not a rule shape). ⚠️ It gates only the startup
    /// restore; the restore on MAP CHANGE always runs.</summary>
    [Serializable]
    public class SetCalibrationModeMsg
    {
        public string type = MessageTypes.SetCalibrationMode;
        public string mode = "";
    }

    // ---- Server → Client ----

    /// <summary>The SHAPE of a mode (§10.5) — SERVER-AUTHORITATIVE; reading rules from the wire is what
    /// keeps an "if (modeId == …)" chain from being born client-side. Values are strings on purpose:
    /// unknown/empty falls back to the default (team TDM), so a new value does not bump the version.</summary>
    [Serializable]
    public class ModeRulesInfo
    {
        /// <summary>"two" (red/blue) | "none" (teamless).</summary>
        public string teamMode = "two";

        /// <summary>"team" (match_state.scoreRed/scoreBlue) | "player" (PlayerInfo.score).</summary>
        public string scoring = "team";

        /// <summary>true = teammates can be hit (§10.3/4).</summary>
        public bool friendlyFire;

        /// <summary>"base" (enter your own BaseZone) | "standstill" (stand still), §10.4.</summary>
        public string reviveAnchor = "base";

        /// <summary>"weaponcanvas" (weapon placed in the scene) | "random" (mode grants) — purely client
        /// presentation. ⚠️ The old name was <c>"rack"</c>; anything but "random" falls back to the
        /// default, so both spellings behave correctly (§10.5).</summary>
        public string weaponSource = "weaponcanvas";

        /// <summary>respawn.delaySeconds; defaults to ArenaProtocol.RESPAWN_DELAY.</summary>
        public float respawnDelay = ArenaProtocol.RESPAWN_DELAY;

        /// <summary>May weapons fire while the phase is not <c>playing</c> (§10.5)? <c>true</c> = a free
        /// firing range like the lobby: flash/sound relayed, ⚠️ still <b>no damage</b> (the
        /// <c>hit_report</c> gate is always <c>playing</c>, §10.3).</summary>
        public bool fireWhilePaused;
    }

    /// <summary>Match state (§10.1). <b>Four fields, four owners:</b> <c>modeId</c> what is played,
    /// <c>phase</c> the core's state, <c>phaseReason</c> why it is paused, <c>modeState</c> the mode's
    /// own intermediate state.</summary>
    [Serializable]
    public class MatchInfo
    {
        /// <summary>ArenaProtocol.PHASE_* — <c>playing</c> is the ONLY phase that processes damage.</summary>
        public string phase;

        /// <summary>ArenaProtocol.PAUSE_REASON_* ; set only while <c>phase == paused</c>.</summary>
        public string phaseReason;

        public string modeId;

        /// <summary>The mode's own intermediate state (free string). <b>The core never interprets
        /// it</b>, only the HUD reads it; never a rule or damage gate (§10.1).</summary>
        public string modeState;

        public string sceneName;

        /// <summary>How long the current scene has been staged (§5.3); resets on scene change only. Its
        /// only consumer is scene ambience (a late joiner starts the music where everyone else is);
        /// missing → 0 → music from the top.</summary>
        public float sceneElapsed;

        public float timeRemaining;
        public int scoreRed;
        public int scoreBlue;

        /// <summary>Rule shape of the running match (§10.5) — late joiners bind to the same rules.</summary>
        public ModeRulesInfo rules;
    }

    [Serializable]
    public class WelcomeMsg
    {
        public string type = MessageTypes.Welcome;
        public int protocolVersion;
        public int playerId;
        public uint udpToken;

        /// <summary>Current calibration mode (§10.6). ⚠️ Pulled ONCE at connect: the decision it gates is
        /// already made by then, so there is NO live push to connected players.</summary>
        public string calibrationMode = "";

        public MatchInfo match;
    }

    [Serializable]
    public class PlayerInfo
    {
        public int playerId;
        public string name;

        /// <summary>Jersey number 1..99 (§2); 0 = unassigned, always 0 for admins. Names are NOT unique,
        /// so this is the distinguishing field ("7 · ertu").</summary>
        public int number;

        public string role;
        public string team;
        public bool ready;

        /// <summary>ArenaProtocol.CONNECTION_* (§2/§5.3); unknown/empty reads as <c>connected</c>, so a
        /// fourth state does not bump the version. ⚠️ There is NO "offline" state — a dropped device is
        /// either waited for or removed.</summary>
        public string connection = ArenaProtocol.CONNECTION_CONNECTED;

        /// <summary>Seconds until removal; meaningful only while <c>reconnecting</c> (0 = none). ⚠️ A
        /// countdown, not a timestamp: roster broadcasts are event-driven, so the UI must also tick it
        /// LOCALLY.</summary>
        public int reconnectSeconds;

        /// <summary>Is this record a participant of the running match (§10.2)? It defines the scope of the
        /// end-of-match table: a <c>left</c> row stays listed only because of this flag.</summary>
        public bool inMatch;

        public float battery;

        /// <summary>Left/right controller state (§5.1); always <c>0</c> on admin records. ⚠️ A STATE, not
        /// a battery percentage; <see cref="battery"/> is the HEADSET's. It rides the roster because it is
        /// discrete state, like the calibration fields.</summary>
        public int ctrlL;

        /// <inheritdoc cref="ctrlL"/>
        public int ctrlR;

        public string scene;

        // Match counters (§10.2) — SERVER-AUTHORITATIVE source of truth for the admin UI; counting only
        // kill_event/health_update would reset the table when an admin reconnects. Lobby phase:
        // hp=PLAYER_MAX_HP, alive=true, counters 0; player clients ignore them.
        public int kills;
        public int deaths;
        public float hp;
        public bool alive;

        /// <summary>INDIVIDUAL match score (§10.2) — NOT kills: written by IGameMode, per-mode in meaning,
        /// used where rules.scoring == "player". Team score stays in match_state.</summary>
        public int score;

        // Calibration state (§10.6) — device state, NOT a match counter: written by PlayerRegistry and
        // preserved across match resets. An uncalibrated player cannot fire, take damage or revive.
        // Always false/"" for admins.
        public bool calibrated;
        public string calibrationSource;

        /// <summary>Floor offset from the last MANUAL calibration (m, signed; §5.1/§10.6); <c>0</c> = none
        /// or clean. Rows past <see cref="ArenaProtocol.CALIB_FLOOR_WARN_METERS"/> are flagged in the UI;
        /// <c>clear_calibration</c> resets it.</summary>
        public float floorOffset;

        /// <summary>Why the last body measurement failed; empty = fine (§10.8). ⚠️ A successful measurement
        /// CLEARS it, or one failure would leave a permanent warning on the row.</summary>
        public string scaleError = "";

        /// <summary>Why the last saved-alignment reload failed; empty = fine (§10.6). ⚠️ A successful
        /// calibration CLEARS it (same reason as <see cref="scaleError"/>).</summary>
        public string calibrationError = "";

        /// <summary>Uniform body scale for the remote avatar (§10.8); <c>0</c> = not measured → readers
        /// apply <c>1</c>. ⚠️ It does NOT go on the skeleton channel, where it would be a constant
        /// repeated every frame.</summary>
        public float bodyScale;
    }

    [Serializable]
    public class LobbyStateMsg
    {
        public string type = MessageTypes.LobbyState;

        /// <summary>Monotonic roster version (§5.3), restarting at 0 per server lifetime. The client DROPS
        /// any message with <c>version &lt;= last applied</c> and resets on every welcome — a second
        /// safety net stopping a stale snapshot from overwriting a newer one.</summary>
        public int version;

        public PlayerInfo[] players;
    }

    [Serializable]
    public class LoadMatchMsg
    {
        public string type = MessageTypes.LoadMatch;
        public string modeId;
        public string sceneName;
        public int roundSeconds;

        /// <summary>The EFFECTIVE score/round limit (already resolved, so "0 = default" no longer
        /// applies): <c>&gt; 0</c> or <see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/>.</summary>
        public int scoreLimit;

        public string yourTeam;

        /// <summary>How long the scene has been staged; 0 while a new one is being staged.</summary>
        public float sceneElapsed;

        /// <summary>Rule shape of this match (§10.5); the client configures itself from THIS.</summary>
        public ModeRulesInfo rules;
    }

    [Serializable]
    public class CountdownMsg
    {
        public string type = MessageTypes.Countdown;
        public int seconds;
    }

    [Serializable]
    public class MatchStateMsg
    {
        public string type = MessageTypes.MatchState;

        /// <summary>ArenaProtocol.PHASE_* (§10.1).</summary>
        public string phase;

        /// <summary>ArenaProtocol.PAUSE_REASON_* ; set only while <c>phase == paused</c>.</summary>
        public string phaseReason;

        /// <summary>The mode's own intermediate state; the core never interprets it (§10.1).</summary>
        public string modeState;

        public float timeRemaining;
        public int scoreRed;
        public int scoreBlue;
    }

    /// <summary>Health change (§10.3). ⚠️ <b>NOT a broadcast</b> — only to <c>playerId</c>'s owner and to
    /// admins; broadcasting made this the only WS channel growing with the SQUARE of the player count
    /// (Docs/Sistem-Ozeti.md §3.12).</summary>
    [Serializable]
    public class HealthUpdateMsg
    {
        public string type = MessageTypes.HealthUpdate;
        public int playerId;
        public float hp;

        /// <summary>The attacking player; <c>0</c> = not an attack (revive / environmental damage).
        /// <para>Read by the client's directional damage indicator: it looks the attacker's pose up in
        /// the snapshot registry, so the bearing costs NOTHING on the wire — do not add a position or
        /// direction field here for it.</para></summary>
        public int attackerId;
    }

    [Serializable]
    public class KillEventMsg
    {
        public string type = MessageTypes.KillEvent;
        public int killerId;
        public int victimId;
        public string weaponId;
    }

    [Serializable]
    public class RespawnMsg
    {
        public string type = MessageTypes.Respawn;
        public int playerId;
        public float delaySeconds;
    }

    /// The winner comes through ONE of two channels (rules.scoring, §10.5): winnerTeam in team-scored
    /// modes, winnerPlayerId (0 = none/draw) otherwise. A mode never fills both.
    [Serializable]
    public class MatchEndMsg
    {
        public string type = MessageTypes.MatchEnd;
        public string winnerTeam;
        public int winnerPlayerId;
        public int scoreRed;
        public int scoreBlue;
    }

    /// <summary>Return to lobby (§10.7); shaped like <see cref="LoadMatchMsg"/>. An empty
    /// <c>sceneName</c> falls back to the client's own shell <c>Lobby</c> scene. <c>modeId</c> is
    /// <c>"lobby"</c>, which is NOT a registered match mode (§10.5).</summary>
    [Serializable]
    public class ReturnToLobbyMsg
    {
        public string type = MessageTypes.ReturnToLobby;
        public string modeId;
        public string sceneName;

        /// <summary>How long the scene has been staged; 0 while a new one is being staged.</summary>
        public float sceneElapsed;

        /// <summary>Rule shape of the lobby profile (§10.5).</summary>
        public ModeRulesInfo rules;
    }

    [Serializable]
    public class PingMsg
    {
        public string type = MessageTypes.Ping;
    }

    [Serializable]
    public class KickedMsg
    {
        public string type = MessageTypes.Kicked;
        public string reason;
    }

    /// Admin connections only (§5.3): the single source of truth for state SHARED between admins —
    /// selection, a "<name>: <action>" notice of the last admin action, online admin count.
    /// ⚠️ Per-screen view preferences (camera, rings, transparency…) do NOT belong here.
    [Serializable]
    public class AdminStateMsg
    {
        public string type = MessageTypes.AdminState;
        public string modeId;
        public string sceneName;

        /// <summary>Shared parameters for the next match; 0 = never chosen (mode default).</summary>
        public int roundSeconds;

        /// <summary>Shared score/round limit; <c>0</c> = never chosen,
        /// <see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/> = unlimited.</summary>
        public int scoreLimit;

        /// <summary>Countdown length (s); 0 = never chosen (COUNTDOWN_SECONDS).</summary>
        public int countdownSeconds;

        /// <summary>CURRENT friendly-fire value (§5.2): effective state, not a selection, so it is outside
        /// the "0 = not chosen" contract.</summary>
        public bool friendlyFire;

        /// <summary>CURRENT calibration mode (§5.2), like <see cref="friendlyFire"/>. ⚠️ Players do NOT
        /// get it from here — they read it once from <see cref="WelcomeMsg.calibrationMode"/>.</summary>
        public string calibrationMode = "";

        public string notice;
        public int adminCount;

        /// <summary>The venue opened for this session (§11) — fixed while running; empty when there is no
        /// venue split.</summary>
        public string venueId;

        /// <summary>Scene names playable in this venue. ⚠️ The admin map picker filters its local catalogue
        /// with THIS — the catalogue knows the whole project. Empty = no filtering.</summary>
        public string[] venueScenes;
    }

    /// <summary>The rule shape of the running match CHANGED (§5.3) — broadcast to <b>everyone</b>. Its
    /// only trigger today is the friendly-fire switch, since rules otherwise arrive with
    /// <c>welcome</c>/<c>load_match</c>. ⚠️ Unlike <see cref="SelectionStateMsg"/> this IS a real rule
    /// message and is applied to <c>ModeRuntime</c>.</summary>
    [Serializable]
    public class RulesUpdateMsg
    {
        public string type = MessageTypes.RulesUpdate;

        /// <summary>The currently valid kind — <c>ModeRuntime</c> stores rules with the mode, so the
        /// message carries both.</summary>
        public string modeId;

        public ModeRulesInfo rules;
    }

    /// <summary>The only PRESENTATION field of the selected mode (§5.3) — goes to <b>everyone</b>.
    /// ⚠️ <b>NOT a rule message:</b> never applied to <c>ModeRuntime</c>; active rules still come from
    /// <c>load_match</c>/<c>welcome</c>/<c>return_to_lobby</c>. It only describes the shape of a match
    /// that has not started, and its sole consumer is <c>BaseZoneVisibility</c> (§10.7).</summary>
    [Serializable]
    public class SelectionStateMsg
    {
        public string type = MessageTypes.SelectionState;

        /// <summary>The shared selection ("lobby" at startup). ⚠️ Does NOT change the match kind — that
        /// waits for `start_match`.</summary>
        public string modeId;

        /// <summary>"two" | "none" — team mode of the selected mode (§10.5); "two" if unknown.</summary>
        public string teamMode;
    }

    /// <summary>One player's net telemetry (an entry of <see cref="NetStatsMsg"/>). Values come from the
    /// CLIENT (<see cref="StatusMsg"/>), the server only carries them; -1 = unknown.</summary>
    [Serializable]
    public class NetStatsEntry
    {
        public int playerId;
        public int rttMs = -1;
        public float jitterMs = -1f;
        public float lossPct = -1f;
    }

    /// <summary>To <c>role=admin</c> connections only, at 1 Hz: ping/jitter/loss per player.
    /// <para>⚠️ <b>NOT broadcast</b> — that would produce a fan-out growing with the square of the player
    /// count, exactly the problem this telemetry measures. ⚠️ Deliberately outside the roster: per-second
    /// telemetry would spin the roster <c>version</c> and break reconciliation (§5.1); losing this
    /// message is harmless. ⚠️ <b>No bandwidth/byte field and none will be added</b> — the number an
    /// operator can act on is ping.</para></summary>
    [Serializable]
    public class NetStatsMsg
    {
        public string type = MessageTypes.NetStats;
        public NetStatsEntry[] players;
    }

    /// <summary>The START or END of a single physical violation (§5.3/§10.9). To <c>role=admin</c>
    /// connections only, like <see cref="HealthUpdateMsg"/>. <b>The SERVER is the source</b>, not edge
    /// detection in the admin client: one list, one clock, one ledger.
    /// <para>⚠️ <b>Edge-triggered</b>, not repeated every tick like the snapshot bits, so a lost message
    /// is only a LOG loss (the ring feeds off the snapshot bit). ⚠️ Contacts shorter than
    /// <see cref="ArenaProtocol.VIOLATION_MIN_SECONDS"/> are never reported.</para></summary>
    [Serializable]
    public class ViolationMsg
    {
        public string type = MessageTypes.Violation;
        public int playerId;

        /// <summary><c>ArenaProtocol.VIOLATION_KIND_*</c>.</summary>
        public string kind;

        /// <summary><c>true</c> = violation started, <c>false</c> = ended.</summary>
        public bool active;

        /// <summary>Duration (s) when <c>active == false</c>; <c>0</c> while it is <c>true</c>.</summary>
        public float seconds;

        /// <summary>The player's count of violations OF THIS KIND in this match.</summary>
        public int count;

        /// <summary>Total time of the same kind (s) — end-of-match statistic.</summary>
        public float totalSeconds;
    }

    /// <summary>The ANSWER to the operator's <see cref="ReloadCalibrationMsg"/> button (§5.3/§10.6). To
    /// <c>role=admin</c> connections only.
    /// <para>⚠️ <b>An EVENT, not state</b> — state lives in the roster; this only answers "what happened
    /// to the button I just pressed". ⚠️ <b>The server keeps NO pending-request ledger:</b> every
    /// <c>set_calibration</c> produces a result line and the ADMIN UI matches it to a button.</para>
    /// <para>It does not ride <c>lobby_state</c> because a successful reload on an already-calibrated
    /// player changes no roster field (broadcast guard, §5.3) — the button would hang on "loading".</para>
    /// </summary>
    [Serializable]
    public class CalibrationResultMsg
    {
        public string type = MessageTypes.CalibrationResult;
        public int playerId;

        /// <summary><c>true</c> = the headset restored alignment from the saved anchor.</summary>
        public bool ok;

        /// <summary>Human-readable reason when <c>ok == false</c>; free, unvalidated text.</summary>
        public string error = "";
    }

    // ---- UDP beacon (§4; not a WS message, the receiver validates the app field) ----

    [Serializable]
    public class BeaconMsg
    {
        public string app;
        public int ver;
        public string ip;
        public int controlPort;
        public int statePort;
        public string serverId;
    }
}
