using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using static PerformativeMail.Client.Core.Input.PhysicalInput;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 009: GDD F4, trigger hysteresis (<c>trigHyst</c> 150 ‰, release point floored at 50 ‰), through
/// <see cref="TriggerHysteresis.IsPressed"/> and, for the pad's Throw (LT) and Tool (RT), through <see cref="InputRouter.Feed"/>.
/// Values are Q15 (1.0 = 32767).
/// </summary>
public class TriggerHysteresisTests
{
    public static TheoryData<int, short[], bool[]> Sequences => new()
    {
        // trigOn 0.50: rising to 0.55 presses, 0.40 holds, 0.34 releases.
        { 500, [18022, 13107, 11141], [true, true, false] },

        // trigOn 0.15: the release point is the 0.05 floor (1639 is the first Q15 value at or above it).
        { 150, [6554, 1639, 1638], [true, true, false] },

        // A digital trigger at either end of the setting range.
        { 900, [32767, 0], [true, false] },
        { 150, [32767, 0], [true, false] },
    };

    [Theory]
    [MemberData(nameof(Sequences))]
    [Trait("Id", "H-F4-1")]
    public void Trigger_PressesAtTrigOn_AndReleasesBelowTheHysteresisPoint(int trigOnPm, short[] values, bool[] expected)
    {
        bool pressed = false;
        var seen = new List<bool>();
        foreach (short v in values)
        {
            pressed = TriggerHysteresis.IsPressed(pressed, v, trigOnPm);
            seen.Add(pressed);
        }

        Assert.Equal(expected, seen);
    }

    [Fact]
    [Trait("Id", "H-F4-1")]
    public void PadTriggers_ReachTheRouterAsThrowAndToolPresses()
    {
        var h = new InputTestHarness();
        var log = new UiEventLog();
        var router = new InputRouter(BuiltInActionMap.CreateM1a(), InputSettings.Defaults, h.Clock, h.Interaction, h.Presented, h.Carrier, h.ThrowAim, h.Ticks, log);

        Feed(router, h.Axis(PadLeftTrigger, 18022));
        Feed(router, h.Axis(PadLeftTrigger, 13107));
        router.FlushToLatch(h.Latch);
        List<EdgeKind> whileHeld = h.Latch.Edges.Select(e => e.Kind).ToList();
        h.AdvanceTicks(8);   // 266 ms: past throwMinAimMs, so the release throws (story 014)
        Feed(router, h.Axis(PadLeftTrigger, 11141));
        router.FlushToLatch(h.Latch);
        Feed(router, h.Axis(PadRightTrigger, 32767));

        Assert.Equal([EdgeKind.Press], whileHeld);
        Assert.Equal([(FrameAction.Aim, EdgeKind.Press), (FrameAction.Aim, EdgeKind.Release)], h.Latch.Edges.Select(e => (e.Action, e.Kind)));
        var fired = new List<(ushort Action, PhysicalInput Input)>();
        for (int i = 0; i < log.Count; i++)
        {
            if (log.TryGet(i, out UiEventEntry e) && e.Kind == LogKind.GateFired)
            {
                fired.Add((e.IdA, (PhysicalInput)e.IdB));
            }
        }

        Assert.Equal([(ActionId.Throw, PadLeftTrigger), (ActionId.Tool, PadRightTrigger)], fired);
    }

    private static void Feed(InputRouter router, RawInputRecord r) => router.Feed(in r);
}
