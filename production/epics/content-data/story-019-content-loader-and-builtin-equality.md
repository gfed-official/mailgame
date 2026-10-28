# Story 019: ContentLoader.Load — balance files, five-stage orchestration, shipped JSONC equals BuiltInContent

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day, including authoring `content/**/*.jsonc` for the M-1a set)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-28 (full `/story-readiness` pass against main 51b68cd, docs PR `docs/md005-cd019-readiness`; log `/workspace/repos/logs/cd019-readiness.txt`. **Ready.** Evan 2026-10-28 7:24 AM PT, Mail Lead chat, card 'Content-data 019 and throwing-catching 002': "Approve bumping hashSchemaVersion 1 to 2 with the shares sort and regenerating the golden hash in the same commit" and "Approve all three content-data 019 defaults: real locations and suppress passed through, the balance-file parser, and no new load counter". Applied: manifest stamp 2026-10-16 → 2026-10-17.1 (that revision does not touch the content rules); new AC "Hash schema version" (`ContentHash.HashSchemaVersion` 1 → 2 with the `shares` sort, goldens regenerated in the same commit); the `shares` sort key stated as `MailShare.Kind.Value` ascending; stage-gating wording matched to the runner; field names `sellPrice`/`buyPrice`; the three TD-178 defaults written into the notes (TD-178 closed, Evan approved); `ParseCount` dropped; Engine Notes: ADR-0019 is Accepted; the presentation fixture named; Definition of done lists the twelve TD-176 code comments to relabel). Earlier: 2026-10-28 (docs PR #52, after main 51b68cd: the `shares` half of the Hash-invariance key-order variant is unblocked — Evan 2026-10-28 6:09 AM PT, Mail Lead chat, answering Mail Lead's card: "Order of shares entries must not change the hash: sort them, per ADR-0010"; this story sorts `shares` by key (ordinal) before hashing (TD-075(3)); `DefCanonical.cs` added to the Location list; no other AC change). 2026-10-28 (PR #53 Mail Review r1 B1: the hand-offs from story 018 recorded in Implementation Notes; the `shares` half of the Hash-invariance key-order variant marked blocked on Evan (TD-075(3)); AC text otherwise unchanged). 2026-10-21 08:08 PT (Implementation Notes: `view` required, so the explicit-fields rule is a load requirement for it; time fields stay ms — owner rulings 08:08 PT, ADR-0010 Errata 2026-10-21; no AC changes). 2026-10-21 (Implementation Note: shipped files and clean fixtures spell out `carryByHand`, `acceptsInsert` and `view` — owner ruling on the ADR-0010 defaults, GDD CD10 note 2026-10-21; no AC changes)

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD1, CD2, CD9, CD21, CD25; Loading edge cases)
**Requirement**: `TR-content-020`, `TR-content-004`, `TR-content-013` (file half)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§4 pipeline, §1 layout, §6 hash)
**ADR Decision Summary**:
- **Pipeline.** `ContentLoader.Load(IContentSource) → ContentLoadResult` runs five stages: syntax, schema, build, references, whole-set. It reports every reachable error and skips (and counts) rules whose inputs failed. The output is one immutable `ContentSet`.
- **Layout.** Balance lives in `content/balance/<owner>.jsonc`. Presentation data lives under `content/presentation/` and is not hashed.
- **Equality test.** ADR-0010 schedules a test asserting that the JSONC files and `BuiltInContent` produce identical canonical bytes for the M-1a subset at M0. **The epic pulls it forward to M-1a** (decision logged at /create-epics).

**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: Packaged (PCK) boot, `manifest.txt` and `ContentHost` are deferred: spike S1 and ADR-0019 (Accepted; its implementation is the `godot-host-shell` epic). At M-1a the loader runs over `DirectorySource`/`InMemorySource` only, in tests and tools.

**Control Manifest Rules (Foundation)**:
- Required: `ContentLoader.Load(IContentSource)` runs five stages (syntax, schema, build, references, whole-set), reports all reachable errors, and produces one immutable `ContentSet`. That set is held for the process lifetime and passed by reference to `SimComposition.Build`. (ADR-0010 §4)
- Required: all Sim content is JSONC under `content/`, one directory per def type. Balance goes in `content/balance/<owner>.jsonc` (`SimSettings` keys in `balance/sim.jsonc`). Presentation data under `content/presentation/` is not hashed. (ADR-0010 §1)
- Required: `content/manifest.txt` is never committed. (ADR-0010 §1)
- Forbidden: swapping a new `ContentSet` into a running `SimWorld` in place. (ADR-0010 §4)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [ ] **Shipped files load.** `ContentLoader.Load(new DirectorySource("content/"))` over the shipped M-1a files returns zero Errors and a `ContentSet`. That set's canonical bytes (story 013) are **identical** to `BuiltInContent.CreateM1a()`'s. (Epic DoD equality test; ADR-0010 Validation Criteria, M-1a form)
- [ ] **Stage gating.** A stage-1 syntax error in one file means the failed type is not built, the set is withheld (`ContentSet` is null) and the stage 4–5 runner's skipped (rule, def) pairs name the blocking file (the runner still runs over the `FailedInput`s, `ContentPipeline.cs:128-131`; clean types are still built, per the Stage order note). The fixture file also holds a CV-GRD-01 input, so the result lists at least one skipped pair for it, and a second, clean file of another type is still parsed and built. (CDD-LOAD-3)
- [ ] **Hash invariance.** One fixture is varied by:
  - line endings (LF, CRLF), a BOM, comments, whitespace;
  - key order within an object, **including the MailMix `shares` map**: each shift's `shares` are sorted by `MailShare.Kind.Value` ascending before the canonical encode — this equals ordinal kind-id order because `DefIdAssigner` assigns ordinals in `StringComparer.Ordinal` id order, and the encoder never sees the string key; a `DefIndex.None` share never reaches the encoder because CV-GEN-01 withholds the set — so swapping two `shares` keys does not change the hash. (Evan 2026-10-28 6:09 AM PT, Mail Lead chat, answering Mail Lead's card, TD-075(3): "Order of shares entries must not change the hash: sort them, per ADR-0010". Story 018 hashes `shares` in authored order today; this story adds the sort, and the AC below bumps the schema version with it.)
  - the order of a set-valued list.

  Every variant yields the same `ContentHash`. Changing an ItemDef `sellPrice` 400 → 401 (or a MailKindDef `baseValue`), any balance key, or the presence of an optional field (`buyPrice`; `grid` or `allowedCategories` would also do — `carryByHand`, `acceptsInsert` and `view` are always spelled out, so they are not presence variants) changes it. (CDD-PARSE-1, CDD-HASH-1)
- [ ] **Hash schema version.** `ContentHash.HashSchemaVersion` is bumped 1 → 2 (`ContentHash.cs:29`) in the same commit as the `shares` sort, as ADR-0010 l.158 requires ("must change `hashSchemaVersion` whenever its layout changes"; GDD F6 variables table). In that commit: `GoldenM1aDigest` and `GoldenM1aDisplay` (`ContentHashTests.cs:31, 33`) are regenerated — they change anyway, because `BuiltInContent`'s shift 1 shares are authored (letter, small, medium) while ordinal order is (letter, medium, small) (`BuiltInContent.cs:154-158`); the literal `1` assert at `ContentHashTests.cs:587` uses the constant; the `DefCanonical.cs` doc comments at l.15 ("`Shares` are hashed in authored order, deliberately") and l.129 ("order as authored") say sorted by `Kind.Value`; the canonical bytes of the shipped files and `BuiltInContent.CreateM1a()` still match (first AC). The regenerated digest must match on every CI leg. (Evan 2026-10-28 7:24 AM PT, Mail Lead chat, card 'Content-data 019 and throwing-catching 002': "Approve bumping hashSchemaVersion 1 to 2 with the shares sort and regenerating the golden hash in the same commit"; ADR-0010 l.158, Validation Criteria)
- [ ] **Balance files.**
  - Each `content/balance/<owner>.jsonc` is an object of `key: value` or `key: { value, safe: [min, max] }`.
  - The owner is the file name, so `player.jsonc` holds `player.*`.
  - The story 004 checks apply to parsed files: a key in the wrong owner's file, a key in two files, or an unregistered key each give one CV-GEN-13, and a value outside `safe` gives CV-GEN-20.

  (CDD-SCHEMA-5, file form)
- [ ] **Presentation exclusion.** Files under `content/presentation/` are excluded from Sim loading and from the hash; changing a value there leaves `ContentHash` unchanged. The fixture is an in-memory `InMemorySource` holding a `presentation/hud.tuning.jsonc` file (the CDD-SCHEMA-1 example, `hud.tuning`) beside the M-1a defs — no presentation file is shipped at M-1a (the `content/presentation/` placeholder is added only if a test needs the directory, per Authoring below). No player-data type (MetaProfile, settings, input bindings) appears in any `FieldSpec` set (architecture test). (CDD-SCHEMA-1)

---

## Implementation Notes

*Derived from ADR-0010 §1, §4, §6:*

- **Location:** `src/PerformativeMail.Sim/Content/ContentLoader.cs` and `ContentLoadResult.cs` (diagnostics, skipped count, `ContentSet?`); `src/PerformativeMail.Sim/Content/Hashing/DefCanonical.cs` (the `shares` sort and its doc comments, TD-075(3)); `Content/Hashing/ContentHash.cs` (`HashSchemaVersion` 2); `Content/Parsing/BalanceWalker.cs` (new, TD-178 item 2); edits to `Content/Parsing/Builder.cs`, `BuiltDefs`, `Content/ContentPipeline.cs`, `Content/Validation/ContentValidationInput.cs` and `Content/ContentIdChecks.cs` (TD-178 item 1); `ContentLoader` constructs `SchemaStage`, whose constructor runs `ContentSchema.SelfCheck`. The shipped data is `content/` at the repo root, which is `res://content/` because the Godot project root is the repo root.
- **Path → def type mapping** is a fixed ordinal table (CD2):
  - `items/containers.jsonc` → ContainerDef; other `items/*.jsonc` → ItemDef;
  - `mail/kinds.jsonc` → MailKindDef, `mail/mix.jsonc` → MailMixDef, `mail/destinations.jsonc` → DestinationTypeDef;
  - `balance/*.jsonc` → balance;
  - `presentation/**` → skipped.

  Any other `.jsonc` path is CV-GEN-16, an unknown content file. This is an implementation choice; flag it for the GDD re-review.
- **Stage order.** Run stages 1–2 per file. Run stage 3 per def type only if that type's files are clean. Run stages 4–5 through the story 003 runner, the same one `BuiltInContent` uses.
- **Hand-off from story 018 (story 018 l.110; PR #53 Mail Review r1 B1).**
  - **`suppress` reaches stages 4–5.** Story 018's walker parses each def's `suppress: [{rule, why}]` into `ParsedDef.Suppress`, but `BuiltDefs` does not carry it: `ContentPipeline.cs:92-94` passes `default` and `ContentValidationInput.BuildRecords` (l.89) sets every def's suppress to empty. The loader must pass the parsed entries to the story 003 runner so a loaded def can waive a Warning (never hashed, never on the record). See also TD-063(3) (set-level suppress).
  - **File locations for stage 4–5 findings.** Stage 4–5 findings on loaded defs still carry the synthesised `builtin/<folder>/<id>` locations (`ContentIdChecks.cs:51, 85, 164`; `ContentValidationInput.LocationOf`, l.60), which breaks GDD `content-data-definitions.md:72` ("Every error carries `path:line:column`") until this story replaces them with each def's real `SourceLocation` (stage 1–3 findings already use `path:line`, TD-176 item 1).
  - **The API for both (TD-178 item 1, Approved by Evan 2026-10-28 7:24 AM PT).** `BuiltDefs` gains `ImmutableArray<DefRecord> Records`, one per built def, holding its `DefRef`, its `ParsedDef.Start` and its `ParsedDef.Suppress`; `Builder` fills it. `ContentPipeline.Build(BalanceTable, BuiltDefs, RuleRegistry)` passes the records to `ContentValidationInput` through a new internal parameter; when present, `BuildRecords` uses them instead of the synthesised `builtin/<folder>/<id>` records. `ContentIdChecks.Prepare` takes an optional location lookup for its CV-GEN-01 and CV-GEN-09 findings. `BuiltInContent` and the list overload are unchanged.
  - **`shares` order (TD-075(3), decided) and the schema bump.** MailMix `shares` keep authored order into the canonical bytes today (`DefCanonical.cs:139-145`). Evan 2026-10-28 6:09 AM PT (Mail Lead chat): "Order of shares entries must not change the hash: sort them, per ADR-0010". This story sorts each shift's `shares` by `MailShare.Kind.Value` ascending (= ordinal kind-id order, see the AC) before the canonical encode, in `DefCanonical.cs`. Sorting is an encoding change inside `DefCanonical`, so ADR-0010 l.158 mandates the `hashSchemaVersion` bump: `ContentHash.HashSchemaVersion` 1 → 2, with `GoldenM1aDigest`/`GoldenM1aDisplay` regenerated in the same commit (they change regardless: authored (letter, small, medium) vs ordinal (letter, medium, small), `BuiltInContent.cs:154-158`). Evan 2026-10-28 7:24 AM PT, Mail Lead chat, card 'Content-data 019 and throwing-catching 002': "Approve bumping hashSchemaVersion 1 to 2 with the shares sort and regenerating the golden hash in the same commit". Follow-on, same change: `production/qa/evidence/host-shell-boot-evidence.md:72` quotes the old display digest `018735cb` (`hash=018735cb`) and goes stale — add a dated note there pointing at the new digest (TD-075(5) notes the golden flips on any content change).
  - **SelfCheck at loader construction.** `ContentLoader` must construct `SchemaStage`; its constructor is where every `FieldSpec` table's `SelfCheck` runs (TD-090; TD-176 item 10, Approved by Evan 2026-10-28 6:09 AM PT, Mail Lead chat: "Approve all ten TD-176 defaults for content-data 018").
- **`ContentSet` construction.** Build via the same constructor that `BuiltInContent` uses, so equal inputs give equal canonical bytes.
- **Authoring `content/`.**
  - Author the JSONC files to mirror story 008's values in **authored units**: seconds, metres and ratios as decimals where the key's unit is decimal.
  - Keep `cargo` out (deferred, M0).
  - **Explicit fields (owner ruling 2026-10-21, GDD CD10 note).** Every ItemDef and MailKindDef spells out `carryByHand`, and every ContainerDef spells out `acceptsInsert` and `view`, even when the value equals the default. This applies to the shipped files and to every fixture meant to load clean. `BuiltInContent` sets the same fields explicitly (story 018), so the equality test compares authored values, not defaults. `view` is required (a missing one is CV-GEN-05), so a fixture without it does not load clean.
  - **Time fields (owner ruling 2026-10-21 08:08 PT, ADR-0010 Errata).** `…Seconds` values load as ms and stay ms in the `ContentSet`; the loader adds no tick conversion, so the equality test compares ms values with `BuiltInContent`'s.
  - Comments may cite the owner GDD.
  - Add a `content/presentation/.gitkeep`-style placeholder only if a test needs the directory.
- **Then and later.** `BuiltInContent` stays the M-1a runtime source of truth, per the epic decision. Once this story is Done, the loader is a tested peer. **Switching the boot source to the loader is a `godot-host-shell`/M0 decision, not this story.**
- **Balance files (TD-178 item 2, Approved by Evan 2026-10-28 7:24 AM PT).** No JSON balance parser exists on main (only code-side `BalanceKnob` entries). A new `src/PerformativeMail.Sim/Content/Parsing/BalanceWalker.cs` turns a `TopLevelShape.Object` `SyntaxResult` into `ImmutableArray<BalanceEntry>`: `File` is the path stem, as owner; `Name` is `<owner>.<key>`; `Value` is scaled by the registry row's `BalanceUnit` through `DecimalToScaled`; `Where` is the key's location; `Safe` comes from `safe: [min, max]`. Then `BalanceTable.Build(BalanceKeys.CreateRegistry(), entries, rules)` applies story 004's checks (CV-GEN-13 and CV-GEN-20).
- **No `ContentLoader.ParseCount` (TD-178 item 3, Approved by Evan 2026-10-28 7:24 AM PT).** There is no `ParseCount` on main; story 008's AC 4 was deferred and landed as the test-side `ContentBuildCount` (`tests/unit/sim/Composition/SimCompositionTests.cs:912-915`), which already covers CDD-LOAD-4's M-1a form. A boot-time parse count belongs to `godot-host-shell/007` with `ContentHost`. No new counter: `CounterId` 56 and 57 go to mail-destinations 005.
- **Definition of done (code-comment relabel).** Every code comment reading "Mail Dev default, pending Evan (TD-176)" is relabelled "Approved by Evan 2026-10-28 6:09 AM PT (TD-176)" in this story (it touches `SchemaStage` and the Builder path; Evan, Mail Lead chat: "Approve all ten TD-176 defaults for content-data 018"). The twelve on main 51b68cd: `src/PerformativeMail.Sim/Content/Parsing/Builder.cs:94`, `:129`, `:170`, `:346`; `src/PerformativeMail.Sim/Content/Parsing/SchemaWalker.cs:65`, `:109`, `:517`, `:536`, `:553`; `src/PerformativeMail.Sim/Content/Parsing/SchemaStage.cs:34`; `src/PerformativeMail.Sim/Content/Rules/Throwing.cs:143`; `tests/unit/content/SchemaStageTests.cs:259`. Also in the same change: `HashSchemaVersion`, the golden digest and its display form, the `DefCanonical` doc comments (l.15, l.129), the GDD F6 set-valued list (`content-data-definitions.md`, done in this readiness PR: `shares` added) and the boot-evidence digest note.
- **Ids.** This story allocates no `CounterId`, `SimAssertCode`, `SectionId` or EventKind. Its TD row is TD-178 (closed); mail-destinations 005 holds TD-177, CounterId 56/57, SimAssertCode 85, SectionIds 6 and the 0x0180 block; TD-179 is reserved for Mail Dev's throwing-catching 002 PR; next free after both is TD-180.

---

## Mail Lead planning defaults (TD-178, closed — Approved by Evan)

One batched row per PR (lighter process, decision log 2026-10-23). All three Approved by Evan 2026-10-28 7:24 AM PT, in his own words in the Mail Lead chat, card 'Content-data 019 and throwing-catching 002': "Approve all three content-data 019 defaults: real locations and suppress passed through, the balance-file parser, and no new load counter". None contradicts an Accepted ADR; the `hashSchemaVersion` bump (same card: "Approve bumping hashSchemaVersion 1 to 2 with the shares sort and regenerating the golden hash in the same commit") is what ADR-0010 l.158 requires, not a default.

1. **Suppress and real locations** — `BuiltDefs.Records` (`DefRef`, `ParsedDef.Start`, `ParsedDef.Suppress`) filled by `Builder`, passed through `ContentPipeline.Build(BalanceTable, BuiltDefs, RuleRegistry)` to `ContentValidationInput` (used by `BuildRecords` when present); `ContentIdChecks.Prepare` takes an optional location lookup; `BuiltInContent` and the list overload unchanged.
2. **Balance-file walk** — `Content/Parsing/BalanceWalker.cs` (`TopLevelShape.Object` → `ImmutableArray<BalanceEntry>`, owner = path stem, `Value` scaled through `DecimalToScaled`, `Safe` from `safe: [min, max]`), then `BalanceTable.Build(BalanceKeys.CreateRegistry(), entries, rules)`.
3. **No `ContentLoader.ParseCount`** — CDD-LOAD-4's M-1a form is covered by the test-side `ContentBuildCount`; a boot-time count belongs to `godot-host-shell/007`.

---

## Out of Scope

- M0 (`tools/ContentValidator`): the validator CLI and exit codes (TR-content-023); `manifest.txt` and CV-GEN-10 for PCK boot (TR-content-002).
- `godot-host-shell` epic: `ContentHost`, the Failed/Valid screens, and hot reload (TR-content-026, M0).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY run 2026-10-28 in full mode — M story, full loop: nine gaps, each verified against main 51b68cd's code and fixed above. Log: `/workspace/repos/logs/cd019-readiness.txt`.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/content/ContentLoaderTests.cs` — must exist and pass (the directory exists; the file is new)
- Edited: `tests/unit/content/ContentHashTests.cs` (`GoldenM1aDigest`, `GoldenM1aDisplay` regenerated; l.587 uses the constant)
- Legs: dev loop net10.0 Debug; close net10.0 Debug and net8.0 Release in full; CI all four Sim legs (net10.0/net8.0 × Debug/Release) plus "Game project build (no editor)". Baseline on main 51b68cd is 5,561 tests per leg. The content digest golden changes in this story and must match on every leg.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004, Story 008, Story 010, Story 011, Story 012, Story 013, Story 018
- Unlocks: M0 `tools/ContentValidator`; `godot-host-shell` ContentHost wiring
