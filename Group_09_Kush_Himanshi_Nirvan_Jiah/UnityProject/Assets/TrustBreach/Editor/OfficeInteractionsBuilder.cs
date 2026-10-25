using System;
using System.IO;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using TrustBreach.Interactions;
using TrustBreach.Interactions.Scenarios;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static TrustBreach.Editor.OfficeBuildKit;
using B = TrustBreach.Editor.TrustBreachCorporateOfficeBuilder;
using Object = UnityEngine.Object;

namespace TrustBreach.Editor
{
    /// <summary>
    /// Phase 2.5: adds the decision/interaction layer to the existing corporate office scene without touching the
    /// environment, layout or XR rig. Idempotent: everything it creates lives under
    /// Scenario_Objects/Scenario_Interactions (deleted and recreated each run), and the components it adds to
    /// existing objects are removed and re-added. The scene file is backed up to &lt;project&gt;/Backups before saving.
    /// The Phase 2 office builder also calls <see cref="Apply"/>, so a full office rebuild keeps these interactions.
    /// </summary>
    public static class OfficeInteractionsBuilder
    {
        public const string RootName = "Scenario_Interactions";
        public const string TriggerHint = "Aim and pull the INDEX TRIGGER to press\n(Meta XR Simulator: T)";

        private static readonly Color Blue = new Color(0.05f, 0.35f, 0.55f);
        private static readonly Color Red = new Color(0.78f, 0.13f, 0.12f);
        private static readonly Color Green = new Color(0.1f, 0.55f, 0.3f);
        private static readonly Color Amber = new Color(0.85f, 0.5f, 0.05f);
        private static readonly Color Slate = new Color(0.35f, 0.38f, 0.42f);
        private static readonly Color PageWhite = new Color(0.97f, 0.97f, 0.98f);
        private static readonly Color Ink = new Color(0.12f, 0.13f, 0.16f);
        private static readonly Color HintTeal = new Color(0.62f, 0.9f, 0.9f);

        // Pages are filled while active (so TextMeshPro initialises its font/material) and hidden at the end of Apply.
        private static readonly System.Collections.Generic.List<GameObject> PagesToHide = new System.Collections.Generic.List<GameObject>();

        [MenuItem("Tools/TRUST∕∕BREACH/Build Office Interactions (Phase 2.5)", priority = 110)]
        [MenuItem("TrustBreach/Phase 2.5/Build Office Interactions", priority = 110)]
        private static void BuildFromMenu()
        {
            XRFoundationValidator.Report report = Build();
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        /// <summary>Opens the office scene, backs it up, applies the interaction layer, saves and validates.</summary>
        public static XRFoundationValidator.Report Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before building office interactions.");
            }
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != B.ScenePath)
            {
                XRFoundationSceneBuilder.BackupDirtyScenes();
                scene = EditorSceneManager.OpenScene(B.ScenePath, OpenSceneMode.Single);
            }
            BackupSceneFile(B.ScenePath);

            Apply(scene);
            Selection.activeObject = null;
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new IOException($"Failed to save {B.ScenePath}");
            }
            AssetDatabase.SaveAssets();

            XRFoundationValidator.Report report = CorporateOfficeValidator.Validate(scene);
            report.Merge(OfficeInteractionsValidator.Validate(scene));
            report.Title = "TrustBreach Office + Interactions validation";
            if (scene.isDirty)
            {
                EditorSceneManager.SaveScene(scene); // validation toggles hidden panels to hit-test them; content is unchanged
            }
            return report;
        }

        /// <summary>Copies the scene (and .meta) to &lt;project&gt;/Backups/scenes/ with a timestamp. Outside Assets: never imported.</summary>
        internal static string BackupSceneFile(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string folder = Path.Combine(projectRoot, "Backups", "scenes");
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(assetPath)}_{DateTime.Now:yyyyMMdd_HHmmss}.unity");
            File.Copy(Path.Combine(projectRoot, assetPath), target, true);
            File.Copy(Path.Combine(projectRoot, assetPath + ".meta"), target + ".meta", true);
            return target;
        }

        /// <summary>Hides pages created with <see cref="Page"/> (they are filled while active so TMP initialises).</summary>
        internal static void HidePendingPages()
        {
            PagesToHide.ForEach(page => page.SetActive(false));
            PagesToHide.Clear();
        }

        /// <summary>Applies the interaction layer to an open office scene (no save).</summary>
        public static void Apply(Scene scene)
        {
            ResetCache();
            PagesToHide.Clear();
            Transform scenario = Root(scene, B.ScenarioRootName);
            Transform environment = Root(scene, B.EnvironmentRootName);

            Transform existing = scenario.Find(RootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }
            Transform root = Group(RootName, scenario, Vector3.zero);

            BuildPhishingEmail(root, Find(scenario, $"{B.PhishingEmailWorkstation}/Monitor/Screen"));
            BuildPasswordKiosk(root, Find(scenario, $"{B.FakeSSLTerminal}/Screen"), Find(scenario, B.FakeSSLTerminal));
            BuildVisitorBadge(root, Find(scenario, B.SpoofedVisitorBadge), Find(environment, "Reception/ReceptionDesk"));
            BuildMaliciousUSB(root, Find(scenario, B.MaliciousUSBDrive), Find(environment, "ITSecurityDesk/ITServiceCounter"),
                Find(environment, "Workstations/Pod_B/Desk_05"), Find(environment, "Workstations/Pod_B/Desk_05/Monitor/Screen"));

            PagesToHide.ForEach(page => page.SetActive(false));
            PagesToHide.Clear();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // =====================================================================================================
        // Phishing email
        // =====================================================================================================
        private static void BuildPhishingEmail(Transform root, Transform screen)
        {
            var size = new Vector2(0.62f, 0.36f);
            Transform ui = Anchor("PhishingEmail_UI", root, screen, Vector3.zero, Quaternion.identity);
            var scenario = Group("PhishingEmailScenario", root, Vector3.zero).gameObject.AddComponent<PhishingEmailScenario>();
            scenario.InjectObjectId(B.PhishingEmailWorkstation);

            // Inbox: upgrade the existing "VERIFY ACCOUNT NOW" quad, add a toolbar "Report phishing" button.
            XRButton verify = UpgradeExistingButton(screen.Find(B.PhishingLinkButton).gameObject, ui, "phishing_email.verify_account_now", Red);
            XRButton reportInbox = Button(ui, "ReportPhishingButton", "phishing_email.report_phishing", "REPORT PHISHING",
                new Vector2(0.235f, 0.163f), new Vector2(0.13f, 0.02f), Amber, 0.066f, -0.0024f);

            // Fake credential-verification page (lookalike site).
            Transform page = Page(ui, "CredentialVerificationPage", size, PageWhite);
            ScreenRect(page, "BrowserBar", 0f, 0.157f, 0.62f, 0.045f, new Color(0.87f, 0.88f, 0.9f));
            ScreenRect(page, "NotSecureChip", -0.25f, 0.157f, 0.1f, 0.028f, new Color(0.98f, 0.86f, 0.85f), 2);
            ScreenText(page, "NotSecure", "<color=#C62828><b>! Not secure</b></color>", -0.25f, 0.157f, 0.1f, 0.028f, 0.07f, Color.white, TextAlignmentOptions.Center);
            ScreenText(page, "Url", "http://aperture-systerns-verify.co/login?id=88213", -0.19f, 0.168f, 0.48f, 0.025f, 0.072f, Ink);
            ScreenText(page, "Brand", "APERTURE SYSTEMS  |  Account Verification", -0.27f, 0.115f, 0.54f, 0.04f, 0.12f, new Color(0.05f, 0.25f, 0.5f), style: FontStyles.Bold);
            ScreenText(page, "Intro", "Your password expires in 2 hours. Confirm your credentials to keep access to email and payroll.",
                -0.27f, 0.075f, 0.54f, 0.04f, 0.078f, Ink);
            ScreenText(page, "EmailLabel", "Work email", -0.27f, 0.03f, 0.3f, 0.02f, 0.07f, Slate);
            ScreenRect(page, "EmailField", -0.07f, 0.0f, 0.4f, 0.032f, new Color(0.9f, 0.91f, 0.93f));
            ScreenText(page, "EmailValue", "you@aperture-systems.com", -0.26f, 0.011f, 0.38f, 0.022f, 0.072f, Ink);
            ScreenText(page, "PasswordLabel", "Password", -0.27f, -0.025f, 0.3f, 0.02f, 0.07f, Slate);
            ScreenRect(page, "PasswordField", -0.07f, -0.055f, 0.4f, 0.032f, new Color(0.9f, 0.91f, 0.93f));
            ScreenText(page, "PasswordValue", "************", -0.26f, -0.044f, 0.38f, 0.022f, 0.08f, Ink);
            XRButton proceed = Button(page, "ProceedButton", "phishing_email.proceed", "PROCEED", new Vector2(-0.16f, -0.115f),
                new Vector2(0.2f, 0.042f), Blue, 0.1f);
            XRButton report = Button(page, "ReportSuspiciousEmailButton", "phishing_email.report_suspicious_email", "REPORT SUSPICIOUS EMAIL",
                new Vector2(0.115f, -0.115f), new Vector2(0.29f, 0.042f), Green, 0.09f);
            ScreenText(page, "SimulationNote", "Training simulation: nothing you enter here is sent anywhere.", -0.27f, -0.152f, 0.54f, 0.02f,
                0.06f, Slate);

            GameObject compromised = Outcome(ui, "Outcome_Compromised", size, Red, "SIMULATED COMPROMISE",
                "You entered your password on a fake site (aperture-systerns-verify.co).\n\n" +
                "In a real attack your email and payroll access would now belong to the attacker.\n\n" +
                "Red flags: look-alike sender domain, 2-hour deadline, generic greeting, link to a non-company domain.",
                0.1f);
            GameObject reportedInbox = Outcome(ui, "Outcome_ReportedFromInbox", size, Green, "REPORTED - WELL SPOTTED",
                "The email was sent to IT Security without opening the link: the safest response.\n\n" +
                "Red flags: sender aperture-systerns.com (not aperture-systems.com), 2-hour pressure, " +
                "generic \"Dear Employee\", link to aperture-systerns-verify.co.", 0.1f);
            GameObject reportedAfter = Outcome(ui, "Outcome_ReportedAfterClick", size, Amber, "REPORTED - GOOD RECOVERY",
                "You opened the link but stopped before entering a password and reported the email.\n\n" +
                "Next time, check the sender and link before clicking: aperture-systerns.com is a look-alike domain.",
                0.1f);

            Hint(ui, "Hint_Trigger", TriggerHint, new Vector3(0f, 0.255f, 0f), 0.5f);

            Inject(scenario, ("_verifyLinkButton", verify), ("_reportFromInboxButton", reportInbox), ("_credentialPage", page.gameObject),
                ("_proceedButton", proceed), ("_reportButton", report), ("_compromisedOutcome", compromised),
                ("_reportedFromInboxOutcome", reportedInbox), ("_reportedAfterClickOutcome", reportedAfter));
        }

        // =====================================================================================================
        // Password-reset kiosk
        // =====================================================================================================
        private static void BuildPasswordKiosk(Transform root, Transform screen, Transform kiosk)
        {
            var size = new Vector2(0.44f, 0.66f);
            Transform ui = Anchor("PasswordKiosk_UI", root, screen, Vector3.zero, Quaternion.identity);
            var scenario = Group("PasswordResetKioskScenario", root, Vector3.zero).gameObject.AddComponent<PasswordResetKioskScenario>();
            scenario.InjectObjectId(B.FakeSSLTerminal);

            XRButton signIn = UpgradeExistingButton(screen.Find(B.FakeSSLSignInButton).gameObject, ui, "password_kiosk.sign_in_and_reset", Blue);

            Transform panel = Page(ui, "VerificationDecisionPanel", size, PageWhite);
            ScreenRect(panel, "Header", 0f, 0.29f, 0.44f, 0.08f, new Color(0.09f, 0.14f, 0.24f));
            ScreenText(panel, "Title", "IDENTITY VERIFICATION", 0f, 0.29f, 0.42f, 0.06f, 0.13f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            ScreenText(panel, "Body",
                "To reset your password this kiosk needs your <b>current</b> Aperture password and username.\n\n" +
                "Connection: <color=#C62828><b>Not secure</b></color> (http)\nCertificate issued to: *.free-hosting-cdn.xyz",
                -0.2f, 0.22f, 0.4f, 0.2f, 0.1f, Ink);
            ScreenText(panel, "Question", "How do you want to proceed?", -0.2f, 0.0f, 0.4f, 0.03f, 0.095f, Ink, style: FontStyles.Bold);
            XRButton cont = Button(panel, "ContinueButton", "password_kiosk.continue", "CONTINUE - ENTER PASSWORD",
                new Vector2(0f, -0.075f), new Vector2(0.38f, 0.055f), Blue, 0.09f);
            XRButton cancel = Button(panel, "CancelButton", "password_kiosk.cancel", "CANCEL",
                new Vector2(0f, -0.15f), new Vector2(0.38f, 0.055f), Slate, 0.09f);
            XRButton report = Button(panel, "ReportKioskButton", "password_kiosk.report", "REPORT KIOSK TO IT SECURITY",
                new Vector2(0f, -0.225f), new Vector2(0.38f, 0.055f), Green, 0.09f);
            ScreenText(panel, "SimulationNote", "Training simulation: nothing is sent or stored.", -0.2f, -0.28f, 0.4f, 0.02f, 0.062f, Slate);

            GameObject captured = Outcome(ui, "Outcome_CredentialsCaptured", size, Red, "SIMULATED COMPROMISE",
                "This kiosk was rogue: your username and password were captured.\n\n" +
                "Red flags: \"Not secure\" HTTP page, certificate for an unrelated domain, look-alike SSO address, " +
                "and a request for your current password.", 0.1f);
            GameObject cancelled = Outcome(ui, "Outcome_Cancelled", size, Amber, "CANCELLED - NOT REPORTED",
                "You did not enter your credentials, which kept you safe.\n\n" +
                "But the rogue kiosk is still live for colleagues. Report suspicious devices to IT Security (ext. 4400).",
                0.1f);
            GameObject reported = Outcome(ui, "Outcome_Reported", size, Green, "REPORTED - WELL DONE",
                "IT Security has been alerted and the kiosk is being taken offline.\n\n" +
                "Genuine password resets only happen at https://sso.aperture-systems.com or at the IT Service Desk.",
                0.1f);

            // Hint on a plate in front of the pedestal, facing the user (kiosk local -Z).
            Transform hint = Anchor("KioskHint", root, kiosk, new Vector3(0f, 0.95f, -0.08f), Quaternion.Euler(-10f, 0f, 0f));
            Hint(hint, "Hint_Trigger", TriggerHint, Vector3.zero, 0.42f);

            Inject(scenario, ("_signInButton", signIn), ("_decisionPanel", panel.gameObject), ("_continueButton", cont),
                ("_cancelButton", cancel), ("_reportButton", report), ("_capturedOutcome", captured), ("_cancelledOutcome", cancelled),
                ("_reportedOutcome", reported));
        }

        // =====================================================================================================
        // Visitor badge
        // =====================================================================================================
        private static void BuildVisitorBadge(Transform root, Transform badge, Transform desk)
        {
            var scenario = Group("VisitorBadgeScenario", root, Vector3.zero).gameObject.AddComponent<VisitorBadgeScenario>();
            scenario.InjectObjectId(B.SpoofedVisitorBadge);

            // Pick-up hint on the ledge beside the badge tray, facing visitors.
            Transform hint = Anchor("BadgePickUpHint", root, desk, new Vector3(-0.5f, 1.2f, -0.3f), Quaternion.Euler(10f, 0f, 0f));
            Hint(hint, "Text", "Pick up a visitor badge with the GRIP\nbutton to inspect it", Vector3.zero, 0.3f);

            // Inspection station on the counter (hidden until the badge is first picked up).
            Transform inspector = Anchor("BadgeInspectionStation", root, desk, new Vector3(-0.1f, 1.14f, -0.3f), Quaternion.identity);
            Material metal = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);
            Box("StandBase", inspector, new Vector3(0f, 0.006f, 0f), new Vector3(0.2f, 0.012f, 0.1f), metal);
            Box("StandNeck", inspector, new Vector3(0f, 0.04f, 0.01f), new Vector3(0.03f, 0.07f, 0.02f), metal);
            var size = new Vector2(0.5f, 0.32f);
            Transform screen = Screen("InspectionScreen", inspector, new Vector3(0f, 0.23f, 0f), Quaternion.Euler(15f, 0f, 0f), size,
                new Color(0.08f, 0.11f, 0.17f), out _, Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            ScreenRect(screen, "Header", 0f, 0.14f, 0.5f, 0.04f, Palette.Teal);
            ScreenText(screen, "Title", "VISITOR BADGE  -  INSPECTION", 0f, 0.14f, 0.48f, 0.04f, 0.11f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            ScreenText(screen, "Fields",
                "Company printed\nBadge type\nName\nOrganisation\nValid until\nEscort\nSecurity hologram",
                -0.23f, 0.108f, 0.17f, 0.18f, 0.1f, new Color(0.62f, 0.7f, 0.76f));
            ScreenText(screen, "Values",
                "APERTURE SYSTMES\nVISITOR\nJames Miller\nNetSecure Ltd (IT Contractor)\n14 MAR 2024\nNOT REQUIRED\nNone",
                -0.05f, 0.108f, 0.28f, 0.18f, 0.1f, Color.white, style: FontStyles.Bold);
            XRButton verify = Button(screen, "VerifyIdentityButton", "visitor_badge.verify_identity", "VERIFY IDENTITY",
                new Vector2(0f, -0.125f), new Vector2(0.24f, 0.04f), Palette.Teal, 0.095f);
            GameObject result = Page(screen, "VerificationResult", size, new Color(0.35f, 0.07f, 0.07f)).gameObject;
            ScreenText(result.transform, "Title", "VERIFICATION FAILED", 0f, 0.11f, 0.46f, 0.05f, 0.15f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            ScreenText(result.transform, "Body",
                "No visitor named James Miller and no company \"NetSecure Ltd\" is on today's visitor log.\n" +
                "The badge expired on 14 MAR 2024 and the company name is misspelled.\n\n" +
                "Badge flagged: reception and IT Security (ext. 4400) notified. Visitors must be escorted.",
                -0.22f, 0.075f, 0.44f, 0.2f, 0.095f, new Color(1f, 0.9f, 0.88f));
            Hint(inspector, "Hint_Trigger", TriggerHint, new Vector3(0f, 0.43f, 0.04f), 0.42f);

            Inject(scenario, ("_badge", badge.GetComponent<Grabbable>()), ("_pickUpHint", hint.gameObject),
                ("_inspectionPanel", inspector.gameObject), ("_verifyIdentityButton", verify), ("_verificationResult", result));
            PagesToHide.Add(inspector.gameObject);
        }

        // =====================================================================================================
        // Malicious USB
        // =====================================================================================================
        private static void BuildMaliciousUSB(Transform root, Transform usb, Transform counter, Transform desk, Transform deskScreen)
        {
            var scenario = Group("MaliciousUSBScenario", root, Vector3.zero).gameObject.AddComponent<MaliciousUSBScenario>();
            scenario.InjectObjectId(B.MaliciousUSBDrive);
            Material dark = Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f);
            Material metal = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);

            // Neutral hint at the lost & found tray.
            Transform lfHint = Anchor("USBChoiceHint", root, counter, new Vector3(0.85f, 1.255f, -0.17f), Quaternion.Euler(10f, 0f, 0f));
            Hint(lfHint, "Text", "Found something? Grab it with the GRIP button.\nTake it to a workstation dock or the IT drop-off.",
                Vector3.zero, 0.36f);

            // ---- Safe: IT Security drop-off on the service counter.
            Transform drop = Anchor("ITDropOffStation", root, counter, new Vector3(0.3f, 1.1f, -0.3f), Quaternion.identity);
            Box("DropBox", drop, new Vector3(0f, 0.04f, 0f), new Vector3(0.22f, 0.08f, 0.14f), Lit("Rack_Black", new Color(0.07f, 0.07f, 0.08f), 0.5f, 0.3f));
            Box("Slot", drop, new Vector3(0f, 0.081f, 0f), new Vector3(0.12f, 0.003f, 0.02f), Unlit("LED_Teal", Palette.Teal), castShadows: false);
            Collider submitZone = Zone(drop, "PlacementZone", new Vector3(0f, 0.15f, 0f), new Vector3(0.36f, 0.34f, 0.34f));
            Transform submitSnap = Group("SnapPoint", drop, new Vector3(0f, 0.0855f, 0f), Quaternion.Euler(0f, 90f, 0f));
            Box("StandNeck", drop, new Vector3(0f, 0.12f, 0.17f), new Vector3(0.03f, 0.08f, 0.02f), metal);
            Transform dropScreen = Screen("Panel", drop, new Vector3(0f, 0.27f, 0.16f), Quaternion.Euler(12f, 0f, 0f), new Vector2(0.36f, 0.22f),
                new Color(0.08f, 0.11f, 0.17f), out _, dark);
            ScreenRect(dropScreen, "Header", 0f, 0.09f, 0.36f, 0.04f, Palette.Teal);
            ScreenText(dropScreen, "Title", "SUSPICIOUS MEDIA DROP-OFF", 0f, 0.09f, 0.35f, 0.04f, 0.095f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            GameObject submitWaiting = ScreenText(dropScreen, "WaitingHint", "Place the USB drive on the drop-off box,\nthen press SUBMIT.",
                0f, 0.035f, 0.34f, 0.05f, 0.085f, HintTeal, TextAlignmentOptions.Center).gameObject;
            XRButton submit = Button(dropScreen, "SubmitToITButton", "usb_drive.submit_to_it", "SUBMIT TO IT SECURITY",
                new Vector2(0f, -0.045f), new Vector2(0.3f, 0.045f), Green, 0.09f);
            GameObject submitOutcome = Outcome(dropScreen, "Outcome_Submitted", new Vector2(0.36f, 0.22f), Green, "SUBMITTED - WELL DONE",
                "IT Security will analyse the drive in an isolated sandbox. Never plug in found USB devices.", 0.078f);
            Hint(drop, "Hint_Trigger", TriggerHint, new Vector3(0f, 0.43f, 0.18f), 0.4f);

            // ---- Unsafe: USB-C dock on workstation Desk_05.
            Transform dock = Anchor("WorkstationDockStation", root, desk, new Vector3(0.45f, 0.74f, 0.2f), Quaternion.identity);
            Box("Dock", dock, new Vector3(0f, 0.015f, 0f), new Vector3(0.16f, 0.03f, 0.08f), dark);
            Box("PortLED", dock, new Vector3(0.06f, 0.031f, -0.03f), new Vector3(0.01f, 0.002f, 0.006f), Unlit("LED_Green", new Color(0.2f, 0.95f, 0.45f)), castShadows: false);
            Collider connectZone = Zone(dock, "PlacementZone", new Vector3(0f, 0.14f, -0.02f), new Vector3(0.32f, 0.32f, 0.32f));
            Transform connectSnap = Group("SnapPoint", dock, new Vector3(0f, 0.031f, 0f), Quaternion.Euler(0f, 90f, 0f));
            Transform dockScreen = Screen("Panel", dock, new Vector3(0f, 0.3f, 0.1f), Quaternion.Euler(10f, 0f, 0f), new Vector2(0.28f, 0.18f),
                new Color(0.08f, 0.11f, 0.17f), out _, dark);
            ScreenText(dockScreen, "Title", "USB-C DOCK", 0f, 0.065f, 0.27f, 0.035f, 0.1f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            GameObject connectWaiting = ScreenText(dockScreen, "WaitingHint", "Place a USB drive on the dock,\nthen press CONNECT.",
                0f, 0.02f, 0.27f, 0.045f, 0.07f, HintTeal, TextAlignmentOptions.Center).gameObject;
            XRButton connect = Button(dockScreen, "ConnectUSBButton", "usb_drive.connect_usb", "CONNECT USB",
                new Vector2(0f, -0.045f), new Vector2(0.2f, 0.04f), Blue, 0.09f);
            GameObject connectOutcome = Outcome(dockScreen, "Outcome_Connected", new Vector2(0.28f, 0.18f), Red, "DRIVE CONNECTED",
                "Simulation: autorun malware launched from the drive.", 0.075f);
            Hint(dock, "Hint_Trigger", TriggerHint, new Vector3(0.05f, 0.44f, 0.12f), 0.36f);

            // Ransomware-style takeover of the Desk_05 monitor (simulated).
            Transform infected = Anchor("InfectedScreen", root, deskScreen, Vector3.zero, Quaternion.identity);
            GameObject infectedPage = Outcome(infected, "Overlay", new Vector2(0.58f, 0.33f), new Color(0.55f, 0.04f, 0.04f),
                "YOUR FILES HAVE BEEN ENCRYPTED",
                "Simulated ransomware from the found USB drive is encrypting this workstation and the shared drives.\n\n" +
                "Unknown USB devices are a classic baiting attack. Hand them to IT Security instead.", 0.1f);

            Inject(scenario, ("_usbPointable", usb.GetComponent<Grabbable>()), ("_usbCollider", usb.GetComponent<Collider>()),
                ("_connectZone", connectZone), ("_connectSnapPoint", connectSnap), ("_connectButton", connect),
                ("_connectWaitingHint", connectWaiting), ("_connectOutcome", connectOutcome), ("_infectedScreen", infectedPage),
                ("_submitZone", submitZone), ("_submitSnapPoint", submitSnap), ("_submitButton", submit),
                ("_submitWaitingHint", submitWaiting), ("_submitOutcome", submitOutcome));
        }

        // =====================================================================================================
        // Building blocks
        // =====================================================================================================
        /// <summary>World-space child of <paramref name="parent"/> posed relative to <paramref name="reference"/>.</summary>
        internal static Transform Anchor(string name, Transform parent, Transform reference, Vector3 localOffset, Quaternion localRotation)
        {
            Transform t = Group(name, parent, Vector3.zero);
            t.SetPositionAndRotation(reference.TransformPoint(localOffset), reference.rotation * localRotation);
            return t;
        }

        /// <summary>Full-screen overlay page (inactive), in front of everything already drawn on the screen.</summary>
        internal static Transform Page(Transform screen, string name, Vector2 size, Color background)
        {
            Transform page = Group(name, screen, new Vector3(0f, 0f, -0.008f));
            Panel("Background", page, Vector2.zero, size, Unlit($"Screen_{ColorUtility.ToHtmlStringRGB(background)}", background));
            PagesToHide.Add(page.gameObject);
            return page;
        }

        internal static GameObject Outcome(Transform screen, string name, Vector2 size, Color accent, string title, string body, float bodySize)
        {
            Transform page = Page(screen, name, size, new Color(0.08f, 0.1f, 0.14f));
            float bar = Mathf.Min(0.07f, size.y * 0.22f);
            ScreenRect(page, "TitleBar", 0f, size.y * 0.5f - bar * 0.5f, size.x, bar, accent);
            ScreenText(page, "Title", title, 0f, size.y * 0.5f - bar * 0.5f, size.x - 0.02f, bar, bodySize * 1.45f, Color.white,
                TextAlignmentOptions.Center, FontStyles.Bold);
            ScreenText(page, "Body", body, -size.x * 0.5f + 0.02f, size.y * 0.5f - bar - 0.015f, size.x - 0.04f, size.y - bar - 0.03f,
                bodySize, new Color(0.9f, 0.92f, 0.95f));
            return page.gameObject;
        }

        /// <summary>
        /// Ray-pressable button: outline (behind), coloured quad with a thin collider, RayInteractable + ColliderSurface,
        /// <see cref="XRButton"/>, and a label. <paramref name="z"/> is the quad's local depth (negative = toward viewer).
        /// </summary>
        internal static XRButton Button(Transform parent, string name, string id, string label, Vector2 center, Vector2 size, Color color,
            float fontSize, float z = -0.0036f)
        {
            GameObject outline = Quad(parent, $"{name}_Outline", center, size + new Vector2(0.012f, 0.012f), Color.white, z + 0.0006f);
            outline.GetComponent<Renderer>().enabled = false; // shown by XRButton on hover / when chosen
            GameObject quad = Quad(parent, name, center, size, color, z);
            ScreenText(parent, $"{name}_Label", label, center.x, center.y, size.x, size.y, fontSize, Color.white, TextAlignmentOptions.Center,
                FontStyles.Bold).transform.localPosition = new Vector3(center.x, center.y, z - 0.0015f);
            return MakeXRButton(quad, outline.GetComponent<Renderer>(), id, color);
        }

        /// <summary>Turns an existing on-screen button quad (Phase 2) into an XRButton; its relay keeps logging but stops tinting.</summary>
        private static XRButton UpgradeExistingButton(GameObject quad, Transform outlineParent, string id, Color color)
        {
            foreach (XRButton old in quad.GetComponents<XRButton>())
            {
                Object.DestroyImmediate(old);
            }
            if (quad.TryGetComponent(out PointableEventRelay relay))
            {
                relay.InjectOptionalRenderer(null);
            }
            Transform t = quad.transform;
            GameObject outline = Quad(outlineParent, $"{quad.name}_Outline", t.localPosition, (Vector2)t.localScale + new Vector2(0.012f, 0.012f),
                Color.white, t.localPosition.z + 0.0006f);
            outline.GetComponent<Renderer>().enabled = false; // shown by XRButton on hover / when chosen
            var button = quad.AddComponent<XRButton>();
            button.InjectAllXRButton(quad.GetComponentInChildren<RayInteractable>(true), quad.GetComponent<Renderer>(), id, color);
            button.InjectOptionalOutline(outline.GetComponent<Renderer>());
            return button;
        }

        private static XRButton MakeXRButton(GameObject quad, Renderer outline, string id, Color color)
        {
            var box = quad.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0f, -0.002f);
            box.size = new Vector3(1f, 1f, 0.002f);
            var interactableGo = new GameObject("RayInteractable");
            interactableGo.transform.SetParent(quad.transform, false);
            var surface = interactableGo.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(box);
            var ray = interactableGo.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(surface);
            var button = quad.AddComponent<XRButton>();
            button.InjectAllXRButton(ray, quad.GetComponent<Renderer>(), id, color);
            button.InjectOptionalOutline(outline);
            return button;
        }

        internal static GameObject Quad(Transform parent, string name, Vector2 center, Vector2 size, Color color, float z)
        {
            GameObject quad = Primitive(PrimitiveType.Quad, name, parent, new Vector3(center.x, center.y, z), new Vector3(size.x, size.y, 1f),
                Unlit($"Screen_{ColorUtility.ToHtmlStringRGB(color)}", color), false, false);
            return quad;
        }

        /// <summary>Trigger volume used to detect the USB drive (ignored by physics queries and locomotion).</summary>
        private static Collider Zone(Transform parent, string name, Vector3 center, Vector3 size)
        {
            Transform t = Group(name, parent, center);
            var box = t.gameObject.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true;
            return box;
        }

        /// <summary>Instruction label: dark plate + teal text, facing the user (local -Z).</summary>
        internal static Transform Hint(Transform parent, string name, string text, Vector3 localPosition, float width)
        {
            Transform hint = Group(name, parent, localPosition);
            Panel("Plate", hint, Vector2.zero, new Vector2(width, 0.062f), Unlit("Hint_Plate", new Color(0.06f, 0.09f, 0.14f)));
            Text("Text", hint, text, new Vector3(0f, 0f, -0.002f), 0.105f, HintTeal, new Vector2(width - 0.02f, 0.056f));
            return hint;
        }

        internal static void Inject(Object target, params (string field, Object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach ((string field, Object value) in values)
            {
                SerializedProperty property = so.FindProperty(field);
                if (property == null)
                {
                    throw new InvalidOperationException($"{target.GetType().Name} has no serialized field '{field}'.");
                }
                property.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform Root(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(r => r.name == name)?.transform
            ?? throw new InvalidOperationException($"Scene root '{name}' not found. Build the corporate office first.");

        private static Transform Find(Transform parent, string path) =>
            parent.Find(path) ?? throw new InvalidOperationException($"'{parent.name}/{path}' not found in the office scene.");
    }
}
