# Story 013: Flight lifecycle — run-end `FlushAllAsTimeout`, disconnect continuity, solo pause and the "never a receiver of your own flight" rule

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: S (~0.5 day; ~250 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Flight resolution story (3 of 3).** How flights end when the world around them ends: the run finishes with parcels still in the air, the thrower leaves, the host pauses. R19's "exactly one terminal per flight" must hold through every teardown.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T11 "Run end"; T5 "Who is never a receiver"; Edge Cases "Lifecycle": thrower disconnects, run ends with flights in the air, solo host pauses, "A flight crosses the end of Delivery"; Edge Cases "If the thrower disconnects before their release is applied")
**Requirement**: `TR-throwing-013` (exactly one terminal state per flight; conservation through flush/disconnect), `TR-throwing-009` (`throwerId`, `thrownPm` and actor unchanged on the record), `TR-throwing-002` (no RNG anywhere in teardown)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T11-1 d, TC-E-6 a/b, TC-T5-1 b (thrower half), TC-X-1 (flush half, story 015).)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§5 "Run end", "Disconnects never cancel a flight") — Accepted 2026-10-16 (primary); ADR-0003 (Run is the orchestrator; phase 12 `RunStep`; `IRunControl`) and ADR-0009 (Flight ledger conservation) secondary — Accepted
**ADR Decision Summary**: Run Structure (the orchestrator) calls `IThrowingApi.FlushAllAsTimeout()` in phase 12 when Victory or Run Over latches: every live flight resolves at once as `TIMEOUT` in creation order, with no mailbox and no pay; a landing on the triggering tick was resolved normally first in phase 4. Disconnects never cancel a flight: `throwerId`, `thrownPm` and `actorId` stay on the record. Run end flushes via this call from phase 12 only.
**ADR Version**: ADR-0015 2026-10-16; ADR-0003 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: run end flushes via `IThrowingApi.FlushAllAsTimeout` from phase 12 only; Run Structure alone holds `IRunControl` (ADR-0015, ADR-0003 §5)
- Required: units leave Flight only through `IInventoryApi`/`IMailApi`; every flight ends in exactly one terminal
- Forbidden: a flush that pays or calls Mail; cancelling a flight on disconnect; wall-clock time in flight ageing
- Guardrail: 0 B

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-T11-1 d, TC-E-6, TC-T5-1 b), scoped to this story:*

- [ ] **TC-T11-1 d.** Run end (Victory or Run Over) with 3 flights airborne at ages 4, 30 and 61: `FlushAllAsTimeout` resolves all three at once as TIMEOUT **in creation order**; each drops onto the surface directly below its **current** position `p(age)` (GROUND or WATER; a mailbox volume below does not count); there is no `DeliveryOutcome`, no pay, no wallet or complaint call; `FlightResolved` for each precedes its WorldItem event; the live count and Grid's Flight ledger column are 0 afterwards.
- [ ] **Victory-tick landing.** A flight that lands in a mailbox on the tick that triggers Victory resolves normally in phase 4 (pays); only flights still in the air after phase 4 are flushed in phase 12. A flight at age 0 (launched this tick) flushes to the surface below its origin.
- [ ] **Flush never pays.** An OPEN-released flight whose box would be OPEN when Delivery's close and the run end coincide is still flushed as TIMEOUT with no pay (T11).
- [ ] **Phase guard.** `FlushAllAsTimeout` outside phase 12 is counted (`flushOutOfPhase`) and does nothing; a second call in the same tick is a no-op (no live flights).
- [ ] **TC-E-6 a (disconnect, FIFO).** Ops [`ThrowRelease`, `PlayerLeave`] in that order: the release launches; [`PlayerLeave`, `ThrowRelease`]: the release is rejected (`frozen` once Players marks a leaver Frozen at M0, else the Core `unknown_actor` pre-check at M-1a — assert whichever the join/leave handlers of `production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md` produce) and the entry stays in its slot / the frozen panel.
- [ ] **Flight continues after disconnect.** After the thrower's `PlayerLeave` mid-flight the flight keeps its `throwerId`, `thrownPm` and `actorId`, lands normally, and a mailbox Accept still pays with `actorId` = the departed thrower (Mail story 008 AC-3). Nobody rejoining within 3 s (Second Wind) ever receives their own flight: the thrower slot is excluded from every receiver list for that `FlightId`'s lifetime (asserted with a catcher stub keyed to the same slot).
- [ ] **TC-E-6 b (solo pause).** A 100-tick pause (the host does not tick) leaves every flight's age unchanged and the dump hash unchanged; resuming sends nothing and throws nothing; the age is `tick − launchTick` from the shared tick counter and no wall-clock source is read anywhere in `Sim/Throwing/` (analyzer clean).
- [ ] **TC-T5-1 b (thrower half).** Over the street fixture a thrower aiming straight up (the flight returns to their own position) never collides with their own flight: with the catcher path available (stub), the thrower is skipped by `receiverKey`/slot.
- [ ] **No RNG.** The 1 000-seed replay of story 014 covers teardown; here: the flush path references no RNG type (architecture test).

---

## Implementation Notes

*Derived from ADR-0015 §5:*

- Files in `src/PerformativeMail.Sim/Throwing/`: `ThrowingSystem.FlushAllAsTimeout()` (body; the interface member was declared in story 008), `FlushResolver.cs` (shares `TerminalResolver`'s WorldItem placement with story 011: contact = `p(min(age, maxFlightTicks))`, kind Timeout, surface via `SurfaceBelow`, `ClampInsideMap`, `TrySnapWalkable`).
- **Unowned seam (EPIC.md):** no story anywhere calls `FlushAllAsTimeout`. Run Structure has no epic yet, and `production/epics/mail-destinations/story-009-shift-stub-deliveries-state.md`'s `ShiftStub` (the only M-1a phase-12 system) does not latch Victory/Run Over. This story provides the API and a test caller (a stub Run system bound to phase 12). The Run/Victory latch story must call it and cite this story; flagged for whoever creates the run-structure epic.
- The phase guard uses the current `PhaseId` from the tick context (ADR-0003 `TickContext { Tick, Phase }`).
- Disconnect handling needs no code beyond not clearing flights on `PlayerLeave`; the work here is the tests and the explicit "no receiver for own flight" rule in the receiver builder (story 003's catcher list is filtered by thrower slot in story 023; keep an assertion hook).
- Pause: the shell's solo pause goes through `TickDriver.Pause()` (ADR-0002); no Throwing code is involved, the test just stops calling `Tick()`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Run Structure's Victory / Run Over latch and who calls the flush (no story yet). Story 015: the 8-player fuzz that includes disconnects and a flush.
- Rejoin join-state (flights contribute 0 B; M0, ADR-0021/0022). Knockdown/Second Wind state machine (Player Character M-1b).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/throwing/FlightLifecycleTests.cs` (`[Trait("Id", "TC-T11-1d")]`, `TC-E-6`, `TC-T5-1b`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 008, 010, 011, 012; `production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md`; `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md` (phase ids, `TickContext`)
- Unlocks: Story 015
