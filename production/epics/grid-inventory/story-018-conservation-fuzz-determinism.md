# Story 018: Conservation fuzz over the M-1a op set (H-R19-1 subset) and H-DET-1 two-world determinism

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: 2026-11-01

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-012`, `TR-inventory-028` (SHA-256 state dump equal after a 50 000-op log on two Sim instances)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (primary); ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: Determinism tier T1: two worlds with the same `ContentSet` hash, `SimSettings`, `runSeed`, applied-op log and sample log give identical canonical dumps after every tick; SHA-256 of the dump lives in tests only (ADR-0006 §4–§5); the inventory invariants are checked by the O(1) ledger after every op and the full scan periodically (ADR-0009 §6).
**ADR Version**: ADR-0006 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required (Foundation, ADR-0006 §5): T1 harness — one op per tick where "after each op" is needed; no test seam in production code
- Required: PCG32 seeded generators, seed printed on failure (ADR-0005/0006)
- Guardrail: `EntryRecord` ≤ 40 B; total inventory heap inside H-PERF-5's 1 MB with 512 WorldItems (advisory until perf baseline)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-R19-1 (M-1a subset)** — a seeded Sim with 8 simulated slots, the Intake and 2 fixture external containers plus each panel and cursor, pre-populated with 400 mail units across 60 addresses (3 kinds, 10 % Cursed) and 200 item units; 18 000 ticks with 4 random ops per slot per tick from the **M-1a set** {pick, move (whole / split-and-place / swap), merge, quick_move, return, open/close via the internal open-set API, spawn into the Intake, WorldItem create (drop/throw-land stand-in incl. water) and expiry, pickup core, TryCatch, ConsumeUnit, DetachForThrow, malformed ops}, ≥ 50 % targeting one shared container; separately named assertions after every op (O(1) counters) and every 100 ops (full check): **a** conservation-mail, **b** conservation-items, **c** bounds `1 ≤ count ≤ effMax`, **d** no-overlap, **e** in-bounds, **h** index consistency (`PeekOutfeed`, `CountItem`, StackKey index vs brute force), **i** coverage ≥ 5 % accepted per well-formed op type. (f/g sort invariants and the take_all/split/sort/bag/disconnect ops join at M1/M-1b/M0.)
- [x] Budget: fixed seed in CI (advisory ≤ 60 s wall); failures print seed, op index and invariant letter; the arena classes peak < 50 % on this fixture (ADR-0009 §5 assertion, retune story 003's PROPOSED capacities if needed).
- [x] **H-DET-1** — two Sim instances from the same content and seed apply the same 50 000-op log (from the fuzz generator); after each op (one op per tick) the SHA-256 of the canonical state dump (inventory section + Sim Core sections) is equal; a third world with another seed diverges.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `tests/integration/inventory/ConservationFuzzTests.cs`, `InventoryDeterminismTests.cs`; generator in `tests/integration/inventory/Fuzz/` (PCG32 from `production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md`).
- Drive ops through `EnqueueOp` + `Tick()` (the real FIFO), automation-style calls (`IInventoryApi`) from a test system bound to a later phase — never by poking state.
- Players join through Sim Core's `PlayerJoin` System op.
- 20-seed nightly runs (H-R19-1 budget) are a CI-config follow-up for devops; not required to close this story.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- M1: f/g sort invariants, take_all, split.
- M-1b: death/bags, inserter TakeUnit/TryInsert, belts.
- M0: disconnect/reconnect, optimistic client (H-OPT-*, H-NET-*).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/inventory/ConservationFuzzTests.cs` — must exist and pass

**Status**: [x] Created — `tests/integration/inventory/ConservationFuzzTests.cs` (H-R19-1) and `tests/integration/inventory/InventoryDeterminismTests.cs` (H-DET-1), generator and fixture in `tests/integration/inventory/Fuzz/`; all pass on the four legs (net10.0/net8.0 × Debug/Release); Release runs the AC numbers, Debug a fifth (see Completion Notes)

---

## Dependencies

- Depends on: Story 007–017
- Depends on: `production/epics/simulation-core/story-019-canonical-state-dump.md`, `production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md`
- Unlocks: Epic DoD (R19, H-DET-1)

---

## Completion Notes

**Completed**: 2026-11-01 (branch `story/grid-inventory-018` off main 56c9446, code commit 3aa8d3e; main 70a5766 (PR #72) and main 8d0398f (PR #71) merged in). Built to the readiness wording in debfb77 (docs PR 73, stacked on PR 72), whose doc changes — including this file's re-stamped Manifest Version and the added `InventoryDeterminismTests.cs` evidence line — are not on this branch.
**Criteria**: 3/3 passing. No Sim code changed.
- **H-R19-1 (M-1a subset)** — `ConservationFuzzTests` over the class fixture `ConservationFuzzRun`: `FuzzRun_KeepsInvariantsAToEAndH_AfterEveryOpAndEveryHundredOps`, `FuzzRun_AcceptsAtLeastFivePercentOfEachWellFormedOpType_AndAimsHalfItsOpsAtTheSharedChest`. 8 players joined by `PlayerJoin` System ops (`ContainerCatalogueTests.JoinOp`'s shape), the Intake and the two fixture chests; 400 mail units (60 addresses; postcard / letter / small; addresses with `a % 10 == 9` Cursed, 40 units) spawned through `Spawn`, 200 item units caught into the panels with `TryCatch`; then 18 000 ticks × 4 ops × 8 slots (576 000 ops) through `EnqueueOp` + `Tick`. The `InventoryInvariantChecker` (the world's `IOpRecorder`, so it runs after each drained op inside the drain, and the automation stand-in's observer, after each call) checks the ledger rows against its own model after every op (a, b), the brute-force full check every 100 ops and at the end (a multiset of mailIds, b units per item def, c `1 ≤ count ≤ effMax`, d no-overlap against the occupancy grid and header counts, e in-bounds, h `PeekOutfeed` / `CountItem` / StackKey index vs brute force), the `ItemsLost` records against the ledger's destroyed units every tick, and coverage at the end (i: every well-formed type ≥ 5 % accepted — lowest Merge 7.5 %; 51.9 % of ops name the shared chest; 1 742 WorldItems expired; malformed ops all `bad_request`).
- **Budget** — fixed seeds (generator `0x0018F0225EED0001`, run `0x5EED0018`); every failure prints `seed=… op=… tick=… invariant <letter> (<name>)`, including failures raised inside a tick (Release's `Tick` swallows them, so the run loop re-raises the first). Run wall time (advisory ≤ 60 s): Release 1.5–2.4 s for 18 000 ticks; Debug 1.7–2.1 s for 3 600. `FuzzRun_KeepsEveryMailIdArenaClassBelowHalfItsBlocks`: observed peaks (Release, full run) class 0 268/3640 (7.4 %), 1 74/1820 (4.1 %), 2 75/910 (8.2 %), 3 44/455 (9.7 %), 4 15/227 (6.6 %), 5 4/113 (3.5 %), 6–8 0; story 003's capacities need no retune.
- **H-DET-1** — `InventoryDeterminismTests` over `InventoryDeterminismRun`: `TwoWorlds_SameContentSeedAndLog_HaveEqualWholeDumpHashAfterEveryOp`, `WorldWithAnotherRunSeed_DivergesAtTheFirstRngDraw`. World R records a 50 000-op log (one op per tick: joins, pre-population, then the generator) with `InMemoryRecorder`; A, B (same run seed) and C (another) are each built and replayed on their own thread through SC-DET-1's `DeterminismRun.ReplayInto` (extracted from `DeterminismRun.Replay` in test code; no second runner), which SHA-256s the whole `TryWriteDump` before the first tick and after every tick; the final hashes also match `DumpHash.Sha256`. No hash constant is committed. C diverges exactly after the first Land command, whose landing position is the world's first RNG draw. The log has accepted ops of every well-formed type. Known limit (TD-146): the open set is not in the dump.
**Fixture chests**: Mail Lead planning default, approved by Evan 2026-11-01 12:05 PM PT by card click (card t222s1054), in his words "Approve TD-204 items 1-8 exactly as spelled out in your message above" (TD-204 item 8) — no shipped M-1a def is a chest, so both are `TryCreateContainer` instances (8 × 6, no owner) of `player_inventory`, an existing M-1a def that accepts mail and items; one helper, `InventoryFuzzWorld.CreateFixtureExternals`.
**Mail Dev defaults pending Evan (TD-206)**: item units arranged through the story-017 Recount seam (no M-1a faucet); the automation set as a test-only command op stashed and executed in `MailStep`; c, d, e, h in the full check only (no O(1) counter at M-1a); a 600-unit spawn ceiling to keep the fixture's scale; the other-seed divergence through an RNG-drawn landing position; the Debug scaling below; and the chest shape (8 × 6, no owner; TD-206 item 7).
**Debug scaling**: Release runs the AC numbers. The unoptimised Debug legs (`#if DEBUG`; they also run the `INVENTORY_ASSERT` scan every tick) run a fifth — 3 600 fuzz ticks, a 10 000-op log — with the same seeds and checks, so the AC evidence runs on every CI leg (nothing Explicit or skipped). Class times (incl. runner start): fuzz Debug 3.5–5.5 s, Release 3.1–4.9 s; H-DET-1 Debug 10–15 s, Release 6–8 s. Full suite here: Debug net10.0 43 s, net8.0 53 s; Release net10.0 37 s, net8.0 37 s.
**Seen failing**: the checker was run against four seeded Sim bugs (with an earlier tuning of the op weights), each reverted: a consume not counted for mail (a, op 122), items not linked in the StackKey index (h, op 100), a one-column out-of-bounds placement (e, op 2 700), a partial overlap placed as free (d, op 400).
**Tests**: 5,796 per leg with main 8d0398f merged (5 new; main alone 5,791), 0 failed; skipped 66 Debug / 7 Release (the same skipped set as main). Before that merge: 5,776 per leg, skipped 65 Debug / 7 Release.
**Review**: a review subagent on Sonnet: nothing serious; applied — the first fuzz test no longer restates loop counts, the shared-chest metric reads a command's container only for Spawn / Open / Consume and never counts malformed ops, the checker sizes its per-container lists from the table capacity, and the tick-end mismatch letter comes from the newest ring record.
