# Story 001: Client.Core assembly, transport abstractions and LoopbackTransport

> **Epic**: Network Adapter Core — Loopback (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 350 lines incl. csproj, tests and CI edits)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` §Network (N1 boundary, N2 roles on the wire); R33 Godot-free
**Requirement**: `TR-sim-042` (the Sim knows no peers/channels/RPC/Godot types — the adapter lives outside it), `TR-sim-014` (host loopback uses the same path as remote), `TR-sim-008` (transport callbacks only enqueue)
*(Requirement text: `docs/architecture/tr-registry.yaml` — TR-sim-042 "Sim knows no peers/accounts/channels/RPC/wall clock/RTT/Godot types", TR-sim-014 "host loopback uses same encoder", TR-sim-008 "Transport callbacks only enqueue; nothing applied inside a callback")*

**ADR Governing Implementation**: ADR-0011: Network Adapter Boundary (§2 adapter core, `ITransport`, loopback) — primary; ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework (§1 `Client.Core` assembly, §6 analyzers, §8 guards) — secondary
**ADR Decision Summary**: The adapter core is Godot-free plain C# in `PerformativeMail.Client.Core/Net/` (no new assembly beyond Client.Core, which this epic creates: `net8.0`, references only the Sim, no runtime packages). The only transport-specific piece is `ITransport { Poll(ITransportSink); Send(peer, channel, bytes); Disconnect(peer, reason); }` with logical `NetChannel` (Unreliable 0, Reliable 1, Bulk 2, Comms 3); callbacks only enqueue and `Poll` delivers on the main thread. `LoopbackTransport` is an in-process pair of byte queues used for solo and the host's own client — the same path as a remote peer.
**ADR Version**: 2026-10-16 (ADR-0011), 2026-10-16 (ADR-0001)

**Engine**: Godot 4.7.1 .NET (plain C#; no Godot API) | **Risk**: LOW
**Engine Notes**: ADR-0011's domain risk is HIGH (Godot 4.4–4.7 multiplayer is NOT SOURCEABLE), but nothing in this story touches it; the real transport (`src/Game/Net/`) is the M0 spike + transport ADR. The `LoopbackTransport` makes no `MultiplayerAPI` assumption.

**Control Manifest Rules (Core — network seam; Foundation — assemblies)**:
- Required: Client.Core at `src/PerformativeMail.Client.Core/`, `net8.0`, references only the Sim, created by the first network-seam story; analyzers with `PrivateAssets="all"` and `BannedSymbols.txt` in the project folder; `ITransport` / `ITransportSink` / `NetChannel` per ADR-0011 §2; `LoopbackTransport` in Client.Core; transport callbacks only enqueue, delivery happens in `Poll` on the main thread.
- Forbidden: any `Godot*` reference, `using Godot` or runtime NuGet package in Client.Core; the Sim referencing Client.Core; threads/timers/static event handlers in Client.Core; committing to a real transport before the M0 spike.
- Guardrail: loopback `Send`/`Poll` allocate 0 B after warm-up (preallocated byte queues).

---

## Acceptance Criteria

*Derived from ADR-0011 §2 and ADR-0001 §1/§8 (the epic DoD's "architecture test asserts Client.Core has no Godot reference"), scoped to this story:*

- [x] **AC-1 Assembly and guards.** `src/PerformativeMail.Client.Core/PerformativeMail.Client.Core.csproj` (`net8.0`) is in `PerformativeMail.sln`, references only `PerformativeMail.Sim`, is referenced by the test project, and receives the same `SIM_ASSERT`/checked mapping (Directory.Build.targets) and analyzers (BannedApiAnalyzers + the custom float analyzer) as the Sim; `AssemblyRulesTests` additionally asserts Client.Core references no `Godot*` assembly and no runtime package, and that the Sim does not reference Client.Core; the CI `using Godot` grep now covers `src/PerformativeMail.Client.Core`.
- [x] **AC-2 Loopback semantics.** `LoopbackTransport.CreatePair()` returns two ends; bytes sent from one end on a channel are delivered by the other end's `Poll(sink)` — never earlier — in send order per channel, as copies (mutating the sender's buffer after `Send` does not change the received bytes); `OnConnected` is delivered once per end on the first `Poll`; `Disconnect` delivers `OnDisconnected` to the other end at its next `Poll` and drops later sends.
- [x] **AC-3 Allocation.** After warm-up, 10 000 send/poll cycles of messages ≤ the configured slot size allocate 0 B (the sink receives a `ReadOnlySpan<byte>` over preallocated storage); a message larger than the configured maximum is refused with a counted failure (never grows at runtime).

---

## Implementation Notes

*Derived from ADR-0011 §2, §5 and ADR-0001 §1, §6, §8:*

- Files in `src/PerformativeMail.Client.Core/Net/` (namespace `PerformativeMail.Client.Core.Net`): `ITransport.cs`, `ITransportSink.cs`, `NetChannel.cs` (`Unreliable = 0, Reliable = 1, Bulk = 2, Comms = 3` — logical; the mapping to real channels is the transport ADR's), `PeerHandle.cs` (readonly struct, explicit equality like ADR-0004 ids — makes no assumption about Godot peer-id ranges), `DisconnectReason.cs`, `LoopbackTransport.cs`.
- `LoopbackTransport`: two ends sharing two preallocated byte rings (one per direction) with per-message headers (channel, length); `Send` copies into the ring; `Poll(ITransportSink sink)` drains the inbound ring and calls `sink.OnMessage(peer, channel, span)`. Same-channel order is preserved; with only Reliable in use at M-1a, cross-channel order is send order too (it is a single ring), which satisfies ADR-0011 §5 item 1 trivially.
- csproj: mirror the Sim's (`Nullable`, `TreatWarningsAsErrors`, analyzers `PrivateAssets="all"`, its own `BannedSymbols.txt` — copy the Sim's list), `InternalsVisibleTo` the test project. Update `Directory.Build.targets` only if needed (ADR-0001 §4's XML already names `PerformativeMail.Client.Core`).
- Test project: add a `ProjectReference` to Client.Core; tests go in `tests/unit/net/` (namespace `PerformativeMail.Sim.Tests.Unit.Net`).
- No `async`, no threads, no events (`event` keyword) — the sink interface is called synchronously from `Poll`.

---

## Out of Scope

- Story 002: codecs and envelopes; Story 003+: `HostAdapter`/`ClientAdapter`.
- Real transports (SceneMultiplayer/ENet/Steam), channels beyond logical ids, fragmentation, pacing — M0.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/net/LoopbackTransportTests.cs`, extended `tests/unit/architecture/AssemblyRulesTests.cs` — must exist and pass (net10.0 + net8.0)

**Status**: [x] Created — tests/unit/net/LoopbackTransportTests.cs, tests/unit/net/NetTypesTests.cs, tests/unit/architecture/AssemblyRulesTests.cs (all pass, 4 legs)

---

## Dependencies

- Depends on: simulation-core Story 001 (build/CI scaffold) and Story 004 (analyzers to copy)
- Unlocks: Story 002, Story 003, Story 006

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 AssemblyRulesTests ClientCore_* + Sim_References_NotClientCore + CI guard at .github/workflows/tests.yml:47-63; AC-2 LoopbackTransportTests Send_*/Poll_*/Disconnect_*; AC-3 SendPoll_WarmedUp_TenThousandCyclesAllocateNothing + oversize/ring-full tests). No deferred items.
**Deviations**: ADVISORY only:
- ADR-0011 sketch drift (OnDisconnected `reason` param, CreatePair tuple names First/Second), logged as TD-012.
- Out-of-scope files, all valid support for the story: tests/PerformativeMail.Sim.Tests/NonParallelCollection.cs (isolates the allocation test); docs/tech-debt-register.md (TD-002 closed by the AssemblyRulesTests rewrite).
**Test Evidence**: Logic. Test files are tests/unit/net/LoopbackTransportTests.cs, tests/unit/net/NetTypesTests.cs and tests/unit/architecture/AssemblyRulesTests.cs. 587 tests per leg (the total includes concurrent simulation-core work), 0 failed, on net10.0/net8.0 × Debug/Release. The Debug legs skip 2 tests, both unrelated to this story: TD-008 TrigQ15 and the sim-core TickDriver alloc test.
**Code Review**: Complete.
- godot-csharp-specialist: APPROVED. Applied A2, the overflow-safe capacity guard in the LoopbackTransport constructor.
- LP-CODE-REVIEW: CONCERNS, resolved. MessageRing.TryWrite narrowed the length header with bare `(byte)` casts, which threw OverflowException in checked (SIM_ASSERT) builds for any payload over 255 B. The fix masks each byte. The regression test is Send_DefaultPairLengthAbove255_DeliveredIntactWithoutThrowing (255/256/300/65535/65536); it was watched failing on the unfixed code, then passing.
- QL-TEST-COVERAGE: GAPS (advisory), logged as TD-013 and TD-015.
**Tech debt**: TD-002 closed. Logged TD-012 to TD-016. TD-016 records forward constraints:
- The HostAdapter must not assume PeerHandle values are unique across transports.
- The IHostClock estimator must be integer-only under PMSIM001.
