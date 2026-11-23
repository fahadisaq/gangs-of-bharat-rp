using System;
using System.Collections.Generic;

namespace GangsOfBharat.Shared.World
{
    public enum InterestTier : byte
    {
        None = 0,
        /// <summary>Sent every tick (20 Hz).</summary>
        Near = 1,
        /// <summary>Sent every MidDivisor ticks (10 Hz by default).</summary>
        Mid = 2,
        /// <summary>Sent every FarDivisor ticks (5 Hz by default).</summary>
        Far = 3,
    }

    public sealed class InterestSettings
    {
        public float NearRadius = 60f;
        public float MidRadius = 150f;
        public float FarRadius = 300f;
        public int NearDivisor = 1;
        public int MidDivisor = 2;
        public int FarDivisor = 4;
        /// <summary>Each client gets at most this many entities (the nearest ones).</summary>
        public int MaxEntitiesPerSnapshot = 100;
    }

    /// <summary>Relevance state of one client. Owned by the server, reused every tick.</summary>
    public sealed class ClientInterest
    {
        internal int[] Stamp = new int[0];
        internal int Generation = 1;

        public readonly List<int> Relevant = new List<int>(128);
        public readonly List<InterestTier> Tiers = new List<InterestTier>(128);
        public readonly List<float> DistancesSq = new List<float>(128);
        /// <summary>Ids that joined the relevance set in the last Update (send as spawns).</summary>
        public readonly List<int> Entered = new List<int>(32);
        /// <summary>Ids that left the relevance set in the last Update (send as despawns).</summary>
        public readonly List<int> Left = new List<int>(32);

        internal readonly List<int> Previous = new List<int>(128);

        public bool IsRelevant(int id)
        {
            return id >= 0 && id < Stamp.Length && Stamp[id] == Generation;
        }

        /// <summary>Forgets the current set (for example after a teleport by an admin).</summary>
        public void Clear()
        {
            Relevant.Clear();
            Tiers.Clear();
            DistancesSq.Clear();
            Entered.Clear();
            Left.Clear();
            Previous.Clear();
            Generation += 2;
        }
    }

    /// <summary>
    /// Picks the entities each client should hear about: everything inside FarRadius, capped
    /// to the MaxEntitiesPerSnapshot nearest, tagged with a distance tier that sets how often the
    /// entity is included in snapshots. Not thread-safe (shared scratch buffers).
    /// </summary>
    public sealed class InterestManager
    {
        private readonly SpatialHashGrid _grid;
        private readonly InterestSettings _settings;
        private readonly List<int> _candidates = new List<int>(512);
        private int[] _ids = new int[512];
        private float[] _dist = new float[512];

        public InterestManager(SpatialHashGrid grid, InterestSettings settings)
        {
            _grid = grid;
            _settings = settings ?? new InterestSettings();
        }

        public InterestSettings Settings
        {
            get { return _settings; }
        }

        public InterestTier TierForDistanceSq(float distanceSq)
        {
            float near = _settings.NearRadius;
            float mid = _settings.MidRadius;
            float far = _settings.FarRadius;
            if (distanceSq <= near * near)
            {
                return InterestTier.Near;
            }
            if (distanceSq <= mid * mid)
            {
                return InterestTier.Mid;
            }
            if (distanceSq <= far * far)
            {
                return InterestTier.Far;
            }
            return InterestTier.None;
        }

        public int DivisorFor(InterestTier tier)
        {
            switch (tier)
            {
                case InterestTier.Near: return Math.Max(1, _settings.NearDivisor);
                case InterestTier.Mid: return Math.Max(1, _settings.MidDivisor);
                case InterestTier.Far: return Math.Max(1, _settings.FarDivisor);
                default: return int.MaxValue;
            }
        }

        /// <summary>
        /// Whether an entity in this tier goes into the snapshot on this tick. Entities are
        /// staggered by id so far updates spread evenly over ticks.
        /// </summary>
        public bool ShouldSend(InterestTier tier, uint tick, int entityId)
        {
            if (tier == InterestTier.None)
            {
                return false;
            }
            int divisor = DivisorFor(tier);
            if (divisor <= 1)
            {
                return true;
            }
            return (tick + (uint)entityId) % (uint)divisor == 0;
        }

        /// <summary>Recomputes the client's relevance set around (x, z), excluding selfId.</summary>
        public void Update(ClientInterest client, int selfId, float x, float z)
        {
            _grid.QueryRadius(x, z, _settings.FarRadius, _candidates);
            int n = 0;
            if (_ids.Length < _candidates.Count)
            {
                int size = _ids.Length;
                while (size < _candidates.Count)
                {
                    size *= 2;
                }
                _ids = new int[size];
                _dist = new float[size];
            }
            for (int i = 0; i < _candidates.Count; i++)
            {
                int id = _candidates[i];
                if (id == selfId)
                {
                    continue;
                }
                float ex;
                float ez;
                _grid.TryGetPosition(id, out ex, out ez);
                float dx = ex - x;
                float dz = ez - z;
                _ids[n] = id;
                _dist[n] = dx * dx + dz * dz;
                n++;
            }

            int cap = _settings.MaxEntitiesPerSnapshot;
            if (cap < 0)
            {
                cap = 0;
            }
            if (n > cap)
            {
                SelectNearest(0, n - 1, cap);
                n = cap;
            }

            // Swap current -> previous.
            client.Previous.Clear();
            client.Previous.AddRange(client.Relevant);
            client.Relevant.Clear();
            client.Tiers.Clear();
            client.DistancesSq.Clear();
            client.Entered.Clear();
            client.Left.Clear();

            int previousGen = client.Generation;
            int gen = previousGen + 1;
            for (int i = 0; i < n; i++)
            {
                int id = _ids[i];
                EnsureStamp(client, id);
                if (client.Stamp[id] != previousGen)
                {
                    client.Entered.Add(id);
                }
                client.Stamp[id] = gen;
                client.Relevant.Add(id);
                client.DistancesSq.Add(_dist[i]);
                client.Tiers.Add(TierForDistanceSq(_dist[i]));
            }
            for (int i = 0; i < client.Previous.Count; i++)
            {
                int id = client.Previous[i];
                if (client.Stamp[id] != gen)
                {
                    client.Left.Add(id);
                }
            }
            client.Generation = gen;
        }

        private static void EnsureStamp(ClientInterest client, int id)
        {
            if (id < client.Stamp.Length)
            {
                return;
            }
            int size = Math.Max(64, client.Stamp.Length);
            while (size <= id)
            {
                size *= 2;
            }
            Array.Resize(ref client.Stamp, size);
        }

        /// <summary>Quickselect: afterwards the k smallest distances are in [lo, lo + k).</summary>
        private void SelectNearest(int lo, int hi, int k)
        {
            int target = lo + k - 1;
            if (k <= 0)
            {
                return;
            }
            while (lo < hi)
            {
                float pivot = _dist[(lo + hi) >> 1];
                int i = lo;
                int j = hi;
                while (i <= j)
                {
                    while (_dist[i] < pivot)
                    {
                        i++;
                    }
                    while (_dist[j] > pivot)
                    {
                        j--;
                    }
                    if (i <= j)
                    {
                        float td = _dist[i];
                        _dist[i] = _dist[j];
                        _dist[j] = td;
                        int ti = _ids[i];
                        _ids[i] = _ids[j];
                        _ids[j] = ti;
                        i++;
                        j--;
                    }
                }
                if (target <= j)
                {
                    hi = j;
                }
                else if (target >= i)
                {
                    lo = i;
                }
                else
                {
                    return;
                }
            }
        }
    }
}
