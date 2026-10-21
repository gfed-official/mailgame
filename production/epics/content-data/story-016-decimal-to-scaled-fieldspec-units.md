# Story 016: DecimalToScaled reader, FieldSpec units and ExactTickCheck

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S (~0.5–1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD3.4, CD4, F2, F3)
**Requirement**: `TR-content-005`, `TR-content-006`
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§2 Numbers) (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (§1 units)
**ADR Decision Summary**:
- Numbers are read from raw token bytes by the hand-written DecimalToScaled reader, never `GetDouble`/`GetDecimal`.
- **Grammar:** the token must match `-?(0|[1-9][0-9]*)(\.[0-9]{1,3})?`. The reader accumulates in `long`, stops on overflow, and rejects exponents, `-0`, a leading `+`, a bare `.` and a fourth fractional digit.
- **Units decide the scale:**

  | Field suffix | Stored as |
  |---|---|
  | `…Seconds` | ms |
  | `…Metres` | mm |
  | `…Ratio`, and a Mul `value` | permille |
  | `…Points` | milli-points |
  | `…Deg` | milli-degrees |
  | `…Ms`, `…Mm`, `…Pm`, `…Ticks`, money | as written |

- A numeric field with no declared unit is a load error.

**ADR Version**: ADR-0010 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: numbers are read from raw token bytes by the `DecimalToScaled` reader, using the grammar and unit table above. A numeric field with no unit is a load error. (ADR-0010 §2)
- Forbidden: `GetDouble`, `GetDecimal`, `double.Parse` on the content path. (ADR-0010 Alt 1)
- Naming: content numeric fields carry a unit suffix (`…Seconds`, `…Metres`, `…Ratio`, `…Points`, `…Deg`, `…Ms`, `…Mm`, `…Pm`, `…Ticks`). (Global Naming Conventions)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] **CDD-UNIT-2.** `DecimalToScaled` on decimal fields:
  - converts `0.4` → 400, `1.5` → 1 500, `15` → 15 000, `0.05` → 50 and `-2.25` → −2 250;
  - rejects `0.1234` with CV-GEN-06;
  - rejects decimal `2147483.648` with CV-GEN-12, while `2147483.647` passes.

  On an integer field, `2147483647` passes and `2147483648` gives CV-GEN-12. For a duration field, `86400.001` s gives CV-GEN-07 and `86400` passes.
- [x] **CDD-PARSE-3, CV-GEN-06 half.** Each of these gives exactly one CV-GEN-06:
  - `1e3`, `-0`, `-0.00` and `0.1000` on a decimal field;
  - `1.0` on an integer field.
- [x] **CDD-UNIT-1.** Unit conversion maps:
  - `batchIntervalSeconds: 15` → 15 000 ms (450 ticks via Sim Core F1 `contentTicks`);
  - `catchRadiusMetres: 1.5` → 1 500;
  - `lateValueRatio: 0.5` → 500;
  - `complaintOnMisdeliveryPoints: 5` → 5 000;
  - `loftDeg: 6` → 6 000;
  - `price: 400` → 400 cents.

  Registering a numeric `FieldSpec` with no unit fails the spec table's self-check with CV-GEN-06.
- [x] **CDD-UNIT-3.** `ExactTickCheck` (F3):
  - 500 → valid (15 ticks) and 100 → valid (3);
  - 501, 50 and 150 → invalid (Sim Core F1 gives 5 ticks for 150);
  - property: for every ms in 0…86 400 000, F3 is true iff `ms % 100 == 0`.

---

## Implementation Notes

*Derived from ADR-0010 §2 and GDD F2/F3/CD4:*

- **Location:** `src/PerformativeMail.Sim/Content/Parsing/DecimalToScaled.cs`, `FieldUnit.cs` (enum: Seconds, Metres, Ratio, Points, Deg, Ms, Mm, Pm, Ticks, Cents, Count) and `FieldSpec.cs` (the row shape: name, unit, required/default, range, enum set or reference target).
- **`DecimalToScaled`:**
  - It works on `ReadOnlySpan<byte>`, which is the token's `ValueSpan`. It does no string allocation and no division.
  - It computes `sign × (intPart × scale + fracDigits × 10^(3 − fracLen))`, with scale 1 000 for decimal units and 1 for integer units.
  - It stops as soon as `intPart > fieldMax / scale`, then range-checks the assembled value against `[fieldMin, fieldMax]`.
  - It returns a status (Ok / Format → CV-GEN-06 / Range → CV-GEN-12 / Duration → CV-GEN-07) plus the value. The caller attaches position and rule.
- **What the JSON reader rejects first.** `5.` and `+5` are invalid JSON, so `Utf8JsonReader` rejects them first as CV-GEN-16 (story 017). This reader still rejects them defensively.
- **Duration cap:** 0…86 400 000 ms (Sim Core F1 cap, CV-GEN-07). It applies to `…Seconds` and `…Ms` duration fields.
- **Seconds → ticks.** Use Sim Core F1 `contentTicks(ms) = (ms × 30 + 999) / 1000` from `production/epics/simulation-core/story-006-tickrate-f1-time-conversions.md`. **Do not redefine it.** F3 is only for fields that must be whole ticks (CV-THR-12).
- **Money** fields (`baseValue`, `price`, `…Cents`) are integers stored as `long` cents.
- **Legacy names.** Names such as `grid`, `maxStack` and `baseValue` keep their names and declare their unit in the `FieldSpec` (CD4).

---

## Out of Scope

- Story 017: the token walk.
- Story 018: the per-def-type `FieldSpec` tables.
- Story 012: the CV-THR-12 rule. If this story lands second, it reuses F3 from here.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/DecimalToScaledTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/content/DecimalToScaledTests.cs (71 cases), passing on four legs

---

## Dependencies

- Depends on: Story 003 (rule ids). Also `production/epics/simulation-core/story-006-tickrate-f1-time-conversions.md` (F1 `contentTicks`).
- Unlocks: Story 018, Story 019

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 4/4 passing (none deferred)
**Deviations**: Advisory, accepted by LP — F3 reuses existing `Sim/Content/ExactTickCheck` and F1 `Hosting.TickTime.ContentTicks` (no duplicate); `FieldUnit : byte` has no `None` (0 = undeclared → CV-GEN-06); format error beats range; early stop `intPart*scale > limit` (no division); negative/over-cap duration → CV-GEN-07; `GeneralRuleIds` gained NumberFormat/DurationOverCap/ValueOutOfRange constants (Rules/General.cs, append-only). Remaining advisories (Convert CC ~13, duration-unit/min-max self-check gaps) → TD-090, to fix in/before story 018.
**Test Evidence**: Logic: tests/unit/content/DecimalToScaledTests.cs. Orchestrator re-verified net10 Release 4 205/4 205 and net8 Debug 0 failed (34 skipped, TD-008); implementer ran all four legs with 71/71 story tests green (one transient foreign Players failure, since cleared).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (no blocking; closed with tech debt TD-090); QL-TEST-COVERAGE ADEQUATE. C#-specialist folded into the LP gate.
