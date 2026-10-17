# Story 011: Headless smoke — boot the greybox, ≥ 300 ticks, exit 0 — with the smoke wrapper, CI wiring and `tests/smoke/critical-paths.md`

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day; ~250 lines of scenario + wrapper + CI)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/simulation-core.md` (R17 frame order / 30 Hz; SC-R2-1 host over loopback; F2 no time created), `design/gdd/content-data-definitions.md` (CDD-SCHEMA-7 placeholder boot reaches `Valid`)
**Requirement**: `TR-sim-007` (main-thread Sim, end to end), `TR-sim-009` (pause as driver state, re-checked in the booted process), `TR-sim-040` (the boot log retains `Sim.AssertsEnabled` and the build mode), `TR-movement-007` (fixed-step driver at 30 Hz in a variable-rate Godot loop), `TR-content-021`, `TR-content-027` (placeholder boot, the host-content failure path stays wired)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. The headless smoke gate, the wrapper and the CI job have **no TR-ID in the registry**; they come from the epic's Definition of Done and ADR-0019 Validation Criteria ("a headless boot of `Main.tscn` logs the boot steps in §3 order").)*

**ADR Governing Implementation**: ADR-0019: Godot Presentation Scene Architecture (Validation Criteria; §3 boot order; Risks: "the editor `--quit-after` segfaulted at shutdown once — CI headless runs use `--quit` from code"; Risks: "export exits 0 despite errors" → the same trust problem applies to Godot runs) — Accepted 2026-10-16 (primary); ADR-0002 (§3, §5; F10 the accumulator at 30 Hz, F12 headless runs uncapped with a ~40 ms first-frame hitch) — Accepted 2026-10-16; ADR-0001 (§7 Godot-side tests deferred to gdUnit4 — the smoke scenarios are the interim) — Accepted 2026-10-16 (secondary)
**ADR Decision Summary**: Godot 4.7.1 headless is the gate for the Godot half until gdUnit4 exists. A run is judged by its log and exit code together — Godot exits 0 on logged script errors — and quits from code, not `--quit-after`. The boot log lists the nine steps in order; `ProcessMode`/`ProcessPriority` are asserted; there are no autoloads; pools and nodes are not created after boot.
**ADR Version**: ADR-0019 2026-10-16; ADR-0002 2026-10-16; ADR-0001 2026-10-16

**Engine**: Godot 4.7.1 .NET (`mise exec dotnet@10.0.402 -- ~/.local/bin/godot-mono --headless --path . -- --smoke=<name>`) | **Risk**: HIGH
**Engine Notes**: Headless runs are uncapped (~145 fps) with a ~40 ms first-frame hitch (F12): the tick count is driven by wall time, so `boot-300` takes ≈ 10 s of real time at 30 Hz. Godot returns exit 0 even when scripts logged `ERROR:`/`SCRIPT ERROR:` (the export exit-0 problem, ADR-0019 Risks) — the wrapper therefore also scans the log; a process killed by a signal (the once-seen shutdown segfault) is a FAIL with the signal named. The 4.7.1 headless binary's shared-library needs on a clean `ubuntu-latest` runner are UNVERIFIED; the Godot 4.7.1 mono release URL pattern below is taken from earlier 4.x releases and is UNVERIFIED for 4.7.1. A CI failure to obtain or run the editor is a failed job, never a skipped one.

**Control Manifest Rules (Foundation / Presentation)**:
- Required: CI headless runs quit from code; every gate prints what it checked and fails on zero checked (repo gate rule: a check that cannot report it did not run is not a check); Godot launched via `mise exec dotnet@10.0.402 -- godot-mono …` locally (manifest Toolchain)
- Required: one `GameLoop`, `ProcessMode = Always`, `ProcessPriority = int.MinValue`, no autoloads, no `SceneTree.Paused` — asserted in the booted process (ADR-0019 Validation Criteria)
- Forbidden: `--quit-after` as a pass condition; treating exit 0 alone as success; skipping the job when Godot is unavailable; committing run logs outside `production/qa/evidence/`
- Guardrail: the whole smoke suite < 3 minutes on a CI runner; `boot-300` < 45 s wall (advisory)

---

## Acceptance Criteria

*From the epic's Definition of Done ("a headless `godot-mono` smoke run boots the greybox, ticks ≥ 300 times and exits 0 … wired into `tests/smoke/critical-paths.md`") and ADR-0019 Validation Criteria, scoped to this story. Delta: stories 001–010 each carry their own scenarios and evidence; there is no single booted run, no wrapper that judges log and exit code together, no CI job with the editor installed, and `tests/smoke/critical-paths.md` is still the generic template (its row 0 runs only the .NET suite).*

- [ ] **`boot-300` — the end-to-end gate.** Scenario `boot-300` (`NeedsBoot = true`) boots `Main.tscn` with the real `StopwatchMonotonicClock` and `BuiltInContent`, and passes only when all of the following hold in one run, then quits `0` from code: (1) all nine `BOOT n … ok` lines appear in ascending order and **none** reads `NOT WIRED` (no skipped step is a pass); (2) `World.CurrentTick ≥ 300` before the 45 s timeout, the world is not `Faulted`, `FrameRunner` ran on the boot thread, and `RunFrame` was called once per frame (frame count > 0); (3) the mean tick rate over ticks 30–300 is within 27–33 Hz (a 10 % band — the accumulator makes wall-time ticks exact, F10; `Driver.Counters` dropped/clamped/hitch totals are logged, and any non-zero dropped total is printed as a WARN, not a failure, because the ~40 ms first-frame hitch is expected); (4) `ObjectNodeCount` is identical at tick 60 and tick 300 (no node instantiated in steady state, ADR-0019 pools rule); (5) `GameLoop` has `ProcessMode = Always`, `ProcessPriority = int.MinValue`, there is one `GameLoop`, no autoload, and `Present` was called every frame with `alphaPermille` in 0–999; (6) the placeholder views resolved (10 `VIEW warn` lines, 0 errors) and `TerrainView` built once; (7) the boot environment line is present (`Sim.AssertsEnabled`, runtime version, `Stopwatch.Frequency`, culture, debug build flag). Managed bytes allocated per frame over ticks 60–300 are logged (advisory).
- [ ] **A wrapper that judges log and exit code together.** `tools/ci/run-godot-smoke.sh` (locates the editor via `GODOT` or `~/.local/bin/godot-mono`; wraps with `mise exec dotnet@10.0.402 --` unless `dotnet` is already on `PATH`; builds with `--build-solutions --quit`; runs `--smoke=list` to **derive** the scenario set and each scenario's expected exit code — `SMOKE LIST <name> expect=<code>` lines produced by the harness from the `[SmokeScenario]` attributes, so a new scenario cannot ship outside the gate; runs each scenario under `timeout 90`) reports a scenario as PASS only if the exit code equals its expected code **and** the log contains `SMOKE RESULT name=<n> pass=<p> fail=0` with `p > 0` **and** the log contains no `ERROR:`, `SCRIPT ERROR:`, `Unhandled exception` or `NOT WIRED` line (negative-path scenarios — boot failure, missing host content — declare `expect=4` and are PASS only on exit 4 with their own PASS lines); a signal exit (code ≥ 128) is FAIL naming the signal. It prints `ran N scenarios, P pass, F fail`, exits non-zero on any failure **or when N is 0**, writes logs to `${SMOKE_LOG_DIR:-/tmp/pm-smoke}`, and has a `--self-check` that feeds the log analyser synthetic logs and is seen failing on each of: exit 0 with no RESULT line, an `ERROR:` line, `fail>0`, a `NOT WIRED` line, zero scenarios, a signal exit.
- [ ] **CI runs it with the editor installed.** `.github/workflows/tests.yml` gains a `godot-smoke` job on `ubuntu-latest` that installs .NET 8.0.x + 10.0.x, downloads the Godot 4.7.1 mono Linux x86_64 release (`https://github.com/godotengine/godot-builds/releases/download/4.7.1-stable/Godot_v4.7.1-stable_mono_linux_x86_64.zip` — URL pattern UNVERIFIED for 4.7.1), verifies the archive against the release's `SHA512-SUMS.txt`, installs whichever shared libraries the headless binary reports missing, sets `GODOT`, runs `bash tools/ci/run-godot-smoke.sh --self-check` and then `bash tools/ci/run-godot-smoke.sh`, and uploads `${SMOKE_LOG_DIR}` as an artifact. The job also runs the epic's guard scripts: `check-scene-rules.sh`, `check-sim-access.sh`, `check-no-scenetree-paused.sh`, `check-catalog-uids.sh`, `check-gdignore.sh` (each with `--self-check`). If the download, checksum, restore or editor start fails, the job fails (recorded in Last Updated), never skips.
- [ ] **`tests/smoke/critical-paths.md` and `project.yaml` name the gate.** `critical-paths.md` gains automated rows: `0b` `bash tools/ci/run-godot-smoke.sh` exits 0 with N > 0 scenarios and `boot-300` PASS, and `0c` the guard-script list above; rows 1–3 (main menu, new game, menu inputs) are marked `N/A at M-1a — replaced by 0b (no menu exists; boot goes straight to the greybox street)` rather than deleted; row 4 (primary mechanic) is updated to "the greybox street boots, the loopback session ticks ≥ 300 and exits cleanly" with a pointer to each feature epic to append its own smoke row. `project.yaml` `commands.run` becomes `mise exec dotnet@10.0.402 -- ~/.local/bin/godot-mono --path . --windowed --resolution 1280x720` and `commands.smoke` becomes `bash tools/ci/run-godot-smoke.sh`.
- [ ] **Evidence.** `production/qa/evidence/host-shell-smoke-evidence.md` records the environment block, the wrapper's full output (including the `--self-check` run), the `boot-300` log excerpt (the nine `BOOT` lines, the env line, the tick-rate figures) and one green CI run link or, if CI cannot be run from the dev machine, `NOT RUN — needs a push` for that row (never PASS).

---

## Implementation Notes

*Derived from ADR-0019 Validation Criteria/Risks, ADR-0002 §3/F10–F12, ADR-0001 §7:*

- Files: `src/Game/Smoke/Boot300Smoke.cs` (+ whatever `SmokeScenario.ExpectedExit` and the `--smoke=list` output need in the story 002 harness — add `ExpectedExit` and `list` there if absent), `tools/ci/run-godot-smoke.sh`, edits to `.github/workflows/tests.yml`, `tests/smoke/critical-paths.md`, `project.yaml`.
- Run it locally exactly as CI does; the manual form is `mise exec dotnet@10.0.402 -- ~/.local/bin/godot-mono --headless --path . -- --smoke=boot-300`.
- Do not assert wall-clock durations tighter than the bands above: CI machines hitch. The tick *rate* is the accumulator's guarantee; the *count* check uses `World.CurrentTick`, not frames.
- A scenario that depends on tick counts for logic (not rate) uses the fake clock via `BootOptions.Clock` (story 005); `boot-300` is the one place the real clock is used.
- Windows and macOS legs are M0 (ADR-0001 §7); the export story owns running the smoke against an exported build.
- Keep the wrapper's analyser a pure function over (exit code, log text, expected code) so `--self-check` can exercise it without running Godot.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Per-story scenarios and guard scripts (stories 001–010 each own theirs; this story only runs them together).
- gdUnit4 setup (deferred, `tests/README.md`), the input-adapter smoke (`--smoke=input-adapter`, input-controls 029 — it is discovered by `--smoke=list` automatically), Windows/macOS legs, export/templates, performance gates (Q-ARCH-10), soak runs.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `production/qa/evidence/host-shell-smoke-evidence.md` (wrapper output, `--self-check`, `boot-300` log excerpt, environment block) and the `godot-smoke` workflow job — `/story-done` checks the scenario, wrapper, workflow job and evidence file exist; pass/fail is established by `/smoke-check` and CI

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001–010 (the booted greybox with nothing `NOT WIRED`); `production/epics/simulation-core/story-024-framerunner.md`, `production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md`
- Unlocks: Epic Definition of Done; `production/epics/input-controls/story-028-gameloop-hookup-router-boot-settings-file.md` and `story-029-adapter-integration-suite-and-manual-evidence.md` ("the godot-host-shell headless smoke still passing", `--smoke=input-adapter` discovery); every Godot-side story's regression gate
