# Story 019: Canonical state dump (CanonicalWriter, contributors, Sim Core section)

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 1 day; ≈ 450 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R29 determinism tiers, R30 canonical dump)
**Requirement**: `TR-sim-035` (canonical dump via `ISimStateContributor`, fixed order, little-endian; SHA-256 in tests only)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (§4) — primary; ADR-0005 (§3 stream tuple), ADR-0007 (§1–§2 queued ops), ADR-0004 (counters, slot table) — secondary
**ADR Decision Summary**: Every stateful system binds `ISimStateContributor { ushort SectionId; void Write(ref CanonicalWriter w); }`. `CanonicalWriter` is a forward-only little-endian writer that buffers each section and flushes `sectionId u16, byteLength u32, payload` to an `IBufferWriter<byte>` at `EndSection`. Format v1: header `"PMSD"` (u32 LE `0x44534D50`) + `formatVersion` u16 = 1, then sections in builder registration order. Fields are written one by one (never a struct blit); collections are `u32 count` + elements in ascending id/`opSeq`/slot; no strings. `SimWorld.TryWriteDump` succeeds only between ticks on a `Ready`/`Running`/`Stopped` world; inside `Tick()` (including from the sink) it returns false and counts `dumpDuringTick`. The Sim never hashes its own state; tests take SHA-256 of the dump.
**ADR Version**: 2026-10-16 (ADR-0006, ADR-0005, ADR-0007, ADR-0004)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None. `System.Buffers.IBufferWriter<byte>` and `BinaryPrimitives` are BCL.

**Control Manifest Rules (Foundation)**:
- Required: `ISimStateContributor` with unique, never-reused `SectionId`s; fields one by one (`WriteU8…WriteI64`, `Bool` as u8), LE, collections `u32 count` + ascending order; dump header `"PMSD"` + `formatVersion` 1; sections in builder registration order; Sim Core section 1 per ADR-0006 §4; `TryWriteDump` only between ticks; tests take SHA-256.
- Forbidden: blitting structs into the dump (`MemoryMarshal.AsBytes` over state); depending on insertion order; hashing state in the Sim tick path; requesting a dump inside a tick; strings or culture-dependent text in the dump.
- Guardrail: the dump is never on the tick path; its internal buffer is reusable.

---

## Acceptance Criteria

*From ADR-0006 §4 (the canonical artefact SC-R8-2, SC-R24-1 and SC-DET-1 hash), scoped to this story:*

- [ ] **AC-1 Format.** A dump of a fresh `Ready` world begins with bytes `50 4D 53 44 01 00` ("PMSD", version 1), followed by section 1 (`01 00`, a u32 byte length equal to the payload length, payload) and then each bound contributor's section in registration order; a contributor writing `WriteU16(0x1234)` emits `34 12`; two contributors with the same `SectionId` fail the build.
- [ ] **AC-2 Sim Core section 1 contents.** Section 1 encodes, in this order: `CurrentTick`; every `EntityId` class counter for classes 0x01–0x08 (and any test class in use) in ascending class; the `mailId` counter; `opSeq`; every live RNG stream as `(nameOrdinal u16, index u32, state u64, inc u64, draws u64)` in `RngNames` order then index (never-created streams absent); the queued ops (u32 count, then each op's header fields and its 48 payload bytes, plus its boundary tick) in ring (`opSeq`) order; the transform-sample latch block (u32 count — 0 until Story 021 fills it); the slot table (8 entries, slot order). Changing any one of these in turn changes the SHA-256 of the dump. Calling `Create(mail, 1)` twice yields one stream entry for that key with `draws == 0` (the dump half of SC-R24-1).
- [ ] **AC-3 Validity and order independence.** `TryWriteDump` returns false and counts `dumpDuringTick` when called from a phase or from `IEventSink.Deliver`, and returns false on a `Faulted` world; it succeeds on `Ready`, `Running` (between ticks) and `Stopped` worlds. A test contributor holding a keyed collection populated in two different insertion orders produces byte-identical dumps (it writes in ascending key order).

---

## Implementation Notes

*Derived from ADR-0006 §4, Key Interfaces and Implementation Guidelines:*

- `src/PerformativeMail.Sim/Primitives/CanonicalWriter.cs`: `public ref struct CanonicalWriter` with `WriteU8/U16/U32/U64/I32/I64/Bool/Bytes`, `BeginSection(ushort id)`, `EndSection()`. Back it with a reusable section buffer (a `byte[]` owned by the world's dump service, grown only outside the tick) and `BinaryPrimitives.Write*LittleEndian`. At `EndSection` write `id`, `length`, payload to the `IBufferWriter<byte>`. ADR-0024 (persistence) and JoinState will reuse it — keep it system-agnostic.
- `src/PerformativeMail.Sim/Core/ISimStateContributor.cs`; `SimWorldBuilder.BindStateContributor(...)` (duplicate `SectionId` → `SimBuildException`, new code `DuplicateSection`); a `SectionIds` constants class (`SimCore = 1`; systems append, never reuse).
- `SimWorld.TryWriteDump(IBufferWriter<byte> output)`: refuse unless stage is `BetweenTicks`, not delivering, and state ∈ {Ready, Running, Stopped}; write the header; write section 1 from Sim Core's own state (an internal `SimCoreStateContributor` registered first); then each bound contributor in registration order (composition order — ADR-0003 §4).
- Op encoding in the dump: write `OpSeq`, `Actor`, `Kind`, `PayloadLength`, `Reserved` and `ReqId` field by field, then the 48 payload bytes (they are canonical because they are zero-filled — ADR-0007 §1), then the boundary tick.
- Excluded from the dump: `SimCounters`, the check ring, perf counters, caches, the event log (empty between ticks), anything derivable.
- Test helper (test project): `DumpHash.Sha256(SimWorld)` using `System.Security.Cryptography.SHA256` over an `ArrayBufferWriter<byte>` — hashing lives only in tests/tools (manifest I-5 concerns the *content* hash, ADR-0010; not this).
- `CanonicalHash64` is **not** built here (only when a cross-machine checksum first needs it).

---

## Out of Scope

- Story 021: the transform-sample latch contents (this story writes the block's count as 0 and leaves the encoding hook).
- Story 026: recorder header (`SimSettings` canonical encoding hash).
- `ISnapshotSource` and `BindSnapshotSource` (frozen interface, built by its first consumer — grid-inventory / network-adapter-loopback).
- SC-R30-1 (M-1b) full include/exclude matrix — AC-2/AC-3 cover its mechanics.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/CanonicalDumpTests.cs`, `tests/unit/sim/Primitives/CanonicalWriterTests.cs` — must exist and pass

**Status**: [x] Created — CanonicalDumpTests.cs (40 incl. NonParallel CanonicalDumpAllocationTests), CanonicalWriterTests.cs (12); four legs green

---

## Dependencies

- Depends on: Story 009 (RNG registry), Story 014 (op ring), Story 015 (delivery stage flag), Story 018 (allocators, slot table)
- Unlocks: Story 020 (SC-R8-2), Story 021, Story 026, Story 027 (SC-DET-1), grid-inventory H-DET-1

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 3/3 passing (all covered by unit tests; no deferred items)
**Deviations**: Advisory only — (1) `SimWorldBuilder.BindCoreState(EntityIds?, MailIds?, RngRegistry?, SlotTable?)` added (not named by ADR-0006) so pre-Freeze allocators/registry/slot table are dumped; null sources dump as fresh; Story 025 must wire it. (2) Extra `SimBuildErrorCode.InvalidSection = 22` (section id 0) beside `DuplicateSection = 21`; `SimAssertCode.DumpWriterMisuse = 46`; existing `CounterId.DumpDuringTick` reused. (3) The world wraps each contributor in Begin/EndSection; contributors write fields only. (4) Faulted/Constructed/re-entrant dumps return false without counting; only in-tick calls count `dumpDuringTick`. (5) Id counters dumped as LAST-ISSUED values (TD-043). Section-1 test classes 0xF0–0xFF are written as u32 count + (class u8, counter u32) for non-zero counters. Latch block = u32 0 with `TODO(Story-021)`. Gate advisories logged as TD-045.
**Test Evidence**: Logic — tests/unit/sim/Core/CanonicalDumpTests.cs, tests/unit/sim/Primitives/CanonicalWriterTests.cs (helper tests/PerformativeMail.Sim.Tests/DumpHash.cs); 1 712 tests/leg green (net10/net8 × Debug/Release; Debug 11 skipped).
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (7 advisories), QL-TEST-COVERAGE ADEQUATE (6 advisories).
