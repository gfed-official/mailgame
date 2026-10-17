# Story 028: Architecture rules suite — SC-ARCH-1

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 350 lines, test-side)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R4 no statics/no locator, R2 `ClientLayer` never reads `SimWorld`, R24 owner-only streams, R33 Godot-free Sim; SC-ARCH-1)
**Requirement**: `TR-sim-013` (reflection test on dependencies), `TR-sim-032` (no statics), `TR-sim-042` (Sim knows no Godot types / wall clock), `TR-sim-029` (owner-only RNG handles)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0003: Phase Order, Composition Root and Cross-System Calls (§4 static-state test, §6) — primary; ADR-0001 (§8 CI guards), ADR-0002 (§1 no clock in `Hosting/`), ADR-0005 (§3 owner-only views) — secondary
**ADR Decision Summary**: CI architecture tests fail the build when: the Sim references a `Godot*` assembly or contains `using Godot`; any Sim static field is not `const` or `static readonly` of an immutable type (primitive, `string`, or `[ImmutableTable]`; `static readonly T[]` counts as mutable; `[CompilerGenerated]` excluded); any `Hosting/` type uses a clock API; `ClientLayer` names `SimWorld` or a gameplay type; a system constructor parameter is outside the injectable list; or a system receives an RNG view containing another owner's names (Throwing receives none).
**ADR Version**: 2026-10-16 (ADR-0003, ADR-0001, ADR-0002, ADR-0005)

**Engine**: Godot 4.7.1 .NET (test-side reflection only) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: static-state architecture test (non-`const`, non-immutable `static readonly` fails; `ReadOnlySpan<T>` literal properties or `[ImmutableTable]` wrappers for tables; `[CompilerGenerated]` excluded); CI guards per ADR-0001 §8; owner-filtered `IRngStreams` (Throwing gets none).
- Forbidden: static mutable state (including `static readonly T[]`); `SimWorld` references in systems or `ClientLayer`; clock APIs in `Hosting/`; DI containers/service locators.
- Guardrail: none (CI-time).

---

## Acceptance Criteria

*From GDD SC-ARCH-1, scoped to this story (rows already delivered by Stories 001 and 025 are re-run here as one suite):*

- [x] **AC-1 SC-ARCH-1.** The architecture test suite (`tests/unit/architecture/`) passes on the built Sim and asserts every row: (a) no reference to any `Godot*` assembly and no `using Godot` under `src/PerformativeMail.Sim` (Story 001's tests + grep, included in the suite run); (b) no type under `PerformativeMail.Sim.ClientLayer` has a field, property, constructor parameter or method signature naming `SimWorld` or a type from a gameplay system namespace; (c) no static field in the Sim assembly is mutable — non-`const`, non-`readonly`, or `static readonly` of a non-immutable type (arrays included), excluding `[CompilerGenerated]` types; (d) no type in `PerformativeMail.Sim.Hosting` references `Stopwatch`, `DateTime`, `DateTimeOffset`, `Environment.TickCount*`, `TimeProvider` or `IMonotonicClock.Timestamp` outside `FrameRunner`; (e) every `[SimSystem]` constructor parameter is on the allowed list (Story 025's `CompositionRulesTests`); (f) in a composed world, each system's `IRngStreams` view contains only names whose registered owner is that system, and Throwing's view is empty.
- [x] **AC-2 Seen failing.** Each rule (b)–(d) and (f) is demonstrated to fail on a deliberately bad test-assembly fixture type (e.g. a `static int[]`, a step type holding `SimWorld`, a hosting-like type calling `Stopwatch`, a view built for the wrong owner) so the gate is known to bite.

---

## Implementation Notes

*Derived from ADR-0003 §4, §6, ADR-0001 §8, ADR-0002 §1, ADR-0005 §3:*

- Files: `tests/unit/architecture/StaticStateRulesTests.cs`, `ClientLayerRulesTests.cs`, `HostingClockRulesTests.cs`, `RngOwnershipRulesTests.cs` (+ Story 001's `AssemblyRulesTests.cs`, Story 025's `CompositionRulesTests.cs`), one class per rule (tests/README).
- `[ImmutableTable]` attribute: define `src/PerformativeMail.Sim/Primitives/ImmutableTableAttribute.cs` (marks a wrapper type whose instances are immutable tables). The static-state test treats `static readonly` fields of primitive types, `string`, enums, readonly structs whose fields are themselves immutable (e.g. `StreamName`, `EntityId`), and `[ImmutableTable]` types as immutable.
- Rule (b) and (d) need member-body inspection: signatures via reflection; bodies via `System.Reflection.Metadata` IL scanning of member references (`MethodBody.GetILBytes` + token resolution) or a source-text scan of `src/PerformativeMail.Sim/Hosting/*.cs` — either is acceptable; prefer metadata (robust to aliases). Story 004's analyzer already bans clock types Sim-wide; this test adds the `Hosting/`-specific ban on reading `IMonotonicClock.Timestamp` outside `FrameRunner` (ADR-0002 §1: "TickDriver never reads a clock").
- Rule (f) uses `SimComposition.Build` with a test profile and inspects views via an internal accessor (owner `SystemId` + names) — until gameplay systems exist, fixture systems stand in; each gameplay epic adds its system to the `Full`/`M1aGreybox` profile and this test covers it automatically.
- Negative fixtures for AC-2 live in the test assembly; run the rule engine against the test assembly with an explicit "expected violations" list so the positive suite still scans only the Sim assembly.

---

## Out of Scope

- Story 004: SC-ARCH-2 analyzer fixtures (float/Random/Guid/GetHashCode/DateTime/Stopwatch) and the `SceneTree.Paused` grep.
- Client.Core rules (no Godot, references only the Sim): network-adapter-loopback extends `AssemblyRulesTests`.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/architecture/*RulesTests.cs` (`[Trait("Id", "SC-ARCH-1")]`) — must exist and pass in CI on every leg

**Status**: [x] Created and passing — tests/unit/architecture/{Assembly,Composition,StaticState,ClientLayer,HostingClock,RngOwnership,SystemIsolation,ImmutableTable}RulesTests.cs, all `[Trait("Id","SC-ARCH-1")]`; shared scan helper tests/PerformativeMail.Sim.Tests/Fixtures/ArchitectureScan.cs

---

## Dependencies

- Depends on: Story 001, Story 009, Story 023, Story 024, Story 025
- Unlocks: epic Definition of Done (SC-ARCH-1 in CI)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 2/2 passing (automated)
**Traceability**: (a) AssemblyRulesTests.Sim_References_NoGodotAssembly + SimSource_ContainsNoUsingGodot; (b) ClientLayerRulesTests (signature ban on SimWorld + gameplay types, IL body ban on SimWorld incl. lambdas; bad/good fixtures); (c) StaticStateRulesTests (10-violation bad fixture incl. static int[], mutable struct, ImmutableArray<int[]>, sealed-over-base-with-field; compiler-generated exclusion non-empty) + ImmutableTableRulesTests (every [ImmutableTable] type sealed/readonly, initonly fields, init-only setters; 6-violation bad fixture); (d) HostingClockRulesTests (Stopwatch/DateTime/DateTimeOffset/TimeProvider/TickCount64/Timestamp + generic-method-argument case; FrameRunner is the only Timestamp reader; Hosting selected by namespace prefix); (e) CompositionRulesTests (story 025, trait added); (f) RngOwnershipRulesTests (Full/M1aGreybox composed with a world; fixture world with one system per stream owner + Throwing; wrong-owner bad fixture); (g, owner brief / TD-061(3)) SystemIsolationRulesTests (no [SimSystem] or nested type names SimSystemContext/SimWorld in fields/properties/ctor params/method signatures; Bind(SimWorldBuilder) allowed). All COVERED; AC-2 seen failing for b, c, d, f, g.
**Production changes**: new Primitives/ImmutableTableAttribute.cs; Core/OpDescriptor.cs and Core/EventDescriptor.cs marked [ImmutableTable] (they back the statics PlayerJoinPayload.JoinDescriptor/LeaveDescriptor and CoreEventDescriptors.OpReply); Core/RngOwnerView.cs gains internal ExposedNames() sharing a private Exposes predicate with Create (behaviour unchanged).
**Deviations** (advisory, TD-087): rule (c) immutable set widened beyond ADR-0003 §4/manifest (enums, readonly structs of immutable fields per the notes, plus Nullable<T>/ImmutableArray<T> of immutable T, decimal, stateless sealed classes — the last admits NullOpRecorder.Instance/NullPlayerJoinListener.Instance; ImmutableArray needed by OpKindBlockTable.Blocks); rule (b) gameplay namespaces = every PerformativeMail.Sim.<Folder> except Primitives/Geometry/Core/Hosting/Composition/ClientLayer/Content (fails closed); body scan via GetILAsByteArray + Module.ResolveMember rather than System.Reflection.Metadata (either allowed); rule (d) FrameRunner exempt only for IMonotonicClock.Timestamp (clock types banned in FrameRunner too).
**Test Evidence**: Logic — files above. Implementer: four legs 4,035/0 (Debug 31 skipped, none SC-ARCH-1) before the fix round; after it *RulesTests 370/0 on net10 and net8 Release in-repo. At close: full net10 Release on a /tmp copy 4,165/4,166 — the single failure is foreign (tests/unit/mail/DestinationDrawTests.cs:92, mail runner in flight); in-repo build blocked at that moment by foreign tests/unit/mail/ManifestDrawCountTests.cs IDE1006 (removed only in the copy).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (LP1 blocking: ClientLayer body scan banned gameplay types against ADR-0003 §6 shared pure functions → narrowed to SimWorld, fixed in-story; LP2/6/7/12 fixed; rest advisory → TD-087); QL-TEST-COVERAGE GAPS (QL1/2/4/5 fixed in-story; QL3/6/7 → TD-087).
