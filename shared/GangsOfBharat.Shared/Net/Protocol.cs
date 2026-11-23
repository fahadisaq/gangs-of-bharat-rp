using System;
using GangsOfBharat.Shared.Serialization;

namespace GangsOfBharat.Shared.Net
{
    public static class ProtocolConstants
    {
        /// <summary>"GOB1" in ASCII. Packets with another id are dropped silently.</summary>
        public const uint ProtocolId = 0x31424F47u;
        public const byte ProtocolVersion = 1;

        /// <summary>Target size for normal packets (safe under common internet MTUs).</summary>
        public const int MaxPacketBytes = 1200;
        /// <summary>Receive buffer size; the Connect packet may carry a longer token.</summary>
        public const int MaxReceiveBytes = 1500;

        public const int MaxNameBytes = 48;
        public const int MaxLanguageBytes = 8;
        /// <summary>
        /// Token cap. A real Firebase ID token is about 900-1200 bytes, so production should
        /// exchange it over HTTPS for a short join ticket (TODO, see server README).
        /// </summary>
        public const int MaxTokenBytes = 1100;
        /// <summary>Chat text cap in UTF-8 bytes (about 200 Latin or 130 Devanagari characters).</summary>
        public const int MaxChatBytes = 400;
        /// <summary>Opus 16 kbps x 20 ms is about 40 bytes; 256 leaves room for higher bitrates.</summary>
        public const int MaxVoiceBytes = 256;
        public const int YawBits = 12;
        public const int AxisBits = 8;
        public const int ChatChannelBits = 4;
    }

    public enum MessageType : byte
    {
        None = 0,
        Connect = 1,
        ConnectAccept = 2,
        ConnectReject = 3,
        Disconnect = 4,
        Ping = 5,
        Pong = 6,
        PlayerInput = 7,
        WorldSnapshot = 8,
        ChatMessage = 9,
        VoiceFrame = 10,
        EnterVehicle = 11,
        ExitVehicle = 12,
        JobStart = 13,
        JobComplete = 14,
        SetMute = 15,
    }

    /// <summary>Text channels from docs/design/10-communication.md.</summary>
    public enum ChatChannel : byte
    {
        /// <summary>Proximity text, players nearby (30 m).</summary>
        Local = 0,
        /// <summary>Whole server with slow mode.</summary>
        Global = 1,
        /// <summary>All members of the sender's faction, map-wide.</summary>
        Faction = 2,
        /// <summary>Player-made group or crew.</summary>
        Crew = 3,
        /// <summary>VIP members only.</summary>
        Vip = 4,
        /// <summary>Whole server, paid in rupees per message.</summary>
        Ad = 5,
        /// <summary>One-to-one phone SMS, needs phone recharge.</summary>
        Sms = 6,
        /// <summary>Whole server, admins only.</summary>
        AdminAnnouncement = 7,
        /// <summary>Server notices (errors, job results). Clients cannot send this.</summary>
        System = 8,
    }

    public enum VoiceChannel : byte
    {
        /// <summary>Players within 40 m hear the speaker.</summary>
        Proximity = 0,
        /// <summary>All online members of the speaker's faction, map-wide.</summary>
        FactionRadio = 1,
    }

    public enum RejectReason : byte
    {
        Unknown = 0,
        VersionMismatch = 1,
        ServerFull = 2,
        InvalidToken = 3,
        Banned = 4,
    }

    public enum DisconnectReason : byte
    {
        ClientQuit = 0,
        Timeout = 1,
        Kicked = 2,
        ServerShutdown = 3,
    }

    public enum EntityKind : byte
    {
        Player = 0,
        Vehicle = 1,
    }

    [Flags]
    public enum InputButtons : byte
    {
        None = 0,
        Sprint = 1,
        Jump = 2,
        Brake = 4,
        Horn = 8,
        Interact = 16,
    }

    public abstract class NetMessage
    {
        public abstract MessageType Type { get; }

        /// <summary>Writes the body (without the type byte).</summary>
        public abstract void Serialize(BitWriter w);

        /// <summary>Reads the body into this instance so hot paths can reuse objects.</summary>
        public abstract void Deserialize(BitReader r);
    }

    public sealed class ConnectMessage : NetMessage
    {
        public byte ProtocolVersion = ProtocolConstants.ProtocolVersion;
        /// <summary>Firebase ID token placeholder (verification is a server TODO).</summary>
        public string SessionToken = string.Empty;
        public string PlayerName = string.Empty;
        /// <summary>"en" or "hi".</summary>
        public string Language = "en";

        public override MessageType Type { get { return MessageType.Connect; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteByte(ProtocolVersion);
            w.WriteString(SessionToken, ProtocolConstants.MaxTokenBytes);
            w.WriteString(PlayerName, ProtocolConstants.MaxNameBytes);
            w.WriteString(Language, ProtocolConstants.MaxLanguageBytes);
        }

        public override void Deserialize(BitReader r)
        {
            ProtocolVersion = r.ReadByte();
            SessionToken = r.ReadString(ProtocolConstants.MaxTokenBytes);
            PlayerName = r.ReadString(ProtocolConstants.MaxNameBytes);
            Language = r.ReadString(ProtocolConstants.MaxLanguageBytes);
        }
    }

    public sealed class ConnectAcceptMessage : NetMessage
    {
        public int PlayerEntityId;
        public uint ServerTick;
        public byte TickRate;
        public float SpawnX;
        public float SpawnY;
        public float SpawnZ;
        public long WalletRupees;
        public long PhoneBalanceRupees;
        public byte FactionId;
        public bool IsVip;

        public override MessageType Type { get { return MessageType.ConnectAccept; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteVarInt(PlayerEntityId);
            w.WriteUInt(ServerTick);
            w.WriteByte(TickRate);
            w.WritePosition(SpawnX, SpawnY, SpawnZ);
            w.WriteVarLong(WalletRupees);
            w.WriteVarLong(PhoneBalanceRupees);
            w.WriteByte(FactionId);
            w.WriteBool(IsVip);
        }

        public override void Deserialize(BitReader r)
        {
            PlayerEntityId = r.ReadVarInt();
            ServerTick = r.ReadUInt();
            TickRate = r.ReadByte();
            r.ReadPosition(out SpawnX, out SpawnY, out SpawnZ);
            WalletRupees = r.ReadVarLong();
            PhoneBalanceRupees = r.ReadVarLong();
            FactionId = r.ReadByte();
            IsVip = r.ReadBool();
        }
    }

    public sealed class ConnectRejectMessage : NetMessage
    {
        public RejectReason Reason;

        public override MessageType Type { get { return MessageType.ConnectReject; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteByte((byte)Reason);
        }

        public override void Deserialize(BitReader r)
        {
            Reason = (RejectReason)r.ReadByte();
        }
    }

    public sealed class DisconnectMessage : NetMessage
    {
        public DisconnectReason Reason;

        public override MessageType Type { get { return MessageType.Disconnect; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteByte((byte)Reason);
        }

        public override void Deserialize(BitReader r)
        {
            Reason = (DisconnectReason)r.ReadByte();
        }
    }

    public sealed class PingMessage : NetMessage
    {
        public uint ClientTimeMs;

        public override MessageType Type { get { return MessageType.Ping; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteUInt(ClientTimeMs);
        }

        public override void Deserialize(BitReader r)
        {
            ClientTimeMs = r.ReadUInt();
        }
    }

    public sealed class PongMessage : NetMessage
    {
        public uint ClientTimeMs;
        public uint ServerTick;

        public override MessageType Type { get { return MessageType.Pong; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteUInt(ClientTimeMs);
            w.WriteUInt(ServerTick);
        }

        public override void Deserialize(BitReader r)
        {
            ClientTimeMs = r.ReadUInt();
            ServerTick = r.ReadUInt();
        }
    }

    /// <summary>
    /// Client input for one client tick. Move is a world-space direction (camera already applied).
    /// ClientX/Y/Z is the client's predicted position, used only for cheat detection; the server
    /// never trusts it for the authoritative state.
    /// </summary>
    public sealed class PlayerInputMessage : NetMessage
    {
        public uint InputTick;
        public float MoveX;
        public float MoveZ;
        public float Yaw;
        public InputButtons Buttons;
        public float Throttle;
        public float Steer;
        public float ClientX;
        public float ClientY;
        public float ClientZ;

        public override MessageType Type { get { return MessageType.PlayerInput; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteUInt(InputTick);
            w.WriteQuantized(MoveX, -1f, 1f, ProtocolConstants.AxisBits);
            w.WriteQuantized(MoveZ, -1f, 1f, ProtocolConstants.AxisBits);
            w.WriteAngle(Yaw, ProtocolConstants.YawBits);
            w.WriteByte((byte)Buttons);
            w.WriteQuantized(Throttle, -1f, 1f, ProtocolConstants.AxisBits);
            w.WriteQuantized(Steer, -1f, 1f, ProtocolConstants.AxisBits);
            w.WritePosition(ClientX, ClientY, ClientZ);
        }

        public override void Deserialize(BitReader r)
        {
            InputTick = r.ReadUInt();
            MoveX = r.ReadQuantized(-1f, 1f, ProtocolConstants.AxisBits);
            MoveZ = r.ReadQuantized(-1f, 1f, ProtocolConstants.AxisBits);
            Yaw = r.ReadAngle(ProtocolConstants.YawBits);
            Buttons = (InputButtons)r.ReadByte();
            Throttle = r.ReadQuantized(-1f, 1f, ProtocolConstants.AxisBits);
            Steer = r.ReadQuantized(-1f, 1f, ProtocolConstants.AxisBits);
            r.ReadPosition(out ClientX, out ClientY, out ClientZ);
        }
    }

    /// <summary>One entity inside a snapshot.</summary>
    public struct EntityState
    {
        public int Id;
        public EntityKind Kind;
        /// <summary>True when the entity just entered this client's relevance set.</summary>
        public bool Spawn;
        /// <summary>Vehicle catalog index or character skin id.</summary>
        public int ModelId;
        public byte FactionId;
        public float X;
        public float Y;
        public float Z;
        public float Yaw;
        /// <summary>Vehicle the player sits in (0 = on foot), or driver id for vehicles.</summary>
        public int LinkedId;
    }

    /// <summary>
    /// Unreliable world state for one client. Spawn entries carry the static fields; despawns list
    /// ids that left the relevance set. Clients also drop entities not updated for 2 s, which
    /// covers lost despawns. A snapshot can be split into several messages with the same tick.
    /// </summary>
    public sealed class WorldSnapshotMessage : NetMessage
    {
        public const int MaxEntitiesPerMessage = 255;

        public uint ServerTick;
        public uint LastProcessedInputTick;
        public float SelfX;
        public float SelfY;
        public float SelfZ;
        public float SelfYaw;
        public int SelfVehicleId;
        public EntityState[] Entities = new EntityState[64];
        public int EntityCount;
        public int[] Despawns = new int[16];
        public int DespawnCount;

        public override MessageType Type { get { return MessageType.WorldSnapshot; } }

        public void Clear()
        {
            EntityCount = 0;
            DespawnCount = 0;
        }

        public void AddEntity(ref EntityState state)
        {
            if (EntityCount == Entities.Length)
            {
                Array.Resize(ref Entities, Entities.Length * 2);
            }
            Entities[EntityCount++] = state;
        }

        public void AddDespawn(int id)
        {
            if (DespawnCount == Despawns.Length)
            {
                Array.Resize(ref Despawns, Despawns.Length * 2);
            }
            Despawns[DespawnCount++] = id;
        }

        public override void Serialize(BitWriter w)
        {
            SerializeRange(w, 0, EntityCount, true);
        }

        /// <summary>
        /// Writes the header plus entities [start, start + count). Used by the server to split a
        /// large snapshot into several messages that each fit in one packet.
        /// </summary>
        public void SerializeRange(BitWriter w, int start, int count, bool includeDespawns)
        {
            w.WriteUInt(ServerTick);
            w.WriteUInt(LastProcessedInputTick);
            w.WritePosition(SelfX, SelfY, SelfZ);
            w.WriteAngle(SelfYaw, ProtocolConstants.YawBits);
            w.WriteVarInt(SelfVehicleId);
            if (start < 0)
            {
                start = 0;
            }
            if (start + count > EntityCount)
            {
                count = EntityCount - start;
            }
            if (count < 0)
            {
                count = 0;
            }
            if (count > MaxEntitiesPerMessage)
            {
                count = MaxEntitiesPerMessage;
            }
            w.WriteByte((byte)count);
            for (int i = start; i < start + count; i++)
            {
                WriteEntity(w, ref Entities[i]);
            }
            int despawns = includeDespawns ? (DespawnCount > 255 ? 255 : DespawnCount) : 0;
            w.WriteByte((byte)despawns);
            for (int i = 0; i < despawns; i++)
            {
                w.WriteVarInt(Despawns[i]);
            }
        }

        public static void WriteEntity(BitWriter w, ref EntityState e)
        {
            w.WriteVarInt(e.Id);
            w.WriteBits((uint)e.Kind, 1);
            w.WriteBool(e.Spawn);
            if (e.Spawn)
            {
                w.WriteVarInt(e.ModelId);
                w.WriteBits(e.FactionId, 4);
            }
            w.WritePosition(e.X, e.Y, e.Z);
            w.WriteAngle(e.Yaw, 10);
            bool linked = e.LinkedId != 0;
            w.WriteBool(linked);
            if (linked)
            {
                w.WriteVarInt(e.LinkedId);
            }
        }

        public static void ReadEntity(BitReader r, ref EntityState e)
        {
            e.Id = r.ReadVarInt();
            e.Kind = (EntityKind)r.ReadBits(1);
            e.Spawn = r.ReadBool();
            if (e.Spawn)
            {
                e.ModelId = r.ReadVarInt();
                e.FactionId = (byte)r.ReadBits(4);
            }
            else
            {
                e.ModelId = 0;
                e.FactionId = 0;
            }
            r.ReadPosition(out e.X, out e.Y, out e.Z);
            e.Yaw = r.ReadAngle(10);
            e.LinkedId = r.ReadBool() ? r.ReadVarInt() : 0;
        }

        public override void Deserialize(BitReader r)
        {
            ServerTick = r.ReadUInt();
            LastProcessedInputTick = r.ReadUInt();
            r.ReadPosition(out SelfX, out SelfY, out SelfZ);
            SelfYaw = r.ReadAngle(ProtocolConstants.YawBits);
            SelfVehicleId = r.ReadVarInt();
            int count = r.ReadByte();
            if (Entities.Length < count)
            {
                Entities = new EntityState[count];
            }
            for (int i = 0; i < count && !r.Overflowed; i++)
            {
                ReadEntity(r, ref Entities[i]);
            }
            EntityCount = r.Overflowed ? 0 : count;
            int despawns = r.ReadByte();
            if (Despawns.Length < despawns)
            {
                Despawns = new int[despawns];
            }
            for (int i = 0; i < despawns && !r.Overflowed; i++)
            {
                Despawns[i] = r.ReadVarInt();
            }
            DespawnCount = r.Overflowed ? 0 : despawns;
        }
    }

    /// <summary>
    /// Text chat. Client to server: Channel, TargetId (SMS only), Text. Server to client adds
    /// SenderId and SenderName. Sent on the reliable channel.
    /// </summary>
    public sealed class ChatMessage : NetMessage
    {
        public ChatChannel Channel;
        public int SenderId;
        /// <summary>Recipient player id for SMS; 0 otherwise.</summary>
        public int TargetId;
        public string SenderName = string.Empty;
        public string Text = string.Empty;

        public override MessageType Type { get { return MessageType.ChatMessage; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteBits((uint)Channel, ProtocolConstants.ChatChannelBits);
            w.WriteVarInt(SenderId);
            w.WriteVarInt(TargetId);
            w.WriteString(SenderName, ProtocolConstants.MaxNameBytes);
            w.WriteString(Text, ProtocolConstants.MaxChatBytes);
        }

        public override void Deserialize(BitReader r)
        {
            uint channel = r.ReadBits(ProtocolConstants.ChatChannelBits);
            if (channel > (uint)ChatChannel.System)
            {
                r.Fail();
            }
            Channel = (ChatChannel)channel;
            SenderId = r.ReadVarInt();
            TargetId = r.ReadVarInt();
            SenderName = r.ReadString(ProtocolConstants.MaxNameBytes);
            Text = r.ReadString(ProtocolConstants.MaxChatBytes);
        }
    }

    /// <summary>One opaque Opus frame. Sent unreliably; the server relays it, never decodes or logs it.</summary>
    public sealed class VoiceFrameMessage : NetMessage
    {
        public VoiceChannel Channel;
        public int SenderId;
        public ushort Sequence;
        public byte[] Data = new byte[ProtocolConstants.MaxVoiceBytes];
        public int Length;

        public override MessageType Type { get { return MessageType.VoiceFrame; } }

        public void SetData(byte[] source, int offset, int count)
        {
            if (count > ProtocolConstants.MaxVoiceBytes)
            {
                throw new ArgumentOutOfRangeException("count");
            }
            Buffer.BlockCopy(source, offset, Data, 0, count);
            Length = count;
        }

        public override void Serialize(BitWriter w)
        {
            w.WriteBits((uint)Channel, 1);
            w.WriteVarInt(SenderId);
            w.WriteUShort(Sequence);
            int length = Length > ProtocolConstants.MaxVoiceBytes ? ProtocolConstants.MaxVoiceBytes : Length;
            w.WriteVarUInt((uint)length);
            w.WriteBytes(Data, 0, length);
        }

        public override void Deserialize(BitReader r)
        {
            Channel = (VoiceChannel)r.ReadBits(1);
            SenderId = r.ReadVarInt();
            Sequence = r.ReadUShort();
            uint length = r.ReadVarUInt();
            if (length > ProtocolConstants.MaxVoiceBytes)
            {
                r.Fail();
                Length = 0;
                return;
            }
            r.ReadBytes(Data, 0, (int)length);
            Length = r.Overflowed ? 0 : (int)length;
        }
    }

    /// <summary>Client requests to enter a vehicle; the server echoes it back with Success set.</summary>
    public sealed class EnterVehicleMessage : NetMessage
    {
        public int VehicleId;
        public int PlayerId;
        public bool Success;

        public override MessageType Type { get { return MessageType.EnterVehicle; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteVarInt(VehicleId);
            w.WriteVarInt(PlayerId);
            w.WriteBool(Success);
        }

        public override void Deserialize(BitReader r)
        {
            VehicleId = r.ReadVarInt();
            PlayerId = r.ReadVarInt();
            Success = r.ReadBool();
        }
    }

    public sealed class ExitVehicleMessage : NetMessage
    {
        public int VehicleId;
        public int PlayerId;
        public bool Success;

        public override MessageType Type { get { return MessageType.ExitVehicle; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteVarInt(VehicleId);
            w.WriteVarInt(PlayerId);
            w.WriteBool(Success);
        }

        public override void Deserialize(BitReader r)
        {
            VehicleId = r.ReadVarInt();
            PlayerId = r.ReadVarInt();
            Success = r.ReadBool();
        }
    }

    /// <summary>Client asks to start a job; the server answers with Accepted and the waypoint.</summary>
    public sealed class JobStartMessage : NetMessage
    {
        public int JobId;
        public bool Accepted;
        public float WaypointX;
        public float WaypointZ;

        public override MessageType Type { get { return MessageType.JobStart; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteVarInt(JobId);
            w.WriteBool(Accepted);
            w.WriteQuantized(WaypointX, WorldBounds.MinX, WorldBounds.MaxX, WorldBounds.HorizontalBits);
            w.WriteQuantized(WaypointZ, WorldBounds.MinZ, WorldBounds.MaxZ, WorldBounds.HorizontalBits);
        }

        public override void Deserialize(BitReader r)
        {
            JobId = r.ReadVarInt();
            Accepted = r.ReadBool();
            WaypointX = r.ReadQuantized(WorldBounds.MinX, WorldBounds.MaxX, WorldBounds.HorizontalBits);
            WaypointZ = r.ReadQuantized(WorldBounds.MinZ, WorldBounds.MaxZ, WorldBounds.HorizontalBits);
        }
    }

    /// <summary>Server tells the client a job finished and what it paid (rupees).</summary>
    public sealed class JobCompleteMessage : NetMessage
    {
        public int JobId;
        public long PaymentRupees;
        public long WalletRupees;

        public override MessageType Type { get { return MessageType.JobComplete; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteVarInt(JobId);
            w.WriteVarLong(PaymentRupees);
            w.WriteVarLong(WalletRupees);
        }

        public override void Deserialize(BitReader r)
        {
            JobId = r.ReadVarInt();
            PaymentRupees = r.ReadVarLong();
            WalletRupees = r.ReadVarLong();
        }
    }

    /// <summary>Mute (hide chat and voice) or block (also refuse SMS) another player.</summary>
    public sealed class SetMuteMessage : NetMessage
    {
        public int TargetPlayerId;
        public bool Block;
        public bool Enabled;

        public override MessageType Type { get { return MessageType.SetMute; } }

        public override void Serialize(BitWriter w)
        {
            w.WriteVarInt(TargetPlayerId);
            w.WriteBool(Block);
            w.WriteBool(Enabled);
        }

        public override void Deserialize(BitReader r)
        {
            TargetPlayerId = r.ReadVarInt();
            Block = r.ReadBool();
            Enabled = r.ReadBool();
        }
    }

    /// <summary>Encoding helpers: a message on the wire is a type byte followed by its body.</summary>
    public static class Protocol
    {
        public static void Write(BitWriter w, NetMessage message)
        {
            w.WriteByte((byte)message.Type);
            message.Serialize(w);
            w.AlignToByte();
        }

        public static MessageType ReadType(BitReader r)
        {
            return (MessageType)r.ReadByte();
        }

        public static NetMessage Create(MessageType type)
        {
            switch (type)
            {
                case MessageType.Connect: return new ConnectMessage();
                case MessageType.ConnectAccept: return new ConnectAcceptMessage();
                case MessageType.ConnectReject: return new ConnectRejectMessage();
                case MessageType.Disconnect: return new DisconnectMessage();
                case MessageType.Ping: return new PingMessage();
                case MessageType.Pong: return new PongMessage();
                case MessageType.PlayerInput: return new PlayerInputMessage();
                case MessageType.WorldSnapshot: return new WorldSnapshotMessage();
                case MessageType.ChatMessage: return new ChatMessage();
                case MessageType.VoiceFrame: return new VoiceFrameMessage();
                case MessageType.EnterVehicle: return new EnterVehicleMessage();
                case MessageType.ExitVehicle: return new ExitVehicleMessage();
                case MessageType.JobStart: return new JobStartMessage();
                case MessageType.JobComplete: return new JobCompleteMessage();
                case MessageType.SetMute: return new SetMuteMessage();
                default: return null;
            }
        }

        /// <summary>Reads one message into a new instance. Returns null for unknown or malformed data.</summary>
        public static NetMessage ReadNew(BitReader r)
        {
            NetMessage message = Create(ReadType(r));
            if (message == null)
            {
                return null;
            }
            message.Deserialize(r);
            r.AlignToByte();
            return r.Overflowed ? null : message;
        }

        /// <summary>Encodes one message into a standalone byte array (tests and rare paths).</summary>
        public static byte[] Encode(NetMessage message)
        {
            BitWriter w = new BitWriter(256);
            Write(w, message);
            return w.ToArray();
        }

        public static NetMessage Decode(byte[] data, int offset, int count)
        {
            BitReader r = new BitReader(data, offset, count);
            return ReadNew(r);
        }
    }
}
