using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.Players;
using static PerformativeMail.Client.Core.Input.PhysicalInput;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 008, H-R6-2 (g), (h) and (j) at M-1a: one binding means one thing per state, and the state is reached
/// through the router's own transitions (Tab) and the published carrier status (holding). One row per cell.
/// </summary>
public class DisjointStateBindingsTests
{
    private static readonly ActionMap _map = BuiltInActionMap.CreateM1a();

    public enum Context
    {
        World,
        Panel,
        HoldingOpen,
        HoldingClosed,
    }

    [Theory]
    [Trait("Id", "H-R6-2")]
    [InlineData(Context.World, PadA, ActionId.Jump)]
    [InlineData(Context.HoldingClosed, PadA, ActionId.Jump)]
    [InlineData(Context.Panel, PadA, ActionId.PickPlace)]
    [InlineData(Context.HoldingOpen, PadA, ActionId.PickPlace)]
    [InlineData(Context.World, MouseLeft, ActionId.Tool)]
    [InlineData(Context.Panel, MouseLeft, ActionId.PickPlace)]
    [InlineData(Context.HoldingClosed, MouseLeft, ActionId.ToolWhileHolding)]
    [InlineData(Context.Panel, KeyEnter, ActionId.PickPlace)]
    [InlineData(Context.Panel, KeyNumpadEnter, ActionId.PickPlace)]
    public void Press_FiresTheOneActionItsStateBinds(Context context, PhysicalInput input, ushort expected)
    {
        // Arrange
        var h = new InputTestHarness();
        var log = new UiEventLog();
        var router = new InputRouter(_map, InputSettings.Defaults, h.Clock, h.Interaction, h.Presented, h.Carrier, h.ThrowAim, h.Ticks, log);
        if (context is Context.Panel or Context.HoldingOpen)
        {
            Feed(router, h.Press(KeyTab));
            Feed(router, h.Release(KeyTab));
        }

        h.Carrier.Set(new CarrierStatus { CursorHolding = context is Context.HoldingOpen or Context.HoldingClosed });
        int before = log.Count;

        // Act
        Feed(router, h.Press(input));

        // Assert: exactly one gate decision for the press, and it fired the expected action.
        var gates = new List<(LogKind, ushort)>();
        for (int i = before; i < log.Count; i++)
        {
            _ = log.TryGet(i, out UiEventEntry e);
            if (e.Kind is LogKind.GateFired or LogKind.GateNotLive && (PhysicalInput)e.IdB == input)
            {
                gates.Add((e.Kind, e.IdA));
            }
        }

        Assert.Equal([(LogKind.GateFired, expected)], gates);
    }

    private static void Feed(InputRouter router, RawInputRecord r) => router.Feed(in r);
}
