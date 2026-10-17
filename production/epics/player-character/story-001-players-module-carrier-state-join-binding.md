# Story 001: Players system skeleton — `PlayerSettings`, `CarrierState`, life-state data and the `PlayerJoin` binding

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/player-character-movement.md` (P1 the body; States and Transitions — life state; Tuning Knobs)
**Requirement**: `TR-movement-001` (the pure solver's state/input types — the types, not the step), `TR-movement-013` (life states host-owned with absolute-tick deadlines — data model and Joining → Alive only; timers/knockdown are M-1b), `TR-movement-020` (balance keys read through `PlayerBalance`)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance id: PCM-P1-1.)*

**ADR Governing Implementation**: ADR-0013: Carrier kinematic solver & fixed-point geometry (§2 `CarrierState`) — Accepted 2026-10-16 (primary); ADR-0014 (§6 life-state record), ADR-0003 (§4 injectable list, `SystemGraph`, `[SimSystem]`), ADR-0004 (§4 players and slots), ADR-0010 (the `players` settings record) — all Accepted
**ADR Decision Summary**: Players is a `[SimSystem]` with exactly one public constructor whose parameters come only from the ADR-0003 §4 injectable list. `CarrierState` is a blittable struct (feet position mm, tick-rational remainders, own velocity, BAM body yaw, `Loco`, flags, support fields, push-pair state) dumped by Players' `ISimStateContributor` in slot order. Life state is a host-owned enum plus absolute-tick deadlines (`ClockInTick`, `FrozenUntilTick`, `ProtectedUntilTick`) — nothing decrements a timer per tick.
**ADR Version**: ADR-0013 2026-10-16; ADR-0014 2026-10-16; ADR-0003 2026-10-16; ADR-0004 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None — `PerformativeMail.Sim/Players/` references no Godot assembly (ADR-0001). Golden vectors for later stories run on the net8.0 leg.

**Control Manifest Rules (Feature layer)**:
- Required: each system lives in `PerformativeMail.Sim/<System>/`, is marked `[SimSystem(SystemId)]`, has exactly one public constructor from the ADR-0003 §4 list; declare dependencies in `Composition/SystemGraph.cs` (ADR-0003 §4)
- Required: every constant comes from the `players` `SimSettings` record or the StatSheet, never a code literal (ADR-0013, ContentValidator PC 1–12)
- Required: bind an `ISimStateContributor` for any state; size every collection from `SimSettings`; 0 B in the tick path (ADR-0006 §4, §8)
- Required: life-state deadlines are absolute ticks (ADR-0014 §6)
- Forbidden: floats, `System.Random`, clocks, `Dictionary` iteration, statics in system code (ADR-0006 §3)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P1-1), scoped to this story:*

- [x] **PCM-P1-1.** Given a `PlayerJoin` for slot 0, then Players holds a record for it whose `EntityId` class is `player` (and equals the id in `SlotTable`), whose capsule radius is 300 and height 1400 (read from `PlayerSettings`, not literals), and whose **body yaw** and **aim** (camera yaw + pitch) are separate fields: writing one leaves the other unchanged.
- [x] **Settings binding.** `PlayerSettings` is built once from content-data's `PlayerBalance` and exposes every key the later stories use (walk/sprint/swim base, `buffCapPm`, `ticksAccel`, `ticksDecel`, `airControlPm`, `turnDegPerSec`, `g`, `jumpHeightMm`, `extraJumpPm`, `terminalMmS`, `coyoteTicks`, `jumpBufferTicks`, `stepUpMm`, `subStepMm`, `maxSubStepsPerTick`, `slideIterations`, `slopeMaxPm`, `capsuleRadiusMm/HeightMm`, `swimEnterMm/ExitMm`, `maxWadeSlowPm`, `haulOutMm/Ticks`, `chestMm`, `rangeMm`, `incumbentSlackMm`, `focus*`, `minHoldTicks`, `rangeSlackMm`, `positionStaleTicks`, `claimToleranceMm`, `maxAnchors`, `maxCarrierCandidates`). With `BuiltInContent.CreateM1a()` every value equals the GDD default (5000/7500/2000, 24 000, 1200, 300/1400, …). A key missing from `PlayerBalance` is a build error, not a default.
- [x] **Spawn.** After join, the carrier's feet are at ring slot `k = PlayerSlot` of the Post Office pad (`PadSlots.Position(world, PostOffice, k)` from world-stub story 005; slot 0 is (0, −1200) relative to the pad centre by the ADR-0013 axis convention), `Y = TerrainHeightMm(X, Z)`, `Loco = Grounded`, life state `Alive`, `ProtectedUntilTick = 0`. (M-1a placeholder for the M0 `Teleport{Join}`; recorded in Implementation Notes.)
- [x] **Life-state data model.** `LifeState { Joining, Alive, Dead, DeadAway, Frozen, Despawned }` exists with the absolute-tick fields `ClockInTick`, `FrozenUntilTick`, `ProtectedUntilTick` and `TeleportSeq` (u8); only `Joining → Alive` is exercised at M-1a. `IPlayersApi.GetLifeState(PlayerSlot)` returns it; an empty slot returns `Despawned`.
- [x] **Leave.** A `PlayerLeave` removes the record and frees nothing else (no spill at M-1a); a re-join of the same slot from tick T + 1 creates a fresh record.
- [x] **Dump.** Players' `ISimStateContributor` writes, per slot in ascending order, `playerId`, life fields and every `CarrierState` field one by one (never a struct blit, ADR-0006 §4); changing any single field changes the dump bytes, and two identical joins produce identical bytes.
- [x] **Composition.** `Players` is `[SimSystem(SystemId.Players)]`, has one public constructor (reflection test from sim-core story 025 still passes), is declared in `SystemGraph` depending only on the world read interface at M-1a, and binds no phase yet (its `PlayersStep` is story 019).

---

## Implementation Notes

*Derived from ADR-0013 §2, ADR-0014 §6, ADR-0004 §4, ADR-0003 §4:*

- Files in `src/PerformativeMail.Sim/Players/`: `PlayersSystem.cs` (`[SimSystem]`), `PlayerSettings.cs` (immutable record built from `PlayerBalance`), `CarrierState.cs`, `LifeState.cs`, `PlayerRecord.cs`, `PlayersStateContributor.cs`, `IPlayersApi.cs` (this story declares only `GetLifeState`; the ADR-0014 members are added by the story that implements them — registry methods in story 012, `CheckInteraction` in story 019; `Teleport` is M0 — so no member ever exists without an implementation).
- `CarrierState` follows the ADR-0013 §2 table exactly: `X,Y,Z` int mm; `RemX,RemZ` (1/30 mm), `RemY` (1/60 mm); `Vx,Vz,Vy` mm/s; `BodyYaw` ushort BAM; `Loco` byte; `Flags` ushort (Sprinting, OnSupport, SupportIsVehicle, ExtraJumpUsed, Wading, …); `CoyoteLeft`, `JumpBufferLeft`, `HaulOutTicks` byte; `SupportId`; `SupportLocalX/Y/Z`, `SupportYawRef`; `Pair[7]` push-pair state. Declare the support and push fields now (stable dump layout) even though P7/P8 are out of M-1a scope; the solver leaves them zero.
- Aim (camera yaw + pitch) lives in the player record next to `CarrierState`, not inside the solver state; the solver reads `LookYaw` from `CarrierInput`.
- Join binding: sim-core story 022 exposes an internal `IPlayerJoinListener` seam called by the `PlayerJoin` handler inside its call scope. Players binds it at build; the handler has already allocated the `player` `EntityId` and assigned the slot.
- Spawn uses world-stub's `PadSlots.Position` (do not re-derive the ring). Note the geometry difference to reconcile in the M-1b clock-in work: GDD F8's `slotOffsetMm[0]` is (1200, 0) but world-stub places slot 0 at (0, −1200) (ADR-0013 §3 convention); either is a valid ring for M-1a spawn, and the rotation/occupancy logic (F8, PCM-P17-2) is out of this epic's M-1a scope.
- `PlayerSettings` is a plain readonly struct/record with `int` fields only; do not read `BalanceTable` in the tick path (ADR-0010).
- Life state has no per-tick decrement anywhere: the fields are deadlines compared with `CurrentTick` by the (later) phase-3 timers.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002: `IRemoteCapsuleSource` / `ISupportTransformSource` ports, `CarrierEnv`, `CarrierScratch`.
- Stories 005–010: any movement code (`OwnedCarrier.Step`).
- Story 012: anchor registry methods on `IPlayersApi`. Story 019: `PlayersStep` (phase 3) and `CheckInteraction`.
- Knockdown, clock-in, standard issue, on-break, rejoin (P15–P19, F8) — deferred to M-1b / M0 (no stories this epic).
- `Teleport` and `JoinState` (ADR-0020, M0).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/PlayerJoinBindingTests.cs` (`[Trait("Id", "PCM-P1-1")]`), `tests/unit/player/PlayerSettingsTests.cs`, `tests/unit/player/PlayersDumpTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/player/PlayerJoinBindingTests.cs, PlayerSettingsTests.cs, PlayersDumpTests.cs (+ PlayersTestKit.cs)

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-018-id-allocators-slottable.md` (`EntityIds`, `SlotTable`), `production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md` (join listener seam), `production/epics/simulation-core/story-019-canonical-state-dump.md` (`ISimStateContributor`), `production/epics/simulation-core/story-025-composition-root-reflection-test.md` (`SystemGraph`), `production/epics/simulation-core/story-009-rng-registry-owner-views.md` (`SystemId`), `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (`PlayerBalance`), `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`IWorldQuery`, `Vec3Mm`), `production/epics/world-stub/story-005-post-office-spawn-pad.md` (`PadSlots.Position`, `PostOfficeRecord`)
- Unlocks: Stories 002, 005, 012, 019, 020; network-adapter-loopback local admission (a player exists after `PlayerJoin`)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 7/7 passing (PCM-P1-1, Settings binding, Spawn, Life-state data model, Leave, Dump, Composition) — all covered by automated tests
**Deviations** (advisory, TD-064): `[SimSystem(SystemId.PlayerCharacter)]` (no `SystemId.Players` exists; graph row is [WorldGeneration, GridInventory, HealthDamage]); `IWorldQuery` added as an injectable (`SimBuildArgs.World`, optional — no world → Players not built; ADR-0003 §4 amendment owed); 8 `player.*` balance keys added to the content-data owner table (slopeMaxPm 700 closes TD-060(1), terminalMmS, chestMm, subStepMm, maxSubStepsPerTick, slideIterations, maxAnchors, maxCarrierCandidates); `IPlayerJoinListener.OnPlayerJoined` gained `uint tick` (TD-052(3)); spawn faces outward (BodyYaw = LookYaw = slot·8192); capsule stored in the record, not dumped; SimBuildErrorCode 47/48, SimAssertCode 52/53, SectionIds.Players = 2.
**Test Evidence**: Logic — tests/unit/player/PlayerJoinBindingTests.cs, PlayerSettingsTests.cs, PlayersDumpTests.cs; 2 550 tests/leg green on net10/net8 × Debug/Release (Debug 16 skipped, optimised-only alloc guards); re-verified net8 Release 2 621/0 failed after other runners' additions. CarrierState 148 B blittable (pinned).
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (incl. godot-csharp .NET secondary), QL-TEST-COVERAGE ADEQUATE; LP 1/5/6/7 + QL 1/3 fixed in-story; rest TD-064.
