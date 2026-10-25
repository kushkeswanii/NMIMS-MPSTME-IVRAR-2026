using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TrustBreach.AI;
using TrustBreach.Dialogue;
using TrustBreach.Dialogue.NPC;
using TrustBreach.Interactions;
using TrustBreach.Interactions.Scenarios;
using TrustBreach.Telemetry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using B = TrustBreach.Editor.TrustBreachCorporateOfficeBuilder;
using P = TrustBreach.Editor.Phase3AdversaryBuilder;

namespace TrustBreach.Editor
{
    /// <summary>Edit-time validation of the Phase 3 adversary scene (NPC, dialogue data, UI, wiring, layout).</summary>
    public static class Phase3Validator
    {
        [MenuItem("Tools/TRUST∕∕BREACH/Validate Phase 3 Scene", priority = 121)]
        [MenuItem("TrustBreach/Phase 3/Validate Adaptive Adversary Scene", priority = 121)]
        private static void ValidateFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != P.ScenePath && scene.path != P.DemoScenePath)
            {
                scene = EditorSceneManager.OpenScene(P.ScenePath, OpenSceneMode.Single);
            }
            XRFoundationValidator.Report report = Validate(scene);
            if (report.Success) Debug.Log(report); else Debug.LogError(report);
        }

        public static XRFoundationValidator.Report Validate(Scene scene)
        {
            var report = new XRFoundationValidator.Report { Title = "TrustBreach Phase 3 adversary validation" };
            GameObject[] roots = scene.GetRootGameObjects();
            List<GameObject> objects = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();

            EncounterDirector director = objects.Select(o => o.GetComponent<EncounterDirector>()).FirstOrDefault(c => c != null);
            report.Check(director != null && director.transform.parent != null && director.transform.parent.name == B.ManagersRootName,
                "EncounterDirector under Managers");
            if (director == null) return report;

            ValidateNpc(report, objects);
            ValidateGraph(report, OfficeInteractionsValidator.GetField<DialogueGraph>(director, "_graph"));
            ValidateWiring(report, objects, director);
            ValidatePanelLayout(report, objects);
            ValidatePanelHitTests(report, objects);
            ValidateSettings(report, director);
            return report;
        }

        private static void ValidateNpc(XRFoundationValidator.Report report, List<GameObject> objects)
        {
            GameObject npc = objects.FirstOrDefault(o => o.name == P.NpcName);
            report.Check(npc != null && npc.transform.parent != null && npc.transform.parent.name == B.ScenarioRootName, "Alex Morgan NPC under Scenario_Objects");
            if (npc == null) return;
            string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(npc);
            report.Check(prefab == AlexMorganCharacterBuilder.PrefabPath, "NPC is an instance of the reusable NPC_AlexMorgan prefab", prefab);
            Vector3 p = npc.transform.position;
            report.Check(Vector3.Distance(p, P.NpcPosition) < 0.05f && p.x > -9f && p.x < 9f && p.z > -2f && p.z < 4f,
                "NPC placed in the reception area (between reception and the staff gates)", p.ToString("F2"));

            // Measure in Alex's own frame (he is rotated to face visitors).
            Quaternion rotation = npc.transform.rotation;
            npc.transform.rotation = Quaternion.identity;
            Bounds body = Encapsulate(npc.GetComponentsInChildren<Renderer>(true).Where(r => !r.GetComponent<TMPro.TMP_Text>() && r.transform.parent?.name != "Subtitle"));
            npc.transform.rotation = rotation;
            report.Check(body.size.y > 1.7f && body.size.y < 1.9f && body.min.y > -0.02f && body.min.y < 0.03f,
                "Humanoid height 1.7-1.9 m standing on the floor", $"height {body.size.y:F2} m, feet at {body.min.y:F3}");
            report.Check(body.size.x > 0.4f && body.size.x < 0.75f && body.size.z > 0.2f && body.size.z < 0.45f, "Body proportions within adult range",
                $"width {body.size.x:F2}, depth {body.size.z:F2}");
            int meshParts = npc.GetComponentsInChildren<MeshFilter>(true).Count(f => f.sharedMesh != null && AssetDatabase.GetAssetPath(f.sharedMesh).Contains("/Avatars/AlexMorgan/"));
            report.Check(meshParts >= 15, "Body built from custom lofted meshes (not a single primitive)", $"{meshParts} custom mesh parts");

            var presence = npc.GetComponent<NPCPresence>();
            var voice = npc.GetComponentInChildren<NPCVoice>(true);
            report.Check(presence != null && OfficeInteractionsValidator.UnassignedReferences(presence).Count == 0, "NPCPresence wired (skeleton, face, badge)",
                presence == null ? "missing" : string.Join(",", OfficeInteractionsValidator.UnassignedReferences(presence)));
            report.Check(voice != null && OfficeInteractionsValidator.UnassignedReferences(voice).Count == 0, "NPCVoice wired (audio, subtitles)");
            AudioSource source = voice != null ? voice.GetComponent<AudioSource>() : null;
            report.Check(source != null && Mathf.Approximately(source.spatialBlend, 1f) && source.rolloffMode == AudioRolloffMode.Logarithmic &&
                         source.maxDistance > source.minDistance && !source.playOnAwake,
                "Voice is 3D spatial audio with distance attenuation");
            TMPro.TMP_Text badgeName = npc.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(t => t.name == "Name");
            report.Check(badgeName != null && badgeName.text == "ALEX MORGAN", "Name badge shows 'ALEX MORGAN' + company");
            report.Check(npc.GetComponent<CapsuleCollider>() != null, "NPC body collider (participant cannot walk through Alex)");
        }

        private static void ValidateGraph(XRFoundationValidator.Report report, DialogueGraph graph)
        {
            report.Check(graph != null && AssetDatabase.GetAssetPath(graph) == P.GraphPath, "Dialogue graph asset assigned", P.GraphPath);
            if (graph == null) return;
            graph.InvalidateLookup();
            var ids = new HashSet<string>(graph.Nodes.Select(n => n.Id));
            report.Check(ids.Count == graph.Nodes.Count && ids.Contains(graph.StartNodeId) && ids.Contains(graph.GlobalReportNodeId) && ids.Contains(graph.GlobalExitNodeId),
                "Graph node ids unique; start/report/exit nodes exist");
            List<string> badTargets = graph.Nodes.SelectMany(n => n.Options.Select(o => (n.Id, o.NextNodeId)).Concat(n.SignalTransitions.Select(t => (n.Id, t.NextNodeId))))
                .Where(x => !ids.Contains(x.Item2)).Select(x => $"{x.Item1}->{x.Item2}").ToList();
            report.Check(badTargets.Count == 0, "Every option / signal transition targets an existing node", string.Join(", ", badTargets));
            List<string> thin = graph.Nodes.Where(n => n.Kind != NodeKind.Terminal && n.Options.Count < 2).Select(n => n.Id).ToList();
            report.Check(thin.Count == 0, "Every non-terminal node defines at least 2 options (plus report / end)", string.Join(", ", thin));

            var reachable = new HashSet<string> { graph.StartNodeId, graph.GlobalReportNodeId, graph.GlobalExitNodeId };
            var queue = new Queue<string>(reachable);
            while (queue.Count > 0)
            {
                DialogueNode node = graph.GetNode(queue.Dequeue());
                if (node == null) continue;
                foreach (string next in node.Options.Select(o => o.NextNodeId).Concat(node.SignalTransitions.Select(t => t.NextNodeId)))
                {
                    if (reachable.Add(next)) queue.Enqueue(next);
                }
            }
            List<string> orphans = ids.Where(id => !reachable.Contains(id)).ToList();
            report.Check(orphans.Count == 0, "Every node is reachable from the opening", string.Join(", ", orphans));
            var strategies = graph.Nodes.SelectMany(n => n.Lines).Select(l => l.Strategy).Where(s => s != PersuasionStrategy.None).Distinct().ToList();
            report.Check(strategies.Count >= 3, "At least 3 persuasion strategies authored", string.Join(", ", strategies));
            var compromise = new[] { ScenarioSignal.KioskCredentialsEntered, ScenarioSignal.PhishingCredentialsSubmitted, ScenarioSignal.UsbConnected };
            report.Check(compromise.All(sig => graph.GlobalSignalTransitions.Any(t => t.Signal == sig && ids.Contains(t.NextNodeId))),
                "Completed compromises move Alex on from any node (graph-wide transitions)");
            var actions = graph.Nodes.SelectMany(n => n.Lines).Select(l => l.Action).Where(a => a != ScenarioAction.None).Distinct().ToList();
            report.Check(actions.Count == 4, "Alex links to all four existing scenarios", string.Join(", ", actions));
            List<LineVariant> lines = graph.AllLines().ToList();
            int voiced = lines.Count(l => l.Voice != null);
            if (voiced == lines.Count) report.Passed.Add($"All {lines.Count} lines have an offline voice clip");
            else report.Warnings.Add($"{lines.Count - voiced} of {lines.Count} lines have no voice clip (subtitles-only pacing for those lines)");
        }

        private static void ValidateWiring(XRFoundationValidator.Report report, List<GameObject> objects, EncounterDirector director)
        {
            void Wired(Component c, string label)
            {
                List<string> missing = c == null ? new List<string> { "component missing" } : OfficeInteractionsValidator.UnassignedReferences(c);
                report.Check(missing.Count == 0, $"{label}: every reference wired", string.Join(", ", missing));
            }
            Wired(director, "EncounterDirector");
            Wired(objects.Select(o => o.GetComponent<DialoguePanel>()).FirstOrDefault(c => c != null), "DialoguePanel");
            Wired(objects.Select(o => o.GetComponent<ScenarioEventAdapter>()).FirstOrDefault(c => c != null), "ScenarioEventAdapter");
            Wired(objects.Select(o => o.GetComponent<DemoModePanel>()).FirstOrDefault(c => c != null), "DemoModePanel");
            report.Check(objects.Any(o => o.GetComponent<TelemetryRecorder>() != null), "TelemetryRecorder present (local CSV + JSONL)");
            var scenarios = OfficeInteractionsValidator.GetField<ScenarioController[]>(director, "_scenarios");
            report.Check(scenarios != null && scenarios.Length == 4 && scenarios.All(s => s != null) && scenarios.Select(s => s.GetType()).Distinct().Count() == 4,
                "Director references the four existing Phase 2.5 scenario controllers (no duplicates created)");
            report.Check(objects.Count(o => o.GetComponent<PhishingEmailScenario>() != null) == 1 && objects.Count(o => o.GetComponent<PasswordResetKioskScenario>() != null) == 1,
                "No duplicate phishing / kiosk systems");
        }

        private static void ValidatePanelLayout(XRFoundationValidator.Report report, List<GameObject> objects)
        {
            GameObject panel = objects.FirstOrDefault(o => o.name == P.DialoguePanelName);
            GameObject npc = objects.FirstOrDefault(o => o.name == P.NpcName);
            if (panel == null || npc == null)
            {
                report.Check(false, "Dialogue panel present");
                return;
            }
            Transform screen = panel.transform.Find("Screen");
            Vector3 eye = P.ParticipantSpot + Vector3.up * 1.6f;
            float distance = Vector3.Distance(eye, screen.position);
            report.Check(distance > 1.0f && distance < 2.3f, "Dialogue panel at a comfortable reading distance (1.0-2.3 m)", $"{distance:F2} m");
            float facing = Vector3.Dot(-screen.forward, (eye - screen.position).normalized);
            report.Check(facing > 0.85f, "Dialogue panel faces the participant", $"dot {facing:F2}");
            float height = screen.position.y;
            report.Check(height > 1.1f && height < 1.6f, "Panel centre at a comfortable height", $"{height:F2} m");

            Bounds panelBounds = Encapsulate(screen.GetComponentsInChildren<Renderer>(true));
            Bounds npcBody = Encapsulate(npc.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(npc.transform.Find("Subtitle"))));
            GameObject subtitleGo = npc.transform.Find("Subtitle").gameObject;
            bool subtitleWasActive = subtitleGo.activeSelf;
            subtitleGo.SetActive(true);
            Bounds subtitle = Encapsulate(subtitleGo.GetComponentsInChildren<Renderer>(true));
            subtitleGo.SetActive(subtitleWasActive);
            report.Check(!panelBounds.Intersects(npcBody), "Dialogue panel does not intersect Alex");
            // Alex must stay visible from the participant's spot: sight lines to his head and chest miss the panel.
            Transform head = npc.GetComponentsInChildren<Transform>(true).First(t => t.name == "Head");
            var blocked = new List<string>();
            foreach (Vector3 target in new[] { head.position + Vector3.up * 0.1f, head.position + Vector3.down * 0.35f })
            {
                Vector3 dir = target - eye;
                if (panelBounds.IntersectRay(new Ray(eye, dir.normalized), out float hit) && hit < dir.magnitude) blocked.Add(target.ToString("F2"));
            }
            report.Check(blocked.Count == 0, "Alex remains fully visible beside the panel from the participant's spot", string.Join(", ", blocked));
            report.Check(!panelBounds.Intersects(subtitle), "Dialogue panel does not overlap Alex's subtitles");

            var others = new List<(string name, Bounds bounds)>();
            GameObject uiRoot = panel.transform.parent.gameObject;
            foreach (Transform t in uiRoot.transform)
            {
                if (t.gameObject != panel) others.Add((t.name, Encapsulate(t.GetComponentsInChildren<Renderer>(true))));
            }
            Transform interactions = objects.FirstOrDefault(o => o.name == OfficeInteractionsBuilder.RootName)?.transform;
            if (interactions != null)
            {
                foreach (Transform t in interactions) others.Add((t.name, Encapsulate(t.GetComponentsInChildren<Renderer>(true))));
            }
            List<string> overlapping = others.Where(o => o.bounds.size != Vector3.zero && o.bounds.Intersects(panelBounds)).Select(o => o.name).ToList();
            report.Check(overlapping.Count == 0, "Dialogue panel overlaps no other UI panel", string.Join(", ", overlapping));

            GameObject demo = objects.FirstOrDefault(o => o.name == P.DemoPanelName);
            if (demo != null)
            {
                Bounds demoBounds = Encapsulate(demo.GetComponentsInChildren<Renderer>(true));
                report.Check(!demoBounds.Intersects(panelBounds) && !demoBounds.Intersects(npcBody), "Demo panel is separate from the participant UI");
            }
        }

        /// <summary>Hit-tests dialogue / demo panel buttons in each UI state the panel can actually be in.</summary>
        private static void ValidatePanelHitTests(XRFoundationValidator.Report report, List<GameObject> objects)
        {
            var panel = objects.Select(o => o.GetComponent<DialoguePanel>()).FirstOrDefault(c => c != null);
            if (panel == null) return;
            Transform s = panel.transform.Find("Screen");
            GameObject G(string path) => s.Find(path)?.gameObject;
            GameObject talk = G("TalkGroup"), cont = G("ContinueGroup"), footer = G("Footer"), debrief = G("DebriefPage");
            List<GameObject> options = Enumerable.Range(1, 6).Select(i => G($"Option{i}")).ToList();
            var all = new List<GameObject> { talk, cont, footer, debrief };
            all.AddRange(options);
            Dictionary<GameObject, bool> original = all.Where(g => g != null).ToDictionary(g => g, g => g.activeSelf);

            void State(string label, IEnumerable<GameObject> active)
            {
                var on = new HashSet<GameObject>(active);
                foreach (GameObject g in original.Keys) g.SetActive(on.Contains(g));
                List<XRButton> buttons = on.SelectMany(g => g.GetComponentsInChildren<XRButton>(true))
                    .Concat(new[] { panel.MuteButton }).Distinct().ToList();
                var temp = new XRFoundationValidator.Report();
                OfficeInteractionsValidator.ValidateHitTesting(temp, s, buttons);
                report.Check(temp.Success, $"Dialogue panel '{label}' state: every visible button is the nearest ray target ({buttons.Count} buttons)",
                    string.Join("; ", temp.Failed));
            }
            try
            {
                State("idle", new[] { talk });
                State("options", options.Concat(new[] { footer }));
                State("continue", new[] { cont });
                State("debrief", new[] { debrief });
            }
            finally
            {
                foreach (KeyValuePair<GameObject, bool> pair in original) pair.Key.SetActive(pair.Value);
            }

            GameObject demo = objects.FirstOrDefault(o => o.name == P.DemoPanelName);
            if (demo != null)
            {
                bool was = demo.activeSelf;
                demo.SetActive(true);
                var temp = new XRFoundationValidator.Report();
                OfficeInteractionsValidator.ValidateHitTesting(temp, demo.transform, demo.GetComponentsInChildren<XRButton>(true).ToList());
                demo.SetActive(was);
                report.Check(temp.Success, "Demo panel buttons are the nearest ray targets", string.Join("; ", temp.Failed));
            }

            // Initial (participant) state.
            report.Check(talk != null && talk.activeSelf && !cont.activeSelf && !footer.activeSelf && !debrief.activeSelf && options.All(o => !o.activeSelf),
                "Panel starts in the invitation state (only TALK TO ALEX visible)");
        }

        private static void ValidateSettings(XRFoundationValidator.Report report, EncounterDirector director)
        {
            var mode = (EncounterMode)new SerializedObject(director).FindProperty("_mode").enumValueIndex;
            GameObject demo = OfficeInteractionsValidator.GetField<GameObject>(director, "_demoPanel");
            Scene scene = director.gameObject.scene;
            bool isDemoScene = scene.path == P.DemoScenePath;
            report.Check(isDemoScene ? mode == EncounterMode.Demo && demo.activeSelf : mode == EncounterMode.Participant && !demo.activeSelf,
                isDemoScene ? "Demo scene: demo mode on, demo panel visible" : "Participant scene: participant mode, demo panel hidden");

            var llm = OfficeInteractionsValidator.GetField<LLMProviderSettings>(director, "_llmSettings");
            report.Check(llm != null && !llm.Enabled, "LLM provider disabled by default (offline scripted engine is the baseline)");
            List<string> keyFields = typeof(LLMProviderSettings).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(f => f.Name.ToLowerInvariant().Contains("key") && f.Name != "_apiKeyEnvironmentVariable").Select(f => f.Name).ToList();
            report.Check(keyFields.Count == 0, "No API key can be stored in Unity assets (only an environment-variable name)", string.Join(", ", keyFields));
            OVRManager manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<OVRManager>(true)).FirstOrDefault();
            report.Check(manager != null && !manager.localDimming, "Local dimming off (removes the project-caused 'Local Dimming feature is not supported' warning)");
            var experienceStart = OfficeInteractionsValidator.GetField<Transform>(director, "_experienceStart");
            report.Check(experienceStart != null && experienceStart.name == B.XRRigRootName, "Restart Experience returns the participant to the entrance spawn");
            report.Check(EditorBuildSettings.scenes.Any(s => s.path == P.ScenePath) && EditorBuildSettings.scenes.Any(s => s.path == P.DemoScenePath) &&
                         EditorBuildSettings.scenes.Any(s => s.path == XRFoundationSceneBuilder.ScenePath) &&
                         EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled)?.path == P.ScenePath,
                "Phase 3 scenes in Build Settings; the participant scene is the boot scene (Experience > Apply Release Build Scene List)");
        }

        private static Bounds Encapsulate(IEnumerable<Renderer> renderers)
        {
            Bounds? total = null;
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue; // inactive renderers report empty bounds at the origin
                if (total == null) total = r.bounds;
                else
                {
                    Bounds b = total.Value;
                    b.Encapsulate(r.bounds);
                    total = b;
                }
            }
            return total ?? new Bounds();
        }
    }
}
