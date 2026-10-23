# Epic: Input & Controls

> **Layer**: Foundation
> **GDD**: design/gdd/input-controls.md (In Review)
> **Architecture Module**: Client.Core.Input (router core, Godot-free, `src/PerformativeMail.Client.Core/Input/`) + `InputFrame`/`holdEff` in the Sim + the Godot input adapter (`src/Game/`)
> **Milestone slice**: M-1a (GDD R12 Phasing)
> **Engine Risk**: HIGH. The Godot adapter covers `_Input` ordering, `joy_connection_changed`, `ignore_joypad_on_unfocused_application` and C# names (NEEDS VERIFICATION — story 001 is the OQ-IC-9 spike). `DEVICE_ID_*` is VERIFIED for 4.7. The router core is LOW.
> **Est. size**: L — 29 stories (1 spike, 18 router-core / Sim stories, 2 integration, 4 corpus / fantasy / perf / lint, 4 Godot-adapter stories); sized to one `/dev-story` session each (≤ ~1 day, ≤ ~600 lines prod + tests)
> **Implementation priority**: 8 of 9 (router core in Client.Core, stories 002–025); the Godot adapter (026–029) goes with priority 9, after the spike (story 001) and `godot-host-shell`
> **Status**: Ready
> **Stories**: 29 stories — 6 Complete, 23 Ready, 0 Blocked; 029 Ready 2026-10-23 with 026–028 as Depends-on (counts refreshed 2026-10-23 from the story headers) — see the Stories table below

## Overview

This epic builds the layer between the player's hands and every verb: the action map with stable ids, the router state machine (World, Panel and Holding at M-1a, plus Aiming for throw), the press grammar machines that latch the verb at press and mute until release on a state change, and per-tick `InputFrame` assembly (int16 move, absolute look, held bits, ordered edges, steps). The promise is that **every mistake is yours**: the verb is fixed at press, a twitch never throws a whole stack, and no press is lost or doubled.

**In scope at M-1a** (GDD R12):
- **Map and router:** the action map and alias table; the router core (R2–R5, R11) as a plain-C# state machine fed abstract raw-input records and a fake monotonic ms clock (Godot-free, xUnit-tested); the conflict validator over reachable router states.
- **Bindings and modes:** default KBM and gamepad bindings for the M-1a verbs (move, look, sprint, jump, interact/deliver hold, throw aim-release, hotbar 1–8/scroll, panel toggle, cursor, pick/place, rotate, return, quick-move, drop); hold and toggle modes and Hold time as config values from `user://input.json` (PROPOSED).
- **Glyphs:** KBM plus one Xbox pad glyph set.
- **Sim side:** the `InputFrame` struct and the shared `holdEff` function (int64, 6-tick floor, `holdCeil` clamp).
- **Robustness:**
  - the two-clock rule (Sim ticks for holds and range; integer ms for repeat, drag, glyph and aim);
  - deadzone, trigger hysteresis, auto-repeat and step-cap integer math;
  - focus-loss handling (release-with-cancel, zero movement, mute, swallow the recapture click, solo auto-pause);
  - the 4096-entry input event-log ring shared with the UI event log;
  - the replay corpus asserting zero unauthored verbs (ADR-0006).
- **Godot input adapter:** `_Input` gathering with arrival stamps, `DEVICE_ID_*` device identity, mouse mode, focus-loss synthesis, `ui_*` never mutated, `FocusMode.None` on game Controls, and a CI lint banning direct `Input.IsAction*` outside the adapter.

**Out of scope**:
- catch, quick ping, chat, push-to-talk and the external-panel focus switch → M0;
- the Controls settings screen, the action menu, PlayStation/Neutral glyphs and persistence through Profile & Settings → M1;
- Map and Build bindings → M2.

**Risks.**
- (1) ~~ADR-0012 is not written~~ — **superseded 2026-10-16**: ADR-0012 (router core + Godot adapter) and ADR-0014 (`holdEff`, cancel range) are **Accepted 2026-10-16** (owner-confirmed batch). The router core is Accepted outright; the **Godot adapter half stays provisional until the OQ-IC-9 spike is recorded under `prototypes/`**. Story 001 is that spike; stories 026–029 are Blocked on its verdict. **Update 2026-10-16:** story 001 Complete, verdict **GO-WITH-DELTAS** (`prototypes/spike-oq-ic-9-input/REPORT.md`); 026–028 Ready, applying ADR-0012 §7 deltas D-1..D-8 (TD-021) until the ADR is amended; 026 not Done until the S-25 retention follow-up (TD-022); 029 Blocked on 026–028 implementation.
- (2) The hold-timing contract (`holdEff`, cancel range with hysteresis) is shared with Player Character and Manual Delivery under ADR-0014 (Accepted). `Hold.HoldTicks` is built once, by `production/epics/player-character/story-011-hold-ticks-holdeff.md`; **this epic cites it and builds no second copy**.
- (3) `InputEvent` has no timestamp in 4.7.1 (the GDD's headless probe), so arrival stamping in `_Input` is mandatory (story 026). The probe is re-run and committed by story 001; until then it is UNVERIFIED.
- (4) Hold counting is Sim-side (ADR-0014): the GDD's hold-engine and G2 tick-numbered ACs are only provable end to end (stories 020–021), not in the router alone.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim — Accepted | edges attach to the first catch-up tick; MP pause is a local overlay, never `SceneTree.Paused`; solo pause is `TickDriver.Pause` (TR-input-014) | HIGH (`GameLoop` half) / LOW |
| ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay | replay corpus asserts zero unauthored verbs (TR-input-021) | LOW |
| ADR-0011: Network Adapter Boundary | server-validated verbs bypass the frame as reliable requests with no hold-duration field (TR-input-013) | LOW (seam) |
| ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework | `Client.Core` assembly (Godot-free net8.0) is where the router core lives | LOW for Client.Core |
| ADR-0012: Input Router Core and Godot Input Adapter — **Accepted 2026-10-16** (adapter rows provisional until the OQ-IC-9 spike) | router in Client.Core, `_Input` arrival stamps, `InputFrame` / `IInputLatchWriter`, immediates, `ui_*` immutable, `DEVICE_ID_*`, CI lint, two clocks | HIGH (adapter) / LOW (core) |
| ADR-0014: Interaction Framework, Hold Timing and Carrier Life States — **Accepted 2026-10-16** | `Hold.HoldTicks` (`holdEff`), 2900 mm cancel range + hysteresis, presented-snapshot press resolution, `IInteractionQuery` | LOW |
| ADR-0016: ClientLayer Previews and Presentation Timing — Accepted 2026-10-16 | `InputLatch` latches only; edges resolve in `PreviewSubStep.EdgeResolve`; step order | LOW |
| ADR-0019: Godot Scene Architecture — Accepted 2026-10-16 | `GameLoop` root, no autoloads, boot order (step 4 input router), `Initialize(deps)` | HIGH (Godot) |
| ADR-0015: Shared Integer Ballistic Sweep and Flight Resolution — Accepted 2026-10-16 (secondary) | throw request payload: `ReqId`, no tick field (H-R10-9) | LOW |

## GDD Requirements

Source: architecture.md v1.1 Appendix A / `docs/architecture/tr-registry.yaml`. **24 / 24 traced to an Accepted ADR** (2026-10-16); `stories: []` in the registry is not edited by this run.

| TR-ID | Requirement | ADR Coverage | Slice | Stories |
|-------|-------------|--------------|-------|---------|
| TR-input-001 | Router core plain C#, fed abstract raw-input records + fake clock; Godot adapter is Presentation | ADR-0012 ✅ | M-1a | 003, 007 |
| TR-input-002 | All gameplay reads via router; direct Input.IsAction* outside adapter is a CI lint failure | ADR-0012 ✅ | M-1a | 025 (026 adapter side) |
| TR-input-003 | RouterState tuple; live predicates as data; reachable-state golden list | ADR-0012 ✅ | M-1a | 004, 008 |
| TR-input-004 | Per-press state machine: latch at press, mute until release on state change, ordered edges | ADR-0012 ✅ | M-1a | 007, 012, 014 |
| TR-input-005 | Action map: stable ids, grammar, predicate, defaults, flags, owner; alias table | ADR-0012 ✅ | M-1a | 005 |
| TR-input-006 | Conflict validator over reachable states; most-specific chord wins | ADR-0012 ✅ | M-1a | 006 |
| TR-input-007 | ui_* InputMap never mutated; game Controls FocusMode=None; router consumes in _Input | ADR-0012 ✅ (adapter rows provisional until spike) | M-1a | 008, 026, 027, 029 |
| TR-input-008 | Events arrival-stamped in _Input (no InputEvent timestamp); edges attached to first catch-up tick | ADR-0012 ✅ (adapter rows provisional until spike) | M-1a | 002, 007, 020, 026, 028 |
| TR-input-009 | InputFrame Sim struct: tick, int16 move, absolute look, held bits, ordered edges, steps | ADR-0012 ✅ | M-1a | 002 |
| TR-input-010 | Two clocks: Sim ticks for holds/range; monotonic integer ms for repeat/drag/glyph/aim | ADR-0012 ✅ | M-1a | 003, 012, 014 |
| TR-input-011 | holdEff single shared int64 Sim function, 6-tick floor, holdCeil clamp | ADR-0014 ✅ | M-1a | PC 011 (cited), 020 |
| TR-input-012 | Hold cancel range 2900 mm with hysteresis = PC incumbentSlackMm | ADR-0014 ✅ | M-1a | PC 017 (cited), 020, 021 |
| TR-input-013 | Server-validated verbs bypass the frame as reliable requests; no hold-duration field | ADR-0011 ✅ | M-1a | 013, 021 (+ network-adapter-loopback 006) |
| TR-input-014 | MP pause is a local overlay, never SceneTree.Paused; Sim keeps ticking | ADR-0002 ✅ | M-1a | 016, 028 |
| TR-input-015 | KBM identified by DEVICE_ID_KEYBOARD/MOUSE; hot-plug via joy_connection_changed (UNVERIFIED) | ADR-0012 ✅ (adapter rows provisional until spike) | M-1a (KBM + one Xbox pad) | 017, 026, 027 |
| TR-input-016 | ignore_joypad_on_unfocused_application=true; per-device axis cache | ADR-0012 ✅ (adapter rows provisional until spike) | M-1a | 026, 027 |
| TR-input-017 | Deadzone/trigger/repeat/step caps in integer math; one step per rendered frame | ADR-0012 ✅ | M-1a | 009, 010 |
| TR-input-018 | Router+gatherer ≤0.1 ms/frame, ≤0.3 ms at 1000 Hz burst, 0 B steady | ADR-0012 ✅ | 0 B clause BLOCKING at M-1a (H-PERF-3); timing advisory | 024 |
| TR-input-019 | Preallocated 4096-entry event-log ring shared with UI event log | ADR-0012 ✅ | M-1a | 003 |
| TR-input-020 | settings.input schema versioned, overrides-only; M-1a user://input.json | ADR-0012 ✅ | M-1a | 019, 028 |
| TR-input-021 | Replay corpus (200 seeded + 10 adversarial) asserts zero unauthored verbs | ADR-0006 ✅ | M-1a | 022 (+ simulation-core 026) |
| TR-input-022 | Focus loss: release-with-cancel, zero movement, mute, swallow recapture click, solo auto-pause | ADR-0012 ✅ (adapter rows provisional until spike) | M-1a | 018, 027, 029 |
| TR-input-023 | One-commit rule for deliver/deconstruct/throw; pendingCommitTimeout 500 ms | ADR-0012 ✅ | M-1a | 015, 021 |
| TR-input-024 | One local carrier per machine; all devices OR-ed; duplicate pad edges within 20 ms collapse | ADR-0012 ✅ | M0 in this table; GDD ACs H-R9-1 / H-EC-2 / H-EC-13 are M-1a → built now | 017 |

**Traced:** all 24 map to an Accepted ADR (ADR-0012 ×19, ADR-0014 ×2 — TR-input-011 / 012 —, ADR-0011, ADR-0002, ADR-0006). TR-input-011 and 012 are implemented by Player Character stories (`production/epics/player-character/story-011-hold-ticks-holdeff.md`, `story-017-hold-counter-cancels-channel-focus-latch.md`) and are **cited, not rebuilt**, here.

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [OQ-IC-9 input spike — verify the Godot 4.7.1 .NET input surface and record the adapter GO / NO-GO](story-001-oq-ic-9-input-spike.md) | Integration | Complete | ADR-0012 |
| 002 | [`InputFrame`, `InputEdge`, `IInputLatchWriter`, `RouterStateInfo` and the `InputLatch` push side](story-002-inputframe-latch-writer-routerstateinfo.md) | Logic | Complete | ADR-0012 |
| 003 | [Client.Core input foundations — `RawInputRecord`, `IInputClock`, `PhysicalInput`, `UiEventLog`](story-003-clientcore-input-foundations-uieventlog.md) | Logic | Complete | ADR-0012 |
| 004 | [`RouterState` tuple, live predicates as data, reachable-state golden list](story-004-routerstate-predicates-reachable-states.md) | Logic | Complete | ADR-0012 |
| 005 | [Action map — `ActionDef`, aliases, M-1a `BuiltInActionMap` and default bindings](story-005-action-map-builtin-m1a-defaults.md) | Logic | Complete | ADR-0012 |
| 006 | [Conflict validator, most-specific-chord resolution, verb-grammar table](story-006-conflict-validator-chord-resolution.md) | Logic | Complete | ADR-0012 |
| 007 | [`InputRouter` shell — `Feed`, press machines, ordered edges, mute until release, `FlushToLatch`](story-007-router-shell-press-machines-mute.md) | Logic | Ready | ADR-0012 |
| 008 | [Router state transitions, `holding` / `body` from the carrier status, pass-through, mouse-mode request](story-008-router-state-transitions-and-passthrough.md) | Logic | Ready | ADR-0012 |
| 009 | [Analog input — F3 deadzone / curve, F4 trigger hysteresis, move across devices, absolute look](story-009-analog-input-deadzone-trigger-move-look.md) | Logic | Ready | ADR-0012 |
| 010 | [Stepped verbs — F5 auto-repeat, F6 saturating steps, F7 click-or-drag](story-010-steps-auto-repeat-drag-resolver.md) | Logic | Ready | ADR-0012 |
| 011 | [Panel navigation without a mouse (R8)](story-011-panel-cursor-navigation.md) | Logic | Ready | ADR-0012 |
| 012 | [Interact presented-target latch, Toggle translation (G7), Sprint modes and F8](story-012-interact-latch-toggle-translation-sprint.md) | Logic | Ready | ADR-0012 |
| 013 | [Immediate verbs — queue, `IImmediateActionSink`, `DispatchImmediates`](story-013-immediate-verbs-queue-sinks-dispatch.md) | Logic | Ready | ADR-0012 |
| 014 | [Throw aim-release — `aiming`, F11 minimum aim, Toggle aim, refusals, commit](story-014-throw-aim-release-min-aim-toggle.md) | Logic | Ready | ADR-0012 |
| 015 | [One-commit gate (G9) — Busy, Release to retry, `pendingCommitTimeout`](story-015-one-commit-gate-g9.md) | Logic | Ready | ADR-0012 |
| 016 | [Back ladder (R5), `no_room` skip (F13) and the Pause capture state](story-016-back-ladder-pause-capture.md) | Logic | Ready | ADR-0012, ADR-0002 |
| 017 | [Devices — OR across devices, hold ownership, pad-edge collapse, last-used glyph (F9)](story-017-devices-glyph-state-or-across-devices.md) | Logic | Ready | ADR-0012 |
| 018 | [Focus loss and pad hot-plug in the router core](story-018-focus-loss-and-pad-hotplug-core.md) | Logic | Ready | ADR-0012 |
| 019 | [`settings.input` — integer-unit `InputSettings` and the `user://input.json` codec](story-019-settings-input-codec-and-validation.md) | Logic | Ready | ADR-0012, ADR-0010 |
| 020 | [Headless router → Sim integration (A) — grammar fire ticks, hold timing, latch rules](story-020-integration-hold-timing-and-interact-end-to-end.md) | Integration | Ready | ADR-0012, ADR-0014 |
| 021 | [Headless router → Sim integration (B) — cancel-cause table, resolution rows, Toggle / aim / commit, request schemas](story-021-integration-cancels-toggle-commit-end-to-end.md) | Integration | Ready | ADR-0012, ADR-0014 |
| 022 | [Input replay corpus (200 + 10) and the zero-unauthored-verbs oracle](story-022-replay-corpus-zero-unauthored-verbs.md) | Integration | Ready | ADR-0006, ADR-0012 |
| 023 | [Fantasy scripts — hold-free shift, route parity, per-class Toggle coverage](story-023-fantasy-scripts-toggle-run-route-parity-a11y.md) | Logic | Ready | ADR-0012 |
| 024 | [Router-core zero-allocation gate (H-PERF-3) and advisory timing probe](story-024-router-core-zero-allocation-gate.md) | Logic | Ready | ADR-0012, ADR-0001 |
| 025 | [CI lint — no direct `Input.*` reads outside `src/Game/Input/`](story-025-ci-lint-no-direct-input-reads.md) | Integration | Ready | ADR-0012 |
| 026 | [Godot `InputAdapter` node — `_Input` gathering, arrival stamps, `DEVICE_ID_*`, axis cache](story-026-godot-input-adapter-node.md) | Integration | Ready | ADR-0012, ADR-0019 |
| 027 | [Adapter — mouse mode, GUI-focus policy, focus-loss / hot-plug delivery, project setting](story-027-adapter-mouse-mode-gui-focus-hotplug-project-setting.md) | Integration | Ready | ADR-0012, ADR-0002 |
| 028 | [`GameLoop` hook-up — router boot, call order, `user://input.json`, solo pause, wiring](story-028-gameloop-hookup-router-boot-settings-file.md) | Integration | Ready | ADR-0012, ADR-0019, ADR-0002 |
| 029 | [Adapter integration suite and MANUAL evidence runs](story-029-adapter-integration-suite-and-manual-evidence.md) | Integration | Ready | ADR-0012 |

**29 stories: 6 Complete, 23 Ready, 0 Blocked** (refreshed 2026-10-23). Types: 20 Logic, 9 Integration, 0 Visual/Feel, 0 UI, 0 Config/Data (the hold ring, prompts and glyph art are HUD's — see Notes).

**Blocked (0).** 029 is Ready since 2026-10-23, and 026–028 must be Complete before it starts (Depends on). The spike gate is satisfied. *History:* 026–029 were `BLOCKED: awaits OQ-IC-9 spike (story 001) outcome per ADR-0012 §7`; story 001 closed 2026-10-16 with **GO-WITH-DELTAS**, so 026–028 are Ready with the note "apply ADR-0012 deltas D-1..D-8 (TD-021) — 026 not Done until the S-25 retention follow-up (TD-022)". ADR-0012 states the adapter half is *provisional until the OQ-IC-9 spike is recorded under `prototypes/`* and (Decision Makers, ADR Dependencies → Blocks, manifest Presentation) that adapter stories wait for it. Unblock rule: story 001 Done with a `REPORT.md` verdict of GO or GO-WITH-DELTAS; each blocked story re-reads the report's deltas, then flips to Ready at `/story-readiness`. They also depend on the godot-host-shell epic (`production/epics/godot-host-shell/story-001-game-csproj-solution-gdignore-build-mapping.md`, `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md`, `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md`, `production/epics/godot-host-shell/story-004-boot-sequence-error-screen-content-step.md`) — a dependency, not a block. **Story 025 (CI lint) is Ready**: ADR §8 fixes its rules and carries no UNVERIFIED row, so it is not spike-conditional even though ADR-0012's Migration Plan lists it after the spike.

### Suggested order

1. **001 (spike)** — first, time-boxed, independent of everything; its verdict gates 026–029.
2. **Sim reconciliation + Client.Core foundations:** 002 → 003 (needs `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md` and `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`).
3. **Data and rules:** 004 → 005 → 006.
4. **Router core:** 007 → 008 → 009 → 010 → 011; 012; 013 → 014 → 015 → 016; 017 → 018; 019 (019 may run any time after 003 / 005).
5. **Integration and gates:** 020 → 021 → 022 → 023; 024 after the router is feature-complete; 025 any time.
6. **Godot adapter (after 001 + godot-host-shell):** 026 → 027 → 028 → 029.

## Notes

### Deferred — rules with no story at M-1a (GDD phase tags)

| Milestone | Rules / ACs deferred (no stories created) |
|---|---|
| **M0** | Catch (R4.3 step 1, catch-armed Toggle latch G7 `catchArmSeconds`, H-G7-4, H-R4-2 (a), H-R4-3, H-FX-4 catch row); quick ping; chat / TextEntry capture (H-R2-1, H-EC-7); push-to-talk and its Toggle / mic indicator (H-G7-3 PTT half); external-panel focus switch and LB (R8 LB clause); MP Pause overlay (H-R2-2); knockdown (H-R2-5b, H-EC-8 — also needs M-1b Dead); H-PERF-4; OQ-IC-17 ring sizing and the relative perf gate; H-FX-2, H-FX-3b (MANUAL playtests); Steam Input double-pad and glyph behaviour (MANUAL / spike) |
| **M1** | Controls settings screen, remap, conflict messages, reset / undo (H-R10-1…8, H-A11Y-4/6, H-EC-10); load-time collision / reset rules (H-EC-9) and **applying** `bindings` overrides (story 019 recognises but does not apply them); alias application at profile load (H-R1-3; the alias *lookup* is built in 005); PlayStation / Neutral glyph sets and Glyph style (H-R9-3); action menu (R7, H-R7-1…3); comms wheel; history overlay; Payday overlay rules (H-R2-7); vehicle bindings and mount / auto-stow (H-R2-6); aim steadiness F10 (H-F10-1); header focus, page / jump rows; stepper (F6 cap 10, wired but unused); H-PERF-1 / 2 adapter timing; H-A11Y-2/3 (keyboard-only / gamepad-only panel flows) |
| **M2 / M3 / M4** | Map and Build bindings; turret; face-button layout, swap sticks, status read, one-handed fixture, screen-reader pass |

### HUD core has no epic

Prompts ("Hold [E] Deliver"), the hold ring (F12), disabled-verb slash, "Busy" / "Release to retry", "Hold to aim" and "Controller disconnected" text, glyph atlases and the focus bracket are **HUD & Notifications** work; HUD core has no epic (Presentation layer not yet run) and there is no `design/ux/` spec for the interact prompt or Controls screen (only `hud-notifications-seed.md`, `throwing-catching-seed.md`). **No HUD or UI stories were created here.** The router publishes *cue ids* and `UiEventLog` lines for all of them. H-A11Y-5 [UI] (hold ring distinguishable in greyscale) and the `/asset-spec system:input-controls` glyph work wait for the HUD epic and its UX spec (`/ux-design` — the GDD's UX Flag).

### Requirements without a TR-ID in the registry (cited by GDD rule / AC id in the stories)

R5 the Back ladder and F13 (stories 016: H-R5-1, H-R5-2, H-F13-1); R8 panel navigation (011: H-R8-1, H-R8-2); R4.2 verb-grammar table (006: H-R4-5); R6 default bindings and collision rulings (005, 008, 013: H-R6-1, H-R6-2); G1–G3, G5–G8 individually (TR-input-004 covers the machine); F5–F7, F9, F11, F12 (the formulas; TR-input-017 covers F3 / F4 / F5 / F6 only by name); the fantasy tests H-FX-3a, H-FX-4, H-A11Y-1 (023); the input-latch writer / `RouterStateInfo` / `IClientInputSettings` ports (002 — ADR-0012 §4 but no TR row). *No registry edit was made.*

### Unowned or newly owned cross-epic seams (no other epic's story file was edited)

1. **`InputFrame` reconciliation** — `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md` defines `InputFrame` as PROVISIONAL and has no `IInputLatchWriter`; story **002** supplies ADR-0012 §4's shapes in the same file, adds the writer and overflow rule. If 023 is already Done when 002 starts, 002 migrates it.
2. **`RouterStateInfo.IsPanel`** requested by `production/epics/player-character/story-020-carrierstep-clientlayer-integration.md` — delivered by story 002. Since the owner ruling of 2026-10-21 (ADR-0012 erratum), story 020 reads Sim-side Panel from `InputFrameFlags.PanelOpen`, which story 008 sets.
3. **`holdSettingMs` source** — Player Character 011/017 and Manual Delivery 001 take it as an argument; the Sim port `IClientInputSettings` (002) and its implementation (019) now exist.
4. **Presented-snapshot tracking** — ADR-0014 §7 says it is "tracked by ADR-0016", but ADR-0016 defines no mechanism and HUD has no epic. Story 012 defines `IPresentedPromptSource` / `PresentedPromptLatch`; story 028 updates it after `Present`. *Needs an ADR-0016 / ADR-0018 note.*
5. **Selected hotbar slot state** (digits 1–8 → `HotbarSlot1..8` immediates, wheel / D-pad → `HotbarStep` steps) — routed by 005 / 010 / 013, **consumed by no story**; Throwing's EPIC seam 1 (`IHotbarSelection`, `ISelectedEntryView`) and Player Character's `IHeldItemView` remain unowned.
6. **`no_room` result for the Back ladder** — `InputRouter.OnReturnResult` (016) must be called by the Grid Inventory return-reply path (`production/epics/grid-inventory/story-024-panel-interactions-feedback.md`, Blocked).
7. **`ReplyObserved` / `CommitSent` wiring** — the router API (015) needs `ClientAdapter` reply events and Throwing's sender to call it; `reqId` exposure from `ClientAdapter` is Throwing EPIC seam 9; wiring is story 028.
8. **Selection of the Godot node's scene position** — ADR-0019's scene tree has no Input node; story 026 places `Main/Input` as the first child of `Main` (ADR-0012 Step 5.5 note 3 requires the adapter to see events first). *Needs an ADR-0019 amendment note.*
9. **Camera / look hand-offs** — `IFocusConeSink` and `SetPitchLimits` (009) are pushed by `production/epics/player-character/story-023-camera-rig-node-focus-cone.md` (through 028); `SetPitchLimits` is additive to ADR-0012's `SetViewParams`.
10. **Replay of `InputFrame`s** — ADR-0012 §4 says "Replay records frames"; ADR-0006 §7's `IOpRecorder` records ops and samples only. Story 022 replays at the trace level; recording frames is a sim-core recorder gap.
11. **`UiEventLog`** is created by story 003 as ADR-0012's "first writer"; Grid Inventory H-UI-1 and HUD (ADR-0018, not written) will read it — no story in those epics owns the ring.

### GDD ↔ ADR (or GDD ↔ GDD) conflicts found — ADR followed

- **R11 / Dependencies "input frame drained by `Inputs.Apply(tick)`"** → ADR-0012 §4: the router pushes into the ClientLayer `InputLatch`; `Inputs.Apply` drains ops only.
- **F1 note "`holdSetting` stored as a count of 50 ms steps" and H-R12-1's `holdSetting 0.6`** → ADR-0012 `holdSettingMs` (integer ms, multiple of 50); the file keeps integer units (story 019).
- **PCM-P12-1 "press tick = held tick 1" vs H-R3-1 / ADR-0014 §4** → hold completes on `PressTick + HoldTicks` (tick 12); stories 020 / 021 assert tick 12.
- **H-FX-3a "≤ 11 inputs, matching Grid H-FX-1" vs Grid H-FX-1 "≤ 12"** (1 open + 1 take-all + 10 holds = 12 ops) → story 023 asserts KBM / pad parity and the Grid bound (≤ 12); take-all is M1 so that step is an explicit `Skip`.
- **TR-input-024 tagged M0 in the epic table vs GDD ACs H-R9-1 / H-EC-2 / H-EC-13 tagged M-1a** → built at M-1a (story 017).
- **GDD hold engine / G2 as router behaviour vs ADR-0014 Sim-side hold counter** → the router emits edges and cancels only; tick-numbered ACs are asserted end to end (020 / 021).
- **ADR-0012 Migration Plan puts the CI lint after the spike** → story 025 is Ready now (no UNVERIFIED row in §8).
- **PCM `CarrierStatus.Aiming` vs router-owned `aiming` (ADR-0012 §2)** → the router ignores the status field (008); it echoes the frame's aim bit.

### What other epics cite from here

- `production/epics/input-controls/story-002-inputframe-latch-writer-routerstateinfo.md` — `InputFrame`, `InputEdge` (`TargetEntity`, `TargetSlot`, `PresentedSeq`), `RouterStateInfo` (Player Character 016, 020).
- `story-013-immediate-verbs-queue-sinks-dispatch.md` — `IImmediateActionSink` registration (Throwing 007, Grid Inventory 024, camera rig).
- `story-014-throw-aim-release-min-aim-toggle.md` — `IThrowAimView` consumes `ThrowPreviewState` (Throwing 004); `story-015-one-commit-gate-g9.md` — `CommitSent` / `ReplyObserved` (Throwing 007).
- `story-009-analog-input-deadzone-trigger-move-look.md` — `IFocusConeSink` (Player Character 023); `story-019-settings-input-codec-and-validation.md` — `IClientInputSettings` (Player Character 011 / 017, Manual Delivery 001).
- `story-011-panel-cursor-navigation.md` and `story-016-back-ladder-pause-capture.md` (`OnReturnResult`) — Grid Inventory 023 / 024.

### Hand-off to the godot-host-shell epic (stories: `production/epics/godot-host-shell/EPIC.md` `## Stories`)

- **Adapter node:** `src/Game/Input/InputAdapter.cs` (story 026), placed as `Main/Input`, the first child of `Main`, `ProcessMode = Always`, `ProcessPriority = int.MinValue`; initialised by `GameLoop` via `Initialize(deps)`; `MouseModeController.cs`, `GameControlFocus.cs`, `FocusPolicyCheck.cs` (story 027).
- **`GameLoop` hook-up points** (story 028, `src/Game/Hosting/GameLoop.cs` + `src/Game/Input/InputBoot.cs`): boot step 4 (validate `BuiltInActionMap`, read `user://input.json`, construct the router); per frame `DispatchImmediates()` → `FlushToLatch(latch)` → `RunFrame()` → `Present()` → update `PresentedPromptLatch`; `PauseRequested` / `ResumeRequested` → `TickDriver.Pause()` / `Resume(now)`; reply observer → `ReplyObserved`; sink registration; `SetLook` on teleport; `SetSessionMode`.
- **Repo-root `project.godot`** gains `[input_devices] joypads/ignore_joypad_on_unfocused_application=true` (story 027).
- **Lint and smoke:** `tools/ci/lint-input-reads.sh` (025); headless smoke `--smoke=input-adapter` (029) must be reachable from the shell's export-filter / debug-scene handling.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All M-1a acceptance criteria from `design/gdd/input-controls.md` are verified (R1–R5, R11 and the M-1a grammar rows)
- All Logic and Integration stories have passing test files in `tests/` (router core under `tests/unit/input/**`, integration under `tests/integration/input/**`, headless, no Godot) or, for the Godot adapter, the retained evidence in `production/qa/evidence/input-adapter-evidence.md` and `prototypes/spike-oq-ic-9-input/REPORT.md`
- Glyph switching and hold-ring screenshots are HUD-epic evidence (no HUD epic yet); this epic has no Visual/Feel or UI stories

## Next Step

Run `/story-readiness production/epics/input-controls/story-001-oq-ic-9-input-spike.md` then `/dev-story` (the spike gated 026–029; it is Complete, GO-WITH-DELTAS, and 029 now also waits on 026–028 Complete), and in parallel `/story-readiness` story 002 → 003 for the router core. Stories 002 and 003 need `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md` and `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md` first.
