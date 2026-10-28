# Epic: Grid Inventory & Containers (M-1a slice)

> **Layer**: Core
> **GDD**: design/gdd/grid-inventory.md (In Review — third review revision applied; targeted check pending)
> **Architecture Module**: Sim.Inventory (`src/PerformativeMail.Sim/Inventory/`) + inventory panel view (Godot, one custom-drawn `Control`)
> **Milestone slice**: M-1a "minimal" (GDD Quick reference; UI phase tags "M-1a = hotbar + own panel")
> **Engine Risk**: LOW for the Sim (plain C#, ADR-0009). The panel view (`Control._Draw`, cached `DrawString`, live-region `Control`) is HIGH/UI (NEEDS VERIFICATION; ADR-0019 Accepted 2026-10-16).
> **Est. size**: L — 16–20 stories (Sim first; the panel view after `godot-host-shell`)
> **Implementation priority**: 3 of 9
> **Status**: Ready
> **Stories**: 25 stories — 18 Complete, 6 Ready, 1 Deferred (counts refreshed 2026-10-23 from the story headers, m1a-replan; order in `production/epics/implementation-order.md`) — see the Stories table below

## Overview

Grid Inventory is where every piece of mail and every item lives between spawning and delivery. It is also where "never duplicated, never lost" is enforced. This epic builds its M-1a slice in the Godot-free Sim, under ADR-0009. ADR-0009 fixes the request pipeline: decode, a fixed validation-precedence chain, atomic multi-container commit, a per-container `version`, event records sealed by a version record, then a reply after the events. It also fixes the storage: a struct-array entry pool, occupancy bitmaps, a `mailIds` size-class arena, a StackKey → non-full-stack index with a negative-fit cache, WorldItem expiry FIFOs and the O(1) `UnitLedger` conservation counters checked in `ITickEndCheck`.

**In scope at M-1a:**
- **Containers** (`ContainerDef`/`ContainerState`, R1):
  - `hotbar` (slots layout, slot 0 reserved);
  - `player_inventory`;
  - `cursor`;
  - `po_intake` as the mail spawn and return target (R14 `Spawn` with NoMerge).
- **Entries and stacking:** `entryId`, `count`, ascending `mailIds`, footprint and rotation (R2); StackKey stacking with `effMax` read from `IStatSheet` at op time (R3); placement validity (R4); the insertion whitelist (R5); oldest-first take-k (R6).
- **Ops** (R7), through the Sim Core op FIFO (one `OpKind` per op): `pick`, `move` (including a count-split place), `merge`, `quick_move` (own panel), `return`, `drop`, `pickup`, plus the sync `IInventoryApi` (`Spawn`, `TryInsert`, `TryExtract`, `TakeUnit`, `CountItem`, `TryCatch`). The orchestrated `deliver` and `throw` handlers are owned here, but each is completed with its feature epic: `manual-delivery`, `throwing-catching` and `mail-destinations`. The `deliver_matching` handler is Delivery's (ADR-0009 erratum 2026-10-16, Risk 4 below); Inventory supplies the read-only scan (story 022's `HostPanelView`), `ConsumeUnit` and seals.
- **Placement and WorldItems:** AutoPlace M/U/R (PassFirst, plus TargetFirst for pickup and catch, R8); the quick_move target order for the own panel (R9); the cursor (R11); WorldItem with cap 512, expiry FIFOs and lossless mail eviction (R12); absolute-tick timers with expiry after `InputsApply` (D6).
- **Weight → speed** (R16, D1): integer permille with one division; `weightPoints` int16.
- **Conservation** (R19): the `UnitLedger` invariant and `INVENTORY_ASSERT`; the `Inserted`/`Removed`/`Merged`/`ItemsLost` events (and the `ContainerDelta`/`UnitsRemoved` network batch forms, GDD l.599 / manifest l.380 — wire packing, not events); H-DET-1 (equal SHA-256 dumps after a 50 000-op log).
- **The own-panel view** (Presentation): one custom-drawn grid `Control` (TR-inventory-027), the hotbar strip, and drag/pick/place/rotate/return/drop/quick-move through the router's Panel and Holding contexts.

**M-1a access to the Intake (provisional, story-level).** The R10 open set allows one external container, but the UI phase tags put external containers at M0+. The solo greybox still needs a way to take spawned mail. The recommended path is to open `po_intake` as the single external container. Contention, `take_all` and the manifest view stay deferred. **Confirmed at /create-stories (2026-10-16), decision logged.** See story 011.

**Out of scope:**
- **M-1b:** backpack, bike, truck, chest, depot and sorter-buffer; death and spill bags (R13, with Health & Damage); belt endpoints calling the Intake outfeed (TR-inventory-010; the outfeed itself ships in story 012).
- **M0:** contention and the optimistic client; JoinState and checksums.
- **M1:** perks that grow containers (R15); grants and standard issue (R17); the manifest view (R18); sort (D3); split; take_all.

**Why ADR-0009 governs.**
- Inventory is the most contended and most replicated state. A fixed precedence chain plus per-container versions plus event-before-reply is what lets the optimistic client (M0) reconcile without a redesign.
- O(1) counters make "never duplicated, never lost" checkable every tick rather than only in tests.
- ADR-0004 fixes `entryId` (u32, run-unique, bit 31 for temp ids).
- ADR-0006 fixes weight math and zero allocation.
- ADR-0010 fixes `ContainerDef`/`ItemDef` and the stat keys.

**Risks.**
- (1) Seven TRs are untraced. Sort and backlog structures (TR-inventory-008, 009, 016, 018 → ADR-0025) and JoinState (023, 024 → ADR-0022) fall outside M-1a. The **panel view** (TR-inventory-027 → ADR-0019) is in the M-1a slice, so its stories carry `ADR: N/A` until ADR-0019 lands.
- (2) The GDD awaits a targeted check of 21 contract/fixture fixes. Op payload shapes could move, so stories must cite ADR-0009 payload layouts (padding-free).
- (3) Intake access at M-1a is not explicitly specified (see above).
- (4) **Superseded for `deliver_matching` 2026-10-16 (/architecture-review, ADR-0009 erratum):** the `DeliverMatching` OpKind and handler are Delivery-owned (`Sim/Delivery`, block `0x0100–0x013F`) because ADR-0017 forbids Inventory → Mail calls. Inventory provides the match scan, `ConsumeUnit` and seals, and story 022 was re-scoped 2026-10-23 to the host `IPanelView` scan primitive (Ready). Original text: the `deliver_matching` and `throw` handlers span epics. Ownership: the handler lives in Sim.Inventory, while Mail's `Evaluate`/consume and Throwing's flight creation are called through their `I…Api` in the same phase (ADR-0003 §5).

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0009: InventoryOp Pipeline and Inventory Storage Layout | op kinds, validation precedence, per-container `version`, batch bumps, event-before-reply, oldest-first, `UnitLedger` in `ITickEndCheck`, `INVENTORY_ASSERT`, struct-array storage, arena, StackKey index, expiry FIFOs; optimistic client specified now, built at M0 | LOW |
| ADR-0007: Op Queue, Drain and Replies | 64 B `Op`, drain ≤64/tick, validate-then-mutate, sender stamped from the peer, malformed → `bad_request` | LOW (Sim) |
| ADR-0008: Event Log, Replication Classes and Event Delivery | per-call coalescing, event-first-then-reply on one stream, OwnerOnly/Replicated classes | LOW (Sim) |
| ADR-0004: Identity and Id Spaces | `EntryId` u32 never reused, temp ids `0x8000_0000 \| slot<<20 \| ctr`, WorldItem `EntityId` class | LOW |
| ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay | integer weight→speed, 0-alloc automation path, H-DET-1 dump equality | LOW |
| ADR-0010: Content Format, Loading, ContentSet and StatSheet | `ContainerDef`/`ItemDef` fields; `InventoryRows`, `MailMaxStack`, `WeightPenaltyPm` stats | LOW |
| ADR-0003: Phase Order, Composition Root and Cross-System Calls | phases 0–2 (InputsApply, expiry, backlog drain), tick-end conservation, player op beats inserter | LOW |
| ADR-0011: Network Adapter Boundary | loopback same path; Intake/Depot GroupCounts and pacing (M0) | LOW (seam) |

## GDD Requirements

Source: architecture.md v1.1 Appendix A. **21 / 28 traced.**

| TR-ID | Requirement | ADR Coverage | Slice |
|-------|-------------|--------------|-------|
| TR-inventory-001 | All ops (player and automation requests) enter one FIFO drained in Inputs.Apply; loopback same path | ADR-0009 ✅ | M-1a |
| TR-inventory-002 | Per-container uint32 version +1 per mutating op; batch bumps once per container | ADR-0009 ✅ | M-1a |
| TR-inventory-003 | Entry: uint32 entryId, uint8 count, ascending mailIds[]; footprint w×h, rotation | ADR-0004 ✅ | M-1a |
| TR-inventory-004 | StackKey=(mailKind,addressId,flags); effMax evaluated at op time; ≤255 | ADR-0009 ✅ | M-1a |
| TR-inventory-005 | Take-k removes lowest mailIds (oldest-first); payment per mailId | ADR-0009 ✅ | M-1a |
| TR-inventory-006 | Fixed validation precedence chain of reason codes | ADR-0009 ✅ | M-1a |
| TR-inventory-007 | AutoPlace passes M/U/R; amortised O(1) non-full-stack index | ADR-0009 ✅ | M-1a |
| TR-inventory-008 | Deterministic consolidating first-fit-decreasing sort, total-order comparator | ❌ No ADR (planned ADR-0025, not written) | M1 |
| TR-inventory-009 | Intake sort ≤8,500 probes; memoised repeat O(1) | ❌ No ADR (planned ADR-0025, not written) | M1 |
| TR-inventory-010 | Intake outfeed by arrivalSeq; PeekOutfeed/TakeUnit ≤4 entries amortised | ADR-0009 ✅ | M-1a (story 012) / M-1b (belt selectors) |
| TR-inventory-011 | Backlog drain ≤32 entries/tick, ≤8,500 probes, 0 when idle | ADR-0009 ✅ | M-1a |
| TR-inventory-012 | O(1) conservation counters checked each tick; INVENTORY_ASSERT compiled out of Release | ADR-0009 ✅ | M-1a |
| TR-inventory-013 | WorldItem cap 512, expiry FIFOs, lossless mail eviction | ADR-0009 ✅ | M-1a |
| TR-inventory-014 | Timers as absolute ticks (bag 900, WorldItem 9000, water 300); expiry after Inputs.Apply | ADR-0009 ✅ | M-1a |
| TR-inventory-015 | Integer-only weight→speed (permille, one division); weightPoints int16 | ADR-0006 ✅ | M-1a |
| TR-inventory-016 | Inventory tick share ≤1.0 ms median / 2.0 p99 / 3.0 worst (advisory) | ❌ No ADR (planned ADR-0025, not written) | advisory |
| TR-inventory-017 | Zero managed allocation on automation path | ADR-0006 ✅ | M-1b (automation path) |
| TR-inventory-018 | Managed heap ≤1 MB for containers + 512 WorldItems; 2,800-entry backlog ≤150 KB | ❌ No ADR (planned ADR-0025, not written) | advisory |
| TR-inventory-019 | Reliable ordered delivery; events, replies, snapshots on one channel, event before reply | ADR-0009 ✅ | M-1a |
| TR-inventory-020 | Intake/Depot non-openers get GroupCounts only; ≤40 B/tick/peer | ADR-0011 ✅ | M0 |
| TR-inventory-021 | Bursts ≤12 KB paced ≤800 B/tick; 24 KB high-water → resync-pending | ADR-0011 ✅ | M0 |
| TR-inventory-022 | Optimistic client: pending queue 16, temp ids, dependsOn, baseVersion, 5 s timeout | ADR-0009 ✅ | M0 (specified now, built at M0) |
| TR-inventory-023 | ContainerChecksum every 2 s, order-independent, never GetHashCode | ❌ No ADR (planned ADR-0022, not written) | M0 |
| TR-inventory-024 | JoinState/RejoinState inventory block sizes; ≤64 KB chunks on bulk channel | ❌ No ADR (planned ADR-0022, not written) | M0 |
| TR-inventory-025 | Sender from transport peer, never client playerId; malformed → bad_request, no exception | ADR-0007 ✅ | M-1a |
| TR-inventory-026 | ContainerDef/ItemDef fields and StatSheet keys for inventory | ADR-0010 ✅ | M-1a |
| TR-inventory-027 | Grid panel one custom-drawn Control; 0 B/frame; feedback via live-region Control | ❌ No ADR (planned ADR-0019, not written) | M-1a |
| TR-inventory-028 | SHA-256 state dump equal after 50,000-op log on two Sim instances | ADR-0006 ✅ | M-1a |

**Untraced (7):** TR-inventory-008, 009, 016 and 018 (ADR-0025, M1/advisory); TR-inventory-023 and 024 (ADR-0022, M0); **TR-inventory-027 (ADR-0019, M-1a panel view, in scope)**.

## Stories

**Recommended order** (each story's `Depends on:` field is authoritative):
- 001 → 002 → 003 → 004 → 005 → 006
- then **011**: 007 and 010 need its open-set predicate
- then 007 → 008 → 009 → 010 → 012 → 013 → 014 → 015 → 016 → 017 → 018
- 019–022 became Ready on 2026-10-16 when ADR-0013/0014/0017/0026 were Accepted; 023–024 became Ready on 2026-10-16 when `design/ux/inventory-panel.md` was authored.

**Simulation-core prerequisites:**
- `production/epics/simulation-core/story-002`, `011`, `013`, `015`, `018`, `019`, `020` and `022`
- the planned `SimComposition` story (025)

**Content-data prerequisites:**
- `production/epics/content-data/story-005`, `008`, `014` and `015`

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Inventory module skeleton — container registry, M-1a containers and per-player panel](story-001-inventory-module-containers.md) | Integration | Complete | ADR-0009, ADR-0003, ADR-0010, ADR-0004 |
| 002 | [Entry pool, occupancy bitmaps and entry identity](story-002-entry-pool-occupancy.md) | Logic | Complete | ADR-0009, ADR-0004, ADR-0006 |
| 003 | [mailIds size-class arena — oldest-first take, sorted-union merge, exhaustion](story-003-mailids-arena.md) | Logic | Complete | ADR-0009, ADR-0006 |
| 004 | [Inventory op kinds, padding-free payloads, reason codes and the 24 B reply extra](story-004-op-kinds-payloads-reasons.md) | Logic | Complete | ADR-0009, ADR-0007, ADR-0011 |
| 005 | [InventoryMutator, per-call versions, event records and InvVersion seals](story-005-mutator-versions-events.md) | Logic | Complete | ADR-0009, ADR-0008, ADR-0003 |
| 006 | [Placement validity, StackKey stacking with effMax at op time, and the insertion whitelist](story-006-placement-stacking-whitelist.md) | Logic | Complete | ADR-0009, ADR-0010 |
| 007 | [Pick and Move ops — whole-entry moves, cross-container atomicity, no-op moves](story-007-pick-move-ops.md) | Logic | Complete | ADR-0009, ADR-0007 |
| 008 | [Move split-and-place, hotbar swap from the cursor, and the Merge op](story-008-move-split-swap-merge.md) | Logic | Complete | ADR-0009, ADR-0004 |
| 009 | [AutoPlace (passes M/U/R, PassFirst/TargetFirst), StackKey non-full index and negative-fit cache](story-009-autoplace-stackkey-index.md) | Logic | Complete | ADR-0009, ADR-0006 |
| 010 | [QuickMove (entry form), Return, and the R9 quick_move target order](story-010-quickmove-return-targets.md) | Logic | Complete | ADR-0009 |
| 011 | [Open-set bookkeeping, Close op, leash put-away hook — and the Intake as the M-1a external](story-011-open-set-close-intake.md) | Logic | Complete | ADR-0009, ADR-0008 |
| 012 | [Spawn into the Intake (NoMerge, arrivalSeq) and the outfeed FIFO (PeekOutfeed / TakeUnit)](story-012-intake-spawn-outfeed.md) | Logic | Complete | ADR-0009, ADR-0003 |
| 013 | [WorldItem entity, absolute-tick expiry FIFOs, water float, cap eviction and tombstones](story-013-worlditem-expiry-cap.md) | Logic | Complete | ADR-0009, ADR-0004, ADR-0003 |
| 014 | [Pickup core (AutoPlace by the R9 pickup rows) and the TryCatch API with fumble](story-014-pickup-core-trycatch.md) | Logic | Complete | ADR-0009 |
| 015 | [IInventoryApi hooks for feature epics — ConsumeUnit, DetachForThrow, TryInsert, CountItem](story-015-inventory-api-feature-hooks.md) | Logic | Complete | ADR-0009, ADR-0003 |
| 016 | [Weight → speed (R16, D1): weightPoints and speedPermille](story-016-weight-speed.md) | Logic | Complete | ADR-0006, ADR-0009, ADR-0010 |
| 017 | [UnitLedger conservation counters, tick-end check, INVENTORY_ASSERT full scan and the canonical dump section](story-017-unitledger-conservation.md) | Logic | Complete | ADR-0009, ADR-0006, ADR-0003 |
| 018 | [Conservation fuzz over the M-1a op set (H-R19-1 subset) and H-DET-1 two-world determinism](story-018-conservation-fuzz-determinism.md) | Integration | Ready | ADR-0006, ADR-0009 |
| 019 | [Range-gated Open and Pickup handlers and the leash auto-close (P13 chain, F5 range)](story-019-range-gated-open-pickup-leash.md) | Logic | Ready | ADR-0014, ADR-0009, ADR-0013 |
| 020 | [Drop op — whole entry to a WorldItem at +500 mm along facing, ground-snapped; swimming refused](story-020-drop-op.md) | Logic | Ready | ADR-0026, ADR-0013, ADR-0009 |
| 021 | [Backlog append and the IntakeDrain phase (≤ 32 entries/tick, idle exit O(1))](story-021-backlog-intake-drain.md) | Logic | Complete | ADR-0017, ADR-0009 |
| 022 | [Host panel view — Inventory's read-only scan primitive for `deliver_matching`](story-022-deliver-matching-handler.md) | Logic | Ready | ADR-0016, ADR-0009, ADR-0017 |
| 023 | [Own-panel grid view and hotbar strip — one custom-drawn Control, 0 B/frame](story-023-panel-grid-view-hotbar.md) | UI | Ready | ADR-0019, ADR-0009 |
| 024 | [Panel interactions through the Panel/Holding input contexts, and the M-1a feedback cues](story-024-panel-interactions-feedback.md) | UI | Ready | ADR-0019, ADR-0012, ADR-0009 |
| 025 | [Inventory panel live-region feedback (TR-inventory-027) — the live-region `Control` and th](story-025-deferred-live-region-feedback.md) | UI | Deferred (Evan 2026-10-23) | — |

**Blocked stories and the ADR each waits on.** Each reserved ADR is listed in `production/epics/index.md`.

| Story | Waits on |
|---|---|
| — | None. 022 was re-scoped 2026-10-23 to the read-only match scan (`IInventoryApi.HostPanelView`). `ConsumeUnit` and seals were already Complete in 015/017, and the handler half is `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md` (ADR-0009 erratum; ADR-0017 forbids Inventory → Mail) |

**Not storied at M-1a:**
- sort (D3, ADR-0025);
- split, `take_all`, `MoveGroup`/`QuickMoveGroup`, manifest view (M1);
- backpack, bags/death and automation selectors (M-1b);
- optimistic client, `GroupCounts`, JoinState and checksums (M0).

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/grid-inventory.md` that apply to the M-1a slice are verified: R1 (M-1a containers), R2–R9, R11, R12, R16, R19, D1, D2, D6 and H-DET-1. Criteria for deferred containers and ops stay open on their milestones
- All Logic and Integration stories have passing test files in `tests/` (`tests/unit/inventory/**`, `tests/integration/**`)
- All Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/` (hotbar, own panel, Holding, drop)

## Next Step

**2026-10-23 (m1a-replan, playable-first):** Next, in `production/epics/implementation-order.md` order (track and wave in parentheses; Evan 2026-10-23 7:13 AM PT "reorder playable-first"): Track A (playable greybox): 020 (A1), 019 (A3), 022 (A7), 023 (A10); then Track B: 018 (B1), 024 (B2); Deferred by Evan 2026-10-23 (not scheduled): 025. Earlier notes below are history.

Stories 001–017 and 021 are Complete (006 merged in PR #16, 009 in PR #20, 012 in PR #21, 013 in PR #26, 015 in PR #27, 016 in PR #29, 011 in PR #30, 007 in PR #31, 008 in PR #32, 010 in PR #33, 014 in PR #34, merged as main c2d4a64; 021 in PR #35, merged as main c889f59; 017 in PR #36, merged as main eda1efd). Next: 018 (Ready; depends on 017) and 020 (013 unblocks it). Stories 019–020 are Ready (ADRs Accepted 2026-10-16); 022 is Ready (re-scoped 2026-10-23 to the host panel view; its API and `CursorSlot` are TD-172 D1/D2, Approved by Evan 2026-10-23 7:56 AM PT (Mail Lead chat); TD-172 D4/D5 Approved by Evan 2026-10-23 8:20 AM PT); 023–024 are Ready (`design/ux/inventory-panel.md` authored 2026-10-16).
