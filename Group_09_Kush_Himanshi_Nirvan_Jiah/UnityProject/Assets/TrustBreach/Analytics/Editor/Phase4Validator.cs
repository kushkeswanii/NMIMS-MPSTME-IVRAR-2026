using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using B = TrustBreach.Editor.TrustBreachCorporateOfficeBuilder;
using P3 = TrustBreach.Editor.Phase3AdversaryBuilder;
using P4 = TrustBreach.Analytics.Editor.Phase4AnalyticsBuilder;

namespace TrustBreach.Analytics.Editor
{
    /// <summary>Static checks for the Phase 4 research layer, plus Phase 2-3 regression validators on the same scenes.</summary>
    public static class Phase4Validator
    {
        private static readonly string[] ForbiddenFieldWords = { "password", "participant", "email", "audio", "voice", "transcript", "freetext", "location", "device" };

        [MenuItem("Tools/TRUST∕∕BREACH/Research/Validate Phase 4 Analytics", priority = 141)]
        private static void ValidateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            XRFoundationValidator.Report report = ValidateAll();
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        public static XRFoundationValidator.Report ValidateAll()
        {
            var report = new XRFoundationValidator.Report { Title = "TrustBreach Phase 4 research analytics validation" };
            foreach ((string path, string dataset) in new[] { (P3.ScenePath, ResearchSchema.Real), (P3.DemoScenePath, ResearchSchema.Test) })
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                XRFoundationValidator.Report sceneReport = ValidatePhase3Scene(scene, dataset);
                Prefix(sceneReport, scene.name);
                report.Merge(sceneReport);
            }
            Scene dashboard = EditorSceneManager.OpenScene(P4.DashboardScenePath, OpenSceneMode.Single);
            report.Merge(ValidateDashboardScene(dashboard));
            report.Merge(ValidateSchemaPrivacy());
            return report;
        }

        public static XRFoundationValidator.Report ValidatePhase3Scene(Scene scene, string dataset)
        {
            var report = new XRFoundationValidator.Report();
            GameObject[] roots = scene.GetRootGameObjects();
            string[] expected = { B.XRRigRootName, B.EnvironmentRootName, B.ScenarioRootName, B.LightingRootName, B.ManagersRootName, B.UIRootName };
            report.Check(roots.Length == expected.Length && expected.All(n => roots.Any(r => r.name == n)), "Scene roots unchanged (6 Phase 2 roots)",
                string.Join(", ", roots.Select(r => r.name)));

            Transform managers = P4.Root(scene, B.ManagersRootName);
            ResearchEventCollector[] collectors = roots.SelectMany(r => r.GetComponentsInChildren<ResearchEventCollector>(true)).ToArray();
            report.Check(collectors.Length == 1 && collectors[0].transform.parent == managers && collectors[0].name == P4.AnalyticsRootName,
                "Exactly one ResearchEventCollector at Managers/ResearchAnalytics", $"{collectors.Length} found");
            if (collectors.Length == 1)
            {
                string configured = new SerializedObject(collectors[0]).FindProperty("_dataset").stringValue;
                report.Check(configured == dataset, $"Collector dataset is '{dataset}'", configured);
            }

            ResearchConsentPanel[] panels = roots.SelectMany(r => r.GetComponentsInChildren<ResearchConsentPanel>(true)).ToArray();
            report.Check(panels.Length == 1 && panels[0].transform.parent == P4.Root(scene, B.UIRootName) && panels[0].name == P4.ConsentPanelName,
                "Exactly one consent panel at UI/ResearchConsentPanel", $"{panels.Length} found");
            if (panels.Length == 1) ValidateConsentPanel(report, scene, panels[0]);

            // Regression: Phase 2, 2.5 and 3 validators still pass on the same scene.
            report.Merge(Phase3Validator.Validate(scene));
            report.Merge(CorporateOfficeValidator.Validate(scene));
            report.Merge(OfficeInteractionsValidator.Validate(scene));
            return report;
        }

        private static void ValidateConsentPanel(XRFoundationValidator.Report report, Scene scene, ResearchConsentPanel panel)
        {
            List<string> missing = OfficeInteractionsValidator.UnassignedReferences(panel);
            report.Check(missing.Count == 0, "Consent panel references assigned", string.Join(", ", missing));
            List<XRButton> buttons = panel.GetComponentsInChildren<XRButton>(true).ToList();
            report.Check(buttons.Select(b => b.ButtonId).OrderBy(s => s).SequenceEqual(new[] { "research.consent_agree", "research.consent_decline", "research.withdraw" }),
                "Consent panel has AGREE / DECLINE / WITHDRAW XR buttons", string.Join(", ", buttons.Select(b => b.ButtonId)));
            OfficeInteractionsValidator.ValidateHitTesting(report, panel.transform, buttons);

            Transform t = panel.transform;
            Vector3 toUser = P4.ParticipantSpawn - t.position;
            toUser.y = 0f;
            float distance = toUser.magnitude;
            report.Check(distance > 1.2f && distance < 2.5f, "Consent panel within reach of the spawn point (1.2-2.5 m)", $"{distance:F2} m");
            report.Check(Vector3.Dot(-t.forward, toUser.normalized) > 0.95f, "Consent panel faces the spawn point");
            report.Check(t.position.y > 1.3f && t.position.y < 1.6f, "Consent panel centre at standing eye height", $"{t.position.y:F2} m");

            // Off the route: nothing else in the scene overlaps the panel volume, and it is away from Alex and his panel.
            Bounds own = Encapsulate(panel.GetComponentsInChildren<Renderer>(true));
            List<string> overlaps = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                .Where(r => !r.transform.IsChildOf(t) && r.enabled && r.bounds.Intersects(own) && r.bounds.size.y < 4f && r.bounds.max.y > 0.05f)
                .Select(r => r.name).Distinct().ToList();
            report.Check(overlaps.Count == 0, "Consent panel and stand do not intersect other scene geometry", string.Join(", ", overlaps.Take(8)));
            float toAlex = Vector3.Distance(Flat(t.position), Flat(P3.NpcPosition));
            report.Check(toAlex > 3f, "Consent panel well away from Alex Morgan", $"{toAlex:F1} m");
            Collider[] blocking = panel.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && c.GetComponent<XRButton>() == null).ToArray();
            report.Check(blocking.Length == 0, "Consent panel adds no blocking colliders (buttons only)", string.Join(", ", blocking.Select(c => c.name)));
        }

        public static XRFoundationValidator.Report ValidateDashboardScene(Scene scene)
        {
            var report = new XRFoundationValidator.Report();
            List<GameObject> objects = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(x => x.gameObject).ToList();
            T Find<T>() where T : Component => objects.Select(o => o.GetComponent<T>()).FirstOrDefault(c => c != null);

            Camera camera = Find<Camera>();
            report.Check(camera != null && camera.CompareTag("MainCamera"), "Dashboard: desktop camera present");
            report.Check(Find<AudioListener>() != null, "Dashboard: one AudioListener");
            EventSystem events = Find<EventSystem>();
            report.Check(events != null && events.GetComponent<InputSystemUIInputModule>() != null, "Dashboard: EventSystem with InputSystemUIInputModule");
            Canvas canvas = Find<Canvas>();
            report.Check(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay, "Dashboard: Screen Space Overlay canvas");
            var scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            report.Check(scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize && scaler.referenceResolution == new Vector2(1920, 1080),
                "Dashboard: canvas scales from a 1920x1080 reference");
            report.Check(canvas != null && canvas.GetComponent<GraphicRaycaster>() != null && canvas.GetComponent<ResearchDashboardController>() != null,
                "Dashboard: GraphicRaycaster + ResearchDashboardController on the canvas");
            report.Check(Find<DesktopModeGuard>() != null, "Dashboard: DesktopModeGuard stops XR for the desktop view");
            report.Check(objects.All(o => !o.name.StartsWith("OVR") && o.GetComponent<OVRManager>() == null), "Dashboard: no XR rig in the dashboard scene");
            report.Check(EditorBuildSettings.scenes.All(s => s.path != P4.DashboardScenePath),
                "Dashboard scene is an instructor desktop tool (not in the Quest build list)");
            return report;
        }

        /// <summary>The stored schema has no field that could hold names, credentials, free text, audio or device identity.</summary>
        public static XRFoundationValidator.Report ValidateSchemaPrivacy()
        {
            var report = new XRFoundationValidator.Report();
            var offending = new List<string>();
            foreach (Type type in new[] { typeof(SessionRecord), typeof(ResearchEvent), typeof(ConsentInfo), typeof(SessionSummary) })
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    string lower = field.Name.ToLowerInvariant();
                    if (ForbiddenFieldWords.Any(w => lower.Contains(w))) offending.Add($"{type.Name}.{field.Name}");
                }
            }
            report.Check(offending.Count == 0, "Research schema has no personal-data fields (names, passwords, email, audio, voice, location, device id)",
                string.Join(", ", offending));
            return report;
        }

        private static void Prefix(XRFoundationValidator.Report report, string prefix)
        {
            for (int i = 0; i < report.Passed.Count; i++) report.Passed[i] = $"[{prefix}] {report.Passed[i]}";
            for (int i = 0; i < report.Failed.Count; i++) report.Failed[i] = $"[{prefix}] {report.Failed[i]}";
        }

        private static Bounds Encapsulate(IEnumerable<Renderer> renderers)
        {
            Bounds? bounds = null;
            foreach (Renderer r in renderers)
            {
                if (bounds == null) bounds = r.bounds;
                else
                {
                    Bounds b = bounds.Value;
                    b.Encapsulate(r.bounds);
                    bounds = b;
                }
            }
            return bounds ?? new Bounds();
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
