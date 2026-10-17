# Story 002: Op wire codec registry, request envelope and the H-NET-5 fuzz test

> **Epic**: Network Adapter Core — Loopback (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (≈ 1 day; ≈ 450 lines incl. fuzz and round-trip tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` §Network (N4 wire widths, descriptor-table hash as protocol check); R6 `reqId` opaque
**Requirement**: `TR-inventory-025` (malformed → `bad_request`, no exception; sender never from payload), `TR-sim-042` (no codec in the Sim), `TR-delivery-011` (u16 `reqId` on the wire — width half)
*(Requirement text: `docs/architecture/tr-registry.yaml` — TR-inventory-025, TR-sim-042, TR-delivery-011 present)*

**ADR Governing Implementation**: ADR-0011: Network Adapter Boundary (§3 codec per descriptor, envelope) — primary; ADR-0007: Op Queue, Drain and Replies (§1 zeroing/`PayloadLength`, §4 `WireCodecId`/`WireSize`, §6 u16 ↔ u32 `reqId`) — secondary
**ADR Decision Summary**: Each Sim `OpDescriptor` declares only a wire codec id and wire size; codec implementations (pure `TryDecode`/`Encode` over spans) live in the adapter core's registry keyed by that id — the Sim never encodes or decodes. Reliable request envelope: `kind u16 · reqId u16 · payload`. Decoding zero-initialises the whole `Op`, sets `PayloadLength` from the descriptor and zero-extends the u16 `reqId` into the u32 header slot; encoding of the echoed value truncates back to u16. Decode failure, an unknown kind or trailing bytes → the adapter answers `bad_request` (8) itself and enqueues nothing. A hash of the descriptor table is the protocol-version check. A fuzz test asserts every input yields `bad_request` or a valid `Op`, never an exception.
**ADR Version**: 2026-10-16 (ADR-0011, ADR-0007)

**Engine**: Godot 4.7.1 .NET (plain C#) | **Risk**: LOW
**Engine Notes**: Untrusted bytes are decoded only by these codecs — never `GD.BytesToVar` or any Variant deserialiser (ADR-0011 §5 item 5; relevant to the M0 transport, enforced here by design).

**Control Manifest Rules (Core — network seam)**:
- Required: codecs in the adapter core's registry keyed by `WireCodecId`, pure `TryDecode`/`Encode` over spans; decode zero-initialises the `Op`, sets `PayloadLength`, zero-extends the u16 `reqId`; envelope `kind u16 · reqId u16 · payload`; decode failure/unknown kind/trailing bytes → `bad_request` (8), nothing enqueued; H-NET-5 fuzz test.
- Forbidden: a wire codec in the Sim; Variant deserialisation of network input; payload fields used for identity; a u32 `reqId` on the wire.
- Guardrail: network envelope 4 B per request; decode/encode allocate 0 B.

---

## Acceptance Criteria

*Derived from ADR-0011 §3 and the epic DoD ("every M-1a `OpKind` round-trips encode → decode bit-exactly, one Theory row per descriptor"), scoped to this story:*

- [x] **AC-1 Envelope and decode.** Encoding `(kind, reqId 0xBEEF, payload)` produces `kind` LE u16, `0xEF 0xBE`, then the codec's payload bytes; decoding it yields an `Op` whose 64 bytes are zero except `Kind`, `PayloadLength` (= descriptor length), `ReqId == 0x0000BEEF` and the payload bytes (`OpSeq`, `Actor`, `Reserved` all 0); an echoed reply `ReqId` of `0x0000BEEF` encodes back to the u16 `0xBEEF`.
- [x] **AC-2 Round-trip Theory.** A reusable Theory enumerates every registered op descriptor that has a codec and asserts encode → decode → encode is bit-exact and that the decoded `Op` equals one built with `SetPayload` from the same payload; this story registers codecs for Sim Core's `PlayerJoin` and `PlayerLeave` (rows present), and feature epics add rows by registering codecs (no test edit needed).
- [x] **AC-3 H-NET-5 fuzz.** 100 000 seeded random byte strings (lengths 0–80) plus structurally valid but out-of-range payloads (e.g. `PlayerJoin` slot 200) each yield either `DecodeResult.BadRequest` or a valid `Op`; no input throws; an unknown kind, a too-short payload and trailing bytes each yield `BadRequest`.
- [x] **AC-4 Protocol hash.** `DescriptorTableHash` over the frozen world's op and event descriptors (kind, payload length, `SystemOnly`, class, codec id, wire size — in ascending kind order) is stable across two builds of the same profile and changes when any descriptor field changes.

---

## Implementation Notes

*Derived from ADR-0011 §3 and ADR-0007 §1, §4, §6:*

- Files in `src/PerformativeMail.Client.Core/Net/Codec/`: `IOpCodec.cs` (`bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength)` / `int Encode(in Op src, Span<byte> dst)` — ADR-0011's sketch is `TryDecode(ReadOnlySpan<byte>, ref OpPayload)`; returning the length or reading it from the descriptor is an implementation detail), `OpCodecRegistry.cs` (keyed by `WireCodecId`, built once at session start from the world's descriptors — array lookup, no dictionary iteration), `RequestEnvelope.cs`, `DecodeResult.cs`, `DescriptorTableHash.cs`, `SimCoreCodecs.cs` (`PlayerJoin`/`PlayerLeave`).
- The Sim's frozen descriptor table must be readable by the adapter (a read-only enumeration exposed by `SimWorld` after `Freeze()` — add it in this story if simulation-core did not).
- Range checks that belong to the wire (e.g. a slot ≥ 8 is out of range for the wire u8 field) may be done by the codec → `bad_request`; semantic checks stay in the handler (ADR-0011 GDD-impact table: "checks stay in the handler").
- `DescriptorTableHash`: use the content-data epic's hand-written SHA-256 if it exists (manifest I-5 forbids `System.Security.Cryptography` in Sim/Client.Core); otherwise fold the canonical bytes with `Mix64` (ADR-0005 primitive) into a 64-bit hash and note that the Hello/HelloAck handshake (M0) will move it to SHA-256. Record the choice.
- PlayerJoin wire payload mirrors simulation-core Story 022's `PlayerJoinPayload` (`playerId` u32, `slot` u8) — those ops are `SystemOnly` and are enqueued by `HostAdapter` itself; the codec exists for the replay/recording path and the round-trip Theory, and because the Sim's descriptor names a codec id.
- Fuzz with `Pcg32` (ADR-0005) seeded — no `System.Random` (Client.Core is under the same analyzer; tests may use it but keep runs reproducible).

---

## Out of Scope

- Story 003: answering `bad_request`/`busy` on the peer's reply stream and enqueueing.
- Event/batch codecs: Story 004.
- Feature op codecs (inventory ops, `deliver_matching`, `throw`): registered by their epics; `deliver_matching` is Story 008 (Blocked).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/net/OpCodecTests.cs`, `tests/unit/net/OpCodecFuzzTests.cs` (`[Trait("Id", "H-NET-5")]`), `tests/unit/net/OpCodecRoundTripTests.cs` — must exist and pass

**Status**: [x] Created — OpCodecTests, OpCodecFuzzTests (H-NET-5), OpCodecRoundTripTests (+ CodecTestKit)

---

## Dependencies

- Depends on: Story 001; simulation-core Story 013 (`Op`, descriptors), Story 017 (frozen descriptor table), Story 022 (`PlayerJoin`/`PlayerLeave` payloads)
- Unlocks: Story 003, Story 004, Story 008, every feature epic's codec registration

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 4/4 passing. No deferred items.
- AC-1 is covered by OpCodecTests: Encode_LeaveWithReqIdBeef_* (exact bytes), TryDecode_ValidJoin_ZeroesEverythingExcept* (all 64 bytes), Encode_EchoedReplyReqId_TruncatesToU16 and Encode_DoesNotSendOpSeqOrActor.
- AC-2 is covered by OpCodecRoundTripTests.EncodeDecodeEncode_IsBitExact_*, whose rows come from registry.Bindings × codec samples × the reqIds 0, 1, 0xBEEF and 0xFFFF, plus Rows_CoverEveryWireReachableDescriptor.
- AC-3 is covered by OpCodecFuzzTests [Trait Id H-NET-5]:
  - 100k Pcg32 inputs of length 0–80, plus 100k targeted payloads;
  - a slot 0–255 sweep and all 65,536 kind values;
  - every truncation and every extension of each valid sample;
  - OpCodecTests.TryDecode_Malformed_* for an unknown kind, a too-short payload, trailing bytes and slot 200.
- AC-4 is covered by the DescriptorTableHash tests:
  - two builds of the same world hash equal;
  - a pinned value for the Sim Core descriptors;
  - every op and event descriptor field changes the hash;
  - adding an op or event changes it;
  - the op table and the event table are not interchangeable.

**Deviations**: Advisory only. All of them are logged as TD-056.
- `DescriptorTableHash` folds with `Mix64` (64 bits) instead of SHA-256 because no managed SHA-256 exists yet. Recorded in the 2026-10-16 dev-story decision.
- `IOpCodec` carries `SampleCount`/`GetSample`, so test samples sit on the production interface.

**Out of scope (valid)**: `src/PerformativeMail.Sim/Core/SimWorld.Descriptors.cs` (read-only `OpDescriptors`/`EventDescriptors`) plus one line in `SimWorld.cs`. The story's Implementation Notes permit both.

**Test Evidence**: Logic. Test files are tests/unit/net/OpCodecTests.cs, OpCodecFuzzTests.cs (H-NET-5), OpCodecRoundTripTests.cs and CodecTestKit.cs.
- Unit.Net passed 129/129 on all four legs (net10.0/net8.0 × Debug/Release) when the review fixes went in. The full suite was 2,101 tests on net10 Debug with 0 failed.
- Re-checked at close on an isolated copy at net10 Debug: 2,117 tests, 0 Net failures. 21 tests failed, all of them other runners' in-flight work: world-stub WorldLotsTests (19) and Hosting FrameRunnerTests (2). The real tree did not build at close because of another runner's in-flight `PostOfficeRecord.cs`, which trips RS0030 (GetHashCode), so the copy suppressed that rule in that one file.

**Code Review**: Complete.
- godot-csharp-specialist: APPROVE.
- LP-CODE-REVIEW: CONCERNS, advisory. Fixed: TryGet nullability, the WireSize ≤ 48 and sample-length construction checks, and 2 new tests. The rest is in TD-056.
- QL-TEST-COVERAGE: ADEQUATE, with three advisory gaps that are in TD-056.
