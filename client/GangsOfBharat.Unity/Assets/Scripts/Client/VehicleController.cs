using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Net;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Vehicle enter/exit and arcade driving. Driving physics is the shared kinematic model
    /// (Movement.StepVehicle) run by ClientPrediction, so the client and server agree; there is
    /// no Rigidbody physics on networked vehicles. Enter/exit is a request: the server checks
    /// range and occupancy and echoes EnterVehicle/ExitVehicle with Success.
    /// Touch with the Input System (ENABLE_INPUT_SYSTEM): OnScreenStick on &lt;Gamepad&gt;/rightStick
    /// (x = steer), OnScreenButtons on &lt;Gamepad&gt;/rightTrigger (accelerate),
    /// &lt;Gamepad&gt;/leftTrigger (reverse), &lt;Gamepad&gt;/buttonNorth (handbrake) and
    /// &lt;Gamepad&gt;/buttonWest (enter/exit).
    /// Without it (legacy Input Manager): TouchControls' left-half joystick (y = throttle and
    /// reverse, x = steer) and Brake / Horn / Exit buttons; keyboard W/S, A/D, Space, H, F.
    /// Offline test mode (ClientPrediction.IsOffline): enter/exit is done locally with a
    /// scene-placed OfflineVehicle instead of a server request.
    /// UNVERIFIED: written against Unity 6 / Input System 1.11 APIs, compiled only against stubs.
    /// </summary>
    public sealed class VehicleController : MonoBehaviour
    {
        [SerializeField] private ClientPrediction prediction;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private EntityInterpolator entities;
        [SerializeField] private CameraRig cameraRig;

#if ENABLE_INPUT_SYSTEM
        private InputAction _throttle;
        private InputAction _steer;
        private InputAction _brake;
        private InputAction _horn;
        private InputAction _exit;
#endif
        private OfflineVehicle _offlineVehicle;
        private int _pendingVehicleId;
        private bool _requestPending;
        private readonly EnterVehicleMessage _enter = new EnterVehicleMessage();
        private readonly ExitVehicleMessage _exitMsg = new ExitVehicleMessage();

#if ENABLE_INPUT_SYSTEM
        private void Awake()
        {
            _throttle = new InputAction("Throttle", InputActionType.Value);
            _throttle.AddCompositeBinding("1DAxis")
                .With("Positive", "<Keyboard>/w")
                .With("Negative", "<Keyboard>/s");
            _throttle.AddCompositeBinding("1DAxis")
                .With("Positive", "<Gamepad>/rightTrigger")
                .With("Negative", "<Gamepad>/leftTrigger");

            _steer = new InputAction("Steer", InputActionType.Value);
            _steer.AddCompositeBinding("1DAxis")
                .With("Positive", "<Keyboard>/d")
                .With("Negative", "<Keyboard>/a");
            _steer.AddBinding("<Gamepad>/rightStick/x");
            _steer.AddBinding("<Gamepad>/leftStick/x");

            _brake = new InputAction("Handbrake", InputActionType.Button);
            _brake.AddBinding("<Keyboard>/space");
            _brake.AddBinding("<Gamepad>/buttonNorth");

            _horn = new InputAction("Horn", InputActionType.Button);
            _horn.AddBinding("<Keyboard>/h");

            _exit = new InputAction("Exit", InputActionType.Button);
            _exit.AddBinding("<Keyboard>/f");
            _exit.AddBinding("<Gamepad>/buttonWest");
            _exit.performed += ctx => RequestExit();
        }

        private float ReadThrottle()
        {
            return _throttle.ReadValue<float>();
        }

        private float ReadSteer()
        {
            return _steer.ReadValue<float>();
        }

        private bool BrakeHeld()
        {
            return _brake.IsPressed();
        }

        private bool HornHeld()
        {
            return _horn.IsPressed();
        }
#else
        private void Update()
        {
            TouchControls touch = TouchControls.Instance;
            bool inVehicle = prediction != null && prediction.InVehicle;
            touch.InVehicle = inVehicle;
            if (inVehicle && (Input.GetKeyDown(KeyCode.F) || touch.GetButtonDown(TouchButton.Exit)))
            {
                RequestExit();
            }
        }

        private static float KeyAxis(KeyCode positive, KeyCode negative)
        {
            float v = 0f;
            if (Input.GetKey(positive))
            {
                v += 1f;
            }
            if (Input.GetKey(negative))
            {
                v -= 1f;
            }
            return v;
        }

        // Whichever of keyboard and joystick is pushed further wins.
        private static float Strongest(float a, float b)
        {
            return Mathf.Abs(a) >= Mathf.Abs(b) ? a : b;
        }

        private float ReadThrottle()
        {
            return Strongest(KeyAxis(KeyCode.W, KeyCode.S), TouchControls.Instance.Move.y);
        }

        private float ReadSteer()
        {
            return Strongest(KeyAxis(KeyCode.D, KeyCode.A), TouchControls.Instance.Move.x);
        }

        private bool BrakeHeld()
        {
            return Input.GetKey(KeyCode.Space) || TouchControls.Instance.GetButton(TouchButton.Handbrake);
        }

        private bool HornHeld()
        {
            return Input.GetKey(KeyCode.H) || TouchControls.Instance.GetButton(TouchButton.Horn);
        }
#endif

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            _throttle.Enable();
            _steer.Enable();
            _brake.Enable();
            _horn.Enable();
            _exit.Enable();
#endif
            if (playerController != null)
            {
                playerController.InteractPressed += RequestEnterNearest;
            }
            if (NetClient.Instance != null)
            {
                NetClient.Instance.OtherReceived += OnServerMessage;
            }
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            _throttle.Disable();
            _steer.Disable();
            _brake.Disable();
            _horn.Disable();
            _exit.Disable();
#endif
            if (playerController != null)
            {
                playerController.InteractPressed -= RequestEnterNearest;
            }
            if (NetClient.Instance != null)
            {
                NetClient.Instance.OtherReceived -= OnServerMessage;
            }
        }

        /// <summary>Asks the server to enter the closest vehicle within Movement.EnterVehicleRange.</summary>
        public void RequestEnterNearest()
        {
            if (prediction != null && prediction.IsOffline)
            {
                EnterOffline();
                return;
            }
            if (prediction.InVehicle || _requestPending || entities == null)
            {
                return;
            }
            EntityInterpolator.RemoteEntity vehicle =
                entities.FindNearestVehicle(prediction.PredictedPosition, Movement.EnterVehicleRange);
            if (vehicle == null)
            {
                return;
            }
            _pendingVehicleId = vehicle.Id;
            _requestPending = true;
            _enter.VehicleId = vehicle.Id;
            _enter.PlayerId = NetClient.Instance.PlayerId;
            _enter.Success = false;
            NetClient.Instance.SendReliable(_enter);
        }

        public void RequestExit()
        {
            if (prediction != null && prediction.IsOffline)
            {
                ExitOffline();
                return;
            }
            if (!prediction.InVehicle || _requestPending)
            {
                return;
            }
            _requestPending = true;
            _exitMsg.VehicleId = prediction.VehicleId;
            _exitMsg.PlayerId = NetClient.Instance.PlayerId;
            _exitMsg.Success = false;
            NetClient.Instance.SendReliable(_exitMsg);
        }

        // Offline test mode: no server to ask, enter the nearest scene-placed OfflineVehicle.
        private void EnterOffline()
        {
            if (prediction.InVehicle)
            {
                return;
            }
            OfflineVehicle vehicle = OfflineVehicle.FindNearest(prediction.PredictedPosition, Movement.EnterVehicleRange);
            if (vehicle == null)
            {
                return;
            }
            VehicleDef def = VehicleCatalog.Get(vehicle.ModelId);
            if (def == null)
            {
                def = VehicleCatalog.All[0];
            }
            Transform t = vehicle.transform;
            prediction.BeginDriving(vehicle.Id, def, t.position, t.eulerAngles.y * Mathf.Deg2Rad);
            vehicle.SetDriver(prediction);
            _offlineVehicle = vehicle;
            if (cameraRig != null)
            {
                cameraRig.SetMode(CameraRig.Mode.Vehicle);
            }
        }

        private void ExitOffline()
        {
            if (!prediction.InVehicle)
            {
                return;
            }
            Vector3 carPosition = prediction.PredictedPosition;
            float yaw = prediction.PredictedYaw;
            prediction.EndDriving();
            // Step out on the driver's side (left of the vehicle; yaw 0 faces +Z).
            Vector3 left = new Vector3(-Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            prediction.PlaceCharacter(carPosition + left * 2f);
            if (_offlineVehicle != null)
            {
                _offlineVehicle.ClearDriver(carPosition, yaw);
                _offlineVehicle = null;
            }
            if (cameraRig != null)
            {
                cameraRig.SetMode(CameraRig.Mode.OnFoot);
            }
        }

        private void OnServerMessage(NetMessage message)
        {
            EnterVehicleMessage enter = message as EnterVehicleMessage;
            if (enter != null && enter.PlayerId == NetClient.Instance.PlayerId)
            {
                _requestPending = false;
                EntityInterpolator.RemoteEntity vehicle;
                if (enter.Success && entities.TryGet(enter.VehicleId, out vehicle) && vehicle.View != null)
                {
                    VehicleDef def = VehicleCatalog.Get(vehicle.ModelId);
                    Transform t = vehicle.View.transform;
                    prediction.BeginDriving(enter.VehicleId, def, t.position, t.eulerAngles.y * Mathf.Deg2Rad);
                    if (cameraRig != null)
                    {
                        cameraRig.SetMode(CameraRig.Mode.Vehicle);
                    }
                }
                return;
            }
            ExitVehicleMessage exit = message as ExitVehicleMessage;
            if (exit != null && exit.PlayerId == NetClient.Instance.PlayerId)
            {
                _requestPending = false;
                if (exit.Success)
                {
                    prediction.EndDriving();
                    if (cameraRig != null)
                    {
                        cameraRig.SetMode(CameraRig.Mode.OnFoot);
                    }
                }
            }
        }

        /// <summary>Fills the driving part of an input frame. Called once per client tick.</summary>
        public void SampleInput(ref InputFrame frame)
        {
            frame.Throttle = Mathf.Clamp(ReadThrottle(), -1f, 1f);
            frame.Steer = Mathf.Clamp(ReadSteer(), -1f, 1f);
            frame.Yaw = prediction.PredictedYaw;
            InputButtons buttons = InputButtons.None;
            if (BrakeHeld())
            {
                buttons |= InputButtons.Brake;
            }
            if (HornHeld())
            {
                buttons |= InputButtons.Horn;
            }
            frame.Buttons = buttons;
        }
    }
}
