# Story 025: Composition root, SystemGraph and the constructor reflection test

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M–L (≈ 1 day; ≈ 500 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R4 binding and composition; R2 two roles composed together)
**Requirement**: `TR-sim-012` (builder violations throw `SimBuildException` — dependency cycle row), `TR-sim-013` (composition root in `Composition/`; constructor injection only; reflection test on dependencies)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0003: Phase Order, Composition Root and Cross-System Calls (§4) — primary; ADR-0010 (`ContentSet` passed by reference into `SimComposition.Build`) — secondary
**ADR Decision Summary**: One composition root, `SimComposition.Build(in SimBuildArgs args) → SimHost { SimWorld World; ClientLayer Client; }` with `SimBuildArgs = { SimProfile, ContentSet, ulong runSeed, SimInitialCounters? }`. Construction order is explicit, ordered code in `SimComposition` that must be a topological order of `Composition/SystemGraph.cs` (dependencies as data; a test checks order and acyclicity); composition order fixes construction, same-phase binder, tick-end and dump order. `SimProfile` selects built systems (`M1aGreybox`, `Full`, per-test). Systems take only constructor parameters from the closed injectable list; a reflection test checks every `[SimSystem(SystemId)]` type. No DI container, no service locator, no statics, no `SimWorld` reference in a system.
**ADR Version**: 2026-10-16 (ADR-0003, ADR-0010)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: The Godot host calls `SimComposition.Build` at boot (ADR-0019 order) and catches `SimBuildException` — godot-host-shell epic.

**Control Manifest Rules (Foundation)**:
- Required: `SimComposition.Build(in SimBuildArgs args) → SimHost { SimWorld World; ClientLayer Client; }`; explicit ordered code in a topological order of `Composition/SystemGraph.cs` (a test checks order and acyclicity); `SimProfile` (`M1aGreybox` = Grid, Players, Throwing, Mail, Manual Delivery, Run stub; `Full`; per-test); the closed injectable list (ADR-0003 §4 table); `IRunControl` only to Run Structure; `IStatWriter` only to Perks and Stamps; reflection test `tests/unit/architecture/CompositionRulesTests.cs`; `Build` and `Freeze` on the thread that will call `Tick()`.
- Forbidden: DI containers, service locators, reflection-driven discovery for construction; static mutable state; a `SimWorld` reference inside a system; `SimWorld` and `ClientLayer` referencing each other.
- Guardrail: none (boot-time only).

---

## Acceptance Criteria

*From GDD SC-R4-1 (row 5) and ADR-0003 §4, scoped to this story:*

- [x] **AC-1 SC-R4-1 row 5 + graph codes.** A `SystemGraph` containing a dependency cycle makes `SimComposition.Build` throw `SimBuildException` with `Code == DependencyCycle`; a system constructor asking for an `I…Api` not declared as its dependency → `UndeclaredDependency`; the same `SystemId` composed twice → `DuplicateSystem`. A test asserts `SimComposition`'s construction order for every profile is a valid topological order of `SystemGraph` and that the shipped `SystemGraph` is acyclic.
- [x] **AC-2 Build.** `SimComposition.Build(in args)` for a per-test profile returns a `SimHost` whose `World` is `Ready` (owner thread captured on the calling thread) with `CurrentTick == 0`, whose `Client` is a built `ClientLayer`, and in which `World` and `Client` hold no reference to each other; the `runSeed` reaches `RngRegistry`; `SimInitialCounters` (when given) seed the id counters and `opSeq`; the M1aGreybox profile builds today with the Sim Core services only (its gameplay systems are added by their epics) and ticks.
- [x] **AC-3 Reflection test.** `tests/unit/architecture/CompositionRulesTests.cs` passes and asserts: every type marked `[SimSystem(SystemId)]` has exactly one public constructor; every parameter type is on ADR-0003 §4's injectable list for that system (`EventLog` facade, `EntityIds`, `MailIds` [Mail only], `ISlotView`, its own `IRngStreams` view, `ITickReader`, `IRunControl` [Run Structure only], declared `I<System>Api`s, `ContentSet` or a sub-table, `IStatSheet`, `IStatWriter` [Perks/Stamps only], `SimCheckRing`, `SimCounters`, its own settings record); a deliberately bad test-only system (e.g. taking `SimWorld`, or `IRunControl` outside Run Structure) is reported by the test's rule engine.

---

## Implementation Notes

*Derived from ADR-0003 §4 and ADR-0010 §4:*

- Files in `src/PerformativeMail.Sim/Composition/` (namespace `PerformativeMail.Sim.Composition`): `SimComposition.cs`, `SimBuildArgs.cs` (readonly struct), `SimHost.cs`, `SimProfile.cs` (`M1aGreybox`, `Full`, plus a way for tests to supply a profile — e.g. a `SimProfile` value carrying a `SystemId` set), `SystemGraph.cs` (dependencies as data: an immutable table `SystemId → SystemId[]` exposed without `static readonly T[]` — e.g. a `switch` returning `ReadOnlySpan<SystemId>` literals), `SimSystemAttribute.cs`, `SimInitialCounters.cs`. `SystemId` lives in `Core/` (Story 009).
- **`ContentSet` dependency.** `SimBuildArgs` carries `ContentSet` (ADR-0003 §4; ADR-0010 §4: "passed by reference into `SimComposition.Build`"; `SimSettings` and the balance table are its members). The content-data epic builds `ContentSet` and `SimSettings`. If its story has not landed, create the minimal `src/PerformativeMail.Sim/Content/ContentSet.cs` shell holding `SimSettings` (whose Sim Core member is Story 011's `SimCoreSettings`) and a 32-byte content `Hash` (zero for the shell); content-data extends the same types. Record which path was taken.
- `Build` order: create `SimCounters`, `SimCheckRing`, the world's `SimWorldBuilder`, `EntityIds`, `MailIds`, `SlotTable`, `RngRegistry`, `EventLog` facade, `ITickReader`; then construct each profile system in `SimComposition`'s explicit order, passing only listed parameters; each system binds its phases/ops/events/readers/contributors; build the `ClientLayer` via `ClientLayerBuilder`; finally `Freeze()` on the calling thread.
- Same-phase binder order, tick-end order and dump-section order all follow this construction order (ADR-0003 §4 "Composition order = construction order").
- The reflection test uses `typeof(SimAssembly).Assembly.GetTypes()` in the test project; the rule table (parameter type → allowed `SystemId`s) lives in the test. `UndeclaredDependency` at runtime is checked by `Build` against `SystemGraph` (the test checks the same statically).
- No DI container; no `Activator.CreateInstance`; construction is plain `new`.

---

## Out of Scope

- Story 017: builder-level codes (handlers, readers, freeze).
- Story 028: the remaining SC-ARCH-1 rules (static mutable fields, clock in `Hosting/`, `ClientLayer` naming, owner-only RNG check across composed systems).
- Gameplay systems in `M1aGreybox` (Grid, Players, Throwing, Mail, Manual Delivery, Run stub): their epics add them to `SimComposition` and `SystemGraph`.
- content-data: the real `ContentSet`/`SimSettings`/`BuiltInContent`.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/architecture/CompositionRulesTests.cs`, `tests/unit/sim/Composition/SimCompositionTests.cs` (`[Trait("Id", "SC-R4-1")]`) — must exist and pass

**Status**: [x] Created and passing — SimCompositionTests.cs (SC-R4-1; incl. TD-045(1) dump proof, TD-052(1), TD-043(2)/(3), content-data 008 AC4), CompositionRulesTests.cs (19 bad fixtures, one exact violation each)

---

## Dependencies

- Depends on: Story 009 (`RngRegistry`, `SystemId`), Story 017, Story 018, Story 020, Story 023; content-data `ContentSet` story if landed (otherwise the shell above)
- Unlocks: Story 026, Story 027, Story 028, network-adapter-loopback (host composition), every gameplay epic (adds its system to the profile)

## Carried-over obligations

- **From content-data story 008 (AC4, CDD-LOAD-4 M-1a form), deferred because `SimComposition`/`SimBuildArgs` did not exist:** two `SimComposition.Build` calls with the same `ContentSet` (from `BuiltInContent.CreateM1a()`) give two `SimWorld`s that hold the same instance (`ReferenceEquals`), and the content factory/loader is invoked once per process (counter `ContentBuildCount` stays 1; the counter belongs to the composition owner — never a static in the Sim). Add these as tests here. (Logged 2026-10-16; TD-tracked in the content-data 008 carry-forward row.)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing + carried-over content-data 008 AC4 (ReferenceEquals across two Builds via `SimHost.Content` and the systems' context; `ContentBuildCount` = 1 on the test-side composition owner)
**Deviations**: None blocking. Advisory (logged TD-061): `SimBuildArgs.HostClock` (optional, additive) and public `SimHost.Content` beyond ADR-0003 §4 shapes; `SimWorld` does not hold the `ContentSet`; `SimInitialCounters` stays in Core/ (+ LastMailId, LastEntityCounters); new `Core/EventLogFacade` (systems are constructed before Freeze creates the EventLog); `UndeclaredDependency` reused for the MailIds/IRunControl guards; graph edge = "may depend on". Appended SystemId 9–18 and SimBuildErrorCode DependencyCycle 44 / UndeclaredDependency 45 / DuplicateSystem 46. Real `ContentSet` (content-data 008) used, no shell. Folded: TD-045(1) (BindCoreState always non-null; dump section 1 proven to come from the composed allocators/registry — fails under a `BindCoreState(ids, null, null, slots)` regression), TD-052(1), TD-043(2)/(3), TD-054(6).
**Test Evidence**: Logic — tests/unit/sim/Composition/SimCompositionTests.cs, tests/unit/architecture/CompositionRulesTests.cs; 2 303 tests/leg; story tests green on all four legs; 9 failures/leg all in Content tests (BuiltInContentTests, GeneralRulesTests, RuleRegistryTests) from the content-data runner's in-flight edit (an earlier run had 2 295/leg, 0 failures)
**Code Review**: Complete — LP-CODE-REVIEW APPROVE; QL-TEST-COVERAGE GAPS → gaps 1, 2, 5 fixed in one revision round
