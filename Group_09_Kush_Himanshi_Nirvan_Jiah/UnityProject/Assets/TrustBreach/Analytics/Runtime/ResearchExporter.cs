using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TrustBreach.Analytics
{
    /// <summary>
    /// Writes one dataset to &lt;dataset&gt;/exports/&lt;yyyyMMdd_HHmmss&gt;/:
    ///   sessions/&lt;id&gt;.json, events.csv, session_summary.csv, aggregate_metrics.csv, export_manifest.json.
    /// CSV follows RFC 4180 (quoted when needed, doubled quotes, CRLF rows), numbers use the invariant culture,
    /// missing values are empty cells. Every file is written atomically.
    /// </summary>
    public static class ResearchExporter
    {
        public const string EventsHeader =
            "dataset,session_id,seq,source_seq,kind,event_name,scenario_id,interaction_id,channel,t_s,elapsed_s,classification," +
            "risky_action,corrective,completion,persuasion_technique,latency_s,dialogue_node,choice_class,content_source,scene,detail";

        public const string SummaryHeader =
            "dataset,session_id,started_utc,duration_s,end_reason,consent,final_outcome,initial_outcome,encounter_outcome,dialogue_choices," +
            "risky_dialogue_choices,security_actions,risky_actions,safe_actions,neutral_actions,unsafe_action_rate,verification_events," +
            "reported,recovered,promises,promises_acted_on,intention_action_agreement,scenarios_completed,completion_rate,median_latency_s,descriptive_index";

        public const string AggregateHeader = "dataset,metric,scope,numerator,denominator,value,unit,definition";

        public sealed class Result
        {
            public string Folder;
            public int Sessions;
            public int Events;
            public readonly List<string> Files = new List<string>();
        }

        [Serializable]
        private class Manifest
        {
            public string schema = ResearchSchema.SessionSchema;
            public string dataset;
            public string generatedUtc;
            public int sessions;
            public int events;
            public string[] files;
            public string notice;
        }

        public static Result Export(ResearchStore store, string dataset, IList<SessionRecord> sessions, DateTime? nowUtc = null)
        {
            DateTime now = nowUtc ?? DateTime.UtcNow;
            string folder = Path.Combine(store.ExportsDir(dataset), now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            int suffix = 1;
            while (Directory.Exists(folder)) folder = Path.Combine(store.ExportsDir(dataset), $"{now:yyyyMMdd_HHmmss}_{++suffix}");
            Directory.CreateDirectory(folder);
            var result = new Result { Folder = folder, Sessions = sessions.Count };

            foreach (SessionRecord s in sessions)
            {
                Write(result, Path.Combine("sessions", s.sessionId + ".json"), JsonUtility.ToJson(s, true));
            }
            Write(result, "events.csv", EventsCsv(sessions));
            result.Events = sessions.Sum(s => s.events.Count);
            Write(result, "session_summary.csv", SummaryCsv(sessions));
            Write(result, "aggregate_metrics.csv", AggregateCsv(dataset, AggregateMetrics.Compute(sessions)));
            var manifest = new Manifest
            {
                dataset = dataset,
                generatedUtc = now.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                sessions = result.Sessions,
                events = result.Events,
                files = result.Files.Concat(new[] { "export_manifest.json" }).ToArray(),
                notice = dataset == ResearchSchema.Demo
                    ? "DEMO DATA: synthetic sessions generated for demonstration. Not research findings."
                    : dataset == ResearchSchema.Test
                        ? "TEST / PRACTICE DATA: developer, demo-scene and automated runs. Not participant data."
                        : "Anonymous consented sessions. Metrics are descriptive of behaviour in this simulation; the index is not a validated measure."
            };
            Write(result, "export_manifest.json", JsonUtility.ToJson(manifest, true));
            return result;
        }

        private static void Write(Result result, string relative, string content)
        {
            ResearchStore.AtomicWrite(Path.Combine(result.Folder, relative), content);
            result.Files.Add(relative.Replace('\\', '/'));
        }

        public static string EventsCsv(IEnumerable<SessionRecord> sessions)
        {
            var sb = new StringBuilder();
            Row(sb, EventsHeader);
            foreach (SessionRecord s in sessions)
            {
                foreach (ResearchEvent e in s.events.OrderBy(e => e.seq))
                {
                    Row(sb, Csv(s.dataset), Csv(s.sessionId), Int(e.seq), Int(e.sourceSeq), Csv(e.kind), Csv(e.name), Csv(e.scenarioId),
                        Csv(e.interactionId), Csv(e.channel), Num(e.t), Num(e.elapsed), Csv(e.classification), Bool(e.riskyAction),
                        Bool(e.corrective), Csv(e.completion), Csv(e.persuasion), e.latency >= 0 ? Num(e.latency) : string.Empty, Csv(e.node),
                        Csv(e.choiceClass), Csv(e.contentSource), Csv(e.scene), Csv(e.detail));
                }
            }
            return sb.ToString();
        }

        public static string SummaryCsv(IEnumerable<SessionRecord> sessions)
        {
            var sb = new StringBuilder();
            Row(sb, SummaryHeader);
            foreach (SessionRecord s in sessions)
            {
                SessionMetrics m = SessionScorer.Score(s);
                Row(sb, Csv(s.dataset), Csv(s.sessionId), Csv(s.startedUtc), Num(s.durationSeconds), Csv(s.endReason), Csv(s.consent?.status),
                    Csv(m.FinalOutcome), Csv(m.InitialOutcome), Csv(m.EncounterOutcome), Int(m.DialogueChoices), Int(m.RiskyDialogueChoices),
                    Int(m.SecurityActions), Int(m.RiskyActions), Int(m.SafeActions), Int(m.NeutralActions), Num(m.UnsafeActionRate),
                    Int(m.VerificationEvents), Bool(m.Reported), Bool(m.Recovered), Int(m.Promises), Int(m.PromisesActedOn),
                    Num(m.IntentionActionAgreement), Csv(string.Join("|", m.ScenariosCompleted.OrderBy(c => c, StringComparer.Ordinal))),
                    Num(m.CompletionRate), Num(m.MedianLatency), Num(m.Index));
            }
            return sb.ToString();
        }

        public static string AggregateCsv(string dataset, AggregateMetrics a)
        {
            var sb = new StringBuilder();
            Row(sb, AggregateHeader);
            void Metric(string metric, string scope, double num, double den, double value, string unit, string definition) =>
                Row(sb, Csv(dataset), Csv(metric), Csv(scope), Num(num), Num(den), Num(value), Csv(unit), Csv(definition));

            Metric("sessions", "all", a.Sessions, double.NaN, a.Sessions, "count", "Sessions in the dataset (after filters)");
            foreach (string o in ResearchSchema.Outcomes)
            {
                Metric("final_outcome_share", o, a.OutcomeCounts[o], a.Sessions, Ratio(a.OutcomeCounts[o], a.Sessions), "ratio",
                    "Sessions with this final outcome / sessions");
                Metric("initial_outcome_share", o, a.InitialOutcomeCounts[o], a.Sessions, Ratio(a.InitialOutcomeCounts[o], a.Sessions), "ratio",
                    "Sessions with this outcome before any corrective step / sessions");
            }
            Metric("risky_action_rate", "all", a.RiskyActions, a.ClassifiedActions, a.RiskyActionRate, "ratio",
                "Completed risky security actions / completed security actions classified Safe or Unsafe (neutral excluded)");
            Metric("recovery_rate", "all", a.RecoveredSessions, a.SessionsWithRiskyAction, a.RecoveryRate, "ratio",
                "RECOVERED sessions / sessions with at least one risky action");
            Metric("reporting_rate", "all", a.ReportedSessions, a.Sessions, Ratio(a.ReportedSessions, a.Sessions), "ratio",
                "Sessions with a report (to IT or of Alex) / sessions");
            Metric("scenario_completion_rate", "all", a.ScenarioCompletions, a.ScenarioSlots, a.CompletionRate, "ratio",
                "Scenario instances reaching a terminal action / (sessions x scenarios)");
            Metric("verification_per_reply", "all", a.VerificationEvents, a.DialogueChoices, Ratio(a.VerificationEvents, a.DialogueChoices), "ratio",
                "Ask-for-ID / independent-verification replies + badge checks / dialogue replies");
            Metric("intention_action_agreement", "all", a.PromisesActedOn, a.Promises, Ratio(a.PromisesActedOn, a.Promises), "ratio",
                "Promises to comply followed by the promised risky action / promises");
            Metric("median_decision_time", "dialogue", double.NaN, a.Latencies.Count, a.MedianLatency, "s", "Median reply latency (n = replies with latency)");
            Metric("p25_decision_time", "dialogue", double.NaN, a.Latencies.Count, a.LatencyP25, "s", "25th percentile reply latency");
            Metric("p75_decision_time", "dialogue", double.NaN, a.Latencies.Count, a.LatencyP75, "s", "75th percentile reply latency");
            Metric("mean_descriptive_index", "all", double.NaN, a.Indices.Count, a.MeanIndex, "0-100",
                "Mean of per-session descriptive index (weights 0.40/0.25/0.15/0.20, renormalised); NOT a validated measure");
            foreach (KeyValuePair<string, ScenarioStats> s in a.Scenarios.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                Metric("scenario_unsafe_rate", s.Key, s.Value.RiskyActions, s.Value.Classified, s.Value.UnsafeRate, "ratio",
                    "Risky completed actions / Safe+Unsafe completed actions in this scenario");
                Metric("scenario_completion", s.Key, s.Value.SessionsCompleted, a.Sessions, Ratio(s.Value.SessionsCompleted, a.Sessions), "ratio",
                    "Sessions completing this scenario / sessions");
            }
            foreach (KeyValuePair<string, TechniqueStats> t in a.Techniques.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                Metric("technique_risky_reply_rate", t.Key, t.Value.RiskyResponses, t.Value.Responses, t.Value.RiskyRate, "ratio",
                    "Risky replies to lines using this technique / replies to this technique");
            }
            return sb.ToString();
        }

        private static double Ratio(double num, double den) => den > 0 ? num / den : double.NaN;

        private static void Row(StringBuilder sb, params string[] cells)
        {
            sb.Append(string.Join(",", cells));
            sb.Append("\r\n");
        }

        /// <summary>RFC 4180 field. Text starting with = + @ or a tab is prefixed with ' so spreadsheets do not run it as a formula.</summary>
        public static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if ("=+@\t".IndexOf(value[0]) >= 0) value = "'" + value;
            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || value != value.Trim();
            return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public static string Num(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? string.Empty : Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Bool(bool value) => value ? "true" : "false";
    }
}
