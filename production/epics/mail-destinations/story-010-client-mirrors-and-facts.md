# Story 010: Client mirror parts — `DestinationMirror`, `WalletMirror`, `RunMirror` — and the client facts constructors for `Evaluate`/F6/F7/F10

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~250 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/mail-destinations.md` (R5 "the client runs the same code"; R17 value hint; Network: host-authoritative, client display-only)
**Requirement**: `TR-mail-005` (same `Evaluate` compiled into the client for prompt and arc colour), `TR-mail-023` (host-authoritative payment/complaint; client F10 display-only), `TR-mail-014` (`DeliveriesState` consumed from the reliable stream)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0016: ClientLayer, Previews and Presentation Timing (primary, §1 mirror parts, §3 facts); ADR-0017 (`DestinationFacts`/`UnitFacts`, `Evaluate`, `MailPay`); ADR-0026 (static rows from the immutable world tables)
**ADR Decision Summary**: The Sim-side mirror parts live in `Sim/<System>/Client/` and are fed only by `MirrorDrain`. `DestinationMirror` holds the static registry rows from the world tables plus OPEN/CLOSED/LOCKED from `DeliveriesState`. `WalletMirror` holds the value from `WalletChanged`. `RunMirror` holds phase, `deadlineTick` and `closeTick`. They are bound with `ClientLayerBuilder.BindMirrorPart(EventKindSet kinds, IMirrorPart part)`. Shared functions take facts structs: the host builds facts from authoritative tables and the client from its mirror parts. One facts constructor per side is unit-tested against the same fixture.
**ADR Version**: ADR-0016 2026-10-16; ADR-0017 2026-10-16; ADR-0026 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation ClientLayer + Feature)**:
- Required: `ClientLayer` never reads `SimWorld`, even on the host; mirror parts are fed only by `MirrorDrain` (ADR-0003, ADR-0016 §1)
- Required: pure shared functions are called by both `SimWorld` and `ClientLayer` (ADR-0003 §6)
- Forbidden: a client-side copy of `Evaluate`/F6/F7/F10; a shared function taking a mirror or system object (facts only, ADR-0016 §3); the client deciding payment
- Guardrail: mirror apply allocates 0 B per event

---

## Acceptance Criteria

*From ADR-0016 §1/§3 and GDD R5/R17, scoped to this story:*

- [ ] **AC-1 Mirrors.** Fed a recorded stream of `DeliveriesState` and `WalletChanged` entries through `MirrorDrain`, `DestinationMirror` reports, per destination ordinal, the same OPEN/CLOSED/LOCKED as the host's `TryGetDestinationFacts` at the same ticks (story 009's stub cycle). `WalletMirror` reports the last `WalletChanged.balance`. `RunMirror` reports phase, `deadlineTick` and `closeTick`. None of these types references `SimWorld` or any `Sim.Mail` host table (namespace `PerformativeMail.Sim.MailDestinations`; reflection check).
- [ ] **AC-2 Facts parity.** For a fixture of every (destination state incl. CLOSED-within-grace and LOCKED) × (unit category, kind, own/other address) pair, the client facts constructor (from mirrors + the `IPanelView` entry's kind/address/hint runs) and the host facts constructor (from the registry + mail table) produce equal `DestinationFacts`/`UnitFacts`, and `MailRules.Evaluate` returns equal results.
- [ ] **AC-3 Display only.** With the mirrors at a stale wallet (one event behind), the client's `HintPayout` and `WalletGuard` preview may differ from the host's outcome, but no client code path can change the authoritative outcome. A test asserts that the client facts path has no reference to `IWalletGate` or to `MailConsumer`.

---

## Implementation Notes

*Derived from ADR-0016 §1, §3 and Implementation Guidelines:*

- **Files:** `src/PerformativeMail.Sim/Mail/Client/DestinationMirror.cs` (`IMirrorPart`; static rows built once from the injected `IWorldQuery.Lots` + `DestinationTypeDef`, exactly as story 002's registry builds them, sharing a static row-builder helper), `Mail/Client/MailClientFacts.cs` (client facts constructors), `src/PerformativeMail.Sim/Economy/Client/WalletMirror.cs`, `src/PerformativeMail.Sim/Run/Client/RunMirror.cs` (M-1a stub, beside story 009's stub).
- **Shared helper for host and client:** the effective-state helper (story 003) is used by both. The client passes its `IHostClock.EstimatedHostTick`-based start/apply ticks. This is the ADR-0017 Risks mitigation for "grace logic differs".
- **Binding:** `ClientLayerBuilder.BindMirrorPart(EventKindSet, IMirrorPart)` is ADR-0016 §1's additive builder method. `MirrorDrain` dispatch comes from `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md`. If `BindMirrorPart` does not exist yet when this story starts, add it as ADR-0016 §1 states (frozen-table rules as `Bind`), beside the shell from `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`. Do not edit those story files; record the addition in this story's completion notes.
- **Unit facts on the client:** kind and address come from the mirrored entry's `StackKey` (ADR-0009 `ContainerMirror`). Spawn value and deadline come from the entry's value-hint runs (R17). At M-1a over loopback, the hint runs are carried by the inventory records Mail/Inventory already send. If the M-1a container records do not yet carry hint runs (TR-mail-016 is tagged M0), build `UnitFacts.SpawnValue` from the run list when present, and expose a `HasHint` flag the preview can show as "?". Record which case applies in the completion notes; do not invent a wire field here.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- manual-delivery: the deliver preview, tracker and M-INT-2 parity over 1,000 seeded pairs (`TR-delivery-012`).
- throwing-catching: arc colour (4c).
- Value-hint wire format and JoinState (M0, TR-mail-016/017).
- HUD rendering of the timer and wallet (Presentation; no HUD epic yet).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/mail/ClientMirrorFactsTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002, Story 003, Story 004, Story 005 (`WalletChanged`), Story 009 (`DeliveriesState`); `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`; `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md`
- Unlocks: manual-delivery preview story; throwing-catching arc colour; Story 016
