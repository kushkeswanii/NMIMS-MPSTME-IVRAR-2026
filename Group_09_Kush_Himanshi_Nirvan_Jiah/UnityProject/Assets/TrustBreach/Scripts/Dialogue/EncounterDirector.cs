using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oculus.Interaction;
using Oculus.Interaction.Locomotion;
using TrustBreach.AI;
using TrustBreach.Core;
using TrustBreach.Dialogue.NPC;
using TrustBreach.Interactions.Scenarios;
using UnityEngine;

namespace TrustBreach.Dialogue
{
    public enum EncounterMode
    {
        /// <summary>Realistic participant run: no debug information, no reset, no branch selection.</summary>
        Participant,
        /// <summary>Clearly labelled developer/demo run: state readout, branch selection and full scenario reset.</summary>
        Demo
    }

    /// <summary>
    /// Orchestrates the Alex Morgan encounter: proximity greeting, line delivery (voice, subtitles, gestures, pacing),
    /// option presentation via the XR dialogue panel, choices into the adaptive engine, Phase 2.5 scenario signals,
    /// optional validated model rephrasing, debrief and (demo mode) reset.
    /// </summary>
    public class EncounterDirector : MonoBehaviour
    {
        public enum Phase
        {
            Idle,
            Delivering,
            /// <summary>A scenario event changed the conversation while the participant was away; Alex reacts on their return.</summary>
            WaitingForReturn,
            AwaitingChoice,
            AwaitingContinue,
            Debrief,
            Closed
        }

        [SerializeField] private DialogueGraph _graph;
        [SerializeField] private DialoguePanel _panel;
        [SerializeField] private NPCPresence _npc;
        [SerializeField] private NPCVoice _voice;
        [SerializeField] private EncounterMode _mode = EncounterMode.Participant;
        [SerializeField, Optional] private GameObject _demoPanel;
        [SerializeField, Optional] private LLMProviderSettings _llmSettings;

        [Header("Scenario targets (for pointing gestures)")]
        [SerializeField] private Transform _phishingEmailTarget;
        [SerializeField] private Transform _passwordKioskTarget;
        [SerializeField] private Transform _visitorBadgeTarget;
        [SerializeField] private Transform _usbTarget;

        [Header("Existing Phase 2.5 scenarios (reset in demo mode)")]
        [SerializeField] private ScenarioController[] _scenarios = new ScenarioController[0];

        [Header("Pacing")]
        [SerializeField] private float _greetDistance = 4.5f;
        [SerializeField, Tooltip("Alex delivers reactions to scenario actions once the participant is back within this distance.")]
        private float _returnDistance = 3.2f;
        [SerializeField, Optional, Tooltip("Where Restart Experience (demo) puts the participant: the XR rig spawn.")]
        private Transform _experienceStart;
        [SerializeField] private float _pauseAfterLine = 0.35f;
        [SerializeField] private float _choiceFeedbackSeconds = 0.35f;

        public const string InviteText = "Alex Morgan from IT Security would like a quick word with you.";

        public AdaptiveDialogueEngine Engine { get; private set; }
        public Phase CurrentPhase { get; private set; } = Phase.Idle;
        public EncounterMode Mode => _mode;
        public IReadOnlyList<ResponseOption> VisibleOptions => _visibleOptions;
        public DialogueContentSource LastLineSource { get; private set; } = DialogueContentSource.Scripted;
        public string LastDeliveredText { get; private set; } = string.Empty;
        public int LinesDelivered { get; private set; }
        public bool Greeted { get; private set; }
        public DialoguePanel Panel => _panel;
        public NPCVoice Voice => _voice;
        public DebriefReport LastDebrief { get; private set; }
        public bool HasPendingReaction => CurrentPhase == Phase.WaitingForReturn;
        /// <summary>
        /// Restart Experience is always available in demo mode; in participant mode only once the debrief has been shown,
        /// so a finished participant can hand over to the next one without stale state (Phase 5).
        /// </summary>
        public bool CanRestart => _mode == EncounterMode.Demo || CurrentPhase == Phase.Debrief || CurrentPhase == Phase.Closed;

        private readonly List<ResponseOption> _visibleOptions = new List<ResponseOption>();
        private Coroutine _delivery;
        private double _optionsShownAt, _lineStartedAt;
        private float _greetGestureUntil = -1f;
        private Pose _npcHome;
        private bool _signalDriven;
        private (DialogueNode node, LineVariant line)? _pendingReaction;

        protected virtual void Awake()
        {
            Engine = new AdaptiveDialogueEngine(_graph);
            Engine.NodeEntered += HandleNodeEntered;
            _npcHome = new Pose(_npc.transform.position, _npc.transform.rotation);
            if (_demoPanel != null)
            {
                _demoPanel.SetActive(_mode == EncounterMode.Demo);
            }
        }

        protected virtual void OnEnable()
        {
            _panel.TalkPressed += StartConversation;
            _panel.OptionPressed += ChooseOption;
            _panel.ContinuePressed += ShowDebrief;
            _panel.ReportPressed += ReportFromFooter;
            _panel.ExitPressed += EndFromFooter;
            _panel.MutePressed += ToggleMute;
            _panel.DebriefClosed += CloseDebrief;
            _panel.ResetPressed += RestartExperience;
        }

        protected virtual void OnDisable()
        {
            _panel.TalkPressed -= StartConversation;
            _panel.OptionPressed -= ChooseOption;
            _panel.ContinuePressed -= ShowDebrief;
            _panel.ReportPressed -= ReportFromFooter;
            _panel.ExitPressed -= EndFromFooter;
            _panel.MutePressed -= ToggleMute;
            _panel.DebriefClosed -= CloseDebrief;
            _panel.ResetPressed -= RestartExperience;
        }

        protected virtual void Start()
        {
            _panel.SetSpeaker(_graph.SpeakerName, _graph.SpeakerRole);
            _panel.SetMuted(NPCVoice.Muted);
            _panel.ShowIdle(InviteText, string.Empty, true);
        }

        protected virtual void Update()
        {
            if (CurrentPhase == Phase.Idle && !Greeted && Camera.main != null &&
                Vector3.Distance(Camera.main.transform.position, _npc.transform.position) < _greetDistance)
            {
                Greeted = true;
                DecisionLog.RecordLifecycle("alex_encounter.approached", _graph.ObjectId);
                _npc.SetGesture(NpcGesture.Wave);
                _voice.Speak(_graph.Greeting.Text, _graph.Greeting.Voice);
                _greetGestureUntil = Time.time + 2.5f;
            }
            if (CurrentPhase == Phase.WaitingForReturn && _pendingReaction.HasValue && ParticipantNearby(_returnDistance))
            {
                (DialogueNode node, LineVariant line) pending = _pendingReaction.Value;
                _pendingReaction = null;
                DecisionLog.RecordLifecycle("alex_encounter.reaction_delivered_on_return", _graph.ObjectId, $"node={pending.node.Id}");
                _delivery = StartCoroutine(Deliver(pending.node, pending.line));
            }
            if (_greetGestureUntil > 0f && Time.time > _greetGestureUntil)
            {
                _greetGestureUntil = -1f;
                if (CurrentPhase == Phase.Idle) _npc.SetGesture(NpcGesture.Idle);
            }
        }

        // ------------------------------------------------------------------ participant actions (panel buttons)
        public void StartConversation()
        {
            if (CurrentPhase != Phase.Idle) return;
            Greeted = true;
            _voice.Stop(notify: false);
            _npc.Engaged = true;
            Engine.Begin();
        }

        public void ChooseOption(int index)
        {
            if (CurrentPhase != Phase.AwaitingChoice || index < 0 || index >= _visibleOptions.Count) return;
            ResponseOption option = _visibleOptions[index];
            double latency = Time.realtimeSinceStartupAsDouble - _optionsShownAt;
            _panel.MarkChosen(index);
            CurrentPhase = Phase.Delivering;
            StartCoroutine(AfterFeedback(() => Engine.Choose(option, latency, LastLineSource)));
        }

        public void ReportFromFooter() => ChooseGlobal(ChoiceClass.Report);
        public void EndFromFooter() => ChooseGlobal(ChoiceClass.Exit);

        private void ChooseGlobal(ChoiceClass choiceClass)
        {
            if (!Engine.IsActive || (CurrentPhase != Phase.AwaitingChoice && CurrentPhase != Phase.Delivering)) return;
            double since = CurrentPhase == Phase.AwaitingChoice ? _optionsShownAt : _lineStartedAt;
            double latency = Time.realtimeSinceStartupAsDouble - since;
            _panel.MarkChosen(-1);
            StopDelivery();
            CurrentPhase = Phase.Delivering;
            StartCoroutine(AfterFeedback(() => Engine.ChooseGlobal(choiceClass, latency, LastLineSource)));
        }

        public void ShowDebrief()
        {
            if (CurrentPhase != Phase.AwaitingContinue) return;
            CurrentPhase = Phase.Debrief;
            _npc.Engaged = false;
            LastDebrief = Engine.BuildDebriefReport();
            string analyticsPage = SessionDebriefHook.Provider?.Invoke();
            if (!string.IsNullOrEmpty(analyticsPage))
            {
                LastDebrief.PageTitles.Add(SessionDebriefHook.PageTitle);
                LastDebrief.Pages.Add(analyticsPage);
            }
            _panel.ShowDebrief(LastDebrief.Headline, LastDebrief.PageTitles, LastDebrief.Pages, CanRestart, OutcomeColor(Engine.State.Outcome));
            DecisionLog.RecordLifecycle("alex_encounter.debrief_shown", _graph.ObjectId,
                $"outcome={Engine.State.Outcome};recovery={LastDebrief.RecoveryOccurred};risky_actions={Engine.State.RiskyActionCount}");
        }

        public void CloseDebrief()
        {
            if (CurrentPhase != Phase.Debrief) return;
            CurrentPhase = Phase.Closed;
            _panel.HideDebrief();
            _panel.ShowIdle("Conversation ended. Thank you for taking part.",
                _mode == EncounterMode.Demo ? "Demo mode: use RESET on the demo panel to run again."
                    : "To start again, press RESTART on the welcome panel at the entrance.", false);
        }

        public void ToggleMute()
        {
            NPCVoice.Muted = !NPCVoice.Muted;
            _panel.SetMuted(NPCVoice.Muted);
            DecisionLog.RecordLifecycle("settings.voice_muted", _graph.ObjectId, $"muted={NPCVoice.Muted}");
        }

        /// <summary>Ends the current line immediately (demo panel "skip" and automated tests).</summary>
        public void SkipLineDelivery()
        {
            if (_voice.IsSpeaking) _voice.Stop(notify: true);
        }

        // ------------------------------------------------------------------ scenario integration
        public void HandleScenarioSignal(ScenarioSignal signal)
        {
            _signalDriven = true;
            try
            {
                Engine.HandleSignal(signal); // node changes are delivered (or deferred) through HandleNodeEntered
            }
            finally
            {
                _signalDriven = false;
            }
        }

        private static Color OutcomeColor(EncounterOutcome outcome) => outcome switch
        {
            EncounterOutcome.Compromised => new Color(0.75f, 0.13f, 0.12f),
            EncounterOutcome.Recovered => new Color(0.82f, 0.5f, 0.06f),
            EncounterOutcome.Resisted => new Color(0.1f, 0.55f, 0.3f),
            _ => new Color(0.32f, 0.36f, 0.42f)
        };

        private bool ParticipantNearby(float distance) =>
            Camera.main != null && Vector3.Distance(Flat(Camera.main.transform.position), Flat(_npc.transform.position)) < distance;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ------------------------------------------------------------------ demo mode
        public void JumpToBranch(string nodeId, int suspicion, int cooperation, params string[] flags)
        {
            if (_mode != EncounterMode.Demo) return;
            StopDelivery();
            _npc.Engaged = true;
            Greeted = true;
            Engine.JumpTo(nodeId, suspicion, cooperation, flags);
        }

        public void ResetDemo()
        {
            if (_mode != EncounterMode.Demo) return;
            ResetEncounter(includeScenarios: true);
        }

        /// <summary>Full reset (encounter, scenarios, new session) and the participant back at the entrance. See <see cref="CanRestart"/>.</summary>
        public void RestartExperience()
        {
            if (!CanRestart) return;
            ResetEncounter(includeScenarios: true);
            var locomotor = FindAnyObjectByType<FirstPersonLocomotor>();
            if (locomotor != null && _experienceStart != null)
            {
                locomotor.HandleLocomotionEvent(new LocomotionEvent(0, new Pose(_experienceStart.position, _experienceStart.rotation),
                    LocomotionEvent.TranslationType.Absolute, LocomotionEvent.RotationType.Absolute));
            }
            DecisionLog.RecordLifecycle("demo.restart_experience", _graph.ObjectId);
        }

        /// <summary>Restores Alex, the conversation and (optionally) the four Phase 2.5 scenarios; starts a new anonymous session.</summary>
        public void ResetEncounter(bool includeScenarios)
        {
            StopDelivery();
            StopAllCoroutines();
            _voice.Stop(notify: false);
            _npc.ResetPresence();
            _npc.transform.SetPositionAndRotation(_npcHome.position, _npcHome.rotation);
            Engine.Reset();
            if (includeScenarios)
            {
                foreach (ScenarioController scenario in _scenarios.Where(s => s != null))
                {
                    scenario.ResetScenario();
                }
            }
            DecisionLog.RecordLifecycle("demo.reset", _graph.ObjectId, $"scenarios={includeScenarios}");
            ResearchConsent.Reset(); // a new participant is asked for consent again
            DecisionLog.StartSession();
            _visibleOptions.Clear();
            _pendingReaction = null;
            Greeted = false;
            LinesDelivered = 0;
            CurrentPhase = Phase.Idle;
            _panel.ShowIdle(InviteText, string.Empty, true);
        }

        // ------------------------------------------------------------------ delivery
        private void HandleNodeEntered(DialogueNode node, LineVariant line)
        {
            StopDelivery();
            if (_signalDriven && !ParticipantNearby(_returnDistance))
            {
                // The participant acted elsewhere in the office; Alex reacts when they come back.
                _pendingReaction = (node, line);
                _visibleOptions.Clear();
                CurrentPhase = Phase.WaitingForReturn;
                _npc.SetGesture(NpcGesture.Idle);
                _panel.ShowWaiting("Alex is waiting for you to come back.");
                DecisionLog.RecordLifecycle("alex_encounter.reaction_deferred", _graph.ObjectId, $"node={node.Id}");
                return;
            }
            _pendingReaction = null;
            _delivery = StartCoroutine(Deliver(node, line));
        }

        private void StopDelivery()
        {
            if (_delivery != null)
            {
                StopCoroutine(_delivery);
                _delivery = null;
            }
            _voice.Stop(notify: false);
        }

        private IEnumerator AfterFeedback(System.Action action)
        {
            yield return new WaitForSeconds(_choiceFeedbackSeconds);
            action();
        }

        private IEnumerator Deliver(DialogueNode node, LineVariant line)
        {
            CurrentPhase = Phase.Delivering;
            _visibleOptions.Clear();
            string text = line?.Text ?? string.Empty;
            DialogueContentSource source = DialogueContentSource.Scripted;

            string generated = null;
            yield return TryModelLine(node, line, accepted => generated = accepted);
            if (!string.IsNullOrEmpty(generated))
            {
                text = generated;
                source = DialogueContentSource.Model;
            }
            LastLineSource = source;
            LastDeliveredText = text;
            LinesDelivered++;
            _lineStartedAt = Time.realtimeSinceStartupAsDouble;
            DecisionLog.RecordLifecycle("alex_encounter.line_delivered", _graph.ObjectId,
                $"node={node.Id};line={line?.Id};strategy={line?.Strategy};escalation={Engine.State.Escalation};source={source.ToString().ToLowerInvariant()}");

            NpcGesture gesture = line?.Gesture ?? NpcGesture.Idle;
            _npc.SetGesture(gesture, TargetFor(line?.Action ?? ScenarioAction.None));
            _panel.ShowLine(text, line?.Objective, "Alex is speaking...");
            // Model-generated wording has no pre-recorded clip: subtitles only.
            _voice.Speak(text, source == DialogueContentSource.Scripted ? line?.Voice : null);
            while (_voice.IsSpeaking)
            {
                yield return null;
            }
            yield return new WaitForSeconds(_pauseAfterLine);

            if (gesture == NpcGesture.Explain || gesture == NpcGesture.Wave || gesture == NpcGesture.Point)
            {
                _npc.SetGesture(NpcGesture.Idle);
            }

            if (node.Kind == NodeKind.Terminal)
            {
                CurrentPhase = Phase.AwaitingContinue;
                _panel.ShowContinue("The conversation is over. Press CONTINUE.");
            }
            else
            {
                _visibleOptions.AddRange(Engine.GetAvailableOptions());
                _panel.ShowOptions(_visibleOptions.Select(o => o.Text).ToList(),
                    node.Kind == NodeKind.AwaitScenario ? "Act on Alex's request in the office, or choose a reply." : "Choose your response.");
                _optionsShownAt = Time.realtimeSinceStartupAsDouble;
                CurrentPhase = Phase.AwaitingChoice;
            }
            _delivery = null;
        }

        private Vector3? TargetFor(ScenarioAction action)
        {
            Transform target = action switch
            {
                ScenarioAction.DirectToPhishingEmail => _phishingEmailTarget,
                ScenarioAction.DirectToPasswordKiosk => _passwordKioskTarget,
                ScenarioAction.PresentVisitorBadge => _visitorBadgeTarget,
                ScenarioAction.EncourageUsbConnection => _usbTarget,
                _ => null
            };
            return target != null ? target.position : (Vector3?)null;
        }

        /// <summary>
        /// Optional model rephrasing. Only runs when enabled in settings AND a provider is registered and available;
        /// the result must pass <see cref="DialogueResponseValidator"/>, otherwise the scripted line is kept.
        /// </summary>
        private IEnumerator TryModelLine(DialogueNode node, LineVariant line, System.Action<string> onAccepted)
        {
            ILLMDialogueProvider provider = LLMDialogueProviderRegistry.Current;
            if (line == null || _llmSettings == null || !_llmSettings.Enabled || provider == null || !provider.IsAvailable)
            {
                yield break;
            }
            var request = new LLMDialogueRequest
            {
                EncounterId = _graph.EncounterId,
                NodeId = node.Id,
                ScriptedLine = line.Text,
                PersuasionStrategy = line.Strategy.ToString(),
                EscalationLevel = Engine.State.Escalation,
                PermittedActions = new[] { "None", line.Action.ToString() },
                ConversationSummary = string.Join(",", Engine.State.Choices.Select(c => c.Class.ToString())),
                MaxCharacters = _llmSettings.MaxCharacters
            };
            using (var cancel = new CancellationTokenSource())
            {
                Task<LLMDialogueResult> task = provider.GenerateLineAsync(request, cancel.Token);
                float deadline = Time.realtimeSinceStartup + _llmSettings.TimeoutSeconds;
                while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                if (!task.IsCompleted)
                {
                    cancel.Cancel();
                    DecisionLog.RecordLifecycle("alex_encounter.model_fallback", _graph.ObjectId, $"node={node.Id};reason=timeout");
                    yield break;
                }
                LLMDialogueResult result = task.IsFaulted || task.IsCanceled ? LLMDialogueResult.Failed("exception") : task.Result;
                ValidationResult validation = DialogueResponseValidator.Validate(result, request);
                if (!validation.Valid)
                {
                    DecisionLog.RecordLifecycle("alex_encounter.model_fallback", _graph.ObjectId, $"node={node.Id};reason={validation.Reason}");
                    yield break;
                }
                onAccepted(result.Text.Trim());
            }
        }
    }
}
