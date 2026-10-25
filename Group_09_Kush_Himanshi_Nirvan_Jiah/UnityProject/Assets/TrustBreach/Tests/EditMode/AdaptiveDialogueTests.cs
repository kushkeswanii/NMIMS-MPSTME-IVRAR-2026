using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TrustBreach.AI;
using TrustBreach.Core;
using TrustBreach.Dialogue;
using TrustBreach.Dialogue.Content;
using UnityEngine;
using E = TrustBreach.Dialogue.Content.AlexMorganEncounter;

namespace TrustBreach.Tests
{
    public class AdaptiveDialogueTests
    {
        private DialogueGraph _graph;

        [SetUp]
        public void SetUp()
        {
            _graph = ScriptableObject.CreateInstance<DialogueGraph>();
            AlexMorganEncounter.Populate(_graph);
            DecisionLog.StartSession();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_graph);
            LLMDialogueProviderRegistry.ResetToOffline();
        }

        private AdaptiveDialogueEngine NewEngine(bool telemetry = false)
        {
            var engine = new AdaptiveDialogueEngine(_graph) { RecordTelemetry = telemetry };
            return engine;
        }

        private static void Pick(AdaptiveDialogueEngine engine, string optionId)
        {
            ResponseOption option = engine.GetAvailableOptions().FirstOrDefault(o => o.Id == optionId);
            Assert.IsNotNull(option, $"Option '{optionId}' not available at '{engine.State.CurrentNodeId}' ({engine.State.Summary})");
            engine.Choose(option, 1.0, DialogueContentSource.Scripted);
        }

        [Test]
        public void OpeningLineMatchesSpecification()
        {
            AdaptiveDialogueEngine engine = NewEngine();
            engine.Begin();
            Assert.AreEqual(E.Opening, engine.State.CurrentNodeId);
            Assert.AreEqual("Hi, I'm Alex from IT Security. We've flagged an urgent authentication issue. Could you help me verify your account?",
                engine.CurrentLine.Text);
            CollectionAssert.AreEquivalent(
                new[] { ChoiceClass.Cooperate, ChoiceClass.AskIdentification, ChoiceClass.Challenge, ChoiceClass.RequestVerification, ChoiceClass.Refuse, ChoiceClass.Report },
                engine.GetAvailableOptions().Select(o => o.Class).ToArray(), "Opening offers the six required response types");
        }

        /// <summary>
        /// Exhaustive exploration of every reachable (node, state) pair: every option, footer report/exit, and every
        /// scenario signal accepted at waiting nodes. Asserts no dead ends, bounded paths, option counts, and coverage.
        /// </summary>
        [Test, Timeout(600000)]
        public void BranchCoverage_AllPathsTerminateWithoutDeadEnds()
        {
            var visitedNodes = new HashSet<string>();
            var outcomes = new HashSet<EncounterOutcome>();
            var strategies = new HashSet<PersuasionStrategy>();
            var actions = new HashSet<ScenarioAction>();
            var seen = new HashSet<string>();
            var stack = new Stack<List<string>>();
            stack.Push(new List<string>());
            int paths = 0, maxDepth = 0;
            var problems = new List<string>();

            while (stack.Count > 0)
            {
                List<string> steps = stack.Pop();
                AdaptiveDialogueEngine engine = Replay(steps);
                DialogueState s = engine.State;
                visitedNodes.Add(s.CurrentNodeId);
                if (engine.CurrentLine != null)
                {
                    strategies.Add(engine.CurrentLine.Strategy);
                    actions.Add(engine.CurrentLine.Action);
                }
                maxDepth = Math.Max(maxDepth, steps.Count);
                if (steps.Count > 30)
                {
                    problems.Add($"path too long: {string.Join(" > ", steps)}");
                    continue;
                }
                if (s.Completed)
                {
                    paths++;
                    outcomes.Add(s.Outcome);
                    if (s.Outcome == EncounterOutcome.InProgress) problems.Add($"terminal without outcome at {s.CurrentNodeId}");
                    continue;
                }
                // Behaviour-relevant state only: suspicion never decreases and every condition treats >= 6 alike;
                // cooperation is not read by any condition. Outcome inputs (risky actions, correctives) are kept.
                string key = $"{s.CurrentNodeId}|{Math.Min(s.Suspicion, 6)}|{string.Join(",", s.Flags.OrderBy(f => f))}|{s.RiskyChoices > 0}|" +
                             $"{string.Join(",", s.SecurityActions.Select(x => (x.Risky ? "R" : x.Corrective ? "C" : "V") + x.Channel))}";
                if (!seen.Add(key)) continue;

                IReadOnlyList<ResponseOption> options = engine.GetAvailableOptions();
                DialogueNode node = engine.CurrentNode;
                if (options.Count < 2 || options.Count > 6)
                {
                    problems.Add($"{s.CurrentNodeId} offers {options.Count} options ({s.Summary})");
                }
                foreach (ResponseOption option in options) stack.Push(new List<string>(steps) { "o:" + option.Id });
                stack.Push(new List<string>(steps) { "footer:Report" });
                stack.Push(new List<string>(steps) { "footer:Exit" });
                // Each office scenario event can happen at most once (the scenarios lock after a decision).
                foreach (ScenarioSignal signal in node.SignalTransitions.Concat(_graph.GlobalSignalTransitions).Select(t => t.Signal).Distinct()
                             .Where(sig => !s.Signals.Contains(sig)))
                {
                    stack.Push(new List<string>(steps) { "s:" + signal });
                }
            }

            Assert.IsEmpty(problems, string.Join("\n", problems.Take(20)));
            CollectionAssert.IsSubsetOf(_graph.Nodes.Select(n => n.Id).ToList(), visitedNodes.ToList(), "Every node is reachable through real choices");
            CollectionAssert.IsSubsetOf(new[] { PersuasionStrategy.Authority, PersuasionStrategy.Urgency, PersuasionStrategy.TrustBuilding }, strategies.ToList());
            CollectionAssert.IsSubsetOf(new[] { ScenarioAction.DirectToPhishingEmail, ScenarioAction.DirectToPasswordKiosk, ScenarioAction.PresentVisitorBadge,
                ScenarioAction.EncourageUsbConnection }, actions.ToList());
            CollectionAssert.IsSubsetOf(new[] { EncounterOutcome.Compromised, EncounterOutcome.Recovered, EncounterOutcome.Resisted,
                EncounterOutcome.Declined, EncounterOutcome.Incomplete }, outcomes.ToList());
            TestContext.WriteLine($"explored {seen.Count} states, {paths} complete paths, max depth {maxDepth}");
        }

        private AdaptiveDialogueEngine Replay(List<string> steps)
        {
            AdaptiveDialogueEngine engine = NewEngine();
            engine.Begin();
            foreach (string step in steps)
            {
                string[] parts = step.Split(new[] { ':' }, 2);
                switch (parts[0])
                {
                    case "o":
                        engine.Choose(engine.GetAvailableOptions().First(o => o.Id == parts[1]), 1.0, DialogueContentSource.Scripted);
                        break;
                    case "footer":
                        engine.ChooseGlobal((ChoiceClass)Enum.Parse(typeof(ChoiceClass), parts[1]), 1.0, DialogueContentSource.Scripted);
                        break;
                    case "s":
                        engine.HandleSignal((ScenarioSignal)Enum.Parse(typeof(ScenarioSignal), parts[1]));
                        break;
                }
            }
            return engine;
        }

        [Test]
        public void Adaptive_ResponseChangesWithEarlierBehaviour()
        {
            // Asking for ID with low suspicion -> trust-building + presents the visitor badge.
            AdaptiveDialogueEngine calm = NewEngine();
            calm.Begin();
            Pick(calm, "o_open_ask_id");
            Assert.AreEqual("sb_trust", calm.CurrentLine.Id);
            Assert.AreEqual(ScenarioAction.PresentVisitorBadge, calm.CurrentLine.Action);

            // Same request after a verification demand -> authority push.
            AdaptiveDialogueEngine pushed = NewEngine();
            pushed.Begin();
            Pick(pushed, "o_open_challenge");          // urgency
            Assert.AreEqual("pr_urgency", pushed.CurrentLine.Id);
            Pick(pushed, "o_pr_verify");               // verification pushback
            Pick(pushed, "o_vp_cooperate");            // request credentials
            Pick(pushed, "o_rc_why");                  // second challenge -> switches strategy to trust/social proof
            Assert.AreEqual("pr_social_proof", pushed.CurrentLine.Id);
            Assert.AreEqual(PersuasionStrategy.TrustBuilding, pushed.CurrentLine.Strategy);

            // A phishing report made in the office before meeting Alex changes his opening.
            AdaptiveDialogueEngine afterReport = NewEngine();
            afterReport.HandleSignal(ScenarioSignal.PhishingReported);
            afterReport.Begin();
            Assert.AreEqual("open_after_report", afterReport.CurrentLine.Id);

            // A failed badge verification changes his reply to a later challenge.
            AdaptiveDialogueEngine badge = NewEngine();
            badge.Begin();
            Pick(badge, "o_open_ask_id");
            badge.HandleSignal(ScenarioSignal.BadgeVerificationFailed);
            Pick(badge, "o_sb_badge");
            Assert.AreEqual("pr_withdraw", badge.CurrentLine.Id, "High suspicion makes Alex disengage");
        }

        [Test]
        public void Escalation_RepeatedRefusalsLeadToWithdrawal()
        {
            AdaptiveDialogueEngine engine = NewEngine();
            engine.Begin();
            Pick(engine, "o_open_refuse");
            Assert.AreEqual("rp_default", engine.CurrentLine.Id);
            int escalation = engine.State.Escalation;
            Pick(engine, "o_rp_refuse");
            Assert.AreEqual("rp_withdraw", engine.CurrentLine.Id);
            Assert.Greater(engine.State.Escalation, escalation);
            Pick(engine, "o_rp_let_go");
            Assert.AreEqual(EncounterOutcome.Declined, engine.State.Outcome, "Refused until Alex gave up, but never reported or verified");
        }

        [Test]
        public void Recovery_CooperateThenReport_IsRecovered()
        {
            AdaptiveDialogueEngine engine = NewEngine(telemetry: true);
            engine.Begin();
            Pick(engine, "o_open_cooperate");
            Pick(engine, "o_rc_tell");
            Pick(engine, "o_pd_recover");
            Assert.IsTrue(engine.State.Completed);
            Assert.AreEqual(EncounterOutcome.Recovered, engine.State.Outcome);
            DecisionEvent last = DecisionLog.History.Last();
            Assert.AreEqual(DecisionOutcome.Safe, last.Outcome);
            Assert.IsTrue(last.RecoveryAfterRisk, "Report after a risky choice is flagged as recovery");
            StringAssert.Contains("RECOVERED", engine.BuildDebrief());
        }

        [Test]
        public void ScenarioIntegration_KioskAndEmailOutcomesDriveAlex()
        {
            AdaptiveDialogueEngine kiosk = NewEngine();
            kiosk.Begin();
            Pick(kiosk, "o_open_cooperate");
            Pick(kiosk, "o_rc_kiosk");
            Assert.AreEqual(E.AwaitKiosk, kiosk.State.CurrentNodeId);
            Assert.IsFalse(kiosk.HandleSignal(ScenarioSignal.UsbPickedUp), "Unrelated signals do not move the conversation");
            Assert.IsTrue(kiosk.HandleSignal(ScenarioSignal.KioskCredentialsEntered));
            Assert.AreEqual(E.ScenarioCompromised, kiosk.State.CurrentNodeId);
            Assert.AreEqual("sc_kiosk", kiosk.CurrentLine.Id, "Alex recognises the specific completed action");
            Pick(kiosk, "o_sc_fine");
            Assert.AreEqual(EncounterOutcome.Compromised, kiosk.State.Outcome);

            AdaptiveDialogueEngine email = NewEngine();
            email.Begin();
            Pick(email, "o_open_challenge");
            Pick(email, "o_pr_email");
            Assert.AreEqual(ScenarioAction.DirectToPhishingEmail, email.CurrentLine.Action);
            Pick(email, "o_er_ok");
            Assert.IsTrue(email.HandleSignal(ScenarioSignal.PhishingReported));
            Assert.AreEqual(EncounterOutcome.Resisted, email.State.Outcome, "Only PROMISED to open the email, then reported it: no risky action happened");

            AdaptiveDialogueEngine usb = NewEngine();
            usb.Begin();
            Pick(usb, "o_open_cooperate");
            Pick(usb, "o_rc_tell");
            Assert.AreEqual(ScenarioAction.EncourageUsbConnection, usb.CurrentLine.Action);
            Pick(usb, "o_pd_ok");
            Assert.IsTrue(usb.HandleSignal(ScenarioSignal.UsbSubmitted));
            Assert.AreEqual(EncounterOutcome.Compromised, usb.State.Outcome,
                "The verbally disclosed password is never corrected by handing in a USB drive (different channel)");

            AdaptiveDialogueEngine handled = NewEngine();
            handled.HandleSignal(ScenarioSignal.KioskReported);
            handled.Begin();
            Pick(handled, "o_open_cooperate");
            Assert.AreEqual("rc_kiosk_unavailable", handled.CurrentLine.Id, "Alex adapts once the kiosk was already reported");
            Assert.IsFalse(handled.GetAvailableOptions().Any(o => o.Id == "o_rc_kiosk"));
        }

        [Test]
        public void Telemetry_ExactlyOneDecisionPerChoice_SignalsAddNone()
        {
            AdaptiveDialogueEngine engine = NewEngine(telemetry: true);
            int start = DecisionLog.History.Count;
            engine.HandleSignal(ScenarioSignal.BadgeInspected);      // scenario signals are not dialogue decisions
            engine.Begin();
            Pick(engine, "o_open_ask_id");
            Pick(engine, "o_sb_reception");
            engine.HandleSignal(ScenarioSignal.UsbPickedUp);
            Pick(engine, "o_vp_walk");
            Assert.AreEqual(3, DecisionLog.History.Count - start);
            DecisionEvent[] events = DecisionLog.History.Skip(start).ToArray();
            Assert.IsTrue(events.All(e => e.Context.Source == DecisionSource.Dialogue && e.ObjectId == E.ObjectId && e.SessionId == DecisionLog.SessionId));
            Assert.AreEqual("alex_encounter.ask_identification", events[0].EventName);
            Assert.AreEqual(E.Opening, events[0].Context.DialogueNodeId);
            Assert.AreEqual("Authority", events[0].Context.PersuasionStrategy);
            Assert.AreEqual("scripted", events[0].Context.ContentSource);
            Assert.AreEqual(1.0, events[0].Context.ResponseLatency, 1e-6);
            Assert.AreEqual(EncounterOutcome.Resisted, engine.State.Outcome);
            StringAssert.StartsWith(DecisionLog.SessionId, events[0].ToCsvRow());
            Assert.IsTrue(DecisionLog.LifecycleHistory.Any(l => l.EventName == "alex_encounter.completed"));
        }

        [Test]
        public void Telemetry_ScenarioDecisionsKeepPhase25Format()
        {
            int start = DecisionLog.History.Count;
            DecisionLog.Record("phishing_email.reported", "PhishingEmailWorkstation", "report_phishing_from_inbox", DecisionOutcome.Safe);
            DecisionEvent e = DecisionLog.History[start];
            Assert.AreEqual(DecisionSource.Scenario, e.Context.Source);
            Assert.AreEqual(start + 1, e.Sequence);
            Assert.AreEqual(20, e.ToCsvRow().Split(',').Length);
            Assert.AreEqual(20, DecisionEvent.CsvHeader.Split(',').Length);
            Assert.AreEqual(DecisionCategory.SecurityAction, e.Context.Category);
            Assert.IsTrue(e.Context.IsCorrective);
            Assert.AreEqual("email", e.Context.Channel);
        }

        // ------------------------------------------------------------------ Phase 3.1 outcome accuracy

        private AdaptiveDialogueEngine KioskPromise(bool telemetry = false)
        {
            AdaptiveDialogueEngine engine = NewEngine(telemetry);
            engine.Begin();
            Pick(engine, "o_open_cooperate");
            Pick(engine, "o_rc_kiosk");
            Assert.AreEqual(E.AwaitKiosk, engine.State.CurrentNodeId);
            return engine;
        }

        [Test]
        public void Outcome_KioskCompromiseWithoutRecovery_IsCompromised_NotRecovered()
        {
            AdaptiveDialogueEngine engine = KioskPromise();
            engine.HandleSignal(ScenarioSignal.KioskSignInSelected);
            engine.HandleSignal(ScenarioSignal.KioskCredentialsEntered);
            Pick(engine, "o_sc_fine");
            Assert.AreEqual(EncounterOutcome.Compromised, engine.State.Outcome);
            DebriefReport debrief = engine.BuildDebriefReport();
            Assert.IsFalse(debrief.RecoveryOccurred);
            StringAssert.Contains("Entered your password at the rogue password-reset kiosk", debrief.AllText, "Debrief lists the completed kiosk action");
            StringAssert.Contains("No. Nothing reported or corrected after", debrief.AllText);
            StringAssert.DoesNotContain("RECOVERED", debrief.AllText);
            StringAssert.Contains("Change your password now", debrief.AllText);
        }

        [Test]
        public void Outcome_KioskCompromiseThenReport_IsRecovered()
        {
            AdaptiveDialogueEngine engine = KioskPromise(telemetry: true);
            DecisionLog.Record("password_kiosk.credentials_entered", "FakeSSLTerminal", "continue", DecisionOutcome.Unsafe);
            engine.HandleSignal(ScenarioSignal.KioskCredentialsEntered);
            Pick(engine, "o_sc_report");
            Assert.AreEqual(EncounterOutcome.Recovered, engine.State.Outcome);
            Assert.IsTrue(DecisionLog.History.Last().RecoveryAfterRisk, "Report after the ACTUAL kiosk compromise is a recovery");
            DebriefReport debrief = engine.BuildDebriefReport();
            Assert.IsTrue(debrief.RecoveryOccurred);
            StringAssert.Contains("Yes. After", debrief.AllText);
        }

        [Test]
        public void Outcome_PromiseThenKioskReport_IsResisted_NotRecovered()
        {
            // Exact event sequence of the real Simulator session trial_d0fa31651122.
            AdaptiveDialogueEngine engine = KioskPromise(telemetry: true);
            DecisionLog.Record("password_kiosk.sign_in_selected", "FakeSSLTerminal", "sign_in_and_reset", DecisionOutcome.Neutral);
            engine.HandleSignal(ScenarioSignal.KioskSignInSelected);
            DecisionEvent report = DecisionLog.Record("password_kiosk.reported", "FakeSSLTerminal", "report_kiosk_to_it", DecisionOutcome.Safe);
            engine.HandleSignal(ScenarioSignal.KioskReported);
            Assert.AreEqual(E.ScenarioReported, engine.State.CurrentNodeId);
            Assert.AreEqual("sr_kiosk", engine.CurrentLine.Id);
            Assert.AreEqual(EncounterOutcome.Resisted, engine.State.Outcome, "Agreeing to use the kiosk is not a risky action");
            Assert.IsFalse(report.RecoveryAfterRisk, "No actual risky action preceded the report");
            StringAssert.Contains("Reported the rogue kiosk to IT Security", engine.BuildDebriefReport().AllText);
        }

        [Test]
        public void Outcome_SafeIndependentReporting_IsResisted()
        {
            AdaptiveDialogueEngine engine = NewEngine();
            engine.Begin();
            Pick(engine, "o_open_ask_id");
            Pick(engine, "o_sb_report");
            Assert.AreEqual(EncounterOutcome.Resisted, engine.State.Outcome);
            Assert.AreEqual(0, engine.State.RiskyActionCount);
        }

        [Test]
        public void Outcome_EndWithoutCompletingRequest_IsIncomplete()
        {
            AdaptiveDialogueEngine engine = KioskPromise();
            engine.ChooseGlobal(ChoiceClass.Exit, 1.0, DialogueContentSource.Scripted);
            Assert.AreEqual(EncounterOutcome.Incomplete, engine.State.Outcome);
            StringAssert.Contains("INCOMPLETE", engine.BuildDebriefReport().Headline);

            AdaptiveDialogueEngine immediate = NewEngine();
            immediate.Begin();
            immediate.ChooseGlobal(ChoiceClass.Exit, 1.0, DialogueContentSource.Scripted);
            Assert.AreEqual(EncounterOutcome.Incomplete, immediate.State.Outcome);
        }

        [Test]
        public void Outcome_PhishingLinkOpenedThenReported_IsRecovered()
        {
            AdaptiveDialogueEngine engine = NewEngine();
            engine.Begin();
            Pick(engine, "o_open_challenge");
            Pick(engine, "o_pr_email");
            Pick(engine, "o_er_ok");
            Assert.IsFalse(engine.HandleSignal(ScenarioSignal.PhishingLinkOpened), "Opening the link does not complete Alex's request");
            Assert.IsTrue(engine.HandleSignal(ScenarioSignal.PhishingReported));
            Assert.AreEqual(EncounterOutcome.Recovered, engine.State.Outcome, "Actual risky action (link opened) then a same-channel report");
        }

        [Test]
        public void Promises_DoNotAdvanceDependentBranches_ButCompletedActionsDo()
        {
            AdaptiveDialogueEngine engine = KioskPromise();
            Pick(engine, "o_ak_where");
            Assert.AreEqual(E.AwaitKiosk, engine.State.CurrentNodeId);
            Assert.IsFalse(engine.HandleSignal(ScenarioSignal.KioskSignInSelected), "Starting a sign-in is not the completed action");
            Assert.AreEqual(E.AwaitKiosk, engine.State.CurrentNodeId);

            AdaptiveDialogueEngine unprompted = NewEngine();
            unprompted.Begin();
            Pick(unprompted, "o_open_cooperate");
            Assert.AreEqual(E.RequestCredentials, unprompted.State.CurrentNodeId);
            Assert.IsTrue(unprompted.HandleSignal(ScenarioSignal.KioskCredentialsEntered), "A completed compromise moves Alex on even without a promise");
            Assert.AreEqual(E.ScenarioCompromised, unprompted.State.CurrentNodeId);
        }

        [Test]
        public void Debrief_ListsDialogueDecisionsActionsTechniquesAndRecommendations()
        {
            AdaptiveDialogueEngine engine = NewEngine();
            engine.HandleSignal(ScenarioSignal.BadgeVerificationFailed); // done before meeting Alex
            engine.Begin();
            Pick(engine, "o_open_ask_id");
            Pick(engine, "o_sb_reception");
            Pick(engine, "o_vp_walk");
            DebriefReport debrief = engine.BuildDebriefReport();
            Assert.AreEqual(4, debrief.Pages.Count);
            StringAssert.Contains("Can I see your staff ID first?", debrief.Pages[0]);
            StringAssert.Contains("Verified the visitor badge", debrief.Pages[1]);
            StringAssert.Contains("Not needed", debrief.Pages[1]);
            StringAssert.Contains("Persuasion techniques", debrief.Pages[2]);
            StringAssert.Contains("What to do next time", debrief.Pages[2]);
            Assert.IsNotEmpty(debrief.Recommendations);
            StringAssert.Contains("Why independent verification matters", debrief.Pages[3]);
            StringAssert.Contains("(3 in conversation, 1 at the badge)", debrief.Pages[3]); // ID request + 2 verification replies + the badge check
        }

        [Test]
        public void Debrief_WorkplacePageNeverClaimsVerificationThatDidNotHappen()
        {
            AdaptiveDialogueEngine engine = NewEngine();
            engine.Begin();
            Pick(engine, "o_open_cooperate");
            DebriefReport debrief = engine.BuildDebriefReport();
            StringAssert.Contains("you did not ask for ID or verify independently", debrief.Pages[3]);
            StringAssert.DoesNotContain("You checked", debrief.Pages[3]);
            StringAssert.DoesNotContain("report(s)", debrief.Pages[3]);
        }

        [Test]
        public void LLMValidator_EnforcesScenarioBoundaries()
        {
            var request = new LLMDialogueRequest
            {
                NodeId = E.RequestCredentials,
                ScriptedLine = "x",
                PermittedActions = new[] { "None", "DirectToPasswordKiosk" },
                MaxCharacters = 200
            };
            LLMDialogueResult R(string text, string action = "None") => new LLMDialogueResult { Success = true, Text = text, Action = action };

            Assert.IsTrue(DialogueResponseValidator.Validate(R("Quick favour: pop over to the kiosk by the IT desk and sign in.", "DirectToPasswordKiosk"), request).Valid);
            Assert.IsFalse(DialogueResponseValidator.Validate(R("Plug in the USB drive.", "EncourageUsbConnection"), request).Valid, "unpermitted action");
            Assert.IsFalse(DialogueResponseValidator.Validate(R("Honestly this is just a phishing test."), request).Valid, "reveals exercise");
            Assert.IsFalse(DialogueResponseValidator.Validate(R("As an AI I can't do that."), request).Valid, "breaks persona");
            Assert.IsFalse(DialogueResponseValidator.Validate(R("Go to http://reset-now.com to fix it."), request).Valid, "link");
            Assert.IsFalse(DialogueResponseValidator.Validate(R("I also need your bank card number."), request).Valid, "out-of-scope data");
            Assert.IsFalse(DialogueResponseValidator.Validate(R(new string('a', 201)), request).Valid, "too long");
            Assert.IsFalse(DialogueResponseValidator.Validate(R(""), request).Valid, "empty");
            Assert.IsFalse(DialogueResponseValidator.Validate(LLMDialogueResult.Failed("timeout"), request).Valid, "provider failure");
        }

        [Test]
        public void LLMProvider_DefaultsToOffline()
        {
            Assert.IsFalse(LLMDialogueProviderRegistry.Current.IsAvailable);
            Assert.AreEqual("offline", LLMDialogueProviderRegistry.Current.ProviderName);
            LLMDialogueResult result = LLMDialogueProviderRegistry.Current.GenerateLineAsync(new LLMDialogueRequest(), CancellationToken.None).Result;
            Assert.IsFalse(result.Success);
            var settings = ScriptableObject.CreateInstance<LLMProviderSettings>();
            Assert.IsFalse(settings.Enabled, "Model generation is opt-in");
            UnityEngine.Object.DestroyImmediate(settings);
        }
    }
}
