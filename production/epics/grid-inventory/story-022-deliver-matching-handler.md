# Story 022: Host panel view — Inventory's read-only scan primitive for `deliver_matching` (the authoritative `IPanelView`)

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~120 lines prod + ~200 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (re-scoped to unblock it, per the 2026-10-16 `/architecture-review` block note. The old title was "DeliverMatching handler — the one deliver hold (matches across cursor/panel; one-unit misdeliver fallback)". The handler, its ACs H-R7-6, H-R7-7, H-R7-11 and H-X-1 a–c, and the wallet cases are already in `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md` ("moved from grid-inventory story 022"). This story keeps only the Inventory primitive that ADR-0009's 2026-10-16 ownership erratum assigns to Inventory: the read-only match scan over the actor's authoritative containers. `ConsumeUnit`, version bumps and seals are already Complete in stories 005, 015 and 017.)

> **History — BLOCKED 2026-10-16 (/architecture-review): re-scope needed.** The handler as first written ("Inventory owns the handler", calling Mail's Evaluate/consume) violated ADR-0017: Inventory **must never** call Mail. **Resolved 2026-10-23** by the re-scope above. Nothing in this story calls Mail.

## Context

**GDD**: `design/gdd/grid-inventory.md` R7 `deliver_matching` row (scan order); `design/gdd/manual-delivery.md` MD6 (host handling)
**Requirement**: `TR-delivery-014` ("Entry scan order cursor→hotbar→inventory→backpack equals host consumption"), the Inventory half; `TR-inventory-001`
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0016: ClientLayer, Previews and Presentation Timing — Accepted 2026-10-16 (primary: l.117 "The host's `deliver_matching` consumption uses the same helper over authoritative containers"; Key Interfaces l.220 `IPanelView`); ADR-0009: InventoryOp Pipeline — Accepted 2026-10-16 (Erratum 2026-10-16, `DeliverMatching` ownership: "Inventory supplies the read-only match scan"); ADR-0017 — Accepted 2026-10-16 (Inventory never calls Mail)
**ADR Decision Summary**: Scan order is cursor → hotbar → inventory → backpack, row-major. One Sim helper, `DeliverScan.Enumerate(IPanelView, …)` (manual-delivery story 002), produces it. The client reads it over the mirror (`IPanelView` from `ContainerMirror`), and the host reads it over the actor's authoritative containers. This story builds the host `IPanelView`. It is read-only: no version bump, event, ledger write or id allocation.
**ADR Version**: ADR-0016 2026-10-16; ADR-0009 2026-10-16 + errata 2026-10-16 and 2026-10-22; ADR-0017 2026-10-16

**Engine**: Godot 4.7.1 (none — Godot-free Sim) | **Risk**: LOW
**Engine Notes**: None (Sim, net8.0, no Godot types).

**Control Manifest Rules (Core layer)**:
- Required: Inventory exposes read access through `IInventoryApi` only; feature systems never read Inventory storage directly (ADR-0009)
- Forbidden: Inventory → Mail calls or references (ADR-0017); allocation on the tick path after warm-up
- Guardrail: a ≤ 26-entry scan stays allocation-free (manual-delivery story 002)

---

## Acceptance Criteria

- [ ] **Scan order (TR-delivery-014, Inventory half).** The actor's state is a cursor entry, hotbar slots 0–7 with slot 2 and slot 5 filled, and inventory entries placed out of row-major order (anchors (3,1), (0,0), (5,0); `player_inventory` is 8×2, `BuiltInContent.cs` l.88). `DeliverScan.Enumerate(view)` over the host view yields the cursor, then hotbar 2, hotbar 5, then inventory (0,0), (5,0), (3,1). Row-major means by anchor row, then column. This order matches the order an in-memory client `IPanelView` built from the same fixture yields (manual-delivery story 002's test double). If manual-delivery 002 has not landed, assert the section contents through `EntryCount`/`Entry` directly, add the `DeliverScan.Enumerate` parity assertion when 002 lands, and say so in the completion notes.
- [ ] **Facts.** Each `PanelEntryFacts` carries the authoritative entry's kind, category, `AddressId`, flags, unit count and `entryId`. Value-hint runs are left **empty** on the host, because money is Mail's `ConsumeHand` (ADR-0017) and Inventory never reads Mail. Flags are the raw `byte` of `EntryRecord.Flags`, and category is derived from `EntryRecord.IsMail`. `PanelEntryFacts` uses no `MailDestinations` type.
- [ ] **Cursor slot.** A cursor entry picked from hotbar 5 → `CursorSlot == 5`. Picked from inventory (2,0) → −1. Empty cursor → −1.
- [ ] **Backpack.** `EntryCount(Backpack) == 0` at M-1a (the backpack is M-1b). Before the slot's first `PlayerJoin`, every section reports 0.
- [ ] **Read-only.** A full enumeration leaves every container `version`, the `UnitLedger` counts, the event log length and the entry-pool free count unchanged.
- [ ] **Live view.** After `ConsumeUnit` removes the last unit of an inventory entry, the next enumeration no longer yields it, and it does so without rebuilding the view.
- [ ] **Allocation.** Getting the view and enumerating 26 entries allocates 0 B after warm-up (`AllocAssert.Zero`; Release-only skip allowed per the owner skip rule).
- [ ] **Isolation.** `SystemIsolationRulesTests` and `CompositionRulesTests` still pass. A reflection test in `HostPanelViewTests` asserts that no field, property, parameter or return type of `HostPanelView` (nested types included) is in `PerformativeMail.Sim.MailDestinations` or `PerformativeMail.Sim.Delivery`.

---

## Implementation Notes

- **API (Mail Lead planning default, pending Evan; TD-172 D1):**
  - Add `IPanelView HostPanelView(PlayerSlot slot)` to `IInventoryApi`. It returns one **per-slot instance preallocated at construction** (one per `PlayerSlot.Count`, reading `_panels[slot]` live). Instances are never created at `PlayerJoin` or released at `PlayerLeave`: a leave keeps the slot's containers (`OnPlayerLeft` only clears the external; `InventorySystem.cs` l.1384–1414), and a rejoin reuses them. A call never allocates. `Entry(s, i)` over a live grid may keep a small per-slot ordered index, refreshed when the container `version` changes.
  - The implementation is `internal sealed class HostPanelView : IPanelView` in `src/PerformativeMail.Sim/Inventory/HostPanelView.cs`. It reads the slot's cursor, hotbar and inventory containers live (not a snapshot).
- `IPanelView`, `PanelSection` and `PanelEntryFacts` are declared by manual-delivery story 002 in `Sim/ClientLayer/Ports/`, exactly as ADR-0016 Key Interfaces shows. If 002 has not landed, declare them there with the same shape and say so in the completion notes. Do not invent different fields.
- **`CursorSlot` (Mail Lead planning default, pending Evan; TD-172 D2):** the hotbar slot index (0–7) the cursor entry was picked from (`TryGetCursorOrigin`, where the origin container is the slot's hotbar → origin `X`), else −1 (empty cursor, or picked from the inventory or an external). Manual-delivery story 002 adopts the same definition for the client view.
- The host handler (manual-delivery story 005) calls `DeliverScan.Enumerate(inventory.HostPanelView(actor), …)`, then evaluates matches through Mail itself. This story never decides a match.

---

## Out of Scope

- The `DeliverMatching` op, descriptor, payload, handler, `IPlayersApi.CheckInteraction` and the wallet floor: `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md`. That includes H-R7-6, H-R7-7, H-R7-11 and H-X-1 a–c, which moved there.
- `ConsumeUnit`, version bumps and seals (stories 005, 015, 017: Complete). The client mirror's `IPanelView` (story 023). The backpack (M-1b).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* QL-STORY-READY: re-run at `/story-readiness` 2026-10-23 after the re-scope.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/HostPanelViewTests.cs` (scan order, facts, cursor slot, backpack, read-only, live view, allocation, isolation) must exist and pass.
- Test policy (`.claude/rules/test-standards.md`): during development run only this story's test classes on net10.0 Debug. At close, run the full suite once on net10.0 Debug and once on net8.0 Release. CI must be green on all four legs.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 015 (`IInventoryApi`, `ConsumeUnit`: Complete), Story 017 (`UnitLedger`: Complete); `production/epics/manual-delivery/story-002-deliver-scan-preview-f1.md` (`IPanelView`, `PanelEntryFacts`, `DeliverScan`: Ready)
- Unlocks: `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md` (the host scan input)
