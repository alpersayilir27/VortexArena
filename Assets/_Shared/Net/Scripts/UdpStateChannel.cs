using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// UDP 47822 pose channel: registers with a 0x00 UdpHello carrying welcome's udpToken, retried every
    /// 1 s until the server echoes the same 6 bytes (ack). Once registered it sends the arena-space
    /// poses from IPoseSource as 20 Hz PoseUpdate (0x01) and forwards incoming Snapshots (0x02) to
    /// RemotePlayerRegistry. Shot/throw events (0x03) go out IMMEDIATELY and incoming event batches
    /// (0x04) are published as NetEvents.OnRemoteFireEvent (§6.4/6.5). Managed by ArenaClient.
    /// </summary>
    public class UdpStateChannel : MonoBehaviour
    {
        // §6.1: retry every 1 s until acked (no dedicated constant in ArenaProtocol).
        private const float HelloRetryIntervalSeconds = 1f;

        // 20 Hz send interval (constant-folded: both operands are const).
        private const float PoseSendInterval = 1f / ArenaProtocol.POSE_RATE_HZ;

        /// <summary>Has the server registered our UDP endpoint (ack received)?</summary>
        public bool Registered { get; private set; }

        /// <summary>Raised once on the main thread when registration completes.</summary>
        public event Action OnRegistered;

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

        private UdpClient _udp;
        private IPEndPoint _serverEndpoint;
        private CancellationTokenSource _cts;
        private byte _playerId;
        private uint _udpToken;
        private volatile bool _acked;

        // ---- 20 Hz pose sending (main thread only) ----
        private IPoseSource _poseSource;
        private ushort _seq;
        private float _sendAccumulator;
        private byte[] _sendBuffer;
        private MemoryStream _sendStream;
        private BinaryWriter _sendWriter;
        private bool _sendWarned;

        // ---- 0x03 event sending (main thread only) ----
        // ⚠️ SEPARATE from the pose buffer: an event goes out IMMEDIATELY and can land mid pose write;
        // on a shared stream the two would clobber each other's position. Allocating per event at
        // 10 events/s would also be pointless GC.
        private byte[] _eventBuffer;
        private MemoryStream _eventStream;
        private BinaryWriter _eventWriter;
        private bool _eventSendWarned;

        // ⚠️ SEPARATE counter from the pose _seq: pose seq enforces order (state — last one wins),
        // event seq only suppresses duplicates (§6.4). Merged into one, pose loss would gap the event
        // numbers and the server's loss measurement would lie.
        private ushort _eventSeq;

        // ---- 0x07 skeleton sending (main thread only) ----
        // ⚠️ SEPARATE from the pose/event buffers and much larger (variable-length blob, §6.9).
        // ⚠️ This channel is PUSH, not PULL — the cadence comes from the Movement SDK (its own
        // keyframe/interval logic), which calls SendSkeleton when a frame is ready. Imposing our own
        // rate would arbitrarily drop frames the SDK produced.
        private byte[] _skeletonBuffer;
        private MemoryStream _skeletonStream;
        private BinaryWriter _skeletonWriter;
        private ushort _skeletonSeq;
        private bool _skeletonSendWarned;
        private bool _skeletonSizeWarned;
        private float _lastSkeletonSendTime = float.NegativeInfinity;

        /// <summary>The corrupt-datagram warning is logged once — a broken sender can emit dozens of
        /// packets a second and flooding the console does not help diagnosis. ⚠️ NOT reset on
        /// reconnect: the first instance already stated the reason.</summary>
        private bool _datagramErrorWarned;

        /// <summary>
        /// Minimum skeleton send interval — a <b>safety valve</b>, not the cadence source.
        /// <para>The SDK component sets the cadence (configured on the prefab); a misconfigured prefab
        /// could emit a packet per frame and burn the §3.12 packet budget alone. Slightly looser than
        /// <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>: exactly equal, timer jitter would drop
        /// legitimate frames.</para>
        /// </summary>
        private const float SkeletonMinSendInterval = 0.9f / ArenaProtocol.SKELETON_RATE_HZ;

        // ---- 0x04 batch receive (network thread) ----
        // Ring of the last processed ticks: a batch is identified by serverTick and at most one batch
        // is produced per tick (§6.5). The ring drops EXACT REPEATS only.
        private readonly uint[] _seenTicks = new uint[ArenaProtocol.EVENT_TICK_HISTORY];
        private readonly bool[] _seenTicksValid = new bool[ArenaProtocol.EVENT_TICK_HISTORY];
        private int _seenTicksNext;

        // ---- Net telemetry (§6.7) — measured ENTIRELY on the client ----
        // ⚠️ A high-resolution monotonic clock is required: Environment.TickCount resolves to ~10-16 ms
        // while the expected LAN RTT is 5-15 ms — measuring with it yields only noise.
        // (System.Diagnostics is fully qualified: a `using` would collide Debug with UnityEngine.Debug
        // and break every Debug.Log line in this file.)
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        // §6.7: RTT probe at 1 Hz. ⚠️ NEVER RAISED — each probe costs 2 datagrams (out + echo) and this
        // product's bottleneck is packet count, not bandwidth. Jitter already comes from snapshot
        // arrivals at 20 Hz with zero extra packets; this packet only feeds the operator's ping number.
        private const float RttProbeIntervalSeconds = 1f;

        /// <summary>Lock for the telemetry fields: net thread writes, main thread (status assembly)
        /// reads. Contention is negligible at 20 Hz writes / 0.2 Hz reads.</summary>
        private readonly object _telemetryGate = new object();

        private float _probeAccumulator;
        private byte[] _probeBuffer;
        private MemoryStream _probeStream;
        private BinaryWriter _probeWriter;

        /// <summary>The pending probe's on-wire nonce and its local high-resolution stamp. Only ONE
        /// probe is in flight at a time (1 Hz sends, RTT ≪ 1 s) so no ring is needed; the nonce only
        /// filters out a stale echo.</summary>
        private uint _probeNonce;
        private long _probeSentTicks;
        private bool _probePending;

        private int _rttMs = -1;
        private float _jitterMs = -1f;

        /// <summary>Downlink snapshot arrival stamp (high resolution) and the last serverTick seen.</summary>
        private long _lastSnapshotTicks;
        private uint _lastServerTick;
        private bool _hasServerTick;

        private int _snapshotsReceived;
        private int _snapshotsLost;

        private void Awake()
        {
            // Preallocated send buffers: the buffer stays, the stream is reused by resetting Position
            // per send (no per-frame GC).
            _sendBuffer = new byte[PoseUpdate.SIZE];
            _sendStream = new MemoryStream(_sendBuffer, 0, _sendBuffer.Length, true);
            _sendWriter = new BinaryWriter(_sendStream);

            _eventBuffer = new byte[FireEvent.SIZE];
            _eventStream = new MemoryStream(_eventBuffer, 0, _eventBuffer.Length, true);
            _eventWriter = new BinaryWriter(_eventStream);

            _probeBuffer = new byte[RttProbe.SIZE];
            _probeStream = new MemoryStream(_probeBuffer, 0, _probeBuffer.Length, true);
            _probeWriter = new BinaryWriter(_probeStream);

            // The blob is variable length but its ceiling is fixed — allocate once for the largest
            // legitimate packet and send only the real length each time.
            _skeletonBuffer = new byte[SkeletonUpdate.HEADER_SIZE + ArenaProtocol.SKELETON_MAX_BLOB_BYTES];
            _skeletonStream = new MemoryStream(_skeletonBuffer, 0, _skeletonBuffer.Length, true);
            _skeletonWriter = new BinaryWriter(_skeletonStream);
        }

        /// <summary>
        /// MAIN THREAD: reads measured telemetry and resets the window counters (§6.7); called by
        /// <c>ArenaClient</c> while assembling <c>status</c>.
        /// <para>RTT and jitter are <b>continuous</b> (EWMA, never reset), loss is per window: the
        /// denominator must be "the last measurement window", otherwise one loss at session start
        /// pollutes the percentage for hours.</para>
        /// </summary>
        public void SampleTelemetry(out int rttMs, out float jitterMs, out float lossPct)
        {
            lock (_telemetryGate)
            {
                rttMs = _rttMs;
                jitterMs = _jitterMs;

                int total = _snapshotsReceived + _snapshotsLost;
                lossPct = total > 0 ? 100f * _snapshotsLost / total : -1f;

                _snapshotsReceived = 0;
                _snapshotsLost = 0;
            }
        }

        /// <summary>
        /// Sets the pose source (App's PlayerPoseTracker calls it in Start; calibration is not awaited).
        /// The source is NOT cleared by Stop(): after a reconnect sending resumes by itself once
        /// registration completes.
        /// </summary>
        public void SetPoseSource(IPoseSource source)
        {
            _poseSource = source;
        }

        /// <summary>Clears only if the registered source is the given one (scene teardown safety).</summary>
        public void ClearPoseSource(IPoseSource source)
        {
            if (ReferenceEquals(_poseSource, source))
            {
                _poseSource = null;
            }
        }

        /// <summary>(Re)starts registration; closes a previous session if there is one.</summary>
        public void StartRegistration(string serverIp, int statePort, byte playerId, uint udpToken)
        {
            Stop();

            IPAddress address;
            if (string.IsNullOrWhiteSpace(serverIp) || !IPAddress.TryParse(serverIp, out address))
            {
                Debug.LogWarning($"[UdpStateChannel] Geçersiz sunucu IP'si: '{serverIp}'; UDP kaydı yapılamadı.");
                return;
            }

            _serverEndpoint = new IPEndPoint(address, statePort);
            _playerId = playerId;
            _udpToken = udpToken;
            _acked = false;
            Registered = false;
            _sendAccumulator = 0f;
            _sendWarned = false; // a new session may log the send warning again
            _eventSendWarned = false;
            _skeletonSendWarned = false;
            _lastSkeletonSendTime = float.NegativeInfinity;
            // ⚠️ _skeletonSizeWarned is NOT reset: an oversized blob is a CONFIGURATION error (prefab
            // compression/joint list), not a network one, and reconnecting does not fix it.

            // New session = new server tick axis and new network path → telemetry is reset.
            // ⚠️ Carrying _lastServerTick over would make the first snapshots look like they "went
            // backwards" after a server restart and the loss percentage would lie.
            lock (_telemetryGate)
            {
                _rttMs = -1;
                _jitterMs = -1f;
                _lastSnapshotTicks = 0;
                _hasServerTick = false;
                _lastServerTick = 0;
                _snapshotsReceived = 0;
                _snapshotsLost = 0;
                _probePending = false;
            }

            _probeAccumulator = 0f;

            // Same reason for the seen-tick ring: after a server restart the old ring could mark this
            // session's ticks as already "seen" and the first batches would drop silently.
            Array.Clear(_seenTicksValid, 0, _seenTicksValid.Length);
            _seenTicksNext = 0;

            try
            {
                _udp = new UdpClient(0); // bind an ephemeral port right away (required to receive)
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UdpStateChannel] UDP soketi açılamadı: {e.Message}");
                _udp = null;
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            UdpClient udp = _udp;

            _ = Task.Run(() => ReceiveLoopAsync(udp, token));
            _ = Task.Run(() => SendHelloLoopAsync(udp, token));
        }

        /// <summary>Closes the channel; ArenaClient calls it on disconnect (rebuilt on the next welcome).</summary>
        public void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (Exception)
            {
                // Swallow it when the CTS is already disposed.
            }

            _cts = null;

            if (_udp != null)
            {
                try
                {
                    _udp.Close();
                }
                catch (Exception)
                {
                    // Swallow it when the socket is already closed.
                }

                _udp = null;
            }

            _acked = false;
            Registered = false;
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UdpStateChannel] Ana thread aksiyonu hata verdi: {e}");
                }
            }

            SendPoseIfDue();
            SendRttProbeIfDue();
        }

        /// <summary>MAIN THREAD: 1 Hz RTT probe (§6.7); nothing is sent before registration.
        /// <para>An unanswered probe is <b>not timed out</b>: RTT keeps showing the last SUCCESSFUL
        /// measurement, because loss has its own indicator — blanking the ping while <c>lossPct</c> is
        /// already falling would tell the operator the same thing twice and flicker the row.</para></summary>
        private void SendRttProbeIfDue()
        {
            if (!Registered || _udp == null)
            {
                _probeAccumulator = 0f;
                return;
            }

            _probeAccumulator += Time.unscaledDeltaTime;
            if (_probeAccumulator < RttProbeIntervalSeconds)
            {
                return;
            }

            _probeAccumulator = 0f;

            long sentTicks = _clock.ElapsedTicks;
            // Nonce = the send moment in ms; enough to filter a stale echo (only one probe in flight).
            // The server does NOT read it, it echoes it verbatim.
            uint nonce = unchecked((uint)_clock.ElapsedMilliseconds);

            lock (_telemetryGate)
            {
                _probeNonce = nonce;
                _probeSentTicks = sentTicks;
                _probePending = true;
            }

            var probe = new RttProbe { playerId = _playerId, clientStamp = nonce };

            try
            {
                _probeStream.Position = 0;
                probe.Write(_probeWriter);
                _probeWriter.Flush();
                _udp.Send(_probeBuffer, RttProbe.SIZE, _serverEndpoint);
            }
            catch (Exception)
            {
                // A lost probe is harmless: another goes out next second. Not even a warning — telemetry
                // is not worth log noise.
            }
        }

        /// <summary>
        /// MAIN THREAD: sends the retargeted skeleton blob + the character root's <b>arena space</b>
        /// pose as <c>0x07</c> (§6.9); a silent no-op before registration.
        /// <para><b>PUSH gate:</b> the cadence comes from the Movement SDK, not this class — the caller
        /// hands over a frame when the SDK produces one. <see cref="SkeletonMinSendInterval"/> is only a
        /// safety net against a runaway cadence.</para>
        /// <para>⚠️ <paramref name="arenaRoot"/> is <b>mandatory and replaces the root inside the
        /// blob</b>: the SDK writes the root joint in the sender's world space, unrelated to the
        /// receiver's arena (§6.9). The caller does the transform — the Net layer does not see
        /// <c>ArenaSpace</c>, same as on the pose channel (see <see cref="IPoseSource"/>).</para>
        /// <para>⚠️ A blob over <see cref="ArenaProtocol.SKELETON_MAX_BLOB_BYTES"/> is <b>NOT SENT</b>:
        /// there is no fragmentation on this channel, and sending it would mean trusting IP
        /// fragmentation (losing one fragment throws away the whole frame).</para>
        /// </summary>
        public void SendSkeleton(byte[] blob, int length, Pose arenaRoot)
        {
            if (!Registered || _udp == null || blob == null || length <= 0)
            {
                return;
            }

            if (length > ArenaProtocol.SKELETON_MAX_BLOB_BYTES)
            {
                if (!_skeletonSizeWarned)
                {
                    _skeletonSizeWarned = true;
                    Debug.LogWarning(
                        $"[UdpStateChannel] İskelet blob'u {length} B — tavan " +
                        $"{ArenaProtocol.SKELETON_MAX_BLOB_BYTES} B (§6.9). Kare gönderilmedi: bu kanalda " +
                        "parçalama yoktur. Sıkıştırmayı yükselt ya da eklem listesini daralt " +
                        "(parmak eklemleri kumandayla oynanırken gerçek veri taşımaz).");
                }

                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastSkeletonSendTime < SkeletonMinSendInterval)
            {
                return;
            }

            _lastSkeletonSendTime = now;

            var update = new SkeletonUpdate
            {
                playerId = _playerId,
                seq = _skeletonSeq++,
                root = ToPoseData(arenaRoot),
                blob = blob,
                blobLength = length
            };

            try
            {
                _skeletonStream.Position = 0;
                update.Write(_skeletonWriter);
                _skeletonWriter.Flush();
                _udp.Send(_skeletonBuffer, SkeletonUpdate.HEADER_SIZE + length, _serverEndpoint);
            }
            catch (Exception e)
            {
                // Same reasoning as the pose path: swallow + one spam-free warning.
                if (!_skeletonSendWarned)
                {
                    _skeletonSendWarned = true;
                    Debug.LogWarning($"[UdpStateChannel] SkeletonUpdate gönderimi başarısız: {e.Message}");
                }
            }
        }

        /// <summary>MAIN THREAD: sends a 20 Hz PoseUpdate once registered and the pose source is ready.</summary>
        private void SendPoseIfDue()
        {
            if (!Registered || _poseSource == null || _udp == null)
            {
                _sendAccumulator = 0f;
                return;
            }

            _sendAccumulator += Time.unscaledDeltaTime;
            if (_sendAccumulator < PoseSendInterval)
            {
                return;
            }

            // One packet is enough after a frame hitch — clamp the accumulator with a modulo.
            _sendAccumulator %= PoseSendInterval;

            if (!_poseSource.TryGetArenaPoses(out Pose head, out Pose handL, out Pose handR))
            {
                return; // tracking not ready yet (e.g. HMD asleep)
            }

            // §6.2: item bytes ride the SAME packet as the pose (same authority — client-authoritative
            // presentation info). The source resolves them; the Net layer knows no item table.
            _poseSource.GetHeldItems(out byte itemL, out byte itemR, out byte gripFlags);

            var update = new PoseUpdate
            {
                playerId = _playerId,
                seq = _seq++,
                clientTimeMs = (uint)Environment.TickCount,
                itemL = itemL,
                itemR = itemR,
                gripFlags = gripFlags,
                head = ToPoseData(head),
                handL = ToPoseData(handL),
                handR = ToPoseData(handR)
            };

            try
            {
                _sendStream.Position = 0;
                update.Write(_sendWriter);
                _sendWriter.Flush();
                _udp.Send(_sendBuffer, PoseUpdate.SIZE, _serverEndpoint);
            }
            catch (Exception e)
            {
                // Swallow + one spam-free warning (reset on re-registration); UDP is lossy anyway.
                if (!_sendWarned)
                {
                    _sendWarned = true;
                    Debug.LogWarning($"[UdpStateChannel] PoseUpdate gönderimi başarısız: {e.Message}");
                }
            }
        }

        /// <summary>
        /// §6.4: sends a shot/throw event. <b>Goes out IMMEDIATELY</b> (no pose tick wait — waiting would
        /// add 0–50 ms between the local trigger and the relay for nothing). A silent no-op before
        /// registration.
        /// </summary>
        /// <param name="kind"><c>FireEventEntry.KIND_SHOT</c> / <c>KIND_THROW</c>.</param>
        /// <param name="rightHand">Did the event come from the right hand.</param>
        /// <param name="itemId">The item's <c>netItemId</c> (§6.6); 0 = unresolved.</param>
        /// <param name="arenaDirection">Aim direction in <b>ARENA space</b> — the world→arena conversion
        /// is the CALLER's job (the Net layer does not know the transform). Need not be normalised.</param>
        /// <param name="magnitudeMeters">Per kind: shot distance (m) or throw initial speed (m/s).</param>
        public void SendFireEvent(byte kind, bool rightHand, byte itemId, Vector3 arenaDirection, float magnitudeMeters)
        {
            if (!Registered || _udp == null)
            {
                return; // not registered yet: no endpoint to send the event to
            }

            OctahedralDirection.Encode(
                arenaDirection.x, arenaDirection.y, arenaDirection.z,
                out short dirOctX, out short dirOctY);

            var entry = new FireEventEntry
            {
                playerId = _playerId,
                kindHand = FireEventEntry.PackKindHand(kind, rightHand),
                itemId = itemId,
                dirOctX = dirOctX,
                dirOctY = dirOctY,
                magnitude = ToMagnitudeCm(magnitudeMeters)
            };

            var msg = new FireEvent { seq = _eventSeq++, entry = entry };

            try
            {
                _eventStream.Position = 0;
                msg.Write(_eventWriter);
                _eventWriter.Flush();
                _udp.Send(_eventBuffer, FireEvent.SIZE, _serverEndpoint);
            }
            catch (Exception e)
            {
                // As on the pose path: swallow + one spam-free warning (reset on re-registration).
                if (!_eventSendWarned)
                {
                    _eventSendWarned = true;
                    Debug.LogWarning($"[UdpStateChannel] FireEvent gönderimi başarısız: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Metres → cm, <b>clamped</b> to u16. No wraparound: a 700 m distance showing as 4400 cm would
        /// draw a stubby backwards tracer, whereas clamping costs at worst a slightly short one.
        /// A negative value (a faulty call) falls to 0.
        /// </summary>
        private static ushort ToMagnitudeCm(float meters)
        {
            if (!(meters > 0f))
            {
                return 0; // NaN lands here too (the comparison is false)
            }

            double cm = Math.Round(meters * 100.0);
            return cm >= ushort.MaxValue ? ushort.MaxValue : (ushort)cm;
        }

        private static PoseData ToPoseData(in Pose pose)
        {
            PoseData data;
            data.px = pose.position.x;
            data.py = pose.position.y;
            data.pz = pose.position.z;
            data.qx = pose.rotation.x;
            data.qy = pose.rotation.y;
            data.qz = pose.rotation.z;
            data.qw = pose.rotation.w;
            return data;
        }

        private void OnDestroy()
        {
            Stop();
        }

        // ------------------------------------------------------------- loops

        private async Task SendHelloLoopAsync(UdpClient udp, CancellationToken ct)
        {
            byte[] packet;
            using (var stream = new MemoryStream(UdpHello.SIZE))
            using (var writer = new BinaryWriter(stream))
            {
                var hello = new UdpHello { playerId = _playerId, udpToken = _udpToken };
                hello.Write(writer);
                packet = stream.ToArray();
            }

            try
            {
                while (!ct.IsCancellationRequested && !_acked)
                {
                    await udp.SendAsync(packet, packet.Length, _serverEndpoint);
                    await Task.Delay(TimeSpan.FromSeconds(HelloRetryIntervalSeconds), ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Stop() was called.
            }
            catch (ObjectDisposedException)
            {
                // The socket was closed.
            }
            catch (Exception e)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning($"[UdpStateChannel] UdpHello gönderimi başarısız: {e.Message}");
                }
            }
        }

        private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    UdpReceiveResult datagram = await udp.ReceiveAsync();

                    // ⚠️ Per-datagram isolation: without this try, a corrupt/truncated packet throwing
                    // during parse (BinaryReader throws EndOfStreamException at stream end) would fall
                    // into the outer catch and KILL THE WHOLE RECEIVE LOOP — the client would silently
                    // freeze, receiving no snapshot/skeleton again. Dropping one packet is correct: this
                    // is a state channel and the next tick fills the gap.
                    try
                    {
                        HandleDatagram(datagram.Buffer);
                    }
                    catch (Exception e)
                    {
                        if (!_datagramErrorWarned)
                        {
                            _datagramErrorWarned = true;
                            Debug.LogWarning(
                                $"[UdpStateChannel] Bozuk datagram düşürüldü: {e.Message}. " +
                                "Bu uyarı bir kez basılır; alım sürüyor.");
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Stop() closed the socket — a normal exit.
            }
            catch (SocketException)
            {
                // Expected on shutdown; a reconnect builds a new channel.
            }
            catch (Exception e)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning($"[UdpStateChannel] UDP alım hatası: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Was this batch tick processed within the last <see cref="ArenaProtocol.EVENT_TICK_HISTORY"/>
        /// ticks (§6.5 duplicate suppression)? The ring is read/written from the receive thread only, so
        /// no lock; the other writer, <see cref="StartRegistration"/>, clears it AFTER the receive loop
        /// is cancelled.
        /// </summary>
        private bool WasTickSeen(uint serverTick)
        {
            for (int i = 0; i < _seenTicks.Length; i++)
            {
                if (_seenTicksValid[i] && _seenTicks[i] == serverTick)
                {
                    return true;
                }
            }

            return false;
        }

        private float TicksToMs(long tickDelta)
            => (float)(tickDelta * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

        /// <summary>
        /// NETWORK THREAD: measures downlink jitter and snapshot loss from the incoming stream (§6.7) —
        /// <b>no extra packets</b>, it is a by-product of the 20 Hz snapshot already received.
        /// <para>⚠️ <b>Parts of the same tick do not count</b> (§6.3 MTU split): counting each part as a
        /// separate "arrival" would pull jitter to 0 and loss onto the wrong denominator.</para>
        /// <para>⚠️ <b>A backwards tick is ignored:</b> UDP may reorder and an "old tick" is not a loss.
        /// Loss is counted from FORWARD gaps only.</para>
        /// </summary>
        private void TrackDownlink(uint serverTick)
        {
            long nowTicks = _clock.ElapsedTicks;

            lock (_telemetryGate)
            {
                if (_hasServerTick)
                {
                    // Same tick = the second datagram of a split snapshot → not measured.
                    if (serverTick == _lastServerTick)
                    {
                        return;
                    }

                    long advance = (long)serverTick - _lastServerTick;
                    if (advance < 0)
                    {
                        return; // arrived out of order; not a loss
                    }

                    if (advance > 1)
                    {
                        _snapshotsLost += (int)(advance - 1);
                    }

                    if (_lastSnapshotTicks != 0)
                    {
                        float intervalMs = TicksToMs(nowTicks - _lastSnapshotTicks);
                        // Loss stretches the interval by whole multiples; without scaling the expected
                        // interval by the gap, loss would be reported a second time as jitter.
                        float expectedMs = 1000f / ArenaProtocol.SNAPSHOT_RATE_HZ * advance;
                        float deviation = Mathf.Abs(intervalMs - expectedMs);
                        _jitterMs = _jitterMs < 0f ? deviation : _jitterMs * 0.9f + deviation * 0.1f;
                    }
                }

                _snapshotsReceived++;
                _lastServerTick = serverTick;
                _hasServerTick = true;
                _lastSnapshotTicks = nowTicks;
            }
        }

        /// <summary>Writes the tick into the ring, over the oldest one (fixed memory, no GC).</summary>
        private void MarkTickSeen(uint serverTick)
        {
            _seenTicks[_seenTicksNext] = serverTick;
            _seenTicksValid[_seenTicksNext] = true;
            _seenTicksNext = (_seenTicksNext + 1) % _seenTicks.Length;
        }

        /// <summary>
        /// NETWORK THREAD: applies a tick's shot/throw events. The <b>shared</b> path of <c>0x04</c> and
        /// <c>0x05</c> (§6.5/6.8) — both MUST use the same tick ring; a separate one would play the same
        /// tick twice (double tracer + double sound).
        /// </summary>
        private void DispatchFireEvents(uint serverTick, FireEventEntry[] events)
        {
            // §6.5: duplicate protection is the TICK, not seq — a second sighting of the same
            // serverTick drops the whole block (UDP may duplicate → double tracer).
            // ⚠️ NO ORDER ENFORCEMENT: never write "tick < lastTick → drop". That is a POSE rule
            // (state: last one wins) and copying it here is the easiest mistake to make. An old but
            // UNSEEN block IS PLAYED — a tracer ~50 ms late beats a lost one.
            if (WasTickSeen(serverTick))
            {
                return;
            }

            MarkTickSeen(serverTick);

            if (events == null)
            {
                return;
            }

            for (int i = 0; i < events.Length; i++)
            {
                FireEventEntry e = events[i];

                // §6.5: the shooter gets its own event back and ignores it here (the server builds no
                // per-target block) — the same pattern as ignoring its own pose in the snapshot.
                if (e.playerId == _playerId)
                {
                    continue;
                }

                OctahedralDirection.Decode(e.dirOctX, e.dirOctY,
                    out float dx, out float dy, out float dz);

                var evt = new RemoteFireEvent
                {
                    playerId = e.playerId,
                    kind = e.Kind,
                    rightHand = e.IsRightHand,
                    itemId = e.itemId,
                    arenaDirection = new Vector3(dx, dy, dz),
                    magnitude = e.magnitude / 100f, // cm on the wire (§6.4)
                    serverTick = serverTick
                };

                // WE ARE ON THE NETWORK THREAD: publishing moves to the main thread (listeners touch the
                // scene / Unity API).
                _mainThreadActions.Enqueue(() => NetEvents.RaiseRemoteFireEvent(evt));
            }
        }

        /// <summary>Runs on the network thread; events move to the main thread through the queue.</summary>
        private void HandleDatagram(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 1)
            {
                return;
            }

            using (var reader = new BinaryReader(new MemoryStream(buffer)))
            {
                byte packetType = reader.ReadByte();
                switch (packetType)
                {
                    case UdpPacketType.UdpHello:
                        if (buffer.Length < UdpHello.SIZE)
                        {
                            return;
                        }

                        UdpHello ack = UdpHello.Read(reader);
                        if (ack.playerId != _playerId || ack.udpToken != _udpToken || _acked)
                        {
                            return;
                        }

                        _acked = true;
                        _mainThreadActions.Enqueue(() =>
                        {
                            Registered = true;
                            Debug.Log("[UdpStateChannel] UDP kaydı tamamlandı (ack alındı).");
                            OnRegistered?.Invoke();
                        });
                        break;

                    case UdpPacketType.Snapshot:
                    {
                        // 1(type) + 1(playerCount) + 4(serverTick) + n×88 — ignore a short packet.
                        if (buffer.Length < 6 || buffer.Length < 6 + buffer[1] * SnapshotEntry.SIZE)
                        {
                            return;
                        }

                        Snapshot snap = Snapshot.Read(reader);
                        // §6.7: downlink jitter and loss are measured from THIS stream — no extra packets.
                        TrackDownlink(snap.serverTick);
                        // NETWORK THREAD: the registry ingests under a lock and publishes on the main thread.
                        RemotePlayerRegistry.Instance?.IngestFromNetThread(snap, Environment.TickCount, _playerId);
                        break;
                    }

                    case UdpPacketType.RttProbe:
                    {
                        if (buffer.Length < RttProbe.SIZE)
                        {
                            return;
                        }

                        RttProbe echo = RttProbe.Read(reader);
                        long nowTicks = _clock.ElapsedTicks;

                        lock (_telemetryGate)
                        {
                            // Filter a stale/foreign echo: only the pending probe's nonce counts.
                            if (!_probePending || echo.clientStamp != _probeNonce)
                            {
                                return;
                            }

                            _probePending = false;

                            float rtt = TicksToMs(nowTicks - _probeSentTicks);
                            // EWMA so one late echo does not jump the readout. The first measurement is
                            // written directly, otherwise it would crawl up from -1 showing a wrong value.
                            _rttMs = _rttMs < 0
                                ? Mathf.RoundToInt(rtt)
                                : Mathf.RoundToInt(_rttMs * 0.7f + rtt * 0.3f);
                        }

                        break;
                    }

                    case UdpPacketType.EventBatch:
                    {
                        // 1(type) + 1(count) + 4(serverTick) + n×9 — ignore a short packet.
                        if (buffer.Length < 6 || buffer.Length < 6 + buffer[1] * FireEventEntry.SIZE)
                        {
                            return;
                        }

                        EventBatch batch = EventBatch.Read(reader);
                        DispatchFireEvents(batch.serverTick, batch.events);
                        break;
                    }

                    case UdpPacketType.SnapshotWithEvents:
                    {
                        // 1(type) + 1(playerCount) + 1(eventCount) + 4(serverTick) + n×88 + m×9
                        if (buffer.Length < SnapshotWithEvents.HEADER_SIZE
                            || buffer.Length < SnapshotWithEvents.HEADER_SIZE
                                               + buffer[1] * SnapshotEntry.SIZE
                                               + buffer[2] * FireEventEntry.SIZE)
                        {
                            return;
                        }

                        SnapshotWithEvents combined = SnapshotWithEvents.Read(reader);

                        // ⚠️ Downlink measurement MUST count 0x05 too (§6.7): otherwise loss reads 100%
                        // the moment combining kicks in.
                        TrackDownlink(combined.serverTick);

                        // Snapshot block: identical handling to 0x02. Reapplying state on a repeated
                        // tick (UDP may duplicate) is harmless — last one wins.
                        RemotePlayerRegistry.Instance?.IngestFromNetThread(
                            new Snapshot { serverTick = combined.serverTick, players = combined.players },
                            Environment.TickCount, _playerId);

                        // Event block: goes through the SAME code and the SAME tick ring as 0x04 (§6.8).
                        DispatchFireEvents(combined.serverTick, combined.events);
                        break;
                    }

                    case UdpPacketType.SkeletonBatch:
                    {
                        // 1(type) + 1(count) + 4(serverTick) + variable entries. Because the entries are
                        // variable length no exact lower bound past the header exists; require at least
                        // one entry's fixed part and let Read's bounds check handle the rest (a
                        // truncated blob comes back empty and the entry drops).
                        if (buffer.Length < SkeletonBatch.HEADER_SIZE
                            || (buffer[1] > 0 && buffer.Length < SkeletonBatch.HEADER_SIZE + SkeletonEntry.HEADER_SIZE))
                        {
                            return;
                        }

                        SkeletonBatch batch = SkeletonBatch.Read(reader);

                        // ⚠️ NOT counted into downlink telemetry (§6.7): jitter/loss come from the 20 Hz
                        // snapshot stream and this channel runs at a different cadence — mixing them
                        // would corrupt the arrival interval and make the measurement lie.
                        RemoteSkeletonRegistry registry = RemoteSkeletonRegistry.Instance;
                        if (registry == null)
                        {
                            break;
                        }

                        int recvMs = Environment.TickCount;
                        for (int i = 0; i < batch.entries.Length; i++)
                        {
                            registry.IngestFromNetThread(batch.entries[i], recvMs, _playerId);
                        }

                        break;
                    }

                    default:
                        // Unknown packet type — ignore.
                        break;
                }
            }
        }
    }
}
