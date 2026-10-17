# Story 005: Post Office record and spawn-pad slots (8 × 1200 mm) with build-time validation

> **Epic**: World Stub — Hand-Authored Street (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~150 lines prod + ~200 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: none for World Generation. Consumers: `design/gdd/player-character-movement.md` P17 (one respawn point: the Post Office spawn pad, 8 ring slots 1.2 m around it), F8 (slot choice and the Post Office door fallback), fixture FX-Pad (pad at (0,0), 8 slots at 1200 mm), L489 (slots above swim depth); `design/gdd/grid-inventory.md` / `design/gdd/mail-destinations.md` (`po_intake` at the Post Office).
**Requirement**: none registered. **Untraced:** PC P17/F8 (spawn pad geometry and validation) has no TR-ID in `docs/architecture/tr-registry.yaml`. ADR-0026 §7 decides it. Flagged for the next `/architecture-review`; this story does not add registry entries. Related: `TR-movement-018` (the "above swim depth" check uses the 900 mm swim-enter depth).
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0026: World Query Contract (`IWorldQuery`) and the M-1a Street Stub (primary); ADR-0013 §3 (BAM axis convention, `TrigQ15`); ADR-0006
**ADR Decision Summary**: `PostOffice` is one record `{ DoorAnchor, IntakeAnchor (po_intake), PadCentre, PadRingRadiusMm = 1200, PadSlots = 8 }`. Pad slot k sits at angle k × 45° using the BAM trig LUT. The builder asserts every slot is `Walkable`, outside every `StaticSolid` and above swim depth. If any slot fails, the build fails. The runtime fallback (PC F8) is `DoorAnchor`.
**ADR Version**: ADR-0026 2026-10-16; ADR-0013 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Core layer + Global)**:
- Required: `WorldTables` immutable after `Build()`; validation failures are `SimBuildException` at boot, never in a tick (ADR-0026 §1, §7; ADR-0003)
- Required: angles are BAM; trig goes through `TrigQ15` (ADR-0013 §3)
- Required: hash, RNG, serial and BAM arithmetic is always inside `unchecked(...)` (Global)
- Forbidden: float trig; a second pad table outside `WorldTables`; new `IWorldQuery` members (ADR-0026 §10). The slot helper is a static function, not an interface member

---

## Acceptance Criteria

*From ADR-0026 §7 and PC FX-Pad / P17, scoped to this story:*

- [x] **AC-1 Slot positions.** With `PadCentre` = (0, y, 0) (FX-Pad), `PadSlots.Position(in PostOfficeRecord, k)` for k = 0…7 returns x/z = `PadCentre + MulDivRH(1200, f, 32767)` per axis, where `f` is ADR-0013 §3's `forward(k · 8192 BAM)` = (−sin, −cos) from `TrigQ15`. Slot 0 is (0, −1200) and slot 2 is (−1200, 0). Every slot's y is `TerrainHeightMm` at that point. Every slot lies 1200 ± 1 mm from the centre.
- [x] **AC-2 Validation.** The build fails with `SimBuildException` when any slot is on a non-`Walkable` tile, when any slot lies inside a `StaticSolid` AABB, or when any slot has `WaterDepthMm ≥ 900`. It also fails when `SetPostOffice` is never called or is called twice, or when any anchor is outside `Bounds`. A pad on flat dry ground with no solids builds, and `IWorldQuery.PostOffice` returns the record unchanged (`PadRingRadiusMm` 1200, `PadSlots` 8).

---

## Implementation Notes

*Derived from ADR-0026 §7 and ADR-0013 §3:*

- **Files:** `src/PerformativeMail.Sim/World/PadSlots.cs` (`static Vec3Mm Position(IWorldQuery w, in PostOfficeRecord po, int k)`, or a two-step x/z then height; keep the height through `IWorldQuery`); `SetPostOffice(Vec3Mm door, Vec3Mm intake, Vec3Mm padCentre)` on the builder. `PadRingRadiusMm` and `PadSlots` are fixed at 1200 and 8 by ADR-0026 §7 / PC P17.
- **Angle direction (story-level decision, logged in the decision log):** ADR-0026 says "k × 45°" without an axis. This story uses the Sim-wide convention (ADR-0013 §3: yaw 0 faces −Z, positive BAM counter-clockwise from +Y) and places slot k along `forward(k·8192)`. The carrier's `Teleport` yaw can then face outward with the same BAM. PC F8's rotation (start index, occupancy) is the player-character epic's; this story only fixes the geometry.
- Q15 scale: use the scale `TrigQ15` documents (`production/epics/simulation-core/story-005-bam-trigq15-stablesort.md`). The formula above writes 32767 because ADR-0013 §3 normalises by `32767`. If the table's unit is 32768, follow the table and adjust the expected values (±1 mm). One `MulDivRH` per axis.
- **Inside a solid:** a slot point is inside if it lies within an AABB's closed x/z interval and its y is within the box's y interval. Use `QueryStaticSolids` with a degenerate region at the point (story 003), not a separate scan.
- The 900 mm threshold is PC's `swimEnterMm`, passed into `Build()` with the other `players` settings (see story 002's note). It is not a literal.
- `IntakeAnchor` is where Presentation and the interaction framework place `po_intake`. This story stores it and checks it is in bounds. Inventory's Intake container is not created here.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- player-character epic: slot rotation, occupancy against living capsules, depenetration and the runtime `DoorAnchor` fallback (PC F8, PCM-P17-2).
- Story 006: authoring the street's Post Office and pad, and the FX-Pad fixture.
- grid-inventory: the `po_intake` container itself.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/world/PostOfficePadTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/world/PostOfficePadTests.cs (+ shared tests/unit/world/PadTestKit.cs)

---

## Dependencies

- Depends on: Story 002 (`Walkable`, `Bounds`), Story 003 (solid query); `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md` (`TrigQ15.Sin/Cos`)
- Unlocks: Story 006; player-character respawn and clock-in (P17/F8)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 2/2 passing (AC-1 → FX-Pad all 8 offsets incl. ±849 mm diagonals with TrigQ15.One = 32767, slot 0 (0,−1200), slot 2 (−1200,0), 1200 ± 1 mm, y = TerrainHeightMm incl. a planar slope, k wrap; AC-2 → slot on Blocked/steep/off-grid tile (40), inside a solid on every closed face (41), depth 899 pass / 900 fail against the swimEnter parameter incl. a fixture where only slots 5–7 are deep (42), never set on the composition path (37), set twice on every overload (38), each anchor outside Bounds (39), `Build()` with a Post Office (43), slot failures also on `Build(int,int)`, happy path returns the record unchanged, failed pad allocates no Destination ids)
**Deviations**: AC-2 narrowing (orchestrator decision, LP-endorsed — ADR-0026 names `Build(EntityIds…)` as the composition path): "never called" fails only `Build(EntityIds, int, int)`; `Build(int,int)` keeps the Post Office optional but validates it when set; `Build()` rejects one. Advisory: slot check order depth → walkable → solid; anchors also |y| ≤ MaxCoordMm. SimBuildErrorCode 37–43 appended. WorldLotsTests' rig now sets a valid pad (intent unchanged)
**Test Evidence**: Logic: tests/unit/world/PostOfficePadTests.cs (20 methods) — full suite 2 182/leg, 0 failed (net10/net8 × Debug/Release; Debug 15 skipped)
**Code Review**: Complete — LP-CODE-REVIEW APPROVE; godot-csharp-specialist APPROVE; QL-TEST-COVERAGE GAPS (gaps 1–2 fixed in-story); advisories → TD-058; TD-028 item 1 complete (no IWorldQuery stubs remain)
**Run result**: N/A — pure Sim library (no scene, no rendering); nothing observable on screen
