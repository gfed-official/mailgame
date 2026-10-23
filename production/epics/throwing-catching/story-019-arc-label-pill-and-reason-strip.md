# Story 019: Arc label pill and the throw reason strip lines (UI)

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: M (~1 day once unblocked; ~350 lines prod + the evidence capture)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **2026-10-23 (m1a-replan, Evan 7:13/7:15 AM PT):** the live-region line and the TC-U-3 b contrast AC moved to `story-025-deferred-throw-accessibility.md` (**Deferred**). Evan, 7:15: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example".

> **UNBLOCKED** (2026-10-16): the UX spec now exists — cite `design/ux/throwing-catching.md` (Layout Specification: label anatomy, placement algorithm + keep-out rect; label catalogue; M-1a reason-line table with F1 lifetimes; Accessibility live region; TC-UX-1…10). A lean `/ux-review` returned NEEDS REVISION; its 5 blockers were fixed in the spec — a confirmatory `/ux-review` re-run is recommended before implementation. The extra label keys (OQ-TC-4) land in story 005 (ordinary Depends-on); the HUD reason strip is a stub behind the same port until ADR-0018 (OQ-TC-7).

## Context

**GDD**: `design/gdd/throwing-catching.md` (UI Requirements "Arc label", "Money is shown", "Timing", "Text scale", "Prompt slot", "Live region", "World vs screen space"; Visual/Audio "Label"; AC TC-U-1, TC-U-2 c, TC-U-3 b). UX spec: `design/ux/throwing-catching.md` (authoritative for layout, strings and lifetimes; the GDD citations above are its sources).
**Requirement**: `TR-throwing-017` (screen-space ribbon family: the label is the ribbon's companion, same budget and 0-alloc rule), `TR-throwing-003` (the label text is built from the published, host-identical facts)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. The label's accessibility behaviour has no TR-ID in the registry; cited by GDD rule/AC id: UI Requirements, TC-U-1, TC-U-2 c, TC-U-3 b — no TR-ID in registry.)*

**ADR Governing Implementation**: ADR-0019: Godot Scene Architecture (§6 view rules, §7 window/stretch and text scale, §5 pools/0-alloc) — Accepted 2026-10-16 (primary); ADR-0016 (§5 predictive visuals; settle timing is story 005) ; ADR-0018 (HUD, minimal slice Proposed 2026-10-23) is not needed: its reason strip is deferred
**ADR Decision Summary**: Views draw published state in `_Draw`/node properties updated only on change; text and layout buffers are cached so steady-state frames allocate 0 B; the stretch mode is `canvas_items`/`keep_height` at a 1920×1080 reference with a 1280×720 minimum; `Control.AccessibilityLive` is typed `AccessibilityServer.AccessibilityLiveMode` in 4.7.
**ADR Version**: ADR-0019 2026-10-16; ADR-0016 2026-10-16; ADR-0018 2026-10-23 (Proposed; not relied on)

**Engine**: Godot 4.7.1 .NET | **Risk**: MEDIUM
**Engine Notes**: `Control.AccessibilityLive` typed `AccessibilityServer.AccessibilityLiveMode` (4.7); text scale 200 % and the 4.7 stretch defaults (hand-written `project.godot` gets `disabled`/`keep` unless set — ADR-0019 DEF-1); no `DrawLine`; no `RichTextLabel.add_image(... size_in_percent ...)`.

**Control Manifest Rules (Presentation layer)**:
- Required: steady-state frames allocate 0 B (text buffers cached; `Text` written only when the value changes); strings are localisation keys with arguments, never concatenated; presentation time from `IRenderClock`
- Forbidden: label text in red or magenta; a label covering its marker; computing money or class in Godot; `RichTextLabel` percent image sizes
- Guardrail: label build + draw within the ribbon's 0.3 ms budget

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (UI Requirements; TC-U-1, TC-U-2 c; TC-U-3 b deferred to story 025). **Provisional until the UX spec exists** — each criterion must be re-derived from `design/ux/throwing-catching.md`:*

- [ ] **TC-U-1 (parametrised, every class in the seed catalogue).** The label is exactly two lines built from localisation keys with arguments (story 005's `ThrowLabel`), never red or magenta; under pseudo-loc +40 % at 1280×720 and 200 % text scale it never truncates an address or overlaps the marker or the prompt rect. Addresses wrap and are never truncated; maximum width 35 % of the screen; a wrapped line 1 grows the pill.
- [ ] **Placement.** A Charcoal pill beside the end marker; it never covers the marker, flips above the marker rather than meet the prompt rect (HUD H11 protected rect), and stays inside a 24 px safe inset. Text is Chalk White; "×N" is Gold.
- [ ] **Timing.** Label text changes only through `PreviewDisplay.DisplayedLabel` (the 100 ms stability gate, story 005); its first appearance is immediate; the pill never shows a previous class's text during `previewSettleMs`.
- [ ] **TC-U-2 c [M-1a].** Each disallowed throw case shows its string in a **throw reason line** owned by this story (one `Label` under the aim layer, below the arc label; it is shown for HUD F1's lifetime ± 1 frame). *Mail Lead planning default, pending Evan:* the HUD's transient reason strip is deferred to the ADR-0018 amendment (ADR-0018 §6, Proposed 2026-10-23), so the greybox draws the line here, and the amendment moves it into the strip. The strings are: "Select mail to throw" (hands slot), "Nothing to throw" (empty slot), "Put it away first" (Holding), "Can't throw while swimming", "Hold to aim" (too-early release), "Aim cancelled" (G2 conditions), and the host rejection "Couldn't throw" (`ThrowReplyPart`, story 007). Dimmed (the lock icon waits for the HUD amendment).
- [ ] **0 B.** 600 frames of aiming with a changing class allocate 0 managed bytes in the label path.

---

## Implementation Notes

*Pending the UX spec. Known inputs:*

- Data: `ThrowLabel`, `PreviewDisplay` (story 005), `ThrowPreviewState.NotThrowable` / `EntryStale` (story 004), `LastThrowReply` (story 007), marker screen rect (story 018). The strings table (localisation keys → text) is part of the UX spec's content; the seed's catalogue (`design/ux/throwing-catching-seed.md` §1) is *input only*.
- Files (suggested): `src/Game/Views/Throwing/ArcLabelView.cs`, `ThrowReasonRouter.cs` (routes reason keys into the HUD's transient strip through a Client.Core port; the HUD is not built), strings in the localisation table owned by the localisation setup.

---

## Out of Scope

- The ribbon and markers (story 018). Catch prompt, result lines, hotbar pulse, incoming-throw cue and hands-up pose (M0 / the UX spec's later sections). Teammate hat pins (M0).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: UI
**Required evidence**:
- UI: retained screenshots of each label class at 1280×720 / 200 % text scale and the reason-strip strings, in `production/qa/evidence/throwing/arc-label/`, signed off by the ux-designer in `production/qa/evidence/throwing/arc-label-evidence.md`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `design/ux/throwing-catching.md` (written 2026-10-16; confirmatory `/ux-review` recommended); Stories 004, 005, 007, 018; no HUD story (the reason line is this story's own until the ADR-0018 amendment adds the strip)
- Unlocks: TC-U-1, TC-U-2 c sign-off
