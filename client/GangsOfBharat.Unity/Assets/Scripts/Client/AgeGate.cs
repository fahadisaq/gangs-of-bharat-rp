using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GangsOfBharat.Client
{
    /// <summary>
    /// 18+ confirmation shown before login (the game has crime, violence and open chat).
    /// The answer is stored in PlayerPrefs; declining keeps the login buttons disabled.
    /// TODO: store the confirmation with a timestamp on the player's Firebase profile as well,
    /// and align with store age ratings (Google Play IARC, App Store 17+).
    /// UNVERIFIED: written against Unity 6 / uGUI / TextMeshPro APIs, not compiled here.
    /// </summary>
    public sealed class AgeGate : MonoBehaviour
    {
        private const string PrefKey = "gob.age_confirmed_18";

        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text body;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private Button declineButton;
        [SerializeField] private TMP_Text declineLabel;
        [Tooltip("Login buttons, enabled only after confirmation.")]
        [SerializeField] private Selectable[] loginControls = new Selectable[0];

        public bool Confirmed { get; private set; }
        public event Action ConfirmedChanged;

        private void Awake()
        {
            Confirmed = PlayerPrefs.GetInt(PrefKey, 0) == 1;
            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(Confirm);
            }
            if (declineButton != null)
            {
                declineButton.onClick.AddListener(Decline);
            }
        }

        private void OnEnable()
        {
            LanguageSettings.Changed += OnLanguageChanged;
            Refresh();
            if (!Confirmed)
            {
                Show();
            }
        }

        private void OnDisable()
        {
            LanguageSettings.Changed -= OnLanguageChanged;
        }

        private void OnLanguageChanged(string language)
        {
            Refresh();
        }

        public void Show()
        {
            if (panel != null)
            {
                panel.SetActive(true);
            }
            Refresh();
        }

        private void Refresh()
        {
            if (title != null) title.text = ClientStrings.Get("age.title");
            if (body != null) body.text = ClientStrings.Get("age.body");
            if (confirmLabel != null) confirmLabel.text = ClientStrings.Get("age.confirm");
            if (declineLabel != null) declineLabel.text = ClientStrings.Get("age.decline");
            for (int i = 0; i < loginControls.Length; i++)
            {
                if (loginControls[i] != null)
                {
                    loginControls[i].interactable = Confirmed;
                }
            }
        }

        private void Confirm()
        {
            Confirmed = true;
            PlayerPrefs.SetInt(PrefKey, 1);
            PlayerPrefs.Save();
            if (panel != null)
            {
                panel.SetActive(false);
            }
            Refresh();
            if (ConfirmedChanged != null)
            {
                ConfirmedChanged();
            }
        }

        private void Decline()
        {
            Confirmed = false;
            PlayerPrefs.SetInt(PrefKey, 0);
            PlayerPrefs.Save();
            Refresh();
            if (body != null)
            {
                body.text = ClientStrings.Get("age.declined");
            }
        }
    }
}
