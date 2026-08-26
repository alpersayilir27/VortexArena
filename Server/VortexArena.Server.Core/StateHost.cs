#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>UDP state channel (statePort): 0x00 UdpHello registration (§6.1), 0x01 PoseUpdate
/// intake (§6.2), 0x02 Snapshot broadcast at 20 Hz (§6.3), 0x03 FireEvent intake + 0x04 EventBatch
/// relay (§6.4/6.5), 0x09 ObjectPose intake → the object section of 0x05 (§6.12/6.8).
/// <para><b>Thread contract:</b> recv and broadcast (20 Hz timer) run on SEPARATE threads. The recv
/// thread NEVER takes <see cref="MatchDirector"/>'s match lock (§10.3) — it reads the gate from the
/// <see cref="MatchDirector.ShotRelayOpen"/> volatile flag and player state from
/// <see cref="PlayerState.Alive"/>/<c>Calibrated</c> lock-free. The only mutable structure shared
/// between the two threads is the <see cref="_events"/> queue.</para></summary>
public sealed class StateHost
{
    private readonly PlayerRegistry _registry;
    private readonly int _port;

    /// <summary>Fire event relay gate (§6.5) — only <c>ShotRelayOpen</c> is read.</summary>
    private readonly MatchDirector _matchDirector;

    /// <summary>Events that passed the relay gate: written by recv thread, read by the 20 Hz
    /// broadcast thread.
    /// <para><see cref="ConcurrentQueue{T}"/> instead of a lock: the recv path shares its thread
    /// with the 20 Hz pose intake, so blocking on a lock would stall that stream too.</para></summary>
    private readonly ConcurrentQueue<FireEventEntry> _events = new();

    /// <summary>This tick's object poses (§6.12), netId → pose: written by the recv thread, drained and
    /// CLEARED by the 20 Hz broadcast thread.
    /// <para>A map, not a queue: a pose is a STATE, so the tick's last one wins and an object that sent
    /// twice takes one entry. Cleared on every tick — a stale pose must not be sent again next tick.</para></summary>
    private readonly Dictionary<ushort, PoseData> _objectPoses = new();

    /// <summary>Guards <see cref="_objectPoses"/> only. Deliberately NOT the match lock: the ownership
    /// check on this path is lock-free (§6.12) and blocking here would stall the pose intake.</summary>
    private readonly object _objectPoseGate = new();

    /// <summary>Last accepted <c>0x09</c> seq + arrival stamp per object; touched by the recv thread
    /// ONLY, so no lock (same as the fire-event seq fields).</summary>
    private readonly Dictionary<ushort, (ushort Seq, long Stamp)> _objectSeq = new();

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Task? _snapshotLoop;

    // ---- Telemetry constants ----

    /// <summary>Nominal tick interval (ms) — reference for tick drift.</summary>
    private const double NominalTickMs = 1000.0 / ArenaProtocol.SNAPSHOT_RATE_HZ;

    /// <summary>Players over these thresholds get an extra <c>[net]</c> line in the per-second
    /// summary. A line per player per second (10 players = 10 lines/s) would make the console
    /// unreadable; surfacing only the problematic one is enough.</summary>
    private const double JitterWarnMs = 25.0;
    private const double LossWarnPct = 2.0;

    /// <summary>Upper bound for treating an event <c>seq</c> gap as loss. The event channel has NO
    /// ordering guarantee (§6.4): an out-of-order packet makes the <c>(ushort)</c> delta ~65535 and
    /// an uncapped counter would report "65k lost". Chosen far above one player's per-second event
    /// rate and far below 65535.</summary>
    private const int EventGapMax = 512;

    /// <summary>
    /// Skeleton blob is dropped from the broadcast if not refreshed within this window
    /// (§6.10 "no input, no packet").
    /// <para><b>Local constant, NOT a protocol constant:</b> the client needs no knowledge of it —
    /// a body-less avatar simply freezes on its last frame and avatar lifetime comes from the
    /// snapshot (§6.3). In the protocol it would become a two-sided contract; here it is only the
    /// server's decision not to broadcast stale data.</para>
    /// <para>Several times the <see cref="ArenaProtocol.SKELETON_RATE_HZ"/> interval: normal packet
    /// loss must not drop the body, but a truly silent sender must not stay on air.</para>
    /// </summary>
    private const double SkeletonStaleMs = 500.0;

    /// <summary>An object's <c>0x09</c> seq counter is forgotten after this silence.
    /// <para><b>Why it exists:</b> a netId is reused after a round/scene reset (the table is rebuilt from
    /// the map), and the previous round's high seq would lock the new object's stream out until the
    /// counter wrapped — the object would simply never move for anyone.</para></summary>
    private const double ObjectSeqStaleMs = 2000.0;

    // ---- Telemetry counters ----
    // TX counters: only the broadcast thread writes and reads them → no lock needed.
    // RX counters: written by the recv thread, read+reset once a second by the broadcast thread →
    // Interlocked required. Per-player counters live in PlayerState (pose ones under PoseGate).

    private long _txSnapshotPackets, _txSnapshotBytes;
    private long _txEventPackets, _txEventBytes;
    private long _txSkeletonPackets, _txSkeletonBytes;
    private long _rxPackets, _rxBytes;

    /// <summary>Datagrams inside the snapshot counter that also carry events (<c>0x05</c>) — shows
    /// how often §6.8 combining applies. NOT a separate channel, a subset.</summary>
    private long _txCombinedPackets;

    /// <summary>Replies sent from the RECV thread: <c>0x00</c> ack and <c>0x06</c> echo.
    /// <b>Kept apart from the broadcast counters purely because of the thread</b> — those can be
    /// incremented lock-free, these are written from another thread and need Interlocked.</summary>
    private long _txAckPackets, _txAckBytes;

    /// <summary>Received but NOT processed: unregistered/foreign endpoint, short packet, stale
    /// <c>seq</c>, unknown type. "Arrived but dropped" and "never arrived" are two very different
    /// diagnoses in the field.</summary>
    private long _rxRejected;

    /// <summary>Successful UDP registration (for the console line).</summary>
    public event Action<byte, IPEndPoint>? UdpRegistered;

    /// <param name="matchDirector">Source of the shot relay gate (§6.5). <b>Why a ctor param:</b>
    /// the director is built BEFORE StateHost in <c>Program.cs</c>, so there is no cycle — a
    /// settable property would create the "forget the wiring, events silently vanish" trap.</param>
    public StateHost(PlayerRegistry registry, int port, MatchDirector matchDirector)
    {
        _registry = registry;
        _port = port;
        _matchDirector = matchDirector;
    }

    public void Start()
    {
        if (_loop is { IsCompleted: false } || _snapshotLoop is { IsCompleted: false }) return;
        var udp = new UdpClient(_port);
        _udp = udp;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => ReceiveLoopAsync(udp, token));
        _snapshotLoop = Task.Run(() => SnapshotLoopAsync(udp, token));
    }

    /// <summary>Cancel → drain BOTH loops → close the socket → dispose. Idempotent.</summary>
    /// <remarks>⚠️ Order matters: closing the UdpClient while the recv loop is still in
    /// <c>ReceiveAsync</c> (or the broadcast loop mid-send) turns a clean shutdown into an
    /// ObjectDisposedException race.</remarks>
    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        var snapshotLoop = _snapshotLoop;
        var udp = _udp;
        _cts = null;
        _loop = null;
        _snapshotLoop = null;
        _udp = null;
        if (cts == null && loop == null && snapshotLoop == null && udp == null) return;

        cts?.Cancel();
        await ServiceShutdown.DrainAsync("state", loop, snapshotLoop);
        udp?.Close();
        cts?.Dispose();
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException)
            {
                // On Windows recv can throw 10054 after sending to an unreachable target — keep the loop alive.
                continue;
            }

            var data = result.Buffer;
            if (data.Length == 0) continue;

            // RX volume counted without type split: per-channel breakdown has no diagnostic value
            // (upstream is almost entirely 0x01).
            Interlocked.Increment(ref _rxPackets);
            Interlocked.Add(ref _rxBytes, data.Length);

            switch (data[0])
            {
                case UdpPacketType.UdpHello:
                    if (data.Length < UdpHello.SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    await HandleUdpHelloAsync(udp, data, result.RemoteEndPoint, token);
                    break;
                case UdpPacketType.PoseUpdate:
                    if (data.Length < PoseUpdate.SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    HandlePoseUpdate(data, result.RemoteEndPoint);
                    break;
                case UdpPacketType.FireEvent:
                    if (data.Length < FireEvent.SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    HandleFireEvent(data, result.RemoteEndPoint);
                    break;
                case UdpPacketType.RttProbe:
                    if (data.Length < RttProbe.SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    await HandleRttProbeAsync(udp, data, result.RemoteEndPoint, token);
                    break;
                case UdpPacketType.ObjectPose:
                    if (data.Length < ObjectPose.SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    HandleObjectPose(data, result.RemoteEndPoint);
                    break;
                case UdpPacketType.SkeletonUpdate:
                    if (data.Length < SkeletonUpdate.HEADER_SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    HandleSkeletonUpdate(data, result.RemoteEndPoint);
                    break;
                default:
                    // Unknown packet type — ignored (forward version compatibility).
                    Interlocked.Increment(ref _rxRejected);
                    break;
            }
        }
    }

    private async Task HandleUdpHelloAsync(UdpClient udp, byte[] data, IPEndPoint remote, CancellationToken token)
    {
        UdpHello hello;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // type byte already consumed by the dispatcher
            hello = UdpHello.Read(reader);
        }

        if (!_registry.TryRegisterUdpEndpoint(hello.playerId, hello.udpToken, remote))
        {
            Console.WriteLine($"[StateHost] udp_hello reddedildi: playerId {hello.playerId} ({remote}) token eşleşmedi.");
            return;
        }

        try
        {
            // Ack = the same 6 bytes echoed back; client retries every 1 s until it arrives.
            await udp.SendAsync(data.AsMemory(0, UdpHello.SIZE), remote, token);
            Interlocked.Increment(ref _txAckPackets);
            Interlocked.Add(ref _txAckBytes, UdpHello.SIZE);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StateHost] ack gönderimi başarısız ({remote}): {ex.Message}");
            return;
        }
        UdpRegistered?.Invoke(hello.playerId, remote);
    }

    /// <summary>0x06 RttProbe: echoes the 6 bytes back <b>verbatim</b> (§6.7). No server-side state
    /// and the stamp is NOT read — the client interprets it (hence no clock sync needed).
    /// <para>Same validation as the pose/event path: only from a <c>0x00</c>-registered endpoint.
    /// Rejection is silent — at 1 Hz × players even one log line is noise.</para>
    /// <para>⚠️ Runs on the <b>recv thread</b>: never takes the <see cref="MatchDirector"/> lock and
    /// writes no player field (measurement lives entirely on the client).</para></summary>
    private async Task HandleRttProbeAsync(UdpClient udp, byte[] data, IPEndPoint remote, CancellationToken token)
    {
        // playerId read directly: no need to decode the stamp, only the endpoint match matters.
        var playerId = data[1];
        if (!_registry.TryGetByPlayerId(playerId, out var state)
            || state.UdpEndpoint == null || !state.UdpEndpoint.Equals(remote))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        try
        {
            await udp.SendAsync(data.AsMemory(0, RttProbe.SIZE), remote, token);
            Interlocked.Increment(ref _txAckPackets);
            Interlocked.Add(ref _txAckBytes, RttProbe.SIZE);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Unreachable target (10054 etc.) — a lost echo is harmless, the next probe follows.
        }
    }

    /// <summary>0x01 PoseUpdate intake: accepted only from a 0x00-registered endpoint, stale or
    /// duplicate seq dropped, accepted pose stored under PoseGate. No console output at 20 Hz;
    /// rejection is silent too.</summary>
    private void HandlePoseUpdate(byte[] data, IPEndPoint remote)
    {
        PoseUpdate pose;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // type byte already consumed by the dispatcher
            pose = PoseUpdate.Read(reader);
        }

        if (!_registry.TryGetByPlayerId(pose.playerId, out var state))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        // No pose from an unregistered/foreign source (spoof protection, §6.1).
        if (state.UdpEndpoint == null || !state.UdpEndpoint.Equals(remote))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        var stamp = Stopwatch.GetTimestamp();

        lock (state.PoseGate)
        {
            // u16 wrap-safe ordering: the (short) delta orders the 65535→0 transition correctly.
            if (state.HasPose && (short)(pose.seq - state.LastSeq) <= 0)
            {
                Interlocked.Increment(ref _rxRejected);
                return;
            }

            // ---- Uplink telemetry (§6.2 "seq gap = loss") ----
            // ⚠️ COUNTS only. §6.4's ban applies here too: no ordering enforcement, measurement
            // never drops a packet.
            if (state.HasPose)
            {
                // Gap = skipped seq count. 1 = no gap (expected successor).
                int gap = (ushort)(pose.seq - state.LastSeq);
                if (gap > 1) state.PoseLost += gap - 1;

                if (state.LastPoseStamp != 0)
                {
                    double intervalMs = StampToMs(stamp - state.LastPoseStamp);
                    // Loss stretches the arrival interval by whole multiples; the expected interval
                    // is scaled by the gap so loss is not reported as jitter (2 lost packets must
                    // not turn a 100 ms interval into "50 ms jitter").
                    double expectedMs = NominalTickMs * Math.Max(1, gap);
                    long deviationMicros = (long)(Math.Abs(intervalMs - expectedMs) * 1000.0);
                    state.PoseJitterSumMicros += deviationMicros;
                    state.PoseJitterSamples++;
                    if (deviationMicros > state.PoseJitterMaxMicros)
                        state.PoseJitterMaxMicros = deviationMicros;
                }
            }

            state.PoseAccepted++;
            state.LastPoseStamp = stamp;

            state.LastPose = pose;
            state.LastSeq = pose.seq;
            state.HasPose = true;
            state.LastPoseAt = DateTime.UtcNow;

            // §10.9: the obstacle-violation flag is mirrored into a SEPARATE field (it also lives in
            // LastPose) because its reader is MatchDirector, which does NOT take PoseGate — reading
            // an 88 B struct lock-free means tearing, reading a single bool does not.
            state.InObstacle = (pose.gripFlags & SnapshotEntry.FLAG_IN_OBSTACLE) != 0;
            // Out-of-bounds mirrored for the same reason. ⚠️ This bit PRODUCES NO PENALTY (§10.9);
            // it only feeds admin visibility and the violation log.
            state.OutOfBounds = (pose.gripFlags & SnapshotEntry.FLAG_OUT_OF_BOUNDS) != 0;
        }
    }

    /// <summary>
    /// 0x07 SkeletonUpdate intake (§6.9): identical gate to <c>0x01</c> (only a <c>0x00</c>-registered
    /// endpoint, u16 wrap-safe staleness check, silent rejection).
    /// <para>⚠️ <b>The blob is NOT parsed.</b> The server stores it as raw bytes and copies it into
    /// the batch; interpreting it would add a second skeleton truth source to the server (§10.3 —
    /// even damage is computed client-side).</para>
    /// <para>⚠️ The staleness counter is <b>separate</b> from the pose channel's
    /// (<c>LastSkeletonSeq</c>): the two channels run at different cadences and a shared counter
    /// would age one channel's packet on behalf of the other.</para>
    /// </summary>
    private void HandleSkeletonUpdate(byte[] data, IPEndPoint remote)
    {
        SkeletonUpdate msg;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // type byte already consumed by the dispatcher
            msg = SkeletonUpdate.Read(reader);
        }

        // Empty blob = corrupt/truncated datagram (Read returns empty past the length limit).
        if (msg.blobLength == 0)
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        if (!_registry.TryGetByPlayerId(msg.playerId, out var state))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        if (state.UdpEndpoint == null || !state.UdpEndpoint.Equals(remote))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        lock (state.PoseGate)
        {
            if (state.HasSkeleton && (short)(msg.seq - state.LastSkeletonSeq) <= 0)
            {
                Interlocked.Increment(ref _rxRejected);
                return;
            }

            // ⚠️ The reference is REPLACED, the array is never written in place: the broadcast thread
            // grabs the reference under the lock and serializes OUTSIDE it (to keep the lock short).
            state.LastSkeleton = msg.blob;
            state.LastSkeletonRoot = msg.root;
            state.LastSkeletonSeq = msg.seq;
            state.HasSkeleton = true;
            state.LastSkeletonStamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>0x09 ObjectPose intake (§6.12): registered endpoint → the sender OWNS the object → a
    /// fresh seq. The pose enters this tick's object section and the director's "last seen" slot.
    /// <para>⚠️ <b>Ownership is validated, the pose is NOT</b>: the server knows no metres, so it has
    /// nothing to falsify it with (same class as <c>hit_report.damage</c>). A packet from the wrong owner
    /// is dropped silently.</para>
    /// <para>⚠️ The check reads the director's LOCK-FREE owner map — this path must never enter the match
    /// lock, or the whole pose intake would queue behind it.</para></summary>
    private void HandleObjectPose(byte[] data, IPEndPoint remote)
    {
        ObjectPose msg;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // type byte already consumed by the dispatcher
            msg = ObjectPose.Read(reader);
        }

        if (!_registry.TryGetByPlayerId(msg.playerId, out var state))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        // Same spoof gate as the pose channel (§6.1) plus ownership (§6.12).
        if (state.UdpEndpoint == null || !state.UdpEndpoint.Equals(remote)
            || !_matchDirector.IsObjectOwner(msg.netId, state.PlayerId))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        var stamp = Stopwatch.GetTimestamp();
        // u16 wrap-safe ordering, per object (§6.12) — a pose is a state, so an old one is worthless.
        if (_objectSeq.TryGetValue(msg.netId, out var last)
            && StampToMs(stamp - last.Stamp) <= ObjectSeqStaleMs
            && (short)(msg.seq - last.Seq) <= 0)
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        _objectSeq[msg.netId] = (msg.seq, stamp);
        lock (_objectPoseGate) _objectPoses[msg.netId] = msg.pose;
        // Kept apart from the broadcast buffer: this one survives the tick and is where the object is
        // freed if its owner drops or dies (§10.10).
        _matchDirector.RecordObjectPose(msg.netId, msg.pose);
    }

    /// <summary>0x03 FireEvent intake (§6.4): accepted only from a 0x00-registered endpoint, exact
    /// duplicates suppressed; events passing the relay gate enter <see cref="_events"/> and go out
    /// as a 0x04 batch on the next 20 Hz tick.
    /// <para><b>Content is NOT validated</b> (§10.3): direction, distance and <c>itemId</c> are free
    /// — the server has no weapon table. The only validated thing is <b>who</b> fired.</para>
    /// <para>Rejection is silent: at 10 shots/s/player even one log line floods the console.</para></summary>
    private void HandleFireEvent(byte[] data, IPEndPoint remote)
    {
        FireEvent msg;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // type byte already consumed by the dispatcher
            msg = FireEvent.Read(reader);
        }

        if (!_registry.TryGetByPlayerId(msg.entry.playerId, out var state))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        // No event from an unregistered/foreign source — same rule as the pose path (§6.1).
        if (state.UdpEndpoint == null || !state.UdpEndpoint.Equals(remote))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        // Duplicate suppression (§6.4): UDP CAN duplicate a packet and an exact repeat shows up as a
        // double tracer + double sound. Only this thread touches these fields, so no lock.
        // ⚠️ NO ORDERING ENFORCEMENT: do NOT copy the pose filter — (short)(seq - LastSeq) <= 0 —
        // here. A pose is a STATE (latest wins, older is worthless), an event is a FACT: an
        // out-of-order shot really happened, and dropping it silently erases a tracer and a sound.
        // Only EXACT repeats are dropped.
        if (state.HasEventSeq && msg.seq == state.LastEventSeq)
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        // ---- Event channel telemetry (§6.4 "seq gap = loss") ----
        // ⚠️ Loss counting is NOT ordering enforcement: no branch below drops a packet.
        // ⚠️ The gap is capped because there is no ordering guarantee: an out-of-order event makes
        // the (ushort) delta ~65535 and an uncapped counter reports "65k lost". A delta above the
        // cap means "arrived out of order", not loss — it is not counted.
        if (state.HasEventSeq)
        {
            int gap = (ushort)(msg.seq - state.LastEventSeq);
            if (gap > 1 && gap <= EventGapMax) Interlocked.Add(ref state.EventLost, gap - 1);
        }

        Interlocked.Increment(ref state.EventAccepted);

        state.LastEventSeq = msg.seq;
        state.HasEventSeq = true;

        // Relay gate (§6.5) — all read LOCK-FREE; this path must not enter MatchDirector's _gate
        // (§10.3: it would stall the 20 Hz pose intake behind the match lock). An uncalibrated
        // player's shot is NOT relayed (§10.6): a muzzle flash on others' screens for someone who
        // cannot fire would be misleading.
        if (!state.IsConnected || state.Role != "player" || !state.Alive || !state.Calibrated) return;
        if (!_matchDirector.ShotRelayOpen) return;

        var entry = msg.entry;
        // The SERVER writes playerId: endpoint validation already bound the identity, and forwarding
        // the wire byte as-is would allow publishing events on someone else's behalf.
        entry.playerId = (byte)state.PlayerId;
        _events.Enqueue(entry);
    }

    /// <summary>20 Hz snapshot broadcast: posed and CONNECTED players go into the packet, the same
    /// buffer goes to EVERY UDP-registered, connected peer (admins included — each admin is its own
    /// target). With targets but no entries a count=0 snapshot is sent (that is how the client
    /// learns no remote avatars remain); with neither (and an empty event queue) nothing is sent and
    /// serverTick does not advance.
    /// <para>
    /// <b>Event batch (§6.5):</b> same tick, after the snapshot, same targets and same
    /// <c>serverTick</c>, as a separate 0x04 datagram — <b>only if events exist</b>.
    /// </para>
    /// <para>
    /// <b>MTU fragmentation (§6.3):</b> beyond <see cref="ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET"/>
    /// the tick splits into several datagrams (all carrying the same serverTick, all to the same
    /// targets). The client does NO reassembly and needs none: each packet applies its own entries
    /// and player dropping is a timeout decision — hence the wire format is unchanged.
    /// </para>
    /// A summary is printed once a second.</summary>
    private async Task SnapshotLoopAsync(UdpClient udp, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / ArenaProtocol.SNAPSHOT_RATE_HZ));
        uint serverTick = 0;
        var summaryDue = DateTime.UtcNow.AddSeconds(1);
        var entries = new List<SnapshotEntry>(ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET);
        var targets = new List<IPEndPoint>();
        var packets = new List<byte[]>(1);
        // Event buffer lives outside the loop to avoid a per-tick allocation (20 Hz × session).
        var eventBuffer = new List<FireEventEntry>(ArenaProtocol.EVENT_MAX_ENTRIES_PER_PACKET);
        var eventsThisSecond = 0;
        var objectBuffer = new List<ObjectPoseEntry>(ArenaProtocol.OBJECT_MAX_ENTRIES_PER_PACKET);

        // ---- Skeleton channel (§6.10) ----
        // Runs INSIDE the snapshot loop but at a SEPARATE cadence: a second timer/thread would only
        // split ownership of serverTick (the batch carries it). Integer accumulator cadence: add
        // SKELETON_RATE_HZ each tick, fire and subtract when it reaches SNAPSHOT_RATE_HZ — exactly
        // 12 times per 20 ticks, without float accumulation or drift.
        var skeletonEntries = new List<SkeletonEntry>(ArenaProtocol.SKELETON_MAX_ENTRIES_PER_PACKET);
        var skeletonPackets = new List<byte[]>(1);
        var skeletonAccumulator = 0;

        // ---- Tick drift measurement ----
        // ⚠️ Monotonic clock required: PeriodicTimer does NOT compensate for delay (a slow send tick
        // pushes the next one and that shows up as client jitter), and DateTime.UtcNow's ~15.6 ms
        // Windows resolution cannot measure the drift of a 50 ms interval.
        long lastTickStamp = 0;
        double tickDriftSumMs = 0, tickDriftMaxMs = 0, sendMaxMs = 0;
        var tickDriftSamples = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token)) break;
            }
            catch (OperationCanceledException) { break; }

            var tickStamp = Stopwatch.GetTimestamp();
            if (lastTickStamp != 0)
            {
                var drift = Math.Abs(StampToMs(tickStamp - lastTickStamp) - NominalTickMs);
                tickDriftSumMs += drift;
                tickDriftSamples++;
                if (drift > tickDriftMaxMs) tickDriftMaxMs = drift;
            }

            lastTickStamp = tickStamp;

            skeletonAccumulator += ArenaProtocol.SKELETON_RATE_HZ;
            var skeletonDue = skeletonAccumulator >= ArenaProtocol.SNAPSHOT_RATE_HZ;
            if (skeletonDue) skeletonAccumulator -= ArenaProtocol.SNAPSHOT_RATE_HZ;

            entries.Clear();
            targets.Clear();
            skeletonEntries.Clear();
            var onlinePlayers = 0;
            // ⚠️ Wall clock read ONCE per tick, not per player: no reason to judge entries of the
            // same snapshot against different instants, and UtcNow is not cheap.
            var nowUtc = DateTime.UtcNow;
            foreach (var state in _registry.Snapshot())
            {
                if (!state.IsConnected) continue;
                if (state.UdpEndpoint != null) targets.Add(state.UdpEndpoint);
                if (state.Role != "player") continue;
                onlinePlayers++;
                // flags bit0 = alive (§10.2): written under the MatchDirector lock, read lock-free
                // here (bool reads are atomic; one tick of lag is irrelevant for a snapshot).
                var alive = state.Alive;
                // flags bit6 = spawn protection (§10.4): same lock-free read. ⚠️ AND'ed with `alive`:
                // a protected dead player is meaningless and would draw the shield on a ghost.
                var spawnProtected = alive && nowUtc < state.SpawnProtectedUntil;
                lock (state.PoseGate)
                {
                    // Skeleton entry (§6.10): SAME lock as the pose, SEPARATE cadence. Collected
                    // BEFORE the pose gate — the `continue` below skips pose-less players and there
                    // is no reason for one channel to drop the other.
                    // ⚠️ Stale blobs are not broadcast: keeping a silent sender's body alive forever
                    // would spend bandwidth presenting a frozen avatar as live.
                    if (skeletonDue && state.HasSkeleton && state.LastSkeleton != null
                        && StampToMs(tickStamp - state.LastSkeletonStamp) <= SkeletonStaleMs)
                    {
                        skeletonEntries.Add(new SkeletonEntry
                        {
                            playerId = (byte)state.PlayerId,
                            // Root travels in arena space; the blob's own root is unused (§6.9).
                            root = state.LastSkeletonRoot,
                            blob = state.LastSkeleton,
                            blobLength = state.LastSkeleton.Length
                        });
                    }

                    if (!state.HasPose) continue;
                    var pose = state.LastPose;
                    entries.Add(new SnapshotEntry
                    {
                        playerId = (byte)state.PlayerId,
                        // Item bytes are client-authoritative presentation data: copied, not
                        // validated (§6.2/6.3) — the server has NO item table.
                        itemL = pose.itemL,
                        itemR = pose.itemR,
                        // ⚠️ flags is ONE byte with TWO writers: bit0 and bit6 are the server's
                        // (authoritative alive + spawn protection), bit1-5 and bit7 the client's.
                        // GRIP_FLAG_MASK is MANDATORY — copied unmasked, a client could set bit0 and
                        // declare itself alive (a dead player self-revives); bit6 is outside the
                        // mask for the same reason.
                        flags = (byte)((alive ? SnapshotEntry.FLAG_ALIVE : 0)
                                       | (spawnProtected ? SnapshotEntry.FLAG_SPAWN_PROTECTED : 0)
                                       | (pose.gripFlags & SnapshotEntry.GRIP_FLAG_MASK)),
                        head = pose.head,
                        handL = pose.handL,
                        handR = pose.handR
                    });
                }
            }

            // Idle tick — no send, no tick advance. ⚠️ Continue if the queue has events: an event's
            // wire identity is serverTick (§6.5), and waiting without advancing would put a second
            // batch on the same tick (the client would drop it as an exact repeat). There are no
            // targets in this case anyway — the events are drained below and dropped; with nobody to
            // draw them, queueing would only build up stale shot debt.
            if (entries.Count == 0 && targets.Count == 0 && _events.IsEmpty) continue;

            serverTick++;

            // ⚠️ Events are drained BEFORE the snapshot: the combine decision (§6.8) needs the event
            // count. Send order is unchanged — the snapshot still goes first.
            var eventCount = DrainEvents(eventBuffer);
            // Object poses ride the same packet (§6.12) — drained here for the same reason as the
            // events: the combine decision needs the count.
            var objectCount = DrainObjectPoses(objectBuffer);

            // §6.8 combine gate. ALL three conditions required:
            //   1) an event OR an object pose exists — otherwise nothing to combine, a plain 0x02 goes out,
            //   2) the snapshot fits one fragment — if fragmented, whichever fragment carried the
            //      event block would break the "at most one event datagram per tick" invariant (§6.5),
            //   3) total size under COMBINED_MAX_BYTES.
            // ⚠️ Failing the gate DROPS this tick's object poses: 0x02/0x04 have no object section and
            // none is added (§6.8). What is lost is the smoothness of the motion, not the final pose —
            // that arrives over WS as object_release → object_state.
            var combinedBytes = SnapshotWithEvents.HEADER_SIZE
                                + entries.Count * SnapshotEntry.SIZE
                                + eventCount * FireEventEntry.SIZE
                                + objectCount * ObjectPoseEntry.SIZE;
            var combine = (eventCount > 0 || objectCount > 0)
                          && entries.Count <= ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET
                          && combinedBytes <= ArenaProtocol.COMBINED_MAX_BYTES;

            packets.Clear();
            if (combine)
            {
                packets.Add(BuildCombinedPacket(entries, eventBuffer, objectBuffer, serverTick));
            }
            else
            {
                BuildPackets(entries, serverTick, packets);
            }

            // Send loop timed separately so the field can tell whether tick drift comes from this
            // sequential await chain or from thread scheduling.
            var sendStart = Stopwatch.GetTimestamp();

            foreach (var packet in packets)
            {
                foreach (var target in targets)
                {
                    try
                    {
                        await udp.SendAsync(packet, target, token);
                        // ⚠️ Counted per ACTUAL send (not computed as packets×targets): a send that
                        // fell into catch never left, and counting it would make telemetry lie.
                        _txSnapshotPackets++;
                        _txSnapshotBytes += packet.Length;
                        if (combine) _txCombinedPackets++;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception)
                    {
                        // On Windows an unreachable target can throw 10054 etc. — keep the loop alive.
                    }
                }
            }

            // 0x04 EventBatch — AFTER the snapshot, SAME tick and SAME targets (§6.5).
            // ⚠️ ONLY when not combined: if both 0x05 and 0x04 leave for the same tick, the client
            // drops the second as an exact repeat (identity is serverTick, §6.5).
            if (!combine && eventCount > 0)
            {
                // The shooter is NOT filtered out: it gets its own event back and ignores it (same
                // pattern as ignoring its own pose in the snapshot). A per-target batch would mean N
                // serializations per tick to save ~90 B/s per player.
                var eventPacket = BuildEventPacket(eventBuffer, serverTick);
                foreach (var target in targets)
                {
                    try
                    {
                        await udp.SendAsync(eventPacket, target, token);
                        _txEventPackets++;
                        _txEventBytes += eventPacket.Length;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception)
                    {
                        // Same reason as the snapshot broadcast — keep the loop alive.
                    }
                }
                eventsThisSecond += eventCount;
            }
            // No events, NO PACKET (§6.5): this channel goes fully silent in the lobby, countdown and
            // quiet moments. Unlike the snapshot's count=0 broadcast — there the client must clear
            // stale avatars, here there is no state to clear (events are instantaneous).

            // 0x08 SkeletonBatch (§6.10) — AFTER snapshot/events, same serverTick and same targets,
            // but only on its own cadence. No entries, no packet.
            // ⚠️ The sender is NOT filtered out: it gets its own entry back and ignores it (it draws
            // its own body from sensors). A per-target batch would mean N serializations per tick —
            // the §6.5 event batch skips filtering for the same reason.
            if (skeletonEntries.Count > 0 && targets.Count > 0)
            {
                BuildSkeletonPackets(skeletonEntries, serverTick, skeletonPackets);
                foreach (var packet in skeletonPackets)
                {
                    foreach (var target in targets)
                    {
                        try
                        {
                            await udp.SendAsync(packet, target, token);
                            _txSkeletonPackets++;
                            _txSkeletonBytes += packet.Length;
                        }
                        catch (OperationCanceledException) { return; }
                        catch (Exception)
                        {
                            // Same reason as the snapshot broadcast — keep the loop alive.
                        }
                    }
                }
            }

            var sendMs = StampToMs(Stopwatch.GetTimestamp() - sendStart);
            if (sendMs > sendMaxMs) sendMaxMs = sendMs;

            var now = DateTime.UtcNow;
            if (now >= summaryDue)
            {
                summaryDue = now.AddSeconds(1);

                var perTickBytes = 0;
                foreach (var packet in packets) perTickBytes += packet.Length;

                PrintSummary(onlinePlayers, entries.Count, targets.Count, perTickBytes, packets.Count,
                    eventsThisSecond, tickDriftSamples > 0 ? tickDriftSumMs / tickDriftSamples : 0,
                    tickDriftMaxMs, sendMaxMs);

                eventsThisSecond = 0;
                tickDriftSumMs = 0;
                tickDriftSamples = 0;
                tickDriftMaxMs = 0;
                sendMaxMs = 0;
            }
        }
    }

    private static double StampToMs(long stampDelta) => stampDelta * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Per-second telemetry summary.
    /// <para><b>Why two size figures:</b> <c>B/tick</c> is one datagram's size, <c>kB/s</c> is the
    /// real volume (multiplied by target count). At 10 players they differ by 220×, so both are
    /// printed and both are <b>labelled</b> — an unlabelled per-tick size gets read as throughput.</para>
    /// <para>Per-player lines only above the thresholds (<see cref="JitterWarnMs"/> /
    /// <see cref="LossWarnPct"/>): 10 players × per second = an unreadable console.</para>
    /// </summary>
    private void PrintSummary(int onlinePlayers, int posedPlayers, int targetCount, int perTickBytes,
        int fragments, int eventsThisSecond, double tickDriftAvgMs, double tickDriftMaxMs, double sendMaxMs)
    {
        // Only this thread touches the broadcast TX counters → plain read+reset. Ack/echo TX is
        // written from the recv thread, hence the atomic read.
        var txPackets = _txSnapshotPackets + _txEventPackets + _txSkeletonPackets
                        + Interlocked.Exchange(ref _txAckPackets, 0);
        var txBytes = _txSnapshotBytes + _txEventBytes + _txSkeletonBytes
                      + Interlocked.Exchange(ref _txAckBytes, 0);
        var txCombined = _txCombinedPackets;
        var txSkeleton = _txSkeletonPackets;
        _txSnapshotPackets = 0; _txSnapshotBytes = 0;
        _txEventPackets = 0; _txEventBytes = 0;
        _txSkeletonPackets = 0; _txSkeletonBytes = 0;
        _txCombinedPackets = 0;

        // RX counters are written by the recv thread → read-and-reset must be atomic.
        var rxPackets = Interlocked.Exchange(ref _rxPackets, 0);
        var rxBytes = Interlocked.Exchange(ref _rxBytes, 0);
        var rxRejected = Interlocked.Exchange(ref _rxRejected, 0);

        int poseAccepted = 0, poseLost = 0;
        long eventAccepted = 0, eventLost = 0;

        foreach (var state in _registry.Snapshot())
        {
            if (state.Role != "player") continue;

            int accepted, lost, jitterSamples;
            long jitterSum, jitterMax;
            lock (state.PoseGate)
            {
                accepted = state.PoseAccepted;
                lost = state.PoseLost;
                jitterSum = state.PoseJitterSumMicros;
                jitterSamples = state.PoseJitterSamples;
                jitterMax = state.PoseJitterMaxMicros;
                state.PoseAccepted = 0;
                state.PoseLost = 0;
                state.PoseJitterSumMicros = 0;
                state.PoseJitterSamples = 0;
                state.PoseJitterMaxMicros = 0;
            }

            var evAccepted = Interlocked.Exchange(ref state.EventAccepted, 0);
            var evLost = Interlocked.Exchange(ref state.EventLost, 0);

            poseAccepted += accepted;
            poseLost += lost;
            eventAccepted += evAccepted;
            eventLost += evLost;

            var jitterAvgMs = jitterSamples > 0 ? jitterSum / 1000.0 / jitterSamples : 0;
            var playerLossPct = accepted + lost > 0 ? 100.0 * lost / (accepted + lost) : 0;
            if (jitterAvgMs >= JitterWarnMs || playerLossPct >= LossWarnPct)
            {
                Console.WriteLine($"[net] {state.Name} #{state.PlayerId}: jitter ort {jitterAvgMs:0.0} ms " +
                                  $"maks {jitterMax / 1000.0:0.0} ms · poz kaybı %{playerLossPct:0.0}" +
                                  (evLost > 0 ? $" · olay kaybı {evLost}" : ""));
            }
        }

        var posePct = poseAccepted + poseLost > 0 ? 100.0 * poseLost / (poseAccepted + poseLost) : 0;
        var eventPct = eventAccepted + eventLost > 0 ? 100.0 * eventLost / (eventAccepted + eventLost) : 0;
        var fragmentNote = fragments > 1 ? $" ×{fragments} parça" : "";
        var rejectNote = rxRejected > 0 ? $" (red {rxRejected})" : "";

        Console.WriteLine(
            $"[state] oyuncu {onlinePlayers} · pozlu {posedPlayers} · hedef {targetCount}" +
            $" | paket {perTickBytes} B/tik{fragmentNote}" +
            $" | çıkış {txBytes / 1024.0:0} kB/s {txPackets} p/s" +
            $" | giriş {rxBytes / 1024.0:0} kB/s {rxPackets} p/s{rejectNote}" +
            $" | tik sapma ort {tickDriftAvgMs:0.0} maks {tickDriftMaxMs:0.0} ms (gönderim maks {sendMaxMs:0.0} ms)" +
            $" | olay {eventsThisSecond}" +
            (txCombined > 0 ? $" (birleşik {txCombined})" : "") +
            (txSkeleton > 0 ? $" | iskelet {txSkeleton} p/s" : "") +
            $" | kayıp poz %{posePct:0.0} olay %{eventPct:0.0}");
    }

    /// <summary>Drains the events that go into this tick's batch.
    /// <para>⚠️ Events over the limit (<see cref="ArenaProtocol.EVENT_MAX_ENTRIES_PER_PACKET"/>) are
    /// <b>NOT dropped; they stay queued and slide to the next tick</b>: the "at most ONE batch per
    /// tick" invariant is what the client's duplicate protection rests on (batch identity is
    /// <c>serverTick</c>, §6.5). A second datagram for the same tick would be discarded as an exact
    /// repeat — solving the overflow with "one more packet" really loses events.</para></summary>
    private int DrainEvents(List<FireEventEntry> output)
    {
        output.Clear();
        while (output.Count < ArenaProtocol.EVENT_MAX_ENTRIES_PER_PACKET
               && _events.TryDequeue(out var entry))
        {
            output.Add(entry);
        }
        return output.Count;
    }

    /// <summary>Takes this tick's object poses into the packet and CLEARS the collection (§6.12).
    /// <para>⚠️ Cleared even when the <see cref="ArenaProtocol.OBJECT_MAX_ENTRIES_PER_PACKET"/> cap cuts
    /// entries off: unlike an event, a pose is a state — carrying it to the next tick would broadcast a
    /// position the object has already left. The owner sends again 100 ms later anyway.</para></summary>
    private int DrainObjectPoses(List<ObjectPoseEntry> output)
    {
        output.Clear();
        lock (_objectPoseGate)
        {
            foreach (var pair in _objectPoses)
            {
                if (output.Count >= ArenaProtocol.OBJECT_MAX_ENTRIES_PER_PACKET) break;
                output.Add(new ObjectPoseEntry { netId = pair.Key, pose = pair.Value });
            }
            _objectPoses.Clear();
        }
        return output.Count;
    }

    /// <summary>Single 0x05 datagram: snapshot + events together (§6.8). The caller has already
    /// validated the three combine-gate conditions.</summary>
    private static byte[] BuildCombinedPacket(List<SnapshotEntry> entries,
        List<FireEventEntry> events, List<ObjectPoseEntry> objects, uint serverTick)
    {
        var combined = new SnapshotWithEvents
        {
            serverTick = serverTick,
            players = entries.ToArray(),
            events = events.ToArray(),
            objects = objects.ToArray()
        };
        using var ms = new MemoryStream(SnapshotWithEvents.HEADER_SIZE
                                        + entries.Count * SnapshotEntry.SIZE
                                        + events.Count * FireEventEntry.SIZE
                                        + objects.Count * ObjectPoseEntry.SIZE);
        using var writer = new BinaryWriter(ms);
        combined.Write(writer);
        return ms.ToArray();
    }

    /// <summary>
    /// Splits skeleton entries into datagrams (§6.10). The only difference from snapshot
    /// fragmentation (<see cref="BuildPackets"/>) is that entries are <b>variable length</b>: the
    /// split follows a <b>byte budget</b> (<see cref="ArenaProtocol.COMBINED_MAX_BYTES"/>), not an
    /// entry count; the entry cap only exists because <c>count</c> is a <c>u8</c>.
    /// <para>⚠️ One more difference: <b>no empty packet when there are no entries</b>. An empty
    /// snapshot is a notification (the client clears stale avatars with it); an empty skeleton batch
    /// has no counterpart — avatar lifetime comes from the snapshot (§6.3), the body just freezes on
    /// its last frame.</para>
    /// <para>⚠️ A single entry exceeding the budget is still sent in <b>its own packet</b>: the send
    /// side already enforces <see cref="ArenaProtocol.SKELETON_MAX_BLOB_BYTES"/>, so this only arises
    /// if the limit grows — and silently dropping the entry would lose the body permanently.</para>
    /// </summary>
    private static void BuildSkeletonPackets(List<SkeletonEntry> entries, uint serverTick, List<byte[]> output)
    {
        output.Clear();

        var offset = 0;
        while (offset < entries.Count)
        {
            var bytes = SkeletonBatch.HEADER_SIZE;
            var count = 0;

            while (offset + count < entries.Count
                   && count < ArenaProtocol.SKELETON_MAX_ENTRIES_PER_PACKET)
            {
                var size = entries[offset + count].Size;
                // The first entry is taken unconditionally: otherwise an oversized blob would spin
                // forever (a packet that never takes an entry).
                if (count > 0 && bytes + size > ArenaProtocol.COMBINED_MAX_BYTES) break;
                bytes += size;
                count++;
            }

            var chunk = new SkeletonEntry[count];
            entries.CopyTo(offset, chunk, 0, count);

            using var ms = new MemoryStream(bytes);
            using var writer = new BinaryWriter(ms);
            SkeletonBatch.Write(writer, serverTick, chunk, 0, count);
            output.Add(ms.ToArray());

            offset += count;
        }
    }

    /// <summary>Single 0x04 datagram: 6 + count×9 B (§6.5).</summary>
    private static byte[] BuildEventPacket(List<FireEventEntry> events, uint serverTick)
    {
        var batch = new EventBatch { serverTick = serverTick, events = events.ToArray() };
        using var ms = new MemoryStream(6 + events.Count * FireEventEntry.SIZE);
        using var writer = new BinaryWriter(ms);
        batch.Write(writer);
        return ms.ToArray();
    }

    /// <summary>
    /// Splits entries into MTU-sized datagrams (§6.3). With no entries it emits a single count=0
    /// packet so clients can still clear stale avatars. All fragments carry the same
    /// <paramref name="serverTick"/>.
    /// </summary>
    private static void BuildPackets(List<SnapshotEntry> entries, uint serverTick, List<byte[]> output)
    {
        output.Clear();
        var perPacket = ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET;

        for (var offset = 0; offset == 0 || offset < entries.Count; offset += perPacket)
        {
            var count = Math.Min(perPacket, entries.Count - offset);
            var chunk = new SnapshotEntry[count];
            entries.CopyTo(offset, chunk, 0, count);

            var snapshot = new Snapshot { serverTick = serverTick, players = chunk };
            using var ms = new MemoryStream(6 + count * SnapshotEntry.SIZE);
            using var writer = new BinaryWriter(ms);
            snapshot.Write(writer);
            output.Add(ms.ToArray());
        }
    }
}
