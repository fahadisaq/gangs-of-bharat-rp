using System;
using System.Collections.Generic;
using System.Globalization;

namespace GangsOfBharat.Shared.Localization
{
    /// <summary>
    /// Server-side UI strings (system messages, job names, faction names, chat channel names)
    /// in English and Hindi. Lookups fall back to English, then to the key itself.
    /// The Unity client keeps larger tables for its own UI; these keys are the ones the server
    /// needs to send already-localized system messages.
    /// </summary>
    public static class LocalizationTable
    {
        public const string English = "en";
        public const string Hindi = "hi";

        public static readonly string[] Languages = new[] { English, Hindi };

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> Hi = new Dictionary<string, string>();

        static LocalizationTable()
        {
            // System messages
            Add("system.welcome", "Welcome to Mayanagri, {0}!", "मायानगरी में आपका स्वागत है, {0}!");
            Add("system.slow_mode", "Global chat slow mode: wait {0} s.", "ग्लोबल चैट स्लो मोड: {0} सेकंड रुकें।");
            Add("system.rate_limited", "You are sending messages too fast.", "आप बहुत तेज़ी से संदेश भेज रहे हैं।");
            Add("system.invalid_channel", "You cannot send to that channel.", "आप उस चैनल पर संदेश नहीं भेज सकते।");
            Add("system.not_in_faction", "You are not in a faction.", "आप किसी फ़ैक्शन में नहीं हैं।");
            Add("system.not_in_crew", "You are not in a crew.", "आप किसी क्रू में नहीं हैं।");
            Add("system.vip_only", "VIP chat is for VIP members only.", "वीआईपी चैट केवल वीआईपी सदस्यों के लिए है।");
            Add("system.ad_insufficient", "Not enough money for an ad (₹{0}).", "विज्ञापन के लिए पर्याप्त पैसे नहीं हैं (₹{0})।");
            Add("system.ad_charged", "Ad posted. ₹{0} charged.", "विज्ञापन पोस्ट हो गया। ₹{0} कटे।");
            Add("system.sms_no_balance", "Phone balance too low. Recharge at a 24/7 store.", "फ़ोन बैलेंस कम है। 24/7 स्टोर पर रिचार्ज करें।");
            Add("system.sms_target_offline", "That player is not online.", "वह खिलाड़ी ऑनलाइन नहीं है।");
            Add("system.sms_blocked", "This player has blocked you.", "इस खिलाड़ी ने आपको ब्लॉक किया है।");
            Add("system.admin_only", "Only admins can send announcements.", "केवल एडमिन घोषणाएँ भेज सकते हैं।");
            Add("system.job_started", "Job started: {0}. Go to the marked waypoint.", "काम शुरू: {0}। निशान वाली जगह पर जाएँ।");
            Add("system.job_complete", "Job done! You earned ₹{0}.", "काम पूरा! आपने ₹{0} कमाए।");
            Add("system.job_needs_vehicle", "You need a vehicle for this job.", "इस काम के लिए आपको गाड़ी चाहिए।");
            Add("system.job_unavailable", "This job is not available yet.", "यह काम अभी उपलब्ध नहीं है।");
            Add("system.job_busy", "Finish your current job first.", "पहले अपना मौजूदा काम पूरा करें।");
            Add("system.vehicle_too_far", "You are too far from that vehicle.", "आप उस गाड़ी से बहुत दूर हैं।");
            Add("system.vehicle_occupied", "Someone is already driving that vehicle.", "उस गाड़ी को कोई और चला रहा है।");
            Add("system.kicked_cheat", "Kicked: impossible movement detected.", "किक किया गया: असंभव मूवमेंट पकड़ा गया।");

            // Chat channel names
            Add("chat.local", "Local", "लोकल");
            Add("chat.global", "Global", "ग्लोबल");
            Add("chat.faction", "Faction", "फ़ैक्शन");
            Add("chat.crew", "Crew", "क्रू");
            Add("chat.vip", "VIP", "वीआईपी");
            Add("chat.ad", "Ad", "विज्ञापन");
            Add("chat.sms", "SMS", "एसएमएस");
            Add("chat.admin", "Announcement", "घोषणा");
            Add("chat.system", "System", "सिस्टम");

            // Job names
            Add("job.dabbawala", "Dabbawala", "डब्बावाला");
            Add("job.construction", "Construction worker", "निर्माण मज़दूर");
            Add("job.food_delivery", "Food delivery", "फ़ूड डिलीवरी");
            Add("job.auto_driver", "Auto driver", "ऑटो ड्राइवर");
            Add("job.fisherman", "Fisherman", "मछुआरा");
            Add("job.bus_driver", "Bus driver", "बस ड्राइवर");
            Add("job.farmer", "Farmer", "किसान");
            Add("job.taxi_driver", "Taxi driver", "टैक्सी ड्राइवर");
            Add("job.quarry_worker", "Quarry worker", "खदान मज़दूर");
            Add("job.truck_driver", "Truck driver", "ट्रक ड्राइवर");
            Add("job.mechanic", "Mechanic", "मैकेनिक");
            Add("job.lawyer", "Lawyer", "वकील");
            Add("job.real_estate_agent", "Real estate agent", "प्रॉपर्टी एजेंट");
            Add("job.shop_staff", "Shop staff", "दुकान कर्मचारी");

            // Faction names (gang and mafia names: DRAFT - owner review)
            Add("faction.police", "Police", "पुलिस");
            Add("faction.army", "Army", "सेना");
            Add("faction.hospital", "Hospital", "अस्पताल");
            Add("faction.fire", "Fire Brigade", "अग्निशमन दल");
            Add("faction.government", "Government", "सरकार");
            Add("faction.news", "News", "न्यूज़");
            Add("faction.gang_slum", "Slum Gang", "स्लम गैंग");
            Add("faction.gang_old_city", "Old City Gang", "पुराना शहर गैंग");
            Add("faction.gang_port", "Port Gang", "पोर्ट गैंग");
            Add("faction.gang_outskirts", "Outskirts Gang", "बाहरी इलाका गैंग");
            Add("faction.mafia_1", "Mafia Family 1", "माफ़िया परिवार 1");
            Add("faction.mafia_2", "Mafia Family 2", "माफ़िया परिवार 2");
            Add("faction.court", "Court", "अदालत");
        }

        private static void Add(string key, string en, string hi)
        {
            En[key] = en;
            Hi[key] = hi;
        }

        public static IEnumerable<string> Keys
        {
            get { return En.Keys; }
        }

        public static bool HasKey(string language, string key)
        {
            Dictionary<string, string> table = TableFor(language);
            return table != null && table.ContainsKey(key);
        }

        private static Dictionary<string, string> TableFor(string language)
        {
            if (language == Hindi)
            {
                return Hi;
            }
            if (language == English)
            {
                return En;
            }
            return null;
        }

        /// <summary>Returns the text for key in language, falling back to English, then to the key.</summary>
        public static string Get(string language, string key)
        {
            string value;
            Dictionary<string, string> table = TableFor(language);
            if (table != null && table.TryGetValue(key, out value))
            {
                return value;
            }
            if (En.TryGetValue(key, out value))
            {
                return value;
            }
            return key;
        }

        public static string Format(string language, string key, params object[] args)
        {
            string pattern = Get(language, key);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, pattern, args);
            }
            catch (FormatException)
            {
                return pattern;
            }
        }
    }
}
