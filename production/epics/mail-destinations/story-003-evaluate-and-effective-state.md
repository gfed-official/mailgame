# Story 003: `MailRules.Evaluate` (pure, 0-alloc) over facts structs, and the effective destination state with closing grace

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: In Progress
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day; ~200 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-17

## Context

**GDD**: `design/gdd/mail-destinations.md` (R4 destination state and closing grace; R5 acceptance)
**Requirement**: `TR-mail-005` (`Evaluate` pure, 0-alloc, same code compiled into the client), `TR-mail-013` (closing grace by action start tick, `closeGraceTicks` 90)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §4, §5 step 1); ADR-0016: ClientLayer, Previews and Presentation Timing (§3 shared pure functions over facts)
**ADR Decision Summary**: `MailRules.Evaluate(in DestinationFacts d, in UnitFacts u) → DeliverEval` is static, pure and 0-alloc. It checks in order: CLOSED → Reject `closed`; LOCKED → Reject `not_on_route`; non-mail → Reject `wont_fit`; kind not accepted (a bitmask on the type def) → Reject `wont_fit`; `u.Address == d.Address` → Accept; otherwise Misdeliver. That is ≤ 6 checks with no registry access. The effective state for a consume is OPEN, or within closing grace (`actionStartTick < closeTick && applyTick ≤ closeTick + closeGraceTicks`). The client calls the same function with facts built from its mirrors (ADR-0016 §3). No shared function takes a system object, mirror, `SimWorld` or stateful interface.
**ADR Version**: ADR-0017 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: `Evaluate` and `MailPay` are static, pure and 0-alloc over facts structs (ADR-0017, ADR-0016)
- Required: pure shared functions live in the Sim assembly and are called by both `SimWorld` and `ClientLayer` (ADR-0003 §6)
- Forbidden: a wallet, RNG, clock, registry or mirror parameter to `Evaluate`; a second copy of the rule anywhere (Presentation included)
- Guardrail: ≤ 6 predicate checks, 0 registry scans, 0 B (M-PERF-1a)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md` (M-R5-1, M-PERF-1a, M-R4-2 a/b/c/f/g at the facts level), scoped to this story:*

- [ ] **AC-1 (M-R5-1a)** Parametrised: CLOSED → Reject `closed`; LOCKED (deliveries open) → Reject `not_on_route`; deliveries closed and district locked → Reject `closed`; a tool or log → Reject `wont_fit`; a kind outside the type's `accepts` (fixture type) → Reject `wont_fit`; same address → Accept; any other address → Misdeliver.
- [ ] **AC-2 (M-R5-1b, c)** `Evaluate` takes no wallet or RNG. 1,000 repeated calls give identical results. A Misdeliver input still returns Misdeliver whatever the wallet (no wallet exists in its signature). With `requiresUnit` (fixture type), a unit-number mismatch → Misdeliver and a match → Accept.
- [ ] **AC-3 (M-PERF-1a)** Over 10,000 calls `Evaluate` allocates 0 B. An instrumented test build counts ≤ 6 predicate checks per call (`[Conditional]` counter, absent in Release).
- [ ] **AC-4 Effective state and grace (M-R4-2 facts level).** With deliveries closing at tick T (`closeTick = T`) and `closeGraceTicks = 90`: an action started at T − 1 and applied at T − 1 is OPEN; started at ≥ T → CLOSED; started before T and applied at T + 90 → OPEN, at T + 91 → CLOSED; with no start tick (feeder/NPC) at T → CLOSED; at Delivery-start tick S an action at S is OPEN.

---

## Implementation Notes

*Derived from ADR-0017 §4–§5 and ADR-0016 §3:*

- **Files:** `src/PerformativeMail.Sim/Mail/MailRules.cs` (`static class`), `DestinationFacts.cs`, `UnitFacts.cs`, `DeliverEval.cs` (`readonly struct { EvalResult Result; MailRejectReason Reason; }`; `EvalResult : byte { Reject, Accept, Misdeliver }`; `MailRejectReason : byte { None, Closed, NotOnRoute, WontFit }`), `DestState.cs` (`Open, Closed, Locked`), `DeliveriesStateRules.cs` (the effective-state helper).
- Shapes follow ADR-0017 Key Interfaces: `DestinationFacts { Id, Address, Type, State }`; `UnitFacts { Kind, Category, Address, SpawnValue, DeadlineShift, Flags }`. Accepted kinds come from the `DestinationTypeDef` `accepts` bitmask. To keep `Evaluate` registry-free, carry the bitmask (and `requiresUnit`) in `DestinationFacts` as extra readonly fields filled by the facts constructor. This is an additive field within ADR-0016 §3's "small facts struct" rule; note it in the completion notes.
- **Reject reasons are Mail's enum, not reply codes.** Delivery declares the wire reason codes for `deliver_matching` (ADR-0009 erratum 2026-10-16) and maps `MailRejectReason` to them. Throwing does the same for its landing.
- **Effective state helper (one function, used by host consume and client facts alike; ADR-0017 Risks "grace logic differs" mitigation):** `DestState Effective(bool districtUnlocked, bool openNow, uint closeTick, uint? actionStartTick, uint applyTick, int closeGraceTicks)`. It returns CLOSED when deliveries are not open (and not within grace), even for a locked district (M-R5-1: "CLOSED and LOCKED → closed"); LOCKED when open but the district is not unlocked; OPEN otherwise. Grace applies only when `actionStartTick` has a value (hand, throw). `closeGraceTicks` comes from the Mail balance record (`production/epics/content-data/story-004-balance-registry-simsettings.md`), not a literal.
- `requiresUnit`: compare `AddressId.Unit` only when the type requires it; otherwise compare district/street/number with the unit masked out. At M-1a no lot has a unit (0), so plain `Value` equality is equivalent. Keep the branch for the fixture test.
- Category (`ItemCategory`) and kind come from content (`production/epics/content-data/story-002-closed-content-enums.md`, `story-005-m1a-def-records.md`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 004: pay and penalty formulas. Story 007/008: consume paths that build host facts and call this. Story 009: the deliveries state store and shift stub. Story 010: client facts constructors from mirrors.
- M-R4-2 d/e (throw bounce and feeder at close): Story 008 / M-1b feeders.
- manual-delivery: M-INT-2 parity over 1,000 seeded pairs (TR-delivery-012).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/mail/EvaluateTests.cs` and `tests/unit/mail/DeliveriesStateRulesTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (facts fields); `production/epics/world-stub/story-004-addressid-lots-destination-ids.md` (`AddressId`); `production/epics/content-data/story-005-m1a-def-records.md`
- Unlocks: Story 007, Story 008, Story 010; manual-delivery preview (4b); throwing-catching arc colour (4c)
