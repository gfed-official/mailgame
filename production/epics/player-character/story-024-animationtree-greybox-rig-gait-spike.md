# Story 024: Greybox rig, one `AnimationTree` state machine, gait playback and jump/land squash — and the AnimationTree cost spike

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: L (~1 day to a working rig; the spike measurement + report may take a further half day — split the report out if it overruns)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/player-character-movement.md` (Visual/Audio — "Rig and blend constraints (technical-artist)", pose language for idle / walk / sprint / jump / wade / swim; Game Feel "The rig never gates movement", "Honest weight: the step rhythm is the speed")
**Requirement**: `TR-movement-025` (one AnimationTree per character, ≤ 4 layers, 48 characters ≤ 1.0 ms — M-1a greybox rig; LOD budget is M1; the registry shows a coverage gap — **ADR-0029 is planned, not written, and this story's spike gates it**), `TR-movement-007` (the rig never feeds the Sim)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. The GDD marks the AnimationTree cost and manual-advance behaviour "UNVERIFIED for 4.7 — a SPIKE in the M-1a greybox gates the AnimationTree ADR". Acceptance ids: PCM-FX-2 (manual, M1 playtest), no PCM id covers rig internals.)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture (§2 tree; §6 `PlayerView` rules: reach layer posed from `heldTicks/holdTicks` by seek, `Animation.Length` is `double` in 4.7, `AnimationPlayer` members are `StringName` in 4.6) — Accepted 2026-10-16 (primary); ADR-0016 (§5–§6 predictive visuals vs outcome beats; render clock) secondary — Accepted
**ADR Decision Summary**: Views animate only from data `Present` hands them (`AlphaPermille`/`IRenderClock`), never `delta`; nothing in the rig feeds the Sim; steady-state frames allocate nothing (pre-allocated, cached `StringName` parameter paths). The rig's detailed shape (layers, LOD driver, camera) will be decided by ADR-0029 from this story's measurements.
**ADR Version**: ADR-0019 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 .NET (`~/.local/bin/godot-mono`) | **Risk**: MEDIUM-HIGH (AnimationTree manual advance, per-character cost and C# interop are UNVERIFIED for 4.7)
**Engine Notes**: Post-cutoff shapes to honour (manifest): `Animation.Length` is `double`; `AnimationPlayer.AssignedAnimation/Autoplay/CurrentAnimation` are `StringName` (`GetQueue` returns `StringName[]`); `MeshInstance3D.skeleton` must be set explicitly on every skinned mesh (4.6 default changed to `""`); scenes use the 4.6 format. Cache `StringName` parameter paths (`"parameters/…"`) once; setting `AnimationTree` parameters with `string` literals per frame allocates.

**Control Manifest Rules (Presentation layer)**:
- Required: views animate from `AlphaPermille`/`IRenderClock`, not `delta`; pools/instances are pre-allocated, steady-state frames instantiate no nodes and allocate 0 B (ADR-0019, ADR-0016)
- Required: 4.7.1 C# API shapes (`Animation.Length` double; `StringName` members) (breaking-changes.md)
- Forbidden: feeding animation time, rig offset or camera into the Sim; ragdoll/Jolt for the carrier; reimplementing a Sim rule (ADR-0019)

---

## Acceptance Criteria

*From GDD Visual/Audio (technical-artist rig constraints) and Game Feel, scoped to this story (carry pose, whole-body load and doze are Story 025):*

- [ ] **Rig shape.** The `PlayerView` rig (under `RigOffset`, Story 022) is a `Skeleton3D` with **≤ 24 bones and no facial bones** (face = UV offset/material cell, greybox: a flat colour), one `AnimationTree` with a state machine whose states are **Ground, Airborne, Swim, Seated, Knockdown, Frozen** (Seated/Knockdown/Frozen exist as declared, unreachable nodes at M-1a), and **≤ 4 layers** at LOD0: (1) base locomotion, (2) upper-body carry pose (bone-filtered; filled by Story 025), (3) upper-body one-shot (empty slot for interact/throw/catch — Manual Delivery and Throwing fill it), (4) additive hat/squash driven by C# critically damped springs, **not clips**. A rig-audit script/test counts bones, states and layers and fails above the limits.
- [ ] **Gait clips, not a speed blend.** Walk, sprint and swim loops are authored/greyboxed for **5.0, 7.5 and 2.0 m/s**; playback rate = `groundSpeed / clipSpeed` clamped to **[0.6, gaitPlaybackMax = 1.4]** (the clamp matches F1's 0.6 floor and 1.4 cap; ContentValidator rule PC 3 keeps `1000 + buffCapPm ≤ 1000 × gaitPlaybackMax`); the blend input is the **self-propelled, support-relative** horizontal speed from `ICarrierView` (so belts and roofs later never make a carrier sprint in place). Stride is proportional to real speed — no cadence thresholds; a walk at 3000 mm/s plays at 0.6×, sprint at 10 500 mm/s at 1.4×, and a speed change changes the rate on the same tick (PCM-F1-1 values).
- [ ] **State mapping.** `Loco` Grounded → Ground (Idle/Walk/Sprint by speed and the Sprinting bit); Airborne → Airborne (stretch on takeoff ×1.2, tucked fall; landing squash 0.8 → 1.0 snap ≤ 0.1 s, hold ≥ 0.15 s, depth scales with fall speed and never goes below 0.8, hat spring ≤ 15 cm — no fall damage concept); Swimming → Swim (chest-deep paddle, hat floating beside the body); Wading (the `Wading` flag) → high-knee steps; transitions follow the same tick the Sim state changes (PCM "the animation never shows a speed the Sim does not give").
- [ ] **Locomotion is exempt from anticipation** (GDD proposal): the rig never gates or delays movement; secondary motion (hat, backpack, squash) overlaps; a unit/headless test shows the carrier's rendered position is independent of animation state (Story 022 pose with the AnimationTree disabled equals enabled).
- [ ] **Zero-allocation steady state.** After a 300-frame warm-up, 600 frames of the golden script drive the rig with 0 managed bytes allocated per frame (`GC.GetAllocatedBytesForCurrentThread` around the presenter update; cached `StringName`s; no `new` in `_Process`).
- [ ] **SPIKE — cost and API facts (gates ADR-0029).** `prototypes/spike-animationtree/REPORT.md` records, measured on this machine with `godot-mono` for **1, 8, 24 and 48** instances of the rig (headless where timing is meaningful, windowed for draw-call counts): CPU time per frame of the AnimationTree + skeleton update (µs; target ≤ 1.0 ms at 48, **advisory/UNVERIFIED**), whether `AnimationTree.CallbackModeProcess = Manual` with `Advance(dt)` works for 15 Hz/30 Hz stepping (M1 LOD driver), managed allocation per frame, the C# interop cost of setting parameters with cached `StringName` vs strings, the actual types of `Animation.Length` / `AnimationPlayer` members, and draw calls per human at LOD0 (target ≈ 4–6). Each fact is marked VERIFIED or still UNVERIFIED — a number not measured is written `NOT MEASURED — <why>`, never guessed.
- [ ] **Evidence (Visual/Feel).** `production/qa/evidence/player-rig-gait-evidence.md` with screenshots/short capture of idle, walk, sprint, jump (stretch/tuck/squash), wade and swim states, the rate-vs-speed table, and a lead sign-off.

---

## Implementation Notes

*Derived from GDD Visual/Audio and ADR-0019 §6:*

- Files: `src/Game/Views/Player/PlayerRig.tscn` (instanced under `RigOffset`), `PlayerRigDriver.cs` (the C# driver: parameter writes, gait rate, spring updates), `HatSquashSprings.cs` (critically damped, float — Presentation only), `GaitRate.cs` (pure `(speedMmS, clipSpeed) → rate` in `Client.Core` with a Godot-free unit test), `tools/GenPlayerRig/` (optional dev-time generator that builds the skeleton and greybox clips and saves `.tscn`/`.tres` — allowed because the output is committed; prototype code is not promoted). Greybox clips can be simple bone-rotation keyframes; art quality is not the point.
- Reach/lunge layer content (Manual Delivery TR-delivery-024: posed from `heldTicks/holdTicks` by *seek*, not a time-scaled clip) is **not** built here; leave layer 3 empty and documented.
- Drive from `ICarrierView` (speed, `Loco`, flags) via `Present`; time from `IRenderClock` differences for springs. Do not read `delta`. `AnimationTree.Active` and a manual-advance mode are the spike's subject — keep the driver switchable between `Idle`(automatic) and manual process so the spike can compare.
- Knockdown, Seated and Frozen states are scaffolding for M-1b/M1: no clips, no transitions into them.
- `MeshInstance3D.skeleton` explicit on every skinned mesh and the hat mesh; `.uid` files committed; if scene format changes are needed run Upgrade Project Files once, in a dedicated change (ADR-0019 §8).
- The spike report folder `prototypes/spike-animationtree/` is a prototype area (CLAUDE.md prototype rules); the rig code in `src/Game` is production code and is *not* copied from it.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 025: carry pose by held item class, whole-body load blend (knees/lean/sag/waddle), idle doze. Story 022: the `PlayerView` scene and pose. 
- Animation LOD driver (LOD0/1/2 rates, off-screen skip) and the 48-character budget gate: M1 (ADR-0029). Emotes (Comms), knockdown set, clock-in crate, bubble-wrap shell, on-break pose (M-1b/M0), VFX/audio, art-bible polish, Postal Sprint streaks (Perks, M2+).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshots in `production/qa/evidence/` + sign-off in `production/qa/evidence/player-rig-gait-evidence.md`; `prototypes/spike-animationtree/REPORT.md`; `tests/unit/player/GaitRateTests.cs` (pure rate clamp) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 022 (`PlayerView`, `RigOffset`), Story 020 (`ICarrierView`), `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (scene root), `production/epics/godot-host-shell/story-009-entity-view-registry-pools-destination-views.md` (view registry), `production/epics/content-data/story-011-cv-plr-rules.md` (context only: CV-PLR-03, the `gaitPlaybackMax` rule, is deferred to M1 there — the playback clamp constant lives here until that rule lands)
- Unlocks: Story 025; ADR-0029 (Animation & camera) can be written from the spike report; Manual Delivery reach layer, Throwing throw/catch one-shots
