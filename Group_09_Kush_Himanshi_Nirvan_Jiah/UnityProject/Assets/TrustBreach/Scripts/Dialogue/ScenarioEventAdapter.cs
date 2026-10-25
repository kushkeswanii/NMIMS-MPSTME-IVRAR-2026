using System.Collections.Generic;
using TrustBreach.Core;
using UnityEngine;

namespace TrustBreach.Dialogue
{
    /// <summary>
    /// Bridges the existing Phase 2.5 scenario controllers to the encounter: listens to their DecisionLog events
    /// (it never re-records them) and forwards the matching <see cref="ScenarioSignal"/> to the director.
    /// </summary>
    public class ScenarioEventAdapter : MonoBehaviour
    {
        [SerializeField] private EncounterDirector _director;

        public static readonly IReadOnlyDictionary<string, ScenarioSignal> EventSignals = new Dictionary<string, ScenarioSignal>
        {
            { "phishing_email.link_opened", ScenarioSignal.PhishingLinkOpened },
            { "phishing_email.credentials_submitted", ScenarioSignal.PhishingCredentialsSubmitted },
            { "phishing_email.reported", ScenarioSignal.PhishingReported },
            { "password_kiosk.sign_in_selected", ScenarioSignal.KioskSignInSelected },
            { "password_kiosk.credentials_entered", ScenarioSignal.KioskCredentialsEntered },
            { "password_kiosk.cancelled", ScenarioSignal.KioskCancelled },
            { "password_kiosk.reported", ScenarioSignal.KioskReported },
            { "visitor_badge.inspected", ScenarioSignal.BadgeInspected },
            { "visitor_badge.identity_verified", ScenarioSignal.BadgeVerificationFailed },
            { "usb_drive.picked_up", ScenarioSignal.UsbPickedUp },
            { "usb_drive.connected", ScenarioSignal.UsbConnected },
            { "usb_drive.submitted_to_it", ScenarioSignal.UsbSubmitted },
        };

        public int SignalsForwarded { get; private set; }

        protected virtual void OnEnable() => DecisionLog.DecisionRecorded += HandleDecision;
        protected virtual void OnDisable() => DecisionLog.DecisionRecorded -= HandleDecision;

        private void HandleDecision(DecisionEvent decision)
        {
            if (decision.Context.Source != DecisionSource.Scenario || !EventSignals.TryGetValue(decision.EventName, out ScenarioSignal signal))
            {
                return;
            }
            SignalsForwarded++;
            _director.HandleScenarioSignal(signal);
        }

        public void InjectDirector(EncounterDirector director) => _director = director;
    }
}
