# TRUST//BREACH — Final QA Report

**Date:** 23 Oct 2026
**Environment:** Unity 6000.5.3f1 (Apple Silicon Mac), URP, Meta XR SDK v207 (`com.meta.xr.sdk.core`), OpenXR, Interaction SDK
**Scope:** final integration and QA of Phases 1–5. No new phase or features.

Every result below is labelled by how it was obtained:
- **AUTO:** an automated Editor test or validator run during this QA pass or earlier today.
- **USER:** a manual Meta XR Simulator test reported by the project team. I have not reproduced it.
- **NOT RUN:** not tested.

## 1. Safety
- **Verified backup before any change:** `Backups/pre_finalqa_20261023_144343/`. It holds 644 files (`Assets/TrustBreach`, `Assets/XR`, `ProjectSettings`, `Packages`), each SHA-1 verified against the live project with 0 mismatches, plus a copy of the research data folder (123 files).
- **Earlier backups:** `Backups/pre_phase5_20261023_115600/`, `Backups/pre_phase4_*`, `pre_phase3.1_*`, `pre_phase2.5_*` and `Backups/scenes/`.
- **Research records:**
  - REAL: 0 sessions, 1 export; never written by any test.
  - DEMO: 24 synthetic sessions.
  - TEST: 38 sessions from automated validation runs.
  - None were deleted or overwritten.

## 2. Compilation and Console audit (AUTO)
| Item | Result |
|---|---|
| Compilation (all assemblies) | **0 errors, 0 warnings** from project code |
| Console after Play Mode runs | **0 errors**; 34 warnings, **all from packages, engine or runtime** (listed below) |

| Warning (count) | Source | Assessment |
|---|---|---|
| `xrGetInstanceProcAddr failed … xrGetDeviceSampleRateFB`, `…SimultaneousHandsAndControllers…`, `…RoomMesh…`, `xrRequestSceneCaptureFB` (3 each) | Meta XR core `OpenXRNative` | The desktop OpenXR runtime doesn't implement these Quest-only extensions. SDK/runtime limitation |
| `XR_ERROR_ACTIONSET_NOT_ATTACHED`, `XR_ERROR_PATH_UNSUPPORTED` (xrSuggestInteractionProfileBindings) | `OVRManager` | Controller profiles the desktop runtime does not support. SDK/runtime limitation |
| `Call to DeinitializeLoader without an initialized manager` | `OpenXRRestarter` (OpenXR package), during shutdown | Package shutdown order. Not caused by `DesktopModeGuard` (it checks `isInitializationComplete` first) |
| `Ignoring depth surface load/store action as it is memoryless` | URP/Metal | Engine information |
| `Account API did not become accessible` | Unity AI Assistant package | Network/account service |
| Meta Project Setup Tool: "outstanding Recommended fixes" | `OVRProjectSetup` | See section 6. **0 required** fixes |
| `Camera.stereoTargetEye only with the built-in renderer` | Seen only during automated screenshot runs (test-harness camera) | Test tooling. Not present in normal Play Mode |

These warnings were investigated and attributed to their sources. They were **not** "resolved", because they are outside the project's control.

## 3. Integration checks
| Feature | Evidence | Result |
|---|---|---|
| Scene integrity: one `OVRCameraRig`, one camera, one AudioListener, no uGUI EventSystem in XR scenes, 0 missing scripts, all references assigned | Phase 5 validator, both scenes | AUTO pass |
| XR rig and controller input: 4 ray, 4 poke, 2 controller-grab, 4 hand-grab, 2 distance-grab interactors, locomotor, 28 controller refs, in both participant scenes | Read-only rig audit | AUTO pass (components present and wired) |
| Office environment, walking route (0.5 m capsule through five zones), scenario visibility | Phase 2 validator | AUTO pass |
| Phishing email, password kiosk, visitor badge, USB: decisions, outcomes, reports | Phase 2.5 validator; Phase 3.1 regression V1–V9 | AUTO pass |
| Alex Morgan: adaptive dialogue, branches, scenario reactions, outcomes, recovery | 53 EditMode tests incl. exhaustive branch test; V1–V9 | AUTO pass |
| Alex audio: 3D voice source, 37 clips on disk, all 37 referenced by the dialogue graph | Rig audit | AUTO pass (wiring). Audible playback: USER |
| Decision logging and telemetry after consent | Phase 4 P2c | AUTO pass |
| Consent accept / decline; decline writes nothing; withdrawal deletes that session's research and telemetry files only | Phase 4 P1, P7; Phase 5 F13, Q-run | AUTO pass |
| Behavioural analytics: 5 outcomes, engine-vs-analytics parity over 300 random play-throughs | P2–P6; EditMode | AUTO pass |
| Dashboard: REAL empty state, DEMO generate, KPIs = metrics, hover definitions, filters, timeline, CSV/JSON export, delete confirmation, TEST view | Dashboard D0–D10 | AUTO pass |
| Onboarding (6 steps), progress strip, checklist, debrief (5 pages fit, NEXT/BACK), RESTART → Welcome with consent re-asked and scenarios reset, EXIT (two presses) | Phase 5 F1–F14 (18 checks) and Q0–Q6 + EXIT | AUTO pass |
| Same features driven by keyboard/mouse in the Meta XR Simulator | Team's manual session | USER (reported pass) |
| Physical Quest 3 headset | — | **NOT RUN** |

## 4. Test totals
| Suite | Passed | Failed | Blocked / not run |
|---|---|---|---|
| Static validation (Phases 2–5, both scenes, dashboard scene, schema privacy) | 418 | 0 | — |
| EditMode unit tests | 53 | 0 | — |
| Phase 5 Play Mode, DEMO scene (rerun in this QA pass) | 18 | 0 | — |
| Phase 5 Play Mode, participant scene + EXIT | 7 + 1 | 0 | — |
| Phase 4 research Play Mode regression | 15 | 0 | — |
| Phase 3.1 scenario/dialogue regression | 9 | 0 | — |
| Dashboard regression | 11 | 0 | — |
| Phase 2.5 exhaustive branch sets | A, C | 0 | **Set B blocked** (Editor stall; not rerun, as instructed) |
| Android / Quest player build | — | — | **NOT RUN** (see section 6) |
| Headset test | — | — | **NOT RUN** |

Before this QA pass, all Play Mode suites ran today on the same runtime code. In this pass only Editor-side code changed, so the Phase 5 DEMO suite was rerun as a smoke test.

## 5. Issues found and fixed
| # | Issue | Fix | Verified |
|---|---|---|---|
| 1 | **Quest build would boot into the Phase 1 XR test room** (index 0); nothing in the app loads another scene, so the experience was unreachable on a headset. `SampleScene` was also in the build | With the team's approval: **participant scene is the only enabled scene (index 0)**; all other scenes kept in the list, disabled (GUIDs unchanged). New menu **Experience ▸ Apply Release Build Scene List**. New build guard (`ReleaseBuildConfig`) stops an Android build whose boot scene is anything else | AUTO: scene list on disk; validator + tests updated to the release rule and pass |
| 2–7 | Earlier today in Phase 5: consent reset the guide; guide overlapped the orientation totem; strip sank into the kiosk; strip label wrapped; dashboard title cut off; per-frame LINQ allocation | See `PHASE5_FINAL_INTEGRATION.md` | AUTO |

**Changed in this pass:**
- `Experience/Editor/ReleaseBuildConfig.cs` (new)
- `ProjectSettings/EditorBuildSettings.asset`
- `Tests/EditMode/XRFoundationSceneTests.cs` and `CorporateOfficeSceneTests.cs` (the boot-scene assertion only)
- `Editor/Phase3Validator.cs` (the boot-scene check only)

## 6. Build readiness (inspected, not built)
| Setting | Value | Status |
|---|---|---|
| Android module installed | Yes | OK |
| Active build target | StandaloneOSX (Editor development) | Switch to Android to build |
| Scene list | `[0] TrustBreach_Phase3_Adversary` (enabled); XR_Foundation, CorporateOffice, SampleScene, Phase3_DEMO kept, disabled; dashboard not included | OK (release) |
| Package id / version | `com.nmims.trustbreach.group09` / 0.1.0 (code 1) | OK |
| Scripting / architecture | IL2CPP / ARM64 | OK for Quest |
| Min / target API | 32 / 34 | OK for Quest |
| Graphics | Vulkan, OpenGLES3 (manual list); Linear colour space | OK |
| XR (Android) | OpenXR loader, initialise on startup; features: Meta XR, foveation, subsampled layout, Oculus Touch controller profiles | OK |
| Meta Project Setup Tool (Android) | **0 Required.** 3 Recommended: two Platform-SDK items (the tool says to ignore them when Platform SDK APIs are not used; this project uses none) and "consider Dynamic Resolution" (performance option; left for on-device profiling) | Acceptable |
| Company name | `DefaultCompany` | Left unchanged: changing it moves `persistentDataPath` and therefore where research data is stored |

**Why no build was produced:** an Android build requires switching the active platform, which re-imports every asset. That is a long, unbounded operation, and the Editor has stalled during long operations earlier today. The configuration above is ready. Build steps are in `SUBMISSION_CHECKLIST.md`. A successful Simulator run does not prove a headset build works.

## 7. Performance (inspected)
- **Participant scene:** 3,215 objects, about 1,470 renderers, 12 lights (1 with realtime shadows), no particles, no reflection probes.
- **Per-frame project code:**
  - Guide panel: integer state check; text is rewritten only on change.
  - Progress strip: one non-allocating raycast.
  - Consent panel: integer polling.
  - Encounter director, NPC presence and voice: vector maths.
  - DEMO panel: debug text throttled to 4 Hz, DEMO scene only (left as is; working Phase 3 code).
- **Event subscriptions:** every `OnEnable` subscription has a matching `OnDisable`. Four rapid restarts produced no duplicate events (P9).
- **Not measured:** frame timing on a Quest. Risks to profile: realtime lighting and draw calls.

## 8. Remaining risks and limitations
1. **No headset run and no Android build yet.** Highest priority before submission.
2. **The Meta XR Simulator crashed the Editor once** (segfault inside `SIMULATOR.so` while stopping a paused Play session). Third-party issue; it left `Assets/_Recovery/0.unity` and `0 (1).unity` (unused, untouched).
3. **Phase 2.5 branch set B** was never completed (Editor stall).
4. **Research validity:** no human study, so no real-world validity is claimed. The descriptive index is not validated, DEMO data is synthetic, and automated decision times are about 0 s.
5. **Dashboard is desktop only.** Quest data has to be copied off the device.
6. **Rebuilding Phase 1** re-inserts the XR test room at index 0. The build guard then stops Android builds until **Apply Release Build Scene List** is run.
7. **Pending, never applied without approval:** removing unused packages (ARCore, mock HMD, XRI) and reviewing manifest permissions (USE_SCENE, USE_ANCHOR_API).
