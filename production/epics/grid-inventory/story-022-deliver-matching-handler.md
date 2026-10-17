# Story 022: DeliverMatching handler — the one deliver hold (matches across cursor/panel; one-unit misdeliver fallback)

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Blocked
> **Layer**: Core
> **Type**: Integration
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

> **BLOCKED 2026-10-16 (/architecture-review): re-scope needed.** The handler as written here ("Inventory owns the handler", calling Mail's Evaluate/consume) violates ADR-0017 ("Inventory **must never** call Mail"; `ConsumeHand` is called from *Delivery's* handler). ADR-0009 erratum 2026-10-16: `DeliverMatching` is a Delivery `OpKind` (block `0x0100–0x013F`) with its handler in `Sim/Delivery`. Inventory supplies the read-only match scan (cursor → hotbar → inventory → backpack, row-major), `ConsumeUnit`, version bumps and seals. Re-scope this story to those Inventory primitives, move the handler and the H-X-1 wallet cases to a manual-delivery story (`/create-stories manual-delivery`), then re-run `/story-readiness`.
>
> **Unblocked 2026-10-16** (superseded by the note above): ADR-0017 and ADR-0014 are Accepted. Re-derive the ADR notes below from the Accepted text at `/story-readiness` (`MailConsumer.Consume`, `IWalletGate`, `CheckInteraction`).

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-001`, `TR-inventory-005`; spans `TR-delivery-*` (Manual Delivery) and `TR-mail-*`
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0017: Mail table, destination registry & IWalletGate — Accepted 2026-10-16 (primary); ADR-0014: Interaction framework & hold timing — Accepted 2026-10-16; ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: `DeliverMatching {mailboxId, selectedEntryId?, holdStartTick}` is range-gated (P13 chain via `IPlayersApi`, ADR-0014), then a batch op: every entry in cursor → hotbar → inventory → backpack (row-major) with the mailbox's address and an accepted kind is delivered (matches only); else a `selectedEntryId` mail entry in the selected hotbar slot gets the one-unit Misdeliver fallback; else `no_target`; Mail's `Evaluate`/consume and `IWalletGate` (wallet floor) are called through Mail's API in the same phase (ADR-0009 §1–§2 batch rules; ADR-0003 §5). Mail's API is ADR-0017, the hold/range framework ADR-0014 — both Accepted 2026-10-16.
**ADR Version**: ADR-0017 2026-10-16; ADR-0014 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (Sim half) — re-assess when the blocking ADRs are written
**Engine Notes**: Re-derive when ADR-0017/0014 land.

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required: batch ops validate op-level checks first, then validate-and-commit each entry atomically in GDD order; a batch that commits nothing replies `ok:false` with the first op-level reason (ADR-0009 §2)
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R7-6** — 4 letters to #17 at inventory (3,1) at #17's mailbox, mocked Accept → all 4 CONSUMED, inventory version +1; the same entry on the cursor → same result, cursor empty.
- [ ] **H-R7-7** — hotbar slot 2 = 5 letters #17, inventory = 2 Mediums #17, (backpack: M-1b), inventory 6 letters #18; `deliver_matching {#17}` with Accept → #17 entries consumed in cursor → hotbar → inventory row-major order, reply `requested = got`, #18 untouched, one bump per touched container; the Misdeliver branch is never invoked on the match branch.
- [ ] **H-R7-11** — no #17 entry, selected hotbar slot 2 = entry 60 (5 letters to #18, ids 300–304), mocked Misdeliver → exactly 300 CONSUMED, entry 60 = 301–304, one bump; `selectedEntryId` absent → `no_target`; one #17 letter elsewhere → only it is delivered.
- [ ] **H-X-1 a–c** — wrong house at wallet −496 ¢ → one unit consumed, wallet −500; again → `wallet_floor`, nothing changes; correct house → 3 consumed; Reject → nothing changes.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Inventory owns the handler and the consume order (`ConsumeUnit`, story 015); Mail owns Evaluate, value/penalty per mailId, `MailConsumed`/`DeliveryOutcome`; Manual Delivery owns the hold and `holdStartTick` semantics. Build it with the manual-delivery epic's deliver story once ADR-0014/0017 are Accepted.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 015: `ConsumeUnit` (Ready).
- manual-delivery / mail-destinations epics: hold, Evaluate, wallet gate.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/inventory/DeliverMatchingTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 015, Story 019
- Depends on: `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md` (Delivery-owned `DeliverMatching` op + handler, which calls `IMailApi.ConsumeHand` from `production/epics/mail-destinations/story-007-consume-routine-hand-path-outcomes.md`)
- Unlocks: manual-delivery epic
