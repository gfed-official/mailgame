using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Mail-destinations story 006 (ADR-0017 section 7 guardrail: 0 B per call): <see cref="ComplaintBuckets.ChargeMisdelivery"/> and
/// <see cref="ComplaintBuckets.ChargeLate"/> allocate nothing. Advisory until M0 (erratum E2): reported, not asserted. Joins the
/// non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class ComplaintBucketAllocationTests
{
    [Fact]
    [Trait("Id", "M-F9-1")]
    public void ChargeMisdeliveryAndChargeLate_Over10000Calls_AllocationIsReported()
    {
        // Arrange: the registry, buckets and sheet are built before the measured window.
        ContentSet content = BuiltInContent.CreateM1a();
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        var registry = new DestinationRegistry(b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm), content);
        var buckets = new ComplaintBuckets(registry, content.Mail, 1000, new SimCounters());
        IStatSheet sheet = StatSheet.Build(content, new StatSources([]));
        long[] block = new long[SimCounterSink.RequiredLength];
        long sum = 0;
        long now = 0;   // carried across AllocAssert's warm-up runs, so ticks never go back

        // Act
        (long bytes, _) = AllocAssert.Measure(() =>
        {
            var sink = new SimCounterSink(block);
            long local = 0;
            for (int i = 0; i < 10_000; i++)
            {
                now += 15;
                local += buckets.ChargeMisdelivery(new DestinationOrdinal((ushort)(i % registry.Count)), now, 5_000, sheet, ref sink);
                local += buckets.ChargeLate(now, graceSuppresses: false);
            }

            sum = local;
        });

        // Assert
        Assert.True(sum > 0);
        TestContext.Current.TestOutputHelper?.WriteLine($"MD-006 advisory: 10,000 ChargeMisdelivery + ChargeLate calls allocated {bytes} B (0 B gate blocking from M0, E2).");
    }
}
