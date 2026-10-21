# Story 005: M-1a def records — ItemDef, ContainerDef, MailKindDef, DestinationTypeDef, MailMixDef

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD6, CD7, CD10); field values belong to `design/gdd/grid-inventory.md` (R1), `design/gdd/mail-destinations.md` (R1, R3, F3), and the base schema is `design/spec/08-data-schemas.md` §2.1–2.3.
**Requirement**: `TR-content-010`, `TR-content-011`
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§3 Defs)
**ADR Decision Summary**:
- Defs are `sealed record class` types in `Sim/Content`, with init-only `int`/`long`/`ushort`/`bool`/enum members. Small leaves may be `readonly record struct`, and lists are `ImmutableArray<T>`.
- No `T[]`, `float`, `double`, `decimal` or `Half` anywhere; a reflection test checks this.
- Defs are identified by `DefIndex`, never by record equality.
- Defaults are always neutral. `weightClass` is required on `ItemDef` and `MailKindDef`. `allowedCategories` omitted means any; present but empty is an error.

**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: No Godot `Resource` holds Sim content, and nothing calls `Resource.Duplicate`/`DuplicateDeep` (the 4.5 change is irrelevant to defs).

**Control Manifest Rules (Foundation)**:
- Required: defs are `sealed record class` (leaves may be `readonly record struct`) with init-only `int/long/ushort/bool/enum` members and `ImmutableArray<T>` lists (hot paths use `AsSpan()`). A reflection test checks every def type. Defs are identified by `DefIndex`, never by record equality. Id regex `^[a-z][a-z0-9_]{0,47}$`. Defaults are neutral. (ADR-0010 §3)
- Forbidden: `T[]`, `float`, `double`, `decimal`, `Half` members in defs. (ADR-0010 Implementation Guidelines)
- Forbidden: Sim content as a Godot `Resource`/`.tres`. (ADR-0010 Alt 2)
- Required: `PerformativeMail.Sim` references the BCL only. (ADR-0001 §1)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] A reflection test runs over every type in `PerformativeMail.Sim.Content` marked as a def or def leaf, including nested types and generic arguments. It finds no `T[]`, `float`, `double`, `decimal` or `Half` member, and every list member is `ImmutableArray<T>`. The Sim assembly references no Godot assembly. (CDD-SCHEMA-2)
- [x] Every def type is a `sealed record class` (or a `readonly record struct` leaf) with only init-only members. A test that sets a property after construction fails to compile; the test documents this by asserting via reflection that no member has a public non-init setter. (CD6)
- [x] The five M-1a def types expose the fields listed in the Implementation Notes, with units already scaled to integers. Reference fields hold a `DefIndex`, and set-valued id lists hold `ImmutableArray<DefIndex>`. (CD10 field table, the M-1a rows)
- [x] Every defaulted field's default is neutral: any / 0 / false / none / 1000 permille. A `FieldSpec`-independent test enumerates the documented defaults and asserts each is in that set. `weightClass` has no default on `ItemDef` and `MailKindDef`. (CD7; record half of CDD-SCHEMA-3)

---

## Implementation Notes

*Derived from ADR-0010 §3, GDD CD6/CD7/CD10 and spec 08 §2:*

- **Location:** `src/PerformativeMail.Sim/Content/Defs/`.
- **Common field.** Every def has a `string Id` (validated ASCII, used by the F6 encoding and the diagnostics). It must not be used for equality or lookups in the tick path.

**M-1a fields.** Values in the notes below are units as stored.

- **`ItemDef`** (spec 08 §2.1 + CD10):
  - `Id`
  - `Name` (localisation key string)
  - `Category` (`ItemCategory`, required)
  - `GridW`/`GridH` (`int`, required)
  - `MaxStack` (`int`, required, ≥ 1)
  - `WeightClass` (required)
  - `SellPriceCents` (`long`, default 0)
  - `BuyPriceCents` (optional; presence flag)
  - `CarryByHand` (`bool`, default true)
  - `EquipSlot` (default none)
  - `Tags` (`ImmutableArray<string>`, default empty)

  The `tool`/`weapon`/`consumable` blocks have no M-1a reader. Their first consumer adds them as optional leaves; M-1a does no harvesting or combat. **`standardIssue` is not a field** (CD10).
- **`ContainerDef`** (Grid R1 + CD10):
  - `Id`
  - `Layout` (required)
  - `GridW`/`GridH`: required for grid/slots; absent for pile, so use a presence flag
  - `View` (default auto)
  - `BeltAccess` (default none)
  - `AllowedCategories` (`ImmutableArray<ItemCategory>`; *absent* = any, which is distinct from empty, so keep a presence flag)
  - `HandCarried` (default false)
  - `AcceptsInsert` (default true)
  - `ReservedSlots` (`ImmutableArray<int>`, default empty, slots layout only)
  - Note Grid's W×H vs spec rows×cols transposition (grid-inventory.md R1). Follow Grid: `GridW` = cols, `GridH` = rows.
- **`MailKindDef`** (spec 08 §2.2 + CD10 + Mail R1):
  - `Id`, `Name`
  - `GridW`/`GridH`
  - `MaxStack`
  - `BaseValueCents` (`long`)
  - `WeightClass` (required)
  - `CarryByHand` (default true)
  - `BeltLanes` (`int`)
  - `BeltLengthMm` (from `beltLengthMetres`, required)
  - `DeadlineOffsetShifts` (default 0)
  - `AcceptedBy` (`ImmutableArray<DefIndex>` → `DestinationTypeDef`, required)
  - `ComplaintOnMisdeliveryMilliPoints` (from `complaintOnMisdeliveryPoints`, required)
  - `UnlockShift` (`int`)
  - `FallbackKind` (`DefIndex`, default None)
- **`DestinationTypeDef`** (Mail R3 + CD10):
  - `Id`
  - `Accepts` (`ImmutableArray<DefIndex>` → `MailKindDef`, required)
  - `InsertRate`: see the flag below
  - `ManualInsertHoldMs` (from `manualInsertHoldSeconds`, required; 0 = no Deliver anchor)
  - `MaxAutomatedFeeders` (`int`)
  - `RequiresUnit` (default false)
  - `CatchVolume`, a `readonly record struct` leaf: `WidthMm`, `HeightMm`, `DepthMm`, `CentreHeightMm`, `YawMilliDeg`; required
- **`MailMixDef`** (spec 08 §2.3 + CD10):
  - `Id`
  - `Shifts`: `ImmutableArray<ShiftMix>`, where `ShiftMix` = `{ Shift, Shares: ImmutableArray<(DefIndex kind, int sharePm)> }`
  - `BatchIntervalMs`
  - `BatchJitterMs`
  - `TailGuardMs` (required)
  - `OpeningRampBatches` (default 0)
  - The ratios `StreetStreakRatioPm`, `RaidSpawnRateRatioPm`, `DistanceStepRatioPm`, `ShiftStepRatioPm`, each required
  - `spawnOverhead` stays as spec 08 spells it until Mail's re-review renames it. Flag it; do not invent a unit-suffixed name. If the owner has not settled it when this story starts, model it as `SpawnOverheadPm` and record the assumption in the story's completion notes.

**Decisions** (logged in `production/session-logs/decision-log.md`):
- (a) **`MailMixDef` is in the M-1a set** in place of the epic's "fixed spawn profile". `SpawnProfile` is a runtime input from Run Structure (mail-destinations.md, spawn section), not content. The shares and batch timing that the M-1a fixed cadence needs live in `MailMixDef`. CV-GEN-18 also lists it as a table the Sim indexes unconditionally.
- (b) **`KitDef` is not built at M-1a.** Its readers are M-1b (PC P15–P18) and M1 (Grid R17).
- (c) **There is no `ThrowDef` record.** CD9 / OQ-CDD-5 make it the `throw` balance owner (story 008).

**Open flag:** `insertRate` is "2.0/s" in Mail R3 but "2000 milli/s" in AC M-R3-1, and CD4 has no `…Rate` suffix row. Store it as an integer per-mille-per-second (`InsertRateMilliPerSec`) per the AC, and flag it for the Mail/C&D re-review.

---

## Out of Scope

- Story 018: `FieldSpec` tables, parsing, CV-GEN-14 (a forbidden variant field such as `grid` on a `pile`) and CV-GRD-03.
- Story 008: the M-1a instances (`hotbar`, `po_intake`, `letter`, `house`, …).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/DefShapeTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/content/DefShapeTests.cs` (21 tests)

---

## Dependencies

- Depends on: Story 001 (`DefIndex`), Story 002 (enums)
- Unlocks: Story 008, Story 018

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 4/4 passing (no deferred items)
**Deviations**: ADVISORY (accepted) — (1) `MailMixDef` shares use a named `MailShare(DefIndex Kind, int SharePm)` leaf inside `ShiftMix`, not a `(DefIndex, int)` ValueTuple (a tuple cannot carry the leaf marker or pass the reflection gate); (2) **`SpawnOverheadPm` assumption** — no owner ruling on the `spawnOverhead` name; modelled as `MailMixDef.SpawnOverheadPm` (1.6 → 1600) pending Mail re-review; (3) `DestinationTypeDef.InsertRateMilliPerSec` (2.0/s → 2000) per AC M-R3-1, flagged for Mail/C&D re-review; (4) `CarryByHand` / `AcceptsInsert` default true per CD10, treated as the "any" neutral value; (5) every field without a stated default is `required`; presence flags not cross-checked in the record (story 018); spec 08 fields `requiresVehicleZone`, `lateValueRatio`, `misdeliveryPenaltyRatio` and item tool/weapon/consumable blocks not modelled (not in the M-1a field list). Residual review advisories logged as TD-044.
**Test Evidence**: Logic: `tests/unit/content/DefShapeTests.cs` (21/21 pass); four legs green at 1514 tests/leg (net10/net8 × Debug/Release; Debug 10 skipped, none from this story); reflection gate seen failing (int[] on ItemDef; static-field and discovery checks via test-side mutation).
**Code Review**: Complete — LP-CODE-REVIEW APPROVE; godot-csharp-specialist no BLOCKING (gate holes fixed); QL-TEST-COVERAGE GAPS (advisory, TD-044).
