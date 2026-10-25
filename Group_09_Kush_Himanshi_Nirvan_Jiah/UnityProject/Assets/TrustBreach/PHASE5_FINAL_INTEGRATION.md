# TRUST//BREACH — Phase 5: Final Integration, Polish & Release Validation

Phase 5 connects the existing phases into one guided participant experience, extends the debrief and validates the release. It is an **additive layer**. The office, the XR rig, the four scenarios, Alex Morgan and the research pipeline are unchanged, apart from the small edits listed below.

## 1. Audit (before any change)
| Area | Found | Action |
|---|---|---|
| Scenes | XR_Foundation (boot), CorporateOffice, Phase3_Adversary (participant, REAL), Phase3_Adversary_DEMO (TEST), ResearchDashboard (desktop, not in the build) | Kept. Only the two Phase 3 scenes gain 2 objects under `UI/` |
| XR rig | One `OVRCameraRig` + ISDK comprehensive rig, one camera, one AudioListener, no uGUI EventSystem in XR scenes, 0 missing scripts | Kept. These are now checked by the Phase 5 validator |
| Controllers | `EncounterDirector` (Alex), 4 `ScenarioController`s, `ResearchEventCollector`, `ResearchConsentPanel`, `TelemetryRecorder`, `DemoModePanel` | Reused. The guide only reads their state |
| Flow gaps | No welcome screen, no controls tutorial, no progress indicator; participant scene could not restart (demo only) | Added guide panel + progress strip; participant restart after the debrief |
| Debrief | 3 pages (outcome & replies, actions & recovery, techniques & tips) | Added page 4: why independent verification matters + workplace steps |
| Consent / data | Consent before first decision; REAL / DEMO / TEST datasets separated by folder | Unchanged. Verified again in Play Mode (REAL stays at 0 sessions) |
| Dashboard | Desktop only, filters, timelines, exports, delete confirmation | Unchanged in Phase 5 (see limitations) |
| Compilation | 0 errors. 4 warnings, all external (Meta Project Setup Tool "2 recommended fixes", OpenXR settings timestamp, Unity AI account timeout) | No project-caused warnings |

## 2. What Phase 5 adds
**Guide panel (`UI/ExperienceGuidePanel`)**
- Position: 0.8 × 0.66 m panel at (0.69, 1.45, 0.55), about 24° to the right of the spawn and facing it.
- The research panel is about 45° to the left, so the two never overlap.
- It sits left of the orientation totem and outside the validated walking route.

Six steps, with a progress bar:

| Step | Shown | Advances when |
|---|---|---|
| 1 Welcome | What TRUST//BREACH is (DEMO scene: "kept in TEST, never REAL") | START |
| 2 Research consent | Points to the research panel on the left; live consent status | I AGREE / NO THANKS on the research panel, or CONTINUE (NOT RECORDING) |
| 3 XR controls | Trigger to select (Simulator: T), hold / release GRIP to grab, sticks to move / turn | GOT IT - LET'S GO |
| 4 Explore the office | Checklist of the four scenarios (ticks from the real scenario state); where Alex is | Talking to Alex |
| 5 Meet Alex Morgan | Reply on Alex's panel; you can leave and come back | Debrief shown |
| 6 Your debrief | Outcome; read the pages on Alex's panel; **RESTART EXPERIENCE** or **EXIT** (press twice) | — |

The guide never traps anyone. Acting in the office, talking to Alex or reaching the debrief moves it forward on its own. It records nothing, so the research data format and contents are unchanged.

**Progress strip (`UI/ExperienceProgressStrip`)**
- Shows "STEP n OF 6 - …" about 1.25 m ahead and 20° below eye level.
- It is lazy-follow, not head-locked: it only glides back after you turn more than 32° away.
- It has no colliders.
- It hides near the guide panel, the research panel and Alex's panel, and while something solid (kiosk, desk, wall) is in front of you, so it never covers what you are using.

**Restart and exit**
- **Participant scene:** RESTART EXPERIENCE now appears on the debrief and the guide once the debrief has been shown. Before that, restart is refused (`EncounterDirector.CanRestart`).
- **What a restart does:** resets Alex and the four scenarios, starts a new anonymous session, asks for consent again and returns the participant to the entrance.
- **EXIT:** needs two presses within 5 s. It quits the app on Quest, or stops Play Mode in the Editor.

**Debrief page 4, "At work: verify and report"**
- Explains why checking through a channel *you* choose beats any proof the visitor offers.
- States how many times the participant actually verified or reported, or plainly that they did not.
- Lists five workplace steps.
- It is built only from recorded choices and actions.

**Launch menu: Tools ▸ TRUST∕∕BREACH ▸ Experience ▸**
- Play Participant Experience (REAL research data)
- Play Demo Experience (TEST data, never REAL)
- Open Research Dashboard (desktop)
- Build Phase 5 Participant Flow
- Validate Phase 5 (includes Phases 2-4)
- Apply Release Build Scene List (participant scene first), added in final QA; see `FINAL_QA_REPORT.md`

## 3. Files
**New**
- `Experience/Runtime/` (asmdef `TrustBreach.Experience`): `ExperienceFlow.cs` (pure stage logic), `ExperienceGuidePanel.cs`, `ExperienceProgressStrip.cs`
- `Experience/Editor/` (asmdef `TrustBreach.Experience.Editor`): `Phase5ExperienceBuilder.cs` (idempotent; also re-applied after a Phase 3 rebuild) and `Phase5Validator.cs`
- `Experience/Tests/ExperienceFlowTests.cs` (6 EditMode tests)
- `PHASE5_FINAL_INTEGRATION.md` (this file) and `TRUSTBREACH_PROJECT_GUIDE.md` (project overview for the viva)

**Modified (small; backups in `Backups/pre_phase5_20261023_115600/`)**
- `Scripts/Dialogue/EncounterDirector.cs`: `CanRestart` (demo always; participant after the debrief), the debrief restart button follows it, and new text after CLOSE.
- `Scripts/Dialogue/AdaptiveDialogueEngine.cs`: debrief page 4.
- `Tests/EditMode/AdaptiveDialogueTests.cs`: expects 4 pages, plus 1 new test that page 4 never claims verification that did not happen.
- `Analytics/Dashboard/ResearchDashboardController.cs`: header layout (Phase 4 fix, now checked on screen) and title font 24 → 21 px so it is not cut off.
- `Editor/AssemblyInfo.cs`: one `InternalsVisibleTo` line so the Phase 5 builder reuses the office build kit.
- Scenes `TrustBreach_Phase3_Adversary.unity` and `_DEMO.unity`: two objects added under `UI/`. GUIDs are unchanged.

**Byte-identical to before Phase 5 (hash-checked):** `TrustBreach_XR_Foundation.unity`, `TrustBreach_CorporateOffice.unity` and `TrustBreach_ResearchDashboard.unity`.

## 4. Performance and reliability checks (evidence-based)
**Participant scene inventory:** 3,215 objects, 1,471 renderers, 12 lights (1 with realtime shadows), 0 particle systems, 0 reflection probes, 1 camera, 1 AudioListener, 0 missing scripts.

**Phase 5 per-frame cost:**
- **Guide panel:** compares an integer state key every frame and rewrites text only when the state changes. There is no per-frame string or LINQ allocation; the one LINQ count found in the audit was replaced with a plain loop.
- **Progress strip:** one `Physics.RaycastNonAlloc` (into a reused buffer) and a few vector operations per frame; visibility changes are debounced by 0.25 s. The text only changes when the step changes.
- **Rendering:** two extra small unlit panels; no new materials beyond the shared screen materials.

**Existing `Update()` loops:** `EncounterDirector`, `NPCPresence`, `NPCVoice`, `MaliciousUSBScenario`, `ResearchConsentPanel` and `DemoModePanel`. These are small state checks, so I didn't change them. A previously documented risk remains: realtime lighting and about 1,200 primitive renderers should be profiled on a Quest (bake lighting, combine meshes) before any performance claims.

**Not measured:** no on-device frame timing has been taken (no headset available to this work).

## 5. Recommended demonstration path (about 8 minutes)
Use the DEMO scene. Its data goes to TEST and can never reach REAL.
1. **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Play Demo Experience** (activate the Meta XR Simulator first, or wear a Link headset).
2. **Welcome:** press START. Show the progress bar.
3. **Consent:** turn left and press I AGREE. The guide moves on by itself, and the status shows "Recording ON".
4. **Controls:** read the controls, press GOT IT. Then turn right and walk. The step strip appears low in view.
5. **Phishing:** at the hero desk in the workstation pod, show the look-alike sender (aperture-systerns.com), then press REPORT.
6. **Kiosk:** at the IT desk, point at "Not secure", then press the sign-in button and continue (risky) to show a compromise.
7. **Badge and USB:** pick up the visitor badge with GRIP and verify it; pick up the "Q4 BONUSES" USB and hand it in at the IT drop-off.
8. **Alex Morgan:** by the IT desk, press TALK. Pick a risky reply, then use REPORT on the footer to show recovery.
9. **Debrief:** CONTINUE, then page through all pages, especially page 4 ("At work") and "Your session data". Back at the guide, the step shows 6 OF 6.
10. **Restart:** press RESTART EXPERIENCE. You return to the entrance, consent is asked again and the guide is back at Welcome.
11. **Dashboard:** stop Play Mode, then **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Open Research Dashboard**. Choose TEST / PRACTICE to see the run just recorded, and DEMO DATA for the labelled synthetic set. REAL shows only consenting participant sessions (currently none).

The DEMO panel on the east lobby wall can also jump to a persuasion branch (Authority / Urgency / Trust) if time is short.

## 6. Tests (focused, with explicit timeouts; 23 Oct 2026)
All tests are automated in the Unity Editor. Play Mode tests press the real XR buttons through the right controller's Interaction SDK `RayInteractor`.

| Test | Result |
|---|---|
| Compilation | 0 errors; 0 warnings from project code |
| Static validation (Phase 5 + Phases 4 / 3 / 2.5 / 2 validators) | 418 / 418 |
| EditMode tests (all assemblies) | 53 / 53 |
| Phase 5 Play Mode, DEMO scene | 18 / 18 |
| Phase 5 Play Mode, participant scene | 7 / 7, and EXIT (two presses) stopped Play Mode |
| Phase 4 consent / research regression | 15 / 15 |
| Phase 3.1 scenario + dialogue regression | 9 / 9 |
| Dashboard regression | 11 / 11 |

**Defects found by these tests and fixed:**
- Agreeing to consent reset the guide to Welcome.
- The guide overlapped the orientation totem.
- The progress strip could sink into the kiosk; it now hides when an object is in front.
- The consent step label wrapped on the strip.
- The dashboard title was cut off (now 21 px, fits).
- A per-frame LINQ allocation in the guide.

## 7. Known limitations (honest)
- **Not manually tested:** keyboard/mouse input through the Meta XR Simulator and physical Quest controllers have **not** been exercised. Use the checklist in section 8.
- **Simulator crash:** the Simulator crashed the Editor once, inside `SIMULATOR.so`, while a paused Play session was being stopped. The final Play Mode tests ran without the Simulator. Unity's crash recovery left `Assets/_Recovery/0.unity` and `0 (1).unity`; they are unused and untouched.
- **Research claims:** the descriptive index is not validated, DEMO data is synthetic, and automated latencies are about 0 s. See `PHASE4_BEHAVIORAL_INTELLIGENCE.md`.
- **Phase 2.5 branch set B:** timed out once during Phase 4 (an Editor stall, not a failure) and was not rerun, as instructed.
- **Quest performance:** no on-device measurement (section 4).

## 8. Manual test checklist: Meta XR Simulator
Default Simulator input is keyboard and mouse:
- W A S D to move;
- hold the right mouse button and move the mouse to look (arrow keys rotate);
- **T** = trigger / select (shown on this project's in-scene hints);
- check grab (GRIP) in the Simulator's **Input Bindings** panel.

1. Open the project. The Console should show 0 errors. Use **Window ▸ Meta ▸ Meta XR Simulator ▸ Activate**.
2. **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Play Demo Experience (TEST data, never REAL)**.
3. [ ] The guide is ahead-right: "STEP 1 OF 6 - WELCOME". The research panel is to the left; the two don't overlap. No step strip is visible at the entrance.
4. [ ] Aim at START: the button lights up with a white frame. Press T: the guide shows step 2, and its button reads "CONTINUE (NOT RECORDING)".
5. [ ] Turn left and press I AGREE: the status shows "Recording ON". The guide moves to step 3 by itself.
6. [ ] Read the controls text: nothing is clipped. Press GOT IT - LET'S GO: step 4, with the scenario checklist and no button.
7. [ ] Walk away: the strip "STEP 4 OF 6 - EXPLORE THE OFFICE" appears low ahead. Turn a little: it stays put. Turn more than about 30°: it glides back. Walk up to the kiosk: the strip hides.
8. [ ] Complete the kiosk (sign in + continue). Back at the guide, "Password reset kiosk" shows [done].
9. [ ] Pick up the visitor badge and the USB with GRIP; they move with the controller and stay where released. Verify the badge and hand the USB in at the IT drop-off. Both are ticked.
10. [ ] Talk to Alex (TALK): the guide shows step 5 and the strip is hidden beside Alex's panel. Choose a few replies, then REPORT.
11. [ ] CONTINUE: the debrief shows OUTCOME: … Use NEXT through all 5 pages: no text cut off on any page, especially page 4, "At work: verify and report". Then BACK.
12. [ ] The guide shows step 6 with RESTART EXPERIENCE and EXIT. Press EXIT once: it reads "PRESS AGAIN TO EXIT", and returns to "EXIT" after 5 s.
13. [ ] Press RESTART EXPERIENCE on the debrief: you are back at the entrance, the guide reads WELCOME, the research panel asks again and the scenarios are reset.
14. [ ] Stop. **Play Participant Experience**, press NO THANKS, and finish a conversation. RESTART appears only after the debrief. Press EXIT twice: Play Mode stops.
15. [ ] **Open Research Dashboard (desktop)**: the title is fully visible. REAL shows "No participant sessions recorded yet" (unless you agreed in the participant scene). TEST shows the DEMO-scene run. DEMO DATA is labelled synthetic.
