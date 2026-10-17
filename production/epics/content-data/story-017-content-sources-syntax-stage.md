# Story 017: IContentSource, InMemory/Directory sources and the stage-1 syntax walk

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: In Progress
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-17

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD2, CD3.1–3.3, CD3.5, CD21; Loading & parsing edge cases)
**Requirement**: `TR-content-001`, `TR-content-003`, `TR-content-021`
*(No `docs/architecture/tr-registry.yaml` exists yet. Requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§1 Sources, §2 Parser)
**ADR Decision Summary**:
- **Sources.** The loader reads only through `IContentSource` (`ListManifest()`, `Read(path) → ReadOnlyMemory<byte>`). `DirectorySource` and `InMemorySource` live in `Sim/Content/Sources/` and use plain `System.IO`. The Sim never reads `res://`.
- **Parsing** is one forward `Utf8JsonReader` pass per file over bytes:
  - the BOM is stripped, CR and LF are both whitespace, comments are skipped and trailing commas are allowed;
  - duplicate keys are errors;
  - no `JsonSerializer`, `JsonDocument` or reflection.

**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**:
- `Utf8JsonReader` is a .NET shared-framework type. Its behaviour in a 4.7.1 release export is spike S2, deferred to `ContentHost`.
- Godot's `ContentHost : IContentSource` belongs to ADR-0019 (not written). It is out of scope; this story keeps the seam Godot-free.

**Control Manifest Rules (Foundation)**:
- Required: the loader reads only through `IContentSource`. `DirectorySource` and `InMemorySource` live in `Sim/Content/Sources/` (plain `System.IO`). (ADR-0010 §1)
- Required: a hand-written forward `Utf8JsonReader` walk. BOM stripped, comments skipped, trailing commas allowed. Duplicate keys are errors. (ADR-0010 §2)
- Required: every diagnostic carries `path:line:column` (1-based UTF-8 byte column). (ADR-0010 §2)
- Forbidden: `JsonSerializer`, `JsonDocument`, `GetDouble`, `GetDecimal`, `double.Parse`, `Enum.Parse`, or runtime reflection on the content path. (ADR-0010 Alt 1)
- Forbidden: `Assembly.Location` or assembly-relative paths. Never read `res://` from the Sim. (ADR-0001, ADR-0010 §1)
- Forbidden: culture-default compares or `ToLower`/`ToUpper` on ids, keys or paths. (ADR-0010)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [ ] One def file saved five ways produces an identical token stream and identical parsed values: with LF, with CRLF, with a leading BOM, with `//` comments, and with trailing commas. (The equal-hash form is in story 019.) A listed-but-missing file gives CV-GEN-10, and a 0-byte file gives CV-GEN-16. (CDD-PARSE-1, syntax half)
- [ ] **CV-GEN-16** is reported, with its byte position, for each of these:
  - invalid UTF-8;
  - a BOM other than at offset 0;
  - a comment-only file;
  - nesting deeper than 32 (`MaxDepth` 32);
  - a def file whose top level is not an array, or a balance file whose top level is not an object;
  - `5.` and `+5` (invalid JSON).

  `[]` is a valid empty file. A syntax error ends that file only, and every other file is still parsed. (Loading edge cases; CDD-PARSE-3, CV-GEN-16 half)
- [ ] `{"id":"a","id":"b"}` starting at line 7, column 14 gives exactly one CV-GEN-04. It reports `path:7:14` (LF lines, 1-based UTF-8 byte column, tab counts 1), the offending value and the allowed form. The keys `$schema` and `suppress` are accepted. (CDD-PARSE-2, duplicate-key half)
- [ ] `DirectorySource` lists only `*.jsonc` files under the content root, with `/`-separated relative paths, ordinal-sorted. `InMemorySource` serves fixture bytes by path. Neither type references Godot, and no Sim type contains the string `res://`. (CD2, TR-content-001/021; architecture test)
- [ ] **CDD-PARSE-4, parser half.** `BannedSymbols.txt` bans these for the Sim (and later for `tools/`):
  - `JsonSerializer`, `JsonDocument`, `Utf8JsonReader.GetDouble`/`GetDecimal`/`GetSingle`, `double.Parse`;
  - culture-default `string.Compare`/`CompareTo`/`StartsWith`/`EndsWith` overloads, and `ToLower`/`ToUpper`.

  A seeded use of each fails the build. The Sim csproj sets `InvariantGlobalization=true`.

---

## Implementation Notes

*Derived from ADR-0010 §1–§2 and GDD CD3:*

- **Location:** `src/PerformativeMail.Sim/Content/Sources/` (`IContentSource`, `DirectorySource`, `InMemorySource`) and `src/PerformativeMail.Sim/Content/Parsing/` (`SyntaxWalker`, `TextPosition`).
- **The walker.** Build `Utf8JsonReader` with `JsonReaderOptions { CommentHandling = Skip, AllowTrailingCommas = true, MaxDepth = 32 }` over the bytes after the loader strips a BOM at offset 0.
  - Stage 1 tracks, per object, the set of keys seen. Use a small pooled list with ordinal byte compare and no `Dictionary` iteration.
  - It records each value's start offset in a side table, so later stages can attach `line:column` to fields.
  - Positions come from byte offsets. Line = count of `\n` before the offset, plus 1. Column = bytes since the last `\n`, plus 1.
  - `Utf8JsonReader` is fail-fast: catch its `JsonException` and map it to CV-GEN-16 at the failing position. One syntax error per file at most.
- **Missing vs empty.** A `Read` that cannot find the path must be distinguishable from an empty file. Give `IContentSource.Read` a "missing" signal (a `TryRead` overload or a sentinel result), so a missing file is never reported as an empty one.
- **No manifest.** `content/manifest.txt` is generated at export and is M0 (TR-content-002). `DirectorySource` enumerates and sorts the directory itself.
- **Culture safety.** Add `InvariantGlobalization` to `PerformativeMail.Sim.csproj` if the simulation-core scaffold has not already added it. The Godot game csproj gets the same setting in `godot-host-shell` (ADR-0010 engine-specialist note).

---

## Out of Scope

- Story 018: unknown fields (CV-GEN-03), required fields, enums, ids and units.
- Story 019: balance file semantics and the full `ContentLoader.Load`.
- `godot-host-shell` epic, ADR-0019 not written: `ContentHost`.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/SyntaxStageTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (diagnostics). Also `production/epics/simulation-core/story-004-determinism-analyzers-sc-arch-2.md` (BannedApiAnalyzers).
- Unlocks: Story 018, Story 019
