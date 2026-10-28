# Story 030: Glyph state — the last-used device, F9 flip rules and `router.Glyphs` (H-F9-1, H-EC-12 logic half)

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Deferred (Evan 2026-10-23 8:54 AM PT)
> **Deferred to**: M1 (glyph sets, Glyph style and prompt rendering) (out of the M-1a slice; split from story 017)
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~150 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **DEFERRED 2026-10-23, not deleted. Approved by Evan 2026-10-23 8:54 AM PT, Mail Lead chat: "Approve the ADR-0024 additions and the extra deferrals (md-008, tc-025, md-010 localisation to md-012, and the split-off hardening stories)".** Earlier context (not the approval of this story): Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it “production ready” is not needed. Accessibility for example". 8:10 AM PT, approving errata E1–E6 and E9 (decision log 2026-10-23): "approve all". This story is freed by **E1 (ADR-0012 — glyph switching, H-EC-12)**. Reason: the glyph set only matters once the HUD renders device-specific prompts (PlayStation / Neutral sets and Glyph style are M1, H-R9-3); the OR-across-devices, hold-ownership and duplicate-pad-edge rules that core play needs stay in story 017. The ACs below were moved verbatim from story 017. Re-run `/story-readiness` when M1 planning starts.

## Context

**GDD**: `design/gdd/input-controls.md` (R9 Devices and glyphs, F9 glyph flip, Edge Cases "synthetic action event"; ACs H-F9-1, H-EC-12 logic half)
**Requirement**: the router half of `TR-input-015` (device classes → last-used device); glyph switching clause of ADR-0012 §7 (E1 moved it out of M-1a)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§7 Devices — glyph set follows the last-used device) — Accepted 2026-10-16; E1 (2026-10-23) moves H-EC-12 glyph switching to M1
**ADR Decision Summary**: The glyph set follows the last-used device, changes only on a deliberate input above F9's thresholds, never during an active hold / aim / menu / stepper, never more often than once per cooldown; synthetic `InputEventAction`s never change the last-used device.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW (router)
**Engine Notes**: Controller-family detection is UNVERIFIED in 4.7: at first delivery **any** pad selects the Xbox glyph set. Whether a Steam virtual pad exposes a second pad id is UNVERIFIED (story 001).

**Control Manifest Rules (Foundation)**:
- Required: synthetic events never change the last-used device (ADR-0012 Implementation Guidelines; manifest Forbidden)
- Forbidden: an "active device" lock on input; flipping glyphs during a hold / aim; more than 1 / `glyphFlipCooldown` flips per second (photosensitivity floor)
- Guardrail: 0 B (advisory until M0, E2); glyph flips ≤ 2.5 / s at the default

---

## Acceptance Criteria

*From GDD R9, F9 and H-F9-1 / H-EC-12 (KBM + one Xbox pad glyph set; PlayStation, Neutral and the Glyph-style override are a later M1 story):*

- [ ] **H-EC-12 (logic half)** (from story 017). A `RawKind.Synthetic` / `DeviceClass.Synthetic` record never changes the last-used device: with last-used KBM, a synthetic action event (device id 0) leaves the glyph set KBM. Records with `DeviceClass.Mouse` include the emulation id (−1) mapping done by the adapter.
- [ ] **H-F9-1 (flip rules)** (from story 017). `flipToKbm` = any key / mouse-button press OR the sum of `|mouseRel|` over a 250 ms window ≥ `mouseFlipPx` (12); `flipToPad` = any pad button press (triggers via F4) OR `|stickOut| ≥ 0.5` continuously for ≥ 100 ms (3 ticks of fake time): stick 0.6 for 50 ms → no flip, for 100 ms → flip; mouse 5 px → no flip, 30 px → flip to KBM; the flip is **deferred to release** while a hold, aim, action menu or stepper is active; `apply` also needs `now − lastFlip ≥ glyphFlipCooldown` (400 ms): a second flip 0.3 s after the first applies at 0.4 s. `GlyphSet { Kbm, Xbox }`; `router.Glyphs` changes only through this machine; any pad → Xbox at M-1a.
- [ ] **Last-used device consumers** (split from story 017's "Pad identity" / story 018's H-EC-6 wording). `router.LastUsedDevice` (`DeviceClass` + `PadIndex`) is published alongside `router.Glyphs`; story 031's "solo auto-pause if that pad was the last-used device" reads it once this story lands (until then story 031 keeps a minimal local marker).
- [ ] **H-EC-12 (from story 029).** A synthetic `InputEventAction` (device 0) while the last-used device is KBM leaves the glyph set KBM.

---

## Implementation Notes

*Derived from ADR-0012 §7 and GDD R9 / F9 (moved from story 017):*

- Files in `src/PerformativeMail.Client.Core/Input/`: `GlyphTracker.cs`, `GlyphSet.cs`.
- Use the ms clock for all F9 windows; pointer-travel accumulation uses raw counts (OQ-IC-8: DPI normalisation is M1).
- Glyph *rendering* is HUD / asset-spec (`/asset-spec system:input-controls` → glyph atlases); the router only publishes the set id. **No glyph style override or PlayStation/Neutral sets here** (H-R9-3 is a separate M1 story).

---

## Out of Scope

- Story 017 (Ready, M-1a): OR across devices, hold ownership, duplicate-pad-edge collapse, pad slots. Story 026: the adapter's `DEVICE_ID_*` classification (the adapter half of H-EC-12 stays there). M1: Glyph style setting, PlayStation / Neutral sets, family detection. Steam double-pad behaviour verification (spike / MANUAL).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/GlyphTrackerTests.cs` (`[Trait("Id", "H-F9-1")]`, `H-EC-12`) — must exist and pass on net10.0 and net8.0 (from story 017)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 017 (device classes, pad slots, ownership); Stories 009 (stick magnitude, F4) and 010 (stepper state) for the "deferred to release" rule
- Unlocks: —
