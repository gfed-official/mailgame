# Story 003: mailIds size-class arena — oldest-first take, sorted-union merge, exhaustion

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-003` (ascending `mailIds[]`), `TR-inventory-005` (take-k removes the lowest mailIds)
*(No `docs/architecture/tr-registry.yaml` exists yet; requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A, mirrored in the epic's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay
**ADR Decision Summary**: `mailIds` live in size classes of 1, 2, 4 … 256 `u32`s, one preallocated slab + free-index stack per class; a handle is `(class, block, start, count)`; take-k-oldest advances `start` (O(1), R6); a merge is a two-pointer sorted union into a block of the target class; a block whose live count falls to ≤ ¼ of its class is re-homed on its next mutation; an empty class borrows from the next larger; if all larger are empty validation fails `capacity_exhausted` (nothing mutated) (ADR-0009 §5).
**ADR Version**: ADR-0009 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: storage preallocated from `SimSettings` at world build; never `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any inventory tick path (ADR-0009 §5, Implementation Guidelines)
- Forbidden: a binary heap for the Intake outfeed, a timing wheel for WorldItem/bag/water expiries, a per-entry `List<uint>` for `mailIds` (ADR-0009 Alts 3–5)
- Required: capacities sized so the H-R19-1 fuzz fixture peaks below 50 % of each class (asserted by a test) (ADR-0009 §5)
- Required (Foundation, ADR-0006): integer-only; order by raw id `Value`; never `GetHashCode` for ordering; 0 B allocation per tick after warm-up
- Guardrail: `EntryRecord` ≤ 40 B; total inventory heap inside H-PERF-5's 1 MB with 512 WorldItems (advisory until perf baseline)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R6-1 a/b (arena form)** — GIVEN A = `[4, 9, 15, 21, 30]`: take-2 → `[4, 9]` returned, A = `[15, 21, 30]`; union of A with B = `[2, 50]` → `[2, 4, 9, 15, 21, 30, 50]`; every result ascending with no duplicate.
- [ ] Take-k-oldest is O(1) (advances `start`, no copy); a union allocates exactly one block of the result's size class and frees both inputs; re-homing triggers only when live ≤ ¼ of the class, and a block oscillating ±1 around the threshold re-homes at most once per crossing (no thrash).
- [ ] With every larger class exhausted, a request that needs a new block returns `capacity_exhausted` (reason 85) to the validator and nothing is mutated; borrowing from a larger class is used before that.
- [ ] A seeded property test (10 000 random take/union/append sequences, units up to 255 per handle) keeps every handle's contents equal to a reference sorted list, and frees every block when all handles are released (no leak).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Storage/MailIdArena.cs`.
- Class capacities are `InventorySettings` fields (story 001), PROPOSED; story 018's fuzz asserts the < 50 % peak and retunes them.
- The arena exposes only span reads (`ReadOnlySpan<uint>`) to callers; `MailId` structs wrap at the API boundary (ids cross system boundaries as `MailId`, ADR-0004).
- `capacity_exhausted` is checked in `Validate` (dry-run reservation), never discovered in `Commit` (Commit never fails).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 005: wiring the arena into mutator primitives and `Inserted`/`Merged` mailId runs.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/MailIdArenaTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 005, Story 008
