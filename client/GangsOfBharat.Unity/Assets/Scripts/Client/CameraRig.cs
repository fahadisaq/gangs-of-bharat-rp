using Unity.Cinemachine;
using UnityEngine;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Third-person camera with Cinemachine 3. Scene setup: a Main Camera with a CinemachineBrain,
    /// and two CinemachineCameras that track the local player's visual transform:
    ///  - onFootCamera: CinemachineOrbitalFollow (radius about 4 m) + CinemachineRotationComposer,
    ///    with a CinemachineInputAxisController so a touch drag on the right half of the screen
    ///    (or the mouse / right stick) orbits the camera.
    ///  - vehicleCamera: same components with a larger radius (about 7 m) and lower damping.
    /// The rig switches by priority; the brain blends between them.
    /// UNVERIFIED: written against Cinemachine 3.1 APIs, not compiled in the sandbox.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public enum Mode
        {
            OnFoot,
            Vehicle,
        }

        [SerializeField] private CinemachineCamera onFootCamera;
        [SerializeField] private CinemachineCamera vehicleCamera;
        [SerializeField] private Transform target;
        [SerializeField] private int activePriority = 20;
        [SerializeField] private int inactivePriority = 10;

        public Mode Current { get; private set; }

        private void Start()
        {
            if (target != null)
            {
                SetTarget(target);
            }
            SetMode(Mode.OnFoot);
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (onFootCamera != null)
            {
                onFootCamera.Target.TrackingTarget = newTarget;
            }
            if (vehicleCamera != null)
            {
                vehicleCamera.Target.TrackingTarget = newTarget;
            }
        }

        public void SetMode(Mode mode)
        {
            Current = mode;
            if (onFootCamera != null)
            {
                onFootCamera.Priority.Value = mode == Mode.OnFoot ? activePriority : inactivePriority;
            }
            if (vehicleCamera != null)
            {
                vehicleCamera.Priority.Value = mode == Mode.Vehicle ? activePriority : inactivePriority;
            }
        }
    }
}
