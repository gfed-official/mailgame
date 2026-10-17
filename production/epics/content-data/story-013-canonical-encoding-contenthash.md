# Story 013: Canonical content encoding, managed SHA-256 and ContentHash

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD25, F6; Hashing & join edge cases)
**Requirement**: `TR-content-024`. The epic table slices this TR at M0. It is **pulled forward to M-1a** by this story, following ADR-0010's Migration Plan ("M-1a: … canonical encoder and managed SHA-256 with tests"). ADR-0006 §7's M-1a recorder header also needs the `ContentSet` hash, and story 019's loader-equality test compares canonical bytes. The handshake use, TR-content-025, stays M0.
*(No `docs/architecture/tr-registry.yaml` exists yet. Requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§6) (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (§4 little-endian conventions, §7 recorder header)
**ADR Decision Summary**:
- `ContentHash = SHA-256(hashSchemaVersion u8 ‖ BalanceBlock ‖ DefBlocks)`, computed over **loaded values**:
  - balance keys in ordinal order as `(u16 len, UTF-8 key, i64 LE)`;
  - def types in a fixed append-only `DefType` order, and defs in `DefIndex` order;
  - fixed-width LE fields, set-valued lists sorted, optional fields with a presence byte.
- The encoder and a hand-written managed SHA-256 (with NIST known-answer tests) both live in the Sim. There is no `System.Security.Cryptography` reference.

**ADR Version**: ADR-0010 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: The hand-written SHA-256 avoids platform crypto (OpenSSL on Linux; availability in a Godot export is NOT SOURCEABLE). That is why spike S3 was retired. The golden digest on both CI operating systems lands when the Windows CI job exists (CDD-HASH-3).

**Control Manifest Rules (Foundation)**:
- Required: `ContentHash = SHA-256(hashSchemaVersion u8 ‖ BalanceBlock ‖ DefBlocks)` over loaded values. The canonical encoder and a hand-written managed SHA-256 (with NIST known-answer tests) live in the Sim. Bump `hashSchemaVersion` whenever the layout changes. (ADR-0010 §6)
- Forbidden: hashing file bytes, or platform crypto (`System.Security.Cryptography`). (ADR-0010 Alt 4)
- Cross-ADR note **I-5** (control manifest): ADR-0006 §4 says "BCL's managed SHA-256 inside the Sim". **Follow ADR-0010**, the owner: hand-written, no `System.Security.Cryptography` reference.
- Required: little-endian fixed-width writes via `BinaryPrimitives`. Strings are written as UTF-8 with no normalisation. (ADR-0006 §4)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] The managed `Sha256` passes the NIST FIPS 180-2 known-answer vectors: `""`, `"abc"`, the 448-bit two-block message and one million `'a'`. It also gives equal digests for one-shot and incremental (chunked) input. The Sim assembly references no `System.Security.Cryptography` type (architecture test). (CDD-HASH-2, SHA half)
- [x] An independent test encoder and the production encoder's primitives both produce exactly the F6 illustrative 27 bytes for the `letter` example: `06 6C 65 74 74 65 72 | 01 00 | 14 00 | 90 01 00 00 00 00 00 00 | FF FF | 02 00 01 00 03 00`. The type block opens `06 00` for a count of 6. The example uses a five-field test-only def shape. (CDD-HASH-2)
- [x] For `BuiltInContent.CreateM1a()`, `ContentHash` changes when:
  - one value changes (e.g. `plank.SellPriceCents` +1);
  - one balance key changes;
  - an optional field's presence changes (e.g. `BuyPriceCents`).

  It does **not** change when a set-valued list (`acceptedBy`, `accepts`, `allowedCategories`) is supplied in a different order. (CDD-HASH-1, value half)
- [x] `ContentHash.Compute` gives the same digest across two processes and in Debug and Release. A golden digest for `CreateM1a()` is checked in and asserted. The display form is the first 4 bytes as 8 lowercase hex characters. (F6; CDD-HASH-3 single-OS form)
- [x] An empty def table encodes as `00 00`. A list or string longer than 65 535 entries or bytes is rejected at encode time with CV-GEN-12. (Loading edge case, F6)

---

## Implementation Notes

*Derived from ADR-0010 §6 and GDD F6:*

- **Location:**
  - `src/PerformativeMail.Sim/Content/Hashing/CanonicalContentWriter.cs`
  - `src/PerformativeMail.Sim/Content/Hashing/Sha256.cs` (~60 lines, `UInt32` rotations, no unsafe needed)
  - `ContentHash.cs` and `Hash256` (a 32-byte value type with explicit equality)
- **Encoding rules:**
  - `hashSchemaVersion` = 1. Bump it whenever the layout changes.
  - **BalanceBlock** = `u16 keyCount`, then every key in ordinal order as `u16 len, UTF-8 key, i64 LE`. This includes all `SimSettings` keys; there is no opt-in flag.
  - **DefBlocks** come in `DefType` order (story 001: Item, Container, MailKind, DestinationType, MailMix; append-only). Each block is `u16 count`, then each def as `u8 idLen, id`, then its fields in declared encode order:
    - bool → u8;
    - enum → u16 numeric value;
    - `DefIndex` → u16 (`0xFFFF` = none);
    - int/long/ushort at their declared width;
    - list → `u16 count` + items, with set-valued lists sorted by value first;
    - nested leaf → its fields in order;
    - string (localisation key) → `u16 len` + UTF-8;
    - optional or variant field → `u8` presence byte + value.
- **Per-def encode order.** Each def type gets a hand-written `WriteCanonical(ref writer)` that defines its field order. Story 018's `FieldSpec` tables must list fields in the same order, and a test there asserts it. `suppress`, `$schema` and comments are never encoded.
- **The `ISimStateContributor` canonical writer** (`production/epics/simulation-core/story-019-canonical-state-dump.md`) is a different consumer with its own conventions. Reuse its LE primitive writers if they fit `IBufferWriter<byte>`. Otherwise keep a small content writer.
- **Replay header.** ADR-0006 §7 records the `ContentSet` hash and the SHA-256 of the `SimSettings` canonical encoding in the recorder header. Expose `ContentHash.ComputeSettings(SimSettings)` for that.
- **Pull-forward** (decision logged): the M0 parts (CLI `--print-hash`, the handshake, the two-OS golden) stay M0.

---

## Out of Scope

- Story 019: hash invariance under comments, whitespace, line endings and key order (needs the JSONC loader; the CDD-HASH-1 file half and CDD-PARSE-1).
- M0 (network-adapter / Networking epic): the `Hello` handshake compare (TR-content-025).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/ContentHashTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/content/ContentHashTests.cs` (49 cases)

---

## Dependencies

- Depends on: Story 001 (`DefType` order), Story 008 (`ContentSet`, `BuiltInContent`)
- Unlocks: Story 019 (equality via canonical bytes); the simulation-core recorder-header work (`production/epics/simulation-core/story-019-canonical-state-dump.md` names the `ContentSet`/`SimSettings` hashes in the header).

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (two-OS golden and handshake stay M0; CDD-HASH-1 file half is story 019)
**Deviations** (advisory, logged as TD-075): encode-time CV-GEN-12 is a `ContentEncodingException` carrying `RuleId`, not a registered diagnostic (the loader, story 018, must report it first); `WriteCanonical` lives as extension methods in `Content/Hashing/DefCanonical.cs`; separate `CanonicalContentWriter` (unframed, throws) instead of sim-core's section-framed `CanonicalWriter`; `Tags`/`ReservedSlots`/`Shares` hashed in authored order; `ComputeSettings` hand-maps the `sim.*` keys (guarded by a registry-equality test); `ResolvedStatBounds` not hashed (TD-055(9)).
**Golden**: `ContentHash.Compute(BuiltInContent.CreateM1a())` = `c8d306fd1c07c3aad1b2adac66b110bb9421e3758635fd0f056aba2538384116` (display `c8d306fd`), hashSchemaVersion 1.
**Test Evidence**: Logic: `tests/unit/content/ContentHashTests.cs` (49 cases: NIST KATs, chunked == one-shot, BCL oracle 0..200, no System.Security.Cryptography architecture test, F6 27-byte letter example, sensitivity/invariance, golden, 65 535/65 536 limits); four legs green (3 175–3 205 tests/leg as other runners added tests, 0 failed; Debug 21 skipped).
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (finding 3 fixed by a guard test, rest → TD-075); QL-TEST-COVERAGE ADEQUATE.
