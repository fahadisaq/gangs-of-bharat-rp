using System;
using System.Text;

namespace GangsOfBharat.Shared.Serialization
{
    /// <summary>
    /// Reader matching BitWriter. Never throws on malformed input: reading past the end sets
    /// Overflowed and returns zeros, so callers check Overflowed once after parsing a packet.
    /// </summary>
    public sealed class BitReader
    {
        private byte[] _buffer;
        private int _startBit;
        private int _endBit;
        private int _bitPosition;
        private bool _overflowed;

        public BitReader()
        {
            _buffer = new byte[0];
        }

        public BitReader(byte[] buffer, int offset, int count)
        {
            Reset(buffer, offset, count);
        }

        public void Reset(byte[] buffer, int offset, int count)
        {
            _buffer = buffer;
            _startBit = offset * 8;
            _endBit = (offset + count) * 8;
            _bitPosition = _startBit;
            _overflowed = false;
        }

        public bool Overflowed
        {
            get { return _overflowed; }
        }

        /// <summary>Marks the stream as invalid (used for semantic errors such as bad lengths).</summary>
        public void Fail()
        {
            _overflowed = true;
        }

        public int BitsRemaining
        {
            get { return _endBit - _bitPosition; }
        }

        /// <summary>Byte offset of the next byte boundary in the underlying buffer.</summary>
        public int BytePosition
        {
            get { return (_bitPosition + 7) >> 3; }
        }

        public uint ReadBits(int bits)
        {
            if (bits <= 0)
            {
                return 0;
            }
            if (bits > 32 || _overflowed || _bitPosition + bits > _endBit)
            {
                _overflowed = true;
                return 0;
            }
            uint result = 0;
            int shift = 0;
            while (bits > 0)
            {
                int byteIndex = _bitPosition >> 3;
                int bitOffset = _bitPosition & 7;
                int available = 8 - bitOffset;
                int n = bits < available ? bits : available;
                uint chunk = ((uint)_buffer[byteIndex] >> bitOffset) & ((1u << n) - 1u);
                result |= chunk << shift;
                shift += n;
                bits -= n;
                _bitPosition += n;
            }
            return result;
        }

        public bool ReadBool()
        {
            return ReadBits(1) != 0;
        }

        public byte ReadByte()
        {
            return (byte)ReadBits(8);
        }

        public ushort ReadUShort()
        {
            return (ushort)ReadBits(16);
        }

        public uint ReadUInt()
        {
            return ReadBits(32);
        }

        public ulong ReadULong()
        {
            ulong low = ReadBits(32);
            ulong high = ReadBits(32);
            return low | (high << 32);
        }

        public ulong ReadVarULong()
        {
            ulong result = 0;
            int shift = 0;
            for (int i = 0; i < 10; i++)
            {
                uint b = ReadBits(8);
                if (_overflowed)
                {
                    return 0;
                }
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
                shift += 7;
            }
            _overflowed = true;
            return 0;
        }

        public uint ReadVarUInt()
        {
            ulong value = ReadVarULong();
            if (value > uint.MaxValue)
            {
                _overflowed = true;
                return 0;
            }
            return (uint)value;
        }

        public long ReadVarLong()
        {
            ulong zigzag = ReadVarULong();
            return (long)(zigzag >> 1) ^ -(long)(zigzag & 1);
        }

        public int ReadVarInt()
        {
            long value = ReadVarLong();
            if (value > int.MaxValue || value < int.MinValue)
            {
                _overflowed = true;
                return 0;
            }
            return (int)value;
        }

        public float ReadQuantized(float min, float max, int bits)
        {
            return Quantization.Dequantize(ReadBits(bits), min, max, bits);
        }

        public float ReadAngle(int bits)
        {
            return Quantization.DequantizeAngle(ReadBits(bits), bits);
        }

        public void ReadPosition(out float x, out float y, out float z)
        {
            x = ReadQuantized(WorldBounds.MinX, WorldBounds.MaxX, WorldBounds.HorizontalBits);
            y = ReadQuantized(WorldBounds.MinY, WorldBounds.MaxY, WorldBounds.VerticalBits);
            z = ReadQuantized(WorldBounds.MinZ, WorldBounds.MaxZ, WorldBounds.HorizontalBits);
        }

        public void ReadBytes(byte[] destination, int offset, int count)
        {
            if (count <= 0)
            {
                return;
            }
            if (_overflowed || _bitPosition + count * 8 > _endBit)
            {
                _overflowed = true;
                return;
            }
            if ((_bitPosition & 7) == 0)
            {
                Buffer.BlockCopy(_buffer, _bitPosition >> 3, destination, offset, count);
                _bitPosition += count * 8;
                return;
            }
            for (int i = 0; i < count; i++)
            {
                destination[offset + i] = (byte)ReadBits(8);
            }
        }

        /// <summary>Reads a string written by WriteString. Lengths above maxBytes fail the stream.</summary>
        public string ReadString(int maxBytes)
        {
            uint length = ReadVarUInt();
            if (_overflowed)
            {
                return string.Empty;
            }
            if (length == 0)
            {
                return string.Empty;
            }
            if (length > (uint)maxBytes || _bitPosition + (int)length * 8 > _endBit)
            {
                _overflowed = true;
                return string.Empty;
            }
            if ((_bitPosition & 7) == 0)
            {
                string s = Encoding.UTF8.GetString(_buffer, _bitPosition >> 3, (int)length);
                _bitPosition += (int)length * 8;
                return s;
            }
            byte[] tmp = new byte[length];
            ReadBytes(tmp, 0, (int)length);
            return Encoding.UTF8.GetString(tmp, 0, tmp.Length);
        }

        public void AlignToByte()
        {
            int rem = (_bitPosition - _startBit) & 7;
            if (rem != 0)
            {
                ReadBits(8 - rem);
            }
        }
    }
}
