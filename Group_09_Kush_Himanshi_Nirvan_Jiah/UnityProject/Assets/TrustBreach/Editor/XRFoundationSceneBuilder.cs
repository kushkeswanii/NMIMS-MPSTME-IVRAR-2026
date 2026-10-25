using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.OVR.Editor.QuickActions;
using Oculus.Interaction.Surfaces;
using TrustBreach.Interactions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TrustBreach.Editor
{
    /// <summary>
    /// Idempotent builder for the Phase 1 XR foundation scene. Every run regenerates the scene from an
    /// empty scene using Meta's own Quick Actions APIs, so repeated runs never accumulate duplicate rigs,
    /// cameras or interactables. Only files under Assets/TrustBreach are written.
    ///
    /// Batch mode:
    ///   Unity -batchmode -quit -projectPath . -executeMethod TrustBreach.Editor.XRFoundationSceneBuilder.BuildFromCommandLine
    /// </summary>
    public static class XRFoundationSceneBuilder
    {
        public const string ScenePath = "Assets/TrustBreach/Scenes/TrustBreach_XR_Foundation.unity";
        public const string GroundName = "Ground";
        public const string TestCubeName = "TB_TestCube";
        public const string RayTargetName = "TB_RayTargetPanel";

        private const string RootFolder = "Assets/TrustBreach";
        private const string MaterialsFolder = RootFolder + "/Materials";
        private const string BackupFolder = RootFolder + "/Scenes/_UnsavedSceneBackups";

        private static readonly string[] RequiredFolders =
        {
            "Scenes", "Scripts/Core", "Scripts/Dialogue", "Scripts/Telemetry", "Scripts/Interactions", "Scripts/AI",
            "Environment", "Avatars", "Audio", "Materials", "UI", "Prefabs", "Tests", "Editor"
        };

        private const float TableHeight = 1.0f; // standing-desk height keeps the cube in view at spawn
        private const float TestCubeSize = 0.1f;

        [MenuItem("TrustBreach/Phase 1/Build XR Foundation Scene", priority = 0)]
        private static void BuildFromMenu()
        {
            XRFoundationValidator.Report report = Build();
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        /// <summary>Entry point for -executeMethod. Exits with code 1 if validation fails.</summary>
        public static void BuildFromCommandLine()
        {
            XRProjectConfigurator.Apply();
            XRFoundationValidator.Report report = Build();
            Debug.Log(report);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.Success ? 0 : 1);
            }
        }

        /// <summary>Rebuilds and saves the scene, then returns the validation report.</summary>
        public static XRFoundationValidator.Report Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before building the XR foundation scene.");
            }

            EnsureFolders();
            BackupDirtyScenes();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            Transform environment = new GameObject("Environment").transform;
            CreateGround(environment);
            CreateTable(environment);

            CreateXRRig();

            Transform interactables = new GameObject("Interactables").transform;
            CreateTestCube(interactables);
            CreateRayTargetPanel(interactables);

            Selection.activeObject = null;
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException($"Failed to save {ScenePath}");
            }
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();

            return XRFoundationValidator.Validate(scene);
        }

        private static void EnsureFolders()
        {
            foreach (string relative in RequiredFolders)
            {
                string parent = RootFolder;
                foreach (string part in relative.Split('/'))
                {
                    string path = $"{parent}/{part}";
                    if (!AssetDatabase.IsValidFolder(path))
                    {
                        AssetDatabase.CreateFolder(parent, part);
                    }
                    parent = path;
                }
            }
        }

        /// <summary>
        /// NewScene(Single) discards unsaved edits without prompting, so any dirty scene other than the one
        /// we regenerate is first saved as a copy. The original scene file is never overwritten.
        /// </summary>
        internal static void BackupDirtyScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (!open.isDirty || open.path == ScenePath)
                {
                    continue;
                }
                if (!AssetDatabase.IsValidFolder(BackupFolder))
                {
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(BackupFolder), Path.GetFileName(BackupFolder));
                }
                string sceneName = string.IsNullOrEmpty(open.name) ? "Untitled" : open.name;
                string backupPath = $"{BackupFolder}/{sceneName}_unsaved_{DateTime.Now:yyyyMMdd_HHmmss}.unity";
                EditorSceneManager.SaveScene(open, backupPath, saveAsCopy: true);
                Debug.LogWarning($"[TrustBreach] '{sceneName}' had unsaved changes. A copy was saved to {backupPath}; " +
                    $"the original file '{open.path}' was not modified.");
            }
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(50f, -30f, 0f));
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.957f, 0.839f);
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.4f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.19f, 0.18f);
        }

        private static void CreateGround(Transform parent)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = GroundName;
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(2f, 1f, 2f); // 20 m x 20 m
            ground.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("TB_Ground", new Color(0.32f, 0.34f, 0.36f));
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.BatchingStatic);
        }

        private static void CreateTable(Transform parent)
        {
            GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "TestTable";
            table.transform.SetParent(parent, false);
            table.transform.localScale = new Vector3(1.2f, TableHeight, 0.6f);
            table.transform.localPosition = new Vector3(0f, TableHeight * 0.5f, 0.8f);
            table.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("TB_Table", new Color(0.45f, 0.33f, 0.24f));
            GameObjectUtility.SetStaticEditorFlags(table, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>
        /// OVRCameraRig + Interaction SDK comprehensive rig (controllers, hands, ray, grab, poke, locomotion),
        /// created through Meta's public Quick Actions API exactly as the "Add OVR Comprehensive Interaction Rig"
        /// wizard does. The package prefab is kept as a prefab instance (no editable copy outside Assets/TrustBreach).
        /// </summary>
        private static void CreateXRRig()
        {
            List<GameObject> created = OVRQuickActionsAPI.AddOVRInteractionRig(generateAsEditableCopy: false);
            if (created == null || created.Count == 0)
            {
                throw new InvalidOperationException("OVRQuickActionsAPI.AddOVRInteractionRig created no objects.");
            }

            OVRCameraRig cameraRig = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<OVRCameraRig>(true))
                .Single();
            cameraRig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        private static void CreateTestCube(Transform parent)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = TestCubeName;
            cube.transform.SetParent(parent, false);
            cube.transform.localScale = Vector3.one * TestCubeSize;
            cube.transform.localPosition = new Vector3(0f, TableHeight + TestCubeSize * 0.5f + 0.005f, 0.6f);
            var renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial = GetOrCreateMaterial("TB_TestCube", new Color(0.12f, 0.45f, 0.95f));

            // Rigidbody + Grabbable + GrabInteractable (controller grip) + HandGrabInteractable (hands).
            QuickActionsAPI.AddGrabInteraction(cube);
            // RayInteractable + ColliderSurface (controller trigger / hand pinch ray grab); reuses the Grabbable.
            QuickActionsAPI.AddRayGrabInteraction(cube);

            var relay = cube.AddComponent<PointableEventRelay>();
            relay.InjectAllPointableEventRelay(cube.GetComponent<Grabbable>(), TestCubeName);
            relay.InjectOptionalRenderer(renderer);
        }

        /// <summary>A wall-mounted panel that is ray-targetable but not grabbable (hover/select only).</summary>
        private static void CreateRayTargetPanel(Transform parent)
        {
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = RayTargetName;
            panel.transform.SetParent(parent, false);
            panel.transform.localScale = new Vector3(0.8f, 0.5f, 0.02f);
            panel.transform.localPosition = new Vector3(0f, 1.5f, 2.5f);
            var renderer = panel.GetComponent<Renderer>();
            renderer.sharedMaterial = GetOrCreateMaterial("TB_RayTarget", new Color(0.85f, 0.85f, 0.88f));

            var interactableObject = new GameObject("RayInteractable");
            interactableObject.transform.SetParent(panel.transform, false);
            var surface = interactableObject.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(panel.GetComponent<Collider>());
            var rayInteractable = interactableObject.AddComponent<RayInteractable>();
            rayInteractable.InjectAllRayInteractable(surface);

            var relay = panel.AddComponent<PointableEventRelay>();
            relay.InjectAllPointableEventRelay(rayInteractable, RayTargetName);
            relay.InjectOptionalRenderer(renderer);
        }

        internal static Material GetOrCreateMaterial(string materialName, Color baseColor)
        {
            string path = $"{MaterialsFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null)
                {
                    throw new InvalidOperationException("URP Lit shader not found.");
                }
                material = new Material(lit) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", baseColor);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Puts the foundation scene first in Build Settings so device builds launch into it.</summary>
        private static void RegisterInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes
                .Where(s => s.path != ScenePath)
                .ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
