# Story 007: `InputRouter` shell — `Feed`, per-press machines (latch at press, mute until release), ordered edges and `FlushToLatch`

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M–L (~1 day; ~550 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-11-08 (implemented by /dev-story; edge-kinds design decision, uncredited implementation detail, not an owner ruling: a `Frame` Tap pushes one Press on the press edge and nothing on release; a `HeldState` press adds to a per-`FrameAction` count whose non-zero bits are `HeldBits` (OR across devices), no edges; a Hold press pushes Press and its release Release; an Aim-release press pushes Press **and** holds the Aim bit until its release pushes Release; a state change that makes a Hold / Aim press non-live pushes `Cancel(StateChange)` stamped with the new state; Immediate, Axis and Stepped presses latch and log `GateFired` and push nothing until stories 013 / 009 / 010; every open Hold / Aim press reserves the ring slot of its one closing edge, so a Press is admitted only beside every reservation and a Release / Cancel always fits)

## Context

**GDD**: `design/gdd/input-controls.md` (R3 grammar + G1, G3, G4, G5, G8; per-press state machine table; R11 "One gate" and "Input frame"; ACs H-G4-1, H-G8-1, H-EC-3 router half)
**Requirement**: `TR-input-004` (per-press state machine: latch at press, mute until release on state change, ordered edges); event-time gating half of `TR-input-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§3 per-press machines, §4 arrival and consumption, Implementation Guidelines) — Accepted 2026-10-16
**ADR Decision Summary**: One `PressMachine` per `(device, physical input)` in a preallocated table: `Idle → Pressed(latched action, verb class, RouterStateId) → {Fired | HoldActive | Aiming | Active | Muted}`. Everything is resolved and latched at the press edge (G1); a state change that makes the latched action non-live mutes the input until release and the release is swallowed (G3); an action that becomes live while its button is down needs a fresh press. Every edge is captured from events, gated against the router state **at event time**, stamped with a wrap-safe `ArrivalSeq` and queued in order (preallocated 256-entry ring); release and cancel edges are never dropped.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: None — the router consumes abstract `RawInputRecord`s. Whether Godot delivers OS key-repeat as `Echo` events is spike item 11 (story 001); the router ignores `Echo` records either way.

**Control Manifest Rules (Foundation)**:
- Required: every edge captured from events, arrival-stamped, gated against the router state at event time, queued in order; release/cancel edges never dropped; presses latch at press and mute on a state change (ADR-0012 §3, §4; manifest Input router core)
- Forbidden: polling input at tick time; reading `Godot.Input`; allocation in `Feed`/`FlushToLatch` after construction
- Guardrail: router core 0 B steady state; ≤ 0.1 ms per frame typical (advisory)

---

## Acceptance Criteria

*From GDD G1, G3, G4, G5, G8 and H-G4-1 / H-G8-1 / H-EC-3, scoped to this story:*

- [x] **Edge kinds per grammar (design decision recorded in Last Updated).** `Frame` Tap actions (Jump) emit a **Press** edge only; `HeldState` actions (Sprint, the Aim held bit) set / clear their bit in `HeldBits` (OR across devices) and emit no edges; Hold-class actions (Interact) and Aim-release (Throw) emit **Press** and **Release** edges (and Cancel edges from stories 012/014/016/018) in arrival order; `Immediate` actions are queued by story 013. Each edge carries `RouterStateId` as gated **at event time**, `ArrivalSeq` (wrap-safe, strictly increasing per router), `DeviceClass`, and the action's `FrameAction`.
- [x] **H-G4-1.** E held for 1 s with OS echo records (`Echo = true`) arriving at 30 / s produces exactly one Interact Press edge and no extra edges; two Jump presses (press, release, press) inside one tick produce one Jump Press edge after the flush-time coalescing of same-action level presses; a Press and Release of Interact inside one tick produce both edges, in order (H-EC-3 router half — the "Hold pulse" cue is Sim-side, story 020).
- [x] **H-G8-1 (Frame Tap actions).** For every `Frame` Tap action in the M-1a map: press at tick 0, release at tick 30 → the Press edge is in tick 0's flushed batch and nothing is produced at tick 30 (there is no tap-on-release; the engine "tap fires on release when a hold shares the button" pattern is not used). Story 013 extends this `[Theory]` to `Immediate` taps.
- [x] **Latch (G1).** On a press edge the router resolves `(action, grammar, state)` once and keeps it for that physical input until release; changing the router state afterwards does not re-resolve it. A press with no live action creates a `Muted` machine (log `GateNotLive`), emits nothing, and its release is swallowed (log `GateSwallowed`).
- [x] **Mute (G3).** `OnStateChanged(old, new)` re-evaluates every active machine: if its latched action is no longer live in `new`, the machine becomes `Muted` — Hold / Aim machines emit a `Cancel(StateChange)` edge at that moment, a HeldState bit is cleared, and the physical release is later swallowed even if the new state binds that input to something else. An action that **becomes live while its input is already down** fires nothing until a fresh press (test: Q down while Drop is not live, state becomes live, release → nothing; press again → fires).
- [x] **Raw held and chords.** The router tracks raw held state per physical input from every record, including muted and swallowed ones; chord modifiers (Shift for quick-move) read **raw** held state, never the mute-filtered one: Shift pressed in World (sprint latched), state → Panel (sprint muted), LMB pressed → resolves Shift+LMB quick-move via story 006's `ChordResolver`; Shift+Enter resolves quick-move, Enter alone pick/place, never both.
- [x] **Event-time gating and flush.** A Jump press arriving before a state change is gated against the old state and keeps that `RouterStateId` after the change; `FlushToLatch(IInputLatchWriter)` pushes every queued edge in arrival order, then the current state (move, look, held bits, cone, `RouterStateId`, flags) via `SetState`, then clears the queue; `ArrivalSeq` wraps 65 535 → 0 without reordering (`SerialU16.IsNewer`).
- [x] **Edge ring.** The router queue holds 256 edges; if it is full, the newest **Press** is dropped (counted, logged `Dropped`), a Release / Cancel never is.

---

## Implementation Notes

*Derived from ADR-0012 §3, §4 and Key Interfaces:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `InputRouter.cs` (partial class — later stories add partials: `InputRouter.Analog.cs`, `.Steps.cs`, `.Hold.cs`, `.Aim.cs`, `.Immediate.cs`, `.Back.cs`, `.Devices.cs`, `.Focus.cs`), `PressMachine.cs` (struct in a preallocated `PressMachine[]` indexed by `(deviceSlot, PhysicalInput)`), `EdgeQueue.cs`, `HeldTable.cs`. Public surface as ADR-0012 Key Interfaces: `Feed(in RawInputRecord)`, `FlushToLatch(IInputLatchWriter)`, `DispatchImmediates()` (story 013), `OnFocusLost/Gained`, `OnPadConnection` (story 018), `SetLook`, `SetViewParams` (story 009), `ReplyObserved`, `CommitSent` (story 015), `State`, `Glyphs`, `Look`. The constructor takes `(ActionMap, InputSettings, IInputClock, UiEventLog)`: story 019 shipped the real `InputSettings` (`Client.Core/Input/Settings/InputSettings.cs`), which this story uses as is; `IInteractionQuery` and `ICarrierStatusSource` are left out of the constructor until stories 012 and 008 add them (Mail Lead readiness note in HANDOFF "Things to know before resuming" and the 2026-11-05 next-picks plan).
- State for this story is driven by an `internal` test hook (`ForceState(RouterStateId)`); story 008 replaces it with real transitions. Mark the hook `[Conditional("SIM_ASSERT")]`-free `internal` with `InternalsVisibleTo` the test assembly.
- Device slotting: a small fixed table — keyboard, mouse, pad 0–3 — each with `PhysicalInput` entries; no `Dictionary` iteration in ordering code (ADR-0006 rule 3 applies in spirit).
- Log every gate decision (fired / not live / muted / swallowed) and every drop to `UiEventLog` with enum ids (story 003).
- Coalescing happens at **flush** time over the queued batch (same action, level verb, no intervening edge of a different kind for that action).
- Do not time holds here (ADR-0014 owns the counter): the router latches and emits edges; nothing in this class counts ticks except where later stories say so (F8 sprint unlatch).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 008: real state transitions, `holding` / `body` from the published carrier status. Story 009: analog axes. Story 010: steps. Story 012: Interact target latch and Toggle translation. Story 013: `Immediate` actions and sinks. Story 014: aim. Story 015: one-commit gate. Story 016: Back ladder. Story 017: devices / glyphs. Story 018: focus loss.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/PressMachineTests.cs` (`[Trait("Id", "H-G4-1")]`, `[Trait("Id", "H-G8-1")]`, `[Trait("Id", "H-EC-3")]`), `tests/unit/input/MuteUntilReleaseTests.cs`, `tests/unit/input/EdgeQueueTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — tests/unit/input/PressMachineTests.cs (H-G4-1, H-G8-1, H-EC-3), MuteUntilReleaseTests.cs, EdgeQueueTests.cs (all pass, four legs)

---

## Dependencies

- Depends on: Stories 002 (latch writer, edges), 003 (record, clock, log, harness), 004 (state), 005 (action map), 006 (`ChordResolver`); `production/epics/player-character/story-018-interaction-snapshot-iinteractionquery.md` (`IInteractionQuery` type; a test double suffices to start)
- Unlocks: Stories 008–019

---

## Completion Notes

**Completed**: 2026-11-08 (by /dev-story, headless; not yet reviewed)
**Criteria**: 8/8 passing (all covered by automated tests)
**Files**: new `src/PerformativeMail.Client.Core/Input/InputRouter.cs` (public `sealed partial class`: `Feed`, `FlushToLatch`, `State`; internal `ForceState`, `OnStateChanged`, `Settings`, `DroppedEdges`), `PressMachine.cs` (`PressPhase`, `PressMachine`, `DeviceSlots`: keyboard, mouse, pads 0–3 × 256 inputs), `EdgeQueue.cs` (256-entry ring, wrap-safe `ArrivalSeq`, flush-time coalescing), `HeldTable.cs` (raw held bits per input; per-`FrameAction` counts for `HeldBits`). No `.csproj`, enum or Sim edit: `InternalsVisibleTo("PerformativeMail.Sim.Tests")` already existed in Client.Core, and every `LogKind` used already existed.
**Tests** (one behaviour test per AC, through `Feed` / `ForceState` / `FlushToLatch`):
- Edge kinds per grammar — `PressMachineTests.Feed_OneActionOfEachGrammar_EmitsTheGrammarsEdges_AndHeldBitsOrAcrossDevices`
- H-G4-1 — `PressMachineTests.HeldWithOsEchoes_GivesOneInteractPress_AndTwoJumpPressesInOneTick_CoalesceToOne`; H-EC-3 (router half) — `PressMachineTests.InteractPressAndReleaseInsideOneTick_DeliverBothEdgesInOrder`
- H-G8-1 — `PressMachineTests.FrameTap_PressAtTick0ReleaseAtTick30_FiresInTick0AndNothingAtTick30` (`[Theory]`, one row per `Frame` Tap action of the M-1a map, read from the map: Jump today)
- Latch (G1) — `MuteUntilReleaseTests.Press_LatchesUntilRelease_AndAPressWithNoLiveActionIsMutedAndItsReleaseSwallowed`
- Mute (G3) — `MuteUntilReleaseTests.StateChange_MutesEveryNonLiveMachine_CancelsHoldAndAim_ClearsHeldState_AndSwallowsTheReleases` and `MuteUntilReleaseTests.ActionThatBecomesLiveWhileItsInputIsDown_FiresNothingUntilAFreshPress` (the test the AC names)
- Raw held and chords — `MuteUntilReleaseTests.ChordModifiers_ReadRawHeldState_EvenWhileTheModifiersOwnActionIsMuted`
- Event-time gating and flush — `EdgeQueueTests.PressBeforeAStateChange_KeepsItsGatedState_AndFlushPushesEdgesThenStateThenClears`, `EdgeQueueTests.ArrivalSeq_WrapsPast65535_WithoutReordering`
- Edge ring — `EdgeQueueTests.FullRing_DropsTheNewestPress_AndNeverAReleaseOrCancel`
**Test Evidence**: all four legs 5,941 total / 0 failed (Debug 67 skipped, Release 7 skipped; the new tests add no skips). A temporary mutation run (coalescing and G3 disabled) failed 5 of the 11 new tests. Guards: check-sim-access, check-analyzer-fixtures (128), check-build-config (10 rows), check-game-build (16 rows) all pass. No 0-alloc test committed: H-PERF-3 is story 024, deferred to M0, and ADR-0012 E2 makes 0 B checks advisory. A throwaway `AllocAssert.Measure` run over 2,000 frames of `Feed` + `ForceState` + `FlushToLatch` measured 0 B on net10.0 Debug and Release.
**Manifest**: restamped 2026-10-16.2 → 2026-10-17.1. The 2026-10-17.1 revision changes only the Godot input adapter rules (ADR-0012 §7 D-1…D-8) and ADR-0026 rules; the "Input router core" rules this story implements did not change, and neither did the later text-only errata.
**Deviations / scope notes**: the constructor is `(ActionMap, InputSettings, IInputClock, UiEventLog)` (Mail Lead readiness note; the `IInteractionQuery` dependency is no longer needed for this story). `State` is a `ushort` (ADR Key Interfaces name a `RouterStateId` type that does not exist). The initial state is World until story 008. `Feed` ignores mouse motion, pad axes (including the LT trigger bound to Throw) and wheel records (stories 009 / 010). Immediate, Axis and Stepped presses latch and log `GateFired` but output nothing yet (stories 013 / 009 / 010). The router writes no `RawInput` log entries, only gate decisions and drops. Gate log layout: Reason = device class, IdA = latched action, IdB = physical input, IdC = router state, Flags = chord modifiers; `Dropped`: Reason = `EdgeKind`, IdA = `FrameAction`. The 12 as-built choices the story and ADR-0012 do not settle are batched in `docs/tech-debt-register.md` TD-212 as Mail Dev defaults pending Evan; none is an Evan ruling.

