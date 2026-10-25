using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using Oculus.Interaction.Surfaces;
using TrustBreach.Interactions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TrustBreach.Editor
{
    /// <summary>
    /// Static (edit-time) validation of the Phase 1 XR foundation scene. Shared by the scene builder,
    /// the TrustBreach menu and the EditMode test suite so all three report identical results.
    /// </summary>
    public static class XRFoundationValidator
    {
        public sealed class Report
        {
            public readonly List<string> Passed = new List<string>();
            public readonly List<string> Failed = new List<string>();
            public readonly List<string> Warnings = new List<string>();

            public string Title = "TrustBreach XR Foundation validation";

            public bool Success => Failed.Count == 0;

            public void Check(bool condition, string description, string detail = null)
            {
                if (condition)
                {
                    Passed.Add(description);
                }
                else
                {
                    Failed.Add(string.IsNullOrEmpty(detail) ? description : $"{description} -> {detail}");
                }
            }

            public void Merge(Report other)
            {
                Passed.AddRange(other.Passed);
                Failed.AddRange(other.Failed);
                Warnings.AddRange(other.Warnings);
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{Title}: {(Success ? "PASS" : "FAIL")} " +
                    $"({Passed.Count} passed, {Failed.Count} failed, {Warnings.Count} warnings)");
                foreach (string p in Passed) sb.AppendLine($"  [PASS] {p}");
                foreach (string f in Failed) sb.AppendLine($"  [FAIL] {f}");
                foreach (string w in Warnings) sb.AppendLine($"  [WARN] {w}");
                return sb.ToString();
            }
        }

        // Approximate standing eye position used for the "visible from spawn" check.
        private static readonly Vector3 ExpectedEyePosition = new Vector3(0f, 1.6f, 0f);

        [MenuItem("TrustBreach/Phase 1/Validate XR Foundation Scene", priority = 20)]
        private static void ValidateFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != XRFoundationSceneBuilder.ScenePath)
            {
                scene = EditorSceneManager.OpenScene(XRFoundationSceneBuilder.ScenePath, OpenSceneMode.Single);
            }
            Report report = Validate(scene);
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        public static Report Validate(Scene scene)
        {
            var report = new Report();
            report.Check(scene.IsValid() && scene.isLoaded, "Scene is loaded");
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return report;
            }

            List<GameObject> objects = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject)
                .ToList();

            ValidateRig(report, objects);
            ValidateCameraAndAudio(report, objects);
            ValidateControllers(report, objects);
            ValidateEnvironment(report, objects);
            ValidateTestCube(report, objects);
            ValidateRayTarget(report, objects);
            ValidateSerializedReferences(report, objects);
            return report;
        }

        internal static void ValidateRig(Report report, List<GameObject> objects)
        {
            int cameraRigs = Count<OVRCameraRig>(objects);
            report.Check(cameraRigs == 1, "Exactly one OVRCameraRig", $"found {cameraRigs}");
            int managers = Count<OVRManager>(objects);
            report.Check(managers == 1, "Exactly one OVRManager", $"found {managers}");
            int interactionRigs = Count<OVRCameraRigRef>(objects);
            report.Check(interactionRigs == 1, "Exactly one Interaction SDK rig (OVRCameraRigRef)", $"found {interactionRigs}");

            // Guard against a second, competing rig architecture (Unity XR Origin / XRI, legacy XRRig).
            string[] competingRigTypes = { "XROrigin", "XRRig", "XRInteractionManager" };
            List<string> competing = objects
                .SelectMany(o => o.GetComponents<Component>())
                .Where(c => c != null && competingRigTypes.Contains(c.GetType().Name))
                .Select(c => $"{c.gameObject.name} ({c.GetType().Name})")
                .ToList();
            report.Check(competing.Count == 0, "No competing XR Origin / XRI rig components", string.Join(", ", competing));

            OVRManager manager = objects.Select(o => o.GetComponent<OVRManager>()).FirstOrDefault(m => m != null);
            if (manager != null)
            {
                report.Check(manager.trackingOriginType == OVRManager.TrackingOrigin.FloorLevel,
                    "OVRManager tracking origin is FloorLevel", manager.trackingOriginType.ToString());
            }
        }

        internal static void ValidateCameraAndAudio(Report report, List<GameObject> objects)
        {
            List<Camera> cameras = objects
                .Where(o => o.activeInHierarchy)
                .SelectMany(o => o.GetComponents<Camera>())
                .Where(c => c.enabled)
                .ToList();
            report.Check(cameras.Count == 1, "Exactly one active camera",
                string.Join(", ", cameras.Select(c => c.name)));

            Camera xrCamera = cameras.FirstOrDefault();
            if (xrCamera != null)
            {
                report.Check(xrCamera.name == "CenterEyeAnchor", "Active camera is OVRCameraRig CenterEyeAnchor", xrCamera.name);
                report.Check(xrCamera.CompareTag("MainCamera"), "XR camera is tagged MainCamera", xrCamera.tag);
                report.Check(xrCamera.stereoTargetEye == StereoTargetEyeMask.Both, "XR camera renders to both eyes",
                    xrCamera.stereoTargetEye.ToString());
            }

            List<AudioListener> listeners = objects
                .Where(o => o.activeInHierarchy)
                .SelectMany(o => o.GetComponents<AudioListener>())
                .Where(l => l.enabled)
                .ToList();
            report.Check(listeners.Count == 1, "Exactly one active AudioListener",
                string.Join(", ", listeners.Select(l => l.name)));
            if (listeners.Count == 1 && xrCamera != null)
            {
                report.Check(listeners[0].gameObject == xrCamera.gameObject, "AudioListener is on the XR camera (head)",
                    listeners[0].name);
            }
        }

        internal static void ValidateControllers(Report report, List<GameObject> objects)
        {
            List<FromOVRControllerDataSource> sources = objects
                .SelectMany(o => o.GetComponents<FromOVRControllerDataSource>())
                .ToList();
            int distinctHands = sources
                .Select(s => new SerializedObject(s).FindProperty("_handedness")?.enumValueIndex ?? -1)
                .Where(h => h >= 0)
                .Distinct()
                .Count();
            report.Check(sources.Count == 2 && distinctHands == 2, "Two OVR controller data sources (left + right)",
                $"found {sources.Count} sources, {distinctHands} distinct hands");

            int controllers = objects
                .Where(o => o.activeInHierarchy)
                .Sum(o => o.GetComponents<Controller>().Length);
            report.Check(controllers >= 2, "Controller tracking components present", $"found {controllers}");

            int controllerRays = objects
                .Where(o => o.activeInHierarchy)
                .SelectMany(o => o.GetComponents<RayInteractor>())
                .Count(r => r.GetComponent<ControllerRef>() != null);
            report.Check(controllerRays == 2, "Controller RayInteractor on each hand", $"found {controllerRays}");

            int controllerGrabs = objects
                .Where(o => o.activeInHierarchy)
                .SelectMany(o => o.GetComponents<GrabInteractor>())
                .Count(g => g.GetComponent<ControllerRef>() != null);
            report.Check(controllerGrabs == 2, "Controller GrabInteractor on each hand", $"found {controllerGrabs}");
        }

        private static void ValidateEnvironment(Report report, List<GameObject> objects)
        {
            GameObject ground = objects.FirstOrDefault(o => o.name == XRFoundationSceneBuilder.GroundName);
            report.Check(ground != null && ground.GetComponent<Collider>() != null, "Ground plane with collider");
            if (ground != null)
            {
                report.Check(Mathf.Abs(ground.transform.position.y) < 0.001f, "Ground plane at floor level (y = 0)");
            }

            bool hasDirectional = objects
                .SelectMany(o => o.GetComponents<Light>())
                .Any(l => l.enabled && l.type == LightType.Directional);
            report.Check(hasDirectional, "Directional light present");
        }

        private static void ValidateTestCube(Report report, List<GameObject> objects)
        {
            GameObject cube = objects.FirstOrDefault(o => o.name == XRFoundationSceneBuilder.TestCubeName);
            report.Check(cube != null, "Test cube exists");
            if (cube == null)
            {
                return;
            }

            var renderer = cube.GetComponent<Renderer>();
            report.Check(cube.activeInHierarchy && renderer != null && renderer.enabled && renderer.sharedMaterial != null,
                "Test cube is active and renders with a material");
            report.Check(cube.GetComponent<Rigidbody>() != null && cube.GetComponent<Collider>() != null,
                "Test cube has Rigidbody + Collider");
            report.Check(cube.GetComponents<Grabbable>().Length == 1, "Test cube has exactly one Grabbable",
                $"found {cube.GetComponents<Grabbable>().Length}");
            report.Check(cube.GetComponentsInChildren<GrabInteractable>(true).Length >= 1, "Controller GrabInteractable on test cube");
            report.Check(cube.GetComponentsInChildren<HandGrabInteractable>(true).Length >= 1, "HandGrabInteractable on test cube");
            report.Check(cube.GetComponentsInChildren<RayInteractable>(true).Length >= 1, "RayInteractable (ray grab) on test cube");
            report.Check(cube.GetComponentsInChildren<ColliderSurface>(true).Length >= 1, "Ray ColliderSurface on test cube");
            report.Check(cube.GetComponent<PointableEventRelay>() != null, "Interaction event relay on test cube");

            Vector3 toCube = cube.transform.position - ExpectedEyePosition;
            float angle = Vector3.Angle(Vector3.forward, toCube);
            report.Check(angle < 50f && toCube.magnitude < 1.5f, "Test cube is in view and within reach of the spawn point",
                $"angle {angle:F0} deg, distance {toCube.magnitude:F2} m");
        }

        private static void ValidateRayTarget(Report report, List<GameObject> objects)
        {
            GameObject target = objects.FirstOrDefault(o => o.name == XRFoundationSceneBuilder.RayTargetName);
            report.Check(target != null && target.GetComponentsInChildren<RayInteractable>(true).Length == 1,
                "Ray target panel has a RayInteractable");
        }

        /// <summary>
        /// Missing scripts, plus required (non-[Optional]) Interaction SDK [Interface] fields left unassigned.
        /// ISDK asserts these in Start(), so catching them here prevents runtime wiring errors.
        /// </summary>
        internal static void ValidateSerializedReferences(Report report, List<GameObject> objects)
        {
            int missingScripts = objects.Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);
            report.Check(missingScripts == 0, "No missing scripts", $"{missingScripts} missing");

            var unassigned = new List<string>();
            foreach (MonoBehaviour behaviour in objects.SelectMany(o => o.GetComponents<MonoBehaviour>()))
            {
                if (behaviour == null)
                {
                    continue;
                }
                for (Type type = behaviour.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                {
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                 BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(Object).IsAssignableFrom(field.FieldType) ||
                            field.GetCustomAttribute<InterfaceAttribute>() == null ||
                            field.GetCustomAttribute<OptionalAttribute>() != null)
                        {
                            continue;
                        }
                        if (field.GetValue(behaviour) as Object == null)
                        {
                            unassigned.Add($"{GetPath(behaviour.transform)}:{type.Name}.{field.Name}");
                        }
                    }
                }
            }
            report.Check(unassigned.Count == 0, "All required Interaction SDK interface references assigned",
                string.Join("; ", unassigned.Take(20)));
        }

        private static int Count<T>(IEnumerable<GameObject> objects) where T : Component =>
            objects.Sum(o => o.GetComponents<T>().Length);

        private static string GetPath(Transform t) =>
            t.parent == null ? t.name : $"{GetPath(t.parent)}/{t.name}";
    }
}
