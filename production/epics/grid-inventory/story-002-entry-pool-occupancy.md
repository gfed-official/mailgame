# Story 002: Entry pool, occupancy bitmaps and entry identity

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-17

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-003` (entry: u32 entryId, u8 count, footprint w×h, rotation)
*(No `docs/architecture/tr-registry.yaml` exists yet; requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A, mirrored in the epic's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0004: Identity and Id Spaces; ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay
**ADR Decision Summary**: A global `EntryRecord` struct pool (≤ 40 B, free-slot stack, `entryId → slot` map; slots reused, ids never); per container one `ulong` occupancy bitmap per row (row-major, bit = column; v1 width ≤ 20) plus a `cellOwner` u16 array (`0xFFFF` free) for O(1) "what is under this cell" (ADR-0009 §5). `EntryId` is u32, run-unique, never reused; temp ids are bit-31 (ADR-0004).
**ADR Version**: ADR-0009 2026-10-16; ADR-0004 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: storage preallocated from `SimSettings` at world build; never `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any inventory tick path (ADR-0009 §5, Implementation Guidelines)
- Required: id wrapper structs (`EntryId`, `MailId`, `EntityId`, `PlayerSlot`, `DefIndex`) on every API crossing a system boundary; ids never reused (ADR-0004)
- Required: real `EntryId`s keep bit 31 clear; temp ids `0x8000_0000 | slot << 20 | ctr` (ADR-0004 §1)
- Forbidden: reusing, wrapping or skipping ids; free lists of ids (slots may be reused, ids never) (ADR-0004 Alt 2)
- Required (Foundation, ADR-0006): integer-only; order by raw id `Value`; never `GetHashCode` for ordering; 0 B allocation per tick after warm-up
- Guardrail: `EntryRecord` ≤ 40 B; total inventory heap inside H-PERF-5's 1 MB with 512 WorldItems (advisory until perf baseline)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-R2-1 (primitive form)** — GIVEN the entry allocator's last id is 41, WHEN an entry is created (42), moved between two containers via mutator primitives, its count driven to 0 (removed in the same call), and a second entry is created, THEN 42 travelled with the entry, no 0-count entry exists after any call, the second is 43, and 42 is never reissued across 10 000 further allocations.
- [x] **H-R2-2** — GIVEN a 2×2 footprint placed with `rotated = true`, WHEN stored, THEN `rotated = false` (square footprints canonicalise) and it occupies (0,0),(1,0),(0,1),(1,1); a 1×2 placed rotated occupies 2×1.
- [x] Footprint test is `h` mask-and-compares on the row bitmaps; `cellOwner` returns the entry slot for every covered cell and `0xFFFF` for free cells; a property test over 10 000 random place/remove sequences on 8×2, 8×4 and 20×16 grids keeps bitmaps, `cellOwner` and a brute-force cell grid identical.
- [x] `EntryRecord` layout is ≤ 40 B (`Unsafe.SizeOf` test) and holds `entryId, container slot, x, y, w, h, rotated, count (u8), isMail, key (mailKind u16, addressId u32, flags u8 | itemId), arrivalSeq u32, mailIds handle`; `1 ≤ count ≤ 255` is asserted on every write (`INVENTORY_ASSERT`).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Storage/` — `EntryRecord.cs`, `EntryPool.cs`, `Occupancy.cs`, `IdMap.cs` (preallocated open-addressed integer map, never enumerated — reused by later stories).
- Entry id allocation is inventory-owned (`EntryId` struct from `production/epics/simulation-core/story-002-id-value-types.md`); exhaustion behaviour follows ADR-0004 §3 (test before increment, return None + `IdExhausted` record + assert; caller refuses).
- Temp-id mapping (`Split`'s `tempId`, per-slot 32-entry ring) is ADR-0009 §5 but its only producer (`Split`) is M1 — not built here.
- These are the lowest primitives; `InventoryMutator` (story 005) is their only caller outside tests.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 003: the `mailIds` arena.
- Story 005: `InventoryMutator`, versions, events.
- M1: temp-id ring (Split).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/EntryStorageTests.cs` — must exist and pass

**Status**: [x] `tests/unit/inventory/EntryStorageTests.cs` (EntryStorageTests 17 methods incl. 2 theories × 3 grids, EntryStorageAllocationTests 1 NonParallel 0 B test, Release-only) + storage wiring test in `tests/integration/inventory/ContainerCatalogueTests.cs`

---

## Dependencies

- Depends on: Story 001
- Depends on: `production/epics/simulation-core/story-002-id-value-types.md` (`EntryId`)
- Unlocks: Story 003, Story 005, Story 006

---

## Completion Notes
**Completed**: 2026-10-17
**Criteria**: 4/4 passing (none deferred)
**Deviations**: (a) `EntryRecord` is 32 B (Sequential), `Count` defaults to 1 on allocation; `SetCount(slot, 0)` asserts under `INVENTORY_ASSERT` and writes 0 in Release — "a 0-count entry is removed in the same call" is `InventoryMutator`'s rule (story 005, carried as an AC in TD-099); (b) `MailIdsHandle` is a `uint Raw` placeholder until story 003 packs `(class, block, start, count)`; (c) `Occupancy` is a global per-container-slot pool sized `containerCapacity × inventory.maxRows × widestDef` (not per container), reset on container creation; every def is validated ≤ 64 columns at build (`SimBuildErrorCode.InventoryDefInvalid = 52`); `inventory.entryPoolCapacity` bounded to ≤ 65 535 in `InventorySettings.FromBalance`; (d) `IdMap` (open-addressed, backward-shift deletion) refuses to fill past `TableSize − 1` so a miss always terminates — found by the tests; (e) appended `CheckCode.InventoryEntryPoolExhausted = 11`, `SimAssertCode` 57–60. Carried: TD-099.
**Test Evidence**: Logic: `tests/unit/inventory/EntryStorageTests.cs` (18 methods incl. the NonParallel 0 B test; H-R2-1/H-R2-2 traits; 10 000-step property test per grid against a brute-force cell grid; assert-path tests for `InventoryEntrySlotFree`/`InventoryCellConflict`/`InventoryFootprintOutOfBounds`) + `Storage_IsSizedFromTheSettings_AndEveryCreatedContainerStartsWithAnEmptyGrid` in ContainerCatalogueTests. Inventory namespace 47/0 on Release and Debug; four full legs 0 inventory failures (the 9 failures on each leg are content-data's in-flight `tests/unit/content` RuleRegistry/PlayerRules/DecimalToScaled tests — foreign); Release build 0 warnings.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (6 low; fixed in-story: entryPoolCapacity bound, Occupancy sized by widest def + all-defs width check with a content-flavoured error code; rest → TD-099). QL-TEST-COVERAGE GAPS (5; closed in-story: gaps 1–3; 4–5 → TD-099).
**Run result**: N/A — Godot-free storage primitives; nothing observable until the inventory panel (stories 023/024).
