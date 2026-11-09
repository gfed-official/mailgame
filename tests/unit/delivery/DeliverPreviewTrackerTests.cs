using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.MailDestinations;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 003 AC-4 (GDD MD3 settle tracker, MD4; TR-delivery-004; ADR-0016 section 4): the
/// <c>DeliverPreviewTracker</c> as the 4b step publishes it, in client Sim ticks, through a ClientLayer (<see cref="DeliverClientRig"/>).
/// </summary>
public class DeliverPreviewTrackerTests
{
    private const int ChangeAt = 3;

    [Theory]
    [InlineData("no change", false)]
    [InlineData("another selected entry", true)]
    [InlineData("branch kind (a match appears)", true)]
    [InlineData("destination (press presents the other mailbox)", true)]
    [InlineData("rows 1-5 (panel open one tick)", true)]
    [InlineData("cents", false)]
    [InlineData("floorRefusal", false)]
    [InlineData("cursed", false)]
    public void Tracker_MisdeliverOneSettlesAfterSixTicks_ResetsOnKeyChangesAndRows1To5_NotOnPayload(string change, bool resets)
    {
        // Arrange: NOTHING focused, then a wrong-house piece selected in slot 2 first shown at T.
        var rig = new DeliverClientRig();
        rig.FocusOn(rig.Home);
        rig.Panel.Hotbar[2] = rig.Letter(rig.FarAddress);
        rig.Source.SelectedSlot = 2;
        var shown = new List<(uint Displayed, bool CanStart)>();
        var destinations = new List<Primitives.EntityId>();

        // Act
        for (int k = 0; k <= ChangeAt + 7; k++)
        {
            if (k == ChangeAt)
            {
                Change(rig, change);
            }
            else if (k == ChangeAt + 1)
            {
                rig.SetStatus();
            }

            rig.Tick();
            shown.Add((rig.State.DisplayedTicks, rig.State.CanStartHoldNext));
            destinations.Add(rig.State.Destination);
        }

        // Assert: T..T+2 untouched by any row.
        for (int k = 0; k < ChangeAt; k++)
        {
            Assert.Equal(((uint)k, false), shown[k]);
        }

        if (!resets)
        {
            Assert.Equal(DeliverBranchKind.MisdeliverOne, rig.State.Branch.Kind);
            Assert.Equal(Enumerable.Range(0, shown.Count).Select(k => ((uint)k, k >= 6)), shown);
            return;
        }

        // A reset: the key is new from the change (or, after a rows 1-5 tick, from the next available tick).
        int since = change.StartsWith("rows", StringComparison.Ordinal) ? ChangeAt + 1 : ChangeAt;
        bool match = change.StartsWith("branch", StringComparison.Ordinal);
        Assert.Equal((0u, match), shown[ChangeAt]);
        if (change.StartsWith("destination", StringComparison.Ordinal))
        {
            // The press-tick preview judged the presented mailbox for that one tick; back on the focus, the key changes again.
            Assert.Equal(rig.Next.Entity, destinations[ChangeAt]);
            since = ChangeAt + 1;
        }

        for (int k = since; k < shown.Count; k++)
        {
            Assert.Equal((uint)(k - since), shown[k].Displayed);
            Assert.Equal(match || k - since >= 6, shown[k].CanStart);
        }
    }

    private static void Change(DeliverClientRig rig, string change)
    {
        switch (change)
        {
            case "another selected entry":
                rig.Panel.Hotbar[3] = rig.Letter(rig.FarAddress);
                rig.Source.SelectedSlot = 3;
                break;
            case "branch kind (a match appears)":
                rig.Panel.Inventory.Add(rig.Letter(rig.HomeAddress));
                break;
            case "destination (press presents the other mailbox)":
                rig.PressEdge(rig.Next);
                break;
            case "rows 1-5 (panel open one tick)":
                rig.SetStatus(panel: true);
                break;
            case "cents":
                rig.Panel.Hotbar[2] = DeliverClientRig.Restyle(in rig.Panel.Hotbar[2], 0, 40);
                break;
            case "floorRefusal":
                rig.SetWallet(-498);
                break;
            case "cursed":
                rig.Panel.Hotbar[2] = DeliverClientRig.Restyle(in rig.Panel.Hotbar[2], (byte)MailFlags.Cursed, 8);
                break;
        }
    }
}
