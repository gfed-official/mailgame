# ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously ("start implementation when it is mostly there"); engine check by `godot-specialist`. TD-ADR skipped (lean mode). Evidence: `prototypes/spike-godot-dotnet/REPORT.md` (OQ4 build spike, 2026-10-16). Inputs: `docs/architecture/architecture.md` v1, `design/gdd/simulation-core.md` R31–R33 (Provisional), `docs/architecture/simulation-core-adr-inputs.md` §1, §3.8–§3.10 (unreviewed seed, re-checked against the GDD), the existing scaffold.

- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
Every later ADR and story needs to know where code lives, which assemblies may reference which, how Debug/Release/Godot-export builds map to optimisation and assertion symbols, and how tests run. This ADR fixes four Godot-free net8.0 libraries plus one Godot game project at the repo root, linked by `ProjectReference`. It also maps Godot's `ExportDebug`/`ExportRelease` configurations onto `Optimize` and a `SIM_ASSERT` symbol through `Directory.Build.props`/`.targets`, and keeps one xUnit v3 test project on net10.0. The OQ4 spike (Godot 4.7.1 .NET) verified the build, the reference and the runtime load.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 .NET (`4.7.1.stable.mono.official.a13da4feb`), C# |
| **Domain** | Core / build (C#/.NET integration) |
| **Knowledge Risk** | HIGH for the Godot half (4.4–4.7 post-cutoff; the .NET integration is NOT SOURCEABLE in `docs/engine-reference/godot/`). It is reduced by the spike, which **VERIFIED** what this ADR relies on. LOW for the Godot-free half. |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md` (4.4→4.5: desktop .NET 8 minimum), `deprecated-apis.md`, `current-best-practices.md` (C# practices NOT SOURCEABLE); `Godot.NET.Sdk.4.7.1.nupkg` `Sdk/Sdk.props` + `Sdk.targets` (read from the installed build); `prototypes/spike-godot-dotnet/REPORT.md` |
| **Post-Cutoff APIs Used** | `Godot.NET.Sdk/4.7.1` MSBuild SDK (configurations `Debug;ExportDebug;ExportRelease`; output under `.godot/mono/temp/`); `godot-mono --build-solutions` (4.7.1 CLI). All VERIFIED by the spike (C3, C4, C11). |
| **Verification Required** | (1) **Export:** which .NET runtime an exported game loads, whether Godot's exporter publishes the csproj with the props mapping below, and whether `System.Globalization.Invariant` holds in the export. UNVERIFIED: no export templates are installed. Owner: ADR-0019 / the first export story. (2) Windows and macOS builds (spike was Linux only). (3) Editor hot reload with referenced projects. |

**Spike-verified facts this ADR relies on** (REPORT §5): F1 ProjectReference from a `Godot.NET.Sdk/4.7.1` net8.0 project to the plain net8.0 Sim builds via `dotnet build` and `godot-mono --build-solutions`; F2 Sim types load and run inside Godot; F3 editor/headless runs execute on the newest installed runtime (10.0.13 here) because Godot hosts .NET through `GodotPlugins.runtimeconfig.json` (`rollForward: LatestMajor`); F4 the game's `InvariantGlobalization` is **not** in force in editor runs; F5 a self-contained `ExportRelease` publish bundles .NET 8.0.32 (partly verified); F6 Export* configurations leave a referenced plain-SDK project unoptimised unless mapped; F7 the root props build the Godot project warning-free, except that `Environment` is ambiguous under `ImplicitUsings`; F8 `Compile Remove` globs work; F13 `Assembly.Location` is empty inside Godot; F16 Godot builds the csproj, not a solution.

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None. This is the root ADR: it decides only where code lives and how it builds, and rests on no other decision. |
| **Enables** | ADR-0002 (`Hosting/` folder, `GameLoop` in the Godot project), ADR-0003 (`Composition/` folder, architecture/reflection test home), ADR-0004–ADR-0011 (Sim folder layout, analyzer mechanism, assert symbols), ADR-0012 and ADR-0018 (`Client.Core`), ADR-0019 (Godot game project shape, export verification) |
| **Blocks** | Every Sim story (folder + assert conventions); the "create Godot game project" story; any `tools/*` story |
| **Ordering Note** | Accept first. The analyzer *rule list* (float/double/decimal ban, culture-free APIs, `GetHashCode` etc.) is ADR-0006's; this ADR only owns the mechanism that carries it. |

## Context

### Problem Statement
The scaffold has `PerformativeMail.sln`, `Directory.Build.props`, a net8.0 Sim and a net10.0 xUnit v3 test project. Three things were open:
- the Godot game project's shape;
- how Godot's export configurations interact with the libraries;
- where asserts and always-on checks hook in (TR-sim-040).

Simulation Core R33 requires a Godot-free Sim with CI enforcement (TR-movement-022 "Sim references no Godot"). Grid Inventory OQ20 and Sim Core OQ4 asked whether Godot 4.7.1's SDK accepts a `ProjectReference` to a plain net8.0 library at all. Without this ADR, the first story would invent the layout.

### Constraints
- Godot 4.7.1 .NET; desktop needs .NET 8 minimum (4.5 upgrade page). The only installed runtime is .NET 10.0.13; the SDK is 10.0.402 via `mise`.
- Sim must be bit-reproducible across machines (Sim Core N6, R27–R30): integer-only, culture-free, no Godot.
- Solo developer: one test project, fast builds, no per-system projects.
- `[Conditional]` binds at the **caller's** assembly (seed §3.8), so assert symbols must be defined in the Sim's own build.
- Seed correction (§1, R33): the `Deterministic` csproj flag means reproducible *builds*, not runtime determinism. Export configurations must still give an optimised Sim with asserts off. No trimming, because descriptor tables use reflection.

### Requirements
- TR-sim-040: always-on checks to a fixed record ring in every build; asserts compile out of Release; `SimCounters`.
- TR-movement-022: the golden per-tick transform hash is equal on Windows and Linux; Sim references no Godot.
- The architecture-test half of TR-sim-013 (composition reflection test home).
- Godot game project builds against the libraries without compiling them twice.
- CI runs without Godot installed.

## Decision

### 1. Assemblies and paths

| Project | Path | Target | May reference | Must never reference |
|---|---|---|---|---|
| `PerformativeMail.Sim` | `src/PerformativeMail.Sim/PerformativeMail.Sim.csproj` | `net8.0` | BCL only; analyzers with `PrivateAssets="all"` | any `Godot*` assembly, `Client.Core`, any runtime NuGet package |
| `PerformativeMail.Client.Core` | `src/PerformativeMail.Client.Core/PerformativeMail.Client.Core.csproj` (created by the first Input or HUD story) | `net8.0` | `PerformativeMail.Sim` | any `Godot*` assembly, runtime NuGet packages |
| Godot game project | `PerformativeMail.Game.csproj` at the **repo root** (Godot's `res://`); Godot code under `src/Game/**` | `net8.0`, `Sdk="Godot.NET.Sdk/4.7.1"`, `EnableDynamicLoading=true` | Sim, Client.Core (`ProjectReference`) | — |
| `PerformativeMail.Sim.Tests` | `tests/PerformativeMail.Sim.Tests/` compiling `tests/unit/**` and `tests/integration/**` | `net10.0` | Sim, Client.Core | any `Godot*` assembly |
| tools | `tools/<Name>/<Name>.csproj` (ContentValidator M0, BotClient M0+, BalanceSim M1) | `net8.0` console | Sim | any `Godot*` assembly |

- `project.godot` sets `[dotnet] project/assembly_name="PerformativeMail.Game"`. This amends architecture.md's `PerformativeMail.csproj`: the distinct name keeps the Godot project out of the Godot-free `PerformativeMail.sln` (§3), and `PerformativeMail.Game` reads as the game, not the solution.
- The **Sim folder layout** is architecture.md §Physical assemblies. Namespace = `PerformativeMail.Sim.<Folder>`.
  - `Primitives/` depends on nothing else in the Sim.
  - `Core/`, `Hosting/`, `Composition/` and `ClientLayer/` hold Sim Core.
  - `Content/` holds content.
  - There is one folder per system.
  - The seed's `src/Sim/` and `tests/unit/PerformativeMail.Sim.Tests/` paths are superseded by the scaffold paths above.

### 2. The Godot game project

```xml
<Project Sdk="Godot.NET.Sdk/4.7.1">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>PerformativeMail.Game</RootNamespace>
    <PublishTrimmed>false</PublishTrimmed>
    <PublishAot>false</PublishAot>
  </PropertyGroup>
  <ItemGroup>
    <Compile Remove="src/PerformativeMail.*/**;tests/**;tools/**;prototypes/**;**/obj/**;**/bin/**" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="src/PerformativeMail.Sim/PerformativeMail.Sim.csproj" />
    <!-- + src/PerformativeMail.Client.Core once it exists -->
  </ItemGroup>
</Project>
```
- It is **hand-written**. In 4.7.1, `--build-solutions` builds an existing csproj but does not create one (spike C1).
- `Godot.NET.Sdk 4.7.1` restores from nuget.org (published, checked 2026-10-16). No machine-specific `nuget.config` is committed.
- The **linked-folder fallback is not adopted.** `ProjectReference` works (F1). The fallback stays documented only in case a future Godot version breaks the reference.
- Godot needs `dotnet` on `PATH`. On this machine, run Godot through `mise exec dotnet@10.0.402 -- godot-mono …`.
- `src/.gdignore`, `tests/.gdignore`, `tools/.gdignore` and `prototypes/.gdignore` keep the editor's resource scan out of non-Godot folders (`src/Game/` must then carry no `.gdignore`; Godot scenes and scripts live there). **Note:** `src/.gdignore` hides `src/Game/` from the editor too. The story that creates `src/Game/` moves the `.gdignore` into each `src/PerformativeMail.*` folder instead.

### 3. Solutions

- **`PerformativeMail.sln` is Godot-free**: Sim, Client.Core, the test project and `tools/*`. CI builds and tests only this solution, with no Godot installed.
- The Godot game project is **not** added to it. Godot builds `PerformativeMail.Game.csproj` directly (F16).
  - A developer may keep an IDE solution `PerformativeMail.Game.sln`. It must declare `Debug`, `ExportDebug` and `ExportRelease` solution configurations. A `dotnet new sln` default (Debug/Release) fails under `-c ExportRelease` (C10).
- With a `.sln` and a `.csproj` both at the root, a bare `dotnet build` / `dotnet test` is ambiguous. Commands must name the solution: `dotnet test --solution PerformativeMail.sln`. `project.yaml` `commands.test` and `tests/README.md` are updated by the story that creates the game csproj.

### 4. Build configurations, optimisation and assert symbols

| Configuration | Used by | `Optimize` | `SIM_ASSERT` (+ per-system aliases) | `checked` arithmetic in Sim/Client.Core |
|---|---|---|---|---|
| `Debug` | dev, Godot editor | false | on | on |
| `Release` | CI, perf gates, tools | true | off | off |
| `Release` + `-p:SimAsserts=true` ("ReleaseAssert") | nightly soaks (H-R19-1, TC-X-1) | true | on | on |
| `ExportDebug` | Godot debug export | false (mapped) | on | on |
| `ExportRelease` | Godot release export | **true (mapped)** | off | off |

- **`Directory.Build.props`** adds the Export* mapping (spike C8; Godot's SDK maps these only for the Godot project itself, F6):
  ```xml
  <PropertyGroup Condition="'$(Configuration)' == 'ExportRelease'">
    <Optimize Condition="'$(Optimize)' == ''">true</Optimize>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'ExportDebug'">
    <Optimize Condition="'$(Optimize)' == ''">false</Optimize>
    <DebugSymbols Condition="'$(DebugSymbols)' == ''">true</DebugSymbols>
  </PropertyGroup>
  ```
- **`Directory.Build.targets`** (new) derives the assert switch *after* the SDK has set `Optimize`. Computing it in `.props` is wrong for `Release` (spike C8):
  ```xml
  <PropertyGroup Condition="'$(MSBuildProjectName)' == 'PerformativeMail.Sim' or '$(MSBuildProjectName)' == 'PerformativeMail.Client.Core'">
    <SimAsserts Condition="'$(SimAsserts)' == '' and '$(Optimize)' != 'true'">true</SimAsserts>
    <DefineConstants Condition="'$(SimAsserts)' == 'true'">$(DefineConstants);SIM_ASSERT;INVENTORY_ASSERT</DefineConstants>
    <CheckForOverflowUnderflow Condition="'$(SimAsserts)' == 'true'">true</CheckForOverflowUnderflow>
  </PropertyGroup>
  ```
  - `SIM_ASSERT` is the umbrella and turns on every per-system alias (seed §3.8).
  - Turning one system off alone is `-p:InventoryAsserts=false`, implemented when first needed. `INVENTORY_ASSERT` is the first alias (Grid Inventory R19; ADR-0009).
- **Rules:**
  - Sim and Client.Core code must never test `Configuration`, `DEBUG`, `RELEASE`, `EXPORTDEBUG` or `EXPORTRELEASE`. It uses `SIM_ASSERT` / `<SYSTEM>_ASSERT` only.
  - MSBuild logic must never key on `Configuration == 'Release'`. It keys on `$(Optimize)` or `$(SimAsserts)`.
- **`checked` arithmetic.** Hashing, RNG and wrap-safe serial code must be written `unchecked(...)` explicitly, or Debug and Release dumps diverge (seed §1; ADR-0005/0006 own those algorithms).

### 5. Assertions, counters and always-on checks (TR-sim-040)

- **`SimAssert`** (`Primitives/`) is a static class of pure `[Conditional("SIM_ASSERT")]` methods:
  - `That(bool, ushort code)`, `Fail(ushort code)`;
  - per-system aliases as `[Conditional("INVENTORY_ASSERT")]` overloads in the owning system's folder.
  - Its arguments must be pure: they are not evaluated when compiled out.
  - Every call site lives inside the Sim or Client.Core assembly, because `[Conditional]` binds at the caller.
  - It holds no state. "No statics" (ADR-0003) bans static *state*, not pure static helpers.
- **`Sim.AssertsEnabled`** is a `public const bool` (true under `SIM_ASSERT`), logged at start. A test run in `Release` asserts it is `false`.
- **`SimCheckRing`** (`Core/`) is per `SimWorld`, never static. It is a fixed-capacity ring (default 256) of `{ushort code, uint tick, long a, long b}`.
  - It rate-limits to one record per code per 30 ticks and keeps a suppressed count per code.
  - It never throws, formats strings or allocates.
  - Always-on checks write to it in **every** configuration.
- **`SimCounters`** (`Core/`) is per `SimWorld`: a `long[]` indexed by `CounterId` (a `ushort` enum appended to, never renumbered), readable between ticks.
- Full-scan invariants are assert-only (Grid Inventory R19 model).

### 6. Analyzers (mechanism only)
- The Sim and Client.Core may reference analyzers with `PrivateAssets="all"`, versions pinned in the csproj. The first is `Microsoft.CodeAnalysis.BannedApiAnalyzers`, with `BannedSymbols.txt` in each project folder.
- **Rule content is ADR-0006's**: float/double/decimal, clocks, `GetHashCode`, culture-sensitive APIs, `Dictionary` iteration.
- A custom Roslyn analyzer, if ADR-0006 needs one, lives in `tools/PerformativeMail.Analyzers/` (netstandard2.0, as Roslyn requires) and is referenced as an analyzer.
- **Culture:** `InvariantGlobalization` stays in `Directory.Build.props`, but it is **not** relied on. Editor runs ignore it (F4). Culture-free behaviour comes from API choice plus the analyzer.

### 7. Test framework
- xUnit **v3** (`xunit.v3` 4.0.2) on **Microsoft.Testing.Platform** (`global.json` `test.runner`). One project, `net10.0`, compiling `tests/unit/**` and `tests/integration/**` with one folder per system (`tests/unit/inventory/`). Shared fixtures go in the project folder. Conventions are in `tests/README.md` (class/method naming, `[Trait("Id", …)]`, exit codes 0/2/8).
- `[PERF]` and 0-alloc tests run in a non-parallel collection. The 0-alloc gate (ADR-0006) runs in a dedicated process because `GC.CollectionCount` is process-wide.
- **Runtime coverage.** Tests run on .NET 10, while shipped builds probably run .NET 8 (F5).
  - The test csproj accepts `-p:PmTestTfm=net8.0`, which overrides `TargetFramework`.
  - From the **first Sim Core story (M-1a)**, CI adds a `net8.0` leg (setup-dotnet 8.0.x + 10.0.x SDK, `-p:PmTestTfm=net8.0`). ADR-0005 golden vectors, ADR-0006 T2 primitives and the 0-alloc gate measure there. At **M0** CI adds a `windows-latest` leg. Locally, `mise install dotnet@8` enables the leg; without it, local runs are net10-only. The golden transform hash and canonical dump must match across all legs (TR-movement-022, T2).
  - Perf numbers from tests or the editor are **not** transferable to exports.
- **Godot-side tests** (gdUnit4) are deferred. They need the Godot .NET build (now installed) and a story of their own.

### 8. CI guards (Linux runner from M-1a; Windows from M0)
1. **Architecture test** (`tests/unit/architecture/AssemblyRulesTests.cs`):
   - the Sim and Client.Core reference no assembly whose name starts with `Godot`;
   - the Sim does not reference Client.Core;
   - neither has a runtime `PackageReference`, checked from the compiled metadata.
   - The scaffold's `SimAssemblyTests` already covers the first rule for the Sim.
2. **Grep guard:** `grep -rn "using Godot" src/PerformativeMail.Sim src/PerformativeMail.Client.Core` fails the build when it matches.
3. **Composition reflection test** (TR-sim-013; the rule is ADR-0003's): lives in `tests/unit/architecture/`.
4. **Release guard:** `Sim.AssertsEnabled == false` in the Release leg.

### Architecture Diagram

```
repo root (= Godot res://)
├── project.godot                      [dotnet] assembly_name = PerformativeMail.Game
├── PerformativeMail.Game.csproj       Godot.NET.Sdk/4.7.1, net8.0 ──ProjectReference──┐
├── PerformativeMail.sln               Godot-free: Sim, Client.Core, Tests, tools        │
├── Directory.Build.props              shared props + Export* → Optimize mapping         │
├── Directory.Build.targets            SimAsserts → SIM_ASSERT, checked arithmetic       │
├── global.json                        SDK 10.0.x, MTP test runner                        │
├── src/
│   ├── Game/**                        Godot C# (GameLoop, adapters, views)             │
│   ├── PerformativeMail.Sim/          net8.0, BCL only  ◄───────────────────────────────┤
│   └── PerformativeMail.Client.Core/  net8.0 ──► Sim   ◄────────────────────────────────┘
├── tests/
│   ├── PerformativeMail.Sim.Tests/    net10.0, xUnit v3 ──► Sim (+ Client.Core)
│   ├── unit/<system>/**  integration/<system>/**
├── tools/<Name>/                      net8.0 console ──► Sim
└── prototypes/                        throwaway; excluded from the game glob
Dependencies point one way: Godot game → Client.Core → Sim.
```

### Key Interfaces
```csharp
namespace PerformativeMail.Sim.Primitives;
public static class SimAssert
{
    [Conditional("SIM_ASSERT")] public static void That(bool condition, ushort code);
    [Conditional("SIM_ASSERT")] public static void Fail(ushort code);
}
namespace PerformativeMail.Sim;
public static class Sim { public const bool AssertsEnabled = /* true iff SIM_ASSERT */; }
namespace PerformativeMail.Sim.Core;
public sealed class SimCheckRing   // per SimWorld; fixed capacity; 1 record/code/30 ticks
{ public void Record(ushort code, uint tick, long a, long b); public int Count { get; } /* + between-tick readers */ }
public sealed class SimCounters    // per SimWorld
{ public void Add(CounterId id, long n = 1); public long this[CounterId id] { get; } }
```

### Implementation Guidelines
- Sim and Client.Core **must never** reference a `Godot*` assembly, contain `using Godot`, or take a runtime NuGet package.
- The Sim **must never** reference Client.Core.
- Code in the Sim/Client.Core **must never** branch on `DEBUG`, `RELEASE`, `EXPORTDEBUG`, `EXPORTRELEASE` or `Configuration`. Use `SIM_ASSERT` / `<SYSTEM>_ASSERT`.
- MSBuild conditions **must** key on `$(Optimize)` or `$(SimAsserts)`, never on `Configuration == 'Release'`.
- Assertion arguments **must** be pure. Assertion calls **must** live in the Sim/Client.Core assemblies.
- Always-on checks **must** write to the per-world `SimCheckRing`. They **must never** throw, format strings or allocate.
- Hash, RNG and serial-number arithmetic **must** be written `unchecked(...)`.
- The Godot project **must** exclude `src/PerformativeMail.*/**`, `tests/**`, `tools/**`, `prototypes/**`, `**/obj/**` and `**/bin/**` from its compile glob, and reference the libraries by `ProjectReference` only.
- Exports **must not** trim or AOT-compile (`PublishTrimmed=false`, `PublishAot=false`).
- Godot-side C# **must** qualify `System.Environment` (and any other `System`/`Godot` clash) explicitly; `ImplicitUsings` stays on (F7).
- No code **may** use `Assembly.Location` or assembly-relative file paths (F13). Content arrives through `IContentSource` (ADR-0010).
- Sim and Client.Core **must not** register process-wide static event handlers (`AppDomain`, `Console`, BCL static events) or start threads or timers. These pin the collectible load context and break editor hot reload.
- New tests go in `tests/unit/<system>/` or `tests/integration/<system>/`, one class per rule, named per `tests/README.md`.

## Alternatives Considered

### Alternative 1: Linked-folder (shared source) compilation of the Sim into the game assembly
- **Description**: The game csproj compiles `src/PerformativeMail.Sim/**/*.cs` directly; the no-Godot rule is enforced on namespaces.
- **Pros**: Works even if the Godot SDK rejected a `ProjectReference`; one assembly to load.
- **Cons**: Sim code compiles under Godot's defines (`GODOT`, `TOOLS`) and could silently gain a Godot dependency. `[Conditional]` symbols would come from the game project. Two compilations of one source.
- **Rejection Reason**: The spike proved `ProjectReference` works (F1). It is kept only as a documented contingency.

### Alternative 2: The Godot project inside `PerformativeMail.sln`
- **Description**: One solution for everything, as `dotnet sln add` would produce.
- **Pros**: One file for IDEs.
- **Cons**: CI would need Godot packages and would build the Godot project in `Debug` under `-c Release` (the Godot SDK has no `Release` configuration; `dotnet sln add` maps it to `Debug`, C10). Export* solution configurations would have to be hand-maintained.
- **Rejection Reason**: It breaks the "CI runs without Godot" requirement for no gain. Godot builds the csproj directly (F16).

### Alternative 3: Test project per system / net8.0 test target only
- **Description**: `PerformativeMail.Inventory.Tests` etc., or tests on net8.0 to match exports.
- **Pros**: Isolation; runtime parity with exports.
- **Cons**: Multiplies build time for a solo developer. No .NET 8 runtime is installed locally.
- **Rejection Reason**: The seed and the TD ruling chose one project. Runtime parity is handled by the `net8.0` CI leg added with the first Sim Core story (M-1a, §7).

### Alternative 4: Key asserts on `DEBUG` / `Configuration`
- **Description**: Standard `#if DEBUG` / `Debug.Assert`.
- **Pros**: Familiar.
- **Cons**: `DEBUG` is undefined in `ExportDebug` for referenced libraries (F6/C7). Godot's `ExportRelease` would ship an **unoptimised** Sim with no mapping. `System.Diagnostics.Debug.Assert` throws/dialogs and formats.
- **Rejection Reason**: Verified wrong for Godot exports (spike C7).

## Consequences

### Positive
- The Godot-free half is testable today with plain `dotnet test`, and CI never needs Godot.
- The Godot integration path is proven on the pinned 4.7.1 build, not assumed.
- One assert symbol behaves the same in Debug, ExportDebug and nightly ReleaseAssert, and is off in Release and ExportRelease.
- The layering (Godot → Client.Core → Sim) is mechanically enforced.

### Negative
- Two solution entry points (CI sln vs Godot's csproj); bare `dotnet test` at the root becomes ambiguous once the game csproj exists.
- The editor runs on whatever newest runtime is installed (F3), so editor behaviour and perf can differ from the export's bundled .NET 8.
- A hand-written game csproj must be kept in step with Godot's template on engine upgrades.

## Risks
- **Export path unverified (no templates).** *Mitigation:* ADR-0019's first export story runs `--export-release` and records the runtime, the Invariant flag, `Optimize` of the bundled Sim, and `[Conditional]` removal (ADR-0009's `INVENTORY_ASSERT` check).
- **Runtime split (net10 tests/editor vs net8 export).** Integer code is runtime-independent by design, but BCL behaviour changes (sorting, string APIs) could differ. *Mitigation:* the M-1a `net8.0` CI leg (§7) + golden vectors/dumps (ADR-0005/0006); the Windows leg follows at M0.
- **Godot name clashes under `ImplicitUsings`** (`Environment`, possibly `Range`, `Timer`, `Thread`). *Mitigation:* explicit qualification; a `GlobalUsings.cs` alias in the game project if it becomes noisy.
- **`src/.gdignore` hides `src/Game/`.** *Mitigation:* noted in §2; the game-project story relocates it.
- **Godot upgrade changes `Godot.NET.Sdk` configurations.** *Mitigation:* re-run the spike (`prototypes/spike-godot-dotnet`, REPORT §7) on every engine pin change.
- **Engine validation (godot-specialist, 2026-10-16): 0 blocking, 8 minor.** The minor notes are folded in here:
  - **`assembly_name` set too late.** If someone creates a C# script in the editor before `[dotnet] project/assembly_name` is set, Godot may generate a csproj/sln named from `config/name` ("PerformativeMail") and collide with `PerformativeMail.sln` (UNVERIFIED for 4.7). *Mitigation:* set `assembly_name="PerformativeMail.Game"` in the **first** story (Migration step 1), not the game-project story.
  - **Dotted assembly name and `res://src/Game/` scripts were not exercised by the spike.** *Mitigation:* the game-project story's validation includes a headless run that instantiates a scene whose script lives under `src/Game/`.
  - **Editor hot reload.** Sim and Client.Core load into Godot's unloadable context (F13). Process-wide state pinning their types blocks reload: static BCL event handlers, reflection-based `System.Text.Json` caches, live threads or timers (UNVERIFIED). *Mitigation:* the guideline below; ADR-0002 bans Sim threads and timers.
  - **`.gdignore` scope.** `.gdignore` affects only the editor's resource scan, never MSBuild; the compile glob alone keeps library code out of the game assembly. `tools/` needs its own `.gdignore`. Because `tests/` is excluded from both, future gdUnit4 C# tests cannot live under `tests/`; the gdUnit4 story picks their folder.
  - **Nested `project.godot` files under `prototypes/`** (`m1a-delivery-throwing-concept`, `spike-godot-dotnet`). `prototypes/.gdignore` must exist before the root project is first opened in the editor.
  - **Trimming/AOT.** Command-line or preset properties override the csproj's `false`. ADR-0019 checks the export preset.
  - **Stale comment.** The root `Directory.Build.props` comment ("Godot-generated root .csproj") is updated with the Export* mapping.
  - **The §4 `.targets` text differs from the spike's first candidate.** Re-verified verbatim as spike C12 (PASS on all five rows); the C8 re-run in Validation Criteria still applies to the real files.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| simulation-core.md | R33: Sim is a plain net8.0 library, zero Godot refs, no runtime packages, adapters outside the Sim, CI fails on `Godot*`/`using Godot` (DEV-7) | §1 table, §8 guards; spike F1/F2 prove the reference path |
| simulation-core.md | R31: asserts compile out of Release; per-system toggles (`INVENTORY_ASSERT`); always-on checks to a fixed record ring; `SimCounters`; ReleaseAssert for soaks (TR-sim-040) | §4 symbol mapping, §5 `SimAssert`/`SimCheckRing`/`SimCounters` |
| simulation-core.md | R4 reflection test (architecture-test half of TR-sim-013) | §8 guard 3 (test home; rule in ADR-0003) |
| simulation-core.md | R32: 0-alloc gate measured in Release, non-parallel | §7 collection/process rule (gate itself: ADR-0006) |
| player-character-movement.md | TR-movement-022: golden per-tick transform hash equal on Windows and Linux; Sim references no Godot | §7 net8.0 leg (M-1a) + Windows leg (M0), §8 guards |
| grid-inventory.md | OQ20: the Godot project must not double-compile the Sim; `INVENTORY_ASSERT` toggle | §2 glob exclusion (F8), §4 alias |
| content-data-definitions.md | Culture-free ordinal sorting; build with `InvariantGlobalization` | §6: kept, but not relied on (F4); the analyzer enforces culture-free APIs (ADR-0006) |

**GDD impact (no GDD edited):**
- simulation-core.md R33 says "CI fails if … `using Godot`" and leaves the build mapping to the ADR: consistent.
- The seed's paths (`src/Sim/`, `tests/unit/PerformativeMail.Sim.Tests/`) are superseded by the scaffold paths. The GDD itself names no path.
- content-data-definitions.md says it builds "with `InvariantGlobalization`" as the cross-OS guarantee. F4 shows that flag is ignored in editor runs, so the guarantee must come from ordinal APIs alone. This is a wording note for C&D's re-review, logged in the planning run's residual conflicts.

## Performance Implications
- **CPU**: None at runtime. Perf budgets are measured in `Release`/`ExportRelease` only (optimised, asserts off). `ExportRelease` is optimised only because of the §4 mapping.
- **Memory**: `SimCheckRing` ≈ 256 × 24 B per world; `SimCounters` a few KB.
- **Load Time**: One extra assembly load (Sim, later Client.Core); negligible.
- **Network**: None.

## Migration Plan
The scaffold already matches §1 and §7 for the Sim and tests. Remaining steps, each in the story that needs it:
1. **First Sim Core story:** add `[dotnet] project/assembly_name="PerformativeMail.Game"` to `project.godot`, plus `prototypes/.gdignore` and `tools/.gdignore`. Add the Export* mapping to `Directory.Build.props` (and refresh its header comment), and create `Directory.Build.targets` (§4). Add `SimAssert`, `Sim.AssertsEnabled`, `SimCheckRing`, `SimCounters`, the architecture test and the grep guard (§5, §8). The props/targets snippets are pre-tested in `prototypes/spike-godot-dotnet/build-config-check/`.
2. **Godot game-project story** (with ADR-0002's `GameLoop`):
   - create `PerformativeMail.Game.csproj` per §2;
   - move `.gdignore` from `src/` into each library folder;
   - run a headless scene whose script lives under `src/Game/`;
   - update `commands.test` / `tests/README.md` to `dotnet test --solution PerformativeMail.sln`;
   - set `project.yaml` `commands.build` once an export preset exists.
3. **First Sim Core story:** add the CI `net8.0` leg and the `PmTestTfm` override (§7; required by ADR-0005/0006). **M0:** add the `windows-latest` leg.

## Validation Criteria
- `dotnet test --solution PerformativeMail.sln -c Release` passes on Linux, with the architecture test, the grep guard and the Release guard green.
- `dotnet msbuild src/PerformativeMail.Sim -p:Configuration=X -getProperty:Optimize -getProperty:DefineConstants` gives the §4 table for X ∈ {Debug, Release, ExportDebug, ExportRelease} (re-run of spike C8 on the real files).
- `mise exec dotnet@10.0.402 -- godot-mono --headless --path . --build-solutions --quit` exits 0 at the repo root, and the Sim is compiled exactly once (no duplicate-type errors).
- First export (ADR-0019): the runtime version, Invariant flag, `Sim.AssertsEnabled == false` and Sim `Optimize` are recorded.

## Related
- Evidence: `prototypes/spike-godot-dotnet/REPORT.md` (C1–C12, F1–F16).
- `docs/architecture/architecture.md` §Physical assemblies, §Godot game project glob, Q-ARCH-02, Q-ARCH-03 (closed by F3–F7 except export).
- ADR-0002 (GameLoop in the Godot project), ADR-0003 (composition reflection rule), ADR-0006 (analyzer rule list, 0-alloc gate), ADR-0009 (`INVENTORY_ASSERT`), ADR-0019 (export verification).
- `design/gdd/simulation-core.md` R31–R33; `tests/README.md`.
