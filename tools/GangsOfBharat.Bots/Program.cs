using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GangsOfBharat.Server;
using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Net;
using GangsOfBharat.Shared.Serialization;

namespace GangsOfBharat.Bots
{
    public sealed class BotOptions
    {
        public int Bots = 100;
        public int Seconds = 20;
        public string Host;
        public int Port = 7777;
        /// <summary>Side of the square the bots spawn and wander in, centred on the city (8000 = whole map).</summary>
        public float Area = 8000f;
        public int Threads = 4;
        public double WarmupSeconds = 3;
        public double ChatIntervalSeconds = 20;
        public double DriverFraction = 0.3;
        public int Vehicles = -1;
        public int MaxEntities = 100;
        public int SnapshotThreads = 4;
        public bool ServerDebug;

        public static BotOptions Parse(string[] args)
        {
            BotOptions o = new BotOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string v = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "--bots": o.Bots = int.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--seconds": o.Seconds = int.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--host": o.Host = v; i++; break;
                    case "--port": o.Port = int.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--area": o.Area = float.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--threads": o.Threads = int.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--warmup": o.WarmupSeconds = double.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--chat-interval": o.ChatIntervalSeconds = double.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--drivers": o.DriverFraction = double.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--vehicles": o.Vehicles = int.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--max-entities": o.MaxEntities = int.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--snapshot-threads": o.SnapshotThreads = int.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "--server-debug": o.ServerDebug = true; break;
                    default: throw new ArgumentException("Unknown option " + a);
                }
            }
            if (o.Bots < 1 || o.Seconds < 1 || o.Threads < 1 || o.Area <= 0)
            {
                throw new ArgumentException("bots, seconds, threads and area must be positive");
            }
            return o;
        }
    }

    /// <summary>One simulated player: own UDP socket, shared protocol client, simple wander/drive AI.</summary>
    public sealed class Bot : IPacketSink
    {
        private static readonly byte[] FakeOpus = new byte[40];

        private readonly Socket _socket;
        private readonly EndPoint _server;
        private readonly byte[] _buffer = new byte[ProtocolConstants.MaxReceiveBytes];
        private readonly Random _rng;
        private readonly BotOptions _options;
        private readonly PlayerInputMessage _input = new PlayerInputMessage();
        private readonly VoiceFrameMessage _voice = new VoiceFrameMessage();
        private readonly Dictionary<int, int> _vehicleModels = new Dictionary<int, int>();

        public readonly ClientConnection Conn = new ClientConnection();
        public readonly bool Driver;
        public readonly string Faction;

        private CharacterState _me;
        private VehicleState _car;
        private VehicleDef _carDef;
        private int _vehicleId;
        private float _targetX;
        private float _targetZ;
        private int _wantVehicle;
        private float _wantX;
        private float _wantZ;
        private double _nextEnterTry;
        private uint _inputTick;
        private double _nextChatMs;
        private double _talkUntilMs;
        private double _nextTalkMs;
        private ushort _voiceSeq;
        private double _voiceCredit;
        private float _steer;
        private double _nextSteerMs;
        private bool _spawned;

        public long ChatSent;
        public long VoiceSent;

        public bool InVehicle
        {
            get { return _vehicleId != 0; }
        }

        public Bot(int index, EndPoint server, BotOptions options)
        {
            _options = options;
            _rng = new Random(index * 7919 + 17);
            _server = server;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _socket.Blocking = false;
            Driver = _rng.NextDouble() < options.DriverFraction;
            double f = _rng.NextDouble();
            Faction = f < 0.08 ? "police" : f < 0.14 ? "army" : f < 0.18 ? "hospital" : f < 0.30 ? "gang_slum" : string.Empty;
            string claims = "dev:crew=" + (1 + _rng.Next(50)).ToString(CultureInfo.InvariantCulture) +
                (Faction.Length > 0 ? ";faction=" + Faction : string.Empty) + (_rng.NextDouble() < 0.1 ? ";vip=1" : string.Empty) +
                ";wallet=100000;phone=100";
            Conn.BeginConnect(claims, "Bot" + index.ToString(CultureInfo.InvariantCulture), _rng.NextDouble() < 0.5 ? "hi" : "en", 0);
            Conn.OnConnected = OnConnected;
            Conn.OnSnapshot = OnSnapshot;
            _nextChatMs = _rng.NextDouble() * options.ChatIntervalSeconds * 2000.0;
            _nextTalkMs = _rng.NextDouble() * 60000.0;
        }

        public void SendPacket(byte[] buffer, int length)
        {
            try
            {
                _socket.SendTo(buffer, 0, length, SocketFlags.None, _server);
            }
            catch (SocketException)
            {
            }
        }

        private void OnConnected(ConnectAcceptMessage accept)
        {
            _me.X = accept.SpawnX;
            _me.Z = accept.SpawnZ;
            _spawned = true;
            PickTarget();
        }

        private void PickTarget()
        {
            float half = _options.Area * 0.5f;
            _targetX = WorldBounds.ClampX(4096f + (float)(_rng.NextDouble() * 2 - 1) * half);
            _targetZ = WorldBounds.ClampZ(4096f + (float)(_rng.NextDouble() * 2 - 1) * half);
        }

        private void OnSnapshot(WorldSnapshotMessage s)
        {
            // Simple reconciliation: adopt the authoritative position.
            _me.X = s.SelfX;
            _me.Z = s.SelfZ;
            if (s.SelfVehicleId != 0)
            {
                if (_vehicleId != s.SelfVehicleId)
                {
                    _vehicleId = s.SelfVehicleId;
                    int model;
                    _carDef = VehicleCatalog.Get(_vehicleModels.TryGetValue(_vehicleId, out model) ? model : 5) ?? VehicleCatalog.Get(5);
                    _car.Speed = 0f;
                }
                _car.X = s.SelfX;
                _car.Z = s.SelfZ;
                _car.Yaw = s.SelfYaw;
            }
            else
            {
                _vehicleId = 0;
            }
            for (int i = 0; i < s.EntityCount; i++)
            {
                if (s.Entities[i].Kind != EntityKind.Vehicle)
                {
                    continue;
                }
                if (s.Entities[i].Spawn)
                {
                    _vehicleModels[s.Entities[i].Id] = s.Entities[i].ModelId;
                }
                if (Driver && _vehicleId == 0 && _wantVehicle == 0 && s.Entities[i].LinkedId == 0)
                {
                    float dx = s.Entities[i].X - _me.X;
                    float dz = s.Entities[i].Z - _me.Z;
                    if (dx * dx + dz * dz < 80f * 80f)
                    {
                        _wantVehicle = s.Entities[i].Id;
                        _wantX = s.Entities[i].X;
                        _wantZ = s.Entities[i].Z;
                    }
                }
            }
        }

        /// <summary>Drains the socket, runs the AI for one 50 ms step, sends input, chat and voice.</summary>
        public void Step(double nowMs)
        {
            try
            {
                // Receive (not ReceiveFrom): the socket only talks to the server, and skipping the
                // per-datagram EndPoint allocation keeps GC pauses from stalling the in-process server.
                while (_socket.Available > 0)
                {
                    int n = _socket.Receive(_buffer, 0, _buffer.Length, SocketFlags.None);
                    Conn.Receive(_buffer, 0, n, nowMs);
                }
            }
            catch (SocketException)
            {
            }

            if (Conn.State == ClientState.Connected && _spawned)
            {
                const float dt = 0.05f;
                _input.InputTick = ++_inputTick;
                _input.Buttons = InputButtons.None;
                _input.MoveX = 0f;
                _input.MoveZ = 0f;
                _input.Throttle = 0f;
                _input.Steer = 0f;
                if (_vehicleId != 0)
                {
                    if (nowMs >= _nextSteerMs)
                    {
                        _steer = (float)(_rng.NextDouble() * 2 - 1) * 0.6f;
                        _nextSteerMs = nowMs + 1000 + _rng.NextDouble() * 3000;
                    }
                    _input.Throttle = 0.8f;
                    _input.Steer = _steer;
                    Movement.StepVehicle(ref _car, _carDef, _input.Throttle, _input.Steer, false, dt);
                    _me.X = _car.X;
                    _me.Z = _car.Z;
                }
                else
                {
                    float tx = _targetX;
                    float tz = _targetZ;
                    if (_wantVehicle != 0)
                    {
                        tx = _wantX;
                        tz = _wantZ;
                    }
                    float dx = tx - _me.X;
                    float dz = tz - _me.Z;
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (_wantVehicle != 0 && d < 4f)
                    {
                        if (nowMs >= _nextEnterTry)
                        {
                            Conn.SendReliable(new EnterVehicleMessage { VehicleId = _wantVehicle });
                            _nextEnterTry = nowMs + 1000;
                            _wantVehicle = 0; // if it fails, look for another one
                        }
                    }
                    else if (d < 2f)
                    {
                        PickTarget();
                    }
                    else
                    {
                        _input.MoveX = dx / d;
                        _input.MoveZ = dz / d;
                        if ((_inputTick / 100) % 2 == 0)
                        {
                            _input.Buttons = InputButtons.Sprint;
                        }
                    }
                    Movement.StepCharacter(ref _me, _input.MoveX, _input.MoveZ, 0f, _input.Buttons == InputButtons.Sprint, dt);
                }
                _input.ClientX = _me.X;
                _input.ClientZ = _me.Z;
                Conn.SendUnreliable(_input);

                if (nowMs >= _nextChatMs)
                {
                    _nextChatMs = nowMs + _options.ChatIntervalSeconds * 1000.0 * (0.5 + _rng.NextDouble());
                    double r = _rng.NextDouble();
                    ChatChannel ch = r < 0.8 ? ChatChannel.Local : r < 0.85 ? ChatChannel.Global : r < 0.95 && Faction.Length > 0 ? ChatChannel.Faction : ChatChannel.Crew;
                    Conn.SendReliable(new ChatMessage { Channel = ch, Text = r < 0.5 ? "Namaste Mayanagri" : "चलो चाय पीते हैं" });
                    ChatSent++;
                }

                if (nowMs >= _nextTalkMs)
                {
                    _talkUntilMs = nowMs + 1000 + _rng.NextDouble() * 2000;
                    _nextTalkMs = nowMs + 30000 + _rng.NextDouble() * 60000;
                }
                if (nowMs < _talkUntilMs)
                {
                    // 20 ms Opus frames: 2.5 frames per 50 ms step.
                    _voiceCredit += 2.5;
                    while (_voiceCredit >= 1)
                    {
                        _voiceCredit -= 1;
                        _voice.Channel = Faction.Length > 0 && _rng.NextDouble() < 0.2 ? VoiceChannel.FactionRadio : VoiceChannel.Proximity;
                        _voice.Sequence = _voiceSeq++;
                        _voice.SetData(FakeOpus, 0, FakeOpus.Length);
                        Conn.SendUnreliable(_voice);
                        VoiceSent++;
                    }
                }
            }
            Conn.Update(nowMs, this);
        }

        public void Close(double nowMs)
        {
            Conn.Disconnect(nowMs, this);
            _socket.Close();
        }
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            BotOptions o;
            try
            {
                o = BotOptions.Parse(args);
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is ArgumentNullException)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine("Usage: GangsOfBharat.Bots [--bots N] [--seconds S] [--host H --port P] [--area M] [--threads T] " +
                    "[--warmup S] [--chat-interval S] [--drivers F] [--vehicles N] [--max-entities N] [--snapshot-threads N] [--server-debug]");
                return 2;
            }

            GameServer server = null;
            EndPoint target;
            if (string.IsNullOrEmpty(o.Host))
            {
                ServerConfig cfg = new ServerConfig();
                cfg.BindAddress = "127.0.0.1";
                cfg.Port = 0;
                cfg.AllowDevTokens = true;
                cfg.MaxPlayers = Math.Max(1000, o.Bots);
                cfg.SpawnRadius = o.Area * 0.5f;
                cfg.ParkedVehicles = o.Vehicles >= 0 ? o.Vehicles : Math.Max(200, o.Bots / 2);
                cfg.MaxEntitiesPerSnapshot = o.MaxEntities;
                cfg.SnapshotThreads = o.SnapshotThreads;
                cfg.LogChatToConsole = false;
                cfg.StatsIntervalSeconds = 0;
                cfg.ClientTimeoutSeconds = 30;
                ServerLog log = new ServerLog();
                log.ChatOutput = null;
                log.DebugEnabled = o.ServerDebug;
                log.Output = line =>
                {
                    if (line.Contains("\"lvl\":\"warn\"") || line.Contains("\"lvl\":\"debug\"") || line.Contains("server_started"))
                    {
                        Console.WriteLine("[server] " + line);
                    }
                };
                server = new GameServer(cfg, log);
                server.Start();
                target = new IPEndPoint(IPAddress.Loopback, server.Port);
            }
            else
            {
                IPAddress[] addresses = Dns.GetHostAddresses(o.Host);
                IPAddress ip = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ip == null)
                {
                    Console.Error.WriteLine("No IPv4 address for " + o.Host);
                    return 2;
                }
                target = new IPEndPoint(ip, o.Port);
            }

            Console.WriteLine("Starting " + o.Bots + " bots on " + o.Threads + " threads against " + target + " (area " + o.Area + " m)");
            Bot[] bots = new Bot[o.Bots];
            for (int i = 0; i < bots.Length; i++)
            {
                bots[i] = new Bot(i, target, o);
            }

            Stopwatch clock = Stopwatch.StartNew();
            Process self = Process.GetCurrentProcess();
            bool running = true;
            Thread[] workers = new Thread[o.Threads];
            long[] lateSteps = new long[o.Threads];
            for (int w = 0; w < workers.Length; w++)
            {
                int worker = w;
                workers[w] = new Thread(() =>
                {
                    double next = clock.Elapsed.TotalMilliseconds;
                    while (Volatile.Read(ref running))
                    {
                        double now = clock.Elapsed.TotalMilliseconds;
                        for (int i = worker; i < bots.Length; i += workers.Length)
                        {
                            bots[i].Step(now);
                        }
                        next += 50.0;
                        double wait = next - clock.Elapsed.TotalMilliseconds;
                        if (wait > 1)
                        {
                            Thread.Sleep((int)wait);
                        }
                        else if (wait < -250)
                        {
                            lateSteps[worker]++;
                            next = clock.Elapsed.TotalMilliseconds;
                        }
                    }
                });
                workers[w].IsBackground = true;
                workers[w].Name = "bots-" + w;
                workers[w].Start();
            }

            // Connect phase
            double connectDeadline = 30000;
            while (clock.Elapsed.TotalMilliseconds < connectDeadline && CountConnected(bots) < bots.Length)
            {
                Thread.Sleep(100);
            }
            double connectSeconds = clock.Elapsed.TotalMilliseconds / 1000.0;
            Console.WriteLine("Connected " + CountConnected(bots) + "/" + bots.Length + " in " + connectSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s; warming up");
            Thread.Sleep((int)(o.WarmupSeconds * 1000));

            // Measurement window
            long[] bytes0 = new long[bots.Length];
            long[] snapBytes0 = new long[bots.Length];
            long[] ents0 = new long[bots.Length];
            long[] ticks0 = new long[bots.Length];
            long[] lost0 = new long[bots.Length];
            long[] pkts0 = new long[bots.Length];
            for (int i = 0; i < bots.Length; i++)
            {
                bytes0[i] = bots[i].Conn.Channel.BytesReceived;
                snapBytes0[i] = bots[i].Conn.SnapshotBytesReceived;
                ents0[i] = bots[i].Conn.SnapshotEntitiesReceived;
                ticks0[i] = bots[i].Conn.SnapshotTicksReceived;
                lost0[i] = bots[i].Conn.Channel.PacketsLostEstimate;
                pkts0[i] = bots[i].Conn.Channel.PacketsReceived;
            }
            ServerMetrics m = server != null ? server.Core.Metrics : null;
            long serverBytes0 = server != null ? server.Transport.BytesSent : 0;
            long chat0 = m != null ? m.ChatDeliveries : 0;
            long voice0 = m != null ? m.VoiceFramesRelayed : 0;
            long late0 = m != null ? m.LateTicks : 0;
            if (m != null)
            {
                m.ResetTickTimes();
            }
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            TimeSpan pause0 = GC.GetTotalPauseDuration();
            TimeSpan cpu0 = self.TotalProcessorTime;
            double t0 = clock.Elapsed.TotalMilliseconds;
            Thread.Sleep(o.Seconds * 1000);
            double t1 = clock.Elapsed.TotalMilliseconds;
            self.Refresh();
            TimeSpan cpu1 = self.TotalProcessorTime;
            double seconds = (t1 - t0) / 1000.0;

            int connected = CountConnected(bots);
            long bytes = 0;
            long snapBytes = 0;
            long ents = 0;
            long ticks = 0;
            long lostDown = 0;
            long pktsDown = 0;
            long chatSent = 0;
            long voiceSent = 0;
            double rtt = 0;
            for (int i = 0; i < bots.Length; i++)
            {
                ClientConnection c = bots[i].Conn;
                bytes += c.Channel.BytesReceived - bytes0[i];
                snapBytes += c.SnapshotBytesReceived - snapBytes0[i];
                ents += c.SnapshotEntitiesReceived - ents0[i];
                ticks += c.SnapshotTicksReceived - ticks0[i];
                lostDown += c.Channel.PacketsLostEstimate - lost0[i];
                pktsDown += c.Channel.PacketsReceived - pkts0[i];
                chatSent += bots[i].ChatSent;
                voiceSent += bots[i].VoiceSent;
                rtt += c.Channel.SmoothedRttMs;
            }
            int n = Math.Max(1, connected);
            Console.WriteLine();
            Console.WriteLine("=== GangsOfBharat bot load test ===");
            Console.WriteLine("bots requested            : " + bots.Length);
            Console.WriteLine("bots connected            : " + connected);
            Console.WriteLine("drivers / walkers         : " + Count(bots, b => b.Driver) + " / " + Count(bots, b => !b.Driver));
            Console.WriteLine("in vehicles at end        : " + Count(bots, b => b.Conn.State == ClientState.Connected && b.InVehicle));
            Console.WriteLine("measured window           : " + F(seconds) + " s (after " + F(o.WarmupSeconds) + " s warm-up)");
            if (m != null)
            {
                double avg;
                double p99;
                double max;
                int samples;
                m.GetTickStats(out avg, out p99, out max, out samples);
                Console.WriteLine("server ticks              : " + samples + " (" + F(samples / seconds) + " Hz, target " + server.Core.Config.TickRate + " Hz)");
                Console.WriteLine("server tick avg / p99 / max: " + F(avg) + " / " + F(p99) + " / " + F(max) + " ms (budget " + F(server.Core.Config.TickIntervalMs) + " ms)");
                Console.WriteLine("server late ticks         : " + (m.LateTicks - late0));
                Console.WriteLine("server upload total       : " + F((server.Transport.BytesSent - serverBytes0) / seconds / 1024.0 / 1024.0 * 8) + " Mbit/s");
                Console.WriteLine("chat deliveries / s       : " + F((m.ChatDeliveries - chat0) / seconds));
                Console.WriteLine("voice frames relayed / s  : " + F((m.VoiceFramesRelayed - voice0) / seconds));
                Console.WriteLine("movement violations       : " + m.MovementViolations + ", kicked " + m.Kicked + ", timed out " + m.TimedOut);
                Console.WriteLine("server queue drops        : " + server.Transport.QueueDrops + ", send errors " + server.Transport.SendErrors);
                long lostUp = 0;
                foreach (PlayerSession s in SnapshotSessions(server))
                {
                    lostUp += s.Channel.PacketsLostEstimate;
                }
                Console.WriteLine("packets lost client->server: " + lostUp + " (whole run)");
            }
            else
            {
                Console.WriteLine("server tick avg / p99     : n/a (remote server; see its stats log)");
            }
            Console.WriteLine("avg downstream / client   : " + F(bytes / seconds / n / 1024.0) + " KB/s (all traffic incl. headers)");
            Console.WriteLine("avg snapshot bytes/client : " + F(snapBytes / seconds / n / 1024.0) + " KB/s");
            Console.WriteLine("avg entities per snapshot : " + F(ticks > 0 ? (double)ents / ticks : 0) + " (cap " + o.MaxEntities + ")");
            Console.WriteLine("snapshot rate per client  : " + F(ticks / seconds / n) + " Hz");
            Console.WriteLine("packets server->client    : " + pktsDown + ", lost " + lostDown + " (" + F(pktsDown > 0 ? 100.0 * lostDown / (pktsDown + lostDown) : 0) + "%)");
            Console.WriteLine("avg RTT (smoothed)        : " + F(rtt / bots.Length) + " ms");
            Console.WriteLine("chat sent / voice sent    : " + chatSent + " / " + voiceSent + " (whole run)");
            Console.WriteLine("process CPU (server+bots) : " + F((cpu1 - cpu0).TotalMilliseconds / (t1 - t0) * 100.0) + "% of one core (" + Environment.ProcessorCount + " cores)");
            Console.WriteLine("bot late steps            : " + Sum(lateSteps));
            Console.WriteLine("GC gen0/gen1/gen2, pause  : " + (GC.CollectionCount(0) - gc0) + "/" + (GC.CollectionCount(1) - gc1) + "/" +
                (GC.CollectionCount(2) - gc2) + ", " + F((GC.GetTotalPauseDuration() - pause0).TotalMilliseconds) + " ms total (server+bots process)");

            Volatile.Write(ref running, false);
            foreach (Thread t in workers)
            {
                t.Join(2000);
            }
            double end = clock.Elapsed.TotalMilliseconds;
            foreach (Bot b in bots)
            {
                b.Close(end);
            }
            if (server != null)
            {
                Thread.Sleep(200);
                server.Stop();
            }
            return connected == bots.Length ? 0 : 1;
        }

        private static IEnumerable<PlayerSession> SnapshotSessions(GameServer server)
        {
            // Approximate read from another thread; good enough for a report.
            PlayerSession[] copy;
            try
            {
                copy = new List<PlayerSession>(server.Core.Sessions).ToArray();
            }
            catch (InvalidOperationException)
            {
                copy = new PlayerSession[0];
            }
            return copy;
        }

        private static int CountConnected(Bot[] bots)
        {
            return Count(bots, b => b.Conn.State == ClientState.Connected);
        }

        private static int Count(Bot[] bots, Func<Bot, bool> predicate)
        {
            int n = 0;
            foreach (Bot b in bots)
            {
                if (predicate(b))
                {
                    n++;
                }
            }
            return n;
        }

        private static long Sum(long[] values)
        {
            long s = 0;
            foreach (long v in values)
            {
                s += v;
            }
            return s;
        }

        private static string F(double v)
        {
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
