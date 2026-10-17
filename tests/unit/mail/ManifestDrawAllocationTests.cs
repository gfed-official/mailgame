using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>Covers the 0 B clause of mail-destinations story 012: the manifest roll allocates nothing. Joins the non-parallel collection (see <see cref="AllocAssert"/>).</summary>
[Collection(NonParallelCollection.Name)]
public class ManifestDrawAllocationTests
{
    [Fact]
    [Trait("Id", "M-PERF-1e")]
    public void Manifest_roll_allocates_nothing_in_optimised_builds()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(ManifestDraws).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        var registry = new RngRegistry(1UL, new SimCounters());
        IRngStreams view = registry.ViewFor(SystemId.MailDestinations);
        BatchKnobs k = BatchKnobs.Default;
        var plan = new BatchPlan(0, 300, 90, 708);
        AddressId[] houses = DrawTestRig.Fifty();
        var kinds = new byte[708];
        var ticks = new long[20];
        var counts = new int[20];
        var addr = new AddressId[708];
        var cursed = new bool[708];
        long nbSum = 0;

        (long bytes, int runs) = AllocAssert.Measure(() =>
        {
            nbSum = 0;
            for (int i = 0; i < 20; i++)
            {
                RngStream mail = view.Create(in RngNames.Mail, 5);
                nbSum += ManifestDraws.Run(mail, in plan, in k, 100, houses, kinds, ticks, counts, addr, cursed);
            }
        });

        Assert.Equal(0L, bytes);
        Assert.Equal(400L, nbSum);
        Assert.True(runs >= 3);
    }
}
