using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// Remote player pose registry singleton: rings the Snapshots UdpStateChannel ingests on the network
    /// thread per player, read back on the main thread with INTERP_DELAY_MS delayed interpolation.
    /// Joins/leaves are announced through OnRemoteJoined/OnRemoteLeft. Snapshot item state (§6.6) lives
    /// here too but is NOT interpolated (TryGetHeldItems). Installed by ArenaClient; no scene/game
    /// knowledge.
    /// </summary>
    public class RemotePlayerRegistry : MonoBehaviour
    {
        /// <summary>A player not seen in a snapshot for this long counts as left (ms).</summary>
        private const int LEFT_TIMEOUT_MS = 1500;

        /// <summary>Stale window for the local player's own state bits (ms) — wide enough for a few
        /// lost/split 20 Hz packets, narrow enough not to delay "protection over".</summary>
        private const int LOCAL_FLAG_STALE_MS = 500;

        /// <summary>Samples kept per player (~3.2 s of history at 20 Hz).</summary>
        private const int RING_SIZE = 64;

        /// <summary>Snapshots kept in the tick→time mapping (~3.2 s at 20 Hz).</summary>
        private const int TICK_RING_SIZE = 64;

        /// <summary>Nominal time between two snapshots (ms) — converts a tick delta to time.</summary>
        private const int MS_PER_SNAPSHOT = 1000 / ArenaProtocol.SNAPSHOT_RATE_HZ;

        /// <summary>
        /// Largest "future" tick delta accepted in extrapolation (2 s of ticks). Anything bigger means
        /// wraparound or a server restart.
        /// </summary>
        private const int MAX_FUTURE_TICKS = ArenaProtocol.SNAPSHOT_RATE_HZ * 2;

        public static RemotePlayerRegistry Instance { get; private set; }

        /// <summary>Raised on the main thread when a remote player is seen for the first time.</summary>
        public event Action<int> OnRemoteJoined;

        /// <summary>Raised on the main thread when a remote player times out / drops.</summary>
        public event Action<int> OnRemoteLeft;

        /// <summary>
        /// A snapshot's tick→local timestamp (recvMs = Environment.TickCount).
        /// <para>⚠️ <b>GLOBAL, not per player</b>: one snapshot per tick holds every player. On
        /// <see cref="RemoteEntry.ring"/> an event from a player with no pose (or absent that tick)
        /// could not be timed.</para>
        /// </summary>
        private struct TickStamp
        {
            public uint serverTick;
            public int recvMs;
            public bool valid;
        }

        /// <summary>A single snapshot's pose sample (recvMs = Environment.TickCount).</summary>
        private struct PoseSample
        {
            public int recvMs;
            public PoseData head, handL, handR;
            public byte flags;
        }

        /// <summary>A remote player record: a fixed-size sample ring + the last time it was seen.</summary>
        private class RemoteEntry
        {
            public int playerId;
            public readonly PoseSample[] ring = new PoseSample[RING_SIZE];
            public int count;
            public int nextIndex;
            public int lastRecvMs;
            public bool announced;

            // ---- §6.6 item state: a single slot, NOT the ring ----
            // ⚠️ STATE, so unlike a pose it is NOT INTERPOLATED: categorical data has no "half way"
            // (no item sits between a pistol and a rifle). Latest snapshot wins; applying it one interp
            // buffer early is invisible (a hand swap cannot beat the 100 ms window).
            public byte itemL;
            public byte itemR;
            public bool gripLinked;
            public bool primaryRight;
        }

        // Ingest (network thread) and sampling/Update (main thread) meet under this lock.
        private readonly object _gate = new object();
        private readonly Dictionary<int, RemoteEntry> _entries = new Dictionary<int, RemoteEntry>();

        // Events are published OUTSIDE the lock; the scratch lists are fields so as not to produce GC.
        private readonly List<int> _joinedScratch = new List<int>();
        private readonly List<int> _leftScratch = new List<int>();

        // Tick→time mapping (TryGetPlaybackTimeMs). Under the same _gate as the pose rings: the writer
        // is the network thread (ingest), the reader the main thread (presentation).
        private readonly TickStamp[] _tickRing = new TickStamp[TICK_RING_SIZE];
        private int _tickRingNext;
        private bool _hasNewestTick;
        private uint _newestTick;
        private int _newestTickRecvMs;

        private int _lastSnapshotMs;

        // The local player's own state bits (§10.4); net thread writes, main thread reads.
        // ⚠️ Deliberately OUTSIDE _gate: locking at 20 Hz for one flag would queue the main thread
        // behind the pose ingest path (the server's PlayerState.Alive pattern).
        private volatile bool _localSpawnProtected;
        private volatile int _localFlagsRecvMs;

        /// <summary>The Environment.TickCount value at which the last snapshot arrived
        /// (diagnostics).</summary>
        public int LastSnapshotMs
        {
            get
            {
                lock (_gate)
                {
                    return _lastSnapshotMs;
                }
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[RemotePlayerRegistry] İkinci örnek yok edildi (tekil).");
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
            NetEvents.OnDisconnected += HandleDisconnected;
        }

        private void OnDisable()
        {
            NetEvents.OnDisconnected -= HandleDisconnected;
        }

        /// <summary>
        /// NETWORK THREAD (UdpStateChannel.HandleDatagram): rings a sample per remote player in the
        /// snapshot, skipping our own playerId, and records the tick stamp
        /// (<see cref="TryGetPlaybackTimeMs"/>).
        /// </summary>
        public void IngestFromNetThread(Snapshot snap, int recvTickMs, int localPlayerId)
        {
            lock (_gate)
            {
                // ⚠️ Tick stamp written BEFORE the pose early-out: a playerCount = 0 snapshot is a
                // legitimate broadcast and a shot event on that tick must still be timeable.
                RecordTickLocked(snap.serverTick, recvTickMs);

                if (snap.players == null)
                {
                    return;
                }

                _lastSnapshotMs = recvTickMs;

                for (int i = 0; i < snap.players.Length; i++)
                {
                    SnapshotEntry se = snap.players[i];
                    if (se.playerId == localPlayerId)
                    {
                        // Pose ignored (server echo), STATE bit read: own spawn protection arrives only
                        // through here (§10.4).
                        _localSpawnProtected = (se.flags & SnapshotEntry.FLAG_SPAWN_PROTECTED) != 0;
                        _localFlagsRecvMs = recvTickMs;
                        continue;
                    }

                    if (!_entries.TryGetValue(se.playerId, out RemoteEntry entry))
                    {
                        entry = new RemoteEntry { playerId = se.playerId };
                        _entries.Add(se.playerId, entry);
                    }

                    ref PoseSample slot = ref entry.ring[entry.nextIndex];
                    slot.recvMs = recvTickMs;
                    slot.head = se.head;
                    slot.handL = se.handL;
                    slot.handR = se.handR;
                    slot.flags = se.flags;

                    // Item state (§6.6): last one wins — it does not enter the ring.
                    entry.itemL = se.itemL;
                    entry.itemR = se.itemR;
                    entry.gripLinked = (se.flags & SnapshotEntry.FLAG_GRIP_LINKED) != 0;
                    entry.primaryRight = (se.flags & SnapshotEntry.FLAG_PRIMARY_RIGHT) != 0;

                    entry.nextIndex = (entry.nextIndex + 1) % RING_SIZE;
                    if (entry.count < RING_SIZE)
                    {
                        entry.count++;
                    }

                    entry.lastRecvMs = recvTickMs;
                }
            }
        }

        /// <summary>
        /// Writes the snapshot's tick stamp into the ring. The caller MUST HOLD <c>_gate</c>.
        /// </summary>
        private void RecordTickLocked(uint serverTick, int recvTickMs)
        {
            // ⚠️ Split snapshot (§6.3): one tick over the MTU arrives as several datagrams with the
            // SAME serverTick. Only the FIRST part's arrival time counts — later parts would
            // needlessly delay playback.
            if (_hasNewestTick && _newestTick == serverTick)
            {
                return;
            }

            ref TickStamp slot = ref _tickRing[_tickRingNext];
            slot.serverTick = serverTick;
            slot.recvMs = recvTickMs;
            slot.valid = true;

            _tickRingNext = (_tickRingNext + 1) % TICK_RING_SIZE;

            // "Newest tick" never moves backwards: UDP is unordered, so extrapolation must rest on the
            // genuinely furthest tick. Signed difference → correct across u32 wraparound too.
            if (!_hasNewestTick || (int)(serverTick - _newestTick) > 0)
            {
                _hasNewestTick = true;
                _newestTick = serverTick;
                _newestTickRecvMs = recvTickMs;
            }
        }

        /// <summary>
        /// MAIN THREAD: the local time (Environment.TickCount axis) at which a server tick will be
        /// played; false when unknown → the caller plays the event IMMEDIATELY.
        /// <para>Remote poses are drawn <c>INTERP_DELAY_MS</c> behind
        /// (<see cref="GetInterpolatedPose"/>), so a sample with <c>recvMs = R</c> is drawn at
        /// <c>R + INTERP_DELAY_MS</c> on the wall clock — exactly when an event should play "while the
        /// hand is in the right place".</para>
        /// </summary>
        public bool TryGetPlaybackTimeMs(uint serverTick, out int playbackMs)
        {
            playbackMs = 0;

            lock (_gate)
            {
                if (!_hasNewestTick)
                {
                    return false; // no snapshot has arrived yet
                }

                for (int i = 0; i < TICK_RING_SIZE; i++)
                {
                    TickStamp stamp = _tickRing[i];
                    if (stamp.valid && stamp.serverTick == serverTick)
                    {
                        playbackMs = stamp.recvMs + ArenaProtocol.INTERP_DELAY_MS;
                        return true;
                    }
                }

                // Not in the ring: either AHEAD (UDP is unordered, an event batch may beat its own
                // snapshot) or too old. u32 arithmetic wraps naturally, so a past tick yields a huge
                // number and hits the ceiling.
                uint delta = serverTick - _newestTick;
                if (delta > MAX_FUTURE_TICKS)
                {
                    // Far too old (≥ ~3.2 s) or a wraparound/server restart — say "unknown" rather than
                    // invent a future time.
                    return false;
                }

                playbackMs = _newestTickRecvMs + (int)delta * MS_PER_SNAPSHOT + ArenaProtocol.INTERP_DELAY_MS;
                return true;
            }
        }

        /// <summary>
        /// A <b>SHARED clock</b> (seconds) on the server's tick axis; false before the first snapshot.
        /// <para><b>Why:</b> the skeleton blob embeds the sender's timestamp and the receiver
        /// interpolates against it (§6.9). Without a common epoch the body plays in 12 Hz steps;
        /// <c>Environment.TickCount</c> is machine-specific.</para>
        /// <para><b>Why no clock-sync packet:</b> <c>serverTick</c> is already the same number on every
        /// client — converted to seconds plus time since the last arrival. Error = one-way latency
        /// difference (a few ms on LAN). ⚠️ Not an <b>absolute</b> clock and no violation of §6.7: RTT
        /// still uses a single-ended stamp, this only puts two clients on a COMMON axis.</para>
        /// <para>⚠️ A server restart makes this clock <b>jump backwards</b>; the SDK buffer resettles in
        /// a few frames. No correction added — a restart already reconnects everyone.</para>
        /// <para>⚠️ <c>float</c> resolution coarsens with uptime (~60 ms after a week); the ceiling is
        /// one skeleton frame (~83 ms) and venue servers restart daily.</para>
        /// </summary>
        public bool TryGetServerTimeSeconds(out float seconds)
        {
            lock (_gate)
            {
                if (!_hasNewestTick)
                {
                    seconds = 0f;
                    return false;
                }

                int sinceNewestMs = Environment.TickCount - _newestTickRecvMs;
                double ms = (double)_newestTick * MS_PER_SNAPSHOT + sinceNewestMs;
                seconds = (float)(ms / 1000.0);
                return true;
            }
        }

        private void Update()
        {
            _joinedScratch.Clear();
            _leftScratch.Clear();

            // TickCount differences via int subtraction — robust against the ~24.9 day wraparound.
            int now = Environment.TickCount;

            lock (_gate)
            {
                foreach (KeyValuePair<int, RemoteEntry> kv in _entries)
                {
                    if (!kv.Value.announced)
                    {
                        kv.Value.announced = true;
                        _joinedScratch.Add(kv.Key);
                    }
                }

                foreach (KeyValuePair<int, RemoteEntry> kv in _entries)
                {
                    if (now - kv.Value.lastRecvMs > LEFT_TIMEOUT_MS)
                    {
                        _leftScratch.Add(kv.Key);
                    }
                }

                for (int i = 0; i < _leftScratch.Count; i++)
                {
                    _entries.Remove(_leftScratch[i]);
                }
            }

            // Events outside the lock: listeners may call back into the registry.
            for (int i = 0; i < _joinedScratch.Count; i++)
            {
                OnRemoteJoined?.Invoke(_joinedScratch[i]);
            }

            for (int i = 0; i < _leftScratch.Count; i++)
            {
                OnRemoteLeft?.Invoke(_leftScratch[i]);
            }
        }

        /// <summary>
        /// MAIN THREAD: samples INTERP_DELAY_MS behind and returns arena-space poses interpolated
        /// between two samples; with no bracketing pair it clamps to the nearest end. False when there
        /// is no sample at all.
        /// </summary>
        public bool GetInterpolatedPose(int playerId, out Pose head, out Pose handL, out Pose handR)
        {
            head = Pose.identity;
            handL = Pose.identity;
            handR = Pose.identity;

            int renderMs = Environment.TickCount - ArenaProtocol.INTERP_DELAY_MS;

            PoseSample before = default;
            PoseSample after = default;
            bool hasBefore = false;
            bool hasAfter = false;

            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out RemoteEntry entry) || entry.count == 0)
                {
                    return false;
                }

                int start = entry.nextIndex - entry.count;
                if (start < 0)
                {
                    start += RING_SIZE;
                }

                // The ring is in chronological order: find the pair bracketing the sampling time.
                for (int i = 0; i < entry.count; i++)
                {
                    PoseSample sample = entry.ring[(start + i) % RING_SIZE];
                    if (renderMs - sample.recvMs >= 0)
                    {
                        before = sample;
                        hasBefore = true;
                    }
                    else
                    {
                        after = sample;
                        hasAfter = true;
                        break;
                    }
                }
            }

            if (hasBefore && hasAfter)
            {
                int span = after.recvMs - before.recvMs;
                float t = span > 0 ? Mathf.Clamp01((renderMs - before.recvMs) / (float)span) : 1f;
                head = LerpPose(before.head, after.head, t);
                handL = LerpPose(before.handL, after.handL, t);
                handR = LerpPose(before.handR, after.handR, t);
                return true;
            }

            if (hasBefore || hasAfter)
            {
                // No bracketing pair → clamp to the nearest end.
                PoseSample edge = hasBefore ? before : after;
                head = ToPose(edge.head);
                handL = ToPose(edge.handL);
                handR = ToPose(edge.handR);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Alive flag from the last snapshot (SnapshotEntry.FLAG_ALIVE). Unknown id → true (assume alive).
        /// </summary>
        public bool IsAlive(int playerId)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out RemoteEntry entry) || entry.count == 0)
                {
                    return true;
                }

                // Newest sample: nextIndex points at the next EMPTY slot.
                int last = entry.nextIndex - 1;
                if (last < 0)
                {
                    last += RING_SIZE;
                }

                return (entry.ring[last].flags & SnapshotEntry.FLAG_ALIVE) != 0;
            }
        }

        /// <summary>
        /// §10.9: was the player <b>inside an inner obstacle</b> in the last snapshot
        /// (<see cref="SnapshotEntry.FLAG_IN_OBSTACLE"/>)? Only consumed by the admin spectator ring.
        /// <para>Unknown id → <c>false</c>. ⚠️ OPPOSITE default to <see cref="IsAlive"/>: counting an
        /// unknown state as a violation would show the operator a non-existent event during a net gap.</para>
        /// <para>⚠️ State, not an event: resent in every snapshot, so it clears by itself on exit.</para>
        /// </summary>
        public bool IsInObstacle(int playerId)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out RemoteEntry entry) || entry.count == 0)
                {
                    return false;
                }

                int last = entry.nextIndex - 1;
                if (last < 0)
                {
                    last += RING_SIZE;
                }

                return (entry.ring[last].flags & SnapshotEntry.FLAG_IN_OBSTACLE) != 0;
            }
        }

        /// <summary>
        /// §10.9: was the player <b>outside</b> the boundary's safe area in the last snapshot
        /// (<see cref="SnapshotEntry.FLAG_OUT_OF_BOUNDS"/>)? Only consumed by the admin spectator ring.
        /// <para>Unknown id → <c>false</c> (same direction as <see cref="IsInObstacle"/>).</para>
        /// <para>⚠️ State, not an event: resent in every snapshot, so it clears by itself on re-entry.</para>
        /// <para>⚠️ <b>Carries no penalty</b> — unlike an obstacle violation it drains no health; the
        /// reader only shows it to the operator.</para>
        /// </summary>
        public bool IsOutOfBounds(int playerId)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out RemoteEntry entry) || entry.count == 0)
                {
                    return false;
                }

                int last = entry.nextIndex - 1;
                if (last < 0)
                {
                    last += RING_SIZE;
                }

                return (entry.ring[last].flags & SnapshotEntry.FLAG_OUT_OF_BOUNDS) != 0;
            }
        }

        /// <summary>
        /// §10.4: was the player under <b>spawn protection</b> in the last snapshot
        /// (<see cref="SnapshotEntry.FLAG_SPAWN_PROTECTED"/>)? Consumed by the remote avatar's shield
        /// visual — ⚠️ fire decisions are NOT based on it, the damage gate is on the server.
        /// <para>⚠️ Our own id NEVER appears here (the local pose is not ringed); use
        /// <see cref="IsLocalSpawnProtected"/>.</para>
        /// <para>Unknown id → <c>false</c>: counting an unknown state as protection would draw a shield
        /// that does not exist.</para>
        /// <para>⚠️ State, not an event: resent in every snapshot, so it clears by itself — no client
        /// side counter.</para>
        /// </summary>
        public bool IsSpawnProtected(int playerId)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out RemoteEntry entry) || entry.count == 0)
                {
                    return false;
                }

                int last = entry.nextIndex - 1;
                if (last < 0)
                {
                    last += RING_SIZE;
                }

                return (entry.ring[last].flags & SnapshotEntry.FLAG_SPAWN_PROTECTED) != 0;
            }
        }

        /// <summary>
        /// §10.4: is the <b>local</b> player spawn protected — the bit of our own snapshot entry.
        /// <para>⚠️ A separate gate for a structural reason: our own POSE is not ringed (the server echo
        /// is ignored) while the STATE bits only ride the snapshot.</para>
        /// <para>⚠️ An indicator, NOT a permission: no fire/damage decision is made from it.</para>
        /// <para>Falls back to <c>false</c> when the flag goes <b>stale</b> — in a split snapshot (§6.3)
        /// our entry may be absent from a datagram, so "not in this packet" alone is not "protection
        /// over".</para>
        /// </summary>
        public bool IsLocalSpawnProtected =>
            _localSpawnProtected && Environment.TickCount - _localFlagsRecvMs <= LOCAL_FLAG_STALE_MS;

        /// <summary>
        /// §6.6: the player's <b>last known</b> item state. False when the player is unknown.
        /// <para>⚠️ Without <c>gripLinked</c>, "same id in both slots" does NOT mean a two-handed grip —
        /// dual wielding is legitimate (§6.6). <c>primaryRight</c> only means something while
        /// <c>gripLinked</c>.</para>
        /// </summary>
        public bool TryGetHeldItems(int playerId, out byte itemL, out byte itemR, out bool gripLinked, out bool primaryRight)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out RemoteEntry entry))
                {
                    itemL = 0;
                    itemR = 0;
                    gripLinked = false;
                    primaryRight = false;
                    return false;
                }

                itemL = entry.itemL;
                itemR = entry.itemR;
                gripLinked = entry.gripLinked;
                primaryRight = entry.primaryRight;
                return true;
            }
        }

        /// <summary>MAIN THREAD: fills the buffer with announced remote player ids.</summary>
        public void GetActivePlayerIds(List<int> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();

            lock (_gate)
            {
                foreach (KeyValuePair<int, RemoteEntry> kv in _entries)
                {
                    if (kv.Value.announced)
                    {
                        buffer.Add(kv.Key);
                    }
                }
            }
        }

        /// <summary>On disconnect: clears every entry and publishes the leaves (we are on the main thread).</summary>
        private void HandleDisconnected()
        {
            _leftScratch.Clear();

            // Our own state bit is stale too — do not keep showing "protected" on a dead connection.
            _localSpawnProtected = false;

            lock (_gate)
            {
                foreach (KeyValuePair<int, RemoteEntry> kv in _entries)
                {
                    if (kv.Value.announced)
                    {
                        _leftScratch.Add(kv.Key);
                    }
                }

                _entries.Clear();

                // Drop the tick→time mapping too: a new session may restart the tick axis at zero and a
                // stale stamp would produce a wrong playback time.
                Array.Clear(_tickRing, 0, _tickRing.Length);
                _tickRingNext = 0;
                _hasNewestTick = false;
                _newestTick = 0;
                _newestTickRecvMs = 0;
            }

            for (int i = 0; i < _leftScratch.Count; i++)
            {
                OnRemoteLeft?.Invoke(_leftScratch[i]);
            }
        }

        // ------------------------------------------------------------- conversions

        private static Pose ToPose(in PoseData data)
        {
            // PoseData already carries a normalised quaternion — no renormalisation needed.
            return new Pose(
                new Vector3(data.px, data.py, data.pz),
                new Quaternion(data.qx, data.qy, data.qz, data.qw));
        }

        private static Pose LerpPose(in PoseData a, in PoseData b, float t)
        {
            Vector3 position = Vector3.Lerp(
                new Vector3(a.px, a.py, a.pz),
                new Vector3(b.px, b.py, b.pz), t);
            Quaternion rotation = Quaternion.Slerp(
                new Quaternion(a.qx, a.qy, a.qz, a.qw),
                new Quaternion(b.qx, b.qy, b.qz, b.qw), t);
            return new Pose(position, rotation);
        }
    }
}
