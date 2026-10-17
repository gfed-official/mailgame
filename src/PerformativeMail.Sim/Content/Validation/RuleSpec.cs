using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// A stage 4 to 5 rule body. A method (by convention an instance method of a stateless checks class, never a static method group, which the compiler caches in a static field, ADR-0003 section 4) that inspects <paramref name="input"/> and calls
/// <see cref="RuleContext.Report"/> once per finding; the rule never chooses the severity (the registry row does).
/// </summary>
/// <typeparam name="TInput">The input the rule reads (a built content set for real rules).</typeparam>
/// <param name="input">The built input.</param>
/// <param name="context">Collects findings and answers <see cref="RuleContext.IsBlocked"/>.</param>
/// <example>
/// <code>
/// void CheckMax(MySet input, RuleContext ctx)
/// {
///     foreach (var d in input.Defs) { if (d.Value &gt; 10) ctx.Report(d.Ref, d.Where, d.Value.ToString(), "0..10", "my.value"); }
/// }
/// </code>
/// </example>
public delegate void RuleCheck<in TInput>(TInput input, RuleContext context)
    where TInput : class, IValidationInput;

/// <summary>
/// One registry row: a permanent validator rule (GDD CD23, ADR-0010 section 5). Immutable metadata plus, for rules
/// the stage 4 to 5 runner executes, a check. Rules are created with <see cref="Create{TInput}"/> (runner-executed),
/// <see cref="Emitted"/> (diagnostics raised by the loader, builders or the runner's own suppress policy, so the
/// runner has nothing to run) or <see cref="Retire"/> (withdrawn, kept listed). Rows are collected in explicit
/// <c>ImmutableArray</c>s in <c>Content/Rules/&lt;Owner&gt;.cs</c>; there is no reflection discovery.
/// Thread-safe (immutable).
/// </summary>
/// <example>
/// <code>
/// public static ImmutableArray&lt;RuleSpec&gt; Rules =&gt; ImmutableArray.Create(
///     RuleSpec.Create&lt;ContentSet&gt;("CV-THR-12", "throwing-catching", "THROW-CV-12", RuleSeverity.Error,
///         ValidationStage.References, ImmutableArray.Create("throw.catchTapMs"), CheckCatchTap));
/// </code>
/// </example>
public abstract class RuleSpec
{
    private protected RuleSpec(
        string id,
        string owner,
        string alias,
        ImmutableArray<string> alsoKnownAs,
        RuleSeverity severity,
        ValidationStage stage,
        ImmutableArray<string> inputs,
        ImmutableArray<RuleClause> clauses,
        RuleStatus status,
        string retiredInto,
        string deferredReason = "")
    {
        Id = id;
        Owner = owner;
        Alias = alias;
        AlsoKnownAs = alsoKnownAs.IsDefault ? ImmutableArray<string>.Empty : alsoKnownAs;
        Severity = severity;
        Stage = stage;
        Inputs = inputs.IsDefault ? ImmutableArray<string>.Empty : inputs;
        Clauses = clauses.IsDefaultOrEmpty ? ImmutableArray.Create(RuleClause.Main) : clauses;
        Status = status;
        RetiredInto = retiredInto;
        DeferredReason = deferredReason;
    }

    /// <summary>The permanent id <c>CV-&lt;AREA&gt;-NN</c>; never renumbered or reused.</summary>
    public string Id { get; }

    /// <summary>The owner GDD (its file stem, for example <c>throwing-catching</c>) whose rule text is the contract.</summary>
    public string Owner { get; }

    /// <summary>The owner's own code for the rule (<c>THROW-CV-12</c>, <c>PC rule 7</c>); empty when the owner has none. Printed after the id in messages.</summary>
    public string Alias { get; }

    /// <summary>Further spellings that resolve to this rule (for example <c>Throwing rule 14</c> on CV-PLR-06).</summary>
    public ImmutableArray<string> AlsoKnownAs { get; }

    /// <summary>Error or Warning (GDD CD22).</summary>
    public RuleSeverity Severity { get; }

    /// <summary>The stage the rule runs in.</summary>
    public ValidationStage Stage { get; }

    /// <summary>The declared inputs: keys of the fields or files the rule reads. A failure of any of them in an earlier stage skips the rule.</summary>
    public ImmutableArray<string> Inputs { get; }

    /// <summary>The testable clauses; each needs a violation fixture. Never empty.</summary>
    public ImmutableArray<RuleClause> Clauses { get; }

    /// <summary>Active, Retired or Deferred.</summary>
    public RuleStatus Status { get; }

    /// <summary>For a retired rule, the id it was folded into (for example CV-PLR-06 for CV-THR-14); empty otherwise.</summary>
    public string RetiredInto { get; }

    /// <summary>For a <see cref="RuleStatus.Deferred"/> rule, why it cannot run yet (for example "inputs not present at M-1a"); empty otherwise.</summary>
    public string DeferredReason { get; }

    /// <summary>True when the stage 4 to 5 runner has a check to run for this row.</summary>
    public abstract bool HasCheck { get; }

    /// <summary>Runs the check over <paramref name="input"/>. Only the runner calls this.</summary>
    internal abstract void Run(IValidationInput input, RuleContext context);

    /// <summary>True when <paramref name="input"/> is the input type this row's check reads (rows without a check accept anything).</summary>
    internal abstract bool Accepts(IValidationInput input);

    /// <summary>Creates a rule whose check the stage 4 to 5 runner executes.</summary>
    /// <typeparam name="TInput">The input the check reads.</typeparam>
    /// <param name="id">The permanent <c>CV-&lt;AREA&gt;-NN</c> id.</param>
    /// <param name="owner">The owner GDD's file stem.</param>
    /// <param name="alias">The owner's own code, or empty.</param>
    /// <param name="severity">Error or Warning.</param>
    /// <param name="stage">The stage (the runner executes <see cref="ValidationStage.References"/> and <see cref="ValidationStage.WholeSet"/>).</param>
    /// <param name="inputs">The declared input keys.</param>
    /// <param name="check">An instance method group of a stateless checks class (not a static method group; see <see cref="RuleCheck{TInput}"/>).</param>
    /// <param name="clauses">The clauses; default means one clause named <c>main</c>.</param>
    /// <param name="alsoKnownAs">Further alias spellings.</param>
    public static RuleSpec Create<TInput>(
        string id,
        string owner,
        string alias,
        RuleSeverity severity,
        ValidationStage stage,
        ImmutableArray<string> inputs,
        RuleCheck<TInput> check,
        ImmutableArray<RuleClause> clauses = default,
        ImmutableArray<string> alsoKnownAs = default)
        where TInput : class, IValidationInput
    {
        ArgumentNullException.ThrowIfNull(check);
        return new RuleSpec<TInput>(id, owner, alias, alsoKnownAs, severity, stage, inputs, clauses, check);
    }

    /// <summary>
    /// Creates a row for a rule whose findings are raised elsewhere (the loader's parse and schema stages, a builder,
    /// or the runner's suppress policy). It is listed, has fixtures, but the stage 4 to 5 runner has no check for it.
    /// </summary>
    /// <param name="id">The permanent <c>CV-&lt;AREA&gt;-NN</c> id.</param>
    /// <param name="owner">The owner GDD's file stem.</param>
    /// <param name="alias">The owner's own code, or empty.</param>
    /// <param name="severity">Error or Warning.</param>
    /// <param name="stage">The stage the finding is raised in.</param>
    /// <param name="inputs">The declared input keys (usually empty for stages 1 to 3).</param>
    /// <param name="clauses">The clauses; default means one clause named <c>main</c>.</param>
    /// <param name="alsoKnownAs">Further alias spellings.</param>
    public static RuleSpec Emitted(
        string id,
        string owner,
        string alias,
        RuleSeverity severity,
        ValidationStage stage,
        ImmutableArray<string> inputs = default,
        ImmutableArray<RuleClause> clauses = default,
        ImmutableArray<string> alsoKnownAs = default) =>
        new EmittedRuleSpec(id, owner, alias, alsoKnownAs, severity, stage, inputs, clauses, RuleStatus.Active, string.Empty);

    /// <summary>
    /// Creates the row of a rule whose id is reserved but whose inputs do not exist yet. It is listed with its real severity and
    /// stage, never runs, and the fixture meta-test skips it (a deferred rule needs no fixtures until it is implemented).
    /// </summary>
    /// <param name="id">The permanent <c>CV-&lt;AREA&gt;-NN</c> id.</param>
    /// <param name="owner">The owner GDD's file stem.</param>
    /// <param name="alias">The owner's own code, or empty.</param>
    /// <param name="severity">Error or Warning.</param>
    /// <param name="stage">The stage the rule will run in.</param>
    /// <param name="reason">Why it cannot run yet; required, for example <c>inputs not present at M-1a: needs PerkDef</c>.</param>
    /// <example>
    /// <code>
    /// RuleSpec.Defer("CV-PLR-12", "player-character-movement", "PC rule 12", RuleSeverity.Error, ValidationStage.WholeSet,
    ///     "inputs not present at M-1a: needs vehicles");
    /// </code>
    /// </example>
    public static RuleSpec Defer(string id, string owner, string alias, RuleSeverity severity, ValidationStage stage, string reason) =>
        new EmittedRuleSpec(id, owner, alias, default, severity, stage, default, default, RuleStatus.Deferred, string.Empty, reason);

    /// <summary>
    /// Creates the row of a withdrawn rule. It stays listed forever, its id is never reused and it never fires.
    /// Severity and stage are placeholders (<see cref="RuleSeverity.Error"/>, <see cref="ValidationStage.Schema"/>).
    /// </summary>
    /// <param name="id">The retired id.</param>
    /// <param name="owner">The owner GDD's file stem.</param>
    /// <param name="alias">The owner's own code, or empty.</param>
    /// <param name="retiredInto">The id of the rule that replaced it.</param>
    public static RuleSpec Retire(string id, string owner, string alias, string retiredInto) =>
        new EmittedRuleSpec(
            id, owner, alias, default, RuleSeverity.Error, ValidationStage.Schema, default, default, RuleStatus.Retired, retiredInto);
}

/// <summary>A <see cref="RuleSpec"/> with a typed check (see <see cref="RuleSpec.Create{TInput}"/>).</summary>
/// <typeparam name="TInput">The input type the check reads.</typeparam>
public sealed class RuleSpec<TInput> : RuleSpec
    where TInput : class, IValidationInput
{
    private readonly RuleCheck<TInput> _check;

    internal RuleSpec(
        string id,
        string owner,
        string alias,
        ImmutableArray<string> alsoKnownAs,
        RuleSeverity severity,
        ValidationStage stage,
        ImmutableArray<string> inputs,
        ImmutableArray<RuleClause> clauses,
        RuleCheck<TInput> check)
        : base(id, owner, alias, alsoKnownAs, severity, stage, inputs, clauses, RuleStatus.Active, string.Empty)
    {
        _check = check;
    }

    /// <inheritdoc />
    public override bool HasCheck => true;

    /// <inheritdoc />
    internal override bool Accepts(IValidationInput input) => input is TInput;

    /// <inheritdoc />
    internal override void Run(IValidationInput input, RuleContext context)
    {
        // The runner has already verified Accepts(input) for every rule before running any of them.
        _check((TInput)input, context);
    }
}

/// <summary>A registry row with no stage 4 to 5 check: an emitted-elsewhere rule or a retired one.</summary>
internal sealed class EmittedRuleSpec : RuleSpec
{
    internal EmittedRuleSpec(
        string id,
        string owner,
        string alias,
        ImmutableArray<string> alsoKnownAs,
        RuleSeverity severity,
        ValidationStage stage,
        ImmutableArray<string> inputs,
        ImmutableArray<RuleClause> clauses,
        RuleStatus status,
        string retiredInto,
        string deferredReason = "")
        : base(id, owner, alias, alsoKnownAs, severity, stage, inputs, clauses, status, retiredInto, deferredReason)
    {
    }

    public override bool HasCheck => false;

    internal override void Run(IValidationInput input, RuleContext context)
    {
        // Nothing to run: findings for this row are raised by the loader, a builder or the runner's suppress policy.
    }

    internal override bool Accepts(IValidationInput input) => true;
}
