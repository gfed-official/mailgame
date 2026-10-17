# Test Infrastructure

**Engine**: Godot 4.7.1 (C#) — simulation tested without Godot
**Test Framework**: xUnit v3 (Microsoft.Testing.Platform runner) for the plain-C#
`PerformativeMail.Sim` assembly; gdUnit4 for Godot-side tests (not set up yet —
needs the Godot .NET build)
**CI**: `.github/workflows/tests.yml`
**Setup date**: 2026-10-16

## Directory Layout

```
src/PerformativeMail.Sim/          # plain-C# gameplay simulation (net8.0, no Godot)
tests/
  PerformativeMail.Sim.Tests/      # xUnit test project (net10.0) + shared fixtures/helpers
  unit/           # [UNIT] / [FUZZ] / [PERF] tests, one folder per system (tests/unit/inventory/)
  integration/    # [INTEGRATION] tests across two systems' public interfaces
  smoke/          # Critical path test list for /smoke-check gate
```

The test project compiles every `.cs` under `tests/unit/` and `tests/integration/`
as well as its own folder, so the file paths the GDD acceptance criteria name
(e.g. `tests/unit/inventory/<Rule>Tests.cs`) work as written. All tests build
into one assembly and run in one `dotnet test`.

```
production/qa/
  evidence/       # Screenshot logs and manual test sign-off records
```

> **Manual evidence lives under `production/qa/evidence/`, not `tests/`** — that
> is where every consumer reads it. [MANUAL] and, until gdUnit4 exists, [UI]
> evidence goes there.

## Running Tests

The .NET SDK is installed user-level via mise and is not on `PATH`:

```bash
mise exec dotnet@10.0.402 -- dotnet build PerformativeMail.sln
mise exec dotnet@10.0.402 -- dotnet test --solution PerformativeMail.sln
```

(`commands.test` in `project.yaml` is the second line.) On a machine with the
SDK on `PATH`, plain `dotnet test --solution PerformativeMail.sln` works. The repo
root holds two solutions (`PerformativeMail.sln`, Godot-free, and
`PerformativeMail.Game.sln`, Godot) plus `PerformativeMail.Game.csproj`, so a bare
`dotnet build` / `dotnet test` is ambiguous (MSB1011): always name the solution
(ADR-0001 section 3). `global.json` opts `dotnet test` into
Microsoft.Testing.Platform: the .NET 10 SDK refuses the old VSTest path for
xUnit v3. Exit code **0** = all passed; **2** = at least one test failed;
**8** = zero tests ran (not a pass).

Filter by class or method: `dotnet test -- --filter-class "*GridPlacementTests"`
or `-- --filter-method "*H_R4_1a*"`.

## Build rules that affect tests

- `Directory.Build.props`: `Nullable`, `TreatWarningsAsErrors`, `Deterministic`,
  `InvariantGlobalization`, `EnforceCodeStyleInBuild` apply to the Sim and the tests.
- `.editorconfig` naming rules are build errors (IDE1006): types and public members
  PascalCase, private fields `_camelCase`, locals/parameters camelCase, constants
  PascalCase. Under `tests/`, method names may use `Scenario_Expected` underscores.
- `PerformativeMail.Sim` grants `InternalsVisibleTo` to the test assembly.
- A smoke test (`tests/unit/sim/SimAssemblyTests.cs`) asserts the Sim assembly
  references no `Godot*` assembly.
- `src/.gdignore` and `tests/.gdignore` keep the Godot editor from scanning these
  folders. When the Godot .NET build generates the root game `.csproj`, it must
  exclude them (`<Compile Remove="src/**;tests/**" />`) and use a
  `ProjectReference` to the Sim instead. Otherwise MSBuild compiles the Sim and the
  tests a second time.

## Test Naming

C# (xUnit), following `.claude/rules/test-standards.md` (Unity/NUnit C# form):
- class `[System]Tests` or `[Rule]Tests` in a file of the same name, namespace
  `PerformativeMail.Sim.Tests.Unit.[System]` / `.Integration.[System]`
- method `[Scenario]_[Expected]` — e.g. `InsertLetter_IntoFullStack_Rejected()`
- GDD test IDs (`H-R4-1a`) go in the method name or a `[Trait("Id", "H-R4-1a")]`
  so a failure is attributable.
- Every test is Arrange / Act / Assert.

## Story Type → Test Evidence

| Story Type | Required Evidence | Location |
|---|---|---|
| Logic | Automated unit test — must pass | `tests/unit/[system]/` |
| Integration | Integration test OR playtest doc | `tests/integration/[system]/` |
| Visual/Feel | Screenshot + lead sign-off | `production/qa/evidence/` |
| UI | Retained screenshot of each screen touched | `production/qa/evidence/` |
| Config/Data | Smoke check pass | `production/qa/smoke-*.md` |

## Not yet set up

- **gdUnit4 (Godot-side [UI] tests)** — requires the Godot 4.7.1 .NET build.
  Then: AssetLib → GdUnit4 → install, enable under Project Settings → Plugins,
  and verify `res://addons/gdUnit4/bin/GdUnitCmdTool.gd` exists (capital U).
  Then add a second CI job.

## CI

Tests run on every push to `main`/`master` and on every pull request
(`actions/setup-dotnet` 10.0.x → restore → build Release → `dotnet test --solution PerformativeMail.sln`).
Set a branch protection rule to require the `Sim tests (...)` matrix checks and
the `Game project build (no editor)` check (`game-build` job: builds
`PerformativeMail.Game.sln` from the `Godot.NET.Sdk` NuGet, no editor), so that a
failed test suite or a broken game project blocks merging.
