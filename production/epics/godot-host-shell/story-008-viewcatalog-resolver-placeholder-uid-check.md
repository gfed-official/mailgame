# Story 008: `ViewCatalog` resource, `ViewCatalogResolver`, the placeholder view with a warning, editor-saved catalogs and the uid CI check (boot step 8)

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M (~1 day; ~300 lines prod + tests + 3 catalogs + scene)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD28 presentation lookups stay out of the Sim: a typed `ViewCatalog` resource keyed by def-id strings, resolved at boot to arrays indexed by `DefIndex`; a def with no entry gets the placeholder and a warning; CDD-SCHEMA-7 "debug and release boots both reach `Valid` with the placeholder view and one warning … the content hash is unchanged")
**Requirement**: `TR-content-027` (Presentation `ViewCatalog` keyed by def id → `DefIndex` arrays; placeholder on missing view — the implementation half; ADR-0010 + ADR-0019)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. The uid CI check and the editor-save rule are ADR-0019 §5 obligations under the same TR.)*

**ADR Governing Implementation**: ADR-0019: Godot Presentation Scene Architecture (§5 `ViewCatalog`: `[GlobalClass] partial class ViewCatalog : Resource` with parallel `string[] DefIds` / `PackedScene[] Scenes` and a `PackedScene Placeholder`; catalogs **must be created and saved in the editor**; resolution at boot by `ViewCatalogResolver.Resolve(ContentSet)`; placeholder + warning in debug and a content error in release exports; array-length mismatch an error in every build; read-only at runtime, never `Duplicate`d) — Accepted 2026-10-16 (primary); ADR-0010 (§Decision / Consequences: the contract — keyed by def id, resolved to `DefIndex` arrays at boot, hash unaffected) — Accepted 2026-10-16 (secondary)
**ADR Decision Summary**: One editor-saved catalog `.tres` per def table that has views, under `res://src/Game/Views/Catalogs/`. The editor writes `uid://` on every ext_resource, so renames survive (spike S4-3). A CI text check fails any `Catalogs/*.tres` ext_resource line without `uid="uid://`. At boot each `DefIds[i]` is mapped through the def table's id → `DefIndex` into a `PackedScene[] byDefIndex`; missing entries get the placeholder plus a warning (debug) or a content error (`!OS.IsDebugBuild()`); entries with no def warn; length mismatch always errors. Catalogs are never mutated or duplicated at runtime. Content hash is unaffected.
**ADR Version**: ADR-0019 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 .NET | **Risk**: HIGH
**Engine Notes**: Spike S4 (`prototypes/spike-adr0019/REPORT.md`): S4-1 `[Export] string[]` + `PackedScene[]` on a `[GlobalClass]` C# `Resource` save, load and instantiate; **S4-2 a catalog saved from a runtime (non-editor) run stores paths only, no `uid://` — saved from an editor context (the spike used an `EditorPlugin` `_EnterTree` under `--headless --editor`) every ext_resource carries `uid=`**; S4-3 after a rename plus editor rescan the uid-bearing catalog still resolves, a path-only catalog breaks; S4-4 selected-resources export pulls the catalog's scenes through dependency tracking (export itself is out of scope here). `.uid` sidecars are committed with their sources; `.godot/` is ignored. Scenes use the 4.6+ format (no `load_steps`). **Never `ResourceSaver.Save` a catalog from game code.** `Resource.Duplicate(true)` semantics changed in 4.5 — catalogs are never duplicated at all. The exact C# name of the post-4.5 deep-duplicate API is not needed and not used.

**Control Manifest Rules (Presentation)**:
- Required: `ViewCatalog` `.tres` files saved by the editor (uid-bearing); the CI uid check passes; `.uid` files committed (ADR-0019 Implementation Guidelines)
- Required: placeholder + one warning per missing id in debug; content error in release exports; array-length mismatch an error in every build (ADR-0019 §5)
- Forbidden: Sim content in a Godot `Resource` (the catalog holds scenes and id strings only); `ResourceSaver.Save` of a catalog from game code; `Duplicate` of any kind on a catalog; `Resource.duplicate(true)` expecting a deep copy (ADR-0010 §3, ADR-0019)
- Guardrail: catalog resolution at boot is small (advisory; logged); nothing instantiates a node here — pools are story 009

---

## Acceptance Criteria

*From CD28, CDD-SCHEMA-7 and ADR-0019 §5 / Validation Criteria, scoped to this story. Delta: no `ViewCatalog` exists; M-1a content (`BuiltInContent`) has Items `axe, fiber, log, plank`, MailKinds `large, letter, medium, postcard, small`, DestinationTypes `house` — all start with **no art**, so every def resolves to the placeholder.*

- [ ] **Pure mapping (Godot-free, unit-tested).** `ViewMapping.Map<T>(defIdsInDefIndexOrder, catalogIds, catalogViews, placeholder, release)` in `PerformativeMail.Client.Core.Presentation` returns the per-`DefIndex` array plus a list of findings. Rows: (a) every def has an entry → no findings, each index holds its view; (b) a def with no entry → placeholder at that index and one `MissingView(defId)` warning in debug, **an error** with `release = true`; (c) a catalog id with no def → one `UnknownDef(id)` warning, nothing mapped; (d) `catalogIds.Length != catalogViews.Length` → an error in **every** build, nothing mapped; (e) a duplicate catalog id → an error (story decision — the ADR is silent and duplicates are ambiguous; record in Last Updated); (f) the result array is indexed by `DefIndex` (ordinal-sorted id order) and has exactly the def count length; (g) mapping the same inputs twice gives equal results and does not touch the `ContentSet` (`ContentHash`, if present, unchanged — CDD-SCHEMA-7). Tests in `tests/unit/presentation/ViewMappingTests.cs`.
- [ ] **The Godot resource and three editor-saved catalogs.** `[GlobalClass] public partial class ViewCatalog : Resource` (`DefIds`, `Scenes`, `Placeholder`) and `ViewCatalogResolver.Resolve(ViewCatalog c, ContentSet set, DefTableId table, bool release)` (returning `ResolvedViews { PackedScene[] ByDefIndex; findings }`) exist in `src/Game/Views/`. Three catalogs — `res://src/Game/Views/Catalogs/items.tres`, `mail_kinds.tres`, `destination_types.tres` — exist, each with `Placeholder = res://src/Game/Views/Placeholder/PlaceholderView.tscn` (a `Node3D` root with a ~0.4 m magenta unshaded `MeshInstance3D` box) and empty `DefIds`/`Scenes` at M-1a. They are **saved from an editor context** (a small editor tool run under `godot-mono --headless --editor`, or the inspector by hand — never `ResourceSaver.Save` from game code); `tools/ci/check-catalog-uids.sh` (with `--self-check` seen failing on a planted path-only fixture) fails any `Catalogs/*.tres` ext_resource line lacking `uid="uid://` and prints `checked N ext_resources in M catalogs` (zero checked is a failure, not a pass).
- [ ] **Boot step 8 resolves the catalogs; missing views warn, never fail, in debug.** Step 8 logs `BOOT 8 views` with per-table `resolved/total` counts and one `VIEW warn missing view for <table>:<id> -> placeholder` line per def (10 for the M-1a content set: 4 items + 5 mail kinds + 1 destination type), uses `release = !OS.IsDebugBuild()`, stores the three `ResolvedViews` in `PresenterDeps` (story 006), and reaches `Valid` with the placeholder for every def. A smoke scenario asserts exactly 10 warnings, 0 errors, that `byDefIndex.Length` equals each table's def count, and that a `release = true` resolve of the same catalogs reports 10 errors (boot would fail) without touching the debug path.
- [ ] **Rename survival (S4-3 repeated on the real project).** Renaming `PlaceholderView.tscn` to another path, running `godot-mono --headless --editor --path . --quit` once (rescan), and re-running the smoke scenario still resolves the placeholder from `items.tres`; the rename is then reverted. The recipe and both logs are retained in the evidence file. A catalog line without a uid is shown to break the same experiment (once, on a scratch copy) so the check is known to guard something.

---

## Implementation Notes

*Derived from ADR-0019 §5, ADR-0010, CD28 and the S4 spike:*

- Files: `src/PerformativeMail.Client.Core/Presentation/ViewMapping.cs`; `src/Game/Views/ViewCatalog.cs`, `ViewCatalogResolver.cs`, `ResolvedViews.cs`, `src/Game/Views/Placeholder/PlaceholderView.tscn` (+ `.uid`), `src/Game/Views/Catalogs/*.tres` (+ `.uid`), `src/Game/Boot/ViewsBootStep.cs`, `tools/ci/check-catalog-uids.sh`, an editor-only tool under `src/Game/Tools/Editor/` (excluded from exports; `@tool`/`EditorPlugin` style as the spike's `addons/spikeplug` — read the spike, do not copy it).
- Def-id order: the resolver asks the def table for its ids in `DefIndex` order (ordinal-sorted, content-data 001) and passes them to `ViewMapping`. `DefTableId` selects which table (Items / MailKinds / DestinationTypes at M-1a); adapt to the actual `ContentSet` accessors of content-data 008.
- Per-entity copies are never `Duplicate`d from a catalog: `PackedScene.Instantiate()` is the only way a view is produced from a catalog entry (story 009).
- How other epics add art: open the catalog in the editor, append the def id string to `DefIds` and the scene to `Scenes` (both arrays, same index), save in the editor (uid written), commit the `.tres` and the scene's `.uid`. mail-destinations 016 adds `house` → its `MailboxView.tscn` to `destination_types.tres`; the grid panel's item icons and world-item meshes (grid-inventory 023, the unowned `WorldItemView` seam) use `items.tres` / `mail_kinds.tres`. Record this recipe in the story's evidence file so feature-epic developers find it.
- GDD wording differs from ADR-0019 on release behaviour: CD28 says the release-**export** check makes it an error while running builds keep the placeholder; ADR-0019 §5 says a content error in release exports (`!OS.IsDebugBuild()`). Follow ADR-0019 (Accepted, later) — M-1a runs debug builds, so the difference is invisible until the export story. Logged in the epic.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 009: instantiating from the catalog, pools, `EntityViewRegistry`, per-lot views. Story 006: carrying the resolved views in `PresenterDeps`.
- Real art/scenes for any def (feature epics add entries); `Texture2D`/audio entries in a catalog (CD28 mentions them; ADR-0019's shape is scenes only — extend when a story needs it); the release-export check itself (export story); hot reload of catalogs.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Logic half: `tests/unit/presentation/ViewMappingTests.cs` (`[Trait("Id", "CDD-SCHEMA-7")]`) — must exist and pass
- Integration half: smoke scenario `view-catalog` (`SMOKE … PASS`), `tools/ci/check-catalog-uids.sh` and `--self-check` output, and the rename experiment logs in `production/qa/evidence/host-shell-viewcatalog-evidence.md` — `/story-done` checks the test, scenario, script and evidence file exist

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 004 (boot plan), 006 (`PresenterDeps`), 002 (harness); `production/epics/content-data/story-001-defindex-stringid-deftable.md`, `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (def tables and ids)
- Unlocks: Story 009, Story 011; `production/epics/mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md` (`destination_types.tres`), `production/epics/grid-inventory/story-023-panel-grid-view-hotbar.md` (item icons by def id)
