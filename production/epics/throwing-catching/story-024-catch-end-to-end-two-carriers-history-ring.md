# Story 024: Catch end to end — two simulated carriers over the real host position-history ring, the loopback host-catch rule and the catch race suite

> **Epic**: Throwing & Catching (M-1a slice) — **Sim-side catch pull-forward (optional for the M-1a gate)**
> **Status**: Blocked
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day once unblocked; ~300 lines of harness + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **BLOCKED: this story needs the M0 host position-history ring, and ADR-0020 is not written.** The F5 history (the 9+3-tick ring of reported positions, dead-reckoned sample, `Teleport` history clearing and `seq` discard, transform replication) is ADR-0020's, deferred to M0 by the Player Character epic with **no story** (`production/epics/player-character/EPIC.md` "Deferred out-of-milestone rules"; `production/epics/player-character/story-019-host-checkinteraction-position-ingest.md` implements only a newest-sample `IPositionHistory`). Without the ring, "the host tests the sphere at every position that catcher reported in the last 300 ms" (T7, TC-N-4) cannot be exercised against production code: stories 022 and 023 prove the algorithm against a multi-sample test double, but not against the real feed. Write ADR-0020 (`/architecture-decision`), accept it, add the Player Character ring story, then run `/story-readiness` on this story. Stories 001–023 do not depend on it.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T7 "Contact"; F5; Network "Moving receivers", "Join / rejoin", "Disconnect"; Edge Cases "If a catcher moves fast", "If the host itself throws or catches (loopback)", "If a player rejoins while flights are in the air"; Fantasy test 1 instrument `armedBeforeLaunch`)
**Requirement**: `TR-throwing-012` (catch arm window host-tick based; contact over the 9-tick history — the end-to-end proof), `TR-throwing-016` (rejoin: flights 0 B; the arm resets), `TR-throwing-010` (corroboration against the real history)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-N-4 a, TC-N-2 b, TC-F5-1 b/c against the real ring, TC-T7-1 c rejoin, TC-E-4 b.)*

**ADR Governing Implementation**: ADR-0020 (planned, **not written**): host position history, claim corroboration and dead-reckoning (the F5 ring). Secondary: ADR-0015 §7 (the Sim-side catch rules, Accepted 2026-10-16) and ADR-0014 §5 (`IPositionHistory` seam, Accepted)
**ADR Decision Summary**: — (pending ADR-0020). From ADR-0015 §7: contact is the minimum `f` over the catcher's history samples (9 ticks) within `catchHistoryMaxOffsetMm` of the newest; ADR-0014 §5 fixes the predicate and leaves the ring behind the `IPositionHistory` seam.
**ADR Version**: ADR-0020 unversioned (not written); ADR-0015 2026-10-16; ADR-0014 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW (Sim); re-assess when ADR-0020 exists
**Engine Notes**: Re-derive once ADR-0020 exists.

**Control Manifest Rules (Feature layer)**:
- Required: the host never steps an Alive remote carrier; positions arrive as samples latched at the tick boundary (ADR-0013, sim-core story 021); catch-position trust is unbounded by design but history spread is bounded by `catchHistoryMaxOffsetMm`
- Forbidden: rewinding a resolved flight; a catch request tied to the catcher *seeing* contact

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-N-4 a, TC-N-2 b, TC-F5-1 b/c, TC-T7-1 c, TC-E-4 b), scoped to the end-to-end catch once the ring exists. **Provisional until ADR-0020 is accepted:***

- [ ] **Two live carriers.** Two scripted carriers (thrower and catcher) report transform samples at 15 Hz through `SimWorld.TryWriteSample`; the catcher presses (CatchArm) before the lob arrives; the host catches it with the real ring: `CAUGHT`, units in the catcher's hotbar, the thrower's `FlightResolved` shows `Caught`. Replays bit for bit.
- [ ] **TC-N-4 a (loopback = remote rule).** The host's own catch uses the same 300 ms history rule as a remote player's: a host catcher whose reach would only be satisfied by a position older than 9 ticks does not catch.
- [ ] **Fast catcher.** A roof rider whose 300 ms history spans 4.2 m counts only samples within 1500 mm of the newest (about the last 3 ticks).
- [ ] **Late press / no rewind.** A `CatchArm{down}` that applies after the flight resolved leaves it resolved; the arm may catch a different flight within its 15 ticks.
- [ ] **TC-N-2 b / TC-T7-1 c (rejoin).** A mid-flight rejoin leaves the arm cleared and the rejoiner not catchable before their first report; flight state is not replayed to the rejoiner (0 B); the resulting WorldItem or consumption events apply.
- [ ] **Race suite.** Two catchers at the same `f` and eight catchers in range follow the rank/tie rules with real histories; the loser keeps the arm and no inventory state changes.
- [ ] **Telemetry hook (with story 016).** A catch records `armedBeforeLaunch` when the arm's `downApplyTick` is earlier than the flight's `launchTick`.

---

## Implementation Notes

- Wait for ADR-0020. The ring replaces story 019's newest-sample adapter behind `IPositionHistory`; the Throwing side is the `ICatcherPositionSource` adapter of story 023 — a one-class swap.
- Harness: the loopback session (`production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md`) with two `ClientLayer`s or scripted bots; the sample writer is Player Character story 020's `ITransformSampleSink`.

---

## Out of Scope

- The catch prompt and prediction (M0), bonk (M0), the wire protocol and channels (ADR-0021, M0), remote interpolation (ADR-0020 M0 body).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/throwing/CatchEndToEndTests.cs` (`[Trait("Id", "TC-N-4")]`) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-0020 accepted and a Player Character position-history-ring story (not created); Stories 022, 023; `production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md`; `production/epics/player-character/story-020-carrierstep-clientlayer-integration.md` (`ITransformSampleSink`)
- Unlocks: M0 catch gate (fantasy test 1)
