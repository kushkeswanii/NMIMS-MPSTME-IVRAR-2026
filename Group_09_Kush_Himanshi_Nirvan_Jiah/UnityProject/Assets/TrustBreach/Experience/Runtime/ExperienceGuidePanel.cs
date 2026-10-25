using System.Linq;
using System.Text;
using TMPro;
using TrustBreach.Core;
using TrustBreach.Dialogue;
using TrustBreach.Interactions;
using TrustBreach.Interactions.Scenarios;
using UnityEngine;

namespace TrustBreach.Experience
{
    /// <summary>
    /// Phase 5 welcome and guide panel at the entrance. Walks the participant through
    /// Welcome -> optional research consent -> XR controls -> the office scenarios -> Alex Morgan -> debrief -> restart / exit,
    /// with a six-step progress bar. It only reads existing state (consent, DecisionLog, scenario controllers, the encounter
    /// director) and records nothing itself, so research data and Phase 2-4 behaviour are unchanged.
    /// </summary>
    public class ExperienceGuidePanel : MonoBehaviour
    {
        [SerializeField] private EncounterDirector _director;
        [SerializeField] private ScenarioController[] _scenarios = new ScenarioController[0];
        [SerializeField] private TMP_Text _step;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _body;
        [SerializeField, Tooltip("Six progress segments, in stage order.")] private Renderer[] _segments = new Renderer[0];
        [SerializeField] private XRButton _primaryButton;
        [SerializeField] private TMP_Text _primaryLabel;
        [SerializeField] private GameObject _primaryGroup;
        [SerializeField] private XRButton _exitButton;
        [SerializeField] private TMP_Text _exitLabel;
        [SerializeField] private GameObject _exitGroup;
        [SerializeField, Tooltip("Seconds the second EXIT press stays armed.")] private float _confirmWindow = 5f;

        public static readonly Color DoneColor = new Color(0.1f, 0.62f, 0.66f);
        public static readonly Color CurrentColor = new Color(0.93f, 0.95f, 0.97f);
        public static readonly Color TodoColor = new Color(0.22f, 0.26f, 0.32f);

        public ExperienceStage Stage { get; private set; } = ExperienceStage.Welcome;
        public ExperienceStage Acknowledged => _acknowledged;
        public bool ExitArmed => Time.unscaledTime < _exitArmedUntil;
        public string BodyText => _body != null ? _body.text : string.Empty;
        public int ResolvedScenarioCount
        {
            get
            {
                int n = 0; // plain loop: read every frame, so no LINQ allocation
                for (int i = 0; i < _scenarios.Length; i++)
                {
                    if (_scenarios[i] != null && _scenarios[i].IsResolved) n++;
                }
                return n;
            }
        }

        private ExperienceStage _acknowledged = ExperienceStage.Welcome;
        private float _exitArmedUntil = -1f;
        private int _lastKey = -1;
        private MaterialPropertyBlock _block;

        protected virtual void OnEnable()
        {
            _primaryButton.WhenActivated += HandlePrimary;
            _exitButton.WhenActivated += HandleExit;
            DecisionLog.SessionStarted += HandleSessionStarted;
            _lastKey = -1;
            Refresh();
        }

        protected virtual void OnDisable()
        {
            _primaryButton.WhenActivated -= HandlePrimary;
            _exitButton.WhenActivated -= HandleExit;
            DecisionLog.SessionStarted -= HandleSessionStarted;
        }

        protected virtual void Update() => Refresh();

        /// <summary>
        /// A restart (from the debrief, this panel or the demo panel) resets consent and starts a new session: back to Welcome.
        /// Granting consent also starts a new session (Phase 4), but with status Granted: that must not reset the guide.
        /// </summary>
        private void HandleSessionStarted(string _)
        {
            if (ResearchConsent.Status != ConsentStatus.Undecided) return;
            _acknowledged = ExperienceStage.Welcome;
            _exitArmedUntil = -1f;
            _lastKey = -1;
        }

        public void HandlePrimary(XRButton _ = null)
        {
            if (Stage == ExperienceStage.Debrief)
            {
                if (_director != null && _director.CanRestart) _director.RestartExperience();
                return;
            }
            if (Stage < ExperienceStage.Explore) _acknowledged = ExperienceFlow.Next(Stage);
            Refresh();
        }

        public void HandleExit(XRButton _ = null)
        {
            if (Stage != ExperienceStage.Debrief) return;
            if (!ExitArmed)
            {
                _exitArmedUntil = Time.unscaledTime + _confirmWindow;
                return;
            }
            _exitArmedUntil = -1f;
            Debug.Log("[TrustBreach.Experience] participant chose EXIT");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Refresh()
        {
            bool started = _director != null && _director.Engine != null && (_director.Engine.State.Started || _director.CurrentPhase != EncounterDirector.Phase.Idle);
            bool debrief = _director != null && (_director.CurrentPhase == EncounterDirector.Phase.Debrief || _director.CurrentPhase == EncounterDirector.Phase.Closed);
            Stage = ExperienceFlow.Resolve(_acknowledged, ResearchConsent.Status != ConsentStatus.Undecided, DecisionLog.History.Count > 0, started, debrief);
            if (Stage > _acknowledged && Stage <= ExperienceStage.Explore) _acknowledged = Stage;

            int resolved = ResolvedScenarioCount;
            bool armed = ExitArmed;
            int phase = _director != null ? (int)_director.CurrentPhase : 0;
            int key = (int)Stage | resolved << 4 | (int)ResearchConsent.Status << 8 | (armed ? 1 : 0) << 12 | phase << 13;
            if (key == _lastKey) return; // text and colours only change with state (no per-frame allocation)
            _lastKey = key;

            _step.text = ExperienceFlow.StepLabel(Stage);
            _title.text = ExperienceFlow.Title(Stage);
            _body.text = BodyFor(Stage);
            PaintSegments();

            bool showPrimary = Stage != ExperienceStage.Explore && Stage != ExperienceStage.Encounter;
            SetGroup(_primaryGroup, _primaryButton, showPrimary);
            _primaryLabel.text = Stage switch
            {
                ExperienceStage.Welcome => "START",
                ExperienceStage.Consent => ResearchConsent.Status == ConsentStatus.Undecided ? "CONTINUE (NOT RECORDING)" : "CONTINUE",
                ExperienceStage.Controls => "GOT IT - LET'S GO",
                ExperienceStage.Debrief => "RESTART EXPERIENCE",
                _ => string.Empty
            };
            SetGroup(_exitGroup, _exitButton, Stage == ExperienceStage.Debrief);
            _exitLabel.text = armed ? "PRESS AGAIN TO EXIT" : "EXIT";
        }

        private string BodyFor(ExperienceStage stage)
        {
            bool demo = _director != null && _director.Mode == EncounterMode.Demo;
            switch (stage)
            {
                case ExperienceStage.Welcome:
                    return "<b>TRUST//BREACH</b> is a short cybersecurity awareness exercise in a virtual office.\n\n" +
                           "You will meet everyday security situations and a visitor who may not be who they say. " +
                           "There are no trick controls and no score on screen: just act as you would at work.\n\n" +
                           (demo ? "<color=#FFB547>DEMO / PRACTICE scene: runs here are kept in the TEST dataset, never with real research data.</color>"
                                 : "It takes about 10 minutes. Press START when you are ready.");
                case ExperienceStage.Consent:
                    string state = ResearchConsent.Status switch
                    {
                        ConsentStatus.Granted => "<color=#38E1FF>You agreed. An anonymous record is being saved.</color>",
                        ConsentStatus.Declined => "<color=#C8CDD4>You declined. Nothing is saved.</color>",
                        ConsentStatus.Withdrawn => "<color=#C8CDD4>Withdrawn. Nothing more is saved.</color>",
                        _ => "Recording is <b>OFF</b> unless you press I AGREE."
                    };
                    return "Turn to your <b>LEFT</b>: the research panel explains what an anonymous record contains.\n\n" +
                           "Taking part is optional and changes nothing in the experience. You can only agree before your first action, " +
                           "and you can withdraw there at any time.\n\n" + state;
                case ExperienceStage.Controls:
                    return "<b>Point and select:</b> aim the controller ray and pull the <b>INDEX TRIGGER</b> (Simulator: T).\n" +
                           "<b>Grab:</b> hold the <b>GRIP</b> button on an object; <b>release GRIP</b> to let go.\n" +
                           "<b>Move:</b> left thumbstick.   <b>Turn:</b> right thumbstick.\n\n" +
                           "Buttons light up when your ray is on them. Walk slowly and take a break whenever you like.";
                case ExperienceStage.Explore:
                    var sb = new StringBuilder("Look around the office and deal with anything that seems off, the way you would at work.\n\n");
                    foreach (ScenarioController s in _scenarios.Where(s => s != null))
                    {
                        sb.AppendLine(s.IsResolved ? $"<color=#5EE08A>[done]</color>  {ExperienceFlow.ScenarioLabel(s.GetType().Name)}"
                                                   : $"<color=#8A94A3>[ -- ]</color>  {ExperienceFlow.ScenarioLabel(s.GetType().Name)}");
                    }
                    sb.AppendLine();
                    sb.Append("<b>Alex Morgan</b> from IT Security is waiting to your <b>RIGHT</b>, by the IT desk.");
                    return sb.ToString();
                case ExperienceStage.Encounter:
                    return "You are talking with <b>Alex Morgan</b>. Use the panel beside Alex to reply.\n\n" +
                           "You can leave to act in the office at any time; Alex reacts when you come back. " +
                           "While Alex is talking, REPORT and END CONVERSATION are on that panel too.";
                case ExperienceStage.Debrief:
                    string outcome = _director != null && _director.LastDebrief != null ? AdaptiveDialogueEngine.OutcomeLabel(_director.LastDebrief.Outcome) : "-";
                    return $"Outcome: <b>{outcome}</b>\n\n" +
                           "Your personal debrief is on Alex's panel: use NEXT and BACK to read every page, including what to do at work.\n\n" +
                           "Press <b>RESTART EXPERIENCE</b> for the next participant (everything resets and consent is asked again), or <b>EXIT</b>.";
                default:
                    return string.Empty;
            }
        }

        private void PaintSegments()
        {
            _block ??= new MaterialPropertyBlock();
            for (int i = 0; i < _segments.Length; i++)
            {
                if (_segments[i] == null) continue;
                Color c = i < (int)Stage ? DoneColor : i == (int)Stage ? CurrentColor : TodoColor;
                _segments[i].GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", c);
                _segments[i].SetPropertyBlock(_block);
            }
        }

        private static void SetGroup(GameObject group, XRButton button, bool visible)
        {
            if (group != null && group.activeSelf != visible) group.SetActive(visible);
            if (button != null) button.SetInteractable(visible);
        }
    }
}
