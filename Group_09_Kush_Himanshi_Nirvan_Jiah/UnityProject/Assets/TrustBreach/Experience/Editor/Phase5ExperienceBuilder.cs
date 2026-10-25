using System;
using System.IO;
using System.Linq;
using TMPro;
using TrustBreach.Analytics.Editor;
using TrustBreach.Dialogue;
using TrustBreach.Editor;
using TrustBreach.Interactions;
using TrustBreach.Interactions.Scenarios;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static TrustBreach.Editor.OfficeBuildKit;
using B = TrustBreach.Editor.TrustBreachCorporateOfficeBuilder;
using P3 = TrustBreach.Editor.Phase3AdversaryBuilder;
using P4 = TrustBreach.Analytics.Editor.Phase4AnalyticsBuilder;

namespace TrustBreach.Experience.Editor
{
    /// <summary>
    /// Phase 5 (idempotent): adds the guided participant flow to the two Phase 3 scenes.
    ///  - UI/ExperienceGuidePanel: welcome, consent pointer, XR controls, scenario checklist, debrief pointer, RESTART / EXIT,
    ///    with a six-step progress bar. Ahead-right of the spawn; the research panel stays ahead-left.
    ///  - UI/ExperienceProgressStrip: small lazy-follow "STEP n OF 6" indicator (no colliders).
    /// Nothing else in the scenes is touched; re-runs replace only these two objects. Also re-applied after a Phase 3 rebuild.
    /// </summary>
    [InitializeOnLoad]
    public static class Phase5ExperienceBuilder
    {
        public const string GuidePanelName = "ExperienceGuidePanel";
        public const string ProgressStripName = "ExperienceProgressStrip";
        public static readonly Vector3 GuidePanelPosition = new Vector3(0.69f, 1.45f, 0.55f);
        public static readonly Vector2 GuideSize = new Vector2(0.8f, 0.66f);
        /// <summary>Checklist order on the guide panel.</summary>
        public static readonly string[] ScenarioOrder = { "PhishingEmailScenario", "PasswordResetKioskScenario", "VisitorBadgeScenario", "MaliciousUSBScenario" };
        private const string Menu = "Tools/TRUST∕∕BREACH/Experience/";

        static Phase5ExperienceBuilder()
        {
            P3.AfterBuild += report =>
            {
                ApplyToPhase3Scenes();
                report.Passed.Add("Phase 5 participant flow re-applied to both Phase 3 scenes");
            };
        }

        [MenuItem(Menu + "Build Phase 5 Participant Flow", priority = 180)]
        private static void BuildFromMenu()
        {
            XRFoundationValidator.Report report = Build();
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        public static XRFoundationValidator.Report Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before building Phase 5.");
            XRFoundationSceneBuilder.BackupDirtyScenes();
            OfficeInteractionsBuilder.BackupSceneFile(P3.ScenePath);
            OfficeInteractionsBuilder.BackupSceneFile(P3.DemoScenePath);
            ApplyToPhase3Scenes();
            XRFoundationValidator.Report report = Phase5Validator.ValidateScenes();
            EditorSceneManager.OpenScene(P3.ScenePath, OpenSceneMode.Single);
            return report;
        }

        public static void ApplyToPhase3Scenes()
        {
            foreach (string path in new[] { P3.ScenePath, P3.DemoScenePath })
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Apply(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException($"Failed to save {path}");
            }
        }

        /// <summary>Adds (or replaces) the Phase 5 objects in one Phase 3 scene.</summary>
        public static void Apply(Scene scene)
        {
            Transform ui = Root(scene, B.UIRootName);
            foreach (Transform old in new[] { ui.Find(GuidePanelName), ui.Find(ProgressStripName) })
            {
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            GameObject[] roots = scene.GetRootGameObjects();
            EncounterDirector director = roots.SelectMany(r => r.GetComponentsInChildren<EncounterDirector>(true)).Single();
            ScenarioController[] scenarios = roots.SelectMany(r => r.GetComponentsInChildren<ScenarioController>(true))
                .OrderBy(s => Array.IndexOf(ScenarioOrder, s.GetType().Name)).ToArray();

            ExperienceGuidePanel guide = BuildGuidePanel(ui, director, scenarios);
            BuildProgressStrip(ui, guide, guide.transform, ui.Find(P3.DialoguePanelName), ui.Find(P4.ConsentPanelName));
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static ExperienceGuidePanel BuildGuidePanel(Transform ui, EncounterDirector director, ScenarioController[] scenarios)
        {
            Transform root = Group(GuidePanelName, ui, Vector3.zero);
            Vector3 towardUser = P4.ParticipantSpawn - GuidePanelPosition;
            towardUser.y = 0f;
            root.SetPositionAndRotation(GuidePanelPosition, Facing(towardUser));

            Color navy = new Color(0.04f, 0.1f, 0.18f), teal = new Color(0.1f, 0.62f, 0.66f), slate = new Color(0.35f, 0.38f, 0.42f);
            Color ink = new Color(0.93f, 0.95f, 0.97f), cyan = new Color(0.22f, 0.88f, 1f);
            Vector2 size = GuideSize;
            Transform s = Screen("Screen", root, Vector3.zero, Quaternion.identity, size, new Color(0.07f, 0.09f, 0.13f), out _,
                Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            // Slim floor stand (visual only: no collider, so it never blocks locomotion or rays).
            Box("Stand", root, new Vector3(0f, -(GuidePanelPosition.y + size.y * 0.5f) * 0.5f, 0.03f),
                new Vector3(0.05f, GuidePanelPosition.y - size.y * 0.5f, 0.05f), Lit("Metal_Graphite", new Color(0.2f, 0.21f, 0.23f), 0.6f, 0.7f));

            ScreenRect(s, "HeaderBar", 0f, 0.29f, size.x, 0.075f, navy);
            TextMeshPro step = ScreenText(s, "Step", "STEP 1 OF 6  -  WELCOME", 0f, 0.29f, 0.76f, 0.07f, 0.16f, cyan, TextAlignmentOptions.Center, FontStyles.Bold);
            var segments = new Renderer[ExperienceFlow.StageCount];
            const float segW = 0.12f, gap = 0.01f;
            float x0 = -(segW * ExperienceFlow.StageCount + gap * (ExperienceFlow.StageCount - 1)) * 0.5f + segW * 0.5f;
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = ScreenRect(s, $"Progress_{i + 1}", x0 + i * (segW + gap), 0.237f, segW, 0.012f, Color.white).GetComponent<Renderer>();
            }
            TextMeshPro title = ScreenText(s, "Title", "Welcome", -0.37f, 0.215f, 0.74f, 0.06f, 0.24f, Color.white, style: FontStyles.Bold);
            TextMeshPro body = ScreenText(s, "Body", "Welcome to TRUST//BREACH.", -0.37f, 0.15f, 0.74f, 0.335f, 0.16f, ink);
            body.enableAutoSizing = true;
            body.fontSizeMin = 0.11f;
            body.fontSizeMax = 0.16f;

            Transform primaryGroup = Group("PrimaryGroup", s, Vector3.zero);
            XRButton primary = OfficeInteractionsBuilder.Button(primaryGroup, "PrimaryButton", "experience.guide_primary", "START",
                new Vector2(-0.09f, -0.265f), new Vector2(0.5f, 0.08f), teal, 0.13f);
            TMP_Text primaryLabel = primaryGroup.Find("PrimaryButton_Label").GetComponent<TMP_Text>();
            Transform exitGroup = Group("ExitGroup", s, Vector3.zero);
            XRButton exit = OfficeInteractionsBuilder.Button(exitGroup, "ExitButton", "experience.guide_exit", "EXIT",
                new Vector2(0.275f, -0.265f), new Vector2(0.19f, 0.08f), slate, 0.11f);
            TMP_Text exitLabel = exitGroup.Find("ExitButton_Label").GetComponent<TMP_Text>();

            OfficeInteractionsBuilder.Hint(root, "Hint_Trigger", OfficeInteractionsBuilder.TriggerHint, new Vector3(0f, -size.y * 0.5f - 0.07f, -0.02f), 0.42f);

            var guide = root.gameObject.AddComponent<ExperienceGuidePanel>();
            OfficeInteractionsBuilder.Inject(guide, ("_director", director), ("_step", step), ("_title", title), ("_body", body),
                ("_primaryButton", primary), ("_primaryLabel", primaryLabel), ("_primaryGroup", primaryGroup.gameObject),
                ("_exitButton", exit), ("_exitLabel", exitLabel), ("_exitGroup", exitGroup.gameObject));
            var so = new SerializedObject(guide);
            SetArray(so.FindProperty("_scenarios"), scenarios.Cast<UnityEngine.Object>().ToArray());
            SetArray(so.FindProperty("_segments"), segments.Cast<UnityEngine.Object>().ToArray());
            so.ApplyModifiedPropertiesWithoutUndo();
            exitGroup.gameObject.SetActive(false); // shown on the debrief step
            return guide;
        }

        private static void BuildProgressStrip(Transform ui, ExperienceGuidePanel guide, params Transform[] hideNear)
        {
            Transform root = Group(ProgressStripName, ui, P4.ParticipantSpawn + new Vector3(0f, 1.1f, 1.25f));
            Transform content = Group("Content", root, Vector3.zero);
            Vector2 size = new Vector2(0.5f, 0.07f);
            Panel("Background", content, Vector2.zero, size, Unlit("Screen_0A1A2E", new Color(0.04f, 0.1f, 0.18f)));
            TextMeshPro label = ScreenText(content, "Label", "STEP 1 OF 6  -  WELCOME", 0f, 0.01f, 0.48f, 0.04f, 0.12f,
                new Color(0.93f, 0.95f, 0.97f), TextAlignmentOptions.Center, FontStyles.Bold);
            var segments = new Renderer[ExperienceFlow.StageCount];
            const float segW = 0.07f, gap = 0.008f;
            float x0 = -(segW * ExperienceFlow.StageCount + gap * (ExperienceFlow.StageCount - 1)) * 0.5f + segW * 0.5f;
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = ScreenRect(content, $"Progress_{i + 1}", x0 + i * (segW + gap), -0.022f, segW, 0.008f, Color.white).GetComponent<Renderer>();
            }

            var strip = root.gameObject.AddComponent<ExperienceProgressStrip>();
            OfficeInteractionsBuilder.Inject(strip, ("_guide", guide), ("_content", content), ("_label", label));
            var so = new SerializedObject(strip);
            SetArray(so.FindProperty("_segments"), segments.Cast<UnityEngine.Object>().ToArray());
            SetArray(so.FindProperty("_hideNear"), hideNear.Where(t => t != null).Cast<UnityEngine.Object>().ToArray());
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(SerializedProperty array, UnityEngine.Object[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        internal static Transform Root(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(r => r.name == name)?.transform
            ?? throw new InvalidOperationException($"Scene root '{name}' not found in {scene.path}. Build Phase 3 first.");

        // =============================================================================================================
        // Launch menus
        // =============================================================================================================
        [MenuItem(Menu + "Play Participant Experience (REAL research data)", priority = 190)]
        public static void PlayParticipant() => PlayScene(P3.ScenePath);

        [MenuItem(Menu + "Play Demo Experience (TEST data, never REAL)", priority = 191)]
        public static void PlayDemo() => PlayScene(P3.DemoScenePath);

        [MenuItem(Menu + "Open Research Dashboard (desktop)", priority = 200)]
        private static void OpenDashboard() => P4.OpenDashboard();

        private static void PlayScene(string path)
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }
    }
}
