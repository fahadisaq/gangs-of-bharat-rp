using System;
using System.Text;

namespace GangsOfBharat.Shared.Serialization
{
    /// <summary>
    /// Little-endian, LSB-first bit packer. Reuse one instance per thread and call Reset()
    /// between packets so the hot path does not allocate.
    /// </summary>
    public sealed class BitWriter
    {
        private byte[] _buffer;
        private int _bitPosition;
        private byte[] _stringScratch = new byte[256];

        public BitWriter() : this(1500)
        {
        }

        public BitWriter(int initialCapacityBytes)
        {
            if (initialCapacityBytes < 16)
            {
                initialCapacityBytes = 16;
            }
            _buffer = new byte[initialCapacityBytes];
        }

        /// <summary>Underlying buffer. Valid bytes are [0, LengthBytes).</summary>
        public byte[] Buffer
        {
            get { return _buffer; }
        }

        public int BitPosition
        {
            get { return _bitPosition; }
        }

        /// <summary>Number of bytes touched so far (partial last byte counts as one).</summary>
        public int LengthBytes
        {
            get { return (_bitPosition + 7) >> 3; }
        }

        public void Reset()
        {
            int used = LengthBytes;
            Array.Clear(_buffer, 0, used);
            _bitPosition = 0;
        }

        private void EnsureBits(int extraBits)
        {
            int neededBytes = (_bitPosition + extraBits + 7) >> 3;
            if (neededBytes <= _buffer.Length)
            {
                return;
            }
            int newSize = _buffer.Length * 2;
            while (newSize < neededBytes)
            {
                newSize *= 2;
            }
            Array.Resize(ref _buffer, newSize);
        }

        /// <summary>Writes the low <paramref name="bits"/> bits of value (1..32).</summary>
        public void WriteBits(uint value, int bits)
        {
            if (bits <= 0)
            {
                return;
            }
            if (bits > 32)
            {
                throw new ArgumentOutOfRangeException("bits");
            }
            if (bits < 32)
            {
                value &= (1u << bits) - 1u;
            }
            EnsureBits(bits);
            while (bits > 0)
            {
                int byteIndex = _bitPosition >> 3;
                int bitOffset = _bitPosition & 7;
                int free = 8 - bitOffset;
                int n = bits < free ? bits : free;
                uint mask = (1u << n) - 1u;
                _buffer[byteIndex] = (byte)(_buffer[byteIndex] | ((value & mask) << bitOffset));
                value >>= n;
                bits -= n;
                _bitPosition += n;
            }
        }

        public void WriteBool(bool value)
        {
            WriteBits(value ? 1u : 0u, 1);
        }

        public void WriteByte(byte value)
        {
            WriteBits(value, 8);
        }

        public void WriteUShort(ushort value)
        {
            WriteBits(value, 16);
        }

        public void WriteUInt(uint value)
        {
            WriteBits(value, 32);
        }

        public void WriteULong(ulong value)
        {
            WriteBits((uint)(value & 0xFFFFFFFFu), 32);
            WriteBits((uint)(value >> 32), 32);
        }

        /// <summary>LEB128-style varint: 7 data bits plus a continuation bit per group.</summary>
        public void WriteVarULong(ulong value)
        {
            while (value >= 0x80)
            {
                WriteBits((uint)(value & 0x7F) | 0x80u, 8);
                value >>= 7;
            }
            WriteBits((uint)value, 8);
        }

        public void WriteVarUInt(uint value)
        {
            WriteVarULong(value);
        }

        /// <summary>ZigZag-encoded signed varint, so small negative numbers stay small.</summary>
        public void WriteVarLong(long value)
        {
            ulong zigzag = (ulong)((value << 1) ^ (value >> 63));
            WriteVarULong(zigzag);
        }

        public void WriteVarInt(int value)
        {
            WriteVarLong(value);
        }

        /// <summary>
        /// Quantizes value in [min, max] to an integer of <paramref name="bits"/> bits.
        /// Maximum error is (max - min) / (2^bits - 1) / 2.
        /// </summary>
        public void WriteQuantized(float value, float min, float max, int bits)
        {
            WriteBits(Quantization.Quantize(value, min, max, bits), bits);
        }

        /// <summary>Angle in radians, wrapped to [0, 2pi) and quantized to bits.</summary>
        public void WriteAngle(float radians, int bits)
        {
            WriteBits(Quantization.QuantizeAngle(radians, bits), bits);
        }

        /// <summary>World position quantized inside WorldBounds (about 1 cm precision).</summary>
        public void WritePosition(float x, float y, float z)
        {
            WriteQuantized(x, WorldBounds.MinX, WorldBounds.MaxX, WorldBounds.HorizontalBits);
            WriteQuantized(y, WorldBounds.MinY, WorldBounds.MaxY, WorldBounds.VerticalBits);
            WriteQuantized(z, WorldBounds.MinZ, WorldBounds.MaxZ, WorldBounds.HorizontalBits);
        }

        public void WriteBytes(byte[] data, int offset, int count)
        {
            if (count <= 0)
            {
                return;
            }
            if ((_bitPosition & 7) == 0)
            {
                EnsureBits(count * 8);
                System.Buffer.BlockCopy(data, offset, _buffer, _bitPosition >> 3, count);
                _bitPosition += count * 8;
                return;
            }
            for (int i = 0; i < count; i++)
            {
                WriteBits(data[offset + i], 8);
            }
        }

        /// <summary>
        /// UTF-8 string with a varint byte length. Strings longer than maxBytes are cut at a
        /// UTF-8 code point boundary, so Devanagari text is never split mid-character.
        /// </summary>
        public void WriteString(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteVarUInt(0);
                return;
            }
            int needed = Encoding.UTF8.GetMaxByteCount(value.Length);
            if (_stringScratch.Length < needed)
            {
                _stringScratch = new byte[needed];
            }
            int length = Encoding.UTF8.GetBytes(value, 0, value.Length, _stringScratch, 0);
            if (length > maxBytes)
            {
                length = maxBytes;
                while (length > 0 && (_stringScratch[length] & 0xC0) == 0x80)
                {
                    length--;
                }
            }
            WriteVarUInt((uint)length);
            WriteBytes(_stringScratch, 0, length);
        }

        public void AlignToByte()
        {
            int rem = _bitPosition & 7;
            if (rem != 0)
            {
                WriteBits(0, 8 - rem);
            }
        }

        /// <summary>Copies the written bytes into a new array (not for hot paths).</summary>
        public byte[] ToArray()
        {
            byte[] result = new byte[LengthBytes];
            System.Buffer.BlockCopy(_buffer, 0, result, 0, result.Length);
            return result;
        }
    }
}
