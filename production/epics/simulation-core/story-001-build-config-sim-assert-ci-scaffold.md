# Story 001: Build configurations, SIM_ASSERT and the net8.0 CI leg (scaffold A)

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 250 lines incl. tests, CI and MSBuild)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R31 asserts/records, R33 Godot-free Sim; Acceptance Criteria legend "Configurations")
**Requirement**: `TR-sim-040` (asserts compile out of Release), `TR-sim-042` (Sim knows no Godot types; assembly half), `TR-sim-034` (net8.0 leg prerequisite for T2 / golden vectors)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (secondary — the checked/unchecked build mandate)
**ADR Decision Summary**: The Sim is a Godot-free `net8.0` library whose assert switch (`SimAsserts` → `SIM_ASSERT;INVENTORY_ASSERT` + `CheckForOverflowUnderflow`) is derived in `Directory.Build.targets` from `$(Optimize)` after the SDK has set it; `Directory.Build.props` maps Godot's `ExportRelease`/`ExportDebug` onto `Optimize`. One xUnit v3 test project runs on `net10.0` and accepts `-p:PmTestTfm=net8.0`, and CI adds a `net8.0` leg from the first Sim Core story.
**ADR Version**: 2026-10-16 (ADR-0001), 2026-10-16 (ADR-0006)

**Engine**: Godot 4.7.1 .NET (this story touches no Godot code) | **Risk**: LOW
**Engine Notes**: ADR-0001's HIGH risk is the Godot half (game csproj, export runtime), which is out of scope. Spike fact F6: Export* configurations leave a referenced plain-SDK project unoptimised unless mapped — that is why the props mapping exists even though no game csproj exists yet.

**Control Manifest Rules (Foundation)**:
- Required: `Directory.Build.props` maps `ExportRelease` → `Optimize=true`, `ExportDebug` → `Optimize=false` + `DebugSymbols=true`; `Directory.Build.targets` derives `SimAsserts` from `$(Optimize)` and, for Sim and Client.Core only, defines `SIM_ASSERT;INVENTORY_ASSERT` and `CheckForOverflowUnderflow=true` — use the exact XML in ADR-0001 §4.
- Required: `SimAssert` (`Primitives/`) is a stateless static class of `[Conditional("SIM_ASSERT")]` `That(bool, ushort code)` / `Fail(ushort code)`; `Sim.AssertsEnabled` is a `public const bool`; a Release test asserts it is `false`.
- Required: test csproj accepts `-p:PmTestTfm=net8.0`; CI `net8.0` leg (setup-dotnet 8.0.x + 10.0.x) from this story; CI guards `AssemblyRulesTests.cs` + `using Godot` grep.
- Forbidden: branching Sim code on `DEBUG`/`RELEASE`/`EXPORTDEBUG`/`EXPORTRELEASE`/`Configuration`; `Debug.Assert`; MSBuild logic keyed on `Configuration == 'Release'`.
- Guardrail: none (build-only story).

---

## Acceptance Criteria

*Derived from ADR-0001 §4, §5, §7, §8 (mandated scaffold gaps, control manifest §Toolchain) and the GDD's "Configurations" legend, scoped to this story:*

- [x] **AC-1 Build-configuration table.** For `src/PerformativeMail.Sim`, `dotnet msbuild -getProperty:Optimize -getProperty:DefineConstants -getProperty:CheckForOverflowUnderflow` reports exactly ADR-0001 §4's table: `Debug` → Optimize false, `SIM_ASSERT;INVENTORY_ASSERT` defined, checked; `Release` → Optimize true, neither symbol, unchecked; `Release -p:SimAsserts=true` → Optimize true, both symbols, checked; `ExportDebug` → Optimize false, symbols defined, checked, DebugSymbols true; `ExportRelease` → Optimize true, no symbols, unchecked. A script (`tools/ci/check-build-config.sh`) asserts all five rows and runs in CI.
- [x] **AC-2 Asserts.** `SimAssert.That(false, code)` and `SimAssert.Fail(code)` throw `SimAssertException` (carrying `code`) when the Sim is built with `SIM_ASSERT`; in `Release` the call site is compiled out (a call whose argument has an observable side effect leaves it unobserved). `Sim.AssertsEnabled` is `true` in the Debug test run and `false` in the Release test run (the ADR-0001 §8 Release guard).
- [x] **AC-3 Test TFM and CI legs.** `dotnet test` with `-p:PmTestTfm=net8.0` builds and runs the suite on the .NET 8 runtime; `.github/workflows/tests.yml` runs net10.0 and net8.0 legs, each in Debug and Release; any leg exiting 8 (zero tests ran) fails the job.
- [x] **AC-4 Assembly guards.** `tests/unit/architecture/AssemblyRulesTests.cs` passes and asserts, from compiled metadata: the Sim references no assembly whose name starts with `Godot`; the Sim does not reference `PerformativeMail.Client.Core`; the Sim references framework assemblies only (no runtime package). A CI step running `grep -rn "using Godot" src/PerformativeMail.Sim src/PerformativeMail.Client.Core` fails the build on any match (missing directories tolerated).

---

## Implementation Notes

*Derived from ADR-0001 Implementation Guidelines, §4, §5, §7, §8:*

1. **`Directory.Build.props`** — append the two Export* `PropertyGroup`s verbatim from ADR-0001 §4 (conditions on `'$(Configuration)' == 'ExportRelease'` / `'ExportDebug'`, each with `Condition="'$(Optimize)' == ''"` guards). This is the only place a configuration *name* is keyed (manifest I-8 — it maps names to `Optimize`; asserts never key on names).
2. **`Directory.Build.targets`** (new, repo root) — the ADR-0001 §4 XML verbatim: `SimAsserts` defaults to `true` when `$(Optimize) != 'true'`; when `SimAsserts == 'true'` and the project is `PerformativeMail.Sim` or `PerformativeMail.Client.Core`, append `SIM_ASSERT;INVENTORY_ASSERT` to `DefineConstants` and set `CheckForOverflowUnderflow=true`. It must be in `.targets`, not `.props`: in `.props` the SDK has not yet set `Optimize` for `Release` (spike C8).
3. **`SimAssert`** in `src/PerformativeMail.Sim/Primitives/SimAssert.cs`: `public static class SimAssert` with `[Conditional("SIM_ASSERT")] public static void That(bool condition, ushort code)` and `Fail(ushort code)`; both throw `SimAssertException(code)` (a sealed exception in `Primitives/`). Stateless — the static-state rule bans static *state*, not pure helpers. Arguments must be pure because they vanish in Release. Do **not** add per-system aliases here; `INVENTORY_ASSERT` overloads live in the inventory folder (ADR-0009).
4. **`Sim.AssertsEnabled`** in `src/PerformativeMail.Sim/Sim.cs` (`namespace PerformativeMail.Sim; public static class Sim`): `#if SIM_ASSERT` → `true`, else `false`. This is the one sanctioned `#if`, keyed on `SIM_ASSERT`, never `DEBUG`.
5. **Release guard test**: the test project does not get `SIM_ASSERT` (targets scope it to Sim/Client.Core). Give the test project its own expectation, e.g. a `PM_EXPECT_SIM_ASSERTS` constant defined in the test csproj when `$(Optimize) != 'true'` (or `SimAsserts == 'true'`), and assert `Sim.AssertsEnabled == expected`. Keep it keyed on `$(Optimize)`/`$(SimAsserts)`, never on `Configuration`.
6. **Test csproj**: `<TargetFramework>$(PmTestTfm)</TargetFramework>` with a default of `net10.0` when `PmTestTfm` is empty (ADR-0001 §7). Do not multi-target by default — the net8.0 leg is an explicit `-p:` run.
7. **CI (`.github/workflows/tests.yml`)**: a matrix over `tfm ∈ {net10.0, net8.0}` × `configuration ∈ {Debug, Release}`; `actions/setup-dotnet` installs `8.0.x` and `10.0.x` (the SDK stays 10 via `global.json`; the 8 runtime is needed for the net8.0 leg). Pass `-p:PmTestTfm=${{ matrix.tfm }}` to restore/build/test. Add the build-config script step and the `using Godot` grep step (fail on match; `|| true` only around a missing-path case, not around a match). Keep `dotnet test --solution PerformativeMail.sln` (no root csproj exists yet, but naming the solution is future-proof).
8. **`AssemblyRulesTests`** (`tests/unit/architecture/`, namespace `PerformativeMail.Sim.Tests.Unit.Architecture`): move the Godot-reference check from `tests/unit/sim/SimAssemblyTests.cs` here (keep the smoke test where it is or delete it if redundant). "No runtime package" is checked as: every `GetReferencedAssemblies()` name is `System.*`, `netstandard`, `mscorlib` or `Microsoft.Win32.*`/`System.Private.*` (framework); anything else fails with the name. Client.Core does not exist yet — the test asserts the Sim side now; the network-adapter-loopback epic extends it to Client.Core.
9. Locally, run every command as `mise exec dotnet@10.0.402 -- dotnet …`. The .NET 8.0.32 runtime is installed, so the net8.0 leg runs locally (control manifest E-1). A machine without it reports the leg **NOT RUN**, never passed.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 004: `BannedApiAnalyzers`, `BannedSymbols.txt`, RS0030 severity, the custom float/double/decimal/Half analyzer and the SC-ARCH-2 fixtures (scaffold B).
- Story 007: `SimCounters` and `SimCheckRing` (the always-on half of TR-sim-040).
- Story 028: the remaining SC-ARCH-1 rows (static mutable fields, no clock in `Hosting/`, `ClientLayer` names no `SimWorld`).
- `PerformativeMail.Game.csproj`, `project.yaml commands.test` edits, `.gdignore` moves — the godot-host-shell epic.
- `windows-latest` CI leg — M0.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/architecture/AssemblyRulesTests.cs` and `tests/unit/sim/Primitives/SimAssertTests.cs` — must exist and pass on net10.0 and net8.0, Debug and Release
- `tools/ci/check-build-config.sh` green in CI

**Status**: [x] Created — 3 test files + check-build-config.sh; 23/23 pass Debug+Release × net10.0+net8.0

---

## Dependencies

- Depends on: None
- Unlocks: every later Simulation Core story (assert symbol, net8.0 leg, guards); Story 004

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 4/4 passing (none deferred)
**Deviations**:
- ADVISORY (accepted): `src/PerformativeMail.Sim/Primitives/SimAssertProbe.cs` — internal test seam (via `InternalsVisibleTo`) so `SimAssert` call sites live inside the Sim (manifest: every call site in Sim/Client.Core; story note 5: test project gets no `SIM_ASSERT`). This lets the tests observe the Sim's own Release compile-out. Do not remove it or copy a workaround — future assert tests go through it.
- ADVISORY: `tests/PerformativeMail.Sim.Tests/TestBuildExpectation.cs` + a test-project-only `PM_EXPECT_SIM_ASSERTS` group in `Directory.Build.targets` (derived from `$(Optimize)`/`$(SimAsserts)`; must live in .targets because the SDK sets Optimize after the csproj body).
- ADVISORY (tech debt TD-001, TD-002): the net8.0 leg is not runtime-pinned by a test; the `System.*` prefix in `IsFrameworkAssembly` would admit a `System.*` NuGet package.
- Notes: the CI workflow has not yet run on GitHub (first run is the real confirmation); the failure paths of check-build-config.sh and the grep guard were not watched failing; checked arithmetic is verified only as an MSBuild property (runtime overflow test belongs to the ADR-0006 numerics stories); `SimAssertException` message uses culture-default interpolation (harmless for ushort; may need InvariantCulture under story 004's analyzers).
**Test Evidence**: Logic: `tests/unit/architecture/AssemblyRulesTests.cs`, `tests/unit/sim/Primitives/SimAssertTests.cs` (+ `tests/unit/sim/SimAssemblyTests.cs` smoke), `tools/ci/check-build-config.sh` 5/5
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (advisory findings only); QL-TEST-COVERAGE ADEQUATE; godot-csharp-specialist review CHANGES MADE (probe seam)
