# Simulation Core — Review Log

## Review — 2026-10-16 — Verdict: MAJOR REVISION NEEDED
Scope signal: L (foundation; 9 new ADRs; every Sim-side system depends on it)
Specialists: systems-designer, lead-programmer, godot-csharp-specialist, network-programmer, qa-lead, creative-director (senior), technical-director (second senior voice: budget split, ADR list, scope)
Blocking items: 11 | Recommended: 24 (cheap ones applied; rest deferred to Open Questions)
Summary: The core contracts hold up: one phase order, one op queue, one ordered event log, the determinism tiers, and F5/F6 (golden vectors recomputed independently and correct). Three things push this past NEEDS REVISION. (1) It contradicts ratified spec 06 §6 on mid-run reconnect. (2) Its edge contracts fail under load: unbounded drain, overflow audience loss, coalescing that breaks reply order, a single binder per phase. (3) It is structurally oversized: 1,772 lines and 131 ACs for a contract GDD. The creative-director ruled a cut to ≤ 1,050 lines and ≤ 55 ACs. Algorithms, layouts, build mechanics and perf slices move to an UNREVIEWED SEED for the TD's 9 ADRs.
Prior verdict resolved: First review
Findings:
- [BLOCKING] R21 / Edge Cases: rejects every PlayerJoin after Prep (`join_closed`). This contradicts spec 06 §6 and PCM P19, which allow a same-account reconnect into a Frozen body within 120 s. Join/reconnect snapshot race not closed (joiner must buffer batches > joinTick).
- [BLOCKING] R8 / F8 / F3: drain is unbounded. A 1 s stall puts about 3 840 ops into one tick, overflows the event log and blows the phase-0 slice. Fix: `maxOpsDrainedPerTick`.
- [BLOCKING] R14 / N3: overflow resync is computed from surviving entries, so an audience whose only entries were dropped is missed. Fix: `droppedAudienceMask`/`droppedKindsMask`.
- [BLOCKING] R3 / R4: duplicate PhaseId bind is an error, yet phase 11 is "any system that reacts". OnTickEnd per bound phase has no defined order. No port for Stop(); no Faulted state. ISnapshotSource/ISimStateContributor have no bind point.
- [BLOCKING] R13 vs R10: phase-scoped coalescing lets op 2 rewrite an entry that precedes op 1's reply. "Event then reply by construction" and "batch order is causal" are false.
- [BLOCKING] Edge Cases (no reqId dedupe) contradicts Grid Inventory rule (8) and Manual Delivery M-NET-2 (duplicate reqId returns the original reply).
- [BLOCKING] F7 / R31: MulDivRH overflows Int128 at long.MinValue and c=0 is unspecified. `checked` fires at 2^63, not the Cents bound 2^62. A Debug-wide `checked` breaks PCG/Mix64/FNV unless the code is explicitly unchecked.
- [BLOCKING] R8 / R29 / R30: at M-1a phase 3 consumes transform samples, but samples are only recorded from M0 and the dump omits the latch, so a T1 replay is incomplete.
- [BLOCKING] R10: the reply reason namespace is undefined (Core reasons have no reserved range), and replies to the System actor are unrouted.
- [BLOCKING] R12 / N1 [technical-director]: `IEventSink.Deliver` runs inside Tick() and includes adapter serialisation, so Godot marshalling falls inside the 0-alloc and p99 spans and loopback can re-enter the Sim. Also, adapter slot→peer resolution at send time can leak a leaver's OwnerOnly events to a newcomer.
- [BLOCKING] Acceptance Criteria / scope: 131 ACs (100 at M-1a); frame-loop ACs test a Godot-side test double (no FrameRunner); no rule for observing asserts/counters; 4 SPIKE "ACs" have no pass condition. Doc 1,772 lines.
- [RECOMMENDED] F2: ticksBeforeGuard is circular, so Advance and DropRemaining need to be split, and the invariant has the wrong units. guard ≥ 2 × tick p99. Clock-backwards counter.
- [RECOMMENDED] F3: delete the "≥ 2 % spike" sentence and "Σ p99 conservative"; integer p99 rank; coincident tick every 150th; budgetReserve derived; budgets are not SimSettings knobs.
- [RECOMMENDED] Tuning Knobs: safe ranges contradict F8 (op 1024, log 2048). Reserves are omitted from F8. The F8 system-event estimate misses phases 1–2.
- [RECOMMENDED] F4: allocation rate is not op-bound; re-derive it per class. F5 collision figure is wrong (≈ 10^-17, not 10^-8). Specify `(uint)class << 24` and unchecked XSH-RR.
- [RECOMMENDED] F7 / F1: negative pm, Fx16.FromMm range and "RH" naming (half away from zero). The F1 round trip is not an identity. int64 deadline subtraction. Display above 99 min.
- [RECOMMENDED] R28 / SC-R28-1: BannedApiAnalyzers cannot reliably ban foreach over Dictionary/HashSet, so this needs a custom analyzer and a failing fixture (ADR).
- [RECOMMENDED] R33: the `Deterministic` csproj flag is build reproducibility, not runtime determinism. Godot ExportDebug/ExportRelease configuration mapping (UNVERIFIED). Zero-init op padding.
- [RECOMMENDED] R18: "MPSC and nothing else changes" is false. R17's `_PhysicsProcess` rejection is stated as fact but is UNVERIFIED.
- [RECOMMENDED] Network: busy-reply channel/order; dropped OpReply → owner resync; IHostClock estimator is Networking's; sample stamping at latch drain; loopback reply arrives at exactly the next client tick; wire codec / protocol hash owner.
- [RECOMMENDED] Visual/Audio vs R2: host Presentation sees tick T's batch in the same frame, and the host ClientLayer at the next client tick.
- [RECOMMENDED] Dependencies: Content & Data "not written" is stale (it is In Design).
- [RECOMMENDED] Player Fantasy: test 3 must exclude injected or logged host hitches; the header pillar line duplicates "Pillars served".
- [DEFERRED] 30 Hz judder on 120/144 Hz hosts (OQ1 spike); per-system resync scoping and cooldown (Networking); custom analyzer; ExportRelease mapping; trimming; reply latency at 2 % loss
Reviewed-Content-Hash: design/gdd/simulation-core.md f4b99598e4ecc6a09f44d8dbaff7ae136d328f90
Reviewed-Content-Hash: design/registry/entities.yaml afc00eff5ebea0f57a6bef258b39f0bd8326300c

### Revision applied — 2026-10-16 (same session, autonomous mode)
All 11 blocking items, the cheap recommended items and the creative-director scope ruling were applied. The GDD went from 1,772 to 1,046 lines (−41 %, under the CD ceiling of 1,050) and from 131 to 53 ACs (ceiling 55; 25 at M-1a against the TD ceiling of 25). The moved material is kept verbatim in `docs/architecture/simulation-core-adr-inputs.md`, together with the review corrections, the TD perf-budget rulings and the AC disposition. That file is an UNREVIEWED SEED. Nothing was deleted.
- **Joins:** R21 now accepts a same-account reconnect into the held, Frozen slot in any run phase; `join_closed` applies to new accounts only (spec 06 §6, PCM P19). N3.5 defines `joinTick`, a point-in-time snapshot and joiner buffering. FreezePlayer and the Frozen expiry route the carrier/inventory effects to PC and Grid Inventory.
- **Drain and overflow:** `maxOpsDrainedPerTick` 64 (R8, F8, knob, SC-R8-1/R8-4). `overflowInfo` carries `droppedAudienceMask` and `droppedKindsMask` (R14). The F8 constraints are enforced by `SimSettings` validation (SC-SET-1).
- **Composition:** several binders per phase in composition order; `ITickEndCheck`; descriptor/reader/contributor/snapshot binds; `Composition/` folder with profiles; `IRunControl.RequestStop()`; a `Faulted` state (States, SC-ST-1…3).
- **Coalescing and replies:** coalescing is scoped to one handler/API call (R13). Reply reasons are keyed by (OpKind, reason), with 1–63 reserved for Core; System replies have an empty audience; `reqId` is opaque. There is no generic dedupe; it is handler-local (Grid Inventory rule 8) (R10).
- **Numerics:** F7 states its result contract (half away from zero, overflow-free MulDivRH, zero divisor → 0 counted, explicit ±2^62 check). R28 rule 11 adds the unchecked hash/PRNG rule.
- **Replay:** samples are recorded from M-1a at the latch drain; T1 includes the sample log; the dump includes the latch (R5, R8, R29, R30).
- **Testability:** a Godot-free `FrameRunner` with `IMonotonicClock` (R17). The AC Legend defines assert observation (`SimAssertException`), `SimCounters` and the record ring. The 4 SPIKE items move to the spike plan.
- **TD blocker:** `Deliver` is copy-only, resolves peers inside the call, and the send happens after `Tick()` (R12, SC-X-4).
- **Recommended applied:** F2 two-step plus invariant; F3 gates only, with integer rank, 150th-tick coincidence, derived reserve and calibrated relative gate; knob ranges fixed and budget rows removed; F1 int64/99:59/round-trip; F4 per-class rate; F5 collision figure; busy reply channel; IHostClock bound (estimator to Networking); host timing in Visual/Audio; Content & Data row; Player Fantasy test 3 and header pillar line (CD).
- **Deferred to Open Questions:** OQ5 perf budget and network bucket; OQ9 per-phase re-derivation; OQ10 custom analyzer; OQ14 mid-Delivery entry; 120/144 Hz judder (OQ1); ExportRelease mapping and trimming (OQ4).
- **Hand-backs:** HB1–HB12 (new: HB1g–i, HB8 reply timing and dedupe source, HB10 expanded, HB11 Content & Data, HB12 Run Structure).
- **Registry:** updated catchUpAccumulator, tickBudgetSplit, entityIdLayout, rngStreamDerivation, moneyRounding, simPhaseOrder, maxCatchUpTicksPerFrame, opQueueCapacity (+ maxOpsDrainedPerTick), eventLogCapacity, eventOrderRule, determinismTiers, playerSlot and tickDriverDefault. No value owned by another GDD changed. The YAML validates.
