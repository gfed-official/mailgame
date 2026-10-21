# Story 006: Placement validity, StackKey stacking with effMax at op time, and the insertion whitelist

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-21 (readiness fixes, docs-only: manifest re-stamp (no Inventory/Core rule changed since 2026-10-16); citations checked against main c7fa0f6; ADR-0017 is Accepted; type names avoid story 009's `Inventory/Placement/` namespace; H-R3-2 / H-R4-1 / H-R5-1 fixtures made concrete from `BuiltInContent`; precedence test split rule-level (here) / handler-level (story 007); `acceptsInsert`, zero-alloc and test-evidence lines added)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-004` (StackKey; `effMax` at op time; ≤ 255), `TR-inventory-006` (fixed precedence chain)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table). GDD rules: R3 Stacking, R4 Placement validity, R5 Insertion whitelist, R1 `reservedSlots` / `acceptsInsert`; R7 merge row ("Full target → `stack_full`"); tests H-R3-1/2, H-R4-1, H-R5-1.)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0010: Content Format, Loading, ContentSet and StatSheet
**ADR Decision Summary**: Validation is one ordered chain per op kind (placement: `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`); `effMax = IStatSheet.Read(MailMaxStack, kind)` for mail (item `maxStack` for items), read at op time (ADR-0009 §2; ADR-0010 §7).
**ADR Version**: ADR-0009 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required: reason precedence is one ordered chain per op kind, verbatim from the GDD (placement: `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`) (ADR-0009 §2)
- Required (Feature/Core, ADR-0010): read stats only through `IStatSheet.Read`; discrete ops read once at op time
- Required (Core, ADR-0009): rules are pure; only `InventoryMutator` writes container, entry or ledger state; Inventory never calls Mail (ADR-0017)
- Required (Foundation, ADR-0006): integer-only; order by raw id `Value`; never `GetHashCode` for ordering; 0 B allocation per tick after warm-up

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R3-1** — StackKey = `(mailKind, addressId, flags)` for mail (Cursed in the key, spawn shift not) and `itemId` for items: 3 letters to #17 (shift 2) onto 5 letters to #17 (shift 1) are mergeable; 3 **Cursed** letters to #17 onto them → `blocked`.
- [ ] **H-R3-2** — with A = 20 letters (effMax 20) a merge of 20 more → `stack_full`; WHEN a synthetic team `MailMaxStack` Mul 2000 source (Bulk Mailer shape: GDD R3 "Bulk Mailer doubles postcard/letter", so the modifier is qualified to the letter and postcard kind ordinals) is pushed through `StatSheetWriter.Push(in ModifierSource, ReadOnlySpan<Modifier>)` and retried → effMax 40 and the merge fits; two Small packages under the same source → `blocked` (Small `MaxStack` = 1, so effMax stays 1 and `effMax > 1` fails — GDD R3). Test-only push (production pushes come only from Perks/Stamps); reuse `GridMailRuleFixtures.DoubleStackPm` (2 000) from `tests/unit/content/GridMailRuleFixtures.cs`.
- [ ] **H-R4-1 (validate form; shared fixture 8×4 grid with a Medium at (6,2) and 4 letters to #3 at (0,0))** — a: 2 letters to #3 at (0,0) → merge target; b: Large at (7,0) → `out_of_bounds`; c: Large at (5,0) → `blocked`; d: Large at (5,1) → `out_of_bounds` (bounds before overlap); e: Small rotated at (0,0) → `blocked`; f: with Log ×3 at (2,0), Log ×2 at (2,1) (partial overlap of a mergeable stack) → `blocked`. *(Fixture: no 8×4 container def exists at M-1a; dims are instance state, so create an 8×4 `ContainerLayout.Grid` instance with `ContainerTable.TryCreate(def, 8, 4, ContainerLayout.Grid, ownerSlot, out id)` from the unrestricted `player_inventory` def. `BuiltInContent` footprints: letter 1×1 max 20, small 1×2, medium 2×2, large 2×4, log 1×2 max 10, plank 1×1 max 20. Put the fixture in `tests/unit/inventory/PlacementFixture.cs` so story 007's op-form H-R4-1 reuses it.)*
- [ ] **Precedence test, rule-level suffix (ADR-0009 Implementation Guidelines)** — a parametrised test over the placement rules sends, for each step of `reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`, a placement violating that step **and every later one** and asserts the earliest code; `reserved_slot` (a `ContainerDef.ReservedSlots` index — hotbar index 0) precedes `out_of_bounds`; `not_carryable` precedes `not_allowed`. The prefix `bad_request → not_open → stale_entry → stale_layout` needs a request, the open set and the entry table, so it is handler-level: story 007 owns the full-chain parametrised test (AC added there 2026-10-21).
- [ ] **Edge rows (PR #15 review advisory A4; GDD R2–R4)** — in `PlacementRulesTests`: (1) a `MailMaxStack` Mul large enough to exceed 255 reads effMax 255 (`StatDefs` clamp 1..255; entry `count` is `uint8`); (2) a mergeable, **non-full** target returns the merge-into verdict (how many units move is story 008's); (3) a whole in-container move treats the mover's own old cells as free while a split-and-place (`count < total`) does not (R4); (4) item StackKey: two Planks merge, a Plank onto a Log → `blocked` (key = `itemId`).
- [ ] **H-R5-1 (validate form)** — `allowedCategories` is checked on every insertion path through one shared predicate: a Plank into `po_intake` (the only mail-only def at M-1a: `HasAllowedCategories`, `AllowedCategories = [Mail]`) → `not_allowed`; a letter into `po_intake` passes the category check; a Plank into `player_inventory` passes; a `HandCarried` container (hotbar, `player_inventory`, cursor) rejects a def with `CarryByHand = false` or `WeightClass.Bulk` with `not_carryable` (dormant in v1 — no M-1a def has either, so build a fixture `ItemDef`/`MailKindDef`); a container def with `AcceptsInsert = false` rejects every insertion with `not_allowed` (GDD R1; fixture def — none at M-1a). The GDD's ×8-path matrix (`mail_truck_cargo`, `po_depot`) is exercised by the consuming handler stories (007–010, 012, 014, 015); this story delivers the single shared predicate they all call.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Rules/` (new folder; namespace `PerformativeMail.Sim.Inventory.Rules`) — `PlacementRules.cs` (static class: pure functions over `Occupancy` + the cell owner returning a reason or a placement verdict the handler copies into `InvPlan`), `StackKey.cs` (packed u64: kind u16 | addressId u32 | flags u8, or item flag | itemId), `InsertionWhitelist.cs`. Do **not** name a type `Placement`: story 009 creates `Inventory/Placement/` (namespace `PerformativeMail.Sim.Inventory.Placement`), which would shadow it (CS0234 — the TD-074/TD-107 pattern).
- Result shape: `InvPlacement` (`src/PerformativeMail.Sim/Inventory/InvPlan.cs`) holds only entry/container slot, X, Y, rotation; carry the verdict (reason 0 = ok, free vs merge-into, target entry slot) in a small readonly struct returned by `PlacementRules` rather than widening `InvPlan` here. Swap (slots layout, cursor source) is story 008's.
- Mergeable (GDD R3): equal StackKey **and** `effMax > 1` **and** the target container's category check admits the entry. A mergeable target that is already full (`count == effMax`) → `stack_full` (GDD R7 merge row; H-R3-2; H-R4 swap case "full same-StackKey slot → `stack_full`, no swap"). How many units a non-full target takes (`min(count, effMax − target.count)`) is the handler's (story 008 merge, R6/R7).
- Merge target rule (R4): every covered cell inside the grid and either free, or all covered by **exactly one** mergeable stack whose placement the footprint matches exactly; a partial overlap with a mergeable stack is `blocked`; a whole in-container move treats the mover's own old cells as free, a split-and-place does not.
- `effMax` for items = `ItemDef.MaxStack`; for mail `IStatSheet.Read(Stat.MailMaxStack, qualifier: kindDefIndex.Value)` (`src/PerformativeMail.Sim/Content/Stats/IStatSheet.cs`; `StatDefs`: Mul, Floor, clamp 1..255) — read in `Validate`, never cached across ticks (CD20).
- `addressId` is the opaque u32 `EntryRecord.AddressId` (packed with `AddressId.Pack`; the `AddressId` type lives in `PerformativeMail.Sim.Primitives`, ADR-0017 Accepted); the key's flags byte is `EntryRecord.Flags`, compared raw. Tests set Cursed with `(byte)MailFlags.Cursed` (= 1; namespace `PerformativeMail.Sim.MailDestinations`, `src/PerformativeMail.Sim/Mail/MailFlags.cs`) and literal addresses. Inventory never calls Mail.
- Footprints: use `Footprint.Effective` / `Footprint.Canonicalise` (`Inventory/Storage/EntryRecord.cs`) and `Occupancy` (`Inventory/Storage/Occupancy.cs`); reason codes from `Inventory/Ops/InvReason.cs` (story 004: `blocked` 71, `out_of_bounds` 72, `not_allowed` 73, `not_carryable` 74, `stack_full` 76, `reserved_slot` 80).
- Performance: the rules, `StackKey` and the whitelist allocate 0 B (assert with `tests/PerformativeMail.Sim.Tests/AllocAssert.cs` in a `[Collection(NonParallelCollection.Name)]` class) and cost O(footprint rows) per test (ADR-0009 Performance Implications).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007/008: the handlers that call these rules.
- Story 009: AutoPlace's own pass logic.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY not run; `/story-readiness` 2026-10-21, report-only, two passes: READY after the doc fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/PlacementRulesTests.cs` (namespace `PerformativeMail.Sim.Tests.Unit.Inventory`) plus the shared fixture `tests/unit/inventory/PlacementFixture.cs` (reused by story 007) — must exist and pass on all four legs (net10/net8 × Debug/Release); the 0-B test may skip only where the owner skip rule allows (decision log 2026-10-19)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (`ContainerTable`, `ContainerDef` lookup), Story 002 (`EntryRecord`, `Occupancy`, `Footprint`), Story 004 (`InvReason` 64–85), Story 005 (`InvPlan` / `InvPlacement`) — all Complete
- Depends on: `production/epics/content-data/story-014-statsheet-build-read-explain.md` (`IStatSheet.Read`) — Complete
- Unlocks: Story 007, Story 008, Story 009
