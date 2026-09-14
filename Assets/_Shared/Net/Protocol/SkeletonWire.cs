using System;

namespace VortexArena.Protocol
{
    /// <summary>Layout of the skeleton blob inside <c>0x07</c>/<c>0x08</c> (§6.9) — VortexArena-authored.</summary>
    /// <remarks>
    /// [u8 FORMAT][u8 jointCount][i16 hipX][i16 hipY][i16 hipZ][i16 footY] + jointCount × u32
    /// smallest-three rotation (<see cref="PoseData"/> encoding), little-endian. All i16 values are mm;
    /// rotations = local rotations in <see cref="JOINT_INDICES"/> order. The server copies it without
    /// unpacking.
    /// <para>⚠️ <b>Two different spaces travel here.</b> <c>hipX/Y/Z</c> are the hips' local position in
    /// the sender avatar's PROPORTION space (the sender divides out its own body scale), because the
    /// receiver re-applies that same scale on the character root. <c>footY</c> is in REAL metres above
    /// the sender's root (floor-relative) and is NOT scaled — it is the grounding target.</para>
    /// <para>⚠️ Changing the list or layout is a <see cref="ArenaProtocol.PROTOCOL_VERSION"/> bump: the
    /// receiver rejects any frame whose size/format/count differs instead of misreading it.</para>
    /// </remarks>
    public static class SkeletonWire
    {
        public const byte FORMAT = 2;

        /// <summary>Target skeleton indices in wire order (body, arms, neck/head; no fingers).</summary>
        public static readonly int[] JOINT_INDICES =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18,
            39, 40, 41, 42, 43, 44, 45
        };

        /// <summary>Target skeleton index of the hips — the only joint whose position travels.</summary>
        public const int HIPS_INDEX = 1;

        /// <summary>First/last TARGET index of the two leg chains (UpLeg→Toe_End, left then right).</summary>
        private const int FIRST_LEG_JOINT = 2;

        /// <inheritdoc cref="FIRST_LEG_JOINT"/>
        private const int LAST_LEG_JOINT = 11;

        public const int HEADER_BYTES = 10;
        public const int BYTES_PER_JOINT = 4;

        /// <summary>Hips/foot quantization: 1 mm steps, i16 → ±32.7 m (clamped).</summary>
        public const float HIP_UNITS_PER_METER = 1000f;

        public static int JointCount => JOINT_INDICES.Length;

        // ⚠️ Must stay BELOW JOINT_INDICES: static initializers run in text order.

        /// <summary><see cref="JOINT_INDICES"/> SLOTS holding a leg joint — both ends must pick the same
        /// joints when measuring the lowest foot, or grounding would fight itself.</summary>
        public static readonly int[] LEG_SLOTS = BuildLegSlots();

        public static readonly int BLOB_BYTES = HEADER_BYTES + BYTES_PER_JOINT * JOINT_INDICES.Length;

        /// <summary>Writes one blob at <paramref name="offset"/>; returns <see cref="BLOB_BYTES"/>.</summary>
        /// <remarks><paramref name="rotXyzw"/> holds x,y,z,w per joint in <see cref="JOINT_INDICES"/>
        /// order. <paramref name="hipX"/>/<paramref name="hipY"/>/<paramref name="hipZ"/> are proportion
        /// space, <paramref name="footY"/> real metres (see the type remarks). Allocation-free.</remarks>
        public static int Write(byte[] dst, int offset, float hipX, float hipY, float hipZ, float footY,
            float[] rotXyzw)
        {
            int count = JOINT_INDICES.Length;
            if (dst == null || offset < 0 || dst.Length - offset < BLOB_BYTES)
            {
                throw new ArgumentException($"İskelet blob tamponu yetersiz (gereken {BLOB_BYTES} B).", nameof(dst));
            }

            if (rotXyzw == null || rotXyzw.Length < 4 * count)
            {
                throw new ArgumentException($"Rotasyon tamponu {4 * count} değerden kısa.", nameof(rotXyzw));
            }

            int p = offset;
            dst[p++] = FORMAT;
            dst[p++] = (byte)count;
            p = WriteInt16(dst, p, QuantizeHip(hipX));
            p = WriteInt16(dst, p, QuantizeHip(hipY));
            p = WriteInt16(dst, p, QuantizeHip(hipZ));
            p = WriteInt16(dst, p, QuantizeHip(footY));

            for (int i = 0; i < count; i++)
            {
                int r = 4 * i;
                uint packed = PoseData.PackRotation(rotXyzw[r], rotXyzw[r + 1], rotXyzw[r + 2], rotXyzw[r + 3]);
                dst[p++] = (byte)packed;
                dst[p++] = (byte)(packed >> 8);
                dst[p++] = (byte)(packed >> 16);
                dst[p++] = (byte)(packed >> 24);
            }

            return BLOB_BYTES;
        }

        /// <summary>Decodes one blob; false on size, format or joint count mismatch.</summary>
        /// <remarks>A pre-v22 (SDK-serialised) or format-1 blob fails the size/format check. Nothing is
        /// written to <paramref name="rotXyzwOut"/> on false. Allocation-free.</remarks>
        public static bool TryRead(byte[] src, int offset, int length,
            out float hipX, out float hipY, out float hipZ, out float footY, float[] rotXyzwOut)
        {
            hipX = hipY = hipZ = footY = 0f;

            int count = JOINT_INDICES.Length;
            if (rotXyzwOut == null || rotXyzwOut.Length < 4 * count)
            {
                throw new ArgumentException($"Rotasyon tamponu {4 * count} değerden kısa.", nameof(rotXyzwOut));
            }

            if (src == null || offset < 0 || length != BLOB_BYTES || src.Length - offset < length ||
                src[offset] != FORMAT || src[offset + 1] != count)
            {
                return false;
            }

            int p = offset + 2;
            hipX = ReadInt16(src, p) / HIP_UNITS_PER_METER;
            hipY = ReadInt16(src, p + 2) / HIP_UNITS_PER_METER;
            hipZ = ReadInt16(src, p + 4) / HIP_UNITS_PER_METER;
            footY = ReadInt16(src, p + 6) / HIP_UNITS_PER_METER;
            p += 8;

            for (int i = 0; i < count; i++)
            {
                uint packed = (uint)(src[p] | src[p + 1] << 8 | src[p + 2] << 16 | src[p + 3] << 24);
                p += 4;

                int r = 4 * i;
                PoseData.UnpackRotation(packed,
                    out rotXyzwOut[r], out rotXyzwOut[r + 1], out rotXyzwOut[r + 2], out rotXyzwOut[r + 3]);
            }

            return true;
        }

        /// <summary>Derived from <see cref="JOINT_INDICES"/> instead of authored: a change to the joint
        /// list must not leave a stale hand-written slot list behind.</summary>
        private static int[] BuildLegSlots()
        {
            int count = 0;
            for (int i = 0; i < JOINT_INDICES.Length; i++)
            {
                if (JOINT_INDICES[i] >= FIRST_LEG_JOINT && JOINT_INDICES[i] <= LAST_LEG_JOINT)
                {
                    count++;
                }
            }

            var slots = new int[count];
            int next = 0;
            for (int i = 0; i < JOINT_INDICES.Length; i++)
            {
                if (JOINT_INDICES[i] >= FIRST_LEG_JOINT && JOINT_INDICES[i] <= LAST_LEG_JOINT)
                {
                    slots[next++] = i;
                }
            }

            return slots;
        }

        private static short QuantizeHip(float meters)
        {
            float scaled = meters * HIP_UNITS_PER_METER;
            if (scaled >= short.MaxValue) return short.MaxValue;
            if (scaled <= short.MinValue) return short.MinValue;
            return (short)Math.Round(scaled);
        }

        private static int WriteInt16(byte[] dst, int p, short value)
        {
            dst[p] = (byte)value;
            dst[p + 1] = (byte)(value >> 8);
            return p + 2;
        }

        private static short ReadInt16(byte[] src, int p)
        {
            return (short)(src[p] | src[p + 1] << 8);
        }
    }
}
