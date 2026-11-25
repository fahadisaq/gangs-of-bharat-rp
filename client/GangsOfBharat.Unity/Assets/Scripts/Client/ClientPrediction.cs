using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Net;
using UnityEngine;

namespace GangsOfBharat.Client
{
    /// <summary>One client tick of input, as sent to the server.</summary>
    public struct InputFrame
    {
        public uint Tick;
        public float MoveX;
        public float MoveZ;
        public float Yaw;
        public InputButtons Buttons;
        public float Throttle;
        public float Steer;
    }

    /// <summary>
    /// Client-side prediction for the local player using the shared Movement code. Every client
    /// tick it samples input, steps the same kinematics the server runs, stores the input in a
    /// ring buffer and sends it. When a snapshot arrives it resets to the authoritative state and
    /// replays the inputs the server has not processed yet (reconciliation). The visible
    /// transform smooths out small corrections; large ones snap.
    /// Offline test mode (offlineTestMode, off by default): when there is no NetClient, or it has
    /// not connected after offlineFallbackSeconds, the same shared Movement code runs locally
    /// every frame and nothing is sent. A later successful connection switches back to the
    /// networked path (OnConnected resets the state to the server spawn).
    /// UNVERIFIED: written against Unity 6 APIs, compiled only against stubs in the sandbox.
    /// </summary>
    public sealed class ClientPrediction : MonoBehaviour
    {
        private const int BufferSize = 128; // 6.4 s at 20 Hz

        [SerializeField] private PlayerController playerController;
        [SerializeField] private VehicleController vehicleController;
        [SerializeField] private Transform visual;
        [Tooltip("Corrections larger than this (metres) snap instead of smoothing.")]
        [SerializeField] private float snapDistance = 4f;
        [SerializeField] private float smoothing = 12f;
        [Header("Offline test mode")]
        [Tooltip("Simulate locally without a server when none is reachable (test scenes only).")]
        [SerializeField] private bool offlineTestMode;
        [Tooltip("Seconds to wait for a NetClient connection before going offline.")]
        [SerializeField] private float offlineFallbackSeconds = 5f;

        private readonly InputFrame[] _inputs = new InputFrame[BufferSize];
        private readonly PlayerInputMessage _outInput = new PlayerInputMessage();
        private uint _tick;
        private CharacterState _character;
        private VehicleState _vehicle;
        private VehicleDef _vehicleDef;
        private int _vehicleId;
        private Vector3 _visualOffset;
        private bool _hasState;
        private bool _offline;
        private float _startTime;

        public bool InVehicle { get { return _vehicleId != 0; } }
        public int VehicleId { get { return _vehicleId; } }
        public Vector3 PredictedPosition
        {
            get
            {
                return InVehicle
                    ? new Vector3(_vehicle.X, _vehicle.Y, _vehicle.Z)
                    : new Vector3(_character.X, _character.Y, _character.Z);
            }
        }
        public float PredictedYaw { get { return InVehicle ? _vehicle.Yaw : _character.Yaw; } }
        /// <summary>Predicted forward speed of the vehicle (m/s); 0 on foot.</summary>
        public float VehicleSpeed { get { return InVehicle ? _vehicle.Speed : 0f; } }
        /// <summary>True while simulating locally without a server (offline test mode).</summary>
        public bool IsOffline { get { return _offline; } }
        /// <summary>The transform that shows the local player (may be null).</summary>
        public Transform Visual { get { return visual; } }

        private void OnEnable()
        {
            NetClient net = NetClient.Instance;
            if (net == null)
            {
                return;
            }
            net.Connected += OnConnected;
            net.ClientTick += OnClientTick;
            net.SnapshotReceived += OnSnapshot;
        }

        private void OnDisable()
        {
            NetClient net = NetClient.Instance;
            if (net == null)
            {
                return;
            }
            net.Connected -= OnConnected;
            net.ClientTick -= OnClientTick;
            net.SnapshotReceived -= OnSnapshot;
        }

        private void Start()
        {
            _startTime = Time.time;
        }

        private void OnConnected(ConnectAcceptMessage accept)
        {
            _offline = false;
            _character.X = accept.SpawnX;
            _character.Y = accept.SpawnY;
            _character.Z = accept.SpawnZ;
            _character.Yaw = 0f;
            _vehicleId = 0;
            _hasState = true;
            _visualOffset = Vector3.zero;
        }

        /// <summary>Called by VehicleController after the server confirmed EnterVehicle.</summary>
        public void BeginDriving(int vehicleId, VehicleDef def, Vector3 position, float yaw)
        {
            _vehicleId = vehicleId;
            _vehicleDef = def;
            _vehicle.X = position.x;
            _vehicle.Y = position.y;
            _vehicle.Z = position.z;
            _vehicle.Yaw = yaw;
            _vehicle.Speed = 0f;
        }

        /// <summary>Offline test mode only: moves the on-foot character (e.g. beside a vehicle).</summary>
        public void PlaceCharacter(Vector3 position)
        {
            if (!_offline || InVehicle)
            {
                return;
            }
            _character.X = position.x;
            _character.Y = position.y;
            _character.Z = position.z;
        }

        /// <summary>Called by VehicleController after the server confirmed ExitVehicle.</summary>
        public void EndDriving()
        {
            _character.X = _vehicle.X;
            _character.Y = _vehicle.Y;
            _character.Z = _vehicle.Z;
            _character.Yaw = _vehicle.Yaw;
            _vehicleId = 0;
            _vehicleDef = null;
        }

        private void Update()
        {
            if (!offlineTestMode)
            {
                return;
            }
            if (!_offline)
            {
                if (_hasState)
                {
                    return; // networked session in progress
                }
                NetClient net = NetClient.Instance;
                bool noServer = net == null
                    || (net.State != ClientState.Connected && Time.time - _startTime >= offlineFallbackSeconds);
                if (!noServer)
                {
                    return;
                }
                StartOffline();
            }
            // Offline there is nothing to stay in lockstep with, so step every frame for smooth motion.
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt > 0f)
            {
                InputFrame frame = SampleFrame();
                Apply(ref frame, dt);
            }
        }

        private void StartOffline()
        {
            Transform from = visual != null ? visual : transform;
            Vector3 p = from.position;
            _character.X = p.x;
            _character.Y = p.y;
            _character.Z = p.z;
            _character.Yaw = from.eulerAngles.y * Mathf.Deg2Rad;
            _vehicleId = 0;
            _vehicleDef = null;
            _visualOffset = Vector3.zero;
            _hasState = true;
            _offline = true;
            Debug.Log("ClientPrediction: no server connection, offline test mode (local movement only).");
        }

        private InputFrame SampleFrame()
        {
            InputFrame frame = new InputFrame();
            if (InVehicle && vehicleController != null)
            {
                vehicleController.SampleInput(ref frame);
            }
            else if (playerController != null)
            {
                playerController.SampleInput(ref frame);
            }
            return frame;
        }

        private void OnClientTick(float dt)
        {
            if (!_hasState || _offline)
            {
                return;
            }
            InputFrame frame = SampleFrame();
            frame.Tick = ++_tick;
            _inputs[frame.Tick % BufferSize] = frame;
            Apply(ref frame, dt);

            Vector3 p = PredictedPosition;
            _outInput.InputTick = frame.Tick;
            _outInput.MoveX = frame.MoveX;
            _outInput.MoveZ = frame.MoveZ;
            _outInput.Yaw = frame.Yaw;
            _outInput.Buttons = frame.Buttons;
            _outInput.Throttle = frame.Throttle;
            _outInput.Steer = frame.Steer;
            _outInput.ClientX = p.x;
            _outInput.ClientY = p.y;
            _outInput.ClientZ = p.z;
            NetClient.Instance.SendUnreliable(_outInput);
        }

        private void Apply(ref InputFrame frame, float dt)
        {
            if (InVehicle && _vehicleDef != null)
            {
                bool brake = (frame.Buttons & InputButtons.Brake) != 0;
                Movement.StepVehicle(ref _vehicle, _vehicleDef, frame.Throttle, frame.Steer, brake, dt);
            }
            else
            {
                bool sprint = (frame.Buttons & InputButtons.Sprint) != 0;
                Movement.StepCharacter(ref _character, frame.MoveX, frame.MoveZ, frame.Yaw, sprint, dt);
            }
        }

        private void OnSnapshot(WorldSnapshotMessage snapshot)
        {
            if (!_hasState)
            {
                return;
            }
            Vector3 before = PredictedPosition;

            // The server reports the vehicle it thinks we are in; trust it over local state.
            if (snapshot.SelfVehicleId == 0 && InVehicle)
            {
                EndDriving();
            }

            // Reset to the authoritative state, then replay unprocessed inputs.
            if (InVehicle)
            {
                _vehicle.X = snapshot.SelfX;
                _vehicle.Y = snapshot.SelfY;
                _vehicle.Z = snapshot.SelfZ;
                _vehicle.Yaw = snapshot.SelfYaw;
                // Speed is not in the snapshot; keep the predicted speed (small drift is
                // corrected by the next snapshot's position).
            }
            else
            {
                _character.X = snapshot.SelfX;
                _character.Y = snapshot.SelfY;
                _character.Z = snapshot.SelfZ;
                _character.Yaw = snapshot.SelfYaw;
            }
            float dt = 1f / NetClient.Instance.TickRate;
            uint acked = snapshot.LastProcessedInputTick;
            if (_tick - acked < BufferSize)
            {
                for (uint t = acked + 1; t <= _tick; t++)
                {
                    InputFrame f = _inputs[t % BufferSize];
                    if (f.Tick == t)
                    {
                        Apply(ref f, dt);
                    }
                }
            }

            Vector3 error = before - PredictedPosition;
            _visualOffset = error.magnitude > snapDistance ? Vector3.zero : _visualOffset + error;
        }

        private void LateUpdate()
        {
            if (!_hasState || visual == null)
            {
                return;
            }
            _visualOffset = Vector3.Lerp(_visualOffset, Vector3.zero, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            visual.position = PredictedPosition + _visualOffset;
            visual.rotation = Quaternion.Euler(0f, PredictedYaw * Mathf.Rad2Deg, 0f);
        }
    }
}
