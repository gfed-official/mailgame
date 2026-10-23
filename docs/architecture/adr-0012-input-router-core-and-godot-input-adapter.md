# ADR-0012: Input Router Core and Godot Input Adapter

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously; engine check by `godot-specialist` (Step 5.5). TD-ADR skipped (lean mode). Acceptance requires the project owner's explicit confirmation (batch request by the main session). **Spike-gated (adapter half):** per architecture.md §Required ADRs, the Godot adapter decisions marked UNVERIFIED stay provisional until the OQ-IC-9 input spike is recorded under `prototypes/`; the router-core half can be accepted on its own. Inputs: `design/gdd/input-controls.md` R1–R12, G1–G9, F1–F13, Edge Cases, ACs, OQ-IC-9/17/18; `design/gdd/player-character-movement.md` P2, P11, P12, P14; `design/gdd/throwing-catching.md` T1, T3, T4, T7; `design/gdd/manual-delivery.md` MD4; `docs/architecture/architecture.md` §Client.Core.Input, §Godot input adapter, §Input (ADR-0012); ADR-0001, 0002, 0003, 0011 (Accepted); ADR-0014 (Proposed).

**Accepted 2026-10-16** by `technical-director` on the project owner's explicit confirmation (owner answer "Accept all 8 (Recommended)" to the main session's batch request for ADR-0012, 0013, 0014, 0015, 0016, 0017, 0019 and 0026; acceptance mode `.claude/skills/architecture-decision/references/0-accept.md`, dependency order 0013 → 0014 → 0012 → 0026 → 0017 → 0015 → 0016 → 0019). Scope of acceptance: the router core is Accepted outright. The Godot adapter rows marked UNVERIFIED stay provisional until the OQ-IC-9 input spike is recorded under `prototypes/`. Adapter stories wait for it; router-core stories may start.

## Summary
All gameplay input must pass one deterministic, headless-testable gate that knows contexts, latches presses, never loses or doubles an edge, and hands the Sim a per-tick struct, while Godot 4.7.1's input behaviour (no `InputEvent` timestamp, device-id constants new in 4.7, `ui_*` focus side effects) is partly unverified. This ADR fixes:
- a plain-C# **router core** in `PerformativeMail.Client.Core/Input/` (the `RouterState` tuple with data predicates, the per-press machine G1–G9, integer F3–F13 maths, the action map and validator, the shared event-log ring);
- a thin **Godot adapter** that arrival-stamps `_Input` events into abstract records;
- a Sim-defined `InputFrame` (ordered edges, absolute look, Q15 move), pushed by the router into the Sim-side ClientLayer `InputLatch` once per frame (before `RunFrame`), which emits one frame per client tick;
- immediate verbs dispatched on their event's frame;
- two clocks (Sim ticks for holds, integer ms for UI timing);
- a CI lint banning direct `Input.*` reads outside the adapter.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 .NET, C# |
| **Domain** | Input |
| **Knowledge Risk** | **HIGH** for the adapter (4.4–4.7 are post-cutoff; most input behaviour is NOT SOURCEABLE in `docs/engine-reference/godot/`). LOW for the router core and `InputFrame` (plain C#, no Godot reference, ADR-0001). |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md` (4.7: `InputEvent.DEVICE_ID_MOUSE` / `DEVICE_ID_KEYBOARD`, previously `0`), `deprecated-apis.md` (comparing `InputEvent.device == 0` is deprecated), `current-best-practices.md` ("compare device ids against the constants"); input-controls.md OQ-IC-9 (headless 4.7.1 non-Mono probe: no `InputEvent` timestamp; keyboard / mouse / emulation ids 16 / 32 / −1; `ignore_joypad_on_unfocused_application` default false; `use_accumulated_input` true; no joypad events on `ui_*`) — probe artifacts not yet committed, so these stay UNVERIFIED |
| **Post-Cutoff APIs Used** | `InputEvent.DEVICE_ID_KEYBOARD`, `InputEvent.DEVICE_ID_MOUSE` (4.7, VERIFIED in breaking-changes.md); C# names `InputEvent.DeviceIdKeyboard`/`DeviceIdMouse`/`DeviceIdEmulation` present in the installed 4.7.1 `GodotSharp.xml` (Step 5.5); behaviour per the spike |
| **Verification Required** | **OQ-IC-9 input spike (Godot 4.7.1 .NET, now installed at `~/.local/bin/godot-mono`), before adapter stories:** (1) `_Input` → GUI → `_UnhandledInput` order and `SetInputAsHandled` effect on GUI focus/`ui_accept`; (2) whether `_Input` events of a frame are delivered before that frame's `_Process` (needed for "immediate verbs leave on the event's frame", §6); (3) C# names of `DEVICE_ID_*` and the emulation id; (4) `Input.JoyConnectionChanged` signal and hot-plug index reuse; (5) `NotificationApplicationFocusOut`/`In` delivery on Linux (X11 and Wayland) and Windows; (6) `input_devices/joypads/ignore_joypad_on_unfocused_application` honoured; (7) `Input.MouseMode` capture/visible switching and cursor restore, Wayland capture; (8) per-event managed allocation of C# `InputEvent` wrappers (OQ-IC-17); (9) `DisplayServer.KeyboardGetLabelFromPhysical` (AZERTY labels); (10) joypad events on `ui_*` with a real pad; (11) OS key-echo flag (`InputEventKey.Echo`). Record results under `prototypes/` before the adapter stories. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (`PerformativeMail.Client.Core` net8.0 Godot-free; the Godot game project hosts the adapter; architecture tests and CI); ADR-0002 (`FrameRunner` runs `ClientLayer.Step(first = i == 0)` per tick; `TickDriver.Pause/Resume` for solo pause; one `GameLoop` caller); ADR-0003 (`ClientStepId.InputLatch`, `ClientLayer` never reads `SimWorld`); ADR-0006 (BAM angles, integer-only Sim types in `InputFrame`); ADR-0011 (`ClientAdapter` + loopback: immediate requests use the same encoder; reply events for pending commits); **ADR-0014** (`IInteractionQuery`/`InteractionSnapshot` the router reads; presented-target fields on edges; `holdEff` and holds live Sim-side) |
| **Enables** | ADR-0016 (ClientLayer `InputLatch` timing, presented snapshot), ADR-0018 (HUD reads prompts, glyph set and the shared UI event log), ADR-0019 (input adapter node placement in the scene/boot order), ADR-0024 (`settings.input` persistence, M1) |
| **Blocks** | Input & Controls epic (TR-input-001..010, 015..020, 022..024); every gameplay story that reads input; Godot adapter stories (until the OQ-IC-9 spike) |
| **Ordering Note** | Accept after ADR-0014. Router-core stories can start once this ADR is Accepted; adapter stories wait for the spike results (UNVERIFIED rows above). The M1 Controls screen (remap UI) is Menus' and ADR-0024's; the validator and schema it uses are decided here. |

## Context

### Problem Statement
Input & Controls makes input a rules system: a tuple of contexts with data predicates, per-press latching and muting (G1–G9), Toggle modes, a Back ladder, a remap validator, two clocks, and the guarantee that a sub-tick tap is never lost. Player Character, Manual Delivery and Throwing all consume its output and require that the Sim sees the same ordered edges on replay. Godot 4.7.1 offers polled reads that ignore "handled" state, `ui_*` actions that steal Space/Enter through GUI focus, no event timestamps, and new device-id constants. Without an architecture, gameplay code would read `Input.IsActionPressed` ad hoc, contexts would leak, and nothing would be testable headless.

### Constraints
- No Godot types in Sim or Client.Core (ADR-0001); the Sim may not read wall clocks (ADR-0006); `ClientLayer` cannot reference Client.Core (dependency points Godot → Client.Core → Sim).
- One `GameLoop` drives `FrameRunner`; nothing else calls into `ClientLayer`/`SimWorld` (ADR-0002).
- Host requests use the same codec as remote (ADR-0011).
- Router core: 0 B steady-state; ≤ 0.1 ms per frame (≤ 0.3 ms at a 1000 Hz burst) (H-PERF-1..3).

### Requirements
TR-input-001..010, 015..020, 022..024 (19 requirements; see GDD Requirements Addressed). TR-input-011/012 (`holdEff`, cancel range) are ADR-0014's.

## Decision

### 1. Split: router core (Client.Core) · adapter (Godot) · `InputFrame` (Sim)
- **Router core** — `PerformativeMail.Client.Core/Input/`, plain C#, no Godot reference, fed only `RawInputRecord`s and an injected `IInputClock` (integer monotonic ms). Owns `RouterState`, live predicates, the per-press state machines, Toggle translation, the Back ladder, the one-commit gate, deadzone/trigger/repeat/step maths, look accumulation, device and glyph tracking, the action map, the conflict validator, `settings.input` parsing and validation, and the event log. Every rule in R2–R11 is testable headless with a fake clock (`ms = floor(tick·1000/30) + subTickOffset`).
- **Godot adapter** — `src/Game/Input/InputAdapter.cs`, one node, the **only** code that touches `Godot.Input`, `InputEvent*`, `Input.MouseMode` and joypad signals (§7).
- **`InputFrame`** — a blittable struct in `PerformativeMail.Sim/ClientLayer/` (Sim types only), assembled by the Sim-side ClientLayer `InputLatch` (simulation-core story 023) from what the router pushes through the Sim-defined writer `IInputLatchWriter` (§4). Client.Core calls a Sim interface, so the dependency direction holds, and the batch rule is Sim-tested.

### 2. The router state and the action map (R1, R2, R10)
- **`RouterState`** is the tuple `(capture, overlay, body, holding, aiming, submenu)`, packed into a `ushort RouterStateId` (3 × 7 × 4 × 2 × 2 × 3 = 1008 values). `capture`, `overlay`, `submenu` and `aiming` are router-owned. `body` (OnFoot/Build/Mounted/Dead) and `holding` are read each frame from the ClientLayer-published carrier status (life state, mount, cursor non-empty; ADR-0014/0016).
- **Live predicates are data:** one bitmask per layer (`captureMask`, `overlayMask`, `bodyMask`, `holdingMask`, `aimingMask`, `submenuMask`); an action is live iff the current value's bit is set in every mask. **Reachable states** are data too: the R2 clauses as a list of `(antecedent masks → consequent masks)` rules. The validator and H-R2-8's golden list read the same data.
- **`ActionDef`** (`ActionId` u16 stable, string id, grammar, `LivePredicate`, `Delivery` Frame/Immediate, `FrameAction` bit (if Frame), default KBM ×2 / pad ×2 bindings, Reserved, Required, owner GDD, alias ids). M-1a ships a code-built `BuiltInActionMap` (like `BuiltInContent`), validated at boot by the same validator (H-R1-1). Game bindings are **physical inputs** in Client.Core's own `PhysicalInput` enum (stable names; the adapter maps Godot `Key`/`MouseButton`/`JoyButton`/`JoyAxis` to it). **Godot's `InputMap` is not used for game actions at all.**
- **Conflict validator (R10):** for every reachable `RouterState` × physical input (modifiers included, after most-specific-chord-wins, Back counted once), at most one live action, and no co-live Tap + Hold on one input except Interact (H-R4-1). It runs at load (M-1a, defaults) and on every remap (M1). O(reachable states × bindings), < 5 ms at load, outside the frame budget.

### 3. Per-press machines, latching and muting (R3, G1–G9)
- One `PressMachine` per `(device, physical input)` in a preallocated table: `Idle → Pressed(latched action, verb class, RouterStateId) → {Fired | HoldActive | Aiming | Active | Muted}`. Everything is resolved and **latched at the press edge** (G1). A state change that makes the latched action non-live **mutes** the input until release (G3); the release is swallowed. An action that becomes live while its button is down needs a fresh press.
- **Action-level aggregation.** Held state is the OR across devices. A second device pressing the same action produces no edge; the action's press belongs to the first input, whose release ends it even if another is still down (H-EC-2). Duplicate pad edges within `padEdgeCollapseMs` (20) collapse (H-EC-13).
- **Toggle mode (G7) is translated here.** For a Hold or Channel action in Toggle mode, the first press emits a logical `Press` and its physical release emits nothing; the second press emits a logical `Release` (consumed, never re-resolved). For Throw aim, the first press enters `aiming` and the second commits if `aimHeld ≥ throwMinAim`, else it is ignored (H-G7-2). For Sprint, held bits latch on/off and unlatch on F8 (neutral for ≥ 8 consecutive client ticks, read from the ClientLayer-published tick counter), death, mount, swim or panel open. Back on a running toggled hold emits `Cancel(Back)` (ladder step 3b). The Sim sees only logical press/release/cancel; it never knows the mode.
- **Interact** latches the target from the **presented** interaction snapshot (ADR-0014 §7): the press edge carries `(TargetEntity, TargetSlot, PresentedSeq)`. Resolution (catch → verb → armed) and hold timing happen Sim-side (ADR-0014 §3–§4); the router does not time holds.
- **Aim-release (Throw)** is router-owned on the ms clock (F11): release with `aimHeld ≥ throwMinAim` commits; earlier, or a hitch-batched press+release with `aimHeld ≈ 0`, cancels with "Hold to aim".
- **One-commit gate (G9, TR-input-023).** A commit (deliver hold, deconstruct, throw) is *in progress* from its press (router aiming, or the snapshot's `Holding` state) and *pending* from send until its reply (`ReplyObserved(reqId)` from the ClientAdapter) or `pendingCommitTimeout` (500 ms, ms clock). Meanwhile a second commit press is muted with "Busy" (then "Release to retry"), and item-moving taps (Drop, quick-move) are not live. Movement, Jump, ping, the comms wheel and PTT are never gated.
- **Back ladder (R5)** is one router action with ordered handlers; the first applicable handler consumes the press.

### 4. The `InputFrame` and how ticks consume it (R11, TR-input-008/009)
```csharp
namespace PerformativeMail.Sim.ClientLayer;
public struct InputFrame                          // ≈ 560 B, blittable, passed by `in`
{
    public uint Tick;
    public short MoveX, MoveY;                    // Q15 unit disc after deadzone (F3); x right, y forward (camera-relative)
    public ushort LookYaw;                        // BAM, absolute (never a delta)
    public short LookPitch;                       // BAM signed, clamped by the camera owner
    public short FocusCosMinPm;                   // focus cone threshold from the camera FOV/aspect (ADR-0014 §2)
    public ushort RouterStateId;                  // informational
    public InputFrameFlags Flags;                 // FirstInBatch, FocusLost, Paused, LookReseeded, PanelOpen (erratum 2026-10-21)
    public ulong HeldBits;                        // logical held state per FrameAction (OR across devices, mute/toggle applied)
    public byte EdgeCount, StepCount; public ushort DroppedEdges;
    public EdgeBuffer Edges;                      // [InlineArray(32)] InputEdge — ordered by arrival
    public StepBuffer Steps;                      // [InlineArray(4)]  InputStep {FrameAction, sbyte n} — saturating (F6)
}
public struct InputEdge                           // 16 B
{ public FrameAction Action; public EdgeKind Kind; public byte Reason; public byte DeviceClass;
  public ushort RouterStateId; public ushort ArrivalSeq; public uint TargetEntity; public byte TargetSlot;
  public byte Reserved; public ushort PresentedSeq; }
public enum EdgeKind : byte { Press = 1, Release = 2, Cancel = 3 }
public enum FrameAction : byte { Interact, Jump, Sprint, Aim, /* … ≤ 64, appended only */ }
public interface IInputLatchWriter                // implemented by the ClientLayer InputLatch (Sim)
{ void PushEdge(in InputEdge e); void PushSteps(FrameAction a, int n); void SetState(in InputState s); } // InputState = move, look, held bits, cone, RouterStateId, flags
```
- **Arrival.** The adapter stamps each event with `IInputClock.NowMs` in `_Input` (Godot 4.7.1 `InputEvent` has no timestamp — probe; stamps are frame-granular) and feeds it to the router at once. Edges get a wrap-safe `ArrivalSeq` and the `RouterStateId` they were gated against **at event time**. A Jump pressed just before Tab is therefore a World edge (H-R11-1). Edges queue in a preallocated 256-entry ring.
- **Hand-over and consumption (push, matching simulation-core story 023).** `GameLoop._Process` calls `InputRouter.FlushToLatch(IInputLatchWriter)` immediately before `FrameRunner.RunFrame()` (after `DispatchImmediates`, §6): the router pushes every edge queued since the last flush, in arrival order, plus the current state (axes, look, held bits, focus cone, router state) and the frame's saturated steps. ClientLayer's `InputLatch` step then emits one `InputFrame` per client tick: on the tick with `IsFirstTickInBatch` (ADR-0002's `i == 0`) it carries **all** latched edges and steps and clears them; later ticks of the batch carry state only (R11). If no tick runs this frame, nothing is cleared (G4). State is constant across a batch because no events arrive between its ticks.
- **Edge overflow** (> 32 latched before one emitted frame, e.g. a long hitch with key-mashing): the router coalesces same-action level presses before pushing; if the latch is still full, the newest *press* edges are dropped (never a release or cancel), counted in `DroppedEdges` and the event log.
- **Order of work in a tick** (Sim side, ADR-0014): edges are processed before hold accrual. A release in a catch-up batch is therefore seen before further hold ticks accrue (H-EC-4).
- **The Sim never sees milliseconds.** Holds, the cancel range and F8 are counted in ticks; F5, F7, F9, F11, F12, F13, G9 and the 20 ms pad collapse use the router's ms clock. The two are never mixed in one formula (R11 two clocks, TR-input-010).
- The frame feeds the **owning client's** ClientLayer only (`InputLatch` → `CarrierStep`); it is **not** drained by `SimWorld` phase 0 (`Inputs.Apply` drains ops). Replay records frames.

### 5. Integer maths in the core (F3–F13, TR-input-017)
- Settings are stored in integer units: permille (`dzInnerPm`, `dzOuterPm`, `trigOnPm`, `curvePm`), ms (`holdSettingMs`, `throwMinAimMs`, `repeatDelayMs`, `repeatIntervalMs`, `backSkipMs`, `previewSettleMs`), counts and px. F3's radial deadzone works on Q15 axis values with `ISqrt` magnitude; the look response curve `n^curve` uses a 1024-entry Q15 power table per curve value, rebuilt on settings change (outside the frame). F4 trigger hysteresis, F5 repeat (`1` or `2 + (T − delay)/interval`, ≤ 1 step per rendered frame), F6 saturation, F7 click-or-drag, F9 glyph flips (cooldown 400 ms), F11 and F13 are integer comparisons. Look accumulates mouse counts × sensitivity into BAM with a carried remainder. `SetLook(yaw, pitch)` re-seeds on Teleport, mount and dismount.
- Every value is clamped on load (NaN impossible: integers; missing → default), per R10 and Edge Cases.

### 6. Immediate verbs (R1 `Immediate`, R11 bypass list, H-R11-2)
- On an `Immediate` action's firing edge, the router appends an `ImmediateAction { ActionId, EdgeKind, ArrivalMs, RouterStateId, PresentedSeq }` to a preallocated queue. **`GameLoop._Process` calls `InputRouter.DispatchImmediates()` immediately before `FrameRunner.RunFrame()`.** Dispatch invokes the registered `IImmediateActionSink` for the action (the verb owner's client code: Throwing's release builder, inventory ops, ping, chat). The sink builds its request from ClientLayer's published state (ADR-0016) and sends it through `ClientAdapter` → loopback. `RunFrame` step 1 (`DrainInbound`) then enqueues it **in the same frame**. This makes "sent on the event's frame" independent of whether Godot delivers `_Input` before `_Process` (spike item 2).
- The router never builds payloads; it knows nothing of throw directions or op layouts. Bypass requests are logged with their issuing tick (H-FX-1 replay corpus).

### 7. The Godot adapter (amended 2026-10-17 by the OQ-IC-9 spike deltas D-1…D-8)
- **One `InputAdapter` node**, created by the host shell before views. **Placement (D-1):** `_Input`/`_UnhandledInput` run in **reverse tree order** and `ProcessPriority` does not affect input order (S-06). The adapter is the **last child of the root window** and re-asserts that (`MoveChild(this, -1)`) whenever a node enters the root (`GetTree().Root.ChildEnteredTree`), so it runs before scenes, menus and the dev overlay. `ProcessMode = Always`. Optional: `Window.WindowInput` fires before every `_Input` and may be used for arrival stamping only (cannot consume, S-06b/c). *(Static last-child placement probed; the `ChildEnteredTree` re-assertion was not run — likely needs `CallDeferred` (H-1); verify in story 028.)* ADR-0019 placement must follow; its OQ1 autoload-first assumption does not carry over to input. In `_Input(InputEvent e)` it converts the event into a `RawInputRecord` (kind, `DeviceClass` + pad index, `PhysicalInput`, pressed/released, axis Q15 value, mouse relative counts, echo flag, arrival ms) and calls `router.Feed(in record)`. In game contexts it calls `GetViewport().SetInputAsHandled()` for consumed game events. OS key-repeat (echo) events never produce edges (G4). Mouse-wheel notches are steps.
- **Handled symmetry (D-3).** When the adapter consumes a press it also consumes that press's release, and vice versa; handling only a release after the GUI saw the press leaves a Control stuck pressed (S-02).
- **Devices.** Keyboard and mouse are identified by comparing `InputEvent.Device` against `DEVICE_ID_KEYBOARD` / `DEVICE_ID_MOUSE` (VERIFIED 4.7), never `0`; the emulation id (−1, probe) counts as mouse. Synthetic `InputEventAction`s (tests, router internals) never change the last-used device (H-EC-12). Joypad motion arrives one axis per event, so the adapter keeps a per-device axis cache (the only polled-style state).
- **C# types (D-6).** `InputEvent.Device` is `int`; `InputEvent.DeviceId*` constants are `long`. Compare directly; cast when assigning (`(int)InputEvent.DeviceIdEmulation`).
- **Pad info (D-7).** `Input.GetJoyInfo`/`GetJoyName` only for indices in `Input.GetConnectedJoypads()` (an unconnected index logs an engine error, S-15).
- **`ui_*` and GUI focus.** The `ui_*` InputMap is never mutated (no `InputMap` writes in game code). HUD and in-world Controls use `FocusMode = None`; entering a game context calls `GuiReleaseFocus()`. Menus outside play keep stock `ui_*` navigation. One navigation owner per context (the custom grid consumes its own navigation).
  - **Stock `ui_*` (D-5, B5).** The stock `ui_*` map binds joypad D-pad, left stick (±1.0), Y (`ui_select`) and X (`ui_colorpicker_delete_preset`) on device −1, and **no** pad button to `ui_accept`/`ui_cancel` (B5, S-08b). Game contexts keep Controls unfocused (as above). Menus outside play that want pad A/B confirm/back handle them in the menu's own navigation owner. (GDD §192 `ui_select`/Y sentence is a GDD follow-up.)
- **Project settings:** `input_devices/joypads/ignore_joypad_on_unfocused_application = true` (probe: present, default false). `use_accumulated_input` is left at its default (true); the router is correct either way (H-PERF-2 tests it off).
- **Mouse mode:** `Input.MouseMode = Captured` in body states with look, `Visible` in Panel, Map, Station, Draft, History, Pause and TextEntry, switched by the adapter on router-state change. **(D-2)** The adapter treats `Input.MouseMode = Captured` as a *request*: re-applied on `NotificationApplicationFocusIn`/window focus-in and on the first click into the window, with readback checked. On native Wayland it is rejected while the window has no pointer focus (S-34w). On X11 capture warps the cursor to window centre and returning to `Visible` does **not** restore the previous position (S-34x); panels must not assume the cursor is where it was.
- **Focus loss (D-4).** `NotificationApplicationFocusOut` reaches every node via `SceneTree` propagation; the WM variant is delivered to the `Window` only (S-30), so the adapter subscribes to its window's `FocusExited`/`FocusEntered`. Either triggers `OnFocusLost()` (idempotent). The engine clears its own pressed-key state on focus-out without delivering release events (S-33), so the router's synthesized releases stay mandatory. OS delivery per platform still UNVERIFIED (M-2). `router.OnFocusLost()` synthesizes release-with-cancel for every held input, zeroes axes, mutes everything until released, swallows the recapturing click, unlatches toggled sprint and mic, and — in solo, setting on — asks the host shell to `TickDriver.Pause()` (ADR-0002). Pause in multiplayer is a local capture state; the Sim keeps ticking with neutral frames; `SceneTree.Paused` is never used.
- **Hot-plug** (`Input.JoyConnectionChanged`, UNVERIFIED): a disconnect synthesizes releases (cancels holds and aim, never a commit); in solo, auto-pause if that pad was the last-used device; in multiplayer a "Controller disconnected" banner. A reconnected pad's held buttons are muted until released.
- **Allocation and retention budget (D-8, §9).** ≈410–550 managed bytes per engine-created event delivered to C# `_Input` (Debug, S-20); `use_accumulated_input` stays true (16:1 fewer motion allocations). Engine-created key/mouse events delivered to C# `_Input` were observed to stay alive after a forced GC (S-25); `Dispose()` in `_Input` reclaimed them. Story 026 is not Done until the follow-up retention spike (Release, real input) confirms or clears this.
- **Physical keys** are stored by Client.Core's stable `PhysicalInput` names (never raw Godot enum ints); prompt labels come from the layout-label API (UNVERIFIED, spike item 9) with the physical name as fallback.

### 8. One gate, enforced (TR-input-002, TR-input-007)
- **CI lint** `tools/ci/lint-input-reads.sh` (grep-based, runs in the build job): any of `Input.IsAction*`, `Input.GetActionStrength`/`GetActionRawStrength`, `Input.GetAxis`, `Input.GetVector`, `Input.IsKeyPressed`/`IsPhysicalKeyPressed`/`IsKeyLabelPressed`, `Input.IsMouseButtonPressed`, `Input.IsJoyButtonPressed`, `Input.GetJoyAxis`, `Input.IsAnythingPressed` and any `InputMap.` mutation in `src/Game/**/*.cs` outside `src/Game/Input/` fails the build. A committed planted-violation fixture proves the lint fires (H-R1-2). Sim and Client.Core cannot reference Godot at all (ADR-0001 architecture test).

### 9. Event log, settings file, performance
- **Event log (R11, TR-input-019).** One preallocated `UiEventLog` ring of 4096 entries × 64 B in `Client.Core` (shared with the UI event log Grid's H-UI-1 and HUD (ADR-0018) use; this ADR defines it as its first writer). Input writes: raw input (mouse motion coalesced to one entry per frame), each gate decision (fired / not live / muted / swallowed), each router-state transition, each hold start/complete/cancel with reason (copied from the interaction snapshot). The 4097th entry overwrites the oldest (H-R11-4). Enum ids only, no strings.
- **`settings.input` (R10, TR-input-020).** Versioned, overrides-only JSON with integer-unit fields; physical keys by stable name. M-1a: `user://input.json`, read as bytes by the adapter (`FileAccess`, ADR-0024 at M1) and parsed in Client.Core with a hand-written `Utf8JsonReader` walk (no reflection binding, matching ADR-0010's content stance). Unknown ids dropped and logged, malformed bindings → default, ranges clamped, migrations vN → vN+1, conflicts re-validated (new defaults lose to player overrides), required-unbound → that device resets to defaults. A malformed file loads defaults with one warning (H-R12-1).
- **Performance (TR-input-018).** Core: no allocation after warm-up (preallocated rings, machine tables, `ReadOnlySpan` action names) — H-PERF-3 BLOCKING at M-1a; ≤ 0.1 ms per frame typical and ≤ 0.3 ms at a 1000 Hz burst — ADVISORY. Adapter allocations from C# `InputEvent` wrappers are measured by the spike before the gate is extended to it.

### Architecture Diagram
```
 Godot main thread, one frame
  _Input(e) ─► InputAdapter (src/Game/Input) ── RawInputRecord + arrival ms ──► InputRouter.Feed   [Client.Core]
                 DEVICE_ID_* · axis cache · SetInputAsHandled · MouseMode · focus/hot-plug          │ RouterState tuple
                                                                                                     │ PressMachines, Toggle, Back,
  GameLoop._Process:                                                                                 │ one-commit gate, F3–F13
    InputRouter.DispatchImmediates() ─► IImmediateActionSink (verb owners) ─► ClientAdapter ─► loopback
    InputRouter.FlushToLatch() ─► IInputLatchWriter (Sim ClientLayer InputLatch: edges, state, steps)
    FrameRunner.RunFrame():  DrainInbound (same-frame requests enter the op ring)
      repeat n: ClientLayer.Step(first)                                                              ▲ reads IInteractionQuery
                  InputLatch ── emits InputFrame (all edges on first tick) ◄── pushed by InputRouter ┘ (presented snapshot,
                  CarrierStep (ADR-0013/0014 consume InputFrame)                                        carrier status)
                SimWorld.Tick()
  UiEventLog ring (4096 × 64 B, Client.Core) ◄── router gate decisions / transitions / hold lines ──► HUD, Dev overlay
```

### Key Interfaces
```csharp
namespace PerformativeMail.Client.Core.Input;
public readonly struct RawInputRecord { public readonly long ArrivalMs; public readonly RawKind Kind; public readonly DeviceClass Device;
    public readonly byte PadIndex; public readonly PhysicalInput Input; public readonly bool Pressed, Echo;
    public readonly short Value, Value2; }                                   // axis Q15 / mouse relative counts
public interface IInputClock { long NowMs { get; } }                         // monotonic integer ms (adapter: Stopwatch-based)
public sealed class InputRouter
{
    public InputRouter(ActionMap map, InputSettings settings, IInputClock clock, IInteractionQuery interaction,
                       ICarrierStatusSource carrier, UiEventLog log);
    public void Feed(in RawInputRecord r);
    public void FlushToLatch(IInputLatchWriter latch);      // GameLoop, before RunFrame
    public void DispatchImmediates();
    public void OnFocusLost(); public void OnFocusGained(); public void OnPadConnection(byte pad, bool connected);
    public void SetLook(ushort yaw, short pitch); public void SetViewParams(short focusCosMinPm);
    public void ReplyObserved(uint reqId); public void CommitSent(uint reqId, ActionId a);
    public RouterStateId State { get; } public GlyphSet Glyphs { get; } public LookState Look { get; }
}
public interface IImmediateActionSink { void OnImmediate(in ImmediateAction a); }
public static class ActionMapValidator { public static ValidationReport Validate(ActionMap m, in ReachableStates r); }
public static class InputSettingsCodec { public static InputSettings Parse(ReadOnlySpan<byte> utf8, ActionMap m, out SettingsLoadReport rep);
    public static int Write(in InputSettings s, Span<byte> utf8); }
```

### Implementation Guidelines
- Gameplay, HUD and view code **must** read input only through the router (snapshot, glyphs, look) or the `InputFrame`. Direct `Godot.Input` reads and `InputMap` mutation **must** occur only in `src/Game/Input/`; the CI lint **must** fail otherwise.
- The router core **must** live in Client.Core with no Godot reference and **must** be driven only by `RawInputRecord`s and `IInputClock`.
- Device identity **must** use `DEVICE_ID_KEYBOARD`/`DEVICE_ID_MOUSE`, never `device == 0`. Synthetic events **must never** change the last-used device.
- Every edge **must** be captured from events, arrival-stamped, gated against the router state at event time, queued in order, and delivered on the first tick of the next catch-up batch. Release and cancel edges **must never** be dropped.
- Presses **must** latch at the press edge and mute on a state change (G1, G3). Toggle modes **must** be translated into logical press/release/cancel in the router; the Sim **must never** branch on a mode.
- Holds **must not** be timed in the router (ADR-0014 owns the counter). Throw aim (F11) and UI timing **must** use the ms clock; the Sim **must never** receive milliseconds.
- Immediate verbs **must** be dispatched by `GameLoop` before `RunFrame` in the frame their event arrived, through the same `ClientAdapter` codec as remote clients.
- `SceneTree.Paused` **must never** be used; solo pause goes through `TickDriver.Pause` (ADR-0002).
- The `ui_*` InputMap **must never** be mutated; game Controls **must** use `FocusMode.None`.
- Router-core steady state **must** allocate 0 B.

## Alternatives Considered

### Alternative 1: Godot `InputMap` actions + polled `Input.IsActionPressed` in gameplay
- **Description**: Define game actions in the project InputMap and poll them in `_Process`/systems.
- **Pros**: Idiomatic Godot; editor remapping UI; least code.
- **Cons**: Polled reads ignore handled state (focus leaks); sub-frame taps are lost; contexts are ad hoc per caller; untestable headless; `ui_*` collisions (Space/Enter activate focused Controls); remap validation over contexts impossible.
- **Rejection Reason**: Fails TR-input-001/002/004/007 and G4.

### Alternative 2: Router inside the Sim assembly
- **Description**: Put the whole router in `PerformativeMail.Sim` so ClientLayer calls it directly.
- **Pros**: No port; one assembly.
- **Cons**: The router needs wall-clock ms (repeat, drag, glyph, aim, pending timeout), which the Sim bans (ADR-0006); input settings, glyphs and remap UI logic are not Sim concerns.
- **Rejection Reason**: Violates the Sim's clock ban; Client.Core exists for exactly this (architecture.md).

### Alternative 3: Godot `_UnhandledInput` only
- **Description**: Let GUI consume first; game reads leftovers.
- **Pros**: Natural GUI precedence.
- **Cons**: Focused Controls would eat Space/Enter in World; releases consumed by GUI are lost to the game (stuck held state).
- **Rejection Reason**: The router must see every event, including releases (R11 "a release is never lost when a UI node consumes it"), so it reads in `_Input`.

### Alternative 4: Immediate requests sent from inside `_Input`
- **Description**: Build and send the request in the event callback.
- **Pros**: Earliest possible send.
- **Cons**: Depends on unverified `_Input`/`_Process` ordering; reads ClientLayer state outside the single `GameLoop` call path (ADR-0002); harder to test.
- **Rejection Reason**: `DispatchImmediates` before `RunFrame` gives the same frame deterministically.

## Consequences

### Positive
- Every grammar rule (G1–G9, R2–R5, F3–F13) is an xUnit test with a fake clock; the Godot surface shrinks to one node.
- The Sim receives an ordered, replayable struct per tick; a sub-tick tap is never lost and never doubled.
- Context leaks (focus, `ui_*`, polled reads) are blocked by construction and by CI.

### Negative
- Bindings, remap and glyph logic are custom code instead of Godot's InputMap and editor.
- A Sim-side writer (`IInputLatchWriter`) and a carrier-status read are needed to respect the dependency direction.
- The adapter half stays provisional until the spike; some prompt-label and Steam behaviours remain MANUAL.

## Risks
- **Spike finds `_Input` events arriving after `_Process`, or `SetInputAsHandled` not stopping GUI.** *Mitigation:* `DispatchImmediates` already decouples ordering; GUI fallback is `FocusMode.None` + `GuiReleaseFocus`; amend §7 only.
- **C# `InputEvent` wrappers allocate per event (OQ-IC-17).** *Mitigation:* the 0 B gate covers the core only until measured; coalesce motion events; the adapter budget becomes its own advisory gate.
- **Wayland mouse capture / layout labels unsupported.** *Mitigation:* the physical-name fallback; MANUAL ACs H-EC-11; a Wayland note in the release checklist.
- **Edge-buffer overflow on extreme hitches.** *Mitigation:* press coalescing; drop presses never releases; counted and logged.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| input-controls.md | TR-input-001: router core plain C#, raw records + fake clock; adapter is Presentation | §1 |
| input-controls.md | TR-input-002: all reads via router; `Input.IsAction*` outside adapter fails CI | §8 |
| input-controls.md | TR-input-003: `RouterState` tuple; predicates as data; reachable-state golden list | §2 |
| input-controls.md | TR-input-004: per-press machine; latch at press; mute until release; ordered edges | §3, §4 |
| input-controls.md | TR-input-005: action map with stable ids, grammar, predicate, defaults, flags, owner; aliases | §2 |
| input-controls.md | TR-input-006: conflict validator over reachable states; most-specific chord | §2 |
| input-controls.md | TR-input-007: `ui_*` never mutated; `FocusMode.None`; consume in `_Input` | §7, §8 |
| input-controls.md | TR-input-008: arrival-stamped in `_Input`; edges on the first catch-up tick | §4 |
| input-controls.md | TR-input-009: `InputFrame` Sim struct (tick, int16 move, absolute look, held bits, ordered edges, steps) | §4 |
| input-controls.md | TR-input-010: two clocks | §4, §5 |
| input-controls.md | TR-input-015: `DEVICE_ID_*`; hot-plug via `joy_connection_changed` (UNVERIFIED) | §7 |
| input-controls.md | TR-input-016: `ignore_joypad_on_unfocused_application = true`; per-device axis cache | §7 |
| input-controls.md | TR-input-017: deadzone/trigger/repeat/step caps integer; ≤ 1 step per frame | §5 |
| input-controls.md | TR-input-018: ≤ 0.1 ms/frame, ≤ 0.3 ms at 1000 Hz, 0 B steady | §9 |
| input-controls.md | TR-input-019: preallocated 4096-entry ring shared with the UI log | §9 |
| input-controls.md | TR-input-020: `settings.input` versioned, overrides-only; M-1a `user://input.json` | §9 |
| input-controls.md | TR-input-022: focus loss — release-with-cancel, zero move, mute, swallow click, solo auto-pause | §7 |
| input-controls.md | TR-input-023: one-commit rule; `pendingCommitTimeout` 500 ms | §3 |
| input-controls.md | TR-input-024: one carrier per machine; devices OR-ed; 20 ms pad collapse | §3 |
| player-character-movement.md | P2 per-edge latching; P11 presented snapshot; `SetLook` | §3, §4, §5 |
| throwing-catching.md | T1/T4 release on the event frame; F11 minimum aim | §3, §6 |

## Performance Implications
- **CPU**: Core ≤ 0.1 ms/frame typical (event feed + one `FlushToLatch` per frame); validator only at load/remap.
- **Memory**: `UiEventLog` 256 KiB; press machines and queues < 32 KiB; `InputFrame` ≈ 560 B per tick (recorder copies at M-1a ≈ 17 KiB/s).
- **Load Time**: Action map validation < 5 ms at boot.
- **Network**: None directly; immediate requests use ADR-0011's codec; no hold durations or per-tick input stream on the wire.

## Migration Plan
No code exists. Stories: (1) `InputFrame`, `InputLatch` and `IInputLatchWriter` in the Sim ClientLayer folder (simulation-core story 023 builds the latch; it adopts this ADR's field widths, replacing its PROVISIONAL marker); (2) Client.Core `Input/` core (router state, machines, maths, action map, validator, settings codec, `UiEventLog`) with the R/G/F ACs; (3) after the OQ-IC-9 spike: the Godot `InputAdapter`, project setting, CI lint and integration ACs.

## Validation Criteria
- All M-1a [Logic] ACs in input-controls.md R1–R12, Formulas and Edge Cases pass headless against the core; H-PERF-3 shows 0 B.
- H-R1-2 lint fails on the planted violation; H-R9-2, H-R11-3, H-EC-5/6/11/12 pass through the adapter after the spike.
- H-FX-1 replay corpus shows zero misfires.

## GDD Impact (no GDD edited by this ADR)
- **input-controls.md R11 / Dependencies / Interactions "the input frame is drained by `Inputs.Apply(tick)` on the owning client":** the router pushes into the ClientLayer `InputLatch` (simulation-core story 023), which emits the frame consumed in `CarrierStep` (ADR-0003 §6, ADR-0014 §7); `Inputs.Apply` (phase 0) drains ops only.
- **input-controls.md F1 implementation note ("`holdSetting` stored as a count of 50 ms steps"):** stored as `holdSettingMs` (integer ms, multiple of 50), the unit Manual Delivery F2 and `Hold.HoldTicks` use.
- **input-controls.md R11 throw-release fields:** see ADR-0015 (adds `units`, `side`).
- **input-controls.md OQ-IC-9 / OQ-IC-17:** become this ADR's spike list (Engine Compatibility).
- **Godot InputMap:** game actions are not InputMap actions (Client.Core bindings); only menus use stock `ui_*`.

## Engine Validation (Step 5.5, godot-specialist, 2026-10-16)
**Verdict: no BLOCKING findings; the approach is idiomatic for 4.7.1 C#.** The specialist ran a headless probe on the installed `godot-mono` 4.7.1 (scripts under `/tmp`, nothing committed) and read member names from the bundle's `GodotSharp.xml`:
- **Probe output:** `KB=16 MOUSE=32 EMU=-1`; default `device` of new `InputEventAction` = 0, `InputEventKey` = 16, `InputEventMouseButton` = 32, `InputEventJoypadButton` = 0; `ignore_joypad_on_unfocused_application` = false; `InputEvent` has no `get_timestamp`; `Input` has signal `joy_connection_changed`; `NOTIFICATION_APPLICATION_FOCUS_OUT` = 2017. *These must still be committed under `prototypes/` (OQ-IC-9) before they are cited as VERIFIED in stories.*
- **C# names present in `GodotSharp.xml` (names verified; behaviour still per spike):** `InputEvent.DeviceIdKeyboard` / `DeviceIdMouse` / `DeviceIdEmulation`, `InputEvent.Device`, `IsEcho()`, `InputEventKey.Echo` / `PhysicalKeycode`, `Input.JoyConnectionChanged`, `Input.MouseMode` (`Input.MouseModeEnum.Captured` / `Visible`), `Input.UseAccumulatedInput`, `Viewport.GuiReleaseFocus()` / `SetInputAsHandled()` / `IsInputHandled()`, `DisplayServer.KeyboardGetLabelFromPhysical(Key)`, `Node.NotificationApplicationFocusOut` / `NotificationApplicationFocusIn` (and `NotificationWMWindowFocusOut`). Spike items 3 and 9 are resolved **for names**.

**Minor notes, adopted:**
1. **Synthetic events.** `InputEventAction` and joypad events both default to device 0. The adapter **must classify by event type first** (an `InputEventAction` never changes the last-used device, H-EC-12) and never index arrays by `Device` before classification. A pad id colliding with 16/32 is UNVERIFIED (Steam virtual pads) — added to the spike.
2. **Mouse look** uses `InputEventMouseMotion.ScreenRelative` (unaffected by viewport stretch), not `Relative`, so `mouseFlipPx` and sensitivity do not depend on window scale.
3. **Adapter ordering** *(superseded 2026-10-17 by §7 D-1)*. `SetInputAsHandled` stops later `_Input` receivers and the GUI. Input callbacks run in reverse tree order and `ProcessPriority` has no effect on input order, so the adapter is the last child of the root window (re-asserted on `ChildEnteredTree`) so that it sees every event (releases included) before menus or the dev overlay can handle it, and `ProcessMode = Always`. ADR-0019 placement follows §7.
4. **Spike additions:** the full phase order `_Input` → GUI → `_ShortcutInput` → `_UnhandledKeyInput` → `_UnhandledInput` (Control shortcuts and accessibility actions can fire in between); both `NotificationApplicationFocusOut` and `NotificationWMWindowFocusOut` are handled (either one triggers `OnFocusLost`); `UseAccumulatedInput` on/off with motion events per frame; `Input.ParseInputEvent` is buffered one frame, so injected-event tests call `Input.FlushBufferedEvents()`; mouse-mode re-apply after focus-in (capture is commonly lost on alt-tab); per-event managed allocation of `InputEvent` wrappers measured with `GC.GetAllocatedBytesForCurrentThread`.
5. **Joypad axes** arrive raw (not deadzoned), per axis, only on change, and triggers are axes. On focus-in and reconnect, the adapter re-seeds its axis cache by polling `Input.GetJoyAxis`; this is the lint's only polled-read exemption, and it stays inside `src/Game/Input/`.

## Related
- ADR-0001 (Client.Core, architecture tests), ADR-0002 (`FrameRunner`, first-tick flag, pause), ADR-0003 (ClientLayer steps), ADR-0011 (ClientAdapter/loopback), ADR-0014 (interaction snapshot, holds, `holdEff`), ADR-0015 (throw release payload), ADR-0016 (ClientLayer timing, presented snapshot), ADR-0018 (HUD, shared event log), ADR-0019 (adapter node placement), ADR-0024 (settings persistence).
- `design/gdd/input-controls.md`.

## Amendments
- 2026-10-17 — accepted by owner — D-1: adapter is the last child of the root window, re-asserted on `ChildEnteredTree`; input runs in reverse tree order and `ProcessPriority` has no effect; `ProcessMode = Always`; `Window.WindowInput` for arrival stamping only (replaces Engine Validation note 3; ADR-0019 placement follows); source TD-021.
- 2026-10-17 — accepted by owner — D-2: `MouseMode = Captured` is a request, re-applied on focus-in and first click with readback; Wayland rejects without pointer focus; X11 does not restore the cursor; source TD-021.
- 2026-10-17 — accepted by owner — D-3: handled-state symmetry (consume press ⇔ consume release); source TD-021.
- 2026-10-17 — accepted by owner — D-4: subscribe to the window's `FocusExited`/`FocusEntered` (WM focus reaches the Window only); either triggers idempotent `OnFocusLost()`; synthesized releases stay mandatory; source TD-021.
- 2026-10-17 — accepted by owner — D-5: stock `ui_*` binds pad D-pad/left stick/Y/X on device −1 and nothing to `ui_accept`/`ui_cancel`; menus handle pad A/B in their own navigation owner; source TD-021.
- 2026-10-17 — accepted by owner — D-6: `InputEvent.Device` is `int`, `DeviceId*` constants are `long`; source TD-021.
- 2026-10-17 — accepted by owner — D-7: `GetJoyInfo`/`GetJoyName` only for connected indices; source TD-021.
- 2026-10-17 — accepted by owner — D-8: ≈410–550 B per engine-created event to C# `_Input`; `use_accumulated_input` true; retention observed, `Dispose()` in `_Input` reclaims; story 026 gated on the Release retention spike; source TD-021.
- 2026-10-21 — owner ruling (Evan, 07:42 PT; recorded by the planning lead) — erratum, `InputFrameFlags.PanelOpen`: panel-open travels in the `InputFrame` (the Panel channel left open for player-character story 020). `InputFrameFlags` gains one member, `PanelOpen`, appended under the enum's append-only rule. It takes the next free bit of the existing one-byte field, so `sizeof(InputFrame)` and the §4 layout are unchanged. The input side sets the bit from its record of the panel UI, the router's `overlay` layer: the bit is set while `overlay` is Panel. The router owns `overlay` (§2) and the Godot adapter only feeds it events (§7). The router pushes the bit in `InputState.Flags` at `FlushToLatch`, and `InputLatch` copies it into every tick's frame like the other state fields. It is the contract that ADR-0014's `CarrierStatus.PanelOpen` reads (ADR-0014 erratum 2026-10-21); `RouterStateId` stays informational. The flag carries the same fact as `RouterStateInfo.IsPanel(RouterStateId)`. §4's last bullet is unchanged: the frame feeds the owning client only, so getting panel-open to the host is ADR-0020's (not written); frame recording for replay is ADR-0006 §7 / simulation-core story 026's. Stories: input-controls 008 (alone appends the member and sets the bit; tests it against `IsPanel`), player-character 020 (reads it).
- 2026-10-23 — accepted by owner — **E1** (M-1a scope): Validation Criteria (l.280–282) and H-PERF-3 (l.144) now read: "At M-1a, the R1–R12 [Logic] ACs the playable path needs pass headless. Pad hot-plug and AZERTY/keyboard layouts (H-EC-5/6/11), glyph switching (H-EC-12), R8 mouse-free panel navigation, R12 rebinding, the H-R1-2 lint and the H-FX-1 replay corpus move to M0 (perf, CI, hot-plug) or M1 (accessibility, layouts, rebinding)." H-PERF-3 is advisory at M-1a and blocking from M0 (see E2). The deferred stories are input-controls 011, 022, 024, 025 and 029, plus the split-out glyph, hot-plug and rebinding halves of 017, 018, 019 and 027. Evan, Mail Lead chat, 2026-10-23 8:10 AM PT, approving draft errata E1–E6 and E9 (decision log 2026-10-23, m1a-replan): "approve all". Context: 7:15 AM PT "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example".
- 2026-10-23 — accepted by owner — **E2** (0 B gates): per-tick and per-frame zero-allocation checks are reported, not blocking, until M0. Evan, Mail Lead chat, 2026-10-23 8:10 AM PT, approving draft errata E1–E6 and E9 (decision log 2026-10-23, m1a-replan): "approve all". Context: 7:15 AM PT "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example".
