using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Localization;
using GangsOfBharat.Shared.Net;
using GangsOfBharat.Shared.Serialization;
using GangsOfBharat.Shared.World;

namespace GangsOfBharat.Server
{
    /// <summary>
    /// Authoritative game state and rules, independent of sockets and threads so tests can drive
    /// it directly. Call HandlePacket for each datagram, then Tick once per simulation step, all
    /// from one thread. Tick fans snapshot building out to worker threads.
    /// </summary>
    public sealed class ServerCore : IMessageReceiver
    {
        public const int SnapshotChunkEntities = 64;
        public const int SpawnRefreshTicks = 40;
        public const float JobWaypointRadius = 8f;
        public const double InputStaleMs = 500.0;
        public const int ViolationsBeforeKick = 10;
        public const double ViolationWindowMs = 10000.0;
        public const int MaxHandshakesPerTick = 64;
        public const int MaxMuteEntries = 500;
        public const int ChatLogCapacity = 4096;

        private readonly ServerConfig _cfg;
        private readonly ITransport _transport;
        private readonly ServerLog _log;
        private readonly ServerMetrics _metrics = new ServerMetrics();
        private readonly SpatialHashGrid _grid;
        private readonly InterestSettings _interestSettings;
        private readonly AbuseFilter _filter = new AbuseFilter();
        private readonly Random _rng;

        private readonly List<PlayerSession> _sessions = new List<PlayerSession>();
        private readonly Dictionary<EndPoint, PlayerSession> _byEndpoint = new Dictionary<EndPoint, PlayerSession>();
        private readonly PlayerSession[] _playerByEntity;
        private readonly Vehicle[] _vehicleByEntity;
        private readonly List<Vehicle> _vehicles = new List<Vehicle>();
        private readonly Queue<int> _freePlayerIds = new Queue<int>();
        private readonly SnapshotWorker[] _workers;

        private readonly BitReader _msgReader = new BitReader();
        private readonly BitWriter _encode = new BitWriter(1500);
        private readonly List<int> _query = new List<int>(256);
        private readonly HandshakeReceiver _handshake = new HandshakeReceiver();

        private readonly PlayerInputMessage _inInput = new PlayerInputMessage();
        private readonly ChatMessage _inChat = new ChatMessage();
        private readonly VoiceFrameMessage _inVoice = new VoiceFrameMessage();
        private readonly EnterVehicleMessage _inEnter = new EnterVehicleMessage();
        private readonly ExitVehicleMessage _inExit = new ExitVehicleMessage();
        private readonly JobStartMessage _inJob = new JobStartMessage();
        private readonly SetMuteMessage _inMute = new SetMuteMessage();
        private readonly PingMessage _inPing = new PingMessage();
        private readonly PongMessage _outPong = new PongMessage();
        private readonly DisconnectMessage _inDisconnect = new DisconnectMessage();

        private readonly ChatLogEntry[] _chatLog = new ChatLogEntry[ChatLogCapacity];
        private int _chatLogNext;
        private int _chatLogCount;

        private PlayerSession _current;
        private double _now;
        private uint _tick;
        private int _handshakesThisTick;

        /// <summary>City treasury in rupees (receives the non-News share of ad fees).</summary>
        public long TreasuryRupees;
        /// <summary>News faction fund: 50% of ad chat revenue.</summary>
        public long NewsFundRupees;

        public ServerCore(ServerConfig config, ITransport transport, ServerLog log)
        {
            _cfg = config;
            _transport = transport;
            _log = log ?? ServerLog.Silent();
            _rng = new Random(config.WorldSeed);
            _grid = new SpatialHashGrid(SpatialHashGrid.DefaultCellSize);
            _interestSettings = new InterestSettings();
            _interestSettings.MaxEntitiesPerSnapshot = config.MaxEntitiesPerSnapshot;
            _interestSettings.FarRadius = config.InterestRadius;
            if (_interestSettings.MidRadius > config.InterestRadius)
            {
                _interestSettings.MidRadius = config.InterestRadius;
            }
            if (_interestSettings.NearRadius > _interestSettings.MidRadius)
            {
                _interestSettings.NearRadius = _interestSettings.MidRadius;
            }

            int capacity = 1 + config.ParkedVehicles + config.MaxPlayers + 16;
            _playerByEntity = new PlayerSession[capacity];
            _vehicleByEntity = new Vehicle[capacity];
            SpawnParkedVehicles();
            for (int id = 1 + config.ParkedVehicles; id < capacity; id++)
            {
                _freePlayerIds.Enqueue(id);
            }

            _workers = new SnapshotWorker[config.SnapshotThreads];
            for (int i = 0; i < _workers.Length; i++)
            {
                _workers[i] = new SnapshotWorker(new InterestManager(_grid, _interestSettings), capacity);
            }
        }

        public ServerConfig Config
        {
            get { return _cfg; }
        }

        public ServerMetrics Metrics
        {
            get { return _metrics; }
        }

        public uint CurrentTick
        {
            get { return _tick; }
        }

        public IReadOnlyList<PlayerSession> Sessions
        {
            get { return _sessions; }
        }

        public IReadOnlyList<Vehicle> Vehicles
        {
            get { return _vehicles; }
        }

        public InterestSettings InterestSettings
        {
            get { return _interestSettings; }
        }

        public PlayerSession GetSession(int id)
        {
            return id > 0 && id < _playerByEntity.Length ? _playerByEntity[id] : null;
        }

        public Vehicle GetVehicle(int id)
        {
            return id > 0 && id < _vehicleByEntity.Length ? _vehicleByEntity[id] : null;
        }

        // ------------------------------------------------------------------ world setup

        private void SpawnParkedVehicles()
        {
            List<VehicleDef> pool = new List<VehicleDef>();
            foreach (VehicleDef d in VehicleCatalog.All)
            {
                if (!d.Premium && !d.FactionOnly && d.Class != VehicleClass.Boat && d.Class != VehicleClass.Helicopter)
                {
                    pool.Add(d);
                }
            }
            for (int i = 0; i < _cfg.ParkedVehicles; i++)
            {
                Vehicle v = new Vehicle();
                v.Id = 1 + i;
                v.Def = pool[_rng.Next(pool.Count)];
                // A quarter of the cars park around the spawn point so new players find one.
                float x;
                float z;
                if (i % 4 == 0)
                {
                    RandomPointAround(_cfg.SpawnX, _cfg.SpawnZ, Math.Max(30f, _cfg.SpawnRadius + 30f), out x, out z);
                }
                else
                {
                    x = 50f + (float)_rng.NextDouble() * (WorldBounds.Size - 100f);
                    z = 50f + (float)_rng.NextDouble() * (WorldBounds.Size - 100f);
                }
                v.State.X = x;
                v.State.Z = z;
                v.State.Yaw = (float)(_rng.NextDouble() * Math.PI * 2);
                _vehicles.Add(v);
                _vehicleByEntity[v.Id] = v;
                _grid.Insert(v.Id, x, z);
            }
        }

        private void RandomPointAround(float cx, float cz, float radius, out float x, out float z)
        {
            double angle = _rng.NextDouble() * Math.PI * 2;
            double dist = Math.Sqrt(_rng.NextDouble()) * radius;
            x = WorldBounds.ClampX((float)(cx + Math.Sin(angle) * dist));
            z = WorldBounds.ClampZ((float)(cz + Math.Cos(angle) * dist));
        }

        // ------------------------------------------------------------------ packets

        private sealed class EndpointSink : IPacketSink
        {
            private readonly ITransport _transport;
            private readonly EndPoint _to;

            public EndpointSink(ITransport transport, EndPoint to)
            {
                _transport = transport;
                _to = to;
            }

            public void SendPacket(byte[] buffer, int length)
            {
                _transport.Send(_to, buffer, length);
            }
        }

        /// <summary>Captures the Connect message from the first packet of an unknown endpoint.</summary>
        private sealed class HandshakeReceiver : IMessageReceiver
        {
            private readonly BitReader _reader = new BitReader();
            public readonly ConnectMessage Message = new ConnectMessage();
            public bool HasConnect;

            public void Reset()
            {
                HasConnect = false;
            }

            public void OnMessage(byte[] buffer, int offset, int length, bool reliable)
            {
                if (HasConnect)
                {
                    return;
                }
                _reader.Reset(buffer, offset, length);
                if (Protocol.ReadType(_reader) != MessageType.Connect)
                {
                    return;
                }
                Message.Deserialize(_reader);
                HasConnect = !_reader.Overflowed;
            }
        }

        /// <summary>Processes one datagram. Call from the simulation thread only.</summary>
        public void HandlePacket(EndPoint from, byte[] data, int offset, int length, double nowMs)
        {
            _now = nowMs;
            _metrics.PacketsReceived++;
            PlayerSession s;
            if (_byEndpoint.TryGetValue(from, out s))
            {
                _current = s;
                PacketReadResult result = s.Channel.ReadPacket(data, offset, length, nowMs, this);
                _current = null;
                if (result == PacketReadResult.Ok || result == PacketReadResult.Duplicate)
                {
                    s.LastReceiveMs = nowMs;
                }
                else
                {
                    _metrics.MalformedPackets++;
                }
                return;
            }

            PacketReadResult header = PacketHeader.Validate(data, offset, length);
            if (header == PacketReadResult.WrongVersion)
            {
                if (_handshakesThisTick++ < MaxHandshakesPerTick)
                {
                    SendReject(from, new ReliableChannel(), RejectReason.VersionMismatch);
                }
                return;
            }
            if (header != PacketReadResult.Ok)
            {
                _metrics.MalformedPackets++;
                return;
            }
            if (_handshakesThisTick++ >= MaxHandshakesPerTick)
            {
                return;
            }
            ReliableChannel channel = new ReliableChannel();
            _handshake.Reset();
            if (channel.ReadPacket(data, offset, length, nowMs, _handshake) != PacketReadResult.Ok || !_handshake.HasConnect)
            {
                _metrics.MalformedPackets++;
                return;
            }
            HandleConnect(from, channel, _handshake.Message, nowMs);
        }

        private void SendReject(EndPoint to, ReliableChannel channel, RejectReason reason)
        {
            _metrics.ConnectsRejected++;
            ConnectRejectMessage reject = new ConnectRejectMessage();
            reject.Reason = reason;
            channel.QueueUnreliable(reject);
            channel.Flush(_now, new EndpointSink(_transport, to), false);
            _log.Info("connect_rejected", "endpoint", to.ToString(), "reason", reason.ToString());
        }

        private struct Claims
        {
            public string AccountId;
            public FactionId Faction;
            public int CrewId;
            public bool Vip;
            public bool Admin;
            public long Wallet;
            public long Phone;
        }

        /// <summary>
        /// Maps a session token to account claims.
        /// TODO(firebase): verify the Firebase ID token (RS256 signature against Google's public
        /// certs, aud = project id, iss, exp), then load the profile from Firestore by UID.
        /// Firebase is unreachable from the build sandbox, so until then every non-dev token is an
        /// anonymous civilian. Tokens are never logged.
        /// </summary>
        private bool TryResolveToken(string token, out Claims claims)
        {
            claims = new Claims();
            claims.Wallet = _cfg.StartingWalletRupees;
            claims.Phone = _cfg.StartingPhoneBalanceRupees;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }
            if (token.StartsWith("dev:", StringComparison.Ordinal))
            {
                if (!_cfg.AllowDevTokens)
                {
                    return false;
                }
                claims.AccountId = "dev";
                string[] parts = token.Substring(4).Split(';');
                foreach (string part in parts)
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = part.Substring(0, eq).Trim();
                    string value = part.Substring(eq + 1).Trim();
                    long number;
                    bool isNumber = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
                    switch (key)
                    {
                        case "faction":
                            FactionDef def = FactionCatalog.FindByKey(value);
                            claims.Faction = def != null ? def.Id : FactionId.None;
                            break;
                        case "crew":
                            claims.CrewId = isNumber ? (int)number : 0;
                            break;
                        case "vip":
                            claims.Vip = value == "1" || value == "true";
                            break;
                        case "admin":
                            claims.Admin = value == "1" || value == "true";
                            break;
                        case "wallet":
                            if (isNumber && number >= 0)
                            {
                                claims.Wallet = number;
                            }
                            break;
                        case "phone":
                            if (isNumber && number >= 0)
                            {
                                claims.Phone = number;
                            }
                            break;
                    }
                }
                return true;
            }
            claims.AccountId = "anon";
            return true;
        }

        private void HandleConnect(EndPoint from, ReliableChannel channel, ConnectMessage msg, double nowMs)
        {
            _metrics.ConnectAttempts++;
            if (msg.ProtocolVersion != ProtocolConstants.ProtocolVersion)
            {
                SendReject(from, channel, RejectReason.VersionMismatch);
                return;
            }
            Claims claims;
            if (!TryResolveToken(msg.SessionToken, out claims))
            {
                SendReject(from, channel, RejectReason.InvalidToken);
                return;
            }
            if (_sessions.Count >= _cfg.MaxPlayers || _freePlayerIds.Count == 0)
            {
                SendReject(from, channel, RejectReason.ServerFull);
                return;
            }
            int id = _freePlayerIds.Dequeue();
            PlayerSession s = new PlayerSession(id, from, channel, _transport);
            string name = _filter.Filter(AbuseFilter.Sanitize(msg.PlayerName, 24));
            s.Name = string.IsNullOrEmpty(name) ? "Player" + id.ToString(CultureInfo.InvariantCulture) : name;
            s.Language = msg.Language == LocalizationTable.Hindi ? LocalizationTable.Hindi : LocalizationTable.English;
            s.AccountId = claims.AccountId + ":" + id.ToString(CultureInfo.InvariantCulture);
            s.Faction = claims.Faction;
            s.CrewId = claims.CrewId;
            s.IsVip = claims.Vip;
            s.IsAdmin = claims.Admin;
            s.WalletRupees = claims.Wallet;
            s.PhoneBalanceRupees = claims.Phone;
            s.ConnectedAtMs = nowMs;
            s.LastReceiveMs = nowMs;
            float x;
            float z;
            RandomPointAround(_cfg.SpawnX, _cfg.SpawnZ, _cfg.SpawnRadius, out x, out z);
            s.Character.X = x;
            s.Character.Z = z;
            s.Character.Yaw = 0f;

            _sessions.Add(s);
            _byEndpoint[from] = s;
            _playerByEntity[id] = s;
            _grid.Insert(id, x, z);

            ConnectAcceptMessage accept = new ConnectAcceptMessage();
            accept.PlayerEntityId = id;
            accept.ServerTick = _tick;
            accept.TickRate = (byte)_cfg.TickRate;
            accept.SpawnX = x;
            accept.SpawnY = 0f;
            accept.SpawnZ = z;
            accept.WalletRupees = s.WalletRupees;
            accept.PhoneBalanceRupees = s.PhoneBalanceRupees;
            accept.FactionId = (byte)s.Faction;
            accept.IsVip = s.IsVip;
            s.Channel.QueueReliable(accept);
            SendSystem(s, "system.welcome", s.Name);
            _log.Info("connect", "player", id, "name", s.Name, "account", s.AccountId, "faction", s.Faction.ToString(),
                "online", _sessions.Count);
        }

        // ------------------------------------------------------------------ messages

        public void OnMessage(byte[] buffer, int offset, int length, bool reliable)
        {
            PlayerSession s = _current;
            if (s == null)
            {
                return;
            }
            _msgReader.Reset(buffer, offset, length);
            MessageType type = Protocol.ReadType(_msgReader);
            switch (type)
            {
                case MessageType.PlayerInput:
                    _inInput.Deserialize(_msgReader);
                    if (!_msgReader.Overflowed)
                    {
                        HandleInput(s, _inInput);
                    }
                    break;
                case MessageType.ChatMessage:
                    _inChat.Deserialize(_msgReader);
                    if (!_msgReader.Overflowed)
                    {
                        HandleChat(s, _inChat);
                    }
                    break;
                case MessageType.VoiceFrame:
                    _inVoice.Deserialize(_msgReader);
                    if (!_msgReader.Overflowed)
                    {
                        HandleVoice(s, _inVoice);
                    }
                    else
                    {
                        _metrics.VoiceFramesDropped++;
                    }
                    break;
                case MessageType.EnterVehicle:
                    _inEnter.Deserialize(_msgReader);
                    if (!_msgReader.Overflowed)
                    {
                        HandleEnterVehicle(s, _inEnter.VehicleId);
                    }
                    break;
                case MessageType.ExitVehicle:
                    _inExit.Deserialize(_msgReader);
                    if (!_msgReader.Overflowed)
                    {
                        HandleExitVehicle(s);
                    }
                    break;
                case MessageType.JobStart:
                    _inJob.Deserialize(_msgReader);
                    if (!_msgReader.Overflowed)
                    {
                        HandleJobStart(s, _inJob.JobId);
                    }
                    break;
                case MessageType.SetMute:
                    _inMute.Deserialize(_msgReader);
                    if (!_msgReader.Overflowed)
                    {
                        HandleSetMute(s, _inMute);
                    }
                    break;
                case MessageType.Ping:
                    _inPing.Deserialize(_msgReader);
                    if (!_msgReader.Overflowed)
                    {
                        _outPong.ClientTimeMs = _inPing.ClientTimeMs;
                        _outPong.ServerTick = _tick;
                        s.Channel.QueueUnreliable(_outPong);
                    }
                    break;
                case MessageType.Disconnect:
                    _inDisconnect.Deserialize(_msgReader);
                    s.PendingKick = true;
                    s.KickReason = "client_quit";
                    break;
                case MessageType.Connect:
                    break; // resent handshake, already accepted
                default:
                    _metrics.MalformedPackets++;
                    break;
            }
        }

        // ------------------------------------------------------------------ movement

        private float MaxSpeedFor(PlayerSession s)
        {
            if (s.InVehicle)
            {
                Vehicle v = GetVehicle(s.VehicleId);
                if (v != null)
                {
                    return v.Def.TopSpeedMetersPerSecond;
                }
            }
            return Movement.RunSpeed;
        }

        /// <summary>
        /// Stores the latest input. The client's predicted position is checked for teleports and
        /// for drifting away from the authoritative position; such inputs are rejected and
        /// counted, and repeated violations kick the player.
        /// </summary>
        public void HandleInput(PlayerSession s, PlayerInputMessage input)
        {
            if (s.LastInputMs > 0 && !IsNewerTick(input.InputTick, s.LastInputTick))
            {
                return; // stale or duplicate input
            }
            uint tickDelta = s.LastInputMs > 0 ? input.InputTick - s.LastInputTick : 1u;
            double elapsed = s.LastInputMs > 0 ? (_now - s.LastInputMs) / 1000.0 : _cfg.TickIntervalMs / 1000.0;
            double byTicks = Math.Min(tickDelta, 40u) / 20.0;
            float dt = (float)Math.Max(Math.Max(elapsed, byTicks), _cfg.TickIntervalMs / 1000.0);
            float maxSpeed = MaxSpeedFor(s);

            string violation = null;
            if (s.HasClientPosition)
            {
                if (!MovementValidator.IsPlausibleMove(s.LastClientX, s.LastClientZ, input.ClientX, input.ClientZ, dt, maxSpeed))
                {
                    violation = "implausible_step";
                }
            }
            float divergenceLimit = 10f + maxSpeed * 0.5f;
            if (MovementValidator.ClientDiverged(s.Character.X, s.Character.Z, input.ClientX, input.ClientZ, divergenceLimit))
            {
                violation = "diverged";
            }
            if (violation != null)
            {
                _log.Debug("movement_violation", "player", s.Id, "kind", violation, "in_vehicle", s.InVehicle,
                    "dt", dt, "max_speed", maxSpeed, "from_x", s.LastClientX, "from_z", s.LastClientZ,
                    "client_x", input.ClientX, "client_z", input.ClientZ, "server_x", s.Character.X, "server_z", s.Character.Z);
            }
            // After the server moved the player (spawn, vehicle enter/exit) the step check restarts
            // only once the client has caught up with the authoritative position; the divergence
            // check above still applies meanwhile.
            float currentSpeed = Movement.RunSpeed;
            Vehicle current = s.InVehicle ? GetVehicle(s.VehicleId) : null;
            if (current != null)
            {
                currentSpeed = Math.Abs(current.State.Speed);
            }
            if (s.HasClientPosition ||
                !MovementValidator.ClientDiverged(s.Character.X, s.Character.Z, input.ClientX, input.ClientZ, 1f + currentSpeed * 0.1f))
            {
                s.HasClientPosition = true;
                s.LastClientX = input.ClientX;
                s.LastClientZ = input.ClientZ;
            }
            s.LastInputTick = input.InputTick;
            s.LastInputMs = _now;

            if (violation != null)
            {
                RecordViolation(s);
                s.MoveX = 0f;
                s.MoveZ = 0f;
                s.Throttle = 0f;
                s.Steer = 0f;
                return;
            }
            s.MoveX = Movement.SanitizeAxis(input.MoveX);
            s.MoveZ = Movement.SanitizeAxis(input.MoveZ);
            s.InputYaw = input.Yaw;
            s.Buttons = input.Buttons;
            s.Throttle = Movement.SanitizeAxis(input.Throttle);
            s.Steer = Movement.SanitizeAxis(input.Steer);
        }

        private static bool IsNewerTick(uint a, uint b)
        {
            return (int)(a - b) > 0;
        }

        private void RecordViolation(PlayerSession s)
        {
            _metrics.MovementViolations++;
            s.TotalViolations++;
            if (_now - s.ViolationWindowStartMs > ViolationWindowMs)
            {
                s.ViolationWindowStartMs = _now;
                s.MovementViolations = 0;
            }
            s.MovementViolations++;
            if (s.MovementViolations >= ViolationsBeforeKick && !s.PendingKick)
            {
                s.PendingKick = true;
                s.KickReason = "movement_violation";
                SendSystem(s, "system.kicked_cheat");
            }
        }

        /// <summary>Called after the server moves a player itself (spawn, vehicle enter/exit).</summary>
        private static void ResetClientPositionCheck(PlayerSession s)
        {
            s.HasClientPosition = false;
        }

        private void Simulate(PlayerSession s, float dt)
        {
            if (_now - s.LastInputMs > InputStaleMs)
            {
                s.MoveX = 0f;
                s.MoveZ = 0f;
                s.Throttle = 0f;
                s.Steer = 0f;
            }
            if (s.InVehicle)
            {
                Vehicle v = GetVehicle(s.VehicleId);
                if (v == null)
                {
                    s.VehicleId = 0;
                    return;
                }
                bool brake = (s.Buttons & InputButtons.Brake) != 0;
                Movement.StepVehicle(ref v.State, v.Def, s.Throttle, s.Steer, brake, dt);
                s.Character.X = v.State.X;
                s.Character.Z = v.State.Z;
                s.Character.Yaw = v.State.Yaw;
                _grid.Move(v.Id, v.State.X, v.State.Z);
            }
            else
            {
                bool sprint = (s.Buttons & InputButtons.Sprint) != 0;
                Movement.StepCharacter(ref s.Character, s.MoveX, s.MoveZ, s.InputYaw, sprint, dt);
            }
            _grid.Move(s.Id, s.Character.X, s.Character.Z);
        }

        // ------------------------------------------------------------------ vehicles

        public void HandleEnterVehicle(PlayerSession s, int vehicleId)
        {
            EnterVehicleMessage reply = new EnterVehicleMessage();
            reply.VehicleId = vehicleId;
            reply.PlayerId = s.Id;
            Vehicle v = GetVehicle(vehicleId);
            if (s.InVehicle || v == null)
            {
                reply.Success = false;
            }
            else if (v.DriverId != 0)
            {
                reply.Success = false;
                SendSystem(s, "system.vehicle_occupied");
            }
            else
            {
                float dx = v.State.X - s.Character.X;
                float dz = v.State.Z - s.Character.Z;
                if (dx * dx + dz * dz > Movement.EnterVehicleRange * Movement.EnterVehicleRange)
                {
                    reply.Success = false;
                    SendSystem(s, "system.vehicle_too_far");
                }
                else
                {
                    v.DriverId = s.Id;
                    v.State.Speed = 0f;
                    s.VehicleId = v.Id;
                    s.Character.X = v.State.X;
                    s.Character.Z = v.State.Z;
                    _grid.Move(s.Id, s.Character.X, s.Character.Z);
                    ResetClientPositionCheck(s);
                    reply.Success = true;
                }
            }
            s.Channel.QueueReliable(reply);
        }

        public void HandleExitVehicle(PlayerSession s)
        {
            ExitVehicleMessage reply = new ExitVehicleMessage();
            reply.PlayerId = s.Id;
            reply.VehicleId = s.VehicleId;
            Vehicle v = GetVehicle(s.VehicleId);
            if (v == null)
            {
                reply.Success = false;
            }
            else
            {
                v.DriverId = 0;
                v.State.Speed = 0f;
                s.VehicleId = 0;
                // Step out on the left side of the vehicle.
                float sideX = -(float)Math.Cos(v.State.Yaw);
                float sideZ = (float)Math.Sin(v.State.Yaw);
                s.Character.X = WorldBounds.ClampX(v.State.X + sideX * 2f);
                s.Character.Z = WorldBounds.ClampZ(v.State.Z + sideZ * 2f);
                _grid.Move(s.Id, s.Character.X, s.Character.Z);
                ResetClientPositionCheck(s);
                reply.Success = true;
            }
            s.Channel.QueueReliable(reply);
        }

        // ------------------------------------------------------------------ jobs

        public void HandleJobStart(PlayerSession s, int jobId)
        {
            JobStartMessage reply = new JobStartMessage();
            reply.JobId = jobId;
            JobDef def = JobCatalog.Get(jobId);
            if (def == null || !def.WaypointFlow)
            {
                SendSystem(s, "system.job_unavailable");
            }
            else if (s.ActiveJobId != 0)
            {
                SendSystem(s, "system.job_busy");
            }
            else if (def.NeedsVehicle && !s.InVehicle)
            {
                SendSystem(s, "system.job_needs_vehicle");
            }
            else
            {
                double angle = _rng.NextDouble() * Math.PI * 2;
                float dist = 200f + (float)_rng.NextDouble() * 400f;
                float wx = Quantization.Clamp(s.Character.X + (float)Math.Sin(angle) * dist, 50f, WorldBounds.Size - 50f);
                float wz = Quantization.Clamp(s.Character.Z + (float)Math.Cos(angle) * dist, 50f, WorldBounds.Size - 50f);
                s.ActiveJobId = jobId;
                s.JobWaypointX = wx;
                s.JobWaypointZ = wz;
                float dx = wx - s.Character.X;
                float dz = wz - s.Character.Z;
                s.JobDistance = (float)Math.Sqrt(dx * dx + dz * dz);
                s.JobStartedMs = _now;
                reply.Accepted = true;
                reply.WaypointX = wx;
                reply.WaypointZ = wz;
                SendSystem(s, "system.job_started", LocalizationTable.Get(s.Language, def.NameKey));
            }
            s.Channel.QueueReliable(reply);
        }

        /// <summary>Pays rupees from the job's hourly range, scaled by the expected trip time.</summary>
        public static long JobPayment(JobDef def, float distance)
        {
            float typicalSpeed = def.NeedsVehicle ? 12f : 4f;
            double expectedSeconds = distance / typicalSpeed;
            long pay = (long)Math.Round(def.MidRupeesPerHour * expectedSeconds / 3600.0);
            return pay < 50 ? 50 : pay;
        }

        private void CheckJob(PlayerSession s)
        {
            if (s.ActiveJobId == 0)
            {
                return;
            }
            float dx = s.JobWaypointX - s.Character.X;
            float dz = s.JobWaypointZ - s.Character.Z;
            if (dx * dx + dz * dz > JobWaypointRadius * JobWaypointRadius)
            {
                return;
            }
            JobDef def = JobCatalog.Get(s.ActiveJobId);
            int jobId = s.ActiveJobId;
            s.ActiveJobId = 0;
            if (def == null)
            {
                return;
            }
            // Arriving faster than the vehicle could have driven there means a teleport.
            double elapsed = (_now - s.JobStartedMs) / 1000.0;
            double minSeconds = (s.JobDistance - JobWaypointRadius) / (MaxSpeedFor(s) * 1.2);
            if (elapsed < minSeconds)
            {
                RecordViolation(s);
                _log.Warn("job_rejected", "player", s.Id, "job", def.Key, "elapsed_s", elapsed, "min_s", minSeconds);
                return;
            }
            long pay = JobPayment(def, s.JobDistance);
            s.WalletRupees += pay;
            _metrics.JobsCompleted++;
            JobCompleteMessage done = new JobCompleteMessage();
            done.JobId = jobId;
            done.PaymentRupees = pay;
            done.WalletRupees = s.WalletRupees;
            s.Channel.QueueReliable(done);
            SendSystem(s, "system.job_complete", pay);
            _log.Info("txn", "player", s.Id, "kind", "job_pay", "job", def.Key, "amount", pay, "wallet", s.WalletRupees);
        }

        // ------------------------------------------------------------------ mute / block

        public void HandleSetMute(PlayerSession s, SetMuteMessage m)
        {
            if (m.TargetPlayerId == s.Id || m.TargetPlayerId <= 0)
            {
                return;
            }
            HashSet<int> set = m.Block ? s.Blocked : s.Muted;
            if (m.Enabled)
            {
                if (set.Count < MaxMuteEntries)
                {
                    set.Add(m.TargetPlayerId);
                }
            }
            else
            {
                set.Remove(m.TargetPlayerId);
            }
        }

        // ------------------------------------------------------------------ chat

        public void SendSystem(PlayerSession s, string key, params object[] args)
        {
            ChatMessage m = new ChatMessage();
            m.Channel = ChatChannel.System;
            m.Text = args.Length == 0 ? LocalizationTable.Get(s.Language, key) : LocalizationTable.Format(s.Language, key, args);
            s.Channel.QueueReliable(m);
        }

        private void Reject(PlayerSession s, string key, params object[] args)
        {
            _metrics.ChatRejected++;
            SendSystem(s, key, args);
        }

        /// <summary>Routes one chat message according to its channel's audience and cost rules.</summary>
        public void HandleChat(PlayerSession s, ChatMessage incoming)
        {
            // Flood limit across all channels.
            if (_now - s.ChatWindowStartMs > _cfg.ChatWindowSeconds * 1000.0)
            {
                s.ChatWindowStartMs = _now;
                s.ChatInWindow = 0;
            }
            if (s.ChatInWindow >= _cfg.ChatBurst)
            {
                Reject(s, "system.rate_limited");
                return;
            }

            string text = AbuseFilter.Sanitize(incoming.Text, 200);
            if (text.Length == 0)
            {
                return;
            }
            text = _filter.Filter(text);
            ChatChannel channel = incoming.Channel;

            ChatMessage outgoing = new ChatMessage();
            outgoing.Channel = channel;
            outgoing.SenderId = s.Id;
            outgoing.SenderName = s.Name;
            outgoing.Text = text;

            switch (channel)
            {
                case ChatChannel.Local:
                    break;
                case ChatChannel.Global:
                    if (_now < s.NextGlobalChatMs)
                    {
                        int wait = (int)Math.Ceiling((s.NextGlobalChatMs - _now) / 1000.0);
                        Reject(s, "system.slow_mode", wait);
                        return;
                    }
                    break;
                case ChatChannel.Faction:
                    if (s.Faction == FactionId.None)
                    {
                        Reject(s, "system.not_in_faction");
                        return;
                    }
                    break;
                case ChatChannel.Crew:
                    if (s.CrewId == 0)
                    {
                        Reject(s, "system.not_in_crew");
                        return;
                    }
                    break;
                case ChatChannel.Vip:
                    if (!s.IsVip)
                    {
                        Reject(s, "system.vip_only");
                        return;
                    }
                    break;
                case ChatChannel.Ad:
                    if (s.WalletRupees < _cfg.AdCostRupees)
                    {
                        Reject(s, "system.ad_insufficient", _cfg.AdCostRupees);
                        return;
                    }
                    break;
                case ChatChannel.Sms:
                    {
                        PlayerSession target = GetSession(incoming.TargetId);
                        if (target == null || target == s)
                        {
                            Reject(s, "system.sms_target_offline");
                            return;
                        }
                        if (target.Blocked.Contains(s.Id))
                        {
                            Reject(s, "system.sms_blocked");
                            return;
                        }
                        if (s.PhoneBalanceRupees < _cfg.SmsCostRupees)
                        {
                            Reject(s, "system.sms_no_balance");
                            return;
                        }
                        outgoing.TargetId = target.Id;
                    }
                    break;
                case ChatChannel.AdminAnnouncement:
                    if (!s.IsAdmin)
                    {
                        Reject(s, "system.admin_only");
                        return;
                    }
                    break;
                default:
                    Reject(s, "system.invalid_channel");
                    return;
            }

            s.ChatInWindow++;
            _encode.Reset();
            Protocol.Write(_encode, outgoing);
            byte[] bytes = _encode.Buffer;
            int len = _encode.LengthBytes;
            int delivered = 0;

            switch (channel)
            {
                case ChatChannel.Local:
                    _grid.QueryRadius(s.Character.X, s.Character.Z, _cfg.LocalChatRadius, _query);
                    for (int i = 0; i < _query.Count; i++)
                    {
                        PlayerSession r = _playerByEntity[_query[i]];
                        if (r != null && Deliver(r, s, bytes, len))
                        {
                            delivered++;
                        }
                    }
                    break;
                case ChatChannel.Global:
                    s.NextGlobalChatMs = _now + _cfg.GlobalSlowModeSeconds * 1000.0;
                    delivered = DeliverWhere(s, bytes, len, null);
                    break;
                case ChatChannel.Faction:
                    {
                        FactionId f = s.Faction;
                        delivered = DeliverWhere(s, bytes, len, r => r.Faction == f);
                    }
                    break;
                case ChatChannel.Crew:
                    {
                        int crew = s.CrewId;
                        delivered = DeliverWhere(s, bytes, len, r => r.CrewId == crew);
                    }
                    break;
                case ChatChannel.Vip:
                    delivered = DeliverWhere(s, bytes, len, r => r.IsVip);
                    break;
                case ChatChannel.Ad:
                    {
                        long cost = _cfg.AdCostRupees;
                        s.WalletRupees -= cost;
                        long news = cost / 2;
                        NewsFundRupees += news;
                        TreasuryRupees += cost - news;
                        _log.Info("txn", "player", s.Id, "kind", "ad_chat", "amount", -cost, "wallet", s.WalletRupees);
                        delivered = DeliverWhere(s, bytes, len, null);
                        SendSystem(s, "system.ad_charged", cost);
                    }
                    break;
                case ChatChannel.Sms:
                    {
                        PlayerSession target = GetSession(outgoing.TargetId);
                        s.PhoneBalanceRupees -= _cfg.SmsCostRupees;
                        if (!target.IsIgnoring(s.Id))
                        {
                            target.Channel.QueueReliable(bytes, 0, len);
                            delivered++;
                        }
                        s.Channel.QueueReliable(bytes, 0, len); // sender's copy for the phone app
                    }
                    break;
                case ChatChannel.AdminAnnouncement:
                    // Announcements cannot be muted.
                    for (int i = 0; i < _sessions.Count; i++)
                    {
                        _sessions[i].Channel.QueueReliable(bytes, 0, len);
                        delivered++;
                    }
                    break;
            }

            _metrics.ChatRouted++;
            _metrics.ChatDeliveries += delivered;
            RecordChat(s.Id, channel, outgoing.TargetId, text);
        }

        private bool Deliver(PlayerSession recipient, PlayerSession sender, byte[] bytes, int len)
        {
            if (recipient != sender && recipient.IsIgnoring(sender.Id))
            {
                return false;
            }
            recipient.Channel.QueueReliable(bytes, 0, len);
            return true;
        }

        private int DeliverWhere(PlayerSession sender, byte[] bytes, int len, Func<PlayerSession, bool> filter)
        {
            int delivered = 0;
            for (int i = 0; i < _sessions.Count; i++)
            {
                PlayerSession r = _sessions[i];
                if (filter != null && !filter(r))
                {
                    continue;
                }
                if (Deliver(r, sender, bytes, len))
                {
                    delivered++;
                }
            }
            return delivered;
        }

        private void RecordChat(int senderId, ChatChannel channel, int targetId, string text)
        {
            ChatLogEntry e = new ChatLogEntry();
            e.TimeMs = _now;
            e.SenderId = senderId;
            e.Channel = channel;
            e.TargetId = targetId;
            e.Text = text;
            _chatLog[_chatLogNext] = e;
            _chatLogNext = (_chatLogNext + 1) % ChatLogCapacity;
            if (_chatLogCount < ChatLogCapacity)
            {
                _chatLogCount++;
            }
            if (_cfg.LogChatToConsole)
            {
                _log.Chat("sender", senderId, "channel", channel.ToString(), "target", targetId, "text", text);
            }
        }

        /// <summary>Copies chat log entries newer than sinceMs (oldest first), e.g. report evidence.</summary>
        public void GetRecentChat(double sinceMs, List<ChatLogEntry> into)
        {
            into.Clear();
            int start = (_chatLogNext - _chatLogCount + ChatLogCapacity) % ChatLogCapacity;
            for (int i = 0; i < _chatLogCount; i++)
            {
                ChatLogEntry e = _chatLog[(start + i) % ChatLogCapacity];
                if (e.TimeMs >= sinceMs)
                {
                    into.Add(e);
                }
            }
        }

        // ------------------------------------------------------------------ voice

        /// <summary>
        /// Relays an Opus frame without decoding or logging it. Proximity goes to players within
        /// ProximityVoiceRadius; faction radio goes to every online member of the sender's faction.
        /// </summary>
        public void HandleVoice(PlayerSession s, VoiceFrameMessage frame)
        {
            _metrics.VoiceFramesIn++;
            if (frame.Length <= 0 || frame.Length > ProtocolConstants.MaxVoiceBytes)
            {
                _metrics.VoiceFramesDropped++;
                return;
            }
            if (_now - s.VoiceWindowStartMs >= 1000.0)
            {
                s.VoiceWindowStartMs = _now;
                s.VoiceInWindow = 0;
            }
            if (s.VoiceInWindow >= _cfg.VoiceFramesPerSecond)
            {
                _metrics.VoiceFramesDropped++;
                return;
            }
            s.VoiceInWindow++;
            if (frame.Channel == VoiceChannel.FactionRadio && s.Faction == FactionId.None)
            {
                _metrics.VoiceFramesDropped++;
                return;
            }
            frame.SenderId = s.Id;
            _encode.Reset();
            Protocol.Write(_encode, frame);
            byte[] bytes = _encode.Buffer;
            int len = _encode.LengthBytes;
            if (frame.Channel == VoiceChannel.Proximity)
            {
                _grid.QueryRadius(s.Character.X, s.Character.Z, _cfg.ProximityVoiceRadius, _query);
                for (int i = 0; i < _query.Count; i++)
                {
                    PlayerSession r = _playerByEntity[_query[i]];
                    if (r != null && r != s && !r.IsIgnoring(s.Id))
                    {
                        r.Channel.QueueUnreliable(bytes, 0, len);
                        _metrics.VoiceFramesRelayed++;
                    }
                }
            }
            else
            {
                for (int i = 0; i < _sessions.Count; i++)
                {
                    PlayerSession r = _sessions[i];
                    if (r != s && r.Faction == s.Faction && !r.IsIgnoring(s.Id))
                    {
                        r.Channel.QueueUnreliable(bytes, 0, len);
                        _metrics.VoiceFramesRelayed++;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ tick

        /// <summary>Advances the simulation one step and sends snapshots and queued messages.</summary>
        public void Tick(double nowMs)
        {
            _now = nowMs;
            _tick++;
            _handshakesThisTick = 0;
            float dt = (float)(_cfg.TickIntervalMs / 1000.0);

            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                PlayerSession s = _sessions[i];
                if (s.PendingKick)
                {
                    bool quit = s.KickReason == "client_quit";
                    Disconnect(s, quit ? DisconnectReason.ClientQuit : DisconnectReason.Kicked, s.KickReason);
                    if (!quit)
                    {
                        _metrics.Kicked++;
                    }
                }
                else if (nowMs - s.LastReceiveMs > _cfg.ClientTimeoutSeconds * 1000.0)
                {
                    _metrics.TimedOut++;
                    Disconnect(s, DisconnectReason.Timeout, "timeout");
                }
            }

            for (int i = 0; i < _sessions.Count; i++)
            {
                Simulate(_sessions[i], dt);
            }
            for (int i = 0; i < _sessions.Count; i++)
            {
                CheckJob(_sessions[i]);
            }

            int workers = Math.Min(_workers.Length, Math.Max(1, _sessions.Count / 32));
            if (workers <= 1)
            {
                BuildAndFlush(_workers[0], 0, 1);
            }
            else
            {
                Parallel.For(0, workers, w => BuildAndFlush(_workers[w], w, workers));
            }
            for (int w = 0; w < _workers.Length; w++)
            {
                SnapshotWorker worker = _workers[w];
                _metrics.Snapshots += worker.Snapshots;
                _metrics.SnapshotEntities += worker.Entities;
                worker.Snapshots = 0;
                worker.Entities = 0;
            }
        }

        private void BuildAndFlush(SnapshotWorker worker, int start, int stride)
        {
            for (int i = start; i < _sessions.Count; i += stride)
            {
                PlayerSession s = _sessions[i];
                BuildSnapshot(worker, s);
                s.Channel.Flush(_now, s, true);
            }
        }

        private sealed class SnapshotWorker
        {
            public readonly InterestManager Interest;
            public readonly WorldSnapshotMessage Snapshot = new WorldSnapshotMessage();
            public readonly BitWriter Writer = new BitWriter(1500);
            public readonly bool[] Entered;
            public long Snapshots;
            public long Entities;

            public SnapshotWorker(InterestManager interest, int capacity)
            {
                Interest = interest;
                Entered = new bool[capacity];
            }
        }

        private void FillEntity(int id, bool spawn, ref EntityState e)
        {
            e.Id = id;
            e.Spawn = spawn;
            PlayerSession p = _playerByEntity[id];
            if (p != null)
            {
                e.Kind = EntityKind.Player;
                e.ModelId = id % 8;
                e.FactionId = (byte)p.Faction;
                e.X = p.Character.X;
                e.Y = p.Character.Y;
                e.Z = p.Character.Z;
                e.Yaw = p.Character.Yaw;
                e.LinkedId = p.VehicleId;
                return;
            }
            Vehicle v = _vehicleByEntity[id];
            e.Kind = EntityKind.Vehicle;
            e.ModelId = v != null ? v.Def.Id : 0;
            e.FactionId = 0;
            if (v != null)
            {
                e.X = v.State.X;
                e.Y = v.State.Y;
                e.Z = v.State.Z;
                e.Yaw = v.State.Yaw;
                e.LinkedId = v.DriverId;
            }
        }

        private void BuildSnapshot(SnapshotWorker worker, PlayerSession s)
        {
            ClientInterest ci = s.Interest;
            worker.Interest.Update(ci, s.Id, s.Character.X, s.Character.Z);

            WorldSnapshotMessage snap = worker.Snapshot;
            snap.Clear();
            snap.ServerTick = _tick;
            snap.LastProcessedInputTick = s.LastInputTick;
            snap.SelfX = s.Character.X;
            snap.SelfY = s.Character.Y;
            snap.SelfZ = s.Character.Z;
            snap.SelfYaw = s.Character.Yaw;
            snap.SelfVehicleId = s.VehicleId;

            for (int i = 0; i < ci.Entered.Count; i++)
            {
                worker.Entered[ci.Entered[i]] = true;
            }
            EntityState e = new EntityState();
            for (int i = 0; i < ci.Relevant.Count; i++)
            {
                int id = ci.Relevant[i];
                bool entered = worker.Entered[id];
                if (!entered && !worker.Interest.ShouldSend(ci.Tiers[i], _tick, id))
                {
                    continue;
                }
                bool spawn = entered || (_tick + (uint)id) % SpawnRefreshTicks == 0;
                FillEntity(id, spawn, ref e);
                snap.AddEntity(ref e);
            }
            for (int i = 0; i < ci.Entered.Count; i++)
            {
                worker.Entered[ci.Entered[i]] = false;
            }
            for (int i = 0; i < ci.Left.Count; i++)
            {
                snap.AddDespawn(ci.Left[i]);
            }

            int written = 0;
            bool first = true;
            do
            {
                int count = Math.Min(SnapshotChunkEntities, snap.EntityCount - written);
                BitWriter w = worker.Writer;
                w.Reset();
                w.WriteByte((byte)MessageType.WorldSnapshot);
                snap.SerializeRange(w, written, count, first);
                w.AlignToByte();
                s.Channel.QueueUnreliable(w.Buffer, 0, w.LengthBytes);
                written += count;
                first = false;
            }
            while (written < snap.EntityCount);

            worker.Snapshots++;
            worker.Entities += snap.EntityCount;
            s.SnapshotsSent++;
            s.SnapshotEntitiesSent += snap.EntityCount;
        }

        // ------------------------------------------------------------------ disconnects

        private void Disconnect(PlayerSession s, DisconnectReason reason, string logReason)
        {
            if (reason != DisconnectReason.ClientQuit)
            {
                DisconnectMessage msg = new DisconnectMessage();
                msg.Reason = reason;
                s.Channel.QueueUnreliable(msg);
                s.Channel.Flush(_now, s, false);
            }
            Remove(s);
            _log.Info("disconnect", "player", s.Id, "reason", logReason, "online", _sessions.Count);
        }

        private void Remove(PlayerSession s)
        {
            if (s.InVehicle)
            {
                Vehicle v = GetVehicle(s.VehicleId);
                if (v != null)
                {
                    v.DriverId = 0;
                    v.State.Speed = 0f;
                }
                s.VehicleId = 0;
            }
            _grid.Remove(s.Id);
            _playerByEntity[s.Id] = null;
            _byEndpoint.Remove(s.EndPoint);
            int index = _sessions.IndexOf(s);
            if (index >= 0)
            {
                int last = _sessions.Count - 1;
                _sessions[index] = _sessions[last];
                _sessions.RemoveAt(last);
            }
            _freePlayerIds.Enqueue(s.Id);
        }

        /// <summary>Tells every client the server is going down.</summary>
        public void Shutdown(double nowMs)
        {
            _now = nowMs;
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                Disconnect(_sessions[i], DisconnectReason.ServerShutdown, "shutdown");
            }
        }

        /// <summary>Test helper: moves a player as if the server simulated it there.</summary>
        public void TeleportForTest(PlayerSession s, float x, float z)
        {
            s.Character.X = x;
            s.Character.Z = z;
            _grid.Move(s.Id, x, z);
            ResetClientPositionCheck(s);
        }

        /// <summary>Test helper: sets the clock used by handlers invoked directly (not via packets).</summary>
        public void SetTimeForTest(double nowMs)
        {
            _now = nowMs;
        }
    }
}
