using System.Collections.Generic;
using UnityEngine;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// A scene-placed vehicle for offline test mode (ClientPrediction.IsOffline). There is no
    /// server to confirm EnterVehicle/ExitVehicle, so VehicleController enters it locally and
    /// the same shared Movement.StepVehicle model drives it; this component only moves the
    /// visual to the predicted vehicle pose and hides the driver while seated.
    /// Networked vehicles do not use this; they come from EntityInterpolator.
    /// UNVERIFIED: written against Unity 6 APIs, compiled only against stubs in the sandbox.
    /// </summary>
    public sealed class OfflineVehicle : MonoBehaviour
    {
        private static readonly List<OfflineVehicle> Active = new List<OfflineVehicle>();
        private static int _nextId = -1;

        [Tooltip("VehicleCatalog id used for the driving model (5 = Marutha Chhotu).")]
        [SerializeField] private int modelId = 5;

        private ClientPrediction _driver;
        private Renderer[] _hiddenDriverRenderers;

        /// <summary>Local-only id, negative so it never clashes with server entity ids.</summary>
        public int Id { get; private set; }
        public int ModelId { get { return modelId; } }

        private void Awake()
        {
            Id = _nextId--;
        }

        private void OnEnable()
        {
            Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        /// <summary>Nearest free offline vehicle within range of a point, or null.</summary>
        public static OfflineVehicle FindNearest(Vector3 point, float range)
        {
            OfflineVehicle best = null;
            float bestSq = range * range;
            for (int i = 0; i < Active.Count; i++)
            {
                OfflineVehicle v = Active[i];
                if (v == null || v._driver != null)
                {
                    continue;
                }
                float sq = (v.transform.position - point).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = v;
                }
            }
            return best;
        }

        public void SetDriver(ClientPrediction driver)
        {
            _driver = driver;
            _hiddenDriverRenderers = null;
            if (driver != null && driver.Visual != null)
            {
                _hiddenDriverRenderers = driver.Visual.GetComponentsInChildren<Renderer>();
                SetRenderers(_hiddenDriverRenderers, false);
            }
        }

        /// <summary>Leaves the vehicle parked at the given pose and shows the driver again.</summary>
        public void ClearDriver(Vector3 position, float yawRadians)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawRadians * Mathf.Rad2Deg, 0f));
            SetRenderers(_hiddenDriverRenderers, true);
            _hiddenDriverRenderers = null;
            _driver = null;
        }

        private static void SetRenderers(Renderer[] renderers, bool visible)
        {
            if (renderers == null)
            {
                return;
            }
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = visible;
                }
            }
        }

        private void LateUpdate()
        {
            if (_driver == null || !_driver.InVehicle || _driver.VehicleId != Id)
            {
                return;
            }
            transform.SetPositionAndRotation(_driver.PredictedPosition,
                Quaternion.Euler(0f, _driver.PredictedYaw * Mathf.Rad2Deg, 0f));
        }
    }
}
