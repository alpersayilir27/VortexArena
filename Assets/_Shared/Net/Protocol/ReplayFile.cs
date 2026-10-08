// Also linked into the nullable-enabled launcher; Unity and the server compile it without nullable.
#nullable disable
using System;
using System.IO;
using System.Text;

namespace VortexArena.Protocol
{
    // Match replay file (.vxr) — binary, little-endian (Docs/ArenaNet-Protokol.md §12).
    // Engine-free on purpose: the server writes it, the Unity replay player reads it (desktop today,
    // WebGL later — so reading works on any Stream/byte[], never on a file path).

    /// <summary>Replay record kinds (§12.3).</summary>
    public static class ReplayRecordKind
    {
        /// <summary>Admin-bound WS JSON text, UTF-8 (§5).</summary>
        public const byte Text = 1;

        /// <summary>Outbound UDP datagram, the exact bytes sent to every target (§6).</summary>
        public const byte Datagram = 2;

        /// <summary>Recording closed; payload = UTF-8 <see cref="ReplayEndReason"/>.</summary>
        public const byte End = 3;
    }

    /// <summary>Payload values of the <see cref="ReplayRecordKind.End"/> record.</summary>
    public static class ReplayEndReason
    {
        public const string Lobby = "lobby";
        public const string Restart = "restart";

        /// <summary>The operator turned recording off (§13).</summary>
        public const string Stopped = "stopped";

        public const string Shutdown = "shutdown";
    }

    /// <summary>Listing metadata stored after the header; playback state comes from records only.</summary>
    [Serializable]
    public class ReplayMeta
    {
        public string sceneName;
        public string modeId;
        public string venue;
        public int roundSeconds;
        public int scoreLimit;

        /// <summary>Player roster at the moment the recording opened (admins excluded).</summary>
        public ReplayRosterEntry[] players;
    }

    [Serializable]
    public class ReplayRosterEntry
    {
        public int playerId;
        public string name;
        public int number;
        public string team;
    }

    /// <summary>Fixed 32 B header (§12.3).</summary>
    public struct ReplayHeader
    {
        public ushort formatVersion;
        public ushort protocolVersion;
        public long startedUnixMs;

        /// <summary>0 until the server finalizes the file.</summary>
        public uint durationMs;

        /// <summary>0 until the server finalizes the file.</summary>
        public uint recordCount;

        public uint flags;
        public int metaLength;

        public bool IsFinalized => (flags & ReplayFile.FLAG_FINALIZED) != 0;
    }

    public struct ReplayRecord
    {
        public uint timeMs;
        public byte kind;
        public byte[] payload;
    }

    public static class ReplayFile
    {
        public const string EXTENSION = ".vxr";
        public const ushort FORMAT_VERSION = 1;
        public const uint FLAG_FINALIZED = 1;
        public const int HEADER_SIZE = 32;
        public const int RECORD_HEADER_SIZE = 9;

        /// <summary>Larger lengths are treated as corruption = end of file, never allocated.</summary>
        public const int MAX_PAYLOAD_BYTES = 1 << 20;

        /// <summary>Offset of <c>durationMs</c>; <c>recordCount</c> and <c>flags</c> follow it.</summary>
        public const int FINAL_FIELDS_OFFSET = 16;

        private static readonly byte[] Magic = { (byte)'V', (byte)'X', (byte)'R', (byte)'P' };

        public static void WriteHeader(BinaryWriter w, long startedUnixMs, byte[] metaUtf8)
        {
            w.Write(Magic);
            w.Write(FORMAT_VERSION);
            w.Write((ushort)ArenaProtocol.PROTOCOL_VERSION);
            w.Write(startedUnixMs);
            w.Write(0u); // durationMs
            w.Write(0u); // recordCount
            w.Write(0u); // flags
            w.Write((uint)metaUtf8.Length);
            w.Write(metaUtf8);
        }

        public static void WriteRecord(BinaryWriter w, uint timeMs, byte kind, byte[] payload)
        {
            w.Write(timeMs);
            w.Write(kind);
            w.Write((uint)payload.Length);
            w.Write(payload);
        }

        /// <summary>Patches duration/count/FINALIZED in place; the stream must be seekable.</summary>
        public static void WriteFinal(Stream s, uint durationMs, uint recordCount)
        {
            long end = s.Position;
            s.Position = FINAL_FIELDS_OFFSET;
            using (var w = new BinaryWriter(s, Encoding.UTF8, true))
            {
                w.Write(durationMs);
                w.Write(recordCount);
                w.Write(FLAG_FINALIZED);
            }
            s.Position = end;
        }

        /// <summary>Reads header + meta JSON. Rejects foreign files and unknown shell versions;
        /// the protocol version check is the caller's (§12.4).</summary>
        public static bool TryReadHeader(BinaryReader r, out ReplayHeader header, out string metaJson,
            out string error)
        {
            header = default;
            metaJson = null;
            error = null;
            try
            {
                byte[] magic = r.ReadBytes(Magic.Length);
                if (magic.Length != Magic.Length || magic[0] != Magic[0] || magic[1] != Magic[1]
                    || magic[2] != Magic[2] || magic[3] != Magic[3])
                {
                    error = "Maç kaydı dosyası değil.";
                    return false;
                }

                header.formatVersion = r.ReadUInt16();
                header.protocolVersion = r.ReadUInt16();
                header.startedUnixMs = r.ReadInt64();
                header.durationMs = r.ReadUInt32();
                header.recordCount = r.ReadUInt32();
                header.flags = r.ReadUInt32();
                uint metaLength = r.ReadUInt32();

                if (header.formatVersion != FORMAT_VERSION)
                {
                    error = $"Kayıt dosyası biçimi desteklenmiyor (v{header.formatVersion}, beklenen v{FORMAT_VERSION}).";
                    return false;
                }

                if (metaLength > MAX_PAYLOAD_BYTES)
                {
                    error = "Kayıt dosyası bozuk (meta uzunluğu).";
                    return false;
                }

                header.metaLength = (int)metaLength;
                byte[] meta = r.ReadBytes(header.metaLength);
                if (meta.Length != header.metaLength)
                {
                    error = "Kayıt dosyası bozuk (meta yarım).";
                    return false;
                }

                metaJson = Encoding.UTF8.GetString(meta);
                return true;
            }
            catch (EndOfStreamException)
            {
                error = "Kayıt dosyası bozuk (başlık yarım).";
                return false;
            }
        }

        /// <summary>Reads the next record; false at end of file, on a truncated tail or on a
        /// corrupt length — everything read before stays valid (§12.3).</summary>
        public static bool TryReadRecord(BinaryReader r, out ReplayRecord record)
        {
            record = default;
            try
            {
                record.timeMs = r.ReadUInt32();
                record.kind = r.ReadByte();
                uint length = r.ReadUInt32();
                if (length > MAX_PAYLOAD_BYTES) return false;

                record.payload = r.ReadBytes((int)length);
                return record.payload.Length == length;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }
    }
}
