# Story 008: Race suite (no misdeliver for a MATCH-latched hold), closing-bell fuzz and the 10,000-seed soak

> **Epic**: Manual Delivery
> **Status**: Deferred (Mail Lead planning default, pending Evan, 2026-10-23)
> **Deferred to**: M0 (out of the M-1a slice; was Ready)
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~400 lines tests + harness glue)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **DEFERRED 2026-10-23 by Mail Lead, not deleted (planning default, pending Evan: this story is not named in his approvals; Mail Review PR #47 B2 is with him).** Context only, not an approval of this story: Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". Reason: a race suite, a closing-bell fuzz and a 10,000-seed soak are hardening (Evan 7:15); none is in an Accepted ADR's Validation Criteria (ADR-0006 l.443–449, ADR-0014 l.245–250, ADR-0017 l.319–325). Coverage note: ADR-0006 lists `TR-delivery-021` (race suite and 10,000-seed soak) as addressed (l.92, l.430), and `docs/architecture/tr-registry.yaml` marks it covered. That coverage is satisfied only once this story lands at M0; `/architecture-review` should mark it deferred. Re-run `/story-readiness` when M0 planning starts; the text below is unchanged from the last Ready/Blocked version.

## Context

**GDD**: `design/gdd/manual-delivery.md` (MD5 "a hold that started on a ✓ can never misdeliver"; Edge Cases: timing, bell fuzz; M-FX-1a, M-MD6-4)
**Requirement**: `TR-delivery-021` (race suite: no misdeliver for a MATCH-latched hold across ticks ±2 and RTTs; 10,000-seed soak)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (primary: the T1 harness and replay); ADR-0011 (loopback = remote; a delay transport for RTT); ADR-0016 (press judged on the press-tick branch)
**ADR Decision Summary**: Determinism is T1 per tick (identical dump hash given the same inputs and logs) and T2 across platforms. Tests run the real Sim with recorded inputs; seeded generators only (PCG32); the failing seed is printed.
**ADR Version**: ADR-0006 2026-10-16; ADR-0011 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Global)**:
- Required: integer-only, ordered, statics-free determinism; T1 per tick (ADR-0006)
- Forbidden: `System.Random`, wall clocks, sleeps in tests (use the stubbed transport delay in ticks)
- Guardrail: the 10,000-seed soak runs in the nightly/CI budget (mark it `[Trait("Category","Soak")]` if it exceeds the PR budget; it still blocks Done)

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md`, scoped to this story:*

- [ ] **AC-1 (M-FX-1a)** With the real Delivery handler, the real Grid Inventory, the host FIFO and a stubbed delaying transport: for holds latched MATCH, each scenario — (a) an own optimistic op snaps back, (b) the phase flips on the completion tick, (c) the selection changes, (d) a reply lands in the same tick, (e) a catch or pickup arrives mid-hold — injected at each tick offset −2…+2 around completion and each simulated RTT in {0, 60, 120, 250 ms}, plus a soak of seeds 1…10,000 (PCG32, the failing seed printed): no misdeliver `DeliveryOutcome` exists for any hold latched MATCH, whatever the resolution (delivered, `no_target`, `closed`, cancelled, `out_of_range`, `dead`, `frozen`, timeout).
- [ ] **AC-2 (M-MD6-4)** With close at tick T, true hold starts T−10…T+10 and `holdStartTick` estimate errors −3…+3 ticks, the verdict equals the true-tick verdict for every true start outside T−3…T+2.
- [ ] **AC-3 Determinism.** Re-running any failing or sampled seed reproduces identical per-tick dump hashes (T1), so a printed seed is a full repro.

---

## Implementation Notes

*Derived from ADR-0006 and the GDD race-suite text:*

- Reuse `production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md` (T1 harness) and `story-026-inmemory-recorder-replay.md` (replay) for the repro guarantee.
- RTT is simulated as a fixed tick delay in a `DelayTransport` between the loopback ends: 60 ms ≈ 2 ticks, 120 ms ≈ 4, 250 ms ≈ 8. One delay value per run; no jitter unless the seed draws it.
- Scenario (e) uses a test thrown item / pickup injected through the ops the grid-inventory and throwing stubs expose. If the throwing epic's flight is not built yet, inject the catch's inventory effect through `IInventoryApi.TryCatch` (grid-inventory story 014) from a test system in phase 4.
- The assertion reads every `DeliveryOutcome` with `result = misdeliver` and joins it to the latched branch logged by the test (the telemetry hook of story 009 is not required; log from the test harness).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 009: the production `latchedBranch` telemetry (M-FX-1b).
- Playtest fantasy tests (M-FX-2, M-FX-3: MANUAL).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/delivery/DeliverRaceSuiteTests.cs` and `tests/integration/delivery/DeliverSoakTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004, Story 005, Story 007; `production/epics/simulation-core/story-026-inmemory-recorder-replay.md`, `production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md`; `production/epics/grid-inventory/story-014-pickup-core-trycatch.md`
- Unlocks: epic Definition of Done (race suite + soak)
