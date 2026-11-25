// Only compiled when the Input System is NOT the active input handler (legacy Input Manager).
// With ENABLE_INPUT_SYSTEM the gameplay scripts read UnityEngine.InputSystem instead and this
// file is empty.
#if !ENABLE_INPUT_SYSTEM
using System.Collections.Generic;
using UnityEngine;

namespace GangsOfBharat.Client
{
    /// <summary>On-screen buttons drawn by TouchControls.</summary>
    public enum TouchButton
    {
        Jump,
        Sprint,
        Interact,
        Exit,
        Handbrake,
        Horn,
        Talk,
        Radio,
    }

    /// <summary>
    /// Touch controls for the legacy Input Manager (UnityEngine.Input), used while the project
    /// has no ProjectSettings.asset with Active Input Handling set to the Input System.
    ///  - Left half of the screen: floating virtual joystick. The stick centre is where the
    ///    finger went down; the value is the drag offset divided by a radius of 8% of the screen
    ///    height, clamped to length 1.
    ///  - Right half: drag to look (LookDelta, in pixels this frame).
    ///  - Buttons on the bottom right. On foot: Jump, Sprint, Enter (Interact). In a vehicle:
    ///    Brake (handbrake), Horn, Exit. Talk/Radio appear while VoiceChat calls ShowVoiceButtons.
    /// Multi-touch is read from Input.touches directly; IMGUI is only used for drawing.
    /// On desktop (Input.touchSupported is false, e.g. the editor) the left mouse button acts
    /// as one finger. Created on first use, survives scene loads.
    /// UNVERIFIED: written against Unity 6 APIs, compiled only against stubs in the sandbox.
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        private const int ButtonCount = 8;
        private const int NoFinger = int.MinValue;
        private const int MouseFingerId = -100;
        private const float StickRadiusFraction = 0.08f;
        private const float StickDeadZone = 0.08f;
        private const float ButtonSizeFraction = 0.15f;

        private static TouchControls _instance;

        private readonly int[] _buttonFinger = new int[ButtonCount];
        private readonly int[] _buttonDownFrame = new int[ButtonCount];
        private readonly HashSet<int> _seen = new HashSet<int>();
        private int _processedFrame = -1;
        private int _stickFinger = NoFinger;
        private Vector2 _stickOrigin;
        private Vector2 _stickPosition;
        private int _lookFinger = NoFinger;
        private Vector2 _lookLast;
        private Vector2 _lookDelta;
        private int _voiceRequestFrame = -100;
        private GUIStyle _buttonStyle;
        private GUIStyle _buttonHeldStyle;
        private int _styleScreenHeight;

        /// <summary>The shared instance; creates it (DontDestroyOnLoad) on first use.</summary>
        public static TouchControls Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("TouchControls");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<TouchControls>();
                }
                return _instance;
            }
        }

        /// <summary>Selects the button layout. Set every frame by VehicleController.</summary>
        public bool InVehicle { get; set; }

        /// <summary>Virtual joystick, x right / y forward, each -1..1, length at most 1.</summary>
        public Vector2 Move
        {
            get
            {
                EnsureUpdated();
                if (_stickFinger == NoFinger)
                {
                    return Vector2.zero;
                }
                Vector2 v = (_stickPosition - _stickOrigin) / StickRadius;
                if (v.sqrMagnitude > 1f)
                {
                    v.Normalize();
                }
                return v.magnitude < StickDeadZone ? Vector2.zero : v;
            }
        }

        /// <summary>Right-half drag this frame, in screen pixels.</summary>
        public Vector2 LookDelta
        {
            get
            {
                EnsureUpdated();
                return _lookDelta;
            }
        }

        /// <summary>True while a finger holds the button (and the button is shown).</summary>
        public bool GetButton(TouchButton button)
        {
            EnsureUpdated();
            int b = (int)button;
            return _buttonFinger[b] != NoFinger && IsVisible(button);
        }

        /// <summary>True only in the frame a finger went down on the button.</summary>
        public bool GetButtonDown(TouchButton button)
        {
            EnsureUpdated();
            return _buttonDownFrame[(int)button] == Time.frameCount;
        }

        /// <summary>Shows Talk/Radio for this frame and the next two. Call every frame.</summary>
        public void ShowVoiceButtons()
        {
            _voiceRequestFrame = Time.frameCount;
        }

        private float StickRadius
        {
            get { return Mathf.Max(20f, Screen.height * StickRadiusFraction); }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            for (int i = 0; i < ButtonCount; i++)
            {
                _buttonFinger[i] = NoFinger;
                _buttonDownFrame[i] = -1;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            EnsureUpdated();
        }

        // Reads all touches once per frame, whichever script asks first.
        private void EnsureUpdated()
        {
            if (_processedFrame == Time.frameCount)
            {
                return;
            }
            _processedFrame = Time.frameCount;
            _lookDelta = Vector2.zero;
            _seen.Clear();

            int count = Input.touchCount;
            for (int i = 0; i < count; i++)
            {
                Touch touch = Input.GetTouch(i);
                bool ended = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
                HandlePointer(touch.fingerId, touch.position, touch.phase == TouchPhase.Began, ended);
            }
            if (!Input.touchSupported)
            {
                bool held = Input.GetMouseButton(0);
                bool up = Input.GetMouseButtonUp(0);
                if (held || up)
                {
                    Vector3 mouse = Input.mousePosition;
                    HandlePointer(MouseFingerId, new Vector2(mouse.x, mouse.y), Input.GetMouseButtonDown(0), !held);
                }
            }

            // Release everything owned by a finger that ended or disappeared.
            if (_stickFinger != NoFinger && !_seen.Contains(_stickFinger))
            {
                _stickFinger = NoFinger;
            }
            if (_lookFinger != NoFinger && !_seen.Contains(_lookFinger))
            {
                _lookFinger = NoFinger;
            }
            for (int b = 0; b < ButtonCount; b++)
            {
                if (_buttonFinger[b] != NoFinger && !_seen.Contains(_buttonFinger[b]))
                {
                    _buttonFinger[b] = NoFinger;
                }
            }
        }

        // position is in screen pixels with the origin at the bottom left (Input convention).
        private void HandlePointer(int finger, Vector2 position, bool began, bool ended)
        {
            if (began)
            {
                int hit = HitButton(position);
                if (hit >= 0)
                {
                    _buttonFinger[hit] = finger;
                    _buttonDownFrame[hit] = Time.frameCount;
                }
                else if (position.x < Screen.width * 0.5f)
                {
                    if (_stickFinger == NoFinger)
                    {
                        _stickFinger = finger;
                        _stickOrigin = position;
                        _stickPosition = position;
                    }
                }
                else if (_lookFinger == NoFinger)
                {
                    _lookFinger = finger;
                    _lookLast = position;
                }
            }
            if (finger == _stickFinger)
            {
                _stickPosition = position;
            }
            if (finger == _lookFinger)
            {
                _lookDelta += position - _lookLast;
                _lookLast = position;
            }
            if (!ended)
            {
                _seen.Add(finger);
            }
        }

        private int HitButton(Vector2 position)
        {
            for (int b = 0; b < ButtonCount; b++)
            {
                TouchButton button = (TouchButton)b;
                if (IsVisible(button) && ButtonRect(button).Contains(position))
                {
                    return b;
                }
            }
            return -1;
        }

        private bool IsVisible(TouchButton button)
        {
            switch (button)
            {
                case TouchButton.Jump:
                case TouchButton.Sprint:
                case TouchButton.Interact:
                    return !InVehicle;
                case TouchButton.Handbrake:
                case TouchButton.Horn:
                case TouchButton.Exit:
                    return InVehicle;
                default:
                    return Time.frameCount - _voiceRequestFrame <= 2;
            }
        }

        // Slot grid from the bottom-right corner: column 0 is the rightmost, row 0 the bottom.
        private static int SlotColumn(TouchButton button)
        {
            switch (button)
            {
                case TouchButton.Jump:
                case TouchButton.Handbrake:
                case TouchButton.Interact:
                case TouchButton.Exit:
                    return 0;
                case TouchButton.Sprint:
                case TouchButton.Horn:
                    return 1;
                default:
                    return 2;
            }
        }

        private static int SlotRow(TouchButton button)
        {
            switch (button)
            {
                case TouchButton.Interact:
                case TouchButton.Exit:
                case TouchButton.Radio:
                    return 1;
                default:
                    return 0;
            }
        }

        // Screen-space rect (origin bottom left).
        private static Rect ButtonRect(TouchButton button)
        {
            float size = Mathf.Min(Screen.width, Screen.height) * ButtonSizeFraction;
            float gap = size * 0.25f;
            float x = Screen.width - (gap + size) * (SlotColumn(button) + 1);
            float y = gap + (gap + size) * SlotRow(button);
            return new Rect(x, y, size, size);
        }

        private static string Label(TouchButton button)
        {
            switch (button)
            {
                case TouchButton.Jump: return "Jump";
                case TouchButton.Sprint: return "Run";
                case TouchButton.Interact: return "Enter";
                case TouchButton.Exit: return "Exit";
                case TouchButton.Handbrake: return "Brake";
                case TouchButton.Horn: return "Horn";
                case TouchButton.Talk: return "Talk";
                default: return "Radio";
            }
        }

        private static Rect ToGui(Rect screenRect)
        {
            return new Rect(screenRect.x, Screen.height - screenRect.y - screenRect.height,
                screenRect.width, screenRect.height);
        }

        private void EnsureStyles()
        {
            if (_buttonStyle != null && _styleScreenHeight == Screen.height)
            {
                return;
            }
            _styleScreenHeight = Screen.height;
            int fontSize = Mathf.Max(12, (int)(Mathf.Min(Screen.width, Screen.height) * 0.035f));
            _buttonStyle = new GUIStyle(GUI.skin.box);
            _buttonStyle.fontSize = fontSize;
            _buttonStyle.alignment = TextAnchor.MiddleCenter;
            _buttonStyle.normal.textColor = Color.white;
            _buttonHeldStyle = new GUIStyle(_buttonStyle);
            _buttonHeldStyle.normal.textColor = Color.yellow;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            EnsureStyles();
            Color previous = GUI.color;

            // Joystick: ring at the touch-down point and a knob, or a faint hint when idle.
            float r = StickRadius;
            Vector2 origin = _stickFinger != NoFinger ? _stickOrigin : new Vector2(r * 2f, r * 2f);
            GUI.color = new Color(1f, 1f, 1f, _stickFinger != NoFinger ? 0.8f : 0.35f);
            GUI.Box(ToGui(new Rect(origin.x - r, origin.y - r, r * 2f, r * 2f)), _stickFinger != NoFinger ? "" : "Move", _buttonStyle);
            if (_stickFinger != NoFinger)
            {
                Vector2 knob = origin + Move * r;
                float k = r * 0.45f;
                GUI.Box(ToGui(new Rect(knob.x - k, knob.y - k, k * 2f, k * 2f)), "", _buttonStyle);
            }

            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            for (int b = 0; b < ButtonCount; b++)
            {
                TouchButton button = (TouchButton)b;
                if (!IsVisible(button))
                {
                    continue;
                }
                bool held = _buttonFinger[b] != NoFinger;
                GUI.Box(ToGui(ButtonRect(button)), Label(button), held ? _buttonHeldStyle : _buttonStyle);
            }
            GUI.color = previous;
        }
    }
}
#endif
