# Spike - ADR-0019 (Godot scene architecture): OQ1 / S4 / S1

- **Date:** 2026-10-16. Throwaway; nothing here is production code. Own `project.godot` (not the repo root's).
- **Env:** Godot `4.7.1.stable.mono.official.a13da4feb` (`~/.local/bin/godot-mono`), .NET SDK 10.0.402 via mise (runtime 10.0.13), Linux x86_64, **no export templates** (`~/.local/share/godot/export_templates/` empty). Repo-root `Directory.Build.props` inherited.
- **Project:** `SpikeAdr0019.csproj` (Godot.NET.Sdk/4.7.1, net8.0), `nuget.config` copied from spike-godot-dotnet, autoload `GameLoopAuto`, scenes `scenes/*.tscn`, `views/`, `content/`, `export_presets.cfg`, `addons/spikeplug` (editor plugin used only to save a resource in an editor context), `SpikeAdr0019.sln` (needed by export, see S1-3). Raw logs in `logs/`.
- **Build:** `cd <spike>; mise exec dotnet@10.0.402 -- bash -c '~/.local/bin/godot-mono --headless --path . --build-solutions --quit'`
- **Run:** `... --headless --path . --quit-after 100000 [res://scenes/X.tscn] -- <user args>`

## OQ1 - GameLoop placement (`logs/oq1-*.log`)

`--oq1` run (autoload `GameLoopAuto`, main scene `MainRoot` with children `ChildA`, `ChildB`, all priority 0):

- Order: `_EnterTree`: GameLoopAuto, MainRoot, ChildA, ChildB. `_Ready`: GameLoopAuto, ChildA, ChildB, MainRoot. All on frame 0.
- At `GameLoopAuto._Ready`, `GetTree().CurrentScene` already is MainRoot (autoload `_Ready` runs after the scene is instanced and entered, before the scene's `_Ready`).
- `_Process` order every frame: GameLoopAuto, MainRoot, ChildA, ChildB (equal priority = tree order; autoloads are earlier root children).
- Autoload `ProcessPriority = int.MinValue`: same order (it was already first). Scene root priority -5, autoload 0: MainRoot runs **before** the autoload, children (prio 0) after it. Scene root -5 + autoload MinValue: autoload first. Scene root +5: root last.
- `GetTree().ChangeSceneToFile`: autoload survives; `Root` children before = `GameLoopAuto, MainRoot`; after = `GameLoopAuto, OtherRoot` (`CurrentScene=OtherRoot`). The change is deferred: new scene's `_EnterTree/_Ready` ran later in the same frame counter value; its first `_Process` was next frame.

## S4 - ViewCatalog resource (`logs/s4-*.log`, `catalog_editor.tres`)

- `[Export] string[] DefIds` and `[Export] PackedScene[] Views` on a `[GlobalClass] partial class : Resource` both work; saved as `PackedStringArray(...)` and an untyped `[ExtResource(...)]` array; reload gives usable `PackedScene`s that instantiate.
- Saved from a **headless non-editor run** (`ResourceSaver.Save`): `.tres` stores **no `uid://`** (neither for the scenes nor the script), and new scenes saved by code also get no uid header. Headless `--editor --quit` / `--import` do not assign uids to such scenes. `ResourceSaver.SetUid(path, ResourceUid.CreateId())` writes a uid header; but the catalog saved afterwards from a non-editor run still has no uid refs.
- Saved from an **editor context** (EditorPlugin `_EnterTree` in `--headless --editor`, env `SPIKE_EDITOR_MODE=makecatalog`): `.tres` stores `uid="uid://..."` plus `path=` on every ext_resource (and a uid for the catalog itself).
- Rename `views/b.tscn` -> `views/b_renamed.tscn` (header uid kept), catalog untouched:
  - without rescan: load FAILS (`Cannot open file 'res://views/b.tscn'`, catalog null);
  - after `--headless --editor --path . --quit` rescan: load OK, `view[1] path=res://views/b_renamed.tscn`; catalog file byte-identical. A catalog saved without uids (path-only) breaks on rename (seen in the first export attempt).
- Selected-resource export of the catalog (see S1-4) pulls both view scenes via dependency tracking.

## S1 - content packaging and defaults (`logs/s1-*.log`, `*.files`)

- `--export-pack` works **without export templates**. It needs a `.sln` next to the csproj, else it logs "no solution file" errors and a C# exception per script (and exits 0 anyway); with `SpikeAdr0019.sln` it is clean. The pck stores `.cs` sources and **no assembly**.
- Preset `Linux` (`all_resources`, `include_filter="*.jsonc, *.txt"`): pck has `res://content/test.jsonc` and `res://content/manifest.txt`. Preset `LinuxNoInc` (same, no include_filter): `content/*` absent (neither file packed). Preset `LinuxSel` (`export_filter="resources"`, `export_files=[catalog_editor.tres]`): pck has catalog, `a`, `b_renamed` (as .scn), `ViewCatalogSpike.cs`, `GameLoopAuto.cs` (autoload), project.binary; no `content/`.
- Read-back from pack (cwd `/tmp/spike-adr0019`, no project dir): **GDScript** reader: `test.jsonc` 43 bytes, `manifest.txt` 12 bytes, `get_open_error`=0 (OK). **C# reader FAILED**: `Cannot instantiate C# script because the associated class could not be found`; autoload C# fails too (no assembly in a bare `--main-pack` run). Same C# reader reads both files fine in-project (43/12 bytes, `Ok`).
- Project defaults in this hand-written 4.7.1 project: `display/window/stretch/mode = disabled`, `aspect = keep` (engine defaults, NOT the new-project canvas_items/expand).

## Not verified
- Full export (needs templates): whether a C# assembly is shipped/loaded from an exported build; reading content from an exported C# game.
- Editor-run (non-headless) autoload ordering; Windows/macOS.
- `--editor --quit-after N` segfaulted at shutdown once (output still produced); not investigated.
