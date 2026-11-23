using System.Collections.Generic;
using System.Net;
using GangsOfBharat.Server;
using GangsOfBharat.Shared.Net;

namespace GangsOfBharat.Tests
{
    public sealed class ReceivedChat
    {
        public ChatChannel Channel;
        public int SenderId;
        public int TargetId;
        public string Text;
    }

    /// <summary>In-memory client wired straight into a ServerCore (no sockets).</summary>
    public sealed class TestClient : IPacketSink
    {
        private readonly Harness _harness;
        public readonly EndPoint EndPoint;
        public readonly ClientConnection Conn = new ClientConnection();
        public readonly List<byte[]> Inbox = new List<byte[]>();
        public readonly List<ReceivedChat> Chats = new List<ReceivedChat>();
        public readonly List<int> VoiceSenders = new List<int>();
        public readonly List<NetMessage> Others = new List<NetMessage>();
        public readonly Dictionary<uint, int> EntitiesPerTick = new Dictionary<uint, int>();
        public readonly Dictionary<int, EntityState> Known = new Dictionary<int, EntityState>();

        public TestClient(Harness harness, int port)
        {
            _harness = harness;
            EndPoint = new IPEndPoint(IPAddress.Loopback, port);
            Conn.OnChat = c => Chats.Add(new ReceivedChat { Channel = c.Channel, SenderId = c.SenderId, TargetId = c.TargetId, Text = c.Text });
            Conn.OnVoice = v => VoiceSenders.Add(v.SenderId);
            Conn.OnOther = m => Others.Add(m);
            Conn.OnSnapshot = s =>
            {
                int n;
                EntitiesPerTick.TryGetValue(s.ServerTick, out n);
                EntitiesPerTick[s.ServerTick] = n + s.EntityCount;
                for (int i = 0; i < s.EntityCount; i++)
                {
                    Known[s.Entities[i].Id] = s.Entities[i];
                }
                for (int i = 0; i < s.DespawnCount; i++)
                {
                    Known.Remove(s.Despawns[i]);
                }
            };
        }

        public int Id
        {
            get { return Conn.PlayerId; }
        }

        public PlayerSession Session
        {
            get { return _harness.Core.GetSession(Conn.PlayerId); }
        }

        public void SendPacket(byte[] buffer, int length)
        {
            byte[] copy = new byte[length];
            System.Buffer.BlockCopy(buffer, 0, copy, 0, length);
            _harness.Core.HandlePacket(EndPoint, copy, 0, length, _harness.Now);
        }

        public void Pump()
        {
            List<byte[]> packets;
            lock (Inbox)
            {
                packets = new List<byte[]>(Inbox);
                Inbox.Clear();
            }
            foreach (byte[] p in packets)
            {
                Conn.Receive(p, 0, p.Length, _harness.Now);
            }
        }

        public void Say(ChatChannel channel, string text, int target = 0)
        {
            Conn.SendReliable(new ChatMessage { Channel = channel, Text = text, TargetId = target });
        }

        public bool Heard(int senderId, ChatChannel channel, string text)
        {
            return Chats.Exists(c => c.SenderId == senderId && c.Channel == channel && c.Text == text);
        }

        public bool GotSystem(string textFragment)
        {
            return Chats.Exists(c => c.Channel == ChatChannel.System && c.Text.Contains(textFragment));
        }
    }

    /// <summary>Drives a ServerCore with in-memory clients and a manual clock (50 ms per step).</summary>
    public sealed class Harness : ITransport
    {
        private readonly Dictionary<EndPoint, TestClient> _clients = new Dictionary<EndPoint, TestClient>();
        private int _nextPort = 40000;
        public readonly ServerCore Core;
        public double Now = 1000.0;

        public Harness(ServerConfig config = null)
        {
            if (config == null)
            {
                config = new ServerConfig();
            }
            config.AllowDevTokens = true;
            config.LogChatToConsole = false;
            config.SnapshotThreads = 1;
            Core = new ServerCore(config, this, ServerLog.Silent());
        }

        public static ServerConfig SmallWorld()
        {
            ServerConfig c = new ServerConfig();
            c.ParkedVehicles = 4;
            c.SpawnRadius = 5f;
            return c;
        }

        public void Send(EndPoint to, byte[] data, int length)
        {
            TestClient c;
            lock (_clients)
            {
                if (!_clients.TryGetValue(to, out c))
                {
                    return;
                }
            }
            byte[] copy = new byte[length];
            System.Buffer.BlockCopy(data, 0, copy, 0, length);
            lock (c.Inbox)
            {
                c.Inbox.Add(copy);
            }
        }

        public TestClient Connect(string name, string claims = "", string language = "en")
        {
            TestClient c = new TestClient(this, _nextPort++);
            lock (_clients)
            {
                _clients[c.EndPoint] = c;
            }
            c.Conn.BeginConnect("dev:" + claims, name, language, Now);
            for (int i = 0; i < 10 && c.Conn.State != ClientState.Connected; i++)
            {
                Step();
            }
            Assert.Equal(ClientState.Connected, c.Conn.State, "client " + name + " connected");
            return c;
        }

        public void Step(int ticks = 1)
        {
            for (int t = 0; t < ticks; t++)
            {
                List<TestClient> clients;
                lock (_clients)
                {
                    clients = new List<TestClient>(_clients.Values);
                }
                foreach (TestClient c in clients)
                {
                    c.Conn.Update(Now, c);
                }
                Core.Tick(Now);
                foreach (TestClient c in clients)
                {
                    c.Pump();
                }
                Now += 50.0;
            }
        }
    }
}
