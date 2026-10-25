using System;
using System.IO;
using System.Linq;
using TMPro;
using TrustBreach.AI;
using TrustBreach.Dialogue;
using TrustBreach.Dialogue.Content;
using TrustBreach.Dialogue.NPC;
using TrustBreach.Interactions;
using TrustBreach.Interactions.Scenarios;
using TrustBreach.Telemetry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static TrustBreach.Editor.OfficeBuildKit;
using B = TrustBreach.Editor.TrustBreachCorporateOfficeBuilder;
using Object = UnityEngine.Object;

namespace TrustBreach.Editor
{
    /// <summary>
    /// Phase 3: generates the adaptive-adversary scenes from the (unchanged) Phase 2.5 office scene.
    ///   Assets/TrustBreach/Scenes/TrustBreach_Phase3_Adversary.unity       participant mode
    ///   Assets/TrustBreach/Scenes/TrustBreach_Phase3_Adversary_DEMO.unity  developer/demo mode
    /// Idempotent: both scenes are regenerated from the office scene each run; the dialogue graph, voice clips,
    /// meshes, prefab and settings assets are updated in place. The office scene is backed up first and never modified.
    /// </summary>
    public static class Phase3AdversaryBuilder
    {
        public const string ScenePath = "Assets/TrustBreach/Scenes/TrustBreach_Phase3_Adversary.unity";
        public const string DemoScenePath = "Assets/TrustBreach/Scenes/TrustBreach_Phase3_Adversary_DEMO.unity";
        public const string GraphPath = "Assets/TrustBreach/Dialogue/AlexMorgan_Encounter.asset";
        public const string LLMSettingsPath = "Assets/TrustBreach/Dialogue/LLMProviderSettings.asset";
        public const string VoiceFolder = "Assets/TrustBreach/Audio/Dialogue/AlexMorgan";

        public const string NpcName = "NPC_AlexMorgan";
        public const string DialoguePanelName = "AlexDialoguePanel";
        public const string DemoPanelName = "DemoModePanel";

        /// <summary>Alex stands in the lobby between reception and the staff gates, facing visitors.</summary>
        public static readonly Vector3 NpcPosition = new Vector3(4.8f, 0f, 1.4f);
        public static readonly Vector3 NpcFacesToward = new Vector3(1.0f, 0f, 0.2f);
        /// <summary>Expected standing spot of a participant talking to Alex (1.6 m in front of him).</summary>
        public static Vector3 ParticipantSpot => NpcPosition + (NpcFacesToward - NpcPosition).normalized * 1.6f;

        public static string LastVoiceReport { get; private set; } = string.Empty;

        /// <summary>Raised after both Phase 3 scenes were regenerated and saved (later phases add their layers here).</summary>
        public static event Action<XRFoundationValidator.Report> AfterBuild;

        [MenuItem("Tools/TRUST∕∕BREACH/Build Phase 3 Adaptive Adversary", priority = 120)]
        [MenuItem("TrustBreach/Phase 3/Build Adaptive Adversary Scenes", priority = 120)]
        private static void BuildFromMenu()
        {
            XRFoundationValidator.Report report = Build();
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        public static XRFoundationValidator.Report Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before building Phase 3.");
            }
            XRFoundationSceneBuilder.BackupDirtyScenes();
            OfficeInteractionsBuilder.BackupSceneFile(B.ScenePath);
            EnsureFolder("Assets/TrustBreach/Dialogue");

            // Data assets (updated in place).
            DialogueGraph graph = LoadOrCreate<DialogueGraph>(GraphPath);
            AlexMorganEncounter.Populate(graph);
            VoiceClipGenerator.Result voice = VoiceClipGenerator.GenerateFor(graph, VoiceFolder);
            LastVoiceReport = voice.Message;
            EditorUtility.SetDirty(graph);
            LLMProviderSettings llm = LoadOrCreate<LLMProviderSettings>(LLMSettingsPath);
            AssetDatabase.SaveAssets();

            // Scene: regenerate from the office scene (copy file content, keep this scene's GUID).
            Scene scene = CopyAndOpen(B.ScenePath, ScenePath);
            ResetCache();
            // OpenScene(Single) unloads unreferenced assets, so re-load the data assets before wiring them.
            graph = AssetDatabase.LoadAssetAtPath<DialogueGraph>(GraphPath);
            llm = AssetDatabase.LoadAssetAtPath<LLMProviderSettings>(LLMSettingsPath);

            Transform scenario = Root(scene, B.ScenarioRootName);
            Transform environment = Root(scene, B.EnvironmentRootName);
            Transform managers = Root(scene, B.ManagersRootName);
            Transform ui = Root(scene, B.UIRootName);

            // NPC prefab instance.
            GameObject npc = AlexMorganCharacterBuilder.BuildPrefabInstance();
            npc.transform.SetParent(scenario, true);
            npc.transform.SetPositionAndRotation(NpcPosition, Quaternion.LookRotation(Flat(NpcFacesToward - NpcPosition)));
            var presence = npc.GetComponent<NPCPresence>();
            var npcVoice = npc.GetComponentInChildren<NPCVoice>(true);

            DialoguePanel panel = BuildDialoguePanel(ui, npc.transform);
            GameObject demoPanelGo = BuildDemoPanel(ui, out DemoModePanel demoPanel);

            // Managers: director, scenario adapter, telemetry.
            var director = new GameObject("EncounterDirector").AddComponent<EncounterDirector>();
            director.transform.SetParent(managers, false);
            var adapter = new GameObject("ScenarioEventAdapter").AddComponent<ScenarioEventAdapter>();
            adapter.transform.SetParent(managers, false);
            adapter.InjectDirector(director);
            var telemetry = new GameObject("TelemetryRecorder").AddComponent<TelemetryRecorder>();
            telemetry.transform.SetParent(managers, false);

            ScenarioController[] scenarios = scenario.GetComponentsInChildren<ScenarioController>(true);
            OfficeInteractionsBuilder.Inject(director,
                ("_graph", graph), ("_panel", panel), ("_npc", presence), ("_voice", npcVoice), ("_demoPanel", demoPanelGo), ("_llmSettings", llm),
                ("_phishingEmailTarget", Find(scenario, $"{B.PhishingEmailWorkstation}/Monitor/Screen")),
                ("_passwordKioskTarget", Find(scenario, $"{B.FakeSSLTerminal}/Screen")),
                ("_visitorBadgeTarget", Find(scenario, B.SpoofedVisitorBadge)),
                ("_usbTarget", Find(environment, "ITSecurityDesk/ITServiceCounter/LostAndFoundTray")),
                ("_experienceStart", Root(scene, B.XRRigRootName)));

            // "Local Dimming feature is not supported" warning: local dimming is Quest Pro-only; off in Phase 3 scenes.
            foreach (OVRManager manager in Root(scene, B.XRRigRootName).GetComponentsInChildren<OVRManager>(true))
            {
                var mso = new SerializedObject(manager);
                mso.FindProperty("_localDimming").boolValue = false;
                mso.ApplyModifiedPropertiesWithoutUndo();
            }
            var so = new SerializedObject(director);
            SerializedProperty array = so.FindProperty("_scenarios");
            array.arraySize = scenarios.Length;
            for (int i = 0; i < scenarios.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = scenarios[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            OfficeInteractionsBuilder.Inject(demoPanel, ("_director", director));

            OfficeInteractionsBuilder.HidePendingPages();
            SetMode(director, demoPanelGo, EncounterMode.Participant);
            Selection.activeObject = null;
            SaveOrThrow(scene, ScenePath);

            // Demo variant: same scene, demo mode on, saved as a separate copy.
            SetMode(director, demoPanelGo, EncounterMode.Demo);
            if (!EditorSceneManager.SaveScene(scene, DemoScenePath, saveAsCopy: true))
            {
                throw new IOException($"Failed to save {DemoScenePath}");
            }
            SetMode(director, demoPanelGo, EncounterMode.Participant);
            SaveOrThrow(scene, ScenePath);
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();

            XRFoundationValidator.Report report = Phase3Validator.Validate(scene);
            report.Merge(CorporateOfficeValidator.Validate(scene));
            report.Merge(OfficeInteractionsValidator.Validate(scene));
            report.Title = "TrustBreach Phase 3 validation";
            report.Passed.Add($"Voice clips: {voice.Message}");
            if (scene.isDirty) SaveOrThrow(scene, ScenePath);
            AfterBuild?.Invoke(report); // Phase 4 re-applies its research layer to the regenerated scenes
            return report;
        }

        // =====================================================================================================
        // Dialogue panel
        // =====================================================================================================
        private static DialoguePanel BuildDialoguePanel(Transform ui, Transform npc)
        {
            // Beside Alex (his left, the participant's right), far enough out that he stays fully visible.
            Vector3 spot = ParticipantSpot + Vector3.up * 1.6f;
            Vector3 position = npc.TransformPoint(new Vector3(-1.05f, 0f, 0.45f));
            Transform root = Group(DialoguePanelName, ui, Vector3.zero);
            root.SetPositionAndRotation(position, Facing(Flat(spot - position)));

            Material metal = Lit("Metal_Graphite", Palette.Graphite, 0.6f, 0.7f);
            Box("StandBase", root, new Vector3(0f, 0.01f, 0.05f), new Vector3(0.42f, 0.02f, 0.32f), metal);
            Box("StandPole", root, new Vector3(0f, 0.44f, 0.05f), new Vector3(0.06f, 0.84f, 0.04f), metal);
            var standCollider = root.gameObject.AddComponent<BoxCollider>();
            standCollider.center = new Vector3(0f, 0.42f, 0.05f);
            standCollider.size = new Vector3(0.42f, 0.84f, 0.32f);

            var size = new Vector2(0.86f, 0.7f);
            Transform s = Screen("Screen", root, new Vector3(0f, 1.25f, 0f), Quaternion.Euler(8f, 0f, 0f), size, new Color(0.035f, 0.05f, 0.085f),
                out _, Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            Color navy = new Color(0.08f, 0.13f, 0.24f), blue = new Color(0.07f, 0.4f, 0.66f), slate = new Color(0.32f, 0.36f, 0.42f);
            Color green = new Color(0.1f, 0.55f, 0.3f), teal = Palette.Teal, amber = new Color(0.85f, 0.5f, 0.05f);

            ScreenRect(s, "HeaderBar", 0f, 0.31f, 0.86f, 0.08f, navy);
            TextMeshPro speaker = ScreenText(s, "SpeakerName", "Alex Morgan", -0.41f, 0.345f, 0.5f, 0.045f, 0.24f, Color.white, style: FontStyles.Bold);
            TextMeshPro role = ScreenText(s, "SpeakerRole", "IT Security  |  Aperture Systems", -0.41f, 0.302f, 0.5f, 0.03f, 0.14f, new Color(0.6f, 0.9f, 0.9f));
            XRButton mute = OfficeInteractionsBuilder.Button(s, "VoiceToggleButton", "dialogue.voice_toggle", "VOICE: ON", new Vector2(0.32f, 0.31f),
                new Vector2(0.17f, 0.05f), slate, 0.12f);
            TMP_Text muteLabel = s.Find("VoiceToggleButton_Label").GetComponent<TMP_Text>();

            TextMeshPro line = ScreenText(s, "LineText", "", -0.405f, 0.255f, 0.81f, 0.205f, 0.22f, Color.white);
            AutoSize(line, 0.16f, 0.22f);
            TextMeshPro objective = ScreenText(s, "ObjectiveText", "Requests from Alex appear here.", -0.405f, 0.042f, 0.81f, 0.04f, 0.13f,
                new Color(1f, 0.8f, 0.4f));
            AutoSize(objective, 0.1f, 0.13f);
            TextMeshPro status = ScreenText(s, "StatusText", "Point at TALK TO ALEX and pull the trigger.", -0.405f, 0.0f, 0.81f, 0.03f, 0.115f,
                new Color(0.7f, 0.85f, 0.9f), style: FontStyles.Italic);

            var optionButtons = new XRButton[6];
            var optionLabels = new TMP_Text[6];
            for (int i = 0; i < 6; i++)
            {
                float x = i % 2 == 0 ? -0.207f : 0.207f, y = -0.07f - (i / 2) * 0.08f;
                optionButtons[i] = OfficeInteractionsBuilder.Button(s, $"Option{i + 1}", $"dialogue.option_{i + 1}", $"Response {i + 1}", new Vector2(x, y),
                    new Vector2(0.4f, 0.07f), blue, 0.13f);
                optionLabels[i] = s.Find($"Option{i + 1}_Label").GetComponent<TMP_Text>();
                optionLabels[i].textWrappingMode = TextWrappingModes.Normal;
                optionLabels[i].rectTransform.sizeDelta = new Vector2(0.38f, 0.064f);
                AutoSize(optionLabels[i], 0.1f, 0.13f);
            }
            Transform talkGroup = Group("TalkGroup", s, Vector3.zero);
            XRButton talk = OfficeInteractionsBuilder.Button(talkGroup, "TalkButton", "dialogue.talk", "TALK TO ALEX", new Vector2(0f, -0.13f),
                new Vector2(0.46f, 0.09f), teal, 0.2f);
            Transform continueGroup = Group("ContinueGroup", s, Vector3.zero);
            XRButton cont = OfficeInteractionsBuilder.Button(continueGroup, "ContinueButton", "dialogue.continue", "CONTINUE", new Vector2(0f, -0.13f),
                new Vector2(0.42f, 0.085f), teal, 0.18f);

            Transform footer = Group("Footer", s, Vector3.zero);
            XRButton report = OfficeInteractionsBuilder.Button(footer, "ReportButton", "dialogue.report", "REPORT TO IT SECURITY", new Vector2(-0.207f, -0.305f),
                new Vector2(0.4f, 0.052f), green, 0.12f);
            XRButton exit = OfficeInteractionsBuilder.Button(footer, "EndConversationButton", "dialogue.end_conversation", "END CONVERSATION",
                new Vector2(0.207f, -0.305f), new Vector2(0.4f, 0.052f), slate, 0.12f);

            // Debrief: headline, page title, body, BACK / NEXT / CLOSE / RESTART EXPERIENCE (demo).
            Transform debrief = OfficeInteractionsBuilder.Page(s, "DebriefPage", size, new Color(0.03f, 0.045f, 0.075f));
            GameObject titleBar = ScreenRect(debrief, "TitleBar", 0f, 0.31f, 0.86f, 0.08f, teal);
            TextMeshPro headline = ScreenText(debrief, "Headline", "DEBRIEF", 0f, 0.31f, 0.82f, 0.075f, 0.2f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            TextMeshPro pageTitle = ScreenText(debrief, "PageTitle", "Debrief", -0.405f, 0.258f, 0.81f, 0.035f, 0.14f, new Color(0.55f, 0.92f, 0.92f), style: FontStyles.Bold);
            TextMeshPro debriefBody = ScreenText(debrief, "Body", "Debrief summary appears here.", -0.405f, 0.215f, 0.81f, 0.47f, 0.15f, new Color(0.93f, 0.95f, 0.97f));
            AutoSize(debriefBody, 0.1f, 0.15f);
            XRButton back = OfficeInteractionsBuilder.Button(debrief, "BackButton", "dialogue.debrief_back", "< BACK", new Vector2(-0.31f, -0.305f),
                new Vector2(0.2f, 0.052f), slate, 0.11f);
            XRButton next = OfficeInteractionsBuilder.Button(debrief, "NextButton", "dialogue.debrief_next", "NEXT >", new Vector2(-0.095f, -0.305f),
                new Vector2(0.2f, 0.052f), blue, 0.11f);
            XRButton close = OfficeInteractionsBuilder.Button(debrief, "CloseButton", "dialogue.debrief_close", "CLOSE", new Vector2(0.115f, -0.305f),
                new Vector2(0.19f, 0.052f), slate, 0.11f);
            Transform resetGroup = Group("ResetGroup", debrief, Vector3.zero);
            XRButton resetButton = OfficeInteractionsBuilder.Button(resetGroup, "ResetButton", "dialogue.restart_experience", "RESTART EXPERIENCE",
                new Vector2(0.315f, -0.305f), new Vector2(0.19f, 0.052f), amber, 0.085f);

            OfficeInteractionsBuilder.Hint(root, "Hint_Trigger", OfficeInteractionsBuilder.TriggerHint, new Vector3(0f, 0.81f, -0.02f), 0.6f);

            var panel = root.gameObject.AddComponent<DialoguePanel>();
            OfficeInteractionsBuilder.Inject(panel, ("_speakerName", speaker), ("_speakerRole", role), ("_lineText", line), ("_objectiveText", objective),
                ("_statusText", status), ("_talkButton", talk), ("_talkGroup", talkGroup.gameObject), ("_continueButton", cont),
                ("_continueGroup", continueGroup.gameObject), ("_debriefResetGroup", resetGroup.gameObject), ("_footer", footer.gameObject), ("_reportButton", report),
                ("_exitButton", exit), ("_muteButton", mute), ("_muteLabel", muteLabel), ("_debriefPage", debrief.gameObject),
                ("_debriefHeadline", headline), ("_debriefTitleBar", titleBar.GetComponent<Renderer>()), ("_debriefPageTitle", pageTitle), ("_debriefBody", debriefBody), ("_debriefNextButton", next),
                ("_debriefBackButton", back), ("_debriefCloseButton", close), ("_debriefResetButton", resetButton));
            var pso = new SerializedObject(panel);
            SerializedProperty buttons = pso.FindProperty("_optionButtons"), labels = pso.FindProperty("_optionLabels");
            buttons.arraySize = 6;
            labels.arraySize = 6;
            for (int i = 0; i < 6; i++)
            {
                buttons.GetArrayElementAtIndex(i).objectReferenceValue = optionButtons[i];
                labels.GetArrayElementAtIndex(i).objectReferenceValue = optionLabels[i];
            }
            pso.ApplyModifiedPropertiesWithoutUndo();

            // Initial (pre-conversation) state: invitation + Talk only.
            for (int i = 0; i < 6; i++)
            {
                optionButtons[i].gameObject.SetActive(false);
                optionLabels[i].gameObject.SetActive(false);
            }
            continueGroup.gameObject.SetActive(false);
            footer.gameObject.SetActive(false);
            line.text = EncounterDirector.InviteText;
            return panel;
        }

        private static void AutoSize(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
            text.textWrappingMode = TextWrappingModes.Normal;
        }

        // =====================================================================================================
        // Demo panel (developer mode only)
        // =====================================================================================================
        private static GameObject BuildDemoPanel(Transform ui, out DemoModePanel demo)
        {
            Transform root = Group(DemoPanelName, ui, Vector3.zero);
            root.SetPositionAndRotation(new Vector3(8.88f, 1.55f, -0.35f), Facing(Vector3.left));
            var size = new Vector2(0.95f, 0.62f);
            Transform s = Screen("Screen", root, Vector3.zero, Quaternion.identity, size, new Color(0.1f, 0.08f, 0.04f), out _,
                Lit("Bezel_Black", new Color(0.05f, 0.05f, 0.06f), 0.5f));
            Color amber = new Color(0.85f, 0.5f, 0.05f), slate = new Color(0.35f, 0.38f, 0.42f);
            ScreenRect(s, "HeaderBar", 0f, 0.27f, 0.95f, 0.08f, amber);
            ScreenText(s, "Title", "DEVELOPER / DEMO MODE  -  not shown to participants", 0f, 0.27f, 0.92f, 0.07f, 0.16f, Color.black,
                TextAlignmentOptions.Center, FontStyles.Bold);
            TextMeshPro state = ScreenText(s, "State", "Encounter state", -0.45f, 0.215f, 0.9f, 0.2f, 0.12f, new Color(1f, 0.9f, 0.75f));
            XRButton restart = OfficeInteractionsBuilder.Button(s, "RestartExperienceButton", "demo.restart_experience",
                "RESTART EXPERIENCE  (full reset + back to entrance)", new Vector2(0f, -0.035f), new Vector2(0.88f, 0.065f), amber, 0.13f);
            XRButton reset = OfficeInteractionsBuilder.Button(s, "ResetAllButton", "demo.reset_all", "RESET ALEX + SCENARIOS",
                new Vector2(-0.225f, -0.12f), new Vector2(0.43f, 0.06f), slate, 0.11f);
            XRButton skip = OfficeInteractionsBuilder.Button(s, "SkipLineButton", "demo.skip_line", "SKIP CURRENT LINE",
                new Vector2(0.225f, -0.12f), new Vector2(0.43f, 0.06f), slate, 0.11f);
            XRButton authority = OfficeInteractionsBuilder.Button(s, "BranchAuthorityButton", "demo.branch_authority", "BRANCH: AUTHORITY",
                new Vector2(-0.3f, -0.21f), new Vector2(0.28f, 0.06f), slate, 0.1f);
            XRButton urgency = OfficeInteractionsBuilder.Button(s, "BranchUrgencyButton", "demo.branch_urgency", "BRANCH: URGENCY",
                new Vector2(0f, -0.21f), new Vector2(0.28f, 0.06f), slate, 0.1f);
            XRButton trust = OfficeInteractionsBuilder.Button(s, "BranchTrustButton", "demo.branch_trust", "BRANCH: TRUST",
                new Vector2(0.3f, -0.21f), new Vector2(0.28f, 0.06f), slate, 0.1f);
            demo = root.gameObject.AddComponent<DemoModePanel>();
            OfficeInteractionsBuilder.Inject(demo, ("_stateText", state), ("_resetButton", reset), ("_authorityBranchButton", authority),
                ("_urgencyBranchButton", urgency), ("_trustBranchButton", trust), ("_skipLineButton", skip), ("_restartExperienceButton", restart));
            return root.gameObject;
        }

        // =====================================================================================================
        // Helpers
        // =====================================================================================================
        private static void SetMode(EncounterDirector director, GameObject demoPanel, EncounterMode mode)
        {
            var so = new SerializedObject(director);
            so.FindProperty("_mode").enumValueIndex = (int)mode;
            so.ApplyModifiedPropertiesWithoutUndo();
            demoPanel.SetActive(mode == EncounterMode.Demo);
            EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
        }

        private static Scene CopyAndOpen(string source, string target)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(target) == null)
            {
                if (!AssetDatabase.CopyAsset(source, target)) throw new IOException($"Copy {source} -> {target} failed");
            }
            else
            {
                File.Copy(Path.Combine(root, source), Path.Combine(root, target), true);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
            }
            return EditorSceneManager.OpenScene(target, OpenSceneMode.Single);
        }

        private static void SaveOrThrow(Scene scene, string path)
        {
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException($"Failed to save {path}");
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (string path in new[] { ScenePath, DemoScenePath })
            {
                if (scenes.All(s => s.path != path))
                {
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                }
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z).normalized;

        private static Transform Root(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(r => r.name == name)?.transform
            ?? throw new InvalidOperationException($"Scene root '{name}' not found.");

        private static Transform Find(Transform parent, string path) =>
            parent.Find(path) ?? throw new InvalidOperationException($"'{parent.name}/{path}' not found.");
    }
}
