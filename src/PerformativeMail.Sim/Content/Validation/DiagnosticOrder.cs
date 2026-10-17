using System.Collections.Immutable;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// The total order on diagnostics (GDD CD24): path (ordinal), then line, then column, then rule id (ordinal). Ties
/// beyond that (same place, same rule) break on def type, def id, value, allowed and input key, all ordinal, so two
/// runs of the same input always print the same bytes whatever order the rules reported in. No culture-sensitive
/// comparison is used. Stateless and thread-safe.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;ContentDiagnostic&gt; sorted = DiagnosticOrder.Sorted(found);
/// </code>
/// </example>
public readonly struct DiagnosticComparer : IComparer<ContentDiagnostic>
{
    /// <summary>Compares two diagnostics by the documented order.</summary>
    public int Compare(ContentDiagnostic? x, ContentDiagnostic? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int c = string.CompareOrdinal(x.Location.Path, y.Location.Path);
        if (c != 0)
        {
            return c;
        }

        c = x.Location.Line.CompareTo(y.Location.Line);
        if (c != 0)
        {
            return c;
        }

        c = x.Location.Column.CompareTo(y.Location.Column);
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(x.RuleId, y.RuleId);
        if (c != 0)
        {
            return c;
        }

        c = DefRef.Compare(x.Def, y.Def);
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(x.Value, y.Value);
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(x.Allowed, y.Allowed);
        return c != 0 ? c : string.CompareOrdinal(x.InputKey, y.InputKey);
    }
}

/// <summary>Sorting helpers for diagnostics, built on <see cref="StableSort"/> (the ADR-0006 rule 5 sort).</summary>
public static class DiagnosticOrder
{
    /// <summary>Sorts <paramref name="diagnostics"/> in place by <see cref="DiagnosticComparer"/>.</summary>
    /// <param name="diagnostics">The diagnostics to order.</param>
    public static void Sort(Span<ContentDiagnostic> diagnostics)
    {
        if (diagnostics.Length < 2)
        {
            return;
        }

        var scratch = new ContentDiagnostic[diagnostics.Length];
        StableSort.Sort(diagnostics, scratch, new DiagnosticComparer());
    }

    /// <summary>Returns a sorted copy of <paramref name="diagnostics"/>; a default array reads as empty.</summary>
    /// <param name="diagnostics">The diagnostics to order.</param>
    public static ImmutableArray<ContentDiagnostic> Sorted(ImmutableArray<ContentDiagnostic> diagnostics)
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            return ImmutableArray<ContentDiagnostic>.Empty;
        }

        ContentDiagnostic[] copy = diagnostics.AsSpan().ToArray();
        Sort(copy);
        return ImmutableArray.Create(copy);
    }
}
