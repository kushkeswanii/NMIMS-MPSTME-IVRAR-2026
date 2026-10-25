using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using P3 = TrustBreach.Editor.Phase3AdversaryBuilder;

namespace TrustBreach.Experience.Editor
{
    /// <summary>
    /// Release (headset) build configuration. Nothing in the app loads another scene, so the first enabled scene is the
    /// only one a participant can reach on a Quest: it must be the participant scene. Every other scene stays in the list
    /// (disabled) so nothing is lost; enable one manually only for a special build.
    /// A build guard stops an Android build that would boot into anything else (e.g. after a Phase 1 rebuild re-inserts the
    /// XR test room at index 0).
    /// </summary>
    public class ReleaseBuildConfig : IPreprocessBuildWithReport
    {
        public const string BootScene = P3.ScenePath;

        public int callbackOrder => 0;

        [MenuItem("Tools/TRUST∕∕BREACH/Experience/Apply Release Build Scene List (participant scene first)", priority = 210)]
        public static void ApplyReleaseSceneList()
        {
            var others = EditorBuildSettings.scenes.Where(s => s.path != BootScene)
                .Select(s => new EditorBuildSettingsScene(s.path, false) { guid = s.guid });
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScene, true) }.Concat(others).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[TrustBreach.Release] Build scene list: {BootScene} is the only enabled scene (index 0); other scenes kept, disabled.");
        }

        /// <summary>Path of the scene a player build starts in (first enabled scene), or null.</summary>
        public static string FirstEnabledScene => EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled)?.path;

        public static bool IsReleaseOrder => FirstEnabledScene == BootScene;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android || IsReleaseOrder) return;
            throw new BuildFailedException(
                $"TRUST//BREACH: the headset build would start in '{FirstEnabledScene}', from which the experience cannot be reached. " +
                "Run Tools > TRUST//BREACH > Experience > Apply Release Build Scene List, then build again.");
        }
    }
}
