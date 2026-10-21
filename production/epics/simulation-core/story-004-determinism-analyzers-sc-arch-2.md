# Story 004: Determinism analyzers and SC-ARCH-2 fixtures (scaffold B)

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 1 day; ≈ 350 lines: analyzer, symbol list, fixtures, CI script)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R27 no floats, R28 determinism checklist, R33; SC-ARCH-2)
**Requirement**: `TR-sim-031` (float/double/decimal banned), `TR-sim-032` (no Random/Guid/hash codes/time/threads/unstable sort)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (rule content, §3 Enforcement) — primary; ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework (§6 mechanism) — secondary
**ADR Decision Summary**: Two build-time tools enforce the determinism checklist. A small custom Roslyn analyzer flags any use of a type whose `SpecialType` is `System_Single`/`System_Double`/`System_Decimal`, plus `System.Half`. `Microsoft.CodeAnalysis.BannedApiAnalyzers` (RS0030, made an error in `.editorconfig`) bans the ADR-0006 §3 symbol list via `BannedSymbols.txt` (doc-comment IDs, no wildcards). Every banned item has a fixture that must be seen failing before the gate counts.
**ADR Version**: 2026-10-16 (ADR-0006), 2026-10-16 (ADR-0001)

**Engine**: Godot 4.7.1 .NET (Godot-free build tooling) | **Risk**: LOW
**Engine Notes**: None engine-side. The analyzer is `netstandard2.0` (Roslyn requirement); pin a `Microsoft.CodeAnalysis.CSharp` version no newer than the compiler shipped with SDK 10.0.402, or the analyzer will not load.

**Control Manifest Rules (Foundation)**:
- Required: analyzers in Sim/Client.Core use `PrivateAssets="all"` and pinned versions; `BannedSymbols.txt` in the project folder; `dotnet_diagnostic.RS0030.severity = error` in `.editorconfig`; custom analyzer lives in `tools/PerformativeMail.Analyzers/` (netstandard2.0); every banned item has a fixture under SC-ARCH-2 seen failing before the gate counts.
- Forbidden: `float`, `double`, `decimal`, `Half` in authoritative code; `System.Random`/`Random.Shared`; `Guid`; `GetHashCode` calls/`HashCode` in state or ordering; time sources (`DateTime*`, `Stopwatch`, `Environment.TickCount*`); `Thread`/`Task`/`Parallel`/`Timer`; `Array.Sort`/`List.Sort`/`MemoryExtensions.Sort`/LINQ `OrderBy`/`ThenBy`(Descending); `SceneTree.Paused` to pause the Sim.
- Guardrail: none (build-time only).

---

## Acceptance Criteria

*From GDD SC-ARCH-2 and ADR-0006 §3 Enforcement, scoped to this story:*

- [x] **AC-1 SC-ARCH-2.** For each fixture — `float`, `double`, `decimal`, `System.Half`, `var x = 1.5;`, a `double` field, `System.Random`, `Guid.NewGuid()`, an explicit `GetHashCode()` call (on `object`, on `string`, and on a type that overrides it), `HashCode`, `DateTime.Now`, `Stopwatch`, `Environment.TickCount64`, `Thread`, `Task.Run`, `Array.Sort`, `List<T>.Sort`, `MemoryExtensions.Sort`, `Enumerable.OrderBy` — compiling it into `PerformativeMail.Sim` fails the build with the expected diagnostic id (the custom analyzer's id for the numeric-type rows, `RS0030` for the rest). The CI script records each fixture's result and the job fails if any fixture *compiles*.
- [x] **AC-2 Clean tree and documented gaps.** The Sim (with Stories 002–003 merged) builds with zero analyzer diagnostics; the `readonly record struct` fixture's result is recorded (expected: compiles — synthesized members have no syntax), and any fixture whose ban the tooling cannot catch is listed in `tests/fixtures/analyzers/README.md` as review-enforced, matching ADR-0006 §3 "What is caught where".
- [x] **AC-3 Paused grep.** A CI step greps `src/PerformativeMail.Sim` and `src/Game/Hosting` (when present) for `SceneTree.Paused` / `GetTree().Paused` and fails on any match.

---

## Implementation Notes

*Derived from ADR-0006 §3 and ADR-0001 §6:*

1. **Custom analyzer** — `tools/PerformativeMail.Analyzers/PerformativeMail.Analyzers.csproj` (`netstandard2.0`, `EnforceExtendedAnalyzerRules=true`, `IsRoslynComponent=true`, assembly name `PerformativeMail.Sim.Analyzers` per ADR-0006). One `DiagnosticAnalyzer` (id e.g. `PMSIM001`, default severity **Error**) that reports any operation, declaration, local, field, parameter, return type, cast or literal whose type (or element type) has `SpecialType` `System_Single`, `System_Double` or `System_Decimal`, or is `System.Half`. Register `OperationAction`s (literals, conversions, binary ops, invocations returning such types) and `SymbolAction`s (fields, properties, methods, parameters, locals via operations). `var x = a * 1.5` and the `double` keyword must both be caught.
   - Reference it from the Sim csproj as `<ProjectReference Include="../../tools/PerformativeMail.Analyzers/PerformativeMail.Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" PrivateAssets="all" />`. Add the tools project to `PerformativeMail.sln` (tools are allowed there; ADR-0001 §3).
   - Rule 3 (Dictionary/HashSet iteration) may be added to this analyzer later (GDD OQ10); until then it is a review rule plus SC-DET-1. Not in this story.
2. **BannedApiAnalyzers** — `<PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="<pinned>" PrivateAssets="all" />` in the Sim csproj; `src/PerformativeMail.Sim/BannedSymbols.txt` as `<AdditionalFiles>`, containing exactly the ADR-0006 §3 list (doc-comment IDs, name-only members cover all overloads, no wildcards): `T:System.Random`; `M:System.Guid.NewGuid`; `M:System.Object.GetHashCode`, `M:System.String.GetHashCode`, `T:System.HashCode`; `T:System.DateTime`, `T:System.DateTimeOffset`, `T:System.Diagnostics.Stopwatch`, `P:System.Environment.TickCount`, `P:System.Environment.TickCount64`; `T:System.Threading.Thread`, `T:System.Threading.Tasks.Task`, `T:System.Threading.Tasks.Parallel`, `T:System.Threading.Timer`; `M:System.Array.Sort`, ``M:System.Collections.Generic.List`1.Sort``, `M:System.MemoryExtensions.Sort`; `M:System.Linq.Enumerable.OrderBy`, `M:System.Linq.Enumerable.OrderByDescending`, `M:System.Linq.Enumerable.ThenBy`, `M:System.Linq.Enumerable.ThenByDescending`.
   - `.editorconfig` `[*.cs]`: `dotnet_diagnostic.RS0030.severity = error` (explicit; do not rely on `TreatWarningsAsErrors` alone).
3. **Fixtures** — the GDD says "fixture files under `src/Sim`"; they must not be compiled into the normal build, so keep them in `tests/fixtures/analyzers/<Name>.cs` and inject one at a time with an MSBuild property, e.g. the Sim csproj has `<Compile Include="$(PmArchFixture)" Condition="'$(PmArchFixture)' != ''" />`. `tools/ci/check-analyzer-fixtures.sh` loops the fixtures, runs `dotnet build src/PerformativeMail.Sim -p:PmArchFixture=<file> -warnaserror`, and asserts non-zero exit with the expected id (declared in a header comment `// expect: PMSIM001`). A fixture that compiles fails the script. Run it in CI after the normal build.
4. **Id-struct overrides** — whether RS0030 flags a `GetHashCode` *override declaration* is NOT ASSESSED (ADR-0006 §3); the fixtures decide. If the Story 002 overrides trip it, add a narrowly scoped `#pragma warning disable RS0030` around those override declarations only, with a comment citing ADR-0004 Implementation Guidelines (overrides are mandated; calls are banned).
5. **Determinism checklist rules not machine-checked** (2, 3, 4, 8, 9, 10) stay review rules + SC-DET-1 (Story 027) + the static-state test (Story 028). Note them in the fixtures README.
6. Client.Core gets the same analyzer references when the network-adapter-loopback epic creates it.

---

## Out of Scope

- Story 001: build configs, `SIM_ASSERT`, net8.0 leg, `AssemblyRulesTests`, `using Godot` grep.
- Story 028: static-mutable-field reflection test, no clock in `Hosting/`, `ClientLayer` names no `SimWorld` (rest of SC-ARCH-1).
- Rule-3 analyzer check (GDD OQ10).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tools/ci/check-analyzer-fixtures.sh` + `tests/fixtures/analyzers/*.cs` — every fixture seen failing in a CI log (link the run in the story-done notes)
- Clean build of the Sim under the analyzers on net8.0 and net10.0 legs

**Status**: [x] Created — local run log retained (no CI run available: no git push in this environment)

---

## Dependencies

- Depends on: Story 001 (CI matrix to hang the script on); Stories 002–003 merged so AC-2 checks real code
- Unlocks: Story 005 onward write code under an enforced float/clock/sort ban

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 80 fixtures: 78 SEEN with expected id, 2 RECORDED compiles; AC-2 clean Sim build, record-struct recorded, README gap table; AC-3 `tools/ci/check-no-scenetree-paused.sh` + `--self-check`)
**Deviations**: (1) ADVISORY, accepted as an improvement: `BannedSymbols.txt` lists every net8.0 overload explicitly — ADR-0006 §3 bare member IDs do not match generic/overloaded methods (stronger ban; ADR erratum TD-007). (2) Review revision at close: +11 bans (`Task`1`, `ValueTask`, `ValueTask`1`, `ThreadPool`, `TimeProvider`, `RuntimeHelpers.GetHashCode(Object)`, `Enumerable.Order`/`OrderDescending` ×2 each) with 10 fixtures; Sim TFM pinned to net8.0 by the fixture script; `AnalyzeOperation` split to meet the 40-line / CC-10 standard. (3) Out-of-scope-but-valid: new `tools/ci/check-no-scenetree-paused.sh`.
**Known gaps (review-enforced, TD-006)**: banned types used only in signatures/fields, and `async ValueTask` awaiting a parameter, compile clean (probed at close); analyzer has no direct unit tests; `// covers:` headers self-declared; paused grep misses `tree.Paused` via a variable.
**Test Evidence**: Logic — `tools/ci/check-analyzer-fixtures.sh` + `tests/fixtures/analyzers/*.cs`; run log `production/qa/evidence/simulation-core-story-004/check-analyzer-fixtures.log` (80 fixtures, 0 failures). Tests 201/201 on net10.0/net8.0 × Debug/Release; Release build 0 warnings. CI-run link pending first push.
**Run result**: N/A — build-time tooling only; nothing renders, the observable output is the fixture-script log above.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (revised: findings 2, 3, 4, 1-guard; 5, 6 → TD-006); QL-TEST-COVERAGE GAPS (advisory; generator wording fixed, paused self-check added).
