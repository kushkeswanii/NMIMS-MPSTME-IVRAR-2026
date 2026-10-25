using System;
using System.Collections.Generic;
using System.Linq;
using TrustBreach.Core;
using UnityEngine;

namespace TrustBreach.Dialogue
{
    public enum PersuasionStrategy
    {
        None,
        /// <summary>Authority and impersonation ("I'm from Security, this isn't optional").</summary>
        Authority,
        /// <summary>Urgency and time pressure ("ten minutes until lock-out").</summary>
        Urgency,
        /// <summary>Trust-building and helpfulness, incl. social proof ("I'm on your side").</summary>
        TrustBuilding
    }

    /// <summary>Research classification of a participant response.</summary>
    public enum ChoiceClass
    {
        Cooperate,
        AskIdentification,
        Challenge,
        RequestVerification,
        Refuse,
        Report,
        Continue,
        Exit
    }

    /// <summary>Phase 2.5 scenario decisions, translated for the dialogue engine.</summary>
    public enum ScenarioSignal
    {
        None,
        PhishingLinkOpened,
        PhishingCredentialsSubmitted,
        PhishingReported,
        KioskSignInSelected,
        KioskCredentialsEntered,
        KioskCancelled,
        KioskReported,
        BadgeInspected,
        BadgeVerificationFailed,
        UsbPickedUp,
        UsbConnected,
        UsbSubmitted
    }

    /// <summary>What the NPC asks the participant to do with an existing Phase 2.5 scenario.</summary>
    public enum ScenarioAction
    {
        None,
        DirectToPhishingEmail,
        DirectToPasswordKiosk,
        PresentVisitorBadge,
        EncourageUsbConnection
    }

    /// <summary>
    /// Evidence-based encounter classification (see AdaptiveDialogueEngine.ResolveOutcome):
    /// risky *actions* decide Compromised / Recovered; risky *statements* (promises) never do.
    /// </summary>
    public enum EncounterOutcome
    {
        InProgress,
        /// <summary>An actual risky security action was completed and not followed by a covering corrective action.</summary>
        Compromised,
        /// <summary>An actual risky security action, then a recorded corrective action (report / independent verification) covering it.</summary>
        Recovered,
        /// <summary>No risky action; the participant independently verified or reported.</summary>
        Resisted,
        /// <summary>No risky action and no report/verification, but the participant refused until Alex gave up (preserved Phase 3 state).</summary>
        Declined,
        /// <summary>Ended before any meaningful security outcome (no risky action, no refusal stand-off, no report or verification).</summary>
        Incomplete
    }

    public enum NodeKind
    {
        /// <summary>NPC line followed by participant options.</summary>
        Line,
        /// <summary>NPC line, then waits for a Phase 2.5 scenario decision (options remain available).</summary>
        AwaitScenario,
        /// <summary>Final NPC line; the encounter outcome is fixed here and the debrief follows.</summary>
        Terminal
    }

    public enum NpcGesture
    {
        Idle,
        Explain,
        Point,
        ShowBadge,
        Wave,
        Shrug,
        PhoneCall
    }

    [Serializable]
    public class DialogueCondition
    {
        public int MinSuspicion = -100;
        public int MaxSuspicion = 100;
        public int MinCooperation = -100;
        public int MaxCooperation = 100;
        [Tooltip("All of these flags must be set.")]
        public List<string> RequiresFlags = new List<string>();
        [Tooltip("At least one of these flags must be set (ignored when empty).")]
        public List<string> RequiresAnyFlags = new List<string>();
        [Tooltip("None of these flags may be set.")]
        public List<string> ExcludesFlags = new List<string>();

        public bool Matches(DialogueState state)
        {
            return state.Suspicion >= MinSuspicion && state.Suspicion <= MaxSuspicion &&
                   state.Cooperation >= MinCooperation && state.Cooperation <= MaxCooperation &&
                   RequiresFlags.All(state.HasFlag) &&
                   (RequiresAnyFlags.Count == 0 || RequiresAnyFlags.Any(state.HasFlag)) &&
                   !ExcludesFlags.Any(state.HasFlag);
        }
    }

    /// <summary>One way the NPC can say a node's line; the first variant whose condition matches is delivered.</summary>
    [Serializable]
    public class LineVariant
    {
        public string Id;
        [TextArea(2, 5)] public string Text;
        public PersuasionStrategy Strategy;
        public int EscalationDelta;
        public NpcGesture Gesture;
        public ScenarioAction Action;
        [Tooltip("Shown on the dialogue panel while the request is pending, e.g. where to go.")]
        public string Objective;
        public DialogueCondition Condition = new DialogueCondition();
        [Tooltip("Pre-generated offline voice clip (optional).")]
        public AudioClip Voice;
    }

    [Serializable]
    public class ResponseOption
    {
        public string Id;
        public string Text;
        public ChoiceClass Class;
        public DecisionOutcome Outcome;
        public int SuspicionDelta;
        public int CooperationDelta;
        public List<string> AddFlags = new List<string>();
        public string NextNodeId;
        public DialogueCondition ShowCondition = new DialogueCondition();
        [Tooltip("Choosing this completes a security action (e.g. telling Alex your password), not just an intention.")]
        public bool IsSecurityAction;
        [Tooltip("A report to IT Security or an independent verification (can correct an earlier risky action).")]
        public bool IsCorrective;
        [Tooltip("Channel of the security action: verbal, dialogue, ...")]
        public string Channel = "dialogue";
        [Tooltip("For promises (agreeing to act later): the scenario channel promised, e.g. kiosk / email / usb.")]
        public string PromisedChannel = string.Empty;
    }

    [Serializable]
    public class SignalTransition
    {
        public ScenarioSignal Signal;
        public string NextNodeId;
    }

    [Serializable]
    public class DialogueNode
    {
        public string Id;
        public NodeKind Kind;
        public List<LineVariant> Lines = new List<LineVariant>();
        public List<ResponseOption> Options = new List<ResponseOption>();
        public List<SignalTransition> SignalTransitions = new List<SignalTransition>();
        [Tooltip("Terminal nodes only: hint used when no risky action happened (Declined / Incomplete); evidence decides the rest.")]
        public EncounterOutcome TerminalOutcome;
    }
}
