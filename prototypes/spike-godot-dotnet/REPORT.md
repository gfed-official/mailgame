# Spike — Godot 4.7.1 .NET ↔ net8.0 Sim (OQ1 / OQ4 / Q-ARCH-02 / Q-ARCH-03)

- **Date:** 2026-10-16
- **Verdict:** **PASS** (editor/headless path). Export path: partially verified (see §6).
- **Feeds:** ADR-0001 (solution layout, build & test), ADR-0002 (tick driver & main thread).
- **Throwaway:** nothing in this folder is production code. The stub `SpikeTickDriver` is not the ADR-0002 implementation.

## 1. Environment (exact versions)

| Item | Value | How obtained |
|---|---|---|
| Godot | `4.7.1.stable.mono.official.a13da4feb` | `godot-mono --version` |
| Godot binary | `~/.local/bin/godot-mono` → `~/.local/opt/godot-4.7.1-mono/Godot_v4.7.1-stable_mono_linux_x86_64/Godot_v4.7.1-stable_mono_linux.x86_64` | `ls -la` |
| Bundled SDK packages | `Godot.NET.Sdk.4.7.1`, `GodotSharp.4.7.1`, `GodotSharpEditor.4.7.1`, `Godot.SourceGenerators.4.7.1` under `.../GodotSharp/Tools/nupkgs/` | `ls` |
| .NET SDK | 10.0.402 (mise) | `mise exec dotnet@10.0.402 -- dotnet --list-sdks` |
| .NET runtimes installed | **Microsoft.NETCore.App 10.0.13 only** (no 8.x) | `dotnet --list-runtimes` |
| OS | Linux x86_64 (Omarchy) | — |
| Export templates | **none installed** (`~/.local/share/godot/export_templates/` empty) | `ls` |

## 2. Project shape

```
prototypes/spike-godot-dotnet/
  project.godot              own project (NOT the repo root's); [dotnet] project/assembly_name="SpikeGodotDotnet"
  SpikeGodotDotnet.csproj    Sdk="Godot.NET.Sdk/4.7.1", net8.0, EnableDynamicLoading,
                             ProjectReference ../../src/PerformativeMail.Sim, Compile Remove="excluded/**"
  SpikeGodotDotnet.sln       dotnet new sln + dotnet sln add (also pulled in the Sim project)
  nuget.config               local source = Godot's bundled nupkgs folder
  Main.tscn                  one Node "GameLoop" with GameLoop.cs
  GameLoop.cs                _Process → SpikeTickDriver.Advance(Stopwatch.GetTimestamp(), Stopwatch.Frequency); prints probes; quits after 5 s
  SpikeTickDriver.cs         integer accumulator stub: 30 Hz, ≤4 ticks/frame, drop-not-bank, Δ clamp 1 s
  excluded/Bad.cs            deliberately not C#; proves the glob exclusion
  build-config-check/        candidate Directory.Build.props/.targets mapping (§5 F6)
  logs/                      raw output of the build and run commands below
```

The repo-root `Directory.Build.props` is **inherited** (MSBuild walks up), on purpose, to test Q-ARCH-03.

## 3. Commands (exact) and results

| # | Command (from `prototypes/spike-godot-dotnet/`) | Result |
|---|---|---|
| C1 | `mise exec dotnet@10.0.402 -- bash -c '~/.local/bin/godot-mono --headless --path . --build-solutions --quit'` with **no csproj present** | Editor starts, scans, quits. **No csproj or sln generated.** 4.7.1's `--build-solutions` builds an existing solution only; it does not create one. |
| C2 | `mise exec dotnet@10.0.402 -- dotnet build SpikeGodotDotnet.sln` (first attempt) | **FAIL** `CS0104: 'Environment' is an ambiguous reference between 'Godot.Environment' and 'System.Environment'` ×3. Cause: `ImplicitUsings` (root props) + `using Godot;`. Fixed by writing `System.Environment`. |
| C3 | same, after the fix | **PASS** — 0 warnings, 0 errors, with the root props' `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` in force. Restore of `Godot.NET.Sdk/4.7.1` came from the bundled nupkgs. Output → `.godot/mono/temp/bin/Debug/` (`GodotSharp.dll`, `GodotSharpEditor.dll`, `PerformativeMail.Sim.dll`, `SpikeGodotDotnet.dll`, `.runtimeconfig.json`). |
| C4 | `mise exec dotnet@10.0.402 -- bash -c '~/.local/bin/godot-mono --headless --path . --build-solutions --quit'` (csproj present, `.godot/mono/temp` deleted first) | **PASS** — `dotnet_build_project … DONE`, exit 0 (`logs/build-solutions.log`). Godot needs `dotnet` on `PATH`; `mise exec` provides it. |
| C5 | `mise exec dotnet@10.0.402 -- bash -c '~/.local/bin/godot-mono --headless --path . --quit-after 100000 -- --maxfps=N'` for N = 0 (uncapped), 60, 20, 5 | **PASS** — all exit 0 (`logs/run-maxfps-*.log`). The node quits itself after 5 s of `Stopwatch` time; `--quit-after` is only a backstop. |
| C6 | `mise exec dotnet@10.0.402 -- dotnet publish SpikeGodotDotnet.csproj -c ExportRelease -r linux-x64 --self-contained true -o /tmp/spike-publish` | **PASS** — self-contained output bundles `System.Private.CoreLib.dll`, `libcoreclr.so`, and `runtimeconfig.json` with `includedFrameworks: Microsoft.NETCore.App 8.0.32` and `System.Globalization.Invariant: true`. (This approximates what Godot's exporter runs; see §6.) |
| C7 | `mise exec dotnet@10.0.402 -- dotnet msbuild src/PerformativeMail.Sim/PerformativeMail.Sim.csproj -p:Configuration={Debug,Release,ExportDebug,ExportRelease} -getProperty:Optimize -getProperty:DefineConstants` (repo root) | `ExportDebug` and `ExportRelease` give **`Optimize` = empty** (unoptimised), defines `EXPORTDEBUG` / `EXPORTRELEASE`, no `DEBUG`/`RELEASE`. |
| C8 | C7 plus `-p:DirectoryBuildPropsPath=<build-config-check props> -p:DirectoryBuildTargetsPath=<build-config-check targets>` | Debug: Optimize=false, `PM_SIM_ASSERTS` ✓ · Release: Optimize=true, no asserts ✓ · ExportDebug: Optimize=false, `PM_SIM_ASSERTS` ✓ · ExportRelease: Optimize=true, no asserts ✓. A first attempt computing the assert switch in `.props` was wrong for `Release` (the SDK sets `Optimize` after `Directory.Build.props`), so it lives in `.targets`. |

| C9 | `mise exec dotnet@10.0.402 -- bash -c '~/.local/bin/godot-mono --headless --path . --quit-after 100000 -- --maxfps=60 --pausetest'` (node sets `ProcessMode = Always`, then `GetTree().Paused = true`) | **PASS** — `_Process` keeps running with `SceneTree.Paused=True`: 150 ticks in 5.002 s (`logs/run-pausetest.log`). |
| C10 | `mise exec dotnet@10.0.402 -- dotnet build SpikeGodotDotnet.sln -c ExportRelease` (sln made by `dotnet new sln`, Debug/Release only) | **FAIL (expected, informative)** — `MSB4126: The specified solution configuration "ExportRelease\|Any CPU" is invalid`. `dotnet sln add` also maps the Godot project's `Release` row to `Debug` (it has no `Release` configuration). |
| C11 | C4 with `SpikeGodotDotnet.sln` moved away | **PASS** — Godot builds the **csproj** directly (`dotnet_build_project … DONE`); no sln is needed or generated. |
| C12 | C8 re-run with the **exact** ADR-0001 §4 `.targets` text (`build-config-check/Directory.Build.targets.adr0001`) for Debug, Release, ExportDebug, ExportRelease and `Release -p:SimAsserts=true`; plus `dotnet build -c ExportRelease` of the Sim | **PASS** — Debug/ExportDebug: Optimize=false, `SIM_ASSERT;INVENTORY_ASSERT`, checked=true · Release/ExportRelease: Optimize=true, no assert symbols, checked=false · ReleaseAssert: Optimize=true, asserts + checked on · build 0 errors. |

## 4. Runtime probes (C5, uncapped run, verbatim)

```
[spike] Engine: 4.7.1-stable (official)
[spike] RuntimeInformation.FrameworkDescription: .NET 10.0.13
[spike] System.Environment.Version: 10.0.13
[spike] System.Private.CoreLib location: /home/evan/.local/share/mise/dotnet-root/shared/Microsoft.NETCore.App/10.0.13/System.Private.CoreLib.dll
[spike] Sim type load: PerformativeMail.Sim.SimAssembly from PerformativeMail.Sim v1.0.0.0; SimAssembly.Name=PerformativeMail.Sim
[spike] Sim assembly location:
[spike] Sim assembly target: .NETCoreApp,Version=v8.0
[spike] CurrentCulture='en-US' InvariantMode=False
[spike] Stopwatch.Frequency=1000000000 IsHighResolution=True
[spike] Engine.MaxFps=0 PhysicsTicksPerSecond=60 vsync=Enabled
[spike] main managed thread id=1
[spike] elapsed=5.005s frames=728 avgFps=145.5
[spike] _Process delta: avg=6.941ms min=0.861ms max=39.578ms; Stopwatch avg=6.875ms
[spike] ticks run=150 dropped=0 expected~=150.2 maxTicksInOneFrame=1 framesWithZeroTicks=578
[spike] _Process always on main managed thread: True
```

Timing at capped frame rates (5 s each):

| maxfps | frames | `_Process` delta avg / min / max (ms) | Stopwatch avg (ms) | ticks run | dropped | max ticks in 1 frame |
|---|---|---|---|---|---|---|
| 0 (uncapped) | 728 | 6.941 / 0.861 / 39.578 | 6.875 | 150 | 0 | 1 |
| 60 | 303 | 16.668 / 2.473 / 39.697 | 16.509 | 150 | 0 | 1 |
| 20 | 103 | 48.957 / 2.447 / 57.834 | 48.568 | 150 | 0 | 2 |
| 5 | 28 | 125.745 / 16.667 / 139.758 | **185.649** | 104 | **51** | **4** |

## 5. Findings

| ID | Finding | Status |
|---|---|---|
| F1 | A `Godot.NET.Sdk/4.7.1` net8.0 game csproj builds against the net8.0, Godot-free `PerformativeMail.Sim` via `ProjectReference` — via both `dotnet build` and `godot-mono --build-solutions`. The linked-folder fallback is **not needed**. | **VERIFIED** |
| F2 | Sim types load and execute at runtime inside Godot (`SimAssembly.Name` read; `TargetFramework` = net8.0). | **VERIFIED** |
| F3 | Editor / headless runs execute on **the newest installed .NET runtime** (here 10.0.13), even though the game's runtimeconfig says net8.0 + `LatestMinor`. Cause: Godot hosts .NET through `GodotSharp/Api/{Debug,Release}/GodotPlugins.runtimeconfig.json`, which says `rollForward: LatestMajor`. The game's own runtimeconfig is not the host config. | **VERIFIED** (mechanism inferred from the shipped runtimeconfig files) |
| F4 | Because of F3, the game runtimeconfig's `System.Globalization.Invariant` is **not in force** in editor/headless runs (`CurrentCulture='en-US'`). The Sim must stay culture-free by API choice and analyzer, not by `InvariantGlobalization`. | **VERIFIED** |
| F5 | A self-contained `ExportRelease` publish bundles **.NET 8.0.32** with Invariant on. Shipped builds therefore probably run .NET 8 while tests and the editor run .NET 10: integer Sim results must not differ, but perf numbers from tests/editor do not transfer. | **PARTLY VERIFIED** — the publish output is verified; that Godot's exporter runs this exact publish is UNVERIFIED (no export templates) |
| F6 | Godot's configurations are `Debug;ExportDebug;ExportRelease` (`Sdk.props`). A referenced plain-SDK project built under `ExportDebug`/`ExportRelease` gets **empty `Optimize`** and no `DEBUG`/`RELEASE`. Any `Configuration == 'Release'` or `#if RELEASE` / `#if DEBUG` check is wrong in exports. The fix is the props/targets pair in `build-config-check/` (C8). | **VERIFIED** |
| F7 | Root `Directory.Build.props` (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `ImplicitUsings`, `Nullable`) builds the Godot project cleanly. Godot's source generators emit no warnings. One gotcha: `Environment` (and probably other names such as `Range`/`Timer`) clash between `System` and `Godot` under `ImplicitUsings`. | **VERIFIED** (Q-ARCH-03 closed: no per-project condition needed) |
| F8 | `<Compile Remove="excluded/**" />` keeps a non-compiling `.cs` under the Godot project dir out of the build. The root game csproj needs `src/**;tests/**;prototypes/**`. | **VERIFIED** |
| F9 | `_Process` runs on managed thread 1 every frame (main thread). | **VERIFIED** |
| F10 | The integer accumulator fed by `Stopwatch.GetTimestamp()` / `Stopwatch.Frequency` (1 GHz on Linux, high-res) gives exactly 30 Hz: 150 ticks in 5.00 s at 145, 60 and 20 fps; ≤4 ticks/frame + drop-not-bank engages at 5 fps (104 run, 51 dropped). | **VERIFIED** |
| F11 | Godot's `_Process(delta)` **under-reports wall time under slow frames**: at 5 fps, delta averaged 125.7 ms vs 185.6 ms measured by `Stopwatch`. The accumulator must use a monotonic timestamp, never summed `delta`. | **VERIFIED** (cause — delta smoothing or a clamp — not investigated) |
| F12 | Headless uncapped runs at ~145 fps with `vsync=Enabled` reported (the dummy driver does not vsync). First frames show a ~40 ms hitch (`max` column). | **VERIFIED** (observation) |
| F13 | `Assembly.Location` is empty for the game and the Sim inside Godot: they are loaded from bytes into Godot's load context. Never depend on `Assembly.Location` / file-relative paths in Sim or Client.Core. | **VERIFIED** (observation); the load-context type is UNVERIFIED |
| F15 | `ProcessMode = Always` on the `GameLoop` node keeps `_Process` running while `SceneTree.Paused` is true (C9). The defensive setting works; the Sim never relies on the tree pause either way. | **VERIFIED** |
| F16 | Godot's editor build targets `<assembly_name>.csproj`, not a solution (C11). A solution that includes the Godot project needs `ExportDebug`/`ExportRelease` solution configurations or `-c Export*` fails (C10). The Godot-free `PerformativeMail.sln` can therefore stay Godot-free. | **VERIFIED** (editor build); the exporter's target (csproj vs sln) is UNVERIFIED |
| F14 | `Godot.NET.Sdk` 4.7.1 has an `EnableGodotDotNetPreview` switch (a new "Godot .NET" bindings package, alternative to GodotSharp). Not used; noted for ADR-0019. | Observation |

## 6. Not verified here

- **Export:** no export templates were installed, so no `--export-release` run. Which runtime an exported game loads (F5) and whether the export honours the Invariant flag remain **UNVERIFIED** until a template-backed export is run (ADR-0019 / first export story).
- **Windows/macOS:** Linux only.
- **Godot's internal delta handling (F11 cause)** and `application/run/delta_smoothing` effects: not investigated; ADR-0002 uses `Stopwatch`, so it is not load-bearing.
- **Hot reload** (editor assembly reload with a referenced project): not exercised.
- **gdUnit4:** not installed.

## 7. Reproduce

```bash
cd prototypes/spike-godot-dotnet
mise exec dotnet@10.0.402 -- bash -c '~/.local/bin/godot-mono --headless --path . --build-solutions --quit'
mise exec dotnet@10.0.402 -- bash -c '~/.local/bin/godot-mono --headless --path . --quit-after 100000 -- --maxfps=5'
```
