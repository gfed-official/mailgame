# Story 028: The host leaky-bucket throw cooldown (`TAT`) — check 7 `throw_cooldown` and the `TAT` commit

> **Epic**: Throwing & Catching
> **Status**: Deferred (Evan 2026-10-23 8:54 AM PT)
> **Deferred to**: M0 (out of the M-1a slice; split from story 009)
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~120 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **DEFERRED 2026-10-23, not deleted. Approved by Evan 2026-10-23 8:54 AM PT, Mail Lead chat: "Approve the ADR-0024 additions and the extra deferrals (md-008, tc-025, md-010 localisation to md-012, and the split-off hardening stories)".** Earlier context (not the approval of this story): Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". 8:10 AM PT, approving errata E1–E6 and E9 (decision log 2026-10-23): "approve all". This story is freed by **E5 (ADR-0015 §6: the leaky-bucket cooldown (anti-spam) moves to M0; the validation chain stays)**. Story 009 keeps the ordered check chain and claim-and-corroborate origin; check 7 keeps its slot and its `throw_cooldown` reason code there but accepts unconditionally at M-1a. The ACs below moved verbatim from story 009. Re-run `/story-readiness` when M0 planning starts (ADR-0021, the owner of `TR-throwing-011`, is planned for M0 and adds the team cap ≤ 48 flights/s).

## Context

**GDD**: `design/gdd/throwing-catching.md` (T1 "Cooldown (leaky bucket)"; Edge Cases "If a hitch bursts releases"; AC TC-T1-2 a)
**Requirement**: `TR-throwing-011` (leaky-bucket cooldown — per-player half; the team cap ≤ 48 flights/s is ADR-0021's, M0)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance id: TC-T1-2 a.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§6 leaky bucket; §1 check 7) — Accepted 2026-10-16 (primary); ADR-0007 (validate-then-mutate, `Reply`) secondary — Accepted
**ADR Decision Summary**: Per slot `TAT` (absolute tick). Accept at `T` iff `T ≥ TAT − throwBurstTicks`; then `TAT ← max(TAT, T) + throwCooldownTicks`. A rejected release does not move `TAT`. Sustained ≤ 6 throws/s per player; ≤ 2 back-to-back after a hitch. Check 7 of the chain (`T < TAT − throwBurstTicks` → `throw_cooldown`) is the bucket's read; `flight_cap` (check 10) is counted and unreachable under the bucket.
**ADR Version**: ADR-0015 2026-10-16; ADR-0007 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Forbidden: a rejected release moving `TAT`
- Guardrail: `Validate` O(1), 0 B

---

## Acceptance Criteria

*Moved verbatim from story 009, plus story 010's "Cooldown commit" AC (the `TAT` commit end to end):*

- [ ] **TC-T1-2 (bucket) (from story 009).** With `TAT` 0, releases applied at ticks 100, 100 and 104: the first two validate, the third gets `throw_cooldown` (entry unchanged, `TAT` unchanged by the rejection); a release at 105 validates. A hitch burst of three in one tick: two pass, the third fails; a rejected release never moves `TAT`.
- [ ] **Check 7 precedence (from story 009 TC-T4-1).** A request violating check 7 **and** check 8 gets `throw_cooldown` (the N = 7 case of story 009's parametrised precedence test, re-enabled here); a request violating only 8 still gets `bad_direction`.
- [ ] **`ThrowCooldown` (from story 009 Implementation Notes).** `ThrowCooldown.cs` (`bool TryAccept(uint T, ref uint tat, int burst, int cooldown)`; `CommitTat` used by story 010) replaces story 009's always-accept stub; `throwCooldownTicks`/`throwBurstTicks` (5/5) come from `ThrowBalance`; per-slot `TAT` is in Throwing's dump section (story 008) and `ThrowPlan.tat'` carries the new value to the commit.
- [ ] **Cooldown commit (from story 010).** After a launch at `T` the bucket state is `TAT = max(TAT, T) + throwCooldownTicks`; TC-T1-2 (100, 100, 104 → launch, launch, `throw_cooldown`; 105 launches) passes end to end through the op ring with the real handler.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/ThrowCooldownTests.cs` (`[Trait("Id", "TC-T1-2")]`) plus the N = 7 row of `tests/unit/throwing/ThrowValidationTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 009 (the chain and the check-7 slot), Story 010 (`CommitTat` call site), Story 008 (per-slot `TAT` in the dump); Story 007's client cooldown hold mirrors this arithmetic on loopback
- Unlocks: —
