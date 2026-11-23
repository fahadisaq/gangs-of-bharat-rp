using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GangsOfBharat.Shared.Net;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// UDP connection to the authoritative game server. A background thread receives datagrams
    /// and queues them; Update drains the queue on the main thread and feeds the shared
    /// ClientConnection, so every callback (snapshot, chat, voice) runs on the Unity main thread.
    /// UNVERIFIED: written against Unity 6 APIs, not compiled in the sandbox.
    /// </summary>
    public sealed class NetClient : MonoBehaviour, IPacketSink
    {
        public static NetClient Instance { get; private set; }

        [Header("Server (override with -server host:port on the command line)")]
        [SerializeField] private string serverHost = "127.0.0.1";
        [SerializeField] private int serverPort = 7777;
        [SerializeField] private string playerName = "Player";
        [Tooltip("Client send rate. Matches the server simulation rate (20 Hz).")]
        [SerializeField] private int sendRateHz = 20;

        private readonly ClientConnection _connection = new ClientConnection();
        private struct Datagram
        {
            public readonly byte[] Buffer;
            public readonly int Length;

            public Datagram(byte[] buffer, int length)
            {
                Buffer = buffer;
                Length = length;
            }
        }

        private readonly ConcurrentQueue<Datagram> _incoming = new ConcurrentQueue<Datagram>();
        private readonly ConcurrentBag<byte[]> _bufferPool = new ConcurrentBag<byte[]>();
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private Socket _socket;
        private Thread _receiveThread;
        private volatile bool _running;
        private double _sendAccumulatorMs;
        private double _lastFrameMs;

        public ClientConnection Connection { get { return _connection; } }
        public ClientState State { get { return _connection.State; } }
        public int PlayerId { get { return _connection.PlayerId; } }
        public int TickRate { get { return _connection.Accept.TickRate > 0 ? _connection.Accept.TickRate : 20; } }
        public double NowMs { get { return _clock.Elapsed.TotalMilliseconds; } }

        /// <summary>Fired once per client tick (sendRateHz) before queued messages are flushed.</summary>
        public event Action<float> ClientTick;
        public event Action<ConnectAcceptMessage> Connected;
        public event Action<WorldSnapshotMessage> SnapshotReceived;
        public event Action<ChatMessage> ChatReceived;
        public event Action<VoiceFrameMessage> VoiceReceived;
        public event Action<NetMessage> OtherReceived;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            ApplyCommandLine();

            _connection.OnConnected = a => { if (Connected != null) Connected(a); };
            _connection.OnSnapshot = s => { if (SnapshotReceived != null) SnapshotReceived(s); };
            _connection.OnChat = c => { if (ChatReceived != null) ChatReceived(c); };
            _connection.OnVoice = v => { if (VoiceReceived != null) VoiceReceived(v); };
            _connection.OnOther = m => { if (OtherReceived != null) OtherReceived(m); };
        }

        private void ApplyCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-server")
                {
                    string[] parts = args[i + 1].Split(':');
                    serverHost = parts[0];
                    int port;
                    if (parts.Length > 1 && int.TryParse(parts[1], out port))
                    {
                        serverPort = port;
                    }
                }
            }
        }

        /// <summary>Runs an action on the main thread during the next Update.</summary>
        public void RunOnMainThread(Action action)
        {
            if (action != null)
            {
                _mainThread.Enqueue(action);
            }
        }

        /// <summary>
        /// Opens the socket and starts the handshake. sessionToken comes from FirebaseBootstrap
        /// (a Firebase ID token or, until the server verifies tokens, a development token).
        /// </summary>
        public void Connect(string sessionToken, string displayName)
        {
            Disconnect();
            if (!string.IsNullOrEmpty(displayName))
            {
                playerName = displayName;
            }
            IPAddress address = ResolveHost(serverHost);
            if (address == null)
            {
                Debug.LogError("NetClient: cannot resolve server host " + serverHost);
                return;
            }
            _socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _socket.Connect(new IPEndPoint(address, serverPort));
            _running = true;
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "GOB-NetReceive" };
            _receiveThread.Start();
            _connection.BeginConnect(sessionToken, playerName, LanguageSettings.Current, NowMs);
            _lastFrameMs = NowMs;
        }

        private static IPAddress ResolveHost(string host)
        {
            IPAddress address;
            if (IPAddress.TryParse(host, out address))
            {
                return address;
            }
            try
            {
                IPAddress[] all = Dns.GetHostAddresses(host);
                return all.Length > 0 ? all[0] : null;
            }
            catch (SocketException)
            {
                return null;
            }
        }

        public void Disconnect()
        {
            if (_socket != null)
            {
                _connection.Disconnect(NowMs, this);
            }
            _running = false;
            if (_socket != null)
            {
                _socket.Close();
                _socket = null;
            }
            if (_receiveThread != null)
            {
                _receiveThread.Join(200);
                _receiveThread = null;
            }
        }

        public void SendReliable(NetMessage message)
        {
            if (!_connection.SendReliable(message))
            {
                Debug.LogWarning("NetClient: reliable queue full, dropped " + message.Type);
            }
        }

        public void SendUnreliable(NetMessage message)
        {
            _connection.SendUnreliable(message);
        }

        private void ReceiveLoop()
        {
            byte[] scratch = new byte[ProtocolConstants.MaxReceiveBytes];
            while (_running)
            {
                try
                {
                    int n = _socket.Receive(scratch);
                    if (n <= 0)
                    {
                        continue;
                    }
                    byte[] copy;
                    if (!_bufferPool.TryTake(out copy))
                    {
                        copy = new byte[ProtocolConstants.MaxReceiveBytes];
                    }
                    Buffer.BlockCopy(scratch, 0, copy, 0, n);
                    _incoming.Enqueue(new Datagram(copy, n));
                }
                catch (SocketException)
                {
                    // ICMP port unreachable while the server is down, or socket closed.
                    if (!_running)
                    {
                        return;
                    }
                    Thread.Sleep(50);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void Update()
        {
            Action action;
            while (_mainThread.TryDequeue(out action))
            {
                action();
            }

            double now = NowMs;
            Datagram packet;
            while (_incoming.TryDequeue(out packet))
            {
                _connection.Receive(packet.Buffer, 0, packet.Length, now);
                _bufferPool.Add(packet.Buffer);
            }

            if (_socket == null)
            {
                return;
            }
            double interval = 1000.0 / Mathf.Max(1, sendRateHz);
            _sendAccumulatorMs += now - _lastFrameMs;
            _lastFrameMs = now;
            if (_sendAccumulatorMs > interval * 5)
            {
                _sendAccumulatorMs = interval; // after a hitch, do not burst-send
            }
            while (_sendAccumulatorMs >= interval)
            {
                _sendAccumulatorMs -= interval;
                if (_connection.State == ClientState.Connected && ClientTick != null)
                {
                    ClientTick((float)(interval / 1000.0));
                }
                _connection.Update(now, this);
            }
        }

        void IPacketSink.SendPacket(byte[] buffer, int length)
        {
            Socket socket = _socket;
            if (socket == null)
            {
                return;
            }
            try
            {
                socket.Send(buffer, 0, length, SocketFlags.None);
            }
            catch (SocketException e)
            {
                Debug.LogWarning("NetClient: send failed: " + e.SocketErrorCode);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            // Mobile OSes suspend sockets in the background; the server times the session out and
            // the player reconnects on resume (TODO: session resume token).
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Disconnect();
                Instance = null;
            }
        }
    }
}
