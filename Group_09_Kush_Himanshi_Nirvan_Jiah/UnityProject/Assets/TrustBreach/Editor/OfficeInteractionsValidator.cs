using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using TrustBreach.Interactions;
using TrustBreach.Interactions.Scenarios;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TrustBreach.Editor
{
    /// <summary>Edit-time validation of the Phase 2.5 interaction layer (wiring, hit-testing, initial states).</summary>
    public static class OfficeInteractionsValidator
    {
        private static readonly Type[] ScenarioTypes =
        {
            typeof(PhishingEmailScenario), typeof(PasswordResetKioskScenario), typeof(VisitorBadgeScenario), typeof(MaliciousUSBScenario)
        };

        [MenuItem("Tools/TRUST∕∕BREACH/Validate Office Interactions", priority = 111)]
        [MenuItem("TrustBreach/Phase 2.5/Validate Office Interactions", priority = 111)]
        private static void ValidateFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != TrustBreachCorporateOfficeBuilder.ScenePath)
            {
                scene = EditorSceneManager.OpenScene(TrustBreachCorporateOfficeBuilder.ScenePath, OpenSceneMode.Single);
            }
            XRFoundationValidator.Report report = Validate(scene);
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        public static XRFoundationValidator.Report Validate(Scene scene)
        {
            var report = new XRFoundationValidator.Report { Title = "TrustBreach Office Interactions validation" };
            GameObject scenarioRoot = scene.GetRootGameObjects().FirstOrDefault(r => r.name == TrustBreachCorporateOfficeBuilder.ScenarioRootName);
            Transform root = scenarioRoot != null ? scenarioRoot.transform.Find(OfficeInteractionsBuilder.RootName) : null;
            report.Check(root != null, "Scenario_Objects/Scenario_Interactions exists");
            if (root == null)
            {
                return report;
            }

            List<GameObject> objects = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();

            foreach (Type type in ScenarioTypes)
            {
                Component[] found = objects.SelectMany(o => o.GetComponents(type)).ToArray();
                report.Check(found.Length == 1, $"Exactly one {type.Name}", $"found {found.Length}");
                foreach (Component c in found)
                {
                    List<string> missing = UnassignedReferences(c);
                    report.Check(missing.Count == 0, $"{type.Name}: every reference wired", string.Join(", ", missing));
                    report.Check(!string.IsNullOrEmpty(((ScenarioController)c).ObjectId), $"{type.Name}: object id set");
                }
            }

            List<XRButton> buttons = objects.SelectMany(o => o.GetComponents<XRButton>()).ToList();
            report.Check(buttons.Count >= 11, "All decision buttons present", buttons.Count.ToString());
            report.Check(buttons.Select(b => b.ButtonId).Distinct().Count() == buttons.Count, "Button ids are unique");
            foreach (XRButton button in buttons)
            {
                List<string> missing = UnassignedReferences(button);
                var ray = GetField<Object>(button, "_pointable") as RayInteractable;
                Collider own = button.GetComponent<Collider>();
                ColliderSurface surface = ray != null ? ray.GetComponent<ColliderSurface>() : null;
                bool wired = missing.Count == 0 && ray != null && own != null && surface != null &&
                             ReferenceEquals(GetField<Collider>(surface, "_collider"), own);
                report.Check(wired, $"Button '{button.ButtonId}' is backed by its own RayInteractable surface", string.Join(", ", missing));
            }
            ValidateHitTesting(report, root, buttons.Where(b => b.transform.IsChildOf(root)).ToList());

            // Initial (pre-decision) states: decision pages and outcomes start hidden.
            string[] hiddenAtStart =
            {
                "PhishingEmail_UI/CredentialVerificationPage", "PhishingEmail_UI/Outcome_Compromised", "PhishingEmail_UI/Outcome_ReportedFromInbox",
                "PhishingEmail_UI/Outcome_ReportedAfterClick", "PasswordKiosk_UI/VerificationDecisionPanel", "PasswordKiosk_UI/Outcome_CredentialsCaptured",
                "PasswordKiosk_UI/Outcome_Cancelled", "PasswordKiosk_UI/Outcome_Reported", "BadgeInspectionStation",
                "InfectedScreen/Overlay"
            };
            List<string> wrongState = hiddenAtStart.Where(p => root.Find(p) == null || root.Find(p).gameObject.activeSelf).ToList();
            report.Check(wrongState.Count == 0, "Decision pages and outcomes start hidden", string.Join(", ", wrongState));

            // USB zones: triggers, and the drive does not start inside either.
            MaliciousUSBScenario usb = objects.Select(o => o.GetComponent<MaliciousUSBScenario>()).FirstOrDefault(c => c != null);
            if (usb != null)
            {
                var connect = GetField<Collider>(usb, "_connectZone");
                var submit = GetField<Collider>(usb, "_submitZone");
                var drive = GetField<Collider>(usb, "_usbCollider");
                report.Check(connect != null && submit != null && connect.isTrigger && submit.isTrigger, "USB placement zones are triggers");
                if (connect != null && submit != null && drive != null)
                {
                    report.Check(!connect.bounds.Contains(drive.bounds.center) && !submit.bounds.Contains(drive.bounds.center),
                        "USB drive starts outside both placement zones");
                }
            }

            int hints = root.GetComponentsInChildren<TMP_Text>(true).Count(t => t.text.Contains("TRIGGER") || t.text.Contains("GRIP"));
            report.Check(hints >= 6, "Instruction hints present (trigger / grip)", hints.ToString());
            return report;
        }

        /// <summary>
        /// Models what an Interaction SDK ray sees: only RayInteractable collider surfaces count (locomotion/body colliders
        /// do not block rays). For each button, its own hidden page is revealed (one state at a time), a ray is cast
        /// from 30 cm in front, and the nearest surface hit must be the button itself.
        /// </summary>
        internal static void ValidateHitTesting(XRFoundationValidator.Report report, Transform root, List<XRButton> buttons)
        {
            var blocked = new List<string>();
            foreach (XRButton button in buttons)
            {
                List<GameObject> revealed = button.GetComponentsInParent<Transform>(true).Select(t => t.gameObject).Where(g => !g.activeSelf).ToList();
                revealed.ForEach(g => g.SetActive(true));
                Physics.SyncTransforms();
                try
                {
                    Transform t = button.transform;
                    var ray = new Ray(t.position - t.forward * 0.3f, t.forward);
                    ColliderSurface nearest = null;
                    float best = float.MaxValue;
                    foreach (RayInteractable interactable in Object.FindObjectsByType<RayInteractable>(FindObjectsInactive.Exclude))
                    {
                        var surface = interactable.GetComponent<ColliderSurface>();
                        var collider = surface != null ? GetField<Collider>(surface, "_collider") : null;
                        if (collider != null && collider.Raycast(ray, out RaycastHit hit, 0.35f) && hit.distance < best)
                        {
                            best = hit.distance;
                            nearest = surface;
                        }
                    }
                    Collider nearestCollider = nearest != null ? GetField<Collider>(nearest, "_collider") : null;
                    if (nearestCollider == null || nearestCollider.gameObject != button.gameObject)
                    {
                        blocked.Add($"{button.ButtonId} -> {(nearestCollider != null ? nearestCollider.name : "nothing")}");
                    }
                }
                finally
                {
                    revealed.ForEach(g => g.SetActive(false));
                }
            }
            report.Check(blocked.Count == 0, "Every button is the nearest ray surface where it is drawn (per state)", string.Join("; ", blocked));
        }

        internal static List<string> UnassignedReferences(Component component)
        {
            var missing = new List<string>();
            for (Type type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    bool serialized = field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
                    if (!serialized || !typeof(Object).IsAssignableFrom(field.FieldType) || field.GetCustomAttribute<OptionalAttribute>() != null)
                    {
                        continue;
                    }
                    if (field.GetValue(component) as Object == null)
                    {
                        missing.Add(field.Name);
                    }
                }
            }
            return missing;
        }

        internal static T GetField<T>(object target, string name) where T : class
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field.GetValue(target) as T;
                }
            }
            return null;
        }
    }
}
