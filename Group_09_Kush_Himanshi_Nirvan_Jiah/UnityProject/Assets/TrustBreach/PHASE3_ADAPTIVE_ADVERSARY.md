# TRUST//BREACH — Phase 3: Adaptive Social-Engineering Adversary ("Alex Morgan")

## Scenes (generated; the Phase 1 and Phase 2.5 scenes are not modified)
- `Assets/TrustBreach/Scenes/TrustBreach_Phase3_Adversary.unity`: **participant mode**. Realistic run with no debug UI and no reset.
- `Assets/TrustBreach/Scenes/TrustBreach_Phase3_Adversary_DEMO.unity`: **developer/demo mode**. An amber DEMO panel on the east lobby wall provides a state readout, branch selection (Authority / Urgency / Trust), skip line, and RESET (resets the encounter, all four office scenarios and the anonymous session).

Rebuild with **Tools ▸ TRUST∕∕BREACH ▸ Build Phase 3 Adaptive Adversary**. The rebuild is idempotent:
- both scenes are regenerated from the office scene;
- assets are updated in place;
- only changed voice lines are re-synthesised;
- the office scene is backed up to `Backups/scenes/` first.

Validate with **Tools ▸ TRUST∕∕BREACH ▸ Validate Phase 3 Scene**. Tests: `AdaptiveDialogueTests`, `Phase3SceneTests`.

## Components
| Piece | Location |
|---|---|
| NPC prefab (procedural low-poly humanoid, about 1.78 m) | `Prefabs/NPC_AlexMorgan.prefab`, meshes in `Avatars/AlexMorgan/Meshes` |
| Presence: gaze with glance-aways, body turn, blink, breathing, gestures | `Scripts/Dialogue/NPC/NPCPresence.cs` |
| Voice: 3D AudioSource, sentence subtitles, lip flap, mute | `Scripts/Dialogue/NPC/NPCVoice.cs` |
| Conversation data | `Dialogue/AlexMorgan_Encounter.asset` (authored in `Scripts/Dialogue/Content/AlexMorganEncounter.cs`) |
| Deterministic adaptive engine | `Scripts/Dialogue/AdaptiveDialogueEngine.cs` |
| Orchestration / UI / adapters | `EncounterDirector`, `DialoguePanel`, `ScenarioEventAdapter`, `DemoModePanel` |
| Optional LLM layer | `Scripts/AI` (`ILLMDialogueProvider`, `DialogueResponseValidator`, `LLMProviderSettings`), with settings in `Dialogue/LLMProviderSettings.asset` |
| Telemetry files | `Scripts/Telemetry/TelemetryRecorder.cs` |

## Conversation design
- **Opening:** "Hi, I'm Alex from IT Security. We've flagged an urgent authentication issue. Could you help me verify your account?"
- **Responses:** cooperate, ask for ID, challenge, request independent verification, refuse, report. *Report* and *End conversation* are always available in the footer.
- **Strategies:** Authority (impersonation, "this isn't optional"), Urgency (lock-out countdown, queue times), and TrustBuilding (gratitude, helpfulness, social proof).
- **Adaptation:** Alex's line and options depend on:
  - suspicion and cooperation scores;
  - repeated challenges or refusals (escalation, then withdrawal);
  - earlier office actions, e.g. a phishing email reported before meeting him, the kiosk already reported, or a failed badge verification.
- **Scenario links:**

  | Alex's request | Existing Phase 2.5 scenario | Effect on Alex |
  |---|---|---|
  | Use the password-reset kiosk | Kiosk | Compromise or report moves him on |
  | Check the email | Phishing email | Compromise or report moves him on |
  | Check the visitor badge (his "colleague") | Visitor badge | A failed verification raises suspicion |
  | Plug the "patch" USB into the dock | USB drive | Connect or submit moves him on |
- **Recovery:** risky steps followed by a report or independent verification produce the outcome **Recovered**.
- **No dead ends:** the exhaustive branch test explores every reachable state.
- **Outcomes:** Compromised, Recovered, Reported, VerifiedIndependently, Resisted. A DEBRIEF PREVIEW (the reveal) appears only after the encounter ends.

## Telemetry
`DecisionLog` (Phase 2.5 API unchanged) records exactly one event per real decision. Columns:
- identity and timing: anonymous `session_id`, `seq`, `session_time_s`;
- the decision: `event_name`, `object_id`, `choice`, `outcome`, `risky`, `recovery_after_risk`, `source`;
- dialogue context: `dialogue_node`, `response_id`, `persuasion_strategy`, `response_latency_s`, `choice_class`, `content_source` (scripted/model).

Non-decisions (lines delivered, scenario signals, encounter completed, mute, demo jumps, resets) go to a separate lifecycle stream.

Files are written to `<persistentDataPath>/TrustBreach/telemetry/trial_<session>.csv` and `trial_<session>_events.jsonl`. On this Mac, `persistentDataPath` is `~/Library/Application Support/DefaultCompany/TrustBreach_Group09`.

No names, credentials, free text or audio are recorded.

## Audio
Voice clips are generated offline at build time with the macOS speech synthesiser (`say -v Daniel`): 30 WAVs in `Audio/Dialogue/AlexMorgan`. No cloud TTS is used. On non-macOS editors the lines fall back to subtitles-only pacing. Model-generated wording (if ever enabled) is subtitles-only, because no clip exists for it.

## LLM
Disabled by default. Nothing registers a live provider.

To add one:
1. Implement `ILLMDialogueProvider`, reading the key from the environment variable named in `LLMProviderSettings` (there is no key field, by design).
2. Call `LLMDialogueProviderRegistry.Register(...)` at startup.
3. Enable the settings asset.

Every generated line must pass `DialogueResponseValidator`, which enforces:
- the node's permitted actions;
- no links;
- no persona break or early reveal;
- no out-of-scope data requests;
- length limits.

Otherwise the scripted line is used. Timeouts also fall back to the script.

## Phase 3.1: Outcome accuracy, scenario integration, VR polish
**Investigated bug.** Session `trial_d0fa31651122` recorded `password_kiosk.reported`, not `credentials_entered`. The red compromise happened in the previous session, which was correctly COMPROMISED. The debrief said RECOVERED because Phase 3 counted *promises* ("OK, I'll use the kiosk") as risky, and it never listed scenario actions. Both are fixed.

**Outcome rules** (`AdaptiveDialogueEngine.ResolveOutcome`; evidence only):
- **COMPROMISED:** an actual risky action with no covering corrective afterwards. Risky actions are: phishing link opened or credentials submitted, kiosk password entered, USB connected, or the password told to Alex.
- **RECOVERED:** every risky action is followed by a corrective that covers it.
  - Reporting Alex, or verifying with IT in person, covers every channel.
  - A scenario report (email or kiosk report, USB hand-in) covers its own channel only.
- **RESISTED:** no risky action, and the participant reported or verified (including the badge check).
- **DECLINED** (preserved state): refused until Alex gave up, with no report or verification.
- **INCOMPLETE:** ended before any security outcome.

**Scenario integration:**
- Phase 2.5 decisions are tagged `security_action`, with `corrective` and `channel` fields.
- Completed compromises move Alex on from any node; promises never do.
- If the participant acts elsewhere, Alex's reaction waits until they return (within 3.2 m). He then names the specific action, e.g. "I can see your kiosk sign-in came through".
- After a compromise the participant can still report or go to IT, which gives RECOVERED.

**Telemetry:**
- `trial_<id>.csv`: one row per decision (20 columns, adding `category`, `risky_action`, `corrective` and `channel`).
- `trial_<id>_scenario_outcomes.csv`: actual scenario actions only.
- `recovery_after_risk` follows the coverage rule above.

**Debrief:** three pages, with BACK / NEXT, colour-coded by outcome:
1. Outcome and your replies.
2. Completed security actions with timestamps, and whether recovery happened.
3. Techniques with quotes, and up to four targeted recommendations.

**Demo:** RESTART EXPERIENCE (demo panel and debrief) resets the encounter, all scenarios and the session, and returns the participant to the entrance.

**Warnings:** "Local Dimming feature is not supported" was project-caused, so `OVRManager._localDimming` is now off in the Phase 3 scenes. The rest are Simulator runtime limitations or the Unity account service.
