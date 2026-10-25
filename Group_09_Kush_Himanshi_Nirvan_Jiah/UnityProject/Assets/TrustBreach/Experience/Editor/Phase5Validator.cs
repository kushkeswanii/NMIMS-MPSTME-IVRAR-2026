using System.Collections.Generic;
using System.Linq;
using TrustBreach.Analytics;
using TrustBreach.Analytics.Editor;
using TrustBreach.Dialogue;
using TrustBreach.Editor;
using TrustBreach.Interactions;
using TrustBreach.Interactions.Scenarios;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using B = TrustBreach.Editor.TrustBreachCorporateOfficeBuilder;
using P3 = TrustBreach.Editor.Phase3AdversaryBuilder;
using P4 = TrustBreach.Analytics.Editor.Phase4AnalyticsBuilder;
using P5 = TrustBreach.Experience.Editor.Phase5ExperienceBuilder;

namespace TrustBreach.Experience.Editor
{
    /// <summary>
    /// Phase 5 static validation (read-only): guide panel and progress strip wiring, placement, hit testing and comfort rules,
    /// scene integrity (one camera rig, one AudioListener, no EventSystem in the XR scenes, no missing scripts),
    /// and the full Phase 4 validator (which includes the Phase 2, 2.5 and 3 validators) as regression.
    /// </summary>
    public static class Phase5Validator
    {
        [MenuItem("Tools/TRUST∕∕BREACH/Experience/Validate Phase 5 (includes Phases 2-4)", priority = 181)]
        private static void ValidateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            XRFoundationValidator.Report report = ValidateAll();
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        public static XRFoundationValidator.Report ValidateAll()
        {
            XRFoundationValidator.Report report = ValidateScenes();
            report.Merge(Phase4Validator.ValidateAll());
            return report;
        }

        public static XRFoundationValidator.Report ValidateScenes()
        {
            var report = new XRFoundationValidator.Report { Title = "TrustBreach Phase 5 participant flow validation" };
            foreach ((string path, EncounterMode mode, string dataset) in new[]
                     { (P3.ScenePath, EncounterMode.Participant, ResearchSchema.Real), (P3.DemoScenePath, EncounterMode.Demo, ResearchSchema.Test) })
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var sceneReport = ValidateScene(scene, mode, dataset);
                foreach (List<string> list in new[] { sceneReport.Passed, sceneReport.Failed, sceneReport.Warnings })
                {
                    for (int i = 0; i < list.Count; i++) list[i] = $"[{scene.name}] {list[i]}";
                }
                report.Merge(sceneReport);
            }
            return report;
        }

        public static XRFoundationValidator.Report ValidateScene(Scene scene, EncounterMode mode, string dataset)
        {
            var report = new XRFoundationValidator.Report();
            GameObject[] roots = scene.GetRootGameObjects();
            List<Transform> all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

            // Scene integrity.
            report.Check(all.Sum(t => t.GetComponents<Component>().Count(c => c == null)) == 0, "No missing script references");
            report.Check(all.Count(t => t.GetComponent<OVRCameraRig>() != null) == 1, "Exactly one OVRCameraRig");
            report.Check(all.Count(t => t.GetComponent<Camera>() != null && t.GetComponent<Camera>().enabled && t.gameObject.activeInHierarchy) == 1,
                "Exactly one active camera (the rig's centre eye)");
            report.Check(all.Count(t => t.GetComponent<AudioListener>() != null && t.gameObject.activeInHierarchy) == 1, "Exactly one active AudioListener");
            report.Check(all.Count(t => t.GetComponent<UnityEngine.EventSystems.EventSystem>() != null) == 0,
                "No uGUI EventSystem in the XR scene (all XR UI uses Interaction SDK rays)");
            EncounterDirector[] directors = all.Select(t => t.GetComponent<EncounterDirector>()).Where(d => d != null).ToArray();
            report.Check(directors.Length == 1 && directors[0].Mode == mode, $"One EncounterDirector in {mode} mode");
            ResearchEventCollector[] collectors = all.Select(t => t.GetComponent<ResearchEventCollector>()).Where(c => c != null).ToArray();
            report.Check(collectors.Length == 1 && new SerializedObject(collectors[0]).FindProperty("_dataset").stringValue == dataset,
                $"One research collector writing to the '{dataset}' dataset");

            // Guide panel.
            Transform ui = P5.Root(scene, B.UIRootName);
            ExperienceGuidePanel[] guides = all.Select(t => t.GetComponent<ExperienceGuidePanel>()).Where(g => g != null).ToArray();
            report.Check(guides.Length == 1 && guides[0].transform.parent == ui && guides[0].name == P5.GuidePanelName,
                "Exactly one guide panel at UI/ExperienceGuidePanel", $"{guides.Length} found");
            if (guides.Length == 1) ValidateGuide(report, scene, guides[0], directors.FirstOrDefault());

            ExperienceProgressStrip[] strips = all.Select(t => t.GetComponent<ExperienceProgressStrip>()).Where(g => g != null).ToArray();
            report.Check(strips.Length == 1 && strips[0].transform.parent == ui && strips[0].name == P5.ProgressStripName,
                "Exactly one progress strip at UI/ExperienceProgressStrip", $"{strips.Length} found");
            if (strips.Length == 1)
            {
                List<string> missing = OfficeInteractionsValidator.UnassignedReferences(strips[0]);
                report.Check(missing.Count == 0, "Progress strip references assigned", string.Join(", ", missing));
                var so = new SerializedObject(strips[0]);
                report.Check(so.FindProperty("_segments").arraySize == ExperienceFlow.StageCount, "Progress strip has one segment per stage");
                report.Check(so.FindProperty("_hideNear").arraySize == 3, "Progress strip hides near the guide, Alex's and the research panels");
                report.Check(strips[0].GetComponentsInChildren<Collider>(true).Length == 0, "Progress strip has no colliders (never blocks rays or movement)");
            }
            return report;
        }

        private static void ValidateGuide(XRFoundationValidator.Report report, Scene scene, ExperienceGuidePanel guide, EncounterDirector director)
        {
            List<string> missing = OfficeInteractionsValidator.UnassignedReferences(guide);
            report.Check(missing.Count == 0, "Guide panel references assigned", string.Join(", ", missing));
            var so = new SerializedObject(guide);
            report.Check(so.FindProperty("_director").objectReferenceValue == director, "Guide panel reads the scene's EncounterDirector");
            SerializedProperty scenarios = so.FindProperty("_scenarios");
            var names = Enumerable.Range(0, scenarios.arraySize).Select(i => scenarios.GetArrayElementAtIndex(i).objectReferenceValue as ScenarioController)
                .Where(s => s != null).Select(s => s.GetType().Name).ToList();
            report.Check(names.SequenceEqual(P5.ScenarioOrder), "Guide checklist covers the four Phase 2.5 scenarios", string.Join(", ", names));
            report.Check(so.FindProperty("_segments").arraySize == ExperienceFlow.StageCount, "Guide progress bar has one segment per stage");

            List<XRButton> buttons = guide.GetComponentsInChildren<XRButton>(true).ToList();
            report.Check(buttons.Select(b => b.ButtonId).OrderBy(s => s).SequenceEqual(new[] { "experience.guide_exit", "experience.guide_primary" }),
                "Guide has PRIMARY and EXIT XR buttons", string.Join(", ", buttons.Select(b => b.ButtonId)));
            OfficeInteractionsValidator.ValidateHitTesting(report, guide.transform, buttons);

            Transform t = guide.transform;
            Vector3 toUser = P4.ParticipantSpawn - t.position;
            toUser.y = 0f;
            report.Check(toUser.magnitude > 1.2f && toUser.magnitude < 2.5f, "Guide panel within reach of the spawn point (1.2-2.5 m)", $"{toUser.magnitude:F2} m");
            report.Check(Vector3.Dot(-t.forward, toUser.normalized) > 0.95f, "Guide panel faces the spawn point");
            report.Check(t.position.y > 1.3f && t.position.y < 1.6f, "Guide panel centre at standing eye height", $"{t.position.y:F2} m");

            Bounds own = Encapsulate(guide.GetComponentsInChildren<Renderer>(true));
            List<string> overlaps = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                .Where(r => !r.transform.IsChildOf(t) && r.enabled && r.GetComponentInParent<ExperienceProgressStrip>() == null
                            && r.bounds.Intersects(own) && r.bounds.size.y < 4f && r.bounds.max.y > 0.05f)
                .Select(r => r.name).Distinct().ToList();
            report.Check(overlaps.Count == 0, "Guide panel and stand do not intersect other scene geometry", string.Join(", ", overlaps.Take(8)));
            Collider[] blocking = guide.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && c.GetComponent<XRButton>() == null).ToArray();
            report.Check(blocking.Length == 0, "Guide panel adds no blocking colliders (buttons only)", string.Join(", ", blocking.Select(c => c.name)));

            // The guide and the research panel are seen from the spawn in different directions (no overlapping floating UI).
            Transform consent = P5.Root(scene, B.UIRootName).Find(P4.ConsentPanelName);
            if (consent != null)
            {
                Vector3 spawnEye = P4.ParticipantSpawn + Vector3.up * 1.6f;
                float apart = Vector3.Angle(Flat(t.position - spawnEye), Flat(consent.position - spawnEye));
                report.Check(apart > 50f, "Guide and research panels are well separated as seen from the spawn", $"{apart:F0} degrees");
            }
            report.Check(Vector3.Distance(Flat(t.position), Flat(P3.NpcPosition)) > 3f, "Guide panel well away from Alex Morgan");
            Transform totem = P5.Root(scene, B.UIRootName).Find("OrientationTotem");
            if (totem != null)
            {
                Vector3 eye = P4.ParticipantSpawn + Vector3.up * 1.6f;
                float guideRight = YawRange(eye, guide.transform.Find("Screen").GetComponentsInChildren<Renderer>(true)).y;
                float totemLeft = YawRange(eye, totem.GetComponentsInChildren<Renderer>(true)).x;
                report.Check(guideRight < totemLeft, "Guide panel does not cover the orientation totem as seen from the spawn", $"guide to {guideRight:F0}, totem from {totemLeft:F0} degrees");
            }
            // Keep clear of the Phase 2 walking route's first leg (spawn straight ahead to the reception, 0.5 m capsule).
            report.Check(own.min.x > 0.3f, "Guide panel clear of the walking route from the spawn", $"min x {own.min.x:F2} m");
        }

        private static Bounds Encapsulate(IEnumerable<Renderer> renderers)
        {
            Bounds? bounds = null;
            foreach (Renderer r in renderers)
            {
                if (bounds == null) bounds = r.bounds;
                else { Bounds b = bounds.Value; b.Encapsulate(r.bounds); bounds = b; }
            }
            return bounds ?? new Bounds();
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Min / max yaw (degrees, +Z = 0, right positive) of the renderers' bounds corners seen from <paramref name="eye"/>.</summary>
        private static Vector2 YawRange(Vector3 eye, IEnumerable<Renderer> renderers)
        {
            float min = 999f, max = -999f;
            foreach (Renderer r in renderers)
            {
                Bounds b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, 0f, (i & 4) == 0 ? b.min.z : b.max.z) - eye;
                    float yaw = Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg;
                    min = Mathf.Min(min, yaw);
                    max = Mathf.Max(max, yaw);
                }
            }
            return new Vector2(min, max);
        }
    }
}
