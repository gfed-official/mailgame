# Story 004: Boot sequence — the nine ADR-0019 steps as an explicit plan, the error screen, window guard and the M-1a `BuiltInContent` step

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day; ~400 lines prod + smoke scenarios)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/simulation-core.md` (R4 composition root, `SimBuildException` at boot; R17 `GameLoop`), `design/gdd/content-data-definitions.md` (CD21 the Godot side hands content to the loader at boot; M-1a uses `BuiltInContent`)
**Requirement**: `TR-sim-007` (`Build` and `Freeze` run on the thread that will tick — boot runs on the main thread), `TR-sim-013` (the host calls `SimComposition.Build` once, at boot; no static or locator), `TR-content-021` (content reaches the game through the content step only; the Sim never reads `res://`)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. The boot order itself, the error screen and the extension slots have **no TR-ID in the registry**; they cite ADR-0019 §3 and `docs/architecture/architecture.md` §4.)*

**ADR Governing Implementation**: ADR-0019: Godot Presentation Scene Architecture (§3 boot order; §1 `Main/Screens`; §7 minimum window set in boot step 1) — Accepted 2026-10-16 (primary); ADR-0003 (§3 `Build`/`Freeze` on the ticking thread, `SimBuildException` only at composition; Risks: the host catches it explicitly) — Accepted 2026-10-16; ADR-0010 (§4: M-1a `BuiltInContent`, one `ContentSet` for the process lifetime) — Accepted 2026-10-16 (secondary)
**ADR Decision Summary**: `GameLoop._Ready` runs nine steps in order: (1) window/stretch guard and `MinSize` 1280×720; (2) content (M-1a `BuiltInContent`; on failure show the content-error screen and never compose the Sim); (3) persistence (ADR-0024); (4) input router (ADR-0012); (5) session (loopback, ADR-0011); (6) `SimComposition.Build` → `ClientLayer.Attach(ports)`; (7) world tables → `Terrain`/`Destinations` built; (8) `ViewCatalogResolver.Resolve(ContentSet)` → pools pre-instantiated; (9) `TickDriver.Start` (armed here, executed on the first `_Process` — ADR-0002). Any exception goes to the error screen and `RunFrame` stops.
**ADR Version**: ADR-0019 2026-10-16; ADR-0003 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 .NET | **Risk**: HIGH
**Engine Notes**: Window API names for 4.7.1 are not in `docs/engine-reference/godot/`: `GetWindow().MinSize` is the ADR-0019 §7 choice ("no project setting exists", VERIFIED in the spike) — assert the value reads back; `Window.MinSize` on Wayland and hiDPI (unit = window pixels) and `keep_height` below 16:9 are UNVERIFIED (V4). `OS.IsDebugBuild()` gates debug-only behaviour (works for `ExportDebug`). Unhandled exceptions in Godot callbacks are logged and the engine keeps running (UNVERIFIED for 4.7) — hence the explicit catch.

**Control Manifest Rules (Foundation)**:
- Required: boot order per ADR-0019 §3; any exception goes to the error screen and `RunFrame` stops; `SimComposition.Build` runs once on the main thread and the host catches `SimBuildException` (ADR-0002/0003/0019)
- Required: `ContentSet` is built once and held for the process lifetime, passed by reference — no static holder (ADR-0010 §4, ADR-0003)
- Forbidden: a locator, static `Instance` or `GetNode` lookup inside a view's `_Ready` to reach boot products; `ChangeSceneToFile` to show the error (the error screen is a `Main/Screens` child); wall-clock time inside boot decisions that reach the Sim (run seed is a constant or `--seed`, story 005)
- Guardrail: boot to first frame well under the content budget (advisory ≤ 100 ms content, TR-content-028) — logged, not gated

---

## Acceptance Criteria

*From ADR-0019 §3 and Validation Criteria ("a headless boot of `Main.tscn` logs the boot steps in §3 order"), scoped to this story. Delta: story 002/003 gave an un-booted tree and a frame loop; nothing composes content, the Sim or views yet. Steps 5–8 are stubs here (`NOT WIRED`), filled by stories 005, 006, 008, 009.*

- [ ] **Explicit ordered plan.** `src/Game/Boot/BootPlan.cs` lists the nine steps in explicit code (no reflection discovery, ADR-0003 style); `BootSequence.Run(BootContext)` executes them in order on the main thread from `GameLoop._Ready` (unless `AutoBoot` is false or a no-boot smoke is requested) and writes one log line per step: `BOOT <n> <name> ok` (or `FAIL`). A step not yet wired by its owning story is an `UnwiredBootStep` that logs `BOOT <n> <name> NOT WIRED (story …)` — never a silent skip; the smoke scenario `boot-order` asserts the nine step numbers appear in ascending order and lists which steps are `NOT WIRED` (story 011 asserts there are none).
- [ ] **Step 1 — window guard.** `GetWindow().MinSize` is set to `Vector2I(1280, 720)` and read back; the log prints the window size, content-scale mode/aspect and `Sim.AssertsEnabled`, `OS.IsDebugBuild()`, `System.Environment.Version`, `Stopwatch.Frequency`, `System.Globalization.CultureInfo.CurrentCulture.Name`, the boot thread id — the facts ADR-0001/ADR-0002 leave UNVERIFIED (invariant flag in editor runs, Windows frequency) become retained log lines, not assumptions.
- [ ] **Step 2 — content (M-1a).** `BuiltInContent.CreateM1a()` is called exactly once; the resulting `ContentSet` is placed in `BootContext` and logged by table counts (and its content hash if `ContentHash` exists). The content-source choice is behind `IContentBootSource` so story 007 can add `ContentHost` without editing the sequence; any exception or Error diagnostic from this step shows the content-error screen and the Sim is never composed (`BOOT 2 content FAIL`, steps 5–9 do not run).
- [ ] **Steps 3 and 4 — extension slots.** Step 3 (persistence, ADR-0024 not written) logs `BOOT 3 persistence: no contributor` and passes. Step 4 runs registered `IBootContributor`s in registration order (`Step`, `Run(BootContext)`); none registered logs `BOOT 4 input: no contributor`. Input-controls 028's `InputBoot` is added as one line in `BootPlan` — this story builds the contract and tests it with a recording contributor.
- [ ] **Failure shows the error screen and stops.** `Main/Screens/BootError` (a `Control` scene; Label with the message, Quit button; stock `ui_*` focus is allowed here) is shown when any step throws; the exception type, message and, for `SimBuildException`, its `Code` are logged and displayed; the `GameLoop` is marked `Faulted` through story 003's `IFaultSink` so `RunFrame` is never called (counted: 0). In a headless smoke the process exits `4` (boot failed). A smoke scenario injects (a) a throwing contributor and (b) a `SimBuildException`-throwing step and asserts the screen is visible, the code is shown and no frame ran.
- [ ] **Step 9 — armed, not started.** Boot completion arms story 003's `StartPending`; `TickDriver.Start` is not called in `_Ready` (asserted by the recording smoke: Start occurs on the first `_Process`).

---

## Implementation Notes

*Derived from ADR-0019 §3, ADR-0003 §3, ADR-0002 §3:*

- Files in `src/Game/Boot/`: `BootPlan.cs`, `BootSequence.cs`, `BootContext.cs` (carries `ContentSet`, later `SimHost`/session, `IWorldQuery`, `PresenterDeps`; plain C#, no statics), `IBootStep.cs`, `IBootContributor.cs`, `UnwiredBootStep.cs`, `IContentBootSource.cs` + `BuiltInContentSource.cs`, `BootLog.cs` (writes to stdout via `GD.Print` **and** an in-memory list the smoke scenarios read); `src/Game/Screens/BootError.tscn` + `BootErrorScreen.cs` (implements `IFaultSink`).
- Log format is contract (story 011's wrapper parses it): `BOOT <n> <name> ok|FAIL|NOT WIRED …`, one line each, plus `BOOT env key=value …`.
- Slots other epics fill (each is one explicit line in `BootPlan`): step 3 ← persistence (ADR-0024); step 4 ← `InputBoot` (input-controls 028: validate `BuiltInActionMap`, read `user://input.json`, construct the router, wire sinks); step 5/6 ← story 005; step 7 ← story 006/010; step 8 ← story 008/009. Document this table in the `BootPlan` header comment.
- The error screen is the generic fault display. The M0 content-error screen (CDD-LOAD-2: lists every diagnostic, only Quit and Retry) and Retry belong to the M0 `ContentHost` work; do not build Retry here.
- `Build`/`Freeze` thread rule: record `System.Environment.CurrentManagedThreadId` in `BootContext` for story 003's thread check.
- `System.Environment` must be written fully qualified in `src/Game/**` (ADR-0001 F7).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 005: steps 5–6 (`HostSession`, `ClientLayer.Attach`). Story 006: step 7 hand-off and `Initialize(deps)`. Story 007: `ContentHost`. Story 008: step 8 (`ViewCatalogResolver`). Story 009–010: what steps 7–8 build.
- Input-controls 028: step 4's content. ADR-0024 persistence. The M0 content-error screen, Retry and F6 hot reload (ADR-0019 §4, TR-content-026).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: smoke scenarios `boot-order` and `boot-failure` (exit 0 / the failure case asserts exit 4 in its own sub-run) and the retained boot log excerpt in `production/qa/evidence/host-shell-boot-evidence.md` — `/story-done` checks the scenarios and the evidence file exist; pass/fail via `/smoke-check`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 003; `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (`BuiltInContent.CreateM1a`), `production/epics/simulation-core/story-025-composition-root-reflection-test.md` (`SimBuildException` flows from `SimComposition`)
- Unlocks: Stories 005, 006, 007, 008, 009, 011; `production/epics/input-controls/story-028-gameloop-hookup-router-boot-settings-file.md` (boot step 4 slot, error screen)
