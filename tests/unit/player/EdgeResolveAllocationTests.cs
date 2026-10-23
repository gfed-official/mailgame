using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 016: <c>EdgeResolveStep.Step</c> allocates 0 B over mixed press edges (ADR-0016 l.192). It times the whole
/// <c>ClientLayer.Step</c> (the focus pass at 4a included), so it is stricter than the AC, which covers the edge resolve only.
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class EdgeResolveAllocationTests
{
    [Fact]
    public void Step_OverMixedPressEdges_AllocatesZeroBytesAfter300WarmUpTicks()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(AnchorRegistry).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        var rig = new PressRig();
        int tick = 0;
        void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++, tick++)
            {
                rig.ScriptTick(tick);
            }
        }

        Run(300);
        int sent = rig.Requests.Sent;
        int downs = rig.CatchArm.Downs;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("EdgeResolveStep.Step x 300 mixed press edges", () => Run(300));
        Assert.True(rig.Requests.Sent > sent && rig.CatchArm.Downs > downs, "the measured window should send requests and arm catches");
    }
}
