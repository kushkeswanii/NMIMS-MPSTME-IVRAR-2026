# TRUST//BREACH — Phase 1 XR Foundation

Scene: `Assets/TrustBreach/Scenes/TrustBreach_XR_Foundation.unity` (Build Settings index 0 during development). Since the final release (Phase 5 QA), the headset build boots into the participant scene instead; this scene stays in the build list, disabled. See `FINAL_QA_REPORT.md`.
Do not hand-edit it: regenerate it with **TrustBreach ▸ Phase 1 ▸ Build XR Foundation Scene**. The builder is idempotent and rebuilds the scene from an empty scene on every run.

## Menu / automation
| Action | Menu | Code entry point |
|---|---|---|
| Apply Quest + Editor settings | TrustBreach ▸ Phase 1 ▸ Apply XR Project Settings | `TrustBreach.Editor.XRProjectConfigurator.Apply()` |
| Build scene (+ validate) | TrustBreach ▸ Phase 1 ▸ Build XR Foundation Scene | `TrustBreach.Editor.XRFoundationSceneBuilder.Build()` |
| Validate scene | TrustBreach ▸ Phase 1 ▸ Validate XR Foundation Scene | `TrustBreach.Editor.XRFoundationValidator.Validate(scene)` |
| Batch mode (editor closed) | — | `Unity -batchmode -projectPath . -executeMethod TrustBreach.Editor.XRFoundationSceneBuilder.BuildFromCommandLine` |
| Tests | Test Runner ▸ EditMode ▸ TrustBreach.Tests | `XRFoundationSceneTests` |

If an open scene has unsaved changes, the builder first saves a **copy** of it to `Scenes/_UnsavedSceneBackups/`. It never overwrites the original scene file.

## Architecture
- **XR rig:** Meta `OVRCameraRig` (with `OVRManager`, FloorLevel origin) plus Interaction SDK `OVRComprehensiveInteractionRig`. Both are created through Meta's public `OVRQuickActionsAPI.AddOVRInteractionRig`, and the rig stays a package prefab instance. There is no XR Origin / XRI rig.
- **Interactables:** created through `QuickActionsAPI.AddGrabInteraction` and `AddRayGrabInteraction`. Meta's default is a kinematic Rigidbody with no gravity, so released objects stay where they are released. For physics drops, set `useGravity = true` and `isKinematic = false`.
- **Assemblies:** `TrustBreach.Core` has no XR dependency (event bus). `TrustBreach.Interactions` depends on Core and `Oculus.Interaction`. `TrustBreach.Editor` holds the builder, configurator and validator. `TrustBreach.Tests.EditMode` holds the tests.
- **Event flow:** ISDK `IPointable` → `PointableEventRelay` → `TrustBreachEventBus.InteractionRaised` (`InteractionEvent`: target id, phase, interactor id, pointer pose, timestamp). Telemetry, Dialogue and AI subscribe to the bus and never reference the XR SDK.
- The professor's scaffold scripts (`PhishingGazeTelemetryLogger`, `SocialEngineeringDialogueTreeManager`) were not imported or modified. Their class names are kept free. The gaze origin for the logger is `OVRCameraRig/TrackingSpace/CenterEyeAnchor`.

## Recorded deviations from the Master Project Bible / toolchain_spec.md
1. **Engine:** Unity 6.5 (6000.5.3f1) instead of Unity 2022.3 LTS. Meta XR SDK v207 requires Unity 6000.0+ for core/platform/MRUK, so going back to 2022.3 would also mean downgrading the SDK.
2. **Interaction toolkit:** Meta Interaction SDK (`com.meta.xr.sdk.interaction(.ovr)` 207.0.1) instead of XR Interaction Toolkit. XRI 3.5.1 is installed but unused in the scene.
3. **XR plug-in:** OpenXR 1.17.1 with the Meta XR feature (spec: OpenXR >= 1.8.0, so compliant).
