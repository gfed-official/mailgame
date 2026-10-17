# Story 005: Host session wiring — `SimComposition.Build` over the loopback session, `ClientLayer.Attach(ports)`, transform-sample sink, local join and the render clock (boot steps 5–6)

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day; ~350 lines prod + smoke scenarios)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/simulation-core.md` (R2 two roles one code path, host loopback uses the same encoder; R4 composition root; R5/R6 sample ring and op ingress; R17 frame order; R21 the local player's slot)
**Requirement**: `TR-sim-013` (composition root; the host calls `Build` once), `TR-sim-014` (`SimWorld` and `ClientLayer` separate; host loopback uses the same encoder — the session is constructed here, no "is host" shortcut), `TR-sim-007` (Build/Freeze on the main thread), `TR-sim-001`/`TR-sim-004` (30 Hz constant, catch-up cap 4 — the driver is constructed from settings, not literals)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. `ClientPorts`/`ClientLayer.Attach` wiring, the transform-sample sink wiring, the local join and the render clock's inputs have **no TR-ID in the registry**; they cite ADR-0016 §1/§6, ADR-0011 §2 and the Player Character epic's "unowned" seam.)*

**ADR Governing Implementation**: ADR-0019: Godot Presentation Scene Architecture (§3 steps 5–6: session loopback, then `SimComposition.Build` → `ClientLayer.Attach(ports)`) — Accepted 2026-10-16 (primary); ADR-0011 (§2: the host's own client uses `ClientAdapter` over `LoopbackTransport`; no "is host" shortcut) and ADR-0016 (§1: the host shell attaches the ports once after `Build`, before the first `Step`; §6: `RenderClock` sampled once per frame) — Accepted 2026-10-16 (secondary); ADR-0003 (§3) and ADR-0002 (§2) for `Build`/`Freeze` thread and `FrameRunner` construction
**ADR Decision Summary**: The host process runs one `SimWorld` and one `ClientLayer`; the host's own client reaches the Sim only through `ClientAdapter` → `LoopbackTransport` → `HostAdapter`. `SimComposition.Build(in SimBuildArgs{ SimProfile, ContentSet, runSeed, SimInitialCounters? })` returns `SimHost{ World, Client }`; `ClientLayer.Attach(in ClientPorts{ Inbox, Panel, Requests, HostClock })` is called once, after `Build` and before the first `Step`. `FrameRunner(clock, driver, client, world, transport, settings)` is what `GameLoop` calls; `TickDriver` takes the catch-up cap from `SimSettings` and the frequency from the host clock.
**ADR Version**: ADR-0019 2026-10-16; ADR-0011 2026-10-16; ADR-0016 2026-10-16; ADR-0003 2026-10-16; ADR-0002 2026-10-16

**Engine**: Godot 4.7.1 .NET (the session itself is plain C# in Client.Core; this story only constructs and wires it) | **Risk**: MEDIUM
**Engine Notes**: Whether a real Godot loopback can bypass `MultiplayerAPI` is UNVERIFIED and irrelevant here (`LoopbackTransport` never touches it). The Godot-specific risk is limited to boot-time exception handling (story 004) and main-thread assumptions (story 003). Frame pacing and `Stopwatch.Frequency` facts are logged by story 004.

**Control Manifest Rules (Foundation / Core network seam)**:
- Required: the host's own client uses `ClientAdapter` over `LoopbackTransport` to its own `HostAdapter`; frame order `PumpInbound` → ticks → `FlushOutbound(tick)` (network-adapter-loopback 007); `ClientLayer.Attach` once, before the first `Step`; `SimComposition.Build` once, on the main thread (ADR-0003 §3, ADR-0011, ADR-0016)
- Required: the run seed comes from a constant or `--seed=<n>`, never the wall clock or `Random` (determinism, ADR-0005/0006)
- Forbidden: a "host" shortcut into the Sim; `ClientLayer` or Presentation reading `SimWorld`; a second `Build` for the same process; swapping a `ContentSet` into a running world (ADR-0010 §4)
- Guardrail: boot-to-ready advisory only; the session allocates nothing per frame (loopback 007)

---

## Acceptance Criteria

*From ADR-0019 §3 steps 5–6, ADR-0016 §1/§6 and the Player Character epic's seam ("wiring `ITransformSampleSink` to `SimWorld.TryWriteSample` for solo M-1a is unowned"), scoped to this story. Delta: story 004 left steps 5–6 `NOT WIRED`; `HostSession`, `FrameRunner`, `TickDriver` exist in Client.Core/Sim by their own epics; `ClientPorts`, `ClientLayer.Attach` and `IRequestSink` are **owned by no existing story** (simulation-core 023 predates ADR-0016; player-character EPIC.md lists the gap).*

- [ ] **Steps 5–6 build the session once.** Boot step 5 logs `BOOT 5 session`, then constructs the host session from `production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md` (`HostSession`: `SimComposition.Build(in SimBuildArgs)` with profile `M1aGreybox`, the step-2 `ContentSet` by reference and the run seed; `LoopbackTransport.CreatePair()`; `HostAdapter`/`ClientAdapter`; `FrameRunner`), and step 6 logs `BOOT 6 compose+attach` after `Build` returns, `ClientLayer.Attach` has run and `SimHost.World` is `Ready` with `CurrentTick == 0` on the boot thread. The factory is called exactly once per process (counter asserted; a second call throws). The run seed is `M1aRunSeed` (a named constant) unless `--seed=<n>` is passed in user args; it is logged. `TickDriver` is constructed with `Stopwatch.Frequency` and the catch-up cap from `ContentSet.SimSettings` (default 4); `FrameRunnerSettings.CatchUpGuardMs = 0`.
- [ ] **`ClientPorts` and `Attach` exist and are attached once.** `ClientPorts { Inbox, Panel, Requests, HostClock }` and `ClientLayer.Attach(in ClientPorts)` are added exactly as ADR-0016 Key Interfaces shows **if absent** (record in Last Updated whether this story added them); `Attach` called twice, or after the first `Step`, is refused. Ports come from the session (`IClientInbox`/`IRequestSink` from `ClientAdapter`, `IHostClock` from the host clock); an absent port (`IPanelView` before grid-inventory's `ContainerMirror` exists) gets a null object and the boot log says so (`BOOT 6 ports: panel=Null …`) — never a silent default. Other epics add their ports through an explicit `IPortContributor { void Contribute(ClientPortsBuilder b, BootContext ctx); }` list in `BootPlan` that runs before `Attach` (player-character 020's `ITransformSampleSink`/`ICarrierLoadSource`/`ILocalPlayer`, 025's `IHeldItemView`, input-controls 028's sinks use it); a smoke scenario registers a recording contributor and sees it called once, before `Attach`.
- [ ] **Local carrier samples reach the Sim and the local player is admitted.** The host wires `ITransformSampleSink` (player-character 020) to `SimWorld.TryWriteSample` for slot 0 (a thin adapter class in `src/Game/Boot/`; the Sim never learns it exists beyond the ring). With a fake `IMonotonicClock` (via `BootOptions.Clock`, a test seam on `BootContext`) advanced 40 ticks of 1/30 s with a scripted idle input, the smoke asserts: `ILocalPlayer.IsKnown` is true and slot 0; every sample write returned true (reject counter 0); player-character 019's `CheckInteraction` position-ingest does not report `PositionStale` (the 30-tick staleness rule) — proving the seam works end to end, not only compiles.
- [ ] **Render clock inputs.** `RenderClock` (Client.Core, `IRenderClock`) is constructed with the host `IHostClock` and the driver's `AlphaPermille` source, and handed to story 003's frame sequence for the once-per-frame `Sample()` (between `RunFrame` and `Present`). With the fake clock: at tick boundaries `RenderSimTimeMs == ((long)HostTick*1000 + AlphaPermille)/30` (floor), `AlphaPermille` is frozen at 0 while the driver is paused, and `IsValid` is true on the host.
- [ ] **One owner of the runner.** After boot, `GameLoop` holds the `FrameRunner`/`HostSession` and no other `src/Game` type references them (story 003's `check-sim-access.sh` passes with `Boot/` allowed). A smoke scenario (`session-ticks`) drives the booted session for 90 fake-clock frames of 1/30 s and asserts `World.CurrentTick == 90`, `Driver.Counters` shows 0 dropped ticks, and the world is not `Faulted`.

---

## Implementation Notes

*Derived from ADR-0019 §3, ADR-0016 §1/§6, ADR-0011 §2, ADR-0002 §2–§3:*

- Files in `src/Game/Boot/`: `SessionBootStep.cs` (steps 5–6), `PortContributor.cs` (`IPortContributor`, `ClientPortsBuilder`), `HostSampleSink.cs` (adapter to `TryWriteSample`), `BootOptions.cs` (`Clock`, `Seed`, `FixtureProfile` — smoke-only seams; defaults used in real runs), plus the Client.Core additions above if absent: `src/PerformativeMail.Sim/ClientLayer/Ports/ClientPorts.cs` (ports are Sim-defined, ADR-0016 §1) and `src/PerformativeMail.Client.Core/Timing/RenderClock.cs` + `IRenderClock.cs` (shared with throwing-catching 005 — whichever story runs first adds it; the other reuses).
- The step-5/6 split is logging granularity: if `HostSession` exposes one factory, log `BOOT 5` immediately before it and `BOOT 6` immediately after `Attach`. Do not re-implement `HostSession`; if its public API differs from what network-adapter-loopback 007 describes, adapt this story's glue and note it in Last Updated.
- Local join: `HostSession` admits the loopback peer into slot 0 (loopback 007 AC-2); nothing to add in the shell beyond waiting for `ILocalPlayer.IsKnown`, which is a published fact (views check it; the shell does not poll).
- `ITransformSampleSink` is a **Sim-side client port** (player-character 020: `bool TryWrite(in TransformSample s)`); in a future remote client (M0, ADR-0020) it is bound to the `ClientAdapter` transform stream instead. Keep the binding in one place (`HostSampleSink`) so M0 swaps it.
- Content hash / handshake is M0; nothing here sends `Hello`.
- Unowned adapters flagged for their epics, not built here: `ICarrierLoadSource` (client-side weight/speed from the inventory mirror) and `IHeldItemView` (selected hotbar item class) — player-character 020/025 define the ports; the grid-inventory Client.Core mirror supplies the adapters. They attach through `IPortContributor` when they exist.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 006: `PresenterDeps`, the `PresentationEventStream` feed, world-table hand-off to presenters (boot step 7). Story 008: step 8. Story 003: `FrameSequence` itself and pause.
- Loopback internals (network-adapter-loopback 001–007), remote peers, `Hello`/`HelloAck`, the real transport (M0 spike), persistence.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: smoke scenarios `session-ticks` and `session-ports` (`SMOKE … PASS`, exit 0) with the boot-log excerpt showing steps 5–6 and the ports line, in `production/qa/evidence/host-shell-session-evidence.md` — `/story-done` checks the scenarios and file exist; pass/fail via `/smoke-check`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004 (boot plan, `BootContext`), Story 003 (`FrameSequence`, `IPeerPresence`); `production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md` (`HostSession`), `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md` (`ClientAdapter`, `IHostClock`), `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`, `production/epics/simulation-core/story-025-composition-root-reflection-test.md` (`SimComposition`, `SimProfile.M1aGreybox`), `production/epics/player-character/story-020-carrierstep-clientlayer-integration.md` (`ITransformSampleSink`, `ILocalPlayer`), `production/epics/player-character/story-019-host-checkinteraction-position-ingest.md`
- Unlocks: Stories 006, 009, 011; `production/epics/player-character/story-022-playerview-scene-render-pose.md`, `production/epics/input-controls/story-028-gameloop-hookup-router-boot-settings-file.md` (`IPortContributor` and `ClientLayer` handles), every feature epic's end-to-end Godot run
