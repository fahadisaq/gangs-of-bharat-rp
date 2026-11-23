using System;
using System.Collections.Generic;
using GangsOfBharat.Shared.Serialization;

namespace GangsOfBharat.Shared.Net
{
    /// <summary>Destination for finished packets (a UDP socket in production, a list in tests).</summary>
    public interface IPacketSink
    {
        void SendPacket(byte[] buffer, int length);
    }

    /// <summary>Receives message bytes extracted from packets. Each slice starts with the type byte.</summary>
    public interface IMessageReceiver
    {
        void OnMessage(byte[] buffer, int offset, int length, bool reliable);
    }

    public enum PacketReadResult
    {
        Ok = 0,
        WrongProtocol = 1,
        WrongVersion = 2,
        Malformed = 3,
        Duplicate = 4,
    }

    /// <summary>
    /// Packet header: protocol id (32), version (8), sequence (16), ack (16), ack bitfield (32).
    /// </summary>
    public struct PacketHeader
    {
        public const int SizeBytes = 13;

        public uint ProtocolId;
        public byte Version;
        public ushort Sequence;
        public ushort Ack;
        public uint AckBits;

        public void Write(BitWriter w)
        {
            w.WriteUInt(ProtocolId);
            w.WriteByte(Version);
            w.WriteUShort(Sequence);
            w.WriteUShort(Ack);
            w.WriteUInt(AckBits);
        }

        public static bool TryRead(BitReader r, out PacketHeader header)
        {
            header = new PacketHeader();
            header.ProtocolId = r.ReadUInt();
            header.Version = r.ReadByte();
            header.Sequence = r.ReadUShort();
            header.Ack = r.ReadUShort();
            header.AckBits = r.ReadUInt();
            return !r.Overflowed;
        }

        /// <summary>Peeks at the version byte (used to reject old clients before creating a session).</summary>
        public static PacketReadResult Validate(byte[] data, int offset, int length)
        {
            if (length < SizeBytes + 2)
            {
                return PacketReadResult.Malformed;
            }
            uint id = (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
            if (id != ProtocolConstants.ProtocolId)
            {
                return PacketReadResult.WrongProtocol;
            }
            if (data[offset + 4] != ProtocolConstants.ProtocolVersion)
            {
                return PacketReadResult.WrongVersion;
            }
            return PacketReadResult.Ok;
        }
    }

    /// <summary>
    /// Per-connection reliability layer over UDP.
    /// - Every packet carries a sequence number, the latest received remote sequence and a
    ///   32-bit bitfield acking the 32 packets before it.
    /// - Reliable messages get a 16-bit message id, are resent until a packet carrying them is
    ///   acked, and are delivered in order without duplicates.
    /// - Unreliable messages are sent once (movement snapshots, voice).
    /// Not thread-safe: one owner thread per channel.
    /// </summary>
    public sealed class ReliableChannel
    {
        private const int SentBufferSize = 1024;
        private const int ReliableBufferSize = 1024;
        /// <summary>Max distance between the oldest unacked reliable id and the newest one sent.</summary>
        public const int ReliableWindow = 512;
        private const int MaxMessagesPerSection = 255;
        private const int MaxUnreliableStagingBytes = 64 * 1024;

        private sealed class SentPacket
        {
            public bool Valid;
            public bool Acked;
            public ushort Sequence;
            public double SentMs;
            public readonly List<ushort> ReliableIds = new List<ushort>(8);
        }

        private sealed class PendingReliable
        {
            public bool Used;
            public ushort Id;
            public byte[] Data;
            public double LastSentMs;
        }

        private readonly SentPacket[] _sent = new SentPacket[SentBufferSize];
        private readonly PendingReliable[] _sendBuffer = new PendingReliable[ReliableBufferSize];
        private readonly byte[][] _receiveBuffer = new byte[ReliableBufferSize][];
        private readonly BitWriter _packet = new BitWriter(ProtocolConstants.MaxReceiveBytes);
        private readonly BitWriter _scratch = new BitWriter(ProtocolConstants.MaxReceiveBytes);
        private readonly BitReader _reader = new BitReader();
        private readonly List<ushort> _selectedReliable = new List<ushort>(32);

        private ushort _localSequence;
        private ushort _remoteSequence;
        private uint _remoteAckBits;
        private bool _hasRemote;

        private ushort _nextReliableId;
        private ushort _oldestUnackedId;
        private ushort _nextExpectedReceiveId;

        private byte[] _unreliableData = new byte[4096];
        private int _unreliableUsed;
        private int[] _unreliableOffsets = new int[32];
        private int[] _unreliableLengths = new int[32];
        private int _unreliableCount;

        /// <summary>Packets larger than this are split into several packets.</summary>
        public int MaxPacketBytes = ProtocolConstants.MaxPacketBytes;
        /// <summary>Minimum time before a reliable message is resent.</summary>
        public double MinResendDelayMs = 100.0;

        public float SmoothedRttMs = 100f;
        public long PacketsSent;
        public long PacketsReceived;
        public long PacketsAcked;
        public long BytesSent;
        public long BytesReceived;
        public long ReliableResends;
        public long DuplicatePacketsDropped;
        public long UnreliableDropped;
        /// <summary>Remote packets never seen: sequence gaps minus late (reordered) arrivals.</summary>
        public long PacketsLostEstimate;

        public ReliableChannel()
        {
            for (int i = 0; i < SentBufferSize; i++)
            {
                _sent[i] = new SentPacket();
            }
            for (int i = 0; i < ReliableBufferSize; i++)
            {
                _sendBuffer[i] = new PendingReliable();
            }
        }

        public ushort LocalSequence
        {
            get { return _localSequence; }
        }

        public ushort RemoteSequence
        {
            get { return _remoteSequence; }
        }

        /// <summary>Reliable messages queued and not yet acked.</summary>
        public int PendingReliableCount
        {
            get { return (ushort)(_nextReliableId - _oldestUnackedId); }
        }

        public bool HasQueuedData
        {
            get { return _unreliableCount > 0 || PendingReliableCount > 0; }
        }

        /// <summary>True if a is newer than b with 16-bit wraparound.</summary>
        public static bool SequenceGreaterThan(ushort a, ushort b)
        {
            return ((a > b) && (a - b <= 32768)) || ((a < b) && (b - a > 32768));
        }

        private static int VarUIntSize(int value)
        {
            int size = 1;
            uint v = (uint)value;
            while (v >= 0x80)
            {
                v >>= 7;
                size++;
            }
            return size;
        }

        private int MaxPayloadBytes
        {
            get { return MaxPacketBytes - PacketHeader.SizeBytes - 2; }
        }

        /// <summary>Queues a reliable message. Returns false if the window is full or the message is too big.</summary>
        public bool QueueReliable(byte[] data, int offset, int length)
        {
            if (length <= 0 || 2 + VarUIntSize(length) + length > MaxPayloadBytes)
            {
                return false;
            }
            if (PendingReliableCount >= ReliableBufferSize - 1)
            {
                return false;
            }
            PendingReliable p = _sendBuffer[_nextReliableId % ReliableBufferSize];
            p.Used = true;
            p.Id = _nextReliableId;
            p.Data = new byte[length];
            Buffer.BlockCopy(data, offset, p.Data, 0, length);
            p.LastSentMs = -1.0;
            _nextReliableId++;
            return true;
        }

        public bool QueueReliable(NetMessage message)
        {
            _scratch.Reset();
            Protocol.Write(_scratch, message);
            return QueueReliable(_scratch.Buffer, 0, _scratch.LengthBytes);
        }

        /// <summary>Queues an unreliable message (copied into an internal staging buffer, no allocation).</summary>
        public bool QueueUnreliable(byte[] data, int offset, int length)
        {
            if (length <= 0 || VarUIntSize(length) + length > MaxPayloadBytes)
            {
                UnreliableDropped++;
                return false;
            }
            if (_unreliableUsed + length > _unreliableData.Length)
            {
                if (_unreliableUsed + length > MaxUnreliableStagingBytes)
                {
                    UnreliableDropped++;
                    return false;
                }
                int newSize = _unreliableData.Length * 2;
                while (newSize < _unreliableUsed + length)
                {
                    newSize *= 2;
                }
                Array.Resize(ref _unreliableData, newSize);
            }
            if (_unreliableCount == _unreliableOffsets.Length)
            {
                Array.Resize(ref _unreliableOffsets, _unreliableCount * 2);
                Array.Resize(ref _unreliableLengths, _unreliableCount * 2);
            }
            Buffer.BlockCopy(data, offset, _unreliableData, _unreliableUsed, length);
            _unreliableOffsets[_unreliableCount] = _unreliableUsed;
            _unreliableLengths[_unreliableCount] = length;
            _unreliableCount++;
            _unreliableUsed += length;
            return true;
        }

        public bool QueueUnreliable(NetMessage message)
        {
            _scratch.Reset();
            Protocol.Write(_scratch, message);
            return QueueUnreliable(_scratch.Buffer, 0, _scratch.LengthBytes);
        }

        private double ResendDelayMs
        {
            get
            {
                double d = SmoothedRttMs * 1.5;
                return d < MinResendDelayMs ? MinResendDelayMs : d;
            }
        }

        /// <summary>
        /// Writes all due reliable messages and all staged unreliable messages into as many packets
        /// as needed. When nothing is queued and sendEmpty is true, an empty packet still goes out
        /// so the remote side receives acks (keep-alive). Returns the number of packets sent.
        /// </summary>
        public int Flush(double nowMs, IPacketSink sink, bool sendEmpty)
        {
            int packets = 0;
            int unreliableIndex = 0;
            ushort cursor = _oldestUnackedId;
            double resendDelay = ResendDelayMs;
            while (true)
            {
                int budget = MaxPayloadBytes;
                _selectedReliable.Clear();
                while (cursor != _nextReliableId && _selectedReliable.Count < MaxMessagesPerSection)
                {
                    if ((ushort)(cursor - _oldestUnackedId) >= ReliableWindow)
                    {
                        break;
                    }
                    PendingReliable p = _sendBuffer[cursor % ReliableBufferSize];
                    if (!p.Used || p.Id != cursor || (p.LastSentMs >= 0 && nowMs - p.LastSentMs < resendDelay))
                    {
                        cursor++;
                        continue;
                    }
                    int size = 2 + VarUIntSize(p.Data.Length) + p.Data.Length;
                    if (size > budget)
                    {
                        break;
                    }
                    budget -= size;
                    _selectedReliable.Add(cursor);
                    cursor++;
                }

                int unreliableStart = unreliableIndex;
                while (unreliableIndex < _unreliableCount && unreliableIndex - unreliableStart < MaxMessagesPerSection)
                {
                    int len = _unreliableLengths[unreliableIndex];
                    int size = VarUIntSize(len) + len;
                    if (size > budget)
                    {
                        break;
                    }
                    budget -= size;
                    unreliableIndex++;
                }
                int unreliableInPacket = unreliableIndex - unreliableStart;

                if (_selectedReliable.Count == 0 && unreliableInPacket == 0)
                {
                    if (packets > 0 || !sendEmpty)
                    {
                        break;
                    }
                }

                WritePacket(nowMs, sink, unreliableStart, unreliableInPacket);
                packets++;

                bool reliableLeft = cursor != _nextReliableId && (ushort)(cursor - _oldestUnackedId) < ReliableWindow;
                if (!reliableLeft && unreliableIndex >= _unreliableCount)
                {
                    break;
                }
                if (_selectedReliable.Count == 0 && unreliableInPacket == 0)
                {
                    break;
                }
            }
            _unreliableCount = 0;
            _unreliableUsed = 0;
            return packets;
        }

        private void WritePacket(double nowMs, IPacketSink sink, int unreliableStart, int unreliableCount)
        {
            ushort sequence = _localSequence;
            _localSequence++;
            SentPacket entry = _sent[sequence % SentBufferSize];
            entry.Valid = true;
            entry.Acked = false;
            entry.Sequence = sequence;
            entry.SentMs = nowMs;
            entry.ReliableIds.Clear();

            _packet.Reset();
            PacketHeader header = new PacketHeader();
            header.ProtocolId = ProtocolConstants.ProtocolId;
            header.Version = ProtocolConstants.ProtocolVersion;
            header.Sequence = sequence;
            header.Ack = _remoteSequence;
            header.AckBits = _hasRemote ? _remoteAckBits : 0u;
            header.Write(_packet);

            _packet.WriteByte((byte)_selectedReliable.Count);
            for (int i = 0; i < _selectedReliable.Count; i++)
            {
                ushort id = _selectedReliable[i];
                PendingReliable p = _sendBuffer[id % ReliableBufferSize];
                if (p.LastSentMs >= 0)
                {
                    ReliableResends++;
                }
                p.LastSentMs = nowMs;
                entry.ReliableIds.Add(id);
                _packet.WriteUShort(id);
                _packet.WriteVarUInt((uint)p.Data.Length);
                _packet.WriteBytes(p.Data, 0, p.Data.Length);
            }

            _packet.WriteByte((byte)unreliableCount);
            for (int i = unreliableStart; i < unreliableStart + unreliableCount; i++)
            {
                int len = _unreliableLengths[i];
                _packet.WriteVarUInt((uint)len);
                _packet.WriteBytes(_unreliableData, _unreliableOffsets[i], len);
            }

            PacketsSent++;
            BytesSent += _packet.LengthBytes;
            sink.SendPacket(_packet.Buffer, _packet.LengthBytes);
        }

        /// <summary>Parses one packet, processes acks and hands messages to the receiver.</summary>
        public PacketReadResult ReadPacket(byte[] data, int offset, int length, double nowMs, IMessageReceiver receiver)
        {
            PacketReadResult valid = PacketHeader.Validate(data, offset, length);
            if (valid != PacketReadResult.Ok)
            {
                return valid;
            }
            _reader.Reset(data, offset, length);
            PacketHeader header;
            if (!PacketHeader.TryRead(_reader, out header))
            {
                return PacketReadResult.Malformed;
            }
            if (!RecordReceived(header.Sequence))
            {
                DuplicatePacketsDropped++;
                return PacketReadResult.Duplicate;
            }
            PacketsReceived++;
            BytesReceived += length;
            ProcessAcks(header.Ack, header.AckBits, nowMs);

            int reliableCount = _reader.ReadByte();
            for (int i = 0; i < reliableCount; i++)
            {
                ushort id = _reader.ReadUShort();
                uint len = _reader.ReadVarUInt();
                int start = _reader.BytePosition;
                if (_reader.Overflowed || len == 0 || start + (int)len > offset + length)
                {
                    return PacketReadResult.Malformed;
                }
                SkipBytes((int)len);
                ReceiveReliable(id, data, start, (int)len, receiver);
            }

            int unreliableCount = _reader.ReadByte();
            if (_reader.Overflowed)
            {
                return PacketReadResult.Malformed;
            }
            for (int i = 0; i < unreliableCount; i++)
            {
                uint len = _reader.ReadVarUInt();
                int start = _reader.BytePosition;
                if (_reader.Overflowed || len == 0 || start + (int)len > offset + length)
                {
                    return PacketReadResult.Malformed;
                }
                SkipBytes((int)len);
                receiver.OnMessage(data, start, (int)len, false);
            }
            return PacketReadResult.Ok;
        }

        private void SkipBytes(int count)
        {
            // All sections are byte aligned, so skipping is a plain bit advance.
            while (count >= 4)
            {
                _reader.ReadBits(32);
                count -= 4;
            }
            while (count > 0)
            {
                _reader.ReadBits(8);
                count--;
            }
        }

        /// <summary>Updates the remote sequence and ack bitfield. Returns false for duplicates.</summary>
        private bool RecordReceived(ushort sequence)
        {
            if (!_hasRemote)
            {
                _hasRemote = true;
                _remoteSequence = sequence;
                _remoteAckBits = 0;
                return true;
            }
            if (SequenceGreaterThan(sequence, _remoteSequence))
            {
                int shift = (ushort)(sequence - _remoteSequence);
                PacketsLostEstimate += shift - 1;
                if (shift > 32)
                {
                    _remoteAckBits = 0;
                }
                else if (shift == 32)
                {
                    _remoteAckBits = 1u << 31;
                }
                else
                {
                    _remoteAckBits = (_remoteAckBits << shift) | (1u << (shift - 1));
                }
                _remoteSequence = sequence;
                return true;
            }
            int diff = (ushort)(_remoteSequence - sequence);
            if (diff == 0)
            {
                return false;
            }
            if (diff <= 32)
            {
                uint bit = 1u << (diff - 1);
                if ((_remoteAckBits & bit) != 0)
                {
                    return false;
                }
                _remoteAckBits |= bit;
            }
            if (PacketsLostEstimate > 0)
            {
                PacketsLostEstimate--; // it arrived late, so it was reordered, not lost
            }
            return true;
        }

        private void ProcessAcks(ushort ack, uint ackBits, double nowMs)
        {
            AckPacket(ack, nowMs);
            for (int i = 0; i < 32; i++)
            {
                if ((ackBits & (1u << i)) != 0)
                {
                    AckPacket((ushort)(ack - 1 - i), nowMs);
                }
            }
            while (_oldestUnackedId != _nextReliableId && !_sendBuffer[_oldestUnackedId % ReliableBufferSize].Used)
            {
                _oldestUnackedId++;
            }
        }

        private void AckPacket(ushort sequence, double nowMs)
        {
            SentPacket entry = _sent[sequence % SentBufferSize];
            if (!entry.Valid || entry.Acked || entry.Sequence != sequence)
            {
                return;
            }
            entry.Acked = true;
            PacketsAcked++;
            float sample = (float)(nowMs - entry.SentMs);
            if (sample >= 0)
            {
                SmoothedRttMs += (sample - SmoothedRttMs) * 0.1f;
            }
            for (int i = 0; i < entry.ReliableIds.Count; i++)
            {
                ushort id = entry.ReliableIds[i];
                PendingReliable p = _sendBuffer[id % ReliableBufferSize];
                if (p.Used && p.Id == id)
                {
                    p.Used = false;
                    p.Data = null;
                }
            }
        }

        private void ReceiveReliable(ushort id, byte[] data, int start, int length, IMessageReceiver receiver)
        {
            if (id != _nextExpectedReceiveId && !SequenceGreaterThan(id, _nextExpectedReceiveId))
            {
                return; // already delivered
            }
            int ahead = (ushort)(id - _nextExpectedReceiveId);
            if (ahead >= ReliableBufferSize)
            {
                return;
            }
            if (ahead > 0)
            {
                int slot = id % ReliableBufferSize;
                if (_receiveBuffer[slot] == null)
                {
                    byte[] copy = new byte[length];
                    Buffer.BlockCopy(data, start, copy, 0, length);
                    _receiveBuffer[slot] = copy;
                }
                return;
            }
            receiver.OnMessage(data, start, length, true);
            _nextExpectedReceiveId++;
            while (true)
            {
                int slot = _nextExpectedReceiveId % ReliableBufferSize;
                byte[] buffered = _receiveBuffer[slot];
                if (buffered == null)
                {
                    break;
                }
                _receiveBuffer[slot] = null;
                receiver.OnMessage(buffered, 0, buffered.Length, true);
                _nextExpectedReceiveId++;
            }
        }
    }
}
