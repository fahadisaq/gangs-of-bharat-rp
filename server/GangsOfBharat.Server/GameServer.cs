using System;
using System.Diagnostics;
using System.Threading;

namespace GangsOfBharat.Server
{
    /// <summary>
    /// Runs ServerCore on a dedicated fixed-rate thread (20 Hz by default) fed by a UdpTransport.
    /// Each tick drains received datagrams, simulates, then sends snapshots.
    /// </summary>
    public sealed class GameServer : IDisposable
    {
        private readonly ServerConfig _config;
        private readonly ServerLog _log;
        private readonly UdpTransport _transport;
        private readonly ServerCore _core;
        private readonly Stopwatch _clock = new Stopwatch();
        private Thread _tickThread;
        private volatile bool _running;
        private volatile int _online;

        public GameServer(ServerConfig config, ServerLog log)
        {
            _config = config;
            _log = log ?? new ServerLog();
            _transport = new UdpTransport(config.BindAddress, config.Port);
            _core = new ServerCore(config, _transport, _log);
        }

        public ServerCore Core
        {
            get { return _core; }
        }

        public UdpTransport Transport
        {
            get { return _transport; }
        }

        public int Port
        {
            get { return _transport.LocalPort; }
        }

        /// <summary>Players online as of the last tick (safe to read from any thread).</summary>
        public int OnlineCount
        {
            get { return _online; }
        }

        public void Start()
        {
            _transport.Start();
            _running = true;
            _clock.Start();
            _tickThread = new Thread(TickLoop);
            _tickThread.Name = "gob-sim";
            _tickThread.IsBackground = true;
            _tickThread.Priority = ThreadPriority.AboveNormal;
            _tickThread.Start();
            _log.Info("server_started", "port", Port, "tick_rate", _config.TickRate, "max_players", _config.MaxPlayers,
                "vehicles", _config.ParkedVehicles, "max_entities", _config.MaxEntitiesPerSnapshot,
                "snapshot_threads", _config.SnapshotThreads, "dev_tokens", _config.AllowDevTokens);
            if (_config.AllowDevTokens)
            {
                _log.Warn("dev_tokens_enabled", "note", "dev: tokens grant faction/VIP/admin flags; never use on a public server");
            }
        }

        private void TickLoop()
        {
            double interval = _config.TickIntervalMs;
            double next = _clock.Elapsed.TotalMilliseconds;
            double nextStats = next + _config.StatsIntervalSeconds * 1000.0;
            while (_running)
            {
                double now = _clock.Elapsed.TotalMilliseconds;
                double wait = next - now;
                if (wait > 0)
                {
                    if (wait > 1.5)
                    {
                        Thread.Sleep((int)(wait - 0.5));
                    }
                    else
                    {
                        Thread.Sleep(0);
                    }
                    continue;
                }

                double start = now;
                Datagram d;
                int budget = 200000;
                while (budget-- > 0 && _transport.TryDequeue(out d))
                {
                    _core.HandlePacket(d.From, d.Buffer, 0, d.Length, now);
                    _transport.Return(d.Buffer);
                }
                _core.Tick(now);
                _online = _core.Sessions.Count;
                double elapsed = _clock.Elapsed.TotalMilliseconds - start;
                _core.Metrics.RecordTick(elapsed);
                if (elapsed > interval)
                {
                    _core.Metrics.LateTicks++;
                }

                next += interval;
                double after = _clock.Elapsed.TotalMilliseconds;
                if (after - next > interval * 5)
                {
                    next = after + interval; // fell far behind: skip instead of bursting
                }

                if (_config.StatsIntervalSeconds > 0 && after >= nextStats)
                {
                    nextStats = after + _config.StatsIntervalSeconds * 1000.0;
                    LogStats();
                }
            }
        }

        private void LogStats()
        {
            double avg;
            double p99;
            double max;
            int samples;
            _core.Metrics.GetTickStats(out avg, out p99, out max, out samples);
            ServerMetrics m = _core.Metrics;
            _log.Info("stats", "online", _online, "tick_avg_ms", avg, "tick_p99_ms", p99, "tick_max_ms", max,
                "late_ticks", m.LateTicks, "packets_in", m.PacketsReceived, "packets_out", _transport.PacketsSent,
                "bytes_out", _transport.BytesSent, "malformed", m.MalformedPackets, "chat", m.ChatRouted,
                "voice_relayed", m.VoiceFramesRelayed, "violations", m.MovementViolations, "queue_drops", _transport.QueueDrops);
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }
            _running = false;
            if (_tickThread != null)
            {
                _tickThread.Join(2000);
            }
            _core.Shutdown(_clock.Elapsed.TotalMilliseconds);
            _transport.Dispose();
            _log.Info("server_stopped");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
