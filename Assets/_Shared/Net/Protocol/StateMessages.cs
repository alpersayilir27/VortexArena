using System.IO;

namespace VortexArena.Protocol
{
    // UDP state layer — binary, little-endian (Docs/ArenaNet-Protokol.md §6).
    // Write writes the type byte; Read assumes the type byte has ALREADY BEEN READ by the
    // dispatcher (it parses the rest).

    /// Packet type bytes.
    public static class UdpPacketType
    {
        public const byte UdpHello = 0x00;
        public const byte PoseUpdate = 0x01;
        public const byte Snapshot = 0x02;
        public const byte FireEvent = 0x03;
        public const byte EventBatch = 0x04;

        /// <summary>0x05 — snapshot + event batch in ONE datagram (<see cref="SnapshotWithEvents"/>).
        /// When it does not fit, the server falls back to <c>0x02</c>+<c>0x04</c>; neither was removed.</summary>
        public const byte SnapshotWithEvents = 0x05;

        /// <summary>0x06 — RTT probe; the server echoes the same bytes back (<see cref="RttProbe"/>).
        /// <para>⚠️ <b>Unrelated</b> to <c>MessageTypes.Ping</c> on WS: that is the server's "send me a
        /// <c>status</c>" trigger over TCP and cannot measure latency (retransmits contaminate it).
        /// Latency is measured here, on the channel the game flows through.</para></summary>
        public const byte RttProbe = 0x06;

        /// <summary>0x07 — retargeted skeleton blob + head-anchored root (yaw + offset, v19)
        /// (<see cref="SkeletonUpdate"/>, §6.9). Client → server,
        /// <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>; players only.</summary>
        public const byte SkeletonUpdate = 0x07;

        /// <summary>0x08 — batch of skeleton entries (<see cref="SkeletonBatch"/>, §6.10).
        /// <para>⚠️ <b>Why a batch:</b> sending a separate datagram per player would mean N packets
        /// per target per tick, and in this product the bottleneck is packet count, not bandwidth
        /// (<c>Docs/Sistem-Ozeti.md</c> §3.12).</para></summary>
        public const byte SkeletonBatch = 0x08;

        /// <summary>0x09 — an owned object's pose (<see cref="ObjectPose"/>, §6.12). Client → server,
        /// <see cref="ArenaProtocol.OBJECT_POSE_RATE_HZ"/>; only the OWNER, and only while the object is
        /// awake and NOT held.
        /// <para>⚠️ The downlink is not a separate packet: the same pose rides the object section of
        /// <c>0x05</c> as <see cref="ObjectPoseEntry"/> (§6.8).</para></summary>
        public const byte ObjectPose = 0x09;
    }

    /// <summary>
    /// Pose block — <b>10 B</b> quantized (§6.2, v19): [u32 rot][i16 x][i16 y][i16 z], arena space.
    /// <para>Position: metres × <see cref="POS_UNITS_PER_METER"/> (2 mm steps, i16 → ±65.5 m,
    /// clamped at the ends). Rotation: "smallest three" — bits 31-30 index the largest |component|
    /// (0=x, 1=y, 2=z, 3=w); bits 29-20 / 19-10 / 9-0 hold the remaining three components in order,
    /// 10-bit signed, scaled by <see cref="ROT_COMPONENT_SCALE"/>; if the largest component is
    /// negative, −q is encoded instead (q ≡ −q). The skipped component is rebuilt as
    /// √(1−a²−b²−c²).</para>
    /// <para>⚠️ The fields stay <c>float</c> ON PURPOSE: quantization lives only in Write/Read, so
    /// no consumer changes. Precision (2 mm / ~0.1°) sits below tracking noise, and damage is
    /// computed client-side from the drawn body anyway (§10.3) — wire precision enters no authority
    /// decision. The win is DATAGRAM SIZE (airtime + the §6.8 combine budget), not bandwidth.</para>
    /// </summary>
    public struct PoseData
    {
        public const int SIZE = 10;

        /// <summary>Position quantization scale: 500 units per metre = 2 mm steps.</summary>
        public const float POS_UNITS_PER_METER = 500f;

        /// <summary>Smallest-three component scale: 511 / √½ — a unit quaternion's non-largest
        /// components stay within ±√½.</summary>
        public const float ROT_COMPONENT_SCALE = 511f / 0.70710678f;

        public float px, py, pz, qx, qy, qz, qw;

        public void Write(BinaryWriter w)
        {
            w.Write(PackRotation(qx, qy, qz, qw));
            w.Write(QuantizePosition(px));
            w.Write(QuantizePosition(py));
            w.Write(QuantizePosition(pz));
        }

        public static PoseData Read(BinaryReader r)
        {
            PoseData p;
            uint rot = r.ReadUInt32();
            p.px = r.ReadInt16() / POS_UNITS_PER_METER;
            p.py = r.ReadInt16() / POS_UNITS_PER_METER;
            p.pz = r.ReadInt16() / POS_UNITS_PER_METER;
            UnpackRotation(rot, out p.qx, out p.qy, out p.qz, out p.qw);
            return p;
        }

        internal static short QuantizePosition(float meters)
        {
            float scaled = meters * POS_UNITS_PER_METER;
            if (scaled >= short.MaxValue) return short.MaxValue;
            if (scaled <= short.MinValue) return short.MinValue;
            return (short)System.Math.Round(scaled);
        }

        private static uint PackRotation(float x, float y, float z, float w)
        {
            // Largest |component| is dropped and rebuilt on read.
            int largest = 0;
            float la = System.Math.Abs(x);
            float a = System.Math.Abs(y);
            if (a > la) { la = a; largest = 1; }
            a = System.Math.Abs(z);
            if (a > la) { la = a; largest = 2; }
            a = System.Math.Abs(w);
            if (a > la) { largest = 3; }

            // q ≡ −q: keep the dropped component non-negative so √ rebuilds it exactly.
            float largestValue = largest switch { 0 => x, 1 => y, 2 => z, _ => w };
            float sign = largestValue < 0f ? -1f : 1f;

            uint packed = (uint)largest << 30;
            int shift = 20;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                float c = (i switch { 0 => x, 1 => y, 2 => z, _ => w }) * sign;
                int q = (int)System.Math.Round(c * ROT_COMPONENT_SCALE);
                if (q > 511) q = 511;
                if (q < -511) q = -511;
                packed |= (uint)(q & 0x3FF) << shift;
                shift -= 10;
            }

            return packed;
        }

        private static void UnpackRotation(uint packed, out float x, out float y, out float z, out float w)
        {
            int largest = (int)(packed >> 30);
            float sumSq = 0f;
            float c0 = 0f, c1 = 0f, c2 = 0f;
            int shift = 20;
            for (int i = 0; i < 3; i++)
            {
                // 10-bit two's complement sign extension.
                int q = (int)((packed >> shift) & 0x3FF);
                if (q >= 512) q -= 1024;
                float c = q / ROT_COMPONENT_SCALE;
                if (i == 0) c0 = c; else if (i == 1) c1 = c; else c2 = c;
                sumSq += c * c;
                shift -= 10;
            }

            float rebuilt = sumSq < 1f ? (float)System.Math.Sqrt(1f - sumSq) : 0f;

            // The three stored components fill the non-largest slots in index order.
            x = y = z = w = 0f;
            int taken = 0;
            for (int i = 0; i < 4; i++)
            {
                float value;
                if (i == largest)
                {
                    value = rebuilt;
                }
                else
                {
                    value = taken switch { 0 => c0, 1 => c1, _ => c2 };
                    taken++;
                }

                if (i == 0) x = value; else if (i == 1) y = value; else if (i == 2) z = value; else w = value;
            }
        }
    }

    /// <summary>
    /// Skeleton root — <b>8 B</b> (§6.9, v19): [u16 yaw][i16 dx][i16 dy][i16 dz]. NOT an absolute
    /// pose: <c>yawDeg</c> is the body's arena yaw (360°/65536 steps) and the offset is the root's
    /// position relative to the POSE CHANNEL head's floor projection, in mm (i16 → ±32.7 m,
    /// clamped). The projection's y is 0, so <c>oy</c> is the root's arena height.
    /// <para><b>Why head-anchored:</b> the receiver rebuilds the root on its OWN interpolated head,
    /// so a lagging/dead skeleton channel can no longer separate the body from the name label —
    /// only the lean/crouch offset and the body yaw go stale (§6.9).</para>
    /// <para>Fields stay <c>float</c> (metres/degrees); quantization lives in Write/Read, like
    /// <see cref="PoseData"/>.</para>
    /// </summary>
    public struct SkeletonRootData
    {
        public const int SIZE = 8;

        /// <summary>Millimetre offset scale.</summary>
        public const float OFFSET_UNITS_PER_METER = 1000f;

        /// <summary>Body yaw in degrees (arena space).</summary>
        public float yawDeg;

        /// <summary>Root offset from the head's floor projection, metres (arena axes).</summary>
        public float ox, oy, oz;

        public void Write(BinaryWriter w)
        {
            w.Write(QuantizeYaw(yawDeg));
            w.Write(QuantizeOffset(ox));
            w.Write(QuantizeOffset(oy));
            w.Write(QuantizeOffset(oz));
        }

        public static SkeletonRootData Read(BinaryReader r)
        {
            SkeletonRootData d;
            d.yawDeg = r.ReadUInt16() * (360f / 65536f);
            d.ox = r.ReadInt16() / OFFSET_UNITS_PER_METER;
            d.oy = r.ReadInt16() / OFFSET_UNITS_PER_METER;
            d.oz = r.ReadInt16() / OFFSET_UNITS_PER_METER;
            return d;
        }

        private static ushort QuantizeYaw(float degrees)
        {
            float wrapped = degrees % 360f;
            if (wrapped < 0f) wrapped += 360f;
            return (ushort)((uint)System.Math.Round(wrapped * (65536f / 360f)) & 0xFFFF);
        }

        private static short QuantizeOffset(float meters)
        {
            float scaled = meters * OFFSET_UNITS_PER_METER;
            if (scaled >= short.MaxValue) return short.MaxValue;
            if (scaled <= short.MinValue) return short.MinValue;
            return (short)System.Math.Round(scaled);
        }
    }

    /// 0x00 — [u8 type][u8 playerId][u32 udpToken] = 6 B. The server echoes the same packet as an ack.
    public struct UdpHello
    {
        public const int SIZE = 6;

        public byte playerId;
        public uint udpToken;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.UdpHello);
            w.Write(playerId);
            w.Write(udpToken);
        }

        public static UdpHello Read(BinaryReader r)
        {
            UdpHello m;
            m.playerId = r.ReadByte();
            m.udpToken = r.ReadUInt32();
            return m;
        }
    }

    /// <summary>
    /// 0x09 — [u8 type][u8 playerId][u16 netId][u16 seq][pose 28] = <b>34 B</b> (client → server,
    /// <see cref="ArenaProtocol.OBJECT_POSE_RATE_HZ"/>; §6.12). Sent only by the OWNER, and only while
    /// the object is awake and NOT held — a held object hangs off the owner's hand, which already
    /// streams at 20 Hz; streaming both would drift the hand and the object apart.
    /// <para><c>seq</c> wraps PER OBJECT; an old <c>seq</c> drops the packet (the state-channel rule of
    /// <c>0x01</c>, §6.2).</para>
    /// <para>⚠️ The server does NOT validate the pose (it knows no metres), it validates OWNERSHIP; and
    /// it does not write the pose into the table as state, it relays it — the table is written by
    /// <c>object_rest</c>, because <c>world_state</c> must carry a RESTING pose, not a frame from
    /// mid-flight.</para>
    /// </summary>
    public struct ObjectPose
    {
        public const int SIZE = 16;

        public byte playerId;

        /// <summary>The object's network id (§10.10).</summary>
        public ushort netId;

        public ushort seq;

        /// <summary>The object's pose in <b>arena space</b> (§3).</summary>
        public PoseData pose;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.ObjectPose);
            w.Write(playerId);
            w.Write(netId);
            w.Write(seq);
            pose.Write(w);
        }

        public static ObjectPose Read(BinaryReader r)
        {
            ObjectPose m;
            m.playerId = r.ReadByte();
            m.netId = r.ReadUInt16();
            m.seq = r.ReadUInt16();
            m.pose = PoseData.Read(r);
            return m;
        }
    }

    /// <summary>The object entry of the <c>0x05</c> object section: [u16 netId][pose 10] = <b>12 B</b>
    /// (§6.12). <c>seq</c> is not carried — the server already dropped the old ones.</summary>
    public struct ObjectPoseEntry
    {
        public const int SIZE = 12;

        public ushort netId;
        public PoseData pose;

        public void Write(BinaryWriter w)
        {
            w.Write(netId);
            pose.Write(w);
        }

        public static ObjectPoseEntry Read(BinaryReader r)
        {
            ObjectPoseEntry e;
            e.netId = r.ReadUInt16();
            e.pose = PoseData.Read(r);
            return e;
        }
    }

    /// <summary>
    /// 0x05 — [u8 type][u8 playerCount][u8 eventCount][u8 objectCount][u32 serverTick] + playerCount×
    /// <see cref="SnapshotEntry"/> + eventCount×<see cref="FireEventEntry"/> + objectCount×
    /// <see cref="ObjectPoseEntry"/>. Header is <b>8 B</b>.
    /// <para>⚠️ <b><c>objectCount</c> grew the header from 7 B to 8 B in v18 and that BREAKS the wire:</b>
    /// a reader that does not know the field decodes the whole body one byte off, losing the entire
    /// snapshot, not just the objects.</para>
    /// <para>⚠️ <c>objectCount = 0</c> is perfectly normal — object poses only exist for awake, unheld
    /// objects. On the fallback path (<c>0x02</c>+<c>0x04</c>) the object section is DROPPED and no
    /// section is added there; what is lost is the smoothness of the motion, not the final pose (that
    /// comes over WS via <c>object_rest</c> → <c>object_state</c>).</para>
    /// <para><b>Exists for packet count:</b> a typical match (10 players) is 340 B of snapshot + 45 B
    /// of events — one datagram instead of <b>two</b> per target per tick. The win is <b>airtime</b>,
    /// not bandwidth (Docs/Sistem-Ozeti.md §3.12).</para>
    /// <para>⚠️ <c>0x02</c>/<c>0x04</c> were <b>not removed and will not be</b>: the server falls back
    /// to them when the snapshot must split (&gt;16 entries) or the total exceeds
    /// <see cref="ArenaProtocol.COMBINED_MAX_BYTES"/>.</para>
    /// <para>⚠️ <b>Per tick either 0x05 or 0x04, NEVER both.</b> §6.5's duplicate protection rests on
    /// "at most one event datagram per tick", keyed on <c>serverTick</c>; a second one is taken for an
    /// exact repeat and <b>dropped</b>. Same reason events never enter this packet when the snapshot is
    /// split.</para>
    /// </summary>
    public struct SnapshotWithEvents
    {
        /// <summary>[type][playerCount][eventCount][objectCount][serverTick] — the fixed part before the
        /// entries.</summary>
        public const int HEADER_SIZE = 8;

        public uint serverTick;
        public SnapshotEntry[] players;
        public FireEventEntry[] events;

        /// <summary>Object poses of this tick (§6.12); <c>null</c> or empty = no object section.</summary>
        public ObjectPoseEntry[] objects;

        public void Write(BinaryWriter w)
        {
            int objectCount = objects != null ? objects.Length : 0;
            w.Write(UdpPacketType.SnapshotWithEvents);
            w.Write((byte)players.Length);
            w.Write((byte)events.Length);
            w.Write((byte)objectCount);
            w.Write(serverTick);
            for (int i = 0; i < players.Length; i++) players[i].Write(w);
            for (int i = 0; i < events.Length; i++) events[i].Write(w);
            for (int i = 0; i < objectCount; i++) objects[i].Write(w);
        }

        public static SnapshotWithEvents Read(BinaryReader r)
        {
            SnapshotWithEvents m;
            byte playerCount = r.ReadByte();
            byte eventCount = r.ReadByte();
            byte objectCount = r.ReadByte();
            m.serverTick = r.ReadUInt32();
            m.players = new SnapshotEntry[playerCount];
            for (int i = 0; i < playerCount; i++) m.players[i] = SnapshotEntry.Read(r);
            m.events = new FireEventEntry[eventCount];
            for (int i = 0; i < eventCount; i++) m.events[i] = FireEventEntry.Read(r);
            m.objects = new ObjectPoseEntry[objectCount];
            for (int i = 0; i < objectCount; i++) m.objects[i] = ObjectPoseEntry.Read(r);
            return m;
        }
    }

    /// <summary>
    /// 0x06 — [u8 type][u8 playerId][u32 clientStamp] = <b>6 B</b>. Client → server, 1 Hz; the server
    /// echoes <b>the same 6 bytes</b> (the <see cref="UdpHello"/> ack pattern).
    /// <para><b>The CLIENT measures:</b> <c>RTT = now − clientStamp</c>. The stamp is opaque to the
    /// server, so <b>no clock sync is needed</b> (both stamps are the client's).</para>
    /// <para><b>Why a separate packet</b> (three alternatives rejected): <c>clientTimeMs</c> gives no
    /// absolute latency without clock sync; echoing the stamp inside the snapshot would make it
    /// per-target and turn one shared buffer into N serialisations per tick (§6.5 avoids the same);
    /// measuring over WS/TCP mixes in retransmits.</para>
    /// <para>⚠️ <b>Never raised above 1 Hz:</b> each probe costs 2 datagrams and the bottleneck is
    /// packet count. Jitter already comes from snapshot arrivals with <b>zero extra packets</b>; this
    /// packet only feeds the operator's "ping" number.</para>
    /// </summary>
    public struct RttProbe
    {
        public const int SIZE = 6;

        public byte playerId;

        /// <summary>The client's send moment — <b>meaningful only to the client</b>, the server does not read it.</summary>
        public uint clientStamp;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.RttProbe);
            w.Write(playerId);
            w.Write(clientStamp);
        }

        public static RttProbe Read(BinaryReader r)
        {
            RttProbe m;
            m.playerId = r.ReadByte();
            m.clientStamp = r.ReadUInt32();
            return m;
        }
    }

    /// <summary>
    /// 0x01 — [u8 type][u8 playerId][u16 seq][u32 clientTimeMs][u8 itemL][u8 itemR][u8 gripFlags]
    /// [head][handL][handR] = <b>41 B</b> (§6.2).
    /// <para><b>Why the item bytes ride the pose packet:</b> same authority — "what is in my hand" is,
    /// like "where my hand is", <b>client-authoritative presentation info</b>. The server copies them
    /// into the snapshot unvalidated (§6.3); there is NO item table on the server.</para>
    /// <para>⚠️ bit0 (<see cref="SnapshotEntry.FLAG_ALIVE"/>) in <c>gripFlags</c> is ignored — the
    /// server filters with <see cref="SnapshotEntry.GRIP_FLAG_MASK"/>, so a client cannot declare
    /// itself alive.</para>
    /// </summary>
    public struct PoseUpdate
    {
        public const int SIZE = 41;

        public byte playerId;
        public ushort seq;
        public uint clientTimeMs;

        /// <summary>The <c>netItemId</c> of the item in the left/right hand; 0 = empty hand (§6.6).</summary>
        public byte itemL;
        public byte itemR;

        /// <summary>Client bits copied into the snapshot (§6.3). The name says grip but the content is
        /// wider: grip, stale hand and the <b>violation measurements</b> ride here too.
        /// <para>⚠️ <b>The list of copied bits is NOT repeated here</b> — the single source of truth is
        /// <see cref="SnapshotEntry.GRIP_FLAG_MASK"/>; listed twice, one copy would silently fall behind
        /// when the mask grows.</para></summary>
        public byte gripFlags;

        public PoseData head;
        public PoseData handL;
        public PoseData handR;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.PoseUpdate);
            w.Write(playerId);
            w.Write(seq);
            w.Write(clientTimeMs);
            w.Write(itemL);
            w.Write(itemR);
            w.Write(gripFlags);
            head.Write(w);
            handL.Write(w);
            handR.Write(w);
        }

        public static PoseUpdate Read(BinaryReader r)
        {
            PoseUpdate m;
            m.playerId = r.ReadByte();
            m.seq = r.ReadUInt16();
            m.clientTimeMs = r.ReadUInt32();
            m.itemL = r.ReadByte();
            m.itemR = r.ReadByte();
            m.gripFlags = r.ReadByte();
            m.head = PoseData.Read(r);
            m.handL = PoseData.Read(r);
            m.handR = PoseData.Read(r);
            return m;
        }
    }

    /// <summary>
    /// Snapshot player entry: [u8 playerId][u8 flags][u8 itemL][u8 itemR][head][handL][handR]
    /// = <b>34 B</b> (§6.3).
    /// <para><b>flags: one byte, two writers</b> (the wire form of the authority split) — bit0
    /// <b>server</b> (authoritative <c>alive</c>), bits 1-5 copied from the client's <c>gripFlags</c>,
    /// bit6 <b>server</b> (spawn protection), bit7 copied from the client (out of bounds).
    /// <b>The byte is FULL</b> — no reserve bit left, a ninth state does not fit.</para>
    /// </summary>
    public struct SnapshotEntry
    {
        public const int SIZE = 34;

        /// <summary>Written by the server: the player is alive (authoritative state).</summary>
        public const byte FLAG_ALIVE = 1 << 0;

        /// <summary>Copied from the client: both hands hold the SAME item (NOT dual wielding, §6.6).</summary>
        public const byte FLAG_GRIP_LINKED = 1 << 1;

        /// <summary>Copied from the client: the primary hand is the right one. Meaningful only while
        /// <see cref="FLAG_GRIP_LINKED"/> is set.</summary>
        public const byte FLAG_PRIMARY_RIGHT = 1 << 2;

        /// <summary>
        /// Copied from the client: the left hand pose is <b>not a measurement</b> — the sender holds the
        /// last valid pose <b>relative to the head</b> (it travels with the body, it does not freeze in
        /// arena space).
        /// <para>On a dead controller battery the rig still writes the hand anchor and the read returns
        /// <c>(0,0,0)</c>. Cutting the stream or zeroing the hand is NOT an option: the packet is fixed
        /// length (no "player without a hand" wire state) and a zero pose puts the hand at the player's
        /// feet — hence the last valid pose, flagged here.</para>
        /// <para>⚠️ The flag exists for the receiver's <b>interpretation</b>: a stale hand is a guess —
        /// no aim/contact diagnosis rests on it, the admin just shows it to the operator.</para>
        /// </summary>
        public const byte FLAG_HAND_L_STALE = 1 << 3;

        /// <inheritdoc cref="FLAG_HAND_L_STALE"/>
        public const byte FLAG_HAND_R_STALE = 1 << 4;

        /// <summary>
        /// Copied from the client: the sender's body is <b>INSIDE an inner obstacle</b> (§10.9) — 30% of
        /// the body, the whole head or the whole weapon has entered the obstacle volume.
        /// <para>⚠️ <b>Measurement is the client's, the RESULT is the server's:</b> the bit only says
        /// "I am inside"; the server drains health on its own tick and clock (the <c>hit_report</c>
        /// damage model). A client cannot write damage on itself with it, only report that the penalty
        /// <b>starts</b>.</para>
        /// <para>⚠️ <b>State, not an event</b>: resent every packet, so a lost one self-repairs in
        /// 50 ms — an edge-triggered "I left" could get lost and leave the player in the wall
        /// forever.</para>
        /// </summary>
        public const byte FLAG_IN_OBSTACLE = 1 << 5;

        /// <summary>
        /// Written by the server: the player is under <b>SPAWN PROTECTION</b>, taking no damage (§10.4).
        /// <para>⚠️ <b>The duration is not on the wire</b> (no counterpart in <c>ModeRulesInfo</c>): the
        /// gate is server-side and this bit drives the shield; sending the number too would be a second
        /// source of truth.</para>
        /// <para>⚠️ <b>State, not an event</b>: it arrives in every snapshot, so it fades by itself when
        /// protection ends — no client-side counter.</para>
        /// <para>⚠️ NOT in <see cref="GRIP_FLAG_MASK"/>: a client cannot declare itself protected (same
        /// reasoning as <see cref="FLAG_ALIVE"/>).</para>
        /// </summary>
        public const byte FLAG_SPAWN_PROTECTED = 1 << 6;

        /// <summary>
        /// Copied from the client: the sender's <b>head is OUTSIDE the boundary's safe area</b>
        /// (<c>ArenaBoundary.IsOutOfBounds</c>, §10.9).
        /// <para>⚠️ <b>Measurement is the client's, the result the reader's</b> — the
        /// <see cref="FLAG_IN_OBSTACLE"/> pattern; the bit only says "I am outside".</para>
        /// <para>⚠️ <b>Produces NO penalty:</b> the server never turns it into health drain — a player
        /// whose calibration drifted a few centimetres would die for nothing. Consumed only by admin
        /// visibility (the top-down ring) and the violation log (§5.3 <c>violation</c>).</para>
        /// <para>⚠️ <b>State, not an event</b>: resent every packet, self-repairing in 50 ms — an
        /// edge-triggered "I came back in" could get lost and pin the player in violation
        /// forever.</para>
        /// </summary>
        public const byte FLAG_OUT_OF_BOUNDS = 1 << 7;

        /// <summary>
        /// The bits allowed to be copied from the client. <b>It exists as a guard:</b> the server
        /// filters <c>PoseUpdate.gripFlags</c> through this mask, so a client CANNOT set bit0
        /// (<see cref="FLAG_ALIVE"/>) — copying unmasked would let a dead player resurrect itself.
        /// <para>Masked bits are copied <b>unvalidated</b>: grip, stale hand, obstacle violation and out
        /// of bounds are <b>client-authoritative MEASUREMENT info</b>, like the item bytes
        /// (§6.6/§10.3/§10.9). Server-written bits stay OUTSIDE the mask.</para>
        /// </summary>
        public const byte GRIP_FLAG_MASK =
            FLAG_GRIP_LINKED | FLAG_PRIMARY_RIGHT | FLAG_HAND_L_STALE | FLAG_HAND_R_STALE |
            FLAG_IN_OBSTACLE | FLAG_OUT_OF_BOUNDS;

        public byte playerId;
        public byte flags;

        /// <summary>The <c>netItemId</c> of the item in the left/right hand; 0 = empty hand (§6.6).</summary>
        public byte itemL;
        public byte itemR;

        public PoseData head;
        public PoseData handL;
        public PoseData handR;

        public void Write(BinaryWriter w)
        {
            w.Write(playerId);
            w.Write(flags);
            w.Write(itemL);
            w.Write(itemR);
            head.Write(w);
            handL.Write(w);
            handR.Write(w);
        }

        public static SnapshotEntry Read(BinaryReader r)
        {
            SnapshotEntry e;
            e.playerId = r.ReadByte();
            e.flags = r.ReadByte();
            e.itemL = r.ReadByte();
            e.itemR = r.ReadByte();
            e.head = PoseData.Read(r);
            e.handL = PoseData.Read(r);
            e.handR = PoseData.Read(r);
            return e;
        }
    }

    /// 0x02 — [u8 type][u8 playerCount][u32 serverTick] + playerCount × SnapshotEntry.
    /// 16 players: 6 + 16×34 = 550 B (a single UDP packet).
    public struct Snapshot
    {
        public uint serverTick;
        public SnapshotEntry[] players;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.Snapshot);
            w.Write((byte)players.Length);
            w.Write(serverTick);
            for (int i = 0; i < players.Length; i++)
                players[i].Write(w);
        }

        public static Snapshot Read(BinaryReader r)
        {
            Snapshot s;
            byte count = r.ReadByte();
            s.serverTick = r.ReadUInt32();
            s.players = new SnapshotEntry[count];
            for (int i = 0; i < count; i++)
                s.players[i] = SnapshotEntry.Read(r);
            return s;
        }
    }

    /// <summary>
    /// A single shot/throw event: [u8 playerId][u8 kindHand][u8 itemId][i16 dirOctX][i16 dirOctY]
    /// [u16 magnitude] = <b>9 B</b>. The same fields ride both <c>0x03</c> (§6.4) and <c>0x04</c>
    /// (§6.5); <c>seq</c> exists uplink only.
    /// <para><b>Why the direction is on the wire:</b> derived from the 20 Hz interpolated hand pose,
    /// two shots on the same tick would share a direction and recoil would vanish. <b>Why the origin is
    /// NOT:</b> the tracer must leave the muzzle the receiver DRAWS; an absolute muzzle position looks
    /// visibly offset (consistency &gt; fidelity, §6.4).</para>
    /// </summary>
    public struct FireEventEntry
    {
        public const int SIZE = 9;

        /// <summary>Kind: hitscan shot.</summary>
        public const byte KIND_SHOT = 0;

        /// <summary>Kind: throw.</summary>
        public const byte KIND_THROW = 1;

        /// <summary>The low nibble of <c>kindHand</c> is the kind.</summary>
        public const byte KIND_MASK = 0x0F;

        /// <summary>Bit7 of <c>kindHand</c> is the hand: set = right, clear = left.</summary>
        public const byte HAND_RIGHT_BIT = 0x80;

        public byte playerId;

        /// <summary>Low nibble = kind (<see cref="KIND_MASK"/>), bit7 = hand (<see cref="HAND_RIGHT_BIT"/>).</summary>
        public byte kindHand;

        /// <summary>The item's <c>netItemId</c> at the moment of the event (§6.6) — resolves the
        /// presentation profile, so the event is self-sufficient even if the state byte is lost.</summary>
        public byte itemId;

        /// <summary>Octahedrally compressed unit direction, in arena space
        /// (<see cref="OctahedralDirection"/>).</summary>
        public short dirOctX, dirOctY;

        /// <summary>Depends on the kind: <b>distance</b> for a shot (cm → 0–655 m), <b>initial
        /// speed</b> for a throw (cm/s).</summary>
        public ushort magnitude;

        public static byte PackKindHand(byte kind, bool rightHand)
            => (byte)((kind & KIND_MASK) | (rightHand ? HAND_RIGHT_BIT : 0));

        public byte Kind => (byte)(kindHand & KIND_MASK);
        public bool IsRightHand => (kindHand & HAND_RIGHT_BIT) != 0;

        /// <summary>Body writer — there is NO type byte. Only <see cref="EventBatch"/> uses it; in
        /// <c>0x03</c> the field order differs (see <see cref="FireEvent"/>).</summary>
        public void Write(BinaryWriter w)
        {
            w.Write(playerId);
            w.Write(kindHand);
            w.Write(itemId);
            w.Write(dirOctX);
            w.Write(dirOctY);
            w.Write(magnitude);
        }

        public static FireEventEntry Read(BinaryReader r)
        {
            FireEventEntry e;
            e.playerId = r.ReadByte();
            e.kindHand = r.ReadByte();
            e.itemId = r.ReadByte();
            e.dirOctX = r.ReadInt16();
            e.dirOctY = r.ReadInt16();
            e.magnitude = r.ReadUInt16();
            return e;
        }
    }

    /// <summary>
    /// 0x03 — [u8 type][u8 playerId][u16 seq][u8 kindHand][u8 itemId][i16][i16][u16] = <b>12 B</b>
    /// (client → server, per event; §6.4). <b>Sent IMMEDIATELY</b>, no pose tick wait.
    /// <para><b>The <c>seq</c> contract — UPLINK ONLY:</b> ✅ <b>duplicate suppression</b> (server keeps
    /// the last <c>seq</c> per player; UDP may duplicate → double tracer + sound), ✅ <b>loss
    /// measurement</b> (a gap = lost events), ❌ <b>NO ORDER ENFORCEMENT.</b></para>
    /// <para>⚠️ "Drop an old <c>seq</c>" is a <b>POSE</b> rule (state: last one wins) and is NOT applied
    /// to events: a shot arriving out of order really happened; dropping it silently deletes a tracer
    /// and a sound.</para>
    /// </summary>
    public struct FireEvent
    {
        public const int SIZE = 12;

        public ushort seq;

        /// <summary>The event body. <c>entry.playerId</c> goes BEFORE <c>seq</c> on the wire (see below).</summary>
        public FireEventEntry entry;

        // ⚠️ Fields are written/read by hand, entry.Write/Read is NOT CALLED: in 0x03 the wire layout is
        // [type][playerId][seq][kindHand]…, i.e. the entry's playerId comes BEFORE seq.
        // FireEventEntry's own Write/Read is for the 0x04 body only (no seq, fields contiguous).
        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.FireEvent);
            w.Write(entry.playerId);
            w.Write(seq);
            w.Write(entry.kindHand);
            w.Write(entry.itemId);
            w.Write(entry.dirOctX);
            w.Write(entry.dirOctY);
            w.Write(entry.magnitude);
        }

        public static FireEvent Read(BinaryReader r)
        {
            FireEvent m;
            m.entry = default;
            m.entry.playerId = r.ReadByte();
            m.seq = r.ReadUInt16();
            m.entry.kindHand = r.ReadByte();
            m.entry.itemId = r.ReadByte();
            m.entry.dirOctX = r.ReadInt16();
            m.entry.dirOctY = r.ReadInt16();
            m.entry.magnitude = r.ReadUInt16();
            return m;
        }
    }

    /// <summary>
    /// 0x04 — [u8 type][u8 count][u32 serverTick] + count × <see cref="FireEventEntry"/>
    /// = 6 + count×9 B (server → all clients, 20 Hz; §6.5). With no events there is <b>no packet</b>.
    /// <para><b>Duplicate protection is the TICK, not <c>seq</c>:</b> a batch is identified by
    /// <c>serverTick</c> and <b>at most one batch per tick</b> is produced (hence an overflowing event
    /// shifts to the next tick instead of being dropped,
    /// <see cref="ArenaProtocol.EVENT_MAX_ENTRIES_PER_PACKET"/>). The client rings the last
    /// <see cref="ArenaProtocol.EVENT_TICK_HISTORY"/> ticks and drops only an <b>exact repeat</b>.</para>
    /// <para>⚠️ An old but unseen tick IS PLAYED (immediately if the interp clock passed it): a tracer
    /// ~50 ms late beats a lost one.</para>
    /// <para>⚠️ NOT added to the snapshot (<c>0x02</c>), a separate datagram — combined carriage is
    /// <c>0x05</c>'s job, and a variable-length section would break <c>0x02</c>'s fixed-entry
    /// fragmentation math.</para>
    /// </summary>
    public struct EventBatch
    {
        public uint serverTick;
        public FireEventEntry[] events;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.EventBatch);
            w.Write((byte)events.Length);
            w.Write(serverTick);
            for (int i = 0; i < events.Length; i++)
                events[i].Write(w);
        }

        public static EventBatch Read(BinaryReader r)
        {
            EventBatch b;
            byte count = r.ReadByte();
            b.serverTick = r.ReadUInt32();
            b.events = new FireEventEntry[count];
            for (int i = 0; i < count; i++)
                b.events[i] = FireEventEntry.Read(r);
            return b;
        }
    }

    /// <summary>
    /// 0x07 — [u8 type][u8 playerId][u16 seq][root: <see cref="SkeletonRootData"/> 8][u16 len][blob]
    /// (client → server, <see cref="ArenaProtocol.SKELETON_RATE_HZ"/>; players only, §6.9).
    /// <para><b>The blob is OPAQUE:</b> the Meta Movement SDK's native serialisation
    /// (<c>SerializeSkeletonAndFace</c>). The server neither unpacks nor validates it, it only copies —
    /// there is NO skeleton table on the server and none will be added. Same reasoning as the
    /// <c>netItemId</c> bytes (§6.6): <b>client-authoritative presentation info</b>.</para>
    /// <para>⚠️ <b>Why <c>root</c> is separate:</b> the blob's joint 0 is written with
    /// <c>JointType.NoWorldSpace</c>, i.e. <b>the sender's world pose</b>, unrelated to the receiver's
    /// arena. The blob being opaque, we cannot transform the root inside it — so the root rides
    /// separately (as yaw + head offset since v19, see <see cref="SkeletonRootData"/>) and the
    /// receiver writes it after <c>ApplyBodyPose</c>.</para>
    /// <para>⚠️ <b>NO fragmentation</b> here: a blob over
    /// <see cref="ArenaProtocol.SKELETON_MAX_BLOB_BYTES"/> is not sent at all — deserialising half a
    /// frame means a broken skeleton.</para>
    /// </summary>
    public struct SkeletonUpdate
    {
        /// <summary>[type][playerId][seq][root 8][len] — the fixed part before the blob.</summary>
        public const int HEADER_SIZE = 14;

        public byte playerId;

        /// <summary>Wraps (u16); an old <c>seq</c> drops the packet — the "last one wins" rule of
        /// <c>0x01</c> (§6.2). A state channel, not an event one.
        /// <para>⚠️ The server RESETS its skeleton ledger on a new hello, like the pose ledger
        /// (§6.9): a restarted client counts from 0 again, and a stale high value would silently
        /// reject every frame for minutes.</para></summary>
        public ushort seq;

        /// <summary>Body yaw + root offset from the pose-channel head (§6.9, v19).</summary>
        public SkeletonRootData root;

        /// <summary>The serialised skeleton. ⚠️ Only the first <see cref="blobLength"/> bytes are valid;
        /// the array length is not binding, so the sender may pass a pooled buffer.</summary>
        public byte[] blob;

        public int blobLength;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.SkeletonUpdate);
            w.Write(playerId);
            w.Write(seq);
            root.Write(w);
            w.Write((ushort)blobLength);
            w.Write(blob, 0, blobLength);
        }

        public static SkeletonUpdate Read(BinaryReader r)
        {
            SkeletonUpdate m;
            m.playerId = r.ReadByte();
            m.seq = r.ReadUInt16();
            m.root = SkeletonRootData.Read(r);
            int len = r.ReadUInt16();

            // A legitimate sender cannot exceed this limit, so a larger value means a corrupt/truncated
            // datagram — return an empty blob and let the caller drop the entry.
            if (len > ArenaProtocol.SKELETON_MAX_BLOB_BYTES)
            {
                m.blob = System.Array.Empty<byte>();
                m.blobLength = 0;
                return m;
            }

            m.blob = r.ReadBytes(len);

            // ⚠️ A truncated blob counts as empty — see SkeletonEntry.Read. ESPECIALLY critical here:
            // the server reads the uplink at this point and relays the blob AS IS, so half a frame would
            // draw EVERYONE in the arena with a broken skeleton. Its blobLength == 0 check only catches
            // truncation if this assignment is correct.
            if (m.blob.Length != len)
            {
                m.blob = System.Array.Empty<byte>();
                m.blobLength = 0;
                return m;
            }

            m.blobLength = len;
            return m;
        }
    }

    /// <summary>
    /// The player entry of the <c>0x08</c> batch: [u8 playerId][root 8][u16 len][blob]
    /// = <b>11 + len</b> B (§6.10). Fields mean the same as in <see cref="SkeletonUpdate"/>;
    /// <c>seq</c> is not carried (the server already filtered out old ones).
    /// </summary>
    public struct SkeletonEntry
    {
        /// <summary>[playerId][root 8][len] — the fixed part before the blob.</summary>
        public const int HEADER_SIZE = 11;

        public byte playerId;
        public SkeletonRootData root;
        public byte[] blob;
        public int blobLength;

        /// <summary>The bytes this entry takes on the wire — used when splitting the batch.</summary>
        public int Size => HEADER_SIZE + blobLength;

        public void Write(BinaryWriter w)
        {
            w.Write(playerId);
            root.Write(w);
            w.Write((ushort)blobLength);
            w.Write(blob, 0, blobLength);
        }

        public static SkeletonEntry Read(BinaryReader r)
        {
            SkeletonEntry e;
            e.playerId = r.ReadByte();
            e.root = SkeletonRootData.Read(r);
            int len = r.ReadUInt16();

            if (len > ArenaProtocol.SKELETON_MAX_BLOB_BYTES)
            {
                e.blob = System.Array.Empty<byte>();
                e.blobLength = 0;
                return e;
            }

            e.blob = r.ReadBytes(len);

            // ⚠️ A SHORT READ IS SILENT CORRUPTION: BinaryReader.ReadBytes does NOT throw at an early
            // stream end, it returns a shorter array. Taking the length from the requested byte count
            // would declare a truncated blob "valid" and the Movement SDK would deserialise it into a
            // broken skeleton (the remote avatar folding into random shapes). Half a frame is ignored:
            // RemoteSkeletonRegistry drops entries with blobLength = 0.
            if (e.blob.Length != len)
            {
                e.blob = System.Array.Empty<byte>();
                e.blobLength = 0;
                return e;
            }

            e.blobLength = len;
            return e;
        }
    }

    /// <summary>
    /// 0x08 — [u8 type][u8 count][u32 serverTick] + count × <see cref="SkeletonEntry"/>
    /// (server → all clients, §6.10). With no entries there is <b>no packet</b>.
    /// <para><b>Splitting:</b> entries being variable length, the server watches both the byte budget
    /// (<see cref="ArenaProtocol.COMBINED_MAX_BYTES"/>) and the entry ceiling
    /// (<see cref="ArenaProtocol.SKELETON_MAX_ENTRIES_PER_PACKET"/>); overflow spills into an extra
    /// datagram in the same tick. Each carries its own <c>count</c>, all share the <c>serverTick</c> —
    /// the snapshot split (§6.3), so the client needs no reassembly logic.</para>
    /// <para>⚠️ <b>The sender gets its own entry back and IGNORES it</b> — it draws its own body from
    /// the sensors. A per-target batch would mean N serialisations per tick; §6.5's event batch skips
    /// filtering for the same reason.</para>
    /// <para>⚠️ <b>Not combined</b> into the snapshot (<c>0x05</c>): the snapshot's size guarantee
    /// rests on fixed-size entries (550 B at 16), and a variable-length block would collapse it.</para>
    /// </summary>
    public struct SkeletonBatch
    {
        /// <summary>[type][count][serverTick] — the fixed part before the entries.</summary>
        public const int HEADER_SIZE = 6;

        public uint serverTick;
        public SkeletonEntry[] entries;

        /// <summary>
        /// Writes <b>a slice</b> of a shared array (splitting). Static rather than an instance method:
        /// the server keeps one buffer array per tick and sends it in pieces — a separate array per
        /// piece would be an allocation per tick.
        /// </summary>
        public static void Write(BinaryWriter w, uint serverTick, SkeletonEntry[] entries, int offset, int count)
        {
            w.Write(UdpPacketType.SkeletonBatch);
            w.Write((byte)count);
            w.Write(serverTick);
            for (int i = 0; i < count; i++)
                entries[offset + i].Write(w);
        }

        public static SkeletonBatch Read(BinaryReader r)
        {
            SkeletonBatch b;
            byte count = r.ReadByte();
            b.serverTick = r.ReadUInt32();
            b.entries = new SkeletonEntry[count];
            for (int i = 0; i < count; i++)
            {
                b.entries[i] = SkeletonEntry.Read(r);

                // Reading past a truncated entry would throw at stream end and take the intact entries
                // read BEFORE it down too. Cut the loop here; the remaining slots stay at
                // blobLength = 0 and are filtered out on the consumer side.
                if (b.entries[i].blobLength == 0)
                {
                    break;
                }
            }

            return b;
        }
    }
}
