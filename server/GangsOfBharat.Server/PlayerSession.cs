using System.Collections.Generic;
using System.Net;
using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Net;
using GangsOfBharat.Shared.World;

namespace GangsOfBharat.Server
{
    /// <summary>Sends raw datagrams. Must be thread-safe (snapshot workers send in parallel).</summary>
    public interface ITransport
    {
        void Send(EndPoint to, byte[] data, int length);
    }

    /// <summary>A parked or driven vehicle in the world. Ids share the entity id space with players.</summary>
    public sealed class Vehicle
    {
        public int Id;
        public VehicleDef Def;
        public VehicleState State;
        /// <summary>Entity id of the driver, 0 when empty.</summary>
        public int DriverId;
    }

    /// <summary>One connected player. Owned by the simulation thread.</summary>
    public sealed class PlayerSession : IPacketSink
    {
        private readonly ITransport _transport;

        public readonly int Id;
        public readonly EndPoint EndPoint;
        public readonly ReliableChannel Channel;
        public readonly ClientInterest Interest = new ClientInterest();

        public string Name = string.Empty;
        public string Language = "en";
        /// <summary>Firebase UID once token verification exists; "dev:..." or "anon:..." until then.</summary>
        public string AccountId = string.Empty;

        public CharacterState Character;
        public int VehicleId;

        public FactionId Faction;
        public int CrewId;
        public bool IsVip;
        public bool IsAdmin;

        /// <summary>In-memory wallet in rupees (persistence to Firestore is a later feature).</summary>
        public long WalletRupees;
        public long PhoneBalanceRupees;

        /// <summary>Players whose chat and voice this player does not receive.</summary>
        public readonly HashSet<int> Muted = new HashSet<int>();
        /// <summary>Like Muted, and SMS from them is refused.</summary>
        public readonly HashSet<int> Blocked = new HashSet<int>();

        public double ConnectedAtMs;
        public double LastReceiveMs;
        public double NextGlobalChatMs;
        public double ChatWindowStartMs;
        public int ChatInWindow;
        public double VoiceWindowStartMs;
        public int VoiceInWindow;

        // Latest input
        public uint LastInputTick;
        public double LastInputMs;
        public float MoveX;
        public float MoveZ;
        public float InputYaw;
        public InputButtons Buttons;
        public float Throttle;
        public float Steer;
        public bool HasClientPosition;
        public float LastClientX;
        public float LastClientZ;

        // Anti-cheat
        public int MovementViolations;
        public double ViolationWindowStartMs;
        public long TotalViolations;

        // Job flow
        public int ActiveJobId;
        public float JobWaypointX;
        public float JobWaypointZ;
        public float JobDistance;
        public double JobStartedMs;

        public bool PendingKick;
        public string KickReason;

        // Stats
        public long SnapshotsSent;
        public long SnapshotEntitiesSent;

        public PlayerSession(int id, EndPoint endPoint, ReliableChannel channel, ITransport transport)
        {
            Id = id;
            EndPoint = endPoint;
            Channel = channel;
            _transport = transport;
        }

        public bool InVehicle
        {
            get { return VehicleId != 0; }
        }

        public void SendPacket(byte[] buffer, int length)
        {
            _transport.Send(EndPoint, buffer, length);
        }

        /// <summary>True if this player should not receive chat or voice from sender.</summary>
        public bool IsIgnoring(int senderId)
        {
            return Muted.Contains(senderId) || Blocked.Contains(senderId);
        }
    }

    /// <summary>Recent chat kept in memory for reports (evidence window).</summary>
    public struct ChatLogEntry
    {
        public double TimeMs;
        public int SenderId;
        public ChatChannel Channel;
        public int TargetId;
        public string Text;
    }
}
