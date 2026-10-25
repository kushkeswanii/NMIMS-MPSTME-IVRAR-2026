using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TrustBreach.Core
{
    /// <summary>Security classification of a participant decision.</summary>
    public enum DecisionOutcome
    {
        /// <summary>Exploratory step with no security consequence (e.g. picked an object up).</summary>
        Neutral,
        /// <summary>Behaviour the training wants to reinforce (verify, report, hand to IT).</summary>
        Safe,
        /// <summary>Behaviour that would compromise security in a real attack.</summary>
        Unsafe
    }

    /// <summary>Which system produced a decision.</summary>
    public enum DecisionSource
    {
        /// <summary>Phase 2.5 object scenarios (email, kiosk, badge, USB).</summary>
        Scenario,
        /// <summary>Phase 3 NPC dialogue.</summary>
        Dialogue
    }

    /// <summary>What kind of decision this is, for separating intentions from completed security actions.</summary>
    public enum DecisionCategory
    {
        /// <summary>Something said to the NPC (an intention or stance, e.g. "OK, I'll use the kiosk").</summary>
        DialogueChoice,
        /// <summary>A completed security action: done at a scenario object, or disclosed verbally (e.g. telling a password).</summary>
        SecurityAction
    }

    /// <summary>
    /// Optional dialogue details attached to a decision. Scenario decisions use <see cref="ForScenario"/>.
    /// Contains identifiers and classifications only: never free text typed or spoken by the participant.
    /// </summary>
    public readonly struct DecisionContext
    {
        public readonly DecisionSource Source;
        public readonly string DialogueNodeId;
        public readonly string ResponseId;
        public readonly string PersuasionStrategy;
        /// <summary>Seconds from the options becoming available to the choice; negative when not applicable.</summary>
        public readonly double ResponseLatency;
        public readonly string ChoiceClass;
        /// <summary>"scripted" or "model" for dialogue lines; empty for scenario decisions.</summary>
        public readonly string ContentSource;
        public readonly DecisionCategory Category;
        /// <summary>A report to IT Security or an independent verification that can correct an earlier risky action.</summary>
        public readonly bool IsCorrective;
        /// <summary>Attack channel: "email", "kiosk", "usb", "badge", "verbal" or "dialogue".</summary>
        public readonly string Channel;

        public DecisionContext(DecisionSource source, string dialogueNodeId, string responseId, string persuasionStrategy,
            double responseLatency, string choiceClass, string contentSource)
            : this(source, dialogueNodeId, responseId, persuasionStrategy, responseLatency, choiceClass, contentSource,
                source == DecisionSource.Scenario ? DecisionCategory.SecurityAction : DecisionCategory.DialogueChoice, false,
                source == DecisionSource.Scenario ? string.Empty : "dialogue")
        {
        }

        public DecisionContext(DecisionSource source, string dialogueNodeId, string responseId, string persuasionStrategy,
            double responseLatency, string choiceClass, string contentSource, DecisionCategory category, bool isCorrective, string channel)
        {
            Category = category;
            IsCorrective = isCorrective;
            Channel = channel ?? string.Empty;
            Source = source;
            DialogueNodeId = dialogueNodeId ?? string.Empty;
            ResponseId = responseId ?? string.Empty;
            PersuasionStrategy = persuasionStrategy ?? string.Empty;
            ResponseLatency = responseLatency;
            ChoiceClass = choiceClass ?? string.Empty;
            ContentSource = contentSource ?? string.Empty;
        }

        public static DecisionContext None => new DecisionContext(DecisionSource.Scenario, null, null, null, -1d, null, null);

        /// <summary>
        /// Context for a Phase 2.5 scenario decision, derived from its event name ("&lt;scenario&gt;.&lt;event&gt;"):
        /// every scenario decision is a completed security action; reports and hand-ins to IT are corrective.
        /// </summary>
        public static DecisionContext ForScenario(string eventName)
        {
            string channel = ChannelFor(eventName);
            bool corrective = eventName.EndsWith(".reported") || eventName.EndsWith(".submitted_to_it");
            return new DecisionContext(DecisionSource.Scenario, null, null, null, -1d, null, null, DecisionCategory.SecurityAction, corrective, channel);
        }

        public static string ChannelFor(string eventName)
        {
            if (eventName.StartsWith("phishing_email.")) return "email";
            if (eventName.StartsWith("password_kiosk.")) return "kiosk";
            if (eventName.StartsWith("usb_drive.")) return "usb";
            if (eventName.StartsWith("visitor_badge.")) return "badge";
            return "other";
        }
    }

    /// <summary>
    /// One participant decision. Contains no personal data: an anonymous session id, scenario identifiers, the option
    /// chosen and its classification.
    /// </summary>
    public readonly struct DecisionEvent
    {
        /// <summary>Anonymous random id for the session (no link to the participant's identity).</summary>
        public readonly string SessionId;
        /// <summary>1-based order of the decision within the session.</summary>
        public readonly int Sequence;
        /// <summary>Dotted event name, e.g. "phishing_email.credentials_submitted".</summary>
        public readonly string EventName;
        /// <summary>Seconds since the session started (unscaled, see <see cref="DecisionLog.SessionTime"/>).</summary>
        public readonly double SessionTime;
        /// <summary>Scenario artifact / NPC id.</summary>
        public readonly string ObjectId;
        /// <summary>Machine-readable option the participant chose, e.g. "report_suspicious_email".</summary>
        public readonly string Choice;
        public readonly DecisionOutcome Outcome;
        /// <summary>
        /// True for a corrective decision (report / independent verification) made after an actual risky security action
        /// that it covers: dialogue reports and verification cover every channel; a scenario report covers its own channel.
        /// Risky dialogue intentions ("I'll use the kiosk") never count as risky actions.
        /// </summary>
        public readonly bool RecoveryAfterRisk;
        public readonly DecisionContext Context;

        public bool IsRisky => Outcome == DecisionOutcome.Unsafe;
        /// <summary>An actual risky security action (not merely a risky dialogue choice).</summary>
        public bool IsRiskyAction => IsRisky && Context.Category == DecisionCategory.SecurityAction;

        public DecisionEvent(string eventName, double sessionTime, string objectId, string choice, DecisionOutcome outcome)
            : this(string.Empty, 0, eventName, sessionTime, objectId, choice, outcome, false, DecisionContext.None)
        {
        }

        public DecisionEvent(string sessionId, int sequence, string eventName, double sessionTime, string objectId, string choice,
            DecisionOutcome outcome, bool recoveryAfterRisk, DecisionContext context)
        {
            SessionId = sessionId;
            Sequence = sequence;
            EventName = eventName;
            SessionTime = sessionTime;
            ObjectId = objectId;
            Choice = choice;
            Outcome = outcome;
            RecoveryAfterRisk = recoveryAfterRisk;
            Context = context;
        }

        public const string CsvHeader =
            "session_id,seq,session_time_s,event_name,object_id,choice,outcome,risky,recovery_after_risk,source," +
            "dialogue_node,response_id,persuasion_strategy,response_latency_s,choice_class,content_source," +
            "category,risky_action,corrective,channel";

        public string ToCsvRow()
        {
            string latency = Context.ResponseLatency >= 0 ? Context.ResponseLatency.ToString("F3", CultureInfo.InvariantCulture) : string.Empty;
            return string.Join(",", SessionId, Sequence.ToString(CultureInfo.InvariantCulture),
                SessionTime.ToString("F3", CultureInfo.InvariantCulture), EventName, ObjectId, Choice, Outcome,
                IsRisky ? "1" : "0", RecoveryAfterRisk ? "1" : "0", Context.Source, Context.DialogueNodeId, Context.ResponseId,
                Context.PersuasionStrategy, latency, Context.ChoiceClass, Context.ContentSource,
                Context.Category == DecisionCategory.SecurityAction ? "security_action" : "dialogue_choice",
                IsRiskyAction ? "1" : "0", Context.IsCorrective ? "1" : "0", Context.Channel);
        }

        public override string ToString() =>
            $"t={SessionTime:F3}s event={EventName} object={ObjectId} choice={Choice} outcome={Outcome}" +
            (RecoveryAfterRisk ? " recovery=true" : string.Empty) +
            (Context.Source == DecisionSource.Dialogue
                ? $" node={Context.DialogueNodeId} strategy={Context.PersuasionStrategy} latency={Context.ResponseLatency:F2}s class={Context.ChoiceClass} source={Context.ContentSource}"
                : string.Empty);
    }

    /// <summary>Non-decision event (encounter started/completed, line delivered, demo actions). Never counted as a decision.</summary>
    public readonly struct LifecycleEvent
    {
        public readonly string SessionId;
        public readonly double SessionTime;
        public readonly string EventName;
        public readonly string ObjectId;
        /// <summary>Short machine-readable detail, e.g. "outcome=Reported;strategies=Authority|Urgency".</summary>
        public readonly string Detail;

        public LifecycleEvent(string sessionId, double sessionTime, string eventName, string objectId, string detail)
        {
            SessionId = sessionId;
            SessionTime = sessionTime;
            EventName = eventName;
            ObjectId = objectId;
            Detail = detail ?? string.Empty;
        }

        public override string ToString() => $"t={SessionTime:F3}s {EventName} object={ObjectId} {Detail}";
    }

    /// <summary>
    /// Session-scoped record of participant decisions. Scenario controllers and the dialogue engine call
    /// <see cref="Record(string,string,string,DecisionOutcome)"/> exactly once per decision; the telemetry layer subscribes to
    /// <see cref="DecisionRecorded"/> / <see cref="LifecycleRecorded"/> or reads the histories.
    /// </summary>
    public static class DecisionLog
    {
        private static readonly List<DecisionEvent> Events = new List<DecisionEvent>();
        private static readonly List<LifecycleEvent> Lifecycle = new List<LifecycleEvent>();
        private static double _sessionStart;
        private static readonly HashSet<string> RiskyActionChannels = new HashSet<string>();

        public static event Action<DecisionEvent> DecisionRecorded;
        public static event Action<LifecycleEvent> LifecycleRecorded;
        /// <summary>Raised after <see cref="StartSession"/> with the new anonymous session id.</summary>
        public static event Action<string> SessionStarted;

        public static IReadOnlyList<DecisionEvent> History => Events;
        public static IReadOnlyList<LifecycleEvent> LifecycleHistory => Lifecycle;

        /// <summary>Anonymous random session id (12 hex characters).</summary>
        public static string SessionId { get; private set; } = NewSessionId();

        /// <summary>Seconds since the session started.</summary>
        public static double SessionTime => Time.realtimeSinceStartupAsDouble - _sessionStart;

        /// <summary>Channels on which an actual risky security action happened this session.</summary>
        public static IReadOnlyCollection<string> RiskyChannels => RiskyActionChannels;

        /// <summary>Phase 2.5 scenario decision (a completed security action; context derived from the event name).</summary>
        public static DecisionEvent Record(string eventName, string objectId, string choice, DecisionOutcome outcome) =>
            Record(eventName, objectId, choice, outcome, DecisionContext.ForScenario(eventName));

        public static DecisionEvent Record(string eventName, string objectId, string choice, DecisionOutcome outcome, DecisionContext context)
        {
            bool recovery = context.IsCorrective && CoversRiskyAction(context);
            if (outcome == DecisionOutcome.Unsafe && context.Category == DecisionCategory.SecurityAction)
            {
                RiskyActionChannels.Add(context.Channel);
            }
            var decision = new DecisionEvent(SessionId, Events.Count + 1, eventName, SessionTime, objectId, choice, outcome, recovery, context);
            Events.Add(decision);
            Debug.Log($"[TrustBreach.Decision] {decision}");
            DecisionRecorded?.Invoke(decision);
            return decision;
        }

        public static LifecycleEvent RecordLifecycle(string eventName, string objectId, string detail = null)
        {
            var lifecycle = new LifecycleEvent(SessionId, SessionTime, eventName, objectId, detail);
            Lifecycle.Add(lifecycle);
            Debug.Log($"[TrustBreach.Lifecycle] {lifecycle}");
            LifecycleRecorded?.Invoke(lifecycle);
            return lifecycle;
        }

        /// <summary>Starts a new anonymous session: new id, cleared histories, reset clock.</summary>
        public static void StartSession()
        {
            Events.Clear();
            Lifecycle.Clear();
            RiskyActionChannels.Clear();
            _sessionStart = Time.realtimeSinceStartupAsDouble;
            SessionId = NewSessionId();
            SessionStarted?.Invoke(SessionId);
        }

        /// <summary>Dialogue reports / verification cover every channel; a scenario report covers its own channel only.</summary>
        private static bool CoversRiskyAction(DecisionContext context) =>
            context.Source == DecisionSource.Dialogue ? RiskyActionChannels.Count > 0 : RiskyActionChannels.Contains(context.Channel);

        private static string NewSessionId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DecisionRecorded = null;
            LifecycleRecorded = null;
            SessionStarted = null;
            StartSession();
        }
    }
}
