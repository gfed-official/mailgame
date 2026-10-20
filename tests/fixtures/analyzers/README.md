# SC-ARCH-2 analyzer fixtures

Each `*.cs` file here is one **deliberate violation** of the ADR-0006 section 3 determinism
checklist. They are **never part of a normal build**: the test project globs only
`../unit/**` and `../integration/**`, and the Sim compiles a fixture only when
`-p:PmArchFixture=<file>` is passed.

`tools/ci/check-analyzer-fixtures.sh` compiles each fixture, one at a time, into
`src/PerformativeMail.Sim` and requires the build to fail with the id in the header:

| Header | Meaning |
|--------|---------|
| `// expect: PMSIM001` | custom analyzer `PerformativeMail.Sim.Analyzers` (float, double, decimal, `System.Half`) |
| `// expect: RS0030` | BannedApiAnalyzers, `src/PerformativeMail.Sim/BannedSymbols.txt` |
| `// expect: compiles` | result is only **recorded** (known gaps below) |
| `// covers: <id>` | the `BannedSymbols.txt` entry (verbatim) or `type:float\|double\|decimal\|System.Half` the fixture proves; one id per line |

The script fails when a fixture **compiles**, fails with the wrong id, has no header, or when a
`BannedSymbols.txt` entry / numeric type has no fixture. `check-analyzer-fixtures.sh --self-check`
proves the "fixture compiles" path is detected.

Adding a ban: add the entry to `BannedSymbols.txt`, add a fixture whose `covers:` line repeats it,
and see it fail. Important: an RS0030 member ID **without a parameter list matches only the
parameterless overload**, and generic methods need their arity (`Sort``1(``0[])`). List every
overload.

There is no generator script; the overload lists are maintained by hand. Procedure for a new
ban or a new target framework: list the public overloads of the member in that framework's
reference assemblies (API docs, or `typeof(X).GetMethods()` in a scratch project), write each
as a documentation-comment ID (`{}` for generic type arguments, `` `n `` / ``` ``n ``` for type /
method type parameters), add one fixture per ID, and run the script: a wrong ID shows as
`COMPILED`. The script refuses to run unless the Sim targets exactly `net8.0`, so retargeting
means re-listing the overloads first, then updating that guard.

## Review-enforced gaps (not caught by tooling)

Per ADR-0006 section 3 "What is caught where": rules 2, 3 (until the OQ10 analyzer check), 4, 8, 9
and 10 are enforced by review, SC-DET-1 (story 027) and the static-state reflection test
(story 028). Concretely:

| Gap | Why tooling cannot catch it | Evidence |
|-----|-----------------------------|----------|
| `readonly record struct` in the Sim | Synthesised members (`Equals`, `GetHashCode`, `PrintMembers`) have no syntax, so neither RS0030 nor PMSIM001 visits them; their bodies use `EqualityComparer<T>.Default`. Allowed only for ids that are never ordered or hashed. | `RecordStructCompiles.cs` (recorded: compiles) |
| Declaring a `GetHashCode` override | Declarations are not calls. Mandated for the id types (ADR-0004 Implementation Guidelines). Calls are banned, including calls that bind to an override, and `int`/enum/struct `GetHashCode()` calls. | `OverrideDeclarationCompiles.cs` (recorded: compiles); `BanGetHashCodeOnOverridingType.cs` (rejected) |
| Rule 3: iterating `Dictionary` / `HashSet` (`foreach`, `Keys`, `Values`, LINQ) | No analyzer yet (GDD OQ10). Probe: a `foreach` over a `Dictionary` compiles. | review + SC-DET-1 |
| Rule 8: static mutable state | Not an analyzer rule. | story 028 reflection test |
| Rules 2, 4, 9, 10 (widen before multiply, stable compaction, culture-free text, sorted def ids) | Semantic, not symbol-level. | review + SC-DET-1 |
| Generic `Enum.GetValues<TEnum>()` | Deliberately NOT in `BannedSymbols.txt`: Sim Core already uses it (max defined enum value at construction) and more Core tables will. All other `Enum` Parse/TryParse/GetNames/GetValues(Type)/IsDefined/GetName overloads are banned. | `ContentEnumTests.ContentSources_NeverUseReflectionBasedEnumApis_SourceScan` scans `Content/` (the ban is deferred to simulation-core story 028, tech debt) |
| Overloads added by newer target frameworks | `BannedSymbols.txt` lists the net8.0 overloads; a newer TFM adds overloads that are not banned until listed. | `check-analyzer-fixtures.sh` fails unless the Sim targets exactly `net8.0`; to retarget, regenerate the overload lists first (see "Adding a ban") |
| `async`/`await` syntax itself (ADR rule 7 prose) | The language feature needs no banned symbol. RS0030 flags member access and object creation on `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` (`Task.Run`, `Task.Yield`, `new Task(..)` fail), but an `async ValueTask M(ValueTask v) { await v; }` compiles clean (probe, story-done 2026-10-16). | review (TD: extend PMSIM analyzer to ban these types in signatures) |
| Banned types in signatures only (return types, parameters, fields, locals) | RS0030 reports symbol *uses* (calls, property access, object creation), not type references in declarations. `static Task<int>? F() => null;` and `static ValueTask G() => default;` compile clean (probe, story-done 2026-10-16). Any actual use of the value still hits a banned member. | review (same TD) |
| AC-3 `SceneTree.Paused` grep (`tools/ci/check-no-scenetree-paused.sh`) | The grep matches `SceneTree.Paused` and `GetTree().Paused` only, and skips mentions inside C# comments (`//`, `///`, trailing `//`, `/* */`); it misses a pause through a variable (`var tree = GetTree(); tree.Paused = true;`). It also misses violations hidden behind raw `"""` strings or string literals inside `$"{...}"` interpolation holes: a `//` or `/*` there is read as a comment start and hides real code after it (TD-103). `--self-check` proves both forms are caught in code (also before a trailing comment and after a closed block comment) and not in comment-only mentions. | review |
| `float`/`double` introduced by generated or external code | Generated code is excluded from analysis (`GeneratedCodeAnalysisFlags.None`). | review |

The `Environment.CurrentManagedThreadId` owner-thread assert (ADR-0002) is intentionally **not**
banned and compiles.
