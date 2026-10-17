# Story 006: Placement validity, StackKey stacking with effMax at op time, and the insertion whitelist

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-004` (StackKey; `effMax` at op time; ≤ 255), `TR-inventory-006` (fixed precedence chain)
*(No `docs/architecture/tr-registry.yaml` exists yet; requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A, mirrored in the epic's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0010: Content Format, Loading, ContentSet and StatSheet
**ADR Decision Summary**: Validation is one ordered chain per op kind (placement: `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`); `effMax = IStatSheet.Read(MailMaxStack, kind)` for mail (item `maxStack` for items), read at op time (ADR-0009 §2; ADR-0010 §7).
**ADR Version**: ADR-0009 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required: reason precedence is one ordered chain per op kind, verbatim from the GDD (placement: `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`) (ADR-0009 §2)
- Required (Feature/Core, ADR-0010): read stats only through `IStatSheet.Read`; discrete ops read once at op time
- Required (Foundation, ADR-0006): integer-only; order by raw id `Value`; never `GetHashCode` for ordering; 0 B allocation per tick after warm-up

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R3-1** — StackKey = `(mailKind, addressId, flags)` for mail (Cursed in the key, spawn shift not) and `itemId` for items: 3 letters to #17 (shift 2) onto 5 letters to #17 (shift 1) are mergeable; 3 **Cursed** letters to #17 onto them → `blocked`.
- [ ] **H-R3-2** — with A = 20 letters (effMax 20) a merge of 20 more → `stack_full`; WHEN a synthetic team `MailMaxStack` Mul 2000 source (Bulk Mailer shape) is pushed to the sheet and retried → effMax 40 and the merge fits; two Small packages under the same source → `blocked` (effMax stays 1).
- [ ] **H-R4-1 (validate form; shared fixture 8×4 grid with a Medium at (6,2) and 4 letters to #3 at (0,0))** — a: 2 letters to #3 at (0,0) → merge target; b: Large at (7,0) → `out_of_bounds`; c: Large at (5,0) → `blocked`; d: Large at (5,1) → `out_of_bounds` (bounds before overlap); e: Small rotated at (0,0) → `blocked`; f: with Log ×3 at (2,0), Log ×2 at (2,1) (partial overlap of a mergeable stack) → `blocked`.
- [ ] **Precedence test (ADR-0009 Implementation Guidelines)** — a parametrised test sends, for each step of the placement chain, a request violating that step **and every later one** and asserts the earliest code; `reserved_slot` (hotbar index 0) precedes `out_of_bounds`; `not_carryable` precedes `not_allowed`.
- [ ] **H-R5-1 (validate form)** — `allowedCategories` is checked on every insertion path through one shared predicate: a Plank into a mail-only container → `not_allowed`; a letter into `po_intake` passes the category check; a `handCarried` container rejects `carryByHand:false` or `bulk` with `not_carryable` (dormant in v1 — fixture item).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Rules/` — `Placement.cs` (pure functions over `Occupancy` + `cellOwner` returning a reason or a placement result into `InvPlan`), `StackKey.cs` (packed u64: kind u16 | addressId u32 | flags u8, or item flag | itemId), `Whitelist.cs`.
- Merge target rule (R4): every covered cell inside the grid and either free, or all covered by **exactly one** mergeable stack whose placement the footprint matches exactly; a partial overlap with a mergeable stack is `blocked`; a whole in-container move treats the mover's own old cells as free, a split-and-place does not.
- `effMax` for items = `ItemDef.MaxStack`; for mail `IStatSheet.Read(Stat.MailMaxStack, kindDefIndex)` — read in `Validate`, never cached across ticks (CD20).
- Addresses (`addressId`) are Mail's (ADR-0017, not written): treat `addressId` as an opaque u32 in the key; tests use literal values.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007/008: the handlers that call these rules.
- Story 009: AutoPlace's own pass logic.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/PlacementRulesTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002, Story 004 (reasons)
- Depends on: `production/epics/content-data/story-014-statsheet-build-read-explain.md` (`IStatSheet.Read`)
- Unlocks: Story 007, Story 008, Story 009
