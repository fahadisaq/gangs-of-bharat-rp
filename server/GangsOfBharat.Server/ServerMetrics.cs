using System;
using System.Threading;

namespace GangsOfBharat.Server
{
    /// <summary>Counters and tick timing. Counters are written by the simulation thread only.</summary>
    public sealed class ServerMetrics
    {
        private readonly object _lock = new object();
        private double[] _tickMs = new double[1 << 16];
        private int _tickCount;
        private int _tickNext;

        public long Ticks;
        public long PacketsReceived;
        public long MalformedPackets;
        public long ConnectAttempts;
        public long ConnectsRejected;
        public long Kicked;
        public long TimedOut;
        public long ChatRouted;
        public long ChatDeliveries;
        public long ChatRejected;
        public long VoiceFramesIn;
        public long VoiceFramesRelayed;
        public long VoiceFramesDropped;
        public long MovementViolations;
        public long JobsCompleted;
        public long Snapshots;
        public long SnapshotEntities;
        public long LateTicks;

        public void RecordTick(double ms)
        {
            lock (_lock)
            {
                _tickMs[_tickNext] = ms;
                _tickNext = (_tickNext + 1) & (_tickMs.Length - 1);
                if (_tickCount < _tickMs.Length)
                {
                    _tickCount++;
                }
            }
            Interlocked.Increment(ref Ticks);
        }

        /// <summary>Clears tick timings (used after a warm-up period).</summary>
        public void ResetTickTimes()
        {
            lock (_lock)
            {
                _tickCount = 0;
                _tickNext = 0;
            }
        }

        public void GetTickStats(out double avg, out double p99, out double max, out int samples)
        {
            double[] copy;
            lock (_lock)
            {
                samples = _tickCount;
                copy = new double[_tickCount];
                int start = (_tickNext - _tickCount) & (_tickMs.Length - 1);
                for (int i = 0; i < _tickCount; i++)
                {
                    copy[i] = _tickMs[(start + i) & (_tickMs.Length - 1)];
                }
            }
            if (copy.Length == 0)
            {
                avg = 0;
                p99 = 0;
                max = 0;
                return;
            }
            double sum = 0;
            for (int i = 0; i < copy.Length; i++)
            {
                sum += copy[i];
            }
            Array.Sort(copy);
            avg = sum / copy.Length;
            int idx = (int)Math.Ceiling(copy.Length * 0.99) - 1;
            p99 = copy[Math.Max(0, Math.Min(copy.Length - 1, idx))];
            max = copy[copy.Length - 1];
        }
    }
}
