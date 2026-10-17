# Story 002: Destination registry built from `IWorldQuery.Lots` — lot-ordered SoA, `DestinationOrdinal`, reverse `AddressId` map, `TryGetDestinationFacts`

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day; ~250 lines prod + ~200 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/mail-destinations.md` (R3 destinations and the `house` `DestinationTypeDef`)
**Requirement**: `TR-mail-006` (registry half: destinations keyed by packed `AddressId`), `TR-mail-018` (the `DestinationTypeDef` content it reads)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §3); ADR-0026 §6 (`Lots` in ascending `AddressId`, ids allocated by World); ADR-0010 (`DestinationTypeDef`)
**ADR Decision Summary**: The registry is built once while `Constructed`, after World has allocated destination `EntityId`s in lot order. Mail reads `IWorldQuery.Lots` and builds its rows in that order ("World allocates the ids; Mail builds the registry"). The dense index `DestinationOrdinal` (u16) = lot order, which is also the wire id map for `destination u16`. SoA columns: `EntityId`, `AddressId`, `TypeIndex` (`DestinationTypeDef`), `DistrictId`, interaction anchor (`Vec3Mm`), catch volume (axis-aligned box, mm), `feederSlot`, plus per-door complaint state `tokensPm` and `lastTick` (long; used by story 006). A reverse map `AddressId → ordinal` is a sorted `uint[]` + `ushort[]` pair searched by binary search (≤ 10 probes at 1,000 lots), used only for misdelivery attribution, never in `Evaluate`.
**ADR Version**: ADR-0017 2026-10-16; ADR-0026 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: the registry is built from `IWorldQuery.Lots` (ADR-0017, ADR-0026 §6)
- Required: wire narrowing of `AddressId`/destination uses the ordinal id maps (ADR-0017 §1, §3)
- Required: build-time tables are frozen at `Freeze()` (Global)
- Forbidden: allocating destination ids in Mail (World does it); registry scans inside `Evaluate`

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md` (M-R3-1a) and ADR-0017 §3, scoped to this story:*

- [x] **AC-1 (M-R3-1a)** Given 10 lots (the `M1aStreet` stub or a 10-lot fixture), building the registry yields exactly 10 destinations. `addressId → destinationId → addressId` round-trips for each. Each house row carries the R3 `house` values read from `DestinationTypeDef`: accepts postcard, letter, small, medium and large; `insertRate` 2000 milli/s; `manualInsertHold` 0.4 s; `maxAutomatedFeeders` 1; `requiresUnit` false; catch volume 1.0 × 0.8 × 1.0 m centred 1.0 m above the mailbox base (as mm).
- [x] **AC-2 Order and ids.** `DestinationOrdinal` i equals lot index i. Row i's `EntityId` equals `Lots[i].Destination` (Mail allocates none). The interaction anchor equals `Lots[i].Anchor`. An unknown `AddressId` misses in the reverse map in ≤ 10 probes (counted) on a 1,000-lot fixture.
- [x] **AC-3 Facts.** `IMailApi.TryGetDestinationFacts(dest, out f)` returns `{Id, Address, Type, State}` for a registered destination and `false` for any other `EntityId`. With no `SetDeliveriesState` call yet, `State` is CLOSED for District 1 (story 009 drives it).

---

## Implementation Notes

*Derived from ADR-0017 §3 and ADR-0026 §6:*

- **Files:** `src/PerformativeMail.Sim/Mail/DestinationRegistry.cs` (SoA arrays sized to `Lots.Length` at build), `DestinationOrdinal.cs` (`readonly struct` u16 with explicit equality per ADR-0004).
- **Catch volume:** the `CatchVolume` record is `DestinationTypeDef`'s (`production/epics/content-data/story-005-m1a-def-records.md`: `WidthMm, HeightMm, DepthMm, CentreHeightMm, YawMilliDeg`). Convert it to a world AABB per lot using `Lots[i].Face` (four axis directions, so the box stays axis-aligned; ADR-0026 §6, Throwing CV 6). The base is the mailbox base at `Anchor.Y − 1000` (ADR-0026: anchor y = ground + 1000 mm). Width runs along the face and depth along the normal. Document the mapping; Throwing's landing test consumes it.
- **Type assignment at M-1a:** every residential lot is type `house` (the only v1 type with lots). Read `DestinationTypeDef` by its def id from the `ContentSet`, never by a string at tick time.
- **`feederSlot`:** present and empty (M-1b feeders, R12).
- **State columns:** `tokensPm`/`lastTick` are initialised to the full bucket (story 006 defines the values; until then 0 and tested there).
- **`DestinationFacts`** and `DestState` (OPEN/CLOSED/LOCKED) are declared in story 003 (`MailRules`). Here `TryGetDestinationFacts` fills them from the registry plus the deliveries state store that story 009 writes (`open`, `closeTick`, `unlockedDistrictMask`; default: closed, District 1 unlocked).
- The registry is built during `Constructed` in the Mail system's construction (story 005 composes it). `IWorldQuery` arrives by constructor (ADR-0003). Dump the registry's static columns once in the `Mail` section after the table rows (story 001's section).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- M-R3-1b (lot spacing / buildable `approachTile` rejection): enforced at world build by `production/epics/world-stub/story-004-addressid-lots-destination-ids.md`; ContentValidator per-lot checks are M1 (generated worlds).
- Story 006: complaint bucket arithmetic. Story 009: deliveries state. Story 010: the client `DestinationMirror`.
- Feeder registration (`CanRegisterFeeder`, M-1b).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/mail/DestinationRegistryTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/mail/DestinationRegistryTests.cs, DestinationRegistryAllocationTests.cs (NonParallel)

---

## Dependencies

- Depends on: `production/epics/world-stub/story-004-addressid-lots-destination-ids.md` (and `production/epics/world-stub/story-006-m1a-street-and-fixtures.md` for the street fixture); `production/epics/content-data/story-005-m1a-def-records.md`, `production/epics/content-data/story-008-contentset-builtincontent-m1a.md`
- Unlocks: Story 003 (facts), Story 006, Story 007, Story 010; manual-delivery Deliver anchors (one per destination)

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 3/3 passing (all automated; none deferred). AC-3 verified through `DestinationRegistry.TryGetDestinationFacts` (identical `IMailApi` signature) — `IMailApi` is created by story 005, which must delegate to it and test that (carried obligation, TD-079).
**Deviations** (advisory, TD-079): `DestinationFacts`/`DestState` declared here (story 003 consumes them; 003 depends on 002); raw state Locked > Closed > Open, no grace; catch AABB centred on the lot anchor (to confirm with Throwing); district mask bit (districtId−1), districts 1..63; `DeliveriesState` (default closed, District 1 unlocked) not yet dumped (story 009); build rejections are `ArgumentException`.
**Implementation**: `src/PerformativeMail.Sim/Mail/{DestinationRegistry,DestinationOrdinal,DestinationFacts,DestState,DeliveriesState}.cs` (namespace `PerformativeMail.Sim.MailDestinations`); EntityId→ordinal computed when World ids are contiguous (M1aStreet), else sorted binary search; AddressId reverse map hand-written binary search with probe count (≤ 10 at 1,000 lots). Mail dump section now appends `u16 destinationCount` + 65 B per ordinal (entityId u32, addressId u32, typeIndex u16, districtId u8, anchor xyz i32, catch AABB min/max xyz i32, feederSlot u16 (0xFFFF none), tokensPm i64, lastTick i64) after the mail rows; `MailStateContributor(MailTable, DestinationRegistry)`, `MailTable.BindDump(builder, registry)`.
**Test Evidence**: Logic — tests/unit/mail/DestinationRegistryTests.cs + DestinationRegistryAllocationTests.cs (MailDestinations namespace 50 tests). Full suite 3,793/leg, 0 failed on net10/net8 × Debug/Release (Debug 28 skipped — TD-008), orchestrator-verified on the real repo.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (blocking ctor 70 lines/CC 13 → refactored to helpers; sort skip on computed path; yaw/extent rejection; split build-rejection tests) + C#-specialist pass; QL-TEST-COVERAGE ADEQUATE (gaps 2–4/6/7 fixed).
