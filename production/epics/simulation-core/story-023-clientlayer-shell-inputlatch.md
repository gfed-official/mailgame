# Story 023: ClientLayer shell, InputLatch and the IHostClock seam

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 400 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R2 two roles — `ClientLayer` step order; R17 input edges on the first tick; N1 `IHostClock`)
**Requirement**: `TR-sim-014` (`SimWorld` and `ClientLayer` separate), `TR-sim-041` (port interfaces: `IClientStep`, `IHostClock`)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0003: Phase Order, Composition Root and Cross-System Calls (§6 two roles) — primary; ADR-0002 (§2 input edges, `isFirstTickInBatch`), ADR-0011 (§1 `IHostClock`) — secondary
**ADR Decision Summary**: `ClientLayer` is a shell that runs bound `IClientStep`s once per client tick in fixed `ClientStepId` order — `MirrorDrain`, `InputLatch`, `CarrierStep`, `Previews` — bound by owning systems through `ClientLayerBuilder.Bind(ClientStepId, IClientStep)`; it reads only its mirror, never `SimWorld`, even on the host. Latched input edges attach only to the tick with `isFirstTickInBatch == true`; later catch-up ticks carry held bits and the latest `moveIntent`; a frame that runs 0 ticks keeps its edges latched in order; a dropped tick never discards an edge. `IHostClock { uint EstimatedHostTick; bool IsValid; }` is defined by the Sim and consumed by `ClientLayer`/Presentation; its implementations are the adapter's.
**ADR Version**: 2026-10-16 (ADR-0003, ADR-0002, ADR-0011)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: `_Input` vs `_Process` order within a Godot frame is UNVERIFIED (ADR-0012 input spike) — it affects when the router hands edges to the latch, not the latch semantics built here.

**Control Manifest Rules (Foundation)**:
- Required: `ClientLayer` runs bound `IClientStep`s in `ClientStepId` order `MirrorDrain`, `InputLatch`, `CarrierStep`, `Previews` via `ClientLayerBuilder.Bind`; input edges attach only to the tick with `isFirstTickInBatch == true`; later catch-up ticks carry held bits and the latest `moveIntent`; a 0-tick frame keeps edges latched in order; a dropped tick never discards an edge.
- Forbidden: `ClientLayer` reading `SimWorld` (even on the host); Sim Core naming a gameplay type in the shell; input frames entering `SimWorld` (DEV-2).
- Guardrail: ClientLayer p99 0.3 ms (advisory until a reference machine exists); the step loop allocates 0 B.

---

## Acceptance Criteria

*From GDD SC-R17-1 and ADR-0003 §6, scoped to this story:*

- [x] **AC-1 SC-R17-1.** Given a frame that runs 3 client ticks with a press and a release latched before it, and a later frame that runs 0 ticks with one press latched: both edges, in order, are delivered on the first tick only (`isFirstTickInBatch == true`); ticks 2 and 3 carry no edges but the current held bits and the latest `moveIntent`; the 0-tick frame's press stays latched and is delivered, in order, on the next tick that runs.
- [x] **AC-2 Step order.** With stubs bound to every `ClientStepId` (two of them on `Previews`), one `ClientLayer.Step(isFirstTickInBatch)` runs `MirrorDrain`, `InputLatch`, `CarrierStep`, `Previews`, `Previews` in that order, the two `Previews` stubs in bind order; unbound steps are no-ops; `ClientStepContext` carries `IsFirstTickInBatch` and the client tick counter; binding after the layer is built throws.
- [x] **AC-3 Seams.** `IHostClock` is declared in the Sim with exactly `uint EstimatedHostTick { get; }` and `bool IsValid { get; }`; `ClientLayer` receives an `IHostClock` by constructor and exposes it to steps through the context; `ClientLayer`'s constructor and fields reference no `SimWorld` type (checked here by reflection; SC-ARCH-1 re-asserts in Story 028).

---

## Implementation Notes

*Derived from ADR-0003 §6, ADR-0002 §2, ADR-0011 §1 and GDD Input & Controls R11:*

- Files in `src/PerformativeMail.Sim/ClientLayer/` (namespace `PerformativeMail.Sim.ClientLayer`): `ClientStepId.cs` (`enum ClientStepId : byte { MirrorDrain, InputLatch, CarrierStep, Previews }`), `IClientStep.cs` (`void Step(in ClientStepContext ctx)`), `ClientStepContext.cs` (readonly struct: `IsFirstTickInBatch`, `ClientTick`, access to the latched `InputFrame` and `IHostClock`), `ClientLayerBuilder.cs`, `ClientLayer.cs` (`Step(bool isFirstTickInBatch)` — the name `FrameRunner` calls, ADR-0002 §2), `InputLatch.cs`. `IHostClock.cs` in `Core/` (the seam belongs to Sim Core; ADR-0011).
- **`InputLatch` (the built-in `InputLatch` step).** The router (input-controls epic, ADR-0012 — Accepted 2026-10-16; `production/epics/input-controls/story-002-inputframe-latch-writer-routerstateinfo.md`) pushes into the latch between ticks: ordered edges and the current axis/held state. On a tick with `IsFirstTickInBatch` the latch emits a per-tick `InputFrame` containing *all* edges latched since the last emitted frame, in arrival order, and clears them; on later ticks of the batch the frame has no edges, only held bits and the latest `moveIntent`. If no tick runs, nothing is cleared.
- **`InputFrame` shape — provisional.** Input & Controls GDD R11 lists the fields: `tick`, `moveIntent` (int16 quantized ×2), `lookYaw`, `lookPitch`, `held` (per-action bits), ordered `edges[]` (action, press/release, arrival sequence, `routerStateId`), `steps[]`, `routerStateId`. ADR-0012 will fix widths and the router; define the struct in `ClientLayer/InputFrame.cs` with fixed-capacity inline edge storage (no `List<T>` on the tick path; capacity a `const`, overflow counted) and integer fields only, marked `// PROVISIONAL — field encoding owned by ADR-0012`. The latch semantics (AC-1) do not depend on the field list. (Assumption logged.)
- `CarrierStep` and `Previews` are filled by player-character, manual-delivery and throwing-catching; `MirrorDrain` by network-adapter-loopback (it applies batches handed over after the previous `Tick()`).
- **Published-prompt slot** (architecture.md `PromptData`): its shape is ADR-0016's (Accepted 2026-10-16). It is added by the first `Previews` consumer (manual-delivery, `production/epics/manual-delivery/story-003-availability-preview-step-tracker.md`), not here.
- No reference to `SimWorld` anywhere in `ClientLayer/` — the host's `ClientLayer` learns everything through its mirror and the loopback adapter.

---

## Out of Scope

- Story 024: `FrameRunner` calling `ClientLayer.Step(i == 0)` before each `Tick()`.
- Input router, action map, device handling: input-controls epic (blocked on ADR-0012).
- `IHostClock` implementations (host: `CurrentTick`; client estimator): network-adapter-loopback.
- `PromptData` / published-prompt slot: manual-delivery (ADR-0016).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/ClientLayer/InputLatchTests.cs` (`[Trait("Id", "SC-R17-1")]`), `tests/unit/sim/ClientLayer/ClientLayerStepOrderTests.cs` — must exist and pass

**Status**: [x] Created and passing — InputLatchTests.cs (7 tests, 5 tagged SC-R17-1), ClientLayerStepOrderTests.cs (13 tests incl. NonParallel 0 B AllocAssert)

---

## Dependencies

- Depends on: Story 001, Story 011 (pattern for builder/freeze; no runtime dependency on `SimWorld`)
- Unlocks: Story 024, network-adapter-loopback (`MirrorDrain`, `IHostClock`), input-controls, player-character (`CarrierStep`)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (none deferred)
**Deviations**: None blocking. Advisory (logged TD-054): `PreviewSubStep`/`BindPreview` left to player-character 014 (Previews kept as a per-id list so it is additive); `InputFrame` PROVISIONAL with a 32-edge `[InlineArray]`, interim drop-newest overflow counted by `CounterId.InputEdgeOverflow = 30` (input-controls 002 owns the final policy + `IInputLatchWriter`); bind-after-build reuses `SimBuildErrorCode.BindAfterFreeze`; built-in latch runs first in the `InputLatch` slot; `ClientLayer` owns a client-side `SimCounters`. Manifest stamped 2026-10-16.2. Extra files: `Core/CounterId.cs` (append), `tests/unit/sim/Core/SimCountersTests.cs` (pin).
**Test Evidence**: Logic — tests/unit/sim/ClientLayer/InputLatchTests.cs, tests/unit/sim/ClientLayer/ClientLayerStepOrderTests.cs; 2 097 tests/leg green (net10/net8 × Debug/Release; Debug 15 skipped)
**Code Review**: Complete — LP-CODE-REVIEW APPROVE, QL-TEST-COVERAGE ADEQUATE
