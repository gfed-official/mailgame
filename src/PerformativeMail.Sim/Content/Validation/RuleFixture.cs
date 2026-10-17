using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Validation;

/// <summary>Which side of a rule clause a fixture exercises.</summary>
public enum FixtureKind : byte
{
    /// <summary>Must trip the clause: exactly one diagnostic of the rule's id at the rule's severity.</summary>
    Violation = 1,

    /// <summary>Sits exactly on the clause's limit: no diagnostics at all.</summary>
    ValidEdge = 2,
}

/// <summary>
/// A rule-clause fixture (GDD CD24, ADR-0010 section 5): a minimal valid input plus an id-addressed overlay, packaged
/// as a producer of the validation result. <paramref name="Produce"/> hides how the input is built, so the same
/// mechanism covers fixtures over a built content set (M-1a: a code-built set from story 008's minimal-valid factory
/// with one def or balance value replaced), and, from the M0 CLI, fixtures that load JSONC trees through the loader.
/// </summary>
/// <param name="RuleId">The rule the fixture belongs to.</param>
/// <param name="Clause">The clause name (see <see cref="RuleClause.Name"/>).</param>
/// <param name="Kind">Violation or valid-edge.</param>
/// <param name="Produce">Builds the input and validates it; must be deterministic and free of I/O and clocks.</param>
/// <param name="AlsoTrips">A reviewed list of other rule ids the violation also trips (empty for most fixtures). Each id must either really fire (a stale entry of a registered rule fails the meta-test) or be a named forward reference passed to <see cref="FixtureChecker.Check"/> (a catalogue id whose rule is not registered yet); any other unregistered id is reported.</param>
/// <example>
/// <code>
/// var fixture = new RuleFixture("CV-GRD-01", "max_stack", FixtureKind.Violation,
///     () =&gt; ContentValidationRunner.Run(registry, Overlay(MinimalValid(), "letter", maxStack: 128)),
///     ImmutableArray&lt;string&gt;.Empty);
/// </code>
/// </example>
public sealed record RuleFixture(
    string RuleId,
    string Clause,
    FixtureKind Kind,
    Func<ValidationResult> Produce,
    ImmutableArray<string> AlsoTrips);

/// <summary>What is wrong with a fixture set.</summary>
public enum FixtureProblemKind : byte
{
    /// <summary>An active clause has no violation fixture.</summary>
    MissingViolationFixture = 1,

    /// <summary>An active clause with a limit has no valid-edge fixture.</summary>
    MissingValidEdgeFixture = 2,

    /// <summary>A fixture names a rule the registry does not have, or a retired one.</summary>
    UnknownRule = 3,

    /// <summary>A fixture names a clause the rule does not declare.</summary>
    UnknownClause = 4,

    /// <summary>Two fixtures cover the same (rule, clause, kind).</summary>
    DuplicateFixture = 5,

    /// <summary>A violation fixture did not produce exactly one diagnostic of its rule id at the rule's severity.</summary>
    DidNotTrip = 6,

    /// <summary>A violation fixture also tripped a rule id missing from its reviewed AlsoTrips.</summary>
    UnreviewedExtraRule = 7,

    /// <summary>An AlsoTrips entry that names a registered rule did not fire.</summary>
    StaleAlsoTrips = 8,

    /// <summary>A valid-edge fixture produced a diagnostic.</summary>
    ValidEdgeTripped = 9,

    /// <summary>An AlsoTrips entry names a rule id that is not registered and not in the checker's forward-reference list (a typo, or a rule that never existed).</summary>
    UnregisteredAlsoTrips = 10,
}

/// <summary>One finding of <see cref="FixtureChecker"/>.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="RuleId">The rule concerned.</param>
/// <param name="Clause">The clause concerned (empty when not clause-specific).</param>
/// <param name="Detail">A human-readable explanation.</param>
public sealed record FixtureProblem(FixtureProblemKind Kind, string RuleId, string Clause, string Detail);

/// <summary>
/// The fixture meta-test engine (CDD-VAL-1): iterates the registry (no hard-coded count) and reports every active
/// clause lacking a violation fixture (or, when the clause has a limit, a valid-edge fixture), and every fixture that
/// does not behave. The unit-test project and the M0 CLI both call it. Stateless; it runs each fixture's
/// <see cref="RuleFixture.Produce"/> once per check.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;FixtureProblem&gt; problems = FixtureChecker.Check(registry, fixtures);
/// Assert.Empty(problems);
/// </code>
/// </example>
public static class FixtureChecker
{
    /// <summary>Checks <paramref name="fixtures"/> against <paramref name="registry"/>.</summary>
    /// <param name="registry">The registry whose active rules need fixtures.</param>
    /// <param name="fixtures">The available fixtures.</param>
    /// <param name="forwardReferences">
    /// Catalogue rule ids that are not registered yet and may appear in an AlsoTrips list (for example <c>CV-PRK-05</c>, which
    /// CV-THR-02's fixture lists until the perk rules land at M1). Default: none. An unregistered id outside this list is
    /// <see cref="FixtureProblemKind.UnregisteredAlsoTrips"/>.
    /// </param>
    /// <returns>All problems, in registry order then fixture order; empty when the set is complete and correct.</returns>
    public static ImmutableArray<FixtureProblem> Check(
        RuleRegistry registry, ImmutableArray<RuleFixture> fixtures, ImmutableArray<string> forwardReferences = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ImmutableArray<RuleFixture> all = fixtures.IsDefault ? ImmutableArray<RuleFixture>.Empty : fixtures;
        ImmutableArray<string> forward = forwardReferences.IsDefault ? ImmutableArray<string>.Empty : forwardReferences;
        ImmutableArray<FixtureProblem>.Builder problems = ImmutableArray.CreateBuilder<FixtureProblem>();

        foreach (RuleFixture f in all)
        {
            CheckFixtureTarget(registry, f, problems);
        }

        foreach (RuleSpec rule in registry.Rules)
        {
            if (rule.Status != RuleStatus.Active)
            {
                continue;
            }

            foreach (RuleClause clause in rule.Clauses)
            {
                CheckClause(registry, forward, rule, clause, all, problems);
            }
        }

        return problems.ToImmutable();
    }

    /// <summary>A fixture must point at an active rule and a clause that rule declares.</summary>
    private static void CheckFixtureTarget(RuleRegistry registry, RuleFixture f, ImmutableArray<FixtureProblem>.Builder problems)
    {
        RuleSpec? rule = registry.Find(f.RuleId);
        if (rule is null || rule.Status != RuleStatus.Active)
        {
            problems.Add(new FixtureProblem(FixtureProblemKind.UnknownRule, f.RuleId, f.Clause, "no active rule with this id"));
        }
        else if (!HasClause(rule, f.Clause))
        {
            problems.Add(new FixtureProblem(FixtureProblemKind.UnknownClause, f.RuleId, f.Clause, "the rule declares no such clause"));
        }
    }

    /// <summary>One active clause needs a violation fixture that trips it and, with a limit, a valid-edge fixture that does not.</summary>
    private static void CheckClause(
        RuleRegistry registry, ImmutableArray<string> forward, RuleSpec rule, RuleClause clause, ImmutableArray<RuleFixture> all, ImmutableArray<FixtureProblem>.Builder problems)
    {
        RuleFixture? violation = Single(all, rule.Id, clause.Name, FixtureKind.Violation, problems);
        if (violation is null)
        {
            problems.Add(new FixtureProblem(
                FixtureProblemKind.MissingViolationFixture, rule.Id, clause.Name, "no violation fixture"));
        }
        else
        {
            CheckViolation(registry, forward, rule, clause, violation, problems);
        }

        if (clause.HasLimit)
        {
            CheckValidEdge(rule, clause, Single(all, rule.Id, clause.Name, FixtureKind.ValidEdge, problems), problems);
        }
    }

    private static void CheckValidEdge(
        RuleSpec rule, RuleClause clause, RuleFixture? edge, ImmutableArray<FixtureProblem>.Builder problems)
    {
        if (edge is null)
        {
            problems.Add(new FixtureProblem(
                FixtureProblemKind.MissingValidEdgeFixture, rule.Id, clause.Name, "the clause has a limit but no valid-edge fixture"));
            return;
        }

        ValidationResult result = edge.Produce();
        if (!result.Diagnostics.IsEmpty)
        {
            problems.Add(new FixtureProblem(
                FixtureProblemKind.ValidEdgeTripped, rule.Id, clause.Name, "valid-edge fixture produced " + Ids(result.Diagnostics)));
        }
    }

    private static void CheckViolation(RuleRegistry registry, ImmutableArray<string> forward, RuleSpec rule, RuleClause clause, RuleFixture fixture, ImmutableArray<FixtureProblem>.Builder problems)
    {
        ValidationResult result = fixture.Produce();
        int own = CountOwn(rule, result, out bool severityOk, out List<string> other);
        if (own != 1 || !severityOk)
        {
            problems.Add(new FixtureProblem(
                FixtureProblemKind.DidNotTrip, rule.Id, clause.Name,
                "expected exactly one " + rule.Id + " at the rule's severity, found " +
                own.ToString(System.Globalization.CultureInfo.InvariantCulture) + (severityOk ? string.Empty : " (wrong severity)")));
        }

        ReviewAlsoTrips(registry, forward, rule, clause, fixture, other, problems);
    }

    /// <summary>Counts the rule's own diagnostics, whether all have its severity, and collects the other rule ids that fired.</summary>
    private static int CountOwn(RuleSpec rule, ValidationResult result, out bool severityOk, out List<string> other)
    {
        int own = 0;
        severityOk = true;
        other = new List<string>();
        foreach (ContentDiagnostic d in result.Diagnostics)
        {
            if (string.Equals(d.RuleId, rule.Id, StringComparison.Ordinal))
            {
                own++;
                severityOk &= d.Severity == rule.Severity;
            }
            else if (!other.Contains(d.RuleId))
            {
                other.Add(d.RuleId);
            }
        }

        return own;
    }

    /// <summary>Every other rule that fired must be in AlsoTrips, and every AlsoTrips entry must have fired, unless it is a named forward reference to a rule not registered yet.</summary>
    private static void ReviewAlsoTrips(
        RuleRegistry registry, ImmutableArray<string> forward, RuleSpec rule, RuleClause clause, RuleFixture fixture, List<string> other, ImmutableArray<FixtureProblem>.Builder problems)
    {
        ImmutableArray<string> reviewed = fixture.AlsoTrips.IsDefault ? ImmutableArray<string>.Empty : fixture.AlsoTrips;
        foreach (string id in other)
        {
            if (!reviewed.Contains(id))
            {
                problems.Add(new FixtureProblem(FixtureProblemKind.UnreviewedExtraRule, rule.Id, clause.Name, "also tripped " + id));
            }
        }

        foreach (string id in reviewed)
        {
            if (other.Contains(id))
            {
                continue;
            }

            if (registry.Find(id) is not null)
            {
                problems.Add(new FixtureProblem(FixtureProblemKind.StaleAlsoTrips, rule.Id, clause.Name, "AlsoTrips lists " + id + " which did not fire"));
            }
            else if (!forward.Contains(id))
            {
                problems.Add(new FixtureProblem(FixtureProblemKind.UnregisteredAlsoTrips, rule.Id, clause.Name, "AlsoTrips lists " + id + " which is not a registered rule or a named forward reference"));
            }
        }
    }

    private static RuleFixture? Single(
        ImmutableArray<RuleFixture> all, string ruleId, string clause, FixtureKind kind, ImmutableArray<FixtureProblem>.Builder problems)
    {
        RuleFixture? found = null;
        foreach (RuleFixture f in all)
        {
            if (f.Kind == kind &&
                string.Equals(f.RuleId, ruleId, StringComparison.Ordinal) &&
                string.Equals(f.Clause, clause, StringComparison.Ordinal))
            {
                if (found is not null)
                {
                    problems.Add(new FixtureProblem(FixtureProblemKind.DuplicateFixture, ruleId, clause, "more than one fixture of this kind"));
                    return found;
                }

                found = f;
            }
        }

        return found;
    }

    private static bool HasClause(RuleSpec rule, string clause)
    {
        foreach (RuleClause c in rule.Clauses)
        {
            if (string.Equals(c.Name, clause, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Ids(ImmutableArray<ContentDiagnostic> diagnostics)
    {
        var parts = new List<string>();
        foreach (ContentDiagnostic d in diagnostics)
        {
            parts.Add(d.RuleId);
        }

        return string.Join(", ", parts);
    }
}
