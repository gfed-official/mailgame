# Story 008: `ThrowingSystem` shell, the `FlightTable` (SoA, 160 slots, wrap-safe `FlightId`), the canonical dump section and the tick-end conservation record

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-29 (full `/story-readiness`, docs PR `docs/next-readiness-tc008-tc006`, main 310afef: NEEDS WORK → doc fixes applied; one batched row of Mail Lead planning defaults, **TD-195, pending Evan**; log `/workspace/repos/logs/tc008-readiness.txt`)

> **Op/host story (2 of 5).** The host-side state every other host story mutates: the live flights in creation order, the per-slot cooldown clock, and the always-on record that proves each flight resolved exactly once. No behaviour yet — launch is story 010, stepping is story 011.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T4 "The host creates the `FlightRecord`" table; T5 "Flights are processed in creation order"; T6 "Every flight ends in exactly one terminal state"; Edge Cases "If `flightId` wraps")
**Requirement**: `TR-throwing-009` (`FlightRecord` snapshot fields; `flightId` wrap-safe u16), `TR-throwing-013` (exactly one terminal per flight — the record half), `TR-throwing-008` (phase 4, 0 alloc, capacity), `TR-throwing-006` (creation order)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-E-6 c, TC-P-2 (0 B half, story 015), TC-X-1 (invariant half, story 015).)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§9 state, capacity, dump; §5 conservation) — Accepted 2026-10-16 (primary); ADR-0004 (`FlightId` u16, never compared numerically), ADR-0003 (`[SimSystem]`, one public constructor, `SystemGraph`, `ITickEndCheck`), ADR-0006 (`ISimStateContributor`, canonical dump, 0-alloc) secondary — Accepted
**ADR Decision Summary**: `FlightRecord` is SoA, preallocated: `FlightId`, entry ref + `mailIds` by reference (ADR-0009 Flight ledger), thrower slot, `classPts`, origin, vel, `launchTick`, `thrownPm` (snapshotted), `releaseTick`, `openAtRelease`, `hop`, `originClamped`. Capacity `maxFlights` = **160** (8 players × 6/s × 3 s = 144, plus slack); 16 is the perf fixture. Per slot: `TAT`, arm state, `lastCatch`. A flight leaves the live list only by resolving; an `ITickEndCheck` asserts `launched == resolved + live` and that no `FlightId` resolved twice (an always-on record, never throws). Throwing's `ISimStateContributor` writes live flights in creation order, the `FlightId` serial, per-slot `TAT` and arm state.
**ADR Version**: ADR-0015 2026-10-16; ADR-0004 2026-10-16; ADR-0003 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: each system lives in `Sim/<System>/`, is marked `[SimSystem(SystemId)]`, has exactly one public constructor with parameters only from the ADR-0003 §4 injectable list; dependencies declared in `Composition/SystemGraph.cs`; bind only phases you own (Throwing: phase 4 and the phase-0 op handlers)
- Required: bind an `ISimStateContributor` for any state; size every collection from settings; allocate 0 B in the tick path; ids ordered by raw `Value`, `FlightId` never compared numerically (ADR-0006 §4/§8, ADR-0004)
- Forbidden: an RNG stream in Throwing; statics or a service locator; subscribing synchronously to another system
- Guardrail: `maxFlights` = 160 preallocated; `FlightTable` operations O(1) amortised, allocation-free

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-E-6 c) and ADR-0015 §5/§9, scoped to this story:*

- [ ] **Shell and composition.** `ThrowingSystem` is `[SimSystem(SystemId.Throwing)]` with one public constructor taking only ADR-0003 §4 injectable parameters: `EventLogFacade`, `ISlotView`, `IInventoryApi`, `IMailApi`, `IPlayersApi`, `IWorldQuery`, `ThrowBalance` (the `ContentSet` sub-table; the constructor builds `ThrowSettings` from it, with an internal settings constructor for tests, as `InventorySystem.cs:162-173` and `PlayersSystem.cs:73-85` do), `SimCheckRing`, `SimCounters`. `IStatSheet` is not taken here (nothing in this story reads a stat; story 009 adds it with the first stat read). `SystemGraph.cs:53` already declares Throwing → PlayerCharacter, MailDestinations, GridInventory, BeltLogistics, HealthDamage — **no edge is added**: `IInventoryApi`, `IMailApi` and `IPlayersApi` are covered by the first three, and `IWorldQuery` needs no edge (ADR-0003 §4; `CompositionRulesTests.cs:496`). `SimComposition.ConstructShipped` gains the `SystemId.Throwing` case (`Composition/SimComposition.cs:190-219`; the M1aGreybox profile already lists Throwing, `SimCompositionTests.cs:449`). The composition-root reflection test (`production/epics/simulation-core/story-025-composition-root-reflection-test.md`) and the architecture rules pass with it. It binds `PhaseId.ThrowingStep` (an empty step here; story 011 fills it) and exposes `IThrowingApi { int LiveFlights { get; } void FlushAllAsTimeout(); }` (the flush body is story 013; until then `FlushAllAsTimeout` is a no-op that returns immediately — never `NotImplementedException`; no dedicated test: a no-op on an empty table is hollow under `.claude/rules/lean-testing.md`, and the reflection test covers the surface).
- [ ] **Capacity and fields.** `FlightTable.TryAppend(in FlightInit)` stores every ADR field (flightId, `EntryId`, units, `kindDef` u16 (the `ThrowLaunched` field), thrower slot, `classPts`, origin, vel, `launchTick`, `thrownPm`, `releaseTick`, `openAtRelease`, `hop`, `originClamped`) and returns an index; 160 appends succeed; the 161st returns false (the caller's `flight_cap` path, story 009) and is counted in `CounterId.ThrowingFlightCapRefusals = 65`.
- [ ] **Creation order.** Iteration is by array position = launch order. Resolving (removing) a middle flight and compacting stably preserves the relative order of the others; one seeded churn test (`Pcg32`; appends and removes that cross capacity several times, ≤ 160 live) keeps order equal to a reference list, and the same run never fires the tick-end check below.
- [ ] **TC-E-6 c (`FlightId` wrap).** The serial is a u16; after 70 000 launches (each resolved shortly after) the table still processes in creation order and no code path compares two `FlightId`s with `<`/`>` or sorts by them (`FlightId` has no relational operators, `IdTypesTests.cs:540`; add `CompareTo`/`Sort` on a `FlightId` to the existing `ThrowingRulesTests` token checks, no new test file; `FlightId` equality is allowed); a `FlightId` is never live twice (the allocator skips an id still live — impossible below 65 536 launches in flight; `SimAssertCode.ThrowingFlightIdStillLive = 88` under `SIM_ASSERT`, no test of its own). One test: the wrap run (more than 65 536 launches, GDD TC-E-6 c).
- [ ] **Per-slot clocks.** `Tat[slot]` (absolute tick, u32) is stored per `PlayerSlot`, initial 0, sized by `PlayerSlot.Count` (8), cleared to 0 in `IPlayerJoinListener.OnPlayerLeft` and `OnPlayerJoined` (`Core/IPlayerJoinListener.cs:21`; Throwing binds as the third listener with `SimWorldBuilder.BindPlayerJoinListener`, `SimWorldBuilder.cs:304`, which allows four; a rejoin reuses the slot and fires `OnPlayerJoined` again; `production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md`); one test: leave then rejoin leaves `Tat[slot] == 0`. Story 009 reads/writes it.
- [ ] **Tick-end record.** An `ITickEndCheck` asserts after phase 12 each tick that `launched == resolved + live` and that no `FlightId` resolved twice in the run; it writes `SimCheckRing.Record(CheckCode.ThrowingConservationMismatch = 17, tick, a, b)` (a = launched − resolved − live, b = the twice-resolved `FlightId` or 0) and adds 1 to `CounterId.ThrowingConservationMismatches = 64`, and **never throws** (always-on; the pattern is Grid's `Inventory/Ledger/ConservationCheck.cs:33-75`; `ITickEndCheck.Check(uint tick)`, `Core/ITickEndCheck.cs:17`). A test that resolves one flight twice or drops one without resolving it (through an internal `FlightTable` method, never the public API) sees the check fire (counter and ring entry); the churn test above shows a normal run never fires it.
- [ ] **Dump.** The Throwing `ISimStateContributor` section (`SectionIds.Throwing = 7`, after `Wallet = 6`; `tests/integration/mail/MailCompositionTests.cs:60` gains it after `SectionIds.Mail` in the M1aGreybox section order) writes live flights in creation order field by field, the `FlightId` serial, per-slot `Tat`, and a reserved block for the catch arm state; changing any single stored field (including `hop`, `openAtRelease`, `thrownPm`, `originClamped`) changes the dump hash; two tables reaching the same live set by different churn histories dump identically. One `[Theory]` (one row per stored field) plus the different-histories case: the single Throwing dump test.
- [ ] **0 B.** Constructing the table allocates its arrays once; one `AllocAssert` test: a churn of at least capacity append/remove cycles allocates 0 B (ADR-0006-required; reported, not blocking, until M0 per erratum E2).

---

## Implementation Notes

*Derived from ADR-0015 §9 and ADR-0003 §4:*

- **Mail Lead planning defaults, pending Evan (TD-195; readiness 2026-10-29).** Build against these; label each in code "Mail Lead planning default, pending Evan (TD-195 item n)": (1) `maxFlights` = `ThrowSettings.MaxFlights` 160, not a balance key; (2) the record keeps the `EntryId` and `Units` from `DetachResult`, read as ADR-0015 §9's "entry ref + `mailIds` by reference"; (3) ids: `SectionIds.Throwing = 7`, `CheckCode.ThrowingConservationMismatch = 17`, `CounterId.ThrowingConservationMismatches = 64`, `CounterId.ThrowingFlightCapRefusals = 65`, `SimAssertCode.ThrowingFlightIdStillLive = 88`. None contradicts an Accepted ADR; if Evan changes one, only the named id or setting moves.
- Files in `src/PerformativeMail.Sim/Throwing/`: `ThrowingSystem.cs`, `IThrowingApi.cs`, `FlightTable.cs` (struct-of-arrays; `int Count`; `ref readonly` accessors; `Remove(int index)` marks dead and `Compact()` stably closes gaps at the end of the step), `FlightInit.cs`, `FlightIdSerial.cs` (u16 counter; `FlightId` from `production/epics/simulation-core/story-002-id-value-types.md`), `ThrowingStateContributor.cs`, `ThrowingTickEndCheck.cs`, `ThrowSettings` wiring from story 001.
- Entry reference (resolved at readiness 2026-10-29 against grid-inventory 015/017 as built): the record stores the `EntryId` and `Units` that `IInventoryApi.DetachForThrow` returns (`DetachResult(InvReason Reason, EntryId Entry, byte Units)`, `Inventory/IInventoryApi.cs:54`); the mailIds stay in Inventory's arena under that live detached entry (`IInventoryApi.cs:59`; Flight ledger column, `production/epics/grid-inventory/story-017-unitledger-conservation.md`), and later hand-offs pass the `EntryId` (`FlightPayload(EntryId, Vec3Mm, bool)`, `IInventoryApi.cs:57-66`). The `EntryId` *is* ADR-0015 §9's "entry ref + `mailIds` by reference" (an as-implemented reading, TD-195 item 2; no erratum). No `FlightEntryRef` type and no Throwing-side unit list.
- Namespace and folder rules: every new file declares `namespace PerformativeMail.Sim.ThrowingCatching;` (the folder stays `Sim/Throwing/`; ADR-0015 erratum 2026-10-20; `tests/unit/architecture/ThrowingRulesTests.cs:20,45`), and `ThrowingRulesTests` bans `Math`, `float`, `double`, and the `/` and `%` operators in this folder (`:29`, `:113`): the u16 serial wraps by `unchecked((ushort)(n + 1))` and compaction uses index arithmetic only. Static descriptors and tables are get-only properties (mutable-static rule, `:186-199`).
- Capacity (Mail Lead planning default, pending Evan, TD-195 item 1): `maxFlights` is `ThrowSettings.MaxFlights` (int, 160), set by the `ThrowSettings` constructor and **not a balance key** — the `InventorySettings.EntryInitialLastId` precedent (`Inventory/InventorySettings.cs:81`). A `throw.maxFlights` balance key would change `ThrowBalance`, the knob list, `BuiltInContentTests.cs:487` and the M-1a content-hash golden (`ContentHashTests.cs:566`) for an engine capacity nobody tunes. The internal test constructor may set a smaller capacity (16 is the perf fixture); a value below 1 or above 65 535 throws `ArgumentOutOfRangeException` there (no new `SimBuildErrorCode`).
- `Tat` belongs to the Throwing dump section because the leaky bucket must replay identically (ADR-0015 §9).
- The tick-end check registers through `SimWorldBuilder.BindTickEnd` (`production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md`); the always-on record pattern mirrors Grid's conservation check (`story-017`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 009: validation chain and the bucket arithmetic. Story 010: launch and `ThrowLaunched`. Story 011: sweeping and terminals. Story 013: `FlushAllAsTimeout`. Story 015: the 8-player conservation fuzz and perf.
- `lastCatch` / T13 relay hops (M0) — the reserved dump block only.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-29, full mode: GAPS, nine items, folded into the Acceptance Criteria and Implementation Notes above.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/FlightTableTests.cs`, `tests/unit/throwing/ThrowingDumpTests.cs` (`[Trait("Id", "TC-E-6c")]`) — must exist and pass on net10.0 and net8.0; edits: `tests/unit/architecture/ThrowingRulesTests.cs` (`FlightId` comparison tokens), `tests/integration/mail/MailCompositionTests.cs:60` (section order). Lean testing (`.claude/rules/lean-testing.md`): one behaviour test per AC plus the ADR-required dump and 0 B tests; no constant, enum or counter-count tests.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; `production/epics/simulation-core/story-002-id-value-types.md`; `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md`; `production/epics/simulation-core/story-018-id-allocators-slottable.md`; `production/epics/simulation-core/story-019-canonical-state-dump.md`; `production/epics/simulation-core/story-025-composition-root-reflection-test.md`; `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md` (`DetachForThrow`, `DetachResult`)
- Unlocks: Stories 009, 010, 011, 012, 013, 015, 022
