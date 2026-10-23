using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>Player Character story 014: <c>FocusStep.Step</c> allocates 0 B with 2 000 anchors registered (ADR-0016 l.192).</summary>
[Collection(NonParallelCollection.Name)]
public sealed class FocusAllocationTests
{
    [Fact]
    public void Step_With2000Anchors_AllocatesZeroBytesAfter300WarmUpTicks()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(AnchorRegistry).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange: the lattice, the carrier 500 mm off a lattice point on both axes (no anchor at d = 0); the look turns so
        // focus and the dwell keep changing.
        var rig = new FocusRig(maxAnchors: 2048);
        rig.AddLattice();
        rig.Source.Feet = new Vec3Mm(FocusRig.Feet.X + 500, 0, FocusRig.Feet.Z + 500);
        int tick = 0;
        int changes = 0;
        void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++, tick++)
            {
                rig.Look((ushort)(tick * 1311));
                rig.State.Latched = tick % 40 < 5;
                AnchorRef before = rig.State.Focused;
                if (rig.Tick() != before)
                {
                    changes++;
                }
            }
        }

        Run(300);
        changes = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("FocusStep.Step x 100 with 2000 anchors", () => Run(100));
        Assert.False(rig.State.Focused.IsNone);
        Assert.True(changes > 0, "focus should change in the measured window");
    }
}
