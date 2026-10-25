using System;
using UnityEngine;

namespace TrustBreach.Core
{
    public enum ConsentStatus
    {
        /// <summary>No decision yet: nothing is written to disk.</summary>
        Undecided,
        /// <summary>Participant agreed: anonymous research data for the current session may be written.</summary>
        Granted,
        /// <summary>Participant declined: gameplay continues, nothing is written.</summary>
        Declined,
        /// <summary>Participant withdrew: the current session's files were deleted and nothing more is written.</summary>
        Withdrawn
    }

    /// <summary>
    /// Research-recording consent for the current participant session. Every component that writes participant data
    /// to disk (Phase 3 TelemetryRecorder, Phase 4 research collector) must check <see cref="CanRecord"/>.
    /// Granting consent starts a NEW anonymous DecisionLog session, so nothing that happened before consent is recorded.
    /// </summary>
    public static class ResearchConsent
    {
        public static ConsentStatus Status { get; private set; } = ConsentStatus.Undecided;

        public static bool CanRecord => Status == ConsentStatus.Granted;

        public static event Action<ConsentStatus> Changed;

        /// <summary>Raised with the anonymous id of the session whose stored data must be deleted.</summary>
        public static event Action<string> Withdrawn;

        public static void Grant()
        {
            if (Status == ConsentStatus.Granted) return;
            Status = ConsentStatus.Granted;
            DecisionLog.StartSession(); // fresh anonymous id; pre-consent activity is never recorded
            Changed?.Invoke(Status);
            DecisionLog.RecordLifecycle("research.consent_granted", "research", "schema=1");
        }

        public static void Decline()
        {
            if (Status == ConsentStatus.Granted)
            {
                Withdraw();
                return;
            }
            Status = ConsentStatus.Declined;
            Changed?.Invoke(Status);
        }

        /// <summary>Stops recording and asks every recorder to delete the current session's data.</summary>
        public static void Withdraw()
        {
            string session = DecisionLog.SessionId;
            bool wasRecording = Status == ConsentStatus.Granted;
            Status = ConsentStatus.Withdrawn;
            if (wasRecording)
            {
                Withdrawn?.Invoke(session);
            }
            Changed?.Invoke(Status);
        }

        /// <summary>New participant (e.g. Restart Experience): the consent question is asked again.</summary>
        public static void Reset()
        {
            Status = ConsentStatus.Undecided;
            Changed?.Invoke(Status);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Status = ConsentStatus.Undecided;
            Changed = null;
            Withdrawn = null;
        }

        /// <summary>Test support: forces a status without side effects.</summary>
        public static void SetStatusForTests(ConsentStatus status) => Status = status;
    }

    /// <summary>
    /// Optional extra page for the VR debrief (e.g. "Your session data" from Phase 4 analytics). The dialogue layer calls
    /// <see cref="Provider"/> when it builds the debrief; analytics registers it. Keeps the assemblies independent.
    /// </summary>
    public static class SessionDebriefHook
    {
        public const string PageTitle = "Your session data";

        /// <summary>Returns the page body, or null/empty to add nothing.</summary>
        public static Func<string> Provider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Provider = null;
    }
}
