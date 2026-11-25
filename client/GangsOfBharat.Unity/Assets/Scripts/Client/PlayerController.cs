using GangsOfBharat.Shared.Net;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GangsOfBharat.Client
{
    /// <summary>
    /// On-foot input.
    /// With the Input System (ENABLE_INPUT_SYSTEM): touch needs an OnScreenStick (Input System
    /// package, UnityEngine.InputSystem.OnScreen) on the HUD with Control Path
    /// &lt;Gamepad&gt;/leftStick and OnScreenButton components for sprint (&lt;Gamepad&gt;/buttonEast),
    /// jump (&lt;Gamepad&gt;/buttonSouth) and interact (&lt;Gamepad&gt;/buttonWest). The same actions
    /// read a keyboard (WASD, Shift, Space, E) and a real gamepad.
    /// Without it (legacy Input Manager, the current build): TouchControls provides a virtual
    /// joystick on the left half of the screen and Jump / Run / Enter buttons; the keyboard
    /// (WASD, Shift, Space, E) works in the editor.
    /// Movement is converted to a world-space direction relative to the camera, which is what
    /// the server and the shared Movement code expect.
    /// UNVERIFIED: written against Unity 6 / Input System 1.11 APIs, compiled only against stubs.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private Transform cameraTransform;

#if ENABLE_INPUT_SYSTEM
        private InputAction _move;
        private InputAction _sprint;
        private InputAction _jump;
        private InputAction _interact;
#endif
        private bool _interactPressed;

        /// <summary>Fired when the interact button is pressed (enter vehicle, talk, use).</summary>
        public event System.Action InteractPressed;

        private void Awake()
        {
#if ENABLE_INPUT_SYSTEM
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddBinding("<Gamepad>/leftStick");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _sprint = new InputAction("Sprint", InputActionType.Button);
            _sprint.AddBinding("<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/buttonEast");

            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _interact = new InputAction("Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonWest");
            _interact.performed += ctx => OnInteract();
#endif

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

#if ENABLE_INPUT_SYSTEM
        private void OnEnable()
        {
            _move.Enable();
            _sprint.Enable();
            _jump.Enable();
            _interact.Enable();
        }

        private void OnDisable()
        {
            _move.Disable();
            _sprint.Disable();
            _jump.Disable();
            _interact.Disable();
        }

        private Vector2 ReadMove()
        {
            return _move.ReadValue<Vector2>();
        }

        private bool SprintHeld()
        {
            return _sprint.IsPressed();
        }

        private bool JumpHeld()
        {
            return _jump.IsPressed();
        }
#else
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E) || TouchControls.Instance.GetButtonDown(TouchButton.Interact))
            {
                OnInteract();
            }
        }

        private Vector2 ReadMove()
        {
            Vector2 keys = Vector2.zero;
            if (Input.GetKey(KeyCode.W))
            {
                keys.y += 1f;
            }
            if (Input.GetKey(KeyCode.S))
            {
                keys.y -= 1f;
            }
            if (Input.GetKey(KeyCode.D))
            {
                keys.x += 1f;
            }
            if (Input.GetKey(KeyCode.A))
            {
                keys.x -= 1f;
            }
            return keys + TouchControls.Instance.Move;
        }

        private bool SprintHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || TouchControls.Instance.GetButton(TouchButton.Sprint);
        }

        private bool JumpHeld()
        {
            return Input.GetKey(KeyCode.Space) || TouchControls.Instance.GetButton(TouchButton.Jump);
        }
#endif

        private void OnInteract()
        {
            _interactPressed = true;
            if (InteractPressed != null)
            {
                InteractPressed();
            }
        }

        /// <summary>Fills the on-foot part of an input frame. Called once per client tick.</summary>
        public void SampleInput(ref InputFrame frame)
        {
            Vector2 stick = ReadMove();
            if (stick.sqrMagnitude > 1f)
            {
                stick.Normalize();
            }
            float camYaw = cameraTransform != null ? cameraTransform.eulerAngles.y * Mathf.Deg2Rad : 0f;
            float sin = Mathf.Sin(camYaw);
            float cos = Mathf.Cos(camYaw);
            // Rotate the stick (x right, y forward) by the camera yaw into world X/Z.
            frame.MoveX = stick.x * cos + stick.y * sin;
            frame.MoveZ = -stick.x * sin + stick.y * cos;
            frame.Yaw = camYaw;

            InputButtons buttons = InputButtons.None;
            if (SprintHeld())
            {
                buttons |= InputButtons.Sprint;
            }
            if (JumpHeld())
            {
                buttons |= InputButtons.Jump;
            }
            if (_interactPressed)
            {
                buttons |= InputButtons.Interact;
                _interactPressed = false;
            }
            frame.Buttons = buttons;
        }
    }
}
