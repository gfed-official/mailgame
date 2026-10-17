# Architecture Review Report
Date: 2026-10-16
Mode: full scope, `--review lean`, automation autonomous (technical-director decisions logged in production/session-logs/decision-log.md)
Engine: Godot 4.7.1 (.NET / C#, Jolt, Forward+)
GDDs Reviewed: 10 with a TR baseline (simulation-core, content-data-definitions, input-controls, player-character-movement, grid-inventory, mail-destinations, manual-delivery, throwing-catching, hud-notifications, comms). belt-logistics, building-system and health-damage have no TR baseline yet (extraction deferred, NOT ASSESSED).
ADRs Reviewed: 19 (0001–0017, 0019, 0026; all Accepted)

Reviewed-Content-Hash: docs/architecture/adr-0001-solution-layout-build-and-test.md 2d06fdbd898c8babc72ea0a39e8fc0502682e1c2
Reviewed-Content-Hash: docs/architecture/adr-0002-tick-driver-and-main-thread.md 5f97e1f3f6854757dc108d93bfadb7ccf3202108
Reviewed-Content-Hash: docs/architecture/adr-0003-phase-order-and-composition.md 5f165c95eda880c8ac10ef1864e7ad2ad1949f29
Reviewed-Content-Hash: docs/architecture/adr-0004-identity-and-id-spaces.md b291bd78e1ef12f8ceb56d5b4095e537579f0e3d
Reviewed-Content-Hash: docs/architecture/adr-0005-rng-streams-and-derivation.md d285c14a2d1475af3126ca26b86c128774c5714c
Reviewed-Content-Hash: docs/architecture/adr-0006-numerics-determinism-dump-replay.md 400d672f0d99089b93c02f18112afeb5b1c93bff
Reviewed-Content-Hash: docs/architecture/adr-0007-op-queue-drain-and-replies.md 2893ddecb1567ffd1d870efd792a2097f65490cf
Reviewed-Content-Hash: docs/architecture/adr-0008-event-log-replication-and-delivery.md 8ff0659a526ae859869c6d799313193e9b49663e
Reviewed-Content-Hash: docs/architecture/adr-0009-inventory-op-pipeline.md f11e06fcac816cee34515e9f7ec1c49cf565d9a8
Reviewed-Content-Hash: docs/architecture/adr-0010-content-format-and-statsheet.md 69d65db04fc0933ccd0c34c3691fa31dfeeecc5e
Reviewed-Content-Hash: docs/architecture/adr-0011-network-adapter-boundary.md 2bdae49e6998be32f2d91198c2788c044cacdaf2
Reviewed-Content-Hash: docs/architecture/adr-0012-input-router-core-and-godot-input-adapter.md 0d942f7359aab156c7047df2da66588b8201447b
Reviewed-Content-Hash: docs/architecture/adr-0013-carrier-kinematic-solver-and-fixed-point-geometry.md c3f246d6282cb917ea56eb0aa9ec5b42f9b5b1a9
Reviewed-Content-Hash: docs/architecture/adr-0014-interaction-framework-and-hold-timing.md 5aa8a38944fb79663b91dfd2162a26ff2c8ac8d2
Reviewed-Content-Hash: docs/architecture/adr-0015-shared-integer-ballistic-sweep-and-flight-resolution.md c25b0ade036d3d9d9cbb2a385ee2b212cbc9fb1c
Reviewed-Content-Hash: docs/architecture/adr-0016-clientlayer-previews-and-presentation-timing.md feac56417bd17e75fc8e64210b2102dd0662a481
Reviewed-Content-Hash: docs/architecture/adr-0017-mail-table-destination-registry-and-wallet-port.md 7c42b9ea360272b933c6295ad7e4645d827a55f9
Reviewed-Content-Hash: docs/architecture/adr-0019-godot-scene-architecture.md cb96dee76de833375a4f2af5a122afc68fbf0da8
Reviewed-Content-Hash: docs/architecture/adr-0026-world-query-contract-and-m1a-street-stub.md 957a0f8e1b97d282bbf90016f2a0af88183e2a2b
Reviewed-Content-Hash: design/gdd/belt-logistics.md 7f1dc4fdf0fc62080364642170273a9a78037a5a
Reviewed-Content-Hash: design/gdd/building-system.md b3d4843041ca7d8766b85c9dc06ac1304fe7cad4
Reviewed-Content-Hash: design/gdd/comms.md e6905af2702f449ddee8eaa94817b8e1a05160a9
Reviewed-Content-Hash: design/gdd/content-data-definitions.md fa29d8a570bbe13cebd5bd4b7c304d7df6180263
Reviewed-Content-Hash: design/gdd/game-concept.md 5dd604c87b0ddc4e12740001308eb1a498379c33
Reviewed-Content-Hash: design/gdd/game-pillars.md bd630ba9ca8b782ad8939c0782ece85432ee97f7
Reviewed-Content-Hash: design/gdd/grid-inventory.md cc1b104515b80b59e6289c0202a6082adfa5ede4
Reviewed-Content-Hash: design/gdd/health-damage.md 10b1026e1b8aad933cc6fa14208680f3767b0a62
Reviewed-Content-Hash: design/gdd/hud-notifications.md 83a08a2f95dcbf78bfb674f158eafbf68e30f868
Reviewed-Content-Hash: design/gdd/input-controls.md ae9501d5c4bd1fc9ee717783a493d807bb5a8e84
Reviewed-Content-Hash: design/gdd/mail-destinations.md 9e1fa223e88f64f9d454c02c3dec4b30c1862195
Reviewed-Content-Hash: design/gdd/manual-delivery.md a62bd5650cea397d0f6fdbdbd0ba1973bae65f74
Reviewed-Content-Hash: design/gdd/player-character-movement.md a2f9ca09858ea6a5291c10a8577ac33c0a913bc7
Reviewed-Content-Hash: design/gdd/simulation-core.md 17356a242ddb2fc67652bcff588e02f34f9805d1
Reviewed-Content-Hash: design/gdd/systems-index.md 9b11651ea3b1264144b1604ce455f7ed81fde50e
Reviewed-Content-Hash: design/gdd/throwing-catching.md 386b634018c2b82a5e8ba357d98acc91c6b36a7f

---

### Traceability Summary
Requirement source: `docs/architecture/architecture.md` Appendix A (250 rows), imported into the new `docs/architecture/tr-registry.yaml` (v1). Coverage was cross-checked against each ADR's `## GDD Requirements Addressed` and against all story files.
Total requirements: 250
✅ Covered: 203 (primary ADR Accepted)
🟡 Covered (Proposed): 0
⚠️ Partial: 0
❌ Gaps: 47
Story-cited TR-IDs: 95 distinct. All 95 resolve in the registry (0 unresolved).

### Coverage Gaps (no Accepted ADR)
Every gap already has a planned home in architecture.md §Required ADRs, and none is in the M-1a story set except TR-sim-037..039 (simulation-core story 029, advisory perf timing).
- ADR-0018 HUD composition: TR-hud-001, 002, 005, 006, 008–018, 021; TR-delivery-019, 020; TR-comms-007, 008. Domain: UI. Engine risk: MEDIUM (4.5–4.7 Control accessibility).
- ADR-0020 Transforms & Teleport: TR-movement-003, 008–010, 015, 023, 026. Domain: Networking/Movement. Engine risk: HIGH (multiplayer NOT SOURCEABLE).
- ADR-0021 Flight protocol & catch: TR-throwing-010–012, 014, 015. Domain: Networking. Engine risk: HIGH.
- ADR-0022 JoinState & resync: TR-inventory-023, 024. Domain: Networking. Engine risk: HIGH.
- ADR-0024 Persistence: TR-delivery-022, TR-throwing-020, TR-hud-019. Domain: Persistence. Engine risk: LOW.
- ADR-0025 Inventory indexing: TR-inventory-008, 009, 016, 018. Domain: Core. Engine risk: LOW.
- ADR-0029 Animation & camera: TR-movement-024, 025. Domain: Presentation. Engine risk: MEDIUM.
- perf-budget.md (not an ADR, not yet written): TR-sim-037–039, TR-movement-021.

### Cross-ADR Conflicts
Known conflict-prone areas (docs/consistency-failures.md): op payload/ownership across Inventory/Mail/Delivery; reqId width and dedupe placement; name drift between ADRs written in separate batches.

**Resolved in this review (technical-director errata, logged):**
1. **ADR-0009 §1 vs Manual Delivery 12/16 B (TR-delivery-008).** This is an ADR-vs-GDD wire-layout conflict, and it blocked network-adapter-loopback story 008. Ruling: range-gated ops carry the claimed position (grid-inventory R7, ADR-0009 §1, ADR-0014 §5). Wire = envelope `kind u16 · reqId u16` + `mailboxId u32 · holdStartTick u32 · ClaimedPosition 12 B` [+ `selectedEntryId u32`] = **24 B MATCH / 28 B MISDELIVER_ONE**. The Sim payload is 24 B fixed. Erratum recorded in ADR-0011 and ADR-0009 GDD Impact. TR-delivery-008 revised.
2. **🔴 ADR-0009 vs ADR-0017 (also ADR-0007 §4, ADR-0014 §5): `DeliverMatching` ownership, data ownership/dependency direction (was BLOCKING).** ADR-0009 put the OpKind in Inventory's block with an Inventory handler that calls Mail. ADR-0017 says "Inventory must never call Mail" and has Delivery's handler call `ConsumeHand`. Ruling: ADR-0017 governs. The OpKind moves to the Mail/Delivery block `0x0100–0x013F` with its handler in `Sim/Delivery`, and Inventory supplies the scan, `ConsumeUnit` and seals. Erratum recorded in ADR-0009 GDD Impact. grid-inventory story 022 is set Blocked pending re-scope.

**Open, non-blocking (errata for the next ADR revision):**
3. ADR-0009 vs ADR-0014: `CheckInteraction` can return Dead/Frozen/Unavailable, but Inventory's reason table (64–85) has no codes for them. Add the codes.
4. ADR-0019 §3 vs ADR-0002 §3: ADR-0019 starts `TickDriver.Start` at `_Ready` step 9, but ADR-0002 requires the first `_Process`. Correct ADR-0019.
5. ADR-0003 §2 vs ADR-0007 Key Interfaces: ADR-0003 defines `TickContext` = {Tick, Phase} with no services, but ADR-0007 has it carry the EventLog writer and counters. ADR-0003 governs.
6. ADR-0003 vs ADR-0006: `IPhaseProbe` is `Before/After(PhaseId)` in ADR-0003 but `Enter/Exit(ProbeScope)` in ADR-0006. ADR-0003 owns it.
7. ADR-0011 `EnqueueOp(in Op)` vs ADR-0003/0007 `EnqueueOp(ActorId, in Op)`. Erratum to ADR-0011.
8. ADR-0008 §1.4 has `DeliveryOutcome` ≤ 19 B and one per op, but ADR-0017 §5/§9 has it at 34 B Sim-side and k per hold. ADR-0017 governs.
9. ADR-0003 §4 closed constructor list omits `IWorldQuery` and `IWalletGate` (ADR-0013/0017/0026). Amend the list and its reflection test.
10. Name/shape drift: `OpKindDescriptor`/`EventKindDescriptor`/`BindReader` (0003) vs `OpDescriptor`/`EventDescriptor`/`BindEventReader` (0007/0008); `Hold.HoldEff`/`FlightQuery.Sweep` (0016) vs `Hold.HoldTicks` (0014)/`FlightSweep.FirstContact` (0015); `IClientInbox` implementer reversed between 0011 and 0016; height is `int` (0013) vs `long` (0026); ADR-0017 Key Interfaces still declare `AddressId` in `Sim.Mail`.
Residual note "ADR-0009/0011 cite ADR-0008 §2 (d)" is fixed (both now cite §7 (d)).

### ADR Dependency Order
`adr-dep-graph.sh` reported cycles, but they are a parser false positive. ADR-0026's Depends On cell says "*Not a dependency:* ADR-0017", and the script reads that as an edge. Strongly-connected components over the real edges: none, so the graph is acyclic. No NO_DEPS_SECTION. Every dependency is Accepted.
Foundation: 0001 → 0004 → 0003 → 0002; 0005 → 0006 → 0007 → 0008 → 0010 → 0009 → 0011.
Core/Feature: 0013 → 0014 → 0012 → 0026 → 0017 → 0015 → 0016 → 0019 (the acceptance order of 2026-10-16).

### GDD Revision Flags
No GDD revision flags from engine findings (no HIGH-risk engine finding contradicts a GDD assumption), so systems-index is unchanged. GDD-sync items from the errata (not engine-driven, owners apply at re-review): manual-delivery.md Network table + M-NET-1 (24/28 B, ADR-0011 envelope); grid-inventory.md R7 / EPIC note (DeliverMatching handler is Delivery's).

### Engine Compatibility Issues
Engine: Godot 4.7.1. ADRs with an Engine Compatibility section: 19/19. Version references are consistent: all 4.7.1, and the "4.4" hits are "4.4–4.7" ranges. Deprecated-API references: none used; every match is an "avoid/replacement" note. Post-cutoff API conflicts: none.
Engine specialist findings (godot-specialist, verdict CONCERNS):
- MEDIUM ADR-0012/0019: the InputAdapter node is not placed in ADR-0019's tree. `ProcessPriority` does not order `_Input` (that runs in reverse tree order), so the adapter must be the last sibling. Add to the OQ-IC-9 spike.
- MEDIUM ADR-0019 §7: below 16:9, `keep_height` likely letterboxes with scale = width/1920 rather than clipping. Verify in V4 and fix the risk text.
- MEDIUM ADR-0019 §6: `AccessibilityLive` on a custom-drawn `Control` may never be announced. Use a `Label`, or publish accessibility info. UNVERIFIED, so spike it.
- LOW: hand-written `project.godot` does not set `physics/3d/physics_engine` (Jolt may not be active). ADR-0019 §5 MultiMesh pre-`InstanceCount` settings are `TransformFormat`/`UseColors`/`UseCustomData`. ADR-0002 F11 cause attribution is likely the physics-step cap. ".NET 8 minimum" for 4.6/4.7 desktop is NOT SOURCEABLE (ADR-0005–0011).
Multiplayer/headless claims are correctly marked NOT SOURCEABLE and spike-gated.

### Architecture Document Coverage
Every systems-index system with a GDD (incl. Belt Logistics, Building, Health & Damage, Comms) appears in architecture.md's layer map. No orphaned architecture. architecture.md §Traceability coverage was stale (0/250) and is now updated to 203/47. Its TR-delivery-008 row is revised.

---

### Verdict: CONCERNS
Not PASS because: 47 requirements have no Accepted ADR (all in planned, milestone-deferred ADRs or perf-budget.md); 8 non-blocking cross-ADR contract/name drifts remain; the engine specialist raised 3 MEDIUM items; 3 GDDs have no TR baseline yet. Not FAIL because the one blocking cross-ADR conflict (DeliverMatching ownership) was resolved in-review by a logged erratum, and no Foundation-layer requirement in the M-1a slice is uncovered.

### Required ADRs (most foundational first)
1. perf-budget.md (TR-sim-037–039, TR-movement-021; cited by simulation-core story 029)
2. ADR-0022 JoinState & resync (M0)
3. ADR-0020 Transforms & Teleport
4. ADR-0021 Flight protocol & catch (M0)
5. ADR-0025 Inventory indexing
6. ADR-0018 HUD composition
7. ADR-0024 Persistence
8. ADR-0029 Animation & camera
Then extract a TR baseline for belt-logistics, building-system and health-damage after their re-reviews.
