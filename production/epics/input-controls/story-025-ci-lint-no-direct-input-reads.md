# Story 025: CI lint — no direct `Input.*` reads or `InputMap` mutation outside `src/Game/Input/` (and no `device == 0`)

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Deferred (Evan 2026-10-23 7:13 AM PT)
> **Deferred to**: M0 (out of the M-1a slice; was Ready)
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: S (~0.5 day; ~120 lines of shell + fixtures + a workflow step)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **DEFERRED 2026-10-23 by Evan, not deleted.** Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". This story is one the 7:13 approval names: "the deferrals" are the Mail Lead recommendations in `/workspace/repos/logs/m0-scope.md` §6a, and item 6 is "Defer the zero-alloc, timing and CI-gate stories (simulation-core 029/030, input-controls 024/025, player-character 010 golden hash) to M0 hardening". Errata E1 (ADR-0012 l.281, H-R1-2) (approved 8:10 AM PT, "approve all") bring the Accepted ADRs in line with it. Reason: CI lint; input-controls 026 no longer waits for it. Re-run `/story-readiness` when M0 planning starts; the text below is unchanged from the last Ready/Blocked version.

> **Not spike-gated.** ADR-0012's Migration Plan lists the lint among the post-spike adapter work, but §8 fixes its rules completely (a grep list) and carries no UNVERIFIED row, so this story is `Ready` and depends on nothing. Schedule it after story 001 for tidiness only. `src/Game/` does not exist yet: the script must handle that case **loudly** (see AC-3).

## Context

**GDD**: `design/gdd/input-controls.md` (R1 "No gameplay code reads a key, button or `Input.*` state directly … A static check enforces it", R11 "One gate"; AC H-R1-2)
**Requirement**: `TR-input-002` (all gameplay reads via the router; direct `Input.IsAction*` outside the adapter is a CI lint failure)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§8 "One gate, enforced (TR-input-002, TR-input-007)") — Accepted 2026-10-16
**ADR Decision Summary**: CI lint `tools/ci/lint-input-reads.sh` (grep-based, runs in the build job): any of `Input.IsAction*`, `Input.GetActionStrength` / `GetActionRawStrength`, `Input.GetAxis`, `Input.GetVector`, `Input.IsKeyPressed` / `IsPhysicalKeyPressed` / `IsKeyLabelPressed`, `Input.IsMouseButtonPressed`, `Input.IsJoyButtonPressed`, `Input.GetJoyAxis`, `Input.IsAnythingPressed` and any `InputMap.` mutation in `src/Game/**/*.cs` **outside `src/Game/Input/`** fails the build. A committed planted-violation fixture proves the lint fires. Sim and Client.Core cannot reference Godot at all (ADR-0001 architecture test).
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (C#) | **Risk**: LOW (text scan)
**Engine Notes**: The adapter itself legitimately polls `Input.GetJoyAxis` to re-seed its axis cache on focus-in / reconnect (ADR-0012 Step 5.5 note 5); that exemption is the `src/Game/Input/` directory.

**Control Manifest Rules (Foundation / Presentation)**:
- Required: direct `Godot.Input` reads and `InputMap` mutation occur only in `src/Game/Input/`; the CI lint fails otherwise (ADR-0012 Implementation Guidelines; manifest Forbidden)
- Required: compare `InputEvent.Device` against `DeviceIdMouse` / `DeviceIdKeyboard`, never `0` (manifest Presentation, Forbidden APIs)
- Forbidden: relying on code review alone; a lint that cannot say it scanned nothing
- Guardrail: runs in seconds in the existing build job

---

## Acceptance Criteria

*From GDD H-R1-2 and ADR-0012 §8, scoped to this story:*

- [ ] **H-R1-2.** `tools/ci/lint-input-reads.sh` exits 0 on a clean tree and non-zero when any `src/Game/**/*.cs` file **outside `src/Game/Input/`** matches `\bInput\.(IsAction\w*|GetActionStrength|GetActionRawStrength|GetAxis|GetVector|IsKeyPressed|IsPhysicalKeyPressed|IsKeyLabelPressed|IsMouseButtonPressed|IsJoyButtonPressed|GetJoyAxis|IsAnythingPressed)\b`, `\bGodot\.Input\.`-qualified forms of the same, a `using static Godot.Input;`, or `\bInputMap\.` (any member); files **inside** `src/Game/Input/` are exempt. A planted violation fixture (`tests/fixtures/input-lint/Violation.cs`, calling `Input.IsActionPressed` and `InputMap.ActionAddEvent`) makes `lint-input-reads.sh --fixture tests/fixtures/input-lint/Violation.cs` fail, and a clean fixture passes.
- [ ] **Device-0 rule (manifest-derived, not in ADR §8).** The same script fails on `\.Device\s*(==|!=)\s*0\b` anywhere under `src/Game/` (including `src/Game/Input/`), since `0` is a valid joypad index and the 4.7 constants are `DeviceIdKeyboard` / `DeviceIdMouse` (`docs/engine-reference/godot/breaking-changes.md`). A fixture proves it.
- [ ] **A scan that scanned nothing says so.** `src/Game/` does not exist yet: the script prints `lint-input-reads: src/Game not present — scanned 0 files (nothing to lint yet)` and exits 0; when it exists it prints `scanned N files` (N > 0 expected once the host shell exists). It never prints a bare "OK" for an empty scan. `--self-check` proves the patterns match the violation fixture forms and do not match a clean file (pattern: `tools/ci/check-no-scenetree-paused.sh`).
- [ ] **CI wiring.** A step "Guard - no direct Input reads outside src/Game/Input" is added to `.github/workflows/tests.yml` next to the existing guards, running `bash tools/ci/lint-input-reads.sh` and `--self-check`; the fixture failure is asserted by a step that expects a non-zero exit (so the gate has been watched failing).

---

## Implementation Notes

*Derived from ADR-0012 §8 and `tools/ci/check-no-scenetree-paused.sh`:*

- Follow the existing script's conventions: `set -u`, repo root from `BASH_SOURCE`, grep exit codes 0 = match = fail / 1 = clean / other = error (an error must **not** read as clean), a `--self-check` mode.
- Known limits to state in the script header: the regex misses an alias (`var i = Input.Singleton; i.IsKeyPressed(...)`) and string-concatenated reflection; both are review-enforced. `Input.MouseMode` is **not** in ADR §8's list (the adapter owns mouse mode by convention, story 027, and a view may read it for display), so the lint does not ban it; record that in the script header.
- Sim and Client.Core cannot reference Godot (`grep -rn "using Godot" src/PerformativeMail.Sim src/PerformativeMail.Client.Core` guard already exists in `tests.yml`); the lint is only for `src/Game/`.
- Do not scan `prototypes/` (throwaway spikes read `Input.*` freely) or `tests/`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 026: the adapter that legitimately lives in `src/Game/Input/`. A Roslyn-based lint (a later hardening if the grep proves too weak).
- `FocusMode.None` enforcement on game Controls (a scene/code convention checked in story 029 and by the owning view stories).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration (CI): `tools/ci/lint-input-reads.sh` with `--self-check`, `tests/fixtures/input-lint/Violation.cs` and `Clean.cs`, and the `.github/workflows/tests.yml` step showing the planted fixture failing — a CI run link or log excerpt in the story-done notes

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Story 026 (the adapter lands under the lint), every `src/Game/**` story that must not read input directly
