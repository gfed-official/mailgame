using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Primitives;

/// <summary>
/// Covers <see cref="StableSort"/> (TR-sim-032; ADR-0006 section 3 rule 5; story AC-3).
/// Records carry a primary <c>Key</c> (many equal values) and a unique <c>Seq</c> (input position),
/// so stability is observable as ascending <c>Seq</c> among equal keys. The key-only comparer
/// deliberately has NO tie-break, because these tests prove the sort's own stability; production
/// comparers must end with an EntityId/opSeq tie-break (see the StableSort remarks), which
/// <see cref="KeyThenSeq"/> demonstrates. Data comes from a fixed-seed local LCG, never
/// System.Random. The reference implementation is an independent top-down recursive merge sort.
/// </summary>
public class StableSortTests
{
    internal readonly record struct Rec(int Key, int Seq);

    internal readonly struct ByKey : IComparer<Rec>
    {
        public int Compare(Rec x, Rec y) => x.Key.CompareTo(y.Key);
    }

    private readonly struct KeyThenSeq : IComparer<Rec>
    {
        public int Compare(Rec x, Rec y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Seq.CompareTo(y.Seq);
    }

    private readonly struct Descending : IComparer<int>
    {
        public int Compare(int x, int y) => y.CompareTo(x);
    }

    private readonly struct Ascending : IComparer<int>
    {
        public int Compare(int x, int y) => x.CompareTo(y);
    }

    /// <summary>A value type whose GetHashCode and Equals throw: a sort that touches either fails the test.</summary>
    private readonly struct HashTrap
    {
        public readonly int Key;
        public readonly int Seq;

        public HashTrap(int key, int seq)
        {
            Key = key;
            Seq = seq;
        }

        public override int GetHashCode() => throw new InvalidOperationException("GetHashCode must not be called");

        public override bool Equals(object? obj) => throw new InvalidOperationException("Equals must not be called");
    }

    private readonly struct HashTrapByKey : IComparer<HashTrap>
    {
        public int Compare(HashTrap x, HashTrap y) => x.Key.CompareTo(y.Key);
    }

    /// <summary>Counts comparer calls (reference-type sink in a struct: no boxing of the comparer).</summary>
    private sealed class CallCounter
    {
        public int Calls;
    }

    private readonly struct CountingByKey : IComparer<Rec>
    {
        private readonly CallCounter _counter;

        public CountingByKey(CallCounter counter)
        {
            _counter = counter;
        }

        public int Compare(Rec x, Rec y)
        {
            _counter.Calls++;
            return x.Key.CompareTo(y.Key);
        }
    }

    // ---- helpers ----

    /// <summary>Fixed-seed LCG (Numerical Recipes constants); deterministic on every platform.</summary>
    private static uint NextLcg(ref uint state)
    {
        state = unchecked(state * 1_664_525u + 1_013_904_223u);
        return state >> 8;
    }

    internal static Rec[] MakeRecords(int count, int distinctKeys, uint seed)
    {
        uint state = seed;
        Rec[] recs = new Rec[count];
        for (int i = 0; i < count; i++)
        {
            recs[i] = new Rec((int)(NextLcg(ref state) % (uint)distinctKeys), i);
        }

        return recs;
    }

    /// <summary>Independent reference: top-down recursive merge sort, stable, allocating.</summary>
    private static Rec[] ReferenceMergeSort(Rec[] input)
    {
        if (input.Length <= 1)
        {
            return (Rec[])input.Clone();
        }

        int mid = input.Length / 2;
        Rec[] left = ReferenceMergeSort(input[..mid]);
        Rec[] right = ReferenceMergeSort(input[mid..]);
        Rec[] merged = new Rec[input.Length];
        int l = 0;
        int r = 0;
        int k = 0;
        while (l < left.Length && r < right.Length)
        {
            merged[k++] = right[r].Key < left[l].Key ? right[r++] : left[l++];
        }

        while (l < left.Length)
        {
            merged[k++] = left[l++];
        }

        while (r < right.Length)
        {
            merged[k++] = right[r++];
        }

        return merged;
    }

    private static void SortAndAssertMatchesReference(Rec[] data)
    {
        Rec[] expected = ReferenceMergeSort(data);
        Rec[] scratch = new Rec[data.Length];

        StableSort.Sort<Rec, ByKey>(data, scratch, new ByKey());

        Assert.Equal(expected, data);
        AssertStableAndOrdered(data);
    }

    internal static void AssertStableAndOrdered(Rec[] data)
    {
        for (int i = 1; i < data.Length; i++)
        {
            Assert.True(data[i - 1].Key <= data[i].Key, $"keys out of order at {i}");
            if (data[i - 1].Key == data[i].Key)
            {
                Assert.True(data[i - 1].Seq < data[i].Seq, $"equal keys lost input order at {i}");
            }
        }
    }

    // ---- AC-3 ----

    [Fact]
    public void StableSort_ThousandSeededRecordsManyEqualKeys_MatchesReferenceAndPreservesOrder()
    {
        // Arrange: 1000 records over only 20 distinct keys, so ~50 records per key.
        Rec[] data = MakeRecords(1_000, 20, 0xC0FFEEu);

        // Act / Assert
        SortAndAssertMatchesReference(data);
    }

    [Fact]
    public void StableSort_ThousandSeededRecords_MatchesStableLinqReference()
    {
        // Arrange: LINQ OrderBy is documented stable; the test assembly is not under the Sim's ban list.
        Rec[] data = MakeRecords(1_000, 20, 0xBADC0DEu);
        Rec[] expected = data.OrderBy(r => r.Key).ToArray();
        Rec[] scratch = new Rec[data.Length];

        // Act
        StableSort.Sort<Rec, ByKey>(data, scratch, new ByKey());

        // Assert
        Assert.Equal(expected, data);
    }

    [Fact]
    public void StableSort_ElementsWithThrowingGetHashCode_NeverReadsHashCodes()
    {
        // Arrange
        uint state = 7u;
        HashTrap[] data = new HashTrap[100];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = new HashTrap((int)(NextLcg(ref state) % 5u), i);
        }

        HashTrap[] scratch = new HashTrap[data.Length];

        // Act (would throw InvalidOperationException if GetHashCode/Equals were touched)
        StableSort.Sort<HashTrap, HashTrapByKey>(data, scratch, new HashTrapByKey());

        // Assert
        for (int i = 1; i < data.Length; i++)
        {
            Assert.True(data[i - 1].Key <= data[i].Key);
            if (data[i - 1].Key == data[i].Key)
            {
                Assert.True(data[i - 1].Seq < data[i].Seq);
            }
        }
    }

    // ---- sizes, boundaries, shapes ----

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(47)]
    [InlineData(48)]
    [InlineData(49)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(100)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(1_000)]
    [InlineData(4_099)]
    public void StableSort_RandomWithDuplicates_MatchesReferenceAtEverySizeBoundary(int count)
    {
        // Arrange
        Rec[] data = MakeRecords(count, 7, 12345u + (uint)count);

        // Act / Assert
        SortAndAssertMatchesReference(data);
    }

    [Fact]
    public void StableSort_EmptyInput_IsNoOpAndTouchesNothing()
    {
        // Arrange
        var counter = new CallCounter();

        // Act
        StableSort.Sort<Rec, CountingByKey>(Span<Rec>.Empty, Span<Rec>.Empty, new CountingByKey(counter));

        // Assert
        Assert.Equal(0, counter.Calls);
    }

    [Fact]
    public void StableSort_SingleElement_IsUnchangedWithNoComparisons()
    {
        // Arrange
        Rec[] data = { new Rec(5, 0) };
        var counter = new CallCounter();

        // Act
        StableSort.Sort<Rec, CountingByKey>(data, new Rec[1], new CountingByKey(counter));

        // Assert
        Assert.Equal(new Rec(5, 0), data[0]);
        Assert.Equal(0, counter.Calls);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(500)]
    public void StableSort_AlreadySorted_StaysUnchanged(int count)
    {
        // Arrange: ascending keys with each key repeated (still sorted, with ties).
        Rec[] data = new Rec[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = new Rec(i / 3, i);
        }

        Rec[] expected = (Rec[])data.Clone();

        // Act
        StableSort.Sort<Rec, ByKey>(data, new Rec[count], new ByKey());

        // Assert
        Assert.Equal(expected, data);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(500)]
    public void StableSort_ReverseSortedDistinctKeys_BecomesAscending(int count)
    {
        // Arrange
        Rec[] data = new Rec[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = new Rec(count - i, i);
        }

        // Act
        StableSort.Sort<Rec, ByKey>(data, new Rec[count], new ByKey());

        // Assert
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(new Rec(i + 1, count - 1 - i), data[i]);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(33)]
    [InlineData(1_000)]
    public void StableSort_AllKeysEqual_KeepsInputOrder(int count)
    {
        // Arrange
        Rec[] data = new Rec[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = new Rec(9, i);
        }

        // Act
        StableSort.Sort<Rec, ByKey>(data, new Rec[count], new ByKey());

        // Assert
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i, data[i].Seq);
        }
    }

    [Fact]
    public void StableSort_ReverseSortedWithTies_PreservesInputOrderWithinEachKey()
    {
        // Arrange: keys descend in blocks of 4 equal keys.
        const int Count = 200;
        Rec[] data = new Rec[Count];
        for (int i = 0; i < Count; i++)
        {
            data[i] = new Rec((Count - i) / 4, i);
        }

        // Act / Assert
        SortAndAssertMatchesReference(data);
    }

    [Fact]
    public void StableSort_DescendingComparer_SortsDescendingAndReverseComparerRoundTrips()
    {
        // Arrange
        uint state = 99u;
        int[] data = new int[300];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (int)(NextLcg(ref state) % 1000u);
        }

        int[] scratch = new int[data.Length];

        // Act
        StableSort.Sort<int, Descending>(data, scratch, new Descending());

        // Assert
        for (int i = 1; i < data.Length; i++)
        {
            Assert.True(data[i - 1] >= data[i]);
        }

        StableSort.Sort<int, Ascending>(data, scratch, new Ascending());
        for (int i = 1; i < data.Length; i++)
        {
            Assert.True(data[i - 1] <= data[i]);
        }
    }

    [Fact]
    public void StableSort_ComparerWithIdTieBreak_ProducesCanonicalOrderFromAnyInputOrder()
    {
        // Arrange: rule 5. With a total tie-break the result is independent of the input permutation.
        Rec[] forward = MakeRecords(300, 6, 5u);
        Rec[] shuffled = (Rec[])forward.Clone();
        uint state = 77u;
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = (int)(NextLcg(ref state) % (uint)(i + 1));
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        // Act
        StableSort.Sort<Rec, KeyThenSeq>(forward, new Rec[300], new KeyThenSeq());
        StableSort.Sort<Rec, KeyThenSeq>(shuffled, new Rec[300], new KeyThenSeq());

        // Assert
        Assert.Equal(forward, shuffled);
    }

    // ---- sub-spans, scratch handling ----

    [Fact]
    public void StableSort_SubSpanOfLargerArray_SortsOnlyThatRange()
    {
        // Arrange
        Rec[] all = MakeRecords(100, 5, 3u);
        Rec[] before = (Rec[])all.Clone();
        Rec[] scratch = new Rec[100];

        // Act: sort elements 10 ... 59 only
        StableSort.Sort<Rec, ByKey>(all.AsSpan(10, 50), scratch, new ByKey());

        // Assert
        Assert.Equal(before[..10], all[..10]);
        Assert.Equal(before[60..], all[60..]);
        AssertStableAndOrdered(all[10..60]);
        Assert.Equal(ReferenceMergeSort(before[10..60]), all[10..60]);
    }

    [Fact]
    public void StableSort_ScratchLongerThanData_Works()
    {
        // Arrange
        Rec[] data = MakeRecords(100, 5, 11u);
        Rec[] expected = ReferenceMergeSort(data);

        // Act
        StableSort.Sort<Rec, ByKey>(data, new Rec[500], new ByKey());

        // Assert
        Assert.Equal(expected, data);
    }

    [Fact]
    public void StableSort_ScratchContainsGarbage_IsIgnored()
    {
        // Arrange
        Rec[] data = MakeRecords(100, 5, 13u);
        Rec[] expected = ReferenceMergeSort(data);
        Rec[] scratch = new Rec[100];
        Array.Fill(scratch, new Rec(-1, -1));

        // Act
        StableSort.Sort<Rec, ByKey>(data, scratch, new ByKey());

        // Assert
        Assert.Equal(expected, data);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(17, 16)]
    [InlineData(100, 0)]
    public void StableSort_ScratchShorterThanData_ThrowsArgumentExceptionAndLeavesDataUntouched(int dataLength, int scratchLength)
    {
        // Arrange
        Rec[] data = MakeRecords(dataLength, 5, 17u);
        Rec[] before = (Rec[])data.Clone();

        // Act
        Rec[] scratch = new Rec[scratchLength];
        var ex = Assert.Throws<ArgumentException>(() => StableSort.Sort<Rec, ByKey>(data, scratch, new ByKey()));

        // Assert
        Assert.Equal("scratch", ex.ParamName);
        Assert.Equal(before, data);
    }

    [Fact]
    public void StableSort_ScratchOverlapsData_ThrowsArgumentException()
    {
        // Arrange
        Rec[] backing = MakeRecords(100, 5, 19u);

        // Act / Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            StableSort.Sort<Rec, ByKey>(backing.AsSpan(0, 50), backing.AsSpan(25, 50), new ByKey()));
        Assert.Equal("scratch", ex.ParamName);
    }

    [Fact]
    public void StableSort_AdjacentNonOverlappingSpansOfOneArray_Works()
    {
        // Arrange: data is the first half, scratch the second half of the same array.
        Rec[] backing = MakeRecords(100, 5, 23u);
        Rec[] expected = ReferenceMergeSort(backing[..50]);

        // Act
        StableSort.Sort<Rec, ByKey>(backing.AsSpan(0, 50), backing.AsSpan(50, 50), new ByKey());

        // Assert
        Assert.Equal(expected, backing[..50]);
    }

    // ---- structure ----

    [Fact]
    public void StableSort_SmallInput_UsesNoMoreThanQuadraticInsertionComparisons()
    {
        // Arrange: reverse-sorted 16 elements is the insertion-sort worst case: 16*15/2 = 120 comparisons.
        Rec[] data = new Rec[StableSort.InsertionThreshold];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = new Rec(data.Length - i, i);
        }

        var counter = new CallCounter();

        // Act
        StableSort.Sort<Rec, CountingByKey>(data, new Rec[data.Length], new CountingByKey(counter));

        // Assert
        Assert.Equal(120, counter.Calls);
        AssertStableAndOrdered(data);
    }

    [Fact]
    public void StableSort_SortedLargeInput_UsesNearLinearComparisons()
    {
        // Arrange: already sorted, n = 1024 -> merge sort does ~n/2 comparisons per level, far below n^2.
        Rec[] data = new Rec[1_024];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = new Rec(i, i);
        }

        var counter = new CallCounter();

        // Act
        StableSort.Sort<Rec, CountingByKey>(data, new Rec[data.Length], new CountingByKey(counter));

        // Assert
        Assert.True(counter.Calls < 1_024 * 8, $"{counter.Calls} comparisons is not O(n log n)");
        AssertStableAndOrdered(data);
    }

    [Fact]
    public void StableSort_InsertionThreshold_Is16()
    {
        // Assert (the documented n <= 16 insertion / bottom-up merge boundary)
        Assert.Equal(16, StableSort.InsertionThreshold);
    }
}

/// <summary>
/// Zero-allocation guardrail for <see cref="StableSort"/> (story AC-3), in the non-parallel collection so runtime
/// warm-up on other test threads cannot land in a measured window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class StableSortAllocationTests
{
    [Fact]
    public void StableSort_WarmedCall_AllocatesZeroBytes()
    {
        // Arrange: each window resets unsorted input before sorting, so every run (warm-up and measured) sorts
        // fresh data. Covers the merge path (n=1000) and the insertion path (n=16).
        StableSortTests.Rec[] source = StableSortTests.MakeRecords(1_000, 20, 42u);
        StableSortTests.Rec[] data = new StableSortTests.Rec[1_000];
        StableSortTests.Rec[] scratch = new StableSortTests.Rec[1_000];

        // Act + Assert (0 B): merge path.
        _ = AllocAssert.Zero("StableSort merge path, n=1000", () =>
        {
            source.AsSpan().CopyTo(data);
            StableSort.Sort<StableSortTests.Rec, StableSortTests.ByKey>(data, scratch, new StableSortTests.ByKey());
        });

        // Act + Assert (0 B): insertion path.
        _ = AllocAssert.Zero("StableSort insertion path, n=16", () =>
        {
            source.AsSpan(0, 16).CopyTo(data);
            StableSort.Sort<StableSortTests.Rec, StableSortTests.ByKey>(data.AsSpan(0, 16), scratch, new StableSortTests.ByKey());
        });

        // Assert
        StableSortTests.AssertStableAndOrdered(data.AsSpan(0, 16).ToArray());
    }
}
