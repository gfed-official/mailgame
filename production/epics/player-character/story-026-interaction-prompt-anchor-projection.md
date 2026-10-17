# Story 026: Interaction prompt anchor — projecting the focused anchor and its bounds to screen, and the HUD focus feed

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M (~0.75 day; ~350 lines prod + tests + evidence)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Scope note (explicit, so the story is not misread as a HUD story).** This story supplies the *data* the HUD needs — the focused anchor, its projected screen position and bounds, the verb/availability/prompt args and hold progress. **The prompt widget, hold ring, lock icon, caret drawing and focus bracket are HUD & Notifications' (H11 / H12) and have no epic yet.** No `design/ux/` interaction-prompt spec exists (only `design/ux/hud-notifications-seed.md`); that is why this story is typed Integration with a debug overlay as evidence rather than a UI story, and why it is not Blocked. The widget's own story must wait for `/ux-design`.

## Context

**GDD**: `design/gdd/player-character-movement.md` (Visual/Audio "Focus highlight = HUD's focus bracket. This system supplies only the focused anchor and its bounds"; UI Requirements "Interaction prompt"; P11)
**Requirement**: `TR-movement-011` (focus published for the prompt — the Presentation consumer half); the projection/bounds feed has **no TR-ID in the registry** — cited by GDD Visual/Audio and UI Requirements, acceptance id PCM-UI-1 (data rows)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture (§2 `Hud` presenter is invoked after `World` by `PresentationRoot.Present`; views receive dependencies through `Initialize(deps)`) — Accepted 2026-10-16 (primary); ADR-0014 (§7 `IInteractionQuery` — the snapshot the HUD reads, `Presented` bookkeeping) secondary — Accepted. ADR-0018 (HUD composition) is planned and not written; it will own the widget.
**ADR Decision Summary**: The Presentation reads `IInteractionQuery.Current` and renders from it; it never reads the registry or `SimWorld`. `World` is presented before `Hud` each frame, so the projection computed in the World presenter is the current frame's. The router echoes the `Seq` of the snapshot the Presentation last *rendered* in each press edge (`PresentedSeq`), so this feed must mark which snapshot it presented.
**ADR Version**: ADR-0019 2026-10-16; ADR-0014 2026-10-16

**Engine**: Godot 4.7.1 .NET (`~/.local/bin/godot-mono`) | **Risk**: MEDIUM
**Engine Notes**: `Camera3D.UnprojectPosition` returns the point in the **viewport/canvas** coordinate space and does not tell you the point is behind the camera — use `Camera3D.IsPositionBehind` first. Under the project's `canvas_items` / `keep_height` stretch (ADR-0019 §7) the returned coordinates are in the 1080-reference space — verify in-engine against a known world point. `VisualInstance3D.GetAabb()` is local-space; transform its eight corners to world before projecting.

**Control Manifest Rules (Presentation layer)**:
- Required: presentation time/clock rules as above; HUD and in-world Controls use `FocusMode = None`; no per-frame allocation in steady state (ADR-0019, ADR-0012 adapter rows)
- Forbidden: Presentation reading `SimWorld` or the anchor registry directly; mutating the Sim; `DrawLine`/`DrawPolyline` in the HUD (manifest)

---

## Acceptance Criteria

*From GDD Visual/Audio "Focus highlight", UI Requirements "Interaction prompt" and PCM-UI-1 (data half), scoped to this story:*

- [ ] **`FocusFeed` contract.** A readonly struct `FocusFeed { bool Visible; AnchorRef Focused; Verb; InteractMode; Availability (ok | reason StringId); PromptArgs; InteractState; ushort ProgressPm; CancelReason LastCancel; Vector2 ScreenPos; Rect2 ScreenBounds; ushort PresentedSeq; byte Kind (None | Focused | TooFarCaret) }` is published each frame through `IHudFocusFeed.Current` for the HUD presenter (`Hud` runs after `World`); `PresentedSeq` equals the snapshot `Seq` it was built from and is what the router echoes in press edges.
- [ ] **Projection.** For a focused anchor in front of the camera, `ScreenPos` is `Camera3D.UnprojectPosition(anchorPointMetres)` and `ScreenBounds` is the screen-space rectangle enclosing the projected corners of the anchor entity's visual bounds (the entity's view node `GetAabb()` via `EntityViewRegistry`; fallback when the entity has no view: a 0.5 m cube around the anchor point); an anchor **behind the camera** (`IsPositionBehind`) gives `Visible = false`; a focused anchor partly off-screen clamps its bounds to the viewport and keeps `Visible = true`.
- [ ] **Unavailable and cancel data pass through.** An unavailable target keeps `Visible = true` with its one reason key (dimmed/lock rendering is the HUD's); `ProgressPm`, `InteractState` and `LastCancel` (for the drain-at-2× / "Moved out of reach" cue) are copied from the snapshot unchanged; the feed never invents a verdict (no green/red — focus must not pre-announce a match).
- [ ] **Too-far caret.** When nothing is focused and the snapshot's `NearestBeyond` anchor is 2.5–6 m away, the feed is `Kind = TooFarCaret` with the projected point of that anchor and no prompt text/args; a press that finds no valid target sets a one-frame `TooFarPulse` flag (from the snapshot's cancel/none data — "Too far" marker pulse) — drawing is the HUD's.
- [ ] **Perk-up hook (cosmetic).** The focused entity's view node receives a `SetFocused(bool)` call on focus change (a mailbox flag may twitch 5°); it is a no-op for views without the method. Presentation only, never affects the Sim.
- [ ] **Debug overlay (evidence).** A debug-build-only `Debug/FocusDebugOverlay` (CanvasLayer) draws the bracket rectangle and caret using `DrawPrimitive`/`DrawRect` outlines (never `DrawLine`/`DrawPolyline`) so the feed can be verified without the HUD; it does not ship in release builds (`OS.IsDebugBuild()`).
- [ ] **Allocation and order.** The feed is a struct written once per frame (0 managed bytes in steady state, probe over 600 frames); the World presenter publishes it before `HudPresenter` reads it (order test against `PresentationRoot.Present`'s World → Hud → Audio sequence).
- [ ] **Evidence (Integration).** `production/qa/evidence/player-prompt-anchor-evidence.md` with screenshots of the debug overlay on: a focused mailbox, an unavailable target (swimming), an out-of-range caret at ~4 m, nothing in front, and an anchor behind the camera (hidden), plus the recorded `PresentedSeq` trace for a press.

---

## Implementation Notes

*Derived from ADR-0019 §2/§6, ADR-0014 §7, GDD Visual/Audio:*

- Files: `src/Game/Hud/FocusFeed.cs` (struct + `IHudFocusFeed`), `src/Game/World/FocusAnchorPresenter.cs` (reads `IInteractionQuery`, `EntityViewRegistry`, `CameraRig`'s `Camera3D`), `src/Game/Debug/FocusDebugOverlay.cs`. The anchor point comes from `AnchorRegistry` *through the snapshot-adjacent query*: the Sim snapshot carries `AnchorRef`, not the point — add a read-only `IAnchorPoints.TryGetPoint(AnchorRef, out Vec3Mm)` to the Sim-side client surface (Story 018's `IInteractionQuery` is deliberately data-only; expose the point via a separate read-only lookup so the HUD never touches the registry). Keep this additive and tell the HUD owner.
- Bounds come from the **view node**, not from the Sim: `AnchorDef` (ADR-0014) has a point but no extents, and extents are presentation geometry. This is a deliberate decision (logged); if a verb owner wants authored bounds later, extend `AnchorDef` in an ADR-0014 amendment.
- Frame order: `PresentationRoot.Present` runs `World` then `Hud` (ADR-0019 §2) — publish the feed at the end of the World presenter so the HUD reads this frame's value. Use `IRenderClock` only for the pulse timing, never `delta`.
- The HUD presenter, prompt widget, device glyphs, hold ring and bracket drawing are the HUD epic's (no epic yet; `/ux-design` for the interaction prompt is required first — GDD UX flag). `design/ux/hud-notifications-seed.md` fixes the prompt zone (96 px under the reticle) and bracket spec (corner carets ≥ 24 px, Chalk, 2 px Charcoal gap; a single 16 px caret at 2.5–6 m) for that story.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Drawing the prompt, hold ring, lock icon, bracket, "Couldn't [verb] — [reason]" text and its localisation (HUD & Notifications epic — not yet created; blocked on `/ux-design` for the interaction prompt).
- Story 018: the snapshot itself. Story 023: the camera. Manual Delivery's prompt body (✓/✗ addresses).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/player/FocusFeedTests.cs` (feed contract against a scripted `IInteractionQuery` and a camera stub, Godot-free where the projection is injected) OR a retained playtest/screenshot doc `production/qa/evidence/player-prompt-anchor-evidence.md` — the document is required either way for the on-screen rows

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 018 (`IInteractionQuery`, `NearestBeyond`), Story 023 (camera), Story 022 (`EntityViewRegistry` views for bounds), `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (`Hud` presenter slot, `Initialize(deps)`) and `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md` (`PresentationRoot` / `Present`), `production/epics/simulation-core/story-024-framerunner.md` (`Present`)
- Unlocks: HUD & Notifications prompt / hold-ring / bracket stories (HUD epic, not yet created, after `/ux-design`); input-controls `PresentedSeq` echo end to end
