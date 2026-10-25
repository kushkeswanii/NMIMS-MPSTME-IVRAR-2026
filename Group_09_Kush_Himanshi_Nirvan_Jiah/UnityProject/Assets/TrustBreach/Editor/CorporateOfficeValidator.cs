using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using TMPro;
using TrustBreach.Core;
using TrustBreach.Interactions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using B = TrustBreach.Editor.TrustBreachCorporateOfficeBuilder;

namespace TrustBreach.Editor
{
    /// <summary>
    /// Edit-time validation of the corporate office: XR rig integrity (shared with Phase 1), hierarchy, scenario
    /// artifacts and their interactions, line of sight to every clue, and a walkable route through all zones.
    /// </summary>
    public static class CorporateOfficeValidator
    {
        private const float CapsuleRadius = 0.25f;

        /// <summary>Artifact id -> standing eye position from which it should be clearly visible.</summary>
        private static readonly Dictionary<string, Vector3> Viewpoints = new Dictionary<string, Vector3>
        {
            { B.SpoofedVisitorBadge, new Vector3(-0.9f, 1.6f, 1.3f) },
            { B.MaliciousUSBDrive, new Vector3(5.5f, 1.6f, 5.7f) },
            { B.EmployeeBadgeReference, new Vector3(5.5f, 1.6f, 7.5f) },
            { B.PhishingEmailWorkstation, new Vector3(-0.88f, 1.5f, 5.4f) },
            { B.PasswordStickyNote, new Vector3(-0.88f, 1.5f, 5.4f) },
            { B.UnattendedConfidentialPrintout, new Vector3(-7.4f, 1.6f, 4.75f) },
            { B.FakeSSLTerminal, new Vector3(4.2f, 1.6f, 8.05f) },
        };

        /// <summary>Floor route visiting every zone (spawn -> lobby -> gates -> workspace -> IT -> kiosk -> meeting).</summary>
        private static readonly Vector2[] Route =
        {
            new Vector2(0f, -1f), new Vector2(0f, 1.0f), new Vector2(2.6f, 1.2f), new Vector2(2.81f, 2.6f), new Vector2(2.81f, 4.0f),
            new Vector2(2.81f, 5.2f), new Vector2(3.4f, 5.2f),
            new Vector2(1.0f, 5.0f), new Vector2(-0.9f, 5.1f), new Vector2(1.0f, 5.0f), new Vector2(3.5f, 6.0f), new Vector2(5.6f, 6.5f),
            new Vector2(4.6f, 7.8f), new Vector2(3.5f, 10.85f), new Vector2(2.0f, 10.85f), new Vector2(1.0f, 10.85f), new Vector2(-2.4f, 10.3f),
            new Vector2(-6.6f, 10.25f)
        };

        [MenuItem("Tools/TRUST∕∕BREACH/Validate Corporate Office Scene", priority = 101)]
        [MenuItem("TrustBreach/Phase 2/Validate Corporate Office Scene", priority = 101)]
        private static void ValidateFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != B.ScenePath)
            {
                scene = EditorSceneManager.OpenScene(B.ScenePath, OpenSceneMode.Single);
            }
            XRFoundationValidator.Report report = Validate(scene);
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        public static XRFoundationValidator.Report Validate(Scene scene)
        {
            var report = new XRFoundationValidator.Report { Title = "TrustBreach Corporate Office validation" };
            report.Check(scene.IsValid() && scene.isLoaded, "Office scene is loaded");
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return report;
            }
            Physics.SyncTransforms();

            GameObject[] roots = scene.GetRootGameObjects();
            List<GameObject> objects = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();

            ValidateHierarchy(report, roots);
            XRFoundationValidator.ValidateRig(report, objects);
            XRFoundationValidator.ValidateCameraAndAudio(report, objects);
            XRFoundationValidator.ValidateControllers(report, objects);
            ValidateArtifacts(report, objects);
            ValidateRoute(report, roots.FirstOrDefault(r => r.name == B.XRRigRootName)?.transform);
            ValidatePresentation(report, objects, roots);
            XRFoundationValidator.ValidateSerializedReferences(report, objects);
            return report;
        }

        private static void ValidateHierarchy(XRFoundationValidator.Report report, GameObject[] roots)
        {
            string[] expected = { B.XRRigRootName, B.EnvironmentRootName, B.ScenarioRootName, B.LightingRootName, B.ManagersRootName, B.UIRootName };
            List<string> rootNames = roots.Select(r => r.name).ToList();
            report.Check(expected.All(rootNames.Contains) && rootNames.Count == expected.Length,
                "Scene roots: XR_Rig_Root, Environment_Office, Scenario_Objects, Lighting, Managers, UI", string.Join(", ", rootNames));

            GameObject xrRoot = roots.FirstOrDefault(r => r.name == B.XRRigRootName);
            report.Check(xrRoot != null && xrRoot.GetComponentInChildren<OVRCameraRig>(true) != null, "OVRCameraRig lives under XR_Rig_Root");
            if (xrRoot != null)
            {
                report.Check(Vector3.Distance(xrRoot.transform.position, B.SpawnPosition) < 0.01f, "XR rig spawns at the entrance");
            }

            GameObject env = roots.FirstOrDefault(r => r.name == B.EnvironmentRootName);
            List<string> zones = env == null ? new List<string>() : env.transform.Cast<Transform>().Select(t => t.name).ToList();
            report.Check(B.ZoneNames.All(zones.Contains), "Environment zones: Reception, Workstations, ITSecurityDesk, MeetingArea, Hallway",
                string.Join(", ", zones));
        }

        private static void ValidateArtifacts(XRFoundationValidator.Report report, List<GameObject> objects)
        {
            List<ScenarioArtifact> artifacts = objects.SelectMany(o => o.GetComponents<ScenarioArtifact>()).ToList();
            List<string> ids = artifacts.Select(a => a.ArtifactId).ToList();
            report.Check(ids.Count == ids.Distinct().Count(), "Scenario artifact ids are unique", string.Join(", ", ids));

            string[] required = { B.SpoofedVisitorBadge, B.MaliciousUSBDrive, B.PhishingEmailWorkstation, B.FakeSSLTerminal };
            foreach (string id in required)
            {
                ScenarioArtifact a = artifacts.FirstOrDefault(x => x.ArtifactId == id);
                report.Check(a != null && a.transform.parent != null && a.transform.parent.name == B.ScenarioRootName && a.gameObject.activeInHierarchy,
                    $"Required artifact '{id}' exists under Scenario_Objects");
            }

            foreach (string id in new[] { B.SpoofedVisitorBadge, B.MaliciousUSBDrive, B.UnattendedConfidentialPrintout, B.EmployeeBadgeReference })
            {
                GameObject go = artifacts.FirstOrDefault(x => x.ArtifactId == id)?.gameObject;
                bool grabbable = go != null && go.GetComponent<Grabbable>() != null && go.GetComponent<Rigidbody>() != null &&
                    go.GetComponentInChildren<GrabInteractable>(true) != null && go.GetComponentInChildren<HandGrabInteractable>(true) != null &&
                    go.GetComponentInChildren<RayInteractable>(true) != null && go.GetComponent<PointableEventRelay>() != null;
                report.Check(grabbable, $"'{id}' is grabbable (controller, hand, ray) and reports events");
                if (go != null)
                {
                    report.Check(IsSupported(go), $"'{id}' rests on a surface (not floating or sunk)");
                }
            }

            foreach (string id in new[] { B.PhishingEmailWorkstation, B.FakeSSLTerminal, B.PasswordStickyNote, B.PhishingLinkButton, B.FakeSSLSignInButton })
            {
                PointableEventRelay relay = objects.SelectMany(o => o.GetComponents<PointableEventRelay>()).FirstOrDefault(r => r.TargetId == id);
                report.Check(relay != null && relay.GetComponentInChildren<RayInteractable>(true) != null, $"'{id}' is a ray target with event relay");
            }

            foreach (KeyValuePair<string, Vector3> view in Viewpoints)
            {
                ScenarioArtifact a = artifacts.FirstOrDefault(x => x.ArtifactId == view.Key);
                if (a == null)
                {
                    report.Check(false, $"'{view.Key}' visible from its approach point", "artifact missing");
                    continue;
                }
                Collider gaze = a.GazeCollider;
                Vector3 target = gaze != null ? gaze.bounds.center : a.transform.position;
                bool clear = HasLineOfSight(view.Value, target, a.transform);
                float distance = Vector3.Distance(view.Value, target);
                report.Check(clear && distance < 2.5f, $"'{view.Key}' visible from its approach point",
                    $"clear={clear}, distance={distance:F2} m");
            }

            int suspicious = artifacts.Count(a => a.IsSuspicious);
            report.Check(suspicious >= 4, "At least four suspicious artifacts placed", suspicious.ToString());
            report.Check(artifacts.All(a => !string.IsNullOrWhiteSpace(a.RedFlags)), "Every artifact documents its red flags");
            int anchors = objects.Count(o => o.transform.parent != null && o.transform.parent.name == "NPC_Anchors");
            report.Check(anchors >= 3, "NPC anchors for future dialogue placed", anchors.ToString());
        }

        private static bool IsSupported(GameObject go)
        {
            Collider own = go.GetComponent<Collider>();
            Vector3 origin = (own != null ? own.bounds.center : go.transform.position) + Vector3.up * 0.05f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 0.3f, ~0, QueryTriggerInteraction.Ignore);
            float bottom = own != null ? own.bounds.min.y : go.transform.position.y;
            RaycastHit? support = hits.Where(h => !h.collider.transform.IsChildOf(go.transform)).OrderBy(h => h.distance).Cast<RaycastHit?>().FirstOrDefault();
            if (support == null)
            {
                return false;
            }
            // Root colliders are padded for easier grabbing, so allow a little slack either way.
            return Mathf.Abs(support.Value.point.y - bottom) < 0.03f || (support.Value.point.y > bottom && support.Value.point.y - bottom < 0.03f);
        }

        private static bool HasLineOfSight(Vector3 eye, Vector3 target, Transform self)
        {
            Vector3 dir = target - eye;
            RaycastHit[] hits = Physics.RaycastAll(eye, dir.normalized, dir.magnitude, ~0, QueryTriggerInteraction.Ignore);
            return hits.All(h => h.collider.transform.IsChildOf(self) || Vector3.Distance(h.point, target) < 0.08f);
        }

        /// <summary>Capsule sweep along the route; the player's own rig colliders are ignored.</summary>
        private static void ValidateRoute(XRFoundationValidator.Report report, Transform xrRoot)
        {
            bool Blocks(Collider c) => xrRoot == null || !c.transform.IsChildOf(xrRoot);
            var blocked = new List<string>();
            for (int i = 0; i < Route.Length; i++)
            {
                Vector3 p = new Vector3(Route[i].x, 0f, Route[i].y);
                Vector3 bottom = p + Vector3.up * (CapsuleRadius + 0.08f), top = p + Vector3.up * 1.6f;
                Collider overlap = Physics.OverlapCapsule(bottom, top, CapsuleRadius, ~0, QueryTriggerInteraction.Ignore).FirstOrDefault(Blocks);
                if (overlap != null)
                {
                    blocked.Add($"waypoint {i} {Route[i]} overlaps {overlap.name}");
                }
                if (i == 0)
                {
                    continue;
                }
                Vector3 prev = new Vector3(Route[i - 1].x, 0f, Route[i - 1].y);
                Vector3 delta = p - prev;
                RaycastHit hit = Physics.CapsuleCastAll(prev + Vector3.up * (CapsuleRadius + 0.08f), prev + Vector3.up * 1.6f, CapsuleRadius,
                        delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore).FirstOrDefault(h => Blocks(h.collider));
                if (hit.collider != null)
                {
                    blocked.Add($"segment {i - 1}->{i} hits {hit.collider.name}");
                }
                if (!Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, 1f))
                {
                    blocked.Add($"no floor at {Route[i]}");
                }
            }
            report.Check(blocked.Count == 0, "Walkable route connects all five zones (0.5 m capsule)", string.Join("; ", blocked));
        }

        private static void ValidatePresentation(XRFoundationValidator.Report report, List<GameObject> objects, GameObject[] roots)
        {
            List<TMP_Text> texts = objects.SelectMany(o => o.GetComponents<TMP_Text>()).ToList();
            report.Check(texts.Count > 0 && texts.All(t => t.font != null && !string.IsNullOrEmpty(t.text)), "All TextMeshPro labels have a font and text",
                $"{texts.Count} labels");
            report.Check(texts.All(t => !GameObjectUtility.AreStaticEditorFlagsSet(t.gameObject, StaticEditorFlags.BatchingStatic)),
                "No TextMeshPro label is static-batched");

            List<Renderer> renderers = objects.SelectMany(o => o.GetComponents<Renderer>()).ToList();
            List<Renderer> missingMaterial = renderers.Where(r => r.sharedMaterials.Any(m => m == null)).ToList();
            report.Check(missingMaterial.Count == 0, "Every renderer has materials", string.Join(", ", missingMaterial.Take(10).Select(r => r.name)));
            if (renderers.Count > 2500)
            {
                report.Warnings.Add($"{renderers.Count} renderers - consider mesh combining before Quest profiling");
            }

            GameObject lighting = roots.FirstOrDefault(r => r.name == B.LightingRootName);
            Light[] lights = lighting != null ? lighting.GetComponentsInChildren<Light>() : new Light[0];
            report.Check(lights.Count(l => l.type == LightType.Directional && l.shadows != LightShadows.None) == 1,
                "One shadow-casting directional light");
            report.Check(lights.Count(l => l.type != LightType.Directional && l.shadows == LightShadows.None) >= 5,
                "Zone fill lights present (no extra shadow cost)", lights.Length.ToString());

            GameObject env = roots.FirstOrDefault(r => r.name == B.EnvironmentRootName);
            int staticCount = env == null ? 0 : env.GetComponentsInChildren<Renderer>()
                .Count(r => GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic));
            report.Check(staticCount > 100, "Environment geometry marked static for batching", staticCount.ToString());
            report.Passed.Add($"Scene stats: {objects.Count} GameObjects, {renderers.Count} renderers, {texts.Count} text labels, {lights.Length} lights");
        }
    }
}
