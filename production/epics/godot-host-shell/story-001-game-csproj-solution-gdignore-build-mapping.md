# Story 001: Godot game project — `PerformativeMail.Game.csproj`, `PerformativeMail.Game.sln`, `[dotnet]` setting, `.gdignore` hygiene and the Export* build check

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: In Progress
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day; ~150 lines of csproj/sln/scripts + CI job; no gameplay code)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-17

## Context

**GDD**: `design/gdd/simulation-core.md` (R33 Godot-free Sim assembly with CI enforcement; R2/R17 host role). This module has no GDD of its own.
**Requirement**: `TR-sim-040` (the Export* → `Optimize`/`SIM_ASSERT` mapping half: asserts compile out of Release/ExportRelease), `TR-movement-022` (the glob/reference half: "Sim references no Godot"; the golden-hash half is player-character 010)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. The csproj shape, solution configurations, `.gdignore` policy and CI build job have **no TR-ID in the registry**; they cite ADR-0001 §2–§4, ADR-0019 §8–§9.)*

**ADR Governing Implementation**: ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework (§2 game project, §3 solutions, §4 build configurations) — Accepted 2026-10-16 (primary); ADR-0019 (§8 project-file hygiene and `.gdignore` list, §9 `[dotnet]` assembly name and the Godot-only `PerformativeMail.Game.sln` with Export* configurations) — Accepted 2026-10-16 (secondary, amends ADR-0001 §2's `src/.gdignore` note and ADR-0001 §3's "IDE-only" solution)
**ADR Decision Summary**: The Godot project is a hand-written `PerformativeMail.Game.csproj` at the repo root (`Godot.NET.Sdk/4.7.1`, `net8.0`, `EnableDynamicLoading`), compiling `src/Game/**` only and referencing the Godot-free Sim and Client.Core by `ProjectReference`. The Godot-free `PerformativeMail.sln` stays Godot-free. A separate Godot-only `PerformativeMail.Game.sln` (configurations `Debug`/`ExportDebug`/`ExportRelease`) sits next to the csproj because the exporter needs one. `Directory.Build.props`/`.targets` already map `ExportRelease`/`ExportDebug` onto `Optimize`/`SIM_ASSERT`. Non-game folders carry `.gdignore`; `src/Game/` and `content/` never do.
**ADR Version**: ADR-0001 2026-10-16; ADR-0019 2026-10-16

**Engine**: Godot 4.7.1 .NET (`~/.local/bin/godot-mono`; Godot needs `dotnet` on `PATH`, so launch it as `mise exec dotnet@10.0.402 -- ~/.local/bin/godot-mono …`) | **Risk**: HIGH
**Engine Notes**: ADR-0001 F1 (the `ProjectReference` from `Godot.NET.Sdk/4.7.1` to the net8.0 Sim builds under both `dotnet build` and `godot-mono --build-solutions`), F6 (a referenced plain-SDK project under `ExportDebug`/`ExportRelease` gets empty `Optimize` unless the props map it — already mapped), F7 (`Environment` is ambiguous between `System` and `Godot` under `ImplicitUsings`: qualify `System.Environment`; `ImplicitUsings` stays on), F13 (`Assembly.Location` is empty inside Godot), F16 (Godot builds the csproj, not a solution) are all VERIFIED by `prototypes/spike-godot-dotnet/REPORT.md` — read it, but never promote its code. UNVERIFIED: that `Godot.NET.Sdk 4.7.1` restores from nuget.org on a clean machine/runner (the spike used a local `nuget.config` pointing at the editor's `GodotSharp/Tools/nupkgs`; ADR-0001 says it is published on nuget.org). **Do not commit a machine-specific `nuget.config`.** If restore fails here, record it in Last Updated and use an untracked override.

**Control Manifest Rules (Foundation)**:
- Required: `PerformativeMail.Game.csproj` hand-written at the repo root with `Sdk="Godot.NET.Sdk/4.7.1"`, `net8.0`, `EnableDynamicLoading=true`, `RootNamespace` `PerformativeMail.Game`, `PublishTrimmed=false`, `PublishAot=false`; Compile Remove `src/PerformativeMail.*/**;tests/**;tools/**;prototypes/**;**/obj/**;**/bin/**`; Sim and Client.Core by `ProjectReference` only (ADR-0001 §2)
- Required: `PerformativeMail.sln` stays Godot-free; every script and CI step names its solution/project explicitly (ADR-0001 §3, ADR-0019 §8)
- Required: MSBuild logic keys on `$(Optimize)`/`$(SimAsserts)`, never `Configuration == 'Release'`; Godot-side C# qualifies `System.Environment` (ADR-0001 §4, Implementation Guidelines)
- Forbidden: `Assembly.Location`; trimming/AOT; `EnableGodotDotNetPreview`; a `.gdignore` under `content/` or `src/Game/` (ADR-0001, ADR-0019 §8)

---

## Acceptance Criteria

*From ADR-0001 §2–§4 and ADR-0019 §8–§9 (no GDD acceptance criteria; TR-sim-040 / TR-movement-022 halves), scoped to this story. Delta from the repo today: there is no `PerformativeMail.Game.csproj`, `PerformativeMail.Game.sln`, `src/Game/` or `[dotnet]` section; `src/.gdignore` and `tests/.gdignore` exist, `tools/.gdignore` and `prototypes/.gdignore` do not; `Directory.Build.props`/`.targets` already carry the Export* mapping (simulation-core 001); `tools/ci/check-build-config.sh` evaluates the Sim csproj only.*

- [ ] **Game project builds warning-free.** `mise exec dotnet@10.0.402 -- dotnet build PerformativeMail.Game.csproj -c Debug` and `godot-mono --headless --path . --build-solutions --quit` both exit 0 with zero warnings (the root `TreatWarningsAsErrors`/`EnforceCodeStyleInBuild`/`ImplicitUsings`/`Nullable` apply). The csproj matches ADR-0001 §2 exactly, references `PerformativeMail.Sim` and `PerformativeMail.Client.Core` once each, and contains one placeholder compilable file under `src/Game/` (`src/Game/GameAssembly.cs`, a `public static class GameAssembly { public const string Name = "PerformativeMail.Game"; }` — **no `using Godot` needed**, so the file builds under the ambiguity rule) so the assembly is non-empty. `project.godot` gains `[dotnet] project/assembly_name="PerformativeMail.Game"`. No `nuget.config` is committed.
- [ ] **Export* configurations reach the libraries (TR-sim-040 mapping half).** After `dotnet build PerformativeMail.Game.csproj -c ExportRelease` the referenced libraries were built in the same configuration (`src/PerformativeMail.Sim/obj/ExportRelease/` and `src/PerformativeMail.Client.Core/obj/ExportRelease/` exist), and the same for `-c ExportDebug`. A new `tools/ci/check-game-build.sh` runs Debug/ExportDebug/ExportRelease builds of `PerformativeMail.Game.sln`, asserts that directory evidence for each, and calls `tools/ci/check-build-config.sh`, which this story extends to evaluate `PerformativeMail.Client.Core.csproj` as well as the Sim (rows: Debug asserts on, Release off, ReleaseAssert on, ExportDebug on, ExportRelease off + `Optimize=true`). The script prints a count of rows checked and exits non-zero on zero rows. (The runtime `Sim.AssertsEnabled` print is story 004's boot log; the shipped-export runtime check waits for the export story, ADR-0019 V1.)
- [ ] **Two solutions, one Godot-free.** `PerformativeMail.Game.sln` at the repo root contains `PerformativeMail.Game.csproj`, the Sim and Client.Core projects, and solution configurations `Debug`, `ExportDebug`, `ExportRelease` mapped per ADR-0019 §9 (Game: same-named; libraries: `Debug`/`Release`/`Release`); `dotnet build PerformativeMail.Game.sln -c ExportRelease` exits 0. `PerformativeMail.sln` is unchanged and a CI grep proves it does not mention `PerformativeMail.Game`. `project.yaml` `commands.test` becomes `mise exec dotnet@10.0.402 -- dotnet test --solution PerformativeMail.sln` and `tests/README.md` states that a bare `dotnet build`/`dotnet test` is ambiguous with two solutions plus a csproj at the root (ADR-0001 §3). `commands.build` stays `[TO BE CONFIGURED]` (set by the export story).
- [ ] **`.gdignore` hygiene (ADR-0019 §8).** `tests/`, `tools/`, `prototypes/`, `docs/`, `design/`, `production/`, `src/PerformativeMail.Sim/` and `src/PerformativeMail.Client.Core/` each carry a `.gdignore`; `src/.gdignore` is removed (it would hide `src/Game/`); `src/Game/` and any `content/` directory carry none. New `tools/ci/check-gdignore.sh` (with `--self-check` that is seen failing on a planted violation) asserts all of that and that no `bin/`, `obj/` or `TestResults/` directory is visible to Godot (generated, gitignored `TestResults/` at the repo root gets `TestResults/.gdignore` created idempotently by the script when the directory exists — print what it did). It prints `checked N rules`, never a bare OK.
- [ ] **CI builds the game project without the editor.** `.github/workflows/tests.yml` gains a `game-build` job (setup-dotnet 8.0.x + 10.0.x; `dotnet restore`/`build PerformativeMail.Game.sln` for Debug and ExportRelease; `check-game-build.sh`; `check-gdignore.sh`; the existing `using Godot` grep guard still passes). If restoring `Godot.NET.Sdk/4.7.1` from nuget.org fails on the runner, the job **fails** (never skipped) and the failure is recorded in this story's Last Updated; the Godot-installed job in story 011 is then the only build evidence.

---

## Implementation Notes

*Derived from ADR-0001 §2–§4, ADR-0019 §8–§9:*

- Hand-write the csproj from ADR-0001 §2 (the XML block is the contract). Add `<Compile Include="src/Game/**/*.cs" />` only if the SDK default glob does not already pick it up after the `Compile Remove`; do not widen the glob.
- `PerformativeMail.Game.sln`: create with `dotnet new sln` then `dotnet sln add`, then edit the solution configurations by hand (a default Debug/Release solution fails under `-c ExportRelease` — spike C10). The name must equal `<assembly_name>.sln` (ADR-0019 V2 assumption: the exporter's lookup; unverified until the export story).
- Moving `src/.gdignore`: ADR-0001 §2's note and ADR-0019 §8 agree — it moves into each `src/PerformativeMail.*` folder. Simulation-core 001 left this to this story (`production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md`, Out of Scope).
- `check-gdignore.sh` and `check-game-build.sh` follow the repo's existing script conventions (`tools/ci/check-build-config.sh`: `DOTNET` override variable, `--self-check`, non-zero on error vs match).
- `project.godot` is hand-written (DEF-1: it gets stretch `disabled`/`keep` until story 002 sets them). Only the `[dotnet]` section is added here; the main scene and display settings are story 002.
- Godot must be able to find `dotnet`: wrap with `mise exec dotnet@10.0.402 --`. The editor's first run creates `.godot/` (gitignored).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002: `Main.tscn`, `[application] run/main_scene`, display/stretch settings, `[autoload]` ban check, the smoke harness.
- Story 011: the Godot-installed CI job (headless run) and `project.yaml` `commands.run`/`commands.smoke`.
- Export templates, `export_presets.cfg`, the first export (ADR-0019 V1–V5), `project.yaml` `commands.build` — out of the M-1a epic scope (export story, ADR-0019 Migration Plan step 4).
- `PerformativeMail.Client.Core.csproj` itself (`production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md`).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: the output of `tools/ci/check-game-build.sh` and `tools/ci/check-gdignore.sh` (with their `--self-check` runs) retained in the story's Last Updated or `production/qa/evidence/game-build-evidence.md`, plus a green `game-build` CI job — `/story-done` checks the scripts and the workflow job exist; pass/fail is established by `/smoke-check` and CI

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md` (props/targets, `check-build-config.sh`, CI scaffold), `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md` (`PerformativeMail.Client.Core.csproj`)
- Unlocks: Story 002 and every other story in this epic; every Godot-side story in the feature epics
