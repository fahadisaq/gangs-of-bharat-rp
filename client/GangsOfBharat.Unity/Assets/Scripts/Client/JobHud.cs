using System.Globalization;
using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Localization;
using GangsOfBharat.Shared.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Taxi and food delivery job HUD. Start buttons send JobStart; the server replies with
    /// Accepted and a waypoint. The HUD shows a world marker, the distance and the job name, and
    /// the server pays automatically (JobComplete) when the player reaches the waypoint. Pay is
    /// decided by the server only; the client never computes money.
    /// UNVERIFIED: written against Unity 6 / uGUI / TextMeshPro APIs, not compiled here.
    /// </summary>
    public sealed class JobHud : MonoBehaviour
    {
        [SerializeField] private ClientPrediction player;
        [SerializeField] private Button taxiButton;
        [SerializeField] private TMP_Text taxiLabel;
        [SerializeField] private Button foodButton;
        [SerializeField] private TMP_Text foodLabel;
        [SerializeField] private GameObject activePanel;
        [SerializeField] private TMP_Text jobName;
        [SerializeField] private TMP_Text distanceText;
        [SerializeField] private TMP_Text resultText;
        [Tooltip("World-space marker (for example a tall light beam) placed at the waypoint.")]
        [SerializeField] private Transform waypointMarker;

        private readonly JobStartMessage _start = new JobStartMessage();
        private int _activeJobId;
        private Vector3 _waypoint;

        private void Awake()
        {
            if (taxiButton != null)
            {
                taxiButton.onClick.AddListener(() => RequestJob(JobId.TaxiDriver));
            }
            if (foodButton != null)
            {
                foodButton.onClick.AddListener(() => RequestJob(JobId.FoodDelivery));
            }
        }

        private void OnEnable()
        {
            LanguageSettings.Changed += OnLanguageChanged;
            if (NetClient.Instance != null)
            {
                NetClient.Instance.OtherReceived += OnServerMessage;
            }
            SetActiveJob(0);
            RefreshLabels();
        }

        private void OnDisable()
        {
            LanguageSettings.Changed -= OnLanguageChanged;
            if (NetClient.Instance != null)
            {
                NetClient.Instance.OtherReceived -= OnServerMessage;
            }
        }

        private void OnLanguageChanged(string language)
        {
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (taxiLabel != null) taxiLabel.text = ClientStrings.Get("job.start_taxi");
            if (foodLabel != null) foodLabel.text = ClientStrings.Get("job.start_food");
            if (jobName != null && _activeJobId != 0)
            {
                JobDef def = JobCatalog.Get(_activeJobId);
                jobName.text = def != null ? LocalizationTable.Get(LanguageSettings.Current, def.NameKey) : string.Empty;
            }
        }

        public void RequestJob(JobId job)
        {
            if (_activeJobId != 0 || NetClient.Instance == null)
            {
                return;
            }
            _start.JobId = (int)job;
            _start.Accepted = false;
            _start.WaypointX = 0f;
            _start.WaypointZ = 0f;
            NetClient.Instance.SendReliable(_start);
        }

        private void OnServerMessage(NetMessage message)
        {
            JobStartMessage start = message as JobStartMessage;
            if (start != null)
            {
                if (start.Accepted)
                {
                    _waypoint = new Vector3(start.WaypointX, 0f, start.WaypointZ);
                    SetActiveJob(start.JobId);
                    if (resultText != null) resultText.text = string.Empty;
                }
                else if (resultText != null)
                {
                    resultText.text = ClientStrings.Get("job.rejected");
                }
                return;
            }
            JobCompleteMessage done = message as JobCompleteMessage;
            if (done != null)
            {
                SetActiveJob(0);
                if (resultText != null)
                {
                    resultText.text = ClientStrings.Format("job.paid",
                        done.PaymentRupees.ToString("N0", CultureInfo.InvariantCulture),
                        done.WalletRupees.ToString("N0", CultureInfo.InvariantCulture));
                }
            }
        }

        private void SetActiveJob(int jobId)
        {
            _activeJobId = jobId;
            bool active = jobId != 0;
            if (activePanel != null) activePanel.SetActive(active);
            if (waypointMarker != null)
            {
                waypointMarker.gameObject.SetActive(active);
                waypointMarker.position = _waypoint;
            }
            if (taxiButton != null) taxiButton.interactable = !active;
            if (foodButton != null) foodButton.interactable = !active;
            RefreshLabels();
        }

        private void Update()
        {
            if (_activeJobId == 0 || player == null || distanceText == null)
            {
                return;
            }
            Vector3 p = player.PredictedPosition;
            float dx = _waypoint.x - p.x;
            float dz = _waypoint.z - p.z;
            int metres = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz));
            distanceText.text = ClientStrings.Format("job.distance", metres);
        }
    }
}
