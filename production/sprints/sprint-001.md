# Sprint 001 — 2026-10-26 to 2026-10-30

> **Generated**: 2026-10-23 by `/sprint-plan new --review lean` (non-interactive, `automation: autonomous`) on branch `docs/m1a-replan`.
> **Source order**: `production/epics/implementation-order.md` (M-1a, playable-first, regenerated 2026-10-23) — Track A, wave order.
> **Authority**: Evan, Mail Lead chat, 2026-10-23 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024"; 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example".
> **PR-SPRINT**: skipped — Lean mode (`--review lean`; not a PHASE-GATE).
> **QA plan**: none found under `production/qa/` — see the warning below. Run `/qa-plan sprint` before the first story starts.
> **Milestone**: no `production/milestones/` directory — planning against the story backlog alone. M-1a is the owner-directed milestone (stage: Technical Setup → implementing M-1a).
> **Risk register**: no `production/risk-register/` — risks assessed from the sprint contents only.
> **`production/sprint-status.yaml`**: not written this run (the planning instruction allowed only this file). Generate it from the tables below before `/dev-story` / `/story-done` need it.

## Sprint Goal

Land the four Sim-side roots of the playable greybox path — the locked carrier-solver golden (player-character 010), focus occlusion (player-character 015), the Mail system shell with the stub wallet (mail-destinations 005) and the throw flight sweep (throwing-catching 002) — plus the Drop op, so that Track A wave 2 (host-side interaction, the hold counter, the mail consume routine, throw receivers) can start in Sprint 002.

## Milestone Context

- **Current Milestone**: M-1a (owner-directed; no milestone definition file)
- **Milestone Deadline**: not defined
- **Sprints Remaining**: not defined. Track A is 73 stories in 16 waves; at this capacity (~4–5 stories per sprint) the greybox path is roughly 15 sprints of serial work unless the draft errata E1–E9 (decision log 2026-10-23) shrink it.

## Capacity

- **Implementer**: Mail Dev — one story at a time
- **Total days**: 5 working days (Mon 2026-10-26 – Fri 2026-10-30)
- **Buffer (20%)**: 1 day reserved for review rounds, readiness fixes and unplanned work
- **Available**: 4 days
- **Sizing**: S ≈ 0.5 d, M ≈ 1 d (per the planning instruction; story headers agree)

### How the stories were picked

1. Only Track A stories, in wave order. Wave A1 holds 12 stories (≈ 10.5 d), more than twice the available capacity, so this sprint is wave A1 only; no wave A2 story has every dependency Complete or in-sprint *and* fits.
2. Inside wave A1, the order is by the length of the downstream Track A chain (a story that gates wave 16 before one that gates wave 12), then by how many Track A stories it unblocks. Stories 010/015 (player-character) gate the host shell (`godot-host-shell/005`, wave 7) through player-character 019/020 and the input router through player-character 017/018; mail-destinations 005 roots the whole Mail chain (006 → 016) and manual-delivery; throwing-catching 002 roots the throw chain (003 → 010 → 011 → 012 → 017 → 020 → 021).
3. **Excluded from wave A1**: `content-data/019` depends on `content-data/018`, which is **In Progress** (partial code, 0/7 ACs ticked as of the last handoff) — neither Complete nor in-sprint, so 019 is not eligible this sprint. `input-controls/019` and `input-controls/025` are last in the pull order: they are kept only because ADR-0012 requires them, draft erratum E1 would defer them, and nothing in Track A needs them before wave 7 (025) and wave 14 (019).
4. `story_granularity: fine` asks for 15–25 stories per sprint; capacity bounds this sprint to 5–8. Nothing was padded.
5. No story in this sprint carries an accessibility, localisation or anti-cheat AC (Evan 7:15 AM PT).

## Tasks

All stories are Sim / Client.Core (`net8.0`, headless xUnit); none touches Godot this sprint. Estimates are implementation days; Mail Review rounds land in the buffer.

### Must Have (Critical Path)

| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria | Status |
|----|------|-------------|-----------|-------------|-------------------|--------|
| S001-001 | [player-character/010](../epics/player-character/story-010-golden-transform-hash-p2-1.md) — PCM-P2-1: the 600-tick golden per-tick transform hash and the solver determinism gates (M, Integration, Feature) | Mail Dev (gameplay-programmer) | 1.0 | None (stories 005–009 Complete; 027 Deferred — capture the goldens on the current solver) | Committed 600-tick golden hash chain reproduces across runs and through the sim-core recorder/replay path; the script covers every solver path (walk, sprint, jump variants, ramp, water); the mutation check flips the final hash for a one-unit input or rounding change; counters BLOCKING (`carrierSubSteps` ≤ 4, `carrierSlideIterations` ≤ 3, overflow 0, 0 B after warm-up); no-Godot architecture test; PCM-PERF-1 advisory only | Not Started |
| S001-002 | [player-character/015](../epics/player-character/story-015-focus-occlusion-lazy-score-order.md) — Focus occlusion: lazy, score-ordered line-of-sight against terrain and static solids (S, Logic, Feature) | Mail Dev (gameplay-programmer) | 0.5 | None (014, 004, world-stub 003 Complete) | PCM-P11-1 occlusion rows (own-box crossings stay eligible); lazy order — exactly one test when the best candidate is clear; PCM-P11-2c latched incumbent survives a 1-tick occlusion; unlatched incumbent drops focus the tick it is occluded and the next-best candidate is focused; host `CheckInteraction` never calls occlusion code (arch test); one `QueryStaticSolids` call per segment, 0 B over 10 000 passes | Not Started |
| S001-003 | [mail-destinations/005](../epics/mail-destinations/story-005-walletgate-stubwallet-mail-system.md) — `IWalletGate` port, the M-1a `StubWallet` (`WalletChanged`, dump) and the Mail system shell (composition, phase 8 idle `Mail.Step`, no-Economy arch test) (M, Integration, Feature) | Mail Dev (gameplay-programmer) | 1.0 | None (001, 002, sim-core 015/025 Complete) | AC-1 `TryDebitPenalty` floor semantics at 0 / −496 / −500 (M-R7-2c, FX-W); AC-2 no `PerformativeMail.Sim.MailDestinations*` namespace and no Economy reference (reflection test); AC-3 composition builds `StubWallet` before `Mail`, injects `IWalletGate`, builds the destination registry from `IWorldQuery`; AC-4 300 idle ticks with 10 destinations visit 0 rows, allocate 0 B, dump unchanged | Not Started |
| S001-004 | [throwing-catching/002](../epics/throwing-catching/story-002-flightsweep-contact-ranks-winner.md) — `FlightSweep.FirstContact`: F4 swept contact, receiver ranks, descending-entry mailbox rule and the `(f, rank, dist², Mix64)` winner (M, Logic, Feature) | Mail Dev (gameplay-programmer) | 1.0 | None (001, player-character 004, world-stub 001 Complete) | TC-F4-1 worked numbers (flat: f = 154, contact (10250, 944, 10500), Mailbox) and the `CeilDiv`/`FloorDiv` edge (f = 1000 hit vs no hit); inside-volume f = 0; heightfield march f = 417; TC-T6-1 rank order for all 7 adjacent pairs and earlier-f-beats-rank; TC-T6-2 descending-only mailbox entry; TC-E-1 b origin inside a volume | Not Started |
| S001-005 | [grid-inventory/020](../epics/grid-inventory/story-020-drop-op.md) — Drop op: whole entry to a WorldItem at +500 mm along facing, ground-snapped; swimming refused (S–M, Logic, Core) | Mail Dev (gameplay-programmer) | 0.5 | None (013 Complete) | H-R7-4 drop half: swimming → `ok:false, swimming` and no WorldItem; otherwise exactly one WorldItem `origin = Dropped` at +500 mm along facing, ground-snapped; focused-cell drop is one atomic lift-and-drop; water drop floats (story 013 water class); non-walkable point falls back to the player's own position | Not Started |

Must Have total: **4.0 days** (= available capacity).

### Should Have

| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria | Status |
|----|------|-------------|-----------|-------------|-------------------|--------|
| S001-010 | [throwing-catching/008](../epics/throwing-catching/story-008-throwing-system-flight-table-dump-tickend.md) — `ThrowingSystem` shell, the `FlightTable` (SoA, 160 slots, wrap-safe `FlightId`), the canonical dump section and the tick-end conservation record (M, Logic, Feature) | Mail Dev (gameplay-programmer) | 1.0 | None (001, sim-core 002/011/022 Complete) | `[SimSystem(SystemId.Throwing)]` shell with one injectable constructor; `FlightTable.TryAppend` stores every ADR-0015 field, 160-slot cap; creation-order iteration survives stable compaction (10 000-op churn); TC-E-6 c u16 `FlightId` wrap after 70 000 launches, no ordinal compares; per-slot `Tat` cleared on leave/rejoin; `ITickEndCheck` `launched == resolved + live` writes to `SimCheckRing`, never throws; dump section field by field; 0 B over 100 000 append/remove cycles | Not Started |

Should Have lands only if the Must Haves close inside 4 days; it consumes the buffer.

### Nice to Have (Cut First)

| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria | Status |
|----|------|-------------|-----------|-------------|-------------------|--------|
| S001-020 | [throwing-catching/006](../epics/throwing-catching/story-006-throw-op-event-declarations-and-wire-codecs.md) — `ThrowRelease` OpKind, `ThrowLaunched` / `FlightResolved` events: declarations, reason codes and loopback wire codecs (M, Integration, Feature) | Mail Dev (gameplay-programmer / network-programmer) | 1.0 | None (001, sim-core 013/015, loopback 002/007 Complete) | 28 B `ThrowReleasePayload` with ADR-0015 offsets, `OpKind.ThrowRelease == 0x0140`; 40 B `ThrowLaunched`, `FlightResolved` event layouts; codecs round-trip at maximum and negative values, `FlightId` travels as raw u16; H-NET-5 fuzz 10 000 inputs with no exception; loopback end-to-end through a stub Throwing system; M0 narrower sizes named in a comment, not asserted | Not Started |
| S001-021 | [player-character/021](../epics/player-character/story-021-camera-boom-solver-config.md) — Camera rig profiles and the boom solver against the Sim geometry (Godot-free, `Client.Core`) (M, Logic, Presentation) | Mail Dev (gameplay-programmer) | 1.0 | None (004, world-stub 001/003 Complete) | PCM-P9-1 default profile (pivot 1600, boom 3000, shoulder +600, pitch −60°…+70°); `BoomSolver.Solve` returns the longest safe boom ≥ 300 mm; `BoomSmoother` pulls in within 50 ms and eases out; camera never below terrain + 250 nor below y = 0 while swimming (2 000-sample sweep); solid set equals `QueryStaticSolids` (property test); integer maths, 0 B over 10 000 calls; no dither / FOV kick / landing dip | Not Started |

### Pull order after this sprint (wave A1 remainder, then wave A2)

Not planned here; this is the order Sprint 002 should pick from, dependencies permitting.

1. `grid-inventory/019` — range-gated Open and Pickup handlers and the leash auto-close (M). Playable leaf.
2. `content-data/018` (In Progress, wave 0 — finish first) → `content-data/019` ContentLoader (M) → `godot-host-shell/007` (S–M).
3. `input-controls/025` CI lint (S) and `input-controls/019` settings codec (M) — ADR-0012-required; draft erratum E1 would defer both. Pull last unless Evan declines E1.
4. Wave A2 opens as its roots land: `player-character/017` (after 015), `player-character/019` (after 010), `throwing-catching/003` (after 002), `mail-destinations/006` (after 005).

## Carryover from Sprint 000

| Original ID | Task | Reason for Carryover | New Estimate | Priority Change |
|------------|------|---------------------|-------------|----------------|
| — | None — this is the first sprint plan. | | | |

Note: `content-data/018` is **In Progress** from pre-sprint work (schema stage; FieldSpec tables, `SchemaTable`, `ParsedDef`/`Value` exist; 0/7 ACs ticked at the 2026-10-17 handoff; the ADR-0010 defaults and seconds → ms rulings are decided, PR #19 merged). It is not a Track A story and is not scheduled here; it stays In Progress and is first in the Sprint 002 pull order because `content-data/019` and `godot-host-shell/007` wait on it.

## Risks to This Sprint

| Risk | Probability | Impact | Mitigation | Owner |
|------|------------|--------|-----------|-------|
| Must Have is sized exactly at the available 4.0 days; one review round or readiness fix on any M story pushes S001-010 out and can push S001-005 out. | High | Low — the Should/Nice rows are independent of the Must Haves; nothing downstream waits on them this sprint | Run in table order (longest downstream chain first). Cut from the bottom: S001-021, S001-020, S001-010, then S001-005. Start the next story while a PR sits in Mail Review; all five Must Haves are mutually independent. | Mail Dev |
| Evan accepts draft erratum **E3** (ADR-0013 l.29(1): the PCM-P2-1 golden moves to M0) while S001-001 is in flight or before it starts. | Low–Medium | Medium — S001-001 becomes optional work, and `player-character/019` lists 010 as "solver locked for the host-role step" | Start S001-001 on Monday as planned; the golden is cheap and guards the solver friends will play on (Mail Lead's E4 reasoning). If E3 lands before Monday, swap S001-001 for `player-character/017` (M; depends on 015 = S001-002, in-sprint) and edit 019's dependency line in the Sprint 002 plan. | Mail Lead |
| 0 B / counter ACs are BLOCKING in S001-001, S001-002, S001-003 and S001-010 until draft erratum **E2** (0 B gates advisory until M0) is accepted. | Medium | Medium — a failing allocation probe can cost half a day | Reuse the existing probe patterns (sim-core 007 counters, the warm-up-then-measure harness from earlier stories). If a 0 B check fails and E2 has been accepted by then, record it as advisory and log the decision; otherwise fix it inside the buffer. | Mail Dev |
| S001-004's worked-number ACs (TC-F4-1 f = 154 / 417, the `CeilDiv`/`FloorDiv` edge) are only as good as the GDD's F4 tables; a convention mismatch means a golden that cannot be met. | Medium | Medium | Re-derive the four worked numbers from `design/gdd/throwing-catching.md` F4 before coding; if the GDD and the story disagree, log the ruling in the decision log and fix the story AC, not the maths. | Mail Dev |
| Mail Review loop: M stories keep the full review loop (the lighter S-story process in the decision log was approved by Evan 2026-10-23 8:10 AM PT: S stories use it). Review turnaround is elapsed time that the 1 d estimates do not include. | High | Medium | The 1 d buffer is for review rounds. Do not block on a review: open the PR, move to the next Must Have, return for findings. | Mail Dev / Mail Lead |
| `content-data/018` stays In Progress unowned; `content-data/019` → `godot-host-shell/007` → `godot-host-shell/011` (the headless smoke, wave A12) slip one sprint. | High | Low this sprint — nothing in Sprint 001 needs it; the host shell (`godot-host-shell/005`) does not depend on 007 | Pull 018 first in Sprint 002. | Mail Lead |

## External Dependencies

| Dependency | Status | Impact if Delayed | Contingency |
|-----------|--------|------------------|-------------|
| Evan: accept **ADR-0018** (minimal HUD composition) | Proposed, awaiting Evan (TD-173) | None this sprint — `hud-notifications/001–003` stay Blocked; first needed around wave A10 | Keep the HUD stories Blocked; the Track A wave numbering already accounts for it. |
| Evan: rulings on draft errata **E1–E9** (decision log 2026-10-23, TD-173) | Pending | E3 touches S001-001 (see Risks); E2 touches every 0 B AC in this sprint; E1 would remove `input-controls/019`/`025` from the pull order | Plan assumes every Accepted ADR stands as written. Re-run `/sprint-plan update` if any erratum is accepted mid-sprint. |
| Evan: confirm the Track A leaf list (a Mail Lead planning default) | Pending | Low — every Sprint 001 story is a wave A1 root that any plausible leaf list keeps | None needed. |
| Mail Dev's unblock PR for `grid-inventory/022` and `input-controls/029` | Separate PR, in flight | None — neither story is in Track A wave A1 | None needed. |
| Godot 4.7.1 pin (VERSION.md: HIGH risk, beyond LLM training data) | Pinned | None this sprint — every story is headless `net8.0` Sim / Client.Core; no Godot API is touched | First Godot-facing Track A story is `godot-host-shell/007` (wave A2) / `input-controls/026` (wave A7). |

## QA Plan

> ⚠️ **No QA Plan**: This sprint was started without a QA plan. Run `/qa-plan sprint`
> before the last story is implemented. The Production → Polish gate requires a QA
> sign-off report, which requires a QA plan.

QA plan: run `/qa-plan sprint` before implementation begins (non-interactive run — the planner could not launch it). All six planned stories are Logic or Integration; each already names its unit/integration tests in its ACs, so the QA plan can be written from the story files alone.

## Definition of Done

- [ ] All Must Have tasks completed (S001-001 … S001-005 at `Complete`)
- [ ] All tasks pass acceptance criteria (each AC ticked in the story file by `/story-done`)
- [ ] QA plan exists (`production/qa/qa-plan-sprint-001-[date].md`, from `/qa-plan sprint`)
- [ ] All Logic/Integration stories have passing unit/integration tests (all four CI legs: net10/net8 × Debug/Release, 0 failed)
- [ ] Smoke check passed (`/smoke-check sprint`)
- [ ] QA sign-off report: APPROVED or APPROVED WITH CONDITIONS (`/team-qa sprint`)
- [ ] No S1 or S2 bugs in delivered features
- [ ] Design documents updated for any deviations (decision log entry per ruling; GDD note where the story AC changed)
- [ ] Code reviewed and merged (Mail Review on each PR; main green)
- [ ] `production/epics/implementation-order.md` regenerated (`gen_impl_order.py`) once the sprint's statuses change

> **Scope check:** If this sprint includes stories added beyond the original epic scope, run `/scope-check [epic]` to detect scope creep before implementation begins. (None were added: every row is an existing Ready story from `implementation-order.md`.)

## Next Steps

1. `/qa-plan sprint` — required before implementation begins.
2. Generate `production/sprint-status.yaml` from the tables above (not written by this run).
3. `/story-readiness production/epics/player-character/story-010-golden-transform-hash-p2-1.md`, then `/dev-story` on it.
4. `/sprint-status` mid-week; `/sprint-plan update` if Evan rules on E2/E3 or accepts ADR-0018.
