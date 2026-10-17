# Story 006: Complaint deltas (F9) — lazy door and team token buckets, late-complaint window, `ComplaintRaised`

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~200 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/mail-destinations.md` (R9 complaint deltas; F9)
**Requirement**: `TR-mail-010` (complaint token buckets in int64 ticks × permille, elapsed clamped), `TR-mail-012` (pause freezes buckets/windows)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §7, §9 `ComplaintRaised`); ADR-0006 (int64, one rounding)
**ADR Decision Summary**: Per door and team-wide, `tokensPm` and `lastTick` are `long`. Refill is lazy, on touch only: `tokens = min(capPm × window, tokens + min(now − lastTick, window) × capPm)`, elapsed clamped to `window` (900). Nothing iterates buckets per tick. `lastLateTick` is team-wide. `ComplaintRaised{destination EntityId, source u8, amountPermille u16}` is Replicated (the GDD's `mailboxId` is `destination`).
**ADR Version**: ADR-0017 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: complaint buckets refill lazily on touch; an idle `Mail.Step` touches only feeder credits (ADR-0017)
- Required: integer-only, int64 intermediates, ticks as absolute values (ADR-0006)
- Forbidden: per-tick bucket iteration; wall-clock reads; int32 elapsed × cap products
- Guardrail: 0 B per call

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md` (letters raw 5,000 ‰ unless stated), scoped to this story:*

- [ ] **AC-1 (M-F9-1, M-R9-1)** Fresh buckets: **a** one 20-letter op → 20,000 (10,000 with `MisdeliveryComplaintMult` 500); **b** six one-letter ops at six doors → 5,000 each; **c** two 20-letter ops at one door in one tick → 20,000 then 0; **d** a 40-postcard op (raw 120,000) → 20,000; **e** emptied at T: T + 450 allows 10,000, T + 900 allows 20,000; after 200,000 idle ticks the bucket is exactly full, with no overflow; **f** one letter per 900 ticks → 5,000 each; **g** `MisdeliveryComplaintMult` 500 halves the raised value after the cap, and the bucket is still debited the full allowance (M-R9-2c).
- [ ] **AC-2 (M-F9-2)** n = 1, one one-letter op every 15 ticks at one door for 1,800 ticks (last at 1,785), fresh buckets: Σ complaint = 59,666 ‰ ± 2, and ticks 0–899 sum to 39,666 ‰ ± 2 (driven by direct bucket calls; the feeder path is M-1b).
- [ ] **AC-3 (M-F9-3)** An Accept consuming ≥ 1 late unit at T → +2,000 ‰ late; any door at T + 899 → 0; at T + 900 → +2,000 (team-wide window). Grace Period → 0. A dead-letter-only Accept → 0 and does not set `lastLateTick`. A Misdeliver op with late units → 0 late complaint. Two late entries in one hold → +2,000 once.
- [ ] **AC-4 (M-F9-4)** Eight 20-letter ops at eight doors in one tick raise Σ ≤ 155,000 ‰ at `playerScalePm` for n = 8 and ≤ 40,000 ‰ at n = 1; the team bucket refills 40,000 ‰ per 900 ticks at n = 1.
- [ ] **AC-5 (M-R9-2a)** Zero allowed complaint raises no `ComplaintRaised`. Each non-zero raise emits exactly one event with `source` ∈ {misdelivery, late}.

---

## Implementation Notes

*Derived from ADR-0017 §7 and GDD F9 (read the F9 block; constants below are its data keys):*

- **File:** `src/PerformativeMail.Sim/Mail/ComplaintBuckets.cs`. Door state lives in the registry's `tokensPm`/`lastTick` columns (story 002); the team bucket and `lastLateTick` are fields on the same object. All start full (`tokens = cap × window`); `lastLateTick` starts at "never" (use a `bool`/sentinel, not `long.MinValue` arithmetic).
- **API (internal):** `int ChargeMisdelivery(DestinationOrdinal d, long now, int rawPm)` returns `allowed`, applies both debits and returns 0 when nothing is allowed; `int ChargeLate(long now, bool graceSuppresses)`. The caller (story 007/008) sums raw complaint over units actually consumed as Misdeliver in one op, calls once per op, applies `MisdeliveryComplaintMult` after the cap with one `MulDivRH`, and raises `ComplaintRaised` when the result > 0.
- **Refill order:** refill the door and team buckets lazily at `now`, then `allowed = min(raw, doorTokens / window, teamTokens / window)` with `FloorDiv` on non-negative values, then subtract `allowed × window` from both. Clamp elapsed to `window` **before** multiplying (`long`).
- **Data keys (PROPOSED in the GDD):** `doorComplaintCap` 20 (→ `capPm` 20,000), `teamComplaintCap` 40, `doorComplaintWindowSeconds` 30 (→ 900 ticks), `lateComplaintPm` 2000, `lateComplaintWindowSeconds` 30, `complaintOnMisdelivery` per `MailKindDef`. Read them from the Mail balance record and `MailKindDef` (content-data stories 004/005), never as literals.
- **Team cap:** `teamCapPm = RH(teamComplaintCap × playerScalePm / 1000) × 1000`. `playerScalePm` (n^0.65 in permille) is Run Structure's and arrives with the deliveries/shift input (story 009's stub passes it, 1000 solo). This story takes it as a parameter. n = 8 uses `playerScalePm` 3864 → RH(154.56) = 155 → 155,000.
- Event `ComplaintRaised`: payload `destination u32, amountPermille u16, source u8` + 1 explicit pad byte (padding-free, ≤ 48 B); Replicated, audience all.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007/008: deciding which units were consumed as Misdeliver (floor stops raise nothing, M-R9-2a/b) and the call per op.
- The complaint meter, its decay and thresholds (Run Structure, M1). `CursedMisdelivered` (R14, M4).
- `misdeliveriesThisShift` (story 007).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/mail/ComplaintBucketTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (registry columns), Story 005 (Mail system, event registration); `production/epics/content-data/story-005-m1a-def-records.md` (`complaintOnMisdelivery`)
- Unlocks: Story 007, Story 008
