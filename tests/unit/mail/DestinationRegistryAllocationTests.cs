using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Covers the 0 B clause of mail-destinations story 002: facts and both reverse lookups allocate nothing. Joins the
/// non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class DestinationRegistryAllocationTests
{
    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void FactsAndLookups_AllocateNothing_InOptimisedBuilds()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(DestinationRegistry).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        WorldTables world = b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
        var registry = new DestinationRegistry(world, BuiltInContent.CreateM1a());
        EntityId hit = world.Lots[4].Destination;
        EntityId miss = EntityId.Compose(EntityClass.Destination, 900);
        AddressId address = world.Lots[7].Address;
        AddressId absent = AddressId.Pack(1, 1, 4000);
        // The sorted-search fallback: descending ids are not contiguous, so the id lookup binary-searches a sorted copy.
        var sorted = new DestinationRegistry(Descending(50), BuiltInContent.CreateM1a());
        EntityId sortedHit = EntityId.Compose(EntityClass.Destination, 7);
        EntityId sortedMiss = EntityId.Compose(EntityClass.Destination, 900);
        long sum = 0;

        (long bytes, int runs) = AllocAssert.Measure(() =>
        {
            sum = 0;
            for (int i = 0; i < 100; i++)
            {
                if (registry.TryGetDestinationFacts(hit, out DestinationFacts f))
                {
                    sum += f.Type.Value + (int)f.State;
                }

                if (registry.TryGetDestinationFacts(miss, out _))
                {
                    sum = -1;
                }

                if (registry.TryGetOrdinal(address, out DestinationOrdinal o, out int probes))
                {
                    sum += o.Value + probes;
                }

                if (registry.TryGetOrdinal(absent, out _))
                {
                    sum = -1;
                }

                if (sorted.TryGetOrdinal(sortedHit, out DestinationOrdinal so))
                {
                    sum += so.Value;
                }

                if (sorted.TryGetOrdinal(sortedMiss, out _) || sorted.TryGetDestinationFacts(sortedMiss, out _))
                {
                    sum = -1;
                }

                if (!sorted.TryGetDestinationFacts(sortedHit, out DestinationFacts sf))
                {
                    sum = -1;
                }
                else
                {
                    sum += (int)sf.State;
                }
            }
        });

        Assert.False(sorted.IdLookupIsComputed);
        Assert.Equal(0L, bytes);
        Assert.True(sum > 0);
        Assert.True(runs >= 3);
    }

    /// <summary>Lots with ascending addresses and descending destination ids (counter n down to 1).</summary>
    private static LotRecord[] Descending(int n)
    {
        var lots = new LotRecord[n];
        for (int i = 0; i < n; i++)
        {
            lots[i] = new LotRecord(
                EntityId.Compose(EntityClass.Destination, (uint)(n - i)), AddressId.Pack(1, 1, i + 1), 1, 1, (ushort)(i + 1),
                default, default, FaceDir.PosZ, new Vec3Mm(i * 1000, 1000, 0), default, default, (ushort)i);
        }

        return lots;
    }
}
