# Story 025: Carry pose by held item class, whole-body load blend and idle doze (P20)

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: M (~1 day; ~400 lines prod + tests + evidence)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/player-character-movement.md` (P20 presentation-only behaviours; Visual/Audio "Weight on the body (resolves Grid Inventory OQ18's weight items — art-director ruling)", "Idle"; Player Fantasy "The body tells the truth about the load"; fantasy test 2)
**Requirement**: `TR-movement-025` (greybox rig carry pose — registry coverage gap, ADR-0029 planned/not written); P20 itself has **no TR-ID in the registry** — cited by GDD rule P20 and acceptance ids PCM-P20-1, PCM-P20-2, PCM-FX-2 (manual, M1)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture (§6 PlayerView rules, §2) — Accepted 2026-10-16 (primary); ADR-0016 (§6 `IRenderClock` — idle uses the render clock, not `delta`) secondary — Accepted
**ADR Decision Summary**: Views are driven by data from `Present` and the render clock; no Presentation value (animation time, rig offset) feeds the Sim. Carry pose and idle are presentation-only: "None of these change the Sim" (GDD P20, PCM-P20-2).
**ADR Version**: ADR-0019 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 .NET (`~/.local/bin/godot-mono`) | **Risk**: MEDIUM
**Engine Notes**: Same 4.7.1 shapes as Story 024 (`StringName` parameter paths, `Animation.Length` double). The pure selection/hysteresis/idle logic is Godot-free (`Client.Core`) and unit-testable; only the bone/animation writes touch Godot.

**Control Manifest Rules (Presentation layer)**:
- Required: presentation time comes from `IRenderClock`; `delta`, Godot `Timer` and `Time.GetTicksMsec` are banned for gameplay-facing timing; client-side logic that needs clocks lives in `Client.Core` behind injected clocks (ADR-0016, ADR-0001 §1)
- Forbidden: never feed a Presentation value into the Sim; never reimplement a Sim rule or formula in Godot or Client.Core (ADR-0019, ADR-0016)

---

## Acceptance Criteria

*From GDD P20 and Visual/Audio, scoped to this story:*

- [ ] **Carry pose follows the held (selected) item's class** (light aloft, medium hugged, heavy overhead), not the heaviest entry in the bag: a heavy parcel buried in the backpack does **not** put the carrier into the overhead pose; with nothing selected the carrier uses the empty-hand pose. The class is read from the `IHeldItemView` port (selected hotbar entry's weight class from the own-container mirror); changing the held item or its class changes the pose on the same client tick (blend ≤ 0.1 s, the animation never gates the Sim). Heavy-overhead is offset to the dominant side so it never hides the throw-arc origin.
- [ ] **Whole-body load is continuous.** `loadBlend = min(P, 40) / 40` from the replicated `weightPoints` (`ICarrierLoadSource.WeightPoints`): knee bend 0–25°, forward lean ≤ 10°, backpack sag 0–12 cm, interpolating continuously (no steps) — P = 0 gives zero offsets, P = 20 gives exactly half, P ≥ 40 gives the maxima; at P ≥ 40 (the D1 floor) the heavy waddle plays, entering at P ≥ 40 and leaving at P ≤ 38 (hysteresis, so a bag hovering at the floor does not flicker). Strain face and hat flatten stay **reserved** for the throw wind-up (not used here).
- [ ] **Honest weight (fantasy test 2, data half).** Stride rate stays proportional to real speed (Story 024) with the load applied on top; carry pose and stride change on the same tick the held item or speed changes (verified against a scripted `ICarrierView`/`IHeldItemView` sequence).
- [ ] **PCM-P20-1 (idle doze).** With an injected clock, no input for **20.0 s** fires the doze (asleep face cell, hat slid over the eyes, snore bubbles omitted at greybox), **19.97 s does not**, and any input wakes it instantly with the 0.1 s hat-pop; the raid-horn wake is a `Wake()` entry point (nothing calls it until Run Structure, M-1b). The idle clock is `IRenderClock.RenderSimTimeMs`/an injected `IInputClock`, so a solo pause does not accumulate idle time.
- [ ] **PCM-P20-2 (isolation).** Idle, pose and (future) emote changes leave the Sim state hash unchanged: running the golden Sim script with the presentation components attached and with them absent produces identical per-tick dump hashes (the presentation reads `ICarrierView`/`IHeldItemView` read-only; no presentation write path to Sim types exists — architecture test over the `src/Game/Views/Player/**` and `Client.Core/Presentation/**` references).
- [ ] **Pose gating rules (no emotes yet).** The carry pose is suppressed while Swimming (swim pose: parcel overhead), Interacting is *not* suppression for carry (the interact hold is an upper-body one-shot in layer 3, Manual Delivery's); Mounted/Knockdown/Frozen poses are M1/M-1b and not implemented.
- [ ] **Evidence (Visual/Feel).** `production/qa/evidence/player-carry-pose-evidence.md`: screenshots of light/medium/heavy held items from behind, a P = 0 / 20 / 40 load sequence, the doze and wake, and a lead sign-off. The M1 manual test PCM-FX-2 (≥ 5 of 6 testers rank loads correctly) is recorded as *deferred to M1 playtest*.

---

## Implementation Notes

*Derived from GDD P20 and Visual/Audio "Weight on the body":*

- Files: `src/PerformativeMail.Client.Core/Presentation/CarryPoseSelector.cs` (pure: `(heldClass, swimming) → pose id`), `LoadBlend.cs` (pure fixed-point `P → blend‰` and `waddle hysteresis`), `IdleTracker.cs` (pure: `Update(nowMs, anyInput) → IdleState`), `IHeldItemView.cs` (Client.Core port); Godot side `src/Game/Views/Player/CarryPoseDriver.cs`, `IdleDozeDriver.cs` writing the layer-2 pose, the load offsets and the face cell/hat tilt through the Story 024 driver.
- `IHeldItemView` is a **new port** (no existing grid-inventory story exposes the selected hotbar entry's weight class to Presentation). Implement it as an adapter over the own-container mirror (`IPanelView` hotbar section + selected slot); flag to the grid-inventory owner. Cross-epic gap logged in EPIC.md.
- `weightPoints` for the blend comes from `ICarrierLoadSource.WeightPoints` (Story 020); the Sim's `speedPermille` is *not* used for the visual load (the art ruling uses `min(P,40)/40`, "continuously from the replicated `weightPoints`").
- Idle-input detection: any non-zero move/look/button in the `InputFrame` stream or the router's activity flag; focus-loss pause stops the clock because `RenderSimTimeMs` is frozen while paused.
- Greybox "asleep" = a flat darker face colour + hat offset; the real face-cell UV offset is an art task (asset-spec).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 024: rig, gait, squash. Manual Delivery: the reach/hold upper-body layer and the lunge. Throwing: throw wind-up strain face, catch one-shot. Comms: emotes and their suppression rules.
- Knockdown, clock-in crate, on-break pose, spawn-protection shell (M-1b / M0), Postal Sprint streaks, reduced-motion variants (Accessibility / M1), audio cues.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshots in `production/qa/evidence/` + sign-off in `production/qa/evidence/player-carry-pose-evidence.md`; plus `tests/unit/player/CarryPoseTests.cs` (selector, `LoadBlend`, hysteresis), `tests/unit/player/IdleTrackerTests.cs` (`[Trait("Id", "PCM-P20-1")]`) and `tests/integration/player/PresentationIsolationTests.cs` (`PCM-P20-2`) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 024 (rig and driver), Story 022 (`PlayerView`), Story 020 (`ICarrierLoadSource.WeightPoints`, `ICarrierView`), Story 010 (golden script for the isolation hash), `production/epics/grid-inventory/story-023-panel-grid-view-hotbar.md` (own-container mirror / hotbar selection that `IHeldItemView` adapts), `production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md` (hash harness)
- Unlocks: M1 playtest PCM-FX-2; Manual Delivery / Throwing upper-body layers (they share the carry layer)
