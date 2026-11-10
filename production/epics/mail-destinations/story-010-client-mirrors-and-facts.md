# Story 010: Client mirror parts — `DestinationMirror`, `WalletMirror`, `RunMirror` — and the client facts constructors for `Evaluate`/F6/F7/F10

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Complete (greybox; reflection checks dropped → TD-220)
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~250 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-11-09 (M-1a package cleanup: Complete (greybox; reflection checks dropped → TD-220); delivered by PR #88, merge 7cd3756; see Completion Notes). Earlier: 2026-10-20 (docs: AC-1 namespace `PerformativeMail.Sim.MailDestinations` per the ADR-0017 erratum 2026-10-20; stamp unchanged — a text-only manifest erratum)

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
- [x] **AC-2 Facts parity.** For a fixture of every (destination state incl. CLOSED-within-grace and LOCKED) × (unit category, kind, own/other address) pair, the client facts constructor (from mirrors + the `IPanelView` entry's kind/address/hint runs) and the host facts constructor (from the registry + mail table) produce equal `DestinationFacts`/`UnitFacts`, and `MailRules.Evaluate` returns equal results.
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

**Status**: [x] Created and passing — `tests/integration/mail/ClientMirrorFactsTests.cs`, PR #88 (created) and PR #96 (AC-1 test updated) (2026-11-09)

---

## Completion Notes

*M-1a package delivery, recorded by Mail Lead's cleanup pass 2026-11-09 (owner ruling 2026-11-08 10:18 PM PT, card t299s1348: "Yes, switch to one PR per package and update the story files in one pass at the end"). Uncredited Mail Dev detail unless a quote says otherwise.*

- **Delivered by:** PR #88 (merge `7cd3756`), package WP-C (M-1a wave 1), merged 2026-11-09 under the merge rule "Merge on Review READY plus green CI, and only flag ADR changes to me" (Evan 2026-11-08 8:52 PM PT, card t292s1310). Bound at boot by PR #93 (merge `545df2f`): `ClientMirrors` binds `WalletMirror` and `RunMirror` via `BindMirrorPart` in boot step 5 (TD-260). PR #96 (merge `b1a3e8d`): see the AC-1 note below.
- **Built:** `DestinationMirror`, `MailClientFacts` (`Sim/Mail/Client`), `WalletMirror` (`Sim/Economy/Client`), `RunMirror` (`Sim/Run/Client`), `ClientLayerBuilder.BindMirrorPart(EventKindSet, IMirrorPart)`. Merge note from PR #88: the duplicate `ClientLayer.EventKindSet` was dropped in favour of main's `Core.EventKindSet`.
- **ACs met:** AC-2.
- **Deferred or partly met (greybox trims, Mail Lead planning default, M-1a regroup plan §3 simplify table; held by the package TD row):** AC-1 — the mirrors report state, wallet, phase/deadline/close ticks; the "no reference to `SimWorld` or host tables" reflection check was dropped → TD-220. AC-3 — display-only by construction; the "no reference to `IWalletGate` / `MailConsumer`" reflection test was dropped → TD-220.
- **AC-1 test note (PR #96, TD-272 (b)):** `ShiftStub` now raises a closed `DeliveriesState` with `deadlineTick` = Prep's end on tick 1, so `ClientMirrorFactsTests` expects Prep's end as the mirrored deadline from tick 1. Only the test changed; the AC text above is not rewritten.
- **Tests:** `tests/integration/mail/ClientMirrorFactsTests.cs` (PR #88; AC-1 expectation updated by PR #96). PR #96 reports 6,510 tests, 0 failed on all four legs.

---

## Dependencies

- Depends on: Story 002, Story 003, Story 004, Story 005 (`WalletChanged`), Story 009 (`DeliveriesState`); `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`; `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md`
- Unlocks: manual-delivery preview story; throwing-catching arc colour; Story 016
