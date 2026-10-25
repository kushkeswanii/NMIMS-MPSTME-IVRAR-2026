# TRUST//BREACH — Project Guide (NMIMS IVRAR, Group 09)

*Immersive social-engineering and cybersecurity awareness training in VR.*
**Platform:** Unity 6.5 (6000.5.3f1), URP, Meta XR SDK v207 (OpenXR) and Interaction SDK; target Meta Quest 3 (Android); developed on an Apple Silicon Mac with the Meta XR Simulator.

---

## 1. Overview and objectives
**Problem.** Most security breaches start with people, not code: a convincing email, a fake sign-in page, a "colleague" asking for help, a USB stick left in the car park. Slide-based awareness training tells people the rules but rarely makes them practise under realistic pressure.

**Idea.** Put the participant inside a believable office (Aperture Systems) where everyday security situations happen naturally. Then a friendly, persuasive visitor, **Alex Morgan** "from IT Security", tries to talk them into risky actions. The participant learns by doing, then gets a personal, non-judgmental debrief built from what they actually did.

**Learning objectives.** After the experience a participant should be able to:
1. Recognise phishing cues: a look-alike sender domain, urgency and a mismatched link.
2. Refuse to enter credentials on a "Not secure" page or kiosk.
3. Verify a visitor's identity instead of trusting a badge.
4. Hand found USB devices to IT instead of plugging them in.
5. Recognise the persuasion techniques authority, urgency and trust-building.
6. Verify independently, through a channel *they* choose, and report, including after a mistake (recovery).

**Research aim (optional).** With explicit consent, store an anonymous record of decisions to study behaviour. No claim of real-world validity is made, because no human study has been run.

## 2. Architecture
```
XR rig (OVRCameraRig + ISDK comprehensive rig)  ── controllers / hands, rays, grab, locomotion
        │  ISDK RayInteractable / Grabbable
        ▼
XRButton / PointableEventRelay ──► TrustBreachEventBus (interaction events)
        │
        ▼
ScenarioController ×4 (email, kiosk, badge, USB) ──► DecisionLog (one event per real decision)
        │                                                   │
        ▼                                                   ├─► TelemetryRecorder (Phase 3 CSV/JSONL, consent-gated)
ScenarioEventAdapter ──► EncounterDirector ◄──► AdaptiveDialogueEngine  ├─► ResearchEventCollector (Phase 4, consent-gated)
                          │   (Alex: voice, gestures, DialoguePanel)    │        └─► ResearchStore (real / demo / test folders)
                          ▼                                             │
                   Debrief (4 pages + "Your session data")              └─► Desktop dashboard (Phase 4, separate scene)
                          ▲
ExperienceGuidePanel + ExperienceProgressStrip (Phase 5) ── read state only, record nothing
```

**Assemblies.** Each layer is a separate assembly, so lower layers never depend on higher ones:
- `TrustBreach.Core` (no XR dependency): event bus, `DecisionLog`, consent.
- `TrustBreach.Interactions`: XR buttons and the four scenarios.
- `TrustBreach.AI`: optional LLM interface; disabled.
- `TrustBreach.Dialogue`: Alex Morgan.
- `TrustBreach.Telemetry`.
- `TrustBreach.Analytics`, `.Dashboard`: Phase 4.
- `TrustBreach.Experience`: Phase 5.
- Editor-only builders and validators, plus EditMode tests.

**Generated, not hand-assembled.** Every scene is produced by an idempotent editor builder and checked by a validator. Rebuilding never duplicates objects, and builders back up scenes to `Backups/scenes/` first.

## 3. Phases
| Phase | What it delivered | Key code |
|---|---|---|
| 1 XR Foundation | Meta `OVRCameraRig` + Interaction SDK rig (controllers, hands, rays, grab, smooth locomotion, turning), test interactables, project settings for Quest | `Editor/XRFoundationSceneBuilder.cs`, `XRProjectConfigurator.cs` |
| 2 Corporate office | "Aperture Systems": reception, hallway, workstation pods, meeting room, IT security desk; scenario artifacts with red flags | `Editor/TrustBreachCorporateOfficeBuilder.cs` |
| 2.5 Decision interactions | Ray-pressable `XRButton`s, the four scenario state machines, `DecisionLog` (Safe / Unsafe / Neutral) | `Scripts/Interactions/*`, `Scripts/Core/DecisionLog.cs` |
| 3 Adaptive adversary | Alex Morgan NPC (procedural humanoid, gaze, gestures, offline-synthesised voice, subtitles), adaptive dialogue engine, debrief, DEMO scene | `Scripts/Dialogue/*` |
| 3.1 Outcome accuracy | Evidence-only outcome rules; promises are not compromises; Alex reacts to real scenario actions when you return | `AdaptiveDialogueEngine.ResolveOutcome` |
| 4 Behavioural intelligence | Consent panel, anonymous research store, deterministic scoring, exports, desktop dashboard, synthetic DEMO data | `Analytics/*` |
| 5 Final integration | Welcome / onboarding guide with a 6-step progress bar, lazy-follow progress strip, participant restart and exit, debrief page "At work: verify and report", validators and tests | `Experience/*` |

Per-phase details: `PHASE1_XR_FOUNDATION.md`, `PHASE2_CORPORATE_OFFICE.md`, `PHASE3_ADAPTIVE_ADVERSARY.md`, `PHASE4_BEHAVIORAL_INTELLIGENCE.md`, `PHASE5_FINAL_INTEGRATION.md`.

## 4. Scenes
| Scene | Path | Use |
|---|---|---|
| XR Foundation | `Assets/TrustBreach/Scenes/TrustBreach_XR_Foundation.unity` | Phase 1 test room (in the build list, disabled) |
| Corporate Office | `Assets/TrustBreach/Scenes/TrustBreach_CorporateOffice.unity` | Phase 2 / 2.5 office with scenarios, without Alex |
| **Participant** | `Assets/TrustBreach/Scenes/TrustBreach_Phase3_Adversary.unity` | Full experience; **headset build boot scene (index 0, the only enabled scene)**. Research data → **REAL** (only with consent) |
| **Demo / practice** | `Assets/TrustBreach/Scenes/TrustBreach_Phase3_Adversary_DEMO.unity` | Same experience plus the DEMO panel. Data → **TEST**, never REAL |
| Research dashboard | `Assets/TrustBreach/Analytics/Scenes/TrustBreach_ResearchDashboard.unity` | Desktop only (mouse and keyboard); not in the Quest build |

## 5. Meta XR setup and interaction
- **Rig:** Meta `OVRCameraRig` (floor-level origin) + `OVRComprehensiveInteractionRig`, created through Meta's public Quick Actions API. No XR Interaction Toolkit rig is used.
- **Selecting:** every on-screen button is an ISDK `RayInteractable` driving `XRButton`. Aim, and the button lightens with a white frame. Pull the **index trigger** to press; it activates on release over the button. A chosen option keeps a teal frame.
- **Grabbing:** the visitor badge and USB drive are ISDK `Grabbable`s (controller grab and hand grab). Hold **GRIP** to grab and release it to let go.
- **Locomotion:** smooth movement with the left stick, turning with the right stick.
- **UI comfort:**
  - Panels sit at standing eye height (1.45 m centre), 1.4–2 m from the participant.
  - Floating panels have no blocking colliders, and validators check they do not obstruct the walking route.
  - The Phase 5 progress strip is lazy-follow, not head-locked.

**Meta XR Simulator controls** (keyboard and mouse is the Simulator's default input):
| Action | Simulator | Quest controller |
|---|---|---|
| Move | W A S D | Left thumbstick |
| Look around | Hold right mouse button + move mouse; arrow keys rotate | Head movement |
| Turn | (look with mouse) | Right thumbstick |
| Select / press | **T** (the trigger key shown on this project's in-scene hints) | Index trigger |
| Grab / release | See the Simulator's **Input Bindings** panel (bindings vary by Simulator version) | Hold / release GRIP |

Activate the Simulator in the Editor with **Window ▸ Meta ▸ Meta XR Simulator ▸ Activate**. Redo this after every Editor restart.

## 6. The four security scenarios
| Scenario | Where | Risky action (Unsafe) | Protective action (Safe) | Red flags |
|---|---|---|---|---|
| Phishing email | Hero desk, workstation pod | Open the "verify now" link; submit credentials | Report the email | Sender `aperture-systerns.com` (look-alike), 2-hour deadline, mismatched link |
| Password-reset kiosk | IT security desk | Sign in and enter the password | Cancel / report the kiosk to IT | "Not secure" HTTP, wrong-domain certificate, asks for the current password |
| Visitor badge | Reception badge tray | (trusting it) | Pick it up (GRIP), inspect, **verify identity** | "SYSTMES" misspelling, off-brand logo, no hologram, expired, "escort not required" |
| Found USB drive | Lost and found, IT desk | Plug it into the workstation dock | Hand it in at the IT drop-off | "Q4 BONUSES" bait label, "plug it in" note |

Everything is simulated: no network access, no credential capture and no real USB access. Each real decision is logged once in `DecisionLog`.

## 7. Alex Morgan: dialogue and adaptation
- **Persona:** "Alex from IT Security" approaches near the IT desk with an "urgent authentication issue".
- **Presentation:**
  - The voice is synthesised offline at build time with the macOS `say` voice; no cloud service is used.
  - Lines are shown as subtitles.
  - Alex has gestures, gaze, and points at the scenario he wants you to use.
- **Replies:** cooperate, ask for ID, challenge, request independent verification, refuse, or report. **REPORT** and **END CONVERSATION** are always on the panel footer while Alex is talking.
- **Persuasion strategies:** Authority ("this isn't optional"), Urgency (lock-out countdown) and Trust-building (gratitude, social proof).
- **Adaptation:** a deterministic engine tracks *suspicion*, *cooperation* and *escalation*. Alex changes strategy, line and request based on your replies.
- **Real-action awareness:** if you act in the office, for example using the kiosk, Alex's reaction waits until you return and then names the action. The engine is pure C# and fully unit-tested, including an exhaustive branch test with no dead ends.
- **Outcomes (evidence only):**
  - **COMPROMISED:** a completed risky action that is not covered by a later report or verification.
  - **RECOVERED:** every risky action was covered afterwards.
  - **RESISTED:** no risky action, and the participant verified or reported.
  - **DECLINED:** refused until Alex gave up, with no report.
  - **INCOMPLETE:** ended early.
  - Agreeing in conversation ("OK, I'll use the kiosk") is a *promise*, not a compromise.
- **Debrief:** four pages, plus a fifth "Your session data" page.
  1. Outcome and your replies, each marked SAFE / RISKY / NEUTRAL.
  2. Security actions you actually completed, with times, and whether recovery happened.
  3. Persuasion techniques Alex used, quoted, and up to four targeted recommendations.
  4. *At work: verify and report*: why independent verification matters, your real verification and report counts, and five workplace steps.
  5. Your session data: the anonymous session code, or "No research data was saved".
- **LLM layer:** optional and disabled. Any generated line must pass a validator or the scripted line is used. No keys are stored in the project.

## 8. Research analytics and scoring (Phase 4)
- **Consent:** nothing is written until the participant presses **I AGREE** on the research panel, and only before their first decision, so sessions are never partial. Declining changes nothing in the experience. **WITHDRAW & DELETE** (two presses) deletes the session's research and telemetry files.
- **Stored:** a random session code, chosen options, security actions and decision times.
- **Never stored:** names, passwords, typed text, voice, audio, video, location or device identifiers.
- **Datasets (never mixed):**
  - `real/`: participant scene, consented sessions only.
  - `test/`: DEMO scene and automated tests.
  - `demo/`: synthetic data, ids `DEMO-…`, seed 20260.
  - All three live under `<persistentDataPath>/TrustBreach/research/`.
- **Metrics:** each rate is shown with its numerator and denominator. An empty denominator shows "—", never 0.
  - Risky-action rate.
  - Recovery rate.
  - Reporting rate.
  - Scenario completion.
  - Verification per reply.
  - Intention vs action (promise followed by the action).
  - Decision-time median and quartiles.
  - Per-technique and per-scenario rates.
- **Descriptive index (0–100), not validated:**
  - Weights: action safety 0.40, persuasion resistance 0.25, verification 0.15, reporting / recovery 0.20.
  - Missing components are renormalised, and at least 2 components are required.
  - It is a transparent summary of behaviour *in this simulation*, not a susceptibility measure.
- **Dashboard** (desktop):
  - Switch between REAL, DEMO DATA and TEST / PRACTICE.
  - Filter by session, scenario and outcome.
  - Hover any number for its definition.
  - Click a row for that session's timeline.
  - EXPORT writes JSON per session, `events.csv`, `session_summary.csv`, `aggregate_metrics.csv` and a manifest.
  - Deleting a session asks for confirmation.

Schema: `Analytics/Docs/RESEARCH_DATA_SCHEMA.md`.

## 9. Participant journey (Phase 5)
| Step | What happens |
|---|---|
| 1 Welcome | Guide panel ahead-right of the entrance explains the exercise. Press START. |
| 2 Research consent | Guide points left to the research panel: I AGREE / NO THANKS (optional). The guide moves on automatically. |
| 3 XR controls | Trigger to select, GRIP to grab / release, sticks to move / turn. Press GOT IT. |
| 4 Explore the office | Checklist of the four scenarios ticks as each is completed; the strip shows "STEP 4 OF 6". |
| 5 Meet Alex Morgan | Talk at the IT desk. The guide follows the conversation. |
| 6 Debrief | Read the pages on Alex's panel, then **RESTART EXPERIENCE** (full reset, consent asked again) or **EXIT** (press twice). |

The guide never traps a participant: acting, talking or finishing moves it forward on its own, and it records nothing.

The progress strip:
- sits about 1.25 m ahead and about 20° below eye level;
- only glides back after you turn more than 32° away;
- hides near panels and when an object is in front of you, so it never covers what you are using.

## 10. How to run
1. Open the project in Unity 6000.5.3f1. Wait for the import and check the Console shows 0 errors.
2. For XR in the Editor, use **Window ▸ Meta ▸ Meta XR Simulator ▸ Activate**, or connect a Quest via Link.
3. **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Play Demo Experience (TEST data, never REAL)** for practice and presentations, or **Play Participant Experience (REAL research data)** for real sessions.
4. Stop Play Mode, then **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Open Research Dashboard (desktop)**.
5. Validation: **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Validate Phase 5 (includes Phases 2-4)**. Expect 418 passed, 0 failed.
6. Unit tests: **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All**. Expect 53 / 53.
7. Quest build: **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Apply Release Build Scene List**, then **File ▸ Build Profiles ▸ Android ▸ Switch Platform ▸ Build And Run**. A build guard stops Android builds whose first scene is not the participant scene. Full steps: `SUBMISSION_CHECKLIST.md` section E.

## 11. Testing results (23 Oct 2026)
All of these are **automated** in the Unity Editor. Play Mode tests press the real XR buttons through the right controller's Interaction SDK `RayInteractor`, the same pipeline the trigger drives.

| Test | Result |
|---|---|
| Compilation | 0 errors, 0 warnings from project code |
| Static validation: Phase 5 + Phase 4 + Phase 3 + Phase 2.5 + Phase 2 validators, both scenes, dashboard scene | **418 / 418** |
| EditMode unit tests (all assemblies) | **53 / 53** |
| Phase 5 Play Mode, DEMO scene (onboarding, consent auto-advance, checklist, progress strip, debrief fits and navigation, EXIT arming, RESTART reset, declined run, REAL untouched) | **18 / 18** |
| Phase 5 Play Mode, participant scene (restart refused before debrief, onboarding, debrief restart, guide restart, REAL untouched) | **7 / 7** |
| EXIT pressed twice stops Play Mode | Pass (log: "participant chose EXIT") |
| Phase 4 consent / research regression (5 outcomes, withdrawal, restarts, scene transition, dataset isolation) | **15 / 15** |
| Phase 3.1 scenario + dialogue regression (V1–V9) | **9 / 9** |
| Dashboard regression (empty REAL, DEMO generate, KPIs, hover, filters, timeline, export, delete confirm, TEST view) | **11 / 11** |
| Phase 2.5 scenario branch sets A and C (Phase 4 session) | Pass; set B timed out (Editor stall, not a failure) and was not rerun |

**Defects found and fixed in Phase 5 testing:**
- Agreeing to consent reset the guide to Welcome.
- The guide overlapped the orientation totem from the spawn.
- The progress strip could sink into the kiosk.
- The step label wrapped on the progress strip.
- The dashboard title was cut off.
- A per-frame LINQ allocation in the guide.
- Final QA: the Quest build would have booted into the Phase 1 test room (index 0), so the experience was unreachable on a headset. Fixed: the participant scene is the boot scene, protected by a build guard.

**Manual Meta XR Simulator test:** reported as passed by the project team: onboarding, consent, XR interactions, office, Alex, kiosk, debrief navigation, restart to Welcome, and Exit. This was not reproduced by automation.

**Not tested:** Android build and physical Quest headset run (`SUBMISSION_CHECKLIST.md` section E). Full QA evidence: `FINAL_QA_REPORT.md`.

## 12. Known limitations
- No human-participant study, so no real-world effectiveness or validity is claimed. DEMO data is synthetic, and the index is not validated.
- Automated decision times are near 0 s, because buttons are pressed by script.
- Realtime lighting and about 1,470 renderers: Quest frame timing has not been measured on a device.
- The dashboard is desktop only. Quest data has to be copied off the device (e.g. `adb pull`).
- The Meta XR Simulator crashed the Editor once (inside `SIMULATOR.so`) while a paused Play session was being stopped. Project tests ran without the Simulator afterwards.
- `Assets/_Recovery/` holds two Unity crash-recovery scenes from that crash. They are unused.
- Pending, never applied without approval: removing unused packages (ARCore, mock HMD, XRI) and reviewing the manifest permissions USE_SCENE / USE_ANCHOR_API.

## 13. Future improvements
- Run a small consented pilot study and compare pre/post knowledge.
- Profile on a Quest 3: bake lighting and combine meshes.
- Add hand-tracking-first tutorials and voice-free accessibility options (text size, seated mode).
- Add more scenarios (tailgating at the speed gates, phone pretexting, an unattended printout follow-up) and more adversary personas.
- An in-headset read-only summary for facilitators; dashboard import from Quest over USB.
- An optional validated LLM rephrasing provider, kept behind the existing safety validator.

## 14. Viva / presentation guide
*Expanded version: `PRESENTATION_VIVA_GUIDE.md`. Also see `USER_MANUAL.md`, `FINAL_QA_REPORT.md` and `SUBMISSION_CHECKLIST.md`.*
**One-minute pitch.** "TRUST//BREACH is a VR office where security mistakes happen the way they do at work: a look-alike email, a fake password kiosk, a forged visitor badge, a bait USB. Then a friendly IT impostor, Alex Morgan, uses authority, urgency and trust-building to pressure you. Everything you actually do is logged as evidence. The debrief explains which techniques were used on you, which decisions were risky or protective, whether you recovered, and what to do at work. With consent, an anonymous record feeds a research dashboard."

**Questions you may be asked:**
- *Why VR instead of slides?* Behaviour under social pressure is different from answering a quiz. VR lets you practise the decision itself, safely.
- *How is the outcome decided?* By evidence only: completed actions, not words. A promise is not a compromise. A later report or independent verification covers earlier risky actions, which gives RECOVERED. The same rules are used in the engine and the analytics, checked by a parity test over 300 random play-throughs.
- *Is the AI generating Alex's lines?* No. The dialogue is scripted and deterministic, so it is testable and safe. An optional LLM layer exists but is disabled, and any generated line must pass a validator.
- *How do you protect participants?* Consent is opt-in, before the first decision. Data is anonymous, with no names, passwords, voice or location. Withdrawal deletes the data. REAL, DEMO and TEST data are kept in separate folders, and the dashboard never mixes them.
- *Is the score a validated measure?* No. It is a transparent descriptive index with published weights. We do not claim validity without a study.
- *How did you test it?* Builders regenerate scenes deterministically, and validators check 418 properties. There are 53 unit tests, and Play Mode tests press the real XR buttons through the Interaction SDK. Simulator and headset checks are manual and listed separately.
- *What would you do next?* A pilot study, Quest performance profiling, and more scenarios.

**Suggested live demo (about 8 minutes):** follow section 5 of `PHASE5_FINAL_INTEGRATION.md`. Use the DEMO scene, so practice runs go to TEST, never REAL.

**Submission checklist**
- [ ] Console shows 0 errors after opening the project
- [ ] Validate Phase 5: 418 / 418
- [ ] Test Runner EditMode: 53 / 53
- [ ] Manual Simulator pass of the participant journey (PHASE5 doc, section 8)
- [ ] (If available) one headset run on Quest 3
- [ ] Dashboard opens; REAL shows only consented sessions; DEMO labelled synthetic
- [ ] Clear TEST data before the presentation if a clean TEST view is wanted (**Tools ▸ TRUST∕∕BREACH ▸ Research ▸ Clear TEST Data**)
- [ ] Documentation: this guide + phase docs + `RESEARCH_DATA_SCHEMA.md`
