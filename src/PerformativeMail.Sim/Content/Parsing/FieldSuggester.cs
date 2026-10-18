using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The "did you mean" helper for CV-GEN-03 (ADR-0010 section 2): the nearest field name by Levenshtein distance at most
/// <see cref="MaxDistance"/>, ties broken by table order. Ordinal over UTF-16 code units; load-time only. Stateless.
/// </summary>
/// <example>
/// <code>
/// int row = FieldSuggester.Suggest(ItemDefFieldSpec.Rows, "maxStak");   // the maxStack row
/// string text = FieldSuggester.Allowed(ItemDefFieldSpec.Rows, "maxStak");   // "did you mean 'maxStack'?"
/// </code>
/// </example>
public static class FieldSuggester
{
    /// <summary>The largest edit distance that still yields a suggestion.</summary>
    public const int MaxDistance = 2;

    /// <summary>The index of the nearest row within <see cref="MaxDistance"/>, or -1.</summary>
    /// <param name="rows">The table's rows.</param>
    /// <param name="name">The unknown key.</param>
    /// <example><code>int row = FieldSuggester.Suggest(rows, "nmae");</code></example>
    public static int Suggest(ImmutableArray<FieldSpec> rows, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int best = -1;
        int bestDistance = MaxDistance + 1;
        for (int i = 0; i < rows.Length; i++)
        {
            int d = Distance(rows[i].Name, name, bestDistance);
            if (d < bestDistance)
            {
                best = i;
                bestDistance = d;
            }
        }

        return best;
    }

    /// <summary>The <c>Allowed</c> text of a CV-GEN-03 finding: the suggestion, or the field list when there is none.</summary>
    /// <param name="rows">The table's rows.</param>
    /// <param name="name">The unknown key.</param>
    /// <example><code>string s = FieldSuggester.Allowed(rows, "zzz");   // "one of: id, name, ..."</code></example>
    public static string Allowed(ImmutableArray<FieldSpec> rows, string name)
    {
        int row = Suggest(rows, name);
        if (row >= 0)
        {
            return "did you mean '" + rows[row].Name + "'?";
        }

        var names = new string[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            names[i] = rows[i].Name;
        }

        return "one of: " + string.Join(", ", names);
    }

    /// <summary>The Levenshtein distance, or <paramref name="cap"/> as soon as it is clear the distance is at least that.</summary>
    /// <param name="a">One string.</param>
    /// <param name="b">The other string.</param>
    /// <param name="cap">The bound above which the exact distance does not matter.</param>
    /// <example><code>int d = FieldSuggester.Distance("maxStack", "maxStak", 3);   // 1</code></example>
    public static int Distance(string a, string b, int cap)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (Math.Abs(a.Length - b.Length) >= cap)
        {
            return cap;
        }

        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowMin = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin >= cap)
            {
                return cap;
            }

            (previous, current) = (current, previous);
        }

        return Math.Min(previous[b.Length], cap);
    }
}
