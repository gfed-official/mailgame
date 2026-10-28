# Story 028: `GameLoop` hook-up — router construction at boot step 4, the per-frame call order, `user://input.json`, solo pause and the cross-system wiring

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M (~1 day; ~350 lines prod + a headless boot run)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-23 (dependencies re-pointed by the m1a-replan PR)

> **Unblocked 2026-10-16 (story 001 Complete, OQ-IC-9 spike verdict GO-WITH-DELTAS — satisfies the ADR-0012 §7 "spike recorded under `prototypes/`" gate):** apply ADR-0012 deltas D-1..D-8 (TD-021) — 026 not Done until the S-25 retention follow-up (TD-022). Read `prototypes/spike-oq-ic-9-input/REPORT.md` `## ADR-0012 §7 deltas` and the `## Consumed by` row for this story before implementing; `/story-readiness` still recommended. Its second unblock condition (stories 026 / 027 Ready) is met by the same change. Also verify D-1's root-last re-assertion (`ChildEnteredTree` → `MoveChild`, likely `CallDeferred`; spike H-1). Still needs `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md` and `story-004-boot-sequence-error-screen-content-step.md` (dependencies).

> **Dependencies re-pointed 2026-10-23 (E1, Evan 8:10 AM PT "approve all").** Story 025 (CI lint) is Deferred to M0 and was not a dependency of this story; story 029 (integration suite and MANUAL evidence) stays Ready and depends on this story. Story 019 was split: the parser, defaults and clamping this story boots with stay in 019 (Ready); the overrides-only writer, `bindings` and migrations are story 032 (Deferred, M1) — this story never writes `user://input.json`. The headless boot/run log below is recorded by this story itself in `production/qa/evidence/input-adapter-evidence.md`.

## Context

**GDD**: `design/gdd/input-controls.md` (R11 "Input frame" — edges latch until a tick consumes them; R12 M-1a `user://input.json`; R2 / R5 Pause; ACs H-R12-1 integration, H-R2-3-style persistence, H-EC-5 solo auto-pause)
**Requirement**: `TR-input-008` (edges attached to the first catch-up tick — the `FlushToLatch` call order), `TR-input-013` (immediates dispatched before `RunFrame`), `TR-input-014` (never `SceneTree.Paused`; solo pause is a `TickDriver` state), `TR-input-020` (read `user://input.json`), `TR-input-022` (solo auto-pause from focus loss)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§4 "Hand-over and consumption", §6 "`GameLoop._Process` calls `InputRouter.DispatchImmediates()` immediately before `FrameRunner.RunFrame()`", §9 settings file) — Accepted 2026-10-16 (primary); ADR-0019 (§1 `GameLoop` root, §3 boot order — step 4 "Input router (ADR-0012)") and ADR-0002 (§3 `GameLoop`, §4 `TickDriver.Pause/Resume`) secondary
**ADR Decision Summary**: `GameLoop._Process` runs, in order: `InputRouter.DispatchImmediates()` → `InputRouter.FlushToLatch(IInputLatchWriter)` → `FrameRunner.RunFrame()` → `PresentationRoot.Present(alpha)`. Boot step 4 builds the router from `BuiltInActionMap`, the parsed `InputSettings` and the ClientLayer ports. Solo pause goes through `TickDriver.Pause()` (allowed only with no remote peers); `Resume(now)` clears the accumulator. `SceneTree.Paused` is never used.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0019); 2026-10-16 (ADR-0002)

**Engine**: Godot 4.7.1 .NET (`src/Game/Hosting/GameLoop.cs`, `src/Game/Input/`) | **Risk**: HIGH
**Engine Notes**: `GameLoop` is the only caller of `FrameRunner` / `ClientLayer` and has the lowest `ProcessPriority`; `TickDriver.Start(now)` happens on the first `_Process` after boot completes. `user://` is read with `FileAccess.GetFileAsBytes` + `GetOpenError()` on every call; never `GetAsText`. Reading `user://` in an exported build is unverified (no export templates yet).

**Control Manifest Rules (Foundation / Presentation)**:
- Required: `GameLoop._Process` calls `InputRouter.FlushToLatch` once before `RunFrame`; immediates dispatched before `RunFrame`; pause is a `TickDriver` state only and only when Networking reports no remote peers (manifest ADR-0002 §4, ADR-0012)
- Required: boot order per ADR-0019 §3 (content → persistence → input router → session → composition …); any exception goes to the error screen and `RunFrame` stops
- Forbidden: `SceneTree.Paused`; `delta` in `GameLoop`; a second node calling `ClientLayer` / `FrameRunner`; `Input.*` reads in `GameLoop` (story 025 lint — Deferred to M0; by convention until then)
- Guardrail: the hook-up adds < 0.05 ms to the frame outside the router's own cost (advisory)

---

## Acceptance Criteria

*From ADR-0012 §4 / §6 / §9 and GDD H-R12-1, scoped to this story:*

- [ ] **Call order.** `GameLoop._Process` executes `DispatchImmediates()` → `FlushToLatch(latchWriter)` → `RunFrame()` → `Present()` → update of the `PresentedPromptLatch` (story 012) from `IInteractionQuery.Current` (done *after* the HUD drew the prompt) in that order, once per frame; a headless smoke asserts the order with a recording router and a recording runner (including a frame that runs 0 ticks — edges stay latched — and one that runs 3 catch-up ticks — edges on the first only).
- [ ] **Boot step 4.** The router is constructed from `BuiltInActionMap.CreateM1a()`, the parsed `InputSettings`, `StopwatchInputClock`, the ClientLayer-published `IInteractionQuery` / `ICarrierStatusSource` / `IClientTickSource` ports, `IThrowAimView` and a new `UiEventLog`; `ActionMapValidator.Validate` runs at boot and **any error stops boot into the error screen** (never a silent fallback). The adapter node (story 026) receives its deps via `Initialize`.
- [ ] **H-R12-1 (integration).** With `user://input.json` containing `holdSettingMs: 600` and the Deliver class `"Toggle"`, a headless boot applies both (observable through `IClientInputSettings.HoldSettingMs` and the router's mode); a malformed file boots with defaults and **one** warning in the log; a missing file boots with defaults and no warning. The file is read with `FileAccess.GetFileAsBytes` and `GetOpenError()` is checked.
- [ ] **Solo pause.** On `router.PauseRequested(solo: true)` the loop calls `TickDriver.Pause()` (refused with a logged reason while any remote peer exists); on `ResumeRequested` it calls `Resume(now)`; while paused `Advance` returns 0, the router keeps running (so Back / Menu / Resume work), `AlphaPermille` is frozen at 0, and held buttons stay muted after resume (G3). `tools/ci/check-no-scenetree-paused.sh` and its `--self-check` pass.
- [ ] **Cross-system wiring.** (a) The Immediate sinks registered at boot: Throwing's release builder for `Throw`; Grid Inventory's panel-op sink for pick / place / rotate / return / drop / quick-move **when its story exists** (an absent sink logs `no_sink`, per story 013); camera rig / host sinks for `Pause`, `ShoulderSwap`; the Hotbar slot / step consumer where the **unowned selected-slot seam** is resolved (EPIC.md). (b) `ClientAdapter` reply events call `router.ReplyObserved(reqId, reason)`; the Throwing sender calls `CommitSent`. (c) The camera rig's `IFocusConeSink` and pitch limits are pushed (`SetViewParams`, `SetPitchLimits`); Teleport / mount / dismount `LookReseeded` events from the carrier view call `router.SetLook`. (d) `router.SetSessionMode(solo)` reflects the session.
- [ ] **Frame cost sanity.** A 60 s headless run at 60 fps with scripted injected input shows no `UiEventLog` overflow of the router queues, no exceptions, and `RunFrame` still ticking 30 Hz.

---

## Implementation Notes

*Derived from ADR-0012 §4 / §6 / §9, ADR-0019 §3 and ADR-0002 §3–§4:*

- Files in `src/Game/Hosting/` (extend `GameLoop.cs` from the host-shell story — **`production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md`**) and `src/Game/Input/` (`InputBoot.cs`: builds router + reads settings + wires sinks; `InputSettingsFile.cs`: the `FileAccess` read). Do not put input logic in `GameLoop`; it only calls the router in the specified order.
- `FlushToLatch(IInputLatchWriter)` is given the Sim `InputLatch` (`production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`, story 002 here) obtained from the `SimHost.Client`; `RunFrame` is `production/epics/simulation-core/story-024-framerunner.md`.
- The `PresentedPromptLatch` update implements the "presented snapshot" seam the ADRs leave undefined (EPIC.md): the HUD presenter draws the prompt in `Present`; immediately after, the loop latches `IInteractionQuery.Current` so the *next* frame's press edges carry what the player actually saw.
- `ReplyObserved` wiring depends on the reply path in `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md` exposing a reply observer. This epic does not edit that story: if no observer exists when this story runs, subscribe to the reply events that `MirrorDrain` applies (the reply-event stream) and use the adapter's `LastReqId` accessor that Throwing 007 proposes, and record the gap in Last Updated.
- Pause allowed only when Networking reports no remote peers: at M-1a (solo, loopback) the check is trivially true; keep the call site correct for M0.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 026 / 027: the adapter node and its Godot behaviours. The host shell's own boot (content, session, composition, views) — godot-host-shell epic. Multiplayer pause overlay (M0). The settings screen and persistence (M1).
- Building the sinks themselves (Grid Inventory 024, Throwing 007, camera rig) — they are registered here, implemented there.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: a recorded headless `godot-mono` boot/run log (call-order assertion, H-R12-1 variants, solo pause/resume) in `production/qa/evidence/input-adapter-evidence.md`, and the godot-host-shell headless smoke (`tests/smoke/critical-paths.md`) still passing

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Story 001 (spike verdict)**, Stories 026, 027, 016 (pause requests), 012 (`PresentedPromptLatch`), 013 (sinks), 015 (`ReplyObserved`), 019 (settings); `production/epics/simulation-core/story-024-framerunner.md`, `story-023-clientlayer-shell-inputlatch.md`; `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md` (`GameLoop`, hook points, pause), `production/epics/godot-host-shell/story-004-boot-sequence-error-screen-content-step.md` (boot order, error screen), `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (`Main.tscn` scene root)
- Unlocks: every presentation story that needs live input (panel view, camera rig, aim ribbon, mailbox prompt); Story 029
