# Story 003: Availability (MD2), the `DeliverPreview` client step (4b) and the `DeliverPreviewTracker` settle

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~250 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/manual-delivery.md` (MD2 availability; MD3 "evaluated every tick while focused" and the settle tracker; States and Transitions)
**Requirement**: `TR-delivery-002` (ordered 6-reason availability; `deliver.pending` derived from the pending queue), `TR-delivery-003` (preview every focused tick in `Previews`), `TR-delivery-004` (`DeliverPreviewTracker` 6-tick settle counts Sim ticks), `TR-delivery-005` (per-tick client order: events/replies → focus → preview → resolve the Interact edge)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0016: ClientLayer, Previews and Presentation Timing (primary: §2 `Previews` sub-order 4a → 4b → 4d → 4e, §4 the tracker); ADR-0014 (§1 `IsAvailable` pure hook, §2 unavailable targets still show)
**ADR Decision Summary**: `DeliverPreviewTracker` lives in `Sim/Delivery/Client/` and runs in sub-step 4b, never in Presentation. Its state is `(BranchKey key, uint sinceTick)` with `BranchKey = (branch kind, destinationId, selectedEntryId if MISDELIVER_ONE)` and `displayedTicks = clientTick − sinceTick`, in Sim ticks. `sinceTick` resets on a key change or when MD2 rows 1–5 make the verb unavailable; a payload change does not reset it. It publishes `DeliverPreviewState { Branch, payload, DisplayedTicks, CanStartHoldNext }` with `CanStartHoldNext = displayedTicks ≥ branchSettleTicks` (6, from content). `IsAvailable` returns the first matching reason of MD2's ordered list.
**ADR Version**: ADR-0016 2026-10-16; ADR-0014 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + ClientLayer)**:
- Required: client steps live in the Sim (`<System>/Client/`) and read only facts built from mirror parts or ports (ADR-0016)
- Required: `DeliverPreviewTracker` counts client Sim ticks; Presentation never derives `CanStartHoldNext` itself (ADR-0016)
- Required: `IInteractable` hooks are pure (ADR-0014 §1)
- Forbidden: a separate "pending" flag (row 3 is derived from the pending queue only); dispatching a press in this step (edges resolve only in 4d)
- Guardrail: ≤ 26 entries scanned per focused tick; nothing evaluated while IDLE; 0 B

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md`, scoped to this story:*

- [ ] **AC-1 (M-MD2-1, 6 + 5 rows)** Each condition alone (Panel open; swim depth; an own pending `deliver_matching` / `throw` / `drop` / `take_all` / `sort`; CLOSED; LOCKED; preview NOTHING) makes `IsAvailable` return `deliver.close_panel`, `deliver.dry_land`, `deliver.pending` (`deliver.sorting` for sort), `mail.reject.closed`, `mail.reject.not_on_route` and `deliver.nothing` respectively. Each adjacent pair (1+2, 2+3, 3+4, 4+5, 5+6) returns the lower-numbered row's key. With none, `ok`.
- [ ] **AC-2 (M-MD2-2)** With a pending own `deliver_matching`: when (a) the reply arrives or (c) a rejoin snapshot clears the queue, `deliver.pending` is false on that same tick, with no separate flag in the code (a reflection/grep test finds no pending boolean in `Sim/Delivery`). When (b) the 5 s timeout fires, it stays true until the resync snapshot is applied, and is false on that tick. While pending it holds at every mailbox.
- [ ] **AC-3 (M-MD3-3)** With a Deliver anchor focused for 30 ticks, the evaluation counter rises by exactly 30 (no cache). IDLE evaluates 0 times. Two client instances with identical inputs give identical branches. A CLOSED destination scans 0 entries.
- [ ] **AC-4 Tracker.** A MISDELIVER_ONE branch first shown at tick T publishes `DisplayedTicks` 0…5 with `CanStartHoldNext = false` for T…T+5 and true from T+6. A key change (a different `selectedEntryId`, a branch kind change or a destination change) resets it. A cents/`k`/`floorRefusal`/`cursed` change does not reset it. Any tick on which MD2 rows 1–5 apply resets it. MATCH branches publish `CanStartHoldNext = true` (no settle; MD4).

---

## Implementation Notes

*Derived from ADR-0016 §2/§4, ADR-0014 §1 and GDD MD2:*

- **Files:** `src/PerformativeMail.Sim/Delivery/Client/DeliverPreviewStep.cs` (`IClientStep` bound at `PreviewSubStep.DeliverPreview` via `ClientLayerBuilder.BindPreview`, which `production/epics/player-character/story-014-focus-scoring-dwell-preview-binding.md` adds), `Delivery/Client/DeliverPreviewTracker.cs`, `Delivery/Client/DeliverPreviewState.cs` + `IDeliverPreviewRead` (ADR-0016 Key Interfaces), and `DeliverInteractable.IsAvailable`.
- **Step logic:** if the focused anchor (from Players' 4a focus output) is a Deliver anchor, build the client facts (mail-destinations story 010), call `DeliverPreview.Preview` (story 002), compute the MD2 reason, update the tracker and publish the state. Otherwise publish IDLE and skip evaluation entirely.
- **MD2 rows and their sources:** row 1 = Panel context (Players' context, `production/epics/player-character/story-013-contexts-verbs-availability-gate-router-declarations.md`); row 2 = swim state from the carrier (`production/epics/player-character/story-009-water-wade-swim-haulout-f7.md`); row 3 = the own pending queue (ADR-0009 non-optimistic op kinds; at M-1a, read the pending set the `ClientAdapter` keeps for in-flight own requests, `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md`, until the M0 `PendingQueue`); rows 4–5 = `DestinationMirror` state; row 6 = this tick's preview. On the host, `IsAvailable` evaluates only rows 2, 4 and 5 against host state (`CheckInteraction` → `Unavailable(reason)`, MD6). Use the same function with a host-facts flag; never duplicate the list.
- `branchSettleTicks` (6) is a content value (F5 constants, `PROPOSED`), read from the Delivery balance record (`production/epics/content-data/story-004-balance-registry-simsettings.md`), never a literal.
- The "dim after 250 ms" (`pendingDebounceMs`) is HUD's rendering rule (M-MD2-3, UI) and is not implemented here.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 004: using `CanStartHoldNext` at press (M-MD4-1) and the cancels. Story 010: prompt text.
- HUD rendering of the dimmed prompt, lock icon and glyph rules (M-MD2-3, M-MD2-4: no HUD epic yet).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/delivery/DeliverAvailabilityTests.cs` and `tests/unit/delivery/DeliverPreviewTrackerTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002; `production/epics/player-character/story-013-contexts-verbs-availability-gate-router-declarations.md`, `production/epics/player-character/story-014-focus-scoring-dwell-preview-binding.md`; `production/epics/mail-destinations/story-010-client-mirrors-and-facts.md`; `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md`
- Unlocks: Story 004, Story 006, Story 010
