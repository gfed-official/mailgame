# Story 011: Hand-side presentation — mailbox flap node, reach scrubbed by hold progress, visual-only lunge, reach echo, sweep riffle, delivery stance label

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Unblocked 2026-10-16 (story hygiene):** previously BLOCKED only because no godot-host-shell stories existed. They now exist and are listed under Depends on, alongside `production/epics/player-character/story-022-playerview-scene-render-pose.md` (PlayerView) and `production/epics/mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md` (MailboxView). No ADR or UX spec is missing (ADR-0019 and ADR-0016 Accepted). Re-check at `/story-readiness`.

## Context

**GDD**: `design/gdd/manual-delivery.md` (Feel & Comedy Feedback; Visual/Audio Requirements; Game Feel FA-MD-1…5; MD8 delivery stance; F4 sweep riffle)
**Requirement**: `TR-delivery-023` (mailbox flap is a separate node within the 150-tri budget; the reach layer is scrubbed by hold progress), `TR-delivery-024` (visual-only lunge ≤ 0.8 m via a rig offset, never feeds the Sim), `TR-delivery-018` (reach echo starts only when `canStartHoldNext` was true)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0019: Godot Presentation Scene Architecture (primary: §6 MailboxView flap child node, PlayerView `RigOffset` lunge ≤ 0.8 m, reach layer posed from `heldTicks/holdTicks`); ADR-0016 §5 (predictive vs outcome; reach echo rule)
**ADR Decision Summary**: `MailboxView` ≤ 150 tris, with the flap as a separate rotating child node. `PlayerView/RigOffset` (Node3D) carries the visual-only lunge (≤ 0.8 m local offset); nothing reads it back. The reach upper-body layer is posed from `heldTicks / holdTicks` (seek, not a time-scaled clip). `Animation.Length` is `double` in 4.7. The reach echo starts only if `CanStartHoldNext` was true in the last completed client tick's published state.
**ADR Version**: ADR-0019 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 (.NET) | **Risk**: HIGH (ADR-0019 Knowledge Risk: 4.4–4.7 post-cutoff)
**Engine Notes**: 4.6 scene format and `.uid` sidecars; `Animation.Length` is `double` (4.7); AnimationTree seek for the reach layer. Spawn the Godot specialist at `/dev-story`.

**Control Manifest Rules (Presentation layer)**:
- Required: predictive visuals read only published ClientLayer state; outcome beats come only from `PresentationEventStream` (ADR-0016 §5)
- Forbidden: anything read back into the Sim from a view (the lunge offset, animation time); Godot `Timer`/`delta` for gameplay-facing timing (use `IRenderClock`)
- Guardrail: `MailboxView` ≤ 150 tris; aim for 0 B per frame in the view update

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md`, scoped to the M-1a greybox:*

- [ ] **AC-1 Flap and reach (TR-delivery-023).** The mailbox flap is a separate child node of `MailboxView` (the view itself is `production/epics/mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md`'s). The reach layer pose equals `heldTicks / holdTicks` each frame, and is never time-scaled.
- [ ] **AC-2 Lunge (TR-delivery-024).** The lunge is applied only on `PlayerView/RigOffset`, ≤ 0.8 m, and no Sim or Client.Core type reads it (architecture test).
- [ ] **AC-3 (M-F4-1b, M-MD8-1b MANUAL, FA-MD timing ADVISORY)** Reduced motion shows one 0.1 s riffle frame and no count label. With letters selected, the label faces the camera within 0.2 s, the carry pose is unchanged and the label shows the entry's real address (`StringId` rendered by Presentation); with hands selected there is no stance. Retained screenshots of the ✓ hold, the ✗ hold and the flap are in `production/qa/evidence/`, with a sign-off in `production/qa/evidence/manual-delivery-hand-presentation-evidence.md`. FA-MD-1…4 are frame-captured at 60 fps (120 ms simulated RTT for FA-MD-2/3), ADVISORY.

---

## Implementation Notes

- Consume story 006's descriptors (`HandBeatController`, `RiffleTiming`) and story 003's `IDeliverPreviewRead` (reach echo). Never recompute them in Godot.
- Greybox only. Follow the GDD's Visual/Audio Requirements for beat content.

---

## Out of Scope

- HUD prompt and ring (HUD epic). Mail door beats (mail-destinations story 016).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshots in `production/qa/evidence/` + sign-off in `production/qa/evidence/manual-delivery-hand-presentation-evidence.md`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (scene root), `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (presenters, event stream cursor), `production/epics/godot-host-shell/story-008-viewcatalog-resolver-placeholder-uid-check.md` (`ViewCatalog`), `production/epics/godot-host-shell/story-009-entity-view-registry-pools-destination-views.md` (`EntityViewRegistry`, `Destinations`, `IDestinationView`, pools); `production/epics/player-character/story-022-playerview-scene-render-pose.md`; `production/epics/mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md`; Story 003, Story 006
- Unlocks: M-1a greybox feel review (M-FX-3)
