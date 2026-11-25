using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GangsOfBharat.Shared.Localization;
using UnityEngine;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Plays a voiced dialogue script (for now the intro, Assets/Localization/Dialogue/onboarding_intro.json,
    /// same schema as tools/content/data/onboarding_dialogue.json) with IMGUI subtitles.
    ///  - Lines play in order. Each line shows the speaker name and the text in the current
    ///    language (LanguageSettings.Current) and plays its clip; it advances 0.4 s after the clip
    ///    ends, or when the subtitle panel is tapped/clicked. Without a clip the text stays for a
    ///    time based on its length.
    ///  - Skip ends the dialogue, the EN / हिंदी button switches language (LanguageSettings.Set) and
    ///    restarts the current line, the Story button (top right) replays from the start.
    ///  - Waits while SplashScreenOverlay.Showing is true, then startDelay seconds.
    /// Input is read only through IMGUI events, so it works with either input handler.
    /// KNOWN LIMIT: IMGUI does not shape Devanagari; conjuncts and matras may render as separate
    /// glyphs (and need a device font with Devanagari glyphs). Real UI should use a shaping-capable
    /// text renderer.
    /// If the script is missing or cannot be parsed, a warning is logged and the component disables itself.
    /// UNVERIFIED: written against Unity 6 APIs, compiled only against stubs in the sandbox.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class DialoguePlayer : MonoBehaviour
    {
        private const float ClipGap = 0.4f;

        [SerializeField] private TextAsset script;
        [Tooltip("Line ids, parallel to enClips and hiClips.")]
        [SerializeField] private string[] lineIds = new string[0];
        [SerializeField] private AudioClip[] enClips = new AudioClip[0];
        [SerializeField] private AudioClip[] hiClips = new AudioClip[0];
        [SerializeField] private bool playOnStart = true;
        [Tooltip("Seconds to wait after the splash screen (if any) before the first line.")]
        [SerializeField] private float startDelay = 0.5f;

        [Serializable]
        private sealed class DialogueFile
        {
            public string id;
            public DialogueLine[] lines;
        }

        [Serializable]
        private sealed class DialogueLine
        {
            public string id;
            public string speaker;
            public string en;
            public string hi;
        }

        private readonly Dictionary<string, int> _clipIndex = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _speakerNames = new Dictionary<string, string>();
        private DialogueLine[] _lines;
        private AudioSource _audio;
        private bool _playing;
        private bool _pendingStart;
        private float _startAt = -1f;
        private int _index;
        private float _lineEndsAt;
        private GUIStyle _panelStyle;
        private GUIStyle _speakerStyle;
        private GUIStyle _textStyle;
        private GUIStyle _buttonStyle;
        private int _styleScreenHeight;

        /// <summary>True while a line is on screen.</summary>
        public bool IsPlaying
        {
            get { return _playing; }
        }

        /// <summary>Editor setup (TestSceneBuilder): script, parallel clip arrays and options.</summary>
        public void Configure(TextAsset dialogue, string[] ids, AudioClip[] english, AudioClip[] hindi,
            bool autoPlay, float delay)
        {
            script = dialogue;
            lineIds = ids ?? new string[0];
            enClips = english ?? new AudioClip[0];
            hiClips = hindi ?? new AudioClip[0];
            playOnStart = autoPlay;
            startDelay = delay;
        }

        private void Awake()
        {
            _audio = GetComponent<AudioSource>();
            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
            }
            _audio.playOnAwake = false;
            _audio.loop = false;
            _audio.spatialBlend = 0f;

            if (!Load())
            {
                enabled = false;
                return;
            }
            for (int i = 0; i < lineIds.Length; i++)
            {
                if (!string.IsNullOrEmpty(lineIds[i]))
                {
                    _clipIndex[lineIds[i]] = i;
                }
            }
        }

        private void OnEnable()
        {
            LanguageSettings.Changed += OnLanguageChanged;
        }

        private void OnDisable()
        {
            LanguageSettings.Changed -= OnLanguageChanged;
        }

        private void Start()
        {
            _pendingStart = playOnStart;
        }

        /// <summary>Starts (or restarts) the dialogue from the first line.</summary>
        public void Play()
        {
            if (_lines == null || _lines.Length == 0)
            {
                return;
            }
            _pendingStart = false;
            _playing = true;
            PlayLine(0);
        }

        /// <summary>Ends the dialogue now.</summary>
        public void Stop()
        {
            _pendingStart = false;
            _playing = false;
            if (_audio != null)
            {
                _audio.Stop();
            }
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (_pendingStart)
            {
                if (SplashScreenOverlay.Showing)
                {
                    _startAt = -1f;
                    return;
                }
                if (_startAt < 0f)
                {
                    _startAt = now + Mathf.Max(0f, startDelay);
                }
                if (now >= _startAt)
                {
                    Play();
                }
                return;
            }
            if (_playing && now >= _lineEndsAt)
            {
                Advance();
            }
        }

        private void Advance()
        {
            if (_index + 1 < _lines.Length)
            {
                PlayLine(_index + 1);
            }
            else
            {
                Stop();
            }
        }

        private void PlayLine(int index)
        {
            _index = index;
            DialogueLine line = _lines[index];
            AudioClip clip = ClipFor(line.id, IsHindi);
            _audio.Stop();
            if (clip != null)
            {
                _audio.clip = clip;
                _audio.Play();
                _lineEndsAt = Time.unscaledTime + clip.length + ClipGap;
            }
            else
            {
                _audio.clip = null;
                _lineEndsAt = Time.unscaledTime + TextDuration(TextFor(line));
            }
        }

        private void OnLanguageChanged(string language)
        {
            if (_playing)
            {
                PlayLine(_index);
            }
        }

        private static bool IsHindi
        {
            get { return LanguageSettings.Current == LocalizationTable.Hindi; }
        }

        private static float TextDuration(string text)
        {
            int length = string.IsNullOrEmpty(text) ? 0 : text.Length;
            return Mathf.Clamp(1.5f + length * 0.06f, 2.5f, 9f);
        }

        private static string TextFor(DialogueLine line)
        {
            if (IsHindi && !string.IsNullOrEmpty(line.hi))
            {
                return line.hi;
            }
            return line.en ?? string.Empty;
        }

        private AudioClip ClipFor(string lineId, bool hindi)
        {
            int i;
            if (string.IsNullOrEmpty(lineId) || !_clipIndex.TryGetValue(lineId, out i))
            {
                return null;
            }
            AudioClip[] clips = hindi ? hiClips : enClips;
            return clips != null && i < clips.Length ? clips[i] : null;
        }

        private string SpeakerName(string key)
        {
            string name;
            if (!string.IsNullOrEmpty(key) && _speakerNames.TryGetValue(key, out name))
            {
                return name;
            }
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }
            return char.ToUpperInvariant(key[0]) + key.Substring(1);
        }

        // ---------------------------------------------------------------- parsing

        private bool Load()
        {
            if (script == null)
            {
                Debug.LogWarning("DialoguePlayer: no dialogue script assigned; disabled.");
                return false;
            }
            string json = script.text;
            DialogueFile file = null;
            try
            {
                file = JsonUtility.FromJson<DialogueFile>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("DialoguePlayer: cannot parse " + script.name + ": " + e.Message);
            }
            if (file == null || file.lines == null || file.lines.Length == 0)
            {
                Debug.LogWarning("DialoguePlayer: " + script.name + " has no lines; disabled.");
                return false;
            }
            List<DialogueLine> lines = new List<DialogueLine>();
            foreach (DialogueLine line in file.lines)
            {
                if (line != null && (!string.IsNullOrEmpty(line.en) || !string.IsNullOrEmpty(line.hi)))
                {
                    lines.Add(line);
                }
            }
            if (lines.Count == 0)
            {
                Debug.LogWarning("DialoguePlayer: " + script.name + " has no text; disabled.");
                return false;
            }
            _lines = lines.ToArray();
            foreach (DialogueLine line in _lines)
            {
                if (!string.IsNullOrEmpty(line.speaker) && !_speakerNames.ContainsKey(line.speaker))
                {
                    string name = ReadSpeakerName(json, line.speaker);
                    if (name != null)
                    {
                        _speakerNames[line.speaker] = name;
                    }
                }
            }
            return true;
        }

        // JsonUtility cannot read the "speakers" map, so the display name is read by hand:
        // find "speakers": { ... "<key>": { ... "name": "<value>" ... } }. Returns null if not found.
        private static string ReadSpeakerName(string json, string key)
        {
            try
            {
                Match speakers = Regex.Match(json, "\"speakers\"\\s*:\\s*\\{");
                if (!speakers.Success)
                {
                    return null;
                }
                int start = speakers.Index + speakers.Length - 1;
                int end = MatchingBrace(json, start);
                if (end < 0)
                {
                    return null;
                }
                string block = json.Substring(start, end - start + 1);
                Match entry = Regex.Match(block, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{");
                if (!entry.Success)
                {
                    return null;
                }
                int entryStart = entry.Index + entry.Length - 1;
                int entryEnd = MatchingBrace(block, entryStart);
                if (entryEnd < 0)
                {
                    return null;
                }
                string obj = block.Substring(entryStart, entryEnd - entryStart + 1);
                Match name = Regex.Match(obj, "\"name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                return name.Success ? Regex.Unescape(name.Groups[1].Value) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Index of the '}' closing the '{' at open, skipping braces inside strings; -1 if none.
        private static int MatchingBrace(string text, int open)
        {
            int depth = 0;
            bool inString = false;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }
                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        // ---------------------------------------------------------------- drawing

        private void EnsureStyles()
        {
            if (_panelStyle != null && _styleScreenHeight == Screen.height)
            {
                return;
            }
            _styleScreenHeight = Screen.height;
            int size = Mathf.Max(18, (int)(Screen.height * 0.042f));

            Texture2D background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            background.Apply();
            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = background;

            _speakerStyle = new GUIStyle(GUI.skin.label);
            _speakerStyle.fontSize = (int)(size * 0.8f);
            _speakerStyle.fontStyle = FontStyle.Bold;
            _speakerStyle.normal.textColor = new Color(1f, 0.78f, 0.25f);
            _speakerStyle.alignment = TextAnchor.UpperLeft;

            _textStyle = new GUIStyle(GUI.skin.label);
            _textStyle.fontSize = size;
            _textStyle.wordWrap = true;
            _textStyle.normal.textColor = Color.white;
            _textStyle.alignment = TextAnchor.UpperLeft;

            _buttonStyle = new GUIStyle(GUI.skin.button);
            _buttonStyle.fontSize = (int)(size * 0.75f);
        }

        private void OnGUI()
        {
            if (_lines == null || SplashScreenOverlay.Showing)
            {
                return;
            }
            GUI.depth = -10;
            EnsureStyles();
            float h = Screen.height;
            float w = Screen.width;
            float pad = h * 0.02f;
            float buttonH = _buttonStyle.fontSize * 2.2f;
            float buttonW = _buttonStyle.fontSize * 5.5f;

            if (!_playing)
            {
                if (!_pendingStart && GUI.Button(new Rect(w - buttonW - pad, pad, buttonW, buttonH), "Story", _buttonStyle))
                {
                    Play();
                }
                return;
            }

            // Bottom centre, kept clear of the TouchControls buttons (bottom right, about 0.4 x
            // screen height wide) by reserving the same width on both sides.
            float reserve = h * 0.42f;
            float panelW = Mathf.Min(w * 0.7f, w - 2f * reserve);
            if (panelW < w * 0.45f)
            {
                panelW = w * 0.45f;
            }
            float panelH = _textStyle.fontSize * 4.6f;
            Rect panel = new Rect((w - panelW) * 0.5f, h - panelH - pad * 2f, panelW, panelH);

            DialogueLine line = _lines[_index];
            GUI.Box(panel, GUIContent.none, _panelStyle);
            float inner = _textStyle.fontSize * 0.6f;
            Rect speakerRect = new Rect(panel.x + inner, panel.y + inner * 0.6f, panel.width - inner * 2f,
                _speakerStyle.fontSize * 1.5f);
            GUI.Label(speakerRect, SpeakerName(line.speaker), _speakerStyle);
            Rect textRect = new Rect(panel.x + inner, speakerRect.yMax, panel.width - inner * 2f,
                panel.yMax - speakerRect.yMax - inner * 0.5f);
            GUI.Label(textRect, TextFor(line), _textStyle);

            // Tap or click on the panel: next line.
            if (GUI.Button(panel, GUIContent.none, GUIStyle.none))
            {
                Advance();
                return;
            }

            float by = panel.y - buttonH - pad * 0.5f;
            if (GUI.Button(new Rect(panel.xMax - buttonW, by, buttonW, buttonH), "Skip", _buttonStyle))
            {
                Stop();
                return;
            }
            string languageLabel = IsHindi ? "EN" : "हिंदी";
            if (GUI.Button(new Rect(panel.xMax - buttonW * 2f - pad * 0.5f, by, buttonW, buttonH), languageLabel,
                _buttonStyle))
            {
                ToggleLanguage();
            }
        }

        private void ToggleLanguage()
        {
            try
            {
                // Raises LanguageSettings.Changed, which restarts the current line.
                LanguageSettings.Set(IsHindi ? LocalizationTable.English : LocalizationTable.Hindi);
            }
            catch (Exception e)
            {
                // Current is already switched before the Localization package is touched.
                Debug.LogWarning("DialoguePlayer: language switch: " + e.Message);
                if (_playing)
                {
                    PlayLine(_index);
                }
            }
        }
    }
}
