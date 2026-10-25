using System;
using System.IO;
using System.Linq;
using TMPro;
using TrustBreach.Analytics.Dashboard;
using TrustBreach.Editor;
using TrustBreach.Interactions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static TrustBreach.Editor.OfficeBuildKit;
using B = TrustBreach.Editor.TrustBreachCorporateOfficeBuilder;
using P3 = TrustBreach.Editor.Phase3AdversaryBuilder;

namespace TrustBreach.Analytics.Editor
{
    /// <summary>
    /// Phase 4 (idempotent): adds the research layer to the two Phase 3 scenes and (re)generates the desktop dashboard scene.
    ///  - Managers/ResearchAnalytics: <see cref="ResearchEventCollector"/> (dataset "real" in the participant scene, "test" in DEMO).
    ///  - UI/ResearchConsentPanel: in-VR information + consent panel beside the entrance, off the walking route.
    ///  - Analytics/Scenes/TrustBreach_ResearchDashboard.unity: camera, EventSystem, Screen Space canvas with the dashboard.
    /// Everything else in the Phase 3 scenes is left untouched. Re-runs replace only the objects listed above.
    /// Also runs automatically after the Phase 3 builder regenerates its scenes.
    /// </summary>
    [InitializeOnLoad]
    public static class Phase4AnalyticsBuilder
    {
        public const string AnalyticsRootName = "ResearchAnalytics";
        public const string ConsentPanelName = "ResearchConsentPanel";
        public const string DashboardScenePath = "Assets/TrustBreach/Analytics/Scenes/TrustBreach_ResearchDashboard.unity";
        public const string DashboardObjectName = "ResearchDashboard";
        public static readonly Vector3 ConsentPanelPosition = new Vector3(-1.3f, 1.45f, 0.2f);
        public static readonly Vector3 ParticipantSpawn = new Vector3(0f, 0f, -1f);
        private const string Menu = "Tools/TRUST∕∕BREACH/Research/";

        static Phase4AnalyticsBuilder()
        {
            P3.AfterBuild += report =>
            {
                ApplyToPhase3Scenes();
                report.Passed.Add("Phase 4 research layer re-applied to both Phase 3 scenes");
            };
        }

        [MenuItem(Menu + "Build Phase 4 Analytics", priority = 140)]
        private static void BuildFromMenu()
        {
            XRFoundationValidator.Report report = Build();
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        public static XRFoundationValidator.Report Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before building Phase 4.");
            XRFoundationSceneBuilder.BackupDirtyScenes();
            OfficeInteractionsBuilder.BackupSceneFile(P3.ScenePath);
            OfficeInteractionsBuilder.BackupSceneFile(P3.DemoScenePath);
            if (File.Exists(Path.Combine(ProjectRoot, DashboardScenePath))) OfficeInteractionsBuilder.BackupSceneFile(DashboardScenePath);

            ApplyToPhase3Scenes();
            BuildDashboardScene();
            XRFoundationValidator.Report report = Phase4Validator.ValidateAll();
            EditorSceneManager.OpenScene(P3.ScenePath, OpenSceneMode.Single);
            return report;
        }

        public static void ApplyToPhase3Scenes()
        {
            foreach ((string path, string dataset) in new[] { (P3.ScenePath, ResearchSchema.Real), (P3.DemoScenePath, ResearchSchema.Test) })
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Apply(scene, dataset);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException($"Failed to save {path}");
            }
        }

        /// <summary>Adds (or replaces) the Phase 4 objects in one Phase 3 scene.</summary>
        public static void Apply(Scene scene, string dataset)
        {
            Transform managers = Root(scene, B.ManagersRootName);
            Transform ui = Root(scene, B.UIRootName);
            foreach (Transform old in new[] { managers.Find(AnalyticsRootName), ui.Find(ConsentPanelName) })
            {
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            var collector = new GameObject(AnalyticsRootName).AddComponent<ResearchEventCollector>();
            collector.transform.SetParent(managers, false);
            var so = new SerializedObject(collector);
            so.FindProperty("_dataset").stringValue = dataset;
            so.ApplyModifiedPropertiesWithoutUndo();

            BuildConsentPanel(ui);
            OfficeInteractionsBuilder.HidePendingPages();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void BuildConsentPanel(Transform ui)
        {
            Transform root = Group(ConsentPanelName, ui, Vector3.zero);
            Vector3 towardUser = ParticipantSpawn - ConsentPanelPosition;
            towardUser.y = 0f;
            root.SetPositionAndRotation(ConsentPanelPosition, Facing(towardUser));

            Color navy = new Color(0.04f, 0.1f, 0.18f), cyan = new Color(0.1f, 0.65f, 0.76f), slate = new Color(0.35f, 0.38f, 0.42f);
            Color red = new Color(0.7f, 0.18f, 0.14f), ink = new Color(0.9f, 0.93f, 0.96f);
            var size = new Vector2(0.9f, 0.66f);
            Transform s = Screen("Screen", root, Vector3.zero, Quaternion.identity, size, new Color(0.07f, 0.09f, 0.13f), out _,
                Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            // Slim floor stand (visual only: no collider, so it never blocks locomotion or rays).
            Box("Stand", root, new Vector3(0f, -(ConsentPanelPosition.y + 0.33f) * 0.5f, 0.03f),
                new Vector3(0.05f, ConsentPanelPosition.y - 0.33f, 0.05f), Lit("Metal_Graphite", new Color(0.2f, 0.21f, 0.23f), 0.6f, 0.7f));

            ScreenRect(s, "HeaderBar", 0f, 0.29f, 0.9f, 0.075f, navy);
            ScreenText(s, "Title", "RESEARCH PARTICIPATION  (OPTIONAL)", 0f, 0.29f, 0.86f, 0.07f, 0.2f, new Color(0.22f, 0.88f, 1f),
                TextAlignmentOptions.Center, FontStyles.Bold);
            TextMeshPro body = ScreenText(s, "Body", ResearchConsentPanel.Notice, -0.43f, 0.235f, 0.86f, 0.3f, 0.22f, ink);
            AutoSize(body, 0.12f, 0.22f);
            TextMeshPro status = ScreenText(s, "Status", "Recording is OFF until you agree.", -0.43f, -0.075f, 0.86f, 0.05f, 0.16f, ink);
            AutoSize(status, 0.1f, 0.16f);

            Transform agreeGroup = Group("AgreeGroup", s, Vector3.zero);
            XRButton agree = OfficeInteractionsBuilder.Button(agreeGroup, "AgreeButton", "research.consent_agree", "I AGREE - RECORD ANONYMOUSLY",
                new Vector2(-0.215f, -0.18f), new Vector2(0.41f, 0.08f), cyan, 0.13f);
            Transform declineGroup = Group("DeclineGroup", s, Vector3.zero);
            XRButton decline = OfficeInteractionsBuilder.Button(declineGroup, "DeclineButton", "research.consent_decline", "NO THANKS - DON'T RECORD",
                new Vector2(0.215f, -0.18f), new Vector2(0.41f, 0.08f), slate, 0.13f);
            Transform withdrawGroup = Group("WithdrawGroup", s, Vector3.zero);
            XRButton withdraw = OfficeInteractionsBuilder.Button(withdrawGroup, "WithdrawButton", "research.withdraw", "WITHDRAW & DELETE MY DATA",
                new Vector2(0f, -0.275f), new Vector2(0.6f, 0.07f), red, 0.13f);

            var panel = root.gameObject.AddComponent<ResearchConsentPanel>();
            OfficeInteractionsBuilder.Inject(panel, ("_body", body), ("_status", status), ("_agreeButton", agree), ("_declineButton", decline),
                ("_withdrawButton", withdraw), ("_agreeGroup", agreeGroup.gameObject), ("_declineGroup", declineGroup.gameObject),
                ("_withdrawGroup", withdrawGroup.gameObject));
            withdrawGroup.gameObject.SetActive(false); // shown once consent is given
        }

        private static void AutoSize(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
            text.textWrappingMode = TextWrappingModes.Normal;
        }

        // =============================================================================================================
        // Dashboard scene
        // =============================================================================================================
        public static void BuildDashboardScene()
        {
            EnsureFolder(Path.GetDirectoryName(DashboardScenePath).Replace('\\', '/'));
            Scene scene;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DashboardScenePath) != null)
            {
                scene = EditorSceneManager.OpenScene(DashboardScenePath, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            var cameraGo = new GameObject("DashboardCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = DashboardUI.Charcoal;
            camera.orthographic = true;
            camera.cullingMask = 0; // the overlay canvas draws everything
            cameraGo.AddComponent<AudioListener>();
            cameraGo.tag = "MainCamera";

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.AddComponent<InputSystemUIInputModule>(); // uses the Input System default UI actions at runtime

            new GameObject("DesktopModeGuard").AddComponent<DesktopModeGuard>();

            var dashboard = new GameObject(DashboardObjectName, typeof(RectTransform));
            var canvas = dashboard.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = dashboard.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            dashboard.AddComponent<GraphicRaycaster>();
            dashboard.AddComponent<ResearchDashboardController>();

            if (!EditorSceneManager.SaveScene(scene, DashboardScenePath)) throw new IOException($"Failed to save {DashboardScenePath}");
        }

        // =============================================================================================================
        // Instructor menus
        // =============================================================================================================
        [MenuItem(Menu + "Open Research Dashboard (Play)", priority = 150)]
        public static void OpenDashboard()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DashboardScenePath) == null) BuildDashboardScene();
            EditorSceneManager.OpenScene(DashboardScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [MenuItem(Menu + "Open Research Data Folder", priority = 151)]
        public static void OpenDataFolder()
        {
            Directory.CreateDirectory(ResearchStore.DefaultRoot);
            EditorUtility.RevealInFinder(ResearchStore.DefaultRoot);
        }

        [MenuItem(Menu + "Export REAL Dataset", priority = 160)]
        private static void ExportReal() => ExportFromMenu(ResearchSchema.Real);

        [MenuItem(Menu + "Export DEMO Dataset", priority = 161)]
        private static void ExportDemo() => ExportFromMenu(ResearchSchema.Demo);

        [MenuItem(Menu + "Export TEST Dataset", priority = 162)]
        private static void ExportTest() => ExportFromMenu(ResearchSchema.Test);

        public static ResearchExporter.Result Export(string dataset)
        {
            var store = new ResearchStore();
            ResearchStore.LoadResult load = store.LoadAll(dataset);
            ResearchExporter.Result result = ResearchExporter.Export(store, dataset, load.Sessions);
            Debug.Log($"[TrustBreach.Research] exported {result.Sessions} {dataset} sessions ({result.Events} events) to {result.Folder}" +
                      (load.Quarantined.Count > 0 ? $"; {load.Quarantined.Count} unreadable file(s) quarantined" : string.Empty));
            return result;
        }

        private static void ExportFromMenu(string dataset)
        {
            ResearchExporter.Result result = Export(dataset);
            EditorUtility.RevealInFinder(result.Folder);
        }

        [MenuItem(Menu + "Generate DEMO Data", priority = 170)]
        private static void GenerateDemo()
        {
            int n = DemoDataGenerator.WriteTo(new ResearchStore());
            Debug.Log($"[TrustBreach.Research] generated {n} synthetic DEMO sessions (seed {DemoDataGenerator.DefaultSeed}) in the demo dataset only.");
        }

        [MenuItem(Menu + "Clear DEMO Data", priority = 171)]
        private static void ClearDemo() => ClearFromMenu(ResearchSchema.Demo);

        [MenuItem(Menu + "Clear TEST Data", priority = 172)]
        private static void ClearTest() => ClearFromMenu(ResearchSchema.Test);

        private static void ClearFromMenu(string dataset)
        {
            if (!EditorUtility.DisplayDialog("TRUST//BREACH research data", $"Delete all {dataset.ToUpperInvariant()} sessions? REAL data is not affected.",
                    "Delete", "Cancel")) return;
            int n = new ResearchStore().ClearSessions(dataset);
            Debug.Log($"[TrustBreach.Research] cleared {n} {dataset} sessions.");
        }

        // =============================================================================================================
        internal static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        internal static Transform Root(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(r => r.name == name)?.transform
            ?? throw new InvalidOperationException($"Scene root '{name}' not found in {scene.path}. Build Phase 3 first.");

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
