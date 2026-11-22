using System;
using GangsOfBharat.Shared.Serialization;

namespace GangsOfBharat.Shared.Net
{
    public enum ClientState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Rejected = 3,
    }

    /// <summary>
    /// Client side of the protocol, independent of the socket so Unity, bots and tests share it.
    /// The owner feeds received datagrams to Receive and calls Update every client tick with a
    /// sink that sends to the server. Incoming messages are decoded into reused instances and
    /// handed to the callbacks; callbacks must copy anything they keep.
    /// </summary>
    public sealed class ClientConnection : IMessageReceiver
    {
        public const double ConnectRetryMs = 250.0;
        public const double KeepAliveMs = 100.0;

        private readonly ReliableChannel _channel = new ReliableChannel();
        private readonly BitReader _reader = new BitReader();
        private readonly ConnectMessage _connect = new ConnectMessage();
        private readonly ConnectAcceptMessage _accept = new ConnectAcceptMessage();
        private readonly ConnectRejectMessage _reject = new ConnectRejectMessage();
        private readonly WorldSnapshotMessage _snapshot = new WorldSnapshotMessage();
        private readonly ChatMessage _chat = new ChatMessage();
        private readonly VoiceFrameMessage _voice = new VoiceFrameMessage();
        private readonly PongMessage _pong = new PongMessage();
        private readonly DisconnectMessage _disconnect = new DisconnectMessage();
        private double _nextConnectMs;
        private double _lastSendMs;

        public ClientState State;
        public int PlayerId;
        public RejectReason RejectReason;
        public DisconnectReason DisconnectReason;
        public long WalletRupees;
        public long SnapshotsReceived;
        public long SnapshotEntitiesReceived;
        /// <summary>Bytes of snapshot messages (without packet headers).</summary>
        public long SnapshotBytesReceived;
        /// <summary>Distinct server ticks for which at least one snapshot message arrived.</summary>
        public long SnapshotTicksReceived;
        public long ChatReceived;
        public long VoiceReceived;
        public long MalformedMessages;
        public uint LastServerTick;

        public Action<ConnectAcceptMessage> OnConnected;
        public Action<WorldSnapshotMessage> OnSnapshot;
        public Action<ChatMessage> OnChat;
        public Action<VoiceFrameMessage> OnVoice;
        /// <summary>Other messages (vehicle, job, pong) as freshly allocated instances.</summary>
        public Action<NetMessage> OnOther;

        public ReliableChannel Channel
        {
            get { return _channel; }
        }

        public ConnectAcceptMessage Accept
        {
            get { return _accept; }
        }

        public void BeginConnect(string sessionToken, string playerName, string language, double nowMs)
        {
            _connect.ProtocolVersion = ProtocolConstants.ProtocolVersion;
            _connect.SessionToken = sessionToken ?? string.Empty;
            _connect.PlayerName = playerName ?? string.Empty;
            _connect.Language = language ?? "en";
            State = ClientState.Connecting;
            _nextConnectMs = nowMs;
        }

        public void SendUnreliable(NetMessage message)
        {
            _channel.QueueUnreliable(message);
        }

        public bool SendReliable(NetMessage message)
        {
            return _channel.QueueReliable(message);
        }

        /// <summary>Sends queued messages; while connecting, resends the Connect request.</summary>
        public void Update(double nowMs, IPacketSink sink)
        {
            if (State == ClientState.Connecting && nowMs >= _nextConnectMs)
            {
                _channel.QueueUnreliable(_connect);
                _nextConnectMs = nowMs + ConnectRetryMs;
            }
            if (State == ClientState.Connecting || State == ClientState.Connected)
            {
                bool keepAlive = nowMs - _lastSendMs >= KeepAliveMs;
                if (_channel.HasQueuedData || keepAlive)
                {
                    _channel.Flush(nowMs, sink, keepAlive);
                    _lastSendMs = nowMs;
                }
            }
        }

        public void Disconnect(double nowMs, IPacketSink sink)
        {
            if (State == ClientState.Connected || State == ClientState.Connecting)
            {
                _disconnect.Reason = DisconnectReason.ClientQuit;
                _channel.QueueUnreliable(_disconnect);
                _channel.Flush(nowMs, sink, false);
            }
            State = ClientState.Disconnected;
        }

        public PacketReadResult Receive(byte[] data, int offset, int length, double nowMs)
        {
            return _channel.ReadPacket(data, offset, length, nowMs, this);
        }

        public void OnMessage(byte[] buffer, int offset, int length, bool reliable)
        {
            _reader.Reset(buffer, offset, length);
            MessageType type = Protocol.ReadType(_reader);
            switch (type)
            {
                case MessageType.ConnectAccept:
                    _accept.Deserialize(_reader);
                    if (_reader.Overflowed)
                    {
                        break;
                    }
                    PlayerId = _accept.PlayerEntityId;
                    WalletRupees = _accept.WalletRupees;
                    State = ClientState.Connected;
                    if (OnConnected != null)
                    {
                        OnConnected(_accept);
                    }
                    break;
                case MessageType.ConnectReject:
                    _reject.Deserialize(_reader);
                    RejectReason = _reject.Reason;
                    State = ClientState.Rejected;
                    break;
                case MessageType.WorldSnapshot:
                    _snapshot.Deserialize(_reader);
                    if (_reader.Overflowed)
                    {
                        break;
                    }
                    SnapshotsReceived++;
                    SnapshotEntitiesReceived += _snapshot.EntityCount;
                    SnapshotBytesReceived += length;
                    if (_snapshot.ServerTick != LastServerTick)
                    {
                        SnapshotTicksReceived++;
                    }
                    if (_snapshot.ServerTick > LastServerTick)
                    {
                        LastServerTick = _snapshot.ServerTick;
                    }
                    if (OnSnapshot != null)
                    {
                        OnSnapshot(_snapshot);
                    }
                    break;
                case MessageType.ChatMessage:
                    _chat.Deserialize(_reader);
                    if (_reader.Overflowed)
                    {
                        break;
                    }
                    ChatReceived++;
                    if (OnChat != null)
                    {
                        OnChat(_chat);
                    }
                    break;
                case MessageType.VoiceFrame:
                    _voice.Deserialize(_reader);
                    if (_reader.Overflowed)
                    {
                        break;
                    }
                    VoiceReceived++;
                    if (OnVoice != null)
                    {
                        OnVoice(_voice);
                    }
                    break;
                case MessageType.Disconnect:
                    _disconnect.Deserialize(_reader);
                    DisconnectReason = _disconnect.Reason;
                    State = ClientState.Disconnected;
                    break;
                default:
                    NetMessage other = Protocol.Create(type);
                    if (other == null)
                    {
                        MalformedMessages++;
                        break;
                    }
                    other.Deserialize(_reader);
                    if (_reader.Overflowed)
                    {
                        MalformedMessages++;
                        break;
                    }
                    JobCompleteMessage job = other as JobCompleteMessage;
                    if (job != null)
                    {
                        WalletRupees = job.WalletRupees;
                    }
                    if (OnOther != null)
                    {
                        OnOther(other);
                    }
                    break;
            }
            if (_reader.Overflowed)
            {
                MalformedMessages++;
            }
        }
    }
}
