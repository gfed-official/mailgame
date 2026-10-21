# Story 013: Op and Reply value types, OpKind and op descriptors

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S–M (≈ 0.5 day; ≈ 300 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R6 what an op is, R10 replies and reasons)
**Requirement**: `TR-sim-015` (`Op` = 64 B blittable: 16 B header + ≤ 48 B payload), `TR-sim-020` (reply `{reqId, result, reason u16, extra ≤ 24 B}`; reasons 1–63 Sim)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Op Queue, Drain and Replies (§1, §4, §5, Key Interfaces)
**ADR Decision Summary**: `Op` is a 64 B blittable struct (`Pack = 4`): `OpSeq u32 · Actor u32 · Kind u16 · PayloadLength u8 · Reserved u8 · ReqId u32 · Payload [InlineArray(48)]`. Each system owns an `unmanaged`, padding-free payload struct read with `PayloadAs<T>()` (unaligned `MemoryMarshal.Read`, never `Unsafe.As`) and written with `SetPayload<T>` (zero-fill then unaligned write). `OpKind` is an append-only `u16` enum in per-system blocks; each kind registers an `OpDescriptor(Kind, PayloadLength, SystemOnly, WireCodecId, WireSize, Reasons)`. `Reply` (`Pack = 1`) is 36 B: `ReqId u32 · Kind u16 · Reason u16 · Result u8 · 3 explicit zero pad bytes · Extra [InlineArray(24)]`, `Ok ⇔ Reason == 0`; Sim Core reasons 1–63 come only from `CoreReason`, system reasons start at 64.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: `[InlineArray]` is a .NET 8 / C# 12 feature (available on the `net8.0` target), not a Godot API.

**Control Manifest Rules (Foundation)**:
- Required: `Op` layout and `Pack = 4` exactly; a test asserts `Unsafe.SizeOf<Op>() == 64`; payload structs are `unmanaged`, padding-free, ≤ 48 B, read/written unaligned; unused payload bytes and `Reserved` are zero; `OpKind` append-only, explicit values, 0 invalid, per-system blocks (Sim Core `0x0001–0x003F`, Players `0x0040–0x007F`, Inventory `0x0080–0x00FF`, Mail/Delivery `0x0100–0x013F`, Throwing `0x0140–0x017F`); `Reply` 36 B asserted; reason codes 1–63 only from `CoreReason`; system codes ≥ 64 declared in the descriptor (a test enumerates descriptors).
- Forbidden: `Unsafe.As` on payloads; `Int128` or references in payloads; an explicit-layout union of payload types in `Sim/Core`; reading the actor from a payload.
- Guardrail: payload accessors allocate 0 B.

---

## Acceptance Criteria

*Derived from ADR-0007 §1, §4, §5 (the type layer SC-R6-1/SC-R8-*/SC-R10-1 build on), scoped to this story:*

- [x] **AC-1 Layouts.** `Unsafe.SizeOf<Op>() == 64` with fields at offsets 0/4/8/10/11/12/16; `Unsafe.SizeOf<Reply>() == 36` with `Extra` at offset 12; `OpPayload` is 48 B and `ReplyExtra` 24 B; `Reply` has explicit zero pad fields so it is padding-free.
- [x] **AC-2 Payload round trip.** For a 12-byte and a 48-byte padding-free test payload, `SetPayload` then `PayloadAs` round-trips bit-exactly; `SetPayload` of the 12-byte payload over a buffer previously filled with 0xFF leaves bytes 12–47 zero; `PayloadAs<T>` with `SizeOf<T>() != PayloadLength` asserts under `SIM_ASSERT`.
- [x] **AC-3 Descriptor validation helpers.** A shared descriptor check (used by every system's descriptor test) asserts for a kind: `SizeOf<T>() ≤ 48`, `== PayloadLength`, sum of field sizes `== SizeOf<T>()` (padding-free), every declared reason ≥ 64, and `Kind` inside its owner's block; it fails for a padded struct, a 49-byte struct and a reason of 63. `CoreReason` exposes exactly `Ok 0, Busy 1, UnknownActor 2, UnknownKind 3, SlotInvalid 4, SlotBusy 5, DuplicatePlayer 6, JoinClosed 7, BadRequest 8, FirstSystemReason 64`.

---

## Implementation Notes

*Derived from ADR-0007 §1, §4–§5 and Key Interfaces:*

- Files in `src/PerformativeMail.Sim/Core/`: `Op.cs` (`[StructLayout(LayoutKind.Sequential, Pack = 4)] public struct Op`, `OpPayload` = `[InlineArray(48)] struct { byte _e0; }`), `OpKind.cs` (`enum OpKind : ushort { None = 0, PlayerJoin = 0x0001, PlayerLeave = 0x0002, FreezePlayer = 0x0003, TeleportAck = 0x0004 }` — other blocks are appended by their systems), `Reply.cs` (`Pack = 1`, `ReplyExtra` `[InlineArray(24)]`, `ReplyResult : byte { Ok = 0, Rejected = 1 }`), `CoreReason.cs` (constants), `OpDescriptor.cs`, `ReasonDef.cs`, `IOpHandler.cs` (`OpKind Kind { get; } Reply Apply(in Op op, in TickContext ctx);`).
- `PayloadAs<T>() where T : unmanaged` → `MemoryMarshal.Read<T>(payloadSpan)` (unaligned — payload sits at offset 16, only 4-byte aligned); `SetPayload<T>(in T value)` → clear the 48 bytes, `MemoryMarshal.Write`/`Unsafe.WriteUnaligned`, set `PayloadLength = (byte)SizeOf<T>()`. Get the span with `MemoryMarshal.CreateSpan(ref payload[0], 48)` — no allocation.
- `OpDescriptor`: ADR-0007's record is `(OpKind Kind, byte PayloadLength, bool SystemOnly, ushort WireCodecId, byte WireSize, ReasonDef[] Reasons)`. ADR-0007 §4 also says the descriptor declares "the unmanaged payload type" — add a `Type PayloadType` (or a generic `OpDescriptor.For<T>(…)` factory that fills `PayloadLength` from `SizeOf<T>()`) so the descriptor check can verify padding-freeness. The descriptor-table hash (protocol check, Sim Core N4) is computed by the network-adapter-loopback epic from these fields.
- Reply construction helpers: `Reply.Ok(in Op op)`, `Reply.Reject(in Op op, ushort reason)` set `ReqId` and `Kind` from the op and assert `Ok ⇔ Reason == 0`. Handlers return exactly one `Reply`; Sim Core appends it to the log (Story 020).
- Descriptor check helper lives in the test project (`tests/PerformativeMail.Sim.Tests/Fixtures/DescriptorAssert.cs` or similar) so every system epic reuses it; the padding-free check sums `Unsafe.SizeOf` of each instance field type via reflection (test-side only).
- Unmanaged payload structs carry ids as their raw `uint`/`ushort` (ADR-0004: bare integers are allowed in payload codecs).

---

## Out of Scope

- Story 014: the ring, `EnqueueOp`, stamping, refusals.
- Story 017: `DuplicateOpHandler`/`MissingOpHandler` build codes (needs `BindOp`).
- Story 020: the drain, pre-checks and `OpReply` append.
- Wire codecs, `reqId` u16 ↔ u32, descriptor-table hash: network-adapter-loopback epic.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/OpTypesTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/sim/Core/OpTypesTests.cs` (51 test methods) + shared `tests/PerformativeMail.Sim.Tests/Fixtures/DescriptorAssert.cs`, green on all 4 legs

---

## Dependencies

- Depends on: Story 001; Story 002 (`ActorId`); Story 011 (`TickContext` for `IOpHandler`)
- Unlocks: Story 014, Story 017, Story 020, every system's op descriptors

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1..AC-3 covered by `OpTypesTests.cs`; 0-B accessor guardrail test runs on Release legs, skipped on Debug by design — TD-008)
**Deviations**: Accepted — `OpKindBlock.All` is a static property backed by an internal `OpKindBlockTable` (a static `ImmutableArray` field on the struct threw `TypeLoadException` on .NET 8); `OpKindBlock.cs` added beyond the listed files to hold the per-system block ranges. Advisory (logged as TD-020): public mutable `Reply.Pad0..Pad2` (ADR: private); `Result`/`Reason` independently settable — story 020 drain must assert `Result == Ok ⇔ Reason == 0`; `OpDescriptor` is a sealed class not a record (network epic hashes fields explicitly); `DescriptorAssert` does not check `ReasonDef.Name` / `WireSize`; descriptor-enumeration test deferred to story 017/020 (`BindOp`); GDD R10 omits `bad_request` (8). `DescriptorAssert` also rejects float/`nint` fields — intended (determinism).
**Test Evidence**: Logic: `tests/unit/sim/Core/OpTypesTests.cs`, `tests/PerformativeMail.Sim.Tests/Fixtures/DescriptorAssert.cs` — 758 tests/leg green (net10.0/net8.0 × Debug/Release; Debug 5 skipped); Release build 0 warnings
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (5 advisory, after an earlier round's 7 fixes); QL-TEST-COVERAGE ADEQUATE (4 minor gaps)
**Run result**: N/A — Godot-free Sim value types; nothing renders or is observable outside the test suite
