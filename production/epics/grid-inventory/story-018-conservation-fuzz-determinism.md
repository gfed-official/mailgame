# Story 018: Conservation fuzz over the M-1a op set (H-R19-1 subset) and H-DET-1 two-world determinism

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-11-01 (readiness doc fixes, TD-204 approved by Evan 2026-11-01 10:11 AM PT; manifest re-stamped 2026-10-16 → 2026-10-17.1 — the .2 revision adds ADR-0012–0017/0019/0026 rules and the .1 revision the input-adapter and `WorldTablesBuilder` rules, none concerning this test-only inventory story)

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

- [ ] **H-R19-1 (M-1a subset)** — a seeded Sim with 8 simulated slots, the Intake and 2 fixture external containers plus each panel and cursor (the two external containers are created with the internal `TryCreateContainer`, `src/PerformativeMail.Sim/Inventory/InventorySystem.cs:561`, visible to the test assembly, from an existing M-1a container def that accepts both mail and items; if no shipped def does — M-1a `BuiltInContent` defines only `hotbar`, `player_inventory`, `po_intake` and `cursor`, `src/PerformativeMail.Sim/Content/BuiltInContent.cs:83-100` — the test builds a content set with a chest def through `ContentLoader` over an in-memory source; Mail Lead planning default, approved by Evan 2026-11-01 10:11 AM PT (TD-204 item 8)), pre-populated with 400 mail units across 60 addresses (3 kinds, 10 % Cursed) and 200 item units; 18 000 ticks with 4 random ops per slot per tick from the **M-1a set** {pick, move (whole / split-and-place / swap), merge, quick_move, return, open/close via the internal open-set API, spawn into the Intake, WorldItem create (drop/throw-land stand-in incl. water) and expiry, pickup core, TryCatch, ConsumeUnit, DetachForThrow, malformed ops}, ≥ 50 % targeting one shared container; separately named assertions after every op (O(1) counters) and every 100 ops (full check): **a** conservation-mail, **b** conservation-items, **c** bounds `1 ≤ count ≤ effMax`, **d** no-overlap, **e** in-bounds, **h** index consistency (`PeekOutfeed`, `CountItem`, StackKey index vs brute force), **i** coverage ≥ 5 % accepted per well-formed op type. (f/g sort invariants and the take_all/split/sort/bag/disconnect ops join at M1/M-1b/M0.)
- [ ] Budget: fixed seed in CI (advisory ≤ 60 s wall); failures print seed, op index and invariant letter; the arena classes peak < 50 % on this fixture (ADR-0009 §5 assertion, retune story 003's PROPOSED capacities if needed).
- [ ] **H-DET-1** — two Sim instances from the same content and seed apply the same 50 000-op log (from the fuzz generator); after each op (one op per tick) the SHA-256 of the whole canonical dump is equal (the shipped `DumpHash.Bytes` hashes every section, `tests/PerformativeMail.Sim.Tests/DumpHash.cs:14`; no section subset); a third world with another seed diverges. Known limit: the open set is not in the dump yet (TD-146, `src/PerformativeMail.Sim/Inventory/OpenSet.cs:20`), so an open-set divergence is invisible to H-DET-1 until TD-146 closes.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `tests/integration/inventory/ConservationFuzzTests.cs`, `InventoryDeterminismTests.cs`; generator in `tests/integration/inventory/Fuzz/` (PCG32 from `production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md`, shipped as `src/PerformativeMail.Sim/Primitives/Pcg32.cs`).
- Harness reuse (lean-testing, readiness 2026-11-01): H-DET-1 reuses the SC-DET-1 two-world harness (`DeterminismRun` in `tests/integration/sim/DeterminismT1Tests.cs`) and `DumpHash` rather than a second two-world runner; one op per tick for H-DET-1, 32 ops per tick (4 × 8 slots) for H-R19-1; players join through `ContainerCatalogueTests.JoinOp`'s shape (`tests/integration/inventory/ContainerCatalogueTests.cs:20`). The arena peak is already public as `MailIdArena.PeakLiveBlocks(int)` (`src/PerformativeMail.Sim/Inventory/Storage/MailIdArena.cs:118`). No hash constant is committed: the comparison is between live worlds.
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
- Integration: `tests/integration/inventory/ConservationFuzzTests.cs` (H-R19-1) and `tests/integration/inventory/InventoryDeterminismTests.cs` (H-DET-1) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007–017
- Depends on: `production/epics/simulation-core/story-019-canonical-state-dump.md`, `production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md`
- Unlocks: Epic DoD (R19, H-DET-1)
