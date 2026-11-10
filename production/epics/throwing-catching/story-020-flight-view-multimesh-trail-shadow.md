# Story 020: `FlightView` (Godot) — the parcel in the air: one bundle mesh via `MultiMeshInstance3D`, spin from `flightId`, ×N tag pool, blob shadow and the launch-verdict trail

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Complete (greybox; shadow, trail, landed query, alpha fade-in, Timeout fade and 1 rev/s bundle spin dropped → TD-250 (7))
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: M (~1 day; ~450 lines prod + the evidence capture)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-11-09 (M-1a package cleanup: Complete (greybox; shadow, trail, landed query, alpha fade-in, Timeout fade and 1 rev/s bundle spin dropped → TD-250 (7)); delivered by PR #91, merge ae8a12d; see Completion Notes).

> **Presentation story (3 of 4).** What every peer sees between the release and the landing. Positions, ages and verdicts come from story 017's mirror; this view places meshes and strokes a trail. Godot work: use `~/.local/bin/godot-mono`.

## Context

**GDD**: `design/gdd/throwing-catching.md` (Visual/Audio "Flight (every peer)" table: Trail, Bundle tiers, ×N tag, Spin, Blob shadow; Player Fantasy "Red arc in flight … the trail stays red and dashed"; Game Feel "Remote peers"; Milestone slice "the trail; one bundle mesh")
**Requirement**: `TR-throwing-021` (spin from `flightId`; MultiMesh bundles; ×N hidden beyond 35 m), `TR-throwing-018` (a WorldItem spawn bound to a `flightId` stays hidden until that flight's visual lands — this view exposes the landed query), `TR-throwing-016` (client timeline consumption)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-VA-1 a (trail verdict; tagged M0 for the closing-box/remote case — the local half is checked here), TC-VA-1 b (**Later**, non-gating), TC-P-1 family (0 B).)*

**ADR Governing Implementation**: ADR-0019: Godot Scene Architecture (§5 "Flights: one `MultiMeshInstance3D` per bundle tier … `TransformFormat` and `Mesh` set before `InstanceCount`, once; `InstanceCount` preallocated to the flight cap; `CustomAabb` set to the map bounds; `VisibleInstanceCount` updated each frame; spin a pure function of `flightId`; the ×N `Label3D` pool sets `Billboard`, `FixedSize`, `NoDepthTest` once and writes `Text` only on change"; pool caps: ×N tags 8, hidden beyond 35 m) — Accepted 2026-10-16 (primary); ADR-0016 §6 (flight timeline) and ADR-0005 §3 (cosmetic spin has no RNG) secondary — Accepted
**ADR Decision Summary**: The scene tree has a `Flights` node holding one `MultiMeshInstance3D` per bundle tier and a pool of 8 `Label3D` ×N tags, all instantiated at boot. Per-frame work is writing instance transforms from the flight timeline and a visible count; no node is created or freed in steady state. Positions come from ADR-0016's flight timeline (`HostTick − 3` + alpha); spin is a pure function of `flightId`.
**ADR Version**: ADR-0019 2026-10-16; ADR-0016 2026-10-16; ADR-0005 2026-10-16

**Engine**: Godot 4.7.1 .NET | **Risk**: MEDIUM
**Engine Notes**: `MultiMesh` buffer updates and `CustomAabb` (GDD Open Question 12 VERIFY); `Label3D` text rebuild cost (write only on change); 4.7 removed CanvasItem line AA, so the trail uses the quad path of story 018, never `DrawLine`. The trail is a screen-space constant-width stroke (art bible §7 outline exception "arc and flight trail"); its draw method is **owned here** — reuse `RibbonGeometry` from story 018.

**Control Manifest Rules (Presentation layer)**:
- Required: pools pre-instantiated at boot within the ADR-0019 §5 caps; steady-state frames instantiate no nodes and allocate 0 B; flight spin is a pure function of `flightId`; cosmetic randomness never feeds the Sim
- Forbidden: Godot physics bodies or engine interpolation for flight positions (ADR-0015 note 2, ADR-0002); `delta`-based timing; a second ballistic; id structs passed through Godot signals; `DrawLine`/`DrawPolyline`
- Guardrail: 0 B per frame over 600 frames with 60 flights; ≤ 8 ×N tags at once

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` Visual/Audio "Flight (every peer)" and TC-VA-1, scoped to M-1a (one bundle mesh; tiers, reduced motion and Force patterns are Later):*

- [ ] **One bundle mesh, MultiMesh.** A single `MultiMeshInstance3D` (tier 1.0×; the node structure keeps room for the other three tiers) with `TransformFormat` and `Mesh` set before `InstanceCount`, once; `InstanceCount` preallocated to the flight cap (160); `CustomAabb` = map bounds; `VisibleInstanceCount` = visible flights each frame. The greybox mesh is a Kraft-coloured parcel with a 2 px Charcoal outline; the same mesh is used for every unit count at M-1a.
- [ ] **Placement from the mirror.** For each visible flight the instance transform is the lerp between the integer segment endpoints `(p(k), p(k+1))` by `AlphaPermille/1000` (story 017), plus the spin rotation; flights hidden before their render tick and removed after landing; at the same (tick, alpha) two views of the same event stream place the instance within 0 mm (same integers) — a headless test checks the pure placement function, a manual check the visual.
- [ ] **Spin.** Singles spin 2 rev/s, bundles (≥ 2 units) 1 rev/s, as a pure function of `flightId` and render ticks (story 017's `FlightSpin`), stopping at contact; two peers/instances show the same angle for the same flight and tick; no RNG is referenced.
- [ ] **×N tag.** A pooled `Label3D` (8) per stack with N ≥ 2: billboarded, Gold digits on Charcoal, ≥ 14 px, 0.4 m above the parcel, `Billboard`/`FixedSize`/`NoDepthTest` set once, `Text` written only when N changes; hidden beyond 35 m from the camera, at most 8 visible (nearest first). The 34 m / 36 m boundary check is **Later** (TC-VA-1 b) and non-gating; the 35 m rule is implemented now.
- [ ] **Blob shadow.** A pooled Charcoal 40 % disc on the ground below the flight (ground height read from the immutable `IWorldQuery`), radius 0.3 m at launch, shrinking and lightening with height, never smaller than 10 screen px; it disappears at landing.
- [ ] **Trail keeps the launch verdict.** The trail takes colour and pattern from the flight's **launch verdict** (story 017: green solid / red dashed / white dotted), frozen at release on this peer's screen — a box that closes mid-flight does not recolour it (the local half of TC-VA-1 a; the remote-peer and closing-box case is M0). Length 0.4 s of flight fading 100 → 0 %; white trails 0.25 s. Stroke is the 6 px / 2 px Charcoal-outline screen-space style of the aim ribbon, drawn through the shared quad path, for at most 32 trails (nearest first). Others see the trail only, never the aim arc, marker or label.
- [ ] **Landed query.** The view exposes `IFlightLandedQuery.HasLanded(flightId)` so a WorldItem view can keep its flight-bound item hidden until the parcel's visual lands (TR-throwing-018). A flight whose `FlightResolved` arrived but whose render time has not reached the contact keeps flying to the contact point, then reports landed; it never jumps ahead of its event.
- [ ] **0 B / budget.** 600 frames with 60 simultaneous flights allocate 0 managed bytes in the view (instance writes, tag updates, shadows, trails); no node is instantiated after boot.
- [ ] **Visual check (manual).** A scripted fixture throws a light single, a 20-stack and a wrong-house red throw across the M1aStreet; screenshots at launch, apex, and just before landing show: parcel spinning, ×20 tag, shadow shrinking, red dashed trail for the wrong-house throw.

---

## Implementation Notes

*Derived from ADR-0019 §2, §5 and GDD Visual/Audio:*

- Files in `src/Game/Views/Throwing/`: `FlightView.cs` (`Node3D` under `World/Flights`, fed by `Present(alpha)`), `FlightPlacement.cs` (pure: `(in FlightRenderState, alphaPermille, spin) → Transform parameters` as floats/ints — testable without Godot), `FlightTagPool.cs`, `FlightShadowPool.cs`, `FlightTrailView.cs` (a `Control` on the aim `CanvasLayer` family, reusing `RibbonGeometry`), `Flights.tscn`, `ParcelBundle.mesh`/material (greybox).
- The view reads `IFlightRead` (story 017) from `Present`; it never reads `SimWorld`, never calls `Ballistic` itself (the mirror/timeline does).
- The trail's point history is a ring per visible flight of the last 12 render positions (0.4 s), written from the timeline each frame into preallocated arrays.
- Kraft / Gold / Charcoal colours come from the shared `ArtBiblePalette` (story 018); Gold is the stack-count voice (Grid's), take its hex from `design/art/art-bible.md`.
- **Unowned seam (EPIC.md):** no story builds the greybox `WorldItemView` (ADR-0019 §2 lists `WorldItemView` under `Entities`); without it, a landed parcel vanishes. It belongs to the grid-inventory or godot-host-shell presentation work and must consult `IFlightLandedQuery`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 017: the mirror/timeline data. Story 018: the aim arc and shared quad geometry. Story 021: the ghost mesh, release snap pose, landing squash/dust and sounds.
- Tier meshes (1.15/1.35/1.6×), reduced-motion spin halving and fixed-ribbon trails, Force patterns (Later); catch carets/edge chevron and the FORWARDED stamp (M0).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshots in `production/qa/evidence/throwing/flight-view/` and sign-off in `production/qa/evidence/throwing/flight-view-evidence.md` by art-director (visual) and gameplay-programmer (feel)
- Logic half: `tests/unit/throwing/FlightPlacementTests.cs` (placement function, tag selection, 35 m rule, 0 B) — must exist and pass

**Status**: [x] Created and passing — `tests/unit/ui/ThrowViewLogicTests.cs` (not `tests/unit/throwing/FlightPlacementTests.cs`; no retained screenshots or sign-off), PR #91 (2026-11-09)

---

## Completion Notes

*M-1a package delivery, recorded by Mail Lead's cleanup pass 2026-11-09 (owner ruling 2026-11-08 10:18 PM PT, card t299s1348: "Yes, switch to one PR per package and update the story files in one pass at the end"). Uncredited Mail Dev detail unless a quote says otherwise.*

- **Delivered by:** PR #91 (merge `ae8a12d`), package WP-E (M-1a wave 2; S1–S3 coded on Sonnet, review fixes on Opus), merged 2026-11-09 under the merge rule "Merge on Review READY plus green CI, and only flag ADR changes to me" (Evan 2026-11-08 8:52 PM PT, card t292s1310). Wired at boot by PR #93 (`545df2f`, TD-260): `ThrowLayers.CreateFlights` under `World/Flights`, with a sampler view registered on the World presenter that samples the flight mirror each frame and passes only `InFlight` flights (the sampler reads `IRenderClock`). Fed by the client loop from PR #94 (`f145219`, TD-270): own releases are noted on the flight mirror and the loop smoke sees the projectile in flight and land (wallet 8 → 16 ¢).
- **ACs met:** none ticked — the PR #91 body records what was built, not AC verdicts. Built: one MultiMesh tier at 1.0×, preallocated to 160 instances; `TransformFormat` and `Mesh` set before `InstanceCount`, which is set once; placement is an integer lerp by `AlphaPermille`; spin is a pure function of flightId and the render tick.
- **Deferred or partly met (greybox trims, Mail Lead planning default, M-1a regroup plan §3 simplify table; held by the package TD row):** One bundle mesh — the tier and preallocation are as specified; `CustomAabb` and the Kraft parcel with Charcoal outline are MultiMesh placeholders, not confirmed → TD-250 (7). Placement — the pure placement function is tested; hide-before-render-tick and remove-after-landing rest on PR #93's `InFlight`-only sampler. Spin — the 1 rev/s bundle spin was dropped (the bundle unit count arrives with the `IFlightSource` adapter) → TD-250 (7). Dropped: the ×N tag (not mentioned in the PR body), the blob shadow, the trail, the landed query (`IFlightLandedQuery`), the alpha fade-in, the Timeout fade, the 0 B/budget measurement and the visual check → TD-250 (7). Tests live in `tests/unit/ui/`, not `tests/unit/throwing/` → TD-250 (8).
- **Tests:** `tests/unit/ui/ThrowViewLogicTests.cs` (placement lerps the integer segment and clamps to the instance cap) — PR #91; 6,445 per leg, 0 failed.

---

## Dependencies

- Depends on: Stories 017, 018; `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (`Flights` node in the greybox scene root), `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (`Present` wiring), `production/epics/godot-host-shell/story-009-entity-view-registry-pools-destination-views.md` (`PoolSpec`); `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (ground height)
- Unlocks: Story 021
