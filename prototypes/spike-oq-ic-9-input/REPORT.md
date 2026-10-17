# Spike OQ-IC-9 — Godot 4.7.1 .NET input surface for the ADR-0012 §7 adapter

- **Story:** `production/epics/input-controls/story-001-oq-ic-9-input-spike.md` (gate for stories 026–029).
- **Date:** 2026-10-16. Throwaway code — nothing here is production, nothing may be promoted into `src/`.
- **Method:** mostly **synthetic events** (`Input.ParseInputEvent` + `Input.FlushBufferedEvents()`, `Node.Notification()`, or natively created events via `Injector.gd`). A synthetic-only row proves the engine's dispatch path, not what the OS/driver sends. Every row states its evidence kind.
- **Recorded by:** the spike implementer returned this report as text (a harness rule blocks subagents from writing report files); saved verbatim by the orchestrating session.

## Environment

| Item | Value |
|---|---|
| Godot | `4.7.1.stable.mono.official.a13da4feb` (`~/.local/bin/godot-mono --version`); runtime string `4.7.1-stable (official)` |
| .NET | SDK 10.0.402 via `mise exec dotnet@10.0.402`; hosting runtime 10.0.13 (`System.Environment.Version`); project `net8.0`, Godot.NET.Sdk/4.7.1, Debug assembly (`--build-solutions` default) |
| OS | Linux x86_64, kernel 7.2.5-3-omarchy; Hyprland 0.56.2 (native Wayland) + XWayland on `:0` |
| Devices | **no joypad** (`/dev/input/js*` absent); US keyboard layout (single layout) |
| Display server per run | `headless` for every row except S-34 (`Wayland` and `X11` via XWayland, both windowed ≈3.5 s) |

## Harness (AC-1)

- Own `project.godot` (main scene `Main.tscn` → `Probe.cs`), `SpikeOqIc9Input.csproj` (Godot.NET.Sdk/4.7.1, net8.0), `nuget.config` copied from `spike-adr0019`; inherits repo-root `Directory.Build.props` (TreatWarningsAsErrors, Nullable). `SpikeOqIc9Input.sln` exists because `--build-solutions` fails ("build callback failed") without one.
- **Build:** `cd prototypes/spike-oq-ic-9-input && mise exec dotnet@10.0.402 -- bash -c '~/.local/bin/godot-mono --headless --path . --build-solutions --quit'` → exit 0 (`logs/build.engine.txt`).
- **Run one probe:** `./run_probe.sh <id>` = `mise exec dotnet@10.0.402 -- bash -c "timeout 120 ~/.local/bin/godot-mono --headless --path . --quit-after 100000 -- --probe=<id>"`; writes `logs/<id>.log`, engine console in `logs/<id>.engine.txt`.
- **All headless:** `./run_all_headless.sh` (build, probes `baseline order devices alloc exitleak focus`, then `run_b3_set.sh`). Windowed S-34: `./run_probe.sh window --windowed --display-driver wayland|x11`, logs renamed `window-<driver>.*`.
- Files: `Probe.cs` (dispatch), `BaselineProbe.cs` (B1–B6), `OrderProbe.cs` (S-00–S-09), `DeviceProbe.cs` (S-10–S-17), `AllocProbe.cs` (S-20–S-26), `FocusProbe.cs` (S-30–S-33), `WindowProbe.cs` (S-34); helpers `RecvNode`, `RecvControl`, `GuiButton`, `AllocRx`, `FocusRx`, `Trace`, `Util`, `Injector.gd`.
- **Harness note H-1 (relevant to 028):** `AddChild` on the root during the main scene's `_Ready` **fails** ("Parent node is busy setting up children"); the first probe version lost every event to this. `Probe._Ready` now waits one frame. An adapter created from a scene's `_Ready` must use `CallDeferred` or wait a frame (placement → ADR-0019).

## Findings

*Result*: VERIFIED · CHANGED (differs from ADR-0012 §7 / its Engine Validation notes / the GDD baseline) · FAILED · NOT RUN · NOT SOURCEABLE. *Kind*: **SYN** synthetic/`Notification()` headless · **WIN** short windowed run, no human input · **API** compile-checked or read from ClassDB/ProjectSettings.

### Baseline re-probe (AC-2)

| ID | Result | Evidence | Kind | Observed | ADR-0012 sentence |
|---|---|---|---|---|---|
| B1 | VERIFIED | `logs/baseline.log` [B1] | API | ClassDB + C# reflection over `InputEvent` and all 16 inheriters: no member containing "time"/"stamp"; `ClassHasMethod(InputEvent,get_timestamp)=False`. | Confirms "no `InputEvent` timestamp" → §7 arrival ms stamped by the adapter. |
| B2 | VERIFIED | `baseline.log` [B2] | API | `DeviceIdKeyboard=16`, `DeviceIdMouse=32`, `DeviceIdEmulation=-1`. Default `Device`: Action 0, Key 16, MouseButton 32, MouseMotion 32, JoypadButton 0, JoypadMotion 0, ScreenTouch 0. | Confirms §7 Devices incl. emulation −1. |
| B3 | VERIFIED | `baseline.log` [B3], `logs/b3.log` | API | Setting exists, default `false`; set `true` in `project.godot` reads `true`, and 4.7 runtime property `Input.IgnoreJoypadOnUnfocusedApplication` reads `True`. | Confirms §7 project setting. |
| B4 | VERIFIED | `baseline.log` [B4] | API | `Input.UseAccumulatedInput=True`; `input_devices/buffering/agile_event_flushing` exists, default `false`. | Confirms §7 "`use_accumulated_input` left at default (true)". |
| B5 | **CHANGED** | `baseline.log` [B5] | API | 91 actions, all `ui_*`. **10 joypad events on `ui_*`**, all device −1: `ui_select` = Y; `ui_left/right/up/down` = D-pad + left stick (±1.0); `ui_colorpicker_delete_preset` = X. `ui_accept`/`ui_cancel` have **no** joypad events. | Contradicts baseline "no joypad events on `ui_*`"; Y **is** on `ui_select`. §7 policy (never mutate `ui_*`; HUD Controls FocusMode None; `GuiReleaseFocus()`) still holds and becomes **necessary**. |
| B6 | VERIFIED | `baseline.log` [B6] | API | `Input.SignalName.JoyConnectionChanged`='joy_connection_changed', `HasSignal=True`; `NotificationApplicationFocusOut=2017`, `In=2016`, `NotificationWMWindowFocusOut=1005`, `In=1004`. | Confirms §7 names. |

### Order and handled state (AC-3)

| ID | Result | Evidence | Kind | Observed | ADR-0012 sentence |
|---|---|---|---|---|---|
| S-00 | VERIFIED | `logs/order.log` [S-00] | SYN | Once the fixture is in the tree (frame 2), every flushed injection reaches all 5 `_Input` receivers. | Harness sanity (see H-1). |
| S-01 | VERIFIED | `order.log` S-01 | SYN | Per event: all `_Input` → GUI (`Button._GuiInput`, `ButtonDown` on press, `Pressed`+`ButtonUp` on release — BaseButton default action mode is *release*) → all `_ShortcutInput` (F6 `Button.Shortcut` fires here) → all `_UnhandledKeyInput` → all `_UnhandledInput`. Space/Enter activate a focused Button via `ui_accept`; **the GUI does not mark a `ui_accept` key handled** (both edges still reach unhandled receivers). | Confirms Engine Validation note 4 order. |
| S-01f | VERIFIED | `order.log` "[S-01 same-frame]", S-17 | SYN | `ParseInputEvent` alone: 0 callbacks, polled state unchanged. After `FlushBufferedEvents()`: all 22 callbacks ran **synchronously inside the flush call, same frame**, polled state updated. | Confirms note 4 (tests call `FlushBufferedEvents()`). |
| S-02 | VERIFIED | `order.log` S-02 | SYN | `SetInputAsHandled()` in `_Input` stops **every later receiver** (later `_Input` nodes, GUI, shortcut, unhandled-key, unhandled). Handled at `_ShortcutInput`/`_UnhandledKeyInput` stops only later phases (Button already activated). **Asymmetric hazard:** handling only the *release* (press reached GUI) leaves the Button stuck (`down=1 up=0 ButtonPressed=True`); handling only the press is safe. | Confirms §7 + note 3. **Delta D-3.** |
| S-03 | VERIFIED | `order.log` "polled right after SetInputAsHandled" | SYN | Handling does **not** change polled state: `IsKeyPressed`, `IsPhysicalKeyPressed`, `IsActionPressed(ui_accept/ui_select)` True after a handled press, False after the handled release; polled state updates before any `_Input`. | GDD polled-read note; §7 unchanged (game code does not poll). |
| S-04 | VERIFIED | `order.log` [S-04a/b/c] | SYN | A release always reaches the adapter's `_Input` first: (a) normal release; (b) focused Button **freed** between press and release; (c) mouse release after the pressed Control was hidden. | Release never lost to the GUI while the adapter's `_Input` runs before the GUI (always true by phase order). |
| S-05 | VERIFIED | `order.log` [S-05a/b] | SYN | An event parsed mid-frame without flush (in `process_frame` of frame 74) is delivered at the **start of the next iteration (f=75), before that iteration's `_Process`**; no physics tick fell between in this sample. Real OS events use the same flush point — not observed with real hardware. | ADR Verification item 2 / §6. |
| S-06 | **CHANGED** | `order.log` [S-06] P0–P6 | SYN | **`_Input`, `_UnhandledKeyInput`, `_UnhandledInput` run in *reverse* tree order (last node first). `ProcessPriority` has no effect on input order** (int.MinValue/MaxValue/−100, set before or after `AddChild`: identical). A first-root-child at `ProcessPriority=int.MinValue` (story AC-3f hypothesis) receives events **last**. As the last root child it was first, except before any root child added later. `_ShortcutInput` uses yet another order (Late > Plain > Early > Overlay > Button). | Contradicts note 3 "`ProcessPriority`/tree position pinned so it sees every event first". Tree position works; priority does not. **Delta D-1.** |
| S-06b | VERIFIED (new) | `order.log` [S-06b] | SYN | `Window.WindowInput` (root window signal) fires **once per event, before every `_Input`**, independent of tree order. | New option for arrival stamping. |
| S-06c | VERIFIED (new) | `order.log` [S-06c] | SYN | `SetInputAsHandled()` inside a `WindowInput` handler **does nothing** (all receivers ran; Button activated). | `WindowInput` observes, cannot consume (D-1). |
| S-07 | VERIFIED | `order.log` [S-07a/b/c] | SYN | Button with `FocusMode.None` cannot take focus and is not activated by Space; after `GuiReleaseFocus()` focus owner is none and Space does not activate; adapter `_Input` still gets both edges. | Confirms §7 fallback (`FocusMode.None` + `GuiReleaseFocus()`). |
| S-08 | VERIFIED (keys/pad) · NOT RUN (mouse GUI click) | `order.log` [S-08a–d] | SYN | Joypad A (device 0) on a focused Button: **no activation** (B5). Pad events go `_Input` → GUI → unhandled like keys. A synthetic mouse click did not hover/activate headless (`hovered=(none)`) — not investigated (time box); mouse events do pass `_Input` and the GUI and are consumed by the Button. Needs windowed/real mouse (M-6). | Stock gamepad *accept* does not exist (D-5). |
| S-09 | VERIFIED | `order.log` [S-09] | SYN | With `SceneTree.Paused=true` only the `ProcessMode=Always` node received `_Input`. | Confirms note 3 `ProcessMode = Always`. |

### Devices and axes (AC-4)

| ID | Result | Evidence | Kind | Observed | ADR-0012 sentence |
|---|---|---|---|---|---|
| S-10 | VERIFIED · CHANGED (types) | `logs/devices.log` [S-10] | API | C# names compile. **`InputEvent.Device` is `int`; `DeviceId*` constants are `long`** — comparisons compile, `Device = InputEvent.DeviceIdEmulation` needs `(int)` (CS0266). | §7 Devices; D-6 (wording). |
| S-11 | VERIFIED | `devices.log` [S-11] | SYN | Engine **accepts and delivers** `InputEventJoypadButton/Motion` with `Device` 16 and 32 unchanged. Type-first classification correct for all 12 cases; device-first misclassifies pad 16 as Keyboard and pad 32 as Mouse. Key/pad events reach `_Input` as the **same managed object** injected; mouse events arrive as a **new object** (viewport-transformed copy). | Confirms note 1 (type first, never index by `Device` before classification). Real pad with index 16/32 (Steam virtual pad): NOT RUN (M-7). |
| S-12 | VERIFIED | `devices.log` [S-12] | SYN | `ScreenTouch` with `emulate_mouse_from_touch=true` (default) yields an engine-created `InputEventMouseButton` `Device=-1`, delivered after the touch. | Confirms emulation id −1 counts as mouse. |
| S-13 | VERIFIED | `devices.log` [S-13] | SYN | After injected pad-A press on device 0/1/16/32, `Input.IsJoyButtonPressed(d, A)` True for each. | Polled pad state follows injected events. |
| S-14 | VERIFIED (synthetic) · NOT RUN (real stick) | `devices.log` [S-14] | SYN | One axis per event, raw value (0.03 delivered, no deadzone); `TriggerLeft` is a `JoypadMotion` axis (0.75). An injected `JoypadMotion` updates `Input.GetJoyAxis` immediately after flush and persists until the next event. Repeated identical synthetic values are **not** filtered. "Only on change" from the OS driver: NOT RUN (M-1). | Confirms note 5 + re-seed via `GetJoyAxis`, synthetically. |
| S-15 | VERIFIED (headless) · NOT RUN (real pad keys) | `devices.log` [S-15], `devices.engine.txt` | SYN | No pad: `GetConnectedJoypads()=[]`, `GetJoyInfo(0)={}`, empty name/GUID, `IsJoyKnown=False`. **`GetJoyInfo` on an unconnected index prints an engine ERROR** (`!joy_names.has(p_device)`). | D-7 (guard with `GetConnectedJoypads()`). |
| S-16 | NOT RUN (needs real pad) · handler path VERIFIED | `devices.log` [S-16] | SYN | No script-callable hot-plug trigger (only `add_joy_mapping`, `remove_joy_mapping`, `get_connected_joypads`). A manual `EmitSignal(JoyConnectionChanged, 3, true)` reached a C# `+=` handler as `(long device, bool connected)` — subscription only. Engine-originated hot-plug + index reuse: NOT RUN (M-1). | §7 Hot-plug stays UNVERIFIED for delivery. |
| S-17 | VERIFIED | `devices.log` [S-17] | SYN | `IsKeyPressed(F8)` False after `ParseInputEvent`, True after `FlushBufferedEvents`. | = S-01f. |

### Focus and mouse (AC-5)

| ID | Result | Evidence | Kind | Observed | ADR-0012 sentence |
|---|---|---|---|---|---|
| S-30 | VERIFIED (handler path only) | `logs/focus.log` [S-30] | SYN | `node.Notification(2017/2016/1005/1004)` reaches `_Notification`. **`SceneTree.Notification(ApplicationFocusOut/In)` propagates to every node; `Root.Notification(WMWindowFocusOut/In)` is NOT propagated** to children. | Note 4: the WM variant only reaches the `Window`; a plain-Node adapter must listen on `GetWindow().FocusExited/FocusEntered` (D-4). OS delivery: MANUAL M-2. |
| S-31 | VERIFIED (headless no-op) | `focus.log` [S-31] | SYN | Headless `HasFeature(Mouse)=False`; `MouseMode=Captured` ignored (reads `Visible`). | Headless CI cannot test mouse mode. |
| S-32 | VERIFIED (setter) · NOT RUN (honoured) | `focus.log` [S-32] | API | `Input.IgnoreJoypadOnUnfocusedApplication` default False, settable at runtime. Honoured with a real unfocused pad: NOT RUN (M-1). | §7 project setting. |
| S-33 | VERIFIED (handler path only) | `focus.log` [S-33] | SYN | After synthetic `WMWindowFocusOut` + `ApplicationFocusOut`, the engine **cleared the held key** (`IsKeyPressed(F10)` True → False) with **no release event** delivered to `_Input`. | Router's synthesized release-with-cancel on focus loss stays mandatory. MANUAL M-2. |
| S-34w | CHANGED (observational) | `logs/window-wayland.log`, `window-wayland.engine.txt` | WIN | Native Wayland window (1896×1030) not focused during the run (no click); no focus notifications. **`MouseMode=Captured` rejected**: `ERROR: Parameter "pointed_win" is null` (`display_server_wayland.cpp:514`), reads back `Visible`. Layout labels US, identity. | On Wayland a capture request without pointer focus is **dropped**, not deferred (D-2). |
| S-34x | CHANGED (observational) | `logs/window-x11.log` | WIN | XWayland window focused at start. `Captured` succeeded and the cursor **warped to window centre (960,553)**; after `Visible` the cursor **stayed at centre** (not restored). | Cursor not restored on X11 (D-2). |
| S-35 | NOT RUN | — | — | Notifications on human alt-tab (Wayland/XWayland) and whether capture survives alt-tab: not run (no programmatic focus changes by policy). | MANUAL M-2 / M-3. |

### Allocation, accumulation, misc (AC-6)

Managed bytes = `GC.GetAllocatedBytesForCurrentThread()` around 10 000 events after 300 warm-up, minus one n=0 harness call. `_Input` receiver (`AllocRx`) reads `Device`, keycode/physical, `Pressed`, `Echo`, `ScreenRelative`, axis/value, button index. Debug assembly.

| Row (S-20) | Created by | Injected | `_Input` calls | B / injected | B / delivered |
|---|---|---|---|---|---|
| InputEventKey | native (`Injector.gd`) | 10 000 | 10 000 | **512.9** | 512.9 |
| InputEventMouseButton | native | 10 000 | 10 000 | **550.5** | 550.5 |
| InputEventJoypadMotion | native | 10 000 | 10 000 | **416.0** | 416.0 |
| InputEventMouseMotion, accumulated on, 16/flush | native | 10 000 | 625 | **25.5** | 408.0 |
| InputEventMouseMotion, accumulated off, 16/flush | native | 10 000 | 10 000 | **408.0** | 408.0 |
| InputEventMouseMotion, accumulated on, 1/flush | native | 10 000 | 10 000 | 408.0 | 408.0 |
| Key / MouseButton / JoyMotion / Motion(off) / Motion(on), fresh C# object per event (incl. the `new`) | C# | 10 000 | as above | 440.8 / 512.0 / 104.0 / 1226.4 / 129.5 | – |
| Key, JoyMotion — one reused C# instance | C# | 10 000 | 10 000 | **0.0 / 0.0** | 0.0 |

| ID | Result | Evidence | Kind | Observed | ADR-0012 sentence |
|---|---|---|---|---|---|
| S-20 | VERIFIED (measured, synthetic) | `logs/alloc.log` [S-20a/b/c] | SYN | **Every natively created event delivered to a C# `_Input` allocates ≈410–550 managed bytes** (wrapper creation + marshalling); dispatch to an existing wrapper costs 0 B. Accumulation merges 16 motions into 1. Order of magnitude: unaccumulated 1000 Hz mouse ≈ 408 KB/s gen-0; accumulated at 60 fps ≈ 24 KB/s. No gen-0 GC inside any 10 000-event run. | ADR item 8 / OQ-IC-17: C# wrappers **do** allocate. Router core zero-alloc budget unaffected; adapter budget ≈0.5 KB/event (D-8). Keep `UseAccumulatedInput=true`. |
| S-25 | **FAILED (open defect, not a NO-GO)** | `alloc.log` [S-25, S-25b/c/d] | SYN | `Performance` ObjectCount after 10 000 events + forced `GC.Collect` + `WaitForPendingFinalizers` + 2 frames: native **Key +9 999**, **MouseButton +10 000**, **MouseMotion +10 000**, engine-created touch→emulated mouse **+20 000** stay alive; JoypadMotion +0; C#-created Key +0; C#-created MouseButton +10 000 (the viewport-transformed copy). **Mitigation that worked:** `@event.Dispose()` at the end of `_Input` → Key and MouseButton back to **+0 after GC** (S-25c). Mechanism not established (ObjectCount shows retention, not the holder); candidate: the managed wrapper/binding created by C# virtual-call marshalling for engine-owned RefCounted events is not released. | Not in ADR-0012. Memory-growth risk for any C# `_Input` (≈1 native object per key/mouse event). D-8 + follow-up spike. |
| S-26 | NOT RUN (did not reproduce) | `logs/exitleak.log`, `exitleak.engine.txt`; first observation `logs/alloc-firstrun.engine.txt` | SYN | The **first** full alloc run (S-20 rows only, no final GC, receivers still in tree) ended with ≈7 943 lines `ERROR: Leaked unsafe reference to object: InputEventKey …` (`csharp_script.cpp:179`), then `FATAL: Condition "csharp_lang && !csharp_lang->script_bindings.is_empty()"` (`csharp_script.cpp:1220`) and an **abort with core dump**. Isolated reproduction (`exitleak`: 10 000 native keys, no GC, quit) exited cleanly. Not reproduced (time box). | For 029 and the follow-up spike. |
| S-21 | VERIFIED | `alloc.log` [S-21] | SYN | Headless window 64×64; content scale 320×180 (CanvasItems and Viewport) → factor 5. Injected `Relative=(36,18)` delivered as **(180,90)**; `ScreenRelative` stayed **(36,18)**; with stretch disabled both (36,18). Matches the class reference (`GodotSharp.xml`). | Confirms note 2 (look uses `ScreenRelative`). |
| S-22 | VERIFIED (flag round-trip) · NOT RUN (OS repeat) | `alloc.log` [S-22] | SYN | Injected `Echo=true` arrives `Echo=True`, `IsEcho()=True`, `IsPressed()=True`. OS key-repeat: NOT RUN (M-4). | §7 echo never produces edges — half verified. |
| S-23 | VERIFIED (headless) · NOT RUN (AZERTY) | `alloc.log` [S-23], `window-*.log` | SYN/WIN | Headless `KeyboardGetLabelFromPhysical(A/Q/W)` returns the same key (identity fallback, no error); `KeyboardGetLayoutCount=0`, `GetLayoutName(0)='Not supported'`. Windowed: US layout, identity labels, 'English (US)'. AZERTY: MANUAL M-5 (H-EC-11). | §7 physical keys + layout-label fallback — does not fail headless. |
| S-24 | NOT RUN (not M-1a) | `alloc.log` [S-24] | API | `AccessibilityServer` class exists; headless `HasFeature(AccessibilityScreenReader)=False`, `AccessibilityScreenReaderActive()=-1`. Screen reader end-to-end not run. | Recorded for completeness. |

**Open defect (S-25/S-26):** a follow-up spike is needed **before story 026 is marked Done** (not before it starts): Release/ExportRelease build, real keyboard + mouse; baseline ObjectCount and process RSS → 60 s mouse motion → forced GC → back to baseline? Compare with/without `Dispose()` in `_Input`, and a GDScript `_input` shim that forwards primitives to C#. If confirmed, report upstream to Godot. `Dispose()` on an event later receivers still use was **not** shown safe (the adapter would have to be the last C# receiver to touch the event, or copy fields and use a disposal pattern proven safe).

## Verdict

**GO-WITH-DELTAS.** The headline assumptions hold on Godot 4.7.1 .NET (synthetic evidence): arrival stamping in `_Input` works and is required (B1); a release always reaches `_Input` before any GUI node, even if the GUI owner was freed or hidden (S-04); `SetInputAsHandled()` in `_Input` stops the GUI and every later receiver (S-02), and the `FocusMode.None` + `GuiReleaseFocus()` fallback works (S-07); type-first `DeviceId*` classification separates every case incl. pads 16/32 (S-11). No row FAILED in a way that breaks arrival-stamped gathering; NO-GO criteria not met. S-06, B5, S-30, S-34, S-10, S-15 are CHANGED and S-20/S-25 add an allocation/retention budget — all handled by §7 text changes. Hardware rows stay NOT RUN and are collected by story 029.

*Caveats added at `/story-done` 2026-10-16 (LP-CODE-REVIEW CONCERNS, accepted):* S-25 is an **open FAILED defect with an unestablished mechanism** (and the S-26 abort/core dump did not reproduce) — it does not break arrival-stamped gathering, so it does not trigger NO-GO, but it **gates story 026 Done** via the follow-up retention spike; the `Dispose()` mitigation is not shown safe while later receivers still read the event (cf. D-3). D-1's runtime re-assertion (`MoveChild(this, -1)` from a `ChildEnteredTree` handler) was **not exercised** — only static placement was probed; per H-1 it likely needs `CallDeferred`; story 028 verifies it.

## ADR-0012 §7 deltas (proposals — apply via `/architecture-decision`)

- **D-1 (adapter placement; replaces Engine Validation note 3 "`ProcessPriority`/tree position pinned").** "`_Input`/`_UnhandledInput` run in **reverse tree order** and `ProcessPriority` does not affect input order (S-06). The adapter is the **last child of the root window** and re-asserts that (`MoveChild(this, -1)`) whenever a node enters the root (`GetTree().Root.ChildEnteredTree`), so it runs before scenes, menus and the dev overlay. `ProcessMode = Always`. Optional: `Window.WindowInput` fires before every `_Input` and may be used for arrival stamping only (cannot consume, S-06b/c)." *(Static last-child placement probed; the `ChildEnteredTree` re-assertion was not run — likely needs `CallDeferred` (H-1); verify in story 028.)* ADR-0019 placement must follow; its OQ1 autoload-first assumption does not carry over to input.
- **D-2 (mouse mode).** "The adapter treats `Input.MouseMode = Captured` as a *request*: re-applied on `NotificationApplicationFocusIn`/window focus-in and on the first click into the window, with readback checked. On native Wayland it is rejected while the window has no pointer focus (S-34w). On X11 capture warps the cursor to window centre and returning to `Visible` does **not** restore the previous position (S-34x); panels must not assume the cursor is where it was."
- **D-3 (handled symmetry).** "When the adapter consumes a press it also consumes that press's release, and vice versa; handling only a release after the GUI saw the press leaves a Control stuck pressed (S-02)."
- **D-4 (focus notifications).** "`NotificationApplicationFocusOut` reaches every node via `SceneTree` propagation; the WM variant is delivered to the `Window` only (S-30), so the adapter subscribes to its window's `FocusExited`/`FocusEntered`. Either triggers `OnFocusLost()` (idempotent). The engine clears its own pressed-key state on focus-out without delivering release events (S-33), so the router's synthesized releases stay mandatory. OS delivery per platform still UNVERIFIED (M-2)."
- **D-5 (`ui_*`, B5).** "The stock `ui_*` map binds joypad D-pad, left stick (±1.0), Y (`ui_select`) and X (`ui_colorpicker_delete_preset`) on device −1, and **no** pad button to `ui_accept`/`ui_cancel` (B5, S-08b). Game contexts keep Controls unfocused (already §7). Menus outside play that want pad A/B confirm/back handle them in the menu's own navigation owner." (GDD §192 `ui_select`/Y sentence is a GDD follow-up.)
- **D-6 (C# types).** "`InputEvent.Device` is `int`; `InputEvent.DeviceId*` constants are `long`. Compare directly; cast when assigning (`(int)InputEvent.DeviceIdEmulation`)."
- **D-7 (pad info).** "`Input.GetJoyInfo`/`GetJoyName` only for indices in `Input.GetConnectedJoypads()` (an unconnected index logs an engine error, S-15)."
- **D-8 (allocation + retention budget, §9).** "≈410–550 managed bytes per engine-created event delivered to C# `_Input` (Debug, S-20); `use_accumulated_input` stays true (16:1 fewer motion allocations). Engine-created key/mouse events delivered to C# `_Input` were observed to stay alive after a forced GC (S-25); `Dispose()` in `_Input` reclaimed them. Story 026 is not Done until the follow-up retention spike (Release, real input) confirms or clears this."
- **No change (confirmed):** arrival-ms stamping (B1); type-first classification + emulation-as-mouse (S-11, S-12); `ScreenRelative` for look (S-21); `FocusMode.None` + `GuiReleaseFocus()` (S-07); `use_accumulated_input` true (B4, S-20); `ignore_joypad_on_unfocused_application = true` (B3); axis re-seed via `GetJoyAxis` (S-14, synthetic); `ProcessMode = Always` (S-09); input events delivered before the frame's `_Process` (S-05).

## Proposed engine-reference additions (`docs/engine-reference/godot/`, not applied)

- `current-best-practices.md`: input callbacks (`_input`, `_unhandled_input`) run in reverse tree order and `process_priority` does not reorder them (4.7.1, S-06); `Window.window_input` fires before all `_input` but cannot mark input handled (S-06b/c); prefer `InputEventMouseMotion.screen_relative` for mouse look (S-21); classify input by event type before `device`, joypad indices 16/32 are accepted (S-11).
- `breaking-changes.md` / notes: default `ui_*` map has pad D-pad + left stick on `ui_left/right/up/down`, Y on `ui_select`, no pad on `ui_accept`/`ui_cancel` (4.7.1, B5); C# `InputEvent.Device` is `int`, `DEVICE_ID_*` are `long` (S-10); 4.7 runtime property `Input.ignore_joypad_on_unfocused_application` exists (B3/S-32).
- Known issues: Wayland `mouse_mode = captured` rejected while the pointer is not over the window (`pointed_win is null`, S-34w); C# `_input` with engine-created RefCounted events shows retention (S-25), under investigation.

## Consumed by

| Story | Relies on rows |
|---|---|
| 026 production `InputAdapter` node | B1, B2, S-01, S-01f, S-02, S-03, S-04, S-05, **S-06/S-06b/c (D-1)**, S-10 (D-6), S-11, S-12, S-13, S-14, S-17, S-20/S-25 (D-8, follow-up gate), S-21, S-22 |
| 027 mouse mode, GUI-focus policy, `project.godot` setting, focus/hot-plug notifications | B3, B5 (D-5), B6, S-07, S-08, S-15 (D-7), S-16, S-30 (D-4), S-31, S-32, S-33, S-34w/x (D-2), S-23 |
| 028 `GameLoop` hook-up | S-05, S-06 (root-last placement, D-1), S-09, H-1 (no `AddChild` to root during scene `_Ready`) |
| 029 adapter integration suite + MANUAL evidence | S-00/S-01f/S-17 (tests call `FlushBufferedEvents()`; delivery is synchronous inside it), every MANUAL row below, S-25/S-26 retention follow-up, S-08 mouse-click GUI check |

## MANUAL / NOT RUN (collected by story 029)

Build once with `./run_all_headless.sh`, then windowed: `mise exec dotnet@10.0.402 -- bash -c '~/.local/bin/godot-mono --path . --display-driver <wayland|x11> -- --probe=window'` (extend `WindowProbe` with a longer timer or use story 029's harness); record the console and `logs/`.

- **M-1 Real gamepad** (S-14, S-15, S-16, S-32, B5/S-08): Xbox-layout pad. (a) log every `InputEventJoypadMotion` while slowly moving the left stick then holding still 2 s — only on change? raw values within the deadzone delivered? (b) both triggers → `JoypadMotion` axes 4/5 in 0..1? (c) print `GetJoyInfo(0)` keys, `GetJoyName`, `GetJoyGuid`; (d) unplug/replug → `JoyConnectionChanged(device, connected)` both times, same index on reconnect? Second pad + unplug the first → index reuse? (e) `ignore_joypad_on_unfocused_application=true`, alt-tab away, press A → no event; (f) focused `Button` in a menu: D-pad moves focus, A does not activate (B5).
- **M-2 Focus loss, native Wayland and XWayland** (S-30, S-33, S-35): windowed `--display-driver wayland`, click in, hold W, alt-tab away by hand, wait 1 s, return; log which of `ApplicationFocusOut/In`, `WMWindowFocusOut/In` reached (a) a child node, (b) the root window's `FocusExited/Entered`, in order, and whether a W release reached `_Input`. Repeat with `x11`.
- **M-3 Mouse capture** (S-34): Wayland — click in, set `Captured`, confirm pointer lock + relative motion (`ScreenRelative`); alt-tab and back — still `Captured` or must it be re-applied?; set `Visible` — where is the cursor? Repeat on X11.
- **M-4 OS key repeat** (S-22): hold a key 2 s, log `Echo` per `InputEventKey` (expect press Echo=false, repeats Echo=true, release).
- **M-5 AZERTY** (S-23, H-EC-11): the human switches the compositor layout to French (`hyprctl keyword input:kb_layout fr`), restart the probe, log `KeyboardGetLabelFromPhysical` for physical A/Q/W/Z/M (expect Q/A/Z/W/','), check the layout name, restore the layout.
- **M-6 Mouse click on a GUI Control** (S-08): windowed, click a `Button`; log `_Input` → GUI order and whether `SetInputAsHandled` on the press in the root-last adapter prevents activation.
- **M-7 Steam Input** (S-11, H-EC-13): Steam build with Steam Input on — pad indices (any 16/32?), duplicate virtual + physical pads, glyph family. Needs Steam (M0 spike).
- **M-8 Retention follow-up** (S-25/S-26): see "Open defect".
- **M-9 Windows** (Verification item 5): focus notifications and capture; no Windows machine here.
- **NOT SOURCEABLE:** none needed — every row observed or NOT RUN with a reason. `docs/engine-reference/godot/` covered only `DEVICE_ID_*`; the rest came from probes, cross-checked against the installed `GodotSharp.xml` (`ScreenRelative`, `FlushBufferedEvents`).

## Housekeeping

- `prototypes/index.md` row appended by the implementer (links here).
- `README.md` written (per `.claude/rules/prototype-code.md`).
