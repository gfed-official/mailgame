using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Delivery.Client;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 003 (GDD MD2, MD3; TR-delivery-002, TR-delivery-003; ADR-0016 sections 2 and 4, ADR-0014 section 1): the
/// client Deliver hook's availability and the 4b preview step through a ClientLayer (<see cref="DeliverClientRig"/>). The host half of
/// MD2 (rows on host state) runs through <c>CheckInteraction</c> in the integration <c>DeliverMatchingHandlerTests</c>. Story 003's
/// greybox scope drops the timeout and rejoin rows of M-MD2-2 (Mail Lead regroup 2026-11-08).
/// </summary>
public class DeliverAvailabilityTests
{
    [Theory]
    [Trait("Id", "M-MD2-1")]
    [InlineData("1", DeliverPromptKeys.ClosePanelKey)]
    [InlineData("2", DeliverPromptKeys.DryLandKey)]
    [InlineData("3:deliver_matching", DeliverPromptKeys.PendingKey)]
    [InlineData("3:throw", DeliverPromptKeys.PendingKey)]
    [InlineData("3:drop", DeliverPromptKeys.PendingKey)]
    [InlineData("3:take_all", DeliverPromptKeys.PendingKey)]
    [InlineData("3:sort", DeliverPromptKeys.SortingKey)]
    [InlineData("4", DeliverPromptKeys.MailRejectClosedKey)]
    [InlineData("5", DeliverPromptKeys.MailRejectNotOnRouteKey)]
    [InlineData("6", DeliverPromptKeys.NothingKey)]
    [InlineData("1+2", DeliverPromptKeys.ClosePanelKey)]
    [InlineData("2+3", DeliverPromptKeys.DryLandKey)]
    [InlineData("3+4", DeliverPromptKeys.PendingKey)]
    [InlineData("4+5", DeliverPromptKeys.MailRejectClosedKey)]
    [InlineData("5+6", DeliverPromptKeys.MailRejectNotOnRouteKey)]
    [InlineData("none", null)]
    public void IsAvailable_EachRowAndAdjacentPair_ReturnsTheLowerRowsKey(string rows, string? expectedKey)
    {
        // Arrange: a matching letter unless row 6 is asked for.
        var rig = new DeliverClientRig();
        bool nothing = rows.Contains('6', StringComparison.Ordinal);
        if (!nothing)
        {
            rig.Panel.Inventory.Add(rig.Letter(rig.HomeAddress));
        }

        rig.FocusOn(rig.Home);
        bool closed = rows.Contains('4', StringComparison.Ordinal);
        bool locked = rows.Contains('5', StringComparison.Ordinal);
        rig.SetDeliveries(open: !closed, unlockedMask: locked ? 0b10UL : 0b01UL);
        rig.SetStatus(panel: rows.Contains('1', StringComparison.Ordinal), swimming: rows.Contains('2', StringComparison.Ordinal));
        if (rows.StartsWith("3:", StringComparison.Ordinal))
        {
            rig.Source.Pending.Add(OpKindOf(rows[2..]));
        }
        else if (rows.Contains('3', StringComparison.Ordinal))
        {
            rig.Source.Pending.Add(DeliveryOpKinds.DeliverMatching);
        }

        rig.Tick();

        // Act
        bool available = rig.IsAvailable(rig.Home, out StringId reason);

        // Assert
        StringId expected = expectedKey is null ? StringId.None : Assert.Single(DeliverPromptKeys.All, k => k.Spelling == expectedKey).Id;
        Assert.Equal(expectedKey is null, available);
        Assert.Equal(expected, reason);
    }

    [Fact]
    [Trait("Id", "M-MD2-2")]
    public void Pending_IsReadFromThePendingRequests_AtEveryMailbox_AndClearsOnTheReplyTick()
    {
        // Arrange: an own deliver_matching in flight.
        var rig = new DeliverClientRig();
        rig.Panel.Inventory.Add(rig.Letter(rig.HomeAddress));
        rig.Panel.Inventory.Add(rig.Letter(rig.NextAddress));
        rig.FocusOn(rig.Home);
        rig.Source.Pending.Add(DeliveryOpKinds.DeliverMatching);
        rig.Tick();
        bool home = rig.IsAvailable(rig.Home, out StringId homeReason);
        bool next = rig.IsAvailable(rig.Next, out StringId nextReason);

        // Act: the reply retires it (MirrorDrain, the first step of the tick); the hook is asked before any later step runs.
        rig.Source.Pending.Clear();
        bool afterReply = rig.IsAvailable(rig.Home, out StringId afterReason);

        // Assert
        Assert.False(home);
        Assert.False(next);
        Assert.Equal(DeliverPromptKeys.Pending, homeReason);
        Assert.Equal(DeliverPromptKeys.Pending, nextReason);
        Assert.True(afterReply);
        Assert.Equal(StringId.None, afterReason);
    }

    [Fact]
    [Trait("Id", "M-MD3-3")]
    public void Preview_EvaluatesOncePerFocusedTick_NeverWhileIdle_IdenticallyOnTwoClients_AndScansNothingWhenClosed()
    {
        // Arrange: two clients with identical inputs, a match and a wrong-house piece selected.
        var a = new DeliverClientRig();
        var b = new DeliverClientRig();
        foreach (DeliverClientRig rig in new[] { a, b })
        {
            rig.Panel.Inventory.Add(rig.Letter(rig.HomeAddress));
            rig.Panel.Hotbar[2] = rig.Letter(rig.NextAddress);
            rig.Source.SelectedSlot = 2;
        }

        var idle = new DeliverClientRig();
        idle.Carrier.Feet = new Vec3Mm(5_000, 0, 5_000);   // nothing within reach: IDLE
        a.FocusOn(a.Home);
        b.FocusOn(b.Home);
        int before = a.Preview.Scratch.Evaluations;

        // Act
        for (int t = 0; t < 30; t++)
        {
            a.Tick();
            b.Tick();
            idle.Tick();
            Assert.Equal(a.State.Branch.Kind, b.State.Branch.Kind);
            Assert.Equal(a.State.Branch.K, b.State.Branch.K);
            Assert.Equal(a.State.Branch.PredictedPay, b.State.Branch.PredictedPay);
            Assert.Equal(a.State.DisplayedTicks, b.State.DisplayedTicks);
        }

        int evaluated = a.Preview.Scratch.Evaluations - before;
        int scannedOpen = a.Preview.Scratch.ScannedEntries;
        a.SetDeliveries(open: false, unlockedMask: 1UL);
        a.Tick();

        // Assert
        Assert.False(idle.State.Active);
        Assert.Equal(0, idle.Preview.Scratch.Evaluations);
        Assert.Equal(DeliverBranchKind.Reject, a.State.Branch.Kind);
        Assert.Equal(scannedOpen, a.Preview.Scratch.ScannedEntries);
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(30, evaluated);
            Assert.True(scannedOpen > 0);
        }
    }

    private static OpKind OpKindOf(string name) => name switch
    {
        "deliver_matching" => DeliveryOpKinds.DeliverMatching,
        "throw" => ThrowOpKinds.ThrowRelease,
        "drop" => InvOpKinds.Drop,
        "take_all" => InvOpKinds.TakeAll,
        _ => InvOpKinds.Sort,
    };
}
