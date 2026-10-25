using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TrustBreach.Analytics
{
    /// <summary>
    /// Deterministic SYNTHETIC sessions for showing the dashboard without participants. Output is always dataset "demo",
    /// ids start with "DEMO-", and the same seed always produces identical files. These are not research findings.
    /// Event names, classifications and channels mirror the real Phase 2.5 scenarios and the Alex Morgan encounter.
    /// </summary>
    public static class DemoDataGenerator
    {
        public const int DefaultSeed = 20260;
        public const int DefaultCount = 24;
        private static readonly string[] Techniques = { "Authority", "Urgency", "TrustBuilding" };
        private static readonly string[] SafeClasses = { "AskIdentification", "Challenge", "RequestVerification", "Refuse" };
        private static readonly string[] PromiseChannels = { "kiosk", "email", "usb" };

        public static List<SessionRecord> Generate(int count = DefaultCount, int seed = DefaultSeed)
        {
            var rng = new Random(seed);
            var sessions = new List<SessionRecord>();
            var start = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < count; i++)
            {
                DateTime started = start.AddDays(i / 4).AddMinutes(37 * (i % 4) + rng.Next(0, 20));
                sessions.Add(GenerateOne(rng, i, started));
            }
            return sessions;
        }

        public static int WriteTo(ResearchStore store, int count = DefaultCount, int seed = DefaultSeed)
        {
            List<SessionRecord> sessions = Generate(count, seed);
            foreach (SessionRecord s in sessions) store.Save(s);
            return sessions.Count;
        }

        private static SessionRecord GenerateOne(Random rng, int index, DateTime started)
        {
            double caution = rng.NextDouble();
            var b = new Builder(rng);
            b.Lifecycle("research.consent_granted", "research", "schema=1", 0.4);
            b.Lifecycle("alex_encounter.approached", "NPC_AlexMorgan", null, 6 + rng.NextDouble() * 20);
            bool quitsEarly = rng.NextDouble() < 0.1;
            var scenarios = new List<string> { "email", "kiosk", "badge", "usb" }.OrderBy(_ => rng.Next()).ToList();
            int replies = quitsEarly ? 1 + rng.Next(2) : 3 + rng.Next(4);
            int refusals = 0;

            for (int r = 0; r < replies; r++)
            {
                string technique = Techniques[rng.Next(Techniques.Length)];
                if (rng.NextDouble() < caution)
                {
                    string cls = SafeClasses[rng.Next(SafeClasses.Length)];
                    if (cls == "Refuse" || cls == "Challenge") refusals++;
                    b.Reply(cls, "Safe", technique, "dialogue", false);
                }
                else if (rng.NextDouble() < 0.12 && caution < 0.35)
                {
                    b.Reply("Cooperate", "Unsafe", technique, "verbal", false, verbalDisclosure: true);
                }
                else
                {
                    b.Reply("Cooperate", "Unsafe", technique, PromiseChannels[rng.Next(PromiseChannels.Length)], false);
                }
                if (!quitsEarly && r < scenarios.Count && rng.NextDouble() < 0.85) Scenario(b, scenarios[r], caution);
            }
            if (!quitsEarly)
            {
                for (int s = replies; s < scenarios.Count; s++)
                {
                    if (rng.NextDouble() < 0.8) Scenario(b, scenarios[s], caution);
                }
                bool anyRisky = b.Events.Any(e => e.riskyAction);
                if ((anyRisky && rng.NextDouble() < 0.35 + caution * 0.6) || (!anyRisky && rng.NextDouble() < caution * 0.5))
                {
                    b.Reply(rng.NextDouble() < 0.6 ? "Report" : "RequestVerification", "Safe", Techniques[rng.Next(3)], "dialogue", true);
                }
                bool corrective = b.Events.Any(e => e.corrective) || b.Events.Any(OutcomeClassifier.IsVerification);
                string encounter = !b.Events.Any(e => e.riskyAction) && !corrective && refusals >= 2 ? ResearchSchema.Declined : null;
                encounter ??= OutcomeClassifier.Classify(b.Events, null, out _);
                b.Lifecycle("alex_encounter.completed", "NPC_AlexMorgan",
                    $"outcome={Title(encounter)};choices={b.Events.Count(e => e.kind == "dialogue_choice")}", 4 + rng.NextDouble() * 6);
                b.Lifecycle("alex_encounter.debrief_shown", "NPC_AlexMorgan", $"outcome={Title(encounter)}", 3 + rng.NextDouble() * 3);
            }

            var record = new SessionRecord
            {
                dataset = ResearchSchema.Demo,
                sessionId = $"DEMO-{index + 1:000}-{rng.Next(0x10000, 0xFFFFF):x5}",
                startedUtc = SessionRecord.UtcMinute(started),
                endReason = quitsEarly ? "application_quit" : "session_restart",
                appVersion = "demo",
                unityVersion = "demo",
                platform = "SyntheticDemo",
                consent = new ConsentInfo { status = "Granted", grantedAt = 0.4 },
                events = b.Events
            };
            record.scenes.Add("TrustBreach_Phase3_Adversary");
            record.durationSeconds = b.Time;
            record.endedUtc = SessionRecord.UtcMinute(started.AddSeconds(b.Time));
            SessionScorer.UpdateSummary(record);
            return record;
        }

        private static void Scenario(Builder b, string channel, double caution)
        {
            Random rng = b.Rng;
            double risk = (1 - caution) * 0.75;
            switch (channel)
            {
                case "email":
                    if (rng.NextDouble() < risk)
                    {
                        b.Action("phishing_email.link_opened", "PhishingEmailWorkstation", "verify_account_now", "Unsafe", channel);
                        if (rng.NextDouble() < 0.65) b.Action("phishing_email.credentials_submitted", "PhishingEmailWorkstation", "proceed", "Unsafe", channel);
                        else b.Action("phishing_email.reported", "PhishingEmailWorkstation", "report_suspicious_email", "Safe", channel, true);
                    }
                    else b.Action("phishing_email.reported", "PhishingEmailWorkstation", "report_phishing_from_inbox", "Safe", channel, true);
                    break;
                case "kiosk":
                    b.Action("password_kiosk.sign_in_selected", "FakeSSLTerminal", "sign_in_and_reset", "Neutral", channel);
                    double k = rng.NextDouble();
                    if (k < risk) b.Action("password_kiosk.credentials_entered", "FakeSSLTerminal", "continue", "Unsafe", channel);
                    else if (k < risk + (1 - risk) * 0.5) b.Action("password_kiosk.reported", "FakeSSLTerminal", "report_kiosk_to_it", "Safe", channel, true);
                    else b.Action("password_kiosk.cancelled", "FakeSSLTerminal", "cancel", "Neutral", channel);
                    break;
                case "badge":
                    b.Action("visitor_badge.inspected", "SpoofedVisitorBadge", "picked_up_badge", "Neutral", channel);
                    if (rng.NextDouble() < 0.3 + caution * 0.6) b.Action("visitor_badge.identity_verified", "SpoofedVisitorBadge", "verify_identity", "Safe", channel);
                    break;
                case "usb":
                    b.Action("usb_drive.picked_up", "MaliciousUSBDrive", "picked_up_usb", "Neutral", channel);
                    if (rng.NextDouble() < risk * 0.8) b.Action("usb_drive.connected", "MaliciousUSBDrive", "connect_usb", "Unsafe", channel);
                    else b.Action("usb_drive.submitted_to_it", "MaliciousUSBDrive", "submit_to_it", "Safe", channel, true);
                    break;
            }
        }

        private static string Title(string outcome) =>
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(outcome.ToLowerInvariant());

        private sealed class Builder
        {
            public readonly Random Rng;
            public readonly List<ResearchEvent> Events = new List<ResearchEvent>();
            public double Time;
            private int _decisions;

            public Builder(Random rng) => Rng = rng;

            private ResearchEvent Add(ResearchEvent e, double gap)
            {
                double previous = Time;
                Time = Math.Round(Time + gap, 3);
                e.seq = Events.Count + 1;
                e.t = Time;
                e.elapsed = Math.Round(Time - previous, 3);
                e.scene = "TrustBreach_Phase3_Adversary";
                e.detail ??= string.Empty;
                Events.Add(e);
                return e;
            }

            public void Lifecycle(string name, string objectId, string detail, double gap) =>
                Add(new ResearchEvent
                {
                    kind = "lifecycle", name = name, scenarioId = objectId, interactionId = string.Empty, channel = string.Empty,
                    classification = string.Empty, completion = "n/a", persuasion = string.Empty, latency = -1, node = string.Empty,
                    choiceClass = string.Empty, contentSource = string.Empty, detail = detail ?? string.Empty
                }, gap);

            public void Reply(string cls, string classification, string technique, string channel, bool corrective, bool verbalDisclosure = false)
            {
                // Log-normal-ish reply latency, 1.5-25 s.
                double latency = Math.Round(Math.Min(25, Math.Exp(1.2 + Rng.NextDouble() * 1.4 + (cls == "Cooperate" ? -0.3 : 0.15))), 3);
                bool promise = !verbalDisclosure && cls == "Cooperate";
                Add(new ResearchEvent
                {
                    sourceSeq = ++_decisions,
                    kind = verbalDisclosure ? "security_action" : "dialogue_choice",
                    name = "alex_encounter." + Snake(cls),
                    scenarioId = "NPC_AlexMorgan",
                    interactionId = "demo_" + Snake(cls),
                    channel = channel,
                    classification = classification,
                    riskyAction = verbalDisclosure,
                    corrective = corrective,
                    completion = verbalDisclosure ? "completed" : promise ? "promised" : "stance",
                    persuasion = technique,
                    latency = latency,
                    node = "demo_node",
                    choiceClass = cls,
                    contentSource = "scripted"
                }, latency + 2 + Rng.NextDouble() * 4);
            }

            public void Action(string name, string objectId, string choice, string classification, string channel, bool corrective = false) =>
                Add(new ResearchEvent
                {
                    sourceSeq = ++_decisions,
                    kind = "security_action",
                    name = name,
                    scenarioId = objectId,
                    interactionId = choice,
                    channel = channel,
                    classification = classification,
                    riskyAction = classification == "Unsafe",
                    corrective = corrective,
                    completion = "completed",
                    persuasion = string.Empty,
                    latency = -1,
                    node = string.Empty,
                    choiceClass = string.Empty,
                    contentSource = string.Empty
                }, 8 + Rng.NextDouble() * 30);

            private static string Snake(string pascal) =>
                string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
        }
    }
}
