using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GangsOfBharat.Shared.Net;

namespace GangsOfBharat.Server
{
    public struct Datagram
    {
        public EndPoint From;
        public byte[] Buffer;
        public int Length;
    }

    /// <summary>
    /// One UDP socket. A dedicated thread blocks on receive and queues datagrams for the
    /// simulation thread; sends happen directly from the caller (Socket.SendTo is thread-safe).
    /// </summary>
    public sealed class UdpTransport : ITransport, IDisposable
    {
        private const int MaxQueuedDatagrams = 200000;

        private readonly Socket _socket;
        private readonly ConcurrentQueue<Datagram> _queue = new ConcurrentQueue<Datagram>();
        private readonly ConcurrentBag<byte[]> _pool = new ConcurrentBag<byte[]>();
        private Thread _receiveThread;
        private volatile bool _running;

        public long ReceiveErrors;
        public long SendErrors;
        public long QueueDrops;
        public long BytesSent;
        public long PacketsSent;

        public UdpTransport(string bindAddress, int port)
        {
            IPAddress address = IPAddress.Parse(bindAddress);
            _socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                _socket.ReceiveBufferSize = 8 * 1024 * 1024;
                _socket.SendBufferSize = 8 * 1024 * 1024;
            }
            catch (SocketException)
            {
                // The OS may cap buffer sizes; the defaults still work.
            }
            _socket.Bind(new IPEndPoint(address, port));
        }

        public int LocalPort
        {
            get { return ((IPEndPoint)_socket.LocalEndPoint).Port; }
        }

        public int QueuedCount
        {
            get { return _queue.Count; }
        }

        public void Start()
        {
            _running = true;
            _receiveThread = new Thread(ReceiveLoop);
            _receiveThread.Name = "gob-udp-recv";
            _receiveThread.IsBackground = true;
            _receiveThread.Start();
        }

        private byte[] Rent()
        {
            byte[] buffer;
            return _pool.TryTake(out buffer) ? buffer : new byte[ProtocolConstants.MaxReceiveBytes];
        }

        public void Return(byte[] buffer)
        {
            if (buffer != null && buffer.Length == ProtocolConstants.MaxReceiveBytes)
            {
                _pool.Add(buffer);
            }
        }

        private void ReceiveLoop()
        {
            EndPoint any = new IPEndPoint(_socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
            while (_running)
            {
                byte[] buffer = Rent();
                try
                {
                    EndPoint from = any;
                    int n = _socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref from);
                    if (n <= 0)
                    {
                        Return(buffer);
                        continue;
                    }
                    if (_queue.Count >= MaxQueuedDatagrams)
                    {
                        Interlocked.Increment(ref QueueDrops);
                        Return(buffer);
                        continue;
                    }
                    Datagram d = new Datagram();
                    d.From = from;
                    d.Buffer = buffer;
                    d.Length = n;
                    _queue.Enqueue(d);
                }
                catch (SocketException)
                {
                    Return(buffer);
                    if (!_running)
                    {
                        break;
                    }
                    Interlocked.Increment(ref ReceiveErrors);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        public bool TryDequeue(out Datagram datagram)
        {
            return _queue.TryDequeue(out datagram);
        }

        public void Send(EndPoint to, byte[] data, int length)
        {
            try
            {
                _socket.SendTo(data, 0, length, SocketFlags.None, to);
                Interlocked.Increment(ref PacketsSent);
                Interlocked.Add(ref BytesSent, length);
            }
            catch (SocketException)
            {
                Interlocked.Increment(ref SendErrors);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                _socket.Close();
            }
            catch (SocketException)
            {
            }
            if (_receiveThread != null)
            {
                _receiveThread.Join(1000);
            }
        }
    }
}
