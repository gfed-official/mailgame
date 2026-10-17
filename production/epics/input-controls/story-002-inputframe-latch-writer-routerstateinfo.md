# Story 002: `InputFrame`, `InputEdge`, `IInputLatchWriter`, `RouterStateInfo` and the `InputLatch` push side (ADR-0012 §4)

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

> **Reconciliation story.** `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md` defines `InputFrame` as **PROVISIONAL** (its own marker: "field encoding owned by ADR-0012") and predates `IInputLatchWriter`. This story is the ADR-0012 §4 migration step (1): it finalises the widths in the same file (`ClientLayer/InputFrame.cs`), adds the push-side writer the router calls, and adds the Sim-side `RouterStateInfo` that Player Character story 020 asked for. Do not edit story 023; if its code already exists, this story migrates it.

## Context

**GDD**: `design/gdd/input-controls.md` (R11 "Input frame", G4, R2 reachable states; AC H-R11-1)
**Requirement**: `TR-input-009` (InputFrame Sim struct: tick, int16 move, absolute look, held bits, ordered edges, steps); Sim half of `TR-input-008` (edges attached to the first catch-up tick)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§4 `InputFrame`, `InputEdge`, `IInputLatchWriter`; edge overflow) — Accepted 2026-10-16
**ADR Decision Summary**: `InputFrame` is a blittable Sim struct (Q15 move, absolute BAM look, `HeldBits` per `FrameAction`, ordered `[InlineArray(32)]` edges, `[InlineArray(4)]` saturating steps) assembled by the Sim-side `InputLatch` from what the router pushes through the Sim-defined `IInputLatchWriter`. On the first tick of a catch-up batch the frame carries **all** latched edges and steps and clears them; later ticks carry state only; a frame that runs no tick clears nothing. Overflow beyond 32 latched edges coalesces same-action level presses, then drops the newest *press* edges only — never a release or a cancel.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None. Pure C# in `PerformativeMail.Sim`.

**Control Manifest Rules (Foundation)**:
- Required: input edges attach only to the tick with `isFirstTickInBatch == true`; later catch-up ticks carry held bits and the latest `moveIntent`; a 0-tick frame keeps edges latched in order; a dropped tick never discards an edge (ADR-0002 §2, ADR-0012 §4)
- Required: the Sim never sees milliseconds; integer-only fields; `InputLatch` latches only and **never dispatches a verb** (ADR-0016 §2)
- Forbidden: `float`/`double`/`decimal`/`Half` in Sim; `ClientLayer` referencing `SimWorld` or Client.Core; `List<T>`/LINQ on the tick path (ADR-0006)
- Guardrail: 0 B per tick; ClientLayer p99 0.3 ms (advisory)

---

## Acceptance Criteria

*From GDD R11 and H-R11-1 and ADR-0012 §4, scoped to this story:*

- [x] **AC-1 Shapes.** In `PerformativeMail.Sim.ClientLayer`: `InputFrame` (`Tick`, `MoveX/MoveY` int16 Q15, `LookYaw` ushort BAM absolute, `LookPitch` short BAM, `FocusCosMinPm` short, `RouterStateId` ushort, `Flags` `InputFrameFlags` {`FirstInBatch`, `FocusLost`, `Paused`, `LookReseeded`}, `HeldBits` ulong, `EdgeCount`, `StepCount`, `DroppedEdges` ushort, `Edges` `[InlineArray(32)] InputEdge`, `Steps` `[InlineArray(4)] InputStep`) is `unmanaged` (no references) and its `sizeof` is pinned by a test to the actual value (ADR estimates ≈ 560 B — a field change must be a deliberate edit); `InputEdge` is exactly 16 B with fields `Action`, `Kind` (`Press=1`, `Release=2`, `Cancel=3`), `Reason` (`EdgeReason`), `DeviceClass`, `RouterStateId`, `ArrivalSeq`, `TargetEntity`, `TargetSlot`, `PresentedSeq`; `FrameAction : byte` is append-only (≤ 64) with at least `Interact, Jump, Sprint, Aim`; `EdgeReason : byte` = {`None, Back, StateChange, FocusLost, DeviceLost, ToggleSecondPress, EntryStale, Busy`}. `HeldBits` bit *n* ↔ `FrameAction` *n* (a test pins the mapping).
- [x] **AC-2 Writer and latch.** `IInputLatchWriter { void PushEdge(in InputEdge e); void PushSteps(FrameAction a, int n); void SetState(in InputState s); }` is declared in the Sim; the built-in `InputLatch` step implements it (`InputState` = move, look yaw/pitch, held bits, `FocusCosMinPm`, `RouterStateId`, flags). Over a script of pushes and `ClientLayer.Step(isFirstTickInBatch)` calls: the first tick of a batch emits one `InputFrame` carrying **all** edges pushed since the last emitted frame in `ArrivalSeq` order and the saturated steps, and clears them; later ticks of the batch carry state only (no edges); a frame that runs 0 ticks clears nothing and the edges arrive, in order, on the next tick that runs (SC-R17-1 re-asserted through the writer path).
- [x] **AC-3 Overflow.** With 40 edges pushed before one emitted frame: same-action level presses are coalesced first (two Press edges of one Tap/level action with no state change between them collapse to the first); if the latch is still over 32, the newest **Press** edges are dropped, never a Release or Cancel; `DroppedEdges` equals the number dropped and a counter / check-ring record is written once per overflow; order of the survivors is unchanged. `ArrivalSeq` comparisons use wrap-safe `SerialU16.IsNewer` (built by its first consumer if absent — see `production/epics/simulation-core/story-002-id-value-types.md`).
- [x] **AC-4 Steps.** `PushSteps(a, n)` accumulates per action in a signed byte with saturation at ±127 (the router applies F6's per-action cap before pushing; the latch only guarantees no wrap); at most 4 distinct actions per frame; a fifth distinct action in one frame is dropped and counted.
- [x] **AC-5 `RouterStateInfo`.** `PerformativeMail.Sim.ClientLayer.RouterStateInfo` defines the layer enums (`CaptureLayer` {None, TextEntry, Pause}, `OverlayLayer` {None, Panel, Map, Station, Draft, Results, History}, `BodyLayer` {OnFoot, Build, Mounted, Dead}, `SubmenuLayer` {None, ActionMenu, SplitStepper}) and pure `static` `Pack/Unpack` over the 3×7×4×2×2×3 = 1008 tuple space to a `ushort` (`((((capture*7+overlay)*4+body)*2+holding)*2+aiming)*3+submenu`), plus `IsPanel(id)`, `IsAiming(id)`, `IsCaptured(id)`, `IsHolding(id)`. A test round-trips all 1008 tuples and rejects ids ≥ 1008. This satisfies the "Sim-side `RouterStateInfo.IsPanel(ushort id)`" request in Player Character story 020.
- [x] **AC-6 Settings port.** `IClientInputSettings { int HoldSettingMs { get; } }` is declared in `ClientLayer/`; a `DefaultClientInputSettings` returns 400. (Player Character 011/017 and Manual Delivery 001 take `holdSettingMs` as an argument; no existing story defines where it comes from — story 019 supplies the Client.Core implementation.)
- [x] **AC-7 H-R11-1 (frame half).** `LookYaw` is absolute: pushing `SetState` with yaw 0x0000, 0x2AAA, 0x5555, 0x4000 over four ticks yields exactly those values in the four emitted frames (no deltas, no accumulation); `Steps` are `sbyte`.

---

## Implementation Notes

*Derived from ADR-0012 §4 and sim-core story 023:*

- Files in `src/PerformativeMail.Sim/ClientLayer/`: `InputFrame.cs`, `InputEdge.cs`, `FrameAction.cs`, `EdgeReason.cs`, `IInputLatchWriter.cs`, `InputState.cs`, `InputLatch.cs` (extend 023's), `RouterStateInfo.cs`, `IClientInputSettings.cs`. Namespace `PerformativeMail.Sim.ClientLayer`. `[InlineArray]` is on the manifest's BCL allow-list.
- Layer **enums live in the Sim** (not Client.Core) because Sim code must decode `RouterStateId`; Client.Core's `RouterState` (story 004) reuses them. This is the only way the dependency direction (Client.Core → Sim) holds and Player Character 020 can read the panel flag.
- The latch is a pure data structure: ring of edges (capacity 256 behind the 32-edge frame, ADR-0012 §4 "preallocated 256-entry ring"), state slot, step accumulators. No allocation after construction; no `foreach` over interfaces.
- "Coalesce" here means *level presses only* (Tap and Held-state actions); Press/Release pairs of Hold, Aim and Interact grammar are never merged (the Sim needs both).
- `Tick` in the emitted frame is the client tick counter from `ClientStepContext`.
- `Flags.Paused` and `FocusLost` are set by the router's `SetState` and cleared when it clears them; this story only transports them.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 003+: everything in Client.Core (`RawInputRecord`, the router, press machines, F3–F13 maths). Story 007 decides which grammar emits which edge kinds.
- Player Character 020: `CarrierInputMapper` consuming the frame. Player Character 016/017: edge resolution and hold counting.
- Replay recording of frames (`production/epics/simulation-core/story-026-inmemory-recorder-replay.md`).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/sim/ClientLayer/InputFrameShapeTests.cs`, `tests/unit/sim/ClientLayer/InputLatchWriterTests.cs` (`[Trait("Id", "SC-R17-1")]`, `[Trait("Id", "H-R11-1")]`), `tests/unit/sim/ClientLayer/RouterStateInfoTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — tests/unit/sim/ClientLayer/InputFrameShapeTests.cs, InputLatchWriterTests.cs (SC-R17-1, H-R11-1), RouterStateInfoTests.cs, tests/unit/sim/Primitives/SerialTests.cs (all pass, four legs)

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md` (`ClientLayer`, `InputLatch` shell, `ClientStepContext`), `production/epics/simulation-core/story-002-id-value-types.md` (`SerialU16` frozen interface), `production/epics/simulation-core/story-007-simcounters-simcheckring.md` (overflow record)
- Unlocks: Stories 003–005 (Client.Core uses these Sim types), 007 (edge emission), 008, 020, 021; `production/epics/player-character/story-020-carrierstep-clientlayer-integration.md` (`InputFrame`, `RouterStateInfo`), `production/epics/player-character/story-016-press-resolution-tap-catch-armed.md` (`InputEdge` fields)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 7/7 passing (no deferred items)
**Deviations** (advisory, logged as TD-073): EdgeCount/StepCount are internal-set properties and Edges/Steps are exposed as ReadOnlySpan; renames InputEdgeKind→EdgeKind, MoveZ→MoveY, Held→HeldBits; the level-action coalesce mask (default 0) is set via ClientLayerBuilder.SetLevelActionMask, and SimComposition still builds the layer with no check ring and an empty mask (left to wiring); a 257-entry linear edge buffer replaces the 256 ring, and its last-resort path drops the newest edge of any kind once 257 non-press edges are latched (router-bug case, contradicts ADR-0012 §4 "never dropped"); Releases/Cancels beyond the 32-edge window carry over to the next first tick; coalescing also stops at a change of edge RouterStateId (QL gap 2, fixed in-story). Out of scope: SerialU8/SerialU32 added beside SerialU16 (accepted by LP).
**Test Evidence**: Logic — tests/unit/sim/ClientLayer/InputFrameShapeTests.cs, InputLatchWriterTests.cs, RouterStateInfoTests.cs, tests/unit/sim/Primitives/SerialTests.cs
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (7 advisories: 3 and 5 fixed in-story, the rest go to TD-073); QL-TEST-COVERAGE GAPS → gaps 1 (all edge fields on emitted frames, including an event-time state that differs from the frame state and a FocusLost Cancel) and 2 (no coalesce across a RouterStateId change; seen failing without the fix) fixed in-story, gaps 3–7 go to TD-073. Closes TD-054(2)(3)(4)(7).

