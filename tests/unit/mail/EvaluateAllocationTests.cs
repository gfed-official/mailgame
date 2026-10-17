using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Mail-destinations story 003, AC-3 (M-PERF-1a): <c>MailRules.Evaluate</c> and <c>DeliveriesStateRules.Effective</c> allocate
/// nothing. Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class EvaluateAllocationTests
{
    [Fact]
    [Trait("Id", "M-PERF-1a")]
    public void Evaluate_Over10000Calls_AllocatesNothing_InOptimisedBuilds()
    {
        SkipWhenUnoptimised();
        AddressId address = AddressId.Pack(1, 1, 7);
        var open = new DestinationFacts(
            EntityId.Compose(EntityClass.Destination, 1), address, DefIndex.FromValue(0), DestState.Open, 0x1FUL, false);
        var closed = new DestinationFacts(
            EntityId.Compose(EntityClass.Destination, 1), address, DefIndex.FromValue(0), DestState.Closed, 0x1FUL, false);
        var accept = new UnitFacts(DefIndex.FromValue(1), ItemCategory.Mail, address, 5, 0, MailFlags.None);
        var other = new UnitFacts(DefIndex.FromValue(1), ItemCategory.Mail, AddressId.Pack(1, 1, 8), 5, 0, MailFlags.None);
        long sum = 0;

        AllocAssert.Zero("MailRules.Evaluate x10000", () =>
        {
            long local = 0;
            for (int i = 0; i < 10_000; i++)
            {
                DeliverEval a = MailRules.Evaluate(in open, in accept);
                DeliverEval b = MailRules.Evaluate(in open, in other);
                DeliverEval c = MailRules.Evaluate(in closed, in accept);
                local += (byte)a.Result + (byte)b.Result + (byte)c.Result + (byte)c.Reason;
            }

            sum = local;
        });

        Assert.Equal(10_000L * (1 + 2 + 0 + 1), sum);
    }

    [Fact]
    [Trait("Id", "M-PERF-1a")]
    public void Effective_Over10000Calls_AllocatesNothing_InOptimisedBuilds()
    {
        SkipWhenUnoptimised();
        long sum = 0;

        AllocAssert.Zero("DeliveriesStateRules.Effective x10000", () =>
        {
            long local = 0;
            for (int i = 0; i < 10_000; i++)
            {
                uint tick = (uint)(i % 2000);
                local += (byte)DeliveriesStateRules.Effective(true, false, 1000u, 999u, tick, 90);
                local += (byte)DeliveriesStateRules.Effective(false, true, 1000u, null, tick, 90);
            }

            sum = local;
        });

        Assert.True(sum > 0);
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(MailRules).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }
}
