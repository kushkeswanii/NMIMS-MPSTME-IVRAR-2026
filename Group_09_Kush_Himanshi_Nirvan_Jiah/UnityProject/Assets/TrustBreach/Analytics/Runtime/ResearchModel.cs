using System;
using System.Collections.Generic;
using System.Globalization;
using TrustBreach.Core;

namespace TrustBreach.Analytics
{
    /// <summary>Schema constants. Bump <see cref="Version"/> when fields change; readers reject unknown major versions.</summary>
    public static class ResearchSchema
    {
        public const string Version = "1.0";
        public const string SessionSchema = "trustbreach.research.session/" + Version;
        public const string SchemaPrefix = "trustbreach.research.session/1.";

        /// <summary>Datasets are stored in separate folders and are never combined.</summary>
        public const string Real = "real";
        public const string Demo = "demo";
        /// <summary>Automated validation runs (Play Mode tests) - kept apart from participant data.</summary>
        public const string Test = "test";

        public const string Compromised = "COMPROMISED";
        public const string Recovered = "RECOVERED";
        public const string Resisted = "RESISTED";
        /// <summary>Preserved Phase 3 state: refused until Alex gave up, no report/verification.</summary>
        public const string Declined = "DECLINED";
        public const string Incomplete = "INCOMPLETE";

        public static readonly string[] Outcomes = { Compromised, Recovered, Resisted, Declined, Incomplete };

        /// <summary>The four office scenarios, keyed by DecisionLog channel.</summary>
        public static readonly string[] ScenarioChannels = { "email", "kiosk", "badge", "usb" };

        public static string ScenarioLabel(string channel)
        {
            switch (channel)
            {
                case "email": return "Phishing email";
                case "kiosk": return "Password kiosk";
                case "badge": return "Visitor badge";
                case "usb": return "Found USB";
                case "verbal": return "Told Alex (verbal)";
                default: return channel;
            }
        }
    }

    /// <summary>
    /// One research event. Built only from DecisionLog decisions and lifecycle events; contains anonymous ids and
    /// classifications only (no names, credentials, free text, voice or audio).
    /// </summary>
    [Serializable]
    public class ResearchEvent
    {
        /// <summary>1-based order within the session file (arrival order = chronological order).</summary>
        public int seq;
        /// <summary>DecisionLog sequence number for decisions (dedupe key); 0 for lifecycle events.</summary>
        public int sourceSeq;
        /// <summary>"dialogue_choice", "security_action" or "lifecycle".</summary>
        public string kind;
        public string name;
        /// <summary>Scenario artifact / NPC id, e.g. "FakeSSLTerminal", "NPC_AlexMorgan".</summary>
        public string scenarioId;
        /// <summary>Response id or scenario choice id, e.g. "o_rc_kiosk", "continue".</summary>
        public string interactionId;
        /// <summary>"email", "kiosk", "badge", "usb", "verbal", "dialogue" or the promised scenario for promises.</summary>
        public string channel;
        /// <summary>Seconds since the session started.</summary>
        public double t;
        /// <summary>Seconds since the previous event in this session.</summary>
        public double elapsed;
        /// <summary>"Safe", "Unsafe", "Neutral" (empty for lifecycle).</summary>
        public string classification;
        public bool riskyAction;
        public bool corrective;
        /// <summary>"completed" (security action), "promised" (agreed to comply later), "stance" (other reply), "n/a".</summary>
        public string completion;
        public string persuasion;
        /// <summary>Response latency in seconds, or -1 when not measurable.</summary>
        public double latency = -1;
        public string node;
        public string choiceClass;
        public string contentSource;
        public string scene;
        /// <summary>Short machine-readable detail for lifecycle events.</summary>
        public string detail;

        public bool IsDecision => kind == "dialogue_choice" || kind == "security_action";

        public static ResearchEvent FromDecision(DecisionEvent d, string scene)
        {
            bool action = d.Context.Category == DecisionCategory.SecurityAction;
            bool promise = !action && d.Outcome == DecisionOutcome.Unsafe && d.Context.ChoiceClass == "Cooperate";
            return new ResearchEvent
            {
                sourceSeq = d.Sequence,
                kind = action ? "security_action" : "dialogue_choice",
                name = d.EventName,
                scenarioId = d.ObjectId,
                interactionId = string.IsNullOrEmpty(d.Context.ResponseId) ? d.Choice : d.Context.ResponseId,
                channel = string.IsNullOrEmpty(d.Context.Channel) ? DecisionContext.ChannelFor(d.EventName) : d.Context.Channel,
                t = d.SessionTime,
                classification = d.Outcome.ToString(),
                riskyAction = d.IsRiskyAction,
                corrective = d.Context.IsCorrective,
                completion = action ? "completed" : promise ? "promised" : "stance",
                persuasion = d.Context.PersuasionStrategy ?? string.Empty,
                latency = d.Context.ResponseLatency >= 0 ? d.Context.ResponseLatency : -1,
                node = d.Context.DialogueNodeId ?? string.Empty,
                choiceClass = d.Context.ChoiceClass ?? string.Empty,
                contentSource = d.Context.ContentSource ?? string.Empty,
                scene = scene ?? string.Empty,
                detail = string.Empty
            };
        }

        public static ResearchEvent FromLifecycle(LifecycleEvent e, string scene) =>
            new ResearchEvent
            {
                kind = "lifecycle",
                name = e.EventName,
                scenarioId = e.ObjectId,
                interactionId = string.Empty,
                channel = string.Empty,
                t = e.SessionTime,
                classification = string.Empty,
                completion = "n/a",
                persuasion = string.Empty,
                latency = -1,
                node = string.Empty,
                choiceClass = string.Empty,
                contentSource = string.Empty,
                scene = scene ?? string.Empty,
                detail = e.Detail ?? string.Empty
            };
    }

    [Serializable]
    public class ConsentInfo
    {
        public string status;
        /// <summary>Session time at which consent was given (sessions start at consent, so normally 0).</summary>
        public double grantedAt;
    }

    /// <summary>Summary written into each session file (recomputed on every save from the events).</summary>
    [Serializable]
    public class SessionSummary
    {
        public string finalOutcome;
        /// <summary>Outcome before any corrective step (COMPROMISED if any risky action happened).</summary>
        public string initialOutcome;
        /// <summary>Alex's encounter outcome from the dialogue engine, if the encounter completed.</summary>
        public string encounterOutcome;
        public bool encounterCompleted;
        public int decisions;
        public int dialogueChoices;
        public int securityActions;
        public int riskyActions;
        public int safeActions;
        public int neutralActions;
        public int promises;
        public double medianLatency = -1;
        /// <summary>Descriptive 0-100 index, or -1 when there is not enough data.</summary>
        public double index = -1;
    }

    [Serializable]
    public class SessionRecord
    {
        public string schema = ResearchSchema.SessionSchema;
        public string dataset = ResearchSchema.Real;
        public string sessionId;
        /// <summary>ISO-8601 UTC, minute precision (ordering only).</summary>
        public string startedUtc;
        public string endedUtc;
        public double durationSeconds;
        /// <summary>"in_progress", "session_restart", "application_quit", "scene_unloaded", "withdrawn".</summary>
        public string endReason = "in_progress";
        public string appVersion;
        public string unityVersion;
        public string platform;
        public List<string> scenes = new List<string>();
        public ConsentInfo consent = new ConsentInfo();
        public List<ResearchEvent> events = new List<ResearchEvent>();
        public SessionSummary summary = new SessionSummary();

        public static string UtcMinute(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mmZ", CultureInfo.InvariantCulture);
    }
}
