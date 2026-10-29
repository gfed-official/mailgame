# Control Manifest

> **Engine**: Godot 4.7.1 .NET (`4.7.1.stable.mono.official.a13da4feb`), C# — Sim and Client.Core on `net8.0`, tests on `net10.0` (+ `net8.0` leg)
> **Last Updated**: 2026-10-17
> **Manifest Version**: 2026-10-17.1
> **ADRs Covered**: ADR-0001 … ADR-0011, ADR-0012, ADR-0013, ADR-0014, ADR-0015, ADR-0016, ADR-0017, ADR-0019, ADR-0026
> **Revision 2026-10-16.2**: targeted amendment adding the rules of ADR-0012–0017, 0019 and 0026, accepted 2026-10-16. Stories stamped `2026-10-16` that touch Input, Players, World, Throwing, Mail, ClientLayer previews or Godot scenes must re-check against this revision.
> **Revision 2026-10-17.1**: owner-accepted amendments — ADR-0012 §7 D-1…D-8 rules for the Godot input adapter layer (last-child-of-root placement, mouse capture as a request, handled-state symmetry, window focus subscription, stock `ui_*` pad bindings, `Device` int vs `DeviceId*` long, connected-pad-only queries, per-event allocation/retention budget); ADR-0026 corner-height bound (±1,000,000 mm), far-edge sampling rule and Post Office `Build` overload rules; plus the 2026-10-17 ADR errata (ADR-0003/0004/0005/0006/0013) already noted. Stories stamped earlier that touch the Godot input adapter or `WorldTablesBuilder` must re-check against this revision.
> **Errata 2026-10-17** (text only, version unchanged): RNG handle count 30 → 21; `rngIndexOutOfRange` counted in every build; `drops` owner = Health & Damage — per the ADR-0005 errata of the same date (TD-010).
> **Errata 2026-10-20** (text only, version unchanged): ADR-0014 erratum of the same date — `CarrierStatus.OccupiedSeat` (`AnchorRef`, `None` unless Mounted) defines the §2 Mounted filter's "own seat"; plus the owner's §3 swim-catch ruling of the same date (a swimmer's press order is unchanged; the host refuses the arm as ineligible). No extracted rule changes (the manifest carries no Mounted-filter rule, and the press-order rule holds for swimmers too).
> **Errata 2026-10-20** (text only, version unchanged): per the ADR-0015 and ADR-0017 errata of the same date (both accepted by Evan), two folders are exceptions to ADR-0001 §1's "Namespace = `PerformativeMail.Sim.<Folder>`": `Sim/Throwing/` uses `PerformativeMail.Sim.ThrowingCatching` (`Sim.Throwing` would shadow `Content.Rules.Throwing`, CS0234) and `Sim/Mail/` uses `PerformativeMail.Sim.MailDestinations` (`Sim.Mail` would shadow `Content.Rules.Mail` / `BalanceKeys.Mail`). The folders are unchanged.
> **Errata 2026-10-21** (text only, version unchanged): ADR-0012 and ADR-0014 errata of the same date (owner ruling, Evan) — panel-open travels in the `InputFrame` as `InputFrameFlags.PanelOpen` (one appended bit; frame size unchanged), which the router sets while its overlay is Panel and Players copies into `CarrierStatus.PanelOpen`. No extracted rule changes (the manifest carries no rule on the Panel source).
> **Errata 2026-10-21** (text only, version unchanged): ADR-0009 probe-counter wording clarified per the owner ruling of 11:43 AM PT — a placement scan counts per cell (each footprint cell examined, never one per anchor); a candidate visit is an entry, list node or FIFO node inspected; rule unchanged.
> **Errata 2026-10-21** (text only, version unchanged): ADR-0009 erratum of the same date (grid-inventory 013, owner-approved by Evan 8:11 PM PT) — WorldItem expiry uses five FIFOs (`waterFloat` split into floating mail and floating non-mail); the non-mail cap candidate is the lower `(expiresTick, id)` of the `nonMailWorldItem` and floating non-mail heads, floating mail never; a mail head qualifies at age ≥ `capEvictMinAgeSeconds` (≥ 300 ticks, Evan 9:16 PM PT); `ItemsLost{itemDef, count}` joins the records; world records 0x0086–0x0089 are `Replicated`; an H-PERF-6d visit is one FIFO node read — already-checked heads and queue clean-out reads are not visits — and the gate stays ≤ created + 1.
> **Errata 2026-10-22** (text only, version unchanged): ADR-0009 and ADR-0017 errata of the same date (owner-approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT — "to all recommended decisions", then "Yes, approve all of it including the ADR text changes") — ADR-0009 Key Interfaces define `FlightPayload`/`CatchResult`, `EntryRef`, `DetachResult`, `ConsumeReason`, mail-only `TryInsert`, `WeightPoints`/`SpeedPermille`; ADR-0009 §5's "API caller gets `false` plus an always-on record" applies to `IInventoryApi` calls, an internal put-away (leash) refusal records its no-fit facts only; ADR-0017 §6 ships one backlog ring at M-1a (ADR-0025's per-footprint sub-queues once that ADR is written) and its Key Interfaces define `BacklogEntry`, `AppendBacklog`, `BacklogUnits`, `BacklogEntries`. No rule below changes.
> **Errata 2026-10-22** (text only, version unchanged): ADR-0009 §6 erratum of the same date (grid-inventory 017, owner-approved by Evan 2026-10-22 6:41 PM PT — "Approve the ADR-0009 l.170 erratum as drafted, including the l.171 never-throws clarification") — `InventoryMutator` is the `UnitLedger`'s only writer (its methods listed in the ADR, including the ledger-only `EnterHolder`/`LeaveHolder`); the `IInventoryApi` crossing rule binds systems outside Inventory, and Inventory's `WorldItems` and the internal `TryAppendDetached` make World and Backlog crossings directly.
> **Errata 2026-10-22** (text only, version unchanged): ADR-0014 §2 l.66 erratum of the same date (player-character 014, approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat) — "Approve the ADR-0014 l.66 erratum and all six 014 defaults as recommended") — focus is computed at `PreviewSubStep.Focus` (4a) of ClientLayer's `Previews` step, after `ClientStepId.CarrierStep` has run `OwnedCarrier.Step`, as ADR-0014 §7 l.114 and ADR-0016 §2 already say; l.66's "at the end of Players' `CarrierStep` binder" was a leftover. No extracted rule changes (the manifest already places focus at 4a, l.214).
> **Errata 2026-10-28** (text only, version unchanged): per the ADR-0003 §4 and ADR-0017 §8 errata and the ADR-0007 §4 table amendment of the same date (all approved by Evan 2026-10-28 7:26 AM PT, Mail Lead chat, card 'Mail-destinations 005 decisions': "Approve the ADR-0003 §4 and ADR-0017 §8 errata: the composition root injects IWalletGate into Mail only, and SystemGraph is unchanged" and "Approve all six TD-177 defaults for mail-destinations 005, including the ADR-0007 §4 economy event block row"; TD-177) — the `OpKind` block rule gains Economy `0x0180–0x01BF`, with later systems from `0x01C0`; the Feature-layer injectable list gains `IWalletGate` (Mail & Destinations only; ADR-0017 §8 port, injected by the composition root, not a `SystemGraph` edge) and `IWorldQuery` (any system, ADR-0026 — the ADR-0003 §4 erratum of 2026-10-17, which the 2026-10-17.1 revision had not carried into that list). Recorded by mail-destinations 005's readiness PR (Mail Review PR #54 r1 advisory 2).
> **Status**: Active — regenerate with `/create-control-manifest update` when ADRs change

`Manifest Version` is the date this manifest was generated. Story files embed
this date when created. `/story-readiness` compares a story's embedded version
to this field to detect stories written against stale rules. Always matches
`Last Updated` — they are the same date, serving different consumers. A second revision on the same date takes a `.N` suffix (`2026-10-16.2`).

This manifest is a programmer's quick-reference extracted from all Accepted ADRs,
technical preferences, and engine reference docs. For the reasoning behind each
rule, see the referenced ADR. Where two ADRs disagree, see
[§ Cross-ADR inconsistencies](#cross-adr-inconsistencies-follow-the-owning-adr)
— follow the ADR named there.

**Layer map.** Layers follow `docs/architecture/architecture.md` §System Layer Map (v1.2), not a generic engine layering:

| Layer | Systems | Assemblies |
|---|---|---|
| Foundation | Simulation Core (Primitives, Geometry, Core, Hosting, Composition, ClientLayer shell + `Previews` composite), Content & Data (incl. StatSheet), Input & Controls, Godot host shell (`GameLoop`, `ContentHost`, boot), build & test layout | `PerformativeMail.Sim`, `PerformativeMail.Game` (host shell only), `PerformativeMail.Sim.Tests` |
| Core | Grid Inventory & Containers (+ WorldItem), Health & Damage, World Generation, Networking & Session (adapter seam), Profile & Settings Persistence | `Sim/Inventory`, `Sim/Health`, `Sim/World`, `Client.Core/Net` |
| Feature | Sim halves of Player Character, Mail, Manual Delivery, Throwing, Building, Belts, Vehicles, Combat, Enemy AI, Economy, Run Structure, Perks, Stamps, Meta, Comms | `Sim/<System>/` |
| Presentation | Godot views, HUD, UI, audio, VFX, camera, input adapter, `Client.Core` (Ui, Input router) | `PerformativeMail.Game` (`src/Game/**`), `PerformativeMail.Client.Core` |

---

## Toolchain & Commands (read first)

*Facts verified on the dev machine 2026-10-16. Sources: ADR-0001 §2–§3, §7; `tests/README.md`; `global.json`; `project.yaml` `commands.test`.*

- **`dotnet` is not on `PATH`.** Run every .NET command as `mise exec dotnet@10.0.402 -- dotnet …`. Installed via mise: SDKs **10.0.402** and **8.0.426**; runtimes **Microsoft.NETCore.App 10.0.13** and **8.0.32**. — source: tests/README.md, ADR-0001 §2
- **Build / test:** `mise exec dotnet@10.0.402 -- dotnet build` and `mise exec dotnet@10.0.402 -- dotnet test` (= `project.yaml` `commands.test`). — source: tests/README.md
- **Test runner:** `dotnet test` runs **xUnit v3 (`xunit.v3` 4.0.2) on Microsoft.Testing.Platform** (`global.json` `"test": {"runner": "Microsoft.Testing.Platform"}`; the .NET 10 SDK refuses VSTest for xUnit v3). Exit codes: **0** all passed, **2** a test failed, **8** zero tests ran (**not** a pass). Filter: `dotnet test -- --filter-class "*GridPlacementTests"` / `-- --filter-method "*H_R4_1a*"`. — source: tests/README.md, ADR-0001 §7
- **`global.json`** pins SDK `10.0.100` with `rollForward: latestFeature` (resolves to 10.0.402). — source: global.json
- **Godot:** the 4.7.1 .NET (mono) editor is `~/.local/bin/godot-mono` (`4.7.1.stable.mono.official.a13da4feb`). Godot needs `dotnet` on `PATH`, so launch it as `mise exec dotnet@10.0.402 -- godot-mono …` (e.g. `--build-solutions`). The plain `godot` in `project.yaml` `commands.run/smoke` is not on `PATH`. — source: ADR-0001 §2
- **Once `PerformativeMail.Game.csproj` exists at the repo root**, a bare `dotnet build`/`dotnet test` is ambiguous: name the solution (`dotnet test --solution PerformativeMail.sln`). The story that creates the game csproj updates `project.yaml` `commands.test` and `tests/README.md`. Today there is no root csproj, so the bare command works. — source: ADR-0001 §3
- **The .NET 8 runtime (8.0.32) is now installed locally**, so the `net8.0` test leg (`-p:PmTestTfm=net8.0`) can run locally once the test csproj supports it. ADR-0005/0006's "net8.0 leg reported NOT RUN locally" applies only to a machine without the .NET 8 runtime; such a run is still reported NOT RUN, never passed. — source: ADR-0001 §7, ADR-0005, ADR-0006 §8
- **Scaffold gaps the first Sim Core story (M-1a) must close** (each is mandated by an Accepted ADR and not yet present): `Directory.Build.targets` (`SimAsserts` → `SIM_ASSERT;INVENTORY_ASSERT` + `CheckForOverflowUnderflow`); the `ExportRelease`/`ExportDebug` `Optimize` mapping in `Directory.Build.props`; the `PmTestTfm` override in the test csproj; the `net8.0` CI leg; `Microsoft.CodeAnalysis.BannedApiAnalyzers` + `BannedSymbols.txt` + `dotnet_diagnostic.RS0030.severity = error`; the custom `SpecialType` float/double/decimal/Half analyzer (`tools/PerformativeMail.Analyzers/`); the SC-ARCH-2 fixtures, each seen failing before the gate counts; `tests/unit/architecture/AssemblyRulesTests.cs`; the `using Godot` grep guard. — source: ADR-0001 §4, §6–§8, ADR-0006 §3

---

## Foundation Layer Rules

*Applies to: Simulation Core (Primitives, Geometry, Core, Hosting, Composition, ClientLayer shell + `Previews` composite), Content & Data and StatSheet, the Godot host shell (`GameLoop`, `ContentHost`, boot), the solution/build/test layout.*

### Required Patterns

#### Assemblies, build configurations, asserts (ADR-0001)
- **`PerformativeMail.Sim` lives at `src/PerformativeMail.Sim/`, targets `net8.0`, and references the BCL only** (plus analyzers with `PrivateAssets="all"`). Namespace = `PerformativeMail.Sim.<Folder>` *(exception: see Errata 2026-10-20)*; `Primitives/` depends on nothing else in the Sim; `Core/`, `Hosting/`, `Composition/`, `ClientLayer/` hold Sim Core; `Content/` holds content; one folder per system. — source: ADR-0001 §1
- **`PerformativeMail.Client.Core`** (`src/PerformativeMail.Client.Core/`, `net8.0`, references only the Sim) is created by the first Input, HUD or network-seam story. — source: ADR-0001 §1
- **The Godot game project is `PerformativeMail.Game.csproj` at the repo root, hand-written** (`Sdk="Godot.NET.Sdk/4.7.1"`, `net8.0`, `EnableDynamicLoading=true`, `RootNamespace` `PerformativeMail.Game`), with Godot code under `src/Game/**`; `project.godot` sets `[dotnet] project/assembly_name="PerformativeMail.Game"`. 4.7.1 `--build-solutions` builds but does not create the csproj. — source: ADR-0001 §1–§2
- **The game csproj excludes `src/PerformativeMail.*/**;tests/**;tools/**;prototypes/**;**/obj/**;**/bin/**` from its compile glob and references Sim/Client.Core by `ProjectReference` only.** — source: ADR-0001 §2
- **`PerformativeMail.sln` is Godot-free** (Sim, Client.Core, tests, `tools/*`); CI builds and tests only it. An IDE-only `PerformativeMail.Game.sln` must declare `Debug`, `ExportDebug`, `ExportRelease` configurations. — source: ADR-0001 §3
- **Build configurations:** `Debug` (asserts + checked), `Release` (optimised, no asserts, unchecked — CI, perf, tools), `Release -p:SimAsserts=true` ("ReleaseAssert", nightly soaks), `ExportDebug` (as Debug), `ExportRelease` (optimised via the props mapping). — source: ADR-0001 §4, ADR-0006 §3
- **`Directory.Build.props` maps `ExportRelease` → `Optimize=true` and `ExportDebug` → `Optimize=false` + `DebugSymbols=true`; `Directory.Build.targets` derives `SimAsserts` from `$(Optimize)` (after the SDK sets it) and, for Sim and Client.Core, defines `SIM_ASSERT;INVENTORY_ASSERT` and `CheckForOverflowUnderflow=true` when `SimAsserts` is true.** Use the exact XML in ADR-0001 §4. — source: ADR-0001 §4
- **Sim/Client.Core code uses `SIM_ASSERT` / `<SYSTEM>_ASSERT` only** for build-dependent behaviour; `SIM_ASSERT` turns on every per-system alias. — source: ADR-0001 §4
- **`SimAssert`** (`Primitives/`) is a stateless static class of `[Conditional("SIM_ASSERT")]` methods `That(bool, ushort code)` / `Fail(ushort code)`; per-system aliases are `[Conditional("<SYSTEM>_ASSERT")]` overloads in the owning folder; arguments must be pure; every call site is inside Sim or Client.Core. — source: ADR-0001 §5
- **`Sim.AssertsEnabled`** is a `public const bool` (true under `SIM_ASSERT`), logged at start; a Release test asserts it is `false`. — source: ADR-0001 §5, §8
- **Always-on checks write to the per-world `SimCheckRing`** (`Core/`, never static; capacity 256; `{ushort code, uint tick, long a, long b}`; one record per code per 30 ticks + suppressed count). — source: ADR-0001 §5
- **`SimCounters`** (`Core/`) is per `SimWorld`: a `long[]` indexed by `CounterId` (`ushort` enum, appended, never renumbered), readable between ticks. Full-scan invariants are assert-only. — source: ADR-0001 §5
- **Analyzers in Sim/Client.Core use `PrivateAssets="all"` and pinned versions**; the first is `Microsoft.CodeAnalysis.BannedApiAnalyzers` with `BannedSymbols.txt` in each project folder. A custom analyzer lives in `tools/PerformativeMail.Analyzers/` (netstandard2.0). — source: ADR-0001 §6
- **Tests: one xUnit v3 project, `net10.0`, compiling `tests/unit/**` and `tests/integration/**`, one folder per system**; shared fixtures in the project folder; the csproj accepts `-p:PmTestTfm=net8.0`. `[PERF]` and 0-alloc tests run in a non-parallel collection; the 0-alloc gate runs in a dedicated process. — source: ADR-0001 §7
- **New tests go in `tests/unit/<system>/` or `tests/integration/<system>/`, one class per rule**: class `[System]Tests`/`[Rule]Tests` in a same-named file, namespace `PerformativeMail.Sim.Tests.Unit.<System>` / `.Integration.<System>`, methods `Scenario_Expected`, GDD test ids in the name or `[Trait("Id", "H-R4-1a")]`, Arrange/Act/Assert. — source: ADR-0001 §7, tests/README.md
- **CI guards:** `tests/unit/architecture/AssemblyRulesTests.cs` (Sim and Client.Core reference no `Godot*` assembly; Sim does not reference Client.Core; no runtime `PackageReference`); grep guard `grep -rn "using Godot" src/PerformativeMail.Sim src/PerformativeMail.Client.Core` fails the build; composition reflection test in `tests/unit/architecture/`; Release leg asserts `Sim.AssertsEnabled == false`. — source: ADR-0001 §8
- **CI legs:** `net8.0` leg (setup-dotnet 8.0.x + 10.0.x, `-p:PmTestTfm=net8.0`) from the first Sim Core story (M-1a); `windows-latest` leg at M0. The golden transform hash and canonical dump must match across all legs. — source: ADR-0001 §7
- **`src/.gdignore`, `tests/.gdignore`, `tools/.gdignore`, `prototypes/.gdignore` keep the editor scan out of non-Godot folders**; the story that creates `src/Game/` moves `src/.gdignore` into each `src/PerformativeMail.*` folder. — source: ADR-0001 §2
- **Godot-side C# qualifies `System.Environment`** (and any other `System`/`Godot` name clash) explicitly; `ImplicitUsings` stays on. — source: ADR-0001 Implementation Guidelines
- **Culture-free behaviour comes from API choice + the analyzer**; `InvariantGlobalization` stays in `Directory.Build.props` but is not relied on (editor runs ignore it). — source: ADR-0001 §6

#### Tick driver, FrameRunner, GameLoop (ADR-0002)
- **`TickDriver` and `FrameRunner` live in `PerformativeMail.Sim/Hosting/`, Godot-free; `TickDriver` never reads a clock.** — source: ADR-0002 §1–§2
- **Tick rate is the compile-time constant `TickRate.Hz = 30`** (`Hosting/TickRate.cs`); variable-step simulation is forbidden. — source: ADR-0002 §1
- **`new TickDriver(long frequency, int maxCatchUpTicksPerFrame)`**: `frequency` given once by the host, 10^3..10^12 else `ArgumentOutOfRangeException` at construction; `maxCatchUpTicksPerFrame` from `SimSettings` (default 4, safe 3–8). — source: ADR-0002 §1
- **`Advance(long now)` implements the GDD F2 two-step form exactly** (ADR-0002 §1 pseudocode): Δ<0 counted `backwardsClock` and treated as 0; Δ clamped to 1 s (`clampedTimeTotal`); `n = min(due, maxCatchUp)`; excess dropped (`droppedTicksTotal`, `acc30 %= freq`, `hitches++`), never banked; returns 0 while `Paused` or `Idle`. — source: ADR-0002 §1
- **`Start(now)`, `Resume(now)`, `Resync(now)` set `last = now; acc30 = 0`**; `Resync` is legal only in `Running`, after known blocking work, and writes no hitch. `DropRemaining(m)` moves `m` from run to dropped. — source: ADR-0002 §1
- **`AlphaPermille = acc30 × 1000 / freq` (0–999), frozen at 0 while paused, is Presentation-only.** — source: ADR-0002 §1
- **`FrameRunner.RunFrame()` order:** (1) `HostAdapter.PumpInbound()` (remote `Poll`, then loopback `Poll`); (2) `n = driver.Advance(clock.Timestamp)`; (3) per tick: `client.Step(isFirstTickInBatch: i == 0)`, `world?.Tick()`, `HostAdapter.FlushOutbound(tick)`; M0 guard drops the rest when `catchUpGuardMs` elapsed (at least one tick always runs); (4) one `TickHitch` `SimCheckRing` record if hitches increased. — source: ADR-0002 §2, ADR-0011 §2 and Key Interfaces (names per I-7)
- **`catchUpGuardMs`: 0 (off) at M-1a; 10 ms at M0, constrained to `≥ 2 × tickBudgetP99(host)`.** — source: ADR-0002 §2
- **Input edges attach only to the tick with `isFirstTickInBatch == true`**; later catch-up ticks carry held bits and the latest `moveIntent`; a 0-tick frame keeps edges latched in order; a dropped tick never discards an edge. — source: ADR-0002 §2
- **Exactly one `GameLoop : Node` per process** (`src/Game/Hosting/GameLoop.cs`): `ProcessMode = Always` in `_Ready`; `_Process` calls `_runner.RunFrame()` then `_presentation.Present(_runner.Driver.AlphaPermille)`; lowest `ProcessPriority` (`int.MinValue`); main-thread (or inherited) `ProcessThreadGroup`. — source: ADR-0002 §3
- **`TickDriver.Start(now)` is called on the first `_Process` after boot and world load complete**, not in `_Ready`. — source: ADR-0002 §3
- **Time source is `StopwatchMonotonicClock : IMonotonicClock`** (`Stopwatch.GetTimestamp()`, `Stopwatch.Frequency`) in `src/Game/Hosting/`. — source: ADR-0002 §3
- **Project settings: `application/run/low_processor_mode` off; `physics/common/physics_interpolation` false.** — source: ADR-0002 §3
- **Pause is a `TickDriver` state only** (`Pause()` → `Advance` returns 0; `Resume(now)` clears the accumulator, no burst); allowed only when Networking reports no remote peers (refused while any peer is in any handshake state); Networking calls `Resume()` at the first connect callback before enqueueing `PlayerJoin`. Multiplayer never pauses. — source: ADR-0002 §4
- **v1 runs the whole Sim on the Godot main thread.** `SimWorld` captures its owner thread at `Freeze()`; `SIM_ASSERT` builds assert it in `Tick()`, `EnqueueOp()`, the sample-ring drain and `RunFrame`. — source: ADR-0002 §5
- **Transport callbacks only enqueue into the adapter's own inbound queue**; step 1 of `RunFrame` drains it on the main thread (`opSeq` and `receivedTick` stamping stay main-thread). — source: ADR-0002 §5, ADR-0011 §5
- **`GameLoop` catches `SimBuildException` at boot and any exception from `RunFrame`**, shows the error, ends the session and stops calling `RunFrame`. — source: ADR-0002 Implementation Guidelines
- **Headless hosts (xUnit, BalanceSim, BotClient) have no driver: they call `SimWorld.Tick()` (and `ClientLayer.Step()`) in a plain loop.** — source: ADR-0002 §5
- **`TickDriver` tests use integer timestamps** and include the F2 invariant property test and the GDD F2 worked examples (freq 10^7: 150 ms → 4/0; again → 4/1; 400 ms → 4/8; 60 fps → alternating 0/1, alpha 500/0 ‰). — source: ADR-0002 Implementation Guidelines

#### Phase order, composition, lifecycle (ADR-0003)
- **Phases run exactly in this order** (`enum PhaseId : byte`): 0 `InputsApply`, 1 `ItemsExpire`, 2 `IntakeDrain`, 3 `PlayersStep`, 4 `ThrowingStep`, 5 `AutomationStep`, 6 `VehiclesStep`, 7 `AgentsStep`, 8 `MailStep`, 9 `EconomyFlush`, 10 `CombatResolve`, 11 `CombatReact`, 12 `RunStep`, then tick-end checks, then 13 `EventsFlush`. A new phase amends ADR-0003 and Sim Core R3. — source: ADR-0003 §1
- **`SimWorld.Tick()` follows the ADR-0003 §2 sequence**: guard (Running, owner thread, not re-entrant; else count `badTick`), sample-ring prologue, phases 0–12, tick-end checks, `EventsFlush`, `CurrentTick++`, apply a latched stop. An exception escaping a phase → `Faulted`, partial log not delivered; `SIM_ASSERT` builds rethrow. — source: ADR-0003 §2
- **`TickContext` is a `readonly struct { uint Tick; PhaseId Phase; }` passed `in`; services arrive by constructor.** — source: ADR-0003 §2 (see inconsistency I-2)
- **`IPhaseProbe` defaults to a null object and is never null-checked per call.** — source: ADR-0003 §2
- **`SimWorldBuilder` is used only in `Constructed`** (`Bind`, `BindTickEnd`, `BindOp(OpDescriptor, IOpHandler)`, `BindEventKind(EventDescriptor)`, `BindEventReader(ReaderId, EventKind[])` (≤ 8), `BindStateContributor`, `BindSnapshotSource`); `Freeze()` validates, copies tables to arrays, captures the owner thread and moves to `Ready`. `Build` and `Freeze` run on the thread that will call `Tick()`; async content loading only before `Build`. — source: ADR-0003 §3, ADR-0007 §4, ADR-0008 §4 (names per I-3)
- **Build errors throw `SimBuildException { Code }`** (`DuplicateOpHandler`, `MissingOpHandler`, `ReaderLimit`, `BindAfterFreeze`, `ReservedPhase`, `DependencyCycle`, `UndeclaredDependency`, `DuplicateSystem`, + descriptor codes). — source: ADR-0003 §3
- **One composition root: `SimComposition.Build(in SimBuildArgs args) → SimHost { SimWorld World; ClientLayer Client; }`**, `SimBuildArgs = { SimProfile, ContentSet, ulong runSeed, SimInitialCounters? }`. — source: ADR-0003 §4
- **Composition is explicit ordered code in `SimComposition`, in a topological order of `Composition/SystemGraph.cs`** (dependencies as data; a test checks the order and acyclicity). Composition order fixes construction, same-phase binder, tick-end and dump order. — source: ADR-0003 §4
- **`SimProfile` selects the built systems** (`M1aGreybox` = Grid, Players, Throwing, Mail, Manual Delivery, Run stub; `Full`; per-test profiles). — source: ADR-0003 §4
- **Lifecycle:** `Constructed → Ready (Freeze) → Running (first Tick) → Stopped | Faulted`; `EnqueueOp` is accepted only in `Ready`/`Running`. Pause is not a world state. — source: ADR-0003 §7
- **`ClientLayer` runs bound `IClientStep`s in `ClientStepId` order `MirrorDrain`, `InputLatch`, `CarrierStep`, `Previews`** via `ClientLayerBuilder.Bind`. — source: ADR-0003 §6
- **The phase loop is allocation-free: arrays and `for`, no LINQ or delegates.** — source: ADR-0003 Implementation Guidelines
- **Static-state architecture test:** fails on any Sim static field that is not `const` or `static readonly` of an immutable type (primitive, `string`, `[ImmutableTable]`); constant tables are `ReadOnlySpan<T>` literal properties or `[ImmutableTable]` wrappers; `[CompilerGenerated]` types excluded. — source: ADR-0003 §4

#### Ids (ADR-0004)
- **Use the id wrapper structs (`EntityId`, `ActorId`, `MailId`, `PlayerSlot`, `EntryId`, `FlightId`, `DefIndex`, `StringId`) in every API that crosses a system boundary**; bare `uint`/`ushort` only inside the struct's storage and in op/event payload codecs. Layouts, none values and ranges are ADR-0004 §1. — source: ADR-0004 §1, Implementation Guidelines
- **`EntityId = ((uint)cls << 24) | counter`** (counter 1…2^24−1, None = 0); every class shift is written `(uint)cls << 24`; `EntityId.Compose` asserts `counter ≤ 0xFF_FFFF` and masks. — source: ADR-0004 §1, Implementation Guidelines
- **`EntityClass` (byte, append-only):** 0x00 None, 0x01 Player, 0x02 WorldItem, 0x03 Construct, 0x04 Vehicle, 0x05 Enemy, 0x06 Npc, 0x07 Destination, 0x08 Prop; 0x09–0xEF reserved (`Allocate` returns None + record); 0xF0–0xFF test-only via `EntityIds(allowTestClasses: true)`. Mail is not an entity class. — source: ADR-0004 §2
- **`EntityIds` and `MailIds` are per-world instances injected by the composition root**; allocation only on the owner thread inside `Tick()` (exception: `Destination`/`Prop` while `Constructed`); Release illegal call returns None and counts `idAllocIllegal`. — source: ADR-0004 §3
- **Exhaustion returns None/0, writes one `IdExhausted` check record and asserts; the caller refuses the spawn.** Allocators test for exhaustion before incrementing. — source: ADR-0004 §3, Implementation Guidelines
- **Order ids by raw `Value` ascending** (`EntityId`, `MailId` implement `IComparable<T>` on `Value`; lower `MailId` = older). — source: ADR-0004 §3
- **Every id struct implements `IEquatable<T>`, `Equals(object)`, `GetHashCode()`, `==`/`!=` explicitly**: single-field ids hash as `(int)Value`; `StringId` as `((int)Ns << 16) | Index`; no `System.HashCode`. — source: ADR-0004 Implementation Guidelines
- **`PlayerSlot` (0–7) and `DefIndex` use `+1` storage so `default` is None**; `PlayerSlot.Value` reads `0xFF` for None, `DefIndex.Value` reads `0xFFFF`; `FromValue(0xFF/0xFFFF)` special-cases None. — source: ADR-0004 §1, §6
- **`SlotTable` (`Core/`) is `PlayerSlotEntry[8]`**; a slot freed in tick T is reassignable only when `CurrentTick ≥ T + 1`; `PlayerJoin` rejects `slot_busy`, `slot_invalid`, `duplicate_player`, allocating nothing. — source: ADR-0004 §4
- **Real `EntryId`s keep bit 31 clear; temp ids are exactly `0x8000_0000 | slot << 20 | ctr`, `ctr < 2^20`.** — source: ADR-0004 §1
- **`SerialU8/U16/U32` (RFC 1982) compare wrapping ids; `FlightId` is compared only through `SerialU16`; flights are processed in list (creation) order.** — source: ADR-0004 §5
- **Narrowing to a wire width goes through `WireNarrow.ToU16/ToU8`** (assert in `SIM_ASSERT`, clamp and record in Release); a narrow wire id comes from an owner-maintained id map. — source: ADR-0004 §5
- **`DefIndex` = ordinal of the def id in its type's `StringComparer.Ordinal`-sorted list** (≤ 65 534 per type); `StringId = (StringNamespace ns, ushort index)`; run-time strings are interned per run under `Run`. Per-tick code looks up by `DefIndex` into typed tables, never by string. — source: ADR-0004 §6, ADR-0010 §3

#### RNG (ADR-0005)
- **Implement `Fnv1a64`, `Mix64` (full SplitMix64 step including the `+0x9E3779B97F4A7C15` gamma), `Derive(runSeed, name, index) = Mix64(Mix64(runSeed ^ Fnv1a64(name)) ^ index)`, PCG32 `pcg32_srandom` / `Step` / `Next32` exactly as ADR-0005 §1**, every line inside `unchecked(...)` (including the `(uint)(((old >> 18) ^ old) >> 27)` narrowing); rotate written `(32 - rot) & 31` with `int rot`. — source: ADR-0005 §1, Implementation Guidelines
- **`NextInt(uint b) = (uint)(((ulong)Next32() * b) >> 32)` — exactly one draw per call**, `b = 0` returns 0 (Debug asserts); Bernoulli p‰ is `NextInt(1000) < p`; list choice is `list[NextInt((uint)list.Count)]`. — source: ADR-0005 §4
- **Streams come only from `RngNames` (static readonly `StreamName` fields; v1: `worldgen.<stage>`, `mail` 1–5, `mail.special` 1–5, `waves`, `mega`, `drops`, `perks`, `shop`, `events` 1–5)**; a new stream amends the ADR-0005 table + one field; `Fnv1a64` computed once at type init from a `"…"u8` literal. — source: ADR-0005 §2
- **`RngRegistry` is per `SimWorld`**, pre-allocates every `(name, index)` handle (21 in v1 — 9 names) at composition; `Create` re-seeds the existing handle in place, allocating nothing; out-of-range index counts `rngIndexOutOfRange` in every build, then asserts in Debug (Release falls back to a spare/refused stream). — source: ADR-0005 §3
- **The `Pcg32` field inside `RngStream` is not `readonly`.** — source: ADR-0005 Implementation Guidelines
- **Each system receives an owner-filtered `IRngStreams` view of only the names it owns** (architecture test SC-ARCH-1); Throwing receives none. — source: ADR-0005 §3
- **Each `RngStream` counts `draws` (u64), +1 per `Next32`/`NextInt` including `NextInt(0)`**; the dump records `(name ordinal, index, state, inc, draws)` in `RngNames` order then index. — source: ADR-0005 §3
- **Tests inject an `IRngSource` stub through the `RngStream` test constructor**; production code never sees the stub. — source: ADR-0005 §3
- **`tests/unit/sim/Core/RngGoldenVectorTests.cs` asserts every V1–V6 golden vector verbatim, in Debug and Release, on net8.0 and net10.0**; a startup test asserts all `RngNames` `Fnv1a64` values are pairwise distinct. — source: ADR-0005 Golden vectors, Implementation Guidelines

#### Numerics, determinism, dump, replay (ADR-0006)
- **Units in authoritative state:** money `Cents` (plain `readonly struct`, private ctor, `FromCents` range-checked, ±2^62); ratios `int` permille; positions/distances `int` mm (widen to `long` before the first multiply); sub-mm `Fx16` (`long Raw`, 16 fractional bits, domain [−2^47, 2^47 − 65 536]); angles `ushort` BAM; ticks `uint`, differences `long`; weight `short weightPoints`. — source: ADR-0006 §1
- **Every money multiply goes through `Cents.MulPermille`; `Cents` add/sub use the range-checked helpers** (Debug asserts; Release saturates to ±2^62 and counts `moneySaturated`). — source: ADR-0006 §1–§2, Implementation Guidelines
- **Ratios go through `Fix.MulDiv` (truncate) or `Fix.MulDivRH` (half away from zero, the overflow-free `UInt128` algorithm in ADR-0006 §2) as the owning GDD states; "RH" in every GDD means `Fix.MulDivRH`.** One division per formula over an `Int128` numerator. — source: ADR-0006 §2
- **`Fx16.ToMm = (int)(Raw >> 16)`; `FromMm(mm) = (long)mm << 16`; signed floor/ceiling use `Fix.FloorDiv`/`Fix.CeilDiv`; integer sqrt uses `Fix.ISqrt`.** Helpers are pure, allocation-free, non-throwing; zero divisor returns 0 + `fixDivZero`; clamps counted (`fixClamped`, `moneyClamped`, `fxClamped`) via an injected `SimCounterSink`. — source: ADR-0006 §1–§2
- **N-vector table in ADR-0006 §2 is asserted verbatim by the primitives tests.** — source: ADR-0006 §2
- **Overflow classes:** (1) hash/PRNG/serial/checksum/BAM wrap by design inside `unchecked(...)`; (2) money, `Fx16`, mm use an explicit range check through the typed helper; (3) everything else stays in the default context so Debug's `checked` surfaces overflow (Debug and Release dumps match when no check fires); (4) narrowing to wire/storage width via `WireNarrow`. — source: ADR-0006 §3
- **Ordered iteration uses arrays/lists in ascending id or `opSeq`; removal from ordered lists is a stable compaction; sorting uses `Sim.Primitives.StableSort` with preallocated scratch and a comparer ending in an `EntityId`/`opSeq` tie-break.** — source: ADR-0006 §3 rules 3–5
- **All encoding uses explicit little-endian `BinaryPrimitives`**; content def ids come from a sorted-key table at load. — source: ADR-0006 §3 rules 9–10
- **Enforcement:** a custom Roslyn analyzer (`PerformativeMail.Sim.Analyzers`, `PrivateAssets=all`) flags any `float`/`double`/`decimal`/`Half` type use; `BannedApiAnalyzers` RS0030 with `src/PerformativeMail.Sim/BannedSymbols.txt` (doc-comment IDs, no wildcards — list in ADR-0006 §3); `dotnet_diagnostic.RS0030.severity = error` in `.editorconfig`; every banned item has a fixture under SC-ARCH-2 seen failing before the gate counts. — source: ADR-0006 §3
- **Every stateful system binds `ISimStateContributor { ushort SectionId; void Write(ref CanonicalWriter w); }`**; contributors write fields one by one (`WriteU8…WriteI64`, `Bool` as u8), little-endian, collections as `u32 count` + elements in ascending id (or `opSeq`/slot), no strings (`StringId` as u32); `SectionId`s are unique declared constants, never reused. — source: ADR-0006 §4
- **Dump format v1:** header `"PMSD"` (u32 LE `0x44534D50`) + `formatVersion` u16 = 1; sections in builder registration order, each `sectionId u16, byteLength u32, payload`; Sim Core section 1 contents per ADR-0006 §4. — source: ADR-0006 §4
- **`SimWorld.TryWriteDump(IBufferWriter<byte>)` succeeds only between ticks on a `Ready`/`Running`/`Stopped` world**; inside `Tick()` it returns false and counts `dumpDuringTick`. Tests and tools take SHA-256 of the dump. — source: ADR-0006 §4
- **Q15 trig: committed `static readonly short[4096]` sine table in `Sim/Primitives/TrigQ15.cs`, indexed `bam >> 4`, generated once by `tools/GenTrigQ15`; cosine = `Sin(bam + 16384)`; a test asserts the table's SHA-256.** — source: ADR-0006 §6
- **Recorder/replay:** `IOpRecorder` (`OnEnqueued`, `OnDrained`, `OnSample`, `OnOverflow`) is optional and injected; M-1a `InMemoryRecorder` grows only outside the tick; header = `formatVersion`, `ContentSet` hash, SHA-256 of the `SimSettings` canonical encoding, `runSeed`, initial counters; `ReplayDriver` refuses on any header mismatch; a recording that saw `OnOverflow` is reported non-reproducible. — source: ADR-0006 §7
- **Determinism tests apply one op per tick when they need per-op hashes**; T1 = identical dumps after every tick (SC-DET-1, M-1a); T2 = bit-identical primitives/helpers/Q15/shared pure functions on Windows + Linux x64 on the `net8.0` target (SC-DET-2, M0). — source: ADR-0006 §5, Implementation Guidelines
- **Tick-path collections are pre-sized from `SimSettings`; growth only during warm-up; a test asserts no growth afterwards.** — source: ADR-0006 §8

#### Op queue, drain, replies (ADR-0007)
- **`Op` is a 64 B blittable struct (`Pack = 4`): `OpSeq u32 · Actor u32 · Kind u16 · PayloadLength u8 · Reserved u8 (=0) · ReqId u32 · Payload [InlineArray(48)]`**; a test asserts `Unsafe.SizeOf<Op>() == 64`. — source: ADR-0007 §1
- **Each system defines its own `unmanaged`, padding-free payload struct, read with `op.PayloadAs<T>()` (`MemoryMarshal.Read`/`Unsafe.ReadUnaligned`) and written with `SetPayload<T>` (`Unsafe.WriteUnaligned` after zero-fill)**; a descriptor test asserts `SizeOf<T>() ≤ 48`, `== PayloadLength`, and sum of field sizes `== SizeOf<T>()`. — source: ADR-0007 §1
- **Unused payload bytes and `Reserved` are zero**; the adapter zero-initialises the whole `Op` before decoding and sets `PayloadLength` from the descriptor. — source: ADR-0007 §1, Implementation Guidelines
- **One preallocated ring `Op[opQueueCapacity]` (default 4096, power of two) + parallel `uint[]` enqueue-boundary ticks**; `bool SimWorld.EnqueueOp(ActorId actor, in Op op)` overwrites `Actor` from the parameter and stamps `OpSeq`. — source: ADR-0007 §2
- **`EnqueueOp` refusals return `false`, consume no `opSeq`, record nothing, and count**: `busyPlayer` (`count ≥ capacity − systemOpReserve`, non-System), `busySystem` (+ one always-on record), `opSeq` exhausted (+ record), `enqueueBadState`. — source: ADR-0007 §2
- **Drain (phase 0): `n = min(count, maxOpsDrainedPerTick)` (default 64, safe 32–256, in the replay header), ascending `opSeq`**; Core pre-checks in order (`unknown_kind` for unregistered kind or wrong length/reserved; `unknown_actor` for non-present actor or non-System actor on a `SystemOnly` kind); then a call scope, `IOpHandler.Apply`, then Sim Core appends `OpReply` after every event the call raised and calls `IOpRecorder.OnDrained`. Ops beyond the cap stay queued (`drainBacklog`); System ops get no bypass. — source: ADR-0007 §3
- **Handlers validate fully before the first mutation, id allocation, RNG draw or event raise; a reject leaves state, allocators and RNG untouched (SC-R8-2 harness per handler); a handler returns exactly one `Reply`.** — source: ADR-0007 §3, Implementation Guidelines
- **Composite actions are one `OpKind` whose handler calls documented `I…Api` methods in a fixed order.** — source: ADR-0007 §3
- **`OpKind` is a `u16` enum, append-only, explicit values, 0 invalid, per-system blocks**: Sim Core `0x0001–0x003F`, Players/movement `0x0040–0x007F`, Inventory `0x0080–0x00FF`, Mail/Delivery `0x0100–0x013F`, Throwing `0x0140–0x017F`, Economy `0x0180–0x01BF` (amendment 2026-10-28; `OpKindBlock.Economy`), later systems 64-wide blocks from `0x01C0` by amending ADR-0007 §4. — source: ADR-0007 §4 (table amended 2026-10-28)
- **Each kind registers an `OpDescriptor(Kind, PayloadLength, SystemOnly, WireCodecId, WireSize, Reasons)`**; `System`-only kinds are declared `SystemOnly`; the descriptor-table hash is the protocol-version check. — source: ADR-0007 §4
- **`Reply` (`Pack = 1`): `ReqId u32 · Kind u16 · Reason u16 · Result u8 · 3 explicit zero pad bytes · Extra [InlineArray(24)]` = 36 B (asserted)**; `Ok ⇔ Reason == 0`; partial results are `Ok` with details in `Extra`. — source: ADR-0007 §5
- **Reason codes 1–63 come only from `CoreReason`** (1 busy, 2 unknown_actor, 3 unknown_kind, 4 slot_invalid, 5 slot_busy, 6 duplicate_player, 7 join_closed, 8 bad_request, 9–63 reserved); **system codes start at 64**, keyed by `(Kind, Reason)` and declared in the descriptor (a test enumerates descriptors). — source: ADR-0007 §5
- **The ring, boundary-tick array and drain loop allocate nothing after construction.** — source: ADR-0007 Implementation Guidelines

#### Event log, replication, delivery (ADR-0008)
- **`EventEntry` is 64 B (`Pack = 4`): `Ordinal u32 · Kind u16 · Class u8 · Audience u8 · CallSeq u32 · PayloadLength u8 · Flags u8 · Reserved u16 · Payload [InlineArray(48)]`**; payloads are `unmanaged`, padding-free, ≤ 48 B, string-free (`StringId` u32), no `Int128`, read/written unaligned, zero-filled past their length. — source: ADR-0008 §1
- **Every `EventKind` declares an `EventDescriptor(Kind, Class, PayloadLength, Coalescible, ResyncScope, WireCodecId, WireSize)`**; an undeclared kind fails the enumeration test (runtime fail-closed to `Internal`). `EventKind` mirrors the `OpKind` block ranges; `OpReply = 0x0001`. — source: ADR-0008 §3, §6, Key Interfaces
- **Large state is never inflated into one entry: use (a) by-reference + `ISnapshotSource`, or (b) chunked `Continuation` entries of the same kind and `CallSeq`.** — source: ADR-0008 §1
- **`EventLog` is preallocated at `Freeze()` (`eventLogCapacity` 4096, power of two) and append-only**; valid from phase 0 to the end of `Deliver`; reset after `Deliver` (Debug fills the used span with `0xDD`). A system needing data on T+1 stores state. — source: ADR-0008 §2
- **`SimSettings` validation rejects a non-power-of-two capacity, `eventLogReserve > capacity/4`, and `maxOpsDrainedPerTick × 4 + 1.5 × systemEventsPerTick > capacity − reserve`.** — source: ADR-0008 §2
- **Audience is resolved at raise time by the raising system**: `Internal` 0; `Replicated` = connected-slot mask at raise; `OwnerOnly` exactly one bit; `Targeted` = owner's subscription query. `OpReply` is `OwnerOnly` to the actor's slot (0 for `System`). — source: ADR-0008 §3
- **Later-phase consumers declare a pull reader at build (≤ 8 per world, sorted `ushort[]` filter) and read with `EventLog.Read(ref cursor, out entry)`**; cursors rewind each tick. — source: ADR-0008 §4
- **Every public `I…Api` mutation entry point opens `using var scope = log.BeginCall();`**; Sim Core opens scopes around op handlers and phase runs. — source: ADR-0008 §5
- **Only `Coalescible` kinds may be rewritten, via `EventLog.Update<T>(handle)`, and only within the same call and past the nested-call fence**; illegal update → Debug assert, Release scratch write counted `coalesceLate`. — source: ADR-0008 §5
- **Overflow admission:** full → drop all; `count ≥ capacity − reserve` → drop kinds that are neither `HasReader` nor `OpReply`; each drop counts `eventsDropped`, sets `Overflow`, ORs audience into `DroppedAudienceMask` and `1UL << ResyncScope` into `DroppedKindsMask`; no rollback, no reorder. — source: ADR-0008 §6
- **`EventsFlush` calls `IEventSink.Deliver(tick, entries, overflow)` exactly once per tick with every entry in raise order** (skipped when no sink is bound); during `Deliver` the Sim is closed (`EnqueueOp` false, `TryWriteDump` false, raises dropped, re-entrant `Tick` counted). Not called for a faulted tick. — source: ADR-0008 §7

#### Content, ContentSet, StatSheet (ADR-0010)
- **All Sim content is JSONC under `content/` (= `res://content/`), one directory per def type**, balance in `content/balance/<owner>.jsonc` (`SimSettings` keys in `balance/sim.jsonc`), Presentation data under `content/presentation/` (not hashed). — source: ADR-0010 §1
- **`content/manifest.txt` is generated by `ContentValidator --write-manifest` at export and never committed**; `DirectorySource` enumerates and ordinal-sorts itself. — source: ADR-0010 §1
- **The loader reads only through `IContentSource` (`ListManifest()`, `Read(path)`)**; `DirectorySource` and `InMemorySource` live in `Sim/Content/Sources/` (plain `System.IO`); `ContentHost` is Godot-side (ADR-0019). — source: ADR-0010 §1
- **Parsing is a hand-written forward `Utf8JsonReader` walk per file driven by a per-def-type `FieldSpec` table**; BOM stripped, comments skipped, trailing commas allowed; duplicate keys and unknown fields are errors (edit-distance ≤ 2 suggestion); `$schema` and `suppress` are the only reserved keys. — source: ADR-0010 §2
- **Numbers are read from raw token bytes by the `DecimalToScaled` reader** (`-?(0|[1-9][0-9]*)(\.[0-9]{1,3})?`, `long` accumulation; rejects exponents, `-0`, leading `+`, bare `.`, a 4th fractional digit); unit suffix decides scale (`…Seconds`→ms, `…Metres`→mm, `…Ratio`/Mul `value`→permille, `…Points`→milli-points, `…Deg`→milli-degrees; `…Ms/…Mm/…Pm/…Ticks`/money as-is); a numeric field with no unit is a load error. — source: ADR-0010 §2
- **Enums are closed C# enums with explicit never-reused values; snake_case ↔ member tables generated at compile time (a source generator is allowed).** — source: ADR-0010 §2
- **Every diagnostic carries `path:line:column` (1-based UTF-8 byte column), the rule id, the value and the allowed range/set.** — source: ADR-0010 §2
- **Defs are `sealed record class` (leaves may be `readonly record struct`) with init-only `int/long/ushort/bool/enum` members and `ImmutableArray<T>` lists** (hot paths use `AsSpan()`); a reflection test checks every def type. Defs are identified by `DefIndex`, never by record equality. Id regex `^[a-z][a-z0-9_]{0,47}$`. Defaults are neutral. — source: ADR-0010 §3
- **`ContentLoader.Load(IContentSource)` runs five stages (syntax, schema, build, references, whole-set) reporting all reachable errors**, producing one immutable `ContentSet` (with `SimSettings`, `BalanceTable`, `DefTable<T>` by `DefIndex`) held for the process lifetime and passed by reference to `SimComposition.Build`; systems receive `ContentSet`/`IStatSheet` by constructor and may cache sub-tables in `readonly` fields. — source: ADR-0010 §4
- **Balance keys are addressed `owner.key`; a code-side `BalanceKey` registry declares owner, unit and hard range; the data file carries the safe range.** — source: ADR-0010 §4
- **M-1a uses `BuiltInContent.CreateM1a()` building the same `ContentSet` types (running stages 4–5)**; at M0 the JSONC loader replaces it and a test asserts identical canonical encoding for the M-1a subset. — source: ADR-0010 §4
- **Debug hot reload (F6) re-runs the full pipeline; any error keeps the old set; success restarts the run from its seed** (tainted, not recorded); refused while a remote peer is connected. — source: ADR-0010 §4
- **Validator rules have permanent ids `CV-<AREA>-NN`, are registered as code in `Sim/Content/Rules/<Owner>.cs`**, each clause ships a violation fixture (and a valid-edge fixture where it has a limit); `--list-rules --format md` generates `docs/content-rules.md` (CI diff). Errors are never suppressible; Warnings need a per-def `suppress: [{rule, why}]`. — source: ADR-0010 §5
- **`tools/ContentValidator`** is a plain `net8.0` console project over `DirectorySource`; output order `(path ordinal, line, column, rule id)`; exit codes 0/1/2/3; M0 flags `--strict`, `--format text|github`, `--print-hash`, `--write-manifest`, `--list-rules --format md`. — source: ADR-0010 §5
- **`ContentHash = SHA-256(hashSchemaVersion u8 ‖ BalanceBlock ‖ DefBlocks)` over loaded values** (canonical encoding per GDD F6); the canonical encoder and a hand-written managed SHA-256 (with NIST known-answer tests) live in the Sim; bump `hashSchemaVersion` whenever the layout changes; the shipped set's digest is golden-tested on Windows and Linux CI. — source: ADR-0010 §6
- **StatSheet:** closed `Stat` enum with a code-side `StatDef` row; modifiers in fixed-capacity pools; resolution exactly GDD F1 (any Set → Set value; else `N = base × ΠM_i + 1000^k × (ΣA_i + transientAdd)`, one rounding HalfUp/Floor/Ceil via ADR-0006 helpers, clamp in `Int128` before narrowing to `int`, `k ≤ 6` asserted). — source: ADR-0010 §7
- **`StatSheet.Build(content, sources)` is a pure function; sheets are rebuilt when a source is added or removed and cache resolved values + pre-rounding components; `IStatSheet.Read` is one array load (or one `Int128` F1 with a transient), 0 B.** An Int64 fast path is built only if CDD-PERF-2 demands it, proven bit-identical. — source: ADR-0010 §7
- **`IStatWriter` is constructor-injected only into Perks & Perk Draft and Stamps & Run Events** (reflection test); meta-progression never pushes modifiers. — source: ADR-0010 §7, ADR-0003 §4

#### ClientLayer steps, previews, presentation timing (ADR-0016)
- **Client step order per tick:** 1 events/replies into mirrors → 2 `InputLatch` (latches only, **never dispatches a verb**) → 3 `CarrierStep` (`OwnedCarrier.Step` only) → 4 `Previews` composite with fixed `PreviewSubStep` order **4a `Focus` → 4b `DeliverPreview` (+ `DeliverPreviewTracker`) → 4c `ThrowPreview` → 4d `EdgeResolve` → 4e `HoldAdvance`**; bind with `BindPreview(PreviewSubStep, IClientStep)`. A composition test asserts the sub-order. — source: ADR-0016 §2
- **Edges resolve only in 4d, on this tick's 4a–4c state.** The presented snapshot (carried on the edge as `Seq`) fixes the **target**; the press tick judges the owner's **branch** (MD4). — source: ADR-0016 §2, ADR-0014 §3
- **Client steps live in `Sim/<System>/Client/`, read only facts built from mirror parts or ports, and never reference `SimWorld` or a host-side system.** — source: ADR-0016 Implementation Guidelines
- **Shared rule functions (`Evaluate`, F6/F7/F10 pay, `LaunchMath`/`Ballistic`/`FlightSweep`, `Hold.HoldTicks`) are `static`, pure and 0-alloc over facts structs or spans, compiled once in the Sim.** Client.Core and Godot call them and never reimplement a rule, rounding or formula. — source: ADR-0016, ADR-0003 §6
- **Outcome beats are `[OutcomeBeat]` components fed only from `PresentationEventStream` cursors; predictive components never start an outcome beat. `DeliverPreviewTracker` counts client Sim ticks; Presentation never derives `CanStartHoldNext`.** — source: ADR-0016
- **Render time comes from Client.Core `IRenderClock.RenderSimTimeMs = (tick·1000 + AlphaPermille)/30`; `IHostClock` gains no members.** — source: ADR-0016
- **M-1a `ContainerMirror` = confirmed state + seal staging; the `PendingQueue` arrives at M0, so `panelView` = confirmed state.** — source: ADR-0016 Ordering Note

#### Input router core (ADR-0012; Godot adapter rows spike-gated on OQ-IC-9)
- **The router core lives in Client.Core with no Godot reference, driven only by `RawInputRecord`s and `IInputClock`; steady state allocates 0 B.** — source: ADR-0012
- **Every edge is captured from events, arrival-stamped, gated against the router state at event time, and queued in order.** `GameLoop._Process` calls `InputRouter.FlushToLatch(IInputLatchWriter)` once before `RunFrame`. `InputLatch` delivers all edges on the first tick of the batch (`IsFirstTickInBatch`) and state only on later ticks. If no tick runs, nothing is cleared. Release and cancel edges are never dropped. — source: ADR-0012 §4
- **Presses latch at the press edge and mute on a state change (G1, G3). Toggle modes become logical press/release/cancel in the router; the Sim never branches on a mode.** — source: ADR-0012
- **The router never times holds (ADR-0014 owns the counter). The ms clock serves throw aim (F11) and UI only; the Sim never receives milliseconds.** `holdSettingMs` is an integer multiple of 50. — source: ADR-0012 §5
- **Immediate verbs are dispatched by `GameLoop` before `RunFrame` in their event's frame, through the same `ClientAdapter` codec.** — source: ADR-0012 §6
- **`InputFrame.FocusCosMinPm` carries the focus cone from the camera FOV; Sim code never asks Godot for it.** — source: ADR-0012, ADR-0014 §2

#### Shared geometry (ADR-0013 §6)
- **`Sim/Geometry/` holds the only `WorldHeight.Sample/At/Gradient`, `SegmentIntervals`, `HeightfieldMarch` and `Obb` functions (pure, T2 surface).** Height uses one `MulDivRH` (half away from zero) over the int64 bilinear sum. Slope tests compare un-divided numerators in `Int128`. — source: ADR-0013 §6, ADR-0026 §2
- **Angles are BAM; `atan2` goes through `TrigQ15.Atan2Bam`; both tables are committed and hash-tested.** — source: ADR-0013 §3

#### Godot host shell & scenes (ADR-0019)
- **No autoloads; never `ChangeSceneToFile`/`ChangeSceneToPacked`. `Main.tscn`'s root is the only `GameLoop`; screens are child subtrees of `Main/Screens`.** — source: ADR-0019
- **Views are inert until `GameLoop` injects dependencies through `Initialize(deps)`; never look dependencies up in `_Ready` (children `_Ready` first, OQ1-1).** Presenters run from `PresentationRoot.Present` in the order World → Hud → Audio. — source: ADR-0019
- **`ContentHost` is a plain synchronous class: `FileAccess.GetFileAsBytes` + `GetOpenError()` on every call; never `GetAsText` or `DirAccess` enumeration of the PCK.** — source: ADR-0019, ADR-0010
- **`export_presets.cfg` keeps the `include_filter` for `content/*.jsonc` and `content/manifest.txt`; the game solution carries Export* configurations.** — source: ADR-0019

### Forbidden Approaches
- **Never dispatch a verb in `InputLatch`; never resolve an edge outside `PreviewSubStep.EdgeResolve`; never add a top-level `ClientStepId` for interaction.** — source: ADR-0016 §2, Alt 3; ADR-0014 Alt 4
- **Never read `Godot.Input` or mutate `InputMap` outside `src/Game/Input/` (CI lint); never compare `device == 0`; never let synthetic events change the last-used device; never mutate `ui_*`.** — source: ADR-0012
- **Never use autoloads, `ChangeScene*`, or `ResourceSaver.Save` on a `ViewCatalog` (catalogs are editor-saved, uid-bearing; CI uid check).** — source: ADR-0019
- **Never reference a `Godot*` assembly, write `using Godot`, or take a runtime NuGet package in Sim or Client.Core**; never let the Sim reference Client.Core. — breaks headless CI and the Sim/Presentation split — source: ADR-0001
- **Never compile Sim source into the game assembly via linked folders** (documented contingency only) — Sim would compile under Godot defines and `[Conditional]` symbols would come from the game — source: ADR-0001 Alt 1
- **Never add the Godot project to `PerformativeMail.sln`** — CI would need Godot and would build it as `Debug` under `-c Release` — source: ADR-0001 Alt 2
- **Never branch Sim/Client.Core code on `DEBUG`, `RELEASE`, `EXPORTDEBUG`, `EXPORTRELEASE` or `Configuration`; never use `Debug.Assert`; never key MSBuild logic on `Configuration == 'Release'`** — `DEBUG` is undefined for referenced libraries in `ExportDebug`; `Debug.Assert` throws/dialogs/formats — source: ADR-0001 §4, Alt 4
- **Never trim or AOT-compile exports** (`PublishTrimmed=false`, `PublishAot=false`) — source: ADR-0001 §2
- **Never use `Assembly.Location` or assembly-relative paths** (empty inside Godot) — content arrives through `IContentSource` — source: ADR-0001
- **Never register process-wide static event handlers (`AppDomain`, `Console`, BCL static events) or start threads/timers in Sim or Client.Core** — pins the collectible load context, breaks editor hot reload — source: ADR-0001
- **Never let an always-on check throw, format strings or allocate.** — source: ADR-0001 §5
- **Never drive or pause the Sim from `_PhysicsProcess`/`physics_ticks_per_second`** — couples Sim to Jolt rate, `SceneTree.Paused` and `Engine.TimeScale`; untestable without Godot — source: ADR-0002 Alt 1
- **Never accumulate `_Process(delta)` or use `delta` at all in `GameLoop`** — float drift; `delta` under-reported wall time by ~32 % under slow frames (spike F11) — source: ADR-0002 Alt 2, §3
- **Never run the Sim on a dedicated thread in v1** (deferred behind the R18 triggers) — source: ADR-0002 Alt 3
- **Never bank excess ticks** — hitch → burst → spiral — source: ADR-0002 Alt 4
- **Never use Godot `Timer` nodes, `System.Threading` timers or background threads to drive or call the Sim; never call `SimWorld`/`ClientLayer`/`FrameRunner` from any node other than the single `GameLoop`.** — source: ADR-0002 §3, Implementation Guidelines
- **Never use `SceneTree.Paused` to pause the Sim.** — source: ADR-0002 §3–§4
- **Never call `EnqueueOp`, `Tick` or write the sample ring from a transport callback.** — source: ADR-0002 §5
- **Never let Sim code read `AlphaPermille` or expose a `float`/`double` `Alpha`.** — source: ADR-0002 §1
- **Never bind phase 0 or 13 from a system** (`SimBuildException(ReservedPhase)`) — source: ADR-0003 §3
- **Never use synchronous event subscribers between systems** — order depends on subscription order, re-entrancy, allocation — source: ADR-0003 Alt 2, ADR-0008 Alt 1
- **Never use a DI container or service locator; never hold static mutable state (including `static readonly T[]`) or a `SimWorld` reference inside a system** — hides construction/composition/dump order and needs a runtime package — source: ADR-0003 Alt 3, §4
- **Never route in-tick effects (automation, expiry, landings, `MailStep`, `CombatReact`) through the op FIFO** — adds a tick of latency and breaks Mail R6 — source: ADR-0003 Alt 4, ADR-0007 §3
- **Never call a mutating `I…Api` at tick end, in phase 13 or between ticks; never call upward or back from callee to caller.** — source: ADR-0003 §5
- **Never let an `ITickEndCheck` mutate state or raise events.** — source: ADR-0003 §1
- **Never throw `SimBuildException` inside `Tick()`.** — source: ADR-0003 Implementation Guidelines
- **Never let `ClientLayer` read `SimWorld`**, even on the host. — source: ADR-0003 §6
- **Never use one global entity counter with a side-table class, or 64-bit entity ids** — source: ADR-0004 Alt 1
- **Never reuse, wrap or skip entity/mail/entry ids (no free lists, no generation counters)** — stale handles across wire and replay — source: ADR-0004 Alt 2
- **Never model temp entry ids as negative numbers or a separate type** (bit-31 layout only) — source: ADR-0004 Alt 3
- **Never use a `DefIndex` whose `default` means ordinal 0** — source: ADR-0004 Alt 4
- **Never order or tie-break by `GetHashCode()`; never let default `ValueType.Equals/GetHashCode` be reached on an id struct.** — source: ADR-0004
- **Never truncate an `EntityId` to its low bits for a narrow wire id** (use an id map) — source: ADR-0004 §5
- **Never compare `FlightId` numerically.** — source: ADR-0004
- **Never persist `DefIndex`** (saves, `user://`, telemetry) — persist the string id — source: ADR-0004
- **Never use `System.Random`/`Random.Shared`** — algorithm not specified across .NET versions, allocates, invites floats — source: ADR-0005 Alt 1
- **Never use xoshiro/xoroshiro, one run-wide stream, rejection-sampled `NextInt`, or seeding directly from `Fnv1a64(name) ^ runSeed ^ index`.** — source: ADR-0005 Alts 2–5
- **Never add `NextFloat`, `NextDouble`, `NextBool(p)`, range-with-rejection, or an `int` overload of `NextInt`.** — source: ADR-0005 §4
- **Never build stream names from runtime strings or player text; never draw before validation passes; never hold a stream across a `Create` of the same key expecting the old sequence.** — source: ADR-0005 Implementation Guidelines
- **Never use `float`, `double`, `decimal` or `Half` in authoritative code** — not bit-reproducible; violates T2 — source: ADR-0006 Alt 1, rule 1
- **Never make the Sim project-wide `checked` in all configurations** — throws inside the Release tick — source: ADR-0006 Alt 2
- **Never use a universal Q32.32 fixed-point type** — source: ADR-0006 Alt 3
- **Never hash state in the Sim tick path** (`CanonicalHash64` only when a cross-machine checksum first needs it) — source: ADR-0006 Alt 4, §4
- **Never blit structs into the dump (`MemoryMarshal.AsBytes` over state)** — padding/union tails leak, insertion order leaks — source: ADR-0006 Alt 5
- **Never iterate `Dictionary`/`HashSet` (incl. `Keys`, `Values`, LINQ over them) in state or ordering code** — lookup-only — source: ADR-0006 rule 3
- **Never use swap-remove where order matters; never use `Array.Sort`, `List.Sort`, `Span.Sort`/`MemoryExtensions.Sort`, LINQ `OrderBy/ThenBy(Descending)`.** — source: ADR-0006 rules 4–5
- **Never use `Guid`, `string.GetHashCode`, `object.GetHashCode`, `HashCode`, `RuntimeHelpers.GetHashCode` in state or ordering.** — source: ADR-0006 rule 6
- **Never use a time source (`DateTime*`, `Stopwatch`, `Environment.TickCount*`, `TimeProvider`) or `Thread`, `Task`, `Parallel`, `ThreadPool`, `Timer`, `async` in the Sim**; the owner-thread assert uses `Environment.CurrentManagedThreadId`. — source: ADR-0006 rule 7
- **Never put culture-dependent text in state or the dump.** — source: ADR-0006 rule 9
- **Never apply `Math.Abs(long)` to a value that can be `long.MinValue`** (use the `UInt128` magnitude form). — source: ADR-0006
- **Never use raw `+` on `Cents`; never rely on `checked` for the ±2^62 money bound; never divide `Fx16` by 65536 (use `>> 16`); never use a raw narrowing cast to a wire width.** — source: ADR-0006 §1, §3
- **Never declare `Cents`/`Fx16` (range-bounded values) as `record struct`; `record struct` only for ids that never appear in ordering or hashing.** — source: ADR-0006 §1, §3
- **Never request a dump inside a tick.** — source: ADR-0006
- **In the tick path never use LINQ, lambdas/closures, method-group delegate creation, `foreach` over interface-typed collections, boxed struct enumerators, `params`, string concatenation/interpolation (including logging), boxing, `Enum.ToString/GetValues`, exceptions as control flow, `async`, `yield`.** — source: ADR-0006 §8
- **Never dedupe inside the Sim** (no per-handler `reqId` cache) — needs connection identity; duplicates would consume `opSeq` — source: ADR-0007 Alt 1, §6
- **Never put a u32 `reqId` on the wire** (u16 per connection) — source: ADR-0007 Alt 2
- **Never use an explicit-layout union of payload types in `Sim/Core`** — inverts the dependency direction — source: ADR-0007 Alt 3
- **Never remove the drain cap** — a 4096-op burst blows the tick budget — source: ADR-0007 Alt 4
- **Never represent `busy` as an event-log entry** (the adapter answers at once) — source: ADR-0007 Alt 5
- **Never read the actor from a payload; never let a handler append its own `OpReply` or reorder the log; never compare, dedupe or store `reqId` in handler state.** — source: ADR-0007 Implementation Guidelines
- **Never read payloads with `Unsafe.As`; never put `Int128` or references in a payload.** — source: ADR-0007 §1
- **Never use a typed queue per event kind or variable-length entries in a byte arena** — loses the global raise order — source: ADR-0008 Alts 2–3
- **Never coalesce per phase** (only per call, behind the fence) — source: ADR-0008 Alt 4
- **Never resync every audience on overflow** (use the masks) — source: ADR-0008 Alt 5
- **Never raise an event outside phases 0–12, in a tick-end check or during `Deliver`.** — source: ADR-0008 §6
- **Never let Sim Core merge events automatically.** — source: ADR-0008 §5
- **Never use `JsonSerializer`, `JsonDocument`, `GetDouble`, `GetDecimal`, `double.Parse`, `Enum.Parse` or runtime reflection discovery on the content path** — silently ignores unknown keys, last-wins duplicates, inexact decimals, trimming-fragile — source: ADR-0010 Alt 1, Implementation Guidelines
- **Never author Sim content as Godot `Resource`/`.tres` (`[GlobalClass]`); never call `Resource.Duplicate`/`DuplicateDeep` on the content path; never read `res://` from the Sim.** — source: ADR-0010 Alt 2, §1, §3
- **Never use YAML or CSV for content** (CSV may be revisited for a table > ~100 rows). — source: ADR-0010 Alt 3, §1
- **Never hash file bytes for the content hash or use platform crypto (`System.Security.Cryptography`)** — source: ADR-0010 Alt 4, §6
- **Never resolve stats as `base × Σmultipliers + Σadders` or round per modifier** — source: ADR-0010 Alt 5
- **Never replicate resolved sheets or modifier events** (replicate source lists) — source: ADR-0010 Alt 6
- **Never put `T[]`, `float`, `double`, `decimal`, `Half` members in defs; never use culture-default compares or `ToLower`/`ToUpper` on ids, keys or paths (ordinal only).** — source: ADR-0010 Implementation Guidelines
- **Never let `IStatSheet.Read` iterate modifiers or allocate.** — source: ADR-0010
- **Never swap a new `ContentSet` into a running `SimWorld` in place.** — source: ADR-0010 §4

### Performance Guardrails
- **`SimWorld.Tick()`**: 0 B allocated after 300 warm-up ticks, no recorder, Release, dedicated process, measured on the **net8.0** target over 18 000 ticks with `GC.GetAllocatedBytesForCurrentThread()` — advisory at M-1a, **blocking from M0**; a net10.0 pass alone does not satisfy it — source: ADR-0006 §8
- **Host tick**: p99 5.0 ms / max 8.0 ms / median 3.0 ms; ClientLayer p99 0.3 ms — advisory until a reference machine exists — source: architecture.md TR-sim-037 (per-phase slices: `perf-budget.md`, not yet written)
- **`Inputs.Apply` (phase 0)**: 0.80 ms p99; ≤ 64 ops/tick (advisory until a reference machine exists; slice owned by perf-budget.md) — source: ADR-0007 Performance Implications, architecture.md §Required ADRs note
- **`Events.Flush` (phase 13)**: 0.25 ms p99 (advisory until a reference machine exists; slice owned by perf-budget.md) — source: ADR-0008 Performance Implications, architecture.md §Required ADRs note
- **Op ring**: 4096 × 64 B = 256 KiB + 16 KiB boundary ticks; `systemOpReserve` 64 — source: ADR-0007, registry `op_and_event_capacity`
- **Event log**: 4096 × 64 B = 256 KiB, reserve 256; F8 `cap×4 + 1.5×systemEventsPerTick ≤ capacity − reserve` (826 ≤ 3840); no overflow on the worst fixture tick (SC-R8-4, M0) — source: ADR-0008 §2, §6
- **TickDriver**: O(1) per frame, ≤ `maxCatchUpTicksPerFrame` ticks per frame (default 4, safe 3–8), no allocation per frame — source: ADR-0002
- **RNG**: ≈ 48 B × 21 preallocated handles (< 2 KB); `Next32` ≈ 1 ns — source: ADR-0005
- **`SimCheckRing`** ≈ 256 × 24 B per world; Q15 table 8 KiB — source: ADR-0001, ADR-0006
- **Perf is measured only in `Release`/`ExportRelease`**; perf numbers from tests or the editor are not transferable to exports — source: ADR-0001 §7, Performance Implications
- **Content**: boot parse + validate + hash ≤ 100 ms CPU; run-start bind ≤ 25 ms (no re-parse); sheet rebuild ≤ 0.2 ms (8 players × 5 perks) (advisory); `IStatSheet.Read` 0 B — source: ADR-0010 Performance Implications, registry `stat_read`

---

## Core Layer Rules

*Applies to: Grid Inventory & Containers (+ WorldItem) — ADR-0009; Networking & Session adapter seam — ADR-0011 (adapter core in `Client.Core/Net/`); World (M-1a `IWorldQuery` + street stub) — ADR-0026; Health & Damage and Persistence follow the Foundation and Feature system rules until their own ADRs exist.*

### Required Patterns

#### Grid Inventory (ADR-0009)
- **Inventory is one Sim module `PerformativeMail.Sim/Inventory/`** binding: one `OpDescriptor` + `IOpHandler` per op, `ItemsExpire` (phase 1), `IntakeDrain` (phase 2), a conservation `ITickEndCheck`, its event kinds, a state contributor and a snapshot source; it publishes `IInventoryApi` for in-tick callers. — source: ADR-0009 Decision
- **Op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`**; no system writes container, entry or ledger state directly. — source: ADR-0009 Decision, Implementation Guidelines
- **Each GDD op is its own `OpKind` in `0x0080–0x00FF`** with the payloads in ADR-0009 §1 (≤ 32 B, padding-free: reorder fields or declare explicit zeroed pad fields counted in `PayloadLength`); `reqId` is in the `Op` header; containers on the wire are `containerId` u32. `throw` is Throwing's `ThrowRelease` (Sim OpKind `0x0140`, 28 B payload, ADR-0015 §1) calling `IInventoryApi.DetachForThrow`. **Backlog:** storage and the phase-2 drain are Inventory's (`Sim/Inventory/Backlog`). Mail appends whole entries through `IInventoryApi.AppendBacklog`, and Inventory never calls Mail. — source: ADR-0009 §1, ADR-0015 §1, ADR-0017 §6
- **Every handler is `Apply = Validate (pure, writes only a stack-allocated `InvPlan`) → Commit (never fails, never recomputes) → Reply`**; `Validate` never allocates ids, draws RNG, raises events or touches the ledger. — source: ADR-0009 §2
- **Reason precedence is one ordered chain per op kind, verbatim from the GDD** (placement: `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`; `Split`: `… → cursor_busy → unsplittable → bad_count`; range-gated: Player Character P13 chain via `IPlayersApi` first); covered by a parametrised test asserting the earliest code. `container_gone` is decided via the tombstone table. — source: ADR-0009 §2
- **Batch ops validate op-level checks before any mutation, then validate-and-commit each entry atomically in GDD order**; a batch that commits nothing replies `ok:false` with the first op-level reason; a partial batch is `Ok` with `requested`/`got`. — source: ADR-0009 §2
- **Inventory reason codes:** `bad_request` = `CoreReason` 8; own codes 64–85 (`not_open 64 … capacity_exhausted 85`, list in ADR-0009 §2), never reused. — source: ADR-0009 §2
- **Versions:** each container `version` u32 bumps by exactly +1 per mutating call that touched it (no-op bumps nothing); `lastReplaceVersion` set by a layout-changing sort; positional ops carry `baseVersion` → `stale_layout` iff `baseVersion < lastReplaceVersion`; `statEpoch` + `lastSorted[mode]` memoise sort idempotence; a rejected op changes no version. — source: ADR-0009 §3
- **Events:** records (`InvInserted`, `InvRemoved`, `InvMerged`, `InvMoved`, `InvSplitFrom`, `InvReplace`, `InvCursor…`, `WorldItem…`, `BacklogUnits`, `GroupCounts`) then exactly **one `InvVersion` seal per touched container per call, after that container's records**; the reply comes last. Container records/seals are `Targeted` (subscriber set at raise); panel and cursor containers `OwnerOnly`. — source: ADR-0009 §4
- **`mailIds` travel only as chunked ascending runs `(start u32, len u16)` on `Inserted`/`Merged` (`Continuation` entries); sort layouts only by reference (`InvReplace` + `ISnapshotSource`).** One `InvReplace` per sort call. — source: ADR-0009 §4
- **`InvReplyExtra` is exactly 24 B with explicit offsets** (`ResultVersionA/B`, `ByActorId`, unit/entry counts saturating at 65 535, `CauseAndFlags`, `SkipReason`). — source: ADR-0009 §4
- **Storage is preallocated from `SimSettings` at world build**: `ContainerHeader` array + open-addressed `containerId → slot` map; occupancy one `ulong` per row + `cellOwner` u16 array; global `EntryRecord` pool (≤ 40 B) with free-slot stack; `mailIds` size-class arena (1…256) with borrow-up then `capacity_exhausted`; Intake outfeed index-linked FIFO with lazy tombstones (Intake sort rebuilds it); per-container FIFO min-heap for lowest mailId; one WorldItem expiry FIFO per duration class (`waterFloat` split into floating mail / non-mail: five FIFOs; erratum 2026-10-21); 256-entry tombstone ring; StackKey → non-full list in row-major order; `itemId → units` map; negative-fit cache; per-slot 32-entry temp-id map (dumped). — source: ADR-0009 §5
- **Conservation:** a `UnitLedger` of O(1) counters written only by `InventoryMutator`'s primitives (ADR-0009 §6 erratum 2026-10-22); the tick-end check asserts `Σ live = spawned + granted − consumed − destroyed` in every build and writes an always-on record on mismatch (never throws); the full multiset scan is `[Conditional("INVENTORY_ASSERT")]`. — source: ADR-0009 §6
- **Probe counters (H-PERF-6) are compiled into Release and incremented once per cell probe or candidate visit** — a placement scan counts per cell (each footprint cell examined, never one per anchor); a candidate visit is an entry, list node or FIFO node inspected (clarified 2026-10-21, owner ruling 11:43 AM PT); for the WorldItem expiry FIFOs (H-PERF-6d) a visit is one FIFO node read, already-checked heads and queue clean-out reads are not visits, gate ≤ created + 1 (erratum 2026-10-21). — source: ADR-0009 Implementation Guidelines
- **Client side (`Client.Core/Inventory/`, built at M0):** `ContainerMirror` applies a container's staged records atomically when its seal arrives after the gap check (`version_from == mirror.version`, else resync); `PendingQueue` (`pendingQueueMax = 16`); `TempIdAllocator`; only a reply retires a pending op; 5 s reply timeout → resync with `ackedReqId`. — source: ADR-0009 Key Interfaces, §4

#### World query & M-1a street stub (ADR-0026)
- **`PerformativeMail.Sim/World/` holds `IWorldQuery` and its single implementation `WorldTables`, immutable after `WorldTablesBuilder.Build()`; M-1a content is `M1aStreet.Author(builder)` (code-authored); tests use `WorldTablesFixture` over the same builder.** — source: ADR-0026 §1, §8
- **Every consumer reads terrain, water, world solids, bounds and lots only through `IWorldQuery`; nobody reimplements bilinear sampling or water depth.** `TerrainHeightMm` is ADR-0013's `WorldHeight.Sample` over `GetCorners` (one `MulDivRH`). — source: ADR-0026 §2, Implementation Guidelines
- **Water authority is `WaterDepthMm = max(0, −TerrainHeightMm)` (sea level 0); `TileFlags.Water` is only a broad-phase hint.** Walkability is decided on exact gradient numerators; `TerrainGradientPm` is a rounded convenience. — source: ADR-0026 §2–§4
- **World solids come only from `QueryStaticSolids(region, Span<ushort>)`: static 4 m grid, ascending deduplicated indices, 0-alloc, overflow counted.** Player-built constructs and vehicles are not world solids; their owners register them. — source: ADR-0026 §5
- **`Lots` are in ascending `AddressId` (= Mail's `DestinationOrdinal`); destination `EntityId`s are allocated by the builder in lot order in `Constructed`; the builder rejects duplicate/out-of-range addresses and bad spacing (`SimBuildException`).** — source: ADR-0026 §6
- **`WorldTables` is an `ISimStateContributor` (section `World`); replays record the world source identity (`M1aStreet` version constant).** — source: ADR-0026 §9
- **`IWorldQuery` may only gain members (M1 revision); §2–§7 semantics change only by superseding ADR-0026.** — source: ADR-0026 §10
- **Corner heights must lie within ±1,000,000 mm (`WorldConstants.MaxCornerHeightMm`); `SetCorners` rejects others (`WorldCornerHeightOutOfRange`).** — source: ADR-0026 §2
- **At the far map edge (x = TilesX·S or z = TilesZ·S) sample the last tile with offset exactly S (far-corner heights); the heightfield march follows the same rule.** — source: ADR-0026 §2
- **Compose the world with `WorldTablesBuilder.Build(EntityIds, slopeMaxPm, swimEnterMm)` — the only overload that requires a Post Office (code 37); `Build(int, int)` keeps it optional (validated when set); never call `Build()` with a Post Office set (code 43).** — source: ADR-0026 §7

#### Persistence (ADR-0004; store details: ADR-0024, not yet written)
- **JSON persistence of id structs uses explicit converters** — `System.Text.Json` ignores fields and would write `{}`; persist content ids as strings, never `DefIndex`. — source: ADR-0004 Implementation Guidelines

#### Network adapter seam (ADR-0011)
- **The Sim's only crossing points are `SimWorld.EnqueueOp`, the transform sample ring (both between ticks, Sim thread), `IEventSink.Deliver` (once per tick, phase 13), `ISnapshotSource.Write` (read-only, between ticks after `Deliver`, stamped with tick + owner version) and `IHostClock { uint EstimatedHostTick; bool IsValid; }`.** `PlayerSlot` is the only player-addressing concept crossing the seam. — source: ADR-0011 §1
- **The adapter core is Godot-free in `PerformativeMail.Client.Core/Net/`**: `HostAdapter` (peer table, actor stamping, handshake gate, decode → `EnqueueOp`, `busy`/`bad_request` replies, dedupe, `IEventSink`, per-peer queues, pacing, high-water valve, overflow resync, snapshots, join/rejoin) and `ClientAdapter` (u16 wrapping `reqId`, inbound decode into the `ClientLayer` inbox, `IHostClock` estimator). Only `ITransport` implementations live in `src/Game/Net/`. — source: ADR-0011 §2
- **`ITransport { Poll(ITransportSink); Send(peer, channel, bytes); Disconnect(peer, reason); }`**; logical `NetChannel` Reliable 1, Unreliable 0, Bulk 2, Comms 3. — source: ADR-0011 §2
- **The host's own client uses `ClientAdapter` over `LoopbackTransport` to its own `HostAdapter`** — same codec, `reqId`, dedupe, packing and ordering as a remote peer. — source: ADR-0011 §2
- **Frame order:** `transport.Poll` (remote) → `loopback.Poll` (host) → ticks, sending each tick's per-peer batches after it → loopback hands batches to the host `ClientLayer` inbox. A remote op beats a same-frame host op on `opSeq`. — source: ADR-0011 §2
- **Codecs live in the adapter core's registry keyed by `WireCodecId`** (pure `TryDecode`/`Encode` over spans); decode zero-initialises the `Op`, sets `PayloadLength`, zero-extends the u16 `reqId`. Request envelope `kind u16 · reqId u16 · payload`; decode failure/unknown kind/trailing bytes → adapter replies `bad_request` (8), enqueues nothing; a fuzz test (H-NET-5) asserts every input yields `bad_request` or a valid `Op` with no exception. — source: ADR-0011 §3
- **`HostAdapter` sets `Op.Actor` from its peer table**; unknown peers' messages before admission are dropped. — source: ADR-0011 §3
- **Dedupe per connection keyed `(peer, reqId u16)`**: last-16 reply cache (resend original, enqueue nothing) + in-flight set (drop silently); otherwise decode, `EnqueueOp`, add to in-flight; on `false` reply `busy`, do not cache. Reset on rejoin. — source: ADR-0011 §3, ADR-0007 §6
- **`ClientAdapter` sends each request exactly once on the reliable channel**; only a `busy`-answered request may be re-sent (as a new attempt); a missing reply after 5 s triggers a state resync carrying `ackedReqId`. — source: ADR-0011 §3
- **Admission:** no `PlayerJoin`, events or `DefIndex`-bearing message until `Hello{gameVersion, contentHash[32], profileId, displayName, kit}` is answered `HelloAck{ok}`; refusal precedence `ContentLoadFailed → VersionMismatch → ContentMismatch → RunInProgress`; on `ok` enqueue the `System` `PlayerJoin`. — source: ADR-0011 §4, ADR-0010 §6
- **Join/rejoin:** `joinTick` = tick whose phase 0 applied `PlayerJoin`; JoinState taken after that tick's `Deliver`; the joiner buffers batches with `tick > joinTick`, drops `≤ joinTick`, sends no container op until the last JoinState chunk applies; rejoin discards mirror, pending queue and `reqId` counter. Disconnect enqueues `System` `FreezePlayer` and drops the peer's queued batches. — source: ADR-0011 §4
- **`IEventSink.Deliver` (adapter) only:** (a) copies needed entries into preallocated buffers; (b) resolves audience masks to peer sets (batches hold peers, never slots); (c) updates the reply cache from `OpReply`; (d) tags same-`CallSeq` entries to the actor's peer with the reply's `reqId`. — source: ADR-0008 §7, ADR-0011 §6
- **After `Tick()`, each peer's entries are encoded into one ordered batch in log order** (`Internal` never leaves); `ContainerDelta`/`GroupCounts`/`UnitsRemoved` encode consecutive entries only; chunked runs reassembled in order; a by-reference entry is followed in place by its keyed snapshot. Per-peer outbound queue with byte accounting; bursts paced (`inventoryBurstBytes`); high-water valve (`inventoryHighWaterBytes`) discards one container's queued deltas, never a reply, and marks it resync-pending; overflow resyncs exactly `DroppedAudienceMask` × `DroppedKindsMask`. — source: ADR-0011 §6
- **Cross-call merging of entries on the wire is off until H-NET-1 and the version-gap tests prove it**, and then only under ADR-0008 §7 (i) same `Coalescible` kind and subject, (ii) at the earliest position, (iii) no other entry about that subject between. — source: ADR-0011 §6, ADR-0008 §7
- **The real transport (M0 spike + transport ADR) must provide the ADR-0011 §5 contract**: reliable-ordered per channel across message types (else a per-peer sequence + reorder buffer), bulk/unreliable/comms paths, callbacks only enqueue, stable peer handles, ≤ 64 KB messages intact (the adapter chunks JoinState/snapshots itself), raw bytes only. — source: ADR-0011 §5

### Forbidden Approaches
- **Never route automation through the inventory op FIFO; never use an event-per-mutation with embedded version pairs instead of the seal.** — source: ADR-0009 Alts 1–2
- **Never use a binary heap for the Intake outfeed, a timing wheel for WorldItem/bag/water expiries, or a per-entry `List<uint>` for `mailIds`** — source: ADR-0009 Alts 3–5
- **Never dedupe `reqId` inside inventory handlers** — source: ADR-0009 Alt 6
- **Never bump a container version per record (only +1 per call); never apply a container's records before its seal; never let the client re-validate a pending op to decide it failed.** — source: ADR-0009 §3–§4, Implementation Guidelines
- **Never send `dependsOn` on the wire** (client-only). — source: ADR-0009 §1
- **Never use `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any inventory tick path.** — source: ADR-0009 Implementation Guidelines
- **Never call Godot high-level RPCs with Variant arguments straight into the Sim; never decode untrusted bytes with `GD.BytesToVar` or any Variant deserialiser** — source: ADR-0011 Alt 1, §5
- **Never let the host's client call the Sim directly or add an "is host" shortcut** — source: ADR-0011 Alt 2
- **Never dedupe in the Sim; never retry un-acked requests at the application level** — a retry after cache eviction double-applies (HB10) — source: ADR-0011 Alts 3–4
- **Never commit to a transport (ENet/SceneMultiplayer channel map, Steam) before the M0 spike and transport ADR.** — source: ADR-0011 Alt 5
- **Never reference peers, connections, channels, RPC names or Godot types from the Sim; never put a wire codec in the Sim.** — source: ADR-0011 §1, Implementation Guidelines
- **Never call the transport, Godot or the Sim inside `Deliver`, or retain its span; never reorder two entries to one peer (except the conditioned merge); never let the high-water valve discard a reply; never send before `Tick()` returns.** — source: ADR-0008 §7, ADR-0011
- **Never use payload fields for identity.** — source: ADR-0011

### Performance Guardrails
- **Inventory probe gates (BLOCKING in CI):** `PeekOutfeed`/`TakeUnit` ≤ 4 candidate entries amortised (n = 320, 6 400); repeat sort 0 probes; idle drain 0 probes; cap eviction ≤ created + 1 candidates; automation path 0 B allocated — source: ADR-0009, registry `inventory_probe_gates`
- **Inventory CPU share**: 1.0 ms median / 2.0 ms p99 (advisory) — source: ADR-0009 Performance Implications
- **Inventory memory**: `EntryRecord` ≤ 40 B × capacity (8 192 → 320 KB); arena ≈ 2 × peak units × 4 B; bitmaps/indices < 64 KB; total inside H-PERF-5's 1 MB with 512 WorldItems; arena classes peak < 50 % on the H-R19-1 fuzz fixture (test) — source: ADR-0009 §5, Performance Implications
- **Inventory wire budget**: ≤ 40 B/tick/peer sustained (`ContainerDelta`) — source: ADR-0009 Performance Implications
- **Adapter memory per peer**: outbound queue ≤ high-water (24 KB PROPOSED) + paced burst buffer (≤ 800 B/tick PROPOSED); dedupe cache 16 replies × ~32 B; all preallocated — source: ADR-0011 §6, Performance Implications
- **Network envelope**: 4 B per request; one batch packet per peer per tick; empty-tick Sim output 0 B — source: ADR-0011, ADR-0008

---

## Feature Layer Rules

*Applies to: the Sim half of every gameplay system — Player Character & Movement, Mail & Destinations, Manual Delivery, Throwing & Catching, Building, Belt Logistics, Vehicles, Combat, Enemy AI & Waves, Defenses, Harvesting, Economy, Run Structure, Perks, Stamps & Run Events, Meta-Progression, Comms. The checklist below repeats the Foundation rules a system author touches. It is followed by the system rules of the Accepted feature ADRs: ADR-0013 and 0014 (Player Character), ADR-0015 (Throwing) and ADR-0017 (Mail). ADR-0018 and ADR-0020–0031 are not written yet.*

### Required Patterns
- **Each system lives in `PerformativeMail.Sim/<System>/`, is marked `[SimSystem(SystemId)]`, and has exactly one public constructor** whose parameters come only from the ADR-0003 §4 injectable list (`EventLog` writer, `EntityIds`/`MailIds`, read-only `SlotTable`, its owner-filtered `IRngStreams`, `ITickReader`, `IRunControl` [Run Structure only], `I<System>Api` of declared dependencies, `ContentSet`/sub-table, `IStatSheet`, `IStatWriter` [Perks, Stamps only], `SimCheckRing`, `SimCounters`, its own settings record, `IWorldQuery` [any system, ADR-0026; erratum 2026-10-17], `IWalletGate` [Mail & Destinations only; ADR-0017 §8 port, erratum 2026-10-28 — injected by the composition root via `SimSystemContext.WalletGate`, not a `SystemGraph` edge]). — source: ADR-0003 §4 (errata 2026-10-17, 2026-10-28)
- **Declare dependencies in `Composition/SystemGraph.cs`; add construction to `SimComposition` in topological order.** — source: ADR-0003 §4
- **Bind only phases you own** (ADR-0003 §1 table) and state intra-phase order in your GDD; same-phase binders run in composition order. — source: ADR-0003 §1
- **Player and `System` requests are `OpKind`s in your block with an `OpDescriptor`**; in-tick effects on other systems are synchronous `I…Api` calls inside your own phase, going down the dependency map; information goes up through events read by pull cursors. — source: ADR-0003 §5, ADR-0007 §3–§4
- **Every mutating `I…Api` method starts with `TickGuard.AssertMutable()` and opens `using var scope = log.BeginCall();`**, is one atomic mutation, raises events on commit, returns no reply. — source: ADR-0003 §5, ADR-0008 §5
- **Op handlers validate fully before mutating, allocating ids, drawing RNG or raising events; return exactly one `Reply`; system reason codes ≥ 64 declared in the descriptor.** — source: ADR-0007
- **Event kinds declare class, ≤ 48 B padding-free payload, `ResyncScope`, `Coalescible` if rewritten; audience resolved at raise.** — source: ADR-0008
- **Use only the RNG streams your system owns from `RngNames`** (Mail: `mail`, `mail.special` by shift; Enemy AI & Waves: `waves`, `mega`; Health & Damage: `drops`; Perks: `perks`; Economy: `shop`; Stamps & Run Events: `events`; World Generation: `worldgen.<stage>`). Mail draws spawn and address from `mail` in a fixed order. — source: ADR-0005 §2
- **All quantities use ADR-0006 units and helpers** (mm, permille, `Cents`, `Fx16`, BAM, ticks as absolute deadlines); one rounding per formula. **Exception:** the carrier position uses tick-rational remainders (1/30 mm horizontal, 1/60 mm vertical), never `Fx16`. — source: ADR-0006 §1–§2, ADR-0013 §2
- **Bind an `ISimStateContributor` for any state; size every collection from `SimSettings`; allocate 0 B in the tick path.** — source: ADR-0006 §4, §8
- **Read stats through `IStatSheet.Read`**; only Perks & Perk Draft and Stamps & Run Events push modifiers via `IStatWriter`. — source: ADR-0010 §7
- **Run Structure is the one orchestrator**: it alone holds `IRunControl`; `RequestStop(StopReason)` is legal in phase 12 (from other phases it is latched and counted `stopOutOfPhase`; a second stop is ignored and counted). — source: ADR-0003 §5
- **"Hands beat machines":** on a contested last free cell a player op (phase 0) beats an inserter (phase 5); the inserter retries next tick. — source: ADR-0003 §5
- **Damage raised in phase 11 or later applies on T+1** (Health's pending list); `RequestStop` from phase 12 takes effect after phase 13 of the same tick. — source: ADR-0003 §1
- **Pure shared functions (`holdEff`, Mail `Evaluate`, the flight query, ballistic) live in the Sim assembly and are called by both `SimWorld` and `ClientLayer`.** — source: ADR-0003 §6
- **Ids: allocate via injected `EntityIds`/`MailIds` inside `Tick()` only; order by raw `Value`; narrow wire ids from an owner id map** (e.g. Throwing's receiver map). — source: ADR-0004
- **Inventory interaction goes only through `IInventoryApi`** (boundary crossings into Flight, Belt, Backlog included) — for systems outside Inventory; Inventory's own `WorldItems` and `TryAppendDetached` are internal (ADR-0009 §6 erratum 2026-10-22). — source: ADR-0009 §6


#### Player Character — carrier solver (ADR-0013)
- **Movement is computed only by the pure `OwnedCarrier.Step`**: no clock, RNG, statics (except `ReadOnlySpan` literal tables), allocation or exceptions; integer maths widened to `long` before the first multiply. The host never steps an Alive remote carrier, and the host role calls the same function with `HostAuthored` input. — source: ADR-0013
- **Other players and supports are read only through `IRemoteCapsuleSource`/`ISupportTransformSource` at tick time.** Render-time poses never enter the Sim. — source: ADR-0013 §7
- **Sub-steps are ≤ 300 mm and ≤ 4 per tick, with ≤ 3 slides per sub-step (counted). Candidate buffers are fixed-capacity `CarrierScratch` fields; overflow is counted, never grown.** — source: ADR-0013
- **Every constant comes from the `players` `SimSettings` record or the StatSheet, never a code literal.** — source: ADR-0013 (ContentValidator PC 1–12)

#### Player Character — interaction framework & holds (ADR-0014)
- **Every interactable verb registers through `IPlayersApi.RegisterAnchor` (2 m dense grid; one verb per anchor per context; `DuplicateVerb` otherwise). `IInteractable` hooks are pure: no mutation, events, RNG or allocation.** — source: ADR-0014 §1
- **Focus uses the §2 integer score, the total tie-break (`EntityId`, then `Slot`) and the 4-tick dwell; the cone comes from `InputFrame.FocusCosMinPm`. Focus is latched while interacting.** — source: ADR-0014 §2
- **Press resolution order: Catch (`ICatchPredictor`, D3 gate) → the presented target's single verb (in `RangeMm`, `IsAvailable` now) → catch-armed.** — source: ADR-0014 §3
- **Holds are counted only at `PreviewSubStep.HoldAdvance` in owner Sim ticks, with `heldTicks = tick − PressTick`. Edges are processed before accrual. The cancel range is `RangeMm + incumbentSlackMm`, compared strictly on squared distances. The host never times a hold or receives a duration.** — source: ADR-0014 §4
- **`holdEff` exists once, as `Hold.HoldTicks` (`long`, one `CeilDiv`), and every caller uses it.** — source: ADR-0014
- **Range-gated op handlers call `IPlayersApi.CheckInteraction(actor, anchor, ClaimedPosition)` first and map its result.** The chain is `Dead → Frozen → NoTarget → Unavailable → PositionStale/OutOfRange`, the host range is `RangeMm + rangeSlackMm`, and `ClaimedPosition` (12 B) rides in every range-gated payload. — source: ADR-0014 §5
- **Life-state deadlines are absolute ticks. The knockdown chain runs in phase 11 in §6's order as a pull reader over `PlayerDied`; Players never calls dependants upward.** — source: ADR-0014 §6

#### Throwing (ADR-0015)
- **Host, preview and every peer call the same `LaunchMath`, `Ballistic` and `FlightSweep`; there is no second implementation (Presentation included).** — source: ADR-0015
- **Throwing's RH is `Throw.RH` (floor-based); never apply C#'s truncating `/` to a signed numerator.** — source: ADR-0015 §2
- **Flights are processed in creation order and `FlightId` is never compared numerically. Ties break on `(f, rank, dist², Mix64)`. Quadratic tests run only after the broad-phase reject (|R| ≤ 65 536 mm).** — source: ADR-0015
- **World solids come from `IWorldQuery.QueryStaticSolids` (never a second world grid).** The preview cache key uses the construct registries' version, because `WorldTables` is immutable. — source: ADR-0015, ADR-0026 §5
- **Every flight ends in exactly one terminal. Units leave Flight only through `IInventoryApi`/`IMailApi`, damage is queued (never applied in phase 4), and run end flushes via `IThrowingApi.FlushAllAsTimeout` from phase 12 only.** — source: ADR-0015
- **`maxFlights` = 160 preallocated; `Throwing.Step` allocates 0 B; Throwing holds no RNG stream.** — source: ADR-0015

#### Mail & Destinations (ADR-0017)
- **`AddressId` is a packed u32 in `Sim/Primitives`: district 6 bits, street 8, number 12, unit 6; 0 = None. Numeric order is manifest order. On the wire it narrows to an ordinal u16 through id maps.** — source: ADR-0017 §1
- **The mail table is server-only SoA with no location column (location is the `UnitLedger`'s). Every consume path goes through `MailConsumer.Consume`.** — source: ADR-0017
- **`Evaluate` and `MailPay` are static, pure and 0-alloc over facts structs.** — source: ADR-0017, ADR-0016
- **`Sim.Mail` (namespace `PerformativeMail.Sim.MailDestinations`, see Errata 2026-10-20) never references `Sim.Economy` (arch test); the wallet is reached only through `IWalletGate` in `Cents` (`StubWallet` at M-1a).** — source: ADR-0017
- **Complaint buckets refill lazily on touch, and an idle `Mail.Step` touches only feeder credits. The registry is built from `IWorldQuery.Lots`.** — source: ADR-0017, ADR-0026 §6
### Forbidden Approaches
- **Never enqueue ops from in-tick system code.** — source: ADR-0003 §5, ADR-0007
- **Never subscribe synchronously to another system, hold a `SimWorld` reference, use statics or a service locator.** — source: ADR-0003, ADR-0008
- **Never give Throwing an RNG stream** (`throwNoDice`); if randomness is ever needed it is a new `RngNames` entry indexed by a per-run launch ordinal, never `flightId`. Throw spread in mrad is retired. — source: ADR-0005 §2, ADR-0006 §1
- **Never use the retired streams `mail.spawn`/`mail.address`.** — source: ADR-0005 §2
- **Never write container, entry or ledger state outside `InventoryMutator`.** — source: ADR-0009
- **Never use floats, `System.Random`, clocks, `Dictionary` iteration or unstable sorts in system code** (ADR-0006 determinism checklist). — source: ADR-0006 §3
- **Never use a Godot physics body, `CharacterBody3D`, Jolt query or engine interpolation for player movement.** — source: ADR-0013
- **Never re-target a hold to the current focus, or time holds on the host.** — source: ADR-0014 Alts 1, 3
- **Never write a second ballistic or sweep implementation, or build a second world-solid grid.** — source: ADR-0015, ADR-0026
- **Never add a second payment or penalty path, or let Inventory call Mail.** — source: ADR-0017

### Performance Guardrails
- **Per-system budgets live in `perf-budget.md` (not yet written)**; until then the Foundation host-tick budget and 0-alloc gate apply. — source: ADR-0003 Performance Implications, architecture.md
- **Owner `CarrierStep` + focus p99 ≤ 0.10 ms with 2 000 anchors (PCM-PERF-1); ClientLayer total p99 ≤ 0.3 ms per client tick; 0 B per tick (advisory).** — source: ADR-0013, ADR-0014, ADR-0016
- **Mail: ≤ 1.0 ms p99 / 2 ms max per tick; manifest roll ≤ 0.5 ms; a 40-unit op ≤ 0.2 ms; 0 B on hand/throw/feeder paths. Throwing perf is budgeted at 16 flights, advisory at 60.** — source: ADR-0017, ADR-0015

---

## Presentation Layer Rules

*Applies to: Godot views, HUD, UI, audio, VFX, camera, the Godot input adapter (`src/Game/**`), and the Godot-free `PerformativeMail.Client.Core` (Ui, Input router).*

### Required Patterns
- **Presentation is called explicitly by `GameLoop` after `RunFrame` via `IPresentationRoot.Present(AlphaPermille)`**; views may animate in their own `_Process` from data `Present` handed them, and should animate from `AlphaPermille` rather than `delta`. — source: ADR-0002 §3
- **Remote/own-carrier smoothing interpolates or extrapolates ≤ 1 tick using `AlphaPermille / 1000f`** (float is allowed in Presentation only). — source: ADR-0002 §1, §3
- **Godot views map `EntityId` → node through a C# `Dictionary` owned by Presentation**; id structs travel no further than Presentation's C# layer; pass `.Value` to Godot and range-check when converting back. — source: ADR-0004 Engine Compatibility, Implementation Guidelines
- **Cosmetic randomness (particles, wobble) uses a Presentation-side generator that never feeds the Sim**; flight spin is a pure function of `flightId`. — source: ADR-0005 §3
- **Client-side logic that needs wall clocks lives in `Client.Core` behind injected clocks**, never in the Sim. — source: ADR-0001 §1 (Client.Core role), architecture.md §Physical assemblies
- **Godot file access for content is only `ContentHost : IContentSource` (ADR-0019)**, reading bytes with `Godot.FileAccess.GetFileAsBytes` (never `get_as_text`) and checking `FileAccess.GetOpenError()`. — source: ADR-0010 Engine Compatibility, §1
- **Compare `InputEvent.Device` against `InputEvent.DeviceIdMouse` / `DeviceIdKeyboard` (engine names `DEVICE_ID_MOUSE` / `DEVICE_ID_KEYBOARD`; C# name: verify against GodotSharp 4.7.1), never `0`** (some joypads use 0). — source: deprecated-apis.md, current-best-practices.md (4.7)
- **4.7.1 C# API shapes:** `Animation.Length` is `double`; `Control.AccessibilityLive` is typed `AccessibilityServer.AccessibilityLiveMode`; `AnimationPlayer.AssignedAnimation/Autoplay/CurrentAnimation` are `StringName` (`GetQueue` returns `StringName[]`); `FileAccess.Store*` return `bool` — check every write. — source: breaking-changes.md
- **`AudioStreamPlayer.AreaMask` defaults to 0 (was 1)** — set it to layer 1 when using `audio_bus_override` on areas. — source: breaking-changes.md
- **Use `Resource.DuplicateDeep(Resource.DeepDuplicateMode.All)` (engine name `duplicate_deep(DEEP_DUPLICATE_ALL)`; C# name: verify against GodotSharp 4.7.1) where a full sub-resource deep copy is required.** — source: deprecated-apis.md, current-best-practices.md (4.5)
- **Run Project > Tools > Upgrade Project Files once after moving the project to 4.6+** (avoid piecemeal re-save diffs). — source: current-best-practices.md (4.6)
- **Jolt (4.6+ default) affects client-side presentation physics only**; the Sim uses its own solver. — source: current-best-practices.md, technical-preferences.md
- **Godot-side tests use gdUnit4, deferred to their own story** (needs the Godot .NET build; until then [UI] evidence goes to `production/qa/evidence/`). — source: ADR-0001 §7, tests/README.md

- **Godot input adapter (ADR-0012 §7, amended 2026-10-17 by the OQ-IC-9 spike):** it stamps `_Input` arrivals into `RawInputRecord`s for the router. HUD and in-world Controls use `FocusMode = None`, and entering a game context calls `GuiReleaseFocus()`. `ignore_joypad_on_unfocused_application = true`. Focus loss synthesises release-with-cancel and pauses through `TickDriver.Pause()` in solo. — source: ADR-0012 §7
- **The adapter is the last child of the root window and re-asserts it (`MoveChild(this, -1)`) when a node enters the root (`GetTree().Root.ChildEnteredTree`; likely via `CallDeferred`, verify in story 028); `ProcessMode = Always`.** Input callbacks run in reverse tree order and `ProcessPriority` does not affect input order. `Window.WindowInput` may be used for arrival stamping only. — source: ADR-0012 §7 (D-1)
- **Treat `Input.MouseMode = Captured` as a request: re-apply on application/window focus-in and on the first click into the window, and check the readback.** Wayland rejects capture without pointer focus; X11 does not restore the cursor position on returning to `Visible`. — source: ADR-0012 §7 (D-2)
- **Subscribe to the adapter window's `FocusExited`/`FocusEntered` as well as `NotificationApplicationFocusOut`; either triggers the idempotent `OnFocusLost()`; the router's synthesized releases stay mandatory (the engine clears pressed keys without release events).** — source: ADR-0012 §7 (D-4)
- **Menus outside play that want pad A/B confirm/back handle them in the menu's own navigation owner** (stock `ui_*` binds pad D-pad, left stick, Y and X on device −1, and no pad button to `ui_accept`/`ui_cancel`). — source: ADR-0012 §7 (D-5)
- **Compare `InputEvent.Device` (`int`) directly with `InputEvent.DeviceId*` (`long`); cast when assigning (`(int)InputEvent.DeviceIdEmulation`).** — source: ADR-0012 §7 (D-6)
- **Budget ≈410–550 managed bytes per engine-created event delivered to C# `_Input`; keep `use_accumulated_input` true; `Dispose()` engine-created events in `_Input` if the Release retention spike confirms retention (story 026 is not Done until it does or clears it).** — source: ADR-0012 §7 (D-8)
- **Pools are pre-instantiated at boot within ADR-0019 §5's caps. Steady-state frames instantiate no nodes and allocate 0 B in the panel and ribbon.** — source: ADR-0019
- **Presentation time comes from `IRenderClock`.** `delta`, Godot `Timer` and `Time.GetTicksMsec` are banned for gameplay-facing timing. Outcome beats play only from `PresentationEventStream`. — source: ADR-0016
- **Presentation colliders (camera boom, picking) are a copy of `IWorldQuery` geometry and need a parity test against `StaticSolids`.** — source: ADR-0013 Risks
### Forbidden Approaches
- **Never mutate the Sim from Presentation, call `SimWorld`/`ClientLayer`/`FrameRunner` from a view's `_Process`/`_PhysicsProcess`, or move Sim-driven entities in `_PhysicsProcess`.** — source: ADR-0002 §3
- **Never use Godot physics interpolation for Sim-driven entities** (`physics_interpolation` stays false; `RenderingServer.instance_set_interpolated`/`instance_reset_physics_interpolation` were removed in 4.5). — source: ADR-0002 §3, deprecated-apis.md
- **Never consume only one half of a press/release pair in the input adapter** (consuming a release after the GUI saw the press leaves a Control stuck pressed). — source: ADR-0012 §7 (D-3)
- **Never rely on `ProcessPriority` for input order, and never call `Input.GetJoyInfo`/`GetJoyName` for an index not in `Input.GetConnectedJoypads()`.** — source: ADR-0012 §7 (D-1, D-7)
- **Never use `SceneTree.Paused` for game pause** (UI may pause the tree; the Sim must be unaffected). — source: ADR-0002 §3
- **Never pass id structs as `[Signal]` parameters, `[Export]` fields, `SetMeta` values, `Godot.Collections` elements, `Callable` binds or Variant RPC arguments.** — source: ADR-0004 Implementation Guidelines
- **Never let `ClientLayer` or Presentation read `SimWorld` directly** (host included; use the mirror). — source: ADR-0003 §6
- **Never hold Sim content in a Godot `Resource`.** — source: ADR-0010 §3
- **Never use `FileAccess.get_as_text(skip_cr)`** (parameter removed in 4.6). — source: deprecated-apis.md
- **UI: never use `RichTextLabel.add_image(... size_in_percent ...)` or `ImageUpdateMask.UPDATE_WIDTH_IN_PERCENT`** (use `width_unit`/`height_unit`, `UPDATE_WIDTH_UNIT`, 4.7). — source: deprecated-apis.md
- **Audio: never use `AudioEffectSpectrumAnalyzer.tap_back_pos`** (removed 4.7). — source: deprecated-apis.md
- **Never use `Resource.duplicate(true)` expecting a full deep copy** (behaviour changed 4.5). — source: deprecated-apis.md
- **Never use `DrawLine`/`DrawPolyline` in the ribbon or HUD; never feed a Presentation value (rig offset, camera, animation time) into the Sim; never reimplement a Sim rule or formula in Godot or Client.Core.** — source: ADR-0019, ADR-0016

### Performance Guardrails
- **Client frame**: 60 fps / 16.6 ms at 1080p on a mid-range GPU during a shift-5 raid — source: technical-preferences.md
- **Draw calls / client memory ceiling**: not configured — source: technical-preferences.md `[TO BE CONFIGURED]`

---

## Global Rules (All Layers)

### Naming Conventions
| Element | Convention | Example | Source |
|---------|-----------|---------|--------|
| Classes / structs / enums / delegates / namespaces | PascalCase (Godot node classes `partial`) | `TickDriver`, `public partial class GameLoop : Node` | `naming.classes` in project.yaml; technical-preferences.md; `.editorconfig` |
| Interfaces | `I` + PascalCase | `IEventSink` | `.editorconfig` |
| Type parameters | `T` + PascalCase | `TPayload` | `.editorconfig` |
| Public/internal/protected members | PascalCase | `MoveSpeed`, `CurrentTick` | technical-preferences.md; `.editorconfig` |
| Private fields | `_camelCase` | `_runner` | technical-preferences.md; `.editorconfig` |
| Locals / parameters | camelCase | `maxCatchUpTicksPerFrame` | `naming.variables` in project.yaml; `.editorconfig` |
| Constants | PascalCase | `TickRate.Hz`, `MaxHealth` | `naming.constants` in project.yaml |
| Godot signals (`[Signal]` delegates) | PascalCase + `EventHandler` suffix | `HealthChangedEventHandler` | technical-preferences.md; `.editorconfig` (delegates are types) — see N-1 |
| Sim events | `EventKind` enum members, PascalCase; no C# `event`s in the Sim | `OpReply`, `InvInserted` | ADR-0008 |
| Files | PascalCase matching the class | `TickDriver.cs` | `naming.files` in project.yaml |
| Scenes | PascalCase matching the root node | `PlayerView.tscn` | `naming.scenes` in project.yaml |
| Test classes / methods | `[System]Tests` / `Scenario_Expected` (underscores allowed under `tests/`) | `InsertLetter_IntoFullStack_Rejected` | tests/README.md; `.editorconfig` |
| Def ids (content) | `^[a-z][a-z0-9_]{0,47}$`; data spelling snake_case | `parcel_small` | ADR-0010 §3, §7 |
| RNG stream names | lowercase ASCII, dot-separated | `mail.special` | ADR-0005 §2 |
| Content numeric fields | unit suffix (`…Seconds`, `…Metres`, `…Ratio`, `…Points`, `…Deg`, `…Ms`, `…Mm`, `…Pm`, `…Ticks`) | `throwRangeMetres` | ADR-0010 §2 |
| `Stat` enum members | PascalCase + unit suffix | — | ADR-0010 §7 |

`.editorconfig` naming rules are **build errors** (IDE1006 at `warning` + `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild`). Also enforced: file-scoped namespaces, braces always, no `this.` qualification, explicit accessibility modifiers, LF line endings, 4-space indent (2 for md/yml/json/csproj/props; tabs for `.tscn/.tres/.godot/.import/.cfg`). — source: `.editorconfig`, `Directory.Build.props`

### Performance Budgets
| Target | Value | Source |
|--------|-------|--------|
| Framerate | 60 fps | technical-preferences.md (no `performance.*` in project.yaml) |
| Frame budget | 16.6 ms client frame at 1080p, mid-range GPU, during a shift-5 raid | technical-preferences.md |
| Host Sim tick | p99 5.0 ms / max 8.0 ms / median 3.0 ms (advisory) | architecture.md TR-sim-037 |
| Dedicated server tick | ≤ 8 ms (8 players, 400 belt tiles, 40 enemies) | technical-preferences.md |
| Draw calls | [TO BE CONFIGURED] | technical-preferences.md |
| Memory ceiling | client [TO BE CONFIGURED]; dedicated server ≤ 512 MB; Sim heap ≤ 64 MB in a dedicated process (BLOCKING from M0) | technical-preferences.md; simulation-core GDD R32 / SC-R32-1 |
| Tick allocation | 0 B after 300 warm-up ticks (net8.0; blocking from M0) | ADR-0006 §8 |

### Approved Libraries / Addons
- `technical-preferences.md` "Allowed Libraries / Addons": none configured.
- Fixed by Accepted ADRs (build/test-time only; **no runtime package may enter Sim or Client.Core**):
  - `Godot.NET.Sdk/4.7.1` — MSBuild SDK of `PerformativeMail.Game.csproj` (restored from nuget.org; no committed `nuget.config`) — source: ADR-0001 §2
  - `xunit.v3` 4.0.2 on Microsoft.Testing.Platform — test project only — source: ADR-0001 §7
  - `Microsoft.CodeAnalysis.BannedApiAnalyzers` — analyzer, `PrivateAssets="all"`, version pinned in the csproj — source: ADR-0001 §6, ADR-0006 §3
  - Custom Roslyn analyzer `PerformativeMail.Sim.Analyzers` (`tools/PerformativeMail.Analyzers/`, netstandard2.0) — source: ADR-0001 §6, ADR-0006 §3
  - BCL APIs explicitly relied on: `System.Text.Json.Utf8JsonReader`, `System.Collections.Immutable.ImmutableArray<T>`, `Int128`/`UInt128`, `[InlineArray]`, `BinaryPrimitives`, `MemoryMarshal`/`Unsafe` — source: ADR-0006, ADR-0007, ADR-0010
  - gdUnit4 — planned for Godot-side tests, not yet installed — source: tests/README.md, ADR-0001 §7

### Forbidden APIs (Godot 4.7.1)
These Godot APIs are renamed/removed for 4.7.1 and apply to this project's Godot-side code (the Sim uses no Godot API). Source: `docs/engine-reference/godot/deprecated-apis.md`.

| Don't use | Use instead | Since | Applies to |
|---|---|---|---|
| `StreamPeerTCP.get_status/poll/disconnect_from_host` (on TCP) | declared on `StreamPeerSocket` | 4.6 | transport (`src/Game/Net/`) |
| `TCPServer.is_connection_available/is_listening/stop` (on TCP) | declared on `SocketServer` | 4.6 | transport |
| `JSONRPC.set_scope` | `JSONRPC.set_method` | 4.5 | transport, if JSONRPC is ever used |
| `Node.get_rpc_config` | `Node.get_node_rpc_config` | 4.5 | transport, if RPC config is read (GDScript-only break) |
| `FileAccess.get_as_text(skip_cr)` | `get_as_text()` — for content read bytes via `GetFileAsBytes` | 4.6 | `ContentHost`, persistence |
| `InputEvent.device == 0` for mouse/keyboard | `InputEvent.DEVICE_ID_MOUSE` / `DEVICE_ID_KEYBOARD` | 4.7 | input adapter |
| `RenderingServer.instance_set_interpolated`, `instance_reset_physics_interpolation` | removed — no replacement | 4.5 | views (physics interpolation is off anyway) |
| `Resource.duplicate(true)` for deep copy | `Resource.duplicate_deep(DEEP_DUPLICATE_ALL)` | 4.5 | Godot-side resources |
| `RichTextLabel.add_image(... size_in_percent ...)` | `width_unit` / `height_unit` | 4.5 / 4.7 | UI |
| `RichTextLabel.ImageUpdateMask.UPDATE_WIDTH_IN_PERCENT` | `UPDATE_WIDTH_UNIT` | 4.7 | UI |
| `AudioEffectSpectrumAnalyzer.tap_back_pos` | removed | 4.7 | audio |
| `RenderingDevice.Features.Address` | `BufferDeviceAddress` | 4.5 | only if `RenderingDevice` is used (none planned) |

**Unverified for 4.7.1 (do not rely on without a spike):** high-level multiplayer (`MultiplayerAPI`, `SceneMultiplayer`, ENet, `[Rpc]`, callback thread, `GetRemoteSenderId`, packet sizes) — ADR-0011 §5 M0 spike checklist; export runtime/`InvariantGlobalization`/PCK filter for `*.jsonc` — ADR-0001, ADR-0010 S1/S2/S4; `low_processor_mode`, `ProcessThreadGroup`, `physics_interpolation` setting behaviour, `delta_smoothing` — ADR-0002 §3; C# source-generator/`[Signal]` patterns — current-best-practices.md (NOT SOURCEABLE).

### Cross-Cutting Constraints
- **Dependencies point one way: Godot (`PerformativeMail.Game`) → `Client.Core` → `Sim`.** — source: ADR-0001 §1, ADR-0011
- **The Sim is the only authoritative state; it knows no peers, accounts, connections, channels, RPC names, wall clock, RTT or Godot type.** — source: ADR-0003 §6, ADR-0011 §1
- **The Sim's only crossings are ADR-0011 §1's: `EnqueueOp` and the transform sample ring (in), `IEventSink.Deliver` (the only per-tick event output, phase 13), `ISnapshotSource.Write` (read-only, between ticks), `IHostClock`.** — source: ADR-0011 §1, ADR-0008 §7
- **Main-thread only: every Sim entry point runs on the owner thread captured at `Freeze()`.** — source: ADR-0002 §5, ADR-0003 §3
- **Integer-only, culture-free, ordered, statics-free determinism (ADR-0006 checklist) for all state-affecting Sim code; T1 per tick, T2 on Windows + Linux on net8.0.** — source: ADR-0006
- **Hash, RNG, serial and BAM arithmetic is always inside `unchecked(...)`; all else stays in the default context.** — source: ADR-0001 §4, ADR-0005, ADR-0006 §3
- **Every Sim build-time table (phases, op/event descriptors, readers, contributors) is frozen at `Freeze()`; nothing grows after warm-up.** — source: ADR-0003, ADR-0006 §8
- **One code path: loopback uses the remote codec; host and client run the same pure functions.** — source: ADR-0003 §6, ADR-0011 §2
- **Content is identified by its `ContentHash`; `DefIndex` crosses the wire only after the handshake confirmed the hash, and is never persisted.** — source: ADR-0004 §6, ADR-0010 §6, ADR-0011 §4
- **Any Godot 4.4–4.7 behaviour not in `docs/engine-reference/godot/` gets a spike before code depends on it.** — source: architecture.md principle 5, ADR-0011 Alt 5

---

## Cross-ADR inconsistencies (follow the owning ADR)

Found while extracting; none edits an ADR. Each needs an erratum on the non-owning ADR at its next revision. Until then, follow the "Follow" column. — logged in `production/session-logs/decision-log.md`

| # | Topic | Conflict | Follow |
|---|---|---|---|
| I-1 | `IPhaseProbe` shape | ADR-0003 `Before(PhaseId)/After(PhaseId)` vs ADR-0006 Key Interfaces `Enter(ProbeScope)/Exit(ProbeScope)` | **ADR-0003** (owner per its port-interface list) |
| I-2 | `TickContext` contents | ADR-0003 `{ uint Tick; PhaseId Phase; }`, services by constructor vs ADR-0007 "carries CurrentTick, the EventLog writer and counters" | **ADR-0003** (defines `TickContext`) |
| I-3 | Event reader / descriptor names | ADR-0003 `BindReader(cursor, kindFilter)`, `OpKindDescriptor`, `EventKindDescriptor` vs ADR-0008 `BindEventReader(ReaderId, EventKind[])`, ADR-0007 `OpDescriptor`, ADR-0008 `EventDescriptor` | **ADR-0007/0008** (own the shapes) |
| I-4 | `EnqueueOp` signature | ADR-0011 seam listing `EnqueueOp(in Op op)` vs ADR-0003/0007 `EnqueueOp(ActorId actor, in Op op)` | **ADR-0007** |
| I-5 | Content-hash SHA-256 | ADR-0006 §4 "BCL's managed SHA-256 inside the Sim" vs ADR-0010 §6 hand-written SHA-256, no `System.Security.Cryptography` reference | **ADR-0010** (owner) |
| I-6 | `ContainerId` type | ADR-0009 `readonly record struct ContainerId(uint Value)` vs ADR-0004 (explicit `IEquatable`/`GetHashCode`/`==` on every id struct) and ADR-0006 (`record struct` only for ids never in ordering or hashing) | **ADR-0004/0006**: plain `readonly struct` with explicit equality |
| I-7 | Adapter hook names | ADR-0002 `IFrameTransport.DrainInbound/FlushAfterTick` vs ADR-0011 `HostAdapter.PumpInbound/FlushOutbound(tick)` (ADR-0002 defers final names to ADR-0011) | **ADR-0011** |
| I-8 | MSBuild keying wording | ADR-0006 §3 "props must key on configuration names" vs ADR-0001 "never key on `Configuration == 'Release'`; key on `$(Optimize)`/`$(SimAsserts)`" — compatible: the props map `Export*` names → `Optimize`, asserts key on `$(Optimize)` | **ADR-0001** §4 XML |
| N-1 | Naming config | `project.yaml` `naming.signals: on_event_name` and `naming.variables: camelCase` vs technical-preferences.md (`…EventHandler` PascalCase delegates; public PascalCase, private `_camelCase`) — `.editorconfig` makes the latter a build error | **technical-preferences.md + `.editorconfig`** (the only form that compiles); `project.yaml` `naming.*` needs correcting (it is the declared primary source) |
| I-9 | Test/Godot project paths | `tests/README.md` (`<Compile Remove="src/**;tests/**" />`) and technical-preferences.md Testing (`tests/Sim.Tests`, `tests/Net.Tests`) vs ADR-0001 §2/§7 (exclude `src/PerformativeMail.*/**;tests/**;tools/**;prototypes/**;**/obj/**;**/bin/**`; one test project `tests/PerformativeMail.Sim.Tests/`) | **ADR-0001** |
| E-1 | Local .NET 8 runtime | ADR-0005/0006 say only .NET 10 runtimes are installed locally | Environment fact (2026-10-16): **8.0.32 is installed**; the net8.0 leg can run locally |
| I-10 | `IWorldQuery` members | ADR-0013 §6 draft listing (`QueryStaticBoxes`, `StaticVersion`, `GetTileFlags`) vs ADR-0026 Key Interfaces | **ADR-0026**. Resolved before acceptance on 2026-10-16: ADR-0013 §6 now states requirements mapped to ADR-0026, and ADR-0015 uses `QueryStaticSolids` |
| I-11 | Focus / press / hold placement | ADR-0014 §7 draft (tail of `CarrierStep`) vs ADR-0016 §2 (`Previews` 4a/4d/4e) | **ADR-0016**. Resolved 2026-10-16: ADR-0014 amended |
| I-12 | ADR-0017 ↔ ADR-0026 dependency cycle; `AddressId` home | ADR-0026 depended on ADR-0017 for `AddressId`, and ADR-0017 on ADR-0026 for `Lots`. `AddressId` sat in `Sim/Mail` but was used by World and Inventory | Resolved 2026-10-16: ADR-0026 references the type without depending on ADR-0017, and `AddressId` moves to `Sim/Primitives` |
| I-13 | Carrier `Fx16`; `throw` OpKind owner | ADR-0006 Enables "`Fx16` carrier step" vs ADR-0013 §2. ADR-0009 cited ADR-0021 for `ThrowRelease` | **ADR-0013**, **ADR-0015 §1**. Errata recorded on ADR-0006 and ADR-0009 on 2026-10-16 |
| E-2 | `perf-budget.md` | Referenced by ADR-0003/0007/0008 and architecture.md as the home of per-phase slices; the file does not exist | Use TR-sim-037 plus the ADR-0007/0008 slices above; write `perf-budget.md` before any perf-gate story |
