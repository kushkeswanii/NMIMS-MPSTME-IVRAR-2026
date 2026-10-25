using System;

namespace TrustBreach.Experience
{
    /// <summary>The participant-facing stages, in order. Shown as "STEP n OF 6".</summary>
    public enum ExperienceStage
    {
        Welcome,
        Consent,
        Controls,
        Explore,
        Encounter,
        Debrief
    }

    /// <summary>
    /// Pure stage logic for the guided participant flow (no Unity objects, so it is unit-tested directly).
    /// The guide never blocks anything: the first three stages are acknowledged with a button, and the stage
    /// jumps forward on its own as soon as the participant acts in the office, talks to Alex or reaches the debrief.
    /// </summary>
    public static class ExperienceFlow
    {
        public const int StageCount = 6;

        public static string Title(ExperienceStage stage) => stage switch
        {
            ExperienceStage.Welcome => "Welcome",
            ExperienceStage.Consent => "Research consent (optional)",
            ExperienceStage.Controls => "XR controls",
            ExperienceStage.Explore => "Explore the office",
            ExperienceStage.Encounter => "Meet Alex Morgan",
            ExperienceStage.Debrief => "Your debrief",
            _ => string.Empty
        };

        /// <summary>"STEP 4 OF 6  -  EXPLORE THE OFFICE" (ASCII separator: the default TMP font has no middle dot).</summary>
        public static string StepLabel(ExperienceStage stage) => $"STEP {(int)stage + 1} OF {StageCount}  -  {ShortTitle(stage).ToUpperInvariant()}";

        /// <summary>Title short enough for the one-line progress strip.</summary>
        public static string ShortTitle(ExperienceStage stage) => stage == ExperienceStage.Consent ? "Research consent" : Title(stage);

        /// <summary>
        /// Current stage from what the participant has acknowledged and what has actually happened.
        /// Later evidence always wins, so a participant who skips the guide is never held back.
        /// </summary>
        public static ExperienceStage Resolve(ExperienceStage acknowledged, bool consentDecided, bool anyDecision, bool encounterStarted, bool debriefReached)
        {
            if (debriefReached) return ExperienceStage.Debrief;
            if (encounterStarted) return ExperienceStage.Encounter;
            if (anyDecision) return Max(acknowledged, ExperienceStage.Explore);
            if (acknowledged == ExperienceStage.Consent && consentDecided) return ExperienceStage.Controls;
            return acknowledged;
        }

        /// <summary>Stage reached by pressing the guide's main button on <paramref name="stage"/>.</summary>
        public static ExperienceStage Next(ExperienceStage stage) => stage < ExperienceStage.Explore ? stage + 1 : stage;

        /// <summary>Friendly checklist name for a Phase 2.5 scenario controller type.</summary>
        public static string ScenarioLabel(string controllerTypeName) => controllerTypeName switch
        {
            "PhishingEmailScenario" => "Phishing email (workstation)",
            "PasswordResetKioskScenario" => "Password reset kiosk",
            "VisitorBadgeScenario" => "Visitor badge (reception)",
            "MaliciousUSBScenario" => "Found USB drive",
            _ => controllerTypeName ?? string.Empty
        };

        private static ExperienceStage Max(ExperienceStage a, ExperienceStage b) => (ExperienceStage)Math.Max((int)a, (int)b);
    }
}
