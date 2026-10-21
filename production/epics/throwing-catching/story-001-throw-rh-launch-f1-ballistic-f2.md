# Story 001: `Throw.RH`, the F1 launch (`LaunchMath`) and the closed-form F2 ballistic (`Ballistic.PositionAt`)

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~350 lines prod + ~350 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-20

> **Primitives story (1 of 3).** The three pure functions every other story in this epic calls: the host, the aim preview and every peer's flight timeline. There must never be a second implementation of any of them (ADR-0015, control manifest).

## Context

**GDD**: `design/gdd/throwing-catching.md` (T4 launch; T5 flight; F1 launch; F2 ballistic position; F3 range table as the test oracle; Edge Cases "Zero, max and out-of-range"; ContentValidator rules 9–11 are content-data story 012's)
**Requirement**: `TR-throwing-001` (host-authoritative closed-form integer ballistic, O(1) `p(k)`), `TR-throwing-004` (floor-based RH, `CeilDiv`/`FloorDiv`/`isqrt`, never truncating `/` on a signed numerator), `TR-throwing-002` (no RNG, no spread term; only the absence is tested here)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-F1-1 a/c/d/e (b is Later), TC-F2-1 a/b, TC-F3-1.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§2 `Throw.RH`, §3 F1/F2, Key Interfaces) — Accepted 2026-10-16 (primary); ADR-0006 (`Fix.FloorDiv`/`CeilDiv`/`ISqrt`, T2 cross-platform goldens), ADR-0010 (`ThrowBalance` knobs) and ADR-0005 (no Throwing RNG stream, §2 — governs `TR-throwing-002`) secondary — Accepted
**ADR Decision Summary**: `Throw.RH(n, d)` is `Fix.FloorDiv(2n + d, 2d)` for `d > 0` (round half toward +∞), deliberately **not** `Fix.MulDivRH` (half away from zero): the GDD golden `p(1).z = 19700 + RH(−13695/30) = 19244` needs it. F1 builds the launch velocity and origin from the client-quantised int16 ×3 direction, `speedPm = ISqrt(ThrowRangePermille × 1000)` and the class base speed; F2 evaluates `p(k)` in closed form at any age, so a late event, a rejoin catch-up and a preview frame are one O(1) call with identical integers.
**ADR Version**: ADR-0015 2026-10-16; ADR-0006 2026-10-16; ADR-0010 2026-10-16; ADR-0005 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None — no engine surface (ADR-0015 Engine Compatibility: Knowledge Risk LOW, no post-cutoff APIs). Goldens must pass on the `net10.0` and `net8.0` test legs (T2).

**Control Manifest Rules (Feature layer)**:
- Required: host, preview and every peer call the same `LaunchMath`, `Ballistic` and `FlightSweep`; Throwing's RH is `Throw.RH`; never apply C#'s truncating `/` to a signed numerator (ADR-0015 §2)
- Required: all quantities in ADR-0006 units (mm, mm/s, ticks, permille); products widened to `long` before the first multiply
- Forbidden: floats, `System.Random`, statics holding state; a spread term or any RNG stream in Throwing (ADR-0005 §2, ADR-0006 §1 — throw spread in mrad is retired)
- Guardrail: 0 B allocated per call; F2 stays ≤ 3.2 × 10⁸ at the knob extremes (fits `int`)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` TC-F1-1, TC-F2-1, TC-F3-1 and the Edge Cases, scoped to this story:*

- [x] **TC-F1-1 (FX-T).** feet (10000, 0, 20000) facing −Z, right-handed, light class, `loftDeg` 6, dir (0, 3406, −16026) → `n` = 16 383, `v` = 14 000, vel (0, 2911, −13 695), origin (10 250, 1200, 19 700).
- [x] **TC-F1-1 c.** The same launch left-handed → origin.x 9 750 (`side = −1`).
- [x] **TC-F1-1 b (Later feature — vector asserted, gating).** `ThrowRangePermille` 1500 → `speedPm` 1224, `v` 17 136, vel (0, 3563, −16 763). The formula supports it now, so the test runs and must pass; only Cannon Arm itself (the perk that sets 1500) is Perks.
- [x] **TC-F1-1 d.** `Throw.RH(−1, 2)` = 0, `Throw.RH(−3, 2)` = −1, `Fix.CeilDiv(−7, 2)` = −3, `Fix.FloorDiv(−7, 2)` = −4 (negative numerators round as floor, never truncate).
- [x] **TC-F1-1 e (host bounds).** dir (0, 0, 15 999) and (0, 0, 16 769) → `bad_direction`; (0, 0, 16 000) and (0, 0, 16 768) pass. Elevation `FloorDiv(1000·dy, n)` 981 and −877 reject; 980 and −876 pass. dir (−32768, 32767, 32767) rejects with no overflow (components widened to `long` before squaring).
  - *How it is tested:* `bad_direction` = `LaunchMath.Compute` returns `false`; pass = returns `true`. Elevation vectors (all `n` = 16 384, so only elevation decides): (0, 16 073, −3178) → 981 reject; (0, 16 057, −3258) → 980 pass; (0, −14 352, −7903) → −876 pass; (0, −14 368, −7874) → −877 reject. The no-overflow half is observed through the `internal` seam `LaunchMath.DirLengthSquared(short, short, short)` = 3 221 094 402 for (−32768, 32767, 32767) (`n` = 56 754; int32 squaring would wrap negative), plus `Compute` returning `false`.
- [x] **TC-F2-1.** For the F1 flight: p(1) = (10250, 1292, 19244), p(20) = (10250, 963, 10570), p(21) = (10250, 837, 10114); the k = 90 gravity drop term is 44 100.
- [x] **TC-F2-1 b.** A direct `PositionAt(launch, 20)` is bit-identical to the age-20 endpoint a per-tick loop (k = 1…20) produces; `PositionAt` is the only way positions are computed (no accumulated state).
  - *How it is tested:* the reference is a **test-side segment integrator** that does not call `PositionAt`: it accumulates exact integer numerators tick by tick (`sx += vx`, `sz += vz`, `sy += vy`, `sg += g·(2k − 1)`, so `sg = g·k²`) and applies the GDD's RH once per axis at each tick; its age-20 endpoint (and every k = 1…20) must equal `PositionAt(launch, k)`. "No accumulated state" is an architecture assertion (reflection): `Ballistic` and every type in `PerformativeMail.Sim.Throwing` declare no mutable static field and `Ballistic` no instance state, and a second evaluation of any `k` after evaluating others is bit-identical.
- [x] **TC-F3-1 (±0.1 m, ±1 tick).** The integer Sim matches a float oracle written **in the test project only**: light at aim 30° (elevation 36°) = 20.55 m / 54 ticks; heavy at aim 45° = 4.4 m / 35 ticks.
  - *How it is tested:* feet (10000, 0, 20000), right-handed, `ThrowRangePermille` 1000. dir integers = `round(16384·(0, sin e, −cos e))`: light e = 36° → (0, 9630, −13 255); heavy e = 51° → (0, 12 733, −10 311). The integer flight's landing segment is the first `k ≥ 1` with `py(k) ≤ feet.y`; within segment `p(k−1) → p(k)` the landing point is linearly interpolated to `y = feet.y` (test-side, in `double`). **Range** = horizontal distance from the launch origin to that point (F3's `R = vh·t_land` is measured from the launch point); **landing tick** = the interpolated flight time `(k − 1) + fraction` rounded to the nearest tick. Both are asserted against the golden (20.55 m / 54; 4.4 m / 35) **and** against the F3 float oracle (`R = vh·t_land`, `t_land = (vy + √(vy² + 2·g·handHeight))/g`, ×30 for ticks) within ±0.1 m and ±1 tick. Expected integers: light lands in segment k = 55 at t ≈ 54.43, R ≈ 20.550 m; heavy in k = 35 at t ≈ 34.87, R ≈ 4.388 m.
- [x] **Defence branch.** When `h = ISqrt(dx² + dz²) < 2048` (unreachable for a host-validated dir) the horizontal basis is the body yaw; a test forces it through an internal seam and asserts a finite, deterministic origin.
  - *How it is tested:* the seam is `internal static Vec3Mm LaunchMath.OriginFor(in Vec3Mm feet, short dx, short dz, sbyte side, ushort bodyYawBam, ThrowSettings s)` (the one origin routine `Compute` calls; it applies no host bounds). With `h < 2048` the basis is the ADR-0013 body forward `(−sin, −cos)` in Q15 (`TrigQ15`, `h` = 32 767) in place of `(dx, dz)`. At feet (10000, 0, 20000), right-handed, default offsets: dir (0, 0) yaw 0 (faces −Z) → origin **(10 250, 1200, 19 700)**; dir (0, 0) yaw 16 384 (faces −X) → **(9700, 1200, 19 750)**; dir (0, +2047) yaw 0 (h 2047, yaw basis) → (10 250, 1200, 19 700); dir (0, +2048) yaw 0 (h 2048, dir basis, faces +Z) → (9750, 1200, 20 300). Repeated calls return identical integers ("finite" = these exact values).
- [x] **No RNG, one RH.** `Sim/Throwing/` references no RNG type and no `Fix.MulDivRH` outside the F7 label code (an architecture test greps the folder, ADR-0015 Risks); no `float`/`double` (analyzer clean); 10 000 calls allocate 0 B.

---

## Implementation Notes

*Derived from ADR-0015 §2–§3 and Key Interfaces:*

- Files in `src/PerformativeMail.Sim/Throwing/`: `Throw.cs` (`static long RH(long n, long d)` = `Fix.FloorDiv(2n + d, 2d)`; assert `d > 0` under `SIM_ASSERT`), `LaunchMath.cs` (`static bool Compute(in LaunchInput i, out Launch l, out bool originFromChest)`; `LaunchInput { Vec3Mm Feet; short Dx, Dy, Dz; sbyte Side; WeightClass Class; int ThrowRangePermille; ushort BodyYawBam; }` (`WeightClass` from `src/PerformativeMail.Sim/Content/Enums/WeightClass.cs`; there is no `PlayerClass`); `Launch { Vec3Mm Origin; Vec3Mm Vel; int G; }` exactly as ADR-0015 Key Interfaces), `Ballistic.cs` (`static Vec3Mm PositionAt(in Launch l, int k)`), `ThrowSettings.cs` (immutable view of the `ThrowBalance` record from `production/epics/content-data/story-008-contentset-builtincontent-m1a.md`: `baseSpeed[class]`, `g`, `maxFlightTicks`, `handHeightMm`, `fwdOffMm`, `sideOffMm`, rank table, …). `ThrowSettings.BaseSpeed(WeightClass)` maps `Light → ThrowBalance.ThrowSpeedLight`, `Medium → ThrowSpeedMedium`, `Heavy → ThrowSpeedHeavy` (GDD defaults 14 000 / 10 000 / 6000 mm/s).
- F1 and F2 formulas are in the GDD (copy the GDD text into XML doc comments): `v = RH(baseSpeed × speedPm, 1000)`; each velocity component `RH(v·dx, n)` / `RH(v·dy, n)` / `RH(v·dz, n)`; origin offsets `RH(fwdOffMm·dx, h) + RH(side·sideOffMm·(−dz), h)` etc.; `px(k) = ox + RH(vx·k, 30)`, `py(k) = oy + RH(vy·k, 30) − RH(g·k², 1800)`. Both RH terms of `py` are separate `Throw.RH` calls (the golden depends on it).
- The chest → origin sweep (`originFromChest`) needs solids and the heightfield, so it is **not** here: `Compute` takes an `IChestOriginProbe` port (default `NoChestProbe`, always "clear") and story 003 supplies the real probe. Keep `originFromChest` in the signature so the ADR shape is frozen now.
- `speedPm = ISqrt(ThrowRangePermille × 1000)`: `ThrowRangePermille` is read by the **caller** from the thrower's StatSheet and passed in the input (the host reads it at apply tick; the preview reads the client sheet copy). `Compute` never touches `IStatSheet`.
- The loft and pitch clamp are **client** aim math (story 004 quantises the direction); the host only validates the integer dir against the bounds. `loftDeg` in `ThrowBalance` is stored in milli-degrees (content-data story 008); story 004 converts, this story never reads it.
- Cross-platform: `ISqrt` and the floor divisions are `Fix` (sim-core story 003); do not re-implement them.
- **`Throw.RH` sink (Mail Lead, decision log 2026-10-17).** `Throw.RH(long n, long d)` keeps ADR-0015's sink-free Key Interface and passes a local `default` `SimCounterSink` to `Fix.FloorDiv`. For `d > 0` the divisor `2d` is never 0 or −1, so no counter can fire on valid input; the only loss is a silent `fixDivZero` drop if `d ≤ 0` reached a Release build (a soft deviation from ADR-0006 §2 / manifest "clamps counted via an injected SimCounterSink", the TD-091 pattern). `d > 0` is asserted under `SIM_ASSERT` with a new append-only `SimAssertCode` constant. Every call site in this story passes a constant or `n`/`h`/`1000`/`30`/`1800` divisor that is positive after host bounds.
- **ADR shape and dependencies.** `Compute` keeps the exact ADR-0015 signature; the `ThrowSettings` view and the `IChestOriginProbe` port travel in `LaunchInput` (the ADR's field list plus these two references), so no extra parameter is added.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002: the swept contact test, receiver ranks and winner selection. Story 003: the chest → origin probe and receiver gathering.
- Story 004: yaw/pitch → quantised dir. Story 010: calling `Compute` at the host apply tick.
- Cannon Arm (Perks), `thrownPm` snapshot semantics (story 009 stores it).

---

## QA Test Cases

QL-STORY-READY (qa-lead, full mode, 2026-10-17 readiness run): **GAPS → resolved in the ACs above** (AC 3 made gating; AC 5 `bad_direction` = `Compute` returns `false` with exact elevation vectors and the `DirLengthSquared` seam; AC 7 test-side segment integrator + reflection assertion; AC 8 landing-segment interpolation and dir integers; AC 9 `OriginFor` seam with exact origins). Additional cases from the same review, tested in `LaunchBallisticTests` (they do not add ACs):
- `PositionAt(l, 0) == l.Origin`.
- `k` contract: `PositionAt` is defined for `0 ≤ k ≤ 150` (the `maxFlightTicks` validator ceiling); outside that range it asserts under `SIM_ASSERT` and Release evaluates the same formula.
- Knob-extreme golden: the F1 launch with `g` 14 000 at k = 150 → gravity term 175 000 (`g·k²` = 3.15 × 10⁸ fits `int`), p(150) = (10 250, −159 245, −48 775).
- Medium class at the F1 dir → `v` 10 000, vel (0, 2079, −9782).
- Architecture test: `tests/unit/architecture/ThrowingRulesTests.cs` (no RNG type, no `Fix.MulDivRH`, no `float`/`double` in `src/PerformativeMail.Sim/Throwing/`; no mutable statics).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/LaunchBallisticTests.cs` (`[Trait("Id", "TC-F1-1")]`, `TC-F2-1`, `TC-F3-1`) — must exist and pass on net10.0 and net8.0; golden vectors kept as data so story 014 reuses them
- Architecture: `tests/unit/architecture/ThrowingRulesTests.cs` (No RNG, one RH; no float/double; no mutable statics)

**Status**: [x] Created — `tests/unit/throwing/LaunchBallisticTests.cs` + `tests/unit/throwing/ThrowGoldenVectors.cs` (golden data) + `tests/unit/architecture/ThrowingRulesTests.cs`; 47 tests, pass on net10.0 and net8.0, Debug and Release, none skipped

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-003-numerics-toolkit.md` (`Fix.FloorDiv/CeilDiv/ISqrt`); `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`Vec3Mm`, `AabbMm`); `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (`ThrowBalance` rows)
- Unlocks: Stories 002, 003, 004, 010, 014

---

## Completion Notes
**Completed**: 2026-10-20
**Criteria**: 10/10 passing (all automated; none deferred). AC → tests (`LaunchBallisticTests` unless noted; `[Trait("Id", …)]`):
- TC-F1-1 (FX-T) — `Compute_FxTLightRightHanded_GivesTheGoldenLengthSpeedVelocityAndOrigin` (TC-F1-1)
- TC-F1-1 c — `Compute_FxTLeftHanded_MovesTheOriginToX9750` (TC-F1-1)
- TC-F1-1 b (gating) — `Compute_CannonArmPermille1500_GivesSpeedPm1224V17136AndTheGoldenVelocity` (TC-F1-1)
- TC-F1-1 d — `ThrowRH_NegativeAndHalfNumerators_RoundHalfTowardPositiveInfinity`, `FixCeilAndFloorDiv_NegativeNumerator_RoundAsCeilingAndFloor_NeverTruncate` (TC-F1-1)
- TC-F1-1 e — `Compute_HostBounds_AcceptOnlyLength16000To16768AndElevationMinus876To980`, `ElevationVectors_HaveLength16384_AndTheStatedFloorElevation`, `DirLengthSquared_Int16Extremes_WidensBeforeSquaring_NoOverflow` (TC-F1-1)
- TC-F2-1 — `PositionAt_FxTFlight_MatchesTheGoldenP1P20P21`, `PositionAt_K90_GravityDropTermIs44100` (TC-F2-1)
- TC-F2-1 b — `PositionAt_EveryAge1To20_EqualsTheTestSideExactNumeratorSegmentIntegrator`, `PositionAt_SecondEvaluationAfterOtherAges_IsBitIdentical` (TC-F2-1); `ThrowingRulesTests.ThrowingTypes_DeclareNoMutableStatic`, `BallisticLaunchMathAndThrow_AreStaticClassesWithNoFieldOtherThanConstants` (TC-F2-1)
- TC-F3-1 — `IntegerFlight_RangeAndLandingTick_MatchTheGoldenAndTheFloatOracle` (light 36°, heavy 51°; oracle from elevation degrees, test project only) (TC-F3-1)
- Defence branch — `OriginFor_HorizontalLengthBelow2048_UsesTheBodyYawBasis_Deterministically`, `OriginFor_AtTheFxTDir_IsTheOriginComputeReturns` (TC-F1-1)
- No RNG, one RH, 0 B — `ThrowingRulesTests.ThrowingSource_NamesNoRngTypeMulDivRhOrFloatingPoint`, `ThrowingIl_ReferencesNoRngOrFloatingPointType` (TR-throwing-002), `ThrowingSource_HasNoDivisionOrRemainderOperator_SoEveryRoundingIsThrowRH`, `ThrowingIl_CallsNoFixHelperOtherThanFloorDivAndISqrt` (TR-throwing-004); `ComputePositionAtAndRH_TenThousandCalls_AllocateZeroBytes` (TC-F1-1, runs in Debug and Release); float/double also blocked by PMSIM001 (build clean)
**Deviations** (advisory, logged as TD-107):
- Namespace `PerformativeMail.Sim.ThrowingCatching` (folder `Sim/Throwing/`), not ADR-0015's `PerformativeMail.Sim.Throwing`: a `Sim.Throwing` namespace shadows `Content.Rules.Throwing` (`RuleRegistry.cs:70`, CS0234) — same fix as TD-074. Type and member shapes are exactly ADR-0015's.
- `Throw.RH` (and the elevation `FloorDiv` in `LaunchMath.Compute`) pass a local `default` SimCounterSink (Mail Lead settled 2026-10-17); `d > 0` asserted under SIM_ASSERT (`SimAssertCode.ThrowRhDivisorNotPositive` = 63).
- Decisions: `LaunchInput` carries `ThrowSettings` + `IChestOriginProbe` (exact ADR `Compute` signature kept; null probe = `NoChestProbe`); `Compute` also returns false for Bulk/invalid `WeightClass`, `ThrowRangePermille <= 0`, `Side` ∉ {+1, −1} (client-supplied; review finding) and missing settings; `PositionAt` asserts `0 ≤ k ≤ 150` (`SimAssertCode.BallisticAgeOutOfRange` = 64, ceiling = `ThrowingChecks.FlightTicksMax`), Release evaluates the formula; `ChestHeightMm` = 1000 is a const (GDD "feet + 1000", no balance key); the rank table is not in `ThrowSettings` (story 002 adds it).
**Test Evidence**: Logic — `tests/unit/throwing/LaunchBallisticTests.cs`, `tests/unit/throwing/ThrowGoldenVectors.cs`, `tests/unit/architecture/ThrowingRulesTests.cs`. Four legs (net10.0/net8.0 × Debug/Release) 4 685 tests, 0 failed, 0 warnings; skips 42 (Debug) / 2 (Release), all pre-existing — the 47 new tests skip in no config. tools/ci guards all pass.
**Run result**: N/A — pure Godot-free Sim functions with no scene, node or HUD consumer yet; the first observable surface is the aim preview (story 004).
**Code Review**: Complete — /code-review APPROVED WITH SUGGESTIONS (fixes applied: independent float oracle, `Side` validation with a watched-fail test, namespace prefix filter, order-insensitive field check, per-method trait ids); QL-TEST-COVERAGE ADEQUATE; LP-CODE-REVIEW APPROVE.

