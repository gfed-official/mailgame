# Story 009: AutoPlace (passes M/U/R, PassFirst/TargetFirst), StackKey non-full index and negative-fit cache

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-007` (AutoPlace passes; amortised O(1) non-full-stack index)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay
**ADR Decision Summary**: AutoPlace runs PASS M (merge into non-full stacks of the same StackKey, row-major), PASS U (unrotated first-fit, row-major scan; slots: lowest free usable slot ≥ 1), PASS R (rotated, only if w ≠ h); `PassFirst` runs each pass over all targets, `TargetFirst` (mail catch / pickup) runs M→U→R per target. Per container an open-addressed `StackKey → head` map of intrusive non-full lists kept in row-major order, and a negative-fit cache `(shape, rotated) → versionNoFit` (ADR-0009 §5; GDD R8).
**ADR Version**: ADR-0009 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: storage preallocated from `SimSettings` at world build; never `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any inventory tick path (ADR-0009 §5, Implementation Guidelines)
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Guardrail: probe counters compiled into Release, one increment per cell probe or candidate visit; `PeekOutfeed`/`TakeUnit` ≤ 4 candidates amortised; cap eviction ≤ created + 1 (BLOCKING in CI)
- Required (Foundation, ADR-0006): integer-only; order by raw id `Value`; never `GetHashCode` for ordering; 0 B allocation per tick after warm-up

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R8-1** — targets `[inventory, second grid]`; inventory has 19 letters to #2 at (5,1) and free (0,0); the second grid has 10 letters to #2 at (0,0); AutoPlacing 3 letters to #2 → the lowest id merges into the inventory stack (20), 2 merge into the second stack (12), and **no** new entry is created at inventory (0,0).
- [ ] **H-R8-2 (fixture containers)** — targets `[8×2 grid (empty), 8×4 grid with a free 2×4 column at (0,0)]`: a Large lands in the 8×4 grid at (0,0) **unrotated** (pass U over all targets precedes pass R); with that grid removed → the 8×2 grid at (0,0) `rotated = true`; both full → `no_room`, no version change; a mail-only target and a Plank → `not_allowed`; a mail-only target with no space and a letter → `no_room`. (bike/chest are fixture `ContainerDef`s; their content is M-1b.)
- [ ] `MergePolicy.NoMerge` skips pass M; `effMax == 1` skips pass M; `TargetFirst` fills the first target (M→U→R) before the next.
- [ ] The StackKey index stays equal to a brute-force scan of non-full stacks across 10 000 random mutator calls (property test); PASS M probes are ≤ list length (1–3 typical) per target, never a container scan (probe counter).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Placement/AutoPlace.cs`, `StackIndex.cs`, `NegativeFitCache.cs`.
- The index is maintained inside `InventoryMutator` on insert, remove, count change and move (ADR-0009 §5) — not by AutoPlace itself.
- AutoPlace returns a plan (per-target placements + merged unit counts) consumed by `Commit`; it never mutates. Partial placement is allowed for stackables; non-stackables are all-or-nothing.
- Per-shape monotone start hints (D3) are ADR-0025's (sort/backlog, M1) — not needed for AutoPlace correctness.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 010: R9 target orders.
- Story 014: `TargetFirst` callers (pickup, catch).
- M1/ADR-0025: sort hints.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/AutoPlaceTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005, Story 006
- Unlocks: Story 010, Story 012, Story 014, Story 015
