# Story 006: TickRate and F1 tick-time conversions

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S (≈ 2–3 hours; ≈ 150 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R1 the tick, R15 absolute deadlines; F1 Tick and time)
**Requirement**: `TR-sim-001` (whole fixed ticks of 1/30 s; `TickRateHz = 30` code constant; no variable step)
*(Requirement text: `docs/architecture/tr-registry.yaml`)*

**ADR Governing Implementation**: ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim (§1 `TickRate.Hz`) — primary; ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (integer-only time, ticks `uint`, differences `long`) — secondary
**ADR Decision Summary**: The tick rate is the compile-time constant `TickRate.Hz = 30` in `Hosting/TickRate.cs`, never data; variable-step simulation is forbidden. Ticks are `uint`; tick differences are computed in `long`; all time maths is integer (F1's exact formulas), never `seconds × 30` in float.
**ADR Version**: 2026-10-16 (ADR-0002), 2026-10-16 (ADR-0006)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: `TickRate.Hz = 30` compile-time constant in `Hosting/TickRate.cs`; ticks `uint`, differences `long`; integer-only maths.
- Forbidden: variable-step simulation; `float`/`double`; culture-dependent text in state (the clock helper returns integers; formatting is Presentation's).
- Guardrail: pure, allocation-free helpers.

---

## Acceptance Criteria

*From GDD SC-F1-1, scoped to this story:*

- [x] **AC-1 SC-F1-1.** `contentTicks(400, 150, 15 000, 0, 1, 100 ms) == 12, 5, 450, 0, 1, 3`; `ticksToMsFloor(1) == 33` and `ticksToMsCeil(1) == 34`; the clock at r = 360, 331, 330, 1, 0, 2 160 reads 0:12, 0:12, 0:11, 0:01, 0:00, 1:12; r = 200 000 reads 99:59 (capped); a deadline 10 ticks in the past reads 0:00 (r clamped at 0 via `long` subtraction, never `uint` wrap).

---

## Implementation Notes

*Derived from GDD F1 and ADR-0002 §1:*

- `src/PerformativeMail.Sim/Hosting/TickRate.cs`: `public static class TickRate { public const int Hz = 30; }` (ADR-0002 Key Interfaces).
- `src/PerformativeMail.Sim/Hosting/TickTime.cs` (pure static helpers, `long` arithmetic):
  - `ticksToMsFloor(t) = (t × 1000) / 30`; `ticksToMsCeil(t) = (t × 1000 + 29) / 30`;
  - `contentTicks(ms) = (ms × 30 + 999) / 1000` (ms ≥ 1 gives ≥ 1 tick; 0 gives 0);
  - `displaySeconds(r) = (r + 29) / 30` with `r = max(0, (long)deadlineTick − (long)currentTick)`;
  - clock = `min(displaySeconds, 5999) / 60 : % 60` — return a `(int Minutes, int Seconds)` value (or a small readonly struct); the HUD formats text. The Sim stores no strings.
  - Write the 30 as `TickRate.Hz` everywhere.
- Never convert ticks → ms → ticks (the round trip is not an identity: `contentTicks(ticksToMsCeil(1)) == 2`) — note it in XML docs.
- Range notes from F1: `t` ≤ 2^31 − 1, `ms` ≤ 86 400 000; `t × 1000` ≤ 2.1 × 10^12 fits `long`. Assert inputs in range under `SIM_ASSERT`.
- Content-side decimal-seconds parsing (`"0.4"` → 400 ms) is the content-data epic's `DecimalToScaled` reader; this story starts from integer ms.

---

## Out of Scope

- Story 010: `TickDriver` (F2 accumulator).
- Story 011: `CurrentTick` semantics and R15 absolute-deadline due checks (SC-R1-1).
- content-data: decimal parsing of seconds into ms.
- HUD clock text formatting: HUD (Presentation).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Hosting/TickTimeTests.cs` (`[Trait("Id", "SC-F1-1")]`) — must exist and pass

**Status**: [x] Created — tests/unit/sim/Hosting/TickTimeTests.cs

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 010, content-data (seconds → ticks), input-controls (F1 mapping)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 1/1 passing (AC-1 SC-F1-1 — every value asserted verbatim; past deadline via `long` subtraction incl. `uint.MaxValue` pairs)
**Deviations**: ADVISORY — (1) out-of-range `t`/`ms` assert under SIM_ASSERT (`SimAssertCode.TickRange = 11`, `MsRange = 12`, appended) and clamp silently in Release with no counter (ticks/ms are not in ADR-0006 §2's counted-clamp list; counting, if wanted, belongs at the call site — follow-up for story 007+/content-data); (2) `TickClock` is a `readonly record struct` display value (never stored, ordered or hashed — XML remark records the exception to the ids-only record-struct rule); (3) API names PascalCase (`TicksToMsFloor`, `ContentTicks`, `DisplaySeconds`, `Remaining`, `Clock`).
**Test Evidence**: Logic — tests/unit/sim/Hosting/TickTimeTests.cs (15 methods, `[Trait("Id","SC-F1-1")]`); suite 334: Release 334/334, Debug 333 + 1 skipped, net10.0 and net8.0; Release build 0 warnings.
**Run result**: N/A — Godot-free integer time helpers; nothing renders.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (revised: `DisplaySeconds` overflow near `long.MaxValue` fixed + tests; record-struct remark); QL-TEST-COVERAGE ADEQUATE.
