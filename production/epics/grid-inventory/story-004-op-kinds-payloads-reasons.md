# Story 004: Inventory op kinds, padding-free payloads, reason codes and the 24 B reply extra

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-025` (sender from transport peer; malformed → `bad_request`, no exception), `TR-inventory-006` (reason-code set)
*(No `docs/architecture/tr-registry.yaml` exists yet; requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A, mirrored in the epic's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0007: Op Queue, Drain and Replies; ADR-0011: Network Adapter Boundary
**ADR Decision Summary**: Each GDD op is its own `OpKind` in `0x0080–0x00FF` with an explicit-layout payload ≤ 32 B, padding-free (reorder or explicit zeroed pad fields counted in `PayloadLength`); `reqId` is in the `Op` header; `bad_request` is Core reason 8; inventory reasons are 64–85, never reused; `InvReplyExtra` is exactly 24 B with explicit offsets. The actor comes from `Op.Actor` stamped by the adapter, never from a payload field (ADR-0009 §1–§2, §4; ADR-0007; ADR-0011 §3).
**ADR Version**: ADR-0009 2026-10-16; ADR-0007 2026-10-16; ADR-0011 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: each GDD op is its own `OpKind` in `0x0080–0x00FF` with ADR-0009 §1 payloads (≤ 32 B, padding-free); containers on the wire are `containerId` u32 (ADR-0009 §1)
- Required: inventory reason codes `bad_request` = CoreReason 8; own codes 64–85 (`not_open 64 … capacity_exhausted 85`), never reused (ADR-0009 §2)
- Required: `InvReplyExtra` exactly 24 B with explicit offsets (ADR-0009 §4)
- Forbidden: payload fields for identity; `dependsOn` on the wire (ADR-0011; ADR-0009 §1)
- Required (Foundation, ADR-0007): 64 B `Op`, ≤ 48 B payload; malformed → `bad_request`, never an exception

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] Op kinds declared for the M-1a op set with ADR-0009 §1 payloads: `Pick` (8 B), `Move` (22 B + explicit pad), `Merge` (16), `QuickMove` (13 + pad), `Return` (0), `Drop` (4), `Pickup` (8 + claimed position), `Open`/`Close` (4 + claimed position for `Open`), `DeliverMatching` (layout per ADR-0009 §1); each descriptor test asserts Σ field sizes == `Unsafe.SizeOf<T>()` == declared `PayloadLength`, and kind values are unique and inside `0x0080–0x00FF`. (`MoveGroup`, `QuickMoveGroup`, `Split`, `TakeAll`, `Sort` values are reserved in the table but not registered until M1.)
- [ ] `InvReason` enum values equal ADR-0009 §2 exactly (`NotOpen = 64` … `CapacityExhausted = 85`); a snapshot test fails on any change or reuse; `bad_request` uses Core's code 8.
- [ ] `InvReplyExtra` is exactly 24 B at offsets 0,4,8,12,14,16,18,20,22,23 (sizeof + `FieldOffset` test); unit counts saturate at 65 535.
- [ ] **H-NET-5 (Sim half)** — GIVEN a recorded seed and 10 000 structurally valid ops with out-of-range fields (count < 0 or > 255, coordinates outside int16, ids at/above an allocator's high-water outside the actor's temp namespace, `rotated` on a slots layout) applied through the phase-0 drain, THEN each yields `bad_request` (or a valid op), state is unchanged, and no exception escapes the Sim. (Byte-level decoding is the adapter codec's — `network-adapter-loopback` epic.)
- [ ] **H-R7-4 (malformed half)** — `move {count: −1}` (as decoded), `move {x: 70000}` and a `move` naming an entryId at or above the high-water → `bad_request`; a `move` naming an already-removed entryId → `stale_entry`; nothing changes.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Ops/` — `InvOpKinds.cs`, one payload struct per kind (`[StructLayout(LayoutKind.Sequential, Pack = 1)]` or explicit, read/written unaligned only), `InvReason.cs`, `InvReplyExtra.cs`.
- Register descriptors through Sim Core's descriptor API (`production/epics/simulation-core/story-013-op-reply-types-descriptors.md`); reasons are declared per descriptor keyed `(OpKind, Reason)`.
- `Move`'s 22 B: reorder fields or add explicit zeroed pad bytes and count them (ADR-0009 Implementation Guidelines).
- The throw op is Throwing's `ThrowRelease` (ADR-0021, M0) — not declared here; inventory exposes `DetachForThrow` (story 015).
- `bad_request` judgments that need Sim state (high-water, temp namespace, `rotated` on slots) live in each handler's `Validate` first step; a shared helper keeps the chain identical across kinds.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Stories 007–010, 019, 020, 022: the handlers using these kinds.
- network-adapter-loopback epic: wire codecs (`WireCodecId`), byte-level decode.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/InvOpKindTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; `production/epics/simulation-core/story-013-op-reply-types-descriptors.md`; `production/epics/simulation-core/story-020-phase0-drain-prechecks-replies.md`
- Unlocks: Story 007, Story 008, Story 010, Story 011
