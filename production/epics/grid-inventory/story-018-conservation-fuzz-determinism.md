# Story 018: Conservation fuzz over the M-1a op set (H-R19-1 subset) and H-DET-1 two-world determinism

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

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

- [ ] **H-R19-1 (M-1a subset)** — a seeded Sim with 8 simulated slots, the Intake and 2 fixture external containers plus each panel and cursor, pre-populated with 400 mail units across 60 addresses (3 kinds, 10 % Cursed) and 200 item units; 18 000 ticks with 4 random ops per slot per tick from the **M-1a set** {pick, move (whole / split-and-place / swap), merge, quick_move, return, open/close via the internal open-set API, spawn into the Intake, WorldItem create (drop/throw-land stand-in incl. water) and expiry, pickup core, TryCatch, ConsumeUnit, DetachForThrow, malformed ops}, ≥ 50 % targeting one shared container; separately named assertions after every op (O(1) counters) and every 100 ops (full check): **a** conservation-mail, **b** conservation-items, **c** bounds `1 ≤ count ≤ effMax`, **d** no-overlap, **e** in-bounds, **h** index consistency (`PeekOutfeed`, `CountItem`, StackKey index vs brute force), **i** coverage ≥ 5 % accepted per well-formed op type. (f/g sort invariants and the take_all/split/sort/bag/disconnect ops join at M1/M-1b/M0.)
- [ ] Budget: fixed seed in CI (advisory ≤ 60 s wall); failures print seed, op index and invariant letter; the arena classes peak < 50 % on this fixture (ADR-0009 §5 assertion, retune story 003's PROPOSED capacities if needed).
- [ ] **H-DET-1** — two Sim instances from the same content and seed apply the same 50 000-op log (from the fuzz generator); after each op (one op per tick) the SHA-256 of the canonical state dump (inventory section + Sim Core sections) is equal; a third world with another seed diverges.

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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007–017
- Depends on: `production/epics/simulation-core/story-019-canonical-state-dump.md`, `production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md`
- Unlocks: Epic DoD (R19, H-DET-1)
