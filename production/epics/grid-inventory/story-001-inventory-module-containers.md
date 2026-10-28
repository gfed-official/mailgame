# Story 001: Inventory module skeleton — container registry, M-1a containers and per-player panel

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-17

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-026` (ContainerDef fields / stat keys consumed), `TR-inventory-001` (module binds into the one op FIFO — wiring only)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0003: Phase Order, Composition Root and Cross-System Calls; ADR-0010: Content Format, Loading, ContentSet and StatSheet; ADR-0004: Identity and Id Spaces
**ADR Decision Summary**: Inventory is one Sim module composed by the root; containers live in a slot-indexed `ContainerHeader` struct array with an open-addressed `containerId → slot` map, preallocated from `SimSettings`; a player's panel containers and cursor get `containerId`s at `PlayerJoin` (ADR-0009 §1 "Containers on the wire", §5). `ContainerDef` comes from the immutable `ContentSet` (ADR-0010).
**ADR Version**: ADR-0009 2026-10-16; ADR-0003 2026-10-16; ADR-0010 2026-10-16; ADR-0004 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: inventory is one Sim module `PerformativeMail.Sim/Inventory/` binding one `OpDescriptor` + `IOpHandler` per op, `ItemsExpire` (phase 1), `IntakeDrain` (phase 2), a conservation `ITickEndCheck`, its event kinds, a state contributor and a snapshot source (ADR-0009)
- Required: storage preallocated from `SimSettings` at world build; never `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any inventory tick path (ADR-0009 §5, Implementation Guidelines)
- Cross-ADR note I-6 (manifest): `ContainerId` is a plain `readonly struct` with explicit `IEquatable`/`GetHashCode`/`==` — **not** ADR-0009's `readonly record struct` (follow ADR-0004/0006)
- Required (Feature/Core, ADR-0003 §4): `[SimSystem(SystemId)]` with exactly one public constructor whose parameters come only from the closed injectable list (`ContentSet`/sub-table, `IStatSheet`, `EntityIds`/`MailIds`, read-only `SlotTable`, `EventLog` writer, `ITickReader`, `SimCounters`/`SimCheckRing`, own settings sub-record, declared `I…Api`s)
- Forbidden: statics other than `const`/`static readonly` immutable (ADR-0003 §4)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-R1-1 (M-1a rows)** — GIVEN `BuiltInContent.CreateM1a()`, WHEN the inventory system builds, THEN each M-1a container's `(id, W, H, cells, layout, view, beltAccess, allowedCategories)` equals its R1 row: hotbar (8,1,8,slots,grid,none,any), player_inventory (8,2,16,grid,grid,none,any), po_intake (20,16,320,grid,manifest,outfeed_only,["mail"]), cursor (1 slot, slots, any hand-carryable); a row fails independently. (Deferred rows — backpack, bike_cargo, truck, po_depot, chest, depot, sorter_buffer, bags — stay open on M-1b/M0.)
- [x] GIVEN a world with `SimProfile.M1aGreybox`, WHEN it is built, THEN exactly one `po_intake` container exists with a stable `containerId`; WHEN a `PlayerJoin` for slot s applies, THEN hotbar, player_inventory and cursor containers exist for s with fresh `containerId`s, each `version = 0`, `lastReplaceVersion = 0`, `statEpoch = 0`; a `PlayerLeave` keeps them (disconnect handling is M0).
- [x] Container dimensions are instance state (`ContainerHeader.w/h`, replicated; perks grow rows later, R15) initialised from `ContainerDef` + `IStatSheet.Read(InventoryRows, container)` at creation (with no sources = def rows).
- [x] All container/entry/index storage is allocated at world build from the inventory settings sub-record; a test runs 1 000 ticks of joins/ops after warm-up and asserts 0 B allocated on the tick thread and no array growth.
- [x] `ContainerId` is a plain `readonly struct` (u32) with explicit `Equals`/`GetHashCode` (`(int)Value`)/`==`/`!=` (reflection test, I-6).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/` — `InventorySystem.cs` (`[SimSystem]`), `ContainerId.cs`, `ContainerHeader.cs`, `ContainerTable.cs`, `InventorySettings.cs`.
- **Inventory settings sub-record** (ADR-0003 §4 "the system's own settings record"; ADR-0009 §5 "capacities in `SimSettings`"): the GDD gives no capacity keys, so this story adds PROPOSED rows (flag to the GDD re-review): `inventory.containerCapacity` (≥ 8 players × 3 panel containers + world containers; 64), `inventory.entryPoolCapacity` 8 192 (ADR-0009 Performance Implications example), `inventory.worldItemCapacity` 1 024 (cap 512 + temporary exceed headroom, R12), arena class sizes (story 003). Register them in content-data's `BalanceKey` registry (`production/epics/content-data/story-004-balance-registry-simsettings.md`) and add values to `BuiltInContent` (`production/epics/content-data/story-008-contentset-builtincontent-m1a.md`); the sub-record hangs off `SimSettings`.
- Bind points created here (handlers come in later stories): `BindOp` per op kind, `Bind(PhaseId.ItemsExpire)`, `Bind(PhaseId.IntakeDrain)`, `BindTickEnd`, `BindEventKind`, `BindStateContributor`, `BindSnapshotSource` (stub returning the container block; envelope is ADR-0022, M0).
- Player containers are created by reacting to Sim Core's `PlayerJoin` (`production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md`) — follow that story's hook (event or `I…Api` callback) rather than inventing a new one.
- `po_intake`'s `view = manifest` is the def value; the manifest *view* (R18) is M1 — see story 011's decision note.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002/003: entries and the `mailIds` arena.
- Story 011: open-set bookkeeping.
- M-1b: backpack (equip slot), bike/truck cargo, chest, depot, sorter_buffer, bags.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/inventory/ContainerCatalogueTests.cs` — must exist and pass

**Status**: [x] `tests/integration/inventory/ContainerCatalogueTests.cs` (ContainerCatalogueTests 12 methods incl. 4-row H-R1-1 theory + ContainerAllocationTests 1, Release-only), `tests/unit/inventory/ContainerTableTests.cs` (5), listener fan-out tests in `tests/unit/sim/Core/PlayerJoinTests.cs`

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md` (builder binds), `production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md` (PlayerJoin), `production/epics/simulation-core/story-002-id-value-types.md` (ids); `production/epics/simulation-core/story-025-composition-root-reflection-test.md` (`SimComposition`)
- Depends on: `production/epics/content-data/story-005-m1a-def-records.md` (`ContainerDef`), `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (`BuiltInContent`), `production/epics/content-data/story-015-statwriter-sources-lifetimes-wiring.md` (`IStatSheet` injected)
- Unlocks: Story 002, Story 004, Story 005, Story 011, Story 012, Story 013

---

## Completion Notes
**Completed**: 2026-10-17
**Criteria**: 5/5 passing (none deferred)
**Deviations**: (a) `SimWorldBuilder.BindPlayerJoinListener` generalised to a fan-out of ≤ 4 listeners in bind order (Players already held the single slot; same instance twice → `PlayerJoinListenerAlreadyBound`, 5th → new `PlayerJoinListenerLimit`); (b) capacity knobs live in `BalanceKeys.Inventory`/`InventoryBalance` → `InventorySettings.FromBalance`, not in `SimSettings` (one owner per prefix; PROPOSED rows flagged for the GDD re-review); (c) no op, event kind or snapshot source bound yet — stories 004/005; the builder has no `BindSnapshotSource` (ADR-0022, M0); (d) `ContainerTable` bump slot allocator (no removal in this story) — dump relies on slot order == id order; (e) all-or-nothing panel creation at join, `CheckCode.InventoryContainerCapacity` on a full table, rejoin reuses the slot's kept containers. Pins updated: `BuiltInContentTests` (121 → 124 rows), `ContentHashTests` golden `bafdec48…`, `SimCompositionTests`/`PlayerJoinBindingTests` (GridInventory is now built without a world). Carried: TD-095.
**Test Evidence**: Integration: `tests/integration/inventory/ContainerCatalogueTests.cs` (14 methods incl. the NonParallel 0 B test), `tests/unit/inventory/ContainerTableTests.cs` (5). Four legs green: net10/net8 × Release/Debug 4,389 tests, 0 failed (Release 1 skipped, Debug 38 skipped); Release build 0 warnings.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (10 low; fixed in-story: unchecked Fibonacci hash + `GetHashCode` for checked Debug builds, stale `SimComposition` comment, handler blank line; rest → TD-095). QL-TEST-COVERAGE GAPS (6; closed in-story: Inventory dump-section layout/determinism test, exact-exhaustion boundary, none-slot accessors, skipped-test confirmed not the 0 B test; 3 and 5 → TD-095).
**Run result**: N/A — Godot-free Sim module with no presentation surface; nothing observable until the inventory panel (stories 023/024).
