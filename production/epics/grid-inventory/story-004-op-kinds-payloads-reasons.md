# Story 004: Inventory op kinds, padding-free payloads, reason codes and the 24 B reply extra

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-20

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

- [x] Op kinds declared for the M-1a op set with ADR-0009 §1 payloads: `Pick` (8 B), `Move` (24: 22 B of fields + 2 zeroed pad), `Merge` (16), `QuickMove` (12: 9 B of fields + 3 zeroed pad), `Return` (0), `Drop` (4), `Pickup` (20: 8 + 12 B claimed position), `Open` (16: 4 + 12 B claimed position), `Close` (4) — explicit zeroed pad fields are counted in `PayloadLength` (sizes per the Implementation Notes table); each descriptor test asserts Σ field sizes == `Unsafe.SizeOf<T>()` == declared `PayloadLength` (exemption: `Return` is payload-free — declared through `OpDescriptor.Empty`, `PayloadLength` 0 and no payload type, because `Unsafe.SizeOf` of an empty struct is 1; its test asserts exactly that), and kind values are unique and inside `0x0080–0x00FF`. Every block-acquiring kind (`Move`, `Merge`, `QuickMove`, `Pickup`, `Return`) declares `(OpKind, 85) capacity_exhausted` on its descriptor (the validator mapping `CheckCode.InventoryArenaExhausted → 85` is story 005's). *(`DeliverMatching` is not declared here: the ADR-0009 erratum of 2026-10-16 moved it to Delivery's `0x0100–0x013F` block — manual-delivery story 005. Reasons 83/84 stay declared in `InvReason` so 64–85 is exact.)* (`MoveGroup`, `QuickMoveGroup`, `Split`, `TakeAll`, `Sort` values are reserved in the table but not registered until M1.)
- [x] `InvReason` enum values equal ADR-0009 §2 exactly (`NotOpen = 64` … `CapacityExhausted = 85`); a snapshot test fails on any change or reuse; `bad_request` uses Core's code 8.
- [x] `InvReplyExtra` is exactly 24 B at offsets 0,4,8,12,14,16,18,20,22,23 (sizeof + `FieldOffset` test); unit counts saturate at 65 535.
- [x] **H-NET-5 (Sim half)** — *(no real handler exists until stories 007–010: verified through a TEST-ONLY stub handler in the test project that calls the shared production validation helper — high-water / temp namespace / `rotated` on slots / ranges, `stale_entry` for removed ids. The fuzz covers the Sim-judgeable classes: container id 0 / at or above the container high-water; entry id 0, at/above the entry high-water, `MaxRealId`, another slot's temp id (or any temp id from System), reserved temp bits set; `rotated` on a slots layout or > 1; a non-zero pad byte; `Pickup.WorldItemId` 0. `count` < 0 or > 255 and coordinates outside int16 are unrepresentable in the narrowed `u8`/`i16` payload fields — the generator fills them with in-range extremes — so that half is the codec decode-boundary checks `InvRequestCheck.CheckCount` / `CheckCoordinate`, tested directly under H-R7-4; codec wiring tracked by TD-105 (1). Tests: `[Trait("Id", "H-NET-5-sim")]` — `H-NET-5` itself is the adapter codec fuzz in `tests/unit/net/OpCodecFuzzTests.cs`)* GIVEN a recorded seed and 10 000 structurally valid ops with out-of-range fields (count < 0 or > 255, coordinates outside int16, ids at/above an allocator's high-water outside the actor's temp namespace, `rotated` on a slots layout) applied through the phase-0 drain, THEN each yields `bad_request` (or a valid op), state is unchanged, and no exception escapes the Sim. (Byte-level decoding is the adapter codec's — `network-adapter-loopback` epic.)
- [x] **H-R7-4 (malformed half)** — *(through the same TEST-ONLY stub `Move` handler and shared validation helper; `count −1` and `x 70000` are not representable in the narrowed `u8`/`i16` payload fields, so those two are rejected at the helper's widened-`int` decode-boundary entry points `CheckCount`/`CheckCoordinate`, which the adapter codec calls — network-adapter-loopback epic. Tests: `[Trait("Id", "H-R7-4")]`)* `move {count: −1}` (as decoded), `move {x: 70000}` and a `move` naming an entryId at or above the high-water → `bad_request`; a `move` naming an already-removed entryId → `stale_entry`; nothing changes.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Ops/` — `InvOpKinds.cs`, one payload struct per kind (`[StructLayout(LayoutKind.Sequential, Pack = 1)]` or explicit, read/written unaligned only), `InvReason.cs`, `InvReplyExtra.cs`.
- Register descriptors through Sim Core's descriptor API (`production/epics/simulation-core/story-013-op-reply-types-descriptors.md`); reasons are declared per descriptor keyed `(OpKind, Reason)`.
- `Move`'s 22 B: reorder fields or add explicit zeroed pad bytes and count them (ADR-0009 Implementation Guidelines).
- **Pad rule (readiness 2026-10-20, Mail Lead):** `[StructLayout(LayoutKind.Sequential, Pack = 1)]` plus **explicit zeroed pad fields counted in `PayloadLength`**, padding each payload to a multiple of 4 B (the alignment of its widest field) — the convention of Sim Core's `PlayerJoinPayload` (5 B of data + `Pad0..Pad2` = 8) and ADR-0009's Implementation Guidelines; the `Op` payload sits at a 4-byte-aligned offset and is read/written unaligned only. A non-zero pad byte is `bad_request`. Final sizes (`PayloadLength`):

  | Kind | Fields | PayloadLength |
  |---|---|---|
  | `Pick` | from u32, entryId u32 | 8 |
  | `Move` | 22 B of ADR fields (u32s first, then x i16, y i16, rotated u8, count u8) + 2 pad | 24 |
  | `Merge` | from, entryId, to, targetEntryId u32 | 16 |
  | `QuickMove` | from u32, entryId u32, count u8 (9 B — ADR-0009 §1's "(13)" is a sum slip; the listed fields total 9) + 3 pad | 12 |
  | `Return` | — (payload-free, `OpDescriptor.Empty`) | 0 |
  | `Drop` | entryId u32 | 4 |
  | `Pickup` | worldItemId u32, entryId u32, claimed position (x, y, z i32) 12 | 20 |
  | `Open` | container u32, claimed position 12 | 16 |
  | `Close` | container u32 | 4 |
- The throw op is Throwing's `ThrowRelease` (ADR-0015 §1, M-1a) — not declared here; inventory exposes `DetachForThrow` (story 015).
- `bad_request` judgments that need Sim state (high-water, temp namespace, `rotated` on slots) live in each handler's `Validate` first step; a shared helper keeps the chain identical across kinds.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Stories 007–010, 019, 020: the handlers using these kinds.
- manual-delivery story 005: `DeliverMatching` (Delivery `OpKind`, block `0x0100–0x013F`, per the ADR-0009 erratum of 2026-10-16).
- grid-inventory story 005: mapping `CheckCode.InventoryArenaExhausted` → reason 85 in validation.
- network-adapter-loopback epic: wire codecs (`WireCodecId`), byte-level decode.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/InvOpKindTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/inventory/InvOpKindTests.cs` + `tests/unit/inventory/InvMalformedOpTests.cs` (109 tests; 104 passing on net10.0/net8.0 × Debug/Release at completion, 109 passing on net10.0 Debug after the 2026-10-20 Mail Review fixes; traits `H-NET-5-sim`, `H-R7-4`)

---

## Dependencies

- Depends on: Story 001; Story 002; `production/epics/simulation-core/story-013-op-reply-types-descriptors.md`; `production/epics/simulation-core/story-020-phase0-drain-prechecks-replies.md`
- Unlocks: Story 007, Story 008, Story 010, Story 011

---

## Completion Notes
**Completed**: 2026-10-20
**Criteria**: 5/5 passing (all covered by automated tests; full matrix net10.0/net8.0 × Debug/Release 4,742 tests, 0 failed, 0 warnings; CI guards pass)
**Deviations** (advisory):
- `QuickMove` is 12 B (9 B of fields + 3 pad): ADR-0009 §1's "(13)" is a sum slip — `from u32 + entryId u32 + count u8` = 9 B.
- H-NET-5 (Sim half) carve-out, same as H-R7-4: the 10 000-op fuzz cannot produce `count` < 0 or > 255 or coordinates outside int16 (unrepresentable in the narrowed `u8`/`i16` fields; filled with in-range extremes, the fault always comes from another field — bad container, bad/foreign entry id, `rotated` on slots or > 1, non-zero pad). It covers the Sim-judgeable classes only; count/coordinate range is the decode-boundary `CheckCount` / `CheckCoordinate` (tested directly; codec wiring TD-105 (1)).
- H-R7-4 `count −1` / `x 70000` are unrepresentable in the narrowed `u8`/`i16` payload fields; they are rejected at the widened decode-boundary statics `InvRequestCheck.CheckCount` / `CheckCoordinate` (to be called by the adapter codec — TD-105 (1)). The stub-handler path covers high-water, removed-id (`stale_entry`), temp-namespace, `rotated`-on-slots and pad cases.
- Inventory `OpKind` values live in `InvOpKinds` (module-owned constants), not Sim Core's `OpKind` enum; descriptors are a static table (`InvOpDescriptors`), not bound by `InventorySystem` until handlers exist (stories 007–010; `Freeze` would throw `MissingOpHandler`).
- The shared helper is two steps — `CheckRequest` (bad_request) then, after the handler's `not_open` / P13 step, `ResolveEntries` (stale_entry) — so the ADR-0009 §2 chain order holds.
- `Pickup.WorldItemId` is judged only for 0 until the WorldItem table exists; `ClaimedPosition` is inlined as three i32 (Inventory must not depend on Players, per `SystemGraph`).
- Carry-forwards logged as TD-105.
**Test Evidence**: Logic: `tests/unit/inventory/InvOpKindTests.cs`, `tests/unit/inventory/InvMalformedOpTests.cs` (TEST-ONLY stub handler + rig in the latter)
**Code Review**: Complete — /code-review (godot-csharp-specialist: 1 blocking fixed; qa-tester: gaps fixed), QL-TEST-COVERAGE ADEQUATE, LP-CODE-REVIEW CONCERNS 1–3 fixed (Merge `blocked`, Pickup `container_gone`, `CheckRequest` split per kind), 4–6 → TD-105

