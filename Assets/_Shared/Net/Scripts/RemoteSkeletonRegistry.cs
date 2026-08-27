using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// The remote player skeleton registry singleton (§6.10): stores the <c>0x08</c> entries
    /// <see cref="UdpStateChannel"/> receives on the network thread per player and lets the main thread
    /// read them. The same pattern as <see cref="RemotePlayerRegistry"/> — ArenaClient installs it with
    /// AddComponent, and it holds no scene/game knowledge.
    /// <para>
    /// <b>The two fields have DELIBERATELY different lifetimes:</b>
    /// </para>
    /// <list type="bullet">
    /// <item><b>The blob is CONSUMED</b> (<see cref="TryTakeBlob"/>): the SDK's <c>ReceiveData</c>
    /// queues every frame it is given, so handing out the same blob twice would play it twice.</item>
    /// <item><b>The root PERSISTS</b> (<see cref="TryGetInterpolatedRoot"/>): it must be written
    /// <b>every frame</b> (the SDK keeps overwriting it in <c>ApplyBodyPose</c>) while a new blob only
    /// arrives at <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>.</item>
    /// </list>
    /// <para>
    /// ⚠️ <b>The root IS INTERPOLATED</b>, not as a beautification: the body streams at 12 Hz and the
    /// hands at <see cref="ArenaProtocol.POSE_RATE_HZ"/>, so a raw root would step at 12 Hz next to
    /// smooth hands — two smoothnesses on one avatar looks detached. The buffer is <b>the same</b> as
    /// the pose channel's (<see cref="ArenaProtocol.INTERP_DELAY_MS"/>); a different one would leave a
    /// constant body-vs-hands time shift.
    /// </para>
    /// <para>
    /// ⚠️ <b>The blob slot holds one frame (last one wins)</b>, which rests on delta compression being
    /// <b>OFF</b> in the SDK (§6.9): every frame is an independent keyframe, so a dropped frame costs
    /// only itself. Turning delta on forces this slot to become a queue — a skipped baseline leaves
    /// every following frame undecodable.
    /// </para>
    /// </summary>
    public class RemoteSkeletonRegistry : MonoBehaviour
    {
        /// <summary>
        /// Root samples kept per player — ~1.3 s of history at
        /// <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>, many times the 100 ms interp buffer so
        /// jitter/loss can be absorbed.
        /// </summary>
        private const int RING_SIZE = 16;

        public static RemoteSkeletonRegistry Instance { get; private set; }

        /// <summary>A single root sample (recvMs = <c>Environment.TickCount</c>): body yaw + the
        /// root's offset from the pose-channel head's floor projection (§6.9, v19) — decoded to
        /// floats at ingest so interpolation needs no unpacking.</summary>
        private struct RootSample
        {
            public int recvMs;
            public float yawDeg;
            public Vector3 offset;
        }

        private class SkeletonEntryState
        {
            /// <summary>A blob not consumed yet; <c>null</c> = no new frame.</summary>
            public byte[] pendingBlob;
            public int pendingLength;

            public readonly RootSample[] ring = new RootSample[RING_SIZE];
            public int count;
            public int nextIndex;
        }

        // Ingest (network thread) and reading (main thread) meet under this lock.
        private readonly object _gate = new object();
        private readonly Dictionary<int, SkeletonEntryState> _entries = new Dictionary<int, SkeletonEntryState>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[RemoteSkeletonRegistry] İkinci örnek yok edildi (tekil).");
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

        private void HandleDisconnected()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
        }

        /// <summary>
        /// NETWORK THREAD: takes a single entry of the <c>0x08</c> batch.
        /// <para>⚠️ Our own <paramref name="localPlayerId"/> is skipped — we draw our own body from the
        /// sensors (§6.10: the server sends to the sender too, the filter is here).</para>
        /// </summary>
        public void IngestFromNetThread(in SkeletonEntry entry, int recvTickMs, int localPlayerId)
        {
            if (entry.playerId == localPlayerId || entry.blobLength <= 0)
            {
                return;
            }

            lock (_gate)
            {
                if (!_entries.TryGetValue(entry.playerId, out SkeletonEntryState state))
                {
                    state = new SkeletonEntryState();
                    _entries.Add(entry.playerId, state);
                }

                // Last one wins (see the class summary — safe because delta is OFF).
                state.pendingBlob = entry.blob;
                state.pendingLength = entry.blobLength;

                state.ring[state.nextIndex] = new RootSample
                {
                    recvMs = recvTickMs,
                    yawDeg = entry.root.yawDeg,
                    offset = new Vector3(entry.root.ox, entry.root.oy, entry.root.oz)
                };
                state.nextIndex = (state.nextIndex + 1) % RING_SIZE;
                if (state.count < RING_SIZE)
                {
                    state.count++;
                }
            }
        }

        /// <summary>
        /// MAIN THREAD: hands out the pending blob and <b>empties</b> the slot. False when there is no
        /// new frame.
        /// </summary>
        public bool TryTakeBlob(int playerId, out byte[] blob, out int length)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(playerId, out SkeletonEntryState state) && state.pendingBlob != null)
                {
                    blob = state.pendingBlob;
                    length = state.pendingLength;
                    state.pendingBlob = null;
                    state.pendingLength = 0;
                    return true;
                }
            }

            blob = null;
            length = 0;
            return false;
        }

        /// <summary>
        /// MAIN THREAD: the character root's interpolated body yaw + head offset (§6.9, v19),
        /// delayed by <see cref="ArenaProtocol.INTERP_DELAY_MS"/>; clamps to the nearest end with no
        /// bracketing pair, false with no sample at all.
        /// <para>⚠️ NOT a pose: the caller rebuilds the arena root on its OWN interpolated
        /// pose-channel head (<c>root = headFloor + offset</c>, rotation from yaw) — that anchoring
        /// is the whole point of the v19 wire form, so this class must not try to produce an
        /// absolute pose.</para>
        /// <para>Uses the same sampling time as the pose channel's <c>GetInterpolatedPose</c>, so no
        /// body-vs-hands time shift is left.</para>
        /// </summary>
        public bool TryGetInterpolatedRoot(int playerId, out float yawDeg, out Vector3 offset)
        {
            yawDeg = 0f;
            offset = Vector3.zero;

            int renderMs = Environment.TickCount - ArenaProtocol.INTERP_DELAY_MS;

            RootSample before = default;
            RootSample after = default;
            bool hasBefore = false;
            bool hasAfter = false;

            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out SkeletonEntryState state) || state.count == 0)
                {
                    return false;
                }

                int start = state.nextIndex - state.count;
                if (start < 0)
                {
                    start += RING_SIZE;
                }

                // The ring is in chronological order: find the pair bracketing the sampling time.
                for (int i = 0; i < state.count; i++)
                {
                    RootSample sample = state.ring[(start + i) % RING_SIZE];
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

            if (!hasBefore && !hasAfter)
            {
                return false;
            }

            if (!hasBefore)
            {
                yawDeg = after.yawDeg;
                offset = after.offset;
                return true;
            }

            if (!hasAfter)
            {
                yawDeg = before.yawDeg;
                offset = before.offset;
                return true;
            }

            int span = after.recvMs - before.recvMs;
            float t = span > 0 ? Mathf.Clamp01((renderMs - before.recvMs) / (float)span) : 0f;

            // LerpAngle: yaw wraps at 360° and a plain lerp would spin the body the long way round.
            yawDeg = Mathf.LerpAngle(before.yawDeg, after.yawDeg, t);
            offset = Vector3.Lerp(before.offset, after.offset, t);
            return true;
        }

        /// <summary>Age of the NEWEST root sample in ms; <c>-1</c> when the player has no sample at all.
        /// <para>⚠️ This exists because samples deliberately never expire (class summary). Without an age
        /// a reader cannot tell a LIVE stream from one that stopped minutes ago and left its last root
        /// behind — and those two must be drawn differently: a dead stream means the sender's body
        /// tracking is gone, so the body has to come from the pose channel instead (§6.11).</para>
        /// <para>⚠️ Same clock as <c>recvMs</c> (<c>Environment.TickCount</c>), so the value is a
        /// RECEIVE age, not a send age — it deliberately includes network silence, which is exactly the
        /// fault being detected.</para></summary>
        public int GetRootAgeMs(int playerId)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out SkeletonEntryState state) || state.count == 0)
                {
                    return -1;
                }

                int newest = (state.nextIndex - 1 + RING_SIZE) % RING_SIZE;
                return Environment.TickCount - state.ring[newest].recvMs;
            }
        }

        /// <summary>Called when an avatar is destroyed / handed over to another player: so the stale
        /// root and blob are not inherited by the new owner.</summary>
        public void Forget(int playerId)
        {
            lock (_gate)
            {
                _entries.Remove(playerId);
            }
        }

    }
}
