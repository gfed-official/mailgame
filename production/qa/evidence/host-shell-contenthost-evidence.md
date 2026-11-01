# Evidence: godot-host-shell story 007 — minimal `ContentHost : IContentSource` and the opt-in host content mode

**Story**: `production/epics/godot-host-shell/story-007-contenthost-minimal-iconentsource.md` · **Type**: Integration
**Date**: 2026-11-01 (Mail Dev, `/dev-story`; smoke table and the loader-`Failed` section rerun 2026-11-01 for Mail Review r1 B1 on commit 28e5c33, all 7 scenarios plus `list` with the same results except `content-host-read`, 26 → 38). Every run below is from the final working tree.
**Engine used for these runs**: `Godot Engine v4.7.2.stable.mono.official.ed1daf0bf` (`~/.local/bin/godot`, headless). The project pins 4.7.1; the owner approved 4.7.2 for local runs. **4.7.1 behaviour is UNVERIFIED**, and so is the exported-PCK C# read (ADR-0019 V1, export story): these runs read `res://` and `user://` from the editor binary only.
**.NET**: SDK 10.0.402 (`~/.dotnet/dotnet`) for the builds and test legs; `smoke.sh` builds the game with `~/.dotnet10`; the Godot process ran on .NET runtime 10.0.13 (`BOOT env dotnet_version`).
**Procedure**: `bash /workspace/repos/logs/smoke.sh <worktree> content-host-read content-host-missing-manifest boot-order boot-failure boot-failure-exit4 selftest-needs-boot main-tree list` imports the project headless (the import also wrote the new scripts' `.uid` files), builds `PerformativeMail.Game.csproj` Debug, runs each `--smoke=<name>` headless (`godot --headless --path . -- --smoke=<name>`) and reverts the 4.7.2 csproj rewrite. The direct runs below are separate commands from the same tree.

## Smoke summary (final run, exit code per scenario)

| Scenario | Expected exit | Actual | Result line |
|---|---|---|---|
| `content-host-read` (new; 38 checks, 12 of them the corrupt-fixture case added in Mail Review r1 B1) | 0 | 0 | `SMOKE RESULT name=content-host-read pass=38 fail=0 exit=0` |
| `content-host-missing-manifest` (new; its sub-run exits 4) | 0 | 0 | `SMOKE RESULT name=content-host-missing-manifest pass=23 fail=0 exit=0` |
| `boot-order` (regression: default source unchanged) | 0 | 0 | `SMOKE RESULT name=boot-order pass=77 fail=0 exit=0` |
| `boot-failure` | 0 | 0 | `SMOKE RESULT name=boot-failure pass=112 fail=0 exit=0` |
| `boot-failure-exit4` | 4 | 4 | `SMOKE RESULT name=boot-failure-exit4 pass=0 fail=1 exit=4` |
| `selftest-needs-boot` (no `--content`) | 0 | 0 | `SMOKE RESULT name=selftest-needs-boot pass=1 fail=0 exit=0` |
| `main-tree` | 0 | 0 | `SMOKE RESULT name=main-tree pass=43 fail=0 exit=0` |
| `list` | 0 | 0 | 18 `SMOKE LIST` lines (below; the two new scenarios are `expect=0`) |

## The exit-4 sub-run (AC 3, failure arm)

`content-host-missing-manifest` launches `OS.GetExecutablePath() --headless --path <res:// globalized> -- --smoke=selftest-needs-boot --content=host` with `OS.Execute` and asserts exit 4. That run takes the production `GameLoop.RequestBoot()` path, where `BuildBootPlan` reads `--content=host` from the user args. The same run, made directly:

Command: `~/.local/bin/godot --headless --path . -- --smoke=selftest-needs-boot --content=host` → **exit 4**

```
[ContentHost] load res://content: Failed in 70.6 ms (advisory budget 100 ms), 1 diagnostic(s)
BOOT 2 content FAIL InvalidOperationException: The host content in res://content did not load (1 diagnostic(s)): ContentHostReadException: cannot read res://content/manifest.txt: FileNotFound (Godot Error 7)
SMOKE selftest-needs-boot FAIL boot failed: GameLoop.RequestBoot() returned false (BOOT 2 content FAIL InvalidOperationException: The host content in res://content did not load (1 diagnostic(s)): ContentHostReadException: cannot read res://content/manifest.txt: FileNotFound (Godot Error 7))
SMOKE RESULT name=selftest-needs-boot pass=0 fail=1 exit=4
```

Env opt-in: `PM_CONTENT=host ~/.local/bin/godot --headless --path . -- --smoke=selftest-needs-boot` → **exit 4**, `SMOKE RESULT name=selftest-needs-boot pass=0 fail=1 exit=4`.

Default unchanged: `~/.local/bin/godot --headless --path . -- --smoke=boot-order` (exit 0) logs
`BOOT 2 content: source=BuiltInContent containers=4 items=4 mail_kinds=5 destination_types=1 mail_mixes=1 hash=10d2358b load_ms=49.6 (advisory budget 100)`.

## Loader-`Failed` arm (AC 2, Mail Review r1 B1)

`content-host-read` copies `content/**` a second time to `user://content-host-fixture-corrupt/` and changes `"rank.water": 7,` to `9` in `balance/throw.jsonc` (hard range 0..7). The file reads and parses; the loader rejects it with an Error and returns no set.

```
SMOKE content-host-read PASS corrupt fixture: Load() ends == Failed
SMOKE content-host-read PASS corrupt fixture: State == Failed
SMOKE content-host-read PASS corrupt fixture: Set is null == True
SMOKE content-host-read PASS corrupt fixture: Result present == True
SMOKE content-host-read PASS corrupt fixture: Result.Set is null == True
SMOKE content-host-read PASS corrupt fixture: diagnostics present (balance/throw.jsonc:36:3: error CV-GEN-13: value 9, allowed 0..7) == True
SMOKE content-host-read PASS corrupt fixture: a diagnostic line names balance/throw.jsonc == True
SMOKE content-host-read PASS corrupt fixture: load line '[ContentHost] load user://content-host-fixture-corrupt: Failed in 2.3 ms (advisory budget 100 ms), 1 diagnostic(s)' logs Failed == True
SMOKE content-host-read PASS corrupt boot source: Load() throws InvalidOperationException == True
SMOKE content-host-read PASS corrupt boot source: message 'The host content in user://content-host-fixture-corrupt did not load (1 diagnostic(s)): balance/throw.jsonc:36:3: error CV-GEN-13: value 9, allowed 0..7' counts 1 diagnostic(s) == True
SMOKE content-host-read PASS corrupt boot source: message carries the line naming balance/throw.jsonc == True
SMOKE content-host-read PASS corrupt boot source: host Set stays null (no built-in fallback) == True
```

Mutation proof (each applied alone on 70d8c44, the game rebuilt, `content-host-read` rerun, then reverted with `git checkout`; `git diff src/Game/Content/ContentHost.cs` empty afterwards):

| Mutation | Result |
|---|---|
| `Diagnostics = ImmutableArray<string>.Empty;` (the diagnostics line, `:170` before the TD-207 labels) | killed: pass=34 fail=4 exit=1 |
| `State = ContentHostState.Valid;` (the state line, `:172` before the labels) | killed: pass=35 fail=3 exit=1 |

The cold fixture load in this rerun logged 149.9 ms (`OVER`), advisory only (TD-207 item 7).

## Load time against the 100 ms advisory (TR-content-028, logged, not gated)

The fixture load (11 files, `user://content-host-fixture`) logged **93.5 ms** on the first run of the session and **178.6 ms (`OVER`)** on the final run, which is the first `ContentLoader` construction and load in a cold process (JIT plus the schema self-check). The built-in path's step-2 `load_ms` in the same tree is 49.6 ms. The budget is advisory per the story: the line is logged and nothing fails on it. Whether a cold host load must meet 100 ms is not decided here (TD-207 item 7).

## R10 grep guard (approved by Evan 2026-11-01 (TD-204 item 6))

- `bash tools/ci/check-scene-rules.sh` → exit 0, `PASS  [R10] no GetAsText/GetFileAsString/DirAccess under src/Game/Content/ (ADR-0019 section 4)`, `check-scene-rules: checked 11 rules`.
- `bash tools/ci/check-scene-rules.sh --self-check` → exit 0, `clean layout accepted, 30 planted violations detected` (R10 plants: `GetAsText`, `GetFileAsString`, `DirAccess`, `get_as_text` in a `.gd`, `src/Game/Content/` absent; and `DirAccess` under `src/Game/Smoke/` is accepted).
- A seeded real violation, `src/Game/Content/SeededViolation.cs` calling `Godot.FileAccess.Open(...).GetAsText()`, made the real run fail: `FAIL  [R10] … SeededViolation.cs:5: …GetAsText();`, `check-scene-rules: 1 of 11 rules FAILED`, exit 1. The seed was deleted, and the rerun exited 0 with 11 rules checked.

## `--smoke=list`

```
SMOKE LIST boot-failure expect=0
SMOKE LIST boot-failure-exit4 expect=4
SMOKE LIST boot-order expect=0
SMOKE LIST content-host-missing-manifest expect=0
SMOKE LIST content-host-read expect=0
SMOKE LIST fault-stop expect=0
SMOKE LIST frame-alloc expect=0
SMOKE LIST frame-order expect=0
SMOKE LIST main-tree expect=0
SMOKE LIST main-tree-noboot expect=0
SMOKE LIST pause-driver-state expect=0
SMOKE LIST screen-boot-error expect=0
SMOKE LIST screen-content-error expect=0
SMOKE LIST selftest-empty expect=3
SMOKE LIST selftest-fail expect=1
SMOKE LIST selftest-needs-boot expect=0
SMOKE LIST selftest-throws expect=1
SMOKE LIST selftest-timeout expect=5
```

## All `SMOKE` lines of the two new scenarios (final run)

```
SMOKE content-host-read PASS TryRead(project.godot) returns == True
SMOKE content-host-read PASS project.godot bytes are non-empty == True
SMOKE content-host-read PASS project.godot starts with '; Engine configuration file' == True
SMOKE content-host-read PASS project.godot bytes (952) equal File.ReadAllBytes (952) == True
SMOKE content-host-read PASS open error checked once for project.godot == (res://project.godot, Ok)
SMOKE content-host-read PASS TryRead(does_not_exist.bin) returns == False
SMOKE content-host-read PASS TryRead(does_not_exist.bin) bytes length == 0
SMOKE content-host-read PASS open error checked once for does_not_exist.bin == (res://does_not_exist.bin, FileNotFound)
SMOKE content-host-read PASS TryRead(content) (a folder) throws ContentHostReadException == True
SMOKE content-host-read PASS the exception carries the path == res://content
SMOKE content-host-read PASS the exception carries the open error the seam saw == FileCantOpen
SMOKE content-host-read PASS folder read: cannot read res://content: FileCantOpen (Godot Error 12)
SMOKE content-host-read PASS fixture: state before Load == Unloaded
SMOKE content-host-read PASS fixture: ListManifest keeps the 11 CRLF lines in order, blank line skipped == balance/delivery.jsonc,balance/inventory.jsonc,balance/mail.jsonc,balance/player.jsonc,balance/sim.jsonc,balance/throw.jsonc,items/containers.jsonc,items/core.jsonc,mail/destinations.jsonc,mail/kinds.jsonc,mail/mix.jsonc
SMOKE content-host-read PASS fixture: Load() ends == Valid
SMOKE content-host-read PASS fixture: State == Valid
SMOKE content-host-read PASS fixture: Set present == True
SMOKE content-host-read PASS fixture: diagnostics () == 0
SMOKE content-host-read PASS fixture: one load line logged == 1
SMOKE content-host-read PASS fixture: load line '[ContentHost] load user://content-host-fixture: Valid in 178.6 ms (advisory budget 100 ms, OVER), 0 diagnostic(s)' names the state and the 100 ms advisory budget == True
SMOKE content-host-read PASS fixture: Load() while Valid is refused: threw InvalidOperationException (ContentHost user://content-host-fixture is Valid; the load is refused (no re-parse, CDD-LOAD-4).)
SMOKE content-host-read PASS fixture: State after the refused load == Valid
SMOKE content-host-read PASS fixture: same set after the refused load == True
SMOKE content-host-read PASS fixture: no second load line (no re-parse) == 1
SMOKE content-host-read PASS boot step 2: BOOT 2 content line names source=ContentHost == True
SMOKE content-host-read PASS boot step 2: BootContext.Content is the host's set == True
SMOKE content-host-missing-manifest PASS precondition: FileExists(res://content/manifest.txt) == False
SMOKE content-host-missing-manifest PASS precondition: Main/Screens/ContentError is wired == True
SMOKE content-host-missing-manifest PASS --content=host selects ContentHost at res://content == True
SMOKE content-host-missing-manifest PASS PM_CONTENT=host selects ContentHost == True
SMOKE content-host-missing-manifest PASS no opt-in selects BuiltInContent == BuiltInContent
SMOKE content-host-missing-manifest PASS BootSequence.Run returns == False
SMOKE content-host-missing-manifest PASS BOOT 2 content FAIL line == True
SMOKE content-host-missing-manifest PASS ContentHost.State == Failed
SMOKE content-host-missing-manifest PASS ContentHost diagnostics == ContentHostReadException: cannot read res://content/manifest.txt: FileNotFound (Godot Error 7)
SMOKE content-host-missing-manifest PASS failure recorded as step 2 content == 2,Content
SMOKE content-host-missing-manifest PASS no ContentSet placed == True
SMOKE content-host-missing-manifest PASS BOOT 3 line logged (a later step ran) == False
SMOKE content-host-missing-manifest PASS loop faulted, no frame sequence installed (Sim never composed) == True,False
SMOKE content-host-missing-manifest PASS Main/Screens/ContentError visible == True
SMOKE content-host-missing-manifest PASS content error title == The game content could not be loaded
SMOKE content-host-missing-manifest PASS content error label names res://content/manifest.txt == True
SMOKE content-host-missing-manifest PASS screen text: The game content could not be loaded | Failed at boot step 2 (content). | InvalidOperationException: The host content in res://content did not load (1 diagnostic(s)): ContentHostReadException: cannot read res://content/manifest.txt: FileNotFound (Godot Error 7)
SMOKE content-host-missing-manifest PASS sub-run --smoke=selftest-needs-boot --content=host exit code == 4
SMOKE content-host-missing-manifest PASS sub-run result line == True
SMOKE content-host-missing-manifest PASS sub-run runner FAIL line names BOOT 2 content FAIL and res://content/manifest.txt == True
SMOKE content-host-missing-manifest PASS sub-run logged the Failed host load == True
SMOKE content-host-missing-manifest PASS sub-run logged a BOOT 3 line == False
SMOKE content-host-missing-manifest PASS sub-run: SMOKE selftest-needs-boot FAIL boot failed: GameLoop.RequestBoot() returned false (BOOT 2 content FAIL InvalidOperationException: The host content in res://content did not load (1 diagnostic(s)): ContentHostReadException: cannot read res://content/manifest.txt: FileNotFound (Godot Error 7))
```

## Run result

`Run result: N/A — Integration story with no new screen; the content error screen it reaches is story 004's (screenshot production/qa/evidence/godot-host-shell-story-004/02-content-error.png). The headless content-host-missing-manifest run asserts it is visible, its title, and that its label names res://content/manifest.txt.`
