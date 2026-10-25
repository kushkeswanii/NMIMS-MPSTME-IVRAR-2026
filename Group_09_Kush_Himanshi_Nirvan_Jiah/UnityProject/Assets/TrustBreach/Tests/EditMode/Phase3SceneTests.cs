using System.Linq;
using NUnit.Framework;
using TrustBreach.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TrustBreach.Tests
{
    public class Phase3SceneTests
    {
        [TestCase(Phase3AdversaryBuilder.ScenePath)]
        [TestCase(Phase3AdversaryBuilder.DemoScenePath)]
        public void Phase3ScenePassesValidation(string path)
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), $"Missing {path}");
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            XRFoundationValidator.Report report = Phase3Validator.Validate(scene);
            report.Merge(CorporateOfficeValidator.Validate(scene));
            report.Merge(OfficeInteractionsValidator.Validate(scene));
            Assert.IsTrue(report.Success, report.ToString());
        }

        [Test]
        public void OriginalScenesArePreserved()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(TrustBreachCorporateOfficeBuilder.ScenePath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(XRFoundationSceneBuilder.ScenePath));
            Scene office = EditorSceneManager.OpenScene(TrustBreachCorporateOfficeBuilder.ScenePath, OpenSceneMode.Single);
            Assert.IsNull(office.GetRootGameObjects()[0].scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<UnityEngine.Transform>(true))
                .FirstOrDefault(t => t.name == Phase3AdversaryBuilder.NpcName), "Phase 2.5 office scene must not contain Phase 3 content");
        }
    }
}
