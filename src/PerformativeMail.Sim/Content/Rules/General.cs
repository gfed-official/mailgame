using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Rules;

/// <summary>
/// The ids of the General rules that the validation framework itself raises (they are not run from a rule body), so
/// the runner and its tests name them without string literals.
/// </summary>
/// <example>
/// <code>
/// bool isSuppressProblem = d.RuleId is GeneralRuleIds.SuppressWithoutWhy or GeneralRuleIds.SuppressBadTarget;
/// </code>
/// </example>
public static class GeneralRuleIds
{
    /// <summary>CV-GEN-05: a required field is missing. At M-1a the clause <c>suppress_why</c> covers a <c>suppress</c> entry without <c>why</c> (or without <c>rule</c>).</summary>
    public const string SuppressWithoutWhy = "CV-GEN-05";

    /// <summary>
    /// CV-GEN-05 under its catalogue meaning, "required field missing": the same id as <see cref="SuppressWithoutWhy"/>.
    /// At M-1a the clause <c>balance_value_missing</c> covers a registered balance key that no file supplied (CD9, no default).
    /// </summary>
    public const string RequiredFieldMissing = "CV-GEN-05";

    /// <summary>
    /// CV-GEN-13: a balance key duplicated across sources, absent from the <c>BalanceKey</c> registry, supplied under a
    /// file that is not its registry owner, or outside its registry hard range (GDD CD9).
    /// </summary>
    public const string BalanceKey = "CV-GEN-13";

    /// <summary>CV-GEN-20 (Warning): a balance value outside its data-file <c>safe</c> range (GDD CD9).</summary>
    public const string BalanceOutsideSafe = "CV-GEN-20";

    /// <summary>
    /// CV-GEN-21 (PROVISIONAL id, pending a GDD catalogue amendment): a <c>suppress</c> entry names an Error rule,
    /// a retired rule or an unknown rule id (GDD CD22 says this "is itself an error" but assigns no id).
    /// </summary>
    public const string SuppressBadTarget = "CV-GEN-21";

    /// <summary>
    /// CV-GEN-15 (F1): a <c>StatDef</c> whose <c>min</c> exceeds its <c>max</c> after its balance-key bounds resolve
    /// (GDD CD12 and the catalogue row; stage 3, Build). Raised by <c>StatDefs.ResolveBounds</c>.
    /// </summary>
    public const string StatBoundsInverted = "CV-GEN-15";

    /// <summary>
    /// CV-GEN-01: an id twice within one def type (stage 3, raised by <c>ContentPipeline</c> before indices are assigned;
    /// neither def is kept), or a reference that does not resolve to a def of the declared type (stage 4, a rule body).
    /// </summary>
    public const string IdsAndReferences = "CV-GEN-01";

    /// <summary>CV-GEN-09: more than 65 534 defs in one type (stage 3, raised by <c>ContentPipeline</c> from the id assigner's count verdict).</summary>
    public const string TooManyDefs = "CV-GEN-09";

    /// <summary>CV-GEN-17: a stat's base (per qualifier) lies outside its own resolved <c>[min, max]</c> (stage 5).</summary>
    public const string StatBaseOutsideBounds = "CV-GEN-17";

    /// <summary>CV-GEN-18: a table the Sim indexes unconditionally is empty (stage 5, one finding per table).</summary>
    public const string EmptyRequiredTable = "CV-GEN-18";

    /// <summary>CV-GEN-06: a number token that is not in the content grammar (exponent, <c>-0</c>, <c>+</c>, a fourth fraction digit, a fraction on an integer field), or a numeric field spec with no unit (story 016).</summary>
    public const string NumberFormat = "CV-GEN-06";

    /// <summary>CV-GEN-07: a duration field outside 0 to 86 400 000 ms (the Sim Core F1 cap, story 016).</summary>
    public const string DurationOverCap = "CV-GEN-07";

    /// <summary>CV-GEN-12: a scaled value outside its field range (the stored <see cref="int"/> range by default). Story 018 reuses it for a list or string over 65 535.</summary>
    public const string ValueOutOfRange = "CV-GEN-12";

    /// <summary>CV-GEN-02: an id that fails the lint <c>^[a-z][a-z0-9_]{0,47}$</c> (a byte scan), or contains a raw escape (stage 2, story 018).</summary>
    public const string IdFormat = "CV-GEN-02";

    /// <summary>CV-GEN-03: a field the def type's table does not declare (stage 2, story 018); the message suggests the nearest field within edit distance 2. <c>$schema</c> and <c>suppress</c> are reserved.</summary>
    public const string UnknownField = "CV-GEN-03";

    /// <summary>CV-GEN-08: an enum value that is not exactly one of the set's data spellings (stage 2, story 018); no numeric or case-insensitive fallback.</summary>
    public const string EnumSpelling = "CV-GEN-08";

    /// <summary>CV-GEN-14: a field present where the def's variant forbids it, for example <c>grid</c> on a <c>pile</c> container (stage 2, story 018).</summary>
    public const string VariantField = "CV-GEN-14";

    /// <summary>CV-GEN-04: a key repeated within one JSON object (stage 1, raised by <c>SyntaxWalker</c> at the duplicate key's own position; GDD CD3.3).</summary>
    public const string DuplicateKey = "CV-GEN-04";

    /// <summary>CV-GEN-10: a path the manifest lists that the content source cannot read (stage 1, raised by <c>SyntaxStage</c>).</summary>
    public const string ManifestFile = "CV-GEN-10";

    /// <summary>CV-GEN-16: a file that is not well-formed UTF-8 / JSON of the expected top-level shape (stage 1, raised by <c>SyntaxWalker</c>; fail-fast, one per file).</summary>
    public const string SyntaxShape = "CV-GEN-16";
}

/// <summary>
/// The General (Content &amp; Data) rule table, owner <c>content-data-definitions</c> (CV-GEN). It registers the rows the
/// validation framework raises itself (CV-GEN-05, 13, 15, 20, 21), the stage 1 syntax rows of story 017 (CV-GEN-04, 10, 16), the
/// stage 2 schema rows of story 018 (CV-GEN-02, 03, 06, 07, 08, 12, 14, plus the <c>required_field</c> clause of CV-GEN-05; all
/// emitted by the schema walker) and the set-level rules of story 009 (CV-GEN-01, 09, 17,
/// 18, whose bodies are in <c>GeneralSetChecks</c>); the remaining CV-GEN rows are registered, each with its clause
/// fixtures, by the stories that implement them (019, loader rules). Add a row here and append a clause to an existing row; never renumber or reuse an id.
/// </summary>
/// <example>
/// <code>
/// RuleSpec? gen05 = RuleRegistry.CreateDefault().Find("CV-GEN-05");   // Severity Error, stage Schema
/// </code>
/// </example>
public static class General
{
    private const string Owner = "content-data-definitions";

    /// <summary>The General rows, in registration order. A property that builds the array on each call, so the class holds no static state (ADR-0003 section 4).</summary>
    public static ImmutableArray<RuleSpec> Rules => Table(new GeneralSetChecks());

    private static ImmutableArray<RuleSpec> Table(GeneralSetChecks checks) => ImmutableArray.Create(
        // Story 017 (stage 1: SyntaxWalker and SyntaxStage raise these).
        RuleSpec.Emitted(
            GeneralRuleIds.DuplicateKey,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Syntax,
            clauses: ImmutableArray.Create(new RuleClause("duplicate_key"))),
        RuleSpec.Emitted(
            GeneralRuleIds.ManifestFile,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Syntax,
            clauses: ImmutableArray.Create(new RuleClause("listed_file_missing"))),
        RuleSpec.Emitted(
            GeneralRuleIds.SyntaxShape,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Syntax,
            clauses: ImmutableArray.Create(
                new RuleClause("invalid_utf8"),
                new RuleClause("bom_not_at_start"),
                new RuleClause("no_json_value"),
                new RuleClause("nesting_too_deep", HasLimit: true),
                new RuleClause("top_level_shape"),
                new RuleClause("invalid_json"))),
        // Catalogue row: "Required field missing". Story 018 added the clause "required_field" (a def lacking a required field,
        // or a variant-required one); story 004 uses the same id for a registered balance key with no value.
        RuleSpec.Emitted(
            GeneralRuleIds.SuppressWithoutWhy,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(
                new RuleClause("suppress_why"), new RuleClause("balance_value_missing"), new RuleClause("required_field"))),
        RuleSpec.Emitted(
            GeneralRuleIds.SuppressBadTarget,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(new RuleClause("names_error_rule"), new RuleClause("names_unknown_rule"))),
        // Story 018 (stage 2: the schema walker raises these; no rule body). One finding per mistake.
        RuleSpec.Emitted(
            GeneralRuleIds.IdFormat,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(new RuleClause("id_regex"), new RuleClause("id_escape"))),
        RuleSpec.Emitted(
            GeneralRuleIds.UnknownField,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(new RuleClause("unknown_field"))),
        RuleSpec.Emitted(
            GeneralRuleIds.NumberFormat,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(
                new RuleClause("integer_dot"),
                new RuleClause("fraction_digits"),
                new RuleClause("exponent"),
                new RuleClause("negative_zero"),
                new RuleClause("no_unit"))),
        RuleSpec.Emitted(
            GeneralRuleIds.DurationOverCap,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(new RuleClause("over_cap", HasLimit: true))),
        RuleSpec.Emitted(
            GeneralRuleIds.EnumSpelling,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(new RuleClause("unknown_spelling"))),
        RuleSpec.Emitted(
            GeneralRuleIds.ValueOutOfRange,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(
                new RuleClause("storage_width", HasLimit: true),
                new RuleClause("list_too_long", HasLimit: true),
                new RuleClause("string_too_long", HasLimit: true))),
        RuleSpec.Emitted(
            GeneralRuleIds.VariantField,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(new RuleClause("forbidden_for_variant"))),
        // Story 004 (BalanceTable.Build raises these; one finding per mistake).
        RuleSpec.Emitted(
            GeneralRuleIds.BalanceKey,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(
                new RuleClause("duplicate_key"),
                new RuleClause("unregistered_key"),
                new RuleClause("wrong_owner_file"),
                new RuleClause("outside_hard_range", HasLimit: true))),
        RuleSpec.Emitted(
            GeneralRuleIds.BalanceOutsideSafe,
            Owner,
            string.Empty,
            RuleSeverity.Warning,
            ValidationStage.Schema,
            clauses: ImmutableArray.Create(new RuleClause("outside_safe_range", HasLimit: true))),
        // Story 006 (StatDefs.ResolveBounds raises it; one finding per stat). Catalogue row: stage 3.
        RuleSpec.Emitted(
            GeneralRuleIds.StatBoundsInverted,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Build,
            clauses: ImmutableArray.Create(new RuleClause("min_above_max", HasLimit: true))),
        // Story 009. CV-GEN-01 is registered at stage 4 (its reference clauses run in the runner); its duplicate_id clause is
        // raised at the start of stage 3 by ContentPipeline (the catalogue says 3-4). The rule body reads only the tables
        // and pending references, and is skipped when a table it reads failed stage 3 (CV-GEN-09).
        RuleSpec.Create<ContentValidationInput>(
            GeneralRuleIds.IdsAndReferences,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.References,
            ImmutableArray.Create(ContentInputKeys.MailKinds, ContentInputKeys.DestinationTypes, ContentInputKeys.MailMixes),
            checks.CheckReferences,
            ImmutableArray.Create(
                new RuleClause("duplicate_id"),
                new RuleClause("unresolved_reference"),
                new RuleClause("wrong_type_reference"),
                new RuleClause("unresolved_index"))),
        // Story 009 (ContentPipeline raises it from the id assigner's count verdict). Catalogue stage 3.
        RuleSpec.Emitted(
            GeneralRuleIds.TooManyDefs,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.Build,
            clauses: ImmutableArray.Create(new RuleClause("too_many_defs", HasLimit: true))),
        // Story 009. Reads the container and mail kind tables (and the resolved stat bounds); a stat whose bounds are
        // inverted (CV-GEN-15) is skipped inside the body, because the runner skips by whole rule when a table failed.
        RuleSpec.Create<ContentValidationInput>(
            GeneralRuleIds.StatBaseOutsideBounds,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.Containers, ContentInputKeys.MailKinds),
            checks.CheckStatBases,
            ImmutableArray.Create(
                new RuleClause("container_rows", HasLimit: true),
                new RuleClause("mail_kind_max_stack", HasLimit: true),
                new RuleClause("stat_base", HasLimit: true))),
        // Story 009. One finding per empty table of the four the Sim indexes unconditionally; an empty ItemDef table is valid.
        RuleSpec.Create<ContentValidationInput>(
            GeneralRuleIds.EmptyRequiredTable,
            Owner,
            string.Empty,
            RuleSeverity.Error,
            ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.MailKinds, ContentInputKeys.MailMixes, ContentInputKeys.DestinationTypes, ContentInputKeys.Containers),
            checks.CheckRequiredTables,
            ImmutableArray.Create(new RuleClause("empty_table", HasLimit: true))));
}
