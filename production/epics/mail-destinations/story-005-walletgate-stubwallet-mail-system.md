# Story 005: `IWalletGate` port, the M-1a `StubWallet` (`WalletChanged`, dump), and the Mail system shell (composition, phase 8 idle `Mail.Step`, no-Economy arch test)

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~250 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-21 (citation fix: `SystemId.Mail` does not exist; the system is `SystemId.MailDestinations`, `Core/SystemId.cs`)

## Context

**GDD**: `design/gdd/mail-destinations.md` (R7 wallet access without a cycle; R8 the floor; M-PERF-1c idle tick; M-E-1 no wall clock)
**Requirement**: `TR-mail-007` (wallet via Mail-owned `IWalletGate`; no Mail→Economy reference), `TR-mail-020` (idle tick touches only feeder credits; scans no rows), `TR-mail-012` (no wall-clock reads; pause freezes buckets/windows/credits)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §7, §8); ADR-0003 (system shape, composition, phase 8 `MailStep`); ADR-0008 (`WalletChanged` Replicated ≤ 24 B)
**ADR Decision Summary**: Mail owns `IWalletGate { bool TryDebitPenalty(Cents amount, Cents floor); void Credit(Cents amount, CreditReason reason); }` in `Sim.Mail.Ports`. Both apply immediately, synchronously in the caller's phase, one call per unit; Mail never reads the balance. Economy implements it, is constructed before Mail and is injected by the composition root (`SystemGraph`: Mail → `IWalletGate`). At M-1a, `StubWallet` (`Sim/Economy/StubWallet.cs`) holds a `Cents` balance starting at `SimSettings.startingWalletCents`, implements `IWalletGate` and `ISimStateContributor`, raises `WalletChanged{balance, delta, reason}` (Replicated, ≤ 24 B) and binds no phase. `Sim.Mail` never references `Sim.Economy` (arch test). `Mail.Step` (phase 8) updates feeder credits each tick and touches nothing else when idle (0 feeders at M-1a).
**ADR Version**: ADR-0017 2026-10-16; ADR-0003 2026-10-16; ADR-0008 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: each system lives in `PerformativeMail.Sim/<System>/`, is marked `[SimSystem(SystemId)]`, has exactly one public constructor of ADR-0003 §4 injectables; dependencies are declared in `Composition/SystemGraph.cs` and constructed in topological order (ADR-0003 §4)
- Required: `Sim.Mail` never references `Sim.Economy`; the wallet is reached only through `IWalletGate` in `Cents` (`StubWallet` at M-1a) (ADR-0017)
- Required: event kinds declare class, ≤ 48 B padding-free payload, `ResyncScope` (ADR-0008)
- Forbidden: Mail reading the wallet balance; deferring Mail's debits to phase 9; `IEconomyApi` from Mail (ADR-0017 §8)
- Guardrail: idle tick visits 0 mail rows and 0 buckets, 0 B (M-PERF-1c)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md` and ADR-0017 §8 Validation, scoped to this story:*

- [ ] **AC-1 Port semantics (M-R7-2c, FX-W).** `StubWallet` at 0 with floor −500: `TryDebitPenalty(4)` → true, balance −4. At −496, `TryDebitPenalty(4)` → true (−500). At −500, `TryDebitPenalty(1)` → false and the balance is unchanged. `Credit(9, DeliveryPay)` adds 9. Each applied call raises exactly one `WalletChanged{balance, delta, reason}`; a refused debit raises none.
- [ ] **AC-2 (M-R7-2d) Arch test.** A reflection test over the Sim assembly finds no type in namespace `PerformativeMail.Sim.Mail*` that references any type in `PerformativeMail.Sim.Economy*` (fields, parameters, base types, attributes, method bodies via IL type references).
- [ ] **AC-3 Composition.** The M-1a composition profile constructs `StubWallet` before `Mail`, injects it as `IWalletGate` and builds the destination registry (story 002) from the injected `IWorldQuery` while `Constructed`. The composition reflection test (`production/epics/simulation-core/story-025-composition-root-reflection-test.md`) passes with Mail and Economy-stub bound. Dumps include the `Mail` and `Wallet` sections.
- [ ] **AC-4 (M-PERF-1c, M-E-1) Idle and pause.** With 10 destinations and 0 feeders, 300 idle ticks visit 0 mail rows and 0 buckets (counters), allocate 0 B and leave the `Mail` dump section unchanged. The Sim assembly references no `DateTime`, `Stopwatch` or `Environment.TickCount` (covered by the analyzer; asserted here for `Sim.Mail` and `Sim.Economy`). With zero ticks run, bucket state is unchanged (pause).

---

## Implementation Notes

*Derived from ADR-0017 §7–§8, Key Interfaces and Implementation Guidelines:*

- **Files:** `src/PerformativeMail.Sim/Mail/Ports/IWalletGate.cs`, `Ports/CreditReason.cs` (`None 0, DeliveryPay 1`; 2–31 reserved for Mail); `src/PerformativeMail.Sim/Mail/MailSystem.cs` (`[SimSystem(SystemId.MailDestinations)]`, the single public constructor taking `EventLog` writer, `MailIds`, `IRngStreams` (mail-owned view), `ITickReader`, `IInventoryApi`, `IWalletGate`, `IWorldQuery`, `ContentSet`, `IStatSheet`, `SimCheckRing`, `SimCounters` and its `MailSettings` record); `src/PerformativeMail.Sim/Economy/StubWallet.cs`.
- `IWorldQuery` is not on ADR-0003 §4's injectable list as written. ADR-0026 §1 says consumers receive it by constructor (ADR-0003), so treat it as an injectable service like `ContentSet`. If the composition reflection test rejects it, record that in the completion notes and raise it at `/architecture-review`; do not route around it with a locator.
- `MailSystem` publishes `IMailApi` (ADR-0017 Key Interfaces). Methods not yet implemented (`ConsumeHand`, `ResolveThrownLanding`, `SetDeliveriesState`) arrive in stories 007–009. Until those stories land, they are no-ops guarded by `SimAssert.Fail("not yet implemented")` (Debug) and have no caller. The Sim never throws in a tick.
- **Phase 8 `MailStep`:** iterates the feeder-credit array (length 0 at M-1a) and returns. Manifest batch firing hooks in later (stories 012–014). Count visited rows and buckets in `SimCounters` (`mailRowsVisited`, `mailBucketsVisited`) as test seams.
- **`StubWallet`:** `Cents _balance` from `SimSettings.startingWalletCents` (`production/epics/content-data/story-004-balance-registry-simsettings.md`; if that key is absent, add it through the content-data owner's registry, not a literal). `TryDebitPenalty` applies iff `balance − amount ≥ floor` (equality allowed) and must not divide. `WalletChanged` payload: `balance i64, delta i64, reason u8` + 7 explicit zeroed pad bytes = 24 B (ADR-0017: ≤ 24 B; ADR-0008 padding-free rule); class Replicated, audience all. Register a `Wallet` dump section (`balance`).
- `SystemGraph`: declare Mail → `IWalletGate` (the port), and Economy-stub implementing it is constructed first (ADR-0017 §8). Mail → Inventory (`IInventoryApi`) as well.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007/008: the per-unit calls from the consume routine (M-X-4). Story 010: `WalletMirror` (client).
- The real Economy (shop, payday, `IEconomyApi`): future Economy ADR.
- Feeder credits arithmetic (F11, M-1b).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/mail/MailCompositionTests.cs`; unit: `tests/unit/mail/StubWalletTests.cs`, `tests/unit/mail/MailArchitectureTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002; `production/epics/simulation-core/story-025-composition-root-reflection-test.md`, `production/epics/simulation-core/story-015-event-log-call-scopes-delivery.md`; `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md` (`IInventoryApi` type); `production/epics/world-stub/story-007-world-dump-composition-replay-id.md` (World composed first)
- Unlocks: Story 006, Story 007, Story 008, Story 009
