# Story 007: Host loopback session — FrameRunner over loopback and next-tick reply timing

> **Epic**: Network Adapter Core — Loopback (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: M (≈ 1 day; ≈ 400 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` R2 (one code path; `FrameRunner`), R17 (frame order), §Network N2 (host op in frame F applies at the next `Inputs.Apply`; reply visible to the host `ClientLayer` at exactly the next client tick)
**Requirement**: `TR-sim-014` (host loopback uses the same encoder; `SimWorld` and `ClientLayer` separate), `TR-sim-008` (inbound drained in `RunFrame` step 1), `TR-sim-042` (host `IHostClock` reads `CurrentTick`; Sim knows no peers)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0011: Network Adapter Boundary (§2 loopback is the remote path; frame order) — primary; ADR-0002 (§2 `RunFrame`), ADR-0008 (§7 send after `Tick()`) — secondary
**ADR Decision Summary**: The host process runs one `SimWorld` and one `ClientLayer`; the host's own `ClientLayer` reaches the Sim only through `ClientAdapter` → `LoopbackTransport` → `HostAdapter`, with no "is host" path. Per frame: `HostAdapter.PumpInbound()` (remote then loopback), `n = Advance(now)`, then per tick `ClientLayer.Step(i == 0)`, `SimWorld.Tick()`, `HostAdapter.FlushOutbound(tick)`; loopback hands the batch to the host `ClientLayer` inbox, applied by `MirrorDrain` at the next client tick. The host `IHostClock` reads `CurrentTick` (frozen in a solo pause).
**ADR Version**: 2026-10-16 (ADR-0011, ADR-0002, ADR-0008)

**Engine**: Godot 4.7.1 .NET (plain C#; the Godot `GameLoop` that will own this session is godot-host-shell) | **Risk**: LOW
**Engine Notes**: Whether a real Godot loopback can bypass `MultiplayerAPI` is UNVERIFIED; this design (ADR-0011, GDD N2) assumes it can — and this story's `LoopbackTransport` never touches it.

**Control Manifest Rules (Core — network seam)**:
- Required: the host's own client uses `ClientAdapter` over `LoopbackTransport` to its own `HostAdapter`; frame order `transport.Poll` (remote) → `loopback.Poll` (host) → ticks, sending each tick's batches after it → loopback hands batches to the host `ClientLayer` inbox; host `IHostClock` = `CurrentTick`.
- Forbidden: the host's client calling the Sim directly; an "is host" shortcut; sending before `Tick()` returns.
- Guardrail: `RunFrame` over loopback allocates 0 B after warm-up (advisory measurement only; the gate is simulation-core Story 029's probe).

---

## Acceptance Criteria

*From GDD SC-R2-1 (loopback half), SC-X-2 (timing rows, exercised with a fixture op until Manual Delivery exists — Story 009 re-runs it with the real request) and N2, scoped to this story:*

- [x] **AC-1 SC-R2-1 over loopback.** Given a host session (`SimComposition.Build` + `HostAdapter` + `ClientAdapter` over `LoopbackTransport.CreatePair()` + `FrameRunner`) whose injected clock makes `Advance` return 3, one `RunFrame()` produces the call log PumpInbound, Client, Sim, Flush, Client, Sim, Flush, Client, Sim, Flush; edges attach only at i = 0; and the transport flush runs after each `Tick()` returns, the last one after the last `Tick()`.
- [x] **AC-2 Local join.** Starting the session admits the local loopback peer into slot 0: a `System` `PlayerJoin` applies on tick 0 and, from then on, requests sent by the host's `ClientAdapter` are stamped with that player's id.
- [x] **AC-3 Next-tick timing (SC-X-2 timing rows, fixture op).** A fixture request sent by the host's own client in frame F applies at the first `Inputs.Apply` after F; a recording `IEventSink` spy (host Presentation stand-in) sees the resulting events in the frame that ran that tick; the host `ClientLayer` sees the reply at exactly the next client tick (its `MirrorDrain` in the following `ClientLayer.Step`), with no extra frame of queueing; and the host `IHostClock.EstimatedHostTick` equals `SimWorld.CurrentTick` between frames.

---

## Implementation Notes

*Derived from ADR-0011 §2 and ADR-0002 §2:*

- `src/PerformativeMail.Client.Core/Net/HostSession.cs` (or `Session/LocalHostSession.cs`): builds `SimHost` via `SimComposition.Build(in args)`, creates `LoopbackTransport.CreatePair()`, `HostAdapter` (loopback host end; `remote = null`), `ClientAdapter` (loopback client end), binds `HostAdapter` as the world's `IEventSink` (before `Freeze()` — composition must accept a sink; coordinate with simulation-core Story 025's `SimBuildArgs`/builder API), binds the `MirrorDrainStep`, and constructs `FrameRunner(clock, driver, client, world, hostAdapter as IFrameTransport, settings)`. Exposes `RunFrame()`, `Driver.AlphaPermille`, `ClientAdapter`, and a host `IHostClock`.
- **Host `IHostClock`**: ADR-0011 §1 — the host implementation returns `CurrentTick` (frozen during a solo pause because no ticks run). Implement as `HostClock(SimWorld)` read by host Presentation; the host `ClientLayer` still uses the `ClientAdapter` estimator (which on loopback equals it — AC-3 checks both).
- Ordering inside the frame: the loopback batch for tick T is sent by `FlushOutbound(T)` and polled by `ClientAdapter.PumpInbound` — call it at the start of the next `ClientLayer.Step` (inside `MirrorDrain`) so the reply is visible "at exactly the next client tick" (N2). Do not add a frame-delayed queue.
- Fixture op: a test-profile op kind (test-only range) whose handler raises one event and replies Ok — the same fixture style as simulation-core Story 027.
- This session class is what godot-host-shell's `GameLoop` will hold; keep it Godot-free.

---

## Out of Scope

- Story 009: SC-X-2 with the real Manual Delivery `deliver_matching` request (Blocked).
- Remote peers, `Hello`/`HelloAck`, SC-X-3 (remote op lower `opSeq` with a real remote transport), SC-X-4 — M0.
- godot-host-shell: `GameLoop`, `StopwatchMonotonicClock`, presentation.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/integration/net/HostLoopbackSessionTests.cs` (`[Trait("Id", "SC-R2-1")]`, `[Trait("Id", "SC-X-2")]` for the timing rows) — must exist and pass

**Status**: [x] Created — tests/integration/net/HostLoopbackSessionTests.cs (SC-R2-1, SC-X-2; 11 tests) + heartbeat tests in tests/unit/net/HostAdapterOutboundTests.cs, ClientAdapterTests.cs — Net/integration 342/342 on four legs

---

## Dependencies

- Depends on: Story 003, Story 004, Story 005, Story 006; simulation-core Story 024 (`FrameRunner`), Story 025 (composition)
- Unlocks: Story 009, godot-host-shell (`GameLoop` owns a `HostSession`), every feature epic's loopback tests

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (none deferred)
**Deviations**: Advisory only — Sim-side `SimBuildArgs.EventSink` added (bound before Freeze in SimComposition); HostSession builds its own ClientLayer (composition's SimHost.Client unused, args.HostClock ignored); HostAdapter sends a 7 B count-0 heartbeat batch to every Admitted peer with no entries on a tick (TD-068(1); ~210 B/s per remote peer at 30 Hz; not yet in ADR-0011); MirrorDrainStep pumps ClientAdapter then drains; RunFrame ends with a decode-only ClientAdapter.PumpInbound so the client clock equals CurrentTick between frames; a request sent from inside a client step applies at the first tick of the next frame; requests before the local join are dropped unadmitted (IsLocalJoined exposed). Remaining advisories → TD-070.
**Test Evidence**: Integration: tests/integration/net/HostLoopbackSessionTests.cs; full suite 2,966 / 0 failed (net10 Debug, net8 Release re-verified; Net/integration 342/342 on four legs)
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (.NET covered); QL-TEST-COVERAGE GAPS (1 required: in-step send in a multi-tick frame) → fixed → re-check by orchestrator on disk (test present, suite green)

