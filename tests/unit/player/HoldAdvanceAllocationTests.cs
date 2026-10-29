using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 017: <c>HoldAdvanceStep.Step</c> allocates 0 B over the seeded <see cref="HoldFuzz"/> (ADR-0016 l.192).
/// Advisory until M0 (ADR-0014 erratum E2). Like <see cref="EdgeResolveAllocationTests"/> it times the whole <c>ClientLayer.Step</c>
/// (4a focus and 4d edges included), so it is stricter than the AC.
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class HoldAdvanceAllocationTests
{
    [Fact]
    [Trait("Id", "PC017-COUNTERS")]
    public void Step_Over10000FuzzedPresses_AllocatesZeroBytesAfterWarmUp()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(AnchorRegistry).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        var fuzz = new HoldFuzz();
        void Run()
        {
            int target = fuzz.Presses + 10_000;
            while (fuzz.Presses < target)
            {
                fuzz.Step();
            }
        }

        Run();
        long completed = fuzz.Rig.Counters[CounterId.HoldsCompleted];

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("HoldAdvanceStep.Step x 10 000 fuzzed presses", Run, warmUpRuns: 0);
        Assert.True(fuzz.Rig.Counters[CounterId.HoldsCompleted] > completed, "the measured window should complete holds");
    }
}
