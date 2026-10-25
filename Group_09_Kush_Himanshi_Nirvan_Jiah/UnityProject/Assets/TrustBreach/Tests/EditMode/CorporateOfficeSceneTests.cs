using System.Linq;
using NUnit.Framework;
using TrustBreach.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TrustBreach.Tests
{
    public class CorporateOfficeSceneTests
    {
        [Test]
        public void OfficeSceneExistsAndIsInBuildSettings()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(TrustBreachCorporateOfficeBuilder.ScenePath),
                $"Missing {TrustBreachCorporateOfficeBuilder.ScenePath}");
            Assert.IsTrue(EditorBuildSettings.scenes.Any(s => s.path == TrustBreachCorporateOfficeBuilder.ScenePath));
            Assert.AreEqual(Phase3AdversaryBuilder.ScenePath, EditorBuildSettings.scenes.First(s => s.enabled).path,
                "Release build: the headset app must start in the participant scene (nothing loads another scene at runtime).");
        }

        [Test]
        public void OfficeScenePassesValidation()
        {
            Scene scene = EditorSceneManager.OpenScene(TrustBreachCorporateOfficeBuilder.ScenePath, OpenSceneMode.Single);
            XRFoundationValidator.Report report = CorporateOfficeValidator.Validate(scene);
            Assert.IsTrue(report.Success, report.ToString());
        }
    }
}
