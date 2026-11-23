using System;
using GangsOfBharat.Shared.Serialization;

namespace GangsOfBharat.Tests
{
    public static class SerializationTests
    {
        private static BitReader ReaderFor(BitWriter w)
        {
            return new BitReader(w.Buffer, 0, w.LengthBytes);
        }

        [Test]
        public static void BitsRoundTripRandomWidths()
        {
            Random rng = new Random(1);
            int[] widths = new int[2000];
            uint[] values = new uint[2000];
            BitWriter w = new BitWriter(16);
            for (int i = 0; i < widths.Length; i++)
            {
                widths[i] = 1 + rng.Next(32);
                uint v = (uint)rng.Next() ^ ((uint)rng.Next() << 16);
                values[i] = widths[i] == 32 ? v : v & ((1u << widths[i]) - 1u);
                w.WriteBits(values[i], widths[i]);
            }
            BitReader r = ReaderFor(w);
            for (int i = 0; i < widths.Length; i++)
            {
                Assert.Equal(values[i], r.ReadBits(widths[i]), "value " + i);
            }
            Assert.False(r.Overflowed);
        }

        [Test]
        public static void PrimitivesAndVarintsRoundTrip()
        {
            BitWriter w = new BitWriter();
            w.WriteBool(true);
            w.WriteByte(0xAB);
            w.WriteUShort(0xBEEF);
            w.WriteUInt(0xDEADBEEF);
            w.WriteULong(0x0123456789ABCDEFUL);
            uint[] uvals = { 0, 1, 127, 128, 300, 16383, 16384, uint.MaxValue };
            foreach (uint v in uvals)
            {
                w.WriteVarUInt(v);
            }
            long[] lvals = { 0, -1, 1, -64, 64, long.MaxValue, long.MinValue, 15000000000L, -250000L };
            foreach (long v in lvals)
            {
                w.WriteVarLong(v);
            }
            BitReader r = ReaderFor(w);
            Assert.True(r.ReadBool());
            Assert.Equal((byte)0xAB, r.ReadByte());
            Assert.Equal((ushort)0xBEEF, r.ReadUShort());
            Assert.Equal(0xDEADBEEFu, r.ReadUInt());
            Assert.Equal(0x0123456789ABCDEFUL, r.ReadULong());
            foreach (uint v in uvals)
            {
                Assert.Equal(v, r.ReadVarUInt());
            }
            foreach (long v in lvals)
            {
                Assert.Equal(v, r.ReadVarLong());
            }
            Assert.False(r.Overflowed);
        }

        [Test]
        public static void VarintSizesAreCompact()
        {
            BitWriter w = new BitWriter();
            w.WriteVarUInt(127);
            Assert.Equal(1, w.LengthBytes);
            w.Reset();
            w.WriteVarUInt(128);
            Assert.Equal(2, w.LengthBytes);
            w.Reset();
            w.WriteVarInt(-1);
            Assert.Equal(1, w.LengthBytes, "zigzag keeps -1 to one byte");
        }

        [Test]
        public static void QuantizedFloatsStayWithinErrorBound()
        {
            Random rng = new Random(2);
            BitWriter w = new BitWriter();
            float[] values = new float[1000];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (float)(rng.NextDouble() * 20.0 - 10.0);
                w.WriteQuantized(values[i], -10f, 10f, 12);
            }
            float bound = Quantization.MaxError(-10f, 10f, 12) + 1e-5f;
            BitReader r = ReaderFor(w);
            for (int i = 0; i < values.Length; i++)
            {
                Assert.Near(values[i], r.ReadQuantized(-10f, 10f, 12), bound);
            }
            // Clamping outside the range
            w.Reset();
            w.WriteQuantized(50f, -10f, 10f, 8);
            w.WriteQuantized(float.NaN, -10f, 10f, 8);
            r = ReaderFor(w);
            Assert.Near(10f, r.ReadQuantized(-10f, 10f, 8), 1e-4);
            Assert.Near(-10f, r.ReadQuantized(-10f, 10f, 8), 1e-4);
        }

        [Test]
        public static void PositionsHaveAboutOneCentimetrePrecision()
        {
            float hBound = Quantization.MaxError(WorldBounds.MinX, WorldBounds.MaxX, WorldBounds.HorizontalBits);
            float vBound = Quantization.MaxError(WorldBounds.MinY, WorldBounds.MaxY, WorldBounds.VerticalBits);
            Assert.AtMost(0.005, hBound, "horizontal error bound");
            Assert.AtMost(0.005, vBound, "vertical error bound");
            Random rng = new Random(3);
            BitWriter w = new BitWriter();
            float[] xs = new float[500];
            float[] ys = new float[500];
            float[] zs = new float[500];
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] = (float)(rng.NextDouble() * WorldBounds.Size);
                ys[i] = (float)(rng.NextDouble() * 1000.0 - 120.0);
                zs[i] = (float)(rng.NextDouble() * WorldBounds.Size);
                w.WritePosition(xs[i], ys[i], zs[i]);
            }
            Assert.Equal(500 * 57, w.BitPosition, "57 bits per position");
            BitReader r = ReaderFor(w);
            for (int i = 0; i < xs.Length; i++)
            {
                float x;
                float y;
                float z;
                r.ReadPosition(out x, out y, out z);
                Assert.Near(xs[i], x, hBound + 0.0005);
                Assert.Near(ys[i], y, vBound + 0.0005);
                Assert.Near(zs[i], z, hBound + 0.0005);
            }
        }

        [Test]
        public static void AnglesWrapAndQuantize()
        {
            int bits = 12;
            double bound = Math.PI / (1 << bits) + 1e-5;
            float[] angles = { 0f, 1f, 3.1f, 6.2f, -1f, 7.5f, -20f, (float)(Math.PI * 2 - 0.0001) };
            BitWriter w = new BitWriter();
            foreach (float a in angles)
            {
                w.WriteAngle(a, bits);
            }
            BitReader r = ReaderFor(w);
            foreach (float a in angles)
            {
                float back = r.ReadAngle(bits);
                Assert.AtMost(bound, Quantization.AngleDelta(a, back), "angle " + a);
            }
        }

        [Test]
        public static void StringsRoundTripUtf8AndRespectCap()
        {
            BitWriter w = new BitWriter();
            w.WriteBits(1, 3); // unaligned on purpose
            w.WriteString("Namaste Mayanagri", 64);
            w.WriteString("नमस्ते मायानगरी", 128);
            w.WriteString(string.Empty, 10);
            w.WriteString(null, 10);
            w.WriteString("अआइ", 7); // 9 bytes, cap 7 -> cut at char boundary = 6 bytes = 2 chars
            BitReader r = ReaderFor(w);
            Assert.Equal(1u, r.ReadBits(3));
            Assert.Equal("Namaste Mayanagri", r.ReadString(64));
            Assert.Equal("नमस्ते मायानगरी", r.ReadString(128));
            Assert.Equal(string.Empty, r.ReadString(10));
            Assert.Equal(string.Empty, r.ReadString(10));
            Assert.Equal("अआ", r.ReadString(7));
            Assert.False(r.Overflowed);
        }

        [Test]
        public static void ReaderRejectsOversizedAndTruncatedInput()
        {
            BitWriter w = new BitWriter();
            w.WriteString("this string is far too long", 100);
            BitReader r = ReaderFor(w);
            r.ReadString(5);
            Assert.True(r.Overflowed, "length above maxBytes fails the stream");

            r = new BitReader(new byte[] { 0x01 }, 0, 1);
            r.ReadUInt();
            Assert.True(r.Overflowed, "reading past the end");
            Assert.Equal(0u, r.ReadBits(8), "returns zero after overflow");

            r = new BitReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, 0, 11);
            r.ReadVarULong();
            Assert.True(r.Overflowed, "varint longer than 10 bytes");
        }
    }
}
