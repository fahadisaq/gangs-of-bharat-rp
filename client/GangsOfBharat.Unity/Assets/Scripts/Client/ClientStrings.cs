using System.Collections.Generic;
using GangsOfBharat.Shared.Localization;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Client-only UI strings in English and Hindi. Lookups fall back to the shared
    /// LocalizationTable (server keys such as chat.* and job.*), then to the key.
    /// Larger UI text belongs in Unity Localization string tables under Assets/Localization;
    /// this table covers the strings the scaffold scripts need.
    /// </summary>
    public static class ClientStrings
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> Hi = new Dictionary<string, string>();

        static ClientStrings()
        {
            Add("age.title", "Age confirmation", "आयु पुष्टि");
            Add("age.body",
                "GANGS OF BHARAT contains crime, violence and online chat with other players. You must be 18 or older to play.",
                "GANGS OF BHARAT में अपराध, हिंसा और दूसरे खिलाड़ियों के साथ ऑनलाइन चैट है। खेलने के लिए आपकी उम्र 18 साल या उससे ज़्यादा होनी चाहिए।");
            Add("age.confirm", "I am 18 or older", "मेरी उम्र 18 साल या उससे ज़्यादा है");
            Add("age.decline", "I am under 18", "मेरी उम्र 18 साल से कम है");
            Add("age.declined", "Sorry, you must be 18 or older to play.", "माफ़ कीजिए, खेलने के लिए 18 साल या उससे ज़्यादा उम्र ज़रूरी है।");
            Add("login.google", "Sign in with Google", "Google से साइन इन करें");
            Add("login.apple", "Sign in with Apple", "Apple से साइन इन करें");
            Add("chat.placeholder", "Type a message...", "संदेश लिखें...");
            Add("chat.send", "Send", "भेजें");
            Add("chat.sms_target", "Player ID", "खिलाड़ी आईडी");
            Add("chat.too_long", "Message is too long.", "संदेश बहुत लंबा है।");
            Add("voice.ptt", "Hold to talk", "बोलने के लिए दबाए रखें");
            Add("voice.open_mic", "Open mic", "ओपन माइक");
            Add("voice.proximity", "Nearby", "आसपास");
            Add("voice.radio", "Faction radio", "फ़ैक्शन रेडियो");
            Add("voice.no_mic", "Microphone permission is needed for voice chat.", "वॉइस चैट के लिए माइक्रोफ़ोन की अनुमति चाहिए।");
            Add("job.start_taxi", "Start taxi job", "टैक्सी का काम शुरू करें");
            Add("job.start_food", "Start food delivery", "फ़ूड डिलीवरी शुरू करें");
            Add("job.distance", "Waypoint: {0} m", "मंज़िल: {0} मीटर");
            Add("job.rejected", "Job could not be started.", "काम शुरू नहीं हो सका।");
            Add("job.paid", "Paid ₹{0}. Wallet: ₹{1}", "₹{0} मिले। बटुआ: ₹{1}");
            Add("settings.language", "Language: English", "भाषा: हिन्दी");
        }

        private static void Add(string key, string en, string hi)
        {
            En[key] = en;
            Hi[key] = hi;
        }

        public static IEnumerable<string> Keys { get { return En.Keys; } }

        public static string Get(string key)
        {
            return Get(LanguageSettings.Current, key);
        }

        public static string Get(string language, string key)
        {
            string value;
            Dictionary<string, string> table = language == LocalizationTable.Hindi ? Hi : En;
            if (table.TryGetValue(key, out value) || En.TryGetValue(key, out value))
            {
                return value;
            }
            return LocalizationTable.Get(language, key);
        }

        public static string Format(string key, params object[] args)
        {
            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, Get(key), args);
            }
            catch (System.FormatException)
            {
                return Get(key);
            }
        }
    }
}
