using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Vehicle enter/exit and arcade driving. Driving physics is the shared kinematic model
    /// (Movement.StepVehicle) run by ClientPrediction, so the client and server agree; there is
    /// no Rigidbody physics on networked vehicles. Enter/exit is a request: the server checks
    /// range and occupancy and echoes EnterVehicle/ExitVehicle with Success.
    /// Touch: OnScreenStick on &lt;Gamepad&gt;/rightStick (x = steer), OnScreenButtons on
    /// &lt;Gamepad&gt;/rightTrigger (accelerate), &lt;Gamepad&gt;/leftTrigger (reverse),
    /// &lt;Gamepad&gt;/buttonNorth (handbrake) and &lt;Gamepad&gt;/buttonWest (enter/exit).
    /// UNVERIFIED: written against Unity 6 / Input System 1.11 APIs, not compiled in the sandbox.
    /// </summary>
    public sealed class VehicleController : MonoBehaviour
    {
        [SerializeField] private ClientPrediction prediction;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private EntityInterpolator entities;
        [SerializeField] private CameraRig cameraRig;

        private InputAction _throttle;
        private InputAction _steer;
        private InputAction _brake;
        private InputAction _horn;
        private InputAction _exit;
        private int _pendingVehicleId;
        private bool _requestPending;
        private readonly EnterVehicleMessage _enter = new EnterVehicleMessage();
        private readonly ExitVehicleMessage _exitMsg = new ExitVehicleMessage();

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

        private void OnEnable()
        {
            _throttle.Enable();
            _steer.Enable();
            _brake.Enable();
            _horn.Enable();
            _exit.Enable();
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
            _throttle.Disable();
            _steer.Disable();
            _brake.Disable();
            _horn.Disable();
            _exit.Disable();
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
            frame.Throttle = Mathf.Clamp(_throttle.ReadValue<float>(), -1f, 1f);
            frame.Steer = Mathf.Clamp(_steer.ReadValue<float>(), -1f, 1f);
            frame.Yaw = prediction.PredictedYaw;
            InputButtons buttons = InputButtons.None;
            if (_brake.IsPressed())
            {
                buttons |= InputButtons.Brake;
            }
            if (_horn.IsPressed())
            {
                buttons |= InputButtons.Horn;
            }
            frame.Buttons = buttons;
        }
    }
}
