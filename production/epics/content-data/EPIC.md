# Epic: Content & Data Definitions (incl. StatSheet)

> **Layer**: Foundation
> **GDD**: design/gdd/content-data-definitions.md (In Review, Provisional)
> **Architecture Module**: Sim.Content (`src/PerformativeMail.Sim/Content/`); the Godot `ContentHost`/`ViewCatalog` halves are in `godot-host-shell`
> **Milestone slice**: M-1a (pulled forward from the GDD's "hard-coded in M-1 → loader at M0"; see Overview)
> **Engine Risk**: LOW (Sim half). The HIGH half (export filter, PCK enumeration, `[Export]` tracking; spikes S1–S4) is deferred to `ContentHost`.
> **Est. size**: M — 10–12 stories
> **Implementation priority**: 2 of 9
> **Status**: Ready
> **Stories**: 19 stories — 17 Complete, 1 In Progress, 1 Ready (counts refreshed 2026-10-20 from the story headers) — see the Stories table below

## Overview

This epic builds the content model every gameplay system reads: immutable `sealed record` defs in `Sim/Content`, ordinal `DefIndex`/`StringId` resolution, closed enums with explicit values, per-owner balance keys and `SimSettings`, and the `StatSheet` that resolves `base × ΠMul + ΣAdd` (or a Set) in one Int128 rounding followed by a design clamp. All of it comes together in one process-lifetime `ContentSet`, passed by reference into `SimComposition.Build`.

**M-1a slice decision.** The GDD's slice and ADR-0010 say M-1a uses hard-coded constants through a `BuiltInContent` factory **in the same types**, with the loader and the validator CLI at M0. The orchestrator's priority list asks for a *minimal loader plus StatSheet* at M-1a. Both are honoured:
- `BuiltInContent` is the M-1a source of truth for the greybox defs;
- a minimal loader (stages 1–3 of the five-stage pipeline: read bytes through `IContentSource`, hand-written `Utf8JsonReader` walk with the exact decimal → scaled-integer reader, def binding to records) is built for the def types M-1a uses, and is tested against `BuiltInContent` for equality.

The full validator catalogue (84 rules), the `tools/ContentValidator` CLI, the generated `manifest.txt` with drift checks, `ContentHash` in the join handshake and hot reload stay at M0. This pull-forward adds work and cuts nothing; it is logged in the decision log.

**In scope at M-1a:**
- **Def records** for the M-1a set:
  - `ContainerDef` and `ItemDef` (Grid Inventory);
  - `MailKindDef`, `DestinationTypeDef` and a fixed spawn profile (Mail);
  - movement balance keys and `KitDef.standardIssueItem` (Player Character);
  - `ThrowDef` and the throwing balance (Throwing);
  - the input hold keys (`holdSetting`, `holdFloor`, `holdCeil`);
  - `SimSettings`.
- **Ids and data rules:** the def id regex, ordinal sort into `DefIndex` (+1 storage, default = None), `StringId`, neutral defaults, a required `weightClass`, the unit-suffix convention with `FieldSpec`, and the rejection of duplicate or unknown keys, exponents and `-0`.
- **StatSheet:**
  - the `Stat` closed enum with `StatDef` rows; `IStatSheet.Read` returns an `int` and allocates 0 B; `IStatWriter` is restricted to Perks and Stamps (no writers at M-1a);
  - `RuleFlag`, `transientAdd` and `Explain()`;
  - team and per-slot derived sheets;
  - the F1 StatResolve vectors.
- **Validator subset:** the validator rule registry, seeded with the rules the M-1a defs need (CV-INV, CV-MAIL, CV-MOV and CV-THR rows referenced by those GDDs). The registry stays the single source of load-time rules.

**Risks.**
- (1) The GDD is Provisional with 14 review blockers revised and awaiting re-review. Field names may still move (architecture.md Q-ARCH-07: `throwSpreadMrad` retired by the no-dice ruling, `cooldownSlackTicks`, `foundValueMultPm`). Stories must follow ADR-0010 field names and flag leftovers rather than invent values.
- (2) `InvariantGlobalization` is not in force in editor runs (ADR-0001 F4). Culture-free parsing must come from ordinal APIs and the ADR-0006 analyzer.
- (3) The loader pull-forward must not leak `res://` or Godot types into the Sim. `IContentSource` is the only file seam (TR-content-021).

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0010: Content Format, Loading, ContentSet and StatSheet | JSONC under `content/`, hand-written `Utf8JsonReader`, immutable records, 5-stage loader via `IContentSource`, canonical encoding + in-Sim SHA-256, StatSheet F1 (Int128, one rounding), `IStatWriter` for Perks/Stamps only, M-1a `BuiltInContent` | LOW (Sim) / HIGH (deferred Godot half) |
| ADR-0004: Identity and Id Spaces | `DefIndex` ushort ordinal (0xFFFF none via +1 storage), `StringId`, never persisted | LOW |
| ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay | integer units, rounding toolkit, culture-free text, analyzer | LOW |
| ADR-0003: Phase Order, Composition Root and Cross-System Calls | `ContentSet`, `IStatSheet`, `IStatWriter` on the closed injectable list | LOW |

## GDD Requirements

Source: architecture.md v1.1 Appendix A. **30 / 30 traced** (TR-content-027's implementation half is in ADR-0019, not yet written).

| TR-ID | Requirement | ADR Coverage | Slice |
|-------|-------------|--------------|-------|
| TR-content-001 | Sim content is JSONC under content/, one dir per def type; YAML and .tres rejected | ADR-0010 ✅ | M-1a |
| TR-content-002 | Generated content/manifest.txt drives loading; CI fails on drift | ADR-0010 ✅ | M0 |
| TR-content-003 | Hand-written Utf8JsonReader walk; duplicate/unknown keys, exponents, -0 rejected | ADR-0010 ✅ | M-1a |
| TR-content-004 | Three data classes: Sim content (hashed), Presentation content, Player data (Persistence) | ADR-0010 ✅ | M-1a |
| TR-content-005 | Unit-suffix convention; decimals stored as ms/mm/permille; money integer cents | ADR-0010 ✅ | M-1a |
| TR-content-006 | Every numeric field declares a unit via code-side FieldSpec | ADR-0010 ✅ | M-1a |
| TR-content-007 | Def id regex; sorted ordinal into DefIndex ushort (0xFFFF none) | ADR-0004 ✅ | M-1a |
| TR-content-008 | DefIndex never persisted; on the wire only after hash match | ADR-0004 ✅ | M-1a |
| TR-content-009 | StringId = (namespace, DefIndex); run-time strings interned per run | ADR-0004 ✅ | M-1a |
| TR-content-010 | Defs immutable sealed record class in Sim/Content; no floats; no Godot Resource | ADR-0010 ✅ | M-1a |
| TR-content-011 | Defaults always neutral; weightClass required on ItemDef and MailKindDef | ADR-0010 ✅ | M-1a |
| TR-content-012 | Closed enums with explicit never-reused values; hand-written parse tables | ADR-0010 ✅ | M-1a |
| TR-content-013 | Balance keys in content/balance/<owner>.jsonc; BalanceKey registry; SimSettings in balance/sim.jsonc | ADR-0010 ✅ | M-1a |
| TR-content-014 | Stat closed enum with StatDef rows; design clamps are balance keys | ADR-0010 ✅ | M-1a |
| TR-content-015 | StatResolve F1 in Int128, one rounding, order-independent, k ≤ 6 | ADR-0010 ✅ | M-1a |
| TR-content-016 | IStatSheet read anywhere; IStatWriter only Perks and Stamps; meta never pushes modifiers | ADR-0010 ✅ | M-1a |
| TR-content-017 | Sheets are derived caches; team + per-slot sheets; source lists replicated | ADR-0010 ✅ | M-1a derived caches; replication M0 |
| TR-content-018 | RuleFlag enum; timed effects via transientAdd; sheet has no timers | ADR-0010 ✅ | M-1a |
| TR-content-019 | Explain() returns resolved/unclamped/sources; card numbers from placeholders | ADR-0010 ✅ | M-1a |
| TR-content-020 | 5-stage load pipeline → one immutable ContentSet passed into new SimWorld | ADR-0010 ✅ | M-1a |
| TR-content-021 | Loader reads via IContentSource; Sim never reads res:// | ADR-0010 ✅ | M-1a |
| TR-content-022 | Validator catalogue 84 active rules (CV-<AREA>-NN); errors block the build | ADR-0010 ✅ | M-1a subset (rules for M-1a defs); full catalogue M0 |
| TR-content-023 | tools/ContentValidator console; flags, exit codes 0–3, fixture per rule | ADR-0010 ✅ | M0 |
| TR-content-024 | ContentHash = SHA-256 over canonical encoding; encoder and managed SHA-256 both in the Sim, at load time (ADR-0010 §6, ADR-0006) | ADR-0010 ✅ | M0 |
| TR-content-025 | Join handshake compares 32-byte hash with refusal precedence; content never transferred | ADR-0010 ✅ | M0 |
| TR-content-026 | Debug hot reload (F6): errors keep old set; M0 reload-and-restart; in-place swap M4 | ADR-0010 ✅ | M0 |
| TR-content-027 | Presentation ViewCatalog keyed by def id → DefIndex arrays; placeholder on missing view | ADR-0010 ✅ (contract; implementation → ADR-0019 ❌ not written) | M-1a placeholder only (host shell); full ViewCatalog M0 |
| TR-content-028 | Boot parse+validate+hash ≤100 ms CPU; bind ≤25 ms; sheet rebuild ≤0.2 ms; Read 0 B | ADR-0010 ✅ | advisory |
| TR-content-029 | No mod support; hash is a mismatch detector, not security | ADR-0010 ✅ | policy |
| TR-content-030 | MetaProfile/settings owned by Persistence; content ids as strings; unknown dropped + logged | ADR-0010 ✅ | M1 |

**Untraced:** none for the Sim half. TR-content-027 (`ViewCatalog`) has its contract in ADR-0010 but its implementation in ADR-0019, which is not written. M-1a uses a placeholder only (see `godot-host-shell`).

## Stories

The numeric order is a valid implementation order. Each story's `Depends on:` field also names its simulation-core prerequisites; that epic has no story files yet.

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [DefIndex assignment, StringNamespace def members and DefTable](story-001-defindex-stringid-deftable.md) | Logic | Complete | ADR-0004, ADR-0010 |
| 002 | [Closed content enums and their data-spelling tables](story-002-closed-content-enums.md) | Logic | Complete | ADR-0010 |
| 003 | [Content diagnostics and the validator rule registry core](story-003-diagnostics-and-rule-registry.md) | Logic | Complete | ADR-0010 |
| 004 | [BalanceKey registry, BalanceTable and SimSettings](story-004-balance-registry-simsettings.md) | Logic | Complete | ADR-0010, ADR-0003 |
| 005 | [M-1a def records (Item, Container, MailKind, DestinationType, MailMix)](story-005-m1a-def-records.md) | Logic | Complete | ADR-0010 |
| 006 | [Stat enum, StatDef table (M-1a rows) and RuleFlag](story-006-stat-statdef-ruleflag.md) | Logic | Complete | ADR-0010 |
| 007 | [F1 StatResolve and F5 WorstCaseStat — pure Int128 formulas](story-007-f1-statresolve-f5-worstcase.md) | Logic | Complete | ADR-0010, ADR-0006 |
| 008 | [ContentSet and BuiltInContent.CreateM1a — the M-1a greybox set](story-008-contentset-builtincontent-m1a.md) | Integration | Complete | ADR-0010, ADR-0003 |
| 009 | [General set-level rules — CV-GEN-01, 09, 17, 18](story-009-cv-gen-set-rules.md) | Logic | Complete | ADR-0010 |
| 010 | [Grid and Mail content rules — CV-GRD-01…04, 06, 07; CV-MAIL-01…06](story-010-cv-grd-cv-mail-rules.md) | Logic | Complete | ADR-0010 |
| 011 | [Player Character content rules — CV-PLR M-1a subset](story-011-cv-plr-rules.md) | Logic | Complete | ADR-0010 |
| 012 | [Throwing content rules — CV-THR M-1a subset](story-012-cv-thr-rules.md) | Logic | Complete | ADR-0010 |
| 013 | [Canonical content encoding, managed SHA-256 and ContentHash](story-013-canonical-encoding-contenthash.md) | Logic | Complete | ADR-0010, ADR-0006 |
| 014 | [StatSheet.Build, cached cells, Read and Explain](story-014-statsheet-build-read-explain.md) | Logic | Complete | ADR-0010 |
| 015 | [IStatWriter, source identity and versioning, lifetimes, RuleFlags, composition wiring](story-015-statwriter-sources-lifetimes-wiring.md) | Integration | Complete | ADR-0010, ADR-0003 |
| 016 | [DecimalToScaled reader, FieldSpec units and ExactTickCheck](story-016-decimal-to-scaled-fieldspec-units.md) | Logic | Complete | ADR-0010, ADR-0006 |
| 017 | [IContentSource, InMemory/Directory sources and the stage-1 syntax walk](story-017-content-sources-syntax-stage.md) | Logic | Complete | ADR-0010 |
| 018 | [Schema and build stages for M-1a def files](story-018-schema-and-build-stages.md) | Logic | In Progress | ADR-0010, ADR-0004 |
| 019 | [ContentLoader.Load — balance files, five stages, shipped JSONC equals BuiltInContent](story-019-content-loader-and-builtin-equality.md) | Integration | Ready | ADR-0010 |

These are not stories at M-1a. Each decision is logged in `production/session-logs/decision-log.md`:
- `KitDef` (its readers are M-1b and M1).
- The input hold keys: `holdSetting` is player data; `holdFloor` is derived from `player.minHoldTicks`; `holdCeil` is an Input constant.
- CV-SIM-01…04 (SC-SET-1 is M-1b).
- CV-GRD-05 (M-1b).
- CV-PLR-03, 05, 07, 12 and 13.
- CV-THR-07.

On naming: `ThrowDef` is realised as the `throw` balance owner record (`ThrowBalance`), and the epic's "fixed spawn profile" is realised as `MailMixDef`.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All M-1a-applicable acceptance criteria from `design/gdd/content-data-definitions.md` are verified: load/parse/units/ids, schema and enums, and StatSheet; the validator/CLI/hash/network/hot-reload groups stay open on M0
- `BuiltInContent` and the minimal loader produce equal `ContentSet`s for the M-1a defs (one equality test)
- All Logic and Integration stories have passing test files in `tests/` (`tests/unit/content/**`)
- All Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/` (none are expected in this epic)

## Next Step

Run `/story-readiness production/epics/content-data/story-001-defindex-stringid-deftable.md`, then `/dev-story`.
