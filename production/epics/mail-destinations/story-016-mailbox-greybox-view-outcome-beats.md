# Story 016: Greybox `MailboxView` and house — flag pop and wobble outcome beats keyed from `DeliveryOutcome`

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Unblocked 2026-10-16 (story hygiene):** previously BLOCKED only because no godot-host-shell stories existed. They now exist; the scene-root, presenter/event-cursor, `ViewCatalog` and destination-view stories are listed under Depends on. No ADR or UX spec is missing (ADR-0019 and ADR-0016 Accepted). Re-check at `/story-readiness`.

## Context

**GDD**: `design/gdd/mail-destinations.md` (§Feel & Comedy Feedback outcome table; §Visual/Audio Requirements; §Game Feel; M-FA-2, M-FA-4)
**Requirement**: `TR-mail-022` (presentation caps: residents, envelope pool, flashes, audio voices)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0019: Godot Presentation Scene Architecture (primary, §6 MailboxView, §5 pool table); ADR-0016 §5 (predictive visuals vs outcome beats: a beat is triggered only by a cursor reading the matching domain event)
**ADR Decision Summary**: One `MailboxView` per lot under `Destinations` (static, not pooled); ≤ 150 tris; the flap is a separate rotating child node; states are material/vertex swaps plus a decal quad. Outcome beats start only from a cursor reading `DeliveryOutcome`/`ComplaintRaised`. They may be delayed but never advanced, and nothing in the predictive path may start one.
**ADR Version**: ADR-0019 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 (.NET) | **Risk**: HIGH (ADR-0019 Knowledge Risk: 4.4–4.7 post-cutoff)
**Engine Notes**: 4.6 scene format (no `load_steps`, unique node ids) and `.uid` sidecars; 4.7 `Animation.Length` is `double`; `AudioStreamPlayer.area_mask` default 0 (4.7). Spawn the Godot specialist at `/dev-story` (risk HIGH). Check `docs/engine-reference/godot/` before any API not listed in ADR-0019.

**Control Manifest Rules (Presentation layer)**:
- Required: views are built from the Sim's published state and events; dependencies point Godot → Client.Core → Sim (Global)
- Required: an outcome beat is triggered only by a cursor reading the domain event (ADR-0016 §5)
- Forbidden: Presentation computing `Evaluate`/pay; any value read back into the Sim; deprecated 4.7 APIs (manifest Forbidden APIs table)
- Guardrail: `MailboxView` ≤ 150 tris; TR-mail-022 caps (ADR-0019 §5 pool table)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md`, scoped to the M-1a greybox (accept and misdeliver beats):*

- [ ] **AC-1 Views.** On the M-1a street, each of the 10 lots shows one greybox house and one `MailboxView` at its anchor, facing its `Face`. Each view is ≤ 150 tris, and the flap is a separate child node.
- [ ] **AC-2 Beats (M-FA-4, M-FA-2 on the host).** A hand Accept plays the accept beat (flag pop) and a misdeliver plays the misdeliver beat (wobble/shake) from the GDD's outcome table. A 20-stack thrown misdelivery logs exactly one sting, one pill and one complaint shake. On the host the outcome beat starts ≤ 200 ms after hold completion, and never before the `DeliveryOutcome` event is read (counted by a test log).
- [ ] **AC-3 Evidence.** Retained screenshots of the accept and misdeliver beats are saved in `production/qa/evidence/`, with a lead sign-off in `production/qa/evidence/mailbox-greybox-beats-evidence.md`.

---

## Implementation Notes

*Derived from ADR-0019 §5–§6 and ADR-0016 §5:*

- Greybox only: primitive meshes, no final art. Follow the GDD's Visual/Audio Requirements for which beat maps to which outcome; residents, pools and audio voices stay within TR-mail-022's caps.
- A `hand` outcome group (same actor, destination and tick) plays one door beat and one "+N¢ · k stacks" pill (R16).
- No engine API outside ADR-0019's verified list without a spike (architecture principle 5).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- manual-delivery: hold-start arm/progress ring, neutral slot-in (M-FA-1), PlayerView lunge.
- throwing-catching: landing beat timing under RTT (M-FA-3).
- HUD wallet/complaint widgets (no HUD epic yet). Audio mix (later).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshots in `production/qa/evidence/` + sign-off in `production/qa/evidence/mailbox-greybox-beats-evidence.md`; the beat-ordering count (AC-2) as a gdUnit4 or host-harness test when gdUnit4 is installed

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (scene root), `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (presenters, event stream cursor), `production/epics/godot-host-shell/story-008-viewcatalog-resolver-placeholder-uid-check.md` (`ViewCatalog`), `production/epics/godot-host-shell/story-009-entity-view-registry-pools-destination-views.md` (`EntityViewRegistry`, `Destinations`, `IDestinationView`, pools); Story 007, Story 008, Story 015; `production/epics/world-stub/story-006-m1a-street-and-fixtures.md`
- Unlocks: M-1a greybox playtest (Mail M-FT-1 protocol later)
