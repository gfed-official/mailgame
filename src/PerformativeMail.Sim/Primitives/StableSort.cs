namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// The only sanctioned sort for ordering-sensitive Sim data (ADR-0006 section 3 rule 5):
/// <c>Array.Sort</c>, <c>List.Sort</c>, <c>MemoryExtensions.Sort</c> and LINQ <c>OrderBy</c> are
/// banned. A stable insertion sort handles runs of up to 16 elements and a bottom-up merge sort
/// combines them, over a caller-provided scratch span. It allocates nothing (0 B), uses no
/// delegates, boxing, recursion or hash codes, and its result depends only on the data and the
/// comparer, so it is bit-identical on every platform.
/// <para>
/// RULE 5 TIE-BREAK REQUIREMENT: although this sort is stable, every comparer used for
/// ordering-sensitive Sim data MUST end with a total tie-break on an <see cref="EntityId"/> or
/// <c>opSeq</c> (or another unique, deterministic key). Stability only preserves the INPUT order
/// of equal elements, and that input order may itself come from a non-canonical source. The sort
/// cannot enforce this; reviewers and tests must.
/// </para>
/// Not thread-safe with respect to the spans passed in; the type itself is stateless.
/// </summary>
/// <example>
/// <code>
/// readonly struct ByDueThenId : IComparer&lt;Job&gt;
/// {
///     public int Compare(Job x, Job y) =&gt; x.Due != y.Due ? x.Due.CompareTo(y.Due) : x.Id.CompareTo(y.Id);
/// }
///
/// // _jobs and _scratch are preallocated once; _scratch.Length &gt;= _jobs.Length.
/// StableSort.Sort(_jobs.AsSpan(0, count), _scratch, new ByDueThenId());
/// </code>
/// </example>
public static class StableSort
{
    /// <summary>Runs of at most this many elements are sorted by insertion sort before merging.</summary>
    public const int InsertionThreshold = 16;

    /// <summary>
    /// Sorts <paramref name="data"/> ascending by <paramref name="comparer"/>, stably. Elements for
    /// which <c>Compare</c> returns 0 keep their input order. See the type remarks for the
    /// mandatory tie-break rule.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TCmp">A comparer struct (prefer <c>readonly struct</c>, which avoids defensive copies); a struct constraint keeps the call free of boxing and delegates.</typeparam>
    /// <param name="data">The elements to sort, in place. Length 0 or 1 is a no-op.</param>
    /// <param name="scratch">Caller-provided working space. Its length must be at least <c>data.Length</c> (checked even for tiny inputs so a too-small buffer fails in testing, not later). Contents on entry are ignored and unspecified on return. Must not overlap <paramref name="data"/>.</param>
    /// <param name="comparer">The ordering; must be a consistent strict weak ordering.</param>
    /// <exception cref="ArgumentException"><paramref name="scratch"/> is shorter than <paramref name="data"/> or overlaps it. Nothing is modified in that case.</exception>
    public static void Sort<T, TCmp>(Span<T> data, Span<T> scratch, in TCmp comparer)
        where TCmp : struct, IComparer<T>
    {
        int n = data.Length;
        ValidateScratch(data, scratch);
        if (n < 2)
        {
            return;
        }

        // Phase 1: stable insertion sort of each run of InsertionThreshold elements.
        for (int lo = 0; lo < n; lo += InsertionThreshold)
        {
            int length = Math.Min(InsertionThreshold, n - lo);
            InsertionSort(data.Slice(lo, length), in comparer);
        }

        if (n > InsertionThreshold)
        {
            MergeRuns(data, scratch.Slice(0, n), in comparer);
        }
    }

    private static void ValidateScratch<T>(Span<T> data, Span<T> scratch)
    {
        if (scratch.Length < data.Length)
        {
            throw new ArgumentException("StableSort scratch must be at least as long as the data.", nameof(scratch));
        }

        if (data.Length >= 2 && data.Overlaps(scratch))
        {
            throw new ArgumentException("StableSort scratch must not overlap the data.", nameof(scratch));
        }
    }

    /// <summary>Bottom-up merge of sorted runs of InsertionThreshold, ping-ponging between data and scratch.</summary>
    private static void MergeRuns<T, TCmp>(Span<T> data, Span<T> scratch, in TCmp comparer)
        where TCmp : struct, IComparer<T>
    {
        int n = data.Length;
        Span<T> src = data;
        Span<T> dst = scratch;
        bool resultInData = true;
        int width = InsertionThreshold;
        while (width < n)
        {
            MergePass(src, dst, width, in comparer);
            Span<T> swap = src;
            src = dst;
            dst = swap;
            resultInData = !resultInData;

            // Doubling would overflow int only beyond 2^30 elements; once width > n / 2 one pass finished the sort.
            width = width > n / 2 ? n : width << 1;
        }

        if (!resultInData)
        {
            src.CopyTo(data);
        }
    }

    private static void MergePass<T, TCmp>(Span<T> src, Span<T> dst, int width, in TCmp comparer)
        where TCmp : struct, IComparer<T>
    {
        int n = src.Length;
        int lo = 0;
        while (lo < n)
        {
            int mid = n - lo > width ? lo + width : n;
            int hi = n - mid > width ? mid + width : n;
            Merge(src, dst, lo, mid, hi, in comparer);
            lo = hi;
        }
    }

    private static void InsertionSort<T, TCmp>(Span<T> run, in TCmp comparer)
        where TCmp : struct, IComparer<T>
    {
        for (int i = 1; i < run.Length; i++)
        {
            T item = run[i];
            int j = i - 1;

            // Strictly greater only: equal elements are never moved past each other (stability).
            while (j >= 0 && comparer.Compare(run[j], item) > 0)
            {
                run[j + 1] = run[j];
                j--;
            }

            run[j + 1] = item;
        }
    }

    private static void Merge<T, TCmp>(Span<T> src, Span<T> dst, int lo, int mid, int hi, in TCmp comparer)
        where TCmp : struct, IComparer<T>
    {
        int left = lo;
        int right = mid;
        int k = lo;
        while (left < mid && right < hi)
        {
            // Take from the right run only when strictly smaller: ties come from the left (stability).
            if (comparer.Compare(src[right], src[left]) < 0)
            {
                dst[k++] = src[right++];
            }
            else
            {
                dst[k++] = src[left++];
            }
        }

        while (left < mid)
        {
            dst[k++] = src[left++];
        }

        while (right < hi)
        {
            dst[k++] = src[right++];
        }
    }
}
