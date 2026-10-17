using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Covers mail-destinations story 001 (TR-mail-006, GDD M-R2-1, ADR-0017 section 2): the paged struct-of-arrays mail table,
/// id-ordered row creation, retire, the page pool and the Mail dump section. The 0 B clause of AC-4 is in
/// <see cref="MailTableAllocationTests"/>.
/// </summary>
public class MailTableTests
{
    // ---- AC-1 (M-R2-1a) ----

    [Fact]
    [Trait("Id", "M-R2-1a")]
    public void Ac1_CounterAt41_ThreeUnitBatch_AllocatesIds42To44_InOrder_WithDeadlineShiftEqualToSpawnShift()
    {
        var rig = new MailTableRig(lastIssued: 41);

        MailId a = rig.Table.Create(DefIndex.FromValue(1), AddressId.Pack(1, 1, 10), 120, spawnShift: 2, MailFlags.None);
        MailId b = rig.Table.Create(DefIndex.FromValue(2), AddressId.Pack(1, 1, 11), 340, spawnShift: 2, MailFlags.Cursed);
        MailId c = rig.Table.Create(DefIndex.FromValue(1), AddressId.Pack(1, 2, 12), 560, spawnShift: 3, MailFlags.Found);

        Assert.Equal(new[] { 42u, 43u, 44u }, new[] { a.Value, b.Value, c.Value });
        Assert.Equal(44u, rig.Ids.Peek());
        Assert.True(rig.Table.TryGetRow(a, out MailRow ra));
        Assert.True(rig.Table.TryGetRow(b, out MailRow rb));
        Assert.True(rig.Table.TryGetRow(c, out MailRow rc));
        Assert.Equal((byte)2, ra.DeadlineShift);
        Assert.Equal((byte)2, rb.DeadlineShift);
        Assert.Equal((byte)3, rc.DeadlineShift);
        Assert.All(new[] { ra, rb, rc }, r => Assert.Equal(r.SpawnShift, r.DeadlineShift));

        // The row stores every field it was given.
        Assert.Equal(a, ra.Id);
        Assert.Equal(DefIndex.FromValue(2), rb.Kind);
        Assert.Equal(AddressId.Pack(1, 2, 12), rc.Address);
        Assert.Equal((ushort)340, rb.SpawnValue);
        Assert.Equal(MailFlags.Cursed, rb.Flags);
        Assert.Equal(MailFlags.Found, rc.Flags);
        Assert.Equal(3, rig.Table.LiveCount);
    }

    // ---- AC-2 (M-R2-1b, c) ----

    [Fact]
    [Trait("Id", "M-R2-1b")]
    public void Ac2_SpawnValue_IsNeverChangedAfterCreation_AndTheRowHasNoSetters()
    {
        var rig = new MailTableRig();
        MailId id = rig.Create(value: 777, shift: 4);
        rig.Create(value: 1);
        rig.Create(value: 2);

        Assert.True(rig.Table.TryGetRow(id, out MailRow before));
        Assert.True(rig.Table.TryGetRow(id, out MailRow after));
        Assert.Equal((ushort)777, before.SpawnValue);
        Assert.Equal(before.SpawnValue, after.SpawnValue);

        // No way to write a row field through the row type: no settable property, no public mutable field.
        foreach (PropertyInfo p in typeof(MailRow).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.Null(p.SetMethod);
        }

        Assert.Empty(typeof(MailRow).GetFields(BindingFlags.Public | BindingFlags.Instance));
        Assert.True(typeof(MailRow).IsValueType);

        // The table's public surface has no mutator of an existing row other than Retire.
        string[] mutators = typeof(MailTable)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Where(n => n.StartsWith("Set", StringComparison.Ordinal) || n.StartsWith("Update", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(mutators);
    }

    [Fact]
    [Trait("Id", "M-R2-1c")]
    public void Ac2_Retire_ClearsTheLiveBit_AndDecrementsThePageLiveCount_AndReadReportsNotLive()
    {
        var rig = new MailTableRig();
        MailId a = rig.Create();
        MailId b = rig.Create();
        MailId c = rig.Create();
        Assert.Equal(3, rig.Table.LiveCountOfPage(0));

        Assert.True(rig.Table.Retire(b));

        Assert.False(rig.Table.IsLive(b));
        Assert.False(rig.Table.TryGetRow(b, out MailRow gone));
        Assert.Equal(default, gone);
        Assert.Equal(2, rig.Table.LiveCountOfPage(0));
        Assert.Equal(2, rig.Table.LiveCount);
        Assert.True(rig.Table.TryGetRow(a, out _));
        Assert.True(rig.Table.TryGetRow(c, out _));

        // Retiring again, an unknown id, or None changes nothing.
        Assert.False(rig.Table.Retire(b));
        Assert.False(rig.Table.Retire(new MailId(9_999)));
        Assert.False(rig.Table.Retire(MailId.None));
        Assert.False(rig.Table.TryGetRow(MailId.None, out _));
        Assert.False(rig.Table.TryGetRow(new MailId(uint.MaxValue), out _));
        Assert.Equal(2, rig.Table.LiveCount);
    }

    // ---- AC-3 (M-R2-1d, e) ----

    [Fact]
    [Trait("Id", "M-R2-1d")]
    public void Ac3_RowsAreRetiredOnlyByRetire_NothingElseRemovesThem()
    {
        var rig = new MailTableRig();
        var ids = new List<MailId>();
        for (int shift = 0; shift < 5; shift++)
        {
            // Rows created in successive shifts: a shift change has no table entry point, so every earlier row stays live.
            rig.Tick.Tick += 1000u;
            ids.Add(rig.Create(shift: (byte)shift));
            ids.Add(rig.Create(shift: (byte)shift));
        }

        Assert.Equal(10, rig.Table.LiveCount);
        Assert.All(ids, id => Assert.True(rig.Table.IsLive(id)));

        rig.Table.Retire(ids[3]);

        Assert.Equal(9, rig.Table.LiveCount);
        Assert.All(ids.Where(i => i != ids[3]), id => Assert.True(rig.Table.IsLive(id)));
    }

    [Fact]
    [Trait("Id", "M-R2-1e")]
    public void Ac3_TenThousandAllocations_YieldNoRepeatedMailId()
    {
        var rig = new MailTableRig();
        var seen = new HashSet<uint>();
        uint previous = 0;
        for (int i = 0; i < 10_000; i++)
        {
            MailId id = rig.Create();
            Assert.False(id.IsNone);
            Assert.True(seen.Add(id.Value), $"mail id {id.Value} repeated");
            Assert.True(id.Value > previous, "ids must ascend (lower = older)");
            previous = id.Value;
        }

        Assert.Equal(10_000, seen.Count);
        Assert.Equal(10_000, rig.Table.LiveCount);
        Assert.Equal(0, rig.Table.PoolGrowCount);
    }

    // ---- AC-4 pages and pool (the 0 B clause is in MailTableAllocationTests) ----

    [Fact]
    [Trait("Id", "M-R2-1-pool")]
    public void Ac4_TwoPages_2048Rows_UseBothPreallocatedPages_WithoutGrowing()
    {
        var rig = new MailTableRig(pages: 2);
        Assert.Equal(2, rig.Table.FreePages);

        for (int i = 0; i < 2048; i++)
        {
            Assert.False(rig.Create().IsNone);
        }

        Assert.Equal(0, rig.Table.FreePages);
        Assert.Equal(2, rig.Table.TotalPages);
        Assert.Equal(0, rig.Table.PoolGrowCount);
        Assert.Equal(0, rig.Ring.Count);
        Assert.Equal(1024, rig.Table.LiveCountOfPage(0));
        Assert.Equal(1024, rig.Table.LiveCountOfPage(1));
    }

    [Fact]
    [Trait("Id", "M-R2-1-pool")]
    public void Ac4_RetiringEveryRowOfPage0_ReturnsItToThePool_AndTheNextPageIsDrawnFromThePool()
    {
        var rig = new MailTableRig(pages: 2);
        for (int i = 0; i < 2048; i++)
        {
            rig.Create();
        }

        // 1023 of page 0's rows retired: the page is not empty yet, so it stays out of the pool.
        for (uint id = 1; id <= 1023; id++)
        {
            Assert.True(rig.Table.Retire(new MailId(id)));
        }

        Assert.Equal(0, rig.Table.FreePages);
        Assert.True(rig.Table.Retire(new MailId(1024)));
        Assert.Equal(1, rig.Table.FreePages);
        Assert.Equal(0, rig.Table.LiveCountOfPage(0));

        // Ids 2049..3072 open page 2: it comes from the pool, no growth, no check.
        for (int i = 0; i < 1024; i++)
        {
            MailId id = rig.Create();
            Assert.Equal(2049u + (uint)i, id.Value);
        }

        Assert.Equal(0, rig.Table.FreePages);
        Assert.Equal(2, rig.Table.TotalPages);
        Assert.Equal(0, rig.Table.PoolGrowCount);
        Assert.Equal(0, rig.RingCount(CheckCode.MailTableGrow));
        Assert.Equal(1024, rig.Table.LiveCountOfPage(2));
    }

    [Fact]
    [Trait("Id", "M-R2-1-pool")]
    public void Ac4_The2049thLiveRow_WithAnEmptyPool_GrowsThePool_RecordsExactlyOneCheck_AndSucceeds()
    {
        var rig = new MailTableRig(pages: 2);
        rig.Tick.Tick = 77;
        for (int i = 0; i < 2048; i++)
        {
            rig.Create();
        }

        MailId id = rig.Create();

        Assert.Equal(2049u, id.Value);
        Assert.True(rig.Table.TryGetRow(id, out _));
        Assert.Equal(1, rig.Table.PoolGrowCount);
        Assert.Equal(0, rig.Table.MapGrowCount);
        Assert.Equal(4, rig.Table.TotalPages);
        Assert.Equal(1, rig.Table.FreePages);   // 2 new pages, 1 taken
        Assert.Equal(1, rig.Ring.Count);
        Assert.Equal(0u, rig.Ring.Suppressed(CheckCode.MailTableGrow));
        SimCheckRecord record = rig.Ring[0];
        Assert.Equal(CheckCode.MailTableGrow, record.Check);
        Assert.Equal(77u, record.Tick);
        Assert.Equal(MailTable.GrowKindPool, record.A);
        Assert.Equal(4L, record.B);   // new total page count

        // The grown pool serves the next pages with no further growth.
        for (int i = 0; i < (2 * 1024) - 1; i++)   // ids 2050..4096: the rest of page 2, then page 3
        {
            Assert.False(rig.Create().IsNone);
        }

        Assert.Equal(1, rig.Table.PoolGrowCount);
        Assert.Equal(1, rig.RingCount(CheckCode.MailTableGrow));
    }

    [Fact]
    [Trait("Id", "M-R2-1-pool")]
    public void Ac4_ADefaultSizedTable_NeverGrowsInANormalFixture()
    {
        var rig = new MailTableRig();   // 64 pages = 65,536 rows
        for (int i = 0; i < 20_000; i++)
        {
            rig.Create();
        }

        Assert.Equal(64, rig.Table.TotalPages);
        Assert.Equal(0, rig.Table.PoolGrowCount);
        Assert.Equal(0, rig.Table.MapGrowCount);
        Assert.Equal(0, rig.Ring.Count);
    }

    [Fact]
    [Trait("Id", "M-R2-1-pool")]
    public void Ac4_ACounterStartingMidPage_StillReturnsPagesOnceFullyIssuedAndEmpty()
    {
        var rig = new MailTableRig(pages: 2, lastIssued: 1020);
        var ids = new List<MailId>();
        for (int i = 0; i < 8; i++)
        {
            ids.Add(rig.Create());
        }

        // ids 1021..1024 are on page 0 (slot 1023 issued), 1025..1028 on page 1.
        Assert.Equal(1021u, ids[0].Value);
        Assert.Equal(0, rig.Table.FreePages);   // both pages in use
        for (int i = 0; i < 4; i++)
        {
            rig.Table.Retire(ids[i]);
        }

        Assert.Equal(1, rig.Table.FreePages);
        Assert.Equal(0, rig.Table.LiveCountOfPage(0));
        Assert.Equal(4, rig.Table.LiveCountOfPage(1));
    }

    [Fact]
    [Trait("Id", "M-R2-1-pool")]
    public void Ac4_IdsRunningPastThePageMap_DoublesTheMap_RecordsAMapGrowCheck_AndEarlierRowsStayReadable()
    {
        // pages = 2: the page map starts at 256 entries (ids up to 262,144). Start two ids short of the map's end.
        const uint MapEndId = 256u * 1024u;
        var rig = new MailTableRig(pages: 2, lastIssued: MapEndId - 2);
        rig.Tick.Tick = 9;
        MailId a = rig.Create(value: 11);
        MailId b = rig.Create(value: 22);
        Assert.Equal(MapEndId - 1, a.Value);
        Assert.Equal(MapEndId, b.Value);
        Assert.Equal(0, rig.Table.MapGrowCount);
        Assert.Equal(0, rig.Ring.Count);

        MailId c = rig.Create(value: 33);   // page 256: past the map

        Assert.Equal(MapEndId + 1, c.Value);
        Assert.Equal(1, rig.Table.MapGrowCount);
        Assert.Equal(0, rig.Table.PoolGrowCount);
        Assert.Equal(1, rig.Ring.Count);
        Assert.Equal(1, rig.RingCount(CheckCode.MailTableGrow));
        SimCheckRecord record = rig.Ring[0];
        Assert.Equal(CheckCode.MailTableGrow, record.Check);
        Assert.Equal(MailTable.GrowKindMap, record.A);
        Assert.Equal(512L, record.B);
        Assert.Equal(9u, record.Tick);
        Assert.NotEqual(MailTable.GrowKindPool, record.A);

        Assert.True(rig.Table.TryGetRow(a, out MailRow ra));
        Assert.True(rig.Table.TryGetRow(b, out MailRow rb));
        Assert.True(rig.Table.TryGetRow(c, out MailRow rc));
        Assert.Equal((ushort)11, ra.SpawnValue);
        Assert.Equal((ushort)22, rb.SpawnValue);
        Assert.Equal((ushort)33, rc.SpawnValue);
        Assert.Equal(3, rig.Table.LiveCount);
        Assert.True(rig.Table.Retire(a));
        Assert.False(rig.Table.IsLive(a));
    }

    [Fact]
    [Trait("Id", "M-R2-1-pool")]
    public void Ac4_ARecycledPage_IsFullyZeroed_AndTheReleasedPagesIdsReportNotLive()
    {
        var rig = new MailTableRig(pages: 2);
        for (int i = 0; i < 1024; i++)
        {
            rig.Table.Create(DefIndex.FromValue(9), AddressId.Pack(5, 6, 7, 8), ushort.MaxValue, 200, MailFlags.Cursed | MailFlags.Found);
        }

        Assert.True(rig.Table.TryGetRow(new MailId(1), out MailRow full));
        Assert.Equal(MailFlags.Cursed | MailFlags.Found, full.Flags);
        for (uint id = 1; id <= 1024; id++)
        {
            Assert.True(rig.Table.Retire(new MailId(id)));
        }

        // Page 0 went back to the pool clean, and its ids read as not live through every entry point.
        Assert.Equal(2, rig.Table.FreePages);
        Assert.True(rig.Table.FreePagesAreClear());
        for (uint id = 1; id <= 1024; id++)
        {
            Assert.False(rig.Table.IsLive(new MailId(id)));
            Assert.False(rig.Table.TryGetRow(new MailId(id), out _));
            Assert.False(rig.Table.Retire(new MailId(id)));
        }

        // The recycled page is drawn again (LIFO pool) for page 1, and starts with no stale state.
        MailId next = rig.Table.Create(DefIndex.FromValue(1), AddressId.Pack(1, 1, 1), 5, 0, MailFlags.None);
        Assert.Equal(1025u, next.Value);
        Assert.Equal(1, rig.Table.FreePages);
        Assert.True(rig.Table.TryGetRow(next, out MailRow row));
        Assert.Equal(MailFlags.None, row.Flags);
        Assert.Equal((byte)0, row.SpawnShift);
        Assert.Equal(1, rig.Table.LiveCountOfPage(1));
        Assert.False(rig.Table.IsLive(new MailId(1026)));
        Assert.False(rig.Table.TryGetRow(new MailId(1026), out _));

        // Retiring the lone row and filling the page to its end releases it again, clean.
        for (int i = 0; i < 1023; i++)
        {
            rig.Create();
        }

        for (uint id = 1025; id <= 2048; id++)
        {
            rig.Table.Retire(new MailId(id));
        }

        Assert.Equal(2, rig.Table.FreePages);
        Assert.True(rig.Table.FreePagesAreClear());
    }

    [Fact]
    [Trait("Id", "M-R2-1a")]
    public void Create_WhenTheAllocatorRefuses_ReturnsNone_WritesNoRow_AndLeavesLiveCountUnchanged()
    {
        var holder = new ForwardingReader();
        var counters = new SimCounters();
        var ring = new SimCheckRing();
        var ids = new MailIds(holder, ring, counters);
        var table = new MailTable(new PerformativeMail.Sim.Content.SimSettings { MailTablePages = 2 }, ids, holder, ring);
        var builder = new SimWorldBuilder(new SimCoreSettings(), counters, ring);
        SimWorld world = builder.Freeze();
        holder.Target = world;

        // Between ticks, MailIds.Allocate is illegal (ADR-0004): it refuses, and Create must not touch the table.
        MailId id = MailId.None;
        Exception? thrown = null;
        try
        {
            id = table.Create(DefIndex.FromValue(1), AddressId.Pack(1, 1, 1), 100, 1, MailFlags.None);
        }
        catch (Exception e)
        {
            thrown = e;
        }

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException e = Assert.IsType<SimAssertException>(thrown);
            Assert.Equal(SimAssertCode.IdAllocIllegal, e.Code);
        }
        else
        {
            Assert.Null(thrown);
        }

        Assert.True(id.IsNone);
        Assert.Equal(0, table.LiveCount);
        Assert.Equal(0, table.LiveCountOfPage(0));
        Assert.Equal(0u, ids.Peek());
        Assert.Equal(2, table.FreePages);
        Assert.False(table.TryGetRow(new MailId(1), out _));
        Assert.Equal(1L, counters[CounterId.IdAllocIllegal]);
    }

    [Fact]
    [Trait("Id", "M-R2-1-pool")]
    public void Ac4_AnEmptyPageThatIsStillBeingFilled_IsNotReturnedToThePool()
    {
        var rig = new MailTableRig(pages: 2);
        MailId a = rig.Create();
        rig.Table.Retire(a);

        // Page 0 is the open page (slots 1..1023 still to be issued): it stays mapped, and the next row reuses it.
        Assert.Equal(1, rig.Table.FreePages);
        MailId b = rig.Create();
        Assert.Equal(2u, b.Value);
        Assert.Equal(1, rig.Table.FreePages);
        Assert.Equal(1, rig.Table.LiveCountOfPage(0));
    }

    // ---- AC-5 dump ----

    private static byte[] DumpSection(MailTable table)
    {
        var output = new ArrayBufferWriter<byte>();
        var scratch = new CanonicalBuffer();
        var w = new CanonicalWriter(output, scratch);
        var contributor = new MailStateContributor(table, DestinationRegistry.CreateEmpty());
        w.BeginSection(contributor.SectionId);
        contributor.Write(ref w);
        w.EndSection();
        return output.WrittenSpan.ToArray();
    }

    private static MailTableRig BuildSequence()
    {
        var rig = new MailTableRig(pages: 2, lastIssued: 1022);
        MailId a = rig.Table.Create(DefIndex.FromValue(5), AddressId.Pack(2, 3, 4, 5), 1234, 1, MailFlags.Cursed);
        MailId b = rig.Table.Create(DefIndex.FromValue(6), AddressId.Pack(1, 1, 1), 99, 2, MailFlags.Found);
        MailId c = rig.Table.Create(DefIndex.FromValue(7), AddressId.Pack(63, 255, 4095, 63), ushort.MaxValue, 255, MailFlags.Cursed | MailFlags.Found);
        rig.Table.Create(DefIndex.FromValue(8), AddressId.Pack(4, 5, 6), 0, 0, MailFlags.None);
        rig.Table.Retire(b);
        Assert.Equal(1023u, a.Value);
        Assert.Equal(1025u, c.Value);
        return rig;
    }

    [Fact]
    [Trait("Id", "M-R2-dump")]
    public void Ac5_Dump_ListsLiveRowsAscending_FieldByField()
    {
        MailTableRig rig = BuildSequence();   // ids 1023, (1024 retired), 1025, 1026: spans two pages

        byte[] bytes = DumpSection(rig.Table);

        // Section framing: id u16, length u32, payload.
        Assert.Equal(SectionIds.Mail, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(2));
        Assert.Equal(bytes.Length - 6, length);
        ReadOnlySpan<byte> p = bytes.AsSpan(6);

        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(p));   // liveCount
        const int RowBytes = 4 + 2 + 4 + 2 + 1 + 1 + 1;
        Assert.Equal(4 + (3 * RowBytes) + 2, p.Length);   // rows, then the empty registry's u16 count
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(4 + (3 * RowBytes))));

        ReadOnlySpan<byte> r0 = p.Slice(4, RowBytes);
        Assert.Equal(1023u, BinaryPrimitives.ReadUInt32LittleEndian(r0));
        Assert.Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(r0.Slice(4)));
        Assert.Equal(AddressId.Pack(2, 3, 4, 5).Value, BinaryPrimitives.ReadUInt32LittleEndian(r0.Slice(6)));
        Assert.Equal((ushort)1234, BinaryPrimitives.ReadUInt16LittleEndian(r0.Slice(10)));
        Assert.Equal((byte)1, r0[12]);   // spawnShift
        Assert.Equal((byte)1, r0[13]);   // deadlineShift
        Assert.Equal((byte)MailFlags.Cursed, r0[14]);

        ReadOnlySpan<byte> r1 = p.Slice(4 + RowBytes, RowBytes);
        Assert.Equal(1025u, BinaryPrimitives.ReadUInt32LittleEndian(r1));
        Assert.Equal(AddressId.Pack(63, 255, 4095, 63).Value, BinaryPrimitives.ReadUInt32LittleEndian(r1.Slice(6)));
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(r1.Slice(10)));
        Assert.Equal((byte)255, r1[12]);
        Assert.Equal((byte)255, r1[13]);
        Assert.Equal((byte)(MailFlags.Cursed | MailFlags.Found), r1[14]);

        ReadOnlySpan<byte> r2 = p.Slice(4 + (2 * RowBytes), RowBytes);
        Assert.Equal(1026u, BinaryPrimitives.ReadUInt32LittleEndian(r2));
        Assert.Equal((byte)0, r2[14]);
    }

    [Fact]
    [Trait("Id", "M-R2-dump")]
    public void Ac5_TwoTablesBuiltByTheSameCallSequence_DumpIdenticalBytes_AndRetiringOneRowChangesThem()
    {
        MailTableRig one = BuildSequence();
        MailTableRig two = BuildSequence();

        byte[] first = DumpSection(one.Table);

        Assert.Equal(first, DumpSection(two.Table));
        Assert.Equal(first, DumpSection(one.Table));   // dumping is repeatable and read-only

        two.Table.Retire(new MailId(1026));

        Assert.NotEqual(first, DumpSection(two.Table));
    }

    [Fact]
    [Trait("Id", "M-R2-dump")]
    public void Ac5_TheDump_DoesNotDependOnPagePoolHistory()
    {
        // Same live rows, different page recycling history: rows live on pages 0 and 2 here, versus a fresh table.
        var recycled = new MailTableRig(pages: 2);
        for (int i = 0; i < 2048; i++)
        {
            recycled.Create();
        }

        for (uint id = 1; id <= 2048; id++)
        {
            recycled.Table.Retire(new MailId(id));
        }

        var fresh = new MailTableRig(pages: 8, lastIssued: 2048);
        for (int i = 0; i < 3; i++)
        {
            recycled.Create(value: (ushort)(10 + i), shift: 2);
            fresh.Create(value: (ushort)(10 + i), shift: 2);
        }

        Assert.Equal(DumpSection(fresh.Table), DumpSection(recycled.Table));
    }

    [Fact]
    [Trait("Id", "M-R2-dump")]
    public void Ac5_EmptyTable_DumpsAZeroCountOnly()
    {
        var rig = new MailTableRig();

        byte[] bytes = DumpSection(rig.Table);

        Assert.Equal(6 + 4 + 2, bytes.Length);   // liveCount, then the empty registry's u16 count
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(6)));
    }

    // ---- world binding: the contributor really lands in the world's dump ----

    private sealed class CreatePhase : ISimPhase
    {
        private readonly Func<MailTable> _table;

        public CreatePhase(Func<MailTable> table)
        {
            _table = table;
        }

        public void Run(in TickContext ctx)
        {
            MailTable t = _table();
            t.Create(DefIndex.FromValue(9), AddressId.Pack(1, 1, 5), 300, 1, MailFlags.None);
            t.Create(DefIndex.FromValue(9), AddressId.Pack(1, 1, 6), 310, 1, MailFlags.Found);
        }
    }

    private sealed class ForwardingReader : ITickReader, IWorldStateReader
    {
        public SimWorld? Target { get; set; }

        public uint CurrentTick => Target!.CurrentTick;

        public TickStage Stage => Target!.Stage;

        public PhaseId Phase => Target!.Phase;

        public SimState State => Target!.State;
    }

    [Fact]
    [Trait("Id", "M-R2-dump")]
    public void Ac5_BoundToAWorld_RowsCreatedInsideTick_AppearInTheWorldDump_InTheMailSection()
    {
        var holder = new ForwardingReader();
        var counters = new SimCounters();
        var ring = new SimCheckRing();
        var ids = new MailIds(holder, ring, counters);
        var table = new MailTable(new PerformativeMail.Sim.Content.SimSettings { MailTablePages = 2 }, ids, holder, ring);
        var builder = new SimWorldBuilder(new SimCoreSettings(), counters, ring);
        builder.Bind(PhaseId.PlayersStep, new CreatePhase(() => table));
        table.BindDump(builder, DestinationRegistry.CreateEmpty());
        SimWorld world = builder.Freeze();
        holder.Target = world;

        world.Tick();

        byte[] dump = DumpHash.Bytes(world);
        Assert.Equal(2, table.LiveCount);
        int at = 6;   // after the dump header
        byte[]? mail = null;
        while (at < dump.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(dump.AsSpan(at));
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(at + 2));
            if (id == SectionIds.Mail)
            {
                mail = dump.AsSpan(at, 6 + len).ToArray();
            }

            at += 6 + len;
        }

        Assert.NotNull(mail);
        Assert.Equal(DumpSection(table), mail);
    }

    [Fact]
    [Trait("Id", "M-R2-1a")]
    public void SectionId_IsTheNextFreeValue_AndNotOneAlreadyBound()
    {
        Assert.Equal((ushort)3, SectionIds.Mail);
        Assert.NotEqual(SectionIds.SimCore, SectionIds.Mail);
        Assert.NotEqual(SectionIds.Players, SectionIds.Mail);
        Assert.Equal((ushort)7, (ushort)CheckCode.MailTableGrow);
    }
}
