# ADR-0010: Content Format, Loading, ContentSet and StatSheet

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously; engine check by `godot-specialist`. TD-ADR skipped (lean mode). Inputs: content-data-definitions.md (Provisional, re-read 2026-10-16 14:07 while under revision), simulation-core.md, grid-inventory.md, architecture.md v1.

- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
Every gameplay number and def must come from data that two peers can prove identical, without floats, reflection or a Godot dependency in the Sim. This ADR fixes the Sim half of Content & Data: JSONC under `content/` read as bytes through `IContentSource`, a hand-written `Utf8JsonReader` walk with an exact decimal → scaled-integer reader, immutable `sealed record` defs in `Sim/Content`, a five-stage loader into one process-lifetime `ContentSet`, one code-side validator rule registry, a canonical encoding hashed with an in-Sim managed SHA-256 for the join check, and a StatSheet whose `Read` resolves `base × ΠMul + ΣAdd` (or a Set) with one Int128 rounding then a clamp. The Godot `ContentHost` packaging and the `ViewCatalog` resource are **not** decided here: they wait for spikes S1/S2/S4 and ADR-0019.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (C#, .NET 8 minimum for desktop) |
| **Domain** | Core / Data (plain-C# Sim + tools); Godot file I/O only behind `IContentSource` |
| **Knowledge Risk** | LOW for the decided (Sim) half; HIGH for the deferred Godot half (export filter, PCK enumeration, trimming, `[Export]` dependency tracking are post-cutoff and NOT SOURCEABLE) |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md` (4.4 `FileAccess.store_*` → bool; 4.6 `FileAccess.get_as_text` lost `skip_cr`; 4.5 `Resource.duplicate(true)` behaviour), `deprecated-apis.md`, `current-best-practices.md` |
| **Post-Cutoff APIs Used** | None in the Sim. The deferred `ContentHost` will use `Godot.FileAccess.GetFileAsBytes` (bytes, never `get_as_text` — 4.6 change VERIFIED) and `FileAccess.GetOpenError()`. |
| **Verification Required** | S1: `*.jsonc` + `manifest.txt` included by the 4.7.1 export filter and readable via `GetFileAsBytes` on Windows and Linux (else `EmbeddedResource`). S2: `Utf8JsonReader`/`ImmutableArray` run in a release desktop export. S4: `[Export] PackedScene` arrays in a C# `ViewCatalog` keep export dependency tracking and survive renames via `uid://`. None blocks the Sim half or M-1a. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0003 (composition root and the injectable list: `ContentSet`, `IStatSheet`, `IStatWriter` for Perks/Stamps only — OQ-CDD-1a); ADR-0004 (`DefIndex` u16 with `0xFFFF` none, `StringId` contract, never persisted); ADR-0006 (integer-only numerics and float/double/decimal ban, `Int128` use, culture-free determinism rules, little-endian canonical encoding conventions) |
| **Enables** | ADR-0009 (container/item defs, `MailMaxStack`/`InventoryRows`/`WeightPenaltyPm` reads); ADR-0011 (content-hash handshake payload); ADR-0019 (Godot `ContentHost` + `ViewCatalog` implementation, after S1/S2/S4); ADR-0024 (profiles store content ids as strings) |
| **Blocks** | Content & Data epic (loader, validator CLI, StatSheet); every system's def/balance reads; M0 `ContentValidator` CI gate |
| **Ordering Note** | M-1a uses a `BuiltInContent` factory that constructs the same def types and `SimSettings` in code (no parser yet); the JSONC loader and CLI land at M0. The Godot half is ADR-0019's and is spike-gated; nothing in this ADR waits on it. |

## Context

### Problem Statement
The Sim must read every def, balance key and `SimSettings` value from data, identically on every peer and across Windows and Linux, with no float anywhere in authoritative state. Spec 07/08 assumed JSON + reflection binding + Godot Resources; the Content & Data GDD rejected each for concrete reasons (silent unknown keys, last-wins duplicates, no exact decimal reading, Godot types in the Sim, OpenSSL dependence) and lists the loader shape, manifest, ContentHost shape, ViewCatalog split and an Int64 fast path for `Read` as ADR candidates. Foundation code (M-1a) needs the def types and StatSheet now.

### Constraints
- Sim is plain C# `net8.0`, zero Godot references, no runtime package dependencies (SC R33); `System.Text.Json` is a shared-framework dependency, allowed.
- No `float`/`double`/`decimal`/`Half` in authoritative state (SC R27); analyzers enforce it.
- Determinism: ordinal compares only, `InvariantGlobalization`, no reflection discovery at runtime, no Dictionary-iteration order (SC R28).
- `Read` allocates 0 B and is called every tick by continuous systems.
- Friends-only trust (spec 13 §4.3): the hash detects accidents, it is not security; content is never transferred.
- Boot budget (PROPOSED): parse + validate + hash ≤ 100 ms CPU; bind ≤ 25 ms; 8 × 5-perk sheet rebuild ≤ 0.2 ms.

### Requirements
- CD1–CD28, F1–F6 and the validator catalogue of content-data-definitions.md; TR-content-001…030; TR-inventory-026; TR-movement-019/020; TR-mail-018; TR-throwing-019; TR-hud-007/020.

## Decision

### 1. Format, layout, sources
- **Format:** JSONC (`//` comments, trailing commas) for all Sim content and JSON Presentation content. YAML, `.tres` for Sim content, and CSV are rejected (CSV may be revisited for a table > ~100 rows).
- **Layout:** repo `content/` (= `res://content/`; the Godot project root is the repo root), one directory per def type, several themed array files per directory (CD2 list), balance in `content/balance/<owner>.jsonc` (one owner GDD per file; `SimSettings` keys in `balance/sim.jsonc`, owned by Simulation Core), Presentation data under `content/presentation/` (not hashed).
- **Manifest:** `content/manifest.txt` (every content path, lowercase ASCII, ordinal-sorted) is **generated by `ContentValidator --write-manifest` as an export step and never committed**. Only a packaged (PCK) boot reads it; `DirectorySource` enumerates and ordinal-sorts the directory itself. File order never affects the hash.
- **Sources:** the loader reads only through `IContentSource` (`ListManifest()`, `Read(path) → ReadOnlyMemory<byte>`). `DirectorySource` (CLI, tests, debug F6 reload) and `InMemorySource` (tests, fixtures) live in the Sim assembly's `Content/Sources/` (plain `System.IO`, no Godot). The Godot `ContentHost : IContentSource` is ADR-0019's. **The Sim never reads `res://`.**

### 2. Parser: hand-written `Utf8JsonReader` walk
- One forward pass per file over bytes (BOM stripped by the loader; CR and LF both whitespace; comments skipped; trailing commas allowed). No `JsonSerializer`, no reflection, no `JsonDocument` DOM.
- Driven by a code-side **`FieldSpec` table per def type** (name, unit, required/default, range, enum set or reference target). Duplicate keys and unknown fields are errors (edit-distance ≤ 2 suggestion); `$schema` and `suppress` are the only reserved keys.
- **Numbers are read from raw token bytes (`ValueSpan`), never `GetDouble`/`GetDecimal`:** the hand-written **DecimalToScaled** reader (GDD F2) accepts `-?(0|[1-9][0-9]*)(\.[0-9]{1,3})?`, accumulates in `long`, stops on overflow, rejects exponents, `-0`, a leading `+`, a bare `.` and a fourth fractional digit; integer-unit fields reject any `.`. Units decide scale (CD4: `…Seconds` → ms, `…Metres` → mm, `…Ratio`/Mul `value` → permille, `…Points` → milli-points, `…Deg` → milli-degrees; `…Ms`/`…Mm`/`…Pm`/`…Ticks` and money integers as-is). A numeric field with no declared unit is a load error.
- **Enums:** closed C# enums with explicit never-reused values; snake_case ↔ member tables generated at compile time (a source generator is allowed; runtime reflection and `Enum.Parse` are not).
- Every diagnostic carries `path:line:column` (LF lines, 1-based UTF-8 byte column), the rule id, the value and the allowed range/set.

### 3. Defs: immutable plain C# records in `Sim/Content`
- Defs are `sealed record class` types with init-only `int`/`long`/`ushort`/`bool`/enum members; small leaves may be `readonly record struct`; lists are `ImmutableArray<T>` (hot paths use `AsSpan()`); **no `T[]`, `float`, `double`, `decimal` or `Half` member anywhere** (reflection test over every def type, nested types and generic arguments). Record equality and `with` are not relied on; defs are identified by `DefIndex`.
- **`DefIndex`:** each def type's ids are sorted with `StringComparer.Ordinal` and numbered from 0 (GDD F4). The type is **ADR-0004 §6's struct** (`PerformativeMail.Sim.Primitives`), not redeclared here: `+1` storage, so `default(DefIndex)` is None; `Value` returns the ordinal, or `0xFFFF` for None. `0xFFFF` is the single external none sentinel (wire, canonical encoding, stat qualifier); ≤ 65,534 defs per type; never persisted (ADR-0004). Id regex `^[a-z][a-z0-9_]{0,47}$`.
- **`StringId`** = `(namespace = def type, DefIndex)` for content strings; run-time strings use the per-run intern table.
- **Defaults are always neutral** (CD7); `weightClass` required on `ItemDef` and `MailKindDef`; `allowedCategories` omitted = any, present-but-empty = error.
- **No Godot `Resource` holds Sim content.** Nothing on the content path calls `Resource.Duplicate`/`DuplicateDeep`.

### 4. Load pipeline and `ContentSet`
- `ContentLoader.Load(IContentSource) → ContentLoadResult` runs five stages: (1) syntax, (2) schema (fields, types, enums, id lint, units, ranges), (3) build (sort ids, assign `DefIndex`, convert units, content seconds → ticks via Sim Core F1) only if 1–2 are clean, (4) references and cross-def rules, (5) whole-set rules (shares, F5 worst-case stat bounds). All reachable errors are reported; a rule whose declared inputs failed earlier is skipped and counted.
- The result is **one immutable `ContentSet`** (with `SimSettings` and `BalanceTable` as members, def tables as `DefTable<T>` indexed by `DefIndex`) held for the **process lifetime** and passed by reference into `new SimWorld(settings, content, runSeed)`, which only `SimComposition.Build` calls (ADR-0003); systems receive `ContentSet`/`IStatSheet` by constructor injection (ADR-0003) and may cache sub-tables in `readonly` fields. No re-parse at run start.
- **Balance keys:** addressed `owner.key`; a code-side `BalanceKey` registry declares owner, unit and **hard range** (correctness); the data file carries the **safe range** annotation (taste; leaving it is a warning). Every balance key is Sim content and hashed.
- **M-1a `BuiltInContent`:** a code factory that builds the same `ContentSet` types (and runs stages 4–5 over them) so M-1a systems are written against the final types; at M0 the JSONC loader replaces it and a test asserts the shipped JSONC and `BuiltInContent` produce the same canonical encoding for the M-1a subset (then `BuiltInContent` is test-only).
- **Debug hot reload (F6 key):** re-runs the full pipeline over a `DirectorySource`; any error keeps the old set; success replaces the process set and **restarts the run from its seed** (tainted, not recorded). No in-place swap into a running `SimWorld`. Refused while any remote peer is connected. (Key handling and directory resolution are Godot-side, ADR-0019.)

### 5. Validator: one rule registry, one CLI
- Every rule has a permanent id `CV-<AREA>-NN` (never reused; retired rules stay listed), owner GDD, owner alias, severity (Error / Warning), stage and declared inputs. **The registry is code** — `Sim/Content/Rules/<Owner>.cs`, explicit lists, no runtime reflection discovery. `--list-rules --format md` generates `docs/content-rules.md`; CI fails if the checked-in copy differs.
- **Severity:** an Error prevents the `ContentSet` from being built (error screen; hosting disabled; CLI exit 1) and can never be suppressed; a Warning is logged, and CI's `--strict` fails on any warning not waived by a per-def `suppress: [{rule, why}]` (`why` mandatory). A rule a gameplay system relies on for correctness is always an Error.
- **Fixtures:** every rule clause has a violation fixture (minimal valid tree + id-addressed overlay → exactly one diagnostic of that id at that severity; extra ids listed in a reviewed `alsoTrips`) and, where it has a limit, a valid-edge fixture; a meta-test fails if any registered clause lacks one.
- **CLI:** `tools/ContentValidator`, a plain `dotnet` console project referencing the Sim (no Godot), same pipeline as boot over `DirectorySource`; deterministic output order `(path ordinal, line, column, rule id)`; exit codes 0 clean / 1 errors (or warnings under `--strict`) / 2 usage-IO / 3 internal. M0 flags: `--strict`, `--format text|github`, `--print-hash`, `--write-manifest`, `--list-rules --format md`; `--emit-schema` at M1, `--format json` at M4. BalanceSim and SeedViewer load through the same loader.

### 6. Content hash
- `ContentHash = SHA-256(canonical)`, `canonical = hashSchemaVersion u8 ‖ BalanceBlock ‖ DefBlocks` exactly as GDD F6: over **loaded values** (comments, whitespace, line endings, key order and set-valued list order never change it), balance keys in ordinal order as `(u16 len, UTF-8 key, i64 LE)`, def types in a fixed append-only `DefType` order, defs in `DefIndex` order, fields in `FieldSpec` order, fixed-width little-endian, set-valued lists sorted, optional/variant fields with a presence byte.
- **The canonical encoder and a managed SHA-256 (with NIST known-answer tests) both live in the Sim** (current CD25): every peer, the headless host and every tool compute the hash without platform crypto, and Linux needs no system OpenSSL. The SHA-256 is hand-written (~60 lines), so the Sim still has **no `System.Security.Cryptography` reference**, consistent with ADR-0006 (which keeps the *state-dump* SHA-256 in tests only; the content hash is a different, shipped consumer).
- **Join check:** the 32-byte hash is compared once in the handshake, before a peer is admitted and before any `DefIndex`-bearing message (refusal precedence `ContentLoadFailed → VersionMismatch → ContentMismatch → RunInProgress`); the handshake transport is ADR-0011's. Display form = first 4 bytes as 8 lowercase hex. The replay header carries `gameVersion`, `hashSchemaVersion`, `contentHash`.

### 7. StatSheet
- **Identity:** closed `Stat` enum (PascalCase + unit suffix; data spelling snake_case, generated mapping) with a code-side `StatDef` row (unit, scope team/personal/both, qualifier kind, base source, allowed ops, Set resolve min/max-wins, rounding HalfUp/Floor/Ceil, min, max). Design clamps are balance keys; only correctness bounds stay in code. `RuleFlag` closed enum for behaviour switches (on while ≥ 1 live source holds it).
- **Modifiers:** `{sourceKind, sourceId, stackIndex, ownerSlot|Team, stat, qualifier|0xFFFF, op Add|Mul|Set, value, lifetime Run|Shift}` in fixed-capacity pools (`stats.modifierPoolPerTeam/PerPlayer`, `SimSettings`).
- **Resolution (GDD F1), exactly:** if any Set is live, `X = S` (min- or max-wins per `StatDef`); otherwise `N = base × ΠM_i + 1000^k × (ΣA_i + transientAdd)`, `D = 1000^k`, `X = R_r(N, D)` with **one rounding** (HalfUp symmetric on magnitude, true Floor, true Ceil); then **clamp** to `[min_s, max_s]` in Int128 before narrowing to `int`. `k ≤ stats.maxMulFactors` (6) is asserted at build. HalfUp is ADR-0006's half-away-from-zero rounding (`Fix.MulDivRH` semantics, `UInt128` magnitude form, never `Math.Abs`); Floor/Ceil use ADR-0006's `FloorDiv`/`CeilDiv`. Order of pushes never changes a result.
- **Faces:** `IStatSheet` (`Read`, `Explain`) is injectable anywhere; `IStatWriter` (push/remove by source) is injected **only** into Perks & Perk Draft and Stamps & Run Events (reflection test). Meta-progression never pushes modifiers.
- **Derived cache:** one team sheet and one personal sheet per `PlayerSlot`. `StatSheet.Build(content, sources)` is a **pure function** (usable by clients and Presentation without a `SimWorld`); the sheet is rebuilt whenever a source is added or removed (phase boundary or the op that applies a pick), so every read in a tick sees one sheet version. Sheets are not replicated; source lists are (CD15).
- **`Read` cost:** at rebuild, each `(stat, qualifier, slot)` cell caches its resolved value plus the pre-rounding components (`base × P` as Int128, `k`, `ΣA`, Set state). `Read` with `transientAdd = 0` is one array load; with a transient it does one Int128 F1 evaluation from the cached components — never a modifier walk, 0 B allocated. **An Int64 fast path is not built now;** it is added only if CDD-PERF-2 shows `Read` cost matters, and then must be proven bit-identical to the Int128 path by a differential test.
- `Explain(stat, qualifier, slot)` → `{resolved, unclamped (saturated to i64), clampedAt, contributing sources}` for cards and debug; card numbers come only from placeholders resolved against defs and `Explain` (CD27).

### Architecture Diagram

```
 content/**/*.jsonc ──► IContentSource ──► ContentLoader (Sim)                         tools/ContentValidator (CLI)
   (DirectorySource | InMemorySource |      1 syntax ─ 2 schema ─ 3 build ─ 4 refs ─ 5 set      │ same pipeline
    ContentHost [ADR-0019, spike-gated])          │ FieldSpec · DecimalToScaled · Rule registry  │
                                                  ▼                                              ▼
                                   ContentSet (immutable, process lifetime) ──► CanonicalEncoder ──► SHA-256 (Sim)
                                     │ SimSettings · BalanceTable · DefTable<T>[DefIndex]              │
                                     ▼                                                                ▼
                     new SimWorld(settings, content, runSeed)                       Hello{contentHash} (ADR-0011)
                                     │
             StatSheet.Build(content, sources) ──► IStatSheet.Read/Explain (all) · IStatWriter (Perks, Stamps)
```

### Key Interfaces

```csharp
namespace PerformativeMail.Sim.Content;

public interface IContentSource { IEnumerable<string> ListManifest(); ReadOnlyMemory<byte> Read(string path); }
public static class ContentLoader { public static ContentLoadResult Load(IContentSource src); } // diagnostics + ContentSet?
public sealed class ContentSet
{
    public SimSettings Settings { get; } public BalanceTable Balance { get; }
    public DefTable<ItemDef> Items { get; } public DefTable<MailKindDef> MailKinds { get; }
    public DefTable<ContainerDef> Containers { get; } /* … one per DefType … */
    public void WriteCanonical(IBufferWriter<byte> w);   // F6 encoding
}
// DefIndex is ADR-0004 §6's struct (Sim.Primitives): +1 storage, default(DefIndex) = None, Value = ordinal or 0xFFFF for None.
// It is not redeclared here (amended 2026-10-16 before acceptance; the earlier record-struct shape made default = ordinal 0).
public static class ContentHash { public static Hash256 Compute(ContentSet set); } // managed SHA-256 in the Sim
public static class BuiltInContent { public static ContentSet CreateM1a(); }       // M-1a only; test-only after M0

public interface IStatSheet
{
    int Read(Stat stat, ushort qualifier = 0xFFFF, PlayerSlot? slot = null, int transientAdd = 0); // 0-alloc
    StatExplain Explain(Stat stat, ushort qualifier = 0xFFFF, PlayerSlot? slot = null);
    bool Has(RuleFlag flag, PlayerSlot? slot = null);
}
public interface IStatWriter { void Push(in ModifierSource src, ReadOnlySpan<Modifier> mods); void Remove(in ModifierSource src); }
public static class StatSheet { public static StatSheetCache Build(ContentSet c, in StatSources sources); } // pure
```
(Exact parameter shapes are finalised in code review; the `Read` return type is `int` because every v1 stat is int-width after the clamp; architecture.md's `long` sketch is superseded.)

### Implementation Guidelines
- Content parsing **must** use the hand-written `Utf8JsonReader` walk and the DecimalToScaled reader; `JsonSerializer`, `JsonDocument`, `GetDouble`, `GetDecimal`, `double.Parse` and `Enum.Parse` **must never** appear on the content path (analyzer/grep in CI).
- Def types **must** be `sealed record class` (or `readonly record struct` leaves) with no array, float, double, decimal or `Half` members.
- Every sort or compare of ids, keys or paths **must** be ordinal; culture-default compares, `ToLower`/`ToUpper` **must never** be used.
- The Sim **must never** read `res://` or reference Godot; Godot file access lives only in `ContentHost` (ADR-0019).
- `IStatWriter` **must** be constructor-injected only into Perks & Perk Draft and Stamps & Run Events.
- `IStatSheet.Read` **must** allocate 0 B and **must never** iterate modifiers.
- Every validator rule clause **must** ship with a violation fixture; Errors **must never** be suppressible.
- The canonical encoding **must** change `hashSchemaVersion` whenever its layout changes; the shipped set's digest is golden-tested on Windows and Linux CI.

## Alternatives Considered

### Alternative 1: `System.Text.Json` `JsonSerializer` reflection binding
- **Description**: Deserialize files straight into def classes.
- **Pros**: Little code.
- **Cons**: Silently ignores unknown keys, last-wins on duplicates, cannot express "decimal with ≤ 3 fractional digits" exactly, reflection is fragile under trimming.
- **Rejection Reason**: Every one of these is a content bug a designer would ship.

### Alternative 2: Godot `Resource` (`.tres`) defs with `[GlobalClass]`
- **Description**: Author defs in the Godot inspector.
- **Pros**: Editor integration.
- **Cons**: Cannot live in the Godot-free Sim; needs a mirror type; the CI validator could not read it without the engine; noisy diffs; shared-by-path mutation risk.
- **Rejection Reason**: Breaks the Sim/Presentation split (architecture principle 1).

### Alternative 3: YAML
- **Description**: Friendlier authoring syntax.
- **Pros**: Comments, less punctuation.
- **Cons**: Needs a runtime package (SC R33 forbids); implicit typing hazards.
- **Rejection Reason**: JSONC gives comments and trailing commas with a framework parser.

### Alternative 4: Hash the file bytes / SHA-256 from platform crypto
- **Description**: SHA-256 over raw files, or `System.Security.Cryptography.SHA256` host-side.
- **Pros**: Simpler encoder; no hand-written hash.
- **Cons**: File bytes change with comments and line endings (false mismatches); platform crypto on Linux depends on system OpenSSL, and its availability inside a Godot export is NOT SOURCEABLE.
- **Rejection Reason**: Hashing loaded values is the only meaningful identity; a ~60-line managed SHA-256 with NIST vectors removes the platform dependency (this is why spike S3 was retired).

### Alternative 5: StatSheet `base × Σmultipliers + Σadders` (spec 07 §6) / per-modifier rounding
- **Description**: Sum multipliers, or round after each modifier.
- **Pros**: Familiar.
- **Cons**: Additive multipliers mis-stack; per-step rounding makes results depend on push order.
- **Rejection Reason**: F1's product form with one Int128 rounding is order-independent and provably overflow-free for k ≤ 6.

### Alternative 6: Replicate resolved sheets or modifier events
- **Description**: Send sheet values or each modifier change to clients.
- **Pros**: Clients need no build function.
- **Cons**: More messages; a missed event diverges silently.
- **Rejection Reason**: Source lists + pure `Build` converge by construction (CD15).

## Consequences

### Positive
- Two peers with equal hashes have equal defs, balance and `SimSettings`; the Sim stays integer-only and engine-free.
- One loader, one validator and one hash serve the game, the headless host, CI and every tool.
- Designers retune numbers in data only; a new perk targeting existing stats needs no code.
- The StatSheet is a pure, order-independent, 0-alloc read.

### Negative
- Hand-written parser and `FieldSpec` tables are more code than reflection binding, and every new def field needs a `FieldSpec` row.
- A managed SHA-256 is a crypto primitive the project must keep correct (mitigated by NIST vectors).
- Int128 arithmetic is software-emulated; transient reads cost more than cached reads.
- Hot reload restarts the run rather than swapping in place.

## Risks
- **Export packaging (S1) fails for `.jsonc`/manifest.** Mitigation: `EmbeddedResource` fallback behind the same `IContentSource` (loses export-time hot reload only); nothing in the Sim changes.
- **Trimming in desktop export (S2) breaks `System.Text.Json`.** Mitigation: the walk uses no reflection; spike before the first export.
- **The Content & Data GDD is mid-revision** (Provisional). Mitigation: this ADR records the current text; `/propagate-design-change` re-checks it after re-review (Q-ARCH-12).
- **Validator catalogue drift** (rule text in GDDs vs registry). Mitigation: generated `docs/content-rules.md` + CI diff; fixture meta-test.
- **Boot budget overrun** on the min-spec machine. Mitigation: CDD-PERF-1 trend gate; the loader is single-pass and allocation-light.
- *Engine-specialist notes (godot-specialist, 2026-10-16, APPROVE WITH NOTES, no blocking issue):*
  - `Utf8JsonReader`, `Int128` and `ImmutableArray<T>` are .NET 8 shared-framework types; whether the 4.7.1 desktop export runtime matches is UNVERIFIED — S2 must smoke-test all three (and the managed SHA-256 golden digest) in a release export.
  - Set `InvariantGlobalization=true` in the Godot project csproj as well (integer `ToString`/`Parse` in diagnostics is otherwise culture-sensitive) and keep a CI grep for culture-sensitive calls (Q-ARCH-03).
  - Desktop export trimming is UNVERIFIED; the no-reflection walk and compile-time enum tables (generator referenced as a project-level analyzer) are trim-safe either way.
  - `DirectorySource` must never be reachable in a PCK boot; `EmbeddedResource` is only the S1 fallback.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| content-data-definitions.md | CD1–CD2 / TR-content-001, 002, 004: JSONC under `content/`, three data classes, generated manifest | §1 |
| content-data-definitions.md | CD3 / TR-content-003: hand-written `Utf8JsonReader`; duplicate/unknown keys, exponents, `-0` rejected | §2 |
| content-data-definitions.md | CD4, F2 / TR-content-005, 006: unit suffixes, `FieldSpec`, exact decimal → scaled integer | §2 |
| content-data-definitions.md | CD5, F4 / TR-content-007…009 (with ADR-0004): ids, `DefIndex`, `StringId` | §3 |
| content-data-definitions.md | CD6–CD8 / TR-content-010…012: immutable records, neutral defaults, closed enums | §3, §2 |
| content-data-definitions.md | CD9 / TR-content-013: balance keys per owner, `BalanceKey` registry, `SimSettings` in `balance/sim.jsonc` | §4 |
| content-data-definitions.md | CD11–CD20, F1, F5 / TR-content-014…019: `Stat`/`StatDef`, F1 Int128 one rounding, `IStatWriter` scope, derived sheets, `RuleFlag`, `transientAdd`, `Explain` | §7 |
| content-data-definitions.md | CD21 / TR-content-020, 021: 5-stage pipeline → one `ContentSet`; `IContentSource`; Sim never reads `res://` | §4, §1 |
| content-data-definitions.md | CD22–CD24 / TR-content-022, 023: rule registry, severities, fixtures, CLI and exit codes | §5 |
| content-data-definitions.md | CD25, F6, Network 1–3 / TR-content-024, 025, 029: canonical encoding, SHA-256, join check, not security | §6 |
| content-data-definitions.md | CD26 / TR-content-026: hot reload keeps old set on error; reload-and-restart | §4 (Godot key handling → ADR-0019) |
| content-data-definitions.md | CD28 / TR-content-027: `ViewCatalog` keyed by def id → `DefIndex` arrays, placeholder on missing view | Contract only here (keyed by def id, resolved to `DefIndex` arrays at boot, placeholder + warning, release-export error); Godot resource shape → ADR-0019 after S4 |
| content-data-definitions.md | Performance / TR-content-028: boot ≤ 100 ms, bind ≤ 25 ms, rebuild ≤ 0.2 ms, `Read` 0 B | single-pass loader; cached cells; no modifier walk in `Read` |
| content-data-definitions.md | CD1 / TR-content-030: player data owned by Persistence, ids as strings | §1 (data classes); ADR-0024 |
| grid-inventory.md | R1, R3, R15, R16 / TR-inventory-026: `ContainerDef`/`ItemDef` fields; `InventoryRows`, `MailMaxStack`, `WeightPenaltyPm` | defs + `StatDef` rows; `effMax` read at op time (ADR-0009) |
| player-character-movement.md, mail-destinations.md, throwing-catching.md | TR-movement-019/020, TR-mail-018, TR-throwing-019: owner balance files, validator rules, stats | §4 balance keys; §5 registry rows per owner |
| hud-notifications.md | TR-hud-007, 020: cue table validated at load; `hud.tuning` presentation data | Presentation-class data validated through the shared rule library (§5); not hashed |
| simulation-core.md | R27–R28, R33: integer-only, culture-free, Godot-free | §2–§3, §6 |

## Performance Implications
- **CPU**: Boot: one pass per file, then build/validate (target ≤ 100 ms CPU for v1). Run start: bind ≤ 25 ms (no re-parse). Sheet rebuild ≤ 0.2 ms for 8 players × 5 perks. `Read`: one array load (cached) or one Int128 F1 (transient).
- **Memory**: `ContentSet` for v1 ≈ hundreds of KB; sheet cells `|Stat| × qualifiers × 9 sheets` of small structs.
- **Load Time**: Behind the boot loading screen only.
- **Network**: 32 B hash in `Hello`; source-list events of ~8 B per pick; no per-tick cost.

## Migration Plan
No code exists. M-1a: def types, `SimSettings`, `StatSheet`, `BuiltInContent`, canonical encoder and managed SHA-256 with tests. M0: JSONC loader, `FieldSpec` tables, rule registry, `tools/ContentValidator`, manifest generation, CI `--strict`; then `ContentHost` per ADR-0019 after S1/S2/S4.

## Validation Criteria
- Content ACs CDD-LOAD-*, CDD-SCHEMA-*, CDD-STAT-*, CDD-HASH-1…3, CDD-RULE-*, CDD-FT-1 pass in the plain-C# test project; CDD-HASH-3 on both CI OSes.
- Reflection tests: no forbidden member types in defs; `IStatWriter` only in Perks/Stamps constructors.
- Fixture meta-test: every registered rule clause has a tripping fixture.
- CDD-PERF-1/2 reported against baselines (advisory until the reference machine exists).
- At M0: `BuiltInContent` ≡ shipped JSONC for the M-1a subset (canonical bytes equal).

## GDD Impact (not edited here — owners apply at re-review)
- **content-data-definitions.md OQ-CDD-1(e)** asks Sim Core to "confirm the canonical encoder lives in the Sim and SHA-256 outside it", contradicting the same GDD's CD25 (managed SHA-256 *in* the Sim; S3 retired). This ADR follows CD25.
- **architecture.md TR-content-024** ("encoder in Sim, SHA-256 host-side") is superseded by §6: both in the Sim.
- **architecture.md §API Boundaries** `IStatSheet.Read` returns `long` with `int qualifier = 0`: superseded by `int` return and a `ushort` qualifier with `0xFFFF` = none (matches CD13's DefIndex qualifier and `0xFFFF` sentinel).
- **TR-content-027** (`ViewCatalog`): the contract stays here; the Godot implementation moves to ADR-0019 (which already lists `ViewCatalog` in its scope).
- Q-ARCH-07 leftovers (`throwSpreadMrad` example, CV-THR-16 `cooldownSlackTicks`, `foundValueMultRatio` vs `foundValueMultPm`) are field-name issues for the GDD re-review; the registry takes whatever names the re-reviewed GDDs settle.

## Related
- ADR-0003, ADR-0004, ADR-0006 (dependencies); ADR-0009 (inventory reads); ADR-0011 (handshake carries the hash); ADR-0019 (`ContentHost`, `ViewCatalog`, F6 key handling); ADR-0024 (persistence of ids as strings).
- `design/gdd/content-data-definitions.md` (CD1–CD28, F1–F6, Network, spikes S1–S5), `design/gdd/simulation-core.md` R27–R33, `docs/architecture/architecture.md` §Content.
