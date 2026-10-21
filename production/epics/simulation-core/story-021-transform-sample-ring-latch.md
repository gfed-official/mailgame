# Story 021: Transform sample ring, tick-boundary latch and stamping

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 350 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R5 sample stamping and recording; R18 thread rules; N1 sample ring crossing)
**Requirement**: `TR-sim-045` (recorder stores ops **and transform samples** — this story provides the sample path), `TR-sim-008` (nothing applied inside a callback — samples are stamped at the drain, never in a callback)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0003: Phase Order, Composition Root and Cross-System Calls (§1 last bullet, §2 prologue) — primary; ADR-0011 (§1 sample-ring crossing), ADR-0006 (§4 latch in section 1, §7 `OnSample`), ADR-0002 (§5 owner thread) — secondary
**ADR Decision Summary**: Transform samples are not ops. The adapter writes them into a sample ring between ticks on the Sim thread; Sim Core drains the ring into the latch in the `SimWorld.Tick` prologue — immediately before the tick that ingests them — stamping each `receivedTick = CurrentTick`. Each sample carries a per-sender sequence number; the latch keeps samples in sequence order and discards older-or-equal ones. Phase 3 (Player Character) ingests the latch. Every ingested sample is recorded via `IOpRecorder.OnSample(receivedTick, slot, in TransformSample)` from M-1a, and the latch is part of the canonical dump.
**ADR Version**: 2026-10-16 (ADR-0003, ADR-0011, ADR-0006, ADR-0002)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None for the Sim. Which thread transport callbacks arrive on is UNVERIFIED (M0 spike) — irrelevant here because the adapter writes the ring from `RunFrame` step 1 on the main thread.

**Control Manifest Rules (Foundation)**:
- Required: samples drained into the latch between ticks, immediately before `Tick()`'s phases, stamped `receivedTick = CurrentTick`; owner-thread assert in the sample-ring drain (`SIM_ASSERT`); `IOpRecorder.OnSample` at the latch drain; latch included in Sim Core dump section 1; ring preallocated (tick-path collections sized from settings, no growth after warm-up).
- Forbidden: stamping or writing the ring from a transport callback; floats in samples; growth of the ring after construction.
- Guardrail: drain allocates 0 B.

---

## Acceptance Criteria

*From GDD R5 (sample stamping, recording) and ADR-0003 §2 prologue, scoped to this story (no M-1a GDD AC targets the latch alone; SC-DET-1 and SC-R30-1 depend on it):*

- [ ] **AC-1 Stamping at the boundary.** Samples written between tick T−1 and tick T are not visible to anything during tick T−1, are drained in the tick-T prologue (before phase 0), and each is stamped `receivedTick == T`; a sample written from inside a phase of tick T is drained at tick T + 1's prologue.
- [ ] **AC-2 Sequence ordering.** For one sender, samples with seq 5, 3, 6, 6, 7 written in that order latch as 5, 6, 7 (older-or-equal discarded, counted); senders are independent; the latch exposes the tick's samples to a phase-3 reader in slot order then sequence order.
- [ ] **AC-3 Recording and dump.** An `IOpRecorder` stub receives `OnSample(receivedTick, slot, sample)` exactly once per latched (not discarded) sample, in latch order; the Sim Core dump section's latch block (Story 019) encodes the per-slot last accepted seq and the latched samples field by field, so changing one latched sample changes the dump hash.
- [ ] **AC-4 Safety.** Writing to the ring from a non-owner thread asserts under `SIM_ASSERT`; a write to a full ring returns false and is counted (never grows, never overwrites); 10 000 write/drain cycles allocate 0 B.

---

## Implementation Notes

*Derived from ADR-0003 §1–§2, ADR-0006 §4/§7, GDD R5:*

- **`TransformSample` shape — provisional.** GDD R5 makes it "a Core-owned blittable type", but its field list belongs to Player Character's ADR-0013 (Accepted 2026-10-16) / ADR-0020 (not yet written; M0). Define `src/PerformativeMail.Sim/Core/TransformSample.cs` as a padding-free `unmanaged` struct with the fields this story needs (`PlayerSlot` raw byte, `Seq` u32) plus the Player Character GDD's sample content as integers (position mm `int` ×3, yaw/pitch BAM `ushort`, a flags byte, explicit pad bytes) and mark it `// PROVISIONAL — layout owned by ADR-0013/0020`. The ring, latch, recorder and dump code must not depend on the body fields beyond copying them and writing them field by field (keep a `WriteCanonical(ref CanonicalWriter)` on the struct). The player-character epic may change the body; the mechanics here stay. (Assumption logged in the decision log.)
- **Seq width.** u32 monotonic per sender within a connection (no wrap within a run at 15 Hz); plain `>` compare. If ADR-0020 later narrows it to u16 on the wire, the adapter widens it — the Sim keeps u32.
- **Ring:** `SampleRing` (Core), preallocated `TransformSample[SampleRingCapacity]` (power of two). `SampleRingCapacity` is a new `SimCoreSettings` key — default 256 (8 players × ~4 frames of 15 Hz samples with headroom; PROPOSED, logged). Full ring → `TryWrite` returns false and counts `samplesRefused` (no policy is specified in the GDD/ADRs — conservative choice, logged).
- **Prologue** (in `SimWorld.Tick()`, after the guard, before phase 0): assert owner thread; clear the latch's per-tick list; drain the whole ring into the latch, stamping `receivedTick = CurrentTick`; for each accepted sample call `recorder.OnSample`. Discarded samples increment `samplesStale`.
- **Latch reader for phase 3:** `ISampleLatch` (read-only: `Count`, `this[i]`, `ReceivedTick`, `LastSeq(slot)`) injected into Player Character by composition. `IRemoteCapsuleSource` (T−1 semantics for the host's `ClientLayer`, SC-R2-3) is M0 and not here.
- Sample-ring writes come only from the host adapter (network-adapter-loopback) via `SimWorld.TryWriteSample(PlayerSlot slot, in TransformSample s)` — add it to the seam list; `ClientLayer` never writes it directly.

---

## Out of Scope

- Phase-3 ingest, Teleport `seq` discard rule, position history: player-character epic (ADR-0013/0020).
- Adapter-side sample encoding/decoding: network-adapter-loopback (transform replication itself is M0).
- Story 026: `InMemoryRecorder` storing samples and replaying them at their `receivedTick`.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/SampleLatchTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/sim/Core/SampleLatchTests.cs (17 tests incl. NonParallel SampleLatchAllocationTests); CanonicalDumpTests section-1 layout updated

---

## Dependencies

- Depends on: Story 012 (prologue position, owner thread), Story 019 (dump section 1 latch block), Story 020 (`IOpRecorder` declared)
- Unlocks: Story 026 (sample recording/replay), Story 027 (SC-DET-1 sample log), player-character phase 3

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 4/4 passing (all covered by unit tests; no deferred items)
**Deviations**: Advisory only — (1) TransformSample (Core/TransformSample.cs) PROVISIONAL 24 B Pack=1: Seq u32, PosX/Y/Z mm i32, YawBam u16, PitchBam u16, Slot u8, Flags u8, Pad0/Pad1 (layout owned by ADR-0013/0020). (2) Samples still pending in the ring between ticks are NOT in the dump (recorder captures at drain; test pins it). (3) Latch keeps one `ReceivedTick`; per-slot `hasSeq` so a first Seq 0 is accepted. (4) Dump latch block: 8 × (hasSeq u8, lastSeq u32), ReceivedTick u32, count u32, samples field by field in slot-then-seq order. (5) `TryWriteSample` stamps Slot from its argument; refuses + counts on full ring / None slot / world not Ready-Running / off-thread in Release (asserts WrongThread under SIM_ASSERT). (6) `SimCoreSettings.SampleRingCapacity` = 256 (PROPOSED); no content key yet (Content/ owned elsewhere — TD-049 item 4). New: CounterId SamplesStale = 28, SamplesRefused = 29; SimAssertCode SampleRingCapacityNotPow2 = 49. Resolved TD-045 item 5, TD-048 item 5. Gate advisories logged as TD-049 (item 1 → story 022 ResetSender).
**Test Evidence**: Logic — tests/unit/sim/Core/SampleLatchTests.cs; four legs (net10/net8 × Debug/Release) green on the real tree, 1 824 tests/leg at implementation (spot re-run Debug net10: 1 877, 0 failed, 14 skipped).
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (7 advisories), QL-TEST-COVERAGE ADEQUATE (5 advisories).
