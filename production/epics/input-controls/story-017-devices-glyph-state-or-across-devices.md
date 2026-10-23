# Story 017: Devices — OR across keyboard / mouse / pads, hold ownership and duplicate-pad-edge collapse

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day; ~300 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **Split 2026-10-23 (E1, ADR-0012).** The glyph state — the last-used device, the F9 flip rules (H-F9-1) and the logic half of H-EC-12 (`GlyphTracker`, `GlyphSet`, `router.Glyphs`) — moved to **story 030 (Deferred to M1)**. This story keeps what core play needs: the OR across devices, per-action hold ownership (H-EC-2, H-R9-1) and the duplicate-pad-edge collapse (H-EC-13). Evan, Mail Lead chat 2026-10-23 8:10 AM PT, approving errata E1–E6 and E9: "approve all".

## Context

**GDD**: `design/gdd/input-controls.md` (R9 Devices, Edge Cases "same action pressed on KBM and pad at once", "Steam Input virtual pad"; ACs H-EC-2, H-EC-13, H-R9-1 held-bit half)
**Requirement**: `TR-input-024` (one local carrier per machine; all devices OR-ed; duplicate pad edges within 20 ms collapse) and the router half of `TR-input-015` (device classes). **Slice conflict:** the epic table tags TR-input-024 "M0", but the GDD ACs it summarises (H-R9-1, H-EC-2, H-EC-13) are tagged **M-1a**; this story implements them now (logged in the decision log).
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§3 "Action-level aggregation", §7 Devices) — Accepted 2026-10-16; E1 (2026-10-23) moves the glyph-switching clause to M1 (story 030)
**ADR Decision Summary**: Held state is the OR across devices. A second device pressing the same action produces no edge; the action's press belongs to the first input, whose release ends it even if another is still down (H-EC-2). Duplicate pad edges within `padEdgeCollapseMs` (20) collapse (H-EC-13).
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW (router) — the `DEVICE_ID_*` classification and Steam-virtual-pad behaviour are adapter / spike matters (stories 001, 026)
**Engine Notes**: The router sees `DeviceClass` and `PadIndex` only; whether a Steam virtual pad exposes a second pad id (and collides with 16 / 32) is UNVERIFIED (story 001).

**Control Manifest Rules (Foundation)**:
- Required: KBM and pad treated through `DeviceClass`, never `device == 0` (ADR-0012 Implementation Guidelines; manifest Forbidden)
- Forbidden: an "active device" lock on input
- Guardrail: 0 B (advisory until M0, E2)

---

## Acceptance Criteria

*From GDD R9 and H-EC-2 / H-EC-13 / H-R9-1, scoped to this story (KBM + up to 4 pads; glyph state is story 030, M1):*

- [ ] **OR across devices (H-R9-1 held half).** Pad Sprint in Hold mode with Shift and L3 both held: releasing one leaves sprint **held** (the bit is the OR); the same for any `HeldState` action across KBM and any connected pad.
- [ ] **H-EC-2.** E (keyboard) and pad X pressed together → **one** Interact Press edge; the hold belongs to E; releasing E early cancels (a `Release`/`Cancel` edge) even though X is still down, and the still-held X is then muted until released; releasing X first changes nothing. In **Toggle** mode a press of the action from any device counts as the second press (story 012).
- [ ] **H-EC-13 (20 ms collapse).** Identical pad button edges from two different pad ids within `padEdgeCollapseMs` = 20: 19 ms apart → **one** D-pad hotbar step; 21 ms apart → two; held state is unaffected (OR); the window is configurable 10–50 ms (default 20). Same-pad repeats are not collapsed.
- [ ] **Pad identity.** Up to 4 pad slots; a pad's inputs are keyed `(PadIndex, PhysicalInput)` so two pads' identical buttons are separate machines before the collapse rule applies; `RawKind.Synthetic` / `DeviceClass.Synthetic` records (synthetic `InputEventAction`s, device id 0) are never attributed to a pad slot or to KBM ownership. (Hot-plug bookkeeping over these slots is story 031, M0; the last-used device is story 030, M1.)

---

## Implementation Notes

*Derived from ADR-0012 §3, §7 and GDD R9:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `InputRouter.Devices.cs`, `DeviceTable.cs`, `ActionOwnership.cs` (per-action "owner input"), `PadEdgeCollapser.cs`.
- Ownership is per *action*: the first physical input to press it owns the press; other inputs for the same action are muted (their press is swallowed, their raw held state still tracked). When the owner releases, the action ends even if another input is down.
- The pad-collapse window keys on `(ActionId, EdgeKind)` across different `PadIndex` values only.
- Use the ms clock for the collapse window (story 003's `IInputClock`).
- `TR-input-024`'s "one carrier per machine" is a design constraint (no split-screen): there is no per-player routing code.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 030 (Deferred, M1): the last-used device, F9 flip rules, `router.Glyphs`, H-EC-12 logic half. Story 031 (Deferred, M0): disconnect / reconnect handling. Story 018: focus loss. Story 026: the adapter's `DEVICE_ID_*` classification and axis cache. M1: Glyph style setting, PlayStation / Neutral sets, family detection. Steam double-pad behaviour verification (spike / MANUAL).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/DeviceOwnershipTests.cs` (`[Trait("Id", "H-EC-2")]`, `H-R9-1`), `tests/unit/input/PadEdgeCollapseTests.cs` (`[Trait("Id", "H-EC-13")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 010 (D-pad steps), 012 (Toggle second press)
- Unlocks: Stories 018, 020, 021, 026 (adapter classification target); Deferred: 030 (glyph state, M1), 031 (hot-plug, M0)
