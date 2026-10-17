# Story 019: `settings.input` — integer-unit `InputSettings`, the versioned overrides-only codec for `user://input.json` and load-time clamping

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (R10 "Hold settings", "Other input settings", "Storage", "Validation at load"; R12 M-1a "hold / toggle modes and Hold time as config values (`user://input.json`, PROPOSED)"; Tuning Knobs; Edge Cases "Settings and data"; ACs H-R12-1 core half, H-F3-1 clamp row, H-F11 settings rows)
**Requirement**: `TR-input-020` (`settings.input` schema versioned, overrides-only; M-1a `user://input.json`)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§5 integer units, §9 "`settings.input` (R10, TR-input-020)") — Accepted 2026-10-16 (primary); ADR-0010 (hand-written `Utf8JsonReader` walk, no reflection binding) secondary
**ADR Decision Summary**: Versioned, overrides-only JSON with integer-unit fields; physical keys by stable name. M-1a: `user://input.json`, read as bytes by the adapter (`FileAccess`; ADR-0024 persistence at M1) and parsed in Client.Core with a hand-written `Utf8JsonReader` walk. Unknown ids are dropped and logged, malformed bindings fall back to default, ranges are clamped, migrations run vN → vN+1, a malformed file loads defaults with one warning. Settings are stored in integer units (permille, ms, counts, px); `holdSettingMs` is an integer multiple of 50.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0010)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW (codec); the `FileAccess` read is story 028
**Engine Notes**: `user://` read via `Godot.FileAccess.GetFileAsBytes` with `GetOpenError()` checked (manifest Presentation) — adapter / host side only.

**Control Manifest Rules (Foundation)**:
- Required: no `JsonSerializer`, `JsonDocument`, `GetDouble`, `GetDecimal`, `double.Parse` or reflection discovery; integer fields only; ordinal comparisons (manifest ADR-0010 forbidden list)
- Required: `holdSettingMs` is an integer multiple of 50, clamped 200–1000 (manifest Input router core)
- Forbidden: persisting `DefIndex` or Godot enum ints (physical keys by **name**)
- Guardrail: parse < 5 ms; the codec allocates at load only

---

## Acceptance Criteria

*From GDD R10, R12 and H-R12-1 / H-F3-1 / Edge Cases, scoped to this story (scalars and hold modes; the `bindings` map is recognised but not applied until M1):*

- [ ] **Schema and defaults.** `InputSettings` (readonly struct / immutable class, integer units) holds `Version`, `HoldSettingMs` (default 400, 200–1000, step 50), `ThrowMinAimMs` (250, 150–400), `PreviewSettleMs` (0, 0–250), `HoldMode` per class (Deliver, ThrowAim, Sprint-KBM = Hold, Sprint-Pad = Toggle; `Hold | Toggle | Auto` for Sprint), sensitivity (mouse, stick X, stick Y), invert (mouse X/Y, stick X/Y), rumble (0–100), deadzone (`DzInnerPm` 150, 50–400; `DzOuterPm` 950, 700–1000; `CurvePm` 1500, 1000–3000), `TriggerOnPm` (500, 150–900), repeat (`DelayMs` 300, 150–600; `IntervalMs` 80, 40–200), `AimSteadinessPm` (1000, 250–1000 — M1 setting, field present), `GlyphStyle` (Auto only at M-1a), `AutoPauseSolo` (true), `BackSkipMs` (2000, 1500–4000), `PendingCommitTimeoutMs` (500, 300–1000), `PadEdgeCollapseMs` (20, 10–50), `CatchArmSeconds` (3, M0). An untouched profile equals `InputSettings.Defaults`.
- [ ] **Parse and write.** `InputSettingsCodec.Parse(ReadOnlySpan<byte> utf8, ActionMap m, out SettingsLoadReport rep)` walks the JSON with `Utf8JsonReader` (integers only; a number with a fraction is malformed for that field); `Write(in InputSettings s, Span<byte> utf8)` emits **overrides only** (fields equal to the default are omitted) — an untouched profile writes `{ "version": 1 }` with `bindings` absent / empty; the `bindings` object is **recognised, validated for shape, logged once as "not applied before M1"** and ignored; physical keys inside it, if present, are by stable name (story 003), never raw enum ints.
- [ ] **H-R12-1 (core half).** A file with `holdSettingMs: 600` and the Deliver class `"Toggle"` yields `HoldSettingMs == 600` and Deliver in Toggle mode (the GDD example `holdSetting 0.6` expressed in the ADR's integer unit); a malformed file (truncated JSON, wrong types, empty) yields `Defaults` and exactly **one** warning in the report and the `UiEventLog` (`Config`).
- [ ] **Clamping and validation.** Out-of-range values clamp to the nearest bound and are logged (`Config`): `dzInner` 900‰ → 400‰ with `dzOuter` unchanged at 950‰ (H-F3-1 clamp row); `dzOuter − dzInner ≥ 100‰` is enforced by raising `dzOuter`; `trigOn` floored at 150‰ and `repeatInterval` at 40 ms so no value means "always pressed" or divides by zero; `holdSettingMs` 430 rounds to the nearest 50 (450) and 150 / 1200 clamp to 200 / 1000; a missing field takes its default; an unknown key is dropped and logged once per key; unknown `holdMode` strings fall back to the class default. NaN cannot occur (integers).
- [ ] **Versioning.** `Version` 1 loads; a higher version loads as far as it can with one warning; a registered `Migrate(v → v+1)` chain exists (empty at v1, with a fixture migration test proving the hook runs in order).
- [ ] **Supply to the Sim and router.** `InputSettingsHolder : IClientInputSettings` (the Sim port from story 002) exposes `HoldSettingMs` to the ClientLayer; the router reads the rest (stories 009–018 switch from `InputDefaults` to this struct). `ApplyDerived(in InputSettings)` rebuilds the response-curve table (story 009) and the repeater constants, **outside the frame**, only when settings change.

---

## Implementation Notes

*Derived from ADR-0012 §5, §9 and GDD R10:*

- Files in `src/PerformativeMail.Client.Core/Input/Settings/`: `InputSettings.cs`, `InputSettingsCodec.cs`, `SettingsLoadReport.cs`, `SettingsRanges.cs` (one table of `(key, min, max, default, step)` driving both clamp and tests), `InputSettingsHolder.cs`.
- **GDD ↔ ADR conflict (ADR followed):** the GDD names `holdSetting` in seconds (0.2–1.0, "a count of 50 ms steps") whereas ADR-0012 §GDD Impact fixes `holdSettingMs` (integer ms, multiple of 50), the unit Manual Delivery F2 and `Hold.HoldTicks` use. The file keeps the ADR's integer form.
- **Settings the router does not read at M-1a** (aim steadiness, glyph style variants, face layout, swap sticks) are parsed and range-checked so the file format does not change at M1; they have no effect yet.
- The adapter (story 028) reads `user://input.json` bytes with `FileAccess.GetFileAsBytes` and hands the span to `Parse`; a missing file is **not** an error (defaults, no warning).
- Cross-references: the Hold time slider range is the creative-director directive in Grid Inventory OQ22 (0.2–1.0 s); `holdCeilMs` 2000 and `holdDefaultMs` 400 are Input constants in Player Character's `HoldSettings` (`production/epics/player-character/story-011-hold-ticks-holdeff.md`), not settings.
- OQ-IC-2 (per-machine vs roaming) is M1 (Profile & Settings Persistence) — the file is per machine.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- M1: the Controls settings screen, binding overrides, conflict re-validation on load (H-EC-9), "Needs binding", required-action reset, reset / undo (H-R10-*), Profile & Settings Persistence (ADR-0024, not written).
- Story 028: reading the file with `FileAccess`.
- Applying `holdSettingMs` in the hold counter (Player Character 011/017, Manual Delivery 001 take it as an argument from `IClientInputSettings`).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/InputSettingsCodecTests.cs` (`[Trait("Id", "H-R12-1")]`, `[Trait("Id", "H-F3-1")]`), `tests/unit/input/InputSettingsRangesTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002 (`IClientInputSettings`), 003 (log, `PhysicalInput` names), 005 (`ActionMap` for binding-shape validation); `production/epics/content-data/story-004-balance-registry-simsettings.md` (settings conventions; this is *player* input settings, not `SimSettings`)
- Unlocks: Stories 009–018 (switch from defaults), 028; `production/epics/player-character/story-017-hold-counter-cancels-channel-focus-latch.md` and `production/epics/manual-delivery/story-001-deliver-anchors-hold-ticks.md` (a real `holdSettingMs` source)
