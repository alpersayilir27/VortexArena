using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>Remote player skeleton registry (§6.10): decoded <c>0x08</c> frames per player, interpolated for the main thread.</summary>
    /// <remarks>
    /// Same pattern as <see cref="RemotePlayerRegistry"/> — ArenaClient installs it with AddComponent,
    /// and it holds no scene/game knowledge. <see cref="UdpStateChannel"/> ingests on the network thread.
    /// <list type="bullet">
    /// <item>⚠️ <b>ONE ring holds root AND bones:</b> each sample carries yaw + head offset + hips +
    /// joint rotations under a single receive stamp, so body placement and pose always interpolate
    /// between the same two frames on the same clock.</item>
    /// <item>⚠️ <b>The buffer is the pose channel's</b> (<see cref="ArenaProtocol.INTERP_DELAY_MS"/>): the
    /// body streams at 12 Hz next to smooth 20 Hz hands, and a different delay would leave a constant
    /// body-vs-hands time shift.</item>
    /// <item>⚠️ <b>An undecodable blob drops the WHOLE frame</b>, root included: the stream then ages
    /// out and the body falls back to the pose channel (§6.11) instead of a half-valid frame.</item>
    /// </list>
    /// </remarks>
    public class RemoteSkeletonRegistry : MonoBehaviour
    {
        /// <summary>Samples kept per player — ~1.3 s at <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>,
        /// many times the interp buffer so jitter/loss can be absorbed.</summary>
        private const int RING_SIZE = 16;

        public static RemoteSkeletonRegistry Instance { get; private set; }

        /// <summary>Sampling time shared by root and bones; pass one value to both per frame.</summary>
        public static int RenderTickMs => Environment.TickCount - ArenaProtocol.INTERP_DELAY_MS;

        /// <summary>One frame (recvMs = <c>Environment.TickCount</c>), decoded at ingest.</summary>
        /// <remarks>Rotations live in the parallel <see cref="SkeletonEntryState.rotations"/> slot.</remarks>
        private struct SkeletonSample
        {
            public int recvMs;
            public float yawDeg;
            public Vector3 offset;
            public Vector3 hips;
        }

        private class SkeletonEntryState
        {
            public readonly SkeletonSample[] ring = new SkeletonSample[RING_SIZE];

            /// <summary>Per-slot joint rotations in <see cref="SkeletonWire.JOINT_INDICES"/> order; preallocated.</summary>
            public readonly Quaternion[][] rotations = new Quaternion[RING_SIZE][];

            public int count;
            public int nextIndex;

            public SkeletonEntryState()
            {
                for (int i = 0; i < RING_SIZE; i++)
                {
                    rotations[i] = new Quaternion[SkeletonWire.JointCount];
                }
            }
        }

        // Ingest (network thread) and reading (main thread) meet under this lock.
        private readonly object _gate = new object();
        private readonly Dictionary<int, SkeletonEntryState> _entries = new Dictionary<int, SkeletonEntryState>();

        /// <summary>Decode buffer (under <see cref="_gate"/>): a failed read must not touch a live ring slot.</summary>
        private readonly float[] _decodeScratch = new float[4 * SkeletonWire.JointCount];

        /// <summary>Players already warned about an undecodable frame (under <see cref="_gate"/>).</summary>
        private readonly HashSet<int> _decodeWarned = new HashSet<int>();

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

        /// <summary>NETWORK THREAD: takes a single entry of the <c>0x08</c> batch.</summary>
        /// <remarks>⚠️ Our own <paramref name="localPlayerId"/> is skipped — we draw our own body from the
        /// sensors (§6.10: the server sends to the sender too, the filter is here).</remarks>
        public void IngestFromNetThread(in SkeletonEntry entry, int recvTickMs, int localPlayerId)
        {
            if (entry.playerId == localPlayerId || entry.blobLength <= 0)
            {
                return;
            }

            bool warn = false;

            lock (_gate)
            {
                if (!SkeletonWire.TryRead(entry.blob, 0, entry.blobLength,
                        out float hipX, out float hipY, out float hipZ, _decodeScratch))
                {
                    warn = _decodeWarned.Add(entry.playerId);
                }
                else
                {
                    if (!_entries.TryGetValue(entry.playerId, out SkeletonEntryState state))
                    {
                        state = new SkeletonEntryState();
                        _entries.Add(entry.playerId, state);
                    }

                    int slot = state.nextIndex;
                    state.ring[slot] = new SkeletonSample
                    {
                        recvMs = recvTickMs,
                        yawDeg = entry.root.yawDeg,
                        offset = new Vector3(entry.root.ox, entry.root.oy, entry.root.oz),
                        hips = new Vector3(hipX, hipY, hipZ)
                    };

                    Quaternion[] rotations = state.rotations[slot];
                    for (int j = 0; j < rotations.Length; j++)
                    {
                        int r = 4 * j;
                        rotations[j] = new Quaternion(
                            _decodeScratch[r], _decodeScratch[r + 1], _decodeScratch[r + 2], _decodeScratch[r + 3]);
                    }

                    state.nextIndex = (state.nextIndex + 1) % RING_SIZE;
                    if (state.count < RING_SIZE)
                    {
                        state.count++;
                    }
                }
            }

            if (warn)
            {
                Debug.LogWarning(
                    $"[İskelet] Oyuncu {entry.playerId} iskelet karesi çözülemedi ({entry.blobLength} B, " +
                    $"beklenen {SkeletonWire.BLOB_BYTES} B) — protokol sürümü uyuşmuyor olabilir");
            }
        }

        /// <summary>MAIN THREAD: interpolated body yaw + head offset (§6.9, v19) at <see cref="RenderTickMs"/>.</summary>
        public bool TryGetInterpolatedRoot(int playerId, out float yawDeg, out Vector3 offset)
        {
            return TryGetInterpolatedRoot(playerId, RenderTickMs, out yawDeg, out offset);
        }

        /// <summary>MAIN THREAD: interpolated body yaw + head offset (§6.9, v19) at <paramref name="renderTick"/>.</summary>
        /// <remarks>
        /// Clamps to the nearest end with no bracketing pair, false with no sample at all.
        /// <para>⚠️ NOT a pose: the caller rebuilds the arena root on its OWN interpolated pose-channel head
        /// (<c>root = headFloor + offset</c>, rotation from yaw) — that anchoring is the point of the v19
        /// wire form, so this class must not produce an absolute pose.</para>
        /// </remarks>
        public bool TryGetInterpolatedRoot(int playerId, int renderTick, out float yawDeg, out Vector3 offset)
        {
            yawDeg = 0f;
            offset = Vector3.zero;

            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out SkeletonEntryState state) || state.count == 0)
                {
                    return false;
                }

                FindBracket(state, renderTick, out int beforeSlot, out int afterSlot);
                if (beforeSlot < 0 && afterSlot < 0)
                {
                    return false;
                }

                if (beforeSlot < 0 || afterSlot < 0)
                {
                    SkeletonSample only = state.ring[beforeSlot < 0 ? afterSlot : beforeSlot];
                    yawDeg = only.yawDeg;
                    offset = only.offset;
                    return true;
                }

                SkeletonSample before = state.ring[beforeSlot];
                SkeletonSample after = state.ring[afterSlot];
                float t = BracketT(before, after, renderTick);

                // LerpAngle: yaw wraps at 360° and a plain lerp would spin the body the long way round.
                yawDeg = Mathf.LerpAngle(before.yawDeg, after.yawDeg, t);
                offset = Vector3.Lerp(before.offset, after.offset, t);
                return true;
            }
        }

        /// <summary>MAIN THREAD: interpolated joint local rotations + hips local position at <paramref name="renderTick"/>.</summary>
        /// <remarks>
        /// <paramref name="rotationsOut"/> is filled in <see cref="SkeletonWire.JOINT_INDICES"/> order.
        /// Same bracketing pair and end clamping as <see cref="TryGetInterpolatedRoot(int, int, out float, out Vector3)"/>
        /// — pass the SAME <paramref name="renderTick"/> to both so root and bones share one moment.
        /// </remarks>
        public bool TryGetInterpolatedBones(int playerId, int renderTick, Quaternion[] rotationsOut, out Vector3 hipsOut)
        {
            int jointCount = SkeletonWire.JointCount;
            if (rotationsOut == null || rotationsOut.Length < jointCount)
            {
                throw new ArgumentException($"Rotasyon tamponu {jointCount} elemandan kısa.", nameof(rotationsOut));
            }

            hipsOut = Vector3.zero;

            lock (_gate)
            {
                if (!_entries.TryGetValue(playerId, out SkeletonEntryState state) || state.count == 0)
                {
                    return false;
                }

                FindBracket(state, renderTick, out int beforeSlot, out int afterSlot);
                if (beforeSlot < 0 && afterSlot < 0)
                {
                    return false;
                }

                if (beforeSlot < 0 || afterSlot < 0)
                {
                    int slot = beforeSlot < 0 ? afterSlot : beforeSlot;
                    Array.Copy(state.rotations[slot], rotationsOut, jointCount);
                    hipsOut = state.ring[slot].hips;
                    return true;
                }

                float t = BracketT(state.ring[beforeSlot], state.ring[afterSlot], renderTick);
                Quaternion[] from = state.rotations[beforeSlot];
                Quaternion[] to = state.rotations[afterSlot];
                for (int j = 0; j < jointCount; j++)
                {
                    rotationsOut[j] = Quaternion.Slerp(from[j], to[j], t);
                }

                hipsOut = Vector3.Lerp(state.ring[beforeSlot].hips, state.ring[afterSlot].hips, t);
                return true;
            }
        }

        /// <summary>Age of the NEWEST sample in ms; <c>-1</c> when the player has no sample at all.</summary>
        /// <remarks>
        /// ⚠️ Samples never expire, so without an age a reader cannot tell a LIVE stream from one that
        /// stopped minutes ago — a dead stream means the body must come from the pose channel (§6.11).
        /// <para>⚠️ Same clock as <c>recvMs</c>, so this is a RECEIVE age: it deliberately includes network
        /// silence, which is exactly the fault being detected.</para>
        /// </remarks>
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

        /// <summary>Called when an avatar is destroyed / handed over: the new owner must not inherit stale frames.</summary>
        public void Forget(int playerId)
        {
            lock (_gate)
            {
                _entries.Remove(playerId);
            }
        }

        /// <summary>Ring slots bracketing <paramref name="renderMs"/>; <c>-1</c> = none on that side. Caller holds the lock.</summary>
        private static void FindBracket(SkeletonEntryState state, int renderMs, out int beforeSlot, out int afterSlot)
        {
            beforeSlot = -1;
            afterSlot = -1;

            int start = state.nextIndex - state.count;
            if (start < 0)
            {
                start += RING_SIZE;
            }

            // The ring is chronological: the last sample at/before renderMs, then the first after it.
            for (int i = 0; i < state.count; i++)
            {
                int slot = (start + i) % RING_SIZE;
                if (renderMs - state.ring[slot].recvMs >= 0)
                {
                    beforeSlot = slot;
                }
                else
                {
                    afterSlot = slot;
                    break;
                }
            }
        }

        private static float BracketT(in SkeletonSample before, in SkeletonSample after, int renderMs)
        {
            int span = after.recvMs - before.recvMs;
            return span > 0 ? Mathf.Clamp01((renderMs - before.recvMs) / (float)span) : 0f;
        }
    }
}
