using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GangsOfBharat.Server;
using GangsOfBharat.Shared.Net;

namespace GangsOfBharat.Tests
{
    /// <summary>Starts the real server (UDP sockets, tick thread) on an ephemeral localhost port.</summary>
    public static class IntegrationTests
    {
        private sealed class UdpTestClient : IPacketSink, IDisposable
        {
            private readonly Socket _socket;
            private readonly EndPoint _server;
            private readonly byte[] _buffer = new byte[ProtocolConstants.MaxReceiveBytes];
            public readonly ClientConnection Conn = new ClientConnection();
            public readonly Dictionary<int, EntityState> Known = new Dictionary<int, EntityState>();
            public readonly List<string> LocalChat = new List<string>();

            public UdpTestClient(int port)
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                _socket.Blocking = false;
                _server = new IPEndPoint(IPAddress.Loopback, port);
                Conn.OnSnapshot = s =>
                {
                    for (int i = 0; i < s.EntityCount; i++)
                    {
                        Known[s.Entities[i].Id] = s.Entities[i];
                    }
                };
                Conn.OnChat = c =>
                {
                    if (c.Channel == ChatChannel.Local)
                    {
                        LocalChat.Add(c.SenderId + ":" + c.Text);
                    }
                };
            }

            public void SendPacket(byte[] buffer, int length)
            {
                _socket.SendTo(buffer, 0, length, SocketFlags.None, _server);
            }

            public void Poll(double now)
            {
                while (_socket.Available > 0)
                {
                    EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    int n = _socket.ReceiveFrom(_buffer, ref from);
                    Conn.Receive(_buffer, 0, n, now);
                }
                Conn.Update(now, this);
            }

            public void Dispose()
            {
                _socket.Close();
            }
        }

        private static bool RunUntil(Func<bool> done, List<UdpTestClient> clients, Stopwatch clock, double timeoutMs, Action<double> perTick = null)
        {
            double end = clock.Elapsed.TotalMilliseconds + timeoutMs;
            while (clock.Elapsed.TotalMilliseconds < end)
            {
                double now = clock.Elapsed.TotalMilliseconds;
                if (perTick != null)
                {
                    perTick(now);
                }
                foreach (UdpTestClient c in clients)
                {
                    c.Poll(now);
                }
                if (done())
                {
                    return true;
                }
                Thread.Sleep(10);
            }
            return false;
        }

        [Test]
        public static void ClientsSeeEachOtherMoveAndChatOverRealUdp()
        {
            ServerConfig cfg = new ServerConfig();
            cfg.Port = 0;
            cfg.BindAddress = "127.0.0.1";
            cfg.AllowDevTokens = true;
            cfg.StatsIntervalSeconds = 0;
            cfg.ParkedVehicles = 10;
            cfg.SpawnRadius = 5f;
            cfg.LogChatToConsole = false;
            using (GameServer server = new GameServer(cfg, ServerLog.Silent()))
            {
                server.Start();
                List<UdpTestClient> clients = new List<UdpTestClient>();
                try
                {
                    Stopwatch clock = Stopwatch.StartNew();
                    for (int i = 0; i < 3; i++)
                    {
                        UdpTestClient c = new UdpTestClient(server.Port);
                        c.Conn.BeginConnect("dev:", "Udp" + i, "en", 0);
                        clients.Add(c);
                    }
                    UdpTestClient mover = clients[0];
                    UdpTestClient watcher = clients[1];
                    UdpTestClient third = clients[2];
                    Assert.True(RunUntil(() => clients.TrueForAll(c => c.Conn.State == ClientState.Connected), clients, clock, 5000), "all connected");
                    Assert.True(RunUntil(() => watcher.Known.ContainsKey(mover.Conn.PlayerId) && third.Known.ContainsKey(mover.Conn.PlayerId), clients, clock, 3000),
                        "others see the mover in snapshots");
                    float startX = watcher.Known[mover.Conn.PlayerId].X;

                    // Walk +X for about a second, sending input at ~50 Hz polling with client prediction.
                    uint tick = 0;
                    float px = mover.Conn.Accept.SpawnX;
                    float pz = mover.Conn.Accept.SpawnZ;
                    double lastInput = clock.Elapsed.TotalMilliseconds;
                    RunUntil(() => false, clients, clock, 1200, now =>
                    {
                        if (now - lastInput >= 50)
                        {
                            px += 3.5f * 0.05f;
                            lastInput = now;
                            mover.Conn.SendUnreliable(new PlayerInputMessage { InputTick = ++tick, MoveX = 1f, ClientX = px, ClientZ = pz });
                        }
                    });
                    Assert.True(RunUntil(() => watcher.Known[mover.Conn.PlayerId].X > startX + 2f, clients, clock, 2000),
                        "watcher saw the mover walk (start " + startX + ", now " + watcher.Known[mover.Conn.PlayerId].X + ")");

                    mover.Conn.SendReliable(new ChatMessage { Channel = ChatChannel.Local, Text = "UDP namaste" });
                    string expected = mover.Conn.PlayerId + ":UDP namaste";
                    Assert.True(RunUntil(() => watcher.LocalChat.Contains(expected) && third.LocalChat.Contains(expected), clients, clock, 3000),
                        "local chat delivered over UDP");
                    Assert.Equal(3, server.OnlineCount);
                    Assert.Equal(0L, server.Core.Metrics.MovementViolations, "honest client not flagged");

                    third.Conn.Disconnect(clock.Elapsed.TotalMilliseconds, third);
                    Assert.True(RunUntil(() => server.OnlineCount == 2, clients, clock, 3000), "disconnect processed");
                }
                finally
                {
                    foreach (UdpTestClient c in clients)
                    {
                        c.Dispose();
                    }
                    server.Stop();
                }
            }
        }

        [Test]
        public static void ConfigReadsArgsAndEnvironment()
        {
            Dictionary<string, string> env = new Dictionary<string, string> { { "GOB_PORT", "9000" }, { "GOB_MAX_PLAYERS", "500" } };
            ServerConfig c = ServerConfig.Load(new[] { "--max-players", "800", "--allow-dev-tokens", "--slow-mode=5" },
                k => env.ContainsKey(k) ? env[k] : null);
            Assert.Equal(9000, c.Port, "env var");
            Assert.Equal(800, c.MaxPlayers, "args override env");
            Assert.True(c.AllowDevTokens);
            Assert.Near(5, c.GlobalSlowModeSeconds, 1e-9);
            ServerConfig d = ServerConfig.Load(new string[0], k => null);
            Assert.Equal(7777, d.Port, "default port");
            Assert.Equal(1000, d.MaxPlayers, "default max players");
            Assert.False(d.AllowDevTokens, "dev tokens off by default");
            bool threw = false;
            try
            {
                ServerConfig.Load(new[] { "--bogus", "1" }, k => null);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Assert.True(threw, "unknown option rejected");
        }
    }
}
