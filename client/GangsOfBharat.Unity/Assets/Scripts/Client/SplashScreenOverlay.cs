using UnityEngine;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Full-screen loading image (aspect fill, cropped) with the game title, shown for
    /// <c>duration</c> seconds at start and then faded out (IMGUI, drawn above everything else).
    /// DialoguePlayer waits while <see cref="Showing"/> is true.
    /// Without an image the overlay disables itself at once.
    /// UNVERIFIED: written against Unity 6 APIs, compiled only against stubs in the sandbox.
    /// </summary>
    public sealed class SplashScreenOverlay : MonoBehaviour
    {
        [SerializeField] private Texture2D image;
        [SerializeField] private string title = "GANGS OF BHARAT";
        [SerializeField] private float duration = 2.5f;
        [SerializeField] private float fadeDuration = 0.6f;

        private float _elapsed;
        private Texture2D _black;
        private GUIStyle _titleStyle;
        private int _styleScreenHeight;

        /// <summary>True from Awake until the fade-out has finished.</summary>
        public static bool Showing { get; private set; }

        /// <summary>Editor setup (TestSceneBuilder).</summary>
        public void Configure(Texture2D texture, string titleText, float seconds)
        {
            image = texture;
            title = titleText;
            duration = seconds;
        }

        private void Awake()
        {
            if (image == null)
            {
                Showing = false;
                enabled = false;
                return;
            }
            Showing = true;
            _black = new Texture2D(1, 1);
            _black.SetPixel(0, 0, Color.black);
            _black.Apply();
        }

        private void Update()
        {
            // Frame time, capped, so a long first frame (scene load) does not eat the splash.
            _elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_elapsed >= duration + fadeDuration)
            {
                enabled = false;
            }
        }

        private void OnDisable()
        {
            Showing = false;
        }

        private void OnGUI()
        {
            if (image == null)
            {
                return;
            }
            GUI.depth = -1000;
            float alpha = _elapsed <= duration ? 1f : 1f - Mathf.Clamp01((_elapsed - duration) / Mathf.Max(0.01f, fadeDuration));
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(screen, _black);
            GUI.DrawTexture(screen, image, ScaleMode.ScaleAndCrop);

            if (!string.IsNullOrEmpty(title))
            {
                if (_titleStyle == null || _styleScreenHeight != Screen.height)
                {
                    _styleScreenHeight = Screen.height;
                    _titleStyle = new GUIStyle(GUI.skin.label);
                    _titleStyle.fontSize = Mathf.Max(24, (int)(Screen.height * 0.09f));
                    _titleStyle.fontStyle = FontStyle.Bold;
                    _titleStyle.alignment = TextAnchor.MiddleCenter;
                    _titleStyle.normal.textColor = Color.white;
                }
                // Lower third (the loading_screen prompt keeps it calm), with a drop shadow.
                Rect titleRect = new Rect(0f, Screen.height * 0.68f, Screen.width, Screen.height * 0.2f);
                float shadow = Mathf.Max(2f, _titleStyle.fontSize * 0.05f);
                GUI.color = new Color(0f, 0f, 0f, alpha * 0.8f);
                GUI.Label(new Rect(titleRect.x + shadow, titleRect.y + shadow, titleRect.width, titleRect.height), title,
                    _titleStyle);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(titleRect, title, _titleStyle);
            }
            GUI.color = previous;
        }
    }
}
