# TRUST//BREACH — Submission Checklist

Tick each item and keep the evidence: Console screenshots, validator output and photos of the headset run.

## A. Project files
- [ ] Unity project folder `TrustBreach_Group09` (exclude `Library/`, `Temp/`, `Logs/`, `obj/`, `Backups/` if size matters; they are regenerated or local-only)
- [ ] Scenes:
  - `Assets/TrustBreach/Scenes/TrustBreach_Phase3_Adversary.unity` (participant, **build index 0**)
  - `Assets/TrustBreach/Scenes/TrustBreach_Phase3_Adversary_DEMO.unity` (demo / practice)
  - `Assets/TrustBreach/Scenes/TrustBreach_CorporateOffice.unity` (Phase 2 office)
  - `Assets/TrustBreach/Scenes/TrustBreach_XR_Foundation.unity` (Phase 1 test room)
  - `Assets/TrustBreach/Analytics/Scenes/TrustBreach_ResearchDashboard.unity` (desktop dashboard, not in the Quest build)
- [ ] Code: `Assets/TrustBreach/Scripts`, `Analytics`, `Experience`, `Editor`, `Tests`
- [ ] Content: `Assets/TrustBreach/Dialogue`, `Audio/Dialogue/AlexMorgan` (37 voice clips), `Prefabs/NPC_AlexMorgan.prefab`, `Avatars`, `Materials`

## B. Documentation (`Assets/TrustBreach/`)
- [ ] `TRUSTBREACH_PROJECT_GUIDE.md`: concept, architecture, scenarios, system flow
- [ ] `USER_MANUAL.md`: controls, navigation, consent, scenarios, debrief, restart, exit
- [ ] `PRESENTATION_VIVA_GUIDE.md`: pitch, system explanations, likely questions
- [ ] `FINAL_QA_REPORT.md`: verified results, warnings, untested items, risks
- [ ] `SUBMISSION_CHECKLIST.md` (this file)
- [ ] Phase notes: `PHASE1_XR_FOUNDATION.md` … `PHASE5_FINAL_INTEGRATION.md`; data schema `Analytics/Docs/RESEARCH_DATA_SCHEMA.md`

## C. Screenshots (`Documentation/Screenshots/`, captured from the automated runs on 23 Oct 2026)
- [x] `01_entrance_guide_and_consent.png`: entrance with the guide (right) and research panel (left)
- [x] `02`–`04`: guide Welcome, Consent and XR controls steps
- [x] `05_progress_strip.png`: STEP 4 OF 6 strip while exploring
- [x] `06_debrief_page1`–`5.png`: all debrief pages
- [x] `07`–`09`: dashboard (DEMO overview, session timeline, REAL empty state)
- [x] `10_research_consent_panel.png`
- [ ] **Add your own:** a Meta XR Simulator window during the run, and a headset photo or capture if a Quest run is done

## D. Editor verification (about 5 minutes)
- [ ] Open the project: the Console shows **0 errors** (package warnings are expected and listed in `FINAL_QA_REPORT.md` section 2)
- [ ] **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Validate Phase 5 (includes Phases 2-4)** → **418 passed, 0 failed**
- [ ] **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All** → **53 / 53**
- [ ] Manual Meta XR Simulator pass: checklist in `PHASE5_FINAL_INTEGRATION.md` section 8 (the team reported this passed; re-check after any change)

## E. Quest build (not yet done; required for a headset submission)
1. [ ] **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Apply Release Build Scene List (participant scene first)**. Confirm in **File ▸ Build Profiles** that the Scene List shows only `TrustBreach_Phase3_Adversary` enabled, at index 0.
2. [ ] **File ▸ Build Profiles ▸ Android ▸ Switch Platform**. This re-imports all assets and can take a long time; don't run tests at the same time.
3. [ ] Check **Edit ▸ Project Settings ▸ Meta XR** (Project Setup Tool) for Android: 0 Required issues. The Recommended Platform-SDK items can be ignored (no Platform SDK APIs are used).
4. [ ] Check **XR Plug-in Management ▸ Android**: OpenXR enabled, with the Meta Quest support / Meta XR feature group and the Oculus Touch Controller profile.
5. [ ] Connect the Quest (developer mode, USB debugging), then **Build And Run**. If the boot scene is wrong, the build guard stops the build with an explanatory message.
6. [ ] On the headset, check:
   - [ ] app opens at the entrance with the guide
   - [ ] trigger presses buttons
   - [ ] grip grabs the badge and USB
   - [ ] Alex's voice is audible
   - [ ] debrief readable
   - [ ] RESTART returns to Welcome
   - [ ] EXIT closes the app
7. [ ] Optional: copy `/sdcard/Android/data/com.nmims.trustbreach.group09/files/TrustBreach/research/` to the desktop data folder and open the dashboard.
8. [ ] Switch back to the macOS platform for Editor work if desired.

## F. Data hygiene before handing in
- [ ] REAL dataset contains only genuine consented sessions (currently 0); never fill it with test runs
- [ ] Optionally **Tools ▸ TRUST∕∕BREACH ▸ Research ▸ Clear TEST Data** (removes automated validation sessions; REAL is not affected)
- [ ] DEMO DATA is synthetic and labelled as such in the dashboard and exports

## G. Known items to mention, not hide
- No headset test or Android build in the evidence yet (until section E is done)
- Phase 2.5 exhaustive branch set B blocked by an Editor stall
- Index not validated; no human study; Quest performance not profiled
- `Assets/_Recovery/` contains two unused Unity crash-recovery scenes from a Simulator crash
