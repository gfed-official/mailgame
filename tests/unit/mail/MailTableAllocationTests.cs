using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Covers the 0 B clause of mail-destinations story 001 AC-4: create, retire and read allocate nothing on the steady-state
/// path (pages and the page map are preallocated). Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class MailTableAllocationTests
{
    private static void SkipIfUnoptimised()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(MailTable).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    [Fact]
    [Trait("Id", "M-R2-1-pool")]
    public void Ac4_CreateRetireAndRead_2048RowsOnTwoPreallocatedPages_AllocateNothing_InOptimisedBuilds()
    {
        SkipIfUnoptimised();
        var rig = new MailTableRig(pages: 2);
        DefIndex kind = DefIndex.FromValue(3);
        AddressId addr = AddressId.Pack(1, 2, 7);
        var ids = new MailId[2048];
        long sum = 0;

        // One window: fill both pages, read every row, retire every row (both pages return to the pool). Repeatable.
        (long bytes, int runs) = AllocAssert.Measure(() =>
        {
            sum = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = rig.Table.Create(kind, addr, 250, 2, MailFlags.Cursed);
            }

            for (int i = 0; i < ids.Length; i++)
            {
                if (rig.Table.TryGetRow(ids[i], out MailRow row))
                {
                    sum += row.SpawnValue;
                }
            }

            for (int i = 0; i < ids.Length; i++)
            {
                rig.Table.Retire(ids[i]);
            }
        });

        Assert.Equal(0L, bytes);
        Assert.Equal(2048L * 250L, sum);
        Assert.Equal(0, rig.Table.LiveCount);
        Assert.Equal(2, rig.Table.FreePages);
        Assert.Equal(0, rig.Table.PoolGrowCount);
        Assert.Equal(0, rig.Table.MapGrowCount);
        Assert.True(runs >= 3);
    }

    [Fact]
    [Trait("Id", "M-R2-dump")]
    public void Dump_OfTheMailTable_AllocatesNothing_InOptimisedBuilds()
    {
        SkipIfUnoptimised();
        var rig = new MailTableRig(pages: 2);
        for (int i = 0; i < 100; i++)
        {
            rig.Create();
        }

        var output = new System.Buffers.ArrayBufferWriter<byte>(64 * 1024);
        var scratch = new CanonicalBuffer();
        var contributor = new MailStateContributor(rig.Table, DestinationRegistry.CreateEmpty());

        (long bytes, int _) = AllocAssert.Measure(() =>
        {
            output.Clear();
            var w = new CanonicalWriter(output, scratch);
            w.BeginSection(contributor.SectionId);
            contributor.Write(ref w);
            w.EndSection();
        });

        Assert.Equal(0L, bytes);
    }
}
