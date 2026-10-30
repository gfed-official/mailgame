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
/// <param name="AlsoTrips">A reviewed list of other rule ids the violation also trips (empty for most fixtures). Each id must either really fire (a stale entry of a registered rule fails the meta-test) or be a named forward reference passed to the test project's <c>FixtureChecker.Check</c> (a catalogue id whose rule is not registered yet); any other unregistered id is reported.</param>
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

/// <summary>One finding of the fixture meta-test (<c>FixtureChecker</c>, in the test project).</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="RuleId">The rule concerned.</param>
/// <param name="Clause">The clause concerned (empty when not clause-specific).</param>
/// <param name="Detail">A human-readable explanation.</param>
public sealed record FixtureProblem(FixtureProblemKind Kind, string RuleId, string Clause, string Detail);
