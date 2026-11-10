# Story 002: `DeliverScan` (one scan order for host and client) and the pure deliver preview (MD3) with F1 figures

> **Epic**: Manual Delivery
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~250 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-11-09 (M-1a package cleanup: Complete; delivered by PR #88, merge 7cd3756; see Completion Notes).

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

**Status**: [ ] Not yet created

---

## Completion Notes

*M-1a package delivery, recorded by Mail Lead's cleanup pass 2026-11-09 (owner ruling 2026-11-08 10:18 PM PT, card t299s1348: "Yes, switch to one PR per package and update the story files in one pass at the end"). Uncredited Mail Dev detail unless a quote says otherwise.*

- **Delivered by:** PR #88 (merge `7cd3756`), package WP-C (M-1a wave 1), merged 2026-11-09 under the merge rule "Merge on Review READY plus green CI, and only flag ADR changes to me" (Evan 2026-11-08 8:52 PM PT, card t292s1310). Consumer wiring: PR #94 (merge `f145219`) feeds `DeliverPreview` from the real own-panel mirror (`OwnPanelMirror`) in the client composition.
- **Built:** `DeliverScan`, `DeliverPreview`, `DeliverBranch` in `Sim/Delivery`; the `IPanelView` / `PanelSection` / `PanelEntryFacts` ports in `Sim/ClientLayer/Ports/`. No greybox trims in the WP-C plan §3 table for this story.
- **ACs met:** AC-1, AC-2, AC-3, AC-4, AC-5.
- **Deferred or partly met (greybox trims, Mail Lead planning default, M-1a regroup plan §3 simplify table; held by the package TD row):** none dropped. As-built notes → TD-220 (1)–(5): no `RunFacts` type yet (the preview takes `currentShift` + `MailBalance` directly), the hotbar section is indexed by slot, and the value-hint runs are carried as `PanelHintRun`.
- **Tests:** PR #88 names no test file per story; its body reports 6,256 tests, 0 failed on all four legs. The repo holds `tests/unit/delivery/DeliverPreviewTests.cs` and `tests/unit/delivery/DeliverScanTests.cs`.

---

## Dependencies

- Depends on: `production/epics/mail-destinations/story-003-evaluate-and-effective-state.md`, `production/epics/mail-destinations/story-004-mailpay-f5-f8-f10.md`, `production/epics/mail-destinations/story-010-client-mirrors-and-facts.md`
- Unlocks: Story 003, Story 004, Story 005
