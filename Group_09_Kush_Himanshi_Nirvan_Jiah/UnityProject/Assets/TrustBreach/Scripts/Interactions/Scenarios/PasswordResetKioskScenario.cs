using TrustBreach.Core;
using UnityEngine;

namespace TrustBreach.Interactions.Scenarios
{
    /// <summary>
    /// Rogue "self-service password reset" kiosk: Sign In &amp; Reset -> verification decision panel ->
    /// Continue (credentials captured) | Cancel (left, but not reported) | Report kiosk to IT.
    /// </summary>
    public class PasswordResetKioskScenario : ScenarioController
    {
        public enum Stage
        {
            LoginForm,
            DecisionPanel,
            CredentialsCaptured,
            Cancelled,
            Reported
        }

        [SerializeField] private XRButton _signInButton;

        [Header("Decision panel")]
        [SerializeField] private GameObject _decisionPanel;
        [SerializeField] private XRButton _continueButton;
        [SerializeField] private XRButton _cancelButton;
        [SerializeField] private XRButton _reportButton;

        [Header("Persistent outcomes")]
        [SerializeField] private GameObject _capturedOutcome;
        [SerializeField] private GameObject _cancelledOutcome;
        [SerializeField] private GameObject _reportedOutcome;

        public Stage CurrentStage { get; private set; } = Stage.LoginForm;
        public override bool IsResolved => CurrentStage != Stage.LoginForm && CurrentStage != Stage.DecisionPanel;

        protected virtual void OnEnable()
        {
            Bind(_signInButton, HandleSignIn, true);
            Bind(_continueButton, HandleContinue, true);
            Bind(_cancelButton, HandleCancel, true);
            Bind(_reportButton, HandleReport, true);
        }

        protected virtual void OnDisable()
        {
            Bind(_signInButton, HandleSignIn, false);
            Bind(_continueButton, HandleContinue, false);
            Bind(_cancelButton, HandleCancel, false);
            Bind(_reportButton, HandleReport, false);
        }

        protected virtual void Start()
        {
            SetStage(Stage.LoginForm);
        }

        public override void ResetScenario()
        {
            _continueButton.SetSelected(false);
            _cancelButton.SetSelected(false);
            _reportButton.SetSelected(false);
            SetStage(Stage.LoginForm);
        }

        private void HandleSignIn(XRButton _)
        {
            if (CurrentStage != Stage.LoginForm) return;
            Record("password_kiosk.sign_in_selected", "sign_in_and_reset", DecisionOutcome.Neutral);
            SetStage(Stage.DecisionPanel);
        }

        private void HandleContinue(XRButton _)
        {
            if (CurrentStage != Stage.DecisionPanel) return;
            Record("password_kiosk.credentials_entered", "continue", DecisionOutcome.Unsafe);
            _continueButton.SetSelected(true);
            SetStage(Stage.CredentialsCaptured);
        }

        private void HandleCancel(XRButton _)
        {
            if (CurrentStage != Stage.DecisionPanel) return;
            Record("password_kiosk.cancelled", "cancel", DecisionOutcome.Neutral);
            _cancelButton.SetSelected(true);
            SetStage(Stage.Cancelled);
        }

        private void HandleReport(XRButton _)
        {
            if (CurrentStage != Stage.DecisionPanel) return;
            Record("password_kiosk.reported", "report_kiosk_to_it", DecisionOutcome.Safe);
            _reportButton.SetSelected(true);
            SetStage(Stage.Reported);
        }

        private void SetStage(Stage stage)
        {
            CurrentStage = stage;
            _signInButton.SetInteractable(stage == Stage.LoginForm);

            bool deciding = stage == Stage.DecisionPanel;
            Show(_decisionPanel, deciding);
            _continueButton.SetInteractable(deciding);
            _cancelButton.SetInteractable(deciding);
            _reportButton.SetInteractable(deciding);

            Show(_capturedOutcome, stage == Stage.CredentialsCaptured);
            Show(_cancelledOutcome, stage == Stage.Cancelled);
            Show(_reportedOutcome, stage == Stage.Reported);
        }
    }
}
