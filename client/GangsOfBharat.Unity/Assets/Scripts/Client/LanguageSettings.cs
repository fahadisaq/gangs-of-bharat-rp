using System;
using GangsOfBharat.Shared.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// English / Hindi toggle. The choice is saved in PlayerPrefs, sent to the server in the
    /// Connect message (so system messages arrive in the right language), applied to the Unity
    /// Localization package's selected locale (for tables under Assets/Localization) and used
    /// by ClientStrings and LocalizationTable lookups in code.
    /// Devanagari needs a font with Hindi glyphs and shaping (for example Noto Sans Devanagari
    /// as a TextMeshPro fallback font). TODO: verify conjunct shaping on device; TextMeshPro has
    /// limited complex-script support, so a HarfBuzz-based text plugin may be needed.
    /// UNVERIFIED: written against Unity 6 / Localization 1.5 APIs, not compiled in the sandbox.
    /// </summary>
    public sealed class LanguageSettings : MonoBehaviour
    {
        private const string PrefKey = "gob.language";

        public static string Current { get; private set; } = LocalizationTable.English;

        /// <summary>Raised with the new language code ("en" or "hi").</summary>
        public static event Action<string> Changed;

        private void Awake()
        {
            string saved = PlayerPrefs.GetString(PrefKey, string.Empty);
            if (string.IsNullOrEmpty(saved))
            {
                // First launch: follow the device language.
                saved = Application.systemLanguage == SystemLanguage.Hindi
                    ? LocalizationTable.Hindi
                    : LocalizationTable.English;
            }
            Set(saved);
        }

        /// <summary>Switches between English and Hindi (bind this to the settings toggle).</summary>
        public void Toggle()
        {
            Set(Current == LocalizationTable.Hindi ? LocalizationTable.English : LocalizationTable.Hindi);
        }

        /// <summary>For a UI Toggle: on = Hindi.</summary>
        public void SetHindi(bool hindi)
        {
            Set(hindi ? LocalizationTable.Hindi : LocalizationTable.English);
        }

        public static void Set(string language)
        {
            if (language != LocalizationTable.Hindi)
            {
                language = LocalizationTable.English;
            }
            Current = language;
            PlayerPrefs.SetString(PrefKey, language);
            PlayerPrefs.Save();
            ApplyUnityLocale(language);
            if (Changed != null)
            {
                Changed(language);
            }
        }

        private static void ApplyUnityLocale(string code)
        {
            if (LocalizationSettings.HasSettings && LocalizationSettings.AvailableLocales != null)
            {
                Locale locale = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(code));
                if (locale != null)
                {
                    LocalizationSettings.SelectedLocale = locale;
                }
            }
        }
    }
}
