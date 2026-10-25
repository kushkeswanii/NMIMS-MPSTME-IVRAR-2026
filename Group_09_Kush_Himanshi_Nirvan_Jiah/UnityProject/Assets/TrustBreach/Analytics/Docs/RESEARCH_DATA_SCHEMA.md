# TRUST//BREACH — Research data schema (v1.0)

Schema id: `trustbreach.research.session/1.0` (constant `ResearchSchema.SessionSchema`). Readers accept any `1.x`. Files with another major version are skipped and reported. They are never deleted.

## Storage layout
Root: `<persistentDataPath>/TrustBreach/research/`. On the development Mac this is `~/Library/Application Support/DefaultCompany/TrustBreach_Group09/TrustBreach/research/`. On Quest it is `/sdcard/Android/data/<package>/files/TrustBreach/research/`.

```
research/
  real/   sessions/<sessionId>.json   participant scene, consented sessions only
          quarantine/                  unreadable files moved aside (+ .reason.txt)
          exports/<yyyyMMdd_HHmmss>/   exports
  demo/   ...                          synthetic DEMO DATA only (generated, ids start with DEMO-)
  test/   ...                          DEMO-scene play-throughs and automated validation runs
```

The three datasets are never combined. The dashboard shows one at a time, and exports cover one dataset.

## Writing guarantees
- **Consent gate.** Nothing is written until the participant presses **I AGREE** on the in-VR research panel. Agreeing starts a new anonymous session, so nothing done before consent is recorded. Consent is offered only before the first decision.
- **Atomic writes.** Each write goes to `<file>.tmp`, is flushed to disk, then replaces the target. A reader never sees a partial file.
- **Durability.** The session file is rewritten after every accepted event, so a crash or power loss loses nothing that was already recorded.
- **Deduplication.**
  - Decisions are keyed by (session, DecisionLog sequence).
  - Lifecycle events are keyed by (name, time to 1 ms, detail).
  - Events from another session id are dropped as stale.
- **Ordering.** `seq` is contiguous and 1-based in arrival order, which is chronological. `elapsed` is the seconds since the previous event.
- **Scene changes.** A single collector survives scene loads, and every scene visited is listed in `scenes`. If the collector is recreated, it resumes the same file without duplicating events.
- **Withdrawal.** Pressing **WITHDRAW & DELETE** twice deletes the session file and the Phase 3 telemetry files for that session, and stops recording.
- **Malformed files.** Empty files, malformed JSON, a session id that does not match the file name, or a dataset that does not match the folder all cause the file to be moved to `quarantine/` with a reason note. The dashboard reports the count.

## Session file (`SessionRecord`)
| field | type | meaning |
|---|---|---|
| `schema` | string | `trustbreach.research.session/1.0` |
| `dataset` | string | `real`, `demo` or `test` |
| `sessionId` | string | anonymous random id (12 hex characters; `DEMO-nnn-xxxxx` for demo data) |
| `startedUtc`, `endedUtc` | string | ISO-8601 UTC, **minute precision** (used for ordering only) |
| `durationSeconds` | number | last event time (session clock) |
| `endReason` | string | `in_progress`, `session_restart`, `application_quit`, `scene_unloaded` |
| `appVersion`, `unityVersion`, `platform` | string | build information (no device identifiers) |
| `scenes` | string[] | scenes visited, in order |
| `consent.status`, `consent.grantedAt` | string, number | always `Granted` in stored files; the session time of consent |
| `events` | ResearchEvent[] | see below |
| `summary` | SessionSummary | recomputed on every save from the events (convenience copy) |

## Event (`ResearchEvent`)
| field | meaning |
|---|---|
| `seq` | 1-based order in this file |
| `sourceSeq` | DecisionLog sequence (decisions), or 0 for lifecycle events |
| `kind` | `dialogue_choice`, `security_action` or `lifecycle` |
| `name` | e.g. `password_kiosk.credentials_entered`, `alex_encounter.report`, `alex_encounter.completed` |
| `scenarioId` | scenario object or NPC id, e.g. `FakeSSLTerminal`, `NPC_AlexMorgan` |
| `interactionId` | response id (e.g. `o_rc_kiosk`) or scenario choice id (e.g. `continue`) |
| `channel` | `email`, `kiosk`, `badge`, `usb`, `verbal`, `dialogue`; for a promise, the scenario that was promised |
| `t`, `elapsed` | seconds since the session started / since the previous event |
| `classification` | `Safe`, `Unsafe` or `Neutral` (empty for lifecycle events) |
| `riskyAction` | a **completed** unsafe security action (promises are never risky actions) |
| `corrective` | a report or independent verification |
| `completion` | `completed` (security action), `promised` (agreed to comply), `stance` (other reply), `n/a` |
| `persuasion` | technique Alex used on the line being answered: `Authority`, `Urgency`, `TrustBuilding` |
| `latency` | seconds from Alex's line to the reply, or `-1` if not measurable |
| `node`, `choiceClass`, `contentSource`, `scene`, `detail` | dialogue node, reply class, `scripted`/`model`, scene name, machine-readable lifecycle detail |

**Never stored:** names, passwords or typed text, free text, voice, audio, video, location, device identifiers. The validator checks the schema field names for these.

## Outcomes (same rules as `AdaptiveDialogueEngine.ResolveOutcome`; parity test over 300 random play-throughs)
- **COMPROMISED:** at least one risky action is not followed by a corrective that covers it.
  - A dialogue corrective (reporting Alex, or verifying with IT) covers every channel.
  - A scenario report covers its own channel only.
- **RECOVERED:** every risky action is covered.
- **RESISTED:** no risky action, and a corrective or a badge verification happened.
- **DECLINED:** no risky action, no corrective, and Alex's encounter ended in the "gave up" state.
- **INCOMPLETE:** none of the above.
- **Initial outcome** (before recovery): COMPROMISED if any risky action happened; otherwise it equals the final outcome.

## Metrics (every rate is reported with its numerator and denominator; an empty denominator gives "no value", never 0)
| metric | definition |
|---|---|
| Risky-action rate | completed risky security actions ÷ completed security actions classified Safe or Unsafe. Neutral steps such as picking up the USB are excluded. |
| Recovery rate | RECOVERED sessions ÷ sessions with ≥ 1 risky action |
| Reporting rate | sessions with a report (Alex, email, kiosk, or USB handed in) ÷ sessions |
| Scenario completion | scenario instances reaching a terminal action ÷ (sessions × scenarios in scope) |
| Verification per reply | (ask-for-ID + verify-independently replies + badge verifications) ÷ dialogue replies |
| Intention vs action | promises followed by the promised risky action on the same channel ÷ promises |
| Decision time | median and 25th / 75th percentile (linear interpolation) of reply latencies |
| Technique risky-reply rate | risky replies to lines using that technique ÷ replies to that technique |
| Scenario unsafe rate | risky ÷ (Safe + Unsafe) completed actions in that scenario |

### Descriptive index (optional, 0–100, **not validated**)
The index summarises behaviour in this simulation only. It is not a validated susceptibility measure.

Components and weights:
- Action safety: 100 × (1 − unsafe-action rate), weight 0.40.
- Persuasion resistance: 100 × (1 − risky replies ÷ replies), weight 0.25.
- Verification: 100 × min(1, verification events ÷ 2), weight 0.15.
- Reporting / recovery: RESISTED 100, RECOVERED 70, DECLINED 50, COMPROMISED 0, INCOMPLETE missing; weight 0.20.

A component is missing when its denominator is empty. The remaining weights are renormalised. With fewer than 2 components there is no index.

## Export files (`exports/<timestamp>/`)
CSV files follow RFC 4180:
- comma separator;
- fields containing commas, quotes, line breaks or surrounding spaces are quoted, with quotes doubled;
- CRLF row endings;
- UTF-8;
- invariant-culture numbers (`.` decimal separator);
- missing values left as empty cells;
- text starting with `= + @` or a tab is prefixed with `'` so spreadsheets do not run it as a formula.

| file | content |
|---|---|
| `sessions/<id>.json` | per-session JSON (the format above) |
| `events.csv` | one row per event: `dataset, session_id, seq, source_seq, kind, event_name, scenario_id, interaction_id, channel, t_s, elapsed_s, classification, risky_action, corrective, completion, persuasion_technique, latency_s, dialogue_node, choice_class, content_source, scene, detail` |
| `session_summary.csv` | one row per session: outcomes, counts, rates, completion, median latency, index |
| `aggregate_metrics.csv` | `dataset, metric, scope, numerator, denominator, value, unit, definition` |
| `export_manifest.json` | schema, dataset, time, counts, file list, data notice |

An export never overwrites an earlier one; each export goes into a new timestamped folder.
