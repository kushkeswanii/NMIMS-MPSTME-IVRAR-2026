# TRUST//BREACH — Phase 2 Corporate Office ("Aperture Systems")

Scene: `Assets/TrustBreach/Scenes/TrustBreach_CorporateOffice.unity`. It is generated, so don't hand-edit it.
- Rebuild: **Tools ▸ TRUST∕∕BREACH ▸ Build Corporate Office Scene** (also under **TrustBreach ▸ Phase 2**).
- Validate: **Tools ▸ TRUST∕∕BREACH ▸ Validate Corporate Office Scene**.
- Batch mode: `-executeMethod TrustBreach.Editor.TrustBreachCorporateOfficeBuilder.BuildFromCommandLine`.

Every rebuild starts from an empty scene, so there are no duplicates. Materials are reused from `Materials/Office/`. The Phase 1 foundation scene is not touched and stays the boot scene (Build Settings index 0). Since the final release (Phase 5 QA), the headset build boots into the participant scene instead; this scene stays in the build list, disabled. See `FINAL_QA_REPORT.md`.

## Layout (metres, spawn at (0, 0, -1) facing +Z)
| Zone | Area | Contents |
|---|---|---|
| Reception | x[-9,9] z[-2,4] | Glass entrance, brand wall, reception desk + visitor badge tray, waiting lounge, visitor policy poster, speed gates ("staff only") |
| Hallway | x[2,5] z[4,14] | Wayfinding sign, art, server-room door (badge reader), planter divider |
| Workstations | x[-9,2] z[4,9.75] | Two four-desk pods (hero phishing desk nearest the hallway), print station |
| MeetingArea | x[-9,2] z[9.75,14] | Glass meeting room, presentation screen, 7 chairs, executive lounge corner |
| ITSecurityDesk | x[5,9] z[4,8.6] | Service counter, ID verification point, lost & found, CCTV wall, network rack, password-reset kiosk |

## Scenario artifacts (`Scenario_Objects`, component `TrustBreach.Core.ScenarioArtifact`)
| Id | Zone | Interaction | Lure | Red flags |
|---|---|---|---|---|
| SpoofedVisitorBadge | Reception | grab / ray-grab | yes | "SYSTMES" misspelling, off-brand logo, no hologram, expired 2024, "escort not required" |
| MaliciousUSBDrive (+ bait note) | IT desk | grab / ray-grab | yes | "Q4 BONUSES" baiting label, found in car park, "plug it in" note |
| PhishingEmailWorkstation | Workstations | ray hover/select (screen + `PhishingLink_VerifyButton`) | yes | aperture-system**r**ns.com sender, 2-hour urgency, mismatched link |
| FakeSSLTerminal | IT desk | ray hover/select (screen + `FakeSSL_SignInButton`) | yes | "Not secure" HTTP, wrong-domain certificate, lookalike SSO URL, asks for current password |
| PasswordStickyNote | Workstations | ray hover/select | yes | Password on the monitor bezel |
| UnattendedConfidentialPrintout | Workstations | grab | yes | Payroll left on a shared printer |
| EmployeeBadge_Reference | IT desk | grab | no (control) | Genuine badge for comparison |

All interactions raise `TrustBreachEventBus.InteractionRaised` with the artifact id. Phase 3 telemetry should subscribe there. `ScenarioArtifactRegistry.TryGet(id, …)` gives lookup by id, and `ScenarioArtifact.GazeCollider` is the hit target for the scaffold's gaze/lure logger.

`Scenario_Objects/NPC_Anchors` holds Receptionist, VisitorImpostor, ITTechnician and MeetingHost, for avatars/dialogue in later phases. `Managers` is empty, reserved for scenario/dialogue/telemetry managers.

## Known limits
- Realtime lighting only (no baked GI or light probes). Bake before final Quest performance work.
- Primitive geometry (about 1,200 renderers, static-batched). Profile on device, and combine meshes if draw calls are high.
- No teleport surfaces. Locomotion is the rig's smooth stick movement and turning.

## Phase 2.5: Decision interactions
- Rebuild: **Tools ▸ TRUST∕∕BREACH ▸ Build Office Interactions (Phase 2.5)**. This edits the saved office scene in place.
  - Everything it creates lives under `Scenario_Objects/Scenario_Interactions`.
  - The scene file is backed up to `<project>/Backups/scenes/` first.
  - The Phase 2 office builder also re-applies it, so a full rebuild keeps the interactions.
- Validate: **Tools ▸ TRUST∕∕BREACH ▸ Validate Office Interactions**. EditMode test: `OfficeInteractionsTests`.
- Buttons (`TrustBreach.Interactions.XRButton`) are driven by ISDK RayInteractables:
  - **hover** = aimed (lighter colour + white frame)
  - **pressed** = trigger held (darker colour)
  - **activate** = trigger released on the button
  - **chosen** = persistent teal frame
  - A cancelled press never activates.
- Decisions: `TrustBreach.Core.DecisionLog.Record(...)` → `DecisionLog.DecisionRecorded` / `DecisionLog.History`.
  - Fields: event name, session time (s), object id, choice, outcome (Safe / Unsafe / Neutral).
  - No personal data is recorded. `DecisionEvent.ToCsvRow()` writes `session_time_s,event_name,object_id,choice,outcome`.

| Event | Object | Choice | Outcome |
|---|---|---|---|
| phishing_email.link_opened | PhishingEmailWorkstation | verify_account_now | Unsafe |
| phishing_email.credentials_submitted | PhishingEmailWorkstation | proceed | Unsafe |
| phishing_email.reported | PhishingEmailWorkstation | report_phishing_from_inbox / report_suspicious_email | Safe |
| password_kiosk.sign_in_selected | FakeSSLTerminal | sign_in_and_reset | Neutral |
| password_kiosk.credentials_entered | FakeSSLTerminal | continue | Unsafe |
| password_kiosk.cancelled | FakeSSLTerminal | cancel | Neutral |
| password_kiosk.reported | FakeSSLTerminal | report_kiosk_to_it | Safe |
| visitor_badge.inspected | SpoofedVisitorBadge | picked_up_badge | Neutral |
| visitor_badge.identity_verified | SpoofedVisitorBadge | verify_identity | Safe |
| usb_drive.picked_up | MaliciousUSBDrive | picked_up_usb | Neutral |
| usb_drive.connected | MaliciousUSBDrive | connect_usb (workstation dock, Desk_05) | Unsafe |
| usb_drive.submitted_to_it | MaliciousUSBDrive | submit_to_it (IT drop-off) | Safe |

Everything is simulated: no network access, no credential capture, and no access to real USB devices.
