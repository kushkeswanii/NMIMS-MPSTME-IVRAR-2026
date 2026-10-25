using TrustBreach.Core;
using UnityEngine;

namespace TrustBreach.Interactions.Scenarios
{
    /// <summary>
    /// Inbox -> (Verify Account Now) -> fake credential page -> Proceed (compromised) | Report (reported after click).
    /// Reporting straight from the inbox is the safest path. Nothing is sent anywhere: all pages are local panels.
    /// </summary>
    public class PhishingEmailScenario : ScenarioController
    {
        public enum Stage
        {
            Inbox,
            CredentialPage,
            Compromised,
            ReportedFromInbox,
            ReportedAfterClick
        }

        [Header("Inbox")]
        [SerializeField] private XRButton _verifyLinkButton;
        [SerializeField] private XRButton _reportFromInboxButton;

        [Header("Fake credential page")]
        [SerializeField] private GameObject _credentialPage;
        [SerializeField] private XRButton _proceedButton;
        [SerializeField] private XRButton _reportButton;

        [Header("Persistent outcomes")]
        [SerializeField] private GameObject _compromisedOutcome;
        [SerializeField] private GameObject _reportedFromInboxOutcome;
        [SerializeField] private GameObject _reportedAfterClickOutcome;

        public Stage CurrentStage { get; private set; } = Stage.Inbox;
        public override bool IsResolved => CurrentStage != Stage.Inbox && CurrentStage != Stage.CredentialPage;

        protected virtual void OnEnable()
        {
            Bind(_verifyLinkButton, HandleVerifyLink, true);
            Bind(_reportFromInboxButton, HandleReportFromInbox, true);
            Bind(_proceedButton, HandleProceed, true);
            Bind(_reportButton, HandleReportAfterClick, true);
        }

        protected virtual void OnDisable()
        {
            Bind(_verifyLinkButton, HandleVerifyLink, false);
            Bind(_reportFromInboxButton, HandleReportFromInbox, false);
            Bind(_proceedButton, HandleProceed, false);
            Bind(_reportButton, HandleReportAfterClick, false);
        }

        protected virtual void Start()
        {
            SetStage(Stage.Inbox);
        }

        public override void ResetScenario()
        {
            _proceedButton.SetSelected(false);
            _reportButton.SetSelected(false);
            SetStage(Stage.Inbox);
        }

        private void HandleVerifyLink(XRButton _)
        {
            if (CurrentStage != Stage.Inbox) return;
            Record("phishing_email.link_opened", "verify_account_now", DecisionOutcome.Unsafe);
            SetStage(Stage.CredentialPage);
        }

        private void HandleReportFromInbox(XRButton _)
        {
            if (CurrentStage != Stage.Inbox) return;
            Record("phishing_email.reported", "report_phishing_from_inbox", DecisionOutcome.Safe);
            SetStage(Stage.ReportedFromInbox);
        }

        private void HandleProceed(XRButton _)
        {
            if (CurrentStage != Stage.CredentialPage) return;
            Record("phishing_email.credentials_submitted", "proceed", DecisionOutcome.Unsafe);
            _proceedButton.SetSelected(true);
            SetStage(Stage.Compromised);
        }

        private void HandleReportAfterClick(XRButton _)
        {
            if (CurrentStage != Stage.CredentialPage) return;
            Record("phishing_email.reported", "report_suspicious_email", DecisionOutcome.Safe);
            _reportButton.SetSelected(true);
            SetStage(Stage.ReportedAfterClick);
        }

        private void SetStage(Stage stage)
        {
            CurrentStage = stage;
            bool inbox = stage == Stage.Inbox;
            _verifyLinkButton.SetInteractable(inbox);
            _reportFromInboxButton.SetInteractable(inbox);

            Show(_credentialPage, stage == Stage.CredentialPage);
            _proceedButton.SetInteractable(stage == Stage.CredentialPage);
            _reportButton.SetInteractable(stage == Stage.CredentialPage);

            Show(_compromisedOutcome, stage == Stage.Compromised);
            Show(_reportedFromInboxOutcome, stage == Stage.ReportedFromInbox);
            Show(_reportedAfterClickOutcome, stage == Stage.ReportedAfterClick);
        }
    }
}
