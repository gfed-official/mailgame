using PerformativeMail.Sim.MailDestinations;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Covers the 0 B clause of mail-destinations story 011: F1 and F3 allocate nothing. Joins the non-parallel collection
/// (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class KindCountsSpawnValueAllocationTests
{
    [Fact]
    [Trait("Id", "M-F3-2")]
    public void SpawnValue_and_KindCounts_allocate_nothing_in_optimised_builds()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SpawnValue).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        MailValueKnobs k = MailValueKnobs.Default;
        var districts = new int[50];
        for (int i = 0; i < districts.Length; i++)
        {
            districts[i] = (i / 10) + 1;
        }

        MeanDist md = MeanDist.Create(districts, k.DistanceStepPm);
        var shares = new[]
        {
            new KindShare(4, 100, -1, true),
            new KindShare(8, 200, -1, true),
            new KindShare(30, 200, -1, true),
            new KindShare(70, 200, -1, true),
            new KindShare(160, 150, -1, true),
            new KindShare(600, 150, 4, false),
        };
        var n = new int[6];
        var r = new Int128[6];
        long sum = 0;

        (long bytes, int runs) = AllocAssert.Measure(() =>
        {
            sum = 0;
            for (int i = 0; i < 100; i++)
            {
                sum += SpawnValue.Compute(70, 4, 3, true, false, in k);
                sum += SpawnValue.Compute(160, 5, 5, false, true, in k);
                sum += KindCounts.Compute(24_704, shares, in md, in k, 5, n);
                sum += KindCounts.Compute(24_704, shares, in md, in k, 5, n, r);
                sum += MeanDist.Create(districts, k.DistanceStepPm).HouseCount;
            }
        });

        Assert.Equal(0L, bytes);
        Assert.True(sum > 0);
        Assert.True(runs >= 3);
    }
}
