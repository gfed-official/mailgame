# Evidence: godot-host-shell story 003 — `GameLoop` frame loop, clock, pause, fault stop

**Story**: `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md`  
**Date**: 2026-10-20  
**Engine used for these runs**: Godot **4.7.2**.stable.mono.official.ed1daf0bf (`~/.local/bin/godot`, headless). The project pins 4.7.1; the owner approved 4.7.2 for local runs. 4.7.1 behaviour is UNVERIFIED.  
**.NET SDK**: 10.0.402 (`~/.dotnet10`)  
**Procedure**: `/workspace/repos/logs/smoke.sh <worktree> <scenario...>` imports the project, builds `PerformativeMail.Game.csproj` Debug, runs each `--smoke=<name>` headless, then reverts the Godot rewrite of the csproj. The full SMOKE lines below come from direct `godot --headless --path . -- --smoke=<name>` runs of the same build, because smoke.sh shows only the first 60 lines per scenario.

## Smoke summary (exit code per scenario)

| Scenario | Expected exit | Actual | Result line |
|---|---|---|---|
| `list` | 0 | 0 | 10 `SMOKE LIST` lines (below) |
| `main-tree` | 0 | 0 | `SMOKE RESULT name=main-tree pass=39 fail=0 exit=0` |
| `selftest-fail` | 1 | 1 | `SMOKE RESULT name=selftest-fail pass=1 fail=1 exit=1` |
| `selftest-throws` | 1 | 1 | `SMOKE RESULT name=selftest-throws pass=0 fail=1 exit=1` |
| `selftest-empty` | 3 | 3 | `SMOKE RESULT name=selftest-empty pass=0 fail=0 exit=3` |
| `selftest-needs-boot` | 4 | 4 | `SMOKE RESULT name=selftest-needs-boot pass=0 fail=1 exit=4` |
| `selftest-timeout` | 5 | 5 | `SMOKE RESULT name=selftest-timeout pass=1 fail=1 exit=5` |
| `frame-order` | 0 | 0 | `SMOKE RESULT name=frame-order pass=50 fail=0 exit=0` |
| `pause-driver-state` | 0 | 0 | `SMOKE RESULT name=pause-driver-state pass=63 fail=0 exit=0 (direct run)` |
| `fault-stop` | 0 | 0 | `SMOKE RESULT name=fault-stop pass=83 fail=0 exit=0 (direct run)` |
| `frame-alloc` | 0 | 0 | `SMOKE RESULT name=frame-alloc pass=1 fail=0 exit=0` |

Non-zero exits for `selftest-*` are the designed outcomes (each declares its `ExpectedExit`).

### smoke.sh output (final run)

```
import exit=0
game build exit=0 (0 error lines)
== --smoke=list
SMOKE LIST fault-stop expect=0
SMOKE LIST frame-alloc expect=0
SMOKE LIST frame-order expect=0
SMOKE LIST main-tree expect=0
SMOKE LIST pause-driver-state expect=0
SMOKE LIST selftest-empty expect=3
SMOKE LIST selftest-fail expect=1
SMOKE LIST selftest-needs-boot expect=4
SMOKE LIST selftest-throws expect=1
SMOKE LIST selftest-timeout expect=5
exit=0
== --smoke=main-tree
SMOKE RESULT name=main-tree pass=39 fail=0 exit=0
exit=0
== --smoke=selftest-fail
SMOKE RESULT name=selftest-fail pass=1 fail=1 exit=1
exit=1
== --smoke=selftest-throws
SMOKE RESULT name=selftest-throws pass=0 fail=1 exit=1
exit=1
== --smoke=selftest-empty
SMOKE RESULT name=selftest-empty pass=0 fail=0 exit=3
exit=3
== --smoke=selftest-needs-boot
SMOKE RESULT name=selftest-needs-boot pass=0 fail=1 exit=4
exit=4
== --smoke=selftest-timeout
SMOKE RESULT name=selftest-timeout pass=1 fail=1 exit=5
exit=5
== --smoke=frame-order
SMOKE RESULT name=frame-order pass=50 fail=0 exit=0
exit=0
== --smoke=pause-driver-state
exit=0
== --smoke=fault-stop
exit=0
== --smoke=frame-alloc
SMOKE RESULT name=frame-alloc pass=1 fail=0 exit=0
exit=0
```

## `frame-order` — AC1, AC2, AC3 (51 SMOKE lines)

```
SMOKE frame-order PASS GameLoop.ProcessMode == Always
SMOKE frame-order PASS GameLoop.ProcessPriority == -2147483648
SMOKE frame-order PASS GameLoop.HasSequence before install == False
SMOKE frame-order PASS GameLoop.BootThreadId (recorded in _Ready) == this main thread == 1
SMOKE frame-order PASS RegisterPreFrameHook with no sequence installed: threw InvalidOperationException (No frame sequence is installed (story 004/005).)
SMOKE frame-order PASS hooks registered through GameLoop (pre,post) == 2,2
SMOKE frame-order PASS StartPending after install without arming == False
SMOKE frame-order PASS frame 1 (not armed): order 11,12,20,30,40,51,52
SMOKE frame-order PASS frame 1 (not armed): run-frame calls this frame == 1
SMOKE frame-order PASS frame 1 (not armed): ticks == 0
SMOKE frame-order PASS frame 1 (not armed): Present alpha 0 == driver.AlphaPermille, in 0..999
SMOKE frame-order PASS frame 1 (not armed): loop not faulted == False
SMOKE frame-order PASS driver state before ArmStart == Idle
SMOKE frame-order PASS StartPending after ArmStart == True
SMOKE frame-order PASS driver state after ArmStart, before the next _Process == Idle
SMOKE frame-order PASS frame 2 (first armed frame, 0 ticks): order 11,12,20,30,40,51,52
SMOKE frame-order PASS frame 2 (first armed frame, 0 ticks): run-frame calls this frame == 1
SMOKE frame-order PASS frame 2 (first armed frame, 0 ticks): ticks == 0
SMOKE frame-order PASS frame 2 (first armed frame, 0 ticks): Present alpha 0 == driver.AlphaPermille, in 0..999
SMOKE frame-order PASS frame 2 (first armed frame, 0 ticks): loop not faulted == False
SMOKE frame-order PASS driver state after the first armed _Process == Running
SMOKE frame-order PASS StartPending after the first armed _Process == False
SMOKE frame-order PASS RegisterPreFrameHook after the first frame: threw InvalidOperationException (Frame hooks must be registered before the first frame runs.)
SMOKE frame-order PASS RegisterPostPresentHook after the first frame: threw InvalidOperationException (Frame hooks must be registered before the first frame runs.)
SMOKE frame-order PASS frame 3 (3 catch-up ticks, after a repeated ArmStart): order 11,12,20,30,40,51,52
SMOKE frame-order PASS frame 3 (3 catch-up ticks, after a repeated ArmStart): run-frame calls this frame == 1
SMOKE frame-order PASS frame 3 (3 catch-up ticks, after a repeated ArmStart): ticks == 3
SMOKE frame-order PASS frame 3 (3 catch-up ticks, after a repeated ArmStart): Present alpha 0 == driver.AlphaPermille, in 0..999
SMOKE frame-order PASS frame 3 (3 catch-up ticks, after a repeated ArmStart): loop not faulted == False
SMOKE frame-order PASS StartPending after the repeated ArmStart's frame == False
SMOKE frame-order PASS frame 4 (1.5 tick periods): order 11,12,20,30,40,51,52
SMOKE frame-order PASS frame 4 (1.5 tick periods): run-frame calls this frame == 1
SMOKE frame-order PASS frame 4 (1.5 tick periods): ticks == 1
SMOKE frame-order PASS frame 4 (1.5 tick periods): Present alpha 500 == driver.AlphaPermille, in 0..999
SMOKE frame-order PASS frame 4 (1.5 tick periods): loop not faulted == False
SMOKE frame-order PASS Present alpha at 1.5 tick periods == 500
SMOKE frame-order PASS frame 5 (empty hook lists, armed): order 20,30,40
SMOKE frame-order PASS frame 5 (empty hook lists, armed): run-frame calls this frame == 1
SMOKE frame-order PASS frame 5 (empty hook lists, armed): ticks == 0
SMOKE frame-order PASS frame 5 (empty hook lists, armed): Present alpha 0 == driver.AlphaPermille, in 0..999
SMOKE frame-order PASS frame 5 (empty hook lists, armed): loop not faulted == False
SMOKE frame-order PASS empty-hook driver state == Running
SMOKE frame-order PASS frame 6 (empty hook lists, 2 ticks): order 20,30,40
SMOKE frame-order PASS frame 6 (empty hook lists, 2 ticks): run-frame calls this frame == 1
SMOKE frame-order PASS frame 6 (empty hook lists, 2 ticks): ticks == 2
SMOKE frame-order PASS frame 6 (empty hook lists, 2 ticks): Present alpha 0 == driver.AlphaPermille, in 0..999
SMOKE frame-order PASS frame 6 (empty hook lists, 2 ticks): loop not faulted == False
SMOKE frame-order PASS StopwatchMonotonicClock.Frequency 1000000000 within [10^3, 10^12]
SMOKE frame-order PASS StopwatchMonotonicClock.Frequency == Stopwatch.Frequency == 1000000000
SMOKE frame-order PASS StopwatchMonotonicClock.Timestamp non-decreasing over 10 frames
SMOKE RESULT name=frame-order pass=50 fail=0 exit=0
```

## `pause-driver-state` — AC4 (64 SMOKE lines)

```
SMOKE pause-driver-state PASS default GameLoop.PeerPresence is the loopback presence == True
SMOKE pause-driver-state PASS TryPause while the driver start is pending returns == False
SMOKE pause-driver-state PASS TryPause while start pending: refusal logged == True
SMOKE pause-driver-state PASS driver state after the refused early pause == Idle,True,True
SMOKE pause-driver-state PASS driver state after the first armed frame == Running
SMOKE pause-driver-state PASS TryPause with a remote peer present returns == False
SMOKE pause-driver-state PASS pause refusal reason: multiplayer never pauses: a remote peer is present (ADR-0002 section 4)
SMOKE pause-driver-state PASS Networking's refusal reason logged by GameLoop == True
SMOKE pause-driver-state PASS refusal reason is Networking's == multiplayer never pauses: a remote peer is present (ADR-0002 section 4)
SMOKE pause-driver-state PASS driver state after the refused pause == Running
SMOKE pause-driver-state PASS ticks on the frame after the refused pause == 2
SMOKE pause-driver-state PASS TryPause with no remote peer returns == True
SMOKE pause-driver-state PASS TryPause reason when allowed ==
SMOKE pause-driver-state PASS driver state after TryPause == Paused
SMOKE pause-driver-state PASS driver-paused frame 1: _Process ran run-frame once == 1
SMOKE pause-driver-state PASS driver-paused frame 1: ticks == 0
SMOKE pause-driver-state PASS driver-paused frame 1: driver.AlphaPermille == 0
SMOKE pause-driver-state PASS driver-paused frame 1: Present alpha == 0
SMOKE pause-driver-state PASS driver-paused frame 1: Present calls == 1
SMOKE pause-driver-state PASS driver-paused frame 1: hook calls (pre,post) == 1,1
SMOKE pause-driver-state PASS driver-paused frame 2: _Process ran run-frame once == 1
SMOKE pause-driver-state PASS driver-paused frame 2: ticks == 0
SMOKE pause-driver-state PASS driver-paused frame 2: driver.AlphaPermille == 0
SMOKE pause-driver-state PASS driver-paused frame 2: Present alpha == 0
SMOKE pause-driver-state PASS driver-paused frame 2: Present calls == 1
SMOKE pause-driver-state PASS driver-paused frame 2: hook calls (pre,post) == 1,1
SMOKE pause-driver-state PASS driver-paused frame 3: _Process ran run-frame once == 1
SMOKE pause-driver-state PASS driver-paused frame 3: ticks == 0
SMOKE pause-driver-state PASS driver-paused frame 3: driver.AlphaPermille == 0
SMOKE pause-driver-state PASS driver-paused frame 3: Present alpha == 0
SMOKE pause-driver-state PASS driver-paused frame 3: Present calls == 1
SMOKE pause-driver-state PASS driver-paused frame 3: hook calls (pre,post) == 1,1
SMOKE pause-driver-state PASS SceneTree.Paused while tree-paused == True
SMOKE pause-driver-state PASS tree-paused frame 1: _Process ran run-frame once == 1
SMOKE pause-driver-state PASS tree-paused frame 1: ticks == 0
SMOKE pause-driver-state PASS tree-paused frame 1: driver.AlphaPermille == 0
SMOKE pause-driver-state PASS tree-paused frame 1: Present alpha == 0
SMOKE pause-driver-state PASS tree-paused frame 1: Present calls == 1
SMOKE pause-driver-state PASS tree-paused frame 1: hook calls (pre,post) == 1,1
SMOKE pause-driver-state PASS tree-paused frame 1: total ticks unchanged == 2
SMOKE pause-driver-state PASS SceneTree.Paused while tree-paused == True
SMOKE pause-driver-state PASS tree-paused frame 2: _Process ran run-frame once == 1
SMOKE pause-driver-state PASS tree-paused frame 2: ticks == 0
SMOKE pause-driver-state PASS tree-paused frame 2: driver.AlphaPermille == 0
SMOKE pause-driver-state PASS tree-paused frame 2: Present alpha == 0
SMOKE pause-driver-state PASS tree-paused frame 2: Present calls == 1
SMOKE pause-driver-state PASS tree-paused frame 2: hook calls (pre,post) == 1,1
SMOKE pause-driver-state PASS tree-paused frame 2: total ticks unchanged == 2
SMOKE pause-driver-state PASS SceneTree.Paused while tree-paused == True
SMOKE pause-driver-state PASS tree-paused frame 3: _Process ran run-frame once == 1
SMOKE pause-driver-state PASS tree-paused frame 3: ticks == 0
SMOKE pause-driver-state PASS tree-paused frame 3: driver.AlphaPermille == 0
SMOKE pause-driver-state PASS tree-paused frame 3: Present alpha == 0
SMOKE pause-driver-state PASS tree-paused frame 3: Present calls == 1
SMOKE pause-driver-state PASS tree-paused frame 3: hook calls (pre,post) == 1,1
SMOKE pause-driver-state PASS tree-paused frame 3: total ticks unchanged == 2
SMOKE pause-driver-state PASS driver state after Resume (tree still paused) == Running
SMOKE pause-driver-state PASS SceneTree.Paused on the first frame after Resume == True
SMOKE pause-driver-state PASS run-frame calls on the first frame after Resume == 1
SMOKE pause-driver-state PASS first frame after Resume ran 1 tick(s) (<= 1, no burst after a 10 s pause)
SMOKE pause-driver-state PASS ticks while SceneTree.Paused and the driver running (tree pause never stops the Sim) == 2
SMOKE pause-driver-state PASS loop not faulted == False
SMOKE pause-driver-state PASS SceneTree.Paused restored == False
SMOKE RESULT name=pause-driver-state pass=63 fail=0 exit=0
```

## `fault-stop` — AC5 (and AC2: a throwing hook is a fault) (84 SMOKE lines)

```
SMOKE fault-stop PASS run-frame-throws: fresh install clears the fault latch == False
SMOKE fault-stop PASS run-frame-throws: GameLoop.IsFaulted after the injected fault (run-frame calls so far 3)
SMOKE fault-stop PASS run-frame-throws: fault sink message count == 1
SMOKE fault-stop PASS run-frame-throws: fault message names 'InjectedFaultException': InjectedFaultException: run-frame threw on call 3
SMOKE fault-stop PASS run-frame-throws: fault sink called on the main (boot) thread == True
SMOKE fault-stop PASS run-frame-throws: fault logged with type and stack == True
SMOKE fault-stop PASS run-frame-throws: session-ended line logged == True
SMOKE fault-stop PASS run-frame-throws: run-frame calls over 10 frames after the fault == 0
SMOKE fault-stop PASS run-frame-throws: Present calls over 10 frames after the fault == 0
SMOKE fault-stop PASS run-frame-throws: fault sink messages after 10 more frames == 1
SMOKE fault-stop PASS run-frame-throws: loop still faulted == True
SMOKE fault-stop PASS pre-hook-throws: fresh install clears the fault latch == False
SMOKE fault-stop PASS pre-hook-throws: GameLoop.IsFaulted after the injected fault (run-frame calls so far 2)
SMOKE fault-stop PASS pre-hook-throws: fault sink message count == 1
SMOKE fault-stop PASS pre-hook-throws: fault message names 'InjectedFaultException': InjectedFaultException: pre-frame hook 2 threw on call 3
SMOKE fault-stop PASS pre-hook-throws: fault sink called on the main (boot) thread == True
SMOKE fault-stop PASS pre-hook-throws: fault logged with type and stack == True
SMOKE fault-stop PASS pre-hook-throws: session-ended line logged == True
SMOKE fault-stop PASS pre-hook-throws: run-frame calls over 10 frames after the fault == 0
SMOKE fault-stop PASS pre-hook-throws: Present calls over 10 frames after the fault == 0
SMOKE fault-stop PASS pre-hook-throws: fault sink messages after 10 more frames == 1
SMOKE fault-stop PASS pre-hook-throws: loop still faulted == True
SMOKE fault-stop PASS post-hook-throws: fresh install clears the fault latch == False
SMOKE fault-stop PASS post-hook-throws: GameLoop.IsFaulted after the injected fault (run-frame calls so far 3)
SMOKE fault-stop PASS post-hook-throws: fault sink message count == 1
SMOKE fault-stop PASS post-hook-throws: fault message names 'InjectedFaultException': InjectedFaultException: post-present hook 1 threw on call 3
SMOKE fault-stop PASS post-hook-throws: fault sink called on the main (boot) thread == True
SMOKE fault-stop PASS post-hook-throws: fault logged with type and stack == True
SMOKE fault-stop PASS post-hook-throws: session-ended line logged == True
SMOKE fault-stop PASS post-hook-throws: run-frame calls over 10 frames after the fault == 0
SMOKE fault-stop PASS post-hook-throws: Present calls over 10 frames after the fault == 0
SMOKE fault-stop PASS post-hook-throws: fault sink messages after 10 more frames == 1
SMOKE fault-stop PASS post-hook-throws: loop still faulted == True
SMOKE fault-stop PASS sampler-throws: fresh install clears the fault latch == False
SMOKE fault-stop PASS sampler-throws: GameLoop.IsFaulted after the injected fault (run-frame calls so far 3)
SMOKE fault-stop PASS sampler-throws: fault sink message count == 1
SMOKE fault-stop PASS sampler-throws: fault message names 'InjectedFaultException': InjectedFaultException: render-clock sample threw on call 3
SMOKE fault-stop PASS sampler-throws: fault sink called on the main (boot) thread == True
SMOKE fault-stop PASS sampler-throws: fault logged with type and stack == True
SMOKE fault-stop PASS sampler-throws: session-ended line logged == True
SMOKE fault-stop PASS sampler-throws: run-frame calls over 10 frames after the fault == 0
SMOKE fault-stop PASS sampler-throws: Present calls over 10 frames after the fault == 0
SMOKE fault-stop PASS sampler-throws: fault sink messages after 10 more frames == 1
SMOKE fault-stop PASS sampler-throws: loop still faulted == True
SMOKE fault-stop PASS present-throws: fresh install clears the fault latch == False
SMOKE fault-stop PASS present-throws: GameLoop.IsFaulted after the injected fault (run-frame calls so far 3)
SMOKE fault-stop PASS present-throws: fault sink message count == 1
SMOKE fault-stop PASS present-throws: fault message names 'InjectedFaultException': InjectedFaultException: present threw on call 3
SMOKE fault-stop PASS present-throws: fault sink called on the main (boot) thread == True
SMOKE fault-stop PASS present-throws: fault logged with type and stack == True
SMOKE fault-stop PASS present-throws: session-ended line logged == True
SMOKE fault-stop PASS present-throws: run-frame calls over 10 frames after the fault == 0
SMOKE fault-stop PASS present-throws: Present calls over 10 frames after the fault == 0
SMOKE fault-stop PASS present-throws: fault sink messages after 10 more frames == 1
SMOKE fault-stop PASS present-throws: loop still faulted == True
SMOKE fault-stop PASS world-faulted: fresh install clears the fault latch == False
SMOKE fault-stop PASS world-faulted: GameLoop.IsFaulted after the injected fault (run-frame calls so far 3)
SMOKE fault-stop PASS world-faulted: fault sink message count == 1
SMOKE fault-stop PASS world-faulted: fault message names 'Faulted': world Faulted after RunFrame (non-SIM_ASSERT builds do not rethrow)
SMOKE fault-stop PASS world-faulted: fault sink called on the main (boot) thread == True
SMOKE fault-stop PASS world-faulted: fault logged with type and stack == True
SMOKE fault-stop PASS world-faulted: session-ended line logged == True
SMOKE fault-stop PASS world-faulted: sample/present calls (stopped after run-frame) == 2,2
SMOKE fault-stop PASS world-faulted: run-frame calls over 10 frames after the fault == 0
SMOKE fault-stop PASS world-faulted: Present calls over 10 frames after the fault == 0
SMOKE fault-stop PASS world-faulted: fault sink messages after 10 more frames == 1
SMOKE fault-stop PASS world-faulted: loop still faulted == True
SMOKE fault-stop PASS wrong-thread: fresh install clears the fault latch == False
SMOKE fault-stop PASS wrong-thread: _Process called from managed thread 4 (boot thread 1)
SMOKE fault-stop PASS wrong-thread: run-frame calls on the off-thread frame == 0
SMOKE fault-stop PASS wrong-thread: loop latched faulted at once == True
SMOKE fault-stop PASS wrong-thread: sink not called on the worker (delivered deferred on the main thread) == 0
SMOKE fault-stop PASS wrong-thread: StartPending cleared (driver never started) == False,Idle
SMOKE fault-stop PASS wrong-thread: GameLoop.IsFaulted after the injected fault (run-frame calls so far 0)
SMOKE fault-stop PASS wrong-thread: fault sink message count == 1
SMOKE fault-stop PASS wrong-thread: fault message names 'InvalidOperationException': InvalidOperationException: GameLoop._Process ran on managed thread 4, not the boot thread 1 (TR-sim-007)
SMOKE fault-stop PASS wrong-thread: fault sink called on the main (boot) thread == True
SMOKE fault-stop PASS wrong-thread: fault logged with type and stack == True
SMOKE fault-stop PASS wrong-thread: session-ended line logged == True
SMOKE fault-stop PASS wrong-thread: run-frame calls over 10 frames after the fault == 0
SMOKE fault-stop PASS wrong-thread: Present calls over 10 frames after the fault == 0
SMOKE fault-stop PASS wrong-thread: fault sink messages after 10 more frames == 1
SMOKE fault-stop PASS wrong-thread: loop still faulted == True
SMOKE RESULT name=fault-stop pass=83 fail=0 exit=0
```

## `frame-alloc` — AC6 (advisory) (2 SMOKE lines)

```
SMOKE frame-alloc PASS FrameSequence.Run x 600 after 60 warm-up: 0 B allocated (advisory, target 0 B; ticks 825, run-frame calls 660)
SMOKE RESULT name=frame-alloc pass=1 fail=0 exit=0
```

## CI guards

| Command | Exit | Last line |
|---|---|---|
| `check-no-scenetree-paused.sh --self-check` | 0 | self-check: PASS (GetTree().Paused / SceneTree.Paused caught in code, skipped in comments, clean code not matched; src/Game/Smoke/ skipped, other src/Game/ and Sim files caught) |
| `check-no-scenetree-paused.sh` | 0 | No SceneTree.Paused in Sim / src/Game (src/Game/Smoke/ excluded; comments skipped). |
| `check-sim-access.sh --self-check` | 0 | check-sim-access --self-check: PASS (14 cases) |
| `check-sim-access.sh` | 0 | check-sim-access: clean |
| `check-scene-rules.sh --self-check` | 0 | check-scene-rules --self-check: clean layout accepted, 25 planted violations detected |
| `check-scene-rules.sh` | 0 | check-scene-rules: checked 10 rules |
| `check-gdignore.sh --self-check` | 0 | check-gdignore --self-check: clean layout accepted, 8 planted violations detected |
| `check-gdignore.sh` | 0 | check-gdignore: checked 15 rules |
| `check-analyzer-fixtures.sh --self-check` | 0 | self-check: PASS (script reported the compiling fixture and exited 1) |
| `check-analyzer-fixtures.sh` | 0 | check-analyzer-fixtures: 128 fixtures, 0 failure(s) |
| `check-build-config.sh` | 0 | check-build-config: all 10 rows passed |
| `check-game-build.sh` (after Game.sln Debug + ExportRelease, 0 warnings / 0 errors each) | 0 | check-game-build: checked 16 rows |

## xUnit (PerformativeMail.sln, 4 legs)

| Leg | Build | Total | Failed | Succeeded | Skipped |
|---|---|---|---|---|---|
| net10.0-Debug | 0 warnings, 0 errors | 4669 | 0 | 4627 | 42 |
| net10.0-Release | 0 warnings, 0 errors | 4669 | 0 | 4667 | 2 |
| net8.0-Debug | 0 warnings, 0 errors | 4669 | 0 | 4627 | 42 |
| net8.0-Release | 0 warnings, 0 errors | 4669 | 0 | 4667 | 2 |

Baseline before this story: 4638. New tests (+31): `tests/unit/net/SoloPausePolicyTests.cs`, `tests/unit/timing/RenderClockTests.cs`.
