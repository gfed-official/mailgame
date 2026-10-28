# Story 020: Owner-role `CarrierStep` in the ClientLayer — `InputFrame` → `CarrierInput`, transform sample emission and the published carrier state

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-27 (Replay AC moved here from story 010 by Evan 10:46 PM PT; depends on simulation-core 026). 2026-10-21 08:40 PT (Mail Review: input-controls 008 alone appends `PanelOpen`; dependency on 008 added). 2026-10-21 (docs: the Panel source is decided — `CarrierStatus.PanelOpen` comes from the tick's `InputFrameFlags.PanelOpen`, owner ruling per the ADR-0012/ADR-0014 errata 2026-10-21; the panel-mirror fallback is removed; stamp unchanged, the errata change no manifest rule). 2026-10-20 (docs: `CarrierStatus.OccupiedSeat` is published as `AnchorRef.None` at M-1a — ADR-0014 erratum 2026-10-20; stamp unchanged, the erratum changes no manifest rule)

## Context

**GDD**: `design/gdd/player-character-movement.md` (P2 authority and stepping, "input is latched per edge", "the Presentation renders the local carrier from the latest tick"; Game Feel "Input to motion"; Open Question OQ23 capsule vs doorway)
**Requirement**: `TR-movement-003` (client-authoritative movement — the owner-role half; the transform stream itself is ADR-0020/M0, registry coverage gap), `TR-movement-004`, `TR-movement-007` (fixed-step driver with catch-up cap; local extrapolation ≤ 1 tick — the Sim-side half), `TR-movement-001`, `TR-movement-020` (StatSheet `MoveSpeedAddPm` / `ExtraJumps`), `TR-movement-021` (owner `CarrierStep` budget — advisory)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-FEEL-1, PCM-P3-3 (end to end), PCM-P2-2 (owner half), PCM-PERF-1 counters.)*

**ADR Governing Implementation**: ADR-0016: ClientLayer previews and presentation timing (§1 ports, §2 step order, §6 own-carrier timeline) — Accepted 2026-10-16 (primary); ADR-0013 (§1 owner role: `ClientStepId.CarrierStep` calls `OwnedCarrier.Step` once per client tick with the tick's `InputFrame` translated into `CarrierInput`), ADR-0003 (§6 `ClientLayer`, "never reads `SimWorld`"), ADR-0012 (`InputFrame`) secondary — Accepted
**ADR Decision Summary**: Order per tick is `MirrorDrain → InputLatch → CarrierStep → Previews`. `CarrierStep` is Players' `IClientStep`: it translates the tick's `InputFrame` (Q15 move on the unit disc, absolute look, held bits, ordered edges on the first tick of a catch-up batch only) into `CarrierInput`, builds `CarrierEnv` from the ClientLayer's views (world tables, remote-capsule and support sources, settings), calls `OwnedCarrier.Step` and reports the result as the authoritative transform through the transform stream. Role differences are inputs, never branches on "am I the host". The own carrier's timeline is "present, ≤ 1-tick interpolation by `AlphaPermille`".
**ADR Version**: ADR-0016 2026-10-16; ADR-0013 2026-10-16 (l.147 and l.226: recorded inputs and snapshots replay bit-identically); ADR-0006 2026-10-16 (§7 recorder and replay); ADR-0003 2026-10-16; ADR-0012 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer / Foundation ClientLayer)**:
- Required: client steps live in the Sim (`<System>/Client/`) and read only facts from mirror parts or ports; they never reference `SimWorld` or any host-side system instance (ADR-0016)
- Required: `OwnedCarrier.Step` is the only movement implementation; the host never steps an Alive remote carrier (ADR-0013)
- Required: `InputLatch` never dispatches verbs; the carrier consumes the frame's move/look/held state and the jump/sprint edges (ADR-0016 §2)
- Guardrail: `CarrierStep` + focus p99 ≤ 0.10 ms; `ClientLayer.Step` ≤ 0.3 ms p99; 0 B per tick after warm-up (advisory until the reference machine)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` and ADR-0016 §2, scoped to this story:*

- [ ] **Binding.** Players binds an `IClientStep` at `ClientStepId.CarrierStep`; one `ClientLayer.Step` runs `MirrorDrain → InputLatch → CarrierStep → Previews(4a Focus … 4e)` (Story 014's order test still passes) and the step calls only `OwnedCarrier.Step` + counter flush + sample emission + state publication — focus/press/hold are not run here.
- [ ] **Frame → input translation (table-driven).** `moveX/moveY` (Q15 on the unit disc) pass through unchanged; `LookYaw` is the frame's absolute BAM; `SprintHeld` is the held bit (Hold / Toggle / Auto modes are the router's); `JumpPressed` is true only on the tick that carries the Jump press edge (edges are on the first tick of a catch-up batch only) and is consumed once; `Aiming` is the aim held bit; `HostAuthored = false`. A frame with 3 catch-up ticks and one Jump press jumps once, on tick 1 of the batch; the two later ticks keep the held state and latest `moveIntent`.
- [ ] **PCM-FEEL-1 (end to end).** In an in-memory ClientLayer harness (`FrameRunner`-free plain `Step` loop, ADR-0002 §5) a walk input latched before tick N has moved the body 41 ± 1 mm by the end of that tick's `CarrierStep`; a 180° camera-relative turn of the stick completes body yaw in 8 ticks; PCM-P3-3's camera-yaw-90° heading holds end to end.
- [ ] **Inputs from other systems.** `SpeedPermille` comes from the `ICarrierLoadSource` port (weight → D1, grid-inventory story 016's pure function over the own-container mirror); `BuffAddPm` = `min(MoveSpeedAddPm, buffCapPm)` and `ExtraJumps` come from the client `IStatSheet` copy (static at M-1a: 0 / 0) with the F1 stat clamps applied (`longLegsStacks` ≤ 3, `addPm` ≥ 0); `ContextFlags` come from `CarrierStatus` (Story 013) — Panel from the tick's `InputFrame` flag (see Implementation Notes), Dead/Mounted from life/body. A table test: a frame with `InputFrameFlags.PanelOpen` set gives `CarrierStatus.PanelOpen = true` and `ContextFlags.Panel` for that tick; the next frame without it clears both.
- [ ] **Seeding.** On the first stepped tick the owned `CarrierState` is created at the same spawn the host record uses (`CarrierSpawn.At(slot, world)`, Story 001), so the host's seeded position and the client's first sample agree; the local slot comes from the `ILocalPlayer` port (M-1a: slot 0, available after the `PlayerJoin` reply).
- [ ] **Transform sample emission.** After each step a `TransformSample` (feet X, Y, Z; yaw; flags incl. swimming/sprinting) with a per-sender monotonically increasing `Seq` is written through `ITransformSampleSink.TryWrite`; at M-1a solo it is emitted **every tick** (the 15 Hz / 4 Hz cadence is ADR-0020's); a refused write (ring full) is counted, never retried within the tick; a test double sink asserts one sample per tick in order.
- [ ] **Published state.** `ICarrierView` exposes, read-only, the current and previous tick's `CarrierState` (feet, BodyYaw, `Loco`, flags, velocity, `RemX/RemZ`), the aim (look yaw + pitch), `CarrierStatus` (Story 013; its `OccupiedSeat` is `AnchorRef.None` at M-1a — Vehicles' client mount state fills it at M1, ADR-0014 erratum 2026-10-20) and the swept result of the last tick — everything Story 022 needs to render the pose without touching the solver — plus the `CarrierInput` of the last step (`LastInput`, read-only) so Story 022 can run its presentation-only one-tick probe on a copy of the state.
- [ ] **Counters and budget.** The solver counters are flushed to `SimCounters` each tick (Story 002); 600 warmed ticks of the golden script (Story 010) through the ClientLayer allocate 0 B; a `[Trait("Category","Perf")]` advisory test logs `CarrierStep` + focus p99 for the worst-case fixture and never fails (PCM-PERF-1 wall-clock is advisory; the 8-player/2 000-anchor fixture stays BLOCKED on the tick-driver spike).
- [ ] **OQ23 measurement.** On the world-stub M-1a street, a scripted walk from the Post Office pad reaches within 2500 mm (chest to anchor, unoccluded) of every lot's mailbox `Anchor` and of `po_intake`'s `IntakeAnchor`, and the 300/1400 capsule passes the Post Office entrance/approach geometry; any obstruction is recorded as a finding for GDD OQ23 in this story's evidence note (not silently adjusted).
- [ ] **Replay (moved from story 010, PCM-P2-1 replay half).** *Evan 2026-10-27 10:46 PM PT, Mail Lead chat, answering the widget "Player-character 010's replay check: where should it go?": "Move player-character 010's replay check (AC 7) to player-character 020".* With `OwnedCarrier.Step` now driven by `CarrierStep`, the replay path is real. In the in-memory harness: (1) run story 010's committed 600-tick script (`tests/unit/player/golden/carrier-600.script.csv`, with its scripted remote-capsule and support snapshots) through `ClientLayer.Step`, with `ITransformSampleSink` wired to `SimWorld.TryWriteSample` on a world built with an `InMemoryRecorder` (sim-core 026); (2) `ReplayDriver.Run` on that recording returns `ReplayOutcome.Reproduced` with `SampleMismatches = 0` (every carrier `TransformSample` replays bit-identically, ADR-0013 l.147 / l.226, ADR-0006 §7); (3) the per-tick transform hash chain of the ClientLayer run equals story 010's committed golden (`carrier-600.golden.txt`), so the binder changes nothing the raw solver produced. The recorder does not log `InputFrame`s or the remote-capsule / support spans at M-1a (solo: empty and inert, ADR-0013 l.146), so the client side of the replay is the committed script itself; never silently pass.

---

## Implementation Notes

*Derived from ADR-0016 §1–§2, ADR-0013 §1, ADR-0012 §4:*

- Files in `src/PerformativeMail.Sim/Players/Client/`: `CarrierStepBinder.cs` (`IClientStep`), `CarrierInputMapper.cs` (pure `(InputFrame, CarrierStatus, load, stats) → CarrierInput`), `ICarrierView.cs` + `CarrierView.cs`, `ITransformSampleSink.cs`, `ICarrierLoadSource.cs`, `ILocalPlayer.cs`, `CarrierSpawn.cs` (shared with Story 001's host spawn).
- **Ports defined here (PROVISIONAL, additive):**
  - `ITransformSampleSink { bool TryWrite(in TransformSample s); }` — wired by the host shell to `SimWorld.TryWriteSample` (sim-core story 021) in solo, and to the ClientAdapter's transform stream at M0 (ADR-0020). Wiring owner: `production/epics/godot-host-shell/story-005-host-session-composition-ports-render-clock.md` and `network-adapter-loopback/story-007-host-loopback-session-frame-timing.md` (local join and per-frame timing); without it Story 019's `CheckInteraction` reports `PositionStale`.
  - `ICarrierLoadSource { int SpeedPermille { get; } int WeightPoints { get; } }` (`WeightPoints` also feeds the whole-body load blend, Story 025) — **no existing grid-inventory story exposes weight on the client** (story 016 is the host-side `IInventoryApi.SpeedPermille`). Implement the adapter over `IPanelView` entry facts using grid-inventory story 016's pure weight function; if that function is host-bound, split the pure part out additively and tell the grid-inventory owner (cross-epic gap logged in EPIC.md).
  - `ILocalPlayer { PlayerSlot Slot { get; } EntityId Id { get; } bool IsKnown { get; } }`.
- `ClientPorts` / `ClientLayer.Attach` and `IRequestSink` are unowned by any current story (see Story 016's note) — attach `ITransformSampleSink`, `ICarrierLoadSource` and `ILocalPlayer` through the same additive `Attach`/builder mechanism, not by a new global.
- **Panel context on the Sim side (owner ruling 2026-10-21; ADR-0012 / ADR-0014 errata 2026-10-21):** `CarrierStatus.PanelOpen` is the tick's `InputFrameFlags.PanelOpen` bit. The router sets it while its overlay is Panel (input-controls story 008), and `InputLatch` latches it on every tick. Pass it to Story 013's status builder; `ContextFlags.Panel` follows from the status. Do not read Grid Inventory's panel mirror and do not decode `RouterStateId` (it stays informational). Input-controls story 008 alone appends the `InputFrameFlags.PanelOpen` member; this story only reads it, so it depends on 008. The router never makes Sprint/Jump live in Panel (action not live), so `SprintHeld`/`JumpPressed` are already false there; the solver's own Panel refusal (PCM-P3-2) is defence in depth. Getting panel-open to the host (the frame is client-only, ADR-0012 §4) is not this story's: it waits on ADR-0020.
- The step must stay within the 0.10 ms budget including `Int128` slope tests; run the PCM-PERF-1 microbenchmark from Story 010 through the binder, not only the raw solver.
- Do not read the wall clock, `delta`, or `IRenderClock` here — everything is client Sim ticks (`ClientStepContext.ClientTick`).

---

- **Catch sender at M-1a (2026-10-23; erratum E9 to ADR-0015 — "At M-1a the Catch press binds a no-op `ICatchArmSender`" — approved by Evan 2026-10-23 8:10 AM PT, "approve all").** throwing-catching 022, which builds the real `ICatchArmSender`, is **Deferred** to M0 (Evan, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024"). When this story wires `EdgeResolveStep`, bind a `NullCatchArmSender` (a sealed class in `Sim/Players/Client/` whose `Send` does nothing). A Catch press then enters Channel exactly as player-character 016 specifies, with no host effect. throwing-catching 022 replaces the binding.

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Stories 014–018: focus, press, hold, snapshot (they run in `Previews`). Story 019: host ingest and `CheckInteraction`.
- Transform replication cadence, `Teleport`, remote interpolation (ADR-0020, M0). Mounted / Dead / Frozen bodies (M-1b / M0 / M1).
- Rendering, camera and animation: Stories 021–026.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/player/CarrierStepClientLayerTests.cs` (`[Trait("Id", "PCM-FEEL-1")]`, `PCM-P3-3`) and `tests/integration/player/StreetReachabilityTests.cs` (OQ23 measurement); the Replay AC lives in `CarrierStepClientLayerTests.cs` (`[Trait("Id", "PCM-P2-1")]`, replay half) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002, 005–010 (the locked solver), 013 (`CarrierStatus`), 014 (Focus order test), 019 (host ingest to pair with the sink); `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md` (`ClientLayer`, `InputFrame`, `ClientStepId.CarrierStep`); `production/epics/input-controls/story-008-router-state-transitions-and-passthrough.md` (appends and sets `InputFrameFlags.PanelOpen`); `production/epics/simulation-core/story-021-transform-sample-ring-latch.md` (`TransformSample`, `TryWriteSample`); `production/epics/grid-inventory/story-016-weight-speed.md` (weight function); `production/epics/content-data/story-014-statsheet-build-read-explain.md` (`IStatSheet.Read`); `production/epics/world-stub/story-006-m1a-street-and-fixtures.md` (M-1a street for the OQ23 measurement); `production/epics/simulation-core/story-026-inmemory-recorder-replay.md` (`InMemoryRecorder`, `ReplayDriver`; the Replay AC moved from story 010, Evan 2026-10-27 10:46 PM PT)
- Unlocks: Stories 022, 023, 024 (published `ICarrierView`); manual-delivery / throwing-catching end-to-end tests (a real moving, interacting carrier)
