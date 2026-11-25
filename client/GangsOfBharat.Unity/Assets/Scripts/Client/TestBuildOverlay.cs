using UnityEngine;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Corner label for test builds: build name, FPS and a short controls hint (IMGUI).
    /// UNVERIFIED: written against Unity 6 APIs, compiled only against stubs in the sandbox.
    /// </summary>
    public sealed class TestBuildOverlay : MonoBehaviour
    {
        [SerializeField] private string title = "Gangs of Bharat test build v0.1";
        [Tooltip("Optional: shows whether the game runs in offline test mode.")]
        [SerializeField] private ClientPrediction prediction;

        private float _smoothedDelta = 1f / 60f;
        private GUIStyle _style;
        private int _styleScreenHeight;

        private void Update()
        {
            _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);
        }

        private void OnGUI()
        {
            if (_style == null || _styleScreenHeight != Screen.height)
            {
                _styleScreenHeight = Screen.height;
                _style = new GUIStyle(GUI.skin.label);
                _style.fontSize = Mathf.Max(12, (int)(Mathf.Min(Screen.width, Screen.height) * 0.03f));
                _style.normal.textColor = Color.white;
            }
            float fps = _smoothedDelta > 0f ? 1f / _smoothedDelta : 0f;
            string mode = prediction == null ? "" : prediction.IsOffline ? "\nOffline test mode" : "";
            string drive = prediction != null && prediction.InVehicle ? "Driving: left stick, Brake, Horn, Exit" : "Left: move  Right: look  Enter near the car";
            string text = title + "\nFPS " + Mathf.RoundToInt(fps) + mode + "\n" + drive;
            float pad = _style.fontSize * 0.6f;
            Rect rect = new Rect(pad, pad, Screen.width * 0.6f, _style.fontSize * 6f);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, _style);
            GUI.color = previous;
            GUI.Label(rect, text, _style);
        }
    }
}
