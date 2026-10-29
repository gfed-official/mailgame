# Evidence: godot-host-shell story 004 — boot sequence, error screens, window guard, `BuiltInContent` step

**Story**: `production/epics/godot-host-shell/story-004-boot-sequence-error-screen-content-step.md` · **Type**: Integration
**Date**: 2026-10-21. Every run below is from the final working tree after the provenance rewrite (see the story's Completion Notes, "Provenance") and its `/code-review` fixes; all smokes, the screenshots and the four legs were re-run on that tree.
**Engine used for these runs**: `Godot Engine v4.7.2.stable.mono.official.ed1daf0bf` (`~/.local/bin/godot`). The project pins 4.7.1; the owner approved 4.7.2 for local runs. **4.7.1 behaviour is UNVERIFIED** (including the headless `MinSize` read-back and the Movie Maker frame size below).
**.NET**: SDK 10.0.402 for the builds and test legs (`/home/box/.dotnet`); `smoke.sh` builds the game with `/home/box/.dotnet10`. The Godot process ran on the .NET runtime `10.0.13` (logged as `BOOT env dotnet_version`).
**Procedure**: `bash /workspace/repos/logs/smoke.sh <worktree> <scenario…>` imports the project headless, builds `PerformativeMail.Game.csproj` Debug, runs each `--smoke=<name>` headless and reverts the 4.7.2 csproj rewrite. The boot log below is from a separate direct headless run, so it shows the unfiltered stdout lines.

## Smoke summary (final run, exit code per scenario)

| Scenario | Expected exit | Actual | Result line |
|---|---|---|---|
| `main-tree` | 0 | 0 | `SMOKE RESULT name=main-tree pass=43 fail=0 exit=0` |
| `boot-order` | 0 | 0 | `SMOKE RESULT name=boot-order pass=77 fail=0 exit=0` |
| `boot-failure` | 0 | 0 | `SMOKE RESULT name=boot-failure pass=112 fail=0 exit=0` |
| `boot-failure-exit4` | 4 | 4 | `SMOKE RESULT name=boot-failure-exit4 pass=0 fail=1 exit=4` |
| `main-tree-noboot` | 0 | 0 | `SMOKE RESULT name=main-tree-noboot pass=26 fail=0 exit=0` |
| `screen-boot-error` (headless) | 0 | 0 | `SMOKE RESULT name=screen-boot-error pass=11 fail=0 exit=0` |
| `screen-content-error` (headless) | 0 | 0 | `SMOKE RESULT name=screen-content-error pass=11 fail=0 exit=0` |
| `selftest-needs-boot` | 0 (was 4) | 0 | `SMOKE RESULT name=selftest-needs-boot pass=1 fail=0 exit=0` |
| `frame-order` | 0 | 0 | `SMOKE RESULT name=frame-order pass=50 fail=0 exit=0` |
| `pause-driver-state` | 0 | 0 | `SMOKE RESULT name=pause-driver-state pass=72 fail=0 exit=0` |
| `fault-stop` | 0 | 0 | `SMOKE RESULT name=fault-stop pass=98 fail=0 exit=0` |
| `frame-alloc` | 0 | 0 | `SMOKE RESULT name=frame-alloc pass=1 fail=0 exit=0` |
| `selftest-fail` | 1 | 1 | `SMOKE RESULT name=selftest-fail pass=1 fail=1 exit=1` |
| `selftest-throws` | 1 | 1 | `SMOKE RESULT name=selftest-throws pass=0 fail=1 exit=1` |
| `selftest-empty` | 3 | 3 | `SMOKE RESULT name=selftest-empty pass=0 fail=0 exit=3` |
| `selftest-timeout` | 5 | 5 | `SMOKE RESULT name=selftest-timeout pass=1 fail=1 exit=5` |
| `list` | 0 | 0 | 16 `SMOKE LIST` lines (below) |

The `boot-failure-exit4` FAIL line names the boot failure that produced the exit:
`SMOKE boot-failure-exit4 FAIL boot failed: GameLoop.RequestBoot() returned false (BOOT 4 input FAIL InjectedFaultException: input contributor failed on purpose (boot smoke))`

`--smoke=list` (the new `expect=` values: `selftest-needs-boot` is now 0; the new scenarios are listed):

```
SMOKE LIST boot-failure expect=0
SMOKE LIST boot-failure-exit4 expect=4
SMOKE LIST boot-order expect=0
SMOKE LIST fault-stop expect=0
SMOKE LIST frame-alloc expect=0
SMOKE LIST frame-order expect=0
SMOKE LIST main-tree expect=0
SMOKE LIST main-tree-noboot expect=0
SMOKE LIST pause-driver-state expect=0
SMOKE LIST screen-boot-error expect=0
SMOKE LIST screen-content-error expect=0
SMOKE LIST selftest-empty expect=3
SMOKE LIST selftest-fail expect=1
SMOKE LIST selftest-needs-boot expect=0
SMOKE LIST selftest-throws expect=1
SMOKE LIST selftest-timeout expect=5
```

## Boot log: `boot-order` (direct headless run, raw stdout lines)

Command: `~/.local/bin/godot --headless --path . -- --smoke=boot-order` (exit 0). These are all of the `BOOT` lines plus the engine version line, as printed:

```
Godot Engine v4.7.2.stable.mono.official.ed1daf0bf - https://godotengine.org
BOOT 1 window: min-size set 1280x720 readback 1280x720
BOOT env window_size=1280x720
BOOT env content_scale_mode=CanvasItems
BOOT env content_scale_aspect=KeepHeight
BOOT env sim_asserts_enabled=True
BOOT env is_debug_build=True
BOOT env dotnet_version=10.0.13
BOOT env stopwatch_frequency=1000000000
BOOT env culture=(invariant)
BOOT env boot_thread=1
BOOT 1 window ok
BOOT 2 content: source=BuiltInContent containers=4 items=4 mail_kinds=5 destination_types=1 mail_mixes=1 hash=018735cb load_ms=51.6 (advisory budget 100)
BOOT 2 content ok
BOOT 3 persistence: no contributor
BOOT 3 persistence ok
BOOT 4 input: no contributor
BOOT 4 input ok
BOOT 5 session NOT WIRED (story 005)
BOOT 6 sim NOT WIRED (story 005)
BOOT 7 world NOT WIRED (story 006)
BOOT 8 views NOT WIRED (story 008)
BOOT 9 start ok
SMOKE RESULT name=boot-order pass=77 fail=0 exit=0
```

- **2026-10-28 (mail-destinations story 005): the `hash=018735cb` on the `BOOT 2 content` line above is stale.** The story adds the `sim.startingWalletCents` balance key, which enters the content hash's BalanceBlock, so the M-1a `ContentHash` is now `54158f09` (`54158f0916409080cc6fa0cd55f5ecf8839e83bd98419acbce5fc0f1b73b8244`, `ContentHashTests.GoldenM1aDigest`). `hashSchemaVersion` stays 1 (a value change, not a layout change). The boot log above was not re-run; it records the run as it was.

- **Observed headless `MinSize` read-back: `1280x720`.** `GetWindow().MinSize` read back equal to the value set under `--headless` (DisplayServer `headless`) on 4.7.2. `boot-order` therefore took the "equal" branch: `GetWindow().MinSize read back 1280x720 == 1280x720 (headless=headless)`, and no `BOOT 1 window min-size readback` mismatch line was logged. The windowed (X11) runs below also read back `1280x720`. 4.7.1, Wayland and hiDPI remain UNVERIFIED (ADR-0019 V4).
- The headless window size logged after the guard is `1280x720`, not the project's 1600×900 override. Why the headless DisplayServer reports this size was not investigated (UNVERIFIED), and the value is recorded as observed.
- `Sim.AssertsEnabled == OS.IsDebugBuild()` was asserted, because the smoke ran an unoptimised Debug game build: `SMOKE boot-order PASS Sim.AssertsEnabled == OS.IsDebugBuild() (unoptimised Debug game build) == True`.
- `BOOT env boot_thread` equals `GameLoop.BootThreadId` (1). After `RequestBoot()` `StartPending == True` and the rig driver is `Idle`. One frame later `StartPending == False`, the driver is `Running` and there was 1 run-frame call.
- Content load took 42–52 ms over the runs, under the advisory 100 ms budget (TR-content-028: logged, not gated).
- The NOT WIRED steps at story 004 are `5 session, 6 sim, 7 world, 8 views` (story 011 asserts none remain).

## Failure path: `boot-failure` (screen text per pass, from the SMOKE lines)

```
a-contributor: screen text: The game could not start | Failed at boot step 4 (input). | InjectedFaultException: input contributor failed on purpose (boot smoke)
b-sim-build:   screen text: The game could not start | Failed at boot step 6 (sim). | SimBuildException (code ReservedPhase): system 'SmokeInjected' tried to bind reserved phase 0 (InputsApply) (injected by the smoke harness)
c-content:     screen text: The game content could not be loaded | Failed at boot step 2 (content). | InvalidOperationException: The M-1a built-in content does not validate (injected by the smoke harness): CV-GEN-01 items: duplicate id 'letter_small' CV-MAIL-04 mail_mixes 'm1a_street': kind shares sum to 950 permille, expected 1000
```

Each of passes a–c installs the rig armed (`StartPending == True` before boot, so the latch has to clear it), runs `BootSequence` on a context from the debug-only `GameLoop.BeginBootForSmoke()` and asserts:
- synchronously after the failing step: `IsFaulted`, `StartPending == False`, exactly one `[GameLoop] FAULT` line carrying the full type and the stack, the recorded `BootFailure` (step, name, kind), the `BOOT n name FAIL` line, no line for a later step (for c, no `BOOT 3` line at all), and for c no `ContentSet` in the context;
- one frame later: `BootErrorScreen.FaultsReceived` +1 on the `BootError` sink, the `ContentError` panel never called as a sink, the right panel visible and the other hidden, the sink state (`Shown.BootStep` / `Shown.ContentStep`), the heading, the type/message/code in the message label, the step in the "Failed at boot step n (name)." line, and Quit visible;
- three frames later: run-frame calls still 0, and still faulted.

Pass d first reports a step-0 boot failure on the loop still faulted by c: it is logged as not delivered, the sink is not called, and its announced step is dropped. Then it installs the rig unarmed (`StartPending == False`, so boot step 9 must arm it) and checks a clean boot followed by a frame fault: `BootError` shows "The game stopped after an error" with the frame exception (sink state `Shown.SessionFault`).

Pass e runs the production `RequestBoot()` → `FaultBoot` path on a second `Main.tscn` instance. The debug-only `GameLoop.InstallBootStepsForSmoke` gives it a plan whose step numbers are out of order, so `BootSequence` rejects it before step 1 (`BootSequence.FailBeforeFirstStep`). It asserts that `RequestBoot()` returns false, `IsFaulted` is set, there is one FAULT line and a `BOOT 0 plan FAIL ArgumentException: …` line (which `BootSmokeFaults.StatusLines` reads as `0 plan FAIL`, asserted), and the second call returns false with `boot already ran`. One frame later the instance's own `BootError` shows "Failed at boot step 0 (plan)." with Quit visible and focused, the focus coming through the production `RequestBoot` path.

## Retained screenshots (windowed `--write-movie`, never `--headless`)

Display `DISPLAY=:6` (X11). Vulkan was unavailable, and Godot fell back to OpenGL 3 by itself (`OpenGL API 4.5 Mesa 25.0.7 llvmpipe`).

Movie Maker takes its frame size from the window at startup. That size comes from the project's `window_*_override` (1600×900), and Movie Maker ignores both `--resolution` and a later window resize. A first capture therefore came out at 1600×900. The retained 1280×720 frames were captured with a temporary `override.cfg` in the project root, which was deleted after the runs (TD-128 item 4):

```bash
printf '[display]\n\nwindow/size/window_width_override=1280\nwindow/size/window_height_override=720\n' > override.cfg
~/.local/bin/godot --path . --windowed --resolution 1280x720 --write-movie /tmp/ghs004-shots/boot-error.png --quit-after 120 -- --smoke=screen-boot-error
~/.local/bin/godot --path . --windowed --resolution 1280x720 --write-movie /tmp/ghs004-shots/content-error.png --quit-after 120 -- --smoke=screen-content-error
rm -f override.cfg
cp /tmp/ghs004-shots/boot-error00000059.png    production/qa/evidence/godot-host-shell-story-004/01-boot-error.png
cp /tmp/ghs004-shots/content-error00000059.png production/qa/evidence/godot-host-shell-story-004/02-content-error.png
git checkout -- PerformativeMail.Game.csproj && rm -f PerformativeMail.Game.csproj.old
```

Both runs exited 0 with `pass=12 fail=0`. The extra windowed assertion over the headless runs (11) is the window size at capture, `(1280, 720)`. Both PNGs are 1280×720 and are the last frame (00000059).

- `production/qa/evidence/godot-host-shell-story-004/01-boot-error.png`
  Run result: OBSERVED — `Main/Screens/BootError` at 1280×720 on a dark background shows:
  - the heading "The game could not start";
  - the line "Failed at boot step 6 (sim)." in amber;
  - the message "SimBuildException (code ReservedPhase): system 'SmokeInjected' tried to bind reserved phase 0 (InputsApply) (injected by the smoke harness)", word-wrapped onto two lines inside the margins;
  - a "Quit" button bottom-left with the focus outline (Quit focus also asserted).

  The exception type, the code name, the message and Quit are all visible, and no text is clipped.
- `production/qa/evidence/godot-host-shell-story-004/02-content-error.png`
  Run result: OBSERVED — `Main/Screens/ContentError` at 1280×720 shows:
  - the heading "The game content could not be loaded";
  - the line "Failed at boot step 2 (content)." in amber;
  - the message "InvalidOperationException: The M-1a built-in content does not validate (injected by the smoke harness):" wrapped to two lines, followed by the two findings lines "CV-GEN-01 items: duplicate id 'letter_small'" and "CV-MAIL-04 mail_mixes 'm1a_street': kind shares sum to 950 permille, expected 1000";
  - a "Quit" button with the focus outline.

  The exception type, the message and Quit are visible, there is no code name (not a `SimBuildException`), no text is clipped, and `BootError` is not shown.

## Unit tests

`tests/unit/boot/BootSequenceTests.cs` (29 tests; trait Ids `GHS-004-AC1..AC6`, `TR-sim-007`, `TR-content-021`, `CD21`) runs the engine-free boot core: every `src/Game/Boot/*.cs` except `GodotBootWindow.cs` is compiled into the xUnit project. A mutation that made the slot filter run every contributor was caught by 3 tests and then reverted (first implementation; the slot filter was not touched by the rewrite).

Four legs (`net10.0`/`net8.0` × Debug/Release, same command as CI):

| Leg | Total | Passed | Skipped | Failed |
|---|---|---|---|---|
| net10.0 Debug | 4900 | 4858 | 42 | 0 |
| net10.0 Release | 4900 | 4898 | 2 | 0 |
| net8.0 Debug | 4900 | 4858 | 42 | 0 |
| net8.0 Release | 4900 | 4898 | 2 | 0 |

All builds had 0 warnings. The new tests add no skips.

## tools/ci guards (final tree)

All PASS:
- `check-no-scenetree-paused` (+ `--self-check`)
- `check-build-config`
- `check-gdignore` (+ `--self-check`)
- `check-scene-rules` (+ `--self-check`)
- `check-analyzer-fixtures` (+ `--self-check`, after net10 Debug)
- `check-sim-access` (+ `--self-check`, run by hand)
- `dotnet build PerformativeMail.Game.sln` for Debug and ExportRelease
- `check-game-build`

`grep -rn "using Godot" src/PerformativeMail.Sim src/PerformativeMail.Client.Core` returns nothing.
