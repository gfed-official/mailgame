using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Mail-destinations story 004 (TR-mail-001, ADR-0017 section 4): <see cref="MailPay"/> allocates nothing. The sheet is built
/// before the measured window. Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class MailPayAllocationTests
{
    [Fact]
    [Trait("Id", "M-F6-1")]
    public void UnitPayUnitPenaltyAndHintPayout_Over10000Calls_AllocateNothing_InOptimisedBuilds()
    {
        SkipWhenUnoptimised();
        ContentSet content = BuiltInContent.CreateM1a();
        var stamp = new Modifier(
            SourceKind.Perk, DefIndex.FromValue(0), 0, PlayerSlot.None, Stat.MailValueMultPm, 1, ModifierOp.Mul, 1250, ModifierLifetime.Run);
        IStatSheet sheet = StatSheet.Build(content, new StatSources([stamp]));
        var unit = new UnitFacts(DefIndex.FromValue(1), ItemCategory.Mail, AddressId.None, 14, 1, MailFlags.None);
        HintRun[] runs = [new HintRun(1, 3, 8), new HintRun(2, 2, 9)];
        long[] block = new long[SimCounterSink.RequiredLength];
        long sum = 0;

        AllocAssert.Zero("MailPay x10000", () =>
        {
            var sink = new SimCounterSink(block);
            long local = 0;
            for (int i = 0; i < 10_000; i++)
            {
                local += MailPay.UnitPay(in unit, 500, 1000, sheet, null, ref sink).Value;
                local += MailPay.UnitPenalty(in unit, 500, 1, sheet, ref sink).Value;
                local += MailPay.HintPayout(runs, 2, unit.Kind, 500, 2, 1000, sheet, null, ref sink).Value;
            }

            sum = local;
        });

        Assert.True(sum > 0);
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(MailPay).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }
}
