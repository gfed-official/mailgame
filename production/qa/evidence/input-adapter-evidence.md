# Evidence: input-controls story 026 — the Godot `InputAdapter` node

**Story**: `production/epics/input-controls/story-026-godot-input-adapter-node.md`
**Date**: 2026-11-09
**Engine used for this run**: Godot **4.7.2**.stable.mono.official.ed1daf0bf (`~/.local/bin/godot`, headless). The project pins 4.7.1; 4.7.1 behaviour is UNVERIFIED.
**.NET SDK**: the box's `~/.dotnet` (net10 SDK); `PerformativeMail.Game.sln` built Debug before the run.
**Procedure**: `godot --headless --path . --import` once (it rewrites the csproj's `Godot.NET.Sdk` version to 4.7.2; the rewrite was reverted and the `.old` copy deleted), `dotnet build PerformativeMail.Game.sln -c Debug`, then `godot --headless --path . -- --smoke=input-adapter`. Every event is synthetic (`Input.ParseInputEvent` + `Input.FlushBufferedEvents()`, or a C#-created event passed to the static converter); no real keyboard, mouse or pad was used. The SMOKE lines below are copied verbatim from that run (exit 0); nothing else in the log matched `ERROR|Leaked|Exception`.

This file holds story 026's headless rows. Story 029 adds the MANUAL rows (real devices, AZERTY, the S-25 retention follow-up).

## Rows by acceptance criterion

| AC | Smoke rows | Result |
|---|---|---|
| Node and placement | `adapter parent is the root window`, `adapter is the last root child ... (D-1 re-assert)`, `ProcessMode`, `ProcessPriority`, `before Initialize: router fed nothing` | PASS. Placement follows ADR-0012 §7 D-1 (last child of the root window, re-asserted on `ChildEnteredTree`), not the AC's `Main/Input` first-child slot, which D-1 superseded. |
| H-R9-2 / H-EC-12 classification | the nine `classify ...` rows, `InputEventAction leaves the last-used device unchanged` | PASS |
| Conversion | `PhysicalInputMap covers every M-1a PhysicalInput`, the key / wheel / mouse / joypad / RT / F12 conversion rows, `Space press stamped with IInputClock.NowMs`, `F12 press and release ignored and counted` | PASS |
| Axes and triggers | `pad 0 LeftX 0.5 and LeftY -1 cached as Q15`, `LT axis at 1.0 -> F4 press`, `RightX 0.75 unseen, then re-seeded from Input.GetJoyAxis` | PASS (synthetic pad events only; real pads are story 029's M-1). The x / y pairing is not observed by the smoke: the router's move intent only leaves through `FlushToLatch`, whose latch types are in the Sim namespace `check-sim-access.sh` bans under `src/Game/` outside Hosting/Boot. |
| Handled state + H-G4-1 | `consumed Space: ...`, `unconsumed Enter: router saw press ... and release ...`, `unconsumed Enter: overlay events, Button downs, Button presses`, `H-G4-1: E + 30 echoes + release -> gate lines for E` | PASS. H-G4-1 is read from the router's event log: one `GateFired` for E and no other gate line (the router's edge per resolved press is covered by `PressMachineTests`). The two handled rows were seen FAILING with `SetInputAsHandled` disabled (`2/1/1` and `2/2/2`). |
| Mouse look | `16 motions ... accumulation on / off: records fed`, `look turn identical with accumulation on and off`, `motion consumed where Look is live` | PASS for the sum and the coalescing. The allocation half ("within the spike's recorded bound") was **NOT RUN**: 0 B / perf gates are advisory until M0 (ADR-0012 erratum E2). |

**Open gate (not this run):** ADR-0012 §7 D-8 keeps story 026 from Done until the follow-up retention spike (TD-022, S-25 / S-26: Release build, real input). The adapter does not `Dispose()` events, because later receivers still read them.

## SMOKE lines (2026-11-09)

```
SMOKE input-adapter PASS classify key, default device (device 16) == True/Keyboard/0
SMOKE input-adapter PASS classify key with device 0 (never pad 0) (device 0) == False/Keyboard/0
SMOKE input-adapter PASS classify mouse button, default device (device 32) == True/Mouse/0
SMOKE input-adapter PASS classify mouse button, emulation id (device -1) == True/Mouse/0
SMOKE input-adapter PASS classify mouse motion, emulation id (device -1) == True/Mouse/0
SMOKE input-adapter PASS classify joypad button, device 0 (device 0) == True/Pad/0
SMOKE input-adapter PASS classify joypad button, device 16 (not keyboard) (device 16) == True/Pad/16
SMOKE input-adapter PASS classify joypad motion, device 32 (not mouse) (device 32) == True/Pad/32
SMOKE input-adapter PASS classify InputEventAction (device 0) (device 0) == True/Synthetic/0
SMOKE input-adapter PASS PhysicalInputMap covers every M-1a PhysicalInput (missing, extra) == ;
SMOKE input-adapter PASS key: physical W with label Z (AZERTY) -> KeyW, echo kept, stamp == Record/Key/KeyW/True/True/7
SMOKE input-adapter PASS wheel down press -> one Wheel notch == Record/Wheel/WheelDown/1
SMOKE input-adapter PASS wheel down release -> ignored == Ignored
SMOKE input-adapter PASS right mouse button == Record/MouseButton/Mouse/MouseRight
SMOKE input-adapter PASS joypad 1 Start -> PadMenu == Record/PadButton/1/PadMenu
SMOKE input-adapter PASS RT at 1.0 -> PadRightTrigger Q15 == Record/PadAxis/PadRightTrigger/32767
SMOKE input-adapter PASS F12 (no M-1a input) -> unmapped == Unmapped
SMOKE input-adapter PASS adapter parent is the root window == True
SMOKE input-adapter PASS adapter is the last root child after the GUI and overlay entered (D-1 re-assert) == 3/True
SMOKE input-adapter PASS adapter ProcessMode == Always
SMOKE input-adapter PASS adapter ProcessPriority == -2147483648
SMOKE input-adapter PASS before Initialize: router fed nothing, overlay saw both edges == False/0/2
SMOKE input-adapter PASS Button has focus == True
SMOKE input-adapter PASS Space press stamped with IInputClock.NowMs (GateFired time, input) == 5000/265
SMOKE input-adapter PASS consumed Space: overlay events, Button downs, Button presses == 0/0/0
SMOKE input-adapter PASS unconsumed Enter: router saw press (GateNotLive) and release (GateSwallowed) == 5100/274;5150/274
SMOKE input-adapter PASS unconsumed Enter: overlay events, Button downs, Button presses == 2/1/1
SMOKE input-adapter PASS F12 press and release ignored and counted (not thrown) == 2
SMOKE input-adapter PASS H-G4-1: E + 30 echoes + release -> gate lines for E (fired, other) == 1,0
SMOKE input-adapter PASS 16 motions (3, -2) in one frame, accumulation on: records fed == 1
SMOKE input-adapter PASS 16 motions (3, -2) in one frame, accumulation off: records fed == 1
SMOKE input-adapter PASS look turn identical with accumulation on and off (64662 BAM yaw)
SMOKE input-adapter PASS motion consumed where Look is live: overlay saw none == 0
SMOKE input-adapter PASS pad 0 LeftX 0.5 and LeftY -1 cached as Q15 == 16384,-32767
SMOKE input-adapter PASS LT axis at 1.0 -> F4 press (Throw aim GateFired) == 8000/786
SMOKE input-adapter PASS RightX 0.75 unseen, then re-seeded from Input.GetJoyAxis == 0,24575
SMOKE input-adapter PASS InputEventAction leaves the last-used device unchanged == Pad/0
SMOKE input-adapter PASS last-used device after the pad rows == Pad/0
SMOKE RESULT name=input-adapter pass=38 fail=0 exit=0
```


---

# Story 027 — mouse mode, GUI-focus policy, joypad project setting

**Story**: `production/epics/input-controls/story-027-adapter-mouse-mode-gui-focus-hotplug-project-setting.md`
**Date**: 2026-11-09
**Engine used for this run**: Godot **4.7.2**.stable.mono (`~/.local/bin/godot`, headless); 4.7.1 behaviour UNVERIFIED.
**Procedure**: as story 026 (`--import` once for the new `.uid` files, csproj rewrite reverted and `.old` deleted), `dotnet build PerformativeMail.Game.sln -c Debug`, then `godot --headless --path . -- --smoke=input-mouse-focus` (exit 0). Synthetic events only. Headless ignores `MouseMode = Captured` (spike S-31), so the smoke checks the mode the adapter **requests and writes**, not the OS cursor. The two H-R11-3 Space/Enter rows and the two focus-released rows were seen FAILING with the `GuiReleaseFocus()` call disabled (`Button presses is 2`, `focus released ... True`). The story-026 scenario `input-adapter` still passes (38/0) with the story-027 adapter.

## Rows by acceptance criterion

| AC | Smoke rows | Result |
|---|---|---|
| Mouse mode | `Initialize in World: mouse mode applied ...`, `Tab -> Panel: mouse mode Visible`, `keys that change no state in Panel: no mouse-mode write`, `Tab -> World: mouse mode Captured ...`, `mouse press in World re-asks capture ...`, `capture readback after the World write` | PASS (requested mode, write-on-change, readback retry). Pause: unreachable without story 016; the router's Pause request is `RouterTransitionsTests.MouseModeRequest_IsCapturedWithLook_VisibleInPanelHoldingOpenAndPause`. Cursor centred on leaving capture and the real captured / visible cursor: **MANUAL / NOT RUN** (story 029, real display). Capture re-applied after focus-in: **NOT RUN** — no focus handlers (greybox simplification per m1a-packages.md; story 018 calls `MouseModeController.Reapply()`). |
| GUI-focus policy | `Button focused before Initialize`, the two `focus released` rows, `FocusPolicyCheck: ...`, `FocusPolicyCheck after GameControlFocus.Apply`, `first frame: ui_* InputMap matches the startup snapshot ...` | PASS. The InputMap-mutated path is not exercised (the smoke writes no `InputMap`). |
| H-R11-3 clauses 2–4 | `project setting input_devices/joypads/ignore_joypad_on_unfocused_application`, `Input.IgnoreJoypadOnUnfocusedApplication at runtime`, the two `H-R11-3: Space, Enter, KP Enter in World ...` rows | PASS (clause 2 and 4). Clause 3 (captured in World, visible in Panel / Pause on a real display): **MANUAL / NOT RUN** (story 029). |
| Project setting | `project setting ...` row; key as written by the spike's `run_b3_set.sh` (B3) | PASS |
| Focus loss delivery, H-EC-5 integration half | — | **NOT RUN** — greybox simplification per m1a-packages.md ("no focus loss"); router `OnFocusLost` / `OnFocusGained` (story 018) do not exist. |
| Pause requests | — | **NOT RUN** — router `PauseRequested` / `ResumeRequested` (story 016) do not exist; the adapter pauses nothing; `check-no-scenetree-paused.sh` green. |

## SMOKE lines (2026-11-09)

```
SMOKE input-mouse-focus PASS project setting input_devices/joypads/ignore_joypad_on_unfocused_application == True
SMOKE input-mouse-focus PASS Input.IgnoreJoypadOnUnfocusedApplication at runtime == True
SMOKE input-mouse-focus PASS Button focused before Initialize == True
SMOKE input-mouse-focus PASS Initialize in World: mouse mode applied, writes, focus released == Captured/1/False
SMOKE input-mouse-focus PASS capture readback after the World write: rejected=True (headless ignores capture, S-31; real display is story 029)
SMOKE input-mouse-focus PASS H-R11-3: Space, Enter, KP Enter in World after Initialize: Button presses == 0
SMOKE input-mouse-focus PASS Tab -> Panel: mouse mode Visible, writes == Visible/2
SMOKE input-mouse-focus PASS keys that change no state in Panel: no mouse-mode write == 2
SMOKE input-mouse-focus PASS Tab -> World: mouse mode Captured, writes, Button focus released == Captured/3/False
SMOKE input-mouse-focus PASS H-R11-3: Space, Enter, KP Enter in World after Panel -> World: Button presses == 0
SMOKE input-mouse-focus PASS mouse press in World re-asks capture only while the readback shows it dropped (D-2) == 1
SMOKE input-mouse-focus PASS FocusPolicyCheck: the visible focusable Hud Button only (hidden, Apply-ed and Screens exempt) == /root/Main/Hud/SmokeHudLayer/Stray (All)
SMOKE input-mouse-focus PASS FocusPolicyCheck after GameControlFocus.Apply on it == 0
SMOKE input-mouse-focus PASS before the first frame: boot checks not run == True
SMOKE input-mouse-focus PASS first frame: ui_* InputMap matches the startup snapshot; focus policy findings in the clean tree == True/0
SMOKE input-mouse-focus PASS ui_* actions in the startup snapshot (non-zero) == True
SMOKE RESULT name=input-mouse-focus pass=16 fail=0 exit=0
```

# Story 028 — boot step 4, the per-frame call order, `user://input.json`

**Story**: `production/epics/input-controls/story-028-gameloop-hookup-router-boot-settings-file.md`
**Date**: 2026-11-09
**Engine used for this run**: Godot **4.7.2**.stable.mono (`~/.local/bin/godot`, headless); 4.7.1 behaviour UNVERIFIED.
**Procedure**: `--import` once for the three new `.uid` files (the csproj rewrite reverted, `.old` deleted), `dotnet build PerformativeMail.Game.sln -c Debug`, then `godot --headless --path . -- --smoke=input-boot` (exit 0). The scenario boots the real nine-step plan with an `InputBoot` step-4 contributor over the shell's frame-loop rig (recording run / sample / present; the booted session is kept for its client latch), then calls `InputBoot.AttachToLoop(loop)` itself: **production `GameLoop` / `BootPlan` do not call `InputBoot` yet** (hooks listed for WP-A in the PR). Synthetic events only. The unit test `RouterFrameCallsTests` (two facts) covers the 0-tick and 3-tick latch rows over the real client latch; both facts were seen FAILING with the dispatch / flush order swapped and with the flush removed. `--smoke=list` gives 22 scenarios; every one exits with its `expect=` code (input-adapter 38/0, input-mouse-focus 16/0, boot-order 84/0, session-ticks 28/0, main-tree 43/0, frame-order 50/0).

## Rows by acceptance criterion

| AC | Evidence | Result |
|---|---|---|
| Call order | smoke `frame order: Drop sink, RunFrame, sample, Present, post-present hook` (`11,20,30,40,51`), `next frame: the immediate is not dispatched again`; `RouterFrameCallsTests.BeforeRunFrame_DispatchesImmediatesBeforeTheLatchFlush`, `RouterFrameCallsTests.ZeroTickFrame_KeepsEdgesLatched_NextThreeTickFrameCarriesThemOnTheFirstTickOnly` | PASS on the WP-B side. The post-present `PresentedPromptLatch.Update` is registered before the smoke's recording hook (51); nothing on main publishes a focused prompt, so its effect is story 012's `InteractLatchTests`. Production wiring needs the WP-A hook. |
| Boot step 4 | smoke `BOOT 4` lines, `step 4 ran InputBoot and passed`, `invalid action map: boot fails at step 4 ...`, `adapter is the last root child after a later root child entered (D-1 re-assert)`, `adapter initialised by AttachToLoop`, `AttachToLoop a second time throws` | PASS. Ports: idle `InputPortSlots` (no publisher on main) except the client tick, bound at attach. |
| H-R12-1 | smoke `H-R12-1: IClientInputSettings.HoldSettingMs from the file == 600`, `... Deliver mode ... == Toggle`, `no warning for a valid file`, `malformed file: ... defaults applied`, `malformed file: warnings == 1`, `missing file: ... defaults`, `missing file: warnings == 0` | PASS (`FileAccess.GetFileAsBytes` + `GetOpenError()`). The Sim does not read `IClientInputSettings` from `InputBoot.Settings` yet (no composed hold step on main; WP-A hook). |
| Solo pause | — | **NOT RUN** — router `PauseRequested` / `ResumeRequested` (story 016) do not exist; `check-no-scenetree-paused.sh` and `--self-check` green. |
| Cross-system wiring | smoke `ShoulderSwap without a sink logs no_sink`; `BOOT 4 input: immediate sinks: none registered ...` | (a) no sink exists on main, every immediate logs `no_sink` (story 013). (b) `ReplyObserved` / `CommitSent`: **NOT RUN**, greybox simplification per m1a-packages.md (story 015). (c) camera rig, `LookReseeded`: **NOT RUN**, no rig on main. (d) `SetSessionMode`: **NOT RUN**, not on the router. |
| Frame cost sanity | smoke `120 frames with input: loop not faulted`, `the driver ticked`, `event log not overwritten` | Short headless run only; the 60 s run and the 0.05 ms guardrail are advisory until M0 (E2) and were skipped. |

## SMOKE lines (2026-11-09)

```
SMOKE input-boot PASS boot log: BOOT 4 input: contributor InputBoot
SMOKE input-boot PASS boot log: BOOT 4 input: action map valid (29 actions, 0 warning(s))
SMOKE input-boot PASS boot log: BOOT 4 input: settings user://smoke_input_028_toggle.json: loaded v1, 0 note(s), holdSettingMs=600 holdModeDeliver=Toggle
SMOKE input-boot PASS boot log: BOOT 4 input: immediate sinks: none registered; no_sink until their stories: throw,tool,hotbar_slot_1,hotbar_slot_2,hotbar_slot_3,hotbar_slot_4,hotbar_slot_5,hotbar_slot_6,hotbar_slot_7,hotbar_slot_8,drop,throw_while_holding,tool_while_holding,open_panel,shoulder_swap,pause,back,grid_cursor,pick_place,rotate,cancel_return,quick_move,close_panel
SMOKE input-boot PASS boot log: BOOT 4 input: router built, adapter queued as the last child of the root window
SMOKE input-boot PASS boot log: BOOT 4 input ok
SMOKE input-boot PASS step 4 ran InputBoot and passed == True
SMOKE input-boot PASS boot ok, session kept == True
SMOKE input-boot PASS H-R12-1: IClientInputSettings.HoldSettingMs from the file == 600
SMOKE input-boot PASS H-R12-1: the router's Deliver mode from the file == Toggle
SMOKE input-boot PASS H-R12-1: no warning for a valid file == True
WARNING: [InputBoot] user://smoke_input_028_malformed.json is malformed; input settings use the defaults
SMOKE input-boot PASS H-R12-1 malformed file: step 4 ok, defaults applied == True/0/True
SMOKE input-boot PASS H-R12-1 malformed file: warnings == 1
SMOKE input-boot PASS H-R12-1 missing file: step 4 ok, defaults applied == True/0/True
SMOKE input-boot PASS H-R12-1 missing file: warnings == 0
BOOT 4 input FAIL InvalidOperationException: The input action map failed validation (4 error(s)): ERROR FIELD move.Id: the numeric ActionId 1 is used by more than one action ERROR FIELD move.StringId: the string id 'move' is used by more than one action ERROR FIELD move.Id: the numeric ActionId 1 is used by more than one action ERROR FIELD move.StringId: the string id 'move' is used by more than one action 
SMOKE input-boot PASS invalid action map: boot fails at step 4 with one fault and no router or adapter == False/4/1/True
SMOKE input-boot PASS invalid action map: the fault names the validation == True
SMOKE input-boot PASS AttachToLoop a second time throws: threw InvalidOperationException (InputBoot.AttachToLoop already ran.)
SMOKE input-boot PASS adapter is a child of the root window == True
SMOKE input-boot PASS adapter is the last root child after a later root child entered (D-1 re-assert) == True/True
SMOKE input-boot PASS adapter initialised by AttachToLoop == True
SMOKE input-boot PASS frame order: Drop sink, RunFrame, sample, Present, post-present hook == 11,20,30,40,51
SMOKE input-boot PASS next frame: the immediate is not dispatched again == 20,30,40,51
SMOKE input-boot PASS ShoulderSwap without a sink logs no_sink == True
SMOKE input-boot PASS 120 frames with input: loop not faulted == False
SMOKE input-boot PASS 120 frames: the driver ticked == True
SMOKE input-boot PASS 120 frames: event log not overwritten == 0
SMOKE RESULT name=input-boot pass=27 fail=0 exit=0
```
