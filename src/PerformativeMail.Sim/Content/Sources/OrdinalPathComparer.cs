namespace PerformativeMail.Sim.Content.Sources;

/// <summary>Orders content paths by ordinal comparison (never culture-aware) for <c>StableSort.Sort</c>.</summary>
/// <example>
/// <code>
/// StableSort.Sort(paths.AsSpan(), scratch, new OrdinalPathComparer());
/// </code>
/// </example>
public readonly struct OrdinalPathComparer : IComparer<string>
{
    /// <inheritdoc />
    public int Compare(string? x, string? y) => string.CompareOrdinal(x, y);
}
