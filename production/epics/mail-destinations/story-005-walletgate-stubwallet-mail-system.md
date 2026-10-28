# Story 005: `IWalletGate` port, the M-1a `StubWallet` (`WalletChanged`, dump), and the Mail system shell (composition, phase 8 idle `Mail.Step`, no-Economy arch test)

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~250 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-28 (full `/story-readiness` pass on main 51b68cd, docs PR `docs/md005-cd019-readiness`; log `/workspace/repos/logs/md005-readiness.txt`. **Ready.** Evan 2026-10-28 7:26 AM PT, Mail Lead chat, card 'Mail-destinations 005 decisions': "Approve the ADR-0003 §4 and ADR-0017 §8 errata: the composition root injects IWalletGate into Mail only, and SystemGraph is unchanged" and "Approve all six TD-177 defaults for mail-destinations 005, including the ADR-0007 §4 economy event block row". Applied: manifest stamp 2026-10-16.2 → 2026-10-17.1 (that revision does not touch this story); the ADR-0003 §4 / ADR-0017 §8 errata and the ADR-0007 §4 Economy block amendment recorded in the ADRs; the six TD-177 defaults written into the notes below (TD-177 closed, Evan approved); AC-1 names the debit reason and how −496 is reached; AC-2 asserts non-vacuity; AC-3 names the section id and the registry observable; AC-4's pause clause rewritten, the M-PERF-1 c scope stated and the 0 B check advisory until M0 (E2); the Implementation Note claiming `IWorldQuery` is not injectable deleted (ADR-0003 §4 has the row; `CompositionRulesTests.cs:475` accepts it); constructor list corrected (`EventLogFacade`, no `MailSettings`, a `SimCounterSink` over `SimCounters`); ids allocated: `CounterId` 56/57, `SectionIds.Wallet = 6`, `SimAssertCode` 85, Economy event block 0x0180–0x01BF. Earlier: 2026-10-21 (citation fix: `SystemId.Mail` does not exist; the system is `SystemId.MailDestinations`, `Core/SystemId.cs`); earlier: 2026-10-20 (docs: namespace `PerformativeMail.Sim.MailDestinations` per the ADR-0017 erratum 2026-10-20; stamp unchanged — a text-only manifest erratum)

## Context

**GDD**: `design/gdd/mail-destinations.md` (R7 wallet access without a cycle; R8 the floor; M-PERF-1c idle tick; M-E-1 no wall clock)
**Requirement**: `TR-mail-007` (wallet via Mail-owned `IWalletGate`; no Mail→Economy reference), `TR-mail-020` (idle tick touches only feeder credits; scans no rows), `TR-mail-012` (no wall-clock reads; pause freezes buckets/windows/credits)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §7, §8); ADR-0003 (system shape, composition, phase 8 `MailStep`); ADR-0008 (`WalletChanged` Replicated ≤ 24 B)
**ADR Decision Summary**: Mail owns `IWalletGate { bool TryDebitPenalty(Cents amount, Cents floor); void Credit(Cents amount, CreditReason reason); }` in `Sim.Mail.Ports` (namespace `PerformativeMail.Sim.MailDestinations.Ports`, ADR-0017 erratum 2026-10-20). Both apply immediately, synchronously in the caller's phase, one call per unit; Mail never reads the balance. Economy implements it. At M-1a the composition root (`SimComposition.Build`) constructs `StubWallet` after `BindWorld` and before the systems loop and injects it into Mail's constructor as `IWalletGate` through `SimSystemContext.WalletGate`; **`SystemGraph` is unchanged** — no Mail → Economy edge (the shipped graph already has Economy → MailDestinations, `SystemGraph.cs:61`; ADR-0003 §4 and ADR-0017 §8 errata 2026-10-28, Evan 2026-10-28 7:26 AM PT, Mail Lead chat, card 'Mail-destinations 005 decisions': "Approve the ADR-0003 §4 and ADR-0017 §8 errata: the composition root injects IWalletGate into Mail only, and SystemGraph is unchanged"). At M-1a, `StubWallet` (`Sim/Economy/StubWallet.cs`) holds a `Cents` balance starting at `SimSettings.startingWalletCents`, implements `IWalletGate` and `ISimStateContributor`, raises `WalletChanged{balance, delta, reason}` (Replicated, ≤ 24 B) and binds no phase. `Sim.Mail` never references `Sim.Economy` (arch test). `Mail.Step` (phase 8) updates feeder credits each tick and touches nothing else when idle (0 feeders at M-1a).
**ADR Version**: ADR-0017 2026-10-16 (errata to 2026-10-28); ADR-0003 2026-10-16 (errata to 2026-10-28); ADR-0008 2026-10-16; ADR-0007 2026-10-16 (§4 table amendment 2026-10-28: Economy event block)

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: each system lives in `PerformativeMail.Sim/<System>/`, is marked `[SimSystem(SystemId)]`, has exactly one public constructor of ADR-0003 §4 injectables; dependencies are declared in `Composition/SystemGraph.cs` and constructed in topological order (ADR-0003 §4)
- Required: `Sim.Mail` never references `Sim.Economy`; the wallet is reached only through `IWalletGate` in `Cents` (`StubWallet` at M-1a) (ADR-0017)
- Required: `IWalletGate` is on the ADR-0003 §4 injectable table for **Mail & Destinations only** (erratum 2026-10-28); `CompositionRulesTests.Table()` gains the matching rule in this story (ADR-0003 §4)
- Required: a new event block is allocated by amending the ADR-0007 §4 table: Economy 0x0180–0x01BF, `OpKindBlock.Economy` appended to `OpKindBlockTable.Blocks` (ADR-0007 §4, amendment 2026-10-28)
- Required: event kinds declare class, ≤ 48 B padding-free payload, `ResyncScope` (ADR-0008)
- Forbidden: Mail reading the wallet balance; deferring Mail's debits to phase 9; `IEconomyApi` from Mail (ADR-0017 §8)
- Guardrail: idle tick visits 0 mail rows and 0 buckets, 0 B (M-PERF-1c)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md` and ADR-0017 §8 Validation, scoped to this story:*

- [ ] **AC-1 Port semantics (M-R7-2c, FX-W).** `StubWallet` built with `startingWalletCents` 0, every call with floor −500 (a `Cents` argument; there is no `walletFloor` key, ADR-0017 erratum 2026-10-17): `TryDebitPenalty(4, floor)` → true, balance −4, raising `WalletChanged{balance −4, delta −4, reason 0}` — a penalty debit carries `Reason = CreditReason.None` (0) and a negative `Delta`. −496 is reached by 124 applied `TryDebitPenalty(4, floor)` calls from 0 (the test asserts the balance after each); at −496, `TryDebitPenalty(4, floor)` → true (−500). At −500, `TryDebitPenalty(1, floor)` → false, the balance is unchanged and no event is raised. `Credit(9, DeliveryPay)` adds 9 (−491) and raises `WalletChanged{−491, +9, DeliveryPay}`. Each applied call raises exactly one `WalletChanged{balance, delta, reason}` through the injected `EventLogFacade`; a refused debit raises none. The payload is `long Balance; long Delta; byte Reason;` plus 7 explicit zeroed pad bytes, 24 B with no implicit padding (ADR-0008).
- [ ] **AC-2 (M-R7-2d) Arch test.** A reflection test over the Sim assembly first asserts that at least one type exists on each side — in `PerformativeMail.Sim.MailDestinations*` and in `PerformativeMail.Sim.Economy*` — and then finds no type in namespace `PerformativeMail.Sim.MailDestinations*` (folder `Sim/Mail/`; ADR-0017 erratum 2026-10-20 — keyed on the real namespace so the test cannot pass vacuously) that references any type in `PerformativeMail.Sim.Economy*` (fields, parameters, base types, attributes, method bodies via IL type references).
- [ ] **AC-3 Composition.** The M-1a profile (`SimProfile.M1aGreybox`) built through `SimComposition.Build` constructs `StubWallet` after `BindWorld` and before the systems loop, injects it into `MailSystem`'s constructor as `IWalletGate` (`SimSystemContext.WalletGate`, guarded `Require(_consumer == SystemId.MailDestinations)`), and `MailSystem` builds the destination registry (story 002) from the injected `IWorldQuery` while `Constructed`: with the 10-lot M-1a world the registry's row count equals the lot count, 10. `SystemGraph` is unchanged (no Mail → Economy edge; `Build` does not throw `DependencyCycle`). `CompositionRulesTests` passes with its `Table()` gaining the `IWalletGate` row for `SystemId.MailDestinations` only, and the composition reflection test (`production/epics/simulation-core/story-025-composition-root-reflection-test.md`) passes with Mail and the Economy stub bound. The canonical dump's section order is SimCore, World, `Wallet` (`SectionIds.Wallet = 6`, payload `balance i64`), then the systems' sections including `Mail`.
- [ ] **AC-4 (M-PERF-1c, M-E-1) Idle and pause.** M-PERF-1 c's scope is 10 destinations and 0 feeders (M-1a has no feeders). Over 300 idle ticks `CounterId.MailRowsVisited` (56, `mailRowsVisited`) and `CounterId.MailBucketsVisited` (57, `mailBucketsVisited`) stay 0 and the `Mail` dump section is byte-identical before and after; the 0 B allocation check is reported, not blocking, until M0 (erratum E2). The Sim assembly references no `DateTime`, `Stopwatch` or `Environment.TickCount` (covered by the analyzer; asserted here for `Sim.Mail` — namespace `PerformativeMail.Sim.MailDestinations` — and `Sim.Economy`). **Pause:** two `Mail` dump reads with no `Step` between, at least 50 ms apart (test-side wall clock only), are byte-identical.

---

## Implementation Notes

*Derived from ADR-0017 §7–§8, Key Interfaces and Implementation Guidelines:*

- **Files:** `src/PerformativeMail.Sim/Mail/Ports/IWalletGate.cs`, `Ports/CreditReason.cs` (`None 0, DeliveryPay 1`; 2–31 reserved for Mail), namespace `PerformativeMail.Sim.MailDestinations.Ports`; `src/PerformativeMail.Sim/Mail/MailSystem.cs` (`[SimSystem(SystemId.MailDestinations)]`, the single public constructor taking `EventLogFacade`, `MailIds`, `IRngStreams` (mail-owned view), `ITickReader`, `IInventoryApi`, `IWalletGate`, `IWorldQuery`, `ContentSet`, `IStatSheet`, `SimCheckRing` and `SimCounters` — **no `MailSettings` record** (TD-177 item 1)); the `IMailApi` surface in `Sim/Mail/`; `src/PerformativeMail.Sim/Economy/StubWallet.cs`, `Economy/EconEventKinds.cs` and `Economy/WalletChangedPayload.cs`. Edits: `Composition/SimComposition.cs` and `SimSystemContext.cs` (`SystemGraph.cs` is **not** edited), `Content/SimSettings.cs` (`StartingWalletCents`), `Core/CounterId.cs`, `Core/SectionIds.cs`, `Core/OpKindBlock.cs`, `Primitives/SimAssertCode.cs`, `design/registry/entities.yaml`.
- `IWorldQuery` is on ADR-0003 §4's injectable table (erratum 2026-10-17, any system) and `CompositionRulesTests.cs:475` accepts it; inject it by constructor like `ContentSet`.
- **Composition (ADR-0003 §4 / ADR-0017 §8 errata 2026-10-28; Evan 2026-10-28 7:26 AM PT, Mail Lead chat, card 'Mail-destinations 005 decisions': "Approve the ADR-0003 §4 and ADR-0017 §8 errata: the composition root injects IWalletGate into Mail only, and SystemGraph is unchanged").** `StubWallet` is **not** a `[SimSystem]`. `SimComposition.Build` constructs it after `BindWorld` and before the systems loop from `content.Settings.StartingWalletCents`, `services.Events`, `services.Slots` (the `WalletChanged` audience is `ISlotView.ConnectedMask`) and `services.Counters` (its `Cents` counter sink). It is bound as a state contributor under `SectionIds.Wallet` and exposed as `SimSystemContext.WalletGate` (type `IWalletGate`), guarded by `Require(_consumer == SystemId.MailDestinations)`. `SystemGraph` is unchanged: no Mail → Economy edge (Economy → MailDestinations already exists at `SystemGraph.cs:61`, so the reverse edge would make `Build` throw `DependencyCycle`; `ShippedOrder` builds Economy after Mail and `SimProfile.M1aGreybox` has no Economy). `CompositionRulesTests.Table()` gains the rule "`IWalletGate` → `SystemId.MailDestinations` only" — that test change is this story's code work.
- `MailSystem` publishes `IMailApi` (ADR-0017 Key Interfaces). **At 005 `IMailApi` declares only `ConsumeHand`, `SetDeliveriesState` and `TryGetDestinationFacts`** (TD-177 item 6): `ResolveThrownLanding` and `LandingResult` arrive with story 008 (`LandingResult` does not exist yet, so a no-op would not compile), `ConsumeFed` with M-1b. The not-yet-implemented members are Debug-guarded no-ops using `SimAssert.Fail(SimAssertCode.MailApiNotImplemented)` (= 85; `Fail` takes a `ushort` code, so the story's earlier `SimAssert.Fail("not yet implemented")` was not compilable) and have no caller. The Sim never throws in a tick. Carried obligations land here: `TryGetDestinationFacts` delegates to `DestinationRegistry.TryGetDestinationFacts`, with a test proving identical results (TD-079 item 1); registry build rejections map to `SimBuildException` codes or are recorded (TD-079 item 5); `MailTable.BindDump` moves into `MailSystem.Bind` (TD-074 item 4).
- **Phase 8 `MailStep`:** iterates the feeder-credit array (length 0 at M-1a) and returns. Manifest batch firing hooks in later (stories 012–014). Count visited rows and buckets in `SimCounters` as test seams: `CounterId.MailRowsVisited = 56` (`mailRowsVisited`) and `CounterId.MailBucketsVisited = 57` (`mailBucketsVisited`), both in the canonical dump (`SimCountersTests`' length pin 56 → 58, after player-character 015's 55; TD-177 item 4). `MailSystem` reads `content.Settings.MailTablePages` through the injected `ContentSet`; the first story that needs a Mail-only knob adds `SimSettings.Mail` (`MailSettings`) and its `settingsOwners` row in `CompositionRulesTests` (TD-177 item 1).
- **`StubWallet`:** `Cents _balance` from `SimSettings.StartingWalletCents` — key `sim.startingWalletCents`, int, default 0, added to `SimSettings` and to `design/registry/entities.yaml` following the `sim.mailTablePages` precedent (TD-074 item 3; `production/epics/content-data/story-004-balance-registry-simsettings.md`); the −500 floor stays a `Cents` argument, there is no `walletFloor` key (ADR-0017 erratum 2026-10-17) (TD-177 item 2). `TryDebitPenalty` applies iff `balance − amount ≥ floor` (equality allowed) and must not divide. `StubWallet` holds a `SimCounterSink` over the injected `SimCounters`, because `Cents.Add` and `Cents.Sub` take `ref SimCounterSink`. Register the `Wallet` dump section, `SectionIds.Wallet = 6`, payload `balance i64` (TD-177 item 4); dump section order under the composition above is SimCore, World, Wallet, then the systems' sections (TD-177 item 5).
- **`WalletChanged` event (TD-177 item 3).** Block: Economy event block 0x0180–0x01BF; `OpKindBlock.Economy` is appended to `OpKindBlockTable.Blocks` (ADR-0007 §4 table amendment 2026-10-28, Evan 2026-10-28 7:26 AM PT, Mail Lead chat, card 'Mail-destinations 005 decisions': "Approve all six TD-177 defaults for mail-destinations 005, including the ADR-0007 §4 economy event block row"). Kind: `EconEventKinds.WalletChanged = (EventKind)0x0180`, in `Sim/Economy/`. Descriptor: `EventDescriptor.For<WalletChangedPayload>(kind, ReplicationClass.Replicated, coalescible: false, resyncScope: 2, wireCodecId: 0, wireSize: 0)` until story 015 assigns a codec. Payload: `long Balance; long Delta; byte Reason;` plus 7 explicit pad bytes = 24 B with no implicit padding (ADR-0017: ≤ 24 B; ADR-0008 padding-free rule); class Replicated, audience `ISlotView.ConnectedMask`. Penalty debits carry `Reason = 0` (`CreditReason.None`) and a negative `Delta`; credits carry their `CreditReason`.
- `SystemGraph` already declares Mail → Inventory (`IInventoryApi`) and Mail → WorldGeneration; it gains nothing here. The wallet port is not a graph edge (errata above).
- **Next free ids after this story** (on main 51b68cd with PR #52's reservation): `CounterId` 58, `SimAssertCode` 86, `SectionIds` 7, EventKind block 0x01C0; TD-180 (TD-178 is content-data 019's row, TD-179 is reserved for Mail Dev's throwing-catching 002 PR).

---

## Mail Lead planning defaults (TD-177, closed — Approved by Evan)

One batched row per PR (lighter process, decision log 2026-10-23). All six Approved by Evan 2026-10-28 7:26 AM PT, in his own words in the Mail Lead chat, card 'Mail-destinations 005 decisions': "Approve all six TD-177 defaults for mail-destinations 005, including the ADR-0007 §4 economy event block row". None contradicts an Accepted ADR once the two errata (approved on the same card: "Approve the ADR-0003 §4 and ADR-0017 §8 errata: the composition root injects IWalletGate into Mail only, and SystemGraph is unchanged") are applied.

1. **No `MailSettings` at M-1a** — `MailSystem` reads `content.Settings.MailTablePages` through the injected `ContentSet`; the first Mail-only knob adds `SimSettings.Mail` and its `settingsOwners` row.
2. **Starting wallet** — `sim.startingWalletCents` (int, default 0) as `SimSettings.StartingWalletCents`, registered in `design/registry/entities.yaml`; the floor stays a `Cents` argument.
3. **`WalletChanged` event** — Economy block 0x0180–0x01BF (`OpKindBlock.Economy`; ADR-0007 §4 amendment), `EconEventKinds.WalletChanged = 0x0180`, the descriptor and 24 B payload above; penalty debits carry `Reason = 0` and a negative `Delta`.
4. **Ids** — `CounterId.MailRowsVisited = 56`, `CounterId.MailBucketsVisited = 57`, `SectionIds.Wallet = 6` (`balance i64`), `SimAssertCode.MailApiNotImplemented = 85`.
5. **Dump section order** — SimCore, World, Wallet, then the systems' sections (named in AC-3).
6. **`IMailApi` at 005** — only `ConsumeHand`, `SetDeliveriesState` and `TryGetDestinationFacts`; `ResolveThrownLanding`/`LandingResult` with story 008, `ConsumeFed` with M-1b; the TD-079 items 1 and 5 and TD-074 item 4 obligations land here.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007/008: the per-unit calls from the consume routine (M-X-4). Story 010: `WalletMirror` (client).
- The real Economy (shop, payday, `IEconomyApi`): future Economy ADR.
- Feeder credits arithmetic (F11, M-1b).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY run 2026-10-28 in full mode — this is an M story, so it keeps the full loop; its gaps (the gap-1 composition conflict, the six planning defaults and the mechanical fixes) are resolved above. Log: `/workspace/repos/logs/md005-readiness.txt`.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/mail/MailCompositionTests.cs`; unit: `tests/unit/mail/StubWalletTests.cs`, `tests/unit/mail/MailArchitectureTests.cs` — must exist and pass
- Edited: `tests/unit/architecture/CompositionRulesTests.cs` (the `IWalletGate` table rule, Mail only), the `SimCountersTests` length pin (56 → 58)
- Legs: dev loop net10.0 Debug; close net10.0 Debug and net8.0 Release in full; CI all four Sim legs (net10.0/net8.0 × Debug/Release) plus "Game project build (no editor)". Baseline on main 51b68cd is 5,561 tests per leg. The 0 B checks are advisory until M0 (E2).

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002; `production/epics/simulation-core/story-025-composition-root-reflection-test.md`, `production/epics/simulation-core/story-015-event-log-call-scopes-delivery.md`; `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md` (`IInventoryApi` type); `production/epics/world-stub/story-007-world-dump-composition-replay-id.md` (World composed first)
- Unlocks: Story 006, Story 007, Story 008, Story 009
