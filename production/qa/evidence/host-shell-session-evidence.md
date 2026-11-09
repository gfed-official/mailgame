# Evidence: godot-host-shell story 005 — host session wiring, `ClientLayer.Attach(ports)`, sample sink, local join, render clock (boot steps 5–6)

**Story**: `production/epics/godot-host-shell/story-005-host-session-composition-ports-render-clock.md` · **Type**: Integration
**Date**: 2026-11-08 (package WP-A, one change set with player-character 020; uncommitted working tree on `feat/wp-a-pc-020`).
**Engine used for these runs**: `Godot Engine v4.7.2.stable.mono.official.ed1daf0bf` (`~/.local/bin/godot`). The project pins 4.7.1; 4.7.1 behaviour is UNVERIFIED.
**Procedure**: `dotnet build PerformativeMail.Game.sln -c Debug`, then `~/.local/bin/godot --headless --path . -- --smoke=<name>` per scenario (exit code checked against `--smoke=list`'s `expect=`). The headless editor import used to generate the new `.uid` files rewrote `PerformativeMail.Game.csproj` to the 4.7.2 SDK; that rewrite and its `.csproj.old` were reverted before the runs.

## Smoke summary (exit code per scenario)

| Scenario | Expected exit | Actual | Result line |
|---|---|---|---|
| `session-ticks` (new) | 0 | 0 | `SMOKE RESULT name=session-ticks pass=28 fail=0 exit=0` |
| `session-ports` (new) | 0 | 0 | `SMOKE RESULT name=session-ports pass=25 fail=0 exit=0` |
| `boot-failure` | 0 | 0 | `SMOKE RESULT name=boot-failure pass=112 fail=0 exit=0` |
| `boot-failure-exit4` | 4 | 4 | `SMOKE RESULT name=boot-failure-exit4 pass=0 fail=1 exit=4` |
| `boot-order` | 0 | 0 | `SMOKE RESULT name=boot-order pass=81 fail=0 exit=0` |
| `fault-stop` | 0 | 0 | `SMOKE RESULT name=fault-stop pass=98 fail=0 exit=0` |
| `frame-alloc` | 0 | 0 | `SMOKE RESULT name=frame-alloc pass=1 fail=0 exit=0` |
| `frame-order` | 0 | 0 | `SMOKE RESULT name=frame-order pass=50 fail=0 exit=0` |
| `main-tree` | 0 | 0 | `SMOKE RESULT name=main-tree pass=43 fail=0 exit=0` |
| `main-tree-noboot` | 0 | 0 | `SMOKE RESULT name=main-tree-noboot pass=26 fail=0 exit=0` |
| `pause-driver-state` | 0 | 0 | `SMOKE RESULT name=pause-driver-state pass=72 fail=0 exit=0` |
| `screen-boot-error` | 0 | 0 | `SMOKE RESULT name=screen-boot-error pass=11 fail=0 exit=0` |
| `screen-content-error` | 0 | 0 | `SMOKE RESULT name=screen-content-error pass=11 fail=0 exit=0` |
| `selftest-empty` | 3 | 3 | `SMOKE RESULT name=selftest-empty pass=0 fail=0 exit=3` |
| `selftest-fail` | 1 | 1 | `SMOKE RESULT name=selftest-fail pass=1 fail=1 exit=1` |
| `selftest-needs-boot` | 0 | 0 | `SMOKE RESULT name=selftest-needs-boot pass=1 fail=0 exit=0` |
| `selftest-throws` | 1 | 1 | `SMOKE RESULT name=selftest-throws pass=0 fail=1 exit=1` |
| `selftest-timeout` | 5 | 5 | `SMOKE RESULT name=selftest-timeout pass=1 fail=1 exit=5` |

Negative control for AC-3 (run once, then reverted): with `CarrierPortContributor` not setting `TransformSamples` (so the null sink refuses every sample), `session-ports` reported `CheckInteraction at the carrier's own position is PositionStale, expected Ok` and `SMOKE RESULT name=session-ports pass=19 fail=6 exit=1`. The scenario therefore detects missing samples.

## Boot log, steps 5–6 and the ports line (`--smoke=session-ticks`, fake clock 30 000 000 counts/s)

```
BOOT 1 window ok
BOOT 2 content: source=BuiltInContent containers=4 items=4 mail_kinds=5 destination_types=1 mail_mixes=1 hash=10d2358b load_ms=45.0 (advisory budget 100)
BOOT 2 content ok
BOOT 3 persistence: no contributor
BOOT 3 persistence ok
BOOT 4 input: no contributor
BOOT 4 input ok
BOOT 5 session: loopback HostSession profile=M1aGreybox seed=5562333973730122033 (M1aRunSeed) world_source=1v1 clock_frequency=30000000 catch_up_cap=4 catch_up_guard_ms=0
BOOT 5 session ok
BOOT 6 sim: port contributor CarrierPortContributor
BOOT 6 sim: ports: panel=Null(IPanelView not declared yet) requests=ClientAdapterRequestSink hostClock=ClientHostClock samples=HostSampleSink load=ConstantCarrierLoadSource(default) localPlayer=HostLocalPlayer
BOOT 6 sim: compose+attach world=Ready tick=0 client_attached=True thread=1
BOOT 6 sim ok
BOOT 7 world NOT WIRED (story 006)
BOOT 8 views NOT WIRED (story 008)
BOOT 9 start ok
```

## `session-ticks` PASS lines (AC-1, AC-4, AC-5)

```
SMOKE session-ticks PASS step 5 logged the loopback session == True
SMOKE session-ticks PASS step 5 logged the named run seed == True
SMOKE session-ticks PASS step 6 logged compose+attach at tick 0 == True
SMOKE session-ticks PASS steps 5 and 6 ok == True
SMOKE session-ticks PASS GameLoop holds the booted session == True
SMOKE session-ticks PASS seed == 5562333973730122033
SMOKE session-ticks PASS driver frequency is the clock's == 30000000
SMOKE session-ticks PASS driver catch-up cap from SimSettings == 4
SMOKE session-ticks PASS driver idle until the first _Process (F12) == Idle
SMOKE session-ticks PASS first frame started the driver == Running
SMOKE session-ticks PASS first frame ran no tick == 0
SMOKE session-ticks PASS World.CurrentTick after 90 frames of 1/30 s == 90
SMOKE session-ticks PASS Driver.Counters dropped ticks == 0
SMOKE session-ticks PASS world not Faulted == True
SMOKE session-ticks PASS loop not faulted == False
SMOKE session-ticks PASS render clock on every tick boundary: alpha 0, HostTick == CurrentTick, IsValid, F1 formula == 0
SMOKE session-ticks PASS RenderSimTimeMs at tick 90 == 3000
SMOKE session-ticks PASS half a tick later: AlphaPermille == 500
SMOKE session-ticks PASS half a tick later: RenderSimTimeMs == 3016
SMOKE session-ticks PASS solo pause accepted == True
SMOKE session-ticks PASS paused: AlphaPermille frozen at 0 () == 0
SMOKE session-ticks PASS paused: no tick ran == 90
SMOKE RESULT name=session-ticks pass=28 fail=0 exit=0
```

## `session-ports` PASS lines (AC-2, AC-3)

```
SMOKE session-ports PASS GameLoop holds the booted session == True
SMOKE session-ports PASS recording contributor called once == 1
SMOKE session-ports PASS recording contributor ran before Attach == True
SMOKE session-ports PASS ports attached after step 6 == True
SMOKE session-ports PASS contributor line precedes the ports line == True
SMOKE session-ports PASS boot log: BOOT 6 sim: ports: panel=Null(IPanelView not declared yet) requests=ClientAdapterRequestSink hostClock=ClientHostClock samples=HostSampleSink load=ConstantCarrierLoadSource(default) localPlayer=HostLocalPlayer
SMOKE session-ports PASS ports line names panel=Null( == True
SMOKE session-ports PASS ports line names requests=ClientAdapterRequestSink == True
SMOKE session-ports PASS ports line names hostClock= == True
SMOKE session-ports PASS ports line names samples=HostSampleSink == True
SMOKE session-ports PASS ports line names load=ConstantCarrierLoadSource(default) == True
SMOKE session-ports PASS ports line names localPlayer=HostLocalPlayer == True
SMOKE session-ports PASS ticks after 40 frames == 40
SMOKE session-ports PASS carrier ports were contributed == True
SMOKE session-ports PASS ILocalPlayer.IsKnown == True
SMOKE session-ports PASS ILocalPlayer.Slot == PerformativeMail.Sim.Primitives.PlayerSlot
SMOKE session-ports PASS host sink: samples refused == 0
SMOKE session-ports PASS carrier step: samples refused == 0
SMOKE session-ports PASS one sample per tick after the tick-0 join == 39
SMOKE session-ports PASS host ingested every sample == 39
SMOKE session-ports PASS world refused no sample == 0
SMOKE session-ports PASS Players host API present == True
SMOKE session-ports PASS probe anchor registered == Ok
SMOKE session-ports PASS CheckInteraction is not PositionStale == True
SMOKE session-ports PASS CheckInteraction at the carrier's own position == Ok
SMOKE RESULT name=session-ports pass=25 fail=0 exit=0
```

## Unit test (AC-2 refusal rule)

`PerformativeMail.Sim.Tests.Unit.Sim.ClientLayerTests.ClientLayerAttachTests.Attach_OnceBeforeTheFirstStep_ElseRefused` (rows `FirstCallBeforeAnyStep`, `SecondCall`, `AfterFirstStep`, `NullPort`): passing on the four Sim legs (each 5962 total, 0 failed; Debug 67 skipped, Release 7).

## Guard

`tools/ci/check-sim-access.sh`: the smokes reach the session only through `BootSession`'s facade; no `FrameRunner|SimWorld|ClientLayer|.RunFrame(` reference outside `src/Game/Hosting` and `src/Game/Boot`: `check-sim-access: clean` (and `--self-check` PASS, 17 cases).
