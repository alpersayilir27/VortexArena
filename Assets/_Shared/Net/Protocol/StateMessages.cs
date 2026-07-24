using System.IO;

namespace VortexArena.Protocol
{
    // UDP state katmanı — binary, little-endian (Docs/ArenaNet-Protokol.md §6).
    // Write tip baytını yazar; Read, tip baytının dispatcher tarafından
    // OKUNMUŞ olduğunu varsayar (geri kalanı parse eder).

    /// Paket tip baytları.
    public static class UdpPacketType
    {
        public const byte UdpHello = 0x00;
        public const byte PoseUpdate = 0x01;
        public const byte Snapshot = 0x02;
    }

    /// Poz bloğu: f32 px,py,pz,qx,qy,qz,qw — 28 B, arena uzayında.
    public struct PoseData
    {
        public const int SIZE = 28;

        public float px, py, pz, qx, qy, qz, qw;

        public void Write(BinaryWriter w)
        {
            w.Write(px); w.Write(py); w.Write(pz);
            w.Write(qx); w.Write(qy); w.Write(qz); w.Write(qw);
        }

        public static PoseData Read(BinaryReader r)
        {
            PoseData p;
            p.px = r.ReadSingle(); p.py = r.ReadSingle(); p.pz = r.ReadSingle();
            p.qx = r.ReadSingle(); p.qy = r.ReadSingle(); p.qz = r.ReadSingle(); p.qw = r.ReadSingle();
            return p;
        }
    }

    /// 0x00 — [u8 tip][u8 playerId][u32 udpToken] = 6 B. Sunucu aynı paketi ack olarak geri yollar.
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

    /// 0x01 — [u8 tip][u8 playerId][u16 seq][u32 clientTimeMs][head][handL][handR] = 92 B.
    public struct PoseUpdate
    {
        public const int SIZE = 92;

        public byte playerId;
        public ushort seq;
        public uint clientTimeMs;
        public PoseData head;
        public PoseData handL;
        public PoseData handR;

        public void Write(BinaryWriter w)
        {
            w.Write(UdpPacketType.PoseUpdate);
            w.Write(playerId);
            w.Write(seq);
            w.Write(clientTimeMs);
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
            m.head = PoseData.Read(r);
            m.handL = PoseData.Read(r);
            m.handR = PoseData.Read(r);
            return m;
        }
    }

    /// Snapshot oyuncu girdisi: [u8 playerId][u8 flags][head][handL][handR] = 86 B.
    public struct SnapshotEntry
    {
        public const int SIZE = 86;
        public const byte FLAG_ALIVE = 1 << 0;

        public byte playerId;
        public byte flags;
        public PoseData head;
        public PoseData handL;
        public PoseData handR;

        public void Write(BinaryWriter w)
        {
            w.Write(playerId);
            w.Write(flags);
            head.Write(w);
            handL.Write(w);
            handR.Write(w);
        }

        public static SnapshotEntry Read(BinaryReader r)
        {
            SnapshotEntry e;
            e.playerId = r.ReadByte();
            e.flags = r.ReadByte();
            e.head = PoseData.Read(r);
            e.handL = PoseData.Read(r);
            e.handR = PoseData.Read(r);
            return e;
        }
    }

    /// 0x02 — [u8 tip][u8 playerCount][u32 serverTick] + playerCount × SnapshotEntry.
    /// 16 oyuncu: 6 + 16×86 = 1382 B (tek UDP paketi).
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
}
