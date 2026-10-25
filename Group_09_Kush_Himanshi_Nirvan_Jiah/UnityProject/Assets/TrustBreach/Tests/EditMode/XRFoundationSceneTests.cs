using System.Linq;
using NUnit.Framework;
using TrustBreach.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TrustBreach.Tests
{
    public class XRFoundationSceneTests
    {
        [Test]
        public void FoundationSceneExistsAndIsInBuildSettings()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(XRFoundationSceneBuilder.ScenePath),
                $"Missing {XRFoundationSceneBuilder.ScenePath}");
            Assert.IsNotEmpty(EditorBuildSettings.scenes);
            // Kept in the list; since the final release the headset build boots into the participant scene (Phase 5).
            Assert.IsTrue(EditorBuildSettings.scenes.Any(s => s.path == XRFoundationSceneBuilder.ScenePath));
        }

        [Test]
        public void FoundationScenePassesValidation()
        {
            Scene scene = EditorSceneManager.OpenScene(XRFoundationSceneBuilder.ScenePath, OpenSceneMode.Single);
            XRFoundationValidator.Report report = XRFoundationValidator.Validate(scene);
            Assert.IsTrue(report.Success, report.ToString());
        }
    }
}
