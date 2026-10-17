# Story 002: Core primitives I — id value types

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 450 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R19 ids, R20 exhaustion, R21 slots; F4)
**Requirement**: `TR-sim-025` (EntityId layout/classes), `TR-sim-026` (MailId separate u32; PlayerSlot u8 0–7)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0004: Identity and Id Spaces
**ADR Decision Summary**: Every id is a value-type wrapper with a fixed layout and a `None` value: `EntityId` = `((uint)cls << 24) | counter` (counter 1…2^24−1, None 0), `ActorId` (`System` = 0x00000001), `MailId` (own u32 counter), `PlayerSlot` and `DefIndex` with `+1` storage so `default` is None, `EntryId` real/temp bit-31 layout, wrap-safe `FlightId`, and `StringId` = `(ns, index)`. Ids order by raw `Value` ascending; equality and hashing are explicit arithmetic overrides.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None engine-side. Godot boundary rule (for later Presentation stories): id structs are not Variant types — never `[Signal]` params, `[Export]` fields or `Godot.Collections` elements; Presentation passes `.Value`.

**Control Manifest Rules (Foundation)**:
- Required: use the id wrapper structs in every API crossing a system boundary; `EntityId = ((uint)cls << 24) | counter`; `EntityId.Compose` asserts `counter ≤ 0xFF_FFFF` and masks; every id implements `IEquatable<T>`, `Equals(object)`, `GetHashCode()`, `==`/`!=` explicitly (single-field ids hash as `(int)Value`; `StringId` as `((int)Ns << 16) | Index`; no `System.HashCode`); `PlayerSlot`/`DefIndex` `+1` storage with `FromValue(0xFF/0xFFFF)` special-casing None; real `EntryId`s keep bit 31 clear, temp ids exactly `0x8000_0000 | slot << 20 | ctr`.
- Forbidden: one global entity counter with a side table; 64-bit entity ids; `DefIndex` whose `default` means ordinal 0; temp entry ids as negative numbers or a separate type; ordering by `GetHashCode()`; reaching default `ValueType.Equals/GetHashCode`; numeric comparison of `FlightId`; `record struct` for ids that appear in ordering/hashing.
- Guardrail: none (pure value types, no allocation).

---

## Acceptance Criteria

*From GDD SC-R19-1 (layout half) and ADR-0004 §1, §2, Implementation Guidelines, scoped to this story:*

- [x] **AC-1 EntityId layout (SC-R19-1, encode/decode rows).** `EntityId.Compose(EntityClass.Player, 1).Value == 16 777 217`; `EntityId.Compose(EntityClass.Construct, 1).Value == 50 331 649`; decoding 50 331 649 gives `Class == Construct (3)` and `Counter == 1`; `EntityId.None.Value == 0` and `default(EntityId) == EntityId.None`; `ActorId.System.Value == 0x00000001`; `Compose` with `counter > 0xFF_FFFF` asserts under `SIM_ASSERT` and masks to 24 bits otherwise.
- [x] **AC-2 None and +1 storage.** `default(PlayerSlot).IsNone` and its `Value == 0xFF`; `PlayerSlot.FromValue(0xFF).IsNone`; `PlayerSlot.FromValue(7).Value == 7`; slots ≥ 8 other than 0xFF are rejected (assert / None); `default(DefIndex).IsNone`, `Value == 0xFFFF`, `DefIndex.FromValue(0xFFFF).IsNone`, `FromValue(0).Value == 0` — all without overflow under checked arithmetic.
- [x] **AC-3 EntryId layout.** `EntryId.Temp(slot 5, ctr 0x12345)` has `Value == 0x8000_0000 | 5 << 20 | 0x12345`, `IsTemp` true, and bits 23–30 zero; `ctr ≥ 2^20` asserts; a real id with bit 31 set asserts.
- [x] **AC-4 Equality, hashing and order.** For every id type, `==`, `!=`, `Equals(T)`, `Equals(object)` and `GetHashCode()` are explicit (a reflection test asserts each type declares its own `Equals(object)` and `GetHashCode()` overrides); single-field ids hash to `(int)Value`; `StringId` hashes to `((int)Ns << 16) | Index`; `EntityId` and `MailId` implement `IComparable<T>` ordering by raw `Value` ascending; `FlightId` exposes no `IComparable` or relational operators.

---

## Implementation Notes

*Derived from ADR-0004 §1–§2, §6, Key Interfaces and Implementation Guidelines:*

- Files in `src/PerformativeMail.Sim/Primitives/` (namespace `PerformativeMail.Sim.Primitives`): `EntityClass.cs`, `EntityId.cs`, `ActorId.cs`, `MailId.cs`, `PlayerSlot.cs`, `EntryId.cs`, `FlightId.cs`, `DefIndex.cs`, `StringId.cs` (+ `StringNamespace` with `None = 0`, `Run = 0xFF`; def-type members are ADR-0010's, added by content-data).
- `EntityClass : byte` — `None = 0, Player = 0x01, WorldItem, Construct, Vehicle, Enemy, Npc, Destination, Prop` (0x08), `TestFirst = 0xF0, TestLast = 0xFF`. Append-only; a new class amends ADR-0004 and GDD R19. Mail is **not** a class.
- Every class shift is written `(uint)cls << 24` (cast first — checked-build safety, ADR-0006 overflow class 4).
- Plain `readonly struct` with a single `readonly` field (`public readonly uint Value` or private `_v` for `+1` types). **Not** `record struct`: synthesized members use `EqualityComparer<T>.Default` and slip past the analyzer (ADR-0006 §3).
- `PlayerSlot`: private `byte _v` = slot + 1; `Value => _v == 0 ? (byte)0xFF : (byte)(_v - 1)`; `FromValue(byte v)` returns `default` for 0xFF and asserts `v < 8` otherwise. `DefIndex` mirrors it with `ushort`/0xFFFF (ordinals 0…65 533).
- `EntryId.Temp(PlayerSlot s, uint ctr) => new(0x8000_0000u | ((uint)s.Value << 20) | (ctr & 0xF_FFFFu))` with `SimAssert.That(ctr < 1u << 20, …)`. Real-id construction asserts bit 31 clear. Temp-counter exhaustion behaviour is ADR-0009's (grid-inventory).
- `ActorId` is `EntityId`-width; `ActorId.System = new(0x0000_0001u)` (class 0 is never allocated, so it cannot collide). Provide `ActorId.FromEntity(EntityId)`.
- Assert codes: allocate a small `SimAssertCode` constants class (`ushort`) in `Primitives/` now (e.g. `IdCounterRange = 1`, `IdSlotRange`, `EntryTempCtrRange`, …); later stories append. Codes are never renumbered.
- `GetHashCode` overrides are arithmetic only (`(int)Value`); the Sim never *calls* `GetHashCode` (ADR-0006 rule 6 — Story 004's analyzer will ban calls; overrides are declarations and must still compile — if RS0030 flags the override, Story 004 adds a targeted suppression for these declarations only).
- Wire narrowing (`WireNarrow`) and serial comparison (`SerialU8/U16/U32`) are **frozen interfaces built by their first consumer** (GDD milestone slice): do not implement them here.

---

## Out of Scope

- Story 003: numerics (`Cents`, `Fx16`, `Fix`).
- Story 018: `EntityIds`/`MailIds` allocators, exhaustion records, test-class gating, `SlotTable` (the allocation half of SC-R19-1).
- `SerialU*`, `WireNarrow`: first consumer (throwing-catching / network-adapter-loopback).
- `DefIndex` assignment and `StringNamespace` def members: content-data epic.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Primitives/IdTypesTests.cs` — must exist and pass (Debug + Release; net10.0 + net8.0)

**Status**: [x] Created — tests/unit/sim/Primitives/IdTypesTests.cs; suite 76/76 pass Debug+Release × net10.0+net8.0

---

## Dependencies

- Depends on: Story 001 (`SimAssert`, `SIM_ASSERT` symbol)
- Unlocks: Story 003, Story 013, Story 018, and every story that names an id

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 4/4 passing (none deferred)
**Deviations** (all ADVISORY, accepted — choices where the story was silent, none contradicting ADR-0004):
- `EntryId.Real` with bit 31 set asserts (`EntryRealBit31`), else returns None; `EntryId.Temp` with a None slot asserts (`EntryTempSlotNone`), else None — the ADR Key Interfaces formula would otherwise shift 0xFF into reserved bits 23–27.
- Extra `EntryId.FromValue(uint)` for wire/snapshot decoding, validating temp reserved bits 23–30 (`EntryTempLayout`).
- `GetHashCode` on u32 ids is `unchecked((int)Value)` (Debug builds are checked; `(int)` of ≥ 2^31 would throw).
- `None`/`ActorId.System` are expression-bodied static properties (no static fields).
- `SimAssertCode` 1–7 allocated (append-only).
**Hand-off to Story 018**: `EntityId.Compose` accepts any class and counter 0 by design; `EntityIds.Allocate` must reject class None, reserved classes 0x09–0xEF (unless `allowTestClasses` for 0xF0–0xFF) and never hand out counter 0 (GDD F4: counters 1…2^24−1). Story 018 also carries the SC-R19-1 allocation half, SC-R20-1 and SC-R21-1.
**Tech debt**: TD-003 (EntryId test gaps), TD-004 (ADR-0004 Key Interfaces doc drift)
**Test Evidence**: Logic: `tests/unit/sim/Primitives/IdTypesTests.cs` (~48 methods; mask assertion mutation-tested)
**Code Review**: Complete — LP-CODE-REVIEW APPROVE; QL-TEST-COVERAGE ADEQUATE; godot-csharp-specialist CHANGES MADE (tests strengthened)
