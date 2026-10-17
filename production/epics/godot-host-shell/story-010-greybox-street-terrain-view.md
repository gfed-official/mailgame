# Story 010: Greybox street — `TerrainView` from `IWorldQuery` (untextured ground, water plane, house/roof/Post Office boxes, pad marker, light and fallback camera)

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: M (~1 day; ~350 lines prod + smoke + screenshots)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: none for World Generation (`production/epics/world-stub/EPIC.md`: consumer GDDs + ADR-0026). Consumers: `design/gdd/mail-destinations.md` R3 (the hand-authored street of 10 houses), `design/gdd/player-character-movement.md` fixture table and OQ23 (capsule vs doorway, measured on this street — a manual observation this scene makes possible), `design/gdd/throwing-catching.md` T5/T11 (water and solids as receivers)
**Requirement**: no TR-ID in the registry for terrain views — the greybox scene is this epic's own deliverable (ADR-0026 §Enables lists "ADR-0019 (terrain and mailbox views)"; ADR-0019 §2 `World/Terrain (TerrainView) — built once from IWorldQuery tables (ADR-0026) at boot step 7`). `TR-movement-007` is respected by construction (static geometry, nothing moves in `_PhysicsProcess`).
*(Registry requirements read fresh at review time; none apply directly.)*

**ADR Governing Implementation**: ADR-0019: Godot Presentation Scene Architecture (§2 `Terrain (TerrainView)` built once at boot step 7; §3 step 7 "World tables → `Terrain`/`Destinations` built") — Accepted 2026-10-16 (primary); ADR-0026: World Query Contract (`IWorldQuery`) and the M-1a Street Stub (§1 `IWorldQuery`/`WorldTables` immutable; §2 2 m bilinear int64 height field, sea level 0; §5 `StaticSolids` with `SolidKind`; §7 Post Office record; "consumers read terrain, water, solids, bounds and lots only through `IWorldQuery`") — Accepted 2026-10-16 (secondary)
**ADR Decision Summary**: The terrain view is built once, from the immutable `IWorldQuery` tables, when the world is composed — `GetCorners`, `Tile`, `Bounds`, `StaticSolids`, `Lots`, `PostOffice`. There is no Godot collider copy used for gameplay; Presentation colliders that need geometry (camera boom, picking) are a *copy of `IWorldQuery` geometry with a parity test* (control manifest), so this mesh is **visual only**. Water is the plane `y = 0` wherever terrain is below it.
**ADR Version**: ADR-0019 2026-10-16; ADR-0026 2026-10-16

**Engine**: Godot 4.7.1 .NET (windowed run needed for the screenshots; `--headless` for the structural smoke) | **Risk**: MEDIUM
**Engine Notes**: Headless runs use the dummy renderer: `ArrayMesh`/`MeshInstance3D` creation works, pixels do not — screenshots need a windowed run on a machine with a display (Forward+ renderer; hand-written project, 4.7 stretch settings from story 002). Sim mm → Godot m by `mm / 1000f` **at the view boundary only**; Sim +Y up equals Godot +Y up (ADR-0013 §3). Mesh generation uses `ArrayMesh.AddSurfaceFromArrays` with arrays built once at boot (not per frame). 4.7 removed CanvasItem line antialiasing — irrelevant here (3D only); do not add 2D overlays in this story.

**Control Manifest Rules (Presentation)**:
- Required: Presentation reads terrain, water, solids, bounds and lots only through `IWorldQuery`, never reimplementing bilinear sampling or water depth (ADR-0026 §Implementation Guidelines); the terrain is built once at boot step 7 (ADR-0019 §3)
- Forbidden: moving anything in `_PhysicsProcess`; a Godot physics body/collider as the authority for movement or throws; floats entering the Sim; hard-coding the street's coordinates (the scene is derived from the tables, so it survives M1's generated worlds)
- Guardrail: draw calls / frame budget not yet configured (technical-preferences); record the observed draw-call count and frame time at 1080p in the evidence, advisory

---

## Acceptance Criteria

*From ADR-0019 §2–§3 and ADR-0026 §1/§2/§5/§7, scoped to this story and the epic's Definition of Done ("retained screenshots … the greybox street at 1280×720 and 1920×1080"). Delta: `World/Terrain` is an empty `Node3D` (story 002); `IWorldQuery` and `M1aStreet` come from the world-stub epic; no mesh, light or lit camera exists.*

- [ ] **Ground mesh from the corner tables.** `TerrainView` (attached to `World/Terrain`, registered as an `IWorldBuildStep`, story 006) builds one `ArrayMesh` covering `TilesX × TilesZ` tiles of `TileSizeMm` (2000 mm → 2 m) from `IWorldQuery.GetCorners` (per-tile vertices so tile colours are flat): vertex heights equal the Sim's corner heights `/1000f` exactly (±1 mm), vertices carry an untextured colour by `Tile` flags (Road, Lot, Walkable, Blocked, water-hint) with a `StandardMaterial3D` using vertex colour as albedo — **no image textures anywhere** (a grep guard on `src/Game/Views/Terrain/**` for `.png/.jpg/.webp/Texture` fails the check). The build runs exactly once at boot step 7 (counter asserted), is logged with its wall time (advisory < 100 ms), and `Present` does no per-frame work. Smoke scenario `terrain-build` asserts vertex/triangle counts from the tile grid, corner-height parity at every tile corner, that the mesh AABB lies within `Bounds`, and — for the documented approximation (two planar triangles vs the Sim's bilinear surface) — that the maximum deviation at tile centres from `TerrainHeightMm / 1000f` is within the analytically expected `|h00 + h11 − h10 − h01| / 4` per tile (so the visual never lies about walkable ramps: the authored 1400 mm / 2000 mm ramp tile is planar and matches exactly).
- [ ] **Water, solids, Post Office.** A single water quad at `y = 0` spans the map bounds (translucent blue-grey, `StandardMaterial3D` alpha; the Sim's water plane, not the tile `Water` hint). Every `StaticSolid` (`House`, `Roof`, `PostOffice`, `Prop`) becomes one box `MeshInstance3D` under `World/Terrain/Solids` with extents exactly `AabbMm / 1000f`, a flat colour per `SolidKind`, named by `Index`; the count equals `IWorldQuery.StaticSolids.Length` (10 houses + 10 roofs + the Post Office at M-1a). A flat disc of `PostOffice.PadRingRadiusMm` at `PadCentre` plus `PadSlots` (8) small markers at the pad slots, and a door marker at `DoorAnchor`, show where players spawn. All geometry is derived from the tables — none of the street's coordinates appear in `src/Game`.
- [ ] **Light, environment and a fallback camera so the scene is visible before the rig exists.** `TerrainView` adds one `DirectionalLight3D` and one `WorldEnvironment` (flat sky colour, ambient light) as children of `World/Terrain`, and sets `World/CameraRig/Boom/Camera3D` to `Current` at a fallback pose derived from `PostOffice.PadCentre` (≈ 6 m back, 4 m up, looking at the pad along the street) with no `_Process`; player-character 023's `CameraRig` takes over by writing its own transform — the fallback must not fight it (the view only sets the pose once, in the build step, and logs `fallback camera pose set`).
- [ ] **Visual/Feel evidence.** Retained screenshots of the greybox street at 1280×720 and 1920×1080 (windowed `godot-mono --path . --windowed --resolution WxH`), plus 1280×800 as an observation of `keep_height` below 16:9 (ADR-0019 V4 — record what is clipped, no pass/fail), showing: ground relief, the beach/water, the ramp, ten placeholder lot markers (story 009), roofs/houses, the Post Office and pad. The evidence file records the observed draw-call count and frame time at 1080p (advisory), the machine, the renderer and a lead sign-off. If no display is available the row reads `NOT RUN — needs a windowed session`, never PASS.

---

## Implementation Notes

*Derived from ADR-0019 §2–§3, ADR-0026 §1–§7:*

- Files in `src/Game/Views/Terrain/`: `TerrainView.cs` (`Node3D`, `IInjectable<PresenterDeps>`, `IWorldBuildStep`), `TerrainMeshBuilder.cs` (pure array building — no Godot calls other than the final `ArrayMesh` assembly, so the vertex/colour logic is unit-testable if the dev wants it in Client.Core), `SolidKindColors.cs`, `PadMarkerBuilder.cs`. Scenes: none required beyond the nodes created in code; if a `.tscn` helps (light/environment), author it in the 4.6+ format and commit its `.uid`.
- Vertex colours and flags: use `IWorldQuery.Tile(tx, tz)`; `Water` is only a hint (ADR-0026 §Implementation Guidelines) — colour water-adjacent tiles by `TerrainHeightMm < 0` at the tile centre if a hint-independent look is wanted, but the water *plane* is always `y = 0`.
- Winding/normals: build normals from the triangles; double-check the winding so the ground is lit from above (a flipped normal shows as a black ground in the screenshot).
- Do not create colliders. The camera boom (player-character 021) queries `IWorldQuery` through the shared `Sim/Geometry` primitives, with a parity test against `StaticSolids` — it never raycasts this mesh.
- Coordinates: Sim `(x, y, z)` mm → Godot `Vector3(x/1000f, y/1000f, z/1000f)`; the map is ≤ 128 × 64 tiles (≈ 256 × 128 m), well inside float precision.
- `M1aStreet` leaves a doorway-sized gap for PC OQ23; the screenshot should make it visible. Measuring the capsule against it is a player-character epic observation, not an AC here.
- For the lot markers the dev-story agent should reuse story 009's placeholder (magenta boxes at the mailbox anchors); this story adds no mailbox art.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 009: per-lot (mailbox) views and pools. Story 006: `IWorldBuildStep`. Story 002: the `World/Terrain` node.
- Camera behaviour, boom solver, focus cone (player-character 021/023); the carrier (022); mailbox art, flag and house detail (mail-destinations 016); textures, props, trees, sky, water shader, post-processing, shadows tuning (art epics, ADR-0029); minimap/HUD.
- Navigation or collision from the mesh; seeded/generated worlds (M1, ADR-0026 §10).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshots in `production/qa/evidence/` (1280×720, 1920×1080, plus the 1280×800 observation) and sign-off in `production/qa/evidence/greybox-street-evidence.md`; plus the headless smoke `terrain-build` log (`SMOKE … PASS`) recorded in the same file — `/story-done` checks the screenshots, sign-off and the scenario exist

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 006 (`IWorldBuildStep`, `PresenterDeps`), 009 (lot markers, `Destinations`), 005 (`IWorldQuery` available), 002 (`World/Terrain`, `CameraRig` nodes); `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md`, `production/epics/world-stub/story-003-static-solids-4m-query.md`, `production/epics/world-stub/story-006-m1a-street-and-fixtures.md` (the street the scene renders)
- Unlocks: Story 011 (the visible boot); every Godot-side story's screenshot evidence (a street to stand on); `production/epics/player-character/story-022-playerview-scene-render-pose.md` (greybox carrier on the M-1a street)
