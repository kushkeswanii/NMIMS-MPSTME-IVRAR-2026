using System;
using System.Text;
using TrustBreach.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrustBreach.Analytics
{
    /// <summary>
    /// Scene entry point of the research pipeline: subscribes a <see cref="ResearchSessionRecorder"/> to DecisionLog and
    /// ResearchConsent, survives scene changes (one instance, DontDestroyOnLoad), and adds the "Your session data" page to
    /// the VR debrief. The dataset is set by the builder: "real" in the participant scene, "test" in the DEMO scene.
    /// The environment variable TB_RESEARCH_DATASET overrides it (automated validation runs use "test").
    /// </summary>
    [DisallowMultipleComponent]
    public class ResearchEventCollector : MonoBehaviour
    {
        public const string DatasetEnvironmentVariable = "TB_RESEARCH_DATASET";

        [SerializeField, Tooltip("real = participant sessions, test = DEMO-scene / practice / automated runs. Never demo (synthetic only).")]
        private string _dataset = ResearchSchema.Real;

        public static ResearchEventCollector Instance { get; private set; }
        public ResearchSessionRecorder Recorder { get; private set; }
        public ResearchStore Store { get; private set; }
        public string Dataset => Recorder?.Dataset ?? ResolveDataset(_dataset);

        public static string ResolveDataset(string configured)
        {
            string env = Environment.GetEnvironmentVariable(DatasetEnvironmentVariable);
            string dataset = string.IsNullOrEmpty(env) ? configured : env.Trim().ToLowerInvariant();
            // Synthetic DEMO data is generated, never recorded from play.
            return ResearchStore.IsValidDataset(dataset) && dataset != ResearchSchema.Demo ? dataset : ResearchSchema.Test;
        }

        protected virtual void Awake()
        {
            string dataset = ResolveDataset(_dataset);
            if (Instance != null && Instance != this)
            {
                if (Instance.Dataset == dataset)
                {
                    Destroy(gameObject); // the surviving instance keeps the running session
                    return;
                }
                Instance.Shutdown("scene_unloaded");
                Destroy(Instance.gameObject);
            }
            Instance = this;
            transform.SetParent(null, false);
            DontDestroyOnLoad(gameObject);
            Store = new ResearchStore();
            Recorder = new ResearchSessionRecorder(Store, dataset, () => SceneManager.GetActiveScene().name);
            DecisionLog.SessionStarted += Recorder.HandleSessionStarted;
            DecisionLog.DecisionRecorded += Recorder.HandleDecision;
            DecisionLog.LifecycleRecorded += Recorder.HandleLifecycle;
            ResearchConsent.Changed += Recorder.HandleConsentChanged;
            ResearchConsent.Withdrawn += Recorder.HandleWithdrawn;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SessionDebriefHook.Provider = BuildDebriefPage;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Recorder?.HandleSceneLoaded(scene.name);

        protected virtual void OnApplicationPause(bool paused)
        {
            if (paused) Recorder?.Flush(DecisionLog.SessionTime);
        }

        protected virtual void OnApplicationQuit() => Shutdown("application_quit");

        protected virtual void OnDestroy()
        {
            if (Instance == this) Shutdown("scene_unloaded");
        }

        private void Shutdown(string reason)
        {
            if (Recorder == null) return;
            Recorder.Flush(DecisionLog.SessionTime);
            Recorder.Finish(reason);
            DecisionLog.SessionStarted -= Recorder.HandleSessionStarted;
            DecisionLog.DecisionRecorded -= Recorder.HandleDecision;
            DecisionLog.LifecycleRecorded -= Recorder.HandleLifecycle;
            ResearchConsent.Changed -= Recorder.HandleConsentChanged;
            ResearchConsent.Withdrawn -= Recorder.HandleWithdrawn;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (SessionDebriefHook.Provider == (Func<string>)BuildDebriefPage) SessionDebriefHook.Provider = null;
            Recorder = null;
            if (Instance == this) Instance = null;
        }

        /// <summary>Optional debrief page: what was (or was not) stored for this session, in plain words.</summary>
        public string BuildDebriefPage()
        {
            var sb = new StringBuilder();
            SessionRecord current = Recorder?.Current;
            if (!ResearchConsent.CanRecord || current == null)
            {
                sb.AppendLine("<b>No research data was saved for this session.</b>");
                sb.AppendLine(ResearchConsent.Status == ConsentStatus.Withdrawn
                    ? "You withdrew: everything recorded for this session has been deleted."
                    : "Research recording was not switched on. Your choices were used only for this debrief.");
                return sb.ToString().TrimEnd();
            }
            SessionMetrics m = SessionScorer.Score(current);
            sb.AppendLine($"Anonymous session <b>{current.sessionId}</b> is stored on this device only.");
            sb.AppendLine($"Recorded: {m.DialogueChoices} replies, {m.SecurityActions} security actions ({m.RiskyActions} risky, {m.SafeActions} safe).");
            sb.AppendLine($"Scenarios completed: {m.ScenariosCompleted.Count} of {ResearchSchema.ScenarioChannels.Length}.");
            if (!double.IsNaN(m.MedianLatency)) sb.AppendLine($"Median reply time: {m.MedianLatency:0.0} s.");
            sb.AppendLine(double.IsNaN(m.Index)
                ? "Descriptive index: not enough data."
                : $"Descriptive index: {m.Index:0}/100 (a summary of this run only, not a validated score).");
            sb.AppendLine();
            sb.AppendLine("No names, passwords, voice or audio were recorded.");
            sb.AppendLine("To delete this session, press <b>WITHDRAW & DELETE</b> on the research panel near the entrance.");
            return sb.ToString().TrimEnd();
        }
    }
}
