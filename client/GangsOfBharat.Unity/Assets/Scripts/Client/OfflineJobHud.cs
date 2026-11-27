using System.Globalization;
using GangsOfBharat.Shared.Gameplay;
using UnityEngine;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Single-player job loop for offline test builds: pick a job, drive to the waypoint, get paid.
    ///
    /// This is deliberately NOT the real economy. In live play <see cref="JobHud"/> is the real HUD:
    /// it sends JobStart over the network and the server alone decides the waypoint and the payment.
    /// That HUD does nothing without a server, so the test APK had no playable loop at all. This
    /// component fills that gap the same way <see cref="OfflineVehicle"/> fills the driving gap, and
    /// it must never be used in a networked build - see TestSceneBuilder, which only adds it when
    /// ClientPrediction is in offline test mode.
    ///
    /// Drawn with IMGUI on purpose. JobHud and ChatUI use TextMeshPro, but this project has no TMP
    /// font assets committed, so TMP text renders as nothing in a cloud build. IMGUI uses the
    /// built-in font and always draws. Money is shown as "Rs." rather than the rupee sign because
    /// the built-in font has no glyph for it.
    /// </summary>
    public sealed class OfflineJobHud : MonoBehaviour
    {
        [Tooltip("Player prediction, used for position and whether the player is in a vehicle.")]
        [SerializeField] private ClientPrediction prediction;

        [Tooltip("Starting cash, matching the intro story: five hundred rupees and one bag.")]
        [SerializeField] private long startingWalletRupees = 500;

        [Tooltip("How close the player must get to the waypoint, in metres.")]
        [SerializeField] private float arriveRadius = 7f;

        [Tooltip("Centre of the test district in world space.")]
        [SerializeField] private Vector3 districtCentre = new Vector3(256f, 0f, 256f);

        [Tooltip("How far along a road a waypoint may be placed, in metres.")]
        [SerializeField] private float waypointSpread = 110f;

        private long _wallet;
        private int _activeJobId;
        private Vector3 _waypoint;
        private float _startDistance;
        private string _result = string.Empty;
        private float _resultUntil;
        private GUIStyle _label;
        private GUIStyle _button;
        private GUIStyle _marker;
        private int _styleHeight;

        private void Awake()
        {
            _wallet = startingWalletRupees;
        }

        private void BuildStyles()
        {
            if (_label != null && _styleHeight == Screen.height)
            {
                return;
            }
            _styleHeight = Screen.height;
            int size = Mathf.Max(12, (int)(Mathf.Min(Screen.width, Screen.height) * 0.032f));
            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = size;
            _label.normal.textColor = Color.white;
            _label.alignment = TextAnchor.UpperRight;
            _button = new GUIStyle(GUI.skin.button);
            _button.fontSize = size;
            _marker = new GUIStyle(GUI.skin.label);
            _marker.fontSize = size;
            _marker.alignment = TextAnchor.MiddleCenter;
            _marker.normal.textColor = new Color(1f, 0.85f, 0.1f);
        }

        /// <summary>Distance from the player to the active waypoint, in metres.</summary>
        private float DistanceToWaypoint()
        {
            Vector3 p = prediction != null ? prediction.PredictedPosition : transform.position;
            float dx = _waypoint.x - p.x;
            float dz = _waypoint.z - p.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Places a waypoint on one of the two district roads: the main road runs along Z through
        /// the centre, the cross road runs along X forty metres further along.
        /// </summary>
        private Vector3 PickWaypoint()
        {
            bool mainRoad = Random.value < 0.5f;
            float along = Random.Range(-waypointSpread, waypointSpread);
            if (Mathf.Abs(along) < 25f)
            {
                along = along < 0f ? -25f : 25f;
            }
            if (mainRoad)
            {
                return new Vector3(districtCentre.x, 0f, districtCentre.z + along);
            }
            return new Vector3(districtCentre.x + along, 0f, districtCentre.z + 40f);
        }

        /// <summary>
        /// Offline payment: the job's mid hourly rate scaled by trip length, so a longer fare pays
        /// more. One hundred metres is treated as one minute of work. The live server uses its own
        /// rules; this only has to feel reasonable in a test build.
        /// </summary>
        private long PayFor(JobDef def, float metres)
        {
            if (def == null)
            {
                return 0;
            }
            float minutes = Mathf.Max(1f, metres / 100f);
            long pay = (long)(def.MidRupeesPerHour * minutes / 60f);
            return pay < 50 ? 50 : pay;
        }

        private void StartJob(JobId job)
        {
            JobDef def = JobCatalog.Get((int)job);
            if (def == null || _activeJobId != 0)
            {
                return;
            }
            if (def.NeedsVehicle && (prediction == null || !prediction.InVehicle))
            {
                _result = "Get into the taxi first.";
                _resultUntil = Time.unscaledTime + 3f;
                return;
            }
            _activeJobId = (int)job;
            _waypoint = PickWaypoint();
            _startDistance = DistanceToWaypoint();
            _result = string.Empty;
        }

        private void CancelJob()
        {
            _activeJobId = 0;
            _result = "Job cancelled.";
            _resultUntil = Time.unscaledTime + 3f;
        }

        private void Update()
        {
            if (_activeJobId == 0)
            {
                return;
            }
            if (DistanceToWaypoint() > arriveRadius)
            {
                return;
            }
            JobDef def = JobCatalog.Get(_activeJobId);
            long pay = PayFor(def, _startDistance);
            _wallet += pay;
            _activeJobId = 0;
            _result = "Delivered. Paid Rs. " + pay.ToString("N0", CultureInfo.InvariantCulture);
            _resultUntil = Time.unscaledTime + 5f;
        }

        private void OnGUI()
        {
            BuildStyles();
            float pad = _label.fontSize * 0.6f;
            float lineHeight = _label.fontSize * 1.5f;
            float panelWidth = Mathf.Min(Screen.width * 0.42f, _label.fontSize * 12f);
            float right = Screen.width - panelWidth - pad;

            Shadowed(new Rect(right, pad, panelWidth, lineHeight),
                "Rs. " + _wallet.ToString("N0", CultureInfo.InvariantCulture), _label);

            float y = pad + lineHeight;

            if (_activeJobId != 0)
            {
                JobDef def = JobCatalog.Get(_activeJobId);
                string name = def != null ? def.Key.Replace('_', ' ') : "job";
                Shadowed(new Rect(right, y, panelWidth, lineHeight), name, _label);
                y += lineHeight;
                Shadowed(new Rect(right, y, panelWidth, lineHeight),
                    Mathf.RoundToInt(DistanceToWaypoint()) + " m to go", _label);
                y += lineHeight;
                if (GUI.Button(new Rect(right, y, panelWidth, lineHeight * 1.2f), "Cancel job", _button))
                {
                    CancelJob();
                }
                DrawWaypointMarker();
            }
            else
            {
                if (GUI.Button(new Rect(right, y, panelWidth, lineHeight * 1.2f), "Taxi fare", _button))
                {
                    StartJob(JobId.TaxiDriver);
                }
                y += lineHeight * 1.4f;
                if (GUI.Button(new Rect(right, y, panelWidth, lineHeight * 1.2f), "Food delivery", _button))
                {
                    StartJob(JobId.FoodDelivery);
                }
                y += lineHeight * 1.4f;
            }

            if (!string.IsNullOrEmpty(_result) && Time.unscaledTime < _resultUntil)
            {
                Shadowed(new Rect(right, y + lineHeight * 0.3f, panelWidth, lineHeight * 2f), _result, _label);
            }
        }

        /// <summary>Projects the waypoint to screen space so no world marker object is needed.</summary>
        private void DrawWaypointMarker()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            Vector3 screen = cam.WorldToScreenPoint(_waypoint + new Vector3(0f, 2f, 0f));
            if (screen.z <= 0f)
            {
                return;
            }
            float size = _marker.fontSize * 2f;
            Rect rect = new Rect(screen.x - size * 0.5f, Screen.height - screen.y - size * 0.5f, size, size);
            Shadowed(rect, "[ ]", _marker);
        }

        private void Shadowed(Rect rect, string text, GUIStyle style)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
            GUI.color = previous;
            GUI.Label(rect, text, style);
        }
    }
}
