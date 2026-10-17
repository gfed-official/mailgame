# Story 017: Devices — OR across keyboard / mouse / pads, hold ownership, duplicate-pad-edge collapse and the last-used glyph state (F9)

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (R9 Devices and glyphs, F9 glyph flip, Edge Cases "same action pressed on KBM and pad at once", "Steam Input virtual pad", "synthetic action event"; ACs H-F9-1, H-EC-2, H-EC-12 logic half, H-EC-13, H-R9-1 held-bit half)
**Requirement**: `TR-input-024` (one local carrier per machine; all devices OR-ed; duplicate pad edges within 20 ms collapse) and the router half of `TR-input-015` (device classes). **Slice conflict:** the epic table tags TR-input-024 "M0", but the GDD ACs it summarises (H-R9-1, H-EC-2, H-EC-13) are tagged **M-1a**; this story implements them now (logged in the decision log).
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§3 "Action-level aggregation", §7 Devices) — Accepted 2026-10-16
**ADR Decision Summary**: Held state is the OR across devices. A second device pressing the same action produces no edge; the action's press belongs to the first input, whose release ends it even if another is still down (H-EC-2). Duplicate pad edges within `padEdgeCollapseMs` (20) collapse (H-EC-13). The glyph set follows the last-used device, changes only on a deliberate input above F9's thresholds, never during an active hold / aim / menu / stepper, never more often than once per cooldown; synthetic `InputEventAction`s never change the last-used device.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW (router) — the `DEVICE_ID_*` classification and Steam-virtual-pad behaviour are adapter / spike matters (stories 001, 026)
**Engine Notes**: The router sees `DeviceClass` and `PadIndex` only; whether a Steam virtual pad exposes a second pad id (and collides with 16 / 32) is UNVERIFIED (story 001). Controller-family detection is UNVERIFIED in 4.7: at M-1a **any** pad selects the Xbox glyph set.

**Control Manifest Rules (Foundation)**:
- Required: synthetic events never change the last-used device; KBM and pad treated through `DeviceClass`, never `device == 0` (ADR-0012 Implementation Guidelines; manifest Forbidden)
- Forbidden: an "active device" lock on input; flipping glyphs during a hold / aim; more than 1 / `glyphFlipCooldown` flips per second (photosensitivity floor)
- Guardrail: 0 B; glyph flips ≤ 2.5 / s at the default

---

## Acceptance Criteria

*From GDD R9, F9 and H-F9-1 / H-EC-2 / H-EC-13 / H-EC-12 / H-R9-1, scoped to this story (KBM + one Xbox pad glyph set; PlayStation, Neutral and the Glyph-style override are M1):*

- [ ] **OR across devices (H-R9-1 held half).** Pad Sprint in Hold mode with Shift and L3 both held: releasing one leaves sprint **held** (the bit is the OR); the same for any `HeldState` action across KBM and any connected pad.
- [ ] **H-EC-2.** E (keyboard) and pad X pressed together → **one** Interact Press edge; the hold belongs to E; releasing E early cancels (a `Release`/`Cancel` edge) even though X is still down, and the still-held X is then muted until released; releasing X first changes nothing. In **Toggle** mode a press of the action from any device counts as the second press (story 012).
- [ ] **H-EC-13 (20 ms collapse).** Identical pad button edges from two different pad ids within `padEdgeCollapseMs` = 20: 19 ms apart → **one** D-pad hotbar step; 21 ms apart → two; held state is unaffected (OR); the window is configurable 10–50 ms (default 20). Same-pad repeats are not collapsed.
- [ ] **H-EC-12 (logic half).** A `RawKind.Synthetic` / `DeviceClass.Synthetic` record never changes the last-used device: with last-used KBM, a synthetic action event (device id 0) leaves the glyph set KBM. Records with `DeviceClass.Mouse` include the emulation id (−1) mapping done by the adapter.
- [ ] **H-F9-1 (flip rules).** `flipToKbm` = any key / mouse-button press OR the sum of `|mouseRel|` over a 250 ms window ≥ `mouseFlipPx` (12); `flipToPad` = any pad button press (triggers via F4) OR `|stickOut| ≥ 0.5` continuously for ≥ 100 ms (3 ticks of fake time): stick 0.6 for 50 ms → no flip, for 100 ms → flip; mouse 5 px → no flip, 30 px → flip to KBM; the flip is **deferred to release** while a hold, aim, action menu or stepper is active; `apply` also needs `now − lastFlip ≥ glyphFlipCooldown` (400 ms): a second flip 0.3 s after the first applies at 0.4 s. `GlyphSet { Kbm, Xbox }`; `router.Glyphs` changes only through this machine; any pad → Xbox at M-1a.
- [ ] **Pad identity.** Up to 4 pad slots; `OnPadConnection` bookkeeping (story 018) uses these slots; a pad's inputs are keyed `(PadIndex, PhysicalInput)` so two pads' identical buttons are separate machines before the collapse rule applies.

---

## Implementation Notes

*Derived from ADR-0012 §3, §7 and GDD R9 / F9:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `InputRouter.Devices.cs`, `DeviceTable.cs`, `ActionOwnership.cs` (per-action "owner input"), `PadEdgeCollapser.cs`, `GlyphTracker.cs`, `GlyphSet.cs`.
- Ownership is per *action*: the first physical input to press it owns the press; other inputs for the same action are muted (their press is swallowed, their raw held state still tracked). When the owner releases, the action ends even if another input is down.
- The pad-collapse window keys on `(ActionId, EdgeKind)` across different `PadIndex` values only.
- Use the ms clock for all F9 windows; pointer-travel accumulation uses raw counts (OQ-IC-8: DPI normalisation is M1).
- Glyph *rendering* is HUD / asset-spec (`/asset-spec system:input-controls` → glyph atlases); the router only publishes the set id. **No glyph style override or PlayStation/Neutral sets at M-1a** (H-R9-3 is M1).
- `TR-input-024`'s "one carrier per machine" is a design constraint (no split-screen): there is no per-player routing code.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 018: disconnect / reconnect handling. Story 026: the adapter's `DEVICE_ID_*` classification and axis cache. M1: Glyph style setting, PlayStation / Neutral sets, family detection. Steam double-pad behaviour verification (spike / MANUAL).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/DeviceOwnershipTests.cs` (`[Trait("Id", "H-EC-2")]`, `H-R9-1`), `tests/unit/input/PadEdgeCollapseTests.cs` (`[Trait("Id", "H-EC-13")]`), `tests/unit/input/GlyphTrackerTests.cs` (`[Trait("Id", "H-F9-1")]`, `H-EC-12`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 009 (stick magnitude, F4), 010 (D-pad steps), 012 (Toggle second press)
- Unlocks: Stories 018, 020, 021, 026 (adapter classification target); HUD prompt rendering (no HUD epic yet) reads `router.Glyphs`
