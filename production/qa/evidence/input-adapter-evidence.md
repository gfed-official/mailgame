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
