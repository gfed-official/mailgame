# ADR-0024: Session Stats File (M-1a slice of Persistence & Telemetry)

## Status
Accepted

## Date
2026-10-23

## Last Verified
2026-10-23

## Decision Makers
Mail Lead (draft, `/workspace/repos/logs/unblock-six.md` §3–4 option A). **Accepted 2026-10-23 by Evan (project owner)**, Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". Scope confirmed by the 7:15 AM PT steer: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". So this ADR is the stats file only, and nothing more. **Basis for the Accepted status:** Evan's 7:13 message approves the one-page telemetry ADR as recommended in `/workspace/repos/logs/unblock-six.md` §3–4 (option A). This text is that draft, plus the `session_end` line and the drop counter that throwing-catching 016's "Failure is visible" AC needs. Mail Lead set the field on that approval. If Evan wants the template's own acceptance step (`.claude/docs/templates/architecture-decision-record.md` l.7–17), he can confirm at review.

## Summary
The M-1a fun test needs two numbers the prototype could not give: the throw share and the hand share (`prototypes/m1a-delivery-throwing-concept/REPORT.md`, Metrics: "No quantitative throw share captured"). This ADR fixes the one file they go to and nothing else. It is a **slice** of the ADR-0024 planned for M1 in `docs/architecture/architecture.md` l.628. Profile and settings persistence, schema migration and the rest of that row are still unwritten, and will be added to this ADR as an M1 amendment.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (.NET) |
| **Domain** | Client.Core (writer) + `src/Game` (file port) |
| **Layer** | Feature (Client.Core tooling); not on the Sim tick path |
| **Knowledge Risk** | LOW. The only Godot call is `FileAccess` on a `user://` path in `src/Game`, which ADR-0019 already uses (l.35). |
| **Post-Cutoff APIs Used** | None new |
| **Verification Required** | None engine-side beyond the story's headless write test. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Client.Core is Godot-free), ADR-0016 (telemetry reads published client state and `PresentationEventStream`, never the Sim tick path), ADR-0019 (boot step 3 "Persistence (ADR-0024)", l.120) — all Accepted |
| **Enables** | `production/epics/manual-delivery/story-009-telemetry-latched-branch-handshare.md`, `production/epics/throwing-catching/story-016-telemetry-t14-session-stats.md` |
| **Blocks** | — |

## Decision

1. **Ownership.** Client.Core owns `SessionStatsWriter`. The Sim never writes stats, and nothing runs on the tick path (ADR-0016). Producers call `ISessionStatsSink.Append(in record)`, which only copies into a pre-sized in-memory buffer and allocates nothing after warm-up.
2. **The file.** Writes go through an `IStatsFile` port implemented in `src/Game` (Godot `FileAccess`), so Client.Core stays Godot-free (ADR-0001). The implementation is created at ADR-0019 boot step 3 (l.120) and handed to the writer at session composition. The path is `user://stats/session-<UTC yyyyMMddTHHmmssZ>.jsonl`: one file per app session, append-only.
3. **Format.** One JSON object per line: `{"v":1,"type":"<recordType>","tick":<uint>, …}`. Each owning story fixes its own record type's fields (manual-delivery 009: `latchedBranch` and the `handShare` inputs; throwing-catching 016: the launch and terminal records and `redAgeAtReleaseMs`). A clean close writes a final `{"v":1,"type":"session_end",…}` line.
4. **Flush.** The writer flushes at run end, on quit or close request, and every 60 s of wall time. A write failure logs one warning, increments a counter and drops that buffer. It never throws into the frame. Analysers report "log incomplete" when `session_end` is missing or a drop was counted (throwing-catching 016 "Failure is visible").
5. **Visibility.** The file is never shown to players and never uploaded, and it holds no personal data.
6. **Out of scope until M1:** profile and settings stores, schema versions and migration, ids as strings, input settings per machine or roaming (architecture.md l.628). `user://input.json` stays input-controls' own (stories 019 and 028).

## Alternatives Considered
- **Defer both telemetry stories to M0** (unblock-six.md option B). Rejected by Evan (7:13 AM PT), because the fun gate wants the throw and hand shares.
- **Write the full M1 ADR-0024 now.** Rejected: it is production persistence that the friends greybox doesn't need (Evan 7:15 AM PT).

## Consequences
- md-009 and tc-016 are unblocked against this text.
- One new Godot-side class (`UserStatsFile : IStatsFile`) and one Client.Core writer. No Sim change.

## Validation Criteria
- A Client.Core test over an in-memory `IStatsFile` sees the records in order, a `session_end` line on clean close, and no allocation per `Append` after warm-up.
- A write-failure test leaves the session running, increments the counter, and makes the analyser report "log incomplete".
- An architecture test checks that `PerformativeMail.Sim` has no reference to `ISessionStatsSink`.

## GDD Requirements Addressed
- `TR-delivery-022` (`design/gdd/manual-delivery.md` M-FX-1b, M-FX-4); `TR-throwing-020` (`design/gdd/throwing-catching.md` T14, TC-T14-1, TC-FE-3). `TR-hud-019` (architecture.md l.573) stays with the M1 amendment.

## Related
`docs/architecture/architecture.md` l.628 (the planned full ADR-0024); `/workspace/repos/logs/unblock-six.md` (the source draft).

## Amendments
None.
