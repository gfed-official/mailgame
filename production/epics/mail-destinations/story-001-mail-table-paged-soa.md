# Story 001: Mail table — paged SoA indexed by `mailId`, row allocation in manifest order, retire, dump contributor

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~300 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/mail-destinations.md` (R2 mail data; R11 rows created at the batch tick)
**Requirement**: `TR-mail-006` (server-only SoA mail table keyed by `mailId`; packed `AddressId`)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §2); ADR-0004 (`MailId` lower = older, allocated only by Mail); ADR-0006 (dump field by field)
**ADR Decision Summary**: `MailId`s are allocated only by Mail, in manifest order (lower = older). The row address is `(mailId − 1) >> 10` (page) and `(mailId − 1) & 1023` (slot); there is no hash map. Each page holds preallocated 1,024-element columns: `kind` (`DefIndex`), `addressId` (`AddressId`), `spawnValue` (ushort cents), `spawnShift`/`deadlineShift` (byte), `flags` (byte: Cursed, Found; Priority/Fragile reserved), plus a `live` bitmap (`ulong[16]`). There is no location column (location is Inventory's `UnitLedger`). Pages come from a pool preallocated at world build (`SimSettings.mailTablePages`, default 64). A page returns to the pool when all its slots were allocated and `liveCount` reaches 0. Exhaustion grows the pool, records `SimCheck(MailTableGrow)` and is the only allocating path. The table is server-only and is dumped as live rows in ascending `mailId`.
**ADR Version**: ADR-0017 2026-10-16; ADR-0004 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None. Verify 0 B on the net8.0 leg.

**Control Manifest Rules (Feature layer + Global)**:
- Required: the mail table is server-only SoA with no location column (ADR-0017)
- Required: ids are allocated via the injected `MailIds` inside `Tick()` only and ordered by raw `Value` (ADR-0004)
- Required: bind an `ISimStateContributor` for any state; size every collection from `SimSettings`; 0 B in the tick path (ADR-0006 §4, §8)
- Forbidden: `Dictionary<MailId, …>` (ADR-0017 Alt 1); a location column; `List<T>` growth in the tick path
- Guardrail: one page ≈ 11 KB; 64 pages ≈ 0.7 MB preallocated (ADR-0017 Performance Implications)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md` (M-R2-1) and ADR-0017 §2, scoped to this story:*

- [x] **AC-1 (M-R2-1a)** With the mail counter at 41, creating rows for a 3-unit batch allocates mailIds 42, 43, 44 in the order given, and each row stores `deadlineShift = spawnShift`.
- [x] **AC-2 (M-R2-1b, c)** A row's `spawnValue` never changes after creation (there is no setter; a later StatSheet change cannot reach it). `Retire(mailId)` clears the live bit and decrements the page's `liveCount`; reading a retired row reports not-live.
- [x] **AC-3 (M-R2-1d, e)** Rows are retired only by `Retire`; nothing retires them on a shift change. 10,000 allocations yield no repeated mailId.
- [x] **AC-4 Pages and pool.** With `mailTablePages = 2`, allocating 2,048 rows uses both preallocated pages with 0 B allocated. Retiring every row of page 0 returns it to the pool, and the next page is drawn from the pool, not newly allocated. The 2,049th live row (pool empty) grows the pool, records exactly one `SimCheck(MailTableGrow)` and succeeds.
- [x] **AC-5 Dump.** The `Mail` dump section lists live rows in ascending `mailId`, field by field. Two tables built by the same call sequence give identical bytes; retiring one row changes them.

---

## Implementation Notes

*Derived from ADR-0017 §2 and Implementation Guidelines:*

- **Files:** `src/PerformativeMail.Sim/Mail/MailTable.cs` (internal sealed class), `MailPage.cs` (struct-of-arrays page), `MailFlags.cs` (`[Flags] byte`: `Cursed = 1, Found = 2`, bits 2–3 reserved for Priority/Fragile).
- **API (internal to `Sim/Mail`):** `MailId Create(DefIndex kind, AddressId addr, ushort spawnValue, byte spawnShift, MailFlags flags)` allocates via the injected `MailIds` (`production/epics/simulation-core/story-018-id-allocators-slottable.md`) and writes the row with `deadlineShift = spawnShift` (GDD R2: every v1 `deadlineOffsetShifts` = 0). Also `bool TryGetRow(MailId id, out MailRow row)` (a readonly struct copy) and `void Retire(MailId id)`.
- `MailId` allocation is legal only inside `Tick()` on the owner thread (ADR-0004). Tests drive creation from a stub phase, or use the allocator's test hook. They never call it between ticks.
- The page map is `MailPage?[]` indexed by page number, sized for the maximum mailId page count. Allocating mailIds strictly ascending means only the newest page is ever partially allocated. A page whose 1,024 slots have all been handed out and whose `liveCount` drops to 0 goes back to the pool (clear its columns on reuse).
- **Pool growth** is the single allocating path: double the pool array outside any hot loop, call `SimCheckRing` (`production/epics/simulation-core/story-007-simcounters-simcheckring.md`) with `MailTableGrow` (add the check id), and continue. The default size makes this a test failure in normal fixtures.
- **Dump:** register `ISimStateContributor` with a new `SectionIds.Mail` (`production/epics/simulation-core/story-019-canonical-state-dump.md`). Write `u32 liveCount`, then per live row in ascending mailId: `mailId u32, kind u16 (DefIndex raw), addressId u32, spawnValue u16, spawnShift u8, deadlineShift u8, flags u8`. Story 002 appends the registry fields to the same section.
- `AddressId` is `Sim/Primitives/AddressId`, created by `production/epics/world-stub/story-004-addressid-lots-destination-ids.md`. Do not re-create it here.
- `DefIndex` is from `production/epics/content-data/story-001-defindex-stringid-deftable.md`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014: creating rows at the batch tick and handing units to Inventory (R11).
- Story 007: retiring rows on consume.
- Value hint / `MailUnitFacts` to clients (R17, M0 wire; client mirror in story 010).
- Payday "returned to sender" retirement (M1).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/mail/MailTableTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/mail/MailTableTests.cs (20), MailTableAllocationTests.cs (2, NonParallel), MailTableTestRig.cs

---

## Dependencies

- Depends on: `production/epics/world-stub/story-004-addressid-lots-destination-ids.md` (`AddressId`); `production/epics/simulation-core/story-018-id-allocators-slottable.md`, `production/epics/simulation-core/story-007-simcounters-simcheckring.md`, `production/epics/simulation-core/story-019-canonical-state-dump.md`; `production/epics/content-data/story-004-balance-registry-simsettings.md` (`mailTablePages`)
- Unlocks: Story 007, Story 014

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 5/5 passing (all covered by automated tests; none deferred)
**Deviations** (advisory, logged as TD-074): namespace `PerformativeMail.Sim.MailDestinations` (folder `Sim/Mail/`) to avoid shadowing `Content.Rules.Mail`; page map `MailPage?[]` sized max(256, 16×mailTablePages) and doubles (second allocating path, `MailTableGrow` a=2; pool growth a=1); page close rule handles mid-page counters; `Retire` returns bool; `Create` returns `MailId.None` when `MailIds` refuses; `sim.mailTablePages` balance key (default 64, hard range 1..1024) added here since content-data 004 had not; `MailTable.BindDump(builder)` stopgap (no SimComposition registration — story 005).
**Enum values**: `CheckCode.MailTableGrow = 7`; `SectionIds.Mail = 3`.
**Test Evidence**: Logic — tests/unit/mail/MailTableTests.cs + MailTableAllocationTests.cs (22 mail tests) + `Build_MailTablePages_HardRangeIs1To1024` in tests/unit/content/BalanceTableTests.cs. Full suite 3,175/leg, 0 failed on net10/net8 × Debug/Release (Debug 21 skipped — zero-alloc guardrails, TD-008), verified by the orchestrator on the real repo.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (+ C#-specialist pass) → one revision round (grow discriminator, nullable idioms, MailPage Add/Remove encapsulation, 1..1024 max, test nit, Create doc); QL-TEST-COVERAGE GAPS → gaps 1–4 fixed (map-growth, refusal, recycled-page tests).
**For story 002**: append registry fields to the Mail section after the rows in `MailStateContributor.Write` (marked append point); give the contributor a registry parameter; the AC-5 byte-exact tests will need updating.
