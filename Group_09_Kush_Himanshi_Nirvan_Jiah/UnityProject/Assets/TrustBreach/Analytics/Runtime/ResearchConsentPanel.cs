using TMPro;
using TrustBreach.Core;
using TrustBreach.Interactions;
using UnityEngine;

namespace TrustBreach.Analytics
{
    /// <summary>
    /// In-VR research information and consent panel near the entrance. Recording starts only after AGREE, and only before
    /// the participant's first decision (so a session is never partially recorded). Declining keeps every scenario and Alex
    /// fully playable. WITHDRAW &amp; DELETE (press twice) deletes everything stored for the current session.
    /// </summary>
    public class ResearchConsentPanel : MonoBehaviour
    {
        public const string Notice =
            "<b>Optional:</b> help the course research by saving an <b>anonymous</b> record of this run on this device.\n" +
            "- <b>Saved:</b> a random session code, the options you choose, the security actions you take, and how long you take to decide.\n" +
            "- <b>Never saved:</b> your name, passwords or anything you type, your voice, audio, video or location.\n" +
            "- Saying no changes nothing in the experience. You can withdraw here at any time; your session is then deleted.";

        [SerializeField] private TMP_Text _body;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private XRButton _agreeButton;
        [SerializeField] private XRButton _declineButton;
        [SerializeField] private XRButton _withdrawButton;
        [SerializeField] private GameObject _agreeGroup;
        [SerializeField] private GameObject _declineGroup;
        [SerializeField] private GameObject _withdrawGroup;
        [SerializeField, Tooltip("Seconds the second WITHDRAW press stays armed.")]
        private float _confirmWindow = 6f;

        private float _withdrawArmedUntil = -1f;
        private int _lastHistoryCount = -1;
        private ConsentStatus _lastStatus = (ConsentStatus)(-1);

        public bool WithdrawArmed => Time.unscaledTime < _withdrawArmedUntil;
        /// <summary>Consent can be given only before the first decision of the current session.</summary>
        public static bool ConsentOpen => ResearchConsent.Status == ConsentStatus.Undecided && DecisionLog.History.Count == 0;

        protected virtual void OnEnable()
        {
            _agreeButton.WhenActivated += HandleAgree;
            _declineButton.WhenActivated += HandleDecline;
            _withdrawButton.WhenActivated += HandleWithdraw;
            ResearchConsent.Changed += HandleChanged;
            if (_body != null) _body.text = Notice;
            Refresh();
        }

        protected virtual void OnDisable()
        {
            _agreeButton.WhenActivated -= HandleAgree;
            _declineButton.WhenActivated -= HandleDecline;
            _withdrawButton.WhenActivated -= HandleWithdraw;
            ResearchConsent.Changed -= HandleChanged;
        }

        protected virtual void Update()
        {
            // Cheap polling for state that has no event (first decision recorded, confirm window expiring).
            bool armed = WithdrawArmed;
            if (DecisionLog.History.Count != _lastHistoryCount || ResearchConsent.Status != _lastStatus || (!armed && _withdrawArmedUntil > 0f))
            {
                if (!armed) _withdrawArmedUntil = -1f;
                Refresh();
            }
        }

        private void HandleChanged(ConsentStatus status) => Refresh();

        public void HandleAgree(XRButton _ = null)
        {
            if (!ConsentOpen) return;
            ResearchConsent.Grant();
            Refresh();
        }

        public void HandleDecline(XRButton _ = null)
        {
            if (ResearchConsent.Status != ConsentStatus.Undecided) return;
            ResearchConsent.Decline();
            DecisionLog.RecordLifecycle("research.consent_declined", "research"); // in memory only: nothing is written
            Refresh();
        }

        public void HandleWithdraw(XRButton _ = null)
        {
            if (ResearchConsent.Status != ConsentStatus.Granted) return;
            if (!WithdrawArmed)
            {
                _withdrawArmedUntil = Time.unscaledTime + _confirmWindow;
                Refresh();
                return;
            }
            _withdrawArmedUntil = -1f;
            ResearchConsent.Withdraw();
            Refresh();
        }

        public void Refresh()
        {
            _lastHistoryCount = DecisionLog.History.Count;
            _lastStatus = ResearchConsent.Status;
            bool open = ConsentOpen;
            bool undecided = ResearchConsent.Status == ConsentStatus.Undecided;
            bool granted = ResearchConsent.Status == ConsentStatus.Granted;
            SetGroup(_agreeGroup, _agreeButton, open);
            SetGroup(_declineGroup, _declineButton, undecided);
            SetGroup(_withdrawGroup, _withdrawButton, granted);
            if (_status == null) return;
            switch (ResearchConsent.Status)
            {
                case ConsentStatus.Granted:
                    _status.text = WithdrawArmed
                        ? "<color=#FFB547>Press WITHDRAW again to delete this session's data.</color>"
                        : $"<color=#38E1FF>Recording ON</color>  -  anonymous session {DecisionLog.SessionId}";
                    break;
                case ConsentStatus.Declined:
                    _status.text = "<color=#C8CDD4>Not recording.</color> Enjoy the experience - nothing is stored.";
                    break;
                case ConsentStatus.Withdrawn:
                    _status.text = "<color=#C8CDD4>Withdrawn.</color> This session's data was deleted; nothing more is stored.";
                    break;
                default:
                    _status.text = open
                        ? "Choose before you start. Recording is OFF until you agree."
                        : "<color=#C8CDD4>Not recording.</color> Consent can only be given before your first decision.";
                    break;
            }
        }

        private static void SetGroup(GameObject group, XRButton button, bool visible)
        {
            if (group != null && group.activeSelf != visible) group.SetActive(visible);
            if (button != null) button.SetInteractable(visible);
        }
    }
}
