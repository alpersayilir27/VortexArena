namespace VortexArena.Protocol
{
    /// Protocol constants — single source of truth: Docs/ArenaNet-Protokol.md §1.
    public static class ArenaProtocol
    {
        /// <summary>
        /// Wire version. A mismatch does NOT reject the connection (warning only), so every bump needs
        /// a full APK round on all headsets. Per-version notes: Docs/ArenaNet-Protokol.md §1.
        /// <para>⚠️ v18 (object ownership, poses, events, dynamic spawn — §10.10) BREAKS THE WIRE
        /// LAYOUT: the <c>0x05</c> header grew from 7 B to 8 B (<c>objectCount</c>), so an old client
        /// misaligns the WHOLE datagram and loses the snapshot itself, not just objects — remote players
        /// teleport to garbage poses.</para>
        /// <para>⚠️ v17 (network objects, §10.10) is additive on the wire but NOT cosmetic when mixed:
        /// an old client sends no <c>targetNetId</c>, so it can break nothing, and it ignores
        /// <c>object_state</c>, so it keeps seeing a cover others already broke — two players on either
        /// side of the same wall see different worlds.</para>
        /// <para>v16 <c>identify</c> REMOVED (both directions) · v15 <c>clear_calibration.keepSaved</c>
        /// (§5.2/§10.6) · v14 out-of-bounds bit + admin
        /// <c>violation</c> (§5.3/§6.3) · v13 calibration mode + diagnostics (§5.2/§10.6/§10.8) ·
        /// v12-v9 obstacle bit, controller state, server→client <c>clear_calibration</c>, body scale —
        /// all additive.</para>
        /// <para>⚠️ v8 is breaking because <c>PlayerInfo.online</c> was REMOVED: an old client reads it
        /// as <c>false</c> and draws the whole roster offline (§2/§5.3/§10.2).</para>
        /// <para>⚠️ v7 is breaking through MEANING, not layout: arena space became world space (§3), so
        /// mixed versions see each other metres away.</para>
        /// <para>⚠️ v6 added skeleton streaming (<c>0x07</c>/<c>0x08</c>); <c>0x01 PoseUpdate</c> is NOT
        /// removed and never will be — weapon pose, item bytes and hit reports feed off raw hand poses
        /// (§6.2). A client that ignores <c>0x07</c> draws no remote bodies at all.</para>
        /// <para>v5 net telemetry + packet combining (<c>0x05</c>) · v4 held item on the wire, shot
        /// events moved to UDP · v3 phase machine · v2 <c>set_identity</c>.</para>
        /// </summary>
        public const int PROTOCOL_VERSION = 18;
        public const string APP_ID = "VortexArena";

        // ---- Calibration mode (§5.2/§10.6): how headsets align AT STARTUP. ----
        // ⚠️ Unknown/empty is REJECTED here (unlike rule values, §10.5): an operator decision must not
        // silently fall back to a default.

        /// <summary>Manual 2-anchor calibration on every app start; the saved anchor UUID is never
        /// read. Server default. ⚠️ The mode gates STARTUP restore only — the in-session restore on map
        /// change always runs (§10.6).</summary>
        public const string CALIB_MODE_TWO_ANCHOR = "two_anchor";

        /// <summary>Headset restores alignment from the saved <c>OVRSpatialAnchor</c> on start.</summary>
        public const string CALIB_MODE_SAVED_ANCHOR = "saved_anchor";

        /// <summary>Shared spatial anchor — <b>reserved</b>. The server REJECTS it; the UI shows it
        /// disabled.</summary>
        public const string CALIB_MODE_ANCHOR_CLOUD = "anchor_cloud";

        /// <summary>Warn admins when |<c>set_calibration.floorOffset</c>| exceeds this (metres, §10.6);
        /// the fix is clearing space data in the headset.
        /// <para>⚠️ Diagnostic threshold, not a gate: calibration is always accepted and the server
        /// never auto-corrects (that would be a second alignment authority).</para></summary>
        public const float CALIB_FLOOR_WARN_METERS = 0.5f;

        /// <summary>Clamp range for <c>set_body_scale.scale</c> (§10.8): the client measures but the
        /// result hits everyone's screen, so the server clamps. <c>0</c> is OUTSIDE this range and
        /// means "not measured" → readers apply <c>1</c>.</summary>
        public const float BODY_SCALE_MIN = 0.5f;

        /// <inheritdoc cref="BODY_SCALE_MIN"/>
        public const float BODY_SCALE_MAX = 1.6f;

        /// <summary>Jersey number range (§2); <c>0</c> = unassigned (always 0 for admins). Unique
        /// across all registered devices.</summary>
        public const int PLAYER_NUMBER_MIN = 1;
        public const int PLAYER_NUMBER_MAX = 99;

        public const int UDP_BEACON_PORT = 47820;
        public const int CONTROL_PORT = 47821;
        public const int STATE_PORT = 47822;
        public const string WS_PATH = "/ws";

        // Intervals/timeouts in seconds.
        public const float BEACON_INTERVAL = 2f;
        public const float DISCOVERY_TIMEOUT = 5f;
        public const float STATUS_INTERVAL = 5f;

        /// <summary>Socket declared dead after this long without status (§1/§8). ⚠️ Does not mean
        /// "player left": the device only drops to <see cref="CONNECTION_RECONNECTING"/>; removal is
        /// <see cref="RECONNECT_GRACE"/>'s call.</summary>
        public const float HEARTBEAT_TIMEOUT = 15f;

        /// <summary>How long a disconnected device is waited for (§2). On expiry the player is removed;
        /// a match participant's record stays as <see cref="CONNECTION_LEFT"/> until the match ends
        /// (§10.2), otherwise it is deleted and the <c>playerId</c> returns to the pool.
        /// <para>⚠️ This is when the SERVER drops the record, not when the client gives up — retry is
        /// infinite (§8). Drop to removal = <see cref="HEARTBEAT_TIMEOUT"/> + this.</para></summary>
        public const float RECONNECT_GRACE = 45f;

        public const float HELLO_TIMEOUT = 10f; // §8: a connection without hello is closed after this

        // Reconnect backoff sequence; last element is the ceiling.
        public static readonly float[] RECONNECT_BACKOFF = { 1f, 2f, 5f };

        public const int POSE_RATE_HZ = 20;
        public const int SNAPSHOT_RATE_HZ = 20;
        public const int INTERP_DELAY_MS = 100;

        /// <summary>Skeleton blob send rate (§6.9) — lower than the pose channel because the bottleneck
        /// is datagram count, not bandwidth. It still looks smooth because the receiver runs the SDK's
        /// own interpolation, so raising it buys packets, not smoothness.</summary>
        public const int SKELETON_RATE_HZ = 12;

        /// <summary>Max skeleton blob size for one player (§6.9). Safety, not budget: with the
        /// <c>0x07</c> header it must fit one datagram, because this channel has NO fragmentation.
        /// <para>⚠️ An oversized blob is NOT sent (warns once); fix it by tightening the joint list.</para></summary>
        public const int SKELETON_MAX_BLOB_BYTES = 1024;

        /// <summary>Max entries per <c>0x08</c> datagram (§6.10). ⚠️ The real limit is the byte budget
        /// (<see cref="COMBINED_MAX_BYTES"/>); overflow spills into another datagram in the same tick
        /// and each entry applies independently (no reassembly).</summary>
        public const int SKELETON_MAX_ENTRIES_PER_PACKET = 16;

        /// <summary><c>playerId</c> ceiling. <b>Wire-format limit, NOT a product quota</b> — playerId is
        /// a <c>u8</c> (0 reserved). Concurrent player/admin counts have no other limit.</summary>
        public const int PLAYER_ID_MAX = 255;

        /// <summary>Network id range of a SCENE object (<c>NetIdentity.sceneId</c>, §10.10); <c>0</c> =
        /// unassigned and never addressable. Enforced at scene save by <c>SceneIdGuard</c>.
        /// <para>⚠️ The upper half (<c>32768..65535</c>) is RESERVED for ids the server will allocate at
        /// runtime for dynamic objects. A scene bake spilling into it would give a scene object and a
        /// dynamic object the same id, and the server could not tell which one it damaged.</para></summary>
        public const int NET_ID_SCENE_MIN = 1;

        /// <inheritdoc cref="NET_ID_SCENE_MIN"/>
        public const int NET_ID_SCENE_MAX = 32767;

        /// <summary>Network id range the server allocates at RUNTIME for dynamic objects (§10.10) — the
        /// half above the scene range, so the two can never collide. Ids return to a pool, but a
        /// despawned id is NOT reissued before the round ends: an in-flight <c>object_event</c> or pose
        /// packet would land on the object that inherited the id, and nobody would see an error.</summary>
        public const int NET_ID_DYNAMIC_MIN = 32768;

        /// <inheritdoc cref="NET_ID_DYNAMIC_MIN"/>
        public const int NET_ID_DYNAMIC_MAX = 65535;

        /// <summary>How often the OWNER streams an object pose (<c>0x09</c>, §6.12) — half of
        /// <see cref="POSE_RATE_HZ"/>. The reason is packet count, not bandwidth
        /// (<c>Docs/Sistem-Ozeti.md</c> §3.12); object poses also flow only for an AWAKE and UNHELD
        /// object, so a typical tick carries none.</summary>
        public const int OBJECT_POSE_RATE_HZ = 10;

        /// <summary>Speed threshold (m/s) under which a released object counts as stopped (§10.10);
        /// staying under it for <see cref="OBJECT_REST_SECONDS"/> makes the owner send
        /// <c>object_rest{pos,rot}</c>, which ends ownership.</summary>
        public const float OBJECT_REST_SPEED = 0.05f;

        /// <summary>Uninterrupted time under <see cref="OBJECT_REST_SPEED"/> before "stopped".
        /// <para>⚠️ A single frame of "I stopped" is NOT enough: speed hits zero at the apex of a bounce,
        /// and an object released there would freeze in mid-air.</para></summary>
        public const float OBJECT_REST_SECONDS = 0.3f;

        /// <summary>Max object entries in the object section of one <c>0x05</c> datagram (§6.8). ⚠️ The
        /// real gate is the byte budget (<see cref="COMBINED_MAX_BYTES"/>) — 8 + 16×88 + 16×30 = 1896 B
        /// exceeds the MTU; this number is the <c>u8</c> ceiling of <c>objectCount</c> and a safety
        /// net.</summary>
        public const int OBJECT_MAX_ENTRIES_PER_PACKET = 16;

        // ---- object_state.flags core bits (§10.10). Positions are FIXED and never renumbered: a
        // shifted bit does not fail, it draws the wrong object broken. Bit4+ is per kind. ----

        /// <summary>Held by a player: the pose comes from <c>owner</c>'s hand pose, <c>pos</c>/<c>rot</c>
        /// are not read.</summary>
        public const int OBJECT_FLAG_HELD = 1 << 0;

        /// <summary>Broken: the client disables the collider and switches to the broken presentation.</summary>
        public const int OBJECT_FLAG_BROKEN = 1 << 1;

        /// <summary>Moving: the owner streams <c>0x09</c> poses (§6.12), the remote side
        /// interpolates.</summary>
        public const int OBJECT_FLAG_AWAKE = 1 << 2;

        /// <summary>The holding hand is the RIGHT one (clear = left). Meaningless without
        /// <see cref="OBJECT_FLAG_HELD"/>.</summary>
        public const int OBJECT_FLAG_HELD_RIGHT = 1 << 3;

        // ---- Kind rule values from maps.json `kinds[]` (§10.10/§11). Carried as strings; an
        // empty/unknown value reads as the restrictive default (grab "none", no events). ----

        /// <summary>Default: the object cannot be picked up.</summary>
        public const string OBJECT_GRAB_NONE = "none";

        /// <summary>A free object goes to the first asker (no stealing, §10.10).</summary>
        public const string OBJECT_GRAB_ANYONE = "anyone";

        /// <summary><c>events[].policy</c>: anyone may raise the event.</summary>
        public const string OBJECT_EVENT_POLICY_ANYONE = "anyone";

        /// <summary><c>events[].policy</c>: only the object's owner may raise the event.</summary>
        public const string OBJECT_EVENT_POLICY_OWNER = "owner";

        /// <summary><c>events[].phaseGate</c>: accepted only while <see cref="PHASE_PLAYING"/>.</summary>
        public const string OBJECT_PHASE_GATE_PLAYING = "playing";

        /// <summary><c>events[].phaseGate</c>: accepted in every phase (lobby included).</summary>
        public const string OBJECT_PHASE_GATE_ANY = "any";

        /// <summary>Close-frame reason used when kicking (§5.4). Second line of defence: if the `kicked`
        /// JSON loses the race with the close, the client would reconnect by itself.</summary>
        public const string KICK_CLOSE_REASON = "kicked";

        /// <summary>Max entries per snapshot datagram; overflow spills into extra datagrams in the same
        /// tick (§6.3, 1414 B &lt; MTU). Each packet applies its own entries independently.</summary>
        public const int SNAPSHOT_MAX_ENTRIES_PER_PACKET = 16;

        /// <summary>Max events per <c>0x04</c> EventBatch datagram (§6.5, 1158 B &lt; MTU).
        /// <para>⚠️ Overflow shifts to the next tick's batch instead of being dropped: "one batch per
        /// tick" is what the client's duplicate guard (keyed on <c>serverTick</c>) relies on.</para></summary>
        public const int EVENT_MAX_ENTRIES_PER_PACKET = 128;

        /// <summary>Ticks remembered for <c>0x04</c> duplicate rejection (§6.5); only exact repeats are
        /// dropped. ⚠️ <c>0x05</c> (§6.8) uses the SAME ring — a separate one would replay a tick twice
        /// (double tracer + double sound).</summary>
        public const int EVENT_TICK_HISTORY = 64;

        /// <summary>Size cap of the combined <c>0x05</c> datagram (§6.8); over it the server falls back
        /// to <c>0x02</c>+<c>0x04</c>. Chosen under MTU 1500 because combining is an optimisation, not
        /// worth a fragmentation risk.</summary>
        public const int COMBINED_MAX_BYTES = 1200;

        /// <summary><c>modeId</c> of the lobby kind (§10.7). <b>NOT a registered match mode</b> — no
        /// <c>IGameMode</c> on the server, so <c>start_match</c> cannot start it (that is where the "no
        /// match starts in lobby" rule comes from). The client resolves loadout, HUD and
        /// <c>rules.fireWhilePaused</c> from it.</summary>
        public const string LOBBY_MODE_ID = "lobby";

        // ---- Connection state (§2/§5.3). Carried as a string; unknown/empty reads as CONNECTED so a
        // fourth state later does not bump PROTOCOL_VERSION.
        // ⚠️ There is NO "offline" value and none will be added: a dropped device is either waited for
        // (reconnecting) or removed (left). ----

        /// <summary>Socket alive; the player passes every match gate.</summary>
        public const string CONNECTION_CONNECTED = "connected";

        /// <summary>Socket dropped, awaited for <see cref="RECONNECT_GRACE"/>. The record stays but
        /// passes NO match gate (not waited for, not hittable, not revivable, not in snapshots).</summary>
        public const string CONNECTION_RECONNECTING = "reconnecting";

        /// <summary>Grace expired, player removed. The record survives only while it belongs to the
        /// running match (<c>PlayerInfo.inMatch</c>), until that match ends (§10.2).</summary>
        public const string CONNECTION_LEFT = "left";

        // ---- Controller state (§5.1/§5.3): ctrlL/ctrlR on status and PlayerInfo. ----
        // ⚠️ A STATE, not a percentage: controller battery is unreadable on Quest under OpenXR, and an
        // unreadable number would show on site as "0% but the controller works". PlayerInfo.battery is
        // the HEADSET's battery.

        /// <summary>Not reported: an admin record, or a client that omits the field. ⚠️ <c>0</c>
        /// deliberately means "unknown" — an unset JSON <c>int</c> is <c>0</c>, so treating it as
        /// healthy would mark every silent record healthy.</summary>
        public const int CONTROLLER_UNKNOWN = 0;

        /// <summary>Connected and tracked — the hand pose is a measurement.</summary>
        public const int CONTROLLER_OK = 1;

        /// <summary>Connected but the pose is invalid (out of view / asleep).</summary>
        public const int CONTROLLER_UNTRACKED = 2;

        /// <summary>Not connected. The sender keeps the last valid hand relative to the head and marks
        /// the pose stale with <c>FLAG_HAND_*_STALE</c> (§6.3).</summary>
        public const int CONTROLLER_LOST = 3;

        // ---- Phase values (§10.1). Carried as a string; unknown reads as PAUSED. ----

        /// <summary>Match not running: lobby, loading, countdown, pause. Damage OFF.</summary>
        public const string PHASE_PAUSED = "paused";

        /// <summary>Match running. <b>The ONLY phase that processes damage</b> (§10.3).</summary>
        public const string PHASE_PLAYING = "playing";

        /// <summary>Match over, scores final. Damage OFF.</summary>
        public const string PHASE_FINISHED = "finished";

        // ---- phaseReason values (§10.1): only set while PHASE_PAUSED. ----

        /// <summary>Lobby kind loaded, no match set up.</summary>
        public const string PAUSE_REASON_LOBBY = "lobby";

        /// <summary>Scene loading gate: waiting for every player's set_ready.</summary>
        public const string PAUSE_REASON_LOADING = "loading";

        /// <summary>Countdown (COUNTDOWN_SECONDS).</summary>
        public const string PAUSE_REASON_COUNTDOWN = "countdown";

        /// <summary>Operator paused a running match.</summary>
        public const string PAUSE_REASON_OPERATOR = "operator";

        /// <summary>The mode asked for a pause; its reason is in <c>modeState</c>.</summary>
        public const string PAUSE_REASON_MODE = "mode";

        // ---- Match flow + combat (Docs/ArenaNet-Protokol.md §10) ----

        /// <summary>Full player health; server-authoritative (health_update cannot exceed it).</summary>
        public const float PLAYER_MAX_HP = 100f;

        /// <summary>Grace inside an obstacle before health drains (s, §10.9). The screen is already
        /// black during it, so what is free is health, not vision — hence it can be generous.
        /// <para>⚠️ Fully resets on leaving (no partial decay): re-entering blinds the player again.</para></summary>
        public const float OBSTACLE_GRACE_SECONDS = 1f;

        /// <summary>Time from FULL health to death after the grace (s, §10.9). ⚠️ A wounded player dies
        /// sooner: the drain is a RATE, not a fixed countdown.</summary>
        public const float OBSTACLE_DRAIN_SECONDS = 5f;

        /// <summary>Health lost per second on obstacle violation (§10.9), applied on the server's own
        /// tick. ⚠️ Derived from the two durations above, not hand-tuned. ⚠️ All three are consumed
        /// ONLY by the server: changing them needs a server build, not a new APK.</summary>
        public const float OBSTACLE_DAMAGE_PER_SECOND = 13f;

        /// <summary>Max time revival stays blocked inside an obstacle (s, §10.9/§10.4). The ceiling
        /// exists because the gate trusts a client flag: a client that keeps claiming "I'm in an
        /// obstacle" could keep a player dead forever (<see cref="OBSTACLE_FLAG_STALE_MS"/> only
        /// handles a SILENT one). The penalty restarts immediately, so the rule keeps its teeth.</summary>
        public const float OBSTACLE_REVIVE_BLOCK_SECONDS = 40f;

        /// <summary>Clear <see cref="SnapshotEntry.FLAG_IN_OBSTACLE"/> and stop the penalty when it is
        /// not refreshed within this (ms, §10.9). It exists because the flag carries state: a frozen
        /// client's last packet would say "in the wall" forever.</summary>
        public const int OBSTACLE_FLAG_STALE_MS = 300;

        /// <summary><c>kill_event.weaponId</c> tag for obstacle deaths (§10.9; <c>killerId</c> is 0), so
        /// the kill feed can tell it apart from a real kill. ⚠️ <c>weaponId</c> is a free, unvalidated
        /// label (§10.3); this constant only keeps both ends writing the same string.</summary>
        public const string WEAPON_ID_OBSTACLE = "obstacle";

        // ---- Violation feed (§5.3/§10.9): `kind` values. Carried as a string; an unknown kind is
        // shown raw rather than dropped. ----

        /// <summary>Head inside an inner obstacle — the only kind that carries a penalty.</summary>
        public const string VIOLATION_KIND_OBSTACLE = "obstacle";

        /// <summary>Head outside the boundary's safe area. ⚠️ <b>No penalty</b>: a player whose
        /// calibration drifted a few centimetres must not die for it, so the rule stays a visibility
        /// signal and the operator decides.</summary>
        public const string VIOLATION_KIND_OUT_OF_BOUNDS = "out_of_bounds";

        /// <summary>Contacts shorter than this never enter the violation feed (s, §10.9) — a player
        /// oscillating on the line would produce three rows a second. ⚠️ Feed-only: the ring (snapshot
        /// bit) and the obstacle penalty run from the first frame.</summary>
        public const float VIOLATION_MIN_SECONDS = 0.5f;

        /// <summary>DEFAULT countdown length (whole seconds). Overridable per match via
        /// start_match.countdownSeconds (§5.2); also the between-round countdown in round-based
        /// modes.</summary>
        public const int COUNTDOWN_SECONDS = 5;

        /// <summary>Clamp range for start_match.countdownSeconds (§5.2). <b>Server-enforced, not a UI
        /// list:</b> 1 s leaves no time to take position, over 30 s is dead time in a round-based
        /// mode.</summary>
        public const int COUNTDOWN_SECONDS_MIN = 5;

        /// <inheritdoc cref="COUNTDOWN_SECONDS_MIN"/>
        public const int COUNTDOWN_SECONDS_MAX = 30;

        /// <summary>End phase: match_end to the automatic return_to_lobby. ⚠️ A safety net, not part of
        /// the flow: the winner screen is meant to be closed by the operator's map/lobby choice (or
        /// START/CANCEL), all of which change the phase and kill this timer (§10.1).</summary>
        public const int MATCH_END_SECONDS = 999;

        /// <summary>Loading waits for every player's set_ready; on timeout it moves to Countdown
        /// anyway.</summary>
        public const float LOADING_TIMEOUT = 20f;

        /// <summary>DEFAULT death → earliest revive delay (respawn.delaySeconds); a mode may override it
        /// via ModeRules.RespawnDelay (§10.5).</summary>
        public const float RESPAWN_DELAY = 5f;

        /// <summary>reviveAnchor="standstill" (§10.5): uninterrupted standstill needed to revive.</summary>
        public const float REVIVE_HOLD_SECONDS = 5f;

        /// <summary>reviveAnchor="standstill": moving beyond this radius (m) from the death anchor
        /// resets both the timer and the anchor.</summary>
        public const float REVIVE_HOLD_RADIUS = 1f;

        /// <summary>Match duration options for the admin UI (seconds, 1 min … 1 h). <b>A UI list, not a
        /// protocol limit</b> — the server accepts any positive roundSeconds (§5.2). In EVERY mode the
        /// value is the whole match's: round-based modes spend it across all their rounds and do not
        /// time-box a single round (§10.5).</summary>
        public static readonly int[] ROUND_SECONDS_OPTIONS = { 60, 90, 120, 150, 180, 300, 600, 900, 1200, 1800, 3600 };

        /// <summary>The UNLIMITED value of <c>scoreLimit</c> (§5.2): no score/round limit, the match
        /// ends on time or on the operator's <c>end_match</c>. A separate value is needed because <c>0</c> already
        /// means "operator did not choose"; every server gate asks "limit &gt; 0", so negatives are
        /// already unlimited.
        /// <para>⚠️ In round-based modes this also removes the round ceiling (<c>2 × limit − 1</c>,
        /// §10.5).</para></summary>
        public const int SCORE_LIMIT_UNLIMITED = -1;

        /// <summary>The single normalisation gate for <c>scoreLimit</c>: positives and <c>0</c> pass
        /// through, EVERY negative becomes <see cref="SCORE_LIMIT_UNLIMITED"/> so the wire carries one
        /// spelling of "unlimited".</summary>
        public static int NormalizeScoreLimit(int scoreLimit) =>
            scoreLimit < 0 ? SCORE_LIMIT_UNLIMITED : scoreLimit;

        // NOTE: there is NO fire-rate tolerance constant and none will be added (§10.3). The server has
        // no rate checking and no weapon table — the client computes damage, the server applies it
        // verbatim; the product runs in supervised venues, so anti-cheat is deliberately left out.
    }
}
