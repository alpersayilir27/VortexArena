using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// Remote network OBJECT pose registry (§6.12): rings the object section of <c>0x05</c> that
    /// <see cref="UdpStateChannel"/> ingests on the network thread, read back on the main thread with
    /// <see cref="ArenaProtocol.INTERP_DELAY_MS"/> delayed interpolation. Installed by ArenaClient; no
    /// scene/game knowledge.
    /// <para>⚠️ A SEPARATE registry from <see cref="RemotePlayerRegistry"/> (an object is not a player,
    /// it has no join/leave and no state bits) but deliberately on the SAME clock
    /// (<c>Environment.TickCount</c> + the same interp delay): a held object hangs off a hand drawn from
    /// the player registry, and two time bases would separate the two.</para>
    /// <para>⚠️ Poses only stream during the FLIGHT WINDOW (awake and not held, between
    /// <c>object_release</c> and <c>object_rest</c>). When the stream stops, the entry goes stale and the
    /// reader falls back to the resting pose from <c>object_state</c> — that is the reliable source
    /// (§6.8: on the fallback path the object section is dropped entirely).</para>
    /// </summary>
    public class RemoteObjectRegistry : MonoBehaviour
    {
        /// <summary>Samples kept per object (~1.6 s of history at OBJECT_POSE_RATE_HZ).</summary>
        private const int RING_SIZE = 16;

        /// <summary>An object not seen in the object section for this long is dropped (ms). Wide enough
        /// for a few lost packets at 10 Hz, short enough that a resting object stops being interpolated
        /// soon after it stopped.</summary>
        private const int STALE_TIMEOUT_MS = 1000;

        public static RemoteObjectRegistry Instance { get; private set; }

        private struct PoseSample
        {
            public int recvMs;
            public PoseData pose;
        }

        private class ObjectEntry
        {
            public readonly PoseSample[] ring = new PoseSample[RING_SIZE];
            public int count;
            public int nextIndex;
            public int lastRecvMs;
        }

        // Ingest (network thread) and sampling/Update (main thread) meet under this lock.
        private readonly object _gate = new object();
        private readonly Dictionary<int, ObjectEntry> _entries = new Dictionary<int, ObjectEntry>();
        private readonly List<int> _staleScratch = new List<int>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[RemoteObjectRegistry] İkinci örnek yok edildi (tekil).");
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

        /// <summary>NETWORK THREAD (UdpStateChannel.HandleDatagram): rings one sample per object entry.
        /// <para>⚠️ Our own object is NOT filtered here: ownership lives on a <see cref="NetObject"/>,
        /// which is a Unity object and cannot be touched from this thread. The gate is on the read side
        /// (<see cref="TryGetInterpolatedPose"/>).</para></summary>
        public void IngestFromNetThread(ObjectPoseEntry[] entries, int recvTickMs)
        {
            if (entries == null || entries.Length == 0)
            {
                return;
            }

            lock (_gate)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    ObjectPoseEntry pe = entries[i];
                    int netId = pe.netId;

                    if (!_entries.TryGetValue(netId, out ObjectEntry entry))
                    {
                        entry = new ObjectEntry();
                        _entries.Add(netId, entry);
                    }

                    ref PoseSample slot = ref entry.ring[entry.nextIndex];
                    slot.recvMs = recvTickMs;
                    slot.pose = pe.pose;

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
        /// MAIN THREAD: samples INTERP_DELAY_MS behind and returns the object's ARENA-space pose
        /// interpolated between two samples; with no bracketing pair it clamps to the nearest end.
        /// <para>False = draw nothing from here: no sample at all, or the object is OURS.</para>
        /// <para>⚠️ <b>We never apply the incoming pose of an object we own</b> — the server relays our
        /// own packets back and applying them would fight the local physics one interp buffer late. Same
        /// rule as not drawing our own player pose from the snapshot.</para>
        /// </summary>
        public bool TryGetInterpolatedPose(int netId, out Pose arenaPose)
        {
            arenaPose = Pose.identity;

            if (NetObjectRegistry.TryGet(netId, out NetObject netObject) && netObject.IsMine)
            {
                return false;
            }

            int renderMs = Environment.TickCount - ArenaProtocol.INTERP_DELAY_MS;

            PoseSample before = default;
            PoseSample after = default;
            bool hasBefore = false;
            bool hasAfter = false;

            lock (_gate)
            {
                if (!_entries.TryGetValue(netId, out ObjectEntry entry) || entry.count == 0)
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
                arenaPose = LerpPose(before.pose, after.pose, t);
                return true;
            }

            if (hasBefore || hasAfter)
            {
                // No bracketing pair → clamp to the nearest end.
                arenaPose = ToPose(hasBefore ? before.pose : after.pose);
                return true;
            }

            return false;
        }

        /// <summary>MAIN THREAD: is a pose stream running for this object right now (it is awake and its
        /// packets are arriving)? The consumer uses it to choose between the stream and the resting
        /// pose.</summary>
        public bool IsStreaming(int netId)
        {
            lock (_gate)
            {
                return _entries.TryGetValue(netId, out ObjectEntry entry) &&
                       entry.count > 0 &&
                       Environment.TickCount - entry.lastRecvMs <= STALE_TIMEOUT_MS;
            }
        }

        private void Update()
        {
            // TickCount differences via int subtraction — robust against the ~24.9 day wraparound.
            int now = Environment.TickCount;

            _staleScratch.Clear();

            lock (_gate)
            {
                foreach (KeyValuePair<int, ObjectEntry> kv in _entries)
                {
                    if (now - kv.Value.lastRecvMs > STALE_TIMEOUT_MS)
                    {
                        _staleScratch.Add(kv.Key);
                    }
                }

                for (int i = 0; i < _staleScratch.Count; i++)
                {
                    _entries.Remove(_staleScratch[i]);
                }
            }
        }

        /// <summary>On disconnect: drops every ring — a new session restarts the pose stream and a stale
        /// sample would draw the object where it was before the drop.</summary>
        private void HandleDisconnected()
        {
            lock (_gate)
            {
                _entries.Clear();
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
