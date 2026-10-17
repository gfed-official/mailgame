# Story 018: `AimRibbonView` (Godot) — the screen-space solid / dashed / dotted arc, end markers and crosshair, drawn from the published preview

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: M (~1 day; ~450 lines prod + the evidence capture)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Presentation story (2 of 4).** The first thing a player sees of the game's signature verb: the honest arc. It draws what story 004 computed — it never recomputes a class, a colour decision or a contact. Godot work: use `~/.local/bin/godot-mono` (launch as `mise exec dotnet@10.0.402 -- ~/.local/bin/godot-mono …`; the plain `godot` is not on `PATH`).

## Context

**GDD**: `design/gdd/throwing-catching.md` (Visual/Audio "Arc preview (screen-space; thrower only)" table and "Greyscale test"; Game Feel "Press → arc visible", aim camera crosshair; T3 "Visibility", "No stale verdict"; Accessibility rows "Colour-blind (arc)", "Preview settle")
**Requirement**: `TR-throwing-017` (aim preview ≤ 0.3 ms/frame, 0 alloc, screen-space dashed ribbon), `TR-throwing-003` (the view draws the published integers)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T3-1 b/d (visual half), TC-U-3 a (legibility), TC-P-1 (draw cost), TC-FE-2 (arc visible ≤ 2 frames).)*

**ADR Governing Implementation**: ADR-0019: Godot Scene Architecture (§6 "Aim ribbon (TR-throwing-017)": a screen-space dashed ribbon drawn by one `Control` on a `CanvasLayer`; each dash a 4-point `DrawPrimitive` quad over preallocated `ReadOnlySpan<Vector2>` buffers; never `DrawLine`/`DrawPolyline`; points from the throw preview projected with `Camera3D.UnprojectPosition`; ≤ 0.3 ms per frame, hard cap 0.5 ms, 0 managed bytes) — Accepted 2026-10-16 (primary); ADR-0016 (§5 predictive visuals read published state) secondary — Accepted
**ADR Decision Summary**: The ribbon is one custom-drawn `Control` fed by `IThrowPreviewRead`, projecting the 3D polyline into preallocated screen buffers each frame and emitting dash quads with `DrawPrimitive`. `DrawPolygon`/`DrawColoredPolygon` triangulate on the CPU every call and are avoided; line antialiasing was removed in 4.7, so `DrawLine`/`DrawPolyline` are banned. If the C# draw call marshals a managed allocation (verification V3), the fallback is `RenderingServer` canvas-item polygon calls or a preallocated `ArrayMesh` with surface updates — screen-space, quads and preallocated stand either way. The ribbon story decides `rendering/anti_aliasing/quality/msaa_2d` versus a feathered texture.
**ADR Version**: ADR-0019 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 .NET | **Risk**: MEDIUM
**Engine Notes**: Post-cutoff surface: 4.7 removal of CanvasItem line antialiasing; `DrawPrimitive` `ReadOnlySpan<Vector2>` overloads (VERIFIED in `GodotSharp.xml` per ADR-0019 §6); `Camera3D.UnprojectPosition` (VERIFY the 4.7.1 C# signature — GDD Open Question 12); near-plane clipping and dash phase are ours to get right. Verification V3 (0 managed bytes per frame through the chosen draw call) is **owned by this story**.

**Control Manifest Rules (Presentation layer)**:
- Required: pools and buffers pre-instantiated/preallocated at boot; steady-state frames instantiate no nodes and allocate 0 B in the ribbon (ADR-0019 §5, Implementation Guidelines)
- Required: presentation time from `IRenderClock`; predictive components never start an outcome beat (ADR-0016)
- Forbidden: `DrawLine`/`DrawPolyline` in the ribbon or HUD; any ballistic, sweep, class or colour decision in Godot (call/read the Sim); mutating the Sim from a view; feeding a Presentation value into the Sim
- Guardrail: ≤ 0.3 ms per frame (hard cap 0.5 ms), 0 managed bytes per frame (`GC.GetAllocatedBytesForCurrentThread` over 600 frames)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` Visual/Audio and TC-T3-1 b/d, TC-U-3 a, TC-P-1, TC-FE-2, scoped to the M-1a classes. Hat pins (M0), Force-patterns sizing and the reduced-motion cut (Later) are deferred:*

- [ ] **Line patterns.** Core line 6 px with a 2 px Charcoal outline each side, constant at any distance (screen-space). **Accept** = Go Green `#3DBF5A`, solid. **Misdeliver** = Stamp Red `#D7263D`, dashed 18 px on / 10 px off, square caps. **Ground, Reject, Water, Belt, Blocked, Timeout** = Chalk White `#F4F1EA`, dotted (6 px dots, 14 px pitch). All dashes and dots are **static** (dash phase anchored at the launch end in screen arc length — no crawl).
- [ ] **End markers (≥ 24 px, 2 px Charcoal outline).** Accept = hollow ring (5 px stroke) with ✓; Misdeliver = heavy X; Ground = flat disc, no glyph; Reject = disc with a Charcoal bar "—"; Water = wave-bottomed disc with "≈"; Belt = chevron-notched disc with ">" (drawable now, data arrives M-1b); Timeout = disc with a clock; Blocked = disc at the contact point with the line truncated there. Inner glyphs ≥ 12 px. The ring is exclusive to Accept; green and red appear only for mailboxes.
- [ ] **Timeout fade.** The Timeout line fades from full to 0 alpha over its last 0.5 s of flight (the last 15 polyline points).
- [ ] **TC-T3-1 d (visual half).** On a class change, line colour, pattern, marker shape and glyph change on the **same frame** the new `ThrowPreviewState` is presented (never the previous verdict); with `previewSettleMs` > 0 the marker is the neutral pending ring (Charcoal-outlined, no glyph, no colour) for the settle window (`PreviewDisplay.PendingMarker`, story 005).
- [ ] **Geometry correct from frame 1.** The arc is drawn from the first `Present` after `Aiming` becomes true; the 0.08 s fade-in is alpha only (TC-FE-2: arc visible ≤ 2 rendered frames after the press, measured on the scripted replay corpus where available).
- [ ] **Per-frame projection.** Each rendered frame re-projects the last published 3D polyline through the current `Camera3D` (`UnprojectPosition`), so the line follows the camera at frame rate while the class stays tick-exact; points behind the near plane are clipped (the segment is cut at the plane crossing), never wrapped or mirrored; an off-screen end marker is clamped to the safe inset with its rect still published.
- [ ] **Crosshair.** A 4 px Chalk White dot with a Charcoal outline marks camera forward only while aiming.
- [ ] **Protected rect.** The marker's screen rect is published every aiming frame through `IProtectedRectSink` (HUD H11, defined in Client.Core; the HUD epic does not exist yet, so a null sink is a counted no-op, not an error).
- [ ] **Thrower only.** The view is attached to the local player's presentation root; it is never instantiated for remote peers, and no state about another player's aim reaches it.
- [ ] **TC-P-1 (draw cost) / V3.** Over 600 frames of aiming while walking with a pad, the view's `_Draw` ≤ 0.3 ms p95 (max ≤ 0.5 ms, ADVISORY) and allocates **0 managed bytes per frame** (BLOCKING). The measured allocation through `DrawPrimitive` is recorded as the V3 result; if non-zero, switch to the `RenderingServer`/`ArrayMesh` fallback in this story and re-measure.
- [ ] **No banned calls.** A CI text check fails any `DrawLine`/`DrawPolyline` in `src/Game/**`; the AA decision (`msaa_2d` setting or feathered texture) is recorded in the evidence file with a before/after screenshot of a dashed edge.
- [ ] **TC-U-3 a (legibility).** At 1080p and 20 m, in greyscale and protan/deutan/tritan simulation, on grass, sand, road and a Chalk White wall, while panning at 90°/s: each line is identifiable by pattern alone (solid / dashed / dotted) and each marker by silhouette alone (ring / X / wave disc / chevron disc / plain disc); ≥ 4/5 testers name each of Accept, Misdeliver, Ground, Reject, Water, Belt and 5/5 tell hit vs miss vs neutral. (Label contrast, TC-U-3 b, is story 019.)

---

## Implementation Notes

*Derived from ADR-0019 §5–§6 and GDD Visual/Audio:*

- Files in `src/Game/Views/Throwing/`: `AimRibbonView.cs` (`partial class : Control`, `_Draw` after `QueueRedraw()` each aiming frame), `RibbonGeometry.cs` (pure C#: projection clipping and dash/dot generation into `Vector2[]` buffers sized for 91 points × the maximum quad count; testable without Godot), `MarkerShapes.cs`, `AimLayer.tscn`. Colour constants come from one `ArtBiblePalette` static (no literals scattered); hex from `design/art/art-bible.md` (Stamp Red `#D7263D`, Go Green `#3DBF5A`, Chalk White `#F4F1EA`, Charcoal `#23252B`).
- The view reads `IThrowPreviewRead` (story 004) and `PreviewDisplay` (story 005) from the `Present(alpha)` call that `PresentationRoot` hands the World/Hud presenters (ADR-0019 §2); it owns no timers. The `CanvasLayer` index is chosen with the HUD (ADR-0018 is not written): use a dedicated `AimLayer` below the HUD stack and record the index in the evidence file.
- Dashes are generated in screen arc length from the projected polyline; compute the cumulative length once per frame into a preallocated buffer. Keep `RibbonGeometry` free of Godot types so xUnit can assert dash counts, near-plane clipping and the 0-alloc claim; the Godot part only issues the draws.
- Capture: a scripted fixture scene (`res://src/Game/Views/Throwing/Fixtures/AimRibbonFixture.tscn`) pins the camera over the M1aStreet and saves one screenshot per class into `production/qa/evidence/throwing/aim-ribbon/`. If no display or `xvfb` is available on the machine, record "NOT RUN — no display" in the evidence file; never mark the story evidenced without screenshots.
- Verification items to record in the evidence file: `Camera3D.UnprojectPosition` C# signature on 4.7.1; V3; the AA decision; the greyscale pass.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 019: the two-line label pill and reason strip (Blocked on the UX spec). Story 004/005: all decisions this view draws. Story 020/021: the in-flight parcel, trail, ghost and beats.
- Hat pins (M0), goon pins (never), reduced-motion opacity cut, Force-patterns ×1.5, haptics (Later).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshots in `production/qa/evidence/throwing/aim-ribbon/` (one per M-1a class, plus the greyscale and CVD-simulated sets and the AA before/after) and sign-off in `production/qa/evidence/throwing/aim-ribbon-evidence.md` by art-director (visual) and gameplay-programmer (feel)
- Logic half: `tests/unit/throwing/RibbonGeometryTests.cs` (dash counts, near-plane clip, 0 B) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 004, 005; `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (greybox scene root), `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md` and `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (`PresentationRoot`/`Present` wiring, `HudPresenter.AddLayer`); `production/epics/player-character/story-023-camera-rig-node-focus-cone.md` (`Camera3D` rig); `production/epics/player-character/story-026-interaction-prompt-anchor-projection.md` (the same screen-projection pattern)
- Unlocks: Story 019 (marker rect); TC-U-3 playtest evidence
