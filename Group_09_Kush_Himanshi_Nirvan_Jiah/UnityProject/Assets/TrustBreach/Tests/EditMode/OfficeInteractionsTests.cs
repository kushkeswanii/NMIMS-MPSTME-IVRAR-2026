using NUnit.Framework;
using TrustBreach.Editor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TrustBreach.Tests
{
    public class OfficeInteractionsTests
    {
        [Test]
        public void OfficeInteractionsPassValidation()
        {
            Scene scene = EditorSceneManager.OpenScene(TrustBreachCorporateOfficeBuilder.ScenePath, OpenSceneMode.Single);
            XRFoundationValidator.Report report = OfficeInteractionsValidator.Validate(scene);
            Assert.IsTrue(report.Success, report.ToString());
        }
    }
}
