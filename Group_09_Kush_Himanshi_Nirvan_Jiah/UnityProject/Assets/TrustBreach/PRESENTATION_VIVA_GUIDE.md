# TRUST//BREACH — Presentation & Viva Guide

Written so every group member can explain any part of the system. Each fact here matches the implementation; the details are in `TRUSTBREACH_PROJECT_GUIDE.md`.

## 1. The 60-second pitch
"Most breaches begin with a person, not a hacker breaking code. TRUST//BREACH puts you in a virtual office where security mistakes happen the way they do at work: a look-alike phishing email, a fake password-reset kiosk, a forged visitor badge and a bait USB stick. Then a friendly impostor, Alex Morgan 'from IT Security', uses authority, urgency and trust-building to pressure you. Everything you *do* is recorded as evidence. The debrief explains which techniques were used on you, which decisions were risky or protective, whether you recovered, and what to do at work. With consent, an anonymous record feeds a research dashboard."

## 2. Suggested presentation structure (10–12 minutes)
1. **Problem and motivation** (1 min): human-factor attacks, and why quizzes don't train behaviour under pressure.
2. **Concept and learning objectives** (1 min): the six objectives in the project guide, section 1.
3. **Live demo** (6 min): follow `PHASE5_FINAL_INTEGRATION.md` section 5, using the DEMO scene so practice data goes to TEST.
4. **How it works** (2 min): the architecture diagram, the evidence-only outcome rules and consent.
5. **Results and honesty** (1 min): what was tested (418 checks, 53 unit tests, Play Mode suites) and what was not (no headset study, index not validated).
6. **Future work** (30 s).

## 3. Explaining each system
| System | One-sentence explanation | If asked for more |
|---|---|---|
| XR rig (Phase 1) | Meta's camera rig plus the Interaction SDK give us controller and hand rays, grabbing and smooth locomotion | Created through Meta's public Quick Actions API; no XR Interaction Toolkit; OpenXR runtime |
| Office (Phase 2) | A generated five-zone office (reception, hallway, workstations, meeting area, IT desk) with planted red flags | Built by an idempotent editor script; a validator checks the walking route, visibility and colliders |
| Scenarios (Phase 2.5) | Four state machines turn button presses and grabs into Safe / Unsafe / Neutral decisions | `ScenarioController` subclasses call `DecisionLog.Record` exactly once per real decision |
| Alex Morgan (Phase 3) | A deterministic dialogue engine changes Alex's strategy based on your replies and on what you actually did in the office | It tracks suspicion, cooperation and escalation; the voice is synthesised offline; the optional LLM is disabled and guarded by a validator |
| Outcome rules (Phase 3.1) | Only completed actions count; a later report or independent check "covers" earlier risky actions | A promise is not a compromise; a dialogue report covers every channel, a scenario report covers its own channel |
| Research analytics (Phase 4) | Opt-in, anonymous, consent-before-first-action recording with transparent metrics | Atomic writes, quarantine for bad files, three separate datasets, RFC 4180 CSV export |
| Dashboard (Phase 4) | A desktop tool for instructors: KPIs, outcome and scenario charts, timelines, export, delete | Every number shows its numerator and denominator; an empty denominator shows "—", not 0 |
| Participant journey (Phase 5) | A six-step guide (welcome → consent → controls → office → Alex → debrief) with a progress strip, restart and exit | Pure stage logic (unit-tested), and the guide records nothing; the strip is lazy-follow for comfort |

## 4. Security concepts to be ready to define
- **Social engineering:** manipulating people into breaking security procedures.
- **Phishing / look-alike domains:** `aperture-systerns.com` imitates `aperture-systems.com` (rn looks like m).
- **Credential harvesting:** a fake sign-in page or kiosk that captures passwords. The "Not secure" HTTP warning and a wrong-domain certificate are the clues.
- **Pretexting and impersonation:** Alex's invented story ("urgent authentication issue") and false authority ("IT Security").
- **Baiting:** the "Q4 BONUSES" USB stick. Malicious USB devices can run code or act as keyboards.
- **Tailgating / visitor verification:** a badge is not proof of identity; verify with reception or the host.
- **Persuasion principles:** authority, urgency (scarcity of time) and liking / trust-building (Cialdini).
- **Independent (out-of-band) verification:** confirm through a channel *you* choose (a known phone number, the directory), never one the requester provides.
- **Recovery:** reporting quickly after a mistake limits damage. That is why RECOVERED is a separate, better outcome than COMPROMISED.

## 5. Analytics in plain words
- **Risky-action rate:** completed risky actions ÷ completed actions that are classed safe or unsafe.
- **Recovery rate:** RECOVERED sessions ÷ sessions with at least one risky action.
- **Reporting rate:** sessions with any report ÷ all sessions.
- **Verification per reply:** ID requests, verify-independently replies and badge checks ÷ dialogue replies.
- **Intention vs action:** of the times someone *agreed* to do something risky, how often they then *did* it.
- **Decision time:** median and quartiles of reply latency (time from Alex's line to the reply).
- **Descriptive index (0–100):** action safety 40%, persuasion resistance 25%, verification 15%, reporting / recovery 20%. Missing parts are re-weighted. It is **not validated** and describes behaviour in this simulation only.

## 6. Likely viva questions and model answers
1. **Why VR?** Behaviour under social pressure differs from quiz answers. VR lets people practise the actual decision, with presence and time pressure, safely.
2. **How do you decide whether someone was compromised?** By evidence only: completed actions, not words. The same rules run in the dialogue engine and in the analytics, checked by a parity test over 300 random play-throughs.
3. **What if someone agrees with Alex but never does it?** That's recorded as a *promise*. The debrief points it out, but it is not a compromise. "Intention vs action" measures it.
4. **Is Alex an AI chatbot?** No. The dialogue is scripted and adaptive by rules, which makes it deterministic, testable and safe. An optional LLM layer exists, disabled, and every generated line would have to pass a validator, otherwise the script is used.
5. **How did you protect participants' privacy?**
   - Recording is opt-in, before the first action.
   - The record is anonymous: no names, passwords, typed text, voice or location.
   - Withdrawal deletes the data.
   - REAL, DEMO and TEST data are stored separately and never mixed.
6. **Can demo data leak into real results?** No. The DEMO scene writes only to TEST; synthetic data lives in DEMO with `DEMO-` ids; REAL accepts only consented participant-scene sessions. Automated tests confirm REAL stayed at 0.
7. **Is your index a valid measure of susceptibility?** No, and we don't claim it is. It's a transparent summary with published weights. Validating it would need a study.
8. **How did you test it?**
   - Builders regenerate scenes deterministically, and validators check 418 properties.
   - There are 53 unit tests, including an exhaustive dialogue-branch test.
   - Play Mode tests press the real XR buttons through the Interaction SDK.
   - The team tested manually in the Meta XR Simulator.
   - A headset run is still to do.
9. **What was the hardest bug?** Outcome accuracy (Phase 3.1): early versions counted promises as compromises and showed RECOVERED wrongly. We moved to evidence-only rules with coverage per channel. Another: the headset build would have booted into the test room; we found it in final QA and added a build guard.
10. **Performance on Quest?** Panels are unlit, there are no particles, one shadowed light and no per-frame allocations in our new code. Not yet profiled on a device: realtime lighting and about 1,470 renderers are the known risks; baking lighting and combining meshes are the plan.
11. **Comfort and accessibility?**
    - Panels sit at eye height, 1.4–2 m away.
    - The progress strip is lazy-follow, not head-locked.
    - Nothing is timed against the participant.
    - Subtitles accompany Alex's voice, which can be muted.
12. **What would you do next?** A consented pilot study with pre/post knowledge tests, Quest profiling, more scenarios (tailgating, phone pretexting) and a hand-tracking tutorial.

## 7. Honest limitations to state proactively
- No human-participant study; no real-world effectiveness claim.
- No headset test or Android build in this submission's evidence (Editor, automated and Simulator only).
- Automated decision times are near zero; DEMO data is synthetic.
- The dashboard is desktop only.

## 8. Demo safety tips
- Use **Play Demo Experience** (TEST data). Clear TEST beforehand for a clean dashboard view: **Tools ▸ TRUST∕∕BREACH ▸ Research ▸ Clear TEST Data**.
- Activate the Meta XR Simulator before pressing Play. If it misbehaves, stop Play Mode normally; avoid pausing and then stopping (the Simulator crashed the Editor once in that state).
- Have the dashboard screenshots in `Documentation/Screenshots/` as a fallback.
