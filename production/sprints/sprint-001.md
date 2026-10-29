# Sprint 001 — 2026-10-26 to 2026-10-30

> **Generated**: 2026-10-23 by `/sprint-plan new --review lean` (non-interactive, `automation: autonomous`) on branch `docs/m1a-replan`.
> **Updated**: 2026-10-28 (docs PR `docs/md005-cd019-readiness`, off main 3956c58): Evan 2026-10-28 7:26 AM PT, Mail Lead chat, card 'Two approvals for sprint 001': "Approve all four TD-175 defaults for player-character 015: the extra incumbent occlusion test, the FocusOcclusionTests counter (55), the terrain-endpoint rule for ground-level anchors, and IWorldQuery on FocusStep" (TD-175 closed; S001-002 may start) and "Treat grid-inventory 020's S–M estimate as M, with full readiness and the full review loop" (S001-005 is M: full readiness and the full review loop; its 0.5 d implementation estimate is unchanged). Same time, card 'Mail-destinations 005 decisions': "Approve the ADR-0003 §4 and ADR-0017 §8 errata: the composition root injects IWalletGate into Mail only, and SystemGraph is unchanged" and "Approve all six TD-177 defaults for mail-destinations 005, including the ADR-0007 §4 economy event block row" — S001-003 passed its full readiness pass and is Ready (TD-177 closed). Earlier: 2026-10-23 by `/sprint-plan update` (non-interactive) after Mail Review PR #47 B3 (the generator now reads every `- Depends on:` line; order regenerated), Evan's 8:10 and 8:20 AM PT approvals and the PR #46 merge (main 6e7b0cd). Story rows, estimates, the pull order, Risks and External Dependencies were re-checked against the regenerated order and the current story headers; the five Must Haves are unchanged.
> **Source order**: `production/epics/implementation-order.md` (M-1a, playable-first, regenerated 2026-10-23 after PR #47 B3; regenerated again 2026-10-27, PR #47 r3, after the generator stopped counting In Progress as done; regenerated 2026-10-28, PR #50, after player-character 010 Complete; regenerated again 2026-10-28, PR #53, after content-data 018 Complete; regenerated again 2026-10-28, PR #55, after throwing-catching 002 Complete, which moves throwing-catching 003 into wave A1; regenerated again 2026-10-28, PR #56, after player-character 015 Complete, which moves player-character 017 into wave A1; regenerated again 2026-10-28, branch `story/mail-destinations-005`, after mail-destinations 005 Complete, which moves mail-destinations 006 into wave A1) — Track A, wave order. Wave A1 is 10 stories: content-data 019, grid-inventory 020, input-controls 019, mail-destinations 006, player-character 017, 019, 021, throwing-catching 003, 006, 008 (throwing-catching 002 is Complete, PR #55, and 003 moved up from wave A2; player-character 015 is Complete, PR #56, and 017 moved up; mail-destinations 005 is Complete and 006 moved up; `grid-inventory/019` is wave A2 — it depends on `player-character/019`; `godot-host-shell/007` is wave A2, behind `content-data/019`).
> **Authority**: Evan, Mail Lead chat, 2026-10-23 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024"; 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it “production ready” is not needed. Accessibility for example"; 8:10 AM PT, "approve all": ADR-0018 Accepted (hud-notifications 001–003 Ready), errata E1–E6 and E9 approved (E7, E8 declined), the lighter TD/review process in force (S stories: one Mail Review round, blocking findings only, checklist-only readiness; M+ stories keep the full loop), 0 B checks advisory until M0 (E2), TD-173 closed; 8:20 AM PT: TD-172 D4–D6 approved, TD-172 closed.
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
- **Sprints Remaining**: not defined. Track A is 72 stories in 15 waves (75 before throwing-catching 002 went Complete, PR #55, player-character 015 went Complete, PR #56, and mail-destinations 005 went Complete, branch `story/mail-destinations-005`; regenerated order, 2026-10-23, regenerated 2026-10-27 and 2026-10-28 (player-character 010 Complete, PR #50; content-data 018 Complete, PR #53; throwing-catching 002 Complete, PR #55; player-character 015 Complete, PR #56; mail-destinations 005 Complete, branch `story/mail-destinations-005`), after the approved errata E1–E6/E9 deferrals and splits); at this capacity (~4–5 stories per sprint) the greybox path is roughly 15 sprints of serial work.

## Capacity

- **Implementer**: Mail Dev — one story at a time
- **Total days**: 5 working days (Mon 2026-10-26 – Fri 2026-10-30)
- **Buffer (20%)**: 1 day reserved for review rounds, readiness fixes and unplanned work
- **Available**: 4 days
- **Sizing**: S ≈ 0.5 d, M ≈ 1 d (per the planning instruction; story headers agree)

### How the stories were picked

1. Only Track A stories, in wave order. Wave A1 holds 10 stories; at planning 9 were Ready (≈ 7.5 d by the current Estimate lines) and the tenth, `content-data/018`, was already In Progress (see the note below; it is Complete since 2026-10-28, PR #53, and the regenerated wave A1 lists `content-data/019` in its place), about twice the available capacity, so this sprint is wave A1 only; no wave A2 story has every dependency Complete or in-sprint *and* fits.
2. Inside wave A1, the order is by the length of the downstream Track A chain (a story that gates wave 16 before one that gates wave 12), then by how many Track A stories it unblocks. Stories 010/015 (player-character) gate the host shell (`godot-host-shell/005`, wave 7) through player-character 019/020 and the input router through player-character 017/018; mail-destinations 005 roots the whole Mail chain (006 → 016) and manual-delivery; throwing-catching 002 roots the throw chain (003 → 010 → 011 → 012 → 017 → 020 → 021).
3. **Not in wave A1**: `content-data/019` depends on `content-data/018` (its `Depends on` line lists 018 directly), which was **In Progress** at planning (0/8 ACs ticked; Complete 2026-10-28, PR #53, which moves 019 to wave A1), so the generator placed 019 in wave A2 (since 2026-10-27; before that it counted In Progress as satisfied and listed 019 in wave A1). At planning 018 was neither Complete nor in-sprint, so 019 was not eligible this sprint; since 018 went Complete (2026-10-28, PR #53) 019 is wave A1 and eligible for the next pull. `input-controls/019` (now S–M after the E1 split: defaults, parse and validation only; the rebinding writer is story 032, Deferred) is last in the pull order: it is kept because ADR-0012 and `input-controls/028` (wave A14) need it, and nothing in Track A needs it earlier. `input-controls/025` is Deferred (Evan 8:10 AM PT, E1) and no longer in the order.
4. `story_granularity: fine` asks for 15–25 stories per sprint; capacity bounds this sprint to 5–8. Nothing was padded.
5. No story in this sprint carries an accessibility, localisation or anti-cheat AC (Evan 7:15 AM PT).

## Tasks

All stories are Sim / Client.Core (`net8.0`, headless xUnit); none touches Godot this sprint. Estimates are implementation days; Mail Review rounds land in the buffer.

### Must Have (Critical Path)

| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria | Status |
|----|------|-------------|-----------|-------------|-------------------|--------|
| S001-001 | [player-character/010](../epics/player-character/story-010-golden-transform-hash-p2-1.md) — PCM-P2-1: the 600-tick golden per-tick transform hash, single-platform Linux CI regression guard (S–M, Integration, Feature; split 2026-10-23 — cross-OS equality and the blocking counters / 0 B gate are story 028, Deferred to M0) | Mail Dev (gameplay-programmer) | 0.75 | None (stories 005–009 Complete; 027, 028 Deferred — capture the golden on the current solver) | Committed 600-tick golden hash chain equals the golden file on the net10.0 and net8.0 legs, Debug and Release, on the Linux runner (the replay check moved to player-character 020); the script covers every solver path (walk, sprint, jump variants, ramp, step, water, haul-out) with a coverage assertion; the mutation check flips the final hash for each named one-unit input perturbation; no-Godot architecture test; PCM-PERF-1 microbenchmark advisory only (no counter or 0 B gate — story 028) | Complete (PR #50) |
| S001-002 | [player-character/015](../epics/player-character/story-015-focus-occlusion-lazy-score-order.md) — Focus occlusion: lazy, score-ordered line-of-sight against terrain and static solids (S, Logic, Feature; lighter S-story review and checklist readiness) | Mail Dev (gameplay-programmer) | 0.5 | None (014, 004, world-stub 003 Complete) | PCM-P11-1 occlusion rows (own-box crossings stay eligible); lazy order — tests in ascending score order stop at the first that passes (exactly one when there is no incumbent or it is the best and clear; at most one extra test for an unlatched incumbent that is not the first passing candidate, TD-175 item 1, Approved by Evan 2026-10-28 7:26 AM PT); PCM-P11-2c latched incumbent survives a 1-tick occlusion; unlatched incumbent drops focus the tick it is occluded and the next-best candidate is focused; no host line-of-sight: an architecture test scans `PerformativeMail.Sim.Players` outside the `.Client` namespace for references to `FocusOcclusion` (story 019's `CheckInteraction` inherits it); one `QueryStaticSolids` call per segment; 0 B over 10 000 passes (advisory until M0, E2) | Complete (PR #56) |
| S001-003 | [mail-destinations/005](../epics/mail-destinations/story-005-walletgate-stubwallet-mail-system.md) — `IWalletGate` port, the M-1a `StubWallet` (`WalletChanged`, dump) and the Mail system shell (composition, phase 8 idle `Mail.Step`, no-Economy arch test) (M, Integration, Feature) | Mail Dev (gameplay-programmer) | 1.0 | None (001, 002, sim-core 015/025 Complete) | AC-1 `TryDebitPenalty` floor semantics at 0 / −496 / −500 (M-R7-2c, FX-W); AC-2 no `PerformativeMail.Sim.MailDestinations*` namespace and no Economy reference (reflection test); AC-3 composition builds `StubWallet` before `Mail`, injects `IWalletGate`, builds the destination registry from `IWorldQuery`; AC-4 300 idle ticks with 10 destinations visit 0 rows, dump unchanged, 0 B (advisory until M0, E2) | Complete (branch `story/mail-destinations-005`) |
| S001-004 | [throwing-catching/002](../epics/throwing-catching/story-002-flightsweep-contact-ranks-winner.md) — `FlightSweep.FirstContact`: F4 swept contact, receiver ranks, descending-entry mailbox rule and the `(f, rank, dist², Mix64)` winner (M, Logic, Feature) | Mail Dev (gameplay-programmer) | 1.0 | None (001, player-character 004, world-stub 001 Complete) | TC-F4-1 worked numbers (flat: f = 154, contact (10250, 944, 10500), Mailbox) and the `CeilDiv`/`FloorDiv` edge (f = 1000 hit vs no hit); inside-volume f = 0; heightfield march f = 417; TC-T6-1 rank order for all 7 adjacent pairs and earlier-f-beats-rank; TC-T6-2 descending-only mailbox entry; TC-E-1 b origin inside a volume | Complete (PR #55) |
| S001-005 | [grid-inventory/020](../epics/grid-inventory/story-020-drop-op.md) — Drop op: whole entry to a WorldItem at +500 mm along facing, ground-snapped; swimming refused (M — was S–M, ruled M by Evan 2026-10-28 7:26 AM PT; Logic, Core; Track A leaf) | Mail Dev (gameplay-programmer) | 0.5 | None (013 Complete) | H-R7-4 drop half: swimming → `ok:false, swimming` and no WorldItem; otherwise exactly one WorldItem `origin = Dropped` at +500 mm along facing, ground-snapped; focused-cell drop is one atomic lift-and-drop; water drop floats (story 013 water class); non-walkable point falls back to the player's own position | Not Started |

Must Have total: **3.75 days** (available capacity 4.0 d; 0.25 d slack after the player-character 010 split, which went from 1.0 d to 0.75 d).

Readiness and review tier for the S–M rows (S001-001, S001-005): take the full `/story-readiness` pass and the full Mail Review loop, i.e. treat S–M as M (Mail Lead planning default; for S001-001 Approved by Evan 2026-10-28 2:35 AM PT (Mail Lead chat): "Approve all seven TD-174 defaults for player-character 010: the FX-Golden world, Boost with buffCapPm 4000, the spawn-crate start, the named ticks, the mutation ticks, the perf fixture, and treating its S–M estimate as M"; TD-174, closed; for S001-005 Approved by Evan 2026-10-28 7:26 AM PT (Mail Lead chat, card 'Two approvals for sprint 001'): "Treat grid-inventory 020's S–M estimate as M, with full readiness and the full review loop" — the story's Estimate line now reads M). S001-002 (S) uses the lighter process approved 8:10 AM PT; its four TD-175 defaults are Approved by Evan 2026-10-28 7:26 AM PT (same card): "Approve all four TD-175 defaults for player-character 015: the extra incumbent occlusion test, the FocusOcclusionTests counter (55), the terrain-endpoint rule for ground-level anchors, and IWorldQuery on FocusStep". S001-003 (M) passed its full `/story-readiness` pass on 2026-10-28 (Ready; TD-177 closed, Evan 7:26 AM PT, card 'Mail-destinations 005 decisions').

### Should Have

| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria | Status |
|----|------|-------------|-----------|-------------|-------------------|--------|
| S001-010 | [throwing-catching/008](../epics/throwing-catching/story-008-throwing-system-flight-table-dump-tickend.md) — `ThrowingSystem` shell, the `FlightTable` (SoA, 160 slots, wrap-safe `FlightId`), the canonical dump section and the tick-end conservation record (M, Logic, Feature) | Mail Dev (gameplay-programmer) | 1.0 | None (001, sim-core 002/011/022 Complete) | `[SimSystem(SystemId.Throwing)]` shell with one injectable constructor; `FlightTable.TryAppend` stores every ADR-0015 field, 160-slot cap; creation-order iteration survives stable compaction (10 000-op churn); TC-E-6 c u16 `FlightId` wrap after 70 000 launches, no ordinal compares; per-slot `Tat` cleared on leave/rejoin; `ITickEndCheck` `launched == resolved + live` writes to `SimCheckRing`, never throws; dump section field by field; 0 B over 100 000 append/remove cycles (advisory until M0, E2) | Not Started |

Should Have lands only if the Must Haves close inside 3.75 days; it uses the 0.25 d slack and 0.75 d of the buffer.

### Nice to Have (Cut First)

| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria | Status |
|----|------|-------------|-----------|-------------|-------------------|--------|
| S001-020 | [throwing-catching/006](../epics/throwing-catching/story-006-throw-op-event-declarations-and-wire-codecs.md) — `ThrowRelease` OpKind, `ThrowLaunched` / `FlightResolved` events: declarations, reason codes and loopback wire codecs (M, Integration, Feature) | Mail Dev (gameplay-programmer / network-programmer) | 1.0 | None (001, sim-core 013/015, loopback 002/007 Complete) | 28 B `ThrowReleasePayload` with ADR-0015 offsets, `OpKind.ThrowRelease == 0x0140`; 40 B `ThrowLaunched`, `FlightResolved` event layouts; codecs round-trip at maximum and negative values, `FlightId` travels as raw u16; H-NET-5 fuzz 10 000 inputs with no exception; loopback end-to-end through a stub Throwing system; M0 narrower sizes named in a comment, not asserted | Not Started |
| S001-021 | [player-character/021](../epics/player-character/story-021-camera-boom-solver-config.md) — Camera rig profiles and the boom solver against the Sim geometry (Godot-free, `Client.Core`) (M, Logic, Presentation) | Mail Dev (gameplay-programmer) | 1.0 | None (004, world-stub 001/003 Complete) | PCM-P9-1 default profile (pivot 1600, boom 3000, shoulder +600, pitch −60°…+70°); `BoomSolver.Solve` returns the longest safe boom ≥ 300 mm; `BoomSmoother` pulls in within 50 ms and eases out; camera never below terrain + 250 nor below y = 0 while swimming (2 000-sample sweep); solid set equals `QueryStaticSolids` (property test); integer maths; 0 B over 10 000 calls (advisory until M0, E2); no dither / FOV kick / landing dip | Not Started |

### Pull order after this sprint (wave A1 remainder, then wave A2)

Not planned here; this is the order Sprint 002 should pick from, dependencies permitting (regenerated order after PR #47 B3).

1. Wave A1 remainder not landed here: `throwing-catching/008` (M), `throwing-catching/006` (M), `player-character/021` (M) — in that order (chain length, then unblocks).
2. `content-data/018` (Complete 2026-10-28, PR #53) → `content-data/019` ContentLoader (M, now wave A1; Complete 2026-10-28, branch `story/content-data-019`) → `godot-host-shell/007` (S–M, now wave A2).
3. Wave A2 opens as its roots land: `player-character/017` (after 015; 015 is Complete, PR #56, so the 2026-10-28 regeneration puts 017 in wave A1), `player-character/019` (after 010; 010 is Complete, PR #50, so the 2026-10-28 regeneration puts 019 in wave A1), `throwing-catching/003` (after 002; 002 is Complete, PR #55, so the 2026-10-28 regeneration puts 003 in wave A1), `mail-destinations/006` (after 005; 005 is Complete, branch `story/mail-destinations-005`, so the 2026-10-28 regeneration puts 006 in wave A1).
4. `input-controls/019` settings codec (S–M after the E1 split: defaults, parse, validation; the rebinding writer is story 032, Deferred to M1) — ADR-0012-required, first needed by `input-controls/028` (wave A14) and `input-controls/020` (Track B). Pull last in wave A1. `input-controls/025` is Deferred (E1) and gone from the order.
5. Wave A3 follows, not before Sprint 003 (the 2026-10-28 regeneration after player-character 010 Complete moves `grid-inventory/019` and `throwing-catching/009` to wave A2): `grid-inventory/019` (M; now depends on `player-character/019`, so it is no longer a wave A1 pick — a Track A playable leaf all the same), `throwing-catching/009` (M; validation chain only after the E5 split, after 019/006/008), `mail-destinations/007`, `player-character/018`.

## Carryover from Sprint 000

| Original ID | Task | Reason for Carryover | New Estimate | Priority Change |
|------------|------|---------------------|-------------|----------------|
| — | None — this is the first sprint plan. | | | |

Note: `content-data/018` was **In Progress** from pre-sprint work (schema stage; FieldSpec tables, `SchemaTable`, `ParsedDef`/`Value` exist; 0/7 ACs ticked at the 2026-10-17 handoff; the ADR-0010 defaults and seconds → ms rulings are decided, PR #19 merged). It sat in wave A1 with Status In Progress but was not scheduled here (Complete 2026-10-28, PR #53: all eight ACs, Sonnet code review approved); it was first in the Sprint 002 pull order because `content-data/019` and `godot-host-shell/007` wait on it.

## Risks to This Sprint

| Risk | Probability | Impact | Mitigation | Owner |
|------|------------|--------|-----------|-------|
| Must Have is sized at 3.75 of the available 4.0 days; one review round or readiness fix on any M story pushes S001-010 out and can push S001-005 out. | High | Low — the Should/Nice rows are independent of the Must Haves; nothing downstream waits on them this sprint | Run in table order (longest downstream chain first). Cut from the bottom: S001-021, S001-020, S001-010, then S001-005. Start the next story while a PR sits in Mail Review; all five Must Haves are mutually independent. | Mail Dev |
| ~~Evan accepts draft erratum E3 while S001-001 is in flight~~ — **Resolved 2026-10-23 8:10 AM PT**: E3 approved as a milestone move only; the cross-OS half went to `player-character/028` (Deferred, M0) and S001-001 stays in the sprint as the single-platform Linux golden that 019, 020, 022 and 025 reuse. | — | — | None needed. S001-001 keeps its Monday slot at 0.75 d. | Mail Lead |
| ~~0 B / counter ACs are BLOCKING until E2 is accepted~~ — **Resolved 2026-10-23 8:10 AM PT**: E2 approved; 0 B checks in S001-002, S001-003, S001-010 and S001-021 are advisory until M0, and S001-001's counter / 0 B gate moved to story 028. Residual risk: an implementer treats an advisory 0 B probe as blocking and spends buffer on it. | Low | Low | Reuse the existing probe patterns; a failing 0 B probe is reported in the PR body (lighter process item 4), not fixed in the buffer, unless it hides a real leak. | Mail Dev |
| S001-004's worked-number ACs (TC-F4-1 f = 154 / 417, the `CeilDiv`/`FloorDiv` edge) are only as good as the GDD's F4 tables; a convention mismatch means a golden that cannot be met. | Medium | Medium | Re-derive the four worked numbers from `design/gdd/throwing-catching.md` F4 before coding; if the GDD and the story disagree, log the ruling in the decision log and fix the story AC, not the maths. | Mail Dev |
| Mail Review loop: the three M stories (S001-003, S001-004, S001-010) and the two S–M stories treated as M (S001-001, S001-005; both Evan-approved, 2026-10-28 2:35 and 7:26 AM PT) keep the full review loop; only S001-002 (S) gets the one-round, blocking-findings-only review approved 8:10 AM PT. Review turnaround is elapsed time that the estimates do not include. | High | Medium | The 1 d buffer is for review rounds. Do not block on a review: open the PR, move to the next Must Have, return for findings. | Mail Dev / Mail Lead |
| **Resolved 2026-10-28** (content-data 018 Complete, PR #53). `content-data/018` stays In Progress unowned; `content-data/019` → `godot-host-shell/007` → `godot-host-shell/011` (the headless smoke, wave A12) slip one sprint. | High | Low this sprint — nothing in Sprint 001 needs it; the host shell (`godot-host-shell/005`) does not depend on 007 | Pull 018 first in Sprint 002. | Mail Lead |

## External Dependencies

| Dependency | Status | Impact if Delayed | Contingency |
|-----------|--------|------------------|-------------|
| Evan: accept **ADR-0018** (minimal HUD composition) | **Resolved** — Accepted, Evan 2026-10-23 8:10 AM PT; `hud-notifications/001–003` are Ready (waves A11, A13, A14); TD-173 closed | None this sprint — first needed around wave A11 | None needed. |
| Evan: rulings on draft errata **E1–E9** (decision log 2026-10-23) | **Resolved** — E1–E6 and E9 approved, E7 and E8 declined, Evan 2026-10-23 8:10 AM PT; applied in PR #47 (story deferrals and splits, this plan's estimates) | None — S001-001 shrank to 0.75 d (E3/E2 split), every 0 B AC in the sprint is advisory (E2), `input-controls/025` left the pull order and `input-controls/019` narrowed (E1); E7/E8 declined keeps `simulation-core/030`, `mail-destinations/015` and `network-adapter-loopback/008` Ready in later waves | None needed. Re-run `/sprint-plan update` only if Mail Review PR #47 B2 (coverage of the split-out stories) changes a Sprint 001 story. |
| Evan: confirm the Track A leaf list (a Mail Lead planning default) | **Resolved** — approved with the TD-173 planning defaults, Evan 2026-10-23 8:10 AM PT | None | None needed. |
| Mail Dev's unblock PR for `grid-inventory/022` and `input-controls/029` | **Resolved** — PR #46 merged (main 6e7b0cd); both stories Ready; TD-172 D1–D3 approved 7:56 AM PT, D4–D6 approved and TD-172 closed 8:20 AM PT | None — `grid-inventory/022` is wave A7, `input-controls/029` Track B | None needed. |
| Godot 4.7.1 pin (VERSION.md: HIGH risk, beyond LLM training data) | Pinned | None this sprint — every story is headless `net8.0` Sim / Client.Core; no Godot API is touched | First Godot-facing Track A story is `godot-host-shell/007` (wave A3) / `input-controls/026` (wave A7). |

## QA Plan

> ⚠️ **No QA Plan**: This sprint was started without a QA plan. Run `/qa-plan sprint`
> before the last story is implemented. The Production → Polish gate requires a QA
> sign-off report, which requires a QA plan.

QA plan: run `/qa-plan sprint` before implementation begins (non-interactive run — the planner could not launch it). All eight planned stories (five Must, one Should, two Nice) are Logic or Integration; each already names its unit/integration tests in its ACs, so the QA plan can be written from the story files alone.

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
3. ~~`/story-readiness production/epics/player-character/story-010-golden-transform-hash-p2-1.md`, then `/dev-story` on it.~~ Done: player-character 010 Complete, PR #50 (2026-10-28).
4. `/sprint-status` mid-week; `/sprint-plan update` now that `content-data/018` has landed (Complete 2026-10-28, PR #53) and freed `content-data/019`.
5. Readiness done 2026-10-28 (docs PR `docs/md005-cd019-readiness`): `mail-destinations/005` (S001-003, full pass, Ready) and `content-data/019` (full pass, Ready; not in this sprint — next pull). `player-character/015` (S001-002) is unblocked (TD-175 approved). `grid-inventory/020` (S001-005) still needs its full `/story-readiness` pass before `/dev-story`.
