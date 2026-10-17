# Story 009: M-1a shift stub (Run Structure stand-in, phase 12) — `SetDeliveriesState`, the `DeliveriesState` event, OPEN/CLOSED/LOCKED from T + 1

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: S–M (~0.5–1 day; ~200 lines prod + ~200 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/mail-destinations.md` (R4 destination state; the flag for tick T is the value set in T − 1); architecture.md Q-ARCH-11 (minimum orchestrator call-down while Run Structure #19 has no GDD)
**Requirement**: `TR-mail-014` (`DeliveriesState` is a reliable-ordered event, not on the 5 Hz snapshot), `TR-mail-013` (`closeTick` for grace), `TR-mail-012` (pause freezes windows: no wall clock, ticks only)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §3 State; Ordering Note: "At M-1a, Run Structure is a shift-timer stub (phase 12) that pushes `DeliveriesState` into Mail"); ADR-0003 (Run is the one orchestrator; phase 12; flags set in T effective from T + 1); ADR-0011 (`DeliveriesState` reliable-ordered); ADR-0016 §1 (`RunMirror`: phase, `deadlineTick`, `closeTick`)
**ADR Decision Summary**: Mail's state inputs arrive from Run Structure by a downward call, `IMailApi.SetDeliveriesState(open, closeTick, unlockedMask)`. Run is the orchestrator. Mail re-raises nothing; Run's `DeliveriesState` event is the replicated one. LOCKED = `districtId ∉ unlockedDistricts`; CLOSED comes from `deliveriesOpen`. At M-1a the orchestrator is a phase-12 shift-timer stub, replaced at M1.
**ADR Version**: ADR-0017 2026-10-16; ADR-0003 2026-10-16; ADR-0011 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: Run Structure is the one orchestrator; it alone holds `IRunControl` (ADR-0003 §5). The stub is that system for M-1a
- Required: bind only phases you own (Run: phase 12) (ADR-0003 §1)
- Required: event kinds declare class, ≤ 48 B payload, `ResyncScope` (ADR-0008)
- Forbidden: Mail calling up into Run; reading a clock; a per-tick `DeliveriesState` event (raise on change only)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md` (M-R4-1, M-R4-2 a/b/f/g at system level, M-X-5 subset), scoped to the M-1a stub:*

- [ ] **AC-1 Stub cycle (M-R4-1a, M-1a form).** With District 1 unlocked and District 2 not (mask `0b10`), a stub configured Prep → Delivery → Closed: D1 destinations are CLOSED in Prep, OPEN for every tick of Delivery, and CLOSED after. D2 is LOCKED while deliveries are open and CLOSED otherwise (R5's precedence) throughout. No district is re-locked.
- [ ] **AC-2 T + 1 rule (M-R4-2a, b, f).** The stub calls `SetDeliveriesState(false, closeTick: T, …)` in phase 12 of tick T − 1. A hand op applied in phase 0 of T − 1 sees OPEN; an op started at T sees CLOSED (`closed`). At Delivery-start tick S (set in S − 1), an op at S is accepted.
- [ ] **AC-3 Event.** Each state change raises exactly one `DeliveriesState{phase, open, closeTick, deadlineTick, unlockedMask}` (Replicated, reliable-ordered, raised by the stub, not by Mail). No event is raised on ticks without a change. `deadlineTick` is set at Delivery start (the HUD timer reads it through `RunMirror`, story 010).
- [ ] **AC-4 Determinism.** The stub's state (phase, `phaseEndTick`, open, `closeTick`, mask) is in the dump. Two identical runs produce identical dumps, and the stub reads no clock.

---

## Implementation Notes

*Derived from ADR-0017 §3, ADR-0003 §1/§5:*

- **Files:** `src/PerformativeMail.Sim/Run/ShiftStub.cs` (`[SimSystem(SystemId.Run)]`, binds phase 12 `RunStructure`, receives `IMailApi` and `IRunControl` by constructor), `src/PerformativeMail.Sim/Run/ShiftStubSettings.cs`, `src/PerformativeMail.Sim/Run/Events/DeliveriesState.cs`. Mark every file `// M-1a STUB — replaced by Run Structure at M1 (no GDD)` (EPIC risk 4: keep it minimal so it is thrown away cleanly).
- **Settings:** `ShiftStubSettings { int prepTicks; int deliverySeconds; byte shift = 1; ulong unlockedMask = 1; int playerScalePm = 1000; }`. It is supplied by the composition profile (greybox config), not content: Run Structure has no GDD and no balance keys yet. Default `deliverySeconds` is 240 (F2's shift-1 examples); flag it PLACEHOLDER. Ticks are derived from `TickRate` (`production/epics/simulation-core/story-006-tickrate-f1-time-conversions.md`).
- **Phase-12 logic:** on the transition tick, compute the next state and call `IMailApi.SetDeliveriesState(open, closeTick, unlockedMask)` so it is effective from the next tick's phase 0. Raise `DeliveriesState` once. After Delivery ends, stay Closed (no Payday or Results at M-1a). Do not call `RequestStop`; the greybox runs on.
- **Mail side (`IMailApi.SetDeliveriesState`):** store `open`, `closeTick`, `unlockedMask` in the deliveries state store read by story 002's `TryGetDestinationFacts` and story 007's facts. `TickGuard.AssertMutable()`; no event from Mail. Dump these in Mail's section.
- **Later stories hook in:** story 013 adds the `PrepareShift(SpawnProfile)` call in Prep (the stub then also supplies `playerCount`/`playerScalePm` and `V`). Story 006's team cap reads `playerScalePm` from the stored profile.
- **`DeliveriesState` payload:** `closeTick u32, deadlineTick u32, unlockedMask u64, phase u8, open u8` + explicit pad to 8-byte alignment. Class Replicated, audience all, reliable channel (ADR-0011). Its wire codec is story 015's.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Stories 011–014: the manifest (`PrepareShift`) and batch firing. Story 010: `RunMirror`/`DestinationMirror` (client side).
- The full Run Structure state machine (Generating, Raid, Payday, Draft, Results), `UnlockDistrict` progression, quotas and the meter: M1.
- M-R4-2d/e: throw (story 008) and feeder (M-1b).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/mail/ShiftStubDeliveriesStateTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005 (Mail system, `IMailApi`), Story 007 (a hand consume to observe the boundary); `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md`, `production/epics/simulation-core/story-012-world-lifecycle-guards-faults.md` (`IRunControl`)
- Unlocks: Story 010 (`RunMirror`), Story 013 (`PrepareShift` call), Story 014 (batches fire only while open); manual-delivery availability row "deliveries closed"
