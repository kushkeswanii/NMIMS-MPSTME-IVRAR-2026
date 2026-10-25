using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.OVR.Editor.QuickActions;
using Oculus.Interaction.Surfaces;
using TMPro;
using TrustBreach.Core;
using TrustBreach.Interactions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static TrustBreach.Editor.OfficeBuildKit;
using Object = UnityEngine.Object;

namespace TrustBreach.Editor
{
    /// <summary>
    /// Idempotent builder for the Phase 2 "Aperture Systems" corporate office scene. Every run regenerates the scene
    /// from an empty scene (no duplicates), reuses/updates the palette materials under Materials/Office, and creates
    /// the XR rig with the same Meta Quick Actions API as the Phase 1 foundation. Phase 1 assets are not modified.
    ///
    /// Floor plan (metres; +Z = into the building from the entrance, spawn at (0, 0, -1) facing +Z):
    ///   Reception  x[-9, 9]  z[-2, 4]     entrance glass at z = -2, brand wall + speed gates at z = 4
    ///   Workspace  x[-9, 2]  z[4, 9.75]   open-plan pods (hero phishing desk nearest the hallway)
    ///   Meeting    x[-9, 2]  z[9.75, 14]  glass meeting room + executive corner
    ///   Hallway    x[2, 5]   z[4, 14]     spine connecting every zone
    ///   IT desk    x[5, 9]   z[4, 8.6]    service counter, identity verification, password-reset kiosk
    /// </summary>
    public static class TrustBreachCorporateOfficeBuilder
    {
        public const string ScenePath = "Assets/TrustBreach/Scenes/TrustBreach_CorporateOffice.unity";

        // Root names (validated by CorporateOfficeValidator).
        public const string XRRigRootName = "XR_Rig_Root";
        public const string EnvironmentRootName = "Environment_Office";
        public const string ScenarioRootName = "Scenario_Objects";
        public const string LightingRootName = "Lighting";
        public const string ManagersRootName = "Managers";
        public const string UIRootName = "UI";
        public static readonly string[] ZoneNames = { "Reception", "Workstations", "ITSecurityDesk", "MeetingArea", "Hallway", "BuildingShell" };

        // Scenario artifact names (also their telemetry ids).
        public const string SpoofedVisitorBadge = "SpoofedVisitorBadge";
        public const string MaliciousUSBDrive = "MaliciousUSBDrive";
        public const string PhishingEmailWorkstation = "PhishingEmailWorkstation";
        public const string FakeSSLTerminal = "FakeSSLTerminal";
        public const string PasswordStickyNote = "PasswordStickyNote";
        public const string UnattendedConfidentialPrintout = "UnattendedConfidentialPrintout";
        public const string EmployeeBadgeReference = "EmployeeBadge_Reference";
        public const string PhishingLinkButton = "PhishingLink_VerifyButton";
        public const string FakeSSLSignInButton = "FakeSSL_SignInButton";

        public static readonly Vector3 SpawnPosition = new Vector3(0f, 0f, -1f);
        private const float CeilingHeight = 3.0f;

        [MenuItem("Tools/TRUST∕∕BREACH/Build Corporate Office Scene", priority = 100)]
        [MenuItem("TrustBreach/Phase 2/Build Corporate Office Scene", priority = 100)]
        private static void BuildFromMenu()
        {
            XRFoundationValidator.Report report = Build();
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        /// <summary>Entry point for -executeMethod. Exits with code 1 if validation fails.</summary>
        public static void BuildFromCommandLine()
        {
            XRFoundationValidator.Report report = Build();
            Debug.Log(report);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.Success ? 0 : 1);
            }
        }

        public static XRFoundationValidator.Report Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before building the corporate office scene.");
            }
            if (TMP_Settings.defaultFontAsset == null)
            {
                throw new InvalidOperationException("TextMesh Pro Essential Resources are missing (Window > TextMeshPro > Import TMP Essential Resources).");
            }

            EnsureFolder(MaterialsFolder);
            ResetCache();
            XRFoundationSceneBuilder.BackupDirtyScenes();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Transform xrRoot = new GameObject(XRRigRootName).transform;
            Transform environment = new GameObject(EnvironmentRootName).transform;
            Transform scenario = new GameObject(ScenarioRootName).transform;
            Transform lighting = new GameObject(LightingRootName).transform;
            new GameObject(ManagersRootName); // reserved for Phase 3 scenario / dialogue / telemetry managers
            Transform ui = new GameObject(UIRootName).transform;

            Transform shell = Group("BuildingShell", environment, Vector3.zero);
            Transform reception = Group("Reception", environment, Vector3.zero);
            Transform workstations = Group("Workstations", environment, Vector3.zero);
            Transform itDesk = Group("ITSecurityDesk", environment, Vector3.zero);
            Transform meeting = Group("MeetingArea", environment, Vector3.zero);
            Transform hallway = Group("Hallway", environment, Vector3.zero);

            BuildShell(shell);
            BuildReception(reception);
            BuildWorkstations(workstations, out Transform heroDesk);
            BuildMeetingArea(meeting);
            BuildHallway(hallway);
            BuildITSecurityDesk(itDesk, out Transform itCounter);
            foreach (Transform zone in environment)
            {
                MarkStatic(zone);
            }

            BuildScenarioObjects(scenario, heroDesk, itCounter);
            BuildLighting(lighting);
            BuildOrientationPanel(ui);
            BuildXRRig(xrRoot);
            OfficeInteractionsBuilder.Apply(scene); // Phase 2.5 decision layer, so a full rebuild keeps the interactions

            Selection.activeObject = null;
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException($"Failed to save {ScenePath}");
            }
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();
            XRFoundationValidator.Report report = CorporateOfficeValidator.Validate(scene);
            report.Merge(OfficeInteractionsValidator.Validate(scene));
            if (scene.isDirty)
            {
                EditorSceneManager.SaveScene(scene); // the interaction validator reveals hidden pages temporarily
            }
            return report;
        }

        // =====================================================================================================
        // Building shell: floors, ceiling, exterior walls, entrance, exterior plaza
        // =====================================================================================================
        private static void BuildShell(Transform shell)
        {
            Material stone = Lit("Floor_Stone", new Color(0.78f, 0.76f, 0.72f), 0.55f);
            Material carpet = Lit("Floor_Carpet", new Color(0.29f, 0.32f, 0.36f), 0.08f);
            Material carpetDark = Lit("Floor_CarpetDark", new Color(0.22f, 0.24f, 0.28f), 0.08f);
            Material concrete = Lit("Floor_PolishedConcrete", new Color(0.6f, 0.61f, 0.62f), 0.6f);
            Material wallWhite = Lit("Wall_White", Palette.OffWhite, 0.15f);
            Material ceiling = Lit("Ceiling_White", new Color(0.96f, 0.96f, 0.95f), 0.05f);

            // Separate floor slabs per zone keep URP's per-object light limit effective and let zones read differently.
            Floor("Floor_Reception", shell, -9f, 9f, -2f, 4f, stone);
            Floor("Floor_Workstations", shell, -9f, 2f, 4f, 9.75f, carpet);
            Floor("Floor_Meeting", shell, -9f, 2f, 9.75f, 14f, carpetDark);
            Floor("Floor_Hallway", shell, 2f, 5f, 4f, 14f, concrete);
            Floor("Floor_ITSecurity", shell, 5f, 9f, 4f, 14f, concrete);

            GameObject ceilingSlab = Box("Ceiling", shell, new Vector3(0f, CeilingHeight + 0.05f, 6f), new Vector3(18.3f, 0.1f, 16.3f), ceiling,
                collider: false, castShadows: false); // non-shadowing so the sun reads as soft interior top light
            ceilingSlab.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Exterior: storefront glass (entrance) with doors, windows on the west and north, solid party wall east.
            Transform front = Group("Facade_Entrance", shell, Vector3.zero);
            GlassWall("Storefront_West", front, new Vector2(-9f, -2f), new Vector2(-1.1f, -2f), CeilingHeight, frostedBand: false);
            GlassWall("Storefront_East", front, new Vector2(1.1f, -2f), new Vector2(9f, -2f), CeilingHeight, frostedBand: false);
            Material doorFrame = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);
            Box("DoorHeader", front, new Vector3(0f, 2.55f, -2f), new Vector3(2.3f, 0.9f, 0.12f), doorFrame, collider: true);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.55f : 0.55f;
                Box($"EntranceDoor{i}", front, new Vector3(x, 1.05f, -2f), new Vector3(1.08f, 2.1f, 0.03f),
                    Transparent("Glass_Clear", new Color(0.78f, 0.88f, 0.94f, 0.16f)), collider: true, castShadows: false);
                Box($"DoorHandle{i}", front, new Vector3(x + (i == 0 ? 0.42f : -0.42f), 1.05f, -2.04f), new Vector3(0.03f, 0.9f, 0.03f),
                    Lit("Metal_Brushed", new Color(0.75f, 0.76f, 0.78f), 0.7f, 1f));
            }
            Text("EntranceVinyl", front, "APERTURE SYSTEMS", new Vector3(0f, 2.45f, -2.07f), 0.9f, Color.white, new Vector2(2.2f, 0.3f),
                style: FontStyles.Bold);

            GlassWall("Windows_West", shell, new Vector2(-9f, -2f), new Vector2(-9f, 9.75f), CeilingHeight, frostedBand: false);
            GlassWall("Windows_North", shell, new Vector2(-9f, 14f), new Vector2(9f, 14f), CeilingHeight, frostedBand: false);
            SolidWall("Wall_East", shell, new Vector2(9f, -2f), new Vector2(9f, 14f), CeilingHeight, wallWhite);

            // Exterior plaza visible through the glass.
            Box("Exterior_Plaza", shell, new Vector3(0f, -0.06f, 4f), new Vector3(70f, 0.1f, 70f),
                Lit("Exterior_Paving", new Color(0.5f, 0.5f, 0.49f), 0.25f), collider: true);
            for (int i = 0; i < 4; i++)
            {
                Plant($"Exterior_Tree{i}", shell, new Vector3(-7.5f + i * 5f, 0f, -4.5f), 2.2f);
            }
        }

        private static void Floor(string name, Transform parent, float x0, float x1, float z0, float z1, Material material)
        {
            Box(name, parent, new Vector3((x0 + x1) * 0.5f, -0.05f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.1f, z1 - z0), material,
                collider: true, castShadows: false);
        }

        // =====================================================================================================
        // Reception / entrance
        // =====================================================================================================
        private static void BuildReception(Transform zone)
        {
            Material navy = Lit("Wall_BrandNavy", Palette.Navy, 0.3f);
            Material oak = Lit("Wood_Oak", Palette.Oak, 0.45f);
            Material stoneWhite = Lit("Desk_StoneWhite", new Color(0.94f, 0.93f, 0.91f), 0.6f);
            Material tealGlow = Unlit("LED_Teal", Palette.Teal);
            Material wallWhite = Lit("Wall_White", Palette.OffWhite, 0.15f);
            Material fabricGrey = Lit("Fabric_Grey", new Color(0.36f, 0.38f, 0.41f), 0.1f);
            Material fabricTeal = Lit("Fabric_Teal", new Color(0.1f, 0.42f, 0.44f), 0.1f);
            Material metal = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);

            // ---- Brand feature wall (z = 4, x[-2.5, 2]) with aperture logo
            Transform brand = Group("BrandWall", zone, new Vector3(-0.25f, 0f, 4f));
            Box("WallPanel", brand, new Vector3(0f, 1.5f, 0f), new Vector3(4.5f, CeilingHeight, 0.15f), navy, collider: true);
            for (int i = 0; i < 9; i++)
            {
                Box($"SlatL{i}", brand, new Vector3(-2.15f + i * 0.09f, 1.5f, -0.1f), new Vector3(0.045f, CeilingHeight, 0.05f), oak);
                Box($"SlatR{i}", brand, new Vector3(1.43f + i * 0.09f, 1.5f, -0.1f), new Vector3(0.045f, CeilingHeight, 0.05f), oak);
            }
            Transform logo = Group("ApertureLogo", brand, new Vector3(0f, 2.42f, -0.09f));
            for (int i = 0; i < 6; i++)
            {
                Quaternion r = Quaternion.Euler(0f, 0f, i * 60f);
                GameObject blade = Box($"Blade{i}", logo, r * new Vector3(0.06f, 0.15f, 0f), new Vector3(0.3f, 0.06f, 0.02f), tealGlow);
                blade.transform.localRotation = r * Quaternion.Euler(0f, 0f, -20f);
            }
            Text("BrandName", brand, "APERTURE SYSTEMS", new Vector3(0f, 1.95f, -0.09f), 1.9f, Color.white, new Vector2(4.2f, 0.4f),
                style: FontStyles.Bold);
            Text("BrandTagline", brand, "Secure infrastructure. Human intelligence.", new Vector3(0f, 1.6f, -0.09f), 0.7f,
                new Color(0.72f, 0.82f, 0.86f), new Vector2(4.2f, 0.2f));

            // ---- Reception desk (visitors at -Z)
            Transform desk = Group("ReceptionDesk", zone, new Vector3(0f, 0f, 2.4f));
            Box("FrontPanel", desk, new Vector3(0f, 0.55f, -0.3f), new Vector3(3.0f, 1.1f, 0.08f), stoneWhite);
            Box("AccentLED", desk, new Vector3(0f, 0.12f, -0.345f), new Vector3(3.0f, 0.025f, 0.01f), tealGlow, castShadows: false);
            Box("TopLedge", desk, new Vector3(0f, 1.12f, -0.25f), new Vector3(3.1f, 0.04f, 0.34f), oak);
            Box("SideL", desk, new Vector3(-1.5f, 0.55f, 0.05f), new Vector3(0.08f, 1.1f, 0.7f), stoneWhite);
            Box("SideR", desk, new Vector3(1.5f, 0.55f, 0.05f), new Vector3(0.08f, 1.1f, 0.7f), stoneWhite);
            Box("Worktop", desk, new Vector3(0f, 0.73f, 0.2f), new Vector3(2.95f, 0.03f, 0.6f), oak);
            Text("DeskLabel", desk, "Reception  |  Visitor Check-in", new Vector3(0f, 0.8f, -0.346f), 0.6f, Palette.Navy,
                new Vector2(2.8f, 0.15f));
            FitCollider(desk);

            Monitor("ReceptionMonitor", desk, new Vector3(0.55f, 0.745f, 0.25f), Quaternion.Euler(0f, 180f, 0f), new Vector2(0.52f, 0.3f),
                new Color(0.93f, 0.95f, 0.97f), out _);
            TaskChair("ReceptionChair", desk, new Vector3(0f, 0f, 0.8f), Vector3.back, fabricTeal);

            // Visitor sign-in tablet on the ledge, angled toward visitors.
            Transform tablet = Screen("VisitorSignInTablet", desk, new Vector3(0.75f, 1.24f, -0.27f), Quaternion.Euler(25f, 0f, 0f),
                new Vector2(0.24f, 0.17f), Palette.Navy, out _, metal);
            ScreenText(tablet, "Title", "Visitor Sign-In", 0f, 0.05f, 0.22f, 0.04f, 0.17f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            ScreenText(tablet, "Prompt", "Tap to register your visit.\nAll visitors must be escorted.", 0f, -0.01f, 0.22f, 0.07f, 0.1f,
                new Color(0.75f, 0.85f, 0.9f), TextAlignmentOptions.Center);
            Box("TabletStand", desk, new Vector3(0.75f, 1.17f, -0.25f), new Vector3(0.08f, 0.1f, 0.06f), metal);

            // Visitor badge tray (the spoofed badge is placed on top of it by the scenario builder).
            Box("BadgeTray", desk, new Vector3(-0.9f, 1.148f, -0.27f), new Vector3(0.22f, 0.015f, 0.15f),
                Lit("Acrylic_Smoke", new Color(0.2f, 0.22f, 0.25f), 0.85f));
            for (int i = 0; i < 2; i++)
            {
                Box($"BlankVisitorBadge{i}", desk, new Vector3(-0.83f, 1.158f + i * 0.004f, -0.27f), new Vector3(0.055f, 0.003f, 0.085f),
                    Lit("Badge_White", new Color(0.97f, 0.97f, 0.96f), 0.4f));
            }
            Transform traySign = Group("BadgeTraySign", desk, new Vector3(-0.9f, 1.19f, -0.18f));
            Box("Acrylic", traySign, Vector3.zero, new Vector3(0.22f, 0.09f, 0.006f), Lit("Acrylic_Smoke", new Color(0.2f, 0.22f, 0.25f), 0.85f));
            Text("Label", traySign, "VISITOR BADGES\nCollect from reception", new Vector3(0f, 0f, -0.005f), 0.1f, Color.white,
                new Vector2(0.21f, 0.08f));
            Plant("ReceptionOrchid", desk, new Vector3(1.32f, 1.14f, -0.25f), 0.3f);

            // ---- Speed gates (access control) across the hallway mouth, x[2, 5] at z = 4
            Transform gates = Group("AccessGates", zone, new Vector3(3.5f, 0f, 4f));
            Material glassFlap = Transparent("Glass_Frosted", new Color(0.95f, 0.96f, 0.97f, 0.55f), 0.4f);
            Material readerGlow = Unlit("LED_Green", new Color(0.2f, 0.95f, 0.45f));
            foreach (float x in new[] { -1.375f, 0f, 1.375f })
            {
                Transform cab = Group($"GateCabinet_{(x < 0 ? "W" : x > 0 ? "E" : "C")}", gates, new Vector3(x, 0f, 0f));
                Box("Body", cab, new Vector3(0f, 0.5f, 0f), new Vector3(0.25f, 1.0f, 1.4f), Lit("Metal_Brushed", new Color(0.75f, 0.76f, 0.78f), 0.7f, 1f));
                Box("TopGlass", cab, new Vector3(0f, 1.005f, 0f), new Vector3(0.26f, 0.01f, 1.41f), Lit("Acrylic_Smoke", new Color(0.2f, 0.22f, 0.25f), 0.85f));
                Box("BadgeReader", cab, new Vector3(0f, 1.02f, -0.55f), new Vector3(0.16f, 0.02f, 0.16f), Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
                Box("ReaderLED", cab, new Vector3(0f, 1.032f, -0.55f), new Vector3(0.1f, 0.004f, 0.1f), readerGlow, castShadows: false);
                if (x < 0.5f) Box("FlapE", cab, new Vector3(0.16f, 0.85f, 0.2f), new Vector3(0.015f, 0.45f, 0.55f), glassFlap, castShadows: false);
                if (x > -0.5f) Box("FlapW", cab, new Vector3(-0.16f, 0.85f, 0.2f), new Vector3(0.015f, 0.45f, 0.55f), glassFlap, castShadows: false);
                cab.gameObject.AddComponent<BoxCollider>().center = new Vector3(0f, 0.5f, 0f);
                cab.GetComponent<BoxCollider>().size = new Vector3(0.25f, 1.0f, 1.4f);
            }
            Transform gateSign = Group("StaffOnlySign", gates, new Vector3(0f, 2.55f, 0f));
            Box("Panel", gateSign, Vector3.zero, new Vector3(2.9f, 0.42f, 0.05f), navy);
            Text("Text", gateSign, "STAFF ONLY BEYOND THIS POINT\n<size=60%>Badge access required  |  Visitors must be escorted</size>",
                new Vector3(0f, 0f, -0.03f), 0.9f, Color.white, new Vector2(2.8f, 0.38f), style: FontStyles.Bold);
            Box("SignRodL", gateSign, new Vector3(-1.2f, 0.33f, 0f), new Vector3(0.015f, 0.24f, 0.015f), metal);
            Box("SignRodR", gateSign, new Vector3(1.2f, 0.33f, 0f), new Vector3(0.015f, 0.24f, 0.015f), metal);

            // ---- Partitions along z = 4: glass to the workspace (west), solid wall to IT (east)
            GlassWall("Partition_LobbyWorkspace", zone, new Vector2(-9f, 4f), new Vector2(-2.5f, 4f), CeilingHeight);
            SolidWall("Wall_LobbyIT", zone, new Vector2(5f, 4f), new Vector2(9f, 4f), CeilingHeight, wallWhite);
            Transform policy = Group("VisitorPolicyPoster", zone, new Vector3(7f, 1.55f, 3.91f));
            Box("Frame", policy, Vector3.zero, new Vector3(1.1f, 1.0f, 0.03f), metal);
            Box("Paper", policy, new Vector3(0f, 0f, -0.017f), new Vector3(1.02f, 0.92f, 0.004f), Lit("Paper_White", new Color(0.97f, 0.97f, 0.95f), 0.1f));
            Text("Title", policy, "VISITOR SECURITY POLICY", new Vector3(0f, 0.36f, -0.022f), 0.55f, Palette.Navy, new Vector2(0.95f, 0.1f),
                style: FontStyles.Bold);
            Text("Body", policy,
                "1. Sign in at reception and show photo ID.\n" +
                "2. Wear your visitor badge visibly at all times.\n" +
                "3. Visitors must be escorted beyond the gates.\n" +
                "4. Never lend your badge or hold the gate open.\n" +
                "5. Report unescorted visitors to IT Security (ext. 4400).",
                new Vector3(0f, -0.08f, -0.022f), 0.36f, Palette.ScreenText, new Vector2(0.95f, 0.7f), TextAlignmentOptions.TopLeft);

            // ---- Visitor waiting area (west)
            Transform waiting = Group("WaitingArea", zone, Vector3.zero);
            Box("Rug", waiting, new Vector3(-6.6f, 0.005f, 1.0f), new Vector3(3.6f, 0.01f, 3.0f), Lit("Rug_Slate", new Color(0.25f, 0.3f, 0.36f), 0.05f),
                castShadows: false);
            Sofa("Sofa", waiting, new Vector3(-8.3f, 0f, 1.0f), Vector3.right, 2.1f, fabricGrey);
            Sofa("Armchair_N", waiting, new Vector3(-5.0f, 0f, 1.8f), Vector3.left, 0.95f, fabricTeal);
            Sofa("Armchair_S", waiting, new Vector3(-5.0f, 0f, 0.2f), Vector3.left, 0.95f, fabricTeal);
            Transform table = Group("CoffeeTable", waiting, new Vector3(-6.6f, 0f, 1.0f));
            Box("Top", table, new Vector3(0f, 0.4f, 0f), new Vector3(0.7f, 0.04f, 1.2f), Lit("Wood_Walnut", Palette.Walnut, 0.5f));
            Box("Base", table, new Vector3(0f, 0.19f, 0f), new Vector3(0.5f, 0.38f, 1.0f), metal);
            Box("Brochures", table, new Vector3(0.1f, 0.43f, -0.25f), new Vector3(0.21f, 0.015f, 0.297f), Lit("Brochure_Teal", Palette.Teal, 0.3f));
            Box("Magazine", table, new Vector3(-0.12f, 0.426f, 0.2f), new Vector3(0.21f, 0.008f, 0.28f), Lit("Paper_White", new Color(0.97f, 0.97f, 0.95f), 0.1f));
            FitCollider(table);
            Transform lamp = Group("FloorLamp", waiting, new Vector3(-8.4f, 0f, 2.55f));
            Cylinder("Base", lamp, new Vector3(0f, 0.01f, 0f), 0.3f, 0.02f, metal);
            Cylinder("Pole", lamp, new Vector3(0f, 0.75f, 0f), 0.03f, 1.5f, metal);
            Cylinder("Shade", lamp, new Vector3(0f, 1.55f, 0f), 0.4f, 0.3f, Unlit("Lampshade_Warm", new Color(1f, 0.92f, 0.78f)));
            FitCollider(lamp);
            Plant("Plant_WaitingSW", waiting, new Vector3(-8.5f, 0f, -1.5f), 1.1f);
            Plant("Plant_WaitingNE", waiting, new Vector3(-3.0f, 0f, 3.5f), 1.0f);

            // ---- East lobby: bench + planters along the party wall
            Sofa("LobbyBench", zone, new Vector3(8.45f, 0f, 1.0f), Vector3.left, 1.8f, fabricGrey);
            Plant("Plant_LobbyE1", zone, new Vector3(8.5f, 0f, -1.4f), 1.1f);
            Plant("Plant_LobbyE2", zone, new Vector3(8.5f, 0f, 3.3f), 1.0f);
            Plant("Plant_Gate", zone, new Vector3(1.6f, 0f, 3.4f), 0.9f);
        }

        // =====================================================================================================
        // Open-plan workstations
        // =====================================================================================================
        private static void BuildWorkstations(Transform zone, out Transform heroDesk)
        {
            heroDesk = null;
            Material deskTop = Lit("Desk_WhiteLaminate", new Color(0.92f, 0.92f, 0.9f), 0.35f);
            Material frame = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);
            Material divider = Lit("Fabric_Teal", new Color(0.1f, 0.42f, 0.44f), 0.1f);
            Material chairFabric = Lit("Fabric_Charcoal", new Color(0.2f, 0.21f, 0.23f), 0.1f);

            var pods = new[] { new Vector2(-6.2f, 6.9f), new Vector2(-1.6f, 6.9f) };
            int deskIndex = 0;
            for (int p = 0; p < pods.Length; p++)
            {
                Transform pod = Group($"Pod_{(char)('A' + p)}", zone, new Vector3(pods[p].x, 0f, pods[p].y));
                Box("PrivacyDivider", pod, new Vector3(0f, 0.97f, 0f), new Vector3(2.9f, 0.48f, 0.03f), divider, collider: true);
                foreach (int sx in new[] { -1, 1 })
                {
                    foreach (int sz in new[] { -1, 1 })
                    {
                        bool isHero = p == 1 && sx == 1 && sz == -1; // nearest the hallway, user faces +Z
                        string name = isHero ? "Desk_Hero" : $"Desk_{++deskIndex:00}";
                        Vector3 userDir = sz < 0 ? Vector3.back : Vector3.forward; // user sits on the outside of the pod
                        Transform desk = Desk(name, pod, new Vector3(sx * 0.72f, 0f, sz * 0.4f), Facing(userDir), new Vector2(1.4f, 0.75f), deskTop, frame);
                        TaskChair("Chair", desk, new Vector3(0f, 0f, -0.68f), Vector3.forward, chairFabric);
                        if (isHero)
                        {
                            heroDesk = desk; // monitor/keyboard are placed by the scenario builder
                            DeskClutter(desk, hero: true);
                            continue;
                        }
                        Transform screen = Monitor("Monitor", desk, new Vector3(0f, 0.74f, 0.17f), Quaternion.identity, new Vector2(0.58f, 0.33f),
                            new Color(0.95f, 0.96f, 0.97f), out _);
                        GenericSpreadsheet(screen, deskIndex);
                        KeyboardAndMouse(desk, new Vector3(0f, 0.74f, -0.12f), Quaternion.identity);
                        DeskClutter(desk, hero: false, variant: deskIndex);
                    }
                }
            }

            // Print station with a confidential printout left in the tray (scenario builder adds the artifact).
            Transform print = Group("PrintStation", zone, new Vector3(-8.3f, 0f, 4.75f), Facing(Vector3.right));
            Box("Cabinet", print, new Vector3(0f, 0.38f, 0f), new Vector3(0.7f, 0.76f, 0.55f), Lit("Desk_WhiteLaminate", new Color(0.92f, 0.92f, 0.9f), 0.35f));
            Box("Printer", print, new Vector3(0f, 0.96f, 0f), new Vector3(0.55f, 0.4f, 0.48f), Lit("Printer_Grey", new Color(0.78f, 0.79f, 0.8f), 0.4f));
            Box("OutputTray", print, new Vector3(0f, 0.9f, -0.27f), new Vector3(0.36f, 0.02f, 0.12f), frame);
            Box("ControlPanel", print, new Vector3(0.15f, 1.17f, -0.2f), new Vector3(0.18f, 0.01f, 0.08f), Unlit("Screen_Panel", new Color(0.3f, 0.6f, 0.85f)));
            FitCollider(print);

            Transform storage = Group("StorageCredenza", zone, new Vector3(-5.5f, 0f, 9.45f));
            Box("Body", storage, new Vector3(0f, 0.38f, 0f), new Vector3(4.5f, 0.76f, 0.45f), Lit("Wood_Oak", Palette.Oak, 0.45f));
            for (int i = 0; i < 6; i++)
            {
                Box($"Handle{i}", storage, new Vector3(-1.9f + i * 0.76f, 0.62f, -0.235f), new Vector3(0.18f, 0.015f, 0.015f), frame);
            }
            FitCollider(storage);
            Plant("Plant_Window1", zone, new Vector3(-8.55f, 0f, 6.2f), 1.1f);
            Plant("Plant_Window2", zone, new Vector3(-8.55f, 0f, 8.6f), 0.9f);
            Plant("Plant_Storage", storage, new Vector3(2.0f, 0.76f, 0f), 0.35f);
        }

        private static void GenericSpreadsheet(Transform screen, int seed)
        {
            ScreenRect(screen, "TitleBar", 0f, 0.15f, 0.58f, 0.03f, new Color(0.13f, 0.45f, 0.35f));
            ScreenText(screen, "Title", seed % 2 == 0 ? "Q4_Forecast.xlsx" : "Project Tracker", -0.27f, 0.163f, 0.4f, 0.03f, 0.1f, Color.white);
            for (int r = 0; r < 8; r++)
            {
                Color c = r % 2 == 0 ? new Color(0.88f, 0.9f, 0.92f) : new Color(0.97f, 0.97f, 0.98f);
                ScreenRect(screen, $"Row{r}", 0f, 0.1f - r * 0.032f, 0.54f, 0.026f, c);
            }
            ScreenRect(screen, "Chart", 0.17f, -0.1f, 0.18f, 0.08f, new Color(0.2f, 0.55f, 0.85f), 2);
        }

        private static void DeskClutter(Transform desk, bool hero, int variant = 0)
        {
            Material mug = Lit("Ceramic_White", new Color(0.95f, 0.95f, 0.94f), 0.7f);
            Material paper = Lit("Paper_White", new Color(0.97f, 0.97f, 0.95f), 0.1f);
            Cylinder("Mug", desk, new Vector3(0.5f, 0.79f, -0.05f), 0.08f, 0.1f, variant % 3 == 0 ? Lit("Ceramic_Teal", Palette.Teal, 0.7f) : mug);
            Box("Papers", desk, new Vector3(-0.48f, 0.745f, -0.1f), new Vector3(0.21f, 0.01f, 0.297f), paper).transform.localRotation =
                Quaternion.Euler(0f, 8f + variant * 5f, 0f);
            if (hero || variant % 2 == 1)
            {
                Transform phone = Group("DeskPhone", desk, new Vector3(-0.5f, 0.74f, 0.2f));
                Box("Body", phone, new Vector3(0f, 0.03f, 0f), new Vector3(0.2f, 0.06f, 0.18f), Lit("Peripheral_Dark", new Color(0.12f, 0.12f, 0.14f), 0.4f));
                Box("Display", phone, new Vector3(0f, 0.062f, 0.02f), new Vector3(0.09f, 0.004f, 0.04f), Unlit("Screen_Panel", new Color(0.3f, 0.6f, 0.85f)));
            }
            if (hero)
            {
                Transform photo = Group("PhotoFrame", desk, new Vector3(0.38f, 0.74f, 0.25f), Quaternion.Euler(-10f, -15f, 0f));
                Box("Frame", photo, new Vector3(0f, 0.08f, 0f), new Vector3(0.15f, 0.12f, 0.015f), Lit("Wood_Walnut", Palette.Walnut, 0.5f));
                Box("Photo", photo, new Vector3(0f, 0.08f, -0.009f), new Vector3(0.12f, 0.09f, 0.002f), Lit("Photo_Print", new Color(0.55f, 0.65f, 0.72f), 0.6f));
            }
        }

        // =====================================================================================================
        // Meeting room + executive corner
        // =====================================================================================================
        private static void BuildMeetingArea(Transform zone)
        {
            Material walnut = Lit("Wood_Walnut", Palette.Walnut, 0.5f);
            Material metal = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);
            Material leather = Lit("Leather_Black", new Color(0.08f, 0.08f, 0.09f), 0.55f);
            Material fabricTeal = Lit("Fabric_Teal", new Color(0.1f, 0.42f, 0.44f), 0.1f);

            GlassWall("Partition_MeetingWorkspace", zone, new Vector2(-9f, 9.75f), new Vector2(2f, 9.75f), CeilingHeight);
            GlassWall("Partition_MeetingHall_S", zone, new Vector2(2f, 9.75f), new Vector2(2f, 10.3f), CeilingHeight, mullionSpacing: 0.6f);
            GlassWall("Partition_MeetingHall_N", zone, new Vector2(2f, 11.4f), new Vector2(2f, 14f), CeilingHeight);
            Box("DoorHeader", zone, new Vector3(2f, 2.6f, 10.85f), new Vector3(0.12f, 0.8f, 1.1f), metal, collider: true);
            Transform roomSign = Group("MeetingRoomSign", zone, new Vector3(2.08f, 2.05f, 10.0f), Facing(Vector3.right));
            Box("Plate", roomSign, Vector3.zero, new Vector3(0.42f, 0.16f, 0.01f), Lit("Wall_BrandNavy", Palette.Navy, 0.3f));
            Text("Text", roomSign, "MEETING ROOM 1\n<size=70%>Ada Lovelace</size>", new Vector3(0f, 0f, -0.007f), 0.22f, Color.white,
                new Vector2(0.4f, 0.14f));

            // Feature wall on the west with the presentation screen (replaces glazing for this bay).
            SolidWall("Wall_MeetingWest", zone, new Vector2(-8.92f, 9.75f), new Vector2(-8.92f, 14f), CeilingHeight, walnut, 0.12f);
            Transform tv = Screen("PresentationScreen", zone, new Vector3(-8.83f, 1.55f, 11.9f), Facing(Vector3.right),
                new Vector2(1.6f, 0.9f), Palette.Navy, out _, Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            ScreenText(tv, "Title", "Q4 Strategy Review", -0.72f, 0.36f, 1.4f, 0.12f, 0.75f, Color.white, style: FontStyles.Bold);
            ScreenText(tv, "Agenda", "14:00  Infrastructure roadmap\n14:30  Vendor access review\n15:00  Budget sign-off",
                -0.72f, 0.18f, 1.0f, 0.4f, 0.42f, new Color(0.78f, 0.86f, 0.9f));
            ScreenText(tv, "Guest", "External guest: D. Price (NetSecure Ltd) - escort: TBC", -0.72f, -0.3f, 1.5f, 0.08f, 0.36f,
                Palette.WarningAmber);

            Transform table = Group("MeetingTable", zone, new Vector3(-5.2f, 0f, 11.9f));
            Box("Top", table, new Vector3(0f, 0.74f, 0f), new Vector3(3.0f, 0.04f, 1.2f), walnut);
            Box("PedestalW", table, new Vector3(-0.9f, 0.36f, 0f), new Vector3(0.12f, 0.72f, 0.7f), metal);
            Box("PedestalE", table, new Vector3(0.9f, 0.36f, 0f), new Vector3(0.12f, 0.72f, 0.7f), metal);
            Cylinder("ConferencePhone", table, new Vector3(0f, 0.775f, 0f), 0.24f, 0.03f, Lit("Peripheral_Dark", new Color(0.12f, 0.12f, 0.14f), 0.4f));
            for (int i = 0; i < 3; i++)
            {
                Box($"NotepadS{i}", table, new Vector3(-0.95f + i * 0.95f, 0.765f, -0.35f), new Vector3(0.15f, 0.01f, 0.21f), Lit("Paper_White", new Color(0.97f, 0.97f, 0.95f), 0.1f));
                Cylinder($"GlassN{i}", table, new Vector3(-0.95f + i * 0.95f, 0.81f, 0.35f), 0.07f, 0.12f, Transparent("Glass_Clear", new Color(0.78f, 0.88f, 0.94f, 0.16f)));
            }
            FitCollider(table);
            for (int i = 0; i < 3; i++)
            {
                float x = -6.2f + i;
                TaskChair($"Chair_S{i}", zone, new Vector3(x, 0f, 11.05f), Vector3.forward, leather);
                TaskChair($"Chair_N{i}", zone, new Vector3(x, 0f, 12.75f), Vector3.back, leather);
            }
            TaskChair("Chair_Head", zone, new Vector3(-3.25f, 0f, 11.9f), Vector3.left, leather);

            // Executive corner by the windows: where a future NPC conversation takes place.
            Transform corner = Group("ExecutiveCorner", zone, Vector3.zero);
            Box("Rug", corner, new Vector3(-0.2f, 0.005f, 12.7f), new Vector3(2.6f, 0.01f, 2.0f), Lit("Rug_Sand", new Color(0.72f, 0.66f, 0.56f), 0.05f), castShadows: false);
            Sofa("LoungeChair_W", corner, new Vector3(-1.15f, 0f, 12.7f), Vector3.right, 0.9f, fabricTeal);
            Sofa("LoungeChair_E", corner, new Vector3(0.95f, 0f, 12.7f), Vector3.left, 0.9f, fabricTeal);
            Transform side = Group("SideTable", corner, new Vector3(-0.1f, 0f, 13.45f));
            Cylinder("Top", side, new Vector3(0f, 0.55f, 0f), 0.5f, 0.03f, walnut);
            Cylinder("Stem", side, new Vector3(0f, 0.27f, 0f), 0.06f, 0.54f, metal);
            FitCollider(side);
            Plant("Plant_CornerNW", corner, new Vector3(-8.3f, 0f, 13.4f), 1.1f);
            Plant("Plant_CornerNE", corner, new Vector3(1.5f, 0f, 13.5f), 1.0f);
        }

        // =====================================================================================================
        // Hallway spine
        // =====================================================================================================
        private static void BuildHallway(Transform zone)
        {
            Material wallWhite = Lit("Wall_White", Palette.OffWhite, 0.15f);
            Material navy = Lit("Wall_BrandNavy", Palette.Navy, 0.3f);
            Material metal = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);

            // East side (x = 5): solid wall with the IT counter opening z[4.6, 8.6] and the server room door.
            SolidWall("Wall_HallIT_S", zone, new Vector2(5f, 4f), new Vector2(5f, 4.6f), CeilingHeight, wallWhite);
            SolidWall("Wall_HallIT_N", zone, new Vector2(5f, 8.6f), new Vector2(5f, 14f), CeilingHeight, wallWhite);
            Box("ITOpeningHeader", zone, new Vector3(5f, 2.7f, 6.6f), new Vector3(0.15f, 0.6f, 4.0f), wallWhite, collider: true);

            Transform door = Group("ServerRoomDoor", zone, new Vector3(4.91f, 0f, 11.0f), Facing(Vector3.left));
            Box("Frame", door, new Vector3(0f, 1.08f, 0.01f), new Vector3(1.1f, 2.16f, 0.02f), metal);
            Box("Leaf", door, new Vector3(0f, 1.05f, -0.005f), new Vector3(1.0f, 2.1f, 0.03f), Lit("Door_Grey", new Color(0.55f, 0.57f, 0.6f), 0.4f));
            Box("Handle", door, new Vector3(-0.4f, 1.05f, -0.035f), new Vector3(0.14f, 0.025f, 0.025f), Lit("Metal_Brushed", new Color(0.75f, 0.76f, 0.78f), 0.7f, 1f));
            Box("BadgeReader", door, new Vector3(-0.75f, 1.2f, -0.01f), new Vector3(0.09f, 0.13f, 0.03f), Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            Box("ReaderLED", door, new Vector3(-0.75f, 1.24f, -0.027f), new Vector3(0.03f, 0.01f, 0.004f), Unlit("LED_Red", Palette.AlertRed), castShadows: false);
            Text("Sign", door, "SERVER ROOM\n<size=55%>Authorised personnel only - badge required</size>", new Vector3(0f, 1.85f, -0.022f),
                0.3f, Color.white, new Vector2(0.9f, 0.25f), style: FontStyles.Bold);
            Box("SignPlate", door, new Vector3(0f, 1.85f, -0.02f), new Vector3(0.95f, 0.27f, 0.004f), navy);

            // Wayfinding sign facing people coming through the gates.
            Transform way = Group("WayfindingSign", zone, new Vector3(3.5f, 2.45f, 5.4f));
            Box("Panel", way, Vector3.zero, new Vector3(2.6f, 0.36f, 0.04f), navy);
            Text("Text", way, "< WORKSPACE        MEETING ROOM ^        IT SERVICE DESK >", new Vector3(0f, 0f, -0.025f), 0.55f, Color.white,
                new Vector2(2.5f, 0.3f), style: FontStyles.Bold);
            Box("RodL", way, new Vector3(-1.1f, 0.4f, 0f), new Vector3(0.015f, 0.44f, 0.015f), metal);
            Box("RodR", way, new Vector3(1.1f, 0.4f, 0f), new Vector3(0.015f, 0.44f, 0.015f), metal);

            // Art on the hallway wall.
            Color[] art = { new Color(0.0f, 0.55f, 0.58f), new Color(0.88f, 0.55f, 0.25f), new Color(0.2f, 0.28f, 0.45f) };
            for (int i = 0; i < 2; i++)
            {
                Transform canvas = Group($"ArtCanvas{i}", zone, new Vector3(4.92f, 1.6f, 9.4f + i * 3.4f), Facing(Vector3.left));
                Box("Canvas", canvas, Vector3.zero, new Vector3(1.0f, 0.75f, 0.03f), Lit("Canvas_White", new Color(0.95f, 0.94f, 0.9f), 0.1f));
                for (int k = 0; k < 3; k++)
                {
                    Box($"Stroke{k}", canvas, new Vector3(-0.25f + k * 0.25f, (k - 1) * 0.1f, -0.017f), new Vector3(0.18f, 0.4f - k * 0.08f, 0.004f),
                        Lit($"Paint_{k}", art[(k + i) % art.Length], 0.3f));
                }
            }

            // Low planter divider between the hallway and the workspace (open z[4, 7.2] to walk in).
            Transform planter = Group("PlanterDivider", zone, new Vector3(2.0f, 0f, 8.35f));
            Box("Trough", planter, new Vector3(0f, 0.3f, 0f), new Vector3(0.45f, 0.6f, 2.2f), Lit("Wood_Oak", Palette.Oak, 0.45f));
            for (int i = 0; i < 4; i++)
            {
                Sphere($"Shrub{i}", planter, new Vector3(0f, 0.75f, -0.8f + i * 0.53f), new Vector3(0.45f, 0.4f, 0.5f), Lit("Foliage_Dark", new Color(0.13f, 0.33f, 0.16f), 0.2f));
            }
            FitCollider(planter);

            Transform extinguisher = Group("FireExtinguisher", zone, new Vector3(4.85f, 0f, 9.0f));
            Cylinder("Body", extinguisher, new Vector3(0f, 0.55f, 0f), 0.16f, 0.5f, Lit("Paint_Red", Palette.AlertRed, 0.6f));
            Box("Bracket", extinguisher, new Vector3(0.05f, 0.55f, 0f), new Vector3(0.04f, 0.3f, 0.12f), metal);
            FitCollider(extinguisher);
            Transform cooler = Group("WaterCooler", zone, new Vector3(4.65f, 0f, 13.4f));
            Box("Body", cooler, new Vector3(0f, 0.5f, 0f), new Vector3(0.32f, 1.0f, 0.32f), Lit("Planter_White", new Color(0.88f, 0.87f, 0.85f), 0.5f));
            Cylinder("Bottle", cooler, new Vector3(0f, 1.2f, 0f), 0.27f, 0.4f, Transparent("Glass_Water", new Color(0.55f, 0.75f, 0.95f, 0.45f)));
            FitCollider(cooler);
            Plant("Plant_HallN", zone, new Vector3(2.45f, 0f, 13.5f), 1.0f);
        }

        // =====================================================================================================
        // IT service / security desk
        // =====================================================================================================
        private static void BuildITSecurityDesk(Transform zone, out Transform counter)
        {
            Material wallWhite = Lit("Wall_White", Palette.OffWhite, 0.15f);
            Material navy = Lit("Wall_BrandNavy", Palette.Navy, 0.3f);
            Material stoneWhite = Lit("Desk_StoneWhite", new Color(0.94f, 0.93f, 0.91f), 0.6f);
            Material oak = Lit("Wood_Oak", Palette.Oak, 0.45f);
            Material metal = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);
            Material tealGlow = Unlit("LED_Teal", Palette.Teal);

            SolidWall("Wall_ITBack", zone, new Vector2(5f, 8.6f), new Vector2(9f, 8.6f), CeilingHeight, wallWhite);

            // Service counter: customers stand on the hallway side (-X).
            counter = Group("ITServiceCounter", zone, new Vector3(6.5f, 0f, 6.5f), Facing(Vector3.left));
            Box("FrontPanel", counter, new Vector3(0f, 0.53f, -0.3f), new Vector3(3.0f, 1.06f, 0.08f), stoneWhite);
            Box("AccentLED", counter, new Vector3(0f, 0.12f, -0.345f), new Vector3(3.0f, 0.025f, 0.01f), tealGlow, castShadows: false);
            Box("TopLedge", counter, new Vector3(0f, 1.08f, -0.25f), new Vector3(3.1f, 0.04f, 0.34f), oak);
            Box("Worktop", counter, new Vector3(0f, 0.73f, 0.2f), new Vector3(2.95f, 0.03f, 0.6f), oak);
            Box("SideL", counter, new Vector3(-1.5f, 0.53f, 0.05f), new Vector3(0.08f, 1.06f, 0.7f), stoneWhite);
            Box("SideR", counter, new Vector3(1.5f, 0.53f, 0.05f), new Vector3(0.08f, 1.06f, 0.7f), stoneWhite);
            Text("CounterLabel", counter, "IT SERVICE DESK  |  Identity verification", new Vector3(0f, 0.78f, -0.346f), 0.55f, Palette.Navy,
                new Vector2(2.8f, 0.14f), style: FontStyles.Bold);
            FitCollider(counter);

            // ID verification point: badge reader + instruction plate on the ledge.
            Transform verify = Group("IDVerificationPoint", counter, new Vector3(-0.45f, 1.1f, -0.28f));
            Box("ReaderBody", verify, new Vector3(0f, 0.02f, 0f), new Vector3(0.14f, 0.04f, 0.18f), Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            Box("ReaderPad", verify, new Vector3(0f, 0.042f, 0.01f), new Vector3(0.1f, 0.004f, 0.1f), Unlit("LED_Teal", Palette.Teal), castShadows: false);
            Transform plate = Group("InstructionPlate", verify, new Vector3(0f, 0.12f, 0.12f), Quaternion.Euler(-15f, 0f, 0f));
            Box("Acrylic", plate, Vector3.zero, new Vector3(0.3f, 0.14f, 0.006f), Lit("Acrylic_Smoke", new Color(0.2f, 0.22f, 0.25f), 0.85f));
            Text("Text", plate, "IDENTITY VERIFICATION\n<size=75%>Tap your staff ID. Visitors: show photo ID\nand your escort's name.</size>",
                new Vector3(0f, 0f, -0.005f), 0.12f, Color.white, new Vector2(0.28f, 0.12f));

            Box("LostAndFoundTray", counter, new Vector3(0.85f, 1.108f, -0.27f), new Vector3(0.26f, 0.016f, 0.18f), Lit("Tray_Blue", new Color(0.18f, 0.35f, 0.6f), 0.4f));
            Transform lfSign = Group("LostAndFoundSign", counter, new Vector3(0.85f, 1.16f, -0.17f));
            Box("Acrylic", lfSign, Vector3.zero, new Vector3(0.24f, 0.07f, 0.006f), Lit("Acrylic_Smoke", new Color(0.2f, 0.22f, 0.25f), 0.85f));
            Text("Text", lfSign, "LOST & FOUND", new Vector3(0f, 0f, -0.005f), 0.14f, Color.white, new Vector2(0.23f, 0.06f), style: FontStyles.Bold);

            // Staff side: technician monitors, badge printer, chair.
            for (int i = 0; i < 2; i++)
            {
                Transform screen = Monitor($"TechMonitor{i}", counter, new Vector3(-0.35f + i * 0.62f, 0.745f, 0.25f), Quaternion.Euler(0f, 180f, 0f),
                    new Vector2(0.55f, 0.31f), new Color(0.07f, 0.09f, 0.12f), out _);
                ScreenText(screen, "Console", i == 0
                        ? "<color=#3BE07A>ACCESS LOG</color>\n08:52 J.Okafor  GATE-2  OK\n09:03 A.Reyes   GATE-1  OK\n09:11 VISITOR   GATE-2  <color=#F5A623>TAILGATE?</color>\n09:14 SRV-ROOM  DOOR    DENIED"
                        : "<color=#3BE07A>TICKETS</color>\n#4471  Password reset - pending\n#4472  New starter badge\n#4473  USB found in car park",
                    -0.26f, 0.14f, 0.52f, 0.28f, 0.11f, new Color(0.8f, 0.85f, 0.9f));
            }
            Transform printer = Group("BadgePrinter", counter, new Vector3(1.15f, 0.745f, 0.25f));
            Box("Body", printer, new Vector3(0f, 0.09f, 0f), new Vector3(0.22f, 0.18f, 0.32f), Lit("Printer_Grey", new Color(0.78f, 0.79f, 0.8f), 0.4f));
            Box("CardSlot", printer, new Vector3(0f, 0.12f, -0.162f), new Vector3(0.1f, 0.01f, 0.005f), Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            Box("BlankCards", printer, new Vector3(0f, 0.19f, 0.06f), new Vector3(0.06f, 0.02f, 0.09f), Lit("Badge_White", new Color(0.97f, 0.97f, 0.96f), 0.4f));
            TaskChair("TechChair", counter, new Vector3(0f, 0f, 0.85f), Vector3.back, Lit("Fabric_Charcoal", new Color(0.2f, 0.21f, 0.23f), 0.1f));

            Transform sign = Group("ITDeskSign", zone, new Vector3(6.5f, 2.45f, 6.5f), Facing(Vector3.left));
            Box("Panel", sign, Vector3.zero, new Vector3(2.4f, 0.42f, 0.05f), navy);
            Text("Text", sign, "IT SERVICE DESK  |  SECURITY", new Vector3(0f, 0f, -0.03f), 0.75f, Color.white, new Vector2(2.3f, 0.36f),
                style: FontStyles.Bold);
            Box("Rod", sign, new Vector3(0f, 0.38f, 0f), new Vector3(0.015f, 0.35f, 0.015f), metal);

            // Security operations: CCTV wall on the party wall + server rack.
            Transform cctv = Group("CCTVWall", zone, new Vector3(8.9f, 1.75f, 6.5f), Facing(Vector3.left));
            string[] cams = { "CAM 01  LOBBY", "CAM 02  GATES", "CAM 03  WORKSPACE", "CAM 04  SERVER RM" };
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -0.5f : 0.5f), y = (i < 2 ? 0.3f : -0.3f);
                Transform feed = Screen($"Feed{i}", cctv, new Vector3(x, y, 0f), Quaternion.identity, new Vector2(0.9f, 0.52f),
                    new Color(0.17f, 0.19f, 0.2f), out _, Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
                ScreenRect(feed, "Scene", 0f, -0.05f, 0.8f, 0.3f, new Color(0.3f, 0.32f, 0.33f));
                ScreenText(feed, "Label", cams[i], -0.42f, 0.24f, 0.6f, 0.05f, 0.2f, new Color(0.85f, 0.9f, 0.9f));
                ScreenText(feed, "Rec", "<color=#E5302B>REC</color>", 0.3f, 0.24f, 0.12f, 0.05f, 0.2f, Color.white);
            }

            Transform rack = Group("NetworkRack", zone, new Vector3(8.55f, 0f, 4.75f), Facing(Vector3.left));
            Box("Cabinet", rack, new Vector3(0f, 1.0f, 0f), new Vector3(0.6f, 2.0f, 0.7f), Lit("Rack_Black", new Color(0.07f, 0.07f, 0.08f), 0.5f, 0.3f));
            for (int u = 0; u < 10; u++)
            {
                Box($"Unit{u}", rack, new Vector3(0f, 0.3f + u * 0.15f, -0.355f), new Vector3(0.5f, 0.1f, 0.01f), Lit("Rack_Panel", new Color(0.18f, 0.19f, 0.2f), 0.4f));
                Box($"LED{u}", rack, new Vector3(0.18f, 0.3f + u * 0.15f, -0.362f), new Vector3(0.06f, 0.012f, 0.004f),
                    Unlit(u % 3 == 0 ? "LED_Amber" : "LED_Green", u % 3 == 0 ? Palette.WarningAmber : new Color(0.2f, 0.95f, 0.45f)), castShadows: false);
            }
            FitCollider(rack);
            Plant("Plant_IT", zone, new Vector3(5.5f, 0f, 4.4f), 0.9f);
        }

        // =====================================================================================================
        // Scenario objects (artifacts + NPC anchors)
        // =====================================================================================================
        private static void BuildScenarioObjects(Transform root, Transform heroDesk, Transform itCounter)
        {
            Transform reception = GameObject.Find($"{EnvironmentRootName}/Reception/ReceptionDesk").transform;

            // ---- Spoofed visitor badge: on the reception badge tray.
            Transform spoofed = BuildBadge(SpoofedVisitorBadge, root, reception.TransformPoint(new Vector3(-0.95f, 1.166f, -0.28f)),
                reception.rotation * Quaternion.Euler(90f, 0f, 12f),
                company: "APERTURE SYSTMES", role: "VISITOR", person: "James Miller\nIT Contractor - NetSecure Ltd",
                footer: "Valid: 14 MAR 2024\nEscort: NOT REQUIRED", header: new Color(0.92f, 0.45f, 0.1f),
                logo: new Color(0.25f, 0.55f, 0.9f), hologram: false);
            MakeGrabbable(spoofed, SpoofedVisitorBadge);
            AddArtifact(spoofed, SpoofedVisitorBadge, ArtifactCategory.SpoofedCredential, OfficeZone.Reception, true,
                "Company name misspelled (\"SYSTMES\"); off-brand blue logo; no hologram; expired (Mar 2024); claims " +
                "\"escort not required\" contrary to visitor policy; contractor company not on the visitor log.");

            // ---- Reference genuine employee badge at the IT desk for comparison.
            Transform genuine = BuildBadge(EmployeeBadgeReference, root, itCounter.TransformPoint(new Vector3(-1.05f, 1.108f, -0.27f)),
                itCounter.rotation * Quaternion.Euler(90f, 0f, -8f),
                company: "APERTURE SYSTEMS", role: "STAFF", person: "Amara Okafor\nIT Security Analyst",
                footer: "Valid: 31 DEC 2026\nStaff ID 004417", header: Palette.Navy, logo: Palette.Teal, hologram: true);
            MakeGrabbable(genuine, EmployeeBadgeReference);
            AddArtifact(genuine, EmployeeBadgeReference, ArtifactCategory.SecurityControl, OfficeZone.ITSecurityDesk, false,
                "Genuine staff badge for comparison: correct spelling, teal brand logo, holographic strip, valid date and staff ID.");

            // ---- Malicious USB drive: in the IT lost & found tray with a baiting note.
            Transform usb = Group(MaliciousUSBDrive, root, itCounter.TransformPoint(new Vector3(0.82f, 1.116f, -0.29f)),
                itCounter.rotation * Quaternion.Euler(0f, 25f, 0f));
            Box("Body", usb, new Vector3(0f, 0.005f, 0f), new Vector3(0.022f, 0.01f, 0.058f), Lit("USB_Red", Palette.AlertRed, 0.55f));
            Box("Connector", usb, new Vector3(0f, 0.005f, 0.036f), new Vector3(0.012f, 0.0045f, 0.014f), Lit("Metal_Brushed", new Color(0.75f, 0.76f, 0.78f), 0.7f, 1f));
            Box("Label", usb, new Vector3(0f, 0.0105f, -0.005f), new Vector3(0.018f, 0.001f, 0.032f), Lit("Paper_White", new Color(0.97f, 0.97f, 0.95f), 0.1f));
            Text("LabelText", usb, "Q4 BONUSES\nCONFIDENTIAL", new Vector3(0f, 0.0112f, -0.005f), 0.022f, Palette.AlertRed, new Vector2(0.032f, 0.016f),
                style: FontStyles.Bold, localRotation: Quaternion.Euler(90f, 90f, 0f));
            AddRootCollider(usb, new Vector3(0f, 0.01f, 0f), new Vector3(0.04f, 0.03f, 0.075f));
            MakeGrabbable(usb, MaliciousUSBDrive);
            AddArtifact(usb, MaliciousUSBDrive, ArtifactCategory.MaliciousMedia, OfficeZone.ITSecurityDesk, true,
                "Unknown USB found in the car park with an enticing \"Q4 BONUSES\" label (baiting). Must never be plugged in; " +
                "hand to IT Security for sandboxed analysis.");
            // Separate object so it stays in the tray when the drive is picked up.
            Transform note = Group($"{MaliciousUSBDrive}_BaitNote", root, itCounter.TransformPoint(new Vector3(0.92f, 1.117f, -0.24f)),
                itCounter.rotation * Quaternion.Euler(90f, 0f, -6f));
            Box("Paper", note, Vector3.zero, new Vector3(0.076f, 0.076f, 0.001f), Lit("StickyNote_Yellow", new Color(0.98f, 0.88f, 0.35f), 0.1f));
            Text("Text", note, "Found in car park.\nCan someone plug it in\nto find the owner?", new Vector3(0f, 0f, -0.001f), 0.056f,
                Palette.ScreenText, new Vector2(0.07f, 0.07f));

            // ---- Phishing email workstation (hero desk).
            Transform station = Group(PhishingEmailWorkstation, root, heroDesk.position, heroDesk.rotation);
            Transform screen = Monitor("Monitor", station, new Vector3(0f, 0.74f, 0.17f), Quaternion.identity, new Vector2(0.62f, 0.36f),
                new Color(0.97f, 0.97f, 0.98f), out Renderer bezel);
            BuildPhishingEmail(screen, out GameObject verifyButton);
            KeyboardAndMouse(station, new Vector3(0f, 0.74f, -0.12f), Quaternion.identity);
            MakeRayTarget(screen, new Vector2(0.62f, 0.36f), PhishingEmailWorkstation, bezel);
            MakeButtonRayTarget(verifyButton, PhishingLinkButton);
            AddArtifact(station, PhishingEmailWorkstation, ArtifactCategory.PhishingMessage, OfficeZone.Workstations, true,
                "Lookalike sender domain (aperture-systerns.com); urgent 2-hour deadline; generic greeting; link text and " +
                "destination (aperture-systerns-verify.co) do not match the company domain; no external-sender banner check.");

            // Password on a sticky note stuck to the hero monitor bezel.
            Transform sticky = Group(PasswordStickyNote, root, screen.TransformPoint(new Vector3(-0.27f, -0.2f, -0.004f)),
                screen.rotation * Quaternion.Euler(0f, 0f, 6f));
            Box("Paper", sticky, Vector3.zero, new Vector3(0.07f, 0.07f, 0.001f), Lit("StickyNote_Yellow", new Color(0.98f, 0.88f, 0.35f), 0.1f), castShadows: false);
            Text("Text", sticky, "VPN / email\npwd: Aperture2024!", new Vector3(0f, 0f, -0.001f), 0.064f, new Color(0.15f, 0.2f, 0.55f),
                new Vector2(0.066f, 0.066f));
            AddRootCollider(sticky, Vector3.zero, new Vector3(0.07f, 0.07f, 0.01f));
            MakeRayTarget(sticky, Vector2.zero, PasswordStickyNote, sticky.GetComponentInChildren<Renderer>(), useExistingCollider: true);
            AddArtifact(sticky, PasswordStickyNote, ArtifactCategory.PoorSecurityHygiene, OfficeZone.Workstations, true,
                "Credentials written down and displayed on the monitor in an open-plan area.");

            // Confidential payroll printout left in the shared printer tray.
            Transform print = GameObject.Find($"{EnvironmentRootName}/Workstations/PrintStation").transform;
            Transform printout = Group(UnattendedConfidentialPrintout, root, print.TransformPoint(new Vector3(-0.12f, 1.162f, 0f)),
                print.rotation * Quaternion.Euler(90f, 0f, -10f));
            Box("Sheet", printout, Vector3.zero, new Vector3(0.21f, 0.297f, 0.002f), Lit("Paper_White", new Color(0.97f, 0.97f, 0.95f), 0.1f));
            Text("Header", printout, "CONFIDENTIAL - HR ONLY", new Vector3(0f, 0.12f, -0.002f), 0.11f, Palette.AlertRed, new Vector2(0.19f, 0.03f),
                style: FontStyles.Bold);
            Text("Body", printout, "Payroll export - Q3\nName        Salary     Account\nJ. Okafor   84,500   ****2231\nA. Reyes    91,200   ****8890\nM. Chen     77,900   ****1045",
                new Vector3(0f, 0.0f, -0.002f), 0.06f, Palette.ScreenText, new Vector2(0.19f, 0.18f), TextAlignmentOptions.TopLeft);
            AddRootCollider(printout, Vector3.zero, new Vector3(0.21f, 0.297f, 0.01f));
            MakeGrabbable(printout, UnattendedConfidentialPrintout);
            AddArtifact(printout, UnattendedConfidentialPrintout, ArtifactCategory.PoorSecurityHygiene, OfficeZone.Workstations, true,
                "Sensitive payroll data left unattended at a shared printer (no secure-print release).");

            // ---- Fake SSL / credential-harvesting kiosk in the IT area, visible from the hallway.
            BuildFakeSSLTerminal(root);

            // ---- NPC anchors for later phases (avatars / dialogue).
            Transform anchors = Group("NPC_Anchors", root, Vector3.zero);
            Group("NPC_Receptionist", anchors, new Vector3(0f, 0f, 3.2f), Quaternion.LookRotation(Vector3.back));
            Group("NPC_VisitorImpostor", anchors, new Vector3(-5.0f, 0f, 1.8f), Quaternion.LookRotation(Vector3.left));
            Group("NPC_ITTechnician", anchors, new Vector3(7.35f, 0f, 6.5f), Quaternion.LookRotation(Vector3.left));
            Group("NPC_MeetingHost", anchors, new Vector3(0.95f, 0f, 12.7f), Quaternion.LookRotation(Vector3.left));
        }

        private static void BuildPhishingEmail(Transform s, out GameObject verifyButton)
        {
            Color bar = new Color(0.05f, 0.35f, 0.55f);
            ScreenRect(s, "TitleBar", 0f, 0.165f, 0.62f, 0.03f, bar);
            ScreenText(s, "AppTitle", "Aperture Mail  -  Inbox (3)", -0.3f, 0.177f, 0.4f, 0.03f, 0.1f, Color.white, style: FontStyles.Bold);
            ScreenRect(s, "InboxPane", -0.215f, -0.015f, 0.19f, 0.33f, new Color(0.92f, 0.93f, 0.95f));
            ScreenRect(s, "SelectedItem", -0.215f, 0.133f, 0.19f, 0.03f, new Color(0.8f, 0.88f, 0.96f), 2);
            ScreenText(s, "InboxItems",
                "<b>IT Service Desk</b>\nURGENT: Password expires\n\n<b>Facilities</b>\nCar park resurfacing\n\n<b>Amara Okafor</b>\nBadge audit reminder",
                -0.305f, 0.145f, 0.18f, 0.3f, 0.075f, Palette.ScreenText);

            const float x0 = -0.11f;
            ScreenText(s, "Subject", "URGENT: Your password expires in 2 hours", x0, 0.145f, 0.42f, 0.03f, 0.115f, Palette.ScreenText,
                style: FontStyles.Bold);
            ScreenText(s, "From", "From: IT Service Desk <it-support@aperture-systerns.com>\nTo: you@aperture-systems.com",
                x0, 0.112f, 0.42f, 0.04f, 0.075f, Palette.MutedText);
            ScreenText(s, "Body",
                "Dear Employee,\n\nOur records show your network password will expire in <b>2 hours</b>. " +
                "To avoid losing access to email and payroll, verify your account immediately using the secure link below.\n\n" +
                "Failure to verify will result in account suspension.\n\nRegards,\nIT Service Desk",
                x0, 0.065f, 0.41f, 0.17f, 0.085f, Palette.ScreenText);
            verifyButton = ScreenRect(s, PhishingLinkButton, 0.03f, -0.12f, 0.17f, 0.035f, new Color(0.82f, 0.16f, 0.14f), 2);
            ScreenText(s, "ButtonText", "VERIFY ACCOUNT NOW", 0.03f, -0.12f, 0.17f, 0.035f, 0.08f, Color.white, TextAlignmentOptions.Center,
                FontStyles.Bold);
            ScreenText(s, "LinkPreview", "http://aperture-systerns-verify.co/login?id=88213", x0, -0.152f, 0.42f, 0.02f, 0.065f,
                new Color(0.1f, 0.35f, 0.8f));
        }

        private static void BuildFakeSSLTerminal(Transform root)
        {
            Material metal = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);
            Transform kiosk = Group(FakeSSLTerminal, root, new Vector3(5.65f, 0f, 8.05f), Facing(Vector3.left));
            Box("BasePlate", kiosk, new Vector3(0f, 0.015f, 0f), new Vector3(0.55f, 0.03f, 0.45f), metal);
            Box("Pedestal", kiosk, new Vector3(0f, 0.55f, 0.05f), new Vector3(0.16f, 1.05f, 0.12f), metal);
            Box("Housing", kiosk, new Vector3(0f, 1.38f, 0.08f), new Vector3(0.52f, 0.78f, 0.08f), Lit("Rack_Black", new Color(0.07f, 0.07f, 0.08f), 0.5f, 0.3f))
                .transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            Transform header = Group("Header", kiosk, new Vector3(0f, 1.83f, 0.12f));
            Box("Panel", header, Vector3.zero, new Vector3(0.52f, 0.1f, 0.04f), Lit("Wall_BrandNavy", Palette.Navy, 0.3f));
            Text("Text", header, "SELF-SERVICE PASSWORD RESET", new Vector3(0f, 0f, -0.025f), 0.16f, Color.white, new Vector2(0.5f, 0.08f),
                style: FontStyles.Bold);

            var size = new Vector2(0.44f, 0.66f);
            Transform s = Screen("Screen", kiosk, new Vector3(0f, 1.38f, 0.035f), Quaternion.Euler(12f, 0f, 0f), size,
                new Color(0.96f, 0.96f, 0.97f), out Renderer bezel, Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            // Browser chrome with a failing certificate.
            ScreenRect(s, "BrowserBar", 0f, 0.3f, 0.44f, 0.05f, new Color(0.87f, 0.88f, 0.9f));
            ScreenRect(s, "NotSecureChip", -0.155f, 0.3f, 0.12f, 0.034f, new Color(0.98f, 0.86f, 0.85f), 2);
            ScreenText(s, "NotSecure", "<color=#C62828><b>! Not secure</b></color>", -0.155f, 0.3f, 0.12f, 0.034f, 0.085f, Color.white, TextAlignmentOptions.Center);
            ScreenText(s, "Url", "http://aperture-sso-login.net/reset", -0.085f, 0.312f, 0.3f, 0.03f, 0.08f, Palette.ScreenText);
            ScreenRect(s, "CertWarning", 0f, 0.24f, 0.42f, 0.06f, new Color(1f, 0.93f, 0.8f));
            ScreenText(s, "CertText", "Certificate error: NET::ERR_CERT_COMMON_NAME_INVALID\nIssued to: *.free-hosting-cdn.xyz",
                -0.2f, 0.264f, 0.4f, 0.05f, 0.072f, new Color(0.55f, 0.3f, 0.0f));
            // Login form.
            ScreenText(s, "Brand", "APERTURE SYSTEMS", -0.2f, 0.19f, 0.4f, 0.04f, 0.15f, Palette.Navy, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            ScreenText(s, "Heading", "Single Sign-On  |  Password reset", -0.2f, 0.145f, 0.4f, 0.03f, 0.1f, Palette.MutedText);
            string[] fields = { "Username", "Current password", "New password" };
            for (int i = 0; i < fields.Length; i++)
            {
                float y = 0.075f - i * 0.085f;
                ScreenText(s, $"Label{i}", fields[i], -0.2f, y + 0.03f, 0.4f, 0.02f, 0.075f, Palette.ScreenText);
                ScreenRect(s, $"Field{i}", 0f, y - 0.005f, 0.4f, 0.04f, new Color(0.84f, 0.85f, 0.87f));
                ScreenRect(s, $"FieldInner{i}", 0f, y - 0.005f, 0.396f, 0.036f, Color.white, 2);
            }
            GameObject signIn = ScreenRect(s, FakeSSLSignInButton, 0f, -0.215f, 0.4f, 0.05f, new Color(0.05f, 0.35f, 0.55f), 3);
            ScreenText(s, "SignInText", "SIGN IN & RESET", 0f, -0.215f, 0.4f, 0.05f, 0.1f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            ScreenText(s, "Footer", "Need help? Call +44 20 7946 0000 (24h)", -0.2f, -0.27f, 0.4f, 0.02f, 0.06f, Palette.MutedText);

            FitCollider(kiosk); // body collider for locomotion
            MakeRayTarget(s, size, FakeSSLTerminal, bezel);
            MakeButtonRayTarget(signIn, FakeSSLSignInButton);
            AddArtifact(kiosk, FakeSSLTerminal, ArtifactCategory.FraudulentLogin, OfficeZone.ITSecurityDesk, true,
                "Unofficial kiosk: \"Not secure\" HTTP page; certificate issued to an unrelated domain; lookalike SSO URL " +
                "(aperture-sso-login.net); asks for the current password; unverified phone number.");
        }

        /// <summary>Badge card lying flat (front facing up); top edge points away from the reader.</summary>
        private static Transform BuildBadge(string name, Transform parent, Vector3 position, Quaternion rotation, string company, string role,
            string person, string footer, Color header, Color logo, bool hologram)
        {
            Transform badge = Group(name, parent, position, rotation);
            const float w = 0.055f, h = 0.086f;
            Box("Card", badge, Vector3.zero, new Vector3(w, h, 0.0025f), Lit("Badge_White", new Color(0.97f, 0.97f, 0.96f), 0.4f));
            Panel("HeaderBand", badge, new Vector2(0f, 0.03f), new Vector2(w, 0.024f), Lit($"Badge_Header_{ColorUtility.ToHtmlStringRGB(header)}", header, 0.4f), 2)
                .transform.localPosition = new Vector3(0f, 0.03f, -0.0014f);
            Text("Company", badge, company, new Vector3(0f, 0.035f, -0.0016f), 0.034f, Color.white, new Vector2(w - 0.004f, 0.01f), style: FontStyles.Bold);
            Text("Role", badge, role, new Vector3(0f, 0.024f, -0.0016f), 0.05f, Color.white, new Vector2(w - 0.004f, 0.012f), style: FontStyles.Bold);
            Panel("Photo", badge, Vector2.zero, new Vector2(0.022f, 0.026f), Lit("Photo_Print", new Color(0.55f, 0.65f, 0.72f), 0.6f), 2)
                .transform.localPosition = new Vector3(-0.012f, 0.002f, -0.0014f);
            Panel("Logo", badge, Vector2.zero, new Vector2(0.014f, 0.014f), Unlit($"Badge_Logo_{ColorUtility.ToHtmlStringRGB(logo)}", logo), 2)
                .transform.localPosition = new Vector3(0.015f, 0.006f, -0.0014f);
            Text("Person", badge, person, new Vector3(0f, -0.022f, -0.0016f), 0.03f, Palette.ScreenText, new Vector2(w - 0.006f, 0.016f));
            Text("Footer", badge, footer, new Vector3(0f, -0.036f, -0.0016f), 0.022f, Palette.MutedText, new Vector2(w - 0.004f, 0.012f));
            if (hologram)
            {
                Panel("Hologram", badge, Vector2.zero, new Vector2(0.012f, 0.012f), Lit("Hologram_Foil", new Color(0.75f, 0.8f, 0.9f), 0.95f, 1f), 2)
                    .transform.localPosition = new Vector3(0.015f, -0.01f, -0.0014f);
            }
            // Lanyard clip at the top edge.
            Box("Clip", badge, new Vector3(0f, h * 0.5f + 0.006f, 0f), new Vector3(0.012f, 0.012f, 0.004f), Lit("Metal_Brushed", new Color(0.75f, 0.76f, 0.78f), 0.7f, 1f));
            Box("Lanyard", badge, new Vector3(0f, h * 0.5f + 0.03f, 0.001f), new Vector3(0.015f, 0.04f, 0.0015f),
                Lit($"Lanyard_{ColorUtility.ToHtmlStringRGB(header)}", header, 0.2f));
            AddRootCollider(badge, new Vector3(0f, 0.008f, 0f), new Vector3(w + 0.01f, h + 0.03f, 0.02f));
            return badge;
        }

        // =====================================================================================================
        // Interaction / artifact helpers
        // =====================================================================================================
        private static void AddRootCollider(Transform root, Vector3 center, Vector3 size)
        {
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        /// <summary>Controller/hand grab + ray grab via Meta Quick Actions, plus the TrustBreach event relay.</summary>
        private static void MakeGrabbable(Transform target, string id)
        {
            QuickActionsAPI.AddGrabInteraction(target.gameObject);
            QuickActionsAPI.AddRayGrabInteraction(target.gameObject);
            var relay = target.gameObject.AddComponent<PointableEventRelay>();
            relay.InjectAllPointableEventRelay(target.GetComponent<Grabbable>(), id);
            relay.InjectOptionalRenderer(target.GetComponentInChildren<MeshRenderer>());
        }

        /// <summary>Ray hover/select target on a screen surface (or an existing root collider).</summary>
        private static void MakeRayTarget(Transform surfaceRoot, Vector2 size, string id, Renderer highlight, bool useExistingCollider = false)
        {
            Collider collider;
            if (useExistingCollider)
            {
                collider = surfaceRoot.GetComponent<Collider>();
            }
            else
            {
                var box = surfaceRoot.gameObject.AddComponent<BoxCollider>();
                // Thin and just behind the display so on-screen button colliders win the ray hit.
                box.center = new Vector3(0f, 0f, 0.001f);
                box.size = new Vector3(size.x, size.y, 0.002f);
                collider = box;
            }
            var interactableGo = new GameObject("RayInteractable");
            interactableGo.transform.SetParent(surfaceRoot, false);
            var surface = interactableGo.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(collider);
            var ray = interactableGo.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(surface);
            var relay = surfaceRoot.gameObject.AddComponent<PointableEventRelay>();
            relay.InjectAllPointableEventRelay(ray, id);
            relay.InjectOptionalRenderer(highlight);
        }

        /// <summary>On-screen button (a scaled Quad): collider slightly proud of the screen so it wins ray hits.</summary>
        private static void MakeButtonRayTarget(GameObject button, string id)
        {
            var box = button.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0f, -0.002f);
            box.size = new Vector3(1f, 1f, 0.002f);
            var interactableGo = new GameObject("RayInteractable");
            interactableGo.transform.SetParent(button.transform, false);
            var surface = interactableGo.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(box);
            var ray = interactableGo.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(surface);
            var relay = button.AddComponent<PointableEventRelay>();
            relay.InjectAllPointableEventRelay(ray, id);
            relay.InjectOptionalRenderer(button.GetComponent<Renderer>());
        }

        private static void AddArtifact(Transform target, string id, ArtifactCategory category, OfficeZone zone, bool suspicious, string redFlags)
        {
            var artifact = target.gameObject.AddComponent<ScenarioArtifact>();
            artifact.InjectAllScenarioArtifact(id, category, zone, suspicious, redFlags);
            artifact.InjectOptionalGazeCollider(target.GetComponent<Collider>());
        }

        // =====================================================================================================
        // Lighting, UI, XR rig
        // =====================================================================================================
        private static void BuildLighting(Transform root)
        {
            var sunGo = new GameObject("Sun_Directional");
            sunGo.transform.SetParent(root, false);
            sunGo.transform.rotation = Quaternion.Euler(58f, -35f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;

            RenderSettings.sun = sun;
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.64f, 0.67f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.52f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.56f, 0.54f, 0.5f); // floor bounce keeps ceilings from going grey
            RenderSettings.fog = false;

            // Realtime downlights (no shadows) – one or two per zone. Spots aimed at the floor avoid ceiling hotspots.
            var fills = new (string name, Vector3 pos, float intensity, float range)[]
            {
                ("Fill_ReceptionW", new Vector3(-5.5f, 2.7f, 1.0f), 1.3f, 6.5f),
                ("Fill_ReceptionDesk", new Vector3(0f, 2.7f, 1.2f), 1.4f, 6.5f),
                ("Fill_ReceptionE", new Vector3(5.5f, 2.7f, 1.0f), 1.1f, 6f),
                ("Fill_WorkspaceW", new Vector3(-6.2f, 2.7f, 6.9f), 1.3f, 6f),
                ("Fill_WorkspaceE", new Vector3(-1.6f, 2.7f, 6.9f), 1.3f, 6f),
                ("Fill_Meeting", new Vector3(-5.2f, 2.7f, 11.9f), 1.3f, 6f),
                ("Fill_ExecCorner", new Vector3(-0.1f, 2.7f, 12.7f), 1.0f, 5f),
                ("Fill_HallS", new Vector3(3.5f, 2.7f, 6.5f), 1.0f, 5f),
                ("Fill_HallN", new Vector3(3.5f, 2.7f, 11.5f), 1.0f, 5f),
                ("Fill_ITDesk", new Vector3(7.0f, 2.7f, 6.4f), 1.3f, 5.5f),
            };
            foreach (var (name, pos, intensity, range) in fills)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(new Vector3(pos.x, CeilingHeight - 0.1f, pos.z), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = new Color(1f, 0.95f, 0.87f);
                light.intensity = intensity * 1.25f;
                light.range = range;
                light.spotAngle = 150f;
                light.innerSpotAngle = 70f;
                light.shadows = LightShadows.None;
            }

            var spotGo = new GameObject("Spot_BrandWall");
            spotGo.transform.SetParent(root, false);
            spotGo.transform.SetPositionAndRotation(new Vector3(-0.25f, 2.95f, 2.4f), Quaternion.LookRotation(new Vector3(0f, -0.45f, 1f)));
            var spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.93f, 0.82f);
            spot.intensity = 3f;
            spot.range = 5f;
            spot.spotAngle = 75f;
            spot.shadows = LightShadows.None;

            // Emissive ceiling panels (visual only) across all zones.
            Transform panels = Group("CeilingLightPanels", root, Vector3.zero);
            Material panel = Unlit("CeilingLight_Panel", new Color(1f, 0.98f, 0.93f));
            int n = 0;
            for (float x = -7.8f; x <= 8f; x += 2.6f)
            {
                for (float z = -0.8f; z <= 13.2f; z += 2.3f)
                {
                    Box($"Panel{n++:00}", panels, new Vector3(x, CeilingHeight - 0.005f, z), new Vector3(1.2f, 0.01f, 0.3f), panel, castShadows: false);
                }
            }
            MarkStatic(panels);
        }

        private static void BuildOrientationPanel(Transform ui)
        {
            Vector3 position = new Vector3(1.7f, 0f, 0.4f);
            Vector3 towardSpawn = (SpawnPosition - position).normalized;
            Transform totem = Group("OrientationTotem", ui, position, Facing(new Vector3(towardSpawn.x, 0f, towardSpawn.z)));
            Box("Body", totem, new Vector3(0f, 0.85f, 0.02f), new Vector3(0.75f, 1.7f, 0.06f), Lit("Wall_BrandNavy", Palette.Navy, 0.3f), collider: true);
            Box("AccentLED", totem, new Vector3(0f, 1.62f, -0.012f), new Vector3(0.65f, 0.01f, 0.004f), Unlit("LED_Teal", Palette.Teal), castShadows: false);
            Text("Title", totem, "TRUST//BREACH", new Vector3(0f, 1.5f, -0.012f), 0.75f, Palette.Teal, new Vector2(0.7f, 0.12f), style: FontStyles.Bold);
            Text("Subtitle", totem, "Aperture Systems\nSecurity awareness floor", new Vector3(0f, 1.32f, -0.012f), 0.38f, Color.white, new Vector2(0.7f, 0.15f));
            Text("Instructions", totem,
                "Explore the office and stay alert.\nSomething here is not what it seems.\n\n" +
                "<color=#9FD8DA>Left stick</color>  move\n<color=#9FD8DA>Right stick</color>  turn\n" +
                "<color=#9FD8DA>Trigger</color>  point & select\n<color=#9FD8DA>Grip</color>  grab",
                new Vector3(0f, 0.82f, -0.012f), 0.3f, new Color(0.85f, 0.9f, 0.92f), new Vector2(0.66f, 0.7f), TextAlignmentOptions.Top);
        }

        private static void BuildXRRig(Transform xrRoot)
        {
            xrRoot.SetPositionAndRotation(SpawnPosition, Quaternion.identity);
            List<GameObject> created = OVRQuickActionsAPI.AddOVRInteractionRig(generateAsEditableCopy: false);
            if (created == null || created.Count == 0)
            {
                throw new InvalidOperationException("OVRQuickActionsAPI.AddOVRInteractionRig created no objects.");
            }
            OVRCameraRig cameraRig = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<OVRCameraRig>(true))
                .Single();
            cameraRig.transform.SetParent(xrRoot, false);
            cameraRig.transform.localPosition = Vector3.zero;
            cameraRig.transform.localRotation = Quaternion.identity;
        }

        private static void RegisterInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(s => s.path != ScenePath))
            {
                // Keep the Phase 1 foundation scene first (device boot scene); the office is added after it.
                int index = scenes.FindIndex(s => s.path == XRFoundationSceneBuilder.ScenePath);
                scenes.Insert(index >= 0 ? index + 1 : scenes.Count, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }

        private static void EnsureFolder(string path)
        {
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(path))
            {
                if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }
    }
}
