using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using TrustBreach.AI;
using TrustBreach.Core;
using TrustBreach.Dialogue;
using TrustBreach.Dialogue.Content;
using UnityEngine;

namespace TrustBreach.Analytics.Tests
{
    public class ResearchAnalyticsTests
    {
        private string _root;
        private ResearchStore _store;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "tb_research_tests_" + Guid.NewGuid().ToString("N"));
            _store = new ResearchStore(_root);
            ResearchConsent.SetStatusForTests(ConsentStatus.Undecided);
            DecisionLog.StartSession();
        }

        [TearDown]
        public void TearDown()
        {
            ResearchConsent.SetStatusForTests(ConsentStatus.Undecided);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Builders
        // ---------------------------------------------------------------------------------------------------------
        private static SessionRecord Session(string id, params ResearchEvent[] events)
        {
            var s = new SessionRecord { sessionId = id, dataset = ResearchSchema.Test, startedUtc = "2026-10-01T10:00Z" };
            for (int i = 0; i < events.Length; i++)
            {
                events[i].seq = i + 1;
                events[i].t = (i + 1) * 5;
                s.events.Add(events[i]);
            }
            return s;
        }

        private static ResearchEvent Act(string name, string classification, bool corrective = false) =>
            new ResearchEvent
            {
                kind = "security_action", name = name, channel = DecisionContext.ChannelFor(name), classification = classification,
                riskyAction = classification == "Unsafe", corrective = corrective, completion = "completed", latency = -1
            };

        private static ResearchEvent Reply(string cls, string classification, string technique = "Authority", double latency = 2, string channel = "dialogue",
            bool corrective = false) =>
            new ResearchEvent
            {
                kind = "dialogue_choice", name = "alex_encounter." + cls.ToLowerInvariant(), channel = channel, classification = classification,
                choiceClass = cls, persuasion = technique, latency = latency, corrective = corrective,
                completion = cls == "Cooperate" && classification == "Unsafe" ? "promised" : "stance"
            };

        private static ResearchEvent Encounter(string outcome) =>
            new ResearchEvent { kind = "lifecycle", name = "alex_encounter.completed", detail = $"outcome={outcome};choices=3", latency = -1 };

        // ---------------------------------------------------------------------------------------------------------
        // Outcomes
        // ---------------------------------------------------------------------------------------------------------
        [Test]
        public void UncoveredRiskyActionIsCompromised()
        {
            SessionMetrics m = SessionScorer.Score(Session("a", Act("password_kiosk.credentials_entered", "Unsafe")));
            Assert.AreEqual(ResearchSchema.Compromised, m.FinalOutcome);
            Assert.AreEqual(ResearchSchema.Compromised, m.InitialOutcome);
        }

        [Test]
        public void ScenarioReportCoversOnlyItsOwnChannel()
        {
            SessionMetrics same = SessionScorer.Score(Session("a", Act("password_kiosk.credentials_entered", "Unsafe"),
                Act("password_kiosk.reported", "Safe", true)));
            Assert.AreEqual(ResearchSchema.Recovered, same.FinalOutcome);
            Assert.AreEqual(ResearchSchema.Compromised, same.InitialOutcome, "initial outcome is before recovery");

            SessionMetrics other = SessionScorer.Score(Session("b", Act("password_kiosk.credentials_entered", "Unsafe"),
                Act("phishing_email.reported", "Safe", true)));
            Assert.AreEqual(ResearchSchema.Compromised, other.FinalOutcome);
        }

        [Test]
        public void DialogueCorrectiveCoversEveryChannelButOnlyAfterwards()
        {
            SessionMetrics after = SessionScorer.Score(Session("a", Act("usb_drive.connected", "Unsafe"), Act("phishing_email.credentials_submitted", "Unsafe"),
                Reply("Report", "Safe", corrective: true)));
            Assert.AreEqual(ResearchSchema.Recovered, after.FinalOutcome);

            SessionMetrics before = SessionScorer.Score(Session("b", Reply("Report", "Safe", corrective: true), Act("usb_drive.connected", "Unsafe")));
            Assert.AreEqual(ResearchSchema.Compromised, before.FinalOutcome);
        }

        [Test]
        public void PromisesAreNeverRiskyActions()
        {
            SessionMetrics m = SessionScorer.Score(Session("a", Reply("Cooperate", "Unsafe", channel: "kiosk"), Reply("Cooperate", "Unsafe", channel: "email")));
            Assert.AreEqual(0, m.RiskyActions);
            Assert.AreEqual(ResearchSchema.Incomplete, m.FinalOutcome);
            Assert.AreEqual(2, m.Promises);
            Assert.AreEqual(0, m.PromisesActedOn);
        }

        [Test]
        public void ResistedDeclinedAndIncomplete()
        {
            Assert.AreEqual(ResearchSchema.Resisted, SessionScorer.Score(Session("a", Act("visitor_badge.identity_verified", "Safe"))).FinalOutcome);
            Assert.AreEqual(ResearchSchema.Resisted, SessionScorer.Score(Session("b", Reply("RequestVerification", "Safe", corrective: true))).FinalOutcome);
            Assert.AreEqual(ResearchSchema.Declined, SessionScorer.Score(Session("c", Reply("Refuse", "Safe"), Encounter("Declined"))).FinalOutcome);
            Assert.AreEqual(ResearchSchema.Incomplete, SessionScorer.Score(Session("d", Reply("Refuse", "Safe"))).FinalOutcome);
            Assert.AreEqual(ResearchSchema.Incomplete, SessionScorer.Score(Session("e")).FinalOutcome);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Metrics and denominators
        // ---------------------------------------------------------------------------------------------------------
        [Test]
        public void UnsafeRateExcludesNeutralStepsAndIntentionAgreementMatchesChannel()
        {
            SessionMetrics m = SessionScorer.Score(Session("a",
                Reply("Cooperate", "Unsafe", channel: "kiosk", latency: 3),
                Act("password_kiosk.sign_in_selected", "Neutral"),
                Act("password_kiosk.credentials_entered", "Unsafe"),
                Act("usb_drive.picked_up", "Neutral"),
                Act("usb_drive.submitted_to_it", "Safe", true),
                Reply("AskIdentification", "Safe", "Urgency", 5)));
            Assert.AreEqual(1, m.RiskyActions);
            Assert.AreEqual(1, m.SafeActions);
            Assert.AreEqual(2, m.NeutralActions);
            Assert.AreEqual(0.5, m.UnsafeActionRate, 1e-9, "1 risky / (1 risky + 1 safe); neutral excluded");
            Assert.AreEqual(1, m.PromisesActedOn);
            Assert.AreEqual(1.0, m.IntentionActionAgreement, 1e-9);
            Assert.AreEqual(4.0, m.MedianLatency, 1e-9);
            CollectionAssert.AreEquivalent(new[] { "kiosk", "usb" }, m.ScenariosCompleted);
            Assert.AreEqual(0.5, m.CompletionRate, 1e-9);
            Assert.AreEqual(1, m.VerificationEvents);
            Assert.IsTrue(m.Reported, "USB hand-in to IT counts as reporting");
        }

        [Test]
        public void AggregateDenominatorsAreExplicit()
        {
            var sessions = new List<SessionRecord>
            {
                Session("a", Act("password_kiosk.credentials_entered", "Unsafe"), Act("password_kiosk.reported", "Safe", true)), // RECOVERED
                Session("b", Act("usb_drive.connected", "Unsafe")), // COMPROMISED
                Session("c", Act("phishing_email.reported", "Safe", true)), // RESISTED
                Session("d") // INCOMPLETE
            };
            AggregateMetrics a = AggregateMetrics.Compute(sessions);
            Assert.AreEqual(4, a.Sessions);
            Assert.AreEqual(2, a.RiskyActions);
            Assert.AreEqual(4, a.ClassifiedActions);
            Assert.AreEqual(0.5, a.RiskyActionRate, 1e-9);
            Assert.AreEqual(2, a.SessionsWithRiskyAction);
            Assert.AreEqual(1, a.RecoveredSessions);
            Assert.AreEqual(0.5, a.RecoveryRate, 1e-9, "RECOVERED / sessions with >= 1 risky action");
            Assert.AreEqual(16, a.ScenarioSlots);
            Assert.AreEqual(3, a.ScenarioCompletions);
            Assert.AreEqual(1, a.OutcomeCounts[ResearchSchema.Compromised]);
            Assert.AreEqual(2, a.InitialOutcomeCounts[ResearchSchema.Compromised]);
            Assert.IsTrue(double.IsNaN(AggregateMetrics.Compute(new SessionRecord[0]).RiskyActionRate), "no denominator -> no rate, never 0");

            AggregateMetrics kiosk = AggregateMetrics.Compute(sessions, "kiosk");
            Assert.AreEqual(4, kiosk.ScenarioSlots, "one slot per session when filtered to a scenario");
            Assert.AreEqual(1, kiosk.RiskyActions);
            Assert.AreEqual(1, AggregateMetrics.Compute(sessions, null, ResearchSchema.Resisted).Sessions);
        }

        [Test]
        public void DescriptiveIndexRenormalisesAndRequiresTwoComponents()
        {
            SessionMetrics one = SessionScorer.Score(Session("a", Act("usb_drive.picked_up", "Neutral")));
            Assert.IsTrue(double.IsNaN(one.Index), "INCOMPLETE with no classified data has < 2 components");

            SessionMetrics m = SessionScorer.Score(Session("b", Act("visitor_badge.identity_verified", "Safe")));
            // Components: action safety 100 (0.40), verification 50 (0.15), reporting RESISTED 100 (0.20). No replies.
            double expected = (0.40 * 100 + 0.15 * 50 + 0.20 * 100) / (0.40 + 0.15 + 0.20);
            Assert.AreEqual(expected, m.Index, 1e-9);
            Assert.AreEqual(3, m.IndexComponents.Count);

            SessionMetrics worst = SessionScorer.Score(Session("c", Reply("Cooperate", "Unsafe", channel: "usb"), Act("usb_drive.connected", "Unsafe")));
            Assert.AreEqual(0, worst.Index, 1e-9);
        }

        [Test]
        public void PercentilesInterpolate()
        {
            Assert.AreEqual(2.5, Stats.Median(new List<double> { 1, 2, 3, 4 }), 1e-9);
            Assert.AreEqual(1.75, Stats.Percentile(new List<double> { 4, 1, 3, 2 }, 0.25), 1e-9);
            Assert.IsTrue(double.IsNaN(Stats.Median(new List<double>())));
        }

        // ---------------------------------------------------------------------------------------------------------
        // Parity with the dialogue engine (random play-throughs through the real DecisionLog pipeline)
        // ---------------------------------------------------------------------------------------------------------
        [Test]
        public void ClassifierMatchesDialogueEngineOnRandomPlaythroughs()
        {
            var graph = ScriptableObject.CreateInstance<DialogueGraph>();
            AlexMorganEncounter.Populate(graph);
            string[] scenarioEvents = ScenarioEventAdapter.EventSignals.Keys.ToArray();
            var outcomes = new Dictionary<string, int>();
            try
            {
                for (int seed = 1; seed <= 300; seed++)
                {
                    var rng = new System.Random(seed);
                    ResearchConsent.SetStatusForTests(ConsentStatus.Granted);
                    var recorder = new ResearchSessionRecorder(_store, ResearchSchema.Test);
                    DecisionLog.DecisionRecorded += recorder.HandleDecision;
                    DecisionLog.LifecycleRecorded += recorder.HandleLifecycle;
                    try
                    {
                        DecisionLog.StartSession();
                        recorder.HandleSessionStarted(DecisionLog.SessionId);
                        var engine = new AdaptiveDialogueEngine(graph) { RecordTelemetry = true };
                        engine.Begin();
                        // Modes widen coverage: all events / no risky scenario events / neutral scenario steps only / short runs.
                        int mode = seed % 4;
                        string[] pool = mode == 0 || mode == 3 ? scenarioEvents
                            : mode == 1 ? scenarioEvents.Where(x => !IsUnsafe(x)).ToArray()
                            : scenarioEvents.Where(x => !IsUnsafe(x) && !IsSafe(x)).ToArray();
                        int steps = mode == 3 ? 3 : 14;
                        for (int step = 0; step < steps; step++)
                        {
                            List<ResponseOption> options = engine.IsActive ? engine.GetAvailableOptions().ToList() : new List<ResponseOption>();
                            if (options.Count > 0 && rng.NextDouble() < 0.6)
                            {
                                engine.Choose(options[rng.Next(options.Count)], rng.NextDouble() * 6, DialogueContentSource.Scripted);
                            }
                            else
                            {
                                string e = pool[rng.Next(pool.Length)];
                                DecisionOutcome o = IsUnsafe(e) ? DecisionOutcome.Unsafe : IsSafe(e) ? DecisionOutcome.Safe : DecisionOutcome.Neutral;
                                DecisionLog.Record(e, "obj", "choice", o);
                                engine.HandleSignal(ScenarioEventAdapter.EventSignals[e]);
                            }
                        }
                        string expected = engine.ResolveOutcome(EncounterOutcome.Incomplete).ToString().ToUpperInvariant();
                        string actual = OutcomeClassifier.Classify(recorder.Current.events, null, out _);
                        Assert.AreEqual(expected, actual, $"seed {seed}: {engine.State.Summary}");
                        outcomes[actual] = outcomes.TryGetValue(actual, out int n) ? n + 1 : 1;
                    }
                    finally
                    {
                        DecisionLog.DecisionRecorded -= recorder.HandleDecision;
                        DecisionLog.LifecycleRecorded -= recorder.HandleLifecycle;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
            foreach (string o in new[] { ResearchSchema.Compromised, ResearchSchema.Recovered, ResearchSchema.Resisted, ResearchSchema.Incomplete })
            {
                Assert.IsTrue(outcomes.ContainsKey(o), $"fuzz covered {o} ({string.Join(", ", outcomes.Select(k => k.Key + "=" + k.Value))})");
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // Recorder: consent, dedupe, ordering, withdrawal
        // ---------------------------------------------------------------------------------------------------------
        private static bool IsUnsafe(string e) =>
            e.EndsWith("credentials_submitted") || e.EndsWith("link_opened") || e.EndsWith("credentials_entered") || e.EndsWith(".connected");

        private static bool IsSafe(string e) => e.EndsWith(".reported") || e.EndsWith("submitted_to_it") || e.EndsWith("identity_verified");

        private static DecisionEvent Decision(int seq, string name, DecisionOutcome outcome, double t) =>
            new DecisionEvent(DecisionLog.SessionId, seq, name, t, "obj", "c", outcome, false, DecisionContext.ForScenario(name));

        [Test]
        public void NothingIsWrittenWithoutConsent()
        {
            var recorder = new ResearchSessionRecorder(_store, ResearchSchema.Test);
            foreach (ConsentStatus status in new[] { ConsentStatus.Undecided, ConsentStatus.Declined, ConsentStatus.Withdrawn })
            {
                ResearchConsent.SetStatusForTests(status);
                recorder.HandleSessionStarted(DecisionLog.SessionId);
                recorder.HandleDecision(Decision(1, "usb_drive.connected", DecisionOutcome.Unsafe, 1));
                Assert.IsFalse(recorder.IsRecording, status.ToString());
            }
            Assert.IsFalse(Directory.Exists(_store.SessionsDir(ResearchSchema.Test)) && Directory.GetFiles(_store.SessionsDir(ResearchSchema.Test)).Length > 0);
        }

        [Test]
        public void RecorderDedupesOrdersPersistsAndFinishes()
        {
            ResearchConsent.SetStatusForTests(ConsentStatus.Granted);
            var recorder = new ResearchSessionRecorder(_store, ResearchSchema.Test, () => "SceneA");
            recorder.HandleSessionStarted(DecisionLog.SessionId);
            string id = DecisionLog.SessionId;
            recorder.HandleLifecycle(new LifecycleEvent(id, 0.2, "research.consent_granted", "research", "schema=1"));
            recorder.HandleDecision(Decision(1, "usb_drive.picked_up", DecisionOutcome.Neutral, 3));
            recorder.HandleDecision(Decision(1, "usb_drive.picked_up", DecisionOutcome.Neutral, 3)); // duplicate delivery
            recorder.HandleLifecycle(new LifecycleEvent(id, 0.2, "research.consent_granted", "research", "schema=1")); // duplicate
            recorder.HandleDecision(Decision(2, "usb_drive.connected", DecisionOutcome.Unsafe, 7.5));
            recorder.HandleDecision(new DecisionEvent("othersession", 3, "usb_drive.connected", 9, "obj", "c", DecisionOutcome.Unsafe, false,
                DecisionContext.ForScenario("usb_drive.connected"))); // stale session
            recorder.HandleSceneLoaded("SceneB");
            Assert.AreEqual(2, recorder.DuplicatesDropped);
            Assert.AreEqual(1, recorder.StaleDropped);

            ResearchStore.LoadResult load = _store.LoadAll(ResearchSchema.Test);
            Assert.AreEqual(1, load.Sessions.Count, "written after every event");
            SessionRecord saved = load.Sessions[0];
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, saved.events.Select(e => e.seq).ToArray());
            CollectionAssert.AreEqual(new[] { 0.2, 2.8, 4.5 }, saved.events.Select(e => e.elapsed).ToArray());
            CollectionAssert.AreEqual(new[] { "SceneA", "SceneB" }, saved.scenes);
            Assert.AreEqual("in_progress", saved.endReason);
            Assert.AreEqual(ResearchSchema.Compromised, saved.summary.finalOutcome);

            // A new participant (restart): the running session is finished, the next one is not recorded until consent.
            ResearchConsent.SetStatusForTests(ConsentStatus.Undecided);
            recorder.HandleConsentChanged(ConsentStatus.Undecided);
            DecisionLog.StartSession();
            recorder.HandleSessionStarted(DecisionLog.SessionId);
            Assert.IsFalse(recorder.IsRecording);
            SessionRecord finished = _store.LoadAll(ResearchSchema.Test).Sessions.Single();
            Assert.AreEqual("session_restart", finished.endReason);
            Assert.AreEqual(id, finished.sessionId);
        }

        [Test]
        public void RecorderResumesSameSessionAfterRecreation()
        {
            ResearchConsent.SetStatusForTests(ConsentStatus.Granted);
            var first = new ResearchSessionRecorder(_store, ResearchSchema.Test, () => "SceneA");
            first.HandleSessionStarted(DecisionLog.SessionId);
            first.HandleDecision(Decision(1, "usb_drive.connected", DecisionOutcome.Unsafe, 2));

            // e.g. the collector was recreated after a scene change: same consented session continues, nothing duplicated.
            var second = new ResearchSessionRecorder(_store, ResearchSchema.Test, () => "SceneB");
            second.HandleDecision(Decision(1, "usb_drive.connected", DecisionOutcome.Unsafe, 2));
            second.HandleDecision(Decision(2, "usb_drive.submitted_to_it", DecisionOutcome.Safe, 4));
            SessionRecord s = _store.LoadAll(ResearchSchema.Test).Sessions.Single();
            Assert.AreEqual(2, s.events.Count);
            Assert.AreEqual(1, second.DuplicatesDropped);
            CollectionAssert.AreEqual(new[] { "SceneA", "SceneB" }, s.scenes);
            Assert.AreEqual(ResearchSchema.Recovered, s.summary.finalOutcome);
        }

        [Test]
        public void WithdrawalDeletesTheSessionFile()
        {
            ResearchConsent.SetStatusForTests(ConsentStatus.Granted);
            var recorder = new ResearchSessionRecorder(_store, ResearchSchema.Test);
            recorder.HandleSessionStarted(DecisionLog.SessionId);
            recorder.HandleDecision(Decision(1, "usb_drive.connected", DecisionOutcome.Unsafe, 2));
            Assert.IsTrue(_store.Exists(ResearchSchema.Test, DecisionLog.SessionId));
            recorder.HandleWithdrawn(DecisionLog.SessionId);
            Assert.IsFalse(_store.Exists(ResearchSchema.Test, DecisionLog.SessionId));
            Assert.IsFalse(recorder.IsRecording);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Store
        // ---------------------------------------------------------------------------------------------------------
        [Test]
        public void StoreRoundTripsAndKeepsDatasetsApart()
        {
            List<SessionRecord> demo = DemoDataGenerator.Generate(5);
            foreach (SessionRecord s in demo) _store.Save(s);
            Assert.AreEqual(0, _store.LoadAll(ResearchSchema.Real).Sessions.Count);
            Assert.AreEqual(0, _store.LoadAll(ResearchSchema.Test).Sessions.Count);
            List<SessionRecord> loaded = _store.LoadAll(ResearchSchema.Demo).Sessions;
            Assert.AreEqual(5, loaded.Count);
            Assert.AreEqual(JsonUtility.ToJson(demo[0]), JsonUtility.ToJson(loaded.Single(s => s.sessionId == demo[0].sessionId)));
            Assert.IsFalse(Directory.GetFiles(_store.SessionsDir(ResearchSchema.Demo), "*.tmp").Any(), "no temp files left behind");

            Assert.AreEqual(5, _store.ClearSessions(ResearchSchema.Demo));
            Assert.AreEqual(0, _store.LoadAll(ResearchSchema.Demo).Sessions.Count);
        }

        [Test]
        public void CorruptEmptyAndMismatchedFilesAreQuarantined()
        {
            string dir = _store.SessionsDir(ResearchSchema.Test);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "empty.json"), "");
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{\"schema\":\"trustbreach.research.session/1.0\",\"sessionId\":");
            File.WriteAllText(Path.Combine(dir, "other.json"), "{\"hello\":1}");
            SessionRecord wrongDataset = DemoDataGenerator.Generate(1)[0];
            File.WriteAllText(Path.Combine(dir, wrongDataset.sessionId + ".json"), JsonUtility.ToJson(wrongDataset));
            var future = new SessionRecord { schema = "trustbreach.research.session/2.0", sessionId = "future", dataset = ResearchSchema.Test };
            File.WriteAllText(Path.Combine(dir, "future.json"), JsonUtility.ToJson(future));
            var good = new SessionRecord { sessionId = "good", dataset = ResearchSchema.Test, startedUtc = "2026-10-01T10:00Z" };
            _store.Save(good);

            ResearchStore.LoadResult load = _store.LoadAll(ResearchSchema.Test);
            Assert.AreEqual(1, load.Sessions.Count);
            Assert.AreEqual("good", load.Sessions[0].sessionId);
            Assert.AreEqual(4, load.Quarantined.Count, string.Join("; ", load.Quarantined));
            Assert.AreEqual(1, load.Unsupported.Count, "unknown schema version is skipped, not destroyed");
            Assert.AreEqual(8, Directory.GetFiles(_store.QuarantineDir(ResearchSchema.Test)).Length, "4 files + 4 reason notes");
            Assert.AreEqual(1, _store.LoadAll(ResearchSchema.Test).Sessions.Count, "second load is stable");
            Assert.AreEqual(0, new ResearchStore(Path.Combine(_root, "missing")).LoadAll(ResearchSchema.Real).Sessions.Count, "missing folder = empty");
        }

        [Test]
        public void SessionIdsCannotEscapeTheStore()
        {
            Assert.Throws<ArgumentException>(() => _store.SessionPath(ResearchSchema.Test, "../escape"));
            Assert.Throws<ArgumentException>(() => _store.SessionsDir("other"));
            Assert.IsFalse(_store.Delete(ResearchSchema.Test, "../../x"));
        }

        // ---------------------------------------------------------------------------------------------------------
        // Export
        // ---------------------------------------------------------------------------------------------------------
        [Test]
        public void CsvEscapingFollowsRfc4180()
        {
            Assert.AreEqual("plain", ResearchExporter.Csv("plain"));
            Assert.AreEqual("\"a,b\"", ResearchExporter.Csv("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", ResearchExporter.Csv("say \"hi\""));
            Assert.AreEqual("\"line1\nline2\"", ResearchExporter.Csv("line1\nline2"));
            Assert.AreEqual("\" padded \"", ResearchExporter.Csv(" padded "));
            Assert.AreEqual("'=SUM(A1)", ResearchExporter.Csv("=SUM(A1)"));
            Assert.AreEqual(string.Empty, ResearchExporter.Csv(null));
            Assert.AreEqual(string.Empty, ResearchExporter.Num(double.NaN));
        }

        [Test]
        public void ExportUsesInvariantCultureAndConsistentColumns()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                List<SessionRecord> sessions = DemoDataGenerator.Generate(6);
                sessions[0].events[0].detail = "note, with \"quotes\"\nand newline";
                ResearchExporter.Result result = ResearchExporter.Export(_store, ResearchSchema.Demo, sessions, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
                StringAssert.EndsWith("20261001_120000", result.Folder);
                foreach (string file in new[] { "events.csv", "session_summary.csv", "aggregate_metrics.csv", "export_manifest.json" })
                {
                    Assert.IsTrue(File.Exists(Path.Combine(result.Folder, file)), file);
                }
                Assert.AreEqual(6, Directory.GetFiles(Path.Combine(result.Folder, "sessions")).Length);

                string events = File.ReadAllText(Path.Combine(result.Folder, "events.csv"));
                List<List<string>> rows = ParseCsv(events);
                int columns = ResearchExporter.EventsHeader.Split(',').Length;
                Assert.IsTrue(rows.All(r => r.Count == columns), "every row has the header's column count (quoted commas/newlines survive)");
                Assert.AreEqual(sessions.Sum(s => s.events.Count) + 1, rows.Count);
                Assert.AreEqual("note, with \"quotes\"\nand newline", rows[1][columns - 1]);
                Assert.IsFalse(rows.Skip(1).Any(r => r[9].Contains(",")), "t_s uses '.' decimals under de-DE");

                List<List<string>> summary = ParseCsv(File.ReadAllText(Path.Combine(result.Folder, "session_summary.csv")));
                Assert.AreEqual(7, summary.Count);
                Assert.IsTrue(summary.All(r => r.Count == ResearchExporter.SummaryHeader.Split(',').Length));
                StringAssert.Contains("DEMO DATA", File.ReadAllText(Path.Combine(result.Folder, "export_manifest.json")));

                ResearchExporter.Result second = ResearchExporter.Export(_store, ResearchSchema.Demo, sessions, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
                Assert.AreNotEqual(result.Folder, second.Folder, "exports never overwrite each other");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void DemoDataIsDeterministicAndLabelled()
        {
            string a = string.Join("|", DemoDataGenerator.Generate(10).Select(JsonUtility.ToJson));
            string b = string.Join("|", DemoDataGenerator.Generate(10).Select(JsonUtility.ToJson));
            Assert.AreEqual(a, b);
            List<SessionRecord> demo = DemoDataGenerator.Generate();
            Assert.IsTrue(demo.All(s => s.dataset == ResearchSchema.Demo && s.sessionId.StartsWith("DEMO-") && ResearchStore.IsValidSessionId(s.sessionId)));
            Assert.Greater(demo.Select(s => s.summary.finalOutcome).Distinct().Count(), 3, "demo data shows several outcomes");
        }

        /// <summary>Minimal RFC 4180 reader used to check the exporter's output.</summary>
        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); i++;
                }
                else cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
