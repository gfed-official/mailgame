# Evidence — godot-host-shell story 001: game csproj, game `.sln`, `.gdignore` hygiene, Export* build check

**Date**: 2026-10-17 · **Story**: `production/epics/godot-host-shell/story-001-game-csproj-solution-gdignore-build-mapping.md` · **Type**: Integration
**Machine**: Linux x86_64; .NET SDK 10.0.402 (mise); Godot `4.7.1.stable.mono.official` at `~/.local/bin/godot-mono`. `Godot.NET.Sdk/4.7.1` restored from the nuget.org cache with no `nuget.config` (none committed). The CI `game-build` job has not yet run on a GitHub runner, so a clean-runner nuget.org restore stays UNVERIFIED (story AC-5: the job fails, never skips).

## Builds (all exit 0, 0 warnings, 0 errors; `TreatWarningsAsErrors` inherited from the root props)

| Command (prefixed `mise exec dotnet@10.0.402 --`) | Result |
|---|---|
| `dotnet build PerformativeMail.Game.csproj -c Debug` | exit 0, 0 warnings |
| `dotnet build PerformativeMail.Game.sln -c ExportDebug` | exit 0, 0 warnings |
| `dotnet build PerformativeMail.Game.sln -c ExportRelease` | exit 0, 0 warnings |
| `dotnet build PerformativeMail.Game.csproj -c ExportDebug` / `-c ExportRelease` | exit 0; creates `src/PerformativeMail.{Sim,Client.Core}/obj/{ExportDebug,ExportRelease}/` |
| `bash -c '~/.local/bin/godot-mono --headless --path . --build-solutions --quit'` | exit 0 — `[ DONE ] dotnet_build_project`; no warning/error lines |
| `dotnet build PerformativeMail.sln -c Release` | exit 0, 0 warnings (Godot-free solution unchanged; `grep -c PerformativeMail.Game PerformativeMail.sln` = 0) |

Finding: an `ExportRelease`/`ExportDebug` build of the **solution** builds the libraries as `Release` (by the ADR-0019 §9 mapping), so `obj/Export*` evidence under the libraries appears only after a **csproj**-level Export* build; `check-game-build.sh` does both.

## `tools/ci/check-build-config.sh` (exit 0)

```
PASS  PerformativeMail.Sim  Debug  (Optimize=false asserts=true checked=true)
PASS  PerformativeMail.Sim  Release  (Optimize=true asserts=false checked=false)
PASS  PerformativeMail.Sim  Release -p:SimAsserts=true  (Optimize=true asserts=true checked=true)
PASS  PerformativeMail.Sim  ExportDebug  (Optimize=false asserts=true checked=true)
PASS  PerformativeMail.Sim  ExportRelease  (Optimize=true asserts=false checked=false)
PASS  PerformativeMail.Client.Core  Debug  (Optimize=false asserts=true checked=true)
PASS  PerformativeMail.Client.Core  Release  (Optimize=true asserts=false checked=false)
PASS  PerformativeMail.Client.Core  Release -p:SimAsserts=true  (Optimize=true asserts=true checked=true)
PASS  PerformativeMail.Client.Core  ExportDebug  (Optimize=false asserts=true checked=true)
PASS  PerformativeMail.Client.Core  ExportRelease  (Optimize=true asserts=false checked=false)
check-build-config: all 10 rows passed
```

## `tools/ci/check-gdignore.sh --self-check` (exit 0; re-captured after the LP/QL gate fixes)

```
PASS  self-check: clean fake layout accepted
PASS  self-check: planted violation detected: src/.gdignore present
PASS  self-check: planted violation detected: .gdignore under src/Game
PASS  self-check: planted violation detected: .gdignore under content/
PASS  self-check: planted violation detected: missing tests/.gdignore
PASS  self-check: planted violation detected: visible obj/ directory
PASS  self-check: planted violation detected: visible bin/ directory
PASS  self-check: planted violation detected: repo-root .gdignore present
PASS  self-check: planted violation detected: new src/PerformativeMail.* library without .gdignore
check-gdignore --self-check: clean layout accepted, 8 planted violations detected
```

## `tools/ci/check-gdignore.sh` (exit 0; re-captured — 15 rules after adding the repo-root `.gdignore` rule and the `src/PerformativeMail.*` glob)

```
PASS  tests/.gdignore present
PASS  tools/.gdignore present
PASS  prototypes/.gdignore present
PASS  docs/.gdignore present
PASS  design/.gdignore present
PASS  production/.gdignore present
PASS  src/PerformativeMail.Client.Core/.gdignore present
PASS  src/PerformativeMail.Sim/.gdignore present
PASS  no repo-root .gdignore
PASS  no src/.gdignore
PASS  no .gdignore under src/Game/
PASS  no .gdignore at or under any content/ directory
TestResults/.gdignore already present
PASS  no bin/ directory visible to Godot
PASS  no obj/ directory visible to Godot
PASS  no TestResults/ directory visible to Godot
check-gdignore: checked 15 rules
```

(The first real run printed `created TestResults/.gdignore`; the repo-root `TestResults/` is gitignored.)

**Gate results (2026-10-17):** LP-CODE-REVIEW CONCERNS (no blocking; findings 1/4/5/6/8 fixed in-story, 2/3/7 → TD) · QL-TEST-COVERAGE GAPS (none critical; gaps 1/3/4/5 fixed in-story, 2/6/7/8 → TD).

## `tools/ci/check-game-build.sh` (exit 0; re-captured — 16 rows after the gate fixes: stale `obj/Export*` removed before the csproj-level builds; evaluation-only rows for the Compile glob, the two ProjectReferences, `project.godot` assembly_name, the game sln's configuration mapping and no stray `nuget.config`)

```
PASS  build PerformativeMail.Game.sln -c Debug (exit 0, no warnings)
PASS  build PerformativeMail.Game.sln -c ExportDebug (exit 0, no warnings)
PASS  build PerformativeMail.Game.sln -c ExportRelease (exit 0, no warnings)
PASS  build PerformativeMail.Game.csproj -c ExportDebug (exit 0, no warnings)
PASS  build PerformativeMail.Game.csproj -c ExportRelease (exit 0, no warnings)
PASS  src/PerformativeMail.Sim/obj/ExportDebug exists
PASS  src/PerformativeMail.Client.Core/obj/ExportDebug exists
PASS  src/PerformativeMail.Sim/obj/ExportRelease exists
PASS  src/PerformativeMail.Client.Core/obj/ExportRelease exists
PASS  PerformativeMail.sln does not mention PerformativeMail.Game
PASS  csproj Compile items are src/Game/** only (1 file(s))
PASS  csproj references Sim and Client.Core exactly once each
PASS  project.godot [dotnet] assembly_name matches PerformativeMail.Game.csproj
PASS  PerformativeMail.Game.sln configurations: Debug/ExportDebug/ExportRelease; Game same-named, libraries Debug/Release/Release
PASS  no nuget.config outside prototypes/
PASS  PerformativeMail.Sim  Debug  (Optimize=false asserts=true checked=true)
PASS  PerformativeMail.Sim  Release  (Optimize=true asserts=false checked=false)
PASS  PerformativeMail.Sim  Release -p:SimAsserts=true  (Optimize=true asserts=true checked=true)
PASS  PerformativeMail.Sim  ExportDebug  (Optimize=false asserts=true checked=true)
PASS  PerformativeMail.Sim  ExportRelease  (Optimize=true asserts=false checked=false)
PASS  PerformativeMail.Client.Core  Debug  (Optimize=false asserts=true checked=true)
PASS  PerformativeMail.Client.Core  Release  (Optimize=true asserts=false checked=false)
PASS  PerformativeMail.Client.Core  Release -p:SimAsserts=true  (Optimize=true asserts=true checked=true)
PASS  PerformativeMail.Client.Core  ExportDebug  (Optimize=false asserts=true checked=true)
PASS  PerformativeMail.Client.Core  ExportRelease  (Optimize=true asserts=false checked=false)
check-build-config: all 10 rows passed
PASS  tools/ci/check-build-config.sh
check-game-build: checked 16 rows
```

The five new shape rows are positive-tested only (they match the current files); their failure branches were not planted. `check-gdignore.sh --self-check` does plant and detect its 8 violations.

## CI

`.github/workflows/tests.yml` gains the `game-build` job (setup-dotnet 8.0.x + 10.0.x; `using Godot` guard; `check-gdignore.sh --self-check` + run; `dotnet restore`/`build PerformativeMail.Game.sln` Debug + ExportRelease; `check-game-build.sh`). The YAML parses with jobs `test` and `game-build`. Pass/fail on a runner is established by CI itself.
