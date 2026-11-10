# Story 026: The Godot `InputAdapter` node — `_Input` gathering, arrival stamping, `DEVICE_ID_*` classification and the per-device axis cache

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Complete (root-last placement per ADR-0012 D-1, not the AC's Main/Input slot → TD-240 (56); D-8 / TD-022 retention gate still applies)
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M (~1 day; ~500 lines prod + smoke harness)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-11-09 (M-1a package cleanup: Complete (root-last placement per ADR-0012 D-1, not the AC's Main/Input slot → TD-240 (56); D-8 / TD-022 retention gate still applies); delivered by PR #90, merge 7b4b6d2; see Completion Notes). Earlier: 2026-10-23 (dependencies re-pointed by the m1a-replan PR)

> **Unblocked 2026-10-16 (story 001 Complete, OQ-IC-9 spike verdict GO-WITH-DELTAS — satisfies the ADR-0012 §7 "spike recorded under `prototypes/`" gate):** apply ADR-0012 deltas D-1..D-8 (TD-021) — 026 not Done until the S-25 retention follow-up (TD-022). Read `prototypes/spike-oq-ic-9-input/REPORT.md` `## ADR-0012 §7 deltas` and the `## Consumed by` row for this story before implementing; `/story-readiness` still recommended. Dependencies (not blocks): `production/epics/godot-host-shell/story-001-game-csproj-solution-gdignore-build-mapping.md`, `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md`.

> **Dependencies re-pointed 2026-10-23 (E1, Evan 8:10 AM PT "approve all").** Story 025 (CI lint) is Deferred to M0 (story 029, the integration suite and MANUAL evidence, stays Ready); the lint rule stays a convention this story follows by hand (direct `Godot.Input` reads only in `src/Game/Input/`) and this story records its own headless smoke rows in `production/qa/evidence/input-adapter-evidence.md`, creating the file if absent. The last-used-device / glyph half of H-EC-12 is story 030 (Deferred, M1); the adapter classification clause below stands on its own. Reconnect re-seeding of the axis cache is exercised by story 031 (Deferred, M0); the focus-in re-seed is exercised by story 027.

## Context

**GDD**: `design/gdd/input-controls.md` (R9 Devices and glyphs, R11 "One gate" and `_Input` consumption, G4 "OS key-repeat (echo) events never produce edges", Edge Cases "Platform and layout"; ACs H-R9-2, H-EC-12, H-G4-1 integration clause, H-R11-3 first clause)
**Requirement**: `TR-input-007` (router consumes in `_Input`), `TR-input-008` (events arrival-stamped in `_Input`; no `InputEvent` timestamp), `TR-input-015` (KBM identified by `DEVICE_ID_KEYBOARD` / `DEVICE_ID_MOUSE`), `TR-input-016` (per-device axis cache)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§7 "The Godot adapter", Engine Validation notes 1–5) — Accepted 2026-10-16 (adapter rows provisional until the spike); ADR-0019 (scene tree, `Initialize(deps)`, no autoloads) secondary
**ADR Decision Summary**: One `InputAdapter` node, created by the host shell before views. In `_Input(InputEvent e)` it converts the event into a `RawInputRecord` (kind, `DeviceClass` + pad index, `PhysicalInput`, pressed / released, axis Q15, mouse relative counts, echo flag, arrival ms) and calls `router.Feed(in record)`; in game contexts it calls `GetViewport().SetInputAsHandled()` for consumed game events. Keyboard and mouse are identified by `InputEvent.Device` vs `DEVICE_ID_KEYBOARD` / `DEVICE_ID_MOUSE` (the emulation id −1 counts as mouse), never `0`; synthetic `InputEventAction`s never change the last-used device and the adapter **classifies by event type first**. Joypad motion arrives one axis per event, so the adapter keeps a per-device axis cache — the only polled-style state.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (`src/Game/Input/`, `PerformativeMail.Game`) | **Risk**: HIGH
**Engine Notes**: `InputEvent` has no timestamp (probe B1) — stamp with the injected `IInputClock` (`Stopwatch`-based) at `_Input` entry; stamps are frame-granular. `DeviceIdKeyboard` / `DeviceIdMouse` / `DeviceIdEmulation` are the C# names (names verified in `GodotSharp.xml`, behaviour per the spike). Mouse look uses `InputEventMouseMotion.ScreenRelative`, not `Relative`. Per-event wrapper allocation is measured by the spike (story 001); this story must stay within the spike's recorded bytes/event and coalesce motion.

**Control Manifest Rules (Presentation)**:
- Required: compare `InputEvent.Device` to `DeviceIdMouse` / `DeviceIdKeyboard`, never `0`; the adapter pins its `ProcessPriority` / tree position so it sees every event (releases included) before menus or the dev overlay; `ProcessMode = Always` (ADR-0012 Step 5.5 note 3)
- Required: direct `Godot.Input` reads only in `src/Game/Input/` (story 025 lint — Deferred to M0; by convention until then)
- Forbidden: `InputMap` mutation; `ui_*` mutation; autoloads; `device == 0`; calling `SimWorld` / `ClientLayer` / `FrameRunner` from the node
- Guardrail: `_Input` allocation ≤ the spike's measured bytes/event; no per-event `Dictionary` lookups (array-indexed tables)

---

## Acceptance Criteria

*From GDD R9 / R11 / G4 and H-R9-2 / H-EC-12, scoped to this story. Every AC is verified by the headless smoke scene and recorded in `production/qa/evidence/input-adapter-evidence.md` (story 029 owns the full suite).*

- [ ] **Node and placement.** `public partial class InputAdapter : Node` in `src/Game/Input/InputAdapter.cs` (namespace `PerformativeMail.Game.Input`), `ProcessMode = Always`, `ProcessPriority = int.MinValue`, the **first child of `Main`** (`Main/Input`, before `World`); inert until `GameLoop` calls `Initialize(deps)` (router, clock, settings) — never looks anything up in `_Ready` (ADR-0019 Implementation Guidelines). *(ADR-0019's tree does not list an Input node; EPIC.md records the amendment request.)*
- [x] **H-R9-2 / H-EC-12 (classification).** Key, mouse and joypad-0 events: a keyboard event is classified `DeviceClass.Keyboard` via `DeviceIdKeyboard`, a mouse button/motion/wheel `Mouse` via `DeviceIdMouse` **or** `DeviceIdEmulation`, a joypad button/motion `Pad` with `PadIndex = Device`; a keyboard event never reads as pad 0; a synthetic `InputEventAction` (device 0) becomes `RawKind.Synthetic` and leaves the last-used device unchanged. Classification is by **event type first**; no array is indexed by `Device` before classification (a joypad index of 16 or 32 does not alias keyboard / mouse — per the spike's row).
- [x] **Conversion.** `PhysicalInputMap` (arrays, no hashing) maps `Key` (physical keycode), `MouseButton` (incl. wheel up / down → `Wheel` records), `JoyButton` and `JoyAxis` to Client.Core's `PhysicalInput` for every input used by the M-1a built-in map; an unmapped input is ignored and counted once (never thrown); `InputEventKey.Echo` sets `RawInputRecord.Echo`; `InputEventMouseMotion.ScreenRelative` supplies the counts; stamps come from `IInputClock.NowMs` read once per `_Input` call; the clock (`StopwatchInputClock : IInputClock`) lives in `src/Game/Input/`.
- [ ] **Axes and triggers.** Joypad motion records carry raw values converted to Q15 and cached per `(pad, axis)`; sticks emit paired x/y reads for the router's F3 via the cached values; triggers are axes (LT / RT) fed to the router's F4; the cache is re-seeded by polling `Input.GetJoyAxis` on `OnFocusGained` and reconnect (the lint's only polled exemption, inside `src/Game/Input/`).
- [x] **Handled state.** For events the router reports as consumed in the current context, the adapter calls `GetViewport().SetInputAsHandled()`; **release** events are always fed before any GUI node can consume them (verified with a focused `Button` and a stand-in dev overlay as in the spike). OS key-repeat never produces edges (H-G4-1 integration clause: E held with injected echo events at 30 / s for 1 s → one press edge at the router).
- [x] **Mouse look.** Mouse motion is coalesced to one record per rendered frame (counts summed) with `UseAccumulatedInput` at its default; with accumulation forced off (16 motion events / frame) the sum is identical and the allocation stays within the spike's recorded bound.

---

## Implementation Notes

*Derived from ADR-0012 §7 and Engine Validation notes (apply any deltas from the spike report):*

- Files in `src/Game/Input/`: `InputAdapter.cs`, `PhysicalInputMap.cs`, `StopwatchInputClock.cs`, `JoyAxisCache.cs`, `AdapterDeps.cs`. Namespace `PerformativeMail.Game.Input`. `partial` Godot node classes per the naming rules; private fields `_camelCase`.
- `src/Game/` and `PerformativeMail.Game.csproj` are created by the godot-host-shell epic — **`production/epics/godot-host-shell/story-001-game-csproj-solution-gdignore-build-mapping.md` (csproj), `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (`Main.tscn`, the `Main/Input` slot) and `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md` (`GameLoop`)**; this story adds its node to `Main.tscn` as a child and references Client.Core by `ProjectReference` only (ADR-0001 §2).
- Event handling is a `switch` on the event's concrete type (pattern matching; no reflection, no `GetType().Name`).
- The adapter is the *only* `Godot.Input`-touching code besides story 027's mouse-mode / focus code (same folder). Everything else asks the router.
- `UseAccumulatedInput`, `SetInputAsHandled` effects and the exact notification set are the spike's rows; do not code a row marked CHANGED / FAILED from this text — read the report.
- Synthetic events injected by tests use `Input.ParseInputEvent` followed by `Input.FlushBufferedEvents()` (parsed events are buffered one frame — Step 5.5 note 4).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 027: mouse mode, GUI-focus policy, focus notifications, the project setting. Story 031 (Deferred, M0): hot-plug notifications. Story 028: constructing the router, `GameLoop` call order, settings file. Story 029 (Ready): the real-pad / AZERTY / MANUAL evidence runs (may close NOT RUN per TD-172 D3).
- Router logic (stories 003–019); glyph state (story 030, M1) and glyph rendering; AZERTY label API (UNVERIFIED — physical-name fallback; MANUAL in 029, M0).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: the headless smoke scene's log for the AC rows above, recorded in `production/qa/evidence/input-adapter-evidence.md` (this story creates the file and contributes its rows; story 029 adds the MANUAL rows) — gdUnit4 is deferred (`tests/README.md`), so Godot-side integration evidence is a retained log + sign-off

**Status**: [x] Created and passing — smoke `input-adapter` (38 asserts, 0 failed); rows recorded in `production/qa/evidence/input-adapter-evidence.md`, PR #90 (2026-11-09)

---

## Completion Notes

*M-1a package delivery, recorded by Mail Lead's cleanup pass 2026-11-09 (owner ruling 2026-11-08 10:18 PM PT, card t299s1348: "Yes, switch to one PR per package and update the story files in one pass at the end"). Uncredited Mail Dev detail unless a quote says otherwise.*

- **Delivered by:** PR #90 (merge `7b4b6d2`), package WP-B part 2 (M-1a wave 2), merged 2026-11-09 under the merge rule "Merge on Review READY plus green CI, and only flag ADR changes to me" (Evan 2026-11-08 8:52 PM PT, card t292s1310). The adapter is initialised and attached to the production loop by PR #93 (`545df2f`, `InputBoot.AttachToLoop`); the `camera-rig` / `player-view` smokes drive it with real key and mouse events since PR #94 (`f145219`, `SmokeInput`).
- **Built:** `src/Game/Input/InputAdapter.cs`, `PhysicalInputMap`, `JoyAxisCache`, `StopwatchInputClock`, `AdapterDeps`; stamps in `_Input`, `DEVICE_ID_*` classification by event type first, handled state from the router's `LastFeedConsumed`, motion coalescing to one record per frame.
- **ACs met:** AC-2 (classification), AC-3 (conversion, echo flag, `ScreenRelative`, one `NowMs` per `_Input`), AC-5 (handled state; echo events never produce edges), AC-6 (mouse look coalesced; the fraction carries over, TD-240 (54)).
- **Deferred or partly met (greybox trims, Mail Lead planning default, M-1a regroup plan §3 simplify table; held by the package TD row):** AC-1 — the node is created in code by `InputAdapter.AttachToRoot(Window)` and kept root-last (ADR-0012 §7 D-1 supersedes the AC's first-child-of-`Main` slot; `Main/Input` in `Main.tscn` stays untouched; `ProcessPriority = int.MinValue` is set but has no effect on input order, spike S-06) → TD-240 (56). AC-4 — the per-pad axis cache and `ReseedAxes(pad)` are built, but the focus-in re-seed is story 027's focus handler, which was not built (no real pad at M-1a) → TD-240 (57). The D-8 / TD-022 S-25 retention check is still owed (Evan's hands-on check; story 029).
- **Tests / smokes:** smoke `input-adapter` (38/0); four legs green (6,449 per leg), no test deleted.

---

## Dependencies

- Depends on: **Story 001 (spike verdict GO / GO-WITH-DELTAS)**, Story 003 (`RawInputRecord`, `IInputClock`, `PhysicalInput`), Story 007 and 009 (router `Feed`); `production/epics/godot-host-shell/story-001-game-csproj-solution-gdignore-build-mapping.md` (csproj), `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (`Main.tscn` scene root), `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md` (`GameLoop`). (Story 025 lint removed 2026-10-23 — Deferred to M0.)
- Unlocks: Stories 027, 028, 029; Deferred: 031 (M0)
