using TMPro;
using TrustBreach.Core;
using TrustBreach.Dialogue.Content;
using TrustBreach.Interactions;
using UnityEngine;

namespace TrustBreach.Dialogue
{
    /// <summary>
    /// Developer / demo controls (only active in <see cref="EncounterMode.Demo"/>): live state readout, branch selection
    /// for each persuasion strategy, skip line, and a full reset of the encounter and all four office scenarios.
    /// Branch jumps and resets are lifecycle events, never participant decisions.
    /// </summary>
    public class DemoModePanel : MonoBehaviour
    {
        [SerializeField] private EncounterDirector _director;
        [SerializeField] private TMP_Text _stateText;
        [SerializeField] private XRButton _resetButton;
        [SerializeField] private XRButton _authorityBranchButton;
        [SerializeField] private XRButton _urgencyBranchButton;
        [SerializeField] private XRButton _trustBranchButton;
        [SerializeField] private XRButton _skipLineButton;
        [SerializeField] private XRButton _restartExperienceButton;

        private float _nextRefresh;

        protected virtual void OnEnable()
        {
            _resetButton.WhenActivated += HandleReset;
            _authorityBranchButton.WhenActivated += HandleAuthority;
            _urgencyBranchButton.WhenActivated += HandleUrgency;
            _trustBranchButton.WhenActivated += HandleTrust;
            _skipLineButton.WhenActivated += HandleSkip;
            _restartExperienceButton.WhenActivated += HandleRestart;
        }

        protected virtual void OnDisable()
        {
            _resetButton.WhenActivated -= HandleReset;
            _authorityBranchButton.WhenActivated -= HandleAuthority;
            _urgencyBranchButton.WhenActivated -= HandleUrgency;
            _trustBranchButton.WhenActivated -= HandleTrust;
            _skipLineButton.WhenActivated -= HandleSkip;
            _restartExperienceButton.WhenActivated -= HandleRestart;
        }

        private void HandleReset(XRButton _) => _director.ResetDemo();
        private void HandleAuthority(XRButton _) => _director.JumpToBranch(AlexMorganEncounter.RefusalPressure, 1, 0, "refused");
        private void HandleUrgency(XRButton _) => _director.JumpToBranch(AlexMorganEncounter.VerificationPushback, 2, 0, "verify_requested");
        private void HandleTrust(XRButton _) => _director.JumpToBranch(AlexMorganEncounter.RequestCredentials, 0, 2);
        private void HandleSkip(XRButton _) => _director.SkipLineDelivery();
        private void HandleRestart(XRButton _) => _director.RestartExperience();

        protected virtual void Update()
        {
            if (Time.unscaledTime < _nextRefresh || _director.Engine == null) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            DialogueState s = _director.Engine.State;
            _stateText.text =
                $"<b>Phase</b> {_director.CurrentPhase}   <b>Node</b> {s.CurrentNodeId ?? "-"}\n" +
                $"<b>Strategy</b> {s.CurrentStrategy}   <b>Escalation</b> {s.Escalation}\n" +
                $"<b>Suspicion</b> {s.Suspicion}   <b>Cooperation</b> {s.Cooperation}   <b>Risky choices/actions</b> {s.RiskyChoices}/{s.RiskyActionCount}\n" +
                $"<b>Outcome</b> {s.Outcome}   <b>Line source</b> {_director.LastLineSource}\n" +
                $"<b>Session</b> {DecisionLog.SessionId}   <b>Decisions</b> {DecisionLog.History.Count}";
        }
    }
}
