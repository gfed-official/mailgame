# Story 002: `DeliverScan` (one scan order for host and client) and the pure deliver preview (MD3) with F1 figures

> **Epic**: Manual Delivery
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~250 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-11-08 (implemented with grid-inventory 022 in one PR; see Completion Notes)

## Context

**GDD**: `design/gdd/manual-delivery.md` (MD3 the deliver preview; MD10; F1 deliver preview figures)
**Requirement**: `TR-delivery-003` (deliver preview is a pure function, run every focused tick in the ClientLayer `Previews` phase), `TR-delivery-013` (preview money in integer cents, one RH per unit), `TR-delivery-014` (entry scan order cursor → hotbar → inventory → backpack equals host consumption)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0016: ClientLayer, Previews and Presentation Timing (primary: §3 shared pure functions over facts; the one `DeliverScan.Enumerate(IPanelView, …)` helper; preview money = Σ per-unit `MailPay.UnitPay`); ADR-0017 (`Evaluate`, `MailPay`)
**ADR Decision Summary**: Shared functions are compiled once in the Sim and take facts structs or spans, never a system, mirror or `SimWorld`. Preview money is the sum over units of `MailPay.UnitPay(...)`, rounded once per unit exactly as F10, never one rounding over a total. The scan order is cursor → hotbar → inventory → backpack, row-major, produced by one Sim helper, `DeliverScan.Enumerate(IPanelView, ...)`. The host's `deliver_matching` uses the same helper over authoritative containers.
**ADR Version**: ADR-0016 2026-10-16; ADR-0017 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: pure shared functions live in the Sim and are called by both `SimWorld` and `ClientLayer` (ADR-0003 §6); `Evaluate`/`MailPay` are static, pure, 0-alloc (ADR-0017)
- Forbidden: a client copy of `Evaluate` or pay; one rounding over a stack total; a cache of the preview (MD3: evaluated every focused tick)
- Guardrail: ≤ 26 entries scanned and ≤ 6 `Evaluate` predicates per scanned unit per focused tick, 0 B (ADR-0016 §7)

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md` (FX-MB #17 at 17 Elm St, FX-L letter 8 ¢ / 4 ¢, FX-P panel), scoped to this story:*

- [x] **AC-1 (M-MD3-1, 7 rows)** REJECT with 0 panel entries scanned when #17 is CLOSED and matches exist; MATCH when a 17 Elm St letter is only in the backpack; MATCH when the only match is the selected entry; MATCH when the match is the cursor entry with the panel closed; MATCH when hands are selected and a match exists; MISDELIVER_ONE when there is no match and the selected slot holds a letter for 14 Larch Lane; NOTHING when there is no match and a tool is selected.
- [x] **AC-2 (M-MD3-2)** Two matching letters (value 9, deadline 1, shift 3) → MATCH with `deadOnly` true and `predictedPay` 0. A selected Cursed non-match → `cursed` true; a non-Cursed one → false.
- [x] **AC-3 (M-F1-1)** Shift 3, entries A (6 letters, value 8, deadline 3), B (one entry with runs: 3 letters value 8 deadline 2; 2 letters value 9 deadline 1), C (5 letters, value 9, deadline 3) → `k` = 3, `n` = 16, `predictedPay` = 105, `lateUnits` = 3, `deadUnits` = 2. **b** Stamp Collector 1250 turns an on-time 8 into 10; Cannon Arm 1100 changes nothing (hand path).
- [x] **AC-4 (M-F1-2)** A selected letter whose run 0 has value 8 → penalty 4 ¢; runs (value 8, value 12) still give 4; value 1 gives 1. At wallet −496, `floorRefusal` is false (lands on −500); at −497 it is true.
- [x] **AC-5 (M-MD10-1a, M-MD8-1a, M-INT-1 order half)** Matching mail only in an open chest → NOTHING (the external container is never scanned). Hands selected with 5 matching letters in the backpack → MATCH with `n` = 5. With matches at the cursor, hotbar slot 2, inventory (r0, c1) and the backpack, the MATCH entry list is in cursor → hotbar → inventory row-major → backpack order.

---

## Implementation Notes

*Derived from ADR-0016 §3 and GDD MD3/F1 (read the F1 block; do not paraphrase it):*

- **Files:** `src/PerformativeMail.Sim/Delivery/DeliverScan.cs` (`static` enumerator over `IPanelView` sections in the fixed order, row-major within each; ref-struct iterator, 0 B), `Delivery/DeliverPreview.cs` (`static DeliverBranch Preview(in DestinationFacts d, IPanelView panel, int selectedSlot, in RunFacts run, IStatSheet stats, Cents wallet, Cents walletFloor, ref DeliverPreviewScratch s)`), `Delivery/DeliverBranch.cs` (`REJECT | MATCH | MISDELIVER_ONE | NOTHING` plus payload: entry refs (fixed 26-capacity inline array), `k`, `n`, kinds-present mask, `predictedPay` `Cents`, `lateUnits`, `deadUnits`, `deadOnly`; or the selected entry, its address, `predictedPenalty`, `floorRefusal`, `cursed`).
- **`IPanelView`** is ADR-0016's port (`EntryCount(PanelSection)`, `Entry(PanelSection, i) → PanelEntryFacts`, `CursorSlot`). If it does not exist yet, declare it in `Sim/ClientLayer/Ports/` exactly as ADR-0016 Key Interfaces shows. The client implementation is the grid-inventory M-1a own-container mirror (`production/epics/grid-inventory/story-023-panel-grid-view-hotbar.md`). Tests use an in-memory `IPanelView`. `PanelEntryFacts` must carry the kind key (`EntryRecord.MailKind` when `EntryRecord.IsMail`, else `EntryRecord.ItemId`; `src/PerformativeMail.Sim/Inventory/Storage/EntryRecord.cs` l.44–47 — Approved by Evan 2026-10-23 8:20 AM PT, TD-172 D4; same text as grid-inventory 022), category, `AddressId`, flags (raw `byte`), unit count, `entryId` and the value-hint runs that F1 needs. It uses no `MailDestinations` type, so the host view (grid-inventory 022) can fill it. `CursorSlot` is the hotbar slot index (0–7) the cursor entry was picked from, else −1 (TD-172 D2, Approved by Evan 2026-10-23 7:56 AM PT (Mail Lead chat): "CursorSlot as the source hotbar slot or −1"; same text as grid-inventory 022).
- **Order of checks** (first match wins): (1) REJECT if the destination state is CLOSED or LOCKED (`Evaluate`'s mailbox-level steps; no panel scan); (2) MATCH if any scanned entry evaluates to Accept; (3) MISDELIVER_ONE if the selected hotbar slot holds mail evaluating to Misdeliver; (4) NOTHING. A selected-entry `wont_fit` is dormant for houses in v1; report it as its own branch flag for the prompt, with the hold blocked.
- **Money:** `predictedPay = Σ_units MailPay.UnitPay(...)` over each entry's hint runs (`thrownPm = 1000`), using `MailPay.Timeliness` with the client's current shift. `predictedPenalty = MailPay.UnitPenalty` of the oldest unit (run 0). `floorRefusal = !MailPay.WalletGuard(wallet, penalty, floor)`.
- **Facts on the client** come from `production/epics/mail-destinations/story-010-client-mirrors-and-facts.md`'s constructors. This story's function takes facts only, so the host handler (story 005) can call `DeliverScan` over its own `IPanelView`.
- **Test seams:** an evaluation counter and a scanned-entries counter, compiled into test builds only (`[Conditional]`), as MD3 requires.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 003: binding the preview at `PreviewSubStep.DeliverPreview`, the per-tick evaluation (M-MD3-3) and the settle tracker.
- Story 005: the host's use of `DeliverScan`. Story 007: M-INT-1/M-INT-2 against the real host.
- Prompt text and keys (story 010).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/delivery/DeliverPreviewTests.cs` and `tests/unit/delivery/DeliverScanTests.cs` — must exist and pass

**Status**: [x] Created and passing — `tests/unit/delivery/DeliverPreviewTests.cs`, `tests/unit/delivery/DeliverScanTests.cs` (test double: `tests/unit/delivery/InMemoryPanelView.cs`). All four CI legs green locally (see Completion Notes).

---

## Dependencies

- Depends on: `production/epics/mail-destinations/story-003-evaluate-and-effective-state.md`, `production/epics/mail-destinations/story-004-mailpay-f5-f8-f10.md`, `production/epics/mail-destinations/story-010-client-mirrors-and-facts.md`
- Unlocks: Story 003, Story 004, Story 005

---

## Completion Notes

**Completed**: 2026-11-08 (with `production/epics/grid-inventory/story-022-deliver-matching-handler.md`, same PR)

**Files added**
- `src/PerformativeMail.Sim/ClientLayer/Ports/IPanelView.cs` — the ADR-0016 Key Interfaces port, exactly `int EntryCount(PanelSection s); PanelEntryFacts Entry(PanelSection s, int i); int CursorSlot { get; }`.
- `src/PerformativeMail.Sim/ClientLayer/Ports/PanelSection.cs` — `Cursor, Hotbar, Inventory, Backpack` (scan order).
- `src/PerformativeMail.Sim/ClientLayer/Ports/PanelEntryFacts.cs` — the story's listed fields only: `EntryId`, `KindKey` (TD-172 D4), `Category`, `Address`, `Flags` (raw byte), `Units`, the value-hint runs (`RunCount`, `Run(i)`; at most 5, inline). Runs use a port-local `PanelHintRun` (same three values as Mail's `HintRun`) so the port names no `MailDestinations` type.
- `src/PerformativeMail.Sim/Delivery/DeliverScan.cs` — `DeliverScan.Enumerate(IPanelView)`, ref-struct iterator (cursor → hotbar → inventory → backpack; `Section`/`Index` locate each entry); `MaxEntries = 26`.
- `src/PerformativeMail.Sim/Delivery/DeliverBranch.cs` — `DeliverBranchKind { Reject, Match, MisdeliverOne, Nothing }` and the `DeliverBranch` payload (26-entry inline id list, `K`, `N`, `KindsPresent`, `PredictedPay`, `LateUnits`, `DeadUnits`, `DeadOnly`; `SelectedEntry`, `SelectedAddress`, `PredictedPenalty`, `FloorRefusal`, `Cursed`, `WontFit`; `RejectReason`).
- `src/PerformativeMail.Sim/Delivery/DeliverPreview.cs` — `DeliverPreview.Preview(...)` and `DeliverPreviewScratch` (the MD3 evaluation and scanned-entries counters, `[Conditional("SIM_ASSERT")]`, so they count in Debug/ReleaseAssert and stay 0 in Release).

**Tests added** (all `PerformativeMail.Sim.Tests.Unit.ManualDelivery.*`)
- `DeliverPreviewTests.Preview_FxPRows_GiveTheMd3Branch_AndClosedScansNothing` (7 rows) — AC-1. The CLOSED row asserts 0 panel reads on the double (every build) and 0 on the scanned-entries counter.
- `DeliverPreviewTests.Preview_TwoDeadLetters_IsMatchWithDeadOnly_AndPaysNothing` — AC-2 first half.
- `DeliverPreviewTests.Preview_SelectedNonMatch_ReportsCursedFromItsFlags` (2 rows) — AC-2 second half.
- `DeliverPreviewTests.Preview_F1Example_ThreeEntries_Give16Units_105Cents_3Late_2Dead` — AC-3.
- `DeliverPreviewTests.Preview_OnTimeLetterWorth8_PaysPerTheSheet_AndCannonArmNeverAppliesToAHand` (3 rows) — AC-3 b.
- `DeliverPreviewTests.Preview_SelectedMisdelivery_PricesRun0_AndRefusesBelowTheFloor` (5 rows) — AC-4.
- `DeliverPreviewTests.Preview_HandsSelected_FiveMatchingLettersInTheBackpack_IsMatchWithN5` — AC-5 (M-MD8-1a).
- `DeliverScanTests.Enumerate_MatchesInEverySection_YieldsCursorHotbarInventoryBackpack_AndTheMatchListKeepsIt` — AC-5 (M-INT-1 order half).
- `DeliverScanTests.Preview_OverTheHostView_MatchingMailOnlyInAnOpenChest_IsNothing` — AC-5 (M-MD10-1a), over the real host view of grid-inventory 022.

**Deviations / decisions (uncredited; Mail Dev, not owner rulings)**
- **`RunFacts` not declared.** `mail-destinations/story-010` does not specify its shape, so per the M-1a regroup plan §3 the signature takes plain inputs: `Preview(in DestinationFacts d, IPanelView panel, int selectedSlot, int currentShift, MailBalance balance, IStatSheet stats, Cents wallet, Cents walletFloor, ref DeliverPreviewScratch s)`. `MailBalance` (immutable content record) supplies `deadLetterShifts`, `misdeliveryPenaltyRatio` and `minPenaltyCents`, which F1 needs and the story's signature did not name. Story 010 may replace `currentShift` + `balance` with a `RunFacts` later.
- **Hotbar section is indexed by slot.** `EntryCount(Hotbar)` is the slot count; `Entry(Hotbar, i)` is the entry covering slot `i` (default facts when empty). That way `selectedSlot` reads directly without adding a position field to `PanelEntryFacts`. `DeliverScan` skips empty slots and yields an entry that covers several slots only once. The other sections are dense, in row-major anchor order. Documented on `IPanelView`; grid-inventory 023's mirror must follow it.
- **REJECT goes through `MailRules.Evaluate`.** It is called with a unit-less probe, and its `Closed` / `NotOnRoute` reason is read, so the state check is not copied.
- **`wont_fit`** (selected **mail** whose kind the type does not accept) is reported as `MisdeliverOne` with `WontFit = true` and no penalty. A selected non-mail item is `Nothing`. The alternative was `Nothing` with a flag.
- **One `Evaluate` per scanned entry**, on its run-0 unit. Kind, category, address and flags are entry-wide, so the result is the same for every unit, and 1 ≤ 6 predicates per unit. Pay is `Σ_runs Count × MailPay.UnitPay` (one F6 rounding per unit; equal to the per-unit sum because a run's units are identical). `N` is the entry's unit count. Late and dead come from `MailPay.Overdue` over the runs.
- **An entry with no runs** (the host view, or a client entry with no hint yet) prices at 0 ¢. As the selected misdelivery it gives `UnitPenalty` of spawn value 0, which is `minPenaltyCents`.
- **The MailPay numerics sink is a dropped `default(SimCounterSink)`** (display-only, inside documented ranges), the same pattern as the geometry helpers.

**Simplifications (M-1a regroup plan §3)**
- No new `AllocAssert` / 0-alloc test (simplified per M-1a regroup plan §3: the 0 B guardrail test was dropped; the code is still allocation-free: ref-struct iterator, inline arrays, no LINQ or boxing).

**Test evidence (2026-11-08, local, warning-clean builds):** 5,971 tests per leg, 0 failures. net10.0 Debug 5,904 passed / 67 skipped; net10.0 Release 5,964 / 7; net8.0 Debug 5,904 / 67; net8.0 Release 5,964 / 7. `tools/ci/check-sim-access.sh` clean; `tools/ci/check-analyzer-fixtures.sh` 128 fixtures, 0 failures.

