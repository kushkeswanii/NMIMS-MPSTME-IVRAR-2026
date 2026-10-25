using System;
using System.Collections.Generic;
using System.Linq;

namespace TrustBreach.Analytics
{
    /// <summary>
    /// Session outcome from the recorded events, using the Phase 3.1 rules (same as AdaptiveDialogueEngine):
    ///  - risky actions = completed security actions classified Unsafe (promises never count);
    ///  - a dialogue corrective (report Alex / verify with IT) covers every channel; a scenario report covers its own;
    ///  - COMPROMISED if any risky action is uncovered, RECOVERED if all are covered;
    ///  - otherwise RESISTED if a corrective or verification happened, DECLINED if Alex's encounter ended that way,
    ///    else INCOMPLETE.
    /// </summary>
    public static class OutcomeClassifier
    {
        public static bool IsVerification(ResearchEvent e) => e.name == "visitor_badge.identity_verified";

        public static string Classify(IList<ResearchEvent> events, string encounterOutcome, out string initialOutcome)
        {
            List<ResearchEvent> ordered = events.Where(e => e.IsDecision).OrderBy(e => e.seq).ToList();
            List<ResearchEvent> risky = ordered.Where(e => e.riskyAction).ToList();
            List<ResearchEvent> correctives = ordered.Where(e => e.corrective).ToList();

            string final;
            if (risky.Count > 0)
            {
                bool allCovered = risky.All(r => correctives.Any(c => c.seq > r.seq && (c.kind == "dialogue_choice" || c.channel == r.channel)));
                final = allCovered ? ResearchSchema.Recovered : ResearchSchema.Compromised;
                initialOutcome = ResearchSchema.Compromised;
                return final;
            }
            if (correctives.Count > 0 || ordered.Any(IsVerification))
            {
                final = ResearchSchema.Resisted;
            }
            else if (encounterOutcome == ResearchSchema.Declined)
            {
                final = ResearchSchema.Declined;
            }
            else
            {
                final = ResearchSchema.Incomplete;
            }
            initialOutcome = final;
            return final;
        }
    }

    /// <summary>
    /// Transparent weights for the optional descriptive index. This index is a summary of behaviour inside this
    /// simulation only; it is NOT a clinically or scientifically validated susceptibility measure.
    /// </summary>
    public static class IndexWeights
    {
        /// <summary>100 x (1 - unsafe-action rate); needs at least one completed Safe/Unsafe security action.</summary>
        public const double ActionSafety = 0.40;
        /// <summary>100 x (1 - risky dialogue replies / dialogue replies); needs at least one reply.</summary>
        public const double PersuasionResistance = 0.25;
        /// <summary>100 x min(1, verification events / 2); needs at least one reply or verification.</summary>
        public const double Verification = 0.15;
        /// <summary>RESISTED 100, RECOVERED 70, DECLINED 50, COMPROMISED 0; INCOMPLETE = missing.</summary>
        public const double ReportingRecovery = 0.20;
        /// <summary>Weights of missing components are dropped and the rest renormalised; fewer than 2 = no index.</summary>
        public const int MinimumComponents = 2;
    }

    public sealed class TechniqueStats
    {
        public int Responses;
        public int RiskyResponses;
        public double RiskyRate => Responses > 0 ? (double)RiskyResponses / Responses : double.NaN;
    }

    public sealed class ScenarioStats
    {
        public int SessionsCompleted;
        public int RiskyActions;
        public int SafeActions;
        public int NeutralActions;
        public int Classified => RiskyActions + SafeActions;
        public double UnsafeRate => Classified > 0 ? (double)RiskyActions / Classified : double.NaN;
    }

    /// <summary>Deterministic per-session behavioural metrics.</summary>
    public sealed class SessionMetrics
    {
        public string SessionId;
        public string Dataset;
        public string StartedUtc;
        public string FinalOutcome;
        public string InitialOutcome;
        public string EncounterOutcome;
        public int DialogueChoices;
        public int RiskyDialogueChoices;
        public int SecurityActions;
        public int RiskyActions;
        public int SafeActions;
        public int NeutralActions;
        public int ClassifiedActions => RiskyActions + SafeActions;
        /// <summary>Risky completed actions / completed actions classified Safe or Unsafe (neutral excluded).</summary>
        public double UnsafeActionRate => ClassifiedActions > 0 ? (double)RiskyActions / ClassifiedActions : double.NaN;
        /// <summary>Ask-for-ID / request-verification replies plus badge verifications.</summary>
        public int VerificationEvents;
        public double VerificationPerReply => DialogueChoices > 0 ? (double)VerificationEvents / DialogueChoices : double.NaN;
        public bool Reported;
        public bool Recovered => FinalOutcome == ResearchSchema.Recovered;
        public readonly List<double> Latencies = new List<double>();
        public double MedianLatency => Stats.Median(Latencies);
        /// <summary>Promises to act at a scenario ("OK, I'll use the kiosk").</summary>
        public int Promises;
        /// <summary>Promises followed by the promised risky action on the same channel.</summary>
        public int PromisesActedOn;
        public double IntentionActionAgreement => Promises > 0 ? (double)PromisesActedOn / Promises : double.NaN;
        public readonly HashSet<string> ScenariosCompleted = new HashSet<string>();
        public double CompletionRate => ScenariosCompleted.Count / (double)ResearchSchema.ScenarioChannels.Length;
        public readonly Dictionary<string, TechniqueStats> Techniques = new Dictionary<string, TechniqueStats>();
        public readonly Dictionary<string, ScenarioStats> Scenarios = new Dictionary<string, ScenarioStats>();
        public readonly Dictionary<string, double> IndexComponents = new Dictionary<string, double>();
        /// <summary>0-100 descriptive index, or NaN when fewer than <see cref="IndexWeights.MinimumComponents"/> components exist.</summary>
        public double Index = double.NaN;
        public double DurationSeconds;
    }

    public static class SessionScorer
    {
        /// <summary>Scenario-terminal events: the scenario counts as completed once one of these happens.</summary>
        private static readonly Dictionary<string, string> CompletionEvents = new Dictionary<string, string>
        {
            { "phishing_email.credentials_submitted", "email" }, { "phishing_email.reported", "email" },
            { "password_kiosk.credentials_entered", "kiosk" }, { "password_kiosk.cancelled", "kiosk" }, { "password_kiosk.reported", "kiosk" },
            { "visitor_badge.identity_verified", "badge" },
            { "usb_drive.connected", "usb" }, { "usb_drive.submitted_to_it", "usb" },
        };

        public static SessionMetrics Score(SessionRecord record, string scenarioFilter = null)
        {
            var m = new SessionMetrics
            {
                SessionId = record.sessionId,
                Dataset = record.dataset,
                StartedUtc = record.startedUtc,
                DurationSeconds = record.durationSeconds
            };
            List<ResearchEvent> events = (record.events ?? new List<ResearchEvent>()).OrderBy(e => e.seq).ToList();
            string encounter = events.Where(e => e.name == "alex_encounter.completed").Select(e => ParseOutcome(e.detail)).LastOrDefault();
            m.EncounterOutcome = encounter ?? string.Empty;
            m.FinalOutcome = OutcomeClassifier.Classify(events, encounter, out string initial);
            m.InitialOutcome = initial;

            bool InFilter(ResearchEvent e) => string.IsNullOrEmpty(scenarioFilter) || e.channel == scenarioFilter;

            foreach (ResearchEvent e in events.Where(x => x.IsDecision))
            {
                if (e.kind == "dialogue_choice")
                {
                    m.DialogueChoices++;
                    if (e.classification == "Unsafe") m.RiskyDialogueChoices++;
                    if (e.latency >= 0) m.Latencies.Add(e.latency);
                    if (e.choiceClass == "AskIdentification" || e.choiceClass == "RequestVerification") m.VerificationEvents++;
                    if (e.completion == "promised") m.Promises++;
                    if (!string.IsNullOrEmpty(e.persuasion) && e.persuasion != "None")
                    {
                        if (!m.Techniques.TryGetValue(e.persuasion, out TechniqueStats t)) m.Techniques[e.persuasion] = t = new TechniqueStats();
                        t.Responses++;
                        if (e.classification == "Unsafe") t.RiskyResponses++;
                    }
                }
                else if (InFilter(e))
                {
                    m.SecurityActions++;
                    if (e.riskyAction) m.RiskyActions++;
                    else if (e.classification == "Safe") m.SafeActions++;
                    else m.NeutralActions++;
                    if (OutcomeClassifier.IsVerification(e)) m.VerificationEvents++;
                    if (!m.Scenarios.TryGetValue(e.channel, out ScenarioStats s)) m.Scenarios[e.channel] = s = new ScenarioStats();
                    if (e.riskyAction) s.RiskyActions++;
                    else if (e.classification == "Safe") s.SafeActions++;
                    else s.NeutralActions++;
                }
                if (e.corrective && (e.choiceClass == "Report" || e.kind == "security_action")) m.Reported = true;
                if (CompletionEvents.TryGetValue(e.name, out string channel) && InFilter(e)) m.ScenariosCompleted.Add(channel);
            }
            foreach (string channel in m.ScenariosCompleted)
            {
                if (!m.Scenarios.TryGetValue(channel, out ScenarioStats s)) m.Scenarios[channel] = s = new ScenarioStats();
                s.SessionsCompleted = 1;
            }

            // Intention vs action: was each promise followed by the promised risky action on that channel?
            foreach (ResearchEvent promise in events.Where(e => e.completion == "promised" && e.channel != "dialogue"))
            {
                if (events.Any(a => a.kind == "security_action" && a.riskyAction && a.channel == promise.channel && a.seq > promise.seq))
                {
                    m.PromisesActedOn++;
                }
            }

            ComputeIndex(m);
            return m;
        }

        private static void ComputeIndex(SessionMetrics m)
        {
            var parts = new List<(string name, double weight, double value)>();
            if (m.ClassifiedActions > 0) parts.Add(("Action safety", IndexWeights.ActionSafety, 100.0 * (1.0 - m.UnsafeActionRate)));
            if (m.DialogueChoices > 0) parts.Add(("Persuasion resistance", IndexWeights.PersuasionResistance, 100.0 * (1.0 - (double)m.RiskyDialogueChoices / m.DialogueChoices)));
            if (m.DialogueChoices > 0 || m.VerificationEvents > 0) parts.Add(("Verification", IndexWeights.Verification, 100.0 * Math.Min(1.0, m.VerificationEvents / 2.0)));
            double? reporting = m.FinalOutcome switch
            {
                ResearchSchema.Resisted => 100.0,
                ResearchSchema.Recovered => 70.0,
                ResearchSchema.Declined => 50.0,
                ResearchSchema.Compromised => 0.0,
                _ => (double?)null
            };
            if (reporting.HasValue) parts.Add(("Reporting / recovery", IndexWeights.ReportingRecovery, reporting.Value));
            foreach (var p in parts) m.IndexComponents[p.name] = p.value;
            if (parts.Count < IndexWeights.MinimumComponents) return;
            double weight = parts.Sum(p => p.weight);
            m.Index = parts.Sum(p => p.weight * p.value) / weight;
        }

        public static string ParseOutcome(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return null;
            foreach (string part in detail.Split(';'))
            {
                if (part.StartsWith("outcome=")) return part.Substring(8).ToUpperInvariant();
            }
            return null;
        }

        /// <summary>Recomputes the summary stored in the session file.</summary>
        public static void UpdateSummary(SessionRecord record)
        {
            SessionMetrics m = Score(record);
            record.summary = new SessionSummary
            {
                finalOutcome = m.FinalOutcome,
                initialOutcome = m.InitialOutcome,
                encounterOutcome = m.EncounterOutcome,
                encounterCompleted = !string.IsNullOrEmpty(m.EncounterOutcome),
                decisions = m.DialogueChoices + m.SecurityActions,
                dialogueChoices = m.DialogueChoices,
                securityActions = m.SecurityActions,
                riskyActions = m.RiskyActions,
                safeActions = m.SafeActions,
                neutralActions = m.NeutralActions,
                promises = m.Promises,
                medianLatency = double.IsNaN(m.MedianLatency) ? -1 : Math.Round(m.MedianLatency, 3),
                index = double.IsNaN(m.Index) ? -1 : Math.Round(m.Index, 1)
            };
        }
    }

    /// <summary>Dataset-level metrics. Every rate carries its numerator and denominator.</summary>
    public sealed class AggregateMetrics
    {
        public int Sessions;
        public readonly Dictionary<string, int> OutcomeCounts = ResearchSchema.Outcomes.ToDictionary(o => o, o => 0);
        public readonly Dictionary<string, int> InitialOutcomeCounts = ResearchSchema.Outcomes.ToDictionary(o => o, o => 0);
        public int RiskyActions;
        public int ClassifiedActions;
        /// <summary>Risky completed actions / completed actions classified Safe or Unsafe, across all sessions.</summary>
        public double RiskyActionRate => ClassifiedActions > 0 ? (double)RiskyActions / ClassifiedActions : double.NaN;
        public int SessionsWithRiskyAction;
        public int RecoveredSessions;
        /// <summary>RECOVERED sessions / sessions with at least one risky action.</summary>
        public double RecoveryRate => SessionsWithRiskyAction > 0 ? (double)RecoveredSessions / SessionsWithRiskyAction : double.NaN;
        public int ScenarioCompletions;
        public int ScenarioSlots;
        /// <summary>Completed scenario instances / (sessions x scenarios in scope).</summary>
        public double CompletionRate => ScenarioSlots > 0 ? (double)ScenarioCompletions / ScenarioSlots : double.NaN;
        public readonly List<double> Latencies = new List<double>();
        public double MedianLatency => Stats.Median(Latencies);
        public double LatencyP25 => Stats.Percentile(Latencies, 0.25);
        public double LatencyP75 => Stats.Percentile(Latencies, 0.75);
        public int Promises;
        public int PromisesActedOn;
        public int VerificationEvents;
        public int DialogueChoices;
        public int ReportedSessions;
        public readonly Dictionary<string, TechniqueStats> Techniques = new Dictionary<string, TechniqueStats>();
        public readonly Dictionary<string, ScenarioStats> Scenarios = ResearchSchema.ScenarioChannels.ToDictionary(c => c, c => new ScenarioStats());
        public readonly List<double> Indices = new List<double>();
        public double MeanIndex => Indices.Count > 0 ? Indices.Average() : double.NaN;
        public readonly List<SessionMetrics> PerSession = new List<SessionMetrics>();

        public static AggregateMetrics Compute(IEnumerable<SessionRecord> sessions, string scenarioFilter = null, string outcomeFilter = null)
        {
            var a = new AggregateMetrics();
            foreach (SessionRecord record in sessions)
            {
                SessionMetrics m = SessionScorer.Score(record, scenarioFilter);
                if (!string.IsNullOrEmpty(outcomeFilter) && m.FinalOutcome != outcomeFilter) continue;
                a.PerSession.Add(m);
                a.Sessions++;
                a.OutcomeCounts[m.FinalOutcome]++;
                a.InitialOutcomeCounts[m.InitialOutcome]++;
                a.RiskyActions += m.RiskyActions;
                a.ClassifiedActions += m.ClassifiedActions;
                if (m.RiskyActions > 0) a.SessionsWithRiskyAction++;
                if (m.Recovered) a.RecoveredSessions++;
                if (m.Reported) a.ReportedSessions++;
                a.ScenarioCompletions += m.ScenariosCompleted.Count;
                a.ScenarioSlots += string.IsNullOrEmpty(scenarioFilter) ? ResearchSchema.ScenarioChannels.Length : 1;
                a.Latencies.AddRange(m.Latencies);
                a.Promises += m.Promises;
                a.PromisesActedOn += m.PromisesActedOn;
                a.VerificationEvents += m.VerificationEvents;
                a.DialogueChoices += m.DialogueChoices;
                if (!double.IsNaN(m.Index)) a.Indices.Add(m.Index);
                foreach (KeyValuePair<string, TechniqueStats> t in m.Techniques)
                {
                    if (!a.Techniques.TryGetValue(t.Key, out TechniqueStats agg)) a.Techniques[t.Key] = agg = new TechniqueStats();
                    agg.Responses += t.Value.Responses;
                    agg.RiskyResponses += t.Value.RiskyResponses;
                }
                foreach (KeyValuePair<string, ScenarioStats> s in m.Scenarios)
                {
                    if (!a.Scenarios.TryGetValue(s.Key, out ScenarioStats agg)) a.Scenarios[s.Key] = agg = new ScenarioStats();
                    agg.SessionsCompleted += s.Value.SessionsCompleted;
                    agg.RiskyActions += s.Value.RiskyActions;
                    agg.SafeActions += s.Value.SafeActions;
                    agg.NeutralActions += s.Value.NeutralActions;
                }
            }
            return a;
        }
    }

    public static class Stats
    {
        public static double Median(IList<double> values) => Percentile(values, 0.5);

        /// <summary>Linear-interpolation percentile (type 7); NaN for an empty list.</summary>
        public static double Percentile(IList<double> values, double p)
        {
            if (values == null || values.Count == 0) return double.NaN;
            List<double> sorted = values.OrderBy(v => v).ToList();
            double position = (sorted.Count - 1) * p;
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
        }
    }
}
