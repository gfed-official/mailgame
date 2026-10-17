# Story 004: `AddressId`, the lot/address table, destination ids in lot order, address and mailbox-spacing validation, `StreetNameAt`

> **Epic**: World Stub — Hand-Authored Street (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day; ~300 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: none for World Generation. Consumer: `design/gdd/mail-destinations.md` R3 (per-lot hand-over: `addressId, mailboxTile, faceNormal, anchor, approachTile, truckStopPoint`; anchor = face + 0.3 m along the normal at 1.0 m height; spacing guarantee: same-side mailboxes ≥ 6 m apart, opposite-side staggered ≥ 3 m along the street) and R10 (addresses ascending). Also `design/gdd/simulation-core.md` R19 (destination ids at world build).
**Requirement**: `TR-mail-006` (address source: packed `AddressId` and the lot table the Mail registry is built from)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*
**Untraced (no TR-ID in the registry):** the Mail R3 mailbox-spacing guarantee (OQ15) and `StreetNameAt` (PC L645 location wording). ADR-0026 §6 decides both, but `tr-registry.yaml` has no entry. Flagged for the next `/architecture-review`; this story does not add registry entries.

**ADR Governing Implementation**: ADR-0026: World Query Contract (`IWorldQuery`) and the M-1a Street Stub (primary); ADR-0017 §1 (`AddressId` packing and order); ADR-0004 (Destination ids at world build)
**ADR Decision Summary**: `Lots` is a `ReadOnlySpan<LotRecord>` in lot order = ascending `AddressId`. The lot index is Mail's `DestinationOrdinal`. Each record carries a `Destination` `EntityId` (class 0x07), allocated by `WorldTablesBuilder` in lot order while the world is `Constructed`, plus `AddressId`, `DistrictId`, `StreetId`, `Number`, `Display` (`StringId`), `MailboxTile`, `Face` (one of four axis directions), `Anchor` (face + 300 mm along the normal, y = ground + 1000 mm), `ApproachTile`, `TruckStopPoint` and the house `StaticSolid` index. The builder rejects duplicate addresses, out-of-range components (`AddressOutOfRange`) and spacing below Mail R3 with `SimBuildException`. `AddressId` is a packed u32 (district 6 · street 8 · number 12 · unit 6 bits, high to low) in `Sim/Primitives`; numeric order equals `(district, street, number, unit)` order; 0 is None.
**ADR Version**: ADR-0026 2026-10-16; ADR-0017 2026-10-16; ADR-0004 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Core layer + Global)**:
- Required: `Lots` are in ascending `AddressId` (= Mail's `DestinationOrdinal`); destination `EntityId`s are allocated by the builder in lot order in `Constructed`; the builder rejects duplicate or out-of-range addresses and bad spacing (`SimBuildException`) (ADR-0026 §6)
- Required: every id struct implements `IEquatable<T>`, `Equals(object)`, `GetHashCode()` and `==`/`!=` explicitly; plain `readonly struct`, not `record struct` (ADR-0004; manifest I-6)
- Required: id allocation is legal only on the owner thread, with the `Destination`/`Prop` exception while `Constructed` (ADR-0004; `production/epics/simulation-core/story-018-id-allocators-slottable.md`)
- Forbidden: interned address strings (ADR-0017 §1, L852); any Mail type referenced from `Sim/World` (ADR-0026 does not depend on ADR-0017; Mail builds its registry *from* `Lots`)

---

## Acceptance Criteria

*From ADR-0026 §6, ADR-0017 §1 Validation Criteria and Mail R3, scoped to this story:*

- [x] **AC-1 `AddressId` packing and order.** `AddressId.Pack(d, s, n, u)` round-trips through `District`/`Street`/`Number`/`Unit` for boundary values (d 1 and 63, s 0 and 255, n 0 and 4095, u 0 and 63). Over 10,000 seeded random tuples, sorting by `Value` equals sorting by the `(district, street, number, unit)` tuple. `default(AddressId)` is None (Value 0).
- [x] **AC-2 Lot order and destination ids.** Lots added in any order come out of `Build()` in ascending `AddressId`. Their `Destination` ids are class `Destination` (0x07), allocated during `Constructed` in that lot order, and contiguous: lot i has raw counter (first + i). `LotRecord.HouseSolid` refers to a `StaticSolid` of kind `House` whose `Owner` equals that lot's `Destination`.
- [x] **AC-3 Address validation.** A duplicate `AddressId`, district 0 or > 63, street > 255, number > 4095 or unit > 63 fails `Build()` with `SimBuildException` (`AddressOutOfRange` for range errors; a distinct code for duplicates).
- [x] **AC-4 Anchor and spacing.** `Anchor` = mailbox face point + 300 mm along `Face`, with y = `TerrainHeightMm` at the anchor's x/z + 1000. Two same-side mailboxes 5,999 mm apart fail the build and 6,000 mm pass. Two opposite-side mailboxes staggered 2,999 mm along the street fail and 3,000 mm pass.
- [x] **AC-5 `StreetNameAt`.** A point inside a street's footprint returns that street's `StringId`; a point off every street returns `StringId.None`.

---

## Implementation Notes

*Derived from ADR-0026 §6, ADR-0017 §1 and Implementation Guidelines:*

- **`AddressId`** (`src/PerformativeMail.Sim/Primitives/AddressId.cs`): exactly ADR-0017's Key Interfaces shape (`Value`, `District`, `Street`, `Number`, `Unit`, `static Pack(int district, int street, int number, int unit = 0)`). Bit layout `district << 26 | street << 18 | number << 6 | unit`. `Pack` asserts the ranges under `SIM_ASSERT`; range *errors* are reported by the world builder (AC-3), not by `Pack`. **This story creates the type.** `production/epics/mail-destinations/` reuses it and must not re-create it (cross-reference noted in that epic).
- **Builder API (story-level shape within ADR-0026 §6):**
  - `AddStreet(byte streetId, byte districtId, StringId display, AabbMm footprint, axis)`: an axis-aligned street centre strip, used for `StreetNameAt` and for "side" and "along the street" in the spacing check.
  - `AddLot(AddressId, StringId display, Vec3Mm mailboxFacePoint, FaceDir face, TileCoord mailboxTile, TileCoord approachTile, Vec3Mm truckStopPoint, ushort houseSolid)`. The builder **derives** `Anchor` from the face point and `Face`, so it cannot drift from R3's rule, and derives `DistrictId`, `StreetId` and `Number` from the `AddressId`.
  - `Build(EntityIds ids, ...)`: sort lots by `AddressId.Value` with a plain `uint` order (no comparer), validate, then allocate `Destination` ids in that order and patch each house solid's `Owner`.
- **Spacing check (Mail R3; story-level geometry):** mailboxes on the same street are on the same side iff the signed perpendicular offset of their face points from the street's centre axis has the same sign. Same-side pairs need a Euclidean xz distance ≥ 6,000 mm. Opposite-side pairs need |Δ along the street axis| ≥ 3,000 mm. Compare squared distances in `long`. The thresholds are Mail R3's (OQ15). Keep them as named constants on the builder (`MinSameSideMailboxSpacingMm = 6000`, `MinOppositeSideStaggerMm = 3000`), citing R3. They are a World-build guarantee, not a balance key.
- **Display strings:** `Display` and the street name are `StringId`s. `StringId`/`StringNamespace` come from `production/epics/content-data/story-001-defindex-stringid-deftable.md`. At M-1a, `WorldTables` owns a small immutable world string table (address and street display text) indexed by these `StringId`s, in the `Run` namespace. If a dedicated namespace turns out to be needed, that is an ADR-0004 amendment: flag it and do not invent one. Presentation renders the text (and the off-street "by the coast" wording); the Sim never formats strings.
- `IWorldQuery.Lots` returns `ReadOnlySpan<LotRecord>` over a `LotRecord[]` built once. `LotRecord` is a `readonly struct` laid out as in ADR-0026 Key Interfaces.
- Destination allocation uses the injected `EntityIds` (`production/epics/simulation-core/story-018-id-allocators-slottable.md`) while the world state is `Constructed`. The builder never allocates after `Freeze()`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 005: `PostOffice` record and spawn-pad validation.
- Story 006: the 10 authored lots of `M1aStreet`.
- mail-destinations epic: the destination registry built from `Lots` (`DestinationTypeDef`, catch volume, feeder slot, reverse `AddressId → ordinal` map), and the wire narrowing to `AddressOrdinal u16`.
- ContentValidator's per-lot spacing check (Mail R3 hand-back). At M-1a the street is code-authored, so the builder check is the gate.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/world/AddressIdTests.cs` and `tests/unit/world/WorldLotsTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/world/AddressIdTests.cs, tests/unit/world/WorldLotsTests.cs

---

## Dependencies

- Depends on: Story 001 (builder, height for the anchor y), Story 003 (house solids); `production/epics/simulation-core/story-002-id-value-types.md` (`EntityId`, `EntityClass.Destination`); `production/epics/simulation-core/story-018-id-allocators-slottable.md` (`EntityIds`, `Constructed` exception); `production/epics/content-data/story-001-defindex-stringid-deftable.md` (`StringId`)
- Unlocks: Story 006; mail-destinations registry story; grid-inventory `StackKey` address field (`AddressId` type)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (AC-1 → AddressIdTests 16 boundary round-trips, bit layout, 10,000 seeded tuples sort equivalence, default None, equality, SIM_ASSERT path; AC-2 → WorldLotsTests 4 shuffle seeds ascending, Destination 0x07 contiguous first+i, house Owner, refused outside Constructed, null `ids` throws; AC-3 → DuplicateAddress + each component over-max / −1 → AddressOutOfRange; AC-4 → anchor on sloped ground for all four faces, same-side 5,999/6,000 incl. diagonal, opposite-side 2,999/3,000 on X and Z streets; AC-5 → inside/edges/just past/off-street/crossing)
**Deviations**: none blocking. Advisory: extra `AddLot(int district, int street, int number, int unit, …)` overload (Pack masks, so range errors need unpacked input); `AddDisplayString` → Run-namespace `StringId` + `WorldTables.TryGetDisplayText` (not on `IWorldQuery`); stable insertion sort (analyzer bans Array.Sort); extra `WorldLotInvalid` rules (registered street, mailbox not on the centre line, no shared house solid); `Build()`/`Build(int,int)` throw `WorldLotsNeedEntityIds` when lots exist; `Build(EntityIds,…)` null check + no-rollback doc added in-story. Enums appended: SimBuildErrorCode 29–36, SimAssertCode 51
**Test Evidence**: Logic: tests/unit/world/AddressIdTests.cs (6 methods), tests/unit/world/WorldLotsTests.cs (25 methods) — full suite 2 098/leg, 0 failed (net10/net8 × Debug/Release; Debug 15 skipped)
**Code Review**: Complete — LP-CODE-REVIEW APPROVE; godot-csharp-specialist APPROVE; QL-TEST-COVERAGE ADEQUATE; advisories → TD-053; TD-028 items 1/2/6 progress noted
**Run result**: N/A — pure Sim library (no scene, no rendering); nothing observable on screen
