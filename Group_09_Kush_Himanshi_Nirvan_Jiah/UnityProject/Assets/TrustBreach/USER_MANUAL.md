# TRUST//BREACH — User Manual

TRUST//BREACH is a short (about 10 minute) VR cybersecurity awareness exercise in a virtual office, Aperture Systems. You deal with everyday security situations and meet a visitor, Alex Morgan, who may not be who he says. There is no score on screen: act as you would at work. Afterwards a personal debrief explains what happened.

## 1. Starting
| Where | How |
|---|---|
| Quest headset (release build) | Launch the app. It opens in the participant experience. |
| Unity Editor + Meta XR Simulator | Activate the Simulator (**Window ▸ Meta ▸ Meta XR Simulator ▸ Activate**), then **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Play Demo Experience** (practice; data → TEST) or **Play Participant Experience** (data → REAL, only with consent). |
| Unity Editor + Quest Link | Connect the headset with Link, then use the same menu items. |

You start at the office entrance:
- **Ahead-right:** the white-on-navy **guide panel**.
- **Ahead-left:** the **research participation** panel.

## 2. Controls
| Action | Quest controller | Meta XR Simulator (keyboard/mouse) |
|---|---|---|
| Point at a button | Aim the controller; a ray appears | Aim the simulated controller |
| Select / press | **Index trigger** (press and release over the button) | **T** |
| Grab an object | Hold **GRIP** on it | Grip binding: see the Simulator's **Input Bindings** panel |
| Release an object | Let go of **GRIP** (the object stays where you release it) | Release the grip binding |
| Move | Left thumbstick | **W A S D** |
| Turn / look | Right thumbstick; head movement | Hold the **right mouse button** and move the mouse; arrow keys rotate |

**Button feedback:**
- **Hover:** lighter colour with a white frame.
- **Held:** darker colour.
- **Activates:** when you release the trigger on the button. Moving off the button cancels the press.
- **Chosen:** dialogue choices keep a teal frame.

Comfort: walk slowly, and take the headset off whenever you like. Nothing is timed against you.

## 3. The six steps
The guide panel and a small strip low in your view show **STEP n OF 6**.

1. **Welcome:** read the introduction and press **START**.
2. **Research consent (optional):** turn left to the research panel.
   - **I AGREE - RECORD ANONYMOUSLY** saves an anonymous record of this run. It records a random session code, your choices, the security actions you take, and how long you take to decide.
   - **NO THANKS - DON'T RECORD** saves nothing. The experience is exactly the same either way.
   - You can only agree **before your first action**.
   - **WITHDRAW & DELETE MY DATA** (press twice) deletes the session at any time.
   - Never saved: your name, passwords or anything typed, your voice, audio, video or location.
3. **XR controls:** a reminder of the controls. Press **GOT IT - LET'S GO**.
4. **Explore the office:** deal with anything that seems off. The guide's checklist ticks each situation once you have made a final decision.
5. **Meet Alex Morgan:** Alex from "IT Security" waits by the IT desk to your right. Press **TALK** on the panel beside him and choose your replies.
   - **REPORT** and **END CONVERSATION** are on that panel while he is talking.
   - You can walk off to act in the office; Alex reacts when you come back.
6. **Your debrief:** press **CONTINUE**, then read the pages with **NEXT** and **BACK**.

The progress strip hides when you are close to a panel or standing right at an object, so it never covers what you are using.

## 4. The situations in the office
| Situation | Where | What to look for |
|---|---|---|
| Phishing email | Hero desk in the workstation pod | Look-alike sender domain, urgent deadline, link that goes somewhere else. **Report** it. |
| Password-reset kiosk | IT security desk | "Not secure" page, wrong certificate, asks for your current password. Don't sign in; **report** it. |
| Visitor badge | Reception badge tray | Pick it up with GRIP. Misspellings, odd logo, expired. **Verify identity** before trusting it. |
| Found USB drive | Lost and found, IT desk | "Q4 BONUSES" bait label. Never plug it in; **hand it in** at the IT drop-off. |

Everything is simulated: nothing connects to a real network or account.

## 5. The debrief
1. **Outcome and your replies.** Each reply is marked SAFE, RISKY or NEUTRAL. The outcomes are:
   - **COMPROMISED:** a risky action that wasn't corrected.
   - **RECOVERED:** a risky action, then a report or check.
   - **RESISTED:** no risky action, and you verified or reported.
   - **DECLINED:** you refused until Alex gave up.
   - **INCOMPLETE:** the run ended early.
2. **Security actions you actually completed**, with times, and whether you recovered.
3. **Persuasion techniques** Alex used on you (authority, urgency, trust-building), with quotes and tips.
4. **At work: verify and report:** why checking through a channel *you* choose matters, and five steps to use at work.
5. **Your session data:** your anonymous code, or "No research data was saved".

Agreeing to something in conversation is not counted as a compromise. Only actions you actually completed are.

## 6. Restart and exit
- **RESTART EXPERIENCE** is available after the debrief, on the debrief page and on the guide panel. It resets Alex and every situation, starts a new anonymous session, asks for consent again, and returns you to the entrance at WELCOME. Use it between participants.
- **EXIT** is on the guide panel at step 6. Press it twice within 5 seconds: the first press shows "PRESS AGAIN TO EXIT". On Quest it closes the app; in the Unity Editor it stops Play Mode.
- **DEMO scene only:** the amber DEMO panel on the east lobby wall can reset or jump to a persuasion branch.

## 7. For facilitators: research dashboard (desktop)
1. Stop Play Mode, then use **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Open Research Dashboard (desktop)**.
2. Choose **REAL** (consenting participants only), **DEMO DATA** (synthetic, clearly labelled) or **TEST / PRACTICE** (DEMO-scene and automated runs).
3. Use the filter arrows for session, scenario and outcome. Hover any number for its definition; click a table row for that session's timeline.
4. **EXPORT** writes JSON and CSV files into a new timestamped folder. **OPEN FOLDER** shows it.
5. **DELETE SESSION** asks for confirmation.

Data folder on the development Mac: `~/Library/Application Support/DefaultCompany/TrustBreach_Group09/TrustBreach/research/`.
On Quest: `/sdcard/Android/data/com.nmims.trustbreach.group09/files/TrustBreach/research/`. Copy it to the same desktop folder to view it.

## 8. Troubleshooting
| Problem | Fix |
|---|---|
| No controller rays in the Editor | Activate the Meta XR Simulator (redo after every Editor restart), or connect Link |
| Text appears pink | TMP Essential Resources missing. They are included in this project; reimport if needed |
| The I AGREE button is gone | You already made a decision this run (consent is only offered before your first action). RESTART to ask again |
| Alex doesn't react to something you did | Walk back to him; he reacts when you are within about 3 m |
| Headset build opens the wrong scene | Run **Tools ▸ TRUST∕∕BREACH ▸ Experience ▸ Apply Release Build Scene List**, then rebuild |
