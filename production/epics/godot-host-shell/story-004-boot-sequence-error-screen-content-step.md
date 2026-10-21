# Story 004: Boot sequence — the nine ADR-0019 steps as an explicit plan, the error screen, window guard and the M-1a `BuiltInContent` step

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day; ~400 lines prod + smoke scenarios)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-21 (implemented and closed: /dev-story → /code-review → /story-done, see Completion Notes). 2026-10-21 (Mail Review R1 on PR #15: retained BootError/ContentError screenshots via windowed `--write-movie` and an AC that they exist before Complete; one-frame wait before fault assertions; `AssertsEnabled` equality pinned to Debug; evidence file holds the real boot log). 2026-10-21 (readiness fixes, docs-only: manifest re-stamp (the only 2026-10-17.1 change near this story is ADR-0019 §3 step 4's input-adapter placement, input-controls 028's); citations checked against main c7fa0f6; boot runs from `GameLoop.RequestBoot()` with a defined return contract; boot faults use a new `GameLoop` boot-fault entry (the `IFaultSink` only receives messages); step-2 failure shows the `Main/Screens/ContentError` subtree (ADR-0019 §1/§3); `ContentHash` exists; smoke seams for the failure and exit-4 cases; TD-098 item 2 and TD-120 folded in)

## Context

**GDD**: `design/gdd/simulation-core.md` (R4 composition root, `SimBuildException` at boot; R17 `GameLoop`), `design/gdd/content-data-definitions.md` (CD21 the Godot side hands content to the loader at boot; M-1a uses `BuiltInContent`)
**Requirement**: `TR-sim-007` (owner thread captured at `Freeze()`; here, per ADR-0003 §3, `Build` and `Freeze` run on the thread that will tick — boot runs on the main thread), `TR-sim-013` (composition root, constructor injection only; here: the host calls `SimComposition.Build` once at boot and passes products by reference, no static or locator), `TR-content-021` (content reaches the game through the content step only; the Sim never reads `res://`)
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

- [x] **Explicit ordered plan.** `src/Game/Boot/BootPlan.cs` lists the nine steps in explicit code (no reflection discovery, ADR-0003 style); `BootSequence.Run(BootContext)` executes them in order on the main thread from `GameLoop.RequestBoot()` — called by `_Ready` when `AutoBoot` is true and no `--smoke=` argument is honoured (a non-debug build ignores `--smoke` and boots), or by `SmokeRunner` for `NeedsBoot` scenarios (`src/Game/Hosting/GameLoop.cs`, `src/Game/Smoke/SmokeRunner.cs`). `RequestBoot()` returns true when every step is `ok` or `NOT WIRED` and false when any step `FAIL`s; a second call is refused (logs `[GameLoop] boot already ran`, returns the first result); it sets `PeerPresence` to `LoopbackPeerPresence.Instance` only when none is set (closes TD-120) and writes one log line per step: `BOOT <n> <name> ok` (or `FAIL`). A step not yet wired by its owning story is an `UnwiredBootStep` that logs `BOOT <n> <name> NOT WIRED (story …)` — never a silent skip; the smoke scenario `boot-order` asserts the nine step numbers appear in ascending order and lists which steps are `NOT WIRED` (story 011 asserts there are none).
- [x] **Step 1 — window guard.** `GetWindow().MinSize` is set to `Vector2I(1280, 720)` and the read-back value is asserted equal by `boot-order` (headless read-back is UNVERIFIED: if it differs under `--headless`, log `BOOT 1 window min-size readback <value>`, assert only that the setter did not throw, and record the observed value in the evidence file); the log prints the window size, content-scale mode/aspect and `Sim.AssertsEnabled`, `OS.IsDebugBuild()`, `System.Environment.Version`, `Stopwatch.Frequency`, `System.Globalization.CultureInfo.CurrentCulture.Name`, the boot thread id (each as a `BOOT env key=value` line; `boot-order` logs both values and asserts `PerformativeMail.Sim.Sim.AssertsEnabled == OS.IsDebugBuild()` only in the Debug game configuration (the smoke runs a Debug build; the pair also matches for ExportDebug/ExportRelease per `tools/ci/check-build-config.sh`, but not for a `Release` game build run from the editor, so no equality is asserted there) and that the logged thread id equals `GameLoop.BootThreadId`) — the facts ADR-0001/ADR-0002 leave UNVERIFIED (invariant flag in editor runs, Windows frequency) become retained log lines, not assumptions.
- [x] **Step 2 — content (M-1a).** `BuiltInContent.CreateM1a()` is called exactly once; the resulting `ContentSet` is placed in `BootContext` and logged by table counts (`Containers`, `Items`, `MailKinds`, `DestinationTypes`, `MailMixes` `.Count`) and `ContentHash.Compute(set).ToDisplayString()` (`PerformativeMail.Sim.Content.Hashing`). The content-source choice is behind `IContentBootSource` so story 007 can add `ContentHost` without editing the sequence; any exception from this step (`BuiltInContent.CreateM1a` throws `InvalidOperationException` listing the findings; diagnostics are not surfaced separately at M-1a) shows the content-error screen `Main/Screens/ContentError` (ADR-0019 §1 lists a separate content-error subtree; at M-1a it is a message Label + Quit, and it may share `BootErrorScreen.cs`; the M0 diagnostics list and Retry stay out of scope) and the Sim is never composed (`BOOT 2 content FAIL`, steps 3–9 do not run).
- [x] **Steps 3 and 4 — extension slots.** Step 3 (persistence, ADR-0024 not written) logs `BOOT 3 persistence: no contributor` and passes. Step 4 runs registered `IBootContributor`s in registration order (`Step`, `Run(BootContext)`); none registered logs `BOOT 4 input: no contributor`. Input-controls 028's `InputBoot` is added as one line in `BootPlan` — this story builds the contract and tests it with a recording contributor.
- [x] **Failure shows the error screen and stops.** `Main/Screens/BootError` (a `Control` scene; Label with the message, Quit button; stock `ui_*` focus is allowed here) is shown when any step throws; the exception type, message and, for `SimBuildException`, its `Code` are logged and displayed; the boot sequence reports the failure through a new public `GameLoop` boot-fault entry (e.g. `FaultBoot(Exception)`) that uses the same private fault latch and `FaultSink` delivery as a frame fault (story 003's `IFaultSink.OnFault(string)` only receives the message; it cannot set the latch): `IsFaulted == true`, `StartPending == false`, one `[GameLoop] FAULT` line, `FaultSink.OnFault` called once — the `boot-failure` scenario waits one frame before asserting the sink call and the visible screen (on the main thread `DeliverFault` runs synchronously today, but the wrong-thread path in `GameLoop.cs` hands it over with `Callable.From(...).CallDeferred()`; a one-frame wait covers both and any later switch to deferred delivery) — and `RunFrame` never called (the recording `IFrameRun` counts 0). `GameLoop.FaultSink` is set to the error screen before step 1, so a frame fault after boot shows the same screen. Smoke: `boot-failure` (`NeedsBoot = false`, exit 0) builds a plan with (a) a throwing `IBootContributor` at step 4 and, in a second pass, (b) a step throwing `SimBuildException(SimBuildErrorCode.ReservedPhase, …)` and, in a third pass, (c) a throwing `IContentBootSource` (`InvalidOperationException` listing findings — asserts `Main/Screens/ContentError` is visible, `BootError` is not, the log has `BOOT 2 content FAIL` and no `BOOT 3` line; `BootErrorScreen` stays the single `IFaultSink` for both screens — `BootSequence` records the failing step's kind in `BootContext` (e.g. a `ShowContentError` call on the screen before the boot-fault entry) and `OnFault` shows the screen matching that kind); before each pass it installs a `FrameLoopRig` sequence via `InstallForSmoke` (which clears the fault latch), then runs `BootSequence.Run` itself and asserts the screen is visible, the label shows the exception type, message and (b) the code name, `IsFaulted`, `StartPending == false` and no frame ran. `boot-failure-exit4` (`NeedsBoot = true`, `ExpectedExit = 4`, its own process) installs a throwing plan before boot through a debug-only seam (e.g. a `SmokeScenario.BeforeBoot(GameLoop)` hook that `SmokeRunner.Begin` calls before `RequestBoot()`, plus a debug-only plan override on `GameLoop` like `InstallForSmoke`), so `RequestBoot()` returns false and the runner exits 4 (`ExitBootFailed`).
- [x] **Step 9 — armed, not started.** Boot completion arms story 003's `StartPending`; `TickDriver.Start` is not called in `_Ready`. Step 9 only calls `GameLoop.ArmStart()` — boot code under `src/Game/Boot/` never references `src/Game/Smoke/`. `boot-order` installs a `FrameLoopRig` sequence (`src/Game/Smoke/FrameLoopDoubles.cs`) in its pre-boot hook via `GameLoop.InstallForSmoke(seq, armStart: false)`, then asserts `StartPending == true` immediately after `RequestBoot()` and, after one frame, `StartPending == false` with the rig's driver started. Until story 005 installs the real sequence, a production boot leaves `StartPending == true` (`_Process` does nothing without a sequence).
- [x] **Retained screenshots (coding standards: "a parse check is not a run"; not waived).** `Main/Screens/BootError` and `Main/Screens/ContentError` are player-visible, so each has one retained PNG, captured from a windowed (never `--headless`) Godot run with `--write-movie` through a screen smoke seam, under `production/qa/evidence/godot-host-shell-story-004/`: `01-boot-error.png` and `02-content-error.png` (commands in Test Evidence). Both PNGs must exist, be looked at against this AC (message, exception type, code name, Quit visible; no clipped text at 1280×720) and be referenced from the evidence file **before the story is marked Complete**.
- [x] **Smoke bookkeeping (TD-098 item 2).** Once boot succeeds, `selftest-needs-boot` (`src/Game/Smoke/SelfTestSmoke.cs`, today `ExpectedExit => 4` only because `RequestBoot()` returns false) flips to `ExpectedExit => 0`, and a `main-tree-noboot` scenario (PackedScene + `AutoBoot = false` + a `RequestBoot` call counter = 0) is added; `--smoke=list` shows the new `expect=` values.

---

## Implementation Notes

*Derived from ADR-0019 §3, ADR-0003 §3, ADR-0002 §3:*

- Files in `src/Game/Boot/`: `BootPlan.cs`, `BootSequence.cs`, `BootContext.cs` (carries the `GameLoop` (or an `Action<Exception>` boot-fault callback), `ContentSet`, later `SimHost`/session, `IWorldQuery`, `PresenterDeps`; plain C#, no statics), `IBootStep.cs`, `IBootContributor.cs`, `UnwiredBootStep.cs`, `IContentBootSource.cs` + `BuiltInContentSource.cs`, `BootLog.cs` (writes to stdout via `GD.Print` **and** an in-memory list the smoke scenarios read); `src/Game/Screens/BootError.tscn`, `ContentError.tscn` + `BootErrorScreen.cs` (implements `IFaultSink`). `BootErrorScreen` may look up its own Label and Button in `_Ready` (`tools/ci/check-scene-rules.sh` R9 covers presenters and `*View.cs`; a screen wiring its own children is not the boot-product lookup R9 forbids) — keep the `*Screen.cs` name. The `.tscn` files are hand-written `format=3` like `Main.tscn` (TD-098 item 4). New smoke files in `src/Game/Smoke/`: `BootOrderSmoke.cs`, `BootFailureSmoke.cs`, `MainTreeNoBootSmoke.cs`, `ErrorScreenCaptureSmoke.cs` (`screen-boot-error`, `screen-content-error`). The screens follow the coding standards like any player-visible UI (retained screenshots, AC above); the only R9 note is that a screen script is not a presenter/view. Edits: `src/Game/Hosting/GameLoop.cs` (`RequestBoot` body, boot-fault entry, plan override seam, `FaultSink` install), `src/Game/Smoke/SmokeScenario.cs` + `SmokeRunner.cs` (pre-boot hook), `SelfTestSmoke.cs`, `PauseDriverStateSmoke.cs` (its final `loop.RequestBoot()` call, made on a loop with an installed test sequence, becomes `loop.PeerPresence = LoopbackPeerPresence.Instance` plus a check that `RequestBoot` leaves a preset presence alone — TD-120), `src/Game/Main.tscn` only if the screens are static children. `tools/ci/check-sim-access.sh` already exempts `src/Game/Boot/`.
- Neighbouring debt on the same fault path, not this story's: TD-113 (4) `_faulted` volatility, TD-121 double delivery when `LogObserver` throws.
- Log format is contract (story 011's wrapper parses it): `BOOT <n> <name> ok|FAIL|NOT WIRED …`, one line each, plus `BOOT env key=value …`.
- Slots other epics fill (each is one explicit line in `BootPlan`): step 3 ← persistence (ADR-0024); step 4 ← `InputBoot` (input-controls 028: validate `BuiltInActionMap`, read `user://input.json`, construct the router, wire sinks; the adapter node's placement — last child of the root window, ADR-0019 §3 erratum 2026-10-17 — is 028's, not the slot's); step 5/6 ← story 005; step 7 ← story 006/010; step 8 ← story 008/009. Document this table in the `BootPlan` header comment.
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

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY ran at `/story-readiness` on 2026-10-21: GAPS, fixed in this revision.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: smoke scenarios `boot-order`, `boot-failure` (exit 0), `boot-failure-exit4` (expect 4, own process), `main-tree-noboot` and the two screen-capture seams `screen-boot-error` / `screen-content-error` (debug-only; each injects one failure — (b) the `SimBuildException(ReservedPhase)` step / (c) the throwing `IContentBootSource` — leaves the screen up, and passes after 60 rendered frames, exit 0), plus the existing `main-tree` / `selftest-*` scenarios at their declared `expect=` values, and the retained boot log excerpt (engine version line included — the box runs Godot 4.7.2 mono; the owner approved 4.7.2 for local runs, see `production/qa/evidence/host-shell-gameloop-evidence.md`; 4.7.1 behaviour stays UNVERIFIED) in `production/qa/evidence/host-shell-boot-evidence.md`. The evidence file holds the actual `boot-order` log lines (all `BOOT <n> …` and `BOOT env …` lines) and the observed headless `MinSize` read-back value, not just a pointer.
- Visual (required, `.claude/docs/run-and-observe.md` Godot 4 row; per-story folder `production/qa/evidence/[story-slug]/[NN]-[what-it-shows].png`): from the repo root on the box (needs a display, e.g. the box desktop's `DISPLAY`; revert the 4.7.2 csproj rewrite afterwards):
  ```bash
  mkdir -p /tmp/ghs004-shots production/qa/evidence/godot-host-shell-story-004
  ~/.local/bin/godot --path . --windowed --resolution 1280x720 --write-movie /tmp/ghs004-shots/boot-error.png --quit-after 120 -- --smoke=screen-boot-error
  cp "$(ls /tmp/ghs004-shots/boot-error*.png | tail -1)" production/qa/evidence/godot-host-shell-story-004/01-boot-error.png
  ~/.local/bin/godot --path . --windowed --resolution 1280x720 --write-movie /tmp/ghs004-shots/content-error.png --quit-after 120 -- --smoke=screen-content-error
  cp "$(ls /tmp/ghs004-shots/content-error*.png | tail -1)" production/qa/evidence/godot-host-shell-story-004/02-content-error.png
  ```
  (Movie Maker writes `<name>00000000.png`, `<name>00000001.png`, …; take the last frame.) Reference both PNGs from `host-shell-boot-evidence.md` and record one `Run result:` line per screen. Coverage target: every step number 1–9 appears in the `boot-order` log; all three failure injections and the env / min-size lines are asserted. Plus the four Sim test legs and `tools/ci` guards stay green — `/story-done` checks the scenarios, the evidence file and both PNGs exist; pass/fail via `/smoke-check`

**Status**: [x] Created — `production/qa/evidence/host-shell-boot-evidence.md` (smoke results, raw `boot-order` log, headless `MinSize` read-back `1280x720`, `Run result:` lines), `production/qa/evidence/godot-host-shell-story-004/01-boot-error.png` and `02-content-error.png`, unit tests `tests/unit/boot/BootSequenceTests.cs`

---

## Dependencies

- Depends on: Stories 002, 003 (both Complete); `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (`BuiltInContent.CreateM1a`), `production/epics/simulation-core/story-025-composition-root-reflection-test.md` (`SimBuildException` flows from `SimComposition`) — both Complete
- Unlocks: Stories 005, 006, 007, 008, 009, 011; `production/epics/input-controls/story-028-gameloop-hookup-router-boot-settings-file.md` (boot step 4 slot, error screen)

---

## Completion Notes
**Completed**: 2026-10-21
**Criteria**: 8/8 passing.
- AC1, AC2, AC4, AC6: `boot-order` (77 assertions) and `tests/unit/boot/BootSequenceTests.cs`.
- AC3, AC5: `boot-failure` passes a–e (105 assertions), `boot-failure-exit4` (exit 4, `src/Game/Smoke/BootFailedExitSmoke.cs`) and unit tests.
- AC7: both PNGs captured windowed with `--write-movie` at 1280×720 and looked at (`Run result: OBSERVED` lines in the evidence file).
- AC8: `selftest-needs-boot` expects 0; `main-tree-noboot` (26 assertions) runs; `--smoke=list` shows the new `expect=` values.

Nothing is deferred. 4.7.1 behaviour is UNVERIFIED: every run used Godot 4.7.2 mono, which the owner approved.

**Deviations** (advisory):
- (1) **Screenshot command.** Movie Maker fixes its frame size from the project's 1600×900 window override at startup and ignores `--resolution`. The retained 1280×720 frames were captured with a temporary `override.cfg`, deleted afterwards (command in the evidence file; TD-128 item 4). The capture seams also resize the window to 1280×720 and assert it when windowed.
- (2) **`main-tree-noboot` controls.** Besides the AutoBoot=false counter (0), it has an explicit-`RequestBoot` control and an AutoBoot=true `_Ready` control. A debug seam `GameLoop.SmokeArgsIgnored` (internal) lets those second instances take the production `_Ready` path.
- (3) **Plan-construction failures.** A throw while building or checking the plan inside `RequestBoot` is reported as step 0 `plan` through `BootSequence.FailBeforeFirstStep` (`BOOT plan FAIL …`, boot error screen), the same path a failing step takes.
- (4) **Frame-fault message format.** `GameLoop`'s frame-fault message now uses the same `BootSequence.Summarize` format, which adds the `SimBuildException` code name. That changes the format for `SimBuildException` only.
- (5) **Engine-free boot core.** It lives in `src/Game/Boot/` behind `IBootWindow` (`GodotBootWindow` is the adapter) and is compiled into the xUnit project (`GodotBootWindow.cs` excluded). Because of the link, a reference from Boot code to `src/Game/Smoke/` fails the test build.

**Test Evidence**:
- Integration smokes (all at their declared exits).
- Unit tests in `tests/unit/boot/BootSequenceTests.cs`, 29 tests.
- Four test legs: 4900 tests per leg (42 skipped Debug / 2 Release), 0 failed, 0 warnings.
- Evidence file `production/qa/evidence/host-shell-boot-evidence.md` and the two PNGs under `production/qa/evidence/godot-host-shell-story-004/`.

**Code Review**: Complete.
- `/code-review` (Sonnet reviewers):
  - godot-csharp-specialist: ISSUES FOUND, advisory only.
  - godot-specialist: ADR-0019 COMPLIANT, advisory only.
  - qa-tester: GAPS, of which the only blockers were the not-yet-captured PNGs and evidence file, both now present.
  - The verified advisories were fixed in this story: the plan-failure screen, stale crefs, the `NextPass` split, the self-describing exit-4 line, `Content` assigned last, the null-error-panel warning, Quit focus, the AutoBoot=true control, and the `RequestBoot` failure pass (e).
- `/story-done` gates (Sonnet): LP-CODE-REVIEW **APPROVE**, QL-TEST-COVERAGE **ADEQUATE**.
- Remaining advisories: TD-127 (error-screen canvas layer, hard-coded strings, keyboard scroll) and TD-128 (Quit press untested, list `expect=` not asserted, the `DebuggableAttribute` gate, the screenshot `override.cfg`, story 011's `no contributor` check). The duplicated plan-failure path (TD-128 item 5) was removed by the provenance rewrite.

**Tech debt**: closed TD-120 and TD-098 item 2; added TD-127 and TD-128.

**Provenance** (2026-10-21):
- **What happened.** The first implementation attempt ran on the wrong model (a Sonnet-orchestrated session) and was discarded. The Opus implementation that replaced it used that draft as a reference. Before writing, it read the draft's `GameLoop.cs`, screen and `Main.tscn` hunks, its file list and the head of its tests. A line-overlap check then found seams, names and some lines that came from the draft.
- **What was rewritten.** On Opus, from the ACs, the handoff and the ADRs only, without reopening the draft:
  - `GameLoop.cs` boot section: re-authored from the pre-story file. It has a private boot record (attempts, result, log) exposed as `BootAttempts` / `BootResult` / `BootLines`; the debug-only seams `InstallBootStepsForSmoke(IReadOnlyList<IBootStep>)` and `BeginBootForSmoke()`; private `InstallErrorPanels` / `NewBootContext`; `BuildBootPlan`; the `BootErrorPanel` / `ContentErrorPanel` exports; and new docs. `FaultBoot` on an already-faulted loop only logs.
  - `BootFaultText.cs` deleted. The formatter is now `BootSequence.Summarize` (same `(code X)` output).
  - `BootSequence` gained `FailBeforeFirstStep`, sharing one private `Abort` path with failing steps. That removed the duplicated plan-failure code (TD-128 item 5).
  - `BootContext` re-written with a primary constructor and a new doc example.
  - `BootFailureExit4Smoke.cs` replaced by `BootFailedExitSmoke.cs`, on the new seam with a new `Start` body.
  - `BootErrorScreen.cs`, `BootError.tscn`, `ContentError.tscn` and the `Main.tscn` wiring re-authored:
    - node hierarchy `Shade` / `Layout` / `Heading`, `Where`, `Details/Message`, `Actions/QuitButton`;
    - a sink state machine (`Shown`) that routes to the paired content panel;
    - new headings "The game could not start" / "The game content could not be loaded" / "The game stopped after an error" and the "Failed at boot step n (name)." line.
- **What was renamed.**
  - Boot steps: `ArmStartStep`, `ContentBootStep`, `WindowGuardStep` → `StartGateStep`, `ContentSourceStep`, `MinimumWindowStep`.
  - Smoke support: `BootDoubles.cs` → `BootSmokeFaults.cs` (`FaultingContributor`, `FaultingStep`, `FaultingContentSource`, `BootSmokeFaults`).
  - Test doubles: `StubWindow`, `TallyingContentSource`, `BrokenContentSource`, `ScriptedStep`, `SlotProbe`, `DisplaySpy`.
  - The test-csproj link is now a labelled `ItemGroup` that uses `Link=` metadata.
- **What was kept on purpose.** The `BootPlan` step names `window`, `content`, `persistence`, `input`, `session`, `sim`, `world`, `views`, `start` and the NOT WIRED owners are a log contract that story 011's wrapper parses, so they stay as they are. `BootErrorScreen.cs` keeps the name the story dictates.
- **Verification after the rewrite.** All smokes, both screenshots, the four legs, the guards and a short Sonnet `/code-review` pass were re-run. The code review's verified advisories were applied:
  - `Showing` changes only when a panel was actually presented;
  - `FaultBoot` is first-fault-wins;
  - Quit focus is asserted on the production `RequestBoot` path;
  - the enum order in `BootErrorScreen.cs`.
