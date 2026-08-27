#nullable enable
using System.Net;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Server-side view of a single connected (or previously connected) device.</summary>
public sealed class PlayerState
{
    public string DeviceId { get; init; } = "";

    /// <summary>The 1..PLAYER_ID_MAX id assigned in welcome (1 byte in UDP packets).</summary>
    public int PlayerId { get; init; }

    public string Name { get; set; } = "";

    /// <summary>Jersey number 1..99 (§2); 0 = unassigned, always 0 for admins (they do not play).
    /// Unique across ALL registered devices, not just connected ones.</summary>
    public int Number { get; set; }

    /// <summary>"player" (VR/Quest) or "admin" (Windows desktop).</summary>
    public string Role { get; set; } = "player";

    /// <summary>"red" | "blue"; empty for admins, who do not play.</summary>
    public string Team { get; set; } = "";

    public bool Ready { get; set; }

    /// <summary>Connection state (§2), written only by <see cref="PlayerRegistry"/>.</summary>
    /// <remarks>⚠️ The socket field is deliberately named <see cref="Socket"/>: both could not be
    /// "connection", and the concept carried on the wire (<c>PlayerInfo.connection</c>) is this
    /// three-valued state, not the WebSocket object.</remarks>
    public PlayerConnection Connection { get; set; }

    /// <summary>When the socket dropped (UTC), the basis of the RECONNECT_GRACE calculation; only
    /// meaningful while <see cref="PlayerConnection.Reconnecting"/>.</summary>
    public DateTime DisconnectedAt { get; set; }

    /// <summary>The single "is online" answer: every match gate (loading, hit, revive, snapshot) reads
    /// this, so <c>reconnecting</c> and <c>left</c> records enter none of them.</summary>
    public bool IsConnected => Connection == PlayerConnection.Connected;

    /// <summary>Written into the running match's ledger (§10.2), which makes the stats row independent
    /// of the connection: a dropped participant's record survives as
    /// <see cref="PlayerConnection.Left"/> until the match ends.</summary>
    public bool MatchParticipant { get; set; }

    /// <summary>0–1 range; -1 = unknown.</summary>
    public float Battery { get; set; } = -1f;

    /// <summary>Left/right controller state (<c>ArenaProtocol.CONTROLLER_*</c>, §5.1);
    /// <c>CONTROLLER_UNKNOWN</c> = unknown, where admin records and clients that do not report stay.</summary>
    /// <remarks>⚠️ Unknown must be <c>0</c>: an unassigned <c>int</c> already arrives as <c>0</c>, so
    /// treating <c>0</c> as "healthy" would show every silent record as healthy.</remarks>
    public int CtrlL { get; set; } = ArenaProtocol.CONTROLLER_UNKNOWN;

    /// <inheritdoc cref="CtrlL"/>
    public int CtrlR { get; set; } = ArenaProtocol.CONTROLLER_UNKNOWN;

    public float Fps { get; set; }
    public string Scene { get; set; } = "";

    // ---- Network telemetry measured by the client and reported in status (§6.7) ----
    // ⚠️ These are NOT added to ToPlayerInfo(). As constantly changing numbers they would open the
    // "did a visible field really change" gate in PlayerRegistry.UpdateStatus on every status and bring
    // back a solved bug (every status = a full roster broadcast). Fps stays out of PlayerInfo for the
    // same reason; follow that precedent. Admins get net_stats instead.

    /// <summary>RTT measured by the client (ms); -1 = unknown (old version or no probe yet).</summary>
    public int RttMs { get; set; } = -1;

    /// <summary>Downlink snapshot jitter measured by the client (ms); -1 = unknown.</summary>
    public float JitterMs { get; set; } = -1f;

    /// <summary>Downlink snapshot loss measured by the client (%); -1 = unknown.</summary>
    public float LossPct { get; set; } = -1f;

    /// <summary>Build scene list reported in hello (for admin catalog validation).</summary>
    public List<string> Scenes { get; set; } = new();

    /// <summary>UTC time of the last hello/status (basis of the HEARTBEAT_TIMEOUT sweep).</summary>
    public DateTime LastSeen { get; set; }

    // ---- Match state (§10.2) ----
    // The ONLY writer is MatchDirector, and all of it is read/written under its _gate lock.
    // (StateHost reads only Alive lock-free — a bool read is atomic and one tick of lag is harmless.)

    /// <summary>0..PLAYER_MAX_HP; refilled on entering Live and on revive.</summary>
    public float Hp { get; set; } = ArenaProtocol.PLAYER_MAX_HP;

    /// <summary>Feeds snapshot flags bit0 (FLAG_ALIVE); everyone is alive in the Lobby phase.</summary>
    public bool Alive { get; set; } = true;

    public int Kills { get; set; }
    public int Deaths { get; set; }

    /// <summary>Individual match score (§10.2) — NOT the same as Kills: it is written by IGameMode
    /// (through MatchDirector's ledger, under _gate) and means something different per mode.</summary>
    public int Score { get; set; }

    /// <summary>UTC time of the last death (RESPAWN_DELAY and the obstacle revive cap, §10.4/§10.9).</summary>
    public DateTime DiedAt { get; set; }

    /// <summary>UTC instant spawn protection ends; in the past or <see cref="DateTime.MinValue"/> = no
    /// protection (§10.4), duration set by the mode's <c>SpawnProtectionSeconds</c>.</summary>
    /// <remarks>Written by MatchDirector (under its <c>_gate</c>, at the revive/death gates) and read by
    /// the <c>hit_report</c> gate and the snapshot writer. StateHost reads it lock-free for the same
    /// reason as <see cref="Alive"/>.</remarks>
    public DateTime SpawnProtectedUntil { get; set; } = DateTime.MinValue;

    // ---- Calibration state (§10.6) ----
    // DEVICE state, not match state: written by PlayerRegistry (SetCalibration, under the registry's own
    // _gate) and PRESERVED across match resets. MatchDirector only READS it under its own _gate — the
    // same pattern as Team; a bool read is atomic, so chaining the two locks would only add deadlock
    // risk for no gain.

    /// <summary>Whether the headset reported being aligned with the arena; forced false on hello
    /// (§10.6), since the server cannot know a reconnecting headset's alignment.</summary>
    /// <remarks>While false the player cannot fire, take damage or revive.</remarks>
    public bool Calibrated { get; set; }

    /// <summary>"manual" | "anchor" | "cloud" | "" — a free, unvalidated label (§5.1).</summary>
    public string CalibrationSource { get; set; } = "";

    /// <summary>Floor offset reported by the last MANUAL calibration (metres, signed; §10.6);
    /// <c>0</c> = no measurement or clean.</summary>
    /// <remarks>The server does not interpret it, only carries it in the roster and warns the operator
    /// past <see cref="ArenaProtocol.CALIB_FLOOR_WARN_METERS"/>. Reset when calibration drops — the
    /// offset belonged to that alignment.</remarks>
    public float FloorOffset { get; set; }

    /// <summary>Failure reason of the last body measurement, empty = fine (§10.8).</summary>
    /// <remarks>A successful measurement clears it; otherwise one failure would leave a warning on the
    /// player's row forever.</remarks>
    public string ScaleError { get; set; } = "";

    /// <summary>Failure reason of the last <c>reload_calibration</c> attempt, empty = fine (§10.6).</summary>
    /// <remarks>A successful calibration clears it (same reasoning as <see cref="ScaleError"/>).</remarks>
    public string CalibrationError { get; set; } = "";

    /// <summary>Uniform body scale to apply to the remote avatar (§10.8); <c>0</c> = not measured.</summary>
    /// <remarks>Same class as calibration (device state, preserved across match resets) and written
    /// under the same lock. The server does not PRODUCE this number: the client measures, the server
    /// only clamps it to range and broadcasts it in the roster.
    /// <para>⚠️ Reset when <see cref="Calibrated"/> goes <c>false</c> — the measurement is relative to
    /// the arena floor, so an invalid floor invalidates it.</para></remarks>
    public float BodyScale { get; set; }

    /// <summary>UDP registration token issued in welcome; renewed on every hello.</summary>
    public uint UdpToken { get; set; }

    /// <summary>UDP endpoint validated by 0x00 UdpHello; null before registration.</summary>
    public IPEndPoint? UdpEndpoint { get; set; }

    /// <summary>Pose read/write lock — the UDP recv thread and the snapshot timer are different
    /// threads, and the multi-field PoseUpdate struct must not tear.</summary>
    public object PoseGate { get; } = new();

    /// <summary>Has at least one valid PoseUpdate arrived (only then is LastPose read).</summary>
    public bool HasPose { get; set; }

    /// <summary>Last accepted pose (arena space; read/write under PoseGate).</summary>
    public PoseUpdate LastPose { get; set; }

    /// <summary>Sequence number of the last accepted pose (for u16-wrapping staleness checks).</summary>
    public ushort LastSeq { get; set; }

    /// <summary>UTC time of the last accepted pose — a LIVENESS (staleness) measure.</summary>
    /// <remarks>⚠️ Not usable for jitter: <c>DateTime.UtcNow</c>'s default Windows resolution (~15.6 ms)
    /// cannot measure the deviation of a 50 ms interval. Jitter is computed from the monotonic
    /// <see cref="LastPoseStamp"/>.</remarks>
    public DateTime LastPoseAt { get; set; }

    /// <summary>§10.9: the sender measured its own body INSIDE an interior obstacle
    /// (<c>gripFlags</c> bit5 = <see cref="SnapshotEntry.FLAG_IN_OBSTACLE"/>).</summary>
    /// <remarks>⚠️ A MEASUREMENT, not a penalty: <c>MatchDirector</c> applies the per-second health
    /// drain on its own tick and clock — the client cannot damage itself with this bit, it only reports
    /// that the penalty may start (same model as <c>hit_report</c>).
    /// <para>⚠️ Not sufficient alone: the reader also checks freshness via <see cref="LastPoseAt"/>
    /// (<see cref="ArenaProtocol.OBSTACLE_FLAG_STALE_MS"/>). No separate timestamp exists because this
    /// field's freshness IS the pose's — the flag arrives in every pose packet.</para>
    /// <para>Written by the UDP recv thread (under PoseGate), read by MatchDirector (under its _gate,
    /// WITHOUT taking PoseGate) — same pattern as <see cref="Alive"/>/<see cref="Calibrated"/>: a bool
    /// read is atomic, and chaining the locks would only add deadlock risk.</para></remarks>
    public bool InObstacle { get; set; }

    /// <summary>§10.9: since when <see cref="InObstacle"/> has held UNINTERRUPTED (UTC); <c>null</c>
    /// when not in an obstacle. The grace (<see cref="ArenaProtocol.OBSTACLE_GRACE_SECONDS"/>) is
    /// measured from here.</summary>
    /// <remarks>⚠️ MatchDirector's business only (written and read under its <c>_gate</c>): not to be
    /// confused with the client's bit. The bit says "I am inside now", this field says "since when",
    /// and only the SERVER's clock may answer that — a duration from the client would hand it the
    /// penalty.
    /// <para>Reset to <c>null</c> on death, on losing calibration and on leaving the obstacle: the
    /// grace restarts on every new entry.</para></remarks>
    public DateTime? ObstacleSince { get; set; }

    /// <summary>§10.9: the sender measured its own head OUTSIDE the boundary's safe area
    /// (<c>gripFlags</c> bit7 = <see cref="SnapshotEntry.FLAG_OUT_OF_BOUNDS"/>).</summary>
    /// <remarks>⚠️ Also a MEASUREMENT, but unlike <see cref="InObstacle"/> it produces NO penalty: no
    /// health drain, no revive block, no match gate. Same reasoning as keeping the outer wall off the
    /// <c>Obstacle</c> layer — a player whose calibration slipped a few centimetres would die for
    /// nothing. Its only consumers are admin visibility (the top-down ring) and the violation ledger
    /// (<see cref="OutOfBoundsTally"/>); intervention is the operator's call.
    /// <para>⚠️ Freshness is again the pose's (<see cref="LastPoseAt"/> +
    /// <see cref="ArenaProtocol.OBSTACLE_FLAG_STALE_MS"/>), so no separate timestamp exists.</para>
    /// <para>Threading is identical to <see cref="InObstacle"/>.</para></remarks>
    public bool OutOfBounds { get; set; }

    /// <summary>Admin ledger of obstacle violations (§10.9).</summary>
    public ViolationTally ObstacleTally { get; } = new();

    /// <summary>Admin ledger of out-of-bounds violations (§10.9).</summary>
    public ViolationTally OutOfBoundsTally { get; } = new();

    // ---- Skeleton channel (0x07, §6.9) ----
    // ⚠️ Read/written UNDER PoseGate — the same lock as the pose, because both are shared between the
    // same two threads (recv writes, the 20 Hz broadcast reads) and a second lock would only raise a
    // lock ordering question. Do not add a new lock.

    /// <summary>Last accepted skeleton blob — OPAQUE: the server never unpacks or validates it, only
    /// copies it into the batch (§6.9); there is no skeleton table on the server and none is
    /// added.</summary>
    /// <remarks>⚠️ Never mutate this array in place; replace it on every packet. The broadcast thread
    /// takes the reference under the lock and serialises outside it, so in-place writes would publish a
    /// half-updated blob.</remarks>
    public byte[]? LastSkeleton { get; set; }

    /// <summary>Body yaw + head-anchored root offset (§6.9, v19) — the blob's own root is unused.
    /// Copied into the batch as-is; the server never interprets it.</summary>
    public SkeletonRootData LastSkeletonRoot { get; set; }

    /// <summary>Sequence number of the skeleton channel. ⚠️ SEPARATE from <see cref="LastSeq"/>: the two
    /// channels flow at different cadences, and a shared counter would age one's packet in the other's
    /// name.</summary>
    public ushort LastSkeletonSeq { get; set; }

    /// <summary>Has at least one valid skeleton arrived (only then is <see cref="LastSkeletonSeq"/>
    /// meaningful — otherwise a first packet with <c>seq=0</c> would be dropped as stale).</summary>
    public bool HasSkeleton { get; set; }

    /// <summary>Monotonic stamp of the last skeleton (<c>Stopwatch.GetTimestamp()</c>), used to drop a
    /// stale body from the broadcast; 0 = no skeleton yet.</summary>
    public long LastSkeletonStamp { get; set; }

    // ---- Uplink telemetry (client → server) ----
    // ⚠️ ALL of these are written and read under PoseGate — NO new lock for telemetry. Reason: StateHost's
    // threading contract — the recv thread may not enter the match lock, and the 20 Hz pose intake path
    // must not stall for a diagnostic counter. The pose path already takes this lock.

    /// <summary>Monotonic stamp of the last accepted pose (<c>Stopwatch.GetTimestamp()</c>); 0 = no pose
    /// yet. Jitter comes from the difference of two consecutive stamps.</summary>
    public long LastPoseStamp { get; set; }

    /// <summary>Poses accepted in this summary window (denominator of the loss percentage).</summary>
    public int PoseAccepted { get; set; }

    /// <summary>Poses counted lost from a <c>seq</c> gap (§6.2). Lost = gap − 1.</summary>
    public int PoseLost { get; set; }

    /// <summary>Sum of the arrival interval's deviations from nominal (50 ms), microseconds.</summary>
    public long PoseJitterSumMicros { get; set; }

    /// <summary>Deviation sample count (for the average) and the largest deviation seen, microseconds.</summary>
    public int PoseJitterSamples { get; set; }
    public long PoseJitterMaxMicros { get; set; }

    // ---- Shot event channel (0x03, §6.4) ----
    // ⚠️ Only the UDP recv thread touches these two (single writer + single reader on the same thread) →
    // no lock needed, and PoseGate is not taken either (they are unrelated to the pose).
    // ⚠️ SEPARATE from LastSeq and must stay so: LastSeq belongs to the POSE channel and ENFORCES ORDER
    // (state: last one wins); this belongs to the EVENT channel and only suppresses exact duplicates.

    /// <summary>The <c>seq</c> of the last handled shot event — for duplicate suppression (§6.4): UDP may
    /// duplicate packets, and a repeated <c>seq</c> is not relayed (double tracer + double sound).</summary>
    public ushort LastEventSeq { get; set; }

    /// <summary>Has at least one shot event been handled (only then is <see cref="LastEventSeq"/>
    /// meaningful — otherwise a first event with <c>seq=0</c> would be dropped as a duplicate).</summary>
    public bool HasEventSeq { get; set; }

    /// <summary>Event channel telemetry: events received and counted lost from a <c>seq</c> gap in this
    /// window.</summary>
    /// <remarks>⚠️ Unlike the pose counters these are NOT under PoseGate — the event path never takes
    /// that lock, and taking it for diagnostics would tie the 20 Hz pose intake to event traffic. Hence
    /// <c>Interlocked</c> (recv thread writes, the 1 Hz summary reads); the three counters not being
    /// atomic as a group is irrelevant for telemetry.</remarks>
    public long EventAccepted;
    public long EventLost;

    /// <summary>The open WS connection; null while dropped. Not named <c>Connection</c>, since that name
    /// belongs to the connection STATE carried on the wire (see <see cref="Connection"/>).</summary>
    public ClientConnection? Socket { get; set; }

    /// <summary>Wire-format snapshot for lobby_state.</summary>
    public PlayerInfo ToPlayerInfo() => new()
    {
        playerId = PlayerId,
        name = Name,
        number = Number,
        role = Role,
        team = Team,
        ready = Ready,
        connection = ConnectionWire(Connection),
        // Recomputed on every snapshot (§5.3): no timestamp on the wire, since the client's clock is
        // not aligned with the server's.
        reconnectSeconds = ReconnectSecondsLeft(),
        inMatch = MatchParticipant,
        battery = Battery,
        // §5.1/§5.3 — discrete device state; unlike the telemetry numbers it does travel in the roster.
        ctrlL = CtrlL,
        ctrlR = CtrlR,
        scene = Scene,
        // §10.2 counters: read by the admin statistics table (§5.3 lobby_state).
        kills = Kills,
        deaths = Deaths,
        hp = Hp,
        alive = Alive,
        score = Score,
        // §10.6 — read by the calibration tick in the admin observer UI.
        calibrated = Calibrated,
        calibrationSource = CalibrationSource,
        // §10.6 — floor estimate deviation; the UI marks rows past the threshold with ⚠.
        floorOffset = FloorOffset,
        // §10.8 — 0 = not measured; the reader applies 1.
        bodyScale = BodyScale,
        scaleError = ScaleError,
        // §10.6 — reason of the last reload attempt; empty = fine.
        calibrationError = CalibrationError
    };

    private static string ConnectionWire(PlayerConnection connection) => connection switch
    {
        PlayerConnection.Reconnecting => ArenaProtocol.CONNECTION_RECONNECTING,
        PlayerConnection.Left => ArenaProtocol.CONNECTION_LEFT,
        _ => ArenaProtocol.CONNECTION_CONNECTED
    };

    /// <summary>Seconds left before the player is dropped (rounded up, clamped to 0); non-zero only
    /// while <see cref="PlayerConnection.Reconnecting"/>.</summary>
    private int ReconnectSecondsLeft()
    {
        if (Connection != PlayerConnection.Reconnecting) return 0;
        var elapsed = (DateTime.UtcNow - DisconnectedAt).TotalSeconds;
        return (int)Math.Max(0d, Math.Ceiling(ArenaProtocol.RECONNECT_GRACE - elapsed));
    }
}

/// <summary>Admin ledger of ONE violation kind (§10.9).</summary>
/// <remarks>SEPARATE from the penalty: the penalty clock is <see cref="PlayerState.ObstacleSince"/>
/// and resets on death/lost calibration, while the ledger is the operator's record and is unaffected.
/// <para>⚠️ No PER-KIND FIELDS: both kinds share the same edge logic, and duplicating four fields
/// would let the two drift silently. One type, two instances.</para>
/// <para>Written and read only by <c>MatchDirector</c> (under its <c>_gate</c>); the fields are not
/// individually atomic, so no other lock touches them.</para></remarks>
public sealed class ViolationTally
{
    /// <summary>When the violation started; null = no violation.</summary>
    public DateTime? Since { get; set; }

    /// <summary>Whether the "started" message went to admins
    /// (<see cref="ArenaProtocol.VIOLATION_MIN_SECONDS"/> threshold passed). Contact below the
    /// threshold never enters the ledger — and produces no end message either.</summary>
    public bool Announced { get; set; }

    public int Count { get; set; }
    public float TotalSeconds { get; set; }

    public void Reset() { Since = null; Announced = false; Count = 0; TotalSeconds = 0f; }
}
