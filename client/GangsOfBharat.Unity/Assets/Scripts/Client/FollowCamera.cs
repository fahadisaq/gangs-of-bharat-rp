using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Minimal third-person orbit camera that needs no Cinemachine input. Used by the generated
    /// test scene (CameraRig's CinemachineInputAxisController reads the Input System, which is
    /// not active in the current build).
    /// Look input: legacy Input Manager = TouchControls right-half drag (left mouse drag on the
    /// right half in the editor) and the arrow keys; Input System = right mouse button drag and
    /// the gamepad right stick.
    /// In a vehicle it drifts back behind the vehicle when the player is not looking around.
    /// Runs after other scripts (execution order 100) so it sees this frame's player position.
    /// UNVERIFIED: written against Unity 6 APIs, compiled only against stubs in the sandbox.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [Tooltip("Optional: switches to the vehicle distance while driving.")]
        [SerializeField] private ClientPrediction prediction;
        [SerializeField] private float onFootDistance = 5f;
        [SerializeField] private float vehicleDistance = 8.5f;
        [SerializeField] private float pivotHeight = 1.6f;
        [SerializeField] private float startPitch = 15f;
        [Tooltip("Degrees of yaw for a drag across the full screen width.")]
        [SerializeField] private float dragDegreesPerScreen = 220f;
        [SerializeField] private float keyDegreesPerSecond = 120f;
        [SerializeField] private float vehicleRealignSpeed = 2.5f;

        private float _yaw;
        private float _pitch;
        private float _distance;
        private bool _initialised;

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }
            bool inVehicle = prediction != null && prediction.InVehicle;
            float wantedDistance = inVehicle ? vehicleDistance : onFootDistance;
            if (!_initialised)
            {
                _yaw = target.eulerAngles.y;
                _pitch = startPitch;
                _distance = wantedDistance;
                _initialised = true;
            }

            Vector2 look = ReadLookDegrees();
            _yaw += look.x;
            _pitch = Mathf.Clamp(_pitch - look.y, -5f, 60f);
            if (inVehicle && look.sqrMagnitude < 0.0001f)
            {
                _yaw = Mathf.LerpAngle(_yaw, target.eulerAngles.y, 1f - Mathf.Exp(-vehicleRealignSpeed * Time.deltaTime));
            }
            _distance = Mathf.Lerp(_distance, wantedDistance, 1f - Mathf.Exp(-3f * Time.deltaTime));

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = target.position + Vector3.up * pivotHeight;
            Vector3 position = pivot - rotation * Vector3.forward * _distance;
            if (position.y < 0.4f)
            {
                position.y = 0.4f;
            }
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(pivot - position, Vector3.up));
        }

        // x = yaw degrees, y = pitch degrees (positive = drag up = look up) for this frame.
        private Vector2 ReadLookDegrees()
        {
            float perPixel = dragDegreesPerScreen / Mathf.Max(1, Screen.width);
            Vector2 degrees = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                degrees += mouse.delta.ReadValue() * perPixel;
            }
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                degrees += pad.rightStick.ReadValue() * keyDegreesPerSecond * Time.deltaTime;
            }
#else
            degrees += TouchControls.Instance.LookDelta * perPixel;
            float keys = 0f;
            if (Input.GetKey(KeyCode.RightArrow))
            {
                keys += 1f;
            }
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                keys -= 1f;
            }
            degrees.x += keys * keyDegreesPerSecond * Time.deltaTime;
#endif
            return degrees;
        }
    }
}
