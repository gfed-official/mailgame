# Story 022: PlayerJoin and PlayerLeave System handlers

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 300 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R21 players and slots; States: "after the last player leaves, the world keeps ticking")
**Requirement**: `TR-sim-026` (`PlayerSlot` u8 0–7; slot freed in T reusable from T+1), `TR-sim-025` (fresh `player` ids, never reused)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0004: Identity and Id Spaces (§4) — primary; ADR-0007 (§3 `SystemOnly` kinds, §5 reasons 4–6) — secondary
**ADR Decision Summary**: Joins, rejoins and leaves are `System`-only ops (`PlayerJoin = 0x0001`, `PlayerLeave = 0x0002`). The `PlayerJoin` handler allocates a fresh `player` `EntityId` (rejoin keeps the same id) and assigns the requested slot in the `SlotTable`; it rejects with `slot_invalid` (≥ 8), `slot_busy` (slot occupied, or freed this tick — reassignable only from T + 1) and `duplicate_player` (already present), allocating nothing on a reject. `PlayerLeave` frees the slot at the current tick. The Sim never sees accounts or transport peers — Networking maps account → `playerId` and peer → slot.
**ADR Version**: 2026-10-16 (ADR-0004, ADR-0007)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: `SlotTable` freed in tick T reassignable only when `CurrentTick ≥ T + 1`; `PlayerJoin` rejects `slot_busy`, `slot_invalid`, `duplicate_player`, allocating nothing; System-only kinds declared `SystemOnly`; handlers validate fully before the first mutation or allocation; reasons from `CoreReason`.
- Forbidden: reading the actor from a payload; the Sim knowing accounts, peers or connections; reusing player ids.
- Guardrail: handler path 0 B.

---

## Acceptance Criteria

*From GDD SC-R21-1, scoped to this story:*

- [ ] **AC-1 SC-R21-1 (accept).** `PlayerJoin` ops (actor `System`) for two new accounts in slots 0 and 3 apply in phase 0: each gets a fresh `player` id (16 777 217, 16 777 218) in the given slot, the `SlotTable` reports both present, and each reply is Ok; the slot-assignment/player-add path is reachable only from the `PlayerJoin` handler (no other public API mutates the slot table — asserted by a reflection/visibility test).
- [ ] **AC-2 SC-R21-1 (Theory rejects).** Each row allocates nothing (`EntityIds.Peek(Player)` unchanged) and leaves the slot table unchanged: a slot ≥ 8 → `slot_invalid` (4); a slot freed by a `PlayerLeave` earlier in the **same drain** → `slot_busy` (5); a `playerId` already present → `duplicate_player` (6). A non-System actor sending `PlayerJoin` is rejected `unknown_actor` by the Core pre-check (Story 020) before the handler runs.
- [ ] **AC-3 Leave and keep ticking.** `PlayerLeave` for a present player frees its slot at the current tick (reassignable from the next tick — a `PlayerJoin` for that slot on tick T + 1 succeeds); after the last player leaves, `Tick()` keeps running with no error and `CurrentTick` keeps advancing.

---

## Implementation Notes

*Derived from ADR-0004 §4 and ADR-0007 §3–§5:*

- `src/PerformativeMail.Sim/Core/Players/PlayerJoinHandler.cs`, `PlayerLeaveHandler.cs` (or `Core/PlayerJoin.cs`) implementing `IOpHandler`; descriptors registered by Sim Core at `Freeze()` with `SystemOnly = true` and no system reasons (they use Core reasons 4–6).
- Payloads (padding-free, `unmanaged`, explicit pad bytes): `PlayerJoinPayload { uint PlayerId; byte Slot; byte Pad0, Pad1, Pad2; }` — `PlayerId == 0` means "new account, allocate"; a non-zero id is a rejoin, which is M0 (SC-R21-2, needs Frozen state from Player Character): at M-1a treat a non-zero id that is present as `duplicate_player` and a non-zero id that is absent as `bad_request` (8) until M0 (logged assumption). `PlayerLeavePayload { uint PlayerId; }`.
- Validate fully, then mutate: (1) slot < 8 else `SlotInvalid`; (2) `SlotTable.CanAssign(slot, CurrentTick)` else `SlotBusy`; (3) not already present else `DuplicatePlayer`; then `EntityIds.Allocate(Player)` (None → reject? exhaustion is unreachable in v1 — assert and reply `bad_request`), then `SlotTable.Assign`.
- **Player Character hook.** GDD SC-R21-1 says "`AddPlayer` is reachable only from that handler": Player Character (a later epic) needs to create its carrier when a player joins. Expose an internal Core seam (e.g. an `IPlayerJoinListener` bound at build by the Player Character system, called by the handler after assignment, inside the handler's call scope so its events precede the `OpReply`). At M-1a with no Player Character, a test stub binds it. Keep `SlotTable.Assign` internal to Core.
- `join_closed` (7) depends on the run phase (Shift vs Prep) owned by Run Structure — M0 (SC-R21-2); do not implement.
- `FreezePlayer` (0x0003) is Player Character's handler (GDD Interactions table) — not here.

---

## Out of Scope

- SC-R21-2 (M0): reconnect into the same slot with the same id, `join_closed`, JoinState.
- Adapter-side account/peer → slot mapping and the `System` `PlayerJoin` enqueue after `HelloAck`: network-adapter-loopback (M-1a: one local slot).
- Player carrier creation: player-character epic (binds the join listener).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/PlayerJoinTests.cs` (`[Trait("Id", "SC-R21-1")]`) — must exist and pass

**Status**: [x] Created — tests/unit/sim/Core/PlayerJoinTests.cs (14 methods / 17 cases, SC-R21-1, + Release-only PlayerJoinAllocationTests)

---

## Dependencies

- Depends on: Story 018 (allocators, `SlotTable`), Story 020 (drain, pre-checks, reply append)
- Unlocks: network-adapter-loopback (local player join over loopback), player-character, Story 027

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 3/3 passing (all covered by unit tests; no deferred items)
**Deviations**: Advisory only — (1) Payloads `PlayerJoinPayload` (8 B: PlayerId u32, Slot u8, Pad0–2) and `PlayerLeavePayload` (4 B) + `CorePlayerOps.JoinDescriptor/LeaveDescriptor` (SystemOnly, Core reasons only; codec ids 1/2 and wire sizes 5/4 are placeholders for network-adapter 002) in Core/PlayerJoinPayload.cs. (2) Handlers are auto-registered at `Freeze()` only when `BindCoreState` supplied EntityIds + SlotTable (else the kinds are unknown_kind); kinds already declared by the composition root are skipped. (3) Internal `IPlayerJoinListener { OnPlayerJoined(slot, player); OnPlayerLeft(slot, player) }` bound via internal `SimWorldBuilder.BindPlayerJoinListener` (second bind → `SimBuildErrorCode.PlayerJoinListenerAlreadyBound = 28`); called inside the call scope after the mutation. The leave callback was added for player-character 001. (4) Leave of an absent player or PlayerId 0 → bad_request; join with non-zero PlayerId: present → duplicate_player, absent → bad_request until M0; id exhaustion → bad_request. (5) Leave order: free the slot at ctx.Tick, then `SampleLatch.ResetSender(slot)` (folded TD-049 item 1), then the listener. New `SimAssertCode.PlayerSlotMutationFailed = 50`. Gate advisories logged as TD-052.
**Test Evidence**: Logic — tests/unit/sim/Core/PlayerJoinTests.cs; four legs (net10/net8 × Debug/Release) green on the real tree, 1 909 tests/leg (Debug 15 skipped); Release net8 re-run by the orchestrator: 1 909, 0 failed.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (4 advisories), QL-TEST-COVERAGE ADEQUATE (6 advisories).
