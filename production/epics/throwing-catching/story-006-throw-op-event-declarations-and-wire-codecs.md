# Story 006: `ThrowRelease` OpKind, `ThrowLaunched` / `FlightResolved` events — declarations, reason codes and loopback wire codecs

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature (declarations in `Sim/Throwing`; codecs in `Client.Core/Net/Codec`)
> **Type**: Integration
> **Estimate**: M (~1 day; ~350 lines prod + ~350 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-29 (full `/story-readiness`, docs PR `docs/next-readiness-tc008-tc006`, main 310afef: NEEDS WORK → doc fixes applied; one batched row of Mail Lead planning defaults, **TD-196, pending Evan**; log `/workspace/repos/logs/tc006-readiness.txt`)

> **Op/host story (1 of 5).** The request and the two events that carry every flight to every peer. Declaring them once, with their codecs, lets the owner-side builder (007), the host handler (009, 010), the resolver (011) and the flight mirror (017) be written against fixed shapes. The M-1a loopback uses the same codec as a remote peer, so the codecs are not optional even though the network protocol is M0.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T4 "The host creates the `FlightRecord`" and the `ThrowRelease` fields; Network table `ThrowRelease`, `ThrowLaunched`, `FlightResolved`; "State changes come only from the owning system's own event")
**Requirement**: `TR-throwing-009` (`FlightRecord` snapshot fields; the event carries `flightId` u16), `TR-throwing-014` (message sizes — **M-1a baseline only**: the Sim payloads below; the GDD's 25/30/18/1 B wire narrowing and `TR-throwing-015` bandwidth are ADR-0021, M0, deferred), `TR-throwing-010` (the request payload half)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-N-3 (Sim-size half only), TC-N-1 (ordering half, story 010).)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§1 `ThrowRelease` = OpKind `0x0140` and its 28 B payload, reasons 64–71; §9 events) — Accepted 2026-10-16 (primary); ADR-0007 (OpKind blocks, `OpDescriptor`, reasons ≥ 64 keyed `(OpKind, Reason)`), ADR-0008 (event kinds ≤ 48 B, `Replicated`, `ResyncScope`) and ADR-0011 (codec registry keyed by `WireCodecId`, envelope `kind u16 · reqId u16`) secondary — Accepted
**ADR Decision Summary**: ADR-0015 fixes the Sim-side OpKind `0x0140`, its 28 B padding-free payload (`entryId u32, units u8, side i8, flags u8, reserved u8, feet 3×i32, dir 3×i16, pad u16`) and the two Replicated events for M-1a over loopback: `ThrowLaunched` 40 B (enough for any peer to evaluate F2 at any age) and `FlightResolved` 32 B. Wire encoding, channels and size narrowing belong to ADR-0021 (M0), which may extend the payloads by amendment. Codecs are pure `Encode`/`TryDecode` over spans in the adapter core's registry — never a codec in the Sim.
**ADR Version**: ADR-0015 2026-10-16; ADR-0007 2026-10-16; ADR-0008 2026-10-16; ADR-0011 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: Payload structs use explicit sequential layout and `byte`/`sbyte` flags only — no `bool`, no padding; reads use `MemoryMarshal.Read` guarded by an explicit length check (`bad_request`, never an exception) — ADR-0015 Engine Validation note 1. `MemoryMarshal.Read` is native-endian; the **wire** form is explicitly little-endian field by field — so the two event codecs are explicit field codecs with the range checks below, not `FixedLengthEventCodec` (a native-endian passthrough that validates nothing, `Net/Codec/FixedLengthEventCodec.cs:37-71`). Performance: codecs are pure, allocation-free span functions by contract (`IOpCodec`, `IEventCodec`); no tick-path change; 0 B checks advisory until M0 (E2).

**Control Manifest Rules (Feature layer + network seam)**:
- Required: player requests are `OpKind`s in the system's block with an `OpDescriptor`; system reason codes ≥ 64 declared in the descriptor; event kinds declare class, ≤ 48 B padding-free payload, `ResyncScope` (ADR-0007, ADR-0008)
- Required: codecs live in the adapter core's registry keyed by `WireCodecId`; decode zero-initialises the `Op`; trailing bytes or a wrong length → `bad_request` (ADR-0011 §3)
- Forbidden: a wire codec in the Sim; Variant serialisation; truncating ids (ADR-0011)
- Guardrail: `ThrowLaunched` 40 B and `FlightResolved` 32 B in the Sim (sizeof tests)

---

## Acceptance Criteria

*From ADR-0015 §1/§9 and GDD `design/gdd/throwing-catching.md` (Network, TC-N-3 Sim-size half), scoped to this story:*

- [ ] **Payload.** `ThrowReleasePayload` is exactly 28 B with the ADR-0015 field order and offsets (`Unsafe.SizeOf` + offset test); `ThrowOpKinds.ThrowRelease = (OpKind)0x0140` (a module const, as `InvOpKinds.cs:12`, not a Core `OpKind` enum member), inside the Throwing block `0x0140–0x017F`; the descriptor is `OpDescriptor.For<ThrowReleasePayload>(ThrowOpKinds.ThrowRelease, systemOnly: false, wireCodecId: 3, wireSize: 28, …)` (a player request; `wireSize` is the payload only — `RequestEnvelope` header 4 + 28 = 32 B on the wire) and declares `PayloadLength` 28 and reasons `StaleEntry 64, Dead 65, Frozen 66, Swimming 67, Unavailable 68, ThrowCooldown 69, BadDirection 70, FlightCap 71`, each named with the ADR-0015 §1 bare snake_case name (`stale_entry` … `flight_cap`; `ReasonDef(Code, Name)`, keyed `(ThrowRelease, Code)`); `throw_cooldown` and `flight_cap` stay declared though unreachable at M-1a (E5). `bad_request` is the core reason 8 (not redeclared). It is a **static declaration** (`ThrowDescriptors.ThrowRelease`, a get-only property) that nothing binds in production until story 010's `BindOp` — a declared kind without a handler fails `Freeze` (`SimWorldBuilder.cs:488-495`) — so tests bind it with a stub handler. One `DescriptorAssert.Check(d, OpKindBlock.Throwing)` covers block membership and reason validity; the wrong-`PayloadLength` → `unknown_kind` drain pre-check is Sim Core's and already tested (`OpDrainTests.cs:305-323`), not retested here.
- [ ] **Events.** `ThrowLaunched` (`flightId u16, thrower u8, flags u8 (bit0 originClamped), entryId u32, units u8, hop u8, kindDef u16, origin 3×i32, vel 3×i32, launchTick u32`) is exactly 40 B and `FlightResolved` (`flightId u16, terminal u8, rank u8, receiver u32, contact 3×i32, tick u32, unitsIn u8, unitsFumbled u8, worldItem u32`) is exactly 32 B (30 B of fields + 2 explicit zero pad). Both are module consts `ThrowEventKinds.ThrowLaunched = (EventKind)0x0140` and `FlightResolved = (EventKind)0x0141` (Mail Lead planning default, pending Evan, TD-196 item 1; not Core `EventKind` enum members — `EventLogTests.cs:421-427` requires every enum member to be bound) with descriptors class `Replicated`, `ResyncScope` 3 (TD-196 item 3), not `Coalescible`, `wireCodecId` 4 and 5 (TD-196 item 2), `wireSize` 40 and 32, ≤ 48 B, checked with `EventDescriptorAssert.Check(d, OpKindBlock.Throwing)`. The audience (all peers) is chosen per raise by the raiser (stories 010/011), not a descriptor property. `Terminal` is the ADR-0015 enum (`Caught, Fumble, MailboxAccept, MailboxMisdeliver, MailboxReject, HitEnemy, Bonk, Belt, Ground, Water, Timeout`); the two `Unsafe.SizeOf`/offset checks are the manifest guardrail, the enum members are not tested.
- [ ] **ThrowRelease codec.** Envelope `kind u16 · reqId u16` + the 28 payload bytes in declared field order, little-endian → 32 B at M-1a; maximum field values (units 255, ±2³¹ feet components, dir extremes) round-trip bit-exactly into the Sim payload; wrong-length frames (31, 33, 0 payload bytes) are swept by the shared fuzz for every registered sample (`OpCodecFuzzTests.cs:101-135`), not retested; `side` ∉ {−1, +1}, non-zero `reserved`/`pad`, or unknown `flags` bits decode to `bad_request`. Negative components survive.
- [ ] **Event codecs.** `ThrowLaunched` and `FlightResolved` round-trip every field at maximum values and with negative positions; `FlightId` travels as its raw u16 (wrap-safe, never re-ordered); decoders return failure (never throw) on truncated input (covered by the shared Theory once the rows exist, `EventCodecRoundTripTests.cs:97-115`), an out-of-range `terminal` (one row, 11) or a non-zero pad (one row). Trailing bytes are **accepted** — for an event codec they are the next record (`IEventCodec.cs:33-40`); record framing is the batch reader's and already tested.
- [ ] **Robustness.** The `ThrowRelease` codec joins `CodecTestKit.AllCodecs()` and its descriptor joins `CodecTestKit.BuildWorld` with a stub handler (`tests/unit/net/CodecTestKit.cs:19, 33-39`), so H-NET-5 (`production/epics/network-adapter-loopback/story-002-op-codec-registry-envelope-fuzz.md`; 100 000 seeded inputs, `OpCodecFuzzTests.cs:22`) and the op round-trip Theory cover it with no other test edit. The two event codecs join `EventCodecRoundTripTests.Codecs()` with a sample factory that yields a valid `terminal` and zero pad (`EventCodecRoundTripTests.cs:42-47`); one seeded fuzz loop (`Pcg32`, 10 000 inputs) over `TryMeasure`/`TryDecode` of both event codecs (no exception, no accept of a malformed record) lives in this story's codec test file.
- [ ] **Loopback end-to-end.** In a host loopback session (`production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md`) a stub system registered under `SystemId.Throwing` in a `SimProfile.ForTest` world (dependency edges are checked only on API requests, `Composition/SimSystemContext.cs:163-180`, so it builds without Players/Mail/Inventory; rig pattern `tests/integration/net/HostLoopbackSessionTests.cs:48-67, 252-272`) that raises `ThrowLaunched` then `FlightResolved` for one fake flight delivers both to the host `ClientLayer` inbox decoded and equal to the Sim payloads, in log order, in the next client tick; a client `ThrowRelease` sent through `ClientAdapter.Send` (as the rig does, `HostLoopbackSessionTests.cs:282`) arrives in the host op ring with the right actor, `kind` and 28 payload bytes.
- [ ] **Deferred sizes are named.** A comment `// M0: ADR-0021 narrows to 25/30/18 B` marks the three codecs; `TR-throwing-014/015` are already listed as deferred in EPIC.md (TR table and the M0 list, `EPIC.md:90-91, 151, 157`) — no EPIC edit; no test asserts 25/30/18 here.

---

## Implementation Notes

*Derived from ADR-0015 §1/§9, ADR-0007, ADR-0008, ADR-0011 §3 and the manual-delivery precedent (`production/epics/network-adapter-loopback/story-008-deliver-matching-wire-codec.md`):*

- **Mail Lead planning defaults, pending Evan (TD-196; readiness 2026-10-29).** Build against these; label each in code "Mail Lead planning default, pending Evan (TD-196 item n)": (1) `ThrowLaunched` = `EventKind` `0x0140`, `FlightResolved` = `0x0141`; (2) wire codec ids in one sequence across the op and event registries: `ThrowRelease` 3, `ThrowLaunched` 4, `FlightResolved` 5; (3) Throwing `ResyncScope` = 3. None contradicts an Accepted ADR; if Evan changes one, only the named id or setting moves.
- Files: `src/PerformativeMail.Sim/Throwing/Ops/ThrowOpKinds.cs`, `ThrowReleasePayload.cs`, `ThrowReason.cs`, `ThrowDescriptors.cs` (static declarations only, through `production/epics/simulation-core/story-013-op-reply-types-descriptors.md`'s descriptor API; the two event descriptors are bound in `ThrowingSystem.Bind` by whichever of 006 and 008 lands second — 008 is planned first, so 006 adds the two event-declaration lines there; story 010 binds the op with its handler), `Events/ThrowEventKinds.cs`, `Events/ThrowEvents.cs` (`ThrowLaunched`, `FlightResolved`, `Terminal`, `ThrowEventDescriptors`); `src/PerformativeMail.Client.Core/Net/Codec/ThrowingCodecs.cs` (one codec per kind under the descriptor's `WireCodecId`: op 3, events 4 and 5 — TD-196 item 2 — with `ThrowingCodecs.CreateOps()` / `CreateEvents()` for `HostSession`, `Net/HostSession.cs:85-86, 172-182`; registries of network stories 002 and 004).
- Namespace and folder rules: every new file under `Sim/Throwing/Ops/` and `Sim/Throwing/Events/` declares `namespace PerformativeMail.Sim.ThrowingCatching;` (no sub-namespace; ADR-0015 erratum 2026-10-20; `tests/unit/architecture/ThrowingRulesTests.cs:39-50`), contains no `/` or `%` in code and none of `float`, `double`, `Math`, `Random`, `Half` (`:28-30`, `:101-122`), and exposes descriptors as `static … { get; } =` properties (`:186-199`; `InvOpKinds.cs:77`).
- Event kinds are module consts in the Throwing block (`0x0140`, `0x0141`; TD-196 item 1) — event and op kinds are separate spaces, so `FlightResolved` = `0x0141` does not clash with story 022's `CatchArm` OpKind `0x0141`; `FlightResolved` is raised before the domain events it causes (ADR-0015 §5, story 011). Kind values are explicit and append-only.
- **GDD vs ADR (ADR followed):** the GDD lists `ThrowRelease` 25 B, `ThrowLaunched` 30 B, `FlightResolved` 18 B (TC-N-3). ADR-0015 §1/§9 fixes 28 B / 40 B / 32 B for the Sim and defers the narrowing to ADR-0021. The field sets differ too (GDD `seq`/`speed`/`tier`/`verdict` vs ADR `kindDef`/`vel`/`launchTick`, `design/gdd/throwing-catching.md:1083-1084`); the ADR governs, and story 017's mirror derives tier and verdict locally (EPIC.md:169). This story implements the ADR sizes; the GDD sizes are the M0 narrowing target.
- `kindDef u16` is the entry's def ordinal (`DefIndex` narrowed through the content ordinal, which fits u16 at M-1a); `receiver u32` and `worldItem u32` are raw `EntityId` values (full ids are fine on loopback; the M0 narrowing uses owner id maps, ADR-0004); `thrower u8` is the raw `PlayerSlot`.
- `flags` bit0 = `originClamped` (host launched from the newest history sample, not the claim). Reserve bits 1–7 as must-be-zero so a future amendment can append.
- Keep the `CatchArm` op declaration **out** of this story: it is catch-pull-forward scope (story 022 declares `0x0141` and its codec).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 009: the validation chain. Story 010: the handler, `ThrowLaunched` raising and reply ordering. Story 011: `FlightResolved` raising. Story 017: the client `FlightMirror` that consumes both events.
- The M0 wire narrowing (25/30/18/1 B), channel selection, `FlightBatch`, bandwidth tests TC-N-3 b (ADR-0021).
- Story 022: `CatchArm` op.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-29, full mode: GAPS, folded into the Acceptance Criteria above.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/throwing/ThrowLoopbackTests.cs` (the loopback AC) — must exist and pass
- Unit: `tests/unit/net/ThrowingCodecTests.cs` (codec semantic rejections, round-trips, the event fuzz loop), `tests/unit/throwing/ThrowDeclarationTests.cs` (namespace `PerformativeMail.Sim.Tests.Unit.ThrowingCatching`; sizeof/offset/descriptor checks); edits: `tests/unit/net/CodecTestKit.cs` (two lines), `tests/unit/net/EventCodecRoundTripTests.cs` (rows plus sample factory)
- Lean testing (`.claude/rules/lean-testing.md`): one behaviour test per AC plus the ADR/manifest-required `Unsafe.SizeOf`/descriptor checks and the H-NET-5 fuzz; no constant, enum-value or reason-list tests

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; `production/epics/simulation-core/story-013-op-reply-types-descriptors.md`; `production/epics/simulation-core/story-015-event-log-call-scopes-delivery.md` (event descriptors); `production/epics/network-adapter-loopback/story-002-op-codec-registry-envelope-fuzz.md`; `production/epics/network-adapter-loopback/story-004-hostadapter-event-sink-outbound-batches.md`; `production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md`
- Unlocks: Stories 007, 009, 010, 011, 017
