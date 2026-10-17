# Story 001: `Throw.RH`, the F1 launch (`LaunchMath`) and the closed-form F2 ballistic (`Ballistic.PositionAt`)

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~350 lines prod + ~350 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Primitives story (1 of 3).** The three pure functions every other story in this epic calls: the host, the aim preview and every peer's flight timeline. There must never be a second implementation of any of them (ADR-0015, control manifest).

## Context

**GDD**: `design/gdd/throwing-catching.md` (T4 launch; T5 flight; F1 launch; F2 ballistic position; F3 range table as the test oracle; Edge Cases "Zero, max and out-of-range"; ContentValidator rules 9–11 are content-data story 012's)
**Requirement**: `TR-throwing-001` (host-authoritative closed-form integer ballistic, O(1) `p(k)`), `TR-throwing-004` (floor-based RH, `CeilDiv`/`FloorDiv`/`isqrt`, never truncating `/` on a signed numerator), `TR-throwing-002` (no RNG, no spread term; only the absence is tested here)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-F1-1 a/c/d/e (b is Later), TC-F2-1 a/b, TC-F3-1.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§2 `Throw.RH`, §3 F1/F2, Key Interfaces) — Accepted 2026-10-16 (primary); ADR-0006 (`Fix.FloorDiv`/`CeilDiv`/`ISqrt`, T2 cross-platform goldens) and ADR-0010 (`ThrowBalance` knobs) secondary — Accepted
**ADR Decision Summary**: `Throw.RH(n, d)` is `Fix.FloorDiv(2n + d, 2d)` for `d > 0` (round half toward +∞), deliberately **not** `Fix.MulDivRH` (half away from zero): the GDD golden `p(1).z = 19700 + RH(−13695/30) = 19244` needs it. F1 builds the launch velocity and origin from the client-quantised int16 ×3 direction, `speedPm = ISqrt(ThrowRangePermille × 1000)` and the class base speed; F2 evaluates `p(k)` in closed form at any age, so a late event, a rejoin catch-up and a preview frame are one O(1) call with identical integers.
**ADR Version**: ADR-0015 2026-10-16; ADR-0006 2026-10-16; ADR-0010 2026-10-16

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

- [ ] **TC-F1-1 (FX-T).** feet (10000, 0, 20000) facing −Z, right-handed, light class, `loftDeg` 6, dir (0, 3406, −16026) → `n` = 16 383, `v` = 14 000, vel (0, 2911, −13 695), origin (10 250, 1200, 19 700).
- [ ] **TC-F1-1 c.** The same launch left-handed → origin.x 9 750 (`side = −1`).
- [ ] **TC-F1-1 b (Later — vector asserted, non-gating).** `ThrowRangePermille` 1500 → `speedPm` 1224, `v` 17 136, vel (0, 3563, −16 763). The formula supports it now; Cannon Arm itself is Perks.
- [ ] **TC-F1-1 d.** `Throw.RH(−1, 2)` = 0, `Throw.RH(−3, 2)` = −1, `Fix.CeilDiv(−7, 2)` = −3, `Fix.FloorDiv(−7, 2)` = −4 (negative numerators round as floor, never truncate).
- [ ] **TC-F1-1 e (host bounds).** dir (0, 0, 15 999) and (0, 0, 16 769) → `bad_direction`; (0, 0, 16 000) and (0, 0, 16 768) pass. Elevation `FloorDiv(1000·dy, n)` 981 and −877 reject; 980 and −876 pass. dir (−32768, 32767, 32767) rejects with no overflow (components widened to `long` before squaring).
- [ ] **TC-F2-1.** For the F1 flight: p(1) = (10250, 1292, 19244), p(20) = (10250, 963, 10570), p(21) = (10250, 837, 10114); the k = 90 gravity drop term is 44 100.
- [ ] **TC-F2-1 b.** A direct `PositionAt(launch, 20)` is bit-identical to the age-20 endpoint a per-tick loop (k = 1…20) produces; `PositionAt` is the only way positions are computed (no accumulated state).
- [ ] **TC-F3-1 (±0.1 m, ±1 tick).** The integer Sim matches a float oracle written **in the test project only**: light at aim 30° (elevation 36°) = 20.55 m / 54 ticks; heavy at aim 45° = 4.4 m / 35 ticks.
- [ ] **Defence branch.** When `h = ISqrt(dx² + dz²) < 2048` (unreachable for a host-validated dir) the horizontal basis is the body yaw; a test forces it through an internal seam and asserts a finite, deterministic origin.
- [ ] **No RNG, one RH.** `Sim/Throwing/` references no RNG type and no `Fix.MulDivRH` outside the F7 label code (an architecture test greps the folder, ADR-0015 Risks); no `float`/`double` (analyzer clean); 10 000 calls allocate 0 B.

---

## Implementation Notes

*Derived from ADR-0015 §2–§3 and Key Interfaces:*

- Files in `src/PerformativeMail.Sim/Throwing/`: `Throw.cs` (`static long RH(long n, long d)` = `Fix.FloorDiv(2n + d, 2d)`; assert `d > 0` under `SIM_ASSERT`), `LaunchMath.cs` (`static bool Compute(in LaunchInput i, out Launch l, out bool originFromChest)`; `LaunchInput { Vec3Mm Feet; short Dx, Dy, Dz; sbyte Side; PlayerClass/baseSpeed; int ThrowRangePermille; ushort BodyYawBam; }`; `Launch { Vec3Mm Origin; Vec3Mm Vel; int G; }` exactly as ADR-0015 Key Interfaces), `Ballistic.cs` (`static Vec3Mm PositionAt(in Launch l, int k)`), `ThrowSettings.cs` (immutable view of the `ThrowBalance` record from `production/epics/content-data/story-008-contentset-builtincontent-m1a.md`: `baseSpeed[class]`, `g`, `maxFlightTicks`, `handHeightMm`, `fwdOffMm`, `sideOffMm`, rank table, …).
- F1 and F2 formulas are in the GDD (copy the GDD text into XML doc comments): `v = RH(baseSpeed × speedPm, 1000)`; each velocity component `RH(v·dx, n)` / `RH(v·dy, n)` / `RH(v·dz, n)`; origin offsets `RH(fwdOffMm·dx, h) + RH(side·sideOffMm·(−dz), h)` etc.; `px(k) = ox + RH(vx·k, 30)`, `py(k) = oy + RH(vy·k, 30) − RH(g·k², 1800)`. Both RH terms of `py` are separate `Throw.RH` calls (the golden depends on it).
- The chest → origin sweep (`originFromChest`) needs solids and the heightfield, so it is **not** here: `Compute` takes an `IChestOriginProbe` port (default `NoChestProbe`, always "clear") and story 003 supplies the real probe. Keep `originFromChest` in the signature so the ADR shape is frozen now.
- `speedPm = ISqrt(ThrowRangePermille × 1000)`: `ThrowRangePermille` is read by the **caller** from the thrower's StatSheet and passed in the input (the host reads it at apply tick; the preview reads the client sheet copy). `Compute` never touches `IStatSheet`.
- The loft and pitch clamp are **client** aim math (story 004 quantises the direction); the host only validates the integer dir against the bounds. `loftDeg` in `ThrowBalance` is stored in milli-degrees (content-data story 008); story 004 converts, this story never reads it.
- Cross-platform: `ISqrt` and the floor divisions are `Fix` (sim-core story 003); do not re-implement them.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002: the swept contact test, receiver ranks and winner selection. Story 003: the chest → origin probe and receiver gathering.
- Story 004: yaw/pitch → quantised dir. Story 010: calling `Compute` at the host apply tick.
- Cannon Arm (Perks), `thrownPm` snapshot semantics (story 009 stores it).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/LaunchBallisticTests.cs` (`[Trait("Id", "TC-F1-1")]`, `TC-F2-1`, `TC-F3-1`) — must exist and pass on net10.0 and net8.0; golden vectors kept as data so story 014 reuses them

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-003-numerics-toolkit.md` (`Fix.FloorDiv/CeilDiv/ISqrt`); `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`Vec3Mm`, `AabbMm`); `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (`ThrowBalance` rows)
- Unlocks: Stories 002, 003, 004, 010, 014
