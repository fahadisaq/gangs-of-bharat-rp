using System.Collections.Generic;
using System.Text;
using GangsOfBharat.Shared.Localization;
using GangsOfBharat.Shared.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// Chat window with one tab per text channel from docs/design/10-communication.md:
    /// Local (nearby), Global (slow mode), Faction (map-wide), Crew, VIP, Ad (paid), SMS
    /// (one-to-one). Admin announcements and System notices are receive-only and appear in
    /// every tab. Channel names come from the shared LocalizationTable (chat.* keys) and follow
    /// the language toggle. The server enforces every rule (slow mode, fees, VIP, faction);
    /// the client only shows the reply.
    /// UNVERIFIED: written against Unity 6 / uGUI 2.0 / TextMeshPro APIs, not compiled here.
    /// </summary>
    public sealed class ChatUI : MonoBehaviour
    {
        private const int MaxLinesPerChannel = 100;

        private static readonly ChatChannel[] SendableChannels =
        {
            ChatChannel.Local, ChatChannel.Global, ChatChannel.Faction, ChatChannel.Crew,
            ChatChannel.Vip, ChatChannel.Ad, ChatChannel.Sms,
        };

        [Header("Layout")]
        [Tooltip("One button per entry in SendableChannels, in the same order.")]
        [SerializeField] private Button[] tabButtons = new Button[0];
        [SerializeField] private TMP_Text[] tabLabels = new TMP_Text[0];
        [SerializeField] private TMP_Text messageLog;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private TMP_InputField input;
        [SerializeField] private TMP_InputField smsTargetInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private TMP_Text sendLabel;
        [Tooltip("Shown only for admin sessions (set by the login flow).")]
        [SerializeField] private Button announcementButton;

        private readonly Dictionary<ChatChannel, List<string>> _lines = new Dictionary<ChatChannel, List<string>>();
        private readonly ChatMessage _out = new ChatMessage();
        private readonly SetMuteMessage _mute = new SetMuteMessage();
        private readonly StringBuilder _sb = new StringBuilder(4096);
        private ChatChannel _active = ChatChannel.Local;

        public bool IsAdmin { get; set; }

        private void Awake()
        {
            for (int i = 0; i < SendableChannels.Length; i++)
            {
                _lines[SendableChannels[i]] = new List<string>();
            }
            for (int i = 0; i < tabButtons.Length && i < SendableChannels.Length; i++)
            {
                ChatChannel channel = SendableChannels[i];
                tabButtons[i].onClick.AddListener(() => SelectChannel(channel));
            }
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(SendCurrent);
            }
            if (announcementButton != null)
            {
                announcementButton.onClick.AddListener(() => Send(ChatChannel.AdminAnnouncement));
            }
            if (input != null)
            {
                input.onSubmit.AddListener(_ => SendCurrent());
            }
        }

        private void OnEnable()
        {
            LanguageSettings.Changed += OnLanguageChanged;
            if (NetClient.Instance != null)
            {
                NetClient.Instance.ChatReceived += OnChat;
            }
            RefreshLabels();
            SelectChannel(_active);
        }

        private void OnDisable()
        {
            LanguageSettings.Changed -= OnLanguageChanged;
            if (NetClient.Instance != null)
            {
                NetClient.Instance.ChatReceived -= OnChat;
            }
        }

        public static string ChannelKey(ChatChannel channel)
        {
            switch (channel)
            {
                case ChatChannel.Local: return "chat.local";
                case ChatChannel.Global: return "chat.global";
                case ChatChannel.Faction: return "chat.faction";
                case ChatChannel.Crew: return "chat.crew";
                case ChatChannel.Vip: return "chat.vip";
                case ChatChannel.Ad: return "chat.ad";
                case ChatChannel.Sms: return "chat.sms";
                case ChatChannel.AdminAnnouncement: return "chat.admin";
                default: return "chat.system";
            }
        }

        private void OnLanguageChanged(string language)
        {
            RefreshLabels();
            Redraw();
        }

        private void RefreshLabels()
        {
            string lang = LanguageSettings.Current;
            for (int i = 0; i < tabLabels.Length && i < SendableChannels.Length; i++)
            {
                if (tabLabels[i] != null)
                {
                    tabLabels[i].text = LocalizationTable.Get(lang, ChannelKey(SendableChannels[i]));
                }
            }
            if (sendLabel != null)
            {
                sendLabel.text = ClientStrings.Get("chat.send");
            }
            if (input != null && input.placeholder is TMP_Text)
            {
                ((TMP_Text)input.placeholder).text = ClientStrings.Get("chat.placeholder");
            }
            if (announcementButton != null)
            {
                announcementButton.gameObject.SetActive(IsAdmin);
            }
        }

        public void SelectChannel(ChatChannel channel)
        {
            _active = channel;
            if (smsTargetInput != null)
            {
                smsTargetInput.gameObject.SetActive(channel == ChatChannel.Sms);
            }
            Redraw();
        }

        private void SendCurrent()
        {
            Send(_active);
        }

        private void Send(ChatChannel channel)
        {
            if (input == null || NetClient.Instance == null)
            {
                return;
            }
            string text = input.text.Trim();
            if (text.Length == 0)
            {
                return;
            }
            if (Encoding.UTF8.GetByteCount(text) > ProtocolConstants.MaxChatBytes)
            {
                AddLine(channel, ClientStrings.Get("chat.too_long"));
                return;
            }
            int target = 0;
            if (channel == ChatChannel.Sms && (smsTargetInput == null || !int.TryParse(smsTargetInput.text, out target)))
            {
                AddLine(channel, ClientStrings.Get("chat.sms_target"));
                return;
            }
            _out.Channel = channel;
            _out.TargetId = target;
            _out.SenderId = 0;
            _out.SenderName = string.Empty;
            _out.Text = text;
            NetClient.Instance.SendReliable(_out);
            input.text = string.Empty;
            input.ActivateInputField();
        }

        /// <summary>Mute hides a player's chat and voice; block also refuses their SMS.</summary>
        public void SetMuted(int playerId, bool block, bool enabled)
        {
            _mute.TargetPlayerId = playerId;
            _mute.Block = block;
            _mute.Enabled = enabled;
            NetClient.Instance.SendReliable(_mute);
        }

        private void OnChat(ChatMessage message)
        {
            string lang = LanguageSettings.Current;
            string tag = LocalizationTable.Get(lang, ChannelKey(message.Channel));
            string line;
            if (message.Channel == ChatChannel.System)
            {
                // System notices carry text already localized by the server.
                line = "<color=#FFD54F>[" + tag + "]</color> " + Escape(message.Text);
            }
            else if (message.Channel == ChatChannel.AdminAnnouncement)
            {
                // Admins must appear only by code (e.g. ADM-27). TODO (server): put the admin code,
                // not the character name, in SenderName for announcements.
                line = "<color=#FF5252>[" + tag + "] " + Escape(message.SenderName) + ":</color> " + Escape(message.Text);
            }
            else
            {
                line = "[" + tag + "] <b>" + Escape(message.SenderName) + "</b> (" + message.SenderId + "): " + Escape(message.Text);
            }

            if (message.Channel == ChatChannel.System || message.Channel == ChatChannel.AdminAnnouncement)
            {
                for (int i = 0; i < SendableChannels.Length; i++)
                {
                    AddLine(SendableChannels[i], line, false);
                }
                Redraw();
            }
            else
            {
                AddLine(message.Channel, line);
            }
        }

        /// <summary>Stops players injecting TextMeshPro rich-text tags into chat.</summary>
        private static string Escape(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : "<noparse>" + text.Replace("</noparse>", string.Empty) + "</noparse>";
        }

        private void AddLine(ChatChannel channel, string line, bool redraw = true)
        {
            List<string> lines;
            if (!_lines.TryGetValue(channel, out lines))
            {
                return;
            }
            lines.Add(line);
            if (lines.Count > MaxLinesPerChannel)
            {
                lines.RemoveAt(0);
            }
            if (redraw && channel == _active)
            {
                Redraw();
            }
        }

        private void Redraw()
        {
            if (messageLog == null)
            {
                return;
            }
            List<string> lines;
            if (!_lines.TryGetValue(_active, out lines))
            {
                return;
            }
            _sb.Length = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                _sb.AppendLine(lines[i]);
            }
            messageLog.text = _sb.ToString();
            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 0f;
            }
        }
    }
}
