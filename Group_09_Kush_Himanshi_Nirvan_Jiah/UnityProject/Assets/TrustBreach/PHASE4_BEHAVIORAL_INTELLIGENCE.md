# TRUST//BREACH — Phase 4: Behavioral Intelligence & Research Analytics

Phase 4 adds an anonymous, consent-gated research pipeline, deterministic scoring, exports, and a desktop dashboard. The Phase 1–3.1 scenes, office, Alex Morgan, XR rig and scenarios are unchanged, apart from the two additions listed below.

## What was added
| Piece | Location |
|---|---|
| Consent state shared by all recorders | `Scripts/Core/ResearchConsent.cs` (also `SessionDebriefHook`) |
| Schema, events, outcome classifier, scoring | `Analytics/Runtime/ResearchModel.cs`, `BehavioralScoring.cs` |
| Durable storage, export, recorder, collector | `Analytics/Runtime/ResearchStore.cs`, `ResearchExporter.cs`, `ResearchSessionRecorder.cs`, `ResearchEventCollector.cs` |
| In-VR consent panel | `Analytics/Runtime/ResearchConsentPanel.cs` |
| Synthetic demo data | `Analytics/Runtime/DemoDataGenerator.cs` |
| Dashboard | `Analytics/Dashboard/*`, scene `Analytics/Scenes/TrustBreach_ResearchDashboard.unity` |
| Builder, validator, menus | `Analytics/Editor/Phase4AnalyticsBuilder.cs`, `Phase4Validator.cs` |
| Tests | `Analytics/Tests/ResearchAnalyticsTests.cs` (20 EditMode tests) |
| Data schema | `Analytics/Docs/RESEARCH_DATA_SCHEMA.md` |

**Scene changes** (both Phase 3 scenes, added by the builder; GUIDs unchanged):
- `Managers/ResearchAnalytics`: the collector. Its dataset is `real` in `TrustBreach_Phase3_Adversary` and `test` in `_DEMO`.
- `UI/ResearchConsentPanel`: a standing panel 1.8 m from the entrance, to the participant's left and facing them. It has no blocking colliders.

**Changes to existing code (small):**
- `TelemetryRecorder` writes only after consent and deletes its files on withdrawal.
- `EncounterDirector` asks for consent again on restart and appends the "Your session data" debrief page.
- `ResponseOption.PromisedChannel` records which scenario a promise refers to.
- `Phase3AdversaryBuilder.AfterBuild` lets a Phase 3 rebuild re-apply Phase 4.
- `Editor/AssemblyInfo.cs` exposes the build kit to the Phase 4 editor code.

## Instructor steps
1. **Build / rebuild** with **Tools ▸ TRUST∕∕BREACH ▸ Research ▸ Build Phase 4 Analytics**. The build is idempotent and backs up the scenes to `Backups/scenes/` first. It runs automatically after a Phase 3 rebuild.
2. **Validate** with **… ▸ Research ▸ Validate Phase 4 Analytics**. This runs the Phase 4 checks plus the Phase 2, 2.5 and 3 validators on both scenes.
3. **Run a participant session** in `TrustBreach_Phase3_Adversary` (dataset REAL). The participant reads the panel by the entrance and chooses **I AGREE** or **NO THANKS** before their first decision.
   - Declining, or ignoring the panel, records nothing. Gameplay is identical either way.
   - **WITHDRAW & DELETE MY DATA** (press twice) deletes the session.
4. **Practise or demonstrate** in `TrustBreach_Phase3_Adversary_DEMO` (dataset TEST / PRACTICE). These runs are never mixed with real data.
5. **Open the dashboard** with **… ▸ Research ▸ Open Research Dashboard (Play)**. It opens the dashboard scene and enters Play Mode. XR is stopped, and the dashboard is used with mouse and keyboard in the Game view.
   - **REAL / DEMO DATA / TEST** switches the dataset.
   - The filter arrows set session, scenario and outcome.
   - Hover any number or bar to see its definition, numerator and denominator in the footer.
   - Click a table row to show that session's timeline.
   - **EXPORT** writes all sessions of the dataset. **OPEN FOLDER** opens the export or data folder.
   - **DELETE SESSION** asks for confirmation. **GENERATE / CLEAR DEMO** and **CLEAR TEST** affect only those datasets.
6. **Exports and data folders** are also available from the menu (**Export REAL / DEMO / TEST Dataset**, **Open Research Data Folder**). Data lives in `<persistentDataPath>/TrustBreach/research/`; on this Mac that is `~/Library/Application Support/DefaultCompany/TrustBreach_Group09/TrustBreach/research/`.

## Limitations (honest)
- **Not validated against human participants.** All automated sessions are synthetic, so no research finding or real-world validity is claimed. DEMO DATA is generated and labelled as such.
- **The index is not a validated measure.** It is a transparent weighted summary of behaviour in this simulation.
- **Dashboard is desktop only** (Editor or standalone desktop). It is not shown in the headset and is not in the Quest build list. On Quest, copy `files/TrustBreach/research/` off the device (for example `adb pull`) and open it on the desktop by placing it under the same folder.
- **Consent window.** Consent can only be given before the first decision of a session, so partial sessions are never recorded. A participant who acts first is not recorded for that run.
- **Timing measures.** Latencies in the automated tests are near zero, because the buttons were pressed by script. Real decision times only come from human runs.
- **Legacy telemetry not imported.** The 61 pre-Phase-4 telemetry files (recorded without consent, mixing automated and manual runs) are not imported into any dataset.
- **Simulator and headset.** Play Mode tests drive the real Interaction SDK interactors in the Editor. No physical-headset or controller test of Phase 4 has been run.
