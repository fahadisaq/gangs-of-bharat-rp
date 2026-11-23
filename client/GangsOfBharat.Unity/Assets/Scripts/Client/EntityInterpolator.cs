using System.Collections.Generic;
using GangsOfBharat.Shared.Net;
using UnityEngine;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Shows remote players and vehicles from server snapshots. Each entity keeps a small buffer
    /// of timestamped states; it is rendered InterpolationDelayMs behind the newest server time
    /// and linearly interpolated between the two surrounding samples, which hides jitter and the
    /// lower update rates of far tiers. Entities not updated for 2 s are removed (covers lost
    /// despawn messages, as the protocol documents).
    /// UNVERIFIED: written against Unity 6 APIs, not compiled in the sandbox.
    /// </summary>
    public sealed class EntityInterpolator : MonoBehaviour
    {
        private const int SamplesPerEntity = 16;
        private const double StaleMs = 2000.0;

        [SerializeField] private GameObject playerPrefab;
        [Tooltip("Indexed by vehicle catalog id; missing entries use defaultVehiclePrefab.")]
        [SerializeField] private GameObject[] vehiclePrefabs = new GameObject[0];
        [SerializeField] private GameObject defaultVehiclePrefab;
        [SerializeField] private ClientPrediction localPlayer;
        [Tooltip("Render delay behind the newest snapshot. 2-3 snapshot intervals.")]
        [SerializeField] private double interpolationDelayMs = 150.0;

        internal struct Sample
        {
            public double TimeMs;
            public Vector3 Position;
            public float Yaw;
        }

        public sealed class RemoteEntity
        {
            public int Id;
            public EntityKind Kind;
            public int ModelId;
            public byte FactionId;
            public int LinkedId;
            public GameObject View;
            public double LastUpdateMs;
            internal readonly Sample[] Samples = new Sample[SamplesPerEntity];
            internal int Count;
            internal int Head;
        }

        private readonly Dictionary<int, RemoteEntity> _entities = new Dictionary<int, RemoteEntity>();
        private readonly List<int> _toRemove = new List<int>();
        private double _latestServerMs;
        private double _renderClockOffsetMs;
        private bool _clockSynced;

        public IEnumerable<RemoteEntity> Entities { get { return _entities.Values; } }

        public bool TryGet(int id, out RemoteEntity entity)
        {
            return _entities.TryGetValue(id, out entity);
        }

        /// <summary>Finds the nearest vehicle within range of a point (for EnterVehicle).</summary>
        public RemoteEntity FindNearestVehicle(Vector3 point, float range)
        {
            RemoteEntity best = null;
            float bestSq = range * range;
            foreach (RemoteEntity e in _entities.Values)
            {
                if (e.Kind != EntityKind.Vehicle || e.View == null)
                {
                    continue;
                }
                float sq = (e.View.transform.position - point).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = e;
                }
            }
            return best;
        }

        private void OnEnable()
        {
            if (NetClient.Instance != null)
            {
                NetClient.Instance.SnapshotReceived += OnSnapshot;
            }
        }

        private void OnDisable()
        {
            if (NetClient.Instance != null)
            {
                NetClient.Instance.SnapshotReceived -= OnSnapshot;
            }
        }

        private void OnSnapshot(WorldSnapshotMessage snapshot)
        {
            NetClient net = NetClient.Instance;
            double serverMs = snapshot.ServerTick * (1000.0 / net.TickRate);
            double now = net.NowMs;
            if (!_clockSynced || serverMs > _latestServerMs)
            {
                _latestServerMs = serverMs;
                // Offset between local clock and server time, smoothed to avoid jumps.
                double offset = serverMs - now;
                _renderClockOffsetMs = _clockSynced ? _renderClockOffsetMs + (offset - _renderClockOffsetMs) * 0.1 : offset;
                _clockSynced = true;
            }

            int self = net.PlayerId;
            for (int i = 0; i < snapshot.EntityCount; i++)
            {
                EntityState s = snapshot.Entities[i];
                if (s.Id == self)
                {
                    continue; // the local player is predicted, not interpolated
                }
                RemoteEntity e;
                if (!_entities.TryGetValue(s.Id, out e))
                {
                    if (!s.Spawn)
                    {
                        continue; // update for an entity whose spawn we missed; wait for respawn
                    }
                    e = new RemoteEntity { Id = s.Id, Kind = s.Kind, ModelId = s.ModelId, FactionId = s.FactionId };
                    e.View = CreateView(e);
                    _entities.Add(s.Id, e);
                }
                e.LinkedId = s.LinkedId;
                e.LastUpdateMs = now;
                AddSample(e, serverMs, new Vector3(s.X, s.Y, s.Z), s.Yaw);
            }
            for (int i = 0; i < snapshot.DespawnCount; i++)
            {
                Remove(snapshot.Despawns[i]);
            }
        }

        private GameObject CreateView(RemoteEntity e)
        {
            GameObject prefab = playerPrefab;
            if (e.Kind == EntityKind.Vehicle)
            {
                prefab = e.ModelId >= 0 && e.ModelId < vehiclePrefabs.Length && vehiclePrefabs[e.ModelId] != null
                    ? vehiclePrefabs[e.ModelId]
                    : defaultVehiclePrefab;
            }
            GameObject view = prefab != null ? Instantiate(prefab, transform) : new GameObject();
            view.name = e.Kind + "_" + e.Id;
            return view;
        }

        private static void AddSample(RemoteEntity e, double timeMs, Vector3 position, float yaw)
        {
            int newest = (e.Head - 1 + SamplesPerEntity) % SamplesPerEntity;
            if (e.Count > 0 && timeMs <= e.Samples[newest].TimeMs)
            {
                return; // out-of-order or duplicate snapshot
            }
            e.Samples[e.Head] = new Sample { TimeMs = timeMs, Position = position, Yaw = yaw };
            e.Head = (e.Head + 1) % SamplesPerEntity;
            if (e.Count < SamplesPerEntity)
            {
                e.Count++;
            }
        }

        private void Remove(int id)
        {
            RemoteEntity e;
            if (_entities.TryGetValue(id, out e))
            {
                if (e.View != null)
                {
                    Destroy(e.View);
                }
                _entities.Remove(id);
            }
        }

        private void Update()
        {
            if (!_clockSynced)
            {
                return;
            }
            NetClient net = NetClient.Instance;
            double now = net.NowMs;
            double renderMs = now + _renderClockOffsetMs - interpolationDelayMs;
            int ownVehicle = localPlayer != null ? localPlayer.VehicleId : 0;

            _toRemove.Clear();
            foreach (RemoteEntity e in _entities.Values)
            {
                if (now - e.LastUpdateMs > StaleMs)
                {
                    _toRemove.Add(e.Id);
                    continue;
                }
                if (e.View == null)
                {
                    continue;
                }
                if (e.Id == ownVehicle && localPlayer != null)
                {
                    // Our own vehicle follows the predicted position, not the delayed one.
                    e.View.transform.SetPositionAndRotation(localPlayer.PredictedPosition,
                        Quaternion.Euler(0f, localPlayer.PredictedYaw * Mathf.Rad2Deg, 0f));
                    continue;
                }
                Vector3 pos;
                float yaw;
                SampleAt(e, renderMs, out pos, out yaw);
                e.View.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f));
                // Players sitting in a vehicle are hidden; the vehicle represents them.
                if (e.Kind == EntityKind.Player)
                {
                    e.View.SetActive(e.LinkedId == 0);
                }
            }
            for (int i = 0; i < _toRemove.Count; i++)
            {
                Remove(_toRemove[i]);
            }
        }

        private static void SampleAt(RemoteEntity e, double renderMs, out Vector3 position, out float yaw)
        {
            // Walk from oldest to newest and find the pair around renderMs.
            int oldest = (e.Head - e.Count + SamplesPerEntity) % SamplesPerEntity;
            Sample prev = e.Samples[oldest];
            for (int i = 1; i < e.Count; i++)
            {
                Sample next = e.Samples[(oldest + i) % SamplesPerEntity];
                if (next.TimeMs >= renderMs)
                {
                    double span = next.TimeMs - prev.TimeMs;
                    float t = span > 0.0 ? (float)((renderMs - prev.TimeMs) / span) : 1f;
                    t = Mathf.Clamp01(t);
                    position = Vector3.Lerp(prev.Position, next.Position, t);
                    yaw = Mathf.LerpAngle(prev.Yaw * Mathf.Rad2Deg, next.Yaw * Mathf.Rad2Deg, t) * Mathf.Deg2Rad;
                    return;
                }
                prev = next;
            }
            // renderMs is newer than every sample: hold the newest (no extrapolation).
            position = prev.Position;
            yaw = prev.Yaw;
        }
    }
}
