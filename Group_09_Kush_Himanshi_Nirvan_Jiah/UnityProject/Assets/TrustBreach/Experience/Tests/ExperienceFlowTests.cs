using NUnit.Framework;
using TrustBreach.Dialogue;

namespace TrustBreach.Experience.Tests
{
    public class ExperienceFlowTests
    {
        private const ExperienceStage Welcome = ExperienceStage.Welcome, Consent = ExperienceStage.Consent, Controls = ExperienceStage.Controls,
            Explore = ExperienceStage.Explore, Encounter = ExperienceStage.Encounter, Debrief = ExperienceStage.Debrief;

        [Test]
        public void Buttons_WalkThroughTheIntroductionInOrder()
        {
            Assert.AreEqual(Consent, ExperienceFlow.Next(Welcome));
            Assert.AreEqual(Controls, ExperienceFlow.Next(Consent));
            Assert.AreEqual(Explore, ExperienceFlow.Next(Controls));
            Assert.AreEqual(Explore, ExperienceFlow.Next(Explore), "Later stages advance only from what actually happens");
            Assert.AreEqual(Encounter, ExperienceFlow.Next(Encounter));
        }

        [Test]
        public void ConsentDecision_MovesOnToControls()
        {
            Assert.AreEqual(Consent, ExperienceFlow.Resolve(Consent, false, false, false, false));
            Assert.AreEqual(Controls, ExperienceFlow.Resolve(Consent, true, false, false, false));
            Assert.AreEqual(Welcome, ExperienceFlow.Resolve(Welcome, true, false, false, false), "Deciding early does not skip the welcome");
        }

        [Test]
        public void ParticipantWhoSkipsTheGuide_IsNeverHeldBack()
        {
            Assert.AreEqual(Explore, ExperienceFlow.Resolve(Welcome, false, true, false, false), "Acting in the office jumps to Explore");
            Assert.AreEqual(Encounter, ExperienceFlow.Resolve(Welcome, false, false, true, false), "Talking to Alex jumps to the encounter");
            Assert.AreEqual(Debrief, ExperienceFlow.Resolve(Welcome, false, false, true, true));
            Assert.AreEqual(Debrief, ExperienceFlow.Resolve(Controls, true, true, false, true));
        }

        [Test]
        public void Stage_NeverMovesBackwardsWhileEvidenceHolds()
        {
            Assert.AreEqual(Explore, ExperienceFlow.Resolve(Explore, false, false, false, false));
            Assert.AreEqual(Explore, ExperienceFlow.Resolve(Explore, true, true, false, false));
        }

        [Test]
        public void Labels_AreReadableAndUseOnlyAsciiSeparators()
        {
            Assert.AreEqual("STEP 1 OF 6  -  WELCOME", ExperienceFlow.StepLabel(Welcome));
            Assert.AreEqual("STEP 6 OF 6  -  YOUR DEBRIEF", ExperienceFlow.StepLabel(Debrief));
            for (var s = Welcome; s <= Debrief; s++)
            {
                foreach (char c in ExperienceFlow.StepLabel(s)) Assert.Less((int)c, 128, $"Non-ASCII glyph in {s}");
                Assert.LessOrEqual(ExperienceFlow.StepLabel(s).Length, 40, "Fits the progress strip on one line");
            }
            Assert.AreEqual("Password reset kiosk", ExperienceFlow.ScenarioLabel("PasswordResetKioskScenario"));
        }

        [Test]
        public void DebriefOutcomeLabels_MatchTheEngine()
        {
            Assert.AreEqual("RECOVERED", AdaptiveDialogueEngine.OutcomeLabel(EncounterOutcome.Recovered));
        }
    }
}
