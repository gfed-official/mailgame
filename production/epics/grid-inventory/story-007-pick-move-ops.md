# Story 007: Pick and Move ops — whole-entry moves, cross-container atomicity, no-op moves

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: 2026-10-21 (docs-only: full-chain placement precedence AC moved here from story 006; status unchanged — still waits on the TD-105 item 3 ruling)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-001` (ops through the one FIFO, phase 0), `TR-inventory-002`, `TR-inventory-006`
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0007: Op Queue, Drain and Replies
**ADR Decision Summary**: Each op is a handler `Apply = Validate → Commit → Reply` drained in phase 0; cross-container moves are all-or-nothing (destination fit computed in `Validate`, committed unchanged); a move onto the entry's own cells with the same rotation is a no-op (`ok:true`, no bump, no event) (ADR-0009 §2–§3; ADR-0007 validate-then-mutate).
**ADR Version**: ADR-0009 2026-10-16; ADR-0007 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required: reason precedence is one ordered chain per op kind, verbatim from the GDD (placement: `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`) (ADR-0009 §2)
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R1-2** — GIVEN an empty hotbar and an Axe on the cursor, WHEN `move {to: hotbar, x: 0}` → `reserved_slot`, Axe stays on the cursor, hotbar version unchanged; `move {x: 1}` → `ok`, Axe at slot 1.
- [ ] **Pick** — `pick {from, entryId}` lifts the whole entry onto an **empty** cursor (`cursor_busy` otherwise, cursor and source unchanged); picking an entry outside the actor's open set → `not_open`.
- [ ] **H-R4-1 a–f (op form)** — the story 006 fixture driven through `move` from the cursor: a → stack 6, cursor empty, version +1; b–f → their reasons; every rejection leaves the container version and the cursor unchanged.
- [ ] **H-R4-2 a/b** — a: a Large at (0,0) moved to (1,0) (overlapping its own cells) → `ok`, version +1; b: a cross-container move of a Medium onto an inventory cell partly covered by an Axe → `blocked`, entry unmoved, **both** versions unchanged.
- [ ] **H-R4-3 a/b** — a: a drag-place of 2 letters to #3 onto a free cell never merges (new entry at (4,2), the existing stack of 5 unchanged, one bump); b: `move {A → its own (0,0), rotated: false}` → `ok:true`, no version bump, no event.
- [ ] **Precedence test, full placement chain (ADR-0009 Implementation Guidelines; moved from story 006 on 2026-10-21)** — a parametrised test sends a `move`/`place` request that violates each step of `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full` **and every later one** and asserts the earliest code (story 006 covers the rule-level suffix from `reserved_slot`).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Ops/PickHandler.cs`, `MoveHandler.cs`.
- `Move` with `count = 0` (whole) is this story; `count < total` (split-and-place) and the hotbar swap are story 008 — keep one `MoveHandler` and add those branches there.
- Positional ops carry `baseVersion`; `stale_layout` iff `baseVersion < lastReplaceVersion` of the destination. `lastReplaceVersion` only changes on `sort` (M1), so at M-1a the check is present but never fires — test it by setting `lastReplaceVersion` through the test seam.
- Source resolution order: id → live entry; not live → tombstone lookup (`container_gone`, story 013) → `stale_entry`.
- The open-set predicate comes from story 011 (`IsInOpenSet(actor, container)`); panel containers and the cursor are always in their owner's set.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 008: split-and-place, hotbar swap, `Merge`.
- Story 010: `QuickMove`, `Return`.
- M1: `MoveGroup`, `Split`, `Sort`, `TakeAll`.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/PickMoveTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004, Story 005, Story 006, Story 011 (open-set predicate)
- Unlocks: Story 008, Story 010, Story 018
