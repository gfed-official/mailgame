using System.Collections.Immutable;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// Assigns <see cref="DefIndex"/> ordinals to one def type's ids: the ordinal of an id is its
/// position in the <see cref="StringComparer.Ordinal"/>-sorted id list (ids are validated ASCII,
/// so this equals UTF-8 byte order and is identical on every platform; no culture-sensitive
/// comparison is ever used). Pure and stateless, hence thread-safe. Runs at load time, not per
/// tick, so it allocates its result.
/// <para>
/// Callers check id uniqueness (CV-GEN-01) and the id regex (CV-GEN-02) first; null/empty ids and
/// duplicates are also SIM_ASSERTed here and, without SIM_ASSERT, reported as
/// <see cref="DefAssignmentStatus.InvalidId"/> / <see cref="DefAssignmentStatus.DuplicateIds"/> with
/// nothing assigned. Precedence when several apply: TooManyDefs (count only, checked first), then
/// InvalidId, then DuplicateIds.
/// </para>
/// </summary>
/// <example>
/// <code>
/// DefAssignment r = DefIdAssigner.Assign(new[] { "small", "cargo", "letter" });
/// // r.SortedIds == ["cargo", "letter", "small"]; r.IndexOfInput[0].Value == 2 ("small")
/// </code>
/// </example>
public static class DefIdAssigner
{
    /// <summary>The most defs one type may have: ordinals 0 to 65 533, as 0xFFFF is None.</summary>
    public const int MaxDefsPerType = 65_534;

    private readonly struct Entry
    {
        public readonly string Id;
        public readonly int InputPos;

        public Entry(string id, int inputPos)
        {
            Id = id;
            InputPos = inputPos;
        }
    }

    // Ordinal id order; ties (duplicates only) fall back to input position so the result is total.
    private readonly struct ByOrdinalId : IComparer<Entry>
    {
        public int Compare(Entry x, Entry y)
        {
            int c = StringComparer.Ordinal.Compare(x.Id, y.Id);
            return c != 0 ? c : x.InputPos.CompareTo(y.InputPos);
        }
    }

    /// <summary>Assigns ordinals to <paramref name="ids"/>.</summary>
    /// <param name="ids">One def type's ids, in any order. Must be non-null, non-empty and unique (SIM_ASSERT).</param>
    /// <returns>The sorted ids and ordinals, or a TooManyDefs / DuplicateIds result carrying the count with nothing assigned.</returns>
    public static DefAssignment Assign(ImmutableArray<string> ids) =>
        Assign(ids.IsDefault ? ReadOnlySpan<string>.Empty : ids.AsSpan());

    /// <summary>Assigns ordinals to <paramref name="ids"/>.</summary>
    /// <param name="ids">One def type's ids, in any order. Must be non-null, non-empty and unique (SIM_ASSERT).</param>
    /// <returns>The sorted ids and ordinals, or a TooManyDefs / DuplicateIds result carrying the count with nothing assigned.</returns>
    public static DefAssignment Assign(ReadOnlySpan<string> ids)
    {
        int n = ids.Length;
        if (n > MaxDefsPerType)
        {
            return Rejected(DefAssignmentStatus.TooManyDefs, n);
        }

        var entries = new Entry[n];
        for (int i = 0; i < n; i++)
        {
            string id = ids[i];
            if (string.IsNullOrEmpty(id))
            {
                SimAssert.Fail(ContentAssertCode.DefIdInvalid);
                return Rejected(DefAssignmentStatus.InvalidId, n);
            }

            entries[i] = new Entry(id, i);
        }

        StableSort.Sort(entries.AsSpan(), new Entry[n].AsSpan(), new ByOrdinalId());

        for (int i = 1; i < n; i++)
        {
            if (StringComparer.Ordinal.Equals(entries[i - 1].Id, entries[i].Id))
            {
                SimAssert.Fail(ContentAssertCode.DefIdsNotUnique);
                return Rejected(DefAssignmentStatus.DuplicateIds, n);
            }
        }

        var sorted = new string[n];
        var indexOfInput = new DefIndex[n];
        for (int ordinal = 0; ordinal < n; ordinal++)
        {
            sorted[ordinal] = entries[ordinal].Id;
            indexOfInput[entries[ordinal].InputPos] = DefIndex.FromValue((ushort)ordinal);
        }

        return new DefAssignment(
            DefAssignmentStatus.Ok,
            n,
            ImmutableCollectionsMarshal.AsImmutableArray(sorted),
            ImmutableCollectionsMarshal.AsImmutableArray(indexOfInput));
    }

    private static DefAssignment Rejected(DefAssignmentStatus status, int count) =>
        new(status, count, ImmutableArray<string>.Empty, ImmutableArray<DefIndex>.Empty);
}
