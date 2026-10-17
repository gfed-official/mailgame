# Story 008: Move split-and-place, hotbar swap from the cursor, and the Merge op

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-004`, `TR-inventory-005` (oldest-first), `TR-inventory-002`
*(No `docs/architecture/tr-registry.yaml` exists yet; requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A, mirrored in the epic's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0004: Identity and Id Spaces
**ADR Decision Summary**: A `move` with `count < total` is an atomic split-and-place (the new piece gets a new entryId, the remainder keeps the old; the k **lowest** mailIds move); a `move` from the cursor onto an occupied, non-mergeable **slots** cell swaps (one op, one bump); `merge` moves `min(count, effMax − target.count)`; entry ids are never reused (ADR-0009; ADR-0004).
**ADR Version**: ADR-0009 2026-10-16; ADR-0004 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required: reason precedence is one ordered chain per op kind, verbatim from the GDD (placement: `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`) (ADR-0009 §2)
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)
- Required: id wrapper structs (`EntryId`, `MailId`, `EntityId`, `PlayerSlot`, `DefIndex`) on every API crossing a system boundary; ids never reused (ADR-0004)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R4-2 c/d** — c: A = 10 letters at (0,0), `move {A, count: 4 → (0,0)}` (split onto own cells) → `blocked`; d (swap): hotbar slot 1 = Axe, cursor = 3 letters, `move {cursor → hotbar, x: 1}` → `ok`, slot 1 = the letters, cursor = the Axe, one version bump; the same onto an occupied **grid** cell → `blocked` (no grid swap); slot 1 = 20 letters to #3 and the cursor 2 letters to #3 → `stack_full`, no swap.
- [ ] **Split-and-place** — `move {A (7 letters [ids 1..7]), count 3 → free cell}` → a new entry with a new entryId holding `[1,2,3]`, A keeps its id with `[4..7]`, one version bump; `count` ≥ total → `bad_count`; `count` > 255 decoded → `bad_request`.
- [ ] **H-R7-3** — T = 18 letters to #5, S = 5 to #5: `merge {S → T}` → T 20, S 3 in place, both versions +1 (same container: +1 once); again → `stack_full`; S' = 5 to #6 onto T → `blocked`.
- [ ] **H-R4-3 c** — `merge {A → A}` → `blocked`, A unchanged, version unchanged.
- [ ] **H-R6-1 b (op form)** — `merge {A [4,9,15,21,30] → B [2,50]}` → B `[2,4,9,15,21,30,50]`, A removed; every destination list ascending, no duplicate id.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: extend `MoveHandler.cs`; add `MergeHandler.cs`.
- Split-and-place uses the arena's take-k-oldest (story 003) and allocates the new entryId in `Commit` (never in `Validate` — ADR-0009 §2).
- Swap applies only to `layout = slots` (hotbar) with the source being the cursor; the displaced entry goes to the cursor; slot 0 is `reserved_slot` before anything else.
- The standalone `split` op (cursor-lift with temp id) is M1 (epic scope) — not this story.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- M1: `Split` op, temp ids, `MoveGroup`.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/MoveSplitMergeTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003, Story 007
- Unlocks: Story 010, Story 018
