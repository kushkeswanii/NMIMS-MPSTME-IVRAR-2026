using System;
using System.Globalization;
using System.IO;
using TrustBreach.Core;
using UnityEngine;

namespace TrustBreach.Telemetry
{
    /// <summary>
    /// Writes every <see cref="DecisionLog"/> decision and lifecycle event to local files for Phase 4 analysis:
    ///   &lt;persistentDataPath&gt;/TrustBreach/telemetry/trial_&lt;sessionId&gt;.csv          (one row per decision)
    ///   &lt;persistentDataPath&gt;/TrustBreach/telemetry/trial_&lt;sessionId&gt;_scenario_outcomes.csv (actual scenario actions only)
    ///   &lt;persistentDataPath&gt;/TrustBreach/telemetry/trial_&lt;sessionId&gt;_events.jsonl (decisions + lifecycle, JSON lines)
    /// Only anonymous ids and classifications are written: no names, credentials, free text or audio.
    /// Phase 4: nothing is written unless <see cref="ResearchConsent.CanRecord"/>; files are created lazily on the first
    /// consented event and deleted when the participant withdraws.
    /// </summary>
    public class TelemetryRecorder : MonoBehaviour
    {
        [SerializeField, Tooltip("Disable to keep events in memory only (DecisionLog.History).")]
        private bool _writeFiles = true;

        [SerializeField, Tooltip("Folder under Application.persistentDataPath.")]
        private string _folder = "TrustBreach/telemetry";

        public string CsvPath { get; private set; }
        public string JsonlPath { get; private set; }
        public string ScenarioOutcomesPath { get; private set; }
        public const string ScenarioOutcomesHeader = "session_id,seq,session_time_s,scenario_id,event_type,choice,outcome,risky_action,corrective,channel";
        public int DecisionsWritten { get; private set; }
        public int LifecycleWritten { get; private set; }
        public int ScenarioOutcomesWritten { get; private set; }

        [Serializable]
        private struct JsonDecision
        {
            public string type, session_id, event_name, object_id, choice, outcome, source, dialogue_node, response_id,
                persuasion_strategy, choice_class, content_source, category, channel;
            public int seq;
            public double session_time_s, response_latency_s;
            public bool risky, risky_action, corrective, recovery_after_risk;
        }

        [Serializable]
        private struct JsonLifecycle
        {
            public string type, session_id, event_name, object_id, detail;
            public double session_time_s;
        }

        protected virtual void OnEnable()
        {
            DecisionLog.DecisionRecorded += HandleDecision;
            DecisionLog.LifecycleRecorded += HandleLifecycle;
            DecisionLog.SessionStarted += HandleSessionStarted;
            ResearchConsent.Withdrawn += HandleWithdrawn;
            OpenSession(DecisionLog.SessionId);
        }

        protected virtual void OnDisable()
        {
            DecisionLog.DecisionRecorded -= HandleDecision;
            DecisionLog.LifecycleRecorded -= HandleLifecycle;
            DecisionLog.SessionStarted -= HandleSessionStarted;
            ResearchConsent.Withdrawn -= HandleWithdrawn;
        }

        private void HandleWithdrawn(string sessionId)
        {
            foreach (string path in new[] { CsvPath, JsonlPath, ScenarioOutcomesPath })
            {
                try
                {
                    if (!string.IsNullOrEmpty(path) && path.Contains(sessionId) && File.Exists(path)) File.Delete(path);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TrustBreach.Telemetry] could not delete {path}: {e.Message}");
                }
            }
            Debug.Log($"[TrustBreach.Telemetry] participant withdrew: deleted telemetry for session {sessionId}");
        }

        private void HandleSessionStarted(string sessionId) => OpenSession(sessionId);

        private void OpenSession(string sessionId)
        {
            DecisionsWritten = 0;
            LifecycleWritten = 0;
            ScenarioOutcomesWritten = 0;
            if (!_writeFiles)
            {
                return;
            }
            try
            {
                // Paths only: files are created on the first consented event (see Append).
                string folder = Path.Combine(Application.persistentDataPath, _folder);
                CsvPath = Path.Combine(folder, $"trial_{sessionId}.csv");
                JsonlPath = Path.Combine(folder, $"trial_{sessionId}_events.jsonl");
                ScenarioOutcomesPath = Path.Combine(folder, $"trial_{sessionId}_scenario_outcomes.csv");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrustBreach.Telemetry] cannot open telemetry files: {e.Message}");
                CsvPath = JsonlPath = null;
            }
        }

        private void HandleDecision(DecisionEvent d)
        {
            if (!_writeFiles || CsvPath == null || !ResearchConsent.CanRecord)
            {
                return;
            }
            EnsureFiles();
            var json = new JsonDecision
            {
                type = "decision", session_id = d.SessionId, seq = d.Sequence, session_time_s = Round(d.SessionTime),
                event_name = d.EventName, object_id = d.ObjectId, choice = d.Choice, outcome = d.Outcome.ToString(),
                risky = d.IsRisky, recovery_after_risk = d.RecoveryAfterRisk, source = d.Context.Source.ToString(),
                dialogue_node = d.Context.DialogueNodeId, response_id = d.Context.ResponseId,
                persuasion_strategy = d.Context.PersuasionStrategy, response_latency_s = Round(d.Context.ResponseLatency),
                choice_class = d.Context.ChoiceClass, content_source = d.Context.ContentSource,
                category = d.Context.Category.ToString(), channel = d.Context.Channel, risky_action = d.IsRiskyAction,
                corrective = d.Context.IsCorrective
            };
            if (Append(CsvPath, d.ToCsvRow(), DecisionEvent.CsvHeader) && Append(JsonlPath, JsonUtility.ToJson(json)))
            {
                DecisionsWritten++;
            }
            if (d.Context.Source == DecisionSource.Scenario)
            {
                // Actual scenario outcomes, kept separate from dialogue intentions.
                string row = string.Join(",", d.SessionId, d.Sequence.ToString(CultureInfo.InvariantCulture),
                    d.SessionTime.ToString("F3", CultureInfo.InvariantCulture), d.ObjectId, d.EventName, d.Choice, d.Outcome,
                    d.IsRiskyAction ? "1" : "0", d.Context.IsCorrective ? "1" : "0", d.Context.Channel);
                if (Append(ScenarioOutcomesPath, row, ScenarioOutcomesHeader)) ScenarioOutcomesWritten++;
            }
        }

        private void HandleLifecycle(LifecycleEvent e)
        {
            if (!_writeFiles || JsonlPath == null || !ResearchConsent.CanRecord)
            {
                return;
            }
            EnsureFiles();
            var json = new JsonLifecycle
            {
                type = "lifecycle", session_id = e.SessionId, session_time_s = Round(e.SessionTime), event_name = e.EventName,
                object_id = e.ObjectId, detail = e.Detail
            };
            if (Append(JsonlPath, JsonUtility.ToJson(json)))
            {
                LifecycleWritten++;
            }
        }

        /// <summary>A consented session always has all three files (as before consent gating), even with no scenario actions.</summary>
        private void EnsureFiles()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CsvPath));
                if (!File.Exists(CsvPath)) File.WriteAllText(CsvPath, DecisionEvent.CsvHeader + "\n");
                if (!File.Exists(ScenarioOutcomesPath)) File.WriteAllText(ScenarioOutcomesPath, ScenarioOutcomesHeader + "\n");
                if (!File.Exists(JsonlPath)) File.WriteAllText(JsonlPath, string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrustBreach.Telemetry] cannot create telemetry files: {e.Message}");
            }
        }

        private static bool Append(string path, string line, string header = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (header != null && !File.Exists(path))
                {
                    File.WriteAllText(path, header + "\n");
                }
                File.AppendAllText(path, line + "\n");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrustBreach.Telemetry] write failed: {e.Message}");
                return false;
            }
        }

        private static double Round(double value) => double.Parse(value.ToString("F3", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
