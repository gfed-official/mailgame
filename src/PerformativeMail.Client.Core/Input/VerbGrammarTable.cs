using System.Collections.Immutable;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The v1 interactable target types (GDD R4.2), as the stable string ids the verb-grammar table and Player Character's
/// <c>IInteractable</c> definitions share.
/// </summary>
/// <example><code>bool ok = VerbGrammarTable.V1.TryGet(TargetTypes.Mailbox, out VerbGrammarRow row);</code></example>
public static class TargetTypes
{
    /// <summary>A container (open).</summary>
    public const string Container = "container";

    /// <summary>A vehicle seat (enter).</summary>
    public const string VehicleSeat = "vehicle_seat";

    /// <summary>A turret (mount).</summary>
    public const string Turret = "turret";

    /// <summary>A shop counter (shop).</summary>
    public const string ShopCounter = "shop_counter";

    /// <summary>A console (use).</summary>
    public const string Console = "console";

    /// <summary>A construct (configure).</summary>
    public const string Construct = "construct";

    /// <summary>A world item (pick up).</summary>
    public const string WorldItem = "world_item";

    /// <summary>A mailbox (deliver hold).</summary>
    public const string Mailbox = "mailbox";

    /// <summary>A destination (deliver hold).</summary>
    public const string Destination = "destination";

    /// <summary>An inbound catchable item (catch, held state while armed).</summary>
    public const string InboundCatchable = "inbound_catchable";
}

/// <summary>One target type's single verb and its single grammar.</summary>
/// <param name="TargetType">The target type id (<see cref="TargetTypes"/>).</param>
/// <param name="Verb">The verb id, for example <c>open</c>.</param>
/// <param name="Grammar">The grammar: <see cref="Grammar.Tap"/>, <see cref="Grammar.Hold"/> (deliver) or <see cref="Grammar.HeldState"/> (catch, armed while held).</param>
/// <example><code>var row = new VerbGrammarRow(TargetTypes.Mailbox, "deliver", Grammar.Hold);</code></example>
public readonly record struct VerbGrammarRow(string TargetType, string Verb, Grammar Grammar);

/// <summary>
/// One grammar per binding per target type (GDD R4.2, H-R4-5) as data: each interactable target type declares exactly one
/// verb with one grammar, and Interact (the one target-driven action) takes its grammar from the focused target. Container,
/// vehicle seat, turret, shop counter, console, construct (configure) and WorldItem are Tap; mailbox and destination are a
/// deliver Hold; an inbound catchable item is Catch (held state, armed). A target needing two verbs is split into two
/// interactables or moves its second verb into the panel. Checking Player Character's registered anchors against this table
/// is story 020. Immutable; thread-safe.
/// </summary>
/// <example>
/// <code>
/// ValidationReport report = VerbGrammarTable.Validate(VerbGrammarTable.V1.Rows);   // no errors
/// bool hold = VerbGrammarTable.V1.TryGet(TargetTypes.Mailbox, out VerbGrammarRow r) &amp;&amp; r.Grammar == Grammar.Hold;
/// </code>
/// </example>
public sealed class VerbGrammarTable
{
    /// <summary>The v1 table (R4.2).</summary>
    public static readonly VerbGrammarTable V1 = new(
        [
            new(TargetTypes.Container, "open", Grammar.Tap),
            new(TargetTypes.VehicleSeat, "enter", Grammar.Tap),
            new(TargetTypes.Turret, "mount", Grammar.Tap),
            new(TargetTypes.ShopCounter, "shop", Grammar.Tap),
            new(TargetTypes.Console, "use", Grammar.Tap),
            new(TargetTypes.Construct, "configure", Grammar.Tap),
            new(TargetTypes.WorldItem, "pick_up", Grammar.Tap),
            new(TargetTypes.Mailbox, "deliver", Grammar.Hold),
            new(TargetTypes.Destination, "deliver", Grammar.Hold),
            new(TargetTypes.InboundCatchable, "catch", Grammar.HeldState),
        ]);

    /// <summary>Creates a table (unvalidated; call <see cref="Validate(IReadOnlyList{VerbGrammarRow})"/>).</summary>
    /// <param name="rows">The rows.</param>
    /// <example><code>var table = new VerbGrammarTable([new(TargetTypes.Container, "open", Grammar.Tap)]);</code></example>
    public VerbGrammarTable(ImmutableArray<VerbGrammarRow> rows)
    {
        Rows = rows.IsDefault ? [] : rows;
    }

    /// <summary>The rows, in declaration order.</summary>
    public ImmutableArray<VerbGrammarRow> Rows { get; }

    /// <summary>The row of <paramref name="targetType"/> (the first, if a malformed table has several).</summary>
    /// <param name="targetType">A target type id.</param>
    /// <param name="row">The row when found.</param>
    /// <returns>Whether the target type is declared.</returns>
    /// <example><code>bool ok = VerbGrammarTable.V1.TryGet(TargetTypes.Container, out VerbGrammarRow row);</code></example>
    public bool TryGet(string targetType, out VerbGrammarRow row)
    {
        foreach (VerbGrammarRow r in Rows)
        {
            if (string.Equals(r.TargetType, targetType, StringComparison.Ordinal))
            {
                row = r;
                return true;
            }
        }

        row = default;
        return false;
    }

    /// <summary>
    /// Checks one verb with one grammar per target type: a target type listed twice (two verbs, or one verb twice), an empty
    /// target type or verb, or a grammar other than Tap / Hold / HeldState is a <see cref="ValidationCodes.GrammarTarget"/>
    /// error that names the target type. Allocates; load time and tests only.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The report (no errors when valid).</returns>
    /// <example><code>bool ok = VerbGrammarTable.Validate(rows).IsValid;</code></example>
    public static ValidationReport Validate(IReadOnlyList<VerbGrammarRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var report = new ValidationReport();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
        {
            VerbGrammarRow row = rows[i];
            if (string.IsNullOrWhiteSpace(row.TargetType) || string.IsNullOrWhiteSpace(row.Verb))
            {
                report.Add(Entry(row, "a verb-grammar row needs a target type and a verb", []));
                continue;
            }

            if (row.Grammar is not (Grammar.Tap or Grammar.Hold or Grammar.HeldState))
            {
                report.Add(Entry(row, $"target type '{row.TargetType}' declares grammar {row.Grammar}; a target's verb is Tap, a deliver Hold or a Catch held state", [row.Verb]));
            }

            if (!seen.Add(row.TargetType))
            {
                continue;
            }

            ImmutableArray<string> verbs = VerbsOf(rows, row.TargetType);
            if (verbs.Length > 1)
            {
                report.Add(Entry(row, $"target type '{row.TargetType}' declares {verbs.Length} verbs ({string.Join(", ", verbs)}); it must have exactly one verb with one grammar: split it into two interactables or move the second verb into the panel", verbs));
            }
        }

        return report;
    }

    /// <summary>Instance form of <see cref="Validate(IReadOnlyList{VerbGrammarRow})"/> over <see cref="Rows"/>.</summary>
    /// <returns>The report.</returns>
    /// <example><code>bool ok = VerbGrammarTable.V1.Validate().IsValid;</code></example>
    public ValidationReport Validate() => Validate(Rows);

    private static ImmutableArray<string> VerbsOf(IReadOnlyList<VerbGrammarRow> rows, string targetType)
    {
        var verbs = ImmutableArray.CreateBuilder<string>();
        for (int j = 0; j < rows.Count; j++)
        {
            if (string.Equals(rows[j].TargetType, targetType, StringComparison.Ordinal))
            {
                verbs.Add($"{rows[j].Verb} ({rows[j].Grammar})");
            }
        }

        return verbs.ToImmutable();
    }

    private static ValidationEntry Entry(VerbGrammarRow row, string message, ImmutableArray<string> verbs) =>
        new("interact", "TargetType", message)
        {
            Code = ValidationCodes.GrammarTarget,
            Target = row.TargetType,
            Actions = verbs,
        };
}
