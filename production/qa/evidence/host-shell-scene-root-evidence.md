# Evidence — godot-host-shell story 002: `Main.tscn` scene root, ADR-0019 node tree, explicit project settings, headless smoke harness

**Date**: 2026-10-17 (re-captured after the LP-CODE-REVIEW / QL-TEST-COVERAGE fixes) · **Story**: `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` · **Type**: Integration
**Machine**: Linux x86_64; Godot `4.7.1.stable.mono.official` (`~/.local/bin/godot-mono`), .NET SDK 10.0.402 (mise). Every run below ends with an `exit=<n>` line appended from the shell's `$?` (the process exit code); the `SMOKE RESULT … exit=<n>` line is the harness's own, authoritative code.

## Builds / editor pass

| Command | Result |
|---|---|
| `mise exec dotnet@10.0.402 -- dotnet build PerformativeMail.Game.csproj -c Debug` | exit 0, 0 warnings |
| `godot-mono --headless --editor --path . --quit` (assigns `.uid` sidecars; `Main.tscn` not rewritten) | exit 0 |
| `mise exec dotnet@10.0.402 -- dotnet build PerformativeMail.sln -c Release` (Godot-free solution) | exit 0, 0 warnings |
| `godot-mono --headless --path . --quit-after 3` (no `--smoke`, `AutoBoot` default) | prints `[GameLoop] boot not wired (story 004)`, exit 0 |

## Smoke runs (one per discovered scenario; exits 0, 1, 3, 4, 5 and the `list` mode all executed)

## `mise exec dotnet@10.0.402 -- bash -c 'cd <repo> && ~/.local/bin/godot-mono --headless --path . -- --smoke=list'` (expected process exit 0)

```

SMOKE LIST main-tree expect=0
SMOKE LIST selftest-empty expect=3
SMOKE LIST selftest-fail expect=1
SMOKE LIST selftest-needs-boot expect=4
SMOKE LIST selftest-throws expect=1
SMOKE LIST selftest-timeout expect=5
exit=0
```

## `mise exec dotnet@10.0.402 -- bash -c 'cd <repo> && ~/.local/bin/godot-mono --headless --path . -- --smoke=main-tree'` (expected process exit 0)

```

SMOKE main-tree PASS Input exists as Node
SMOKE main-tree PASS World exists as WorldPresenter
SMOKE main-tree PASS World/Terrain exists as Node3D
SMOKE main-tree PASS World/Destinations exists as Node3D
SMOKE main-tree PASS World/Entities exists as Node3D
SMOKE main-tree PASS World/Flights exists as Node3D
SMOKE main-tree PASS World/Props exists as Node3D
SMOKE main-tree PASS World/CameraRig exists as Node3D
SMOKE main-tree PASS World/CameraRig/Boom exists as Node3D
SMOKE main-tree PASS World/CameraRig/Boom/Camera3D exists as Camera3D
SMOKE main-tree PASS Hud exists as HudPresenter
SMOKE main-tree PASS Audio exists as AudioPresenter
SMOKE main-tree PASS Screens exists as Node
SMOKE main-tree PASS Debug CanvasLayer present=True matches OS.IsDebugBuild()=True
SMOKE main-tree PASS Main child order is Input,World,Hud,Audio,Screens,Debug
SMOKE main-tree PASS Input is child index 0 of Main
SMOKE main-tree PASS Main node name == Main
SMOKE main-tree PASS Main is a direct child of the SceneTree root
SMOKE main-tree PASS exactly one GameLoop in the tree
SMOKE main-tree PASS Main.ProcessMode == Always
SMOKE main-tree PASS Main.ProcessPriority == -2147483648
SMOKE main-tree PASS Main.ProcessThreadGroup == Inherit
SMOKE main-tree PASS World presenter IsInitialized == False
SMOKE main-tree PASS Hud presenter IsInitialized == False
SMOKE main-tree PASS Audio presenter IsInitialized == False
SMOKE main-tree PASS setting application/run/main_scene == res://src/Game/Main.tscn
SMOKE main-tree PASS setting application/run/low_processor_mode == False
SMOKE main-tree PASS setting display/window/size/viewport_width == 1920
SMOKE main-tree PASS setting display/window/size/viewport_height == 1080
SMOKE main-tree PASS setting display/window/size/window_width_override == 1600
SMOKE main-tree PASS setting display/window/size/window_height_override == 900
SMOKE main-tree PASS setting display/window/stretch/mode == canvas_items
SMOKE main-tree PASS setting display/window/stretch/aspect == keep_height
SMOKE main-tree PASS setting physics/common/physics_interpolation == False
SMOKE main-tree PASS root ContentScaleMode == CanvasItems
SMOKE main-tree PASS root ContentScaleAspect == KeepHeight
SMOKE main-tree PASS root ContentScaleSize == (1920, 1080)
SMOKE main-tree PASS OS.LowProcessorUsageMode == False
SMOKE main-tree PASS no autoload/* settings among 952 ProjectSettings properties
SMOKE RESULT name=main-tree pass=39 fail=0 exit=0
exit=0
```

## `mise exec dotnet@10.0.402 -- bash -c 'cd <repo> && ~/.local/bin/godot-mono --headless --path . -- --smoke=selftest-fail'` (expected process exit 1)

```

SMOKE selftest-fail PASS first assertion passes
SMOKE selftest-fail FAIL second assertion fails on purpose
SMOKE RESULT name=selftest-fail pass=1 fail=1 exit=1
exit=1
```

## `mise exec dotnet@10.0.402 -- bash -c 'cd <repo> && ~/.local/bin/godot-mono --headless --path . -- --smoke=selftest-throws'` (expected process exit 1)

```

SMOKE selftest-throws FAIL InvalidOperationException: selftest-throws threw on purpose
SMOKE RESULT name=selftest-throws pass=0 fail=1 exit=1
exit=1
```

## `mise exec dotnet@10.0.402 -- bash -c 'cd <repo> && ~/.local/bin/godot-mono --headless --path . -- --smoke=selftest-empty'` (expected process exit 3)

```

SMOKE RESULT name=selftest-empty pass=0 fail=0 exit=3
exit=3
```

## `mise exec dotnet@10.0.402 -- bash -c 'cd <repo> && ~/.local/bin/godot-mono --headless --path . -- --smoke=selftest-needs-boot'` (expected process exit 4)

```

[GameLoop] boot not wired (story 004)
SMOKE selftest-needs-boot FAIL boot failed: GameLoop.RequestBoot() returned false
SMOKE RESULT name=selftest-needs-boot pass=0 fail=1 exit=4
exit=4
```

## `mise exec dotnet@10.0.402 -- bash -c 'cd <repo> && ~/.local/bin/godot-mono --headless --path . -- --smoke=selftest-timeout'` (expected process exit 5)

```

SMOKE selftest-timeout PASS started
SMOKE selftest-timeout FAIL timeout after 0.5s
SMOKE RESULT name=selftest-timeout pass=1 fail=1 exit=5
exit=5
```

## `--smoke=does-not-exist` (unknown scenario → exit 2; first capture)

```
SMOKE RESULT name=does-not-exist pass=0 fail=0 exit=2
```

## `tools/ci/check-scene-rules.sh --self-check` (exit 0)

```
PASS  self-check: clean fake layout accepted
PASS  self-check: planted violation detected by R1: [autoload] section
PASS  self-check: planted violation detected by R1: autoload/ key (top level)
PASS  self-check: planted violation detected by R2: ChangeSceneToFile in .cs
PASS  self-check: planted violation detected by R2: ChangeSceneToPacked in .cs
PASS  self-check: planted violation detected by R2: change_scene_to_file in .gd
PASS  self-check: planted violation detected by R3: low_processor_mode=true
PASS  self-check: planted violation detected by R4: physics_interpolation=true
PASS  self-check: planted violation detected by R5: _PhysicsProcess override
PASS  self-check: planted violation detected by R6: GetTree().Paused = true
PASS  self-check: planted violation detected by R6: get_tree().paused = true (.gd)
PASS  self-check: planted violation detected by R7: wrong main_scene
PASS  self-check: planted violation detected by R7: main_scene absent
PASS  self-check: planted violation detected by R7: project.godot absent
PASS  self-check: planted violation detected by R8a: second scene referencing GameLoop.cs
PASS  self-check: planted violation detected by R8a: no scene referencing GameLoop.cs
PASS  self-check: planted violation detected by R8b: second partial class GameLoop
PASS  self-check: planted violation detected by R8b: no partial class GameLoop
PASS  self-check: planted violation detected by R3: flat-form low_processor_mode=true (top level)
PASS  self-check: planted violation detected by R7: flat-form main_scene wrong + section key removed
PASS  self-check: planted violation detected by R9: presenter GetNode in _Ready
PASS  self-check: planted violation detected by R9: view GetTree in _Ready
PASS  self-check: planted violation detected by R1: [autoload_prepend] section
PASS  self-check: planted violation detected by R2: src/Game/ absent
check-scene-rules --self-check: clean layout accepted, 23 planted violations detected
```

## `tools/ci/check-scene-rules.sh` (exit 0)

```
PASS  [R1] project.godot has no [autoload] section and no autoload/ keys
PASS  [R2] no ChangeSceneToFile/ChangeSceneToPacked under src/Game/
PASS  [R3] application/run/low_processor_mode is not true
PASS  [R4] physics/common/physics_interpolation is not true
PASS  [R5] no _PhysicsProcess override under src/Game/
PASS  [R6] no SceneTree.Paused assignment under src/Game/ (ADR-0002)
PASS  [R7] application/run/main_scene="res://src/Game/Main.tscn"
PASS  [R8a] exactly one scene under src/Game/ references GameLoop.cs (src/Game/Main.tscn)
PASS  [R8b] exactly one .cs under src/Game/ declares partial class GameLoop (src/Game/Hosting/GameLoop.cs)
PASS  [R9] no node/tree lookups in a presenter or view _Ready (inert until Initialize(deps))
check-scene-rules: checked 10 rules
```

## Not evidenced here (deferred, see TD entry in the story's Completion Notes)
- Non-debug build behaviour (`--smoke` ignored; `Debug` CanvasLayer stripped) — needs an exported release build (first export story / story 011).
- `AutoBoot = false` instantiate-and-add seam and "starts no tick" — meaningful only once stories 003/004 wire the tick and boot.
- The headless smoke run in CI — story 011 (the `check-scene-rules.sh` step is already in the `game-build` job).

**Gate results (2026-10-17):** LP-CODE-REVIEW CONCERNS (no blocking; 1/2/3/4/7 + part of 6 fixed in-story) · QL-TEST-COVERAGE GAPS (none critical; 1, 2 (R9 rule), 5a/5b/5c/5e/5f fixed in-story; 3/4/5d deferred).
