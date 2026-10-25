using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TrustBreach.AI;
using TrustBreach.Core;

namespace TrustBreach.Dialogue
{
    public readonly struct ChoiceRecord
    {
        public readonly int Order;
        public readonly double Time;
        public readonly string NodeId;
        public readonly string OptionId;
        public readonly string Text;
        public readonly ChoiceClass Class;
        public readonly DecisionOutcome Outcome;
        public readonly PersuasionStrategy RespondingTo;

        public ChoiceRecord(int order, double time, string nodeId, string optionId, string text, ChoiceClass choiceClass, DecisionOutcome outcome,
            PersuasionStrategy respondingTo)
        {
            Order = order;
            Time = time;
            NodeId = nodeId;
            OptionId = optionId;
            Text = text;
            Class = choiceClass;
            Outcome = outcome;
            RespondingTo = respondingTo;
        }
    }

    /// <summary>A completed security action (scenario decision or verbal disclosure / report) - never a mere promise.</summary>
    public readonly struct SecurityActionRecord
    {
        public readonly int Order;
        public readonly double Time;
        public readonly string Channel;
        public readonly string Description;
        public readonly bool Risky;
        /// <summary>Report to IT Security or independent verification that can correct a risky action.</summary>
        public readonly bool Corrective;
        /// <summary>Safe identity / authenticity check (counts toward Resisted, does not correct a compromise).</summary>
        public readonly bool Verification;
        /// <summary>"scenario" (done at an office object) or "dialogue" (said to Alex).</summary>
        public readonly string Source;

        public SecurityActionRecord(int order, double time, string channel, string description, bool risky, bool corrective, bool verification, string source)
        {
            Order = order;
            Time = time;
            Channel = channel;
            Description = description;
            Risky = risky;
            Corrective = corrective;
            Verification = verification;
            Source = source;
        }

        /// <summary>Dialogue correctives (reporting Alex, verifying with IT) cover every channel; scenario reports cover their own.</summary>
        public bool Covers(SecurityActionRecord risky) => Corrective && Order > risky.Order && (Source == "dialogue" || Channel == risky.Channel);
    }

    /// <summary>Everything the adaptive engine tracks about one encounter.</summary>
    public class DialogueState
    {
        public string CurrentNodeId;
        public string CurrentLineId;
        public PersuasionStrategy CurrentStrategy;
        /// <summary>Participant's demonstrated suspicion (challenges, verification, reports).</summary>
        public int Suspicion;
        /// <summary>Participant's demonstrated compliance.</summary>
        public int Cooperation;
        /// <summary>How hard the NPC has pushed (0 = calm request).</summary>
        public int Escalation;
        /// <summary>Risky dialogue choices (intentions and verbal disclosures).</summary>
        public int RiskyChoices;
        public bool Started;
        public bool Completed;
        public EncounterOutcome Outcome = EncounterOutcome.InProgress;
        public readonly HashSet<string> Flags = new HashSet<string>();
        public readonly List<PersuasionStrategy> StrategiesUsed = new List<PersuasionStrategy>();
        public readonly Dictionary<PersuasionStrategy, string> StrategyExamples = new Dictionary<PersuasionStrategy, string>();
        public readonly List<ChoiceRecord> Choices = new List<ChoiceRecord>();
        public readonly List<ScenarioSignal> Signals = new List<ScenarioSignal>();
        public readonly List<SecurityActionRecord> SecurityActions = new List<SecurityActionRecord>();
        internal int NextOrder;

        public bool HasFlag(string flag) => Flags.Contains(flag);

        /// <summary>Sets a flag; setting it again also sets "&lt;flag&gt;_x2" (used for repeated challenges/refusals).</summary>
        public void AddFlag(string flag)
        {
            if (!Flags.Add(flag))
            {
                Flags.Add(flag + "_x2");
            }
        }

        public IEnumerable<SecurityActionRecord> RiskyActions => SecurityActions.Where(a => a.Risky);
        public IEnumerable<SecurityActionRecord> UncorrectedRiskyActions =>
            RiskyActions.Where(r => !SecurityActions.Any(c => c.Covers(r)));
        public int RiskyActionCount => SecurityActions.Count(a => a.Risky);

        public string Summary =>
            $"node={CurrentNodeId} line={CurrentLineId} suspicion={Suspicion} cooperation={Cooperation} escalation={Escalation} " +
            $"strategy={CurrentStrategy} riskyChoices={RiskyChoices} riskyActions={RiskyActionCount} outcome={Outcome}";
    }

    /// <summary>Multi-page personalised debrief.</summary>
    public sealed class DebriefReport
    {
        public EncounterOutcome Outcome;
        public string Headline;
        public readonly List<string> PageTitles = new List<string>();
        public readonly List<string> Pages = new List<string>();
        public bool RecoveryOccurred;
        public readonly List<string> Recommendations = new List<string>();

        public string AllText => Headline + "\n" + string.Join("\n", Pages);
    }

    /// <summary>
    /// Deterministic, offline adaptive state machine for an NPC social-engineering encounter. Given the same graph,
    /// choices and scenario signals it always produces the same lines and outcome; it changes its next line (strategy,
    /// escalation, scenario requests) based on the participant's choices and on Phase 2.5 scenario decisions.
    /// Dialogue intentions and completed security actions are tracked separately; only actions decide compromise.
    /// It is scripted branching, not generative AI: model output can only replace line wording via the director.
    /// </summary>
    public sealed class AdaptiveDialogueEngine
    {
        private readonly DialogueGraph _graph;

        public AdaptiveDialogueEngine(DialogueGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public DialogueGraph Graph => _graph;
        public DialogueState State { get; private set; } = new DialogueState();
        public DialogueNode CurrentNode => _graph.GetNode(State.CurrentNodeId);
        public LineVariant CurrentLine { get; private set; }
        public bool IsActive => State.Started && !State.Completed;

        /// <summary>When false, nothing is written to <see cref="DecisionLog"/> (used by exhaustive branch tests).</summary>
        public bool RecordTelemetry { get; set; } = true;

        /// <summary>Session clock used for timestamps in the debrief.</summary>
        public Func<double> Clock { get; set; } = () => DecisionLog.SessionTime;

        public event Action<DialogueNode, LineVariant> NodeEntered;
        public event Action<EncounterOutcome> Completed;

        private readonly struct SignalInfo
        {
            public readonly int Suspicion, Cooperation;
            public readonly string[] Flags;
            public readonly string Channel, Description;
            public readonly bool Risky, Corrective, Verification, IsAction;

            public SignalInfo(int suspicion, int cooperation, string[] flags, string channel, string description, bool isAction,
                bool risky = false, bool corrective = false, bool verification = false)
            {
                Suspicion = suspicion;
                Cooperation = cooperation;
                Flags = flags;
                Channel = channel;
                Description = description;
                IsAction = isAction;
                Risky = risky;
                Corrective = corrective;
                Verification = verification;
            }
        }

        private static readonly Dictionary<ScenarioSignal, SignalInfo> Signals = new Dictionary<ScenarioSignal, SignalInfo>
        {
            { ScenarioSignal.PhishingLinkOpened, new SignalInfo(0, 1, new[] { "sig_phish_opened" }, "email", "Opened the link in the phishing email", true, risky: true) },
            { ScenarioSignal.PhishingCredentialsSubmitted, new SignalInfo(0, 3, new[] { "sig_phish_compromised", "any_compromised" }, "email", "Entered your password on the fake verification page", true, risky: true) },
            { ScenarioSignal.PhishingReported, new SignalInfo(3, 0, new[] { "sig_phish_reported", "any_reported" }, "email", "Reported the phishing email to IT Security", true, corrective: true) },
            { ScenarioSignal.KioskSignInSelected, new SignalInfo(0, 1, new[] { "sig_kiosk_signin" }, "kiosk", "Started a sign-in at the self-service kiosk", false) },
            { ScenarioSignal.KioskCredentialsEntered, new SignalInfo(0, 3, new[] { "sig_kiosk_compromised", "any_compromised" }, "kiosk", "Entered your password at the rogue password-reset kiosk", true, risky: true) },
            { ScenarioSignal.KioskCancelled, new SignalInfo(1, 0, new[] { "sig_kiosk_cancelled" }, "kiosk", "Cancelled at the kiosk without entering a password", true) },
            { ScenarioSignal.KioskReported, new SignalInfo(3, 0, new[] { "sig_kiosk_reported", "any_reported" }, "kiosk", "Reported the rogue kiosk to IT Security", true, corrective: true) },
            { ScenarioSignal.BadgeInspected, new SignalInfo(0, 0, new[] { "sig_badge_inspected" }, "badge", "Inspected the visitor badge", false) },
            { ScenarioSignal.BadgeVerificationFailed, new SignalInfo(3, 0, new[] { "sig_badge_failed" }, "badge", "Verified the visitor badge (verification failed)", true, verification: true) },
            { ScenarioSignal.UsbPickedUp, new SignalInfo(0, 0, new[] { "sig_usb_picked" }, "usb", "Picked up the found USB drive", false) },
            { ScenarioSignal.UsbConnected, new SignalInfo(0, 3, new[] { "sig_usb_connected", "any_compromised" }, "usb", "Connected the found USB drive to a workstation", true, risky: true) },
            { ScenarioSignal.UsbSubmitted, new SignalInfo(2, 0, new[] { "sig_usb_submitted", "any_reported" }, "usb", "Handed the found USB drive to IT Security", true, corrective: true) },
        };

        /// <summary>Starts the conversation at the graph's start node. Scenario signals received earlier still apply.</summary>
        public void Begin()
        {
            if (State.Started)
            {
                return;
            }
            State.Started = true;
            Lifecycle("alex_encounter.started", $"suspicion={State.Suspicion};flags={string.Join("|", State.Flags.OrderBy(f => f))}");
            Enter(_graph.StartNodeId);
        }

        /// <summary>Options visible in the current state (empty for terminal nodes).</summary>
        public IReadOnlyList<ResponseOption> GetAvailableOptions()
        {
            DialogueNode node = CurrentNode;
            if (!IsActive || node == null)
            {
                return Array.Empty<ResponseOption>();
            }
            return node.Options.Where(o => o.ShowCondition.Matches(State)).ToList();
        }

        /// <summary>Applies a participant choice. Records exactly one decision event.</summary>
        public DecisionEvent? Choose(ResponseOption option, double responseLatency, DialogueContentSource lineSource)
        {
            if (!IsActive || option == null || !GetAvailableOptions().Contains(option))
            {
                return null;
            }
            return Apply(option, responseLatency, lineSource);
        }

        /// <summary>Footer "Report" / "End conversation", available in every non-terminal state.</summary>
        public DecisionEvent? ChooseGlobal(ChoiceClass choiceClass, double responseLatency, DialogueContentSource lineSource)
        {
            if (!IsActive || (choiceClass != ChoiceClass.Report && choiceClass != ChoiceClass.Exit))
            {
                return null;
            }
            bool report = choiceClass == ChoiceClass.Report;
            var option = new ResponseOption
            {
                Id = report ? "footer_report" : "footer_end_conversation",
                Text = report ? "Reported Alex to IT Security" : "Ended the conversation",
                Class = choiceClass,
                Outcome = report ? DecisionOutcome.Safe : DecisionOutcome.Neutral,
                NextNodeId = report ? _graph.GlobalReportNodeId : _graph.GlobalExitNodeId,
                IsCorrective = report
            };
            return Apply(option, responseLatency, lineSource);
        }

        private DecisionEvent? Apply(ResponseOption option, double responseLatency, DialogueContentSource lineSource)
        {
            string nodeId = State.CurrentNodeId;
            PersuasionStrategy respondingTo = State.CurrentStrategy;
            double now = Clock();
            State.Suspicion += option.SuspicionDelta;
            State.Cooperation += option.CooperationDelta;
            foreach (string flag in option.AddFlags)
            {
                State.AddFlag(flag);
            }
            if (option.Outcome == DecisionOutcome.Unsafe)
            {
                State.RiskyChoices++;
            }
            State.Choices.Add(new ChoiceRecord(State.NextOrder++, now, nodeId, option.Id, option.Text, option.Class, option.Outcome, respondingTo));
            if (option.IsSecurityAction || option.IsCorrective)
            {
                bool risky = option.IsSecurityAction && option.Outcome == DecisionOutcome.Unsafe;
                string description = risky ? "Told Alex your password"
                    : option.Class == ChoiceClass.Report ? "Reported Alex to IT Security"
                    : "Insisted on verifying with IT in person";
                State.SecurityActions.Add(new SecurityActionRecord(State.NextOrder++, now, option.IsSecurityAction ? option.Channel : "dialogue",
                    description, risky, option.IsCorrective, false, "dialogue"));
            }

            DecisionEvent? recorded = null;
            if (RecordTelemetry)
            {
                var context = new DecisionContext(DecisionSource.Dialogue, nodeId, option.Id, respondingTo.ToString(), responseLatency,
                    option.Class.ToString(), lineSource == DialogueContentSource.Model ? "model" : "scripted",
                    option.IsSecurityAction ? DecisionCategory.SecurityAction : DecisionCategory.DialogueChoice, option.IsCorrective,
                    option.IsSecurityAction ? option.Channel : string.IsNullOrEmpty(option.PromisedChannel) ? "dialogue" : option.PromisedChannel);
                recorded = DecisionLog.Record($"{_graph.EncounterId}.{ToSnake(option.Class)}", _graph.ObjectId, option.Id, option.Outcome, context);
            }
            Enter(option.NextNodeId);
            return recorded;
        }

        /// <summary>
        /// Feeds an actual Phase 2.5 scenario decision into the encounter. Always records it as a security action / updates
        /// indicators (so later lines adapt, even before the conversation starts). Transitions only if the current node
        /// waits for this signal, or for completed compromises (graph-wide). Records no decision (the scenario already did).
        /// </summary>
        public bool HandleSignal(ScenarioSignal signal)
        {
            if (signal == ScenarioSignal.None || !Signals.TryGetValue(signal, out SignalInfo info))
            {
                return false;
            }
            State.Signals.Add(signal);
            State.Suspicion += info.Suspicion;
            State.Cooperation += info.Cooperation;
            foreach (string flag in info.Flags)
            {
                State.Flags.Add(flag);
            }
            if (info.IsAction)
            {
                State.SecurityActions.Add(new SecurityActionRecord(State.NextOrder++, Clock(), info.Channel, info.Description, info.Risky,
                    info.Corrective, info.Verification, "scenario"));
            }

            SignalTransition transition = null;
            if (IsActive)
            {
                transition = CurrentNode?.SignalTransitions.FirstOrDefault(t => t.Signal == signal)
                             ?? _graph.GlobalSignalTransitions.FirstOrDefault(t => t.Signal == signal);
            }
            Lifecycle("alex_encounter.scenario_signal", $"signal={signal};node={State.CurrentNodeId};transition={transition?.NextNodeId ?? "none"}");
            if (transition == null || transition.NextNodeId == State.CurrentNodeId)
            {
                return false;
            }
            Enter(transition.NextNodeId);
            return true;
        }

        /// <summary>Developer/demo branch selection: jumps to a node without recording a decision.</summary>
        public void JumpTo(string nodeId, int suspicion, int cooperation, params string[] flags)
        {
            if (_graph.GetNode(nodeId) == null)
            {
                throw new ArgumentException($"Unknown node '{nodeId}'");
            }
            State.Started = true;
            State.Completed = false;
            State.Outcome = EncounterOutcome.InProgress;
            State.Suspicion = suspicion;
            State.Cooperation = cooperation;
            foreach (string flag in flags)
            {
                State.Flags.Add(flag);
            }
            Lifecycle("demo.branch_jump", $"node={nodeId}");
            Enter(nodeId);
        }

        public void Reset()
        {
            State = new DialogueState();
            CurrentLine = null;
        }

        /// <summary>Line the NPC would use for a node in the current state.</summary>
        public LineVariant SelectLine(DialogueNode node) => node.Lines.FirstOrDefault(l => l.Condition.Matches(State)) ?? node.Lines.LastOrDefault();

        private void Enter(string nodeId)
        {
            DialogueNode node = _graph.GetNode(nodeId) ?? throw new InvalidOperationException($"Dialogue node '{nodeId}' not found.");
            LineVariant line = SelectLine(node);
            State.CurrentNodeId = node.Id;
            State.CurrentLineId = line?.Id;
            CurrentLine = line;
            if (line != null)
            {
                State.CurrentStrategy = line.Strategy;
                State.Escalation += line.EscalationDelta;
                if (line.Strategy != PersuasionStrategy.None && !State.StrategiesUsed.Contains(line.Strategy))
                {
                    State.StrategiesUsed.Add(line.Strategy);
                    State.StrategyExamples[line.Strategy] = line.Text;
                }
            }

            if (node.Kind == NodeKind.Terminal)
            {
                State.Outcome = ResolveOutcome(node.TerminalOutcome);
                State.Completed = true;
            }
            NodeEntered?.Invoke(node, line);
            if (State.Completed)
            {
                Lifecycle("alex_encounter.completed",
                    $"outcome={State.Outcome};strategies={string.Join("|", State.StrategiesUsed)};escalation={State.Escalation};" +
                    $"choices={State.Choices.Count};risky_choices={State.RiskyChoices};risky_actions={State.RiskyActionCount};" +
                    $"uncorrected={State.UncorrectedRiskyActions.Count()};final_node={node.Id}");
                Completed?.Invoke(State.Outcome);
            }
        }

        /// <summary>
        /// Evidence-based classification:
        ///  - any actual risky action: Recovered if every one is followed by a covering corrective action, else Compromised;
        ///  - otherwise: Resisted if the participant reported or verified; Declined if they refused until Alex gave up;
        ///    Incomplete if the encounter ended before any meaningful security outcome.
        /// Promises ("OK, I'll use the kiosk") never count as risky actions.
        /// </summary>
        public EncounterOutcome ResolveOutcome(EncounterOutcome terminalHint)
        {
            if (State.RiskyActionCount > 0)
            {
                return State.UncorrectedRiskyActions.Any() ? EncounterOutcome.Compromised : EncounterOutcome.Recovered;
            }
            if (State.SecurityActions.Any(a => a.Corrective || a.Verification))
            {
                return EncounterOutcome.Resisted;
            }
            return terminalHint == EncounterOutcome.Declined ? EncounterOutcome.Declined : EncounterOutcome.Incomplete;
        }

        /// <summary>Personalised multi-page debrief (the educational reveal), built only from recorded choices and actions.</summary>
        public DebriefReport BuildDebriefReport()
        {
            var report = new DebriefReport { Outcome = State.Outcome };
            List<SecurityActionRecord> risky = State.RiskyActions.ToList();
            List<SecurityActionRecord> uncorrected = State.UncorrectedRiskyActions.ToList();
            report.RecoveryOccurred = risky.Count > 0 && uncorrected.Count == 0;
            report.Headline = $"OUTCOME: {OutcomeLabel(State.Outcome)}";

            // Page 1: outcome + dialogue decisions.
            var p1 = new StringBuilder();
            p1.AppendLine($"<b>{OutcomeLabel(State.Outcome)}</b>: {DescribeOutcome(State.Outcome)}");
            p1.AppendLine($"\"{_graph.SpeakerName}\" was a simulated social engineer, not a real Aperture Systems employee.");
            p1.AppendLine();
            int safe = State.Choices.Count(c => c.Outcome == DecisionOutcome.Safe);
            p1.AppendLine($"<b>What you said to Alex</b> ({State.Choices.Count} choices: {safe} safe, {State.RiskyChoices} risky)");
            foreach (ChoiceRecord c in State.Choices.Take(8))
            {
                p1.AppendLine($"{Mark(c.Outcome)} \"{c.Text}\"");
            }
            if (State.Choices.Count == 0) p1.AppendLine("No replies were chosen.");
            report.PageTitles.Add("Outcome and your replies");
            report.Pages.Add(p1.ToString().TrimEnd());

            // Page 2: completed security actions + recovery.
            var p2 = new StringBuilder();
            p2.AppendLine("<b>Security actions you actually completed</b>");
            List<SecurityActionRecord> actions = State.SecurityActions.OrderBy(a => a.Order).ToList();
            foreach (SecurityActionRecord a in actions.Take(9))
            {
                string tag = a.Risky ? "<color=#FF6B5E>RISKY</color>" : a.Corrective ? "<color=#5EE08A>SAFE</color>" : a.Verification ? "<color=#5EE08A>CHECK</color>" : "<color=#C8CDD4>INFO</color>";
                p2.AppendLine($"{FormatTime(a.Time)}  {tag}  {a.Description}");
            }
            if (actions.Count == 0) p2.AppendLine("None. You did not act at the kiosk, the email, the badge or the USB.");
            List<string> promises = State.Choices.Where(c => c.Outcome == DecisionOutcome.Unsafe && !actions.Any(a => a.Risky && a.Source == "dialogue" && a.Time == c.Time))
                .Select(c => c.Text).ToList();
            if (promises.Count > 0)
            {
                p2.AppendLine($"<i>You agreed to {promises.Count} request(s) in conversation; agreeing alone is risky, but only completed actions count as a compromise.</i>");
            }
            p2.AppendLine();
            p2.AppendLine("<b>Did recovery happen?</b>");
            if (risky.Count == 0) p2.AppendLine("Not needed: you completed no risky security action.");
            else if (uncorrected.Count == 0) p2.AppendLine($"Yes. After \"{risky.Last().Description}\" you took a recorded corrective step: \"{actions.First(a => a.Corrective && a.Order > risky.First().Order).Description}\".");
            else p2.AppendLine($"No. Nothing reported or corrected after: {string.Join("; ", uncorrected.Select(u => $"\"{u.Description}\""))}.");
            report.PageTitles.Add("Security actions and recovery");
            report.Pages.Add(p2.ToString().TrimEnd());

            // Page 3: persuasion techniques + recommendations.
            var p3 = new StringBuilder();
            p3.AppendLine("<b>Persuasion techniques Alex used on you</b>");
            foreach (PersuasionStrategy s in State.StrategiesUsed)
            {
                string example = State.StrategyExamples.TryGetValue(s, out string text) ? Excerpt(text) : string.Empty;
                p3.AppendLine($"- {Capitalise(Describe(s))}: \"{example}\"");
            }
            if (State.StrategiesUsed.Count == 0) p3.AppendLine("- None (the conversation ended immediately).");
            p3.AppendLine();
            p3.AppendLine("<b>What to do next time</b>");
            report.Recommendations.AddRange(Recommend(uncorrected, risky));
            foreach (string r in report.Recommendations) p3.AppendLine($"- {r}");
            report.PageTitles.Add("Techniques and recommendations");
            report.Pages.Add(p3.ToString().TrimEnd());

            // Page 4: independent verification and workplace practice (Phase 5), personalised from recorded behaviour only.
            int verifyReplies = State.Choices.Count(c => c.Class == ChoiceClass.AskIdentification || c.Class == ChoiceClass.RequestVerification);
            int verifyActions = State.SecurityActions.Count(a => a.Verification);
            int reports = State.SecurityActions.Count(a => a.Corrective);
            var p4 = new StringBuilder();
            p4.AppendLine("<b>Why independent verification matters</b>");
            p4.AppendLine("Everything Alex offered as proof (name, job title, urgency, \"IT already approved it\") came from Alex. " +
                          "Checking through a channel <i>you</i> choose, such as the staff directory or the Service Desk's known number, is the one step an attacker cannot fake.");
            if (verifyReplies + verifyActions > 0)
                p4.AppendLine($"You checked {verifyReplies + verifyActions} time(s) in this run ({verifyReplies} in conversation, {verifyActions} at the badge).");
            else
                p4.AppendLine("In this run you did not ask for ID or verify independently. That is the habit to build first.");
            if (reports > 0) p4.AppendLine($"You made {reports} report(s) or corrective step(s). Reports let the security team warn others.");
            p4.AppendLine();
            p4.AppendLine("<b>At work, when someone asks for access, a password or a quick favour</b>");
            p4.AppendLine("1. Pause. Urgency and authority are pressure, not proof.");
            p4.AppendLine("2. Verify through a known channel before you help.");
            p4.AppendLine("3. Never share passwords or sign-in codes, and never use a \"Not secure\" page.");
            p4.AppendLine("4. Hand found USB devices to IT unopened.");
            p4.AppendLine("5. Report it, even if you refused, and at once if you slipped: recovering early limits the damage.");
            report.PageTitles.Add("At work: verify and report");
            report.Pages.Add(p4.ToString().TrimEnd());
            return report;
        }

        /// <summary>Single-string debrief (all pages).</summary>
        public string BuildDebrief() => BuildDebriefReport().AllText;

        private IEnumerable<string> Recommend(List<SecurityActionRecord> uncorrected, List<SecurityActionRecord> risky)
        {
            var list = new List<string>();
            bool Had(string channel) => risky.Any(r => r.Channel == channel);
            if (Had("kiosk")) list.Add("Never sign in on a page marked \"Not secure\". Change your password now from a trusted device and tell IT which kiosk you used.");
            if (risky.Any(r => r.Description.Contains("fake verification"))) list.Add("Change your password and report the email. Check the sender's domain (aperture-systerns.com is a look-alike) before clicking.");
            else if (Had("email")) list.Add("Check where a link really goes before clicking, and report the email even if you entered nothing.");
            if (Had("usb")) list.Add("Unplug the workstation from the network and call IT Security. Never connect found USB devices.");
            if (Had("verbal")) list.Add("IT will never ask for your password. Change it immediately and report who asked.");
            if (uncorrected.Count > 0) list.Add("Report straight away (IT Security, ext. 4400): early reports limit the damage.");
            if (risky.Count == 0 && State.RiskyChoices > 0) list.Add("You agreed to unverified requests before backing out. Verify first, then help.");
            if (!State.SecurityActions.Any(a => a.Corrective)) list.Add("Report suspicious approaches to IT Security, even when you didn't comply.");
            if (!State.Choices.Any(c => c.Class == ChoiceClass.AskIdentification || c.Class == ChoiceClass.RequestVerification))
                list.Add("Ask for staff ID and confirm through an official channel (call the Service Desk on a known number).");
            if (list.Count == 0) list.Add("Good work: keep verifying identities through official channels and reporting approaches like this.");
            return list.Take(4);
        }

        public static string OutcomeLabel(EncounterOutcome outcome) => outcome.ToString().ToUpperInvariant();

        public static string Describe(PersuasionStrategy strategy)
        {
            switch (strategy)
            {
                case PersuasionStrategy.Authority: return "authority & impersonation";
                case PersuasionStrategy.Urgency: return "urgency & time pressure";
                case PersuasionStrategy.TrustBuilding: return "trust-building & helpfulness";
                default: return "none";
            }
        }

        public static string DescribeOutcome(EncounterOutcome outcome)
        {
            switch (outcome)
            {
                case EncounterOutcome.Compromised: return "you completed a risky security action and did not correct it.";
                case EncounterOutcome.Recovered: return "you completed a risky action, then took a recorded corrective step.";
                case EncounterOutcome.Resisted: return "you avoided every risky action and verified or reported.";
                case EncounterOutcome.Declined: return "you refused until Alex gave up, but did not report or verify.";
                case EncounterOutcome.Incomplete: return "the conversation ended before any security outcome.";
                default: return "in progress";
            }
        }

        private static string Mark(DecisionOutcome outcome) =>
            outcome == DecisionOutcome.Unsafe ? "<color=#FF6B5E>RISKY</color>" : outcome == DecisionOutcome.Safe ? "<color=#5EE08A>SAFE</color>" : "<color=#C8CDD4>NEUTRAL</color>";

        private static string FormatTime(double seconds) => seconds < 0 ? "--:--" : $"{(int)(seconds / 60):00}:{(int)(seconds % 60):00}";

        private static string Excerpt(string text) => text.Length <= 70 ? text : text.Substring(0, 67).TrimEnd() + "...";

        private static string Capitalise(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private void Lifecycle(string name, string detail)
        {
            if (RecordTelemetry)
            {
                DecisionLog.RecordLifecycle(name, _graph.ObjectId, detail);
            }
        }

        private static string ToSnake(ChoiceClass choiceClass)
        {
            string s = choiceClass.ToString();
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(s[i]));
            }
            return sb.ToString();
        }
    }
}
