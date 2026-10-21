# Story 020: Drop op — whole entry to a WorldItem at +500 mm along facing, ground-snapped; swimming refused

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S–M (~0.5 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

> **Unblocked 2026-10-16**: ADR-0026 (`IWorldQuery` + M-1a street stub) and ADR-0013 are Accepted. Use `IWorldQuery.TrySnapWalkable`/`WaterDepthMm` (ADR-0026 §3–§4) for snap and water. Re-derive at `/story-readiness`.

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-013` (WorldItem creation via drop)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0026: World generation pipeline, tile grid & routing graph (M-1a stub `IWorldQuery`) — Accepted 2026-10-16 (primary); ADR-0013: Carrier kinematic solver & fixed-point geometry — Accepted 2026-10-16; ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: Drop spawns one WorldItem (`origin = Dropped`) at the player's position + 500 mm along facing, snapped to the nearest ground tile (water → floats 10 s then returns/destroys; inside non-walkable geometry → the player's own position); rejected while swimming (`swimming`) (GDD R7 drop row). The ground/water/walkability query is `IWorldQuery` (ADR-0026 stub section, Accepted 2026-10-16); the player pose, facing and swim state are the carrier solver's (ADR-0013, Accepted 2026-10-16).
**ADR Version**: ADR-0026 2026-10-16; ADR-0013 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (Sim half) — re-assess when the blocking ADRs are written
**Engine Notes**: Re-derive when ADR-0026/0013 land.

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R7-4 (drop half)** — `isSwimming = true` and a letter on the cursor → `drop` → `ok:false, swimming`, no WorldItem; `isSwimming = false` → exactly one WorldItem `origin = Dropped` at position + 500 mm along facing, snapped to ground, `expiresTick = tick + 9000`.
- [ ] A focused-cell drop (panel entry, no `pick` first) is one atomic lift-and-drop of the whole entry; a drop point on water floats (story 013 water class); a point inside non-walkable geometry uses the player's own position.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- When unblocked: the handler reads pose/facing/swim from `IPlayersApi` (ADR-0013/0014 shape) and asks `IWorldQuery` (ADR-0026 stub) for ground snap / tile kind, then calls story 013's `CreateWorldItem`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 013: WorldItem storage/expiry (Ready).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/DropTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 013
- Depends on: `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md`, `production/epics/world-stub/story-002-tile-flags-bounds-clamp-snap.md`, `production/epics/world-stub/story-003-static-solids-4m-query.md` (`IWorldQuery` ground snap / tile kind / solids, ADR-0026); `production/epics/player-character/story-001-players-module-carrier-state-join-binding.md`, `production/epics/player-character/story-009-water-wade-swim-haulout-f7.md` (pose, facing, `isSwimming` via `IPlayersApi`, ADR-0013)
- Unlocks: Story 024
