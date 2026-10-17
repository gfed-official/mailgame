# Story 003: Core primitives II — numerics toolkit (Cents, Fx16, Fix)

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 1 day; ≈ 500 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R27 units, R28 rounding/overflow; F7)
**Requirement**: `TR-sim-031` (integer-only numerics: mm, permille, ticks, `Cents`, `Fx16`), `TR-sim-034` (T2: these helpers are on the cross-platform bit-identical list)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay
**ADR Decision Summary**: Authoritative quantities are integers with one rounding toolkit: `Cents` (plain `readonly struct`, ±2^62, range-checked add/sub that assert in Debug and saturate + count in Release), `Fx16` (`long Raw`, 16 fractional bits, domain [−2^47, 2^47 − 65 536], `ToMm = Raw >> 16`), `Fix.MulDiv` (truncate), `Fix.MulDivRH` (half away from zero, overflow-free `UInt128` algorithm), `FloorDiv`/`CeilDiv`, `ISqrt`, and `Cents.MulPermille ≡ clamp(MulDivRH(c, pm, 1000), ±2^62)`. Helpers are pure, allocation-free and non-throwing; zero divisors and clamps are counted through an injected sink, never statics.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: `Int128`/`UInt128` are .NET 7+ BCL types available on `net8.0`; not Godot APIs. ADR-0006's MEDIUM risk (export runtime) does not apply to unit-tested helpers.

**Control Manifest Rules (Foundation)**:
- Required: every money multiply through `Cents.MulPermille`; `Cents` add/sub through range-checked helpers; ratios through `Fix.MulDiv` / `Fix.MulDivRH`; one division per formula over an `Int128` numerator; `Fx16.ToMm = (int)(Raw >> 16)`, `FromMm(mm) = (long)mm << 16`; zero divisor returns 0 + `fixDivZero`; clamps counted (`fixClamped`, `moneyClamped`, `fxClamped`) via an injected sink; the N-vector table in ADR-0006 §2 is asserted verbatim.
- Forbidden: `float`/`double`/`decimal`/`Half`; `Math.Abs(long)` on a value that can be `long.MinValue`; raw `+` on `Cents`; relying on `checked` for the ±2^62 bound; dividing `Fx16` by 65 536; `record struct` for `Cents`/`Fx16`; static mutable state.
- Guardrail: helpers allocate 0 B (they run on the tick path).

---

## Acceptance Criteria

*From GDD SC-F7-1 and ADR-0006 §2 N-vectors, scoped to this story:*

- [x] **AC-1 SC-F7-1.** `MulPermille(15, 1500) == 23` and `(−15, 1500) == −23`; `MulPermille(2^62, 10^6) == 2^62` counting `moneyClamped` +1; `MulDiv(7, 1, 2) == 3` and `(−7, 1, 2) == −3`; `MulDiv(5, 5, 0) == 0` counting `fixDivZero` +1; `MulDivRH(long.MinValue, long.MinValue, 1)` returns `2^63 − 1`, counts `fixClamped` +1 and throws nothing (Debug checked build included); `Fx16.ToMm(98 304) == 1` and `ToMm(−98 304) == −2`; `Cents(2^62 − 1) + Cents(2)` via `Cents.Add` throws `SimAssertException` when `Sim.AssertsEnabled`, and otherwise returns 2^62 counting `moneySaturated` +1.
- [x] **AC-2 N-vector table.** Every row of ADR-0006 §2's N-vector table is asserted verbatim as one Theory row (including `MulPermille(1, 500)/(−1, 500)/(1, 499) = 1/−1/0`, `MulPermille(336, 1100) = 370`, `MulPermille(−2^62, 1000) = −2^62` uncounted, `MulDivRH(7,1,2)/(−7,1,2) = 4/−4`, `MulDivRH(5,1,3)/(4,1,3)/(−5,1,−3) = 2/1/2`, the three remaining `long.MinValue` rows with `fixClamped` +1, `Fx16.ToMm(−1) = −1`), and passes identically in Debug and Release on net10.0 and net8.0.
- [x] **AC-3 Remaining helpers.** `Fix.FloorDiv`/`Fix.CeilDiv` match mathematical floor/ceiling for all sign combinations (Theory over ±7/±2, ±6/±3, 0/±5); `Fix.ISqrt` returns ⌊√n⌋ for 0, 1, 2, 3, 4, 2^32 − 1, 2^62, `ulong.MaxValue` and a 10^4-sample seeded sweep checked by `r*r ≤ n < (r+1)*(r+1)` in `UInt128`; `Fx16.FromMm(int.MinValue/int.MaxValue)` round-trips through `ToMm`; an out-of-domain `Fx16` result is clamped to [−2^47, 2^47 − 65 536] and counts `fxClamped`.

---

## Implementation Notes

*Derived from ADR-0006 §1–§3, Key Interfaces and Implementation Guidelines:*

- Files: `src/PerformativeMail.Sim/Primitives/Cents.cs`, `Fx16.cs`, `Fix.cs`, `SimCounterSink.cs`.
- **`MulDivRH` is normative** (ADR-0006 §2): `c == 0` → count `fixDivZero`, return 0; `neg = (a<0) ^ (b<0) ^ (c<0)`; magnitudes as `UInt128`, with `|long.MinValue|` computed as `(UInt128)(ulong)(-(v + 1)) + 1` — **never `Math.Abs`**; `n = ua * ub` (< 2^126); `q = n / d; r = n % d; if (r >= d - r) q++`; clamp `q` to the limit (2^63 − 1 or the typed domain) counting `fixClamped`; return `neg ? -(long)q : (long)q`. `MulDiv` is `trunc((Int128)a * b / c)` with the same zero-divisor and clamp handling.
- `Cents.MulPermille(c, pm)` ≡ `clamp(MulDivRH(c.Value, pm, 1000), ±2^62)` with the clamp counted `moneyClamped` (not `fixClamped`) — implement by calling the core with the typed limit. `Cents` is a plain `readonly struct` with a private ctor, `FromCents(long, ref SimCounterSink)` range-checked, `public const long Max = 1L << 62`, `Add`/`Sub` range-checked: `SimAssert.Fail` under `SIM_ASSERT`, otherwise saturate to ±2^62 and count `moneySaturated`. Negative balances inside the range are legal.
- **`SimCounterSink` shape (settled here, per ADR-0006 Key Interfaces note).** `Primitives/` may depend on nothing else in the Sim, so the sink cannot name Core's `SimCounters`. Recommended: `public ref struct SimCounterSink` wrapping a `Span<long>` of a fixed `PrimitiveCounter` block (`FixDivZero = 0, FixClamped, MoneyClamped, MoneySaturated, FxClamped`); Story 007's `SimCounters` reserves its first `CounterId` values for this block and hands out a sink over that slice. Tests construct a sink over a local `long[5]`. Signatures take `ref SimCounterSink` as in the ADR.
- Overflow classes (ADR-0006 §3): money/Fx16/mm are class 2 — explicit range check, never `checked`; everything else in the helpers stays in the default context so Debug's `checked` surfaces real bugs. Nothing here is class 1, so no `unchecked` blocks except where the UInt128 algorithm needs the `(ulong)(-(v+1))` form (that expression is in range and needs none).
- `Fix.ISqrt(ulong)`: bitwise/Newton integer method, no float; `FloorDiv`/`CeilDiv` on signed `long` (Throwing TR-throwing-004 will use them).
- "RH" in every GDD means `Fix.MulDivRH`. Throwing's floor-based RH (`FloorDiv(2ab + c, 2c)`) is ADR-0015's concern; do not add it here.
- Ratios (`int` permille) are plain `int`; no wrapper type is mandated. BAM angles and the Q15 table are Story 005.

---

## Out of Scope

- Story 005: BAM arithmetic, `TrigQ15`, `StableSort`.
- Story 007: the Core `SimCounters`/`CounterId` that back the sink.
- Story 004: the analyzer that enforces the float ban (this story must simply contain no float).
- `CanonicalHash64`, `WireNarrow`: built by their first consumer.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Primitives/NumericsTests.cs` (SC-F7-1 + N-vectors, `[Trait("Id", "SC-F7-1")]`) — must exist and pass in Debug and Release on net10.0 and net8.0

**Status**: [x] Created — tests/unit/sim/Primitives/NumericsTests.cs; suite 201/201 pass Debug+Release × net10.0+net8.0 (+ ReleaseAssert)

---

## Dependencies

- Depends on: Story 001 (asserts, checked Debug build, net8.0 leg)
- Unlocks: Story 005, Story 007, every money/geometry consumer (mail-destinations, player-character, throwing-catching, content-data StatSheet)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (none deferred); all 19 ADR-0006 §2 N-vector rows reproduced verbatim
**Deviations**:
- ADVISORY (decided, logged): a zero divisor returns 0 + `fixDivZero` and does **not** assert in Debug — ADR-0006 §2's "Faults" bullet says Debug asserts, but its own N-vector table (identical Debug/Release), GDD F7/R31, the manifest and AC-2 say no assert (TD-005 to amend the ADR).
- REVISED at review (LP-CODE-REVIEW CONCERNS, finding 1): `Cents.MulPermille` now applies GDD F7's pm rule — negative permille asserts (`SimAssertCode.MoneyBadPermille = 10`) under `SIM_ASSERT`, else returns 0 and counts the new `PrimitiveCounter.MoneyBadPermille = 5`. `SimCounterSink.RequiredLength` = 6. Story 007's notes updated to pin a 0–5 primitive block.
- ADVISORY (accepted): `Fix.ISqrt` returns `uint` (ADR Key Interfaces say `ulong`); `FloorDiv`/`CeilDiv` take `ref SimCounterSink` and clamp (`long.MinValue`, −1) to `long.MaxValue` counting `fixClamped`; `Fx16.ToMm` is static; extras `Fx16.FromRaw/Add/Sub`; `Fix.MulDivRHCore` internal (typed-limit wrapper); `Cents.FromCents` out of range asserts `MoneyRange` (8) / clamps + `moneyClamped`; `Cents.Add/Sub` assert `MoneyOverflow` (9) / saturate. Doc drift in ADR-0006 Key Interfaces tracked as TD-005.
**Hand-off to Story 007**: size the sink slice to `SimCounterSink.RequiredLength` (6); a short/default sink silently drops counts — add a test that `PrimitiveSink().Length >= RequiredLength`.
**Test Evidence**: Logic: `tests/unit/sim/Primitives/NumericsTests.cs` (`[Trait("Id","SC-F7-1")]` on headline/N-vector tests; seeded integer sweeps incl. GDD F7 closed form; 0-B allocation test)
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS → finding 1 revised, others accepted/logged; QL-TEST-COVERAGE ADEQUATE; godot-csharp-specialist APPROVED
