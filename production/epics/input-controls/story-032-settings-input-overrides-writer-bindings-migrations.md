# Story 032: `settings.input` rebinding support — the overrides-only writer, the `bindings` map (R12) and the version-migration chain

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Deferred (Evan 2026-10-23 8:54 AM PT)
> **Deferred to**: M1 (Controls settings screen and remap) (out of the M-1a slice; split from story 019)
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~150 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **DEFERRED 2026-10-23, not deleted. Approved by Evan 2026-10-23 8:54 AM PT, Mail Lead chat: "Approve the ADR-0024 additions and the extra deferrals (md-008, tc-025, md-010 localisation to md-012, and the split-off hardening stories)".** Earlier context (not the approval of this story): Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it “production ready” is not needed. Accessibility for example". 8:10 AM PT, approving errata E1–E6 and E9 (decision log 2026-10-23): "approve all". This story is freed by **E1 (ADR-0012 — R12 rebinding)**. Reason: nothing writes `user://input.json` before the M1 Controls screen exists, the `bindings` map is only applied at M1, and a one-version file needs no migration chain; story 019 keeps the integer-unit `InputSettings`, its defaults and the parse / clamp path that story 028 boots with. The ACs below were moved verbatim from story 019 (the Parse clause of "Parse and write" stays in 019 and is quoted here only for context). Re-run `/story-readiness` when M1 planning starts.

## Context

**GDD**: `design/gdd/input-controls.md` (R10 "Storage"; R12 rebinding — "overrides-only", `bindings` by stable physical-key name; Edge Cases "Settings and data"; AC H-R12-1 storage half)
**Requirement**: `TR-input-020` (`settings.input` schema versioned, overrides-only) — the "overrides-only" write and "versioned" migration clauses; the parse / clamp clause stays in story 019
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§9 "`settings.input` (R10, TR-input-020)") — Accepted 2026-10-16 (primary); ADR-0010 (hand-written `Utf8JsonReader` walk, no reflection binding) secondary; E1 (2026-10-23) moves R12 rebinding to M1
**ADR Decision Summary**: Versioned, overrides-only JSON; physical keys by stable name. Unknown ids are dropped and logged, malformed bindings fall back to default, migrations run vN → vN+1.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0010)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW (codec); the `FileAccess` write is the M1 settings-screen story's
**Engine Notes**: None for the codec; writing `user://` goes through ADR-0024 persistence at M1.

**Control Manifest Rules (Foundation)**:
- Required: no `JsonSerializer`, `JsonDocument`, `GetDouble`, `GetDecimal`, `double.Parse` or reflection discovery; integer fields only; ordinal comparisons (manifest ADR-0010 forbidden list)
- Forbidden: persisting `DefIndex` or Godot enum ints (physical keys by **name**)
- Guardrail: the codec allocates at load / save only

---

## Acceptance Criteria

*From GDD R10 Storage, R12 and Edge Cases "Settings and data", moved verbatim from story 019:*

- [ ] **Parse and write — the write and `bindings` clauses** (from story 019; the Parse clause stays there). `Write(in InputSettings s, Span<byte> utf8)` emits **overrides only** (fields equal to the default are omitted) — an untouched profile writes `{ "version": 1 }` with `bindings` absent / empty; the `bindings` object is **recognised, validated for shape, logged once as "not applied before M1"** and ignored; physical keys inside it, if present, are by stable name (story 003), never raw enum ints. (Story 019's parser ignores an unknown `bindings` key like any other unknown key until this story lands; `InputSettingsCodec.Parse` regains an `ActionMap` parameter here for the shape validation.)
- [ ] **Versioning** (from story 019). `Version` 1 loads; a higher version loads as far as it can with one warning; a registered `Migrate(v → v+1)` chain exists (empty at v1, with a fixture migration test proving the hook runs in order).
- [ ] **Round trip.** `Parse(Write(s))` equals `s` for `Defaults` and for a profile with every scalar off-default; the written bytes contain no field equal to its default and no floating-point literal.

---

## Implementation Notes

*Derived from ADR-0012 §9 and GDD R10 / R12 (moved from story 019):*

- Files in `src/PerformativeMail.Client.Core/Input/Settings/`: the `Write` half of `InputSettingsCodec.cs`, `InputSettingsMigrations.cs`, the `bindings` walker.
- Physical keys by stable `PhysicalInput` name (story 003); `ActionMap` (story 005) supplies the action ids for shape validation.
- Applying `bindings` (H-EC-9 load-time collision / reset rules, "Needs binding", required-action reset) is the M1 Controls-screen story, not this one.
- OQ-IC-2 (per-machine vs roaming) is M1 (Profile & Settings Persistence) — the file is per machine.

---

## Out of Scope

- Story 019 (Ready, M-1a): `InputSettings`, defaults, the integer parser, clamping and the supply to Sim / router. M1: the Controls settings screen, applying binding overrides, conflict re-validation on load (H-EC-9), reset / undo (H-R10-*), Profile & Settings Persistence (ADR-0024).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/InputSettingsWriteAndBindingsTests.cs` (`[Trait("Id", "H-R12-1")]`), `tests/unit/input/InputSettingsMigrationTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 019 (`InputSettings`, `SettingsRanges`, the parser), Story 003 (`PhysicalInput` names), Story 005 (`ActionMap` for binding-shape validation)
- Unlocks: —
