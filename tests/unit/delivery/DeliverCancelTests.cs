using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 004 (GDD MD4; TR-delivery-007; ADR-0014 section 4): the Deliver hold's extra cancels, checked every held tick up
/// to and including completion, through a ClientLayer (<see cref="DeliverClientRig"/>). A 12-tick hold is latched at tick 0.
/// </summary>
public class DeliverCancelTests
{
    private const int HoldTicks = 12;

    [Theory]
    [Trait("Id", "M-MD4-2")]
    [InlineData("a: MATCH loses its matches (snap-back)", false, DeliverPromptKeys.CancelPromptChangedKey)]
    [InlineData("b: a match appears during MISDELIVER_ONE", true, DeliverPromptKeys.CancelPromptChangedKey)]
    [InlineData("c: the selected slot empties", true, DeliverPromptKeys.CancelPromptChangedKey)]
    [InlineData("d: swim depth", false, DeliverPromptKeys.DryLandKey)]
    [InlineData("e: an own throw becomes pending", false, DeliverPromptKeys.PendingKey)]
    [InlineData("f: MATCH k drops from 3 to 2", false, null)]
    public void HeldTickChange_CancelsWithItsReason_OrCompletesWhenStillTheSameBranch(string row, bool misdeliver, string? expectedKey)
    {
        // Arrange
        var rig = Latched(misdeliver, matches: 3);

        // Act
        for (int k = 1; k <= HoldTicks; k++)
        {
            if (k == 6)
            {
                Change(rig, row[0]);
            }

            rig.Tick();
        }

        // Assert
        if (expectedKey is null)
        {
            Assert.Equal(InteractState.Complete, rig.Interaction.State);
            Assert.Equal(1, rig.Requests.Sent);
            return;
        }

        Assert.Equal(InteractState.Idle, rig.Interaction.State);
        Assert.Equal(Key(expectedKey), rig.Interaction.LastCancelReason);
        Assert.Equal(0, rig.Publisher.Current.ProgressPm);
        Assert.Equal(0, rig.Requests.Attempts);
    }

    [Theory]
    [Trait("Id", "M-MD4-3")]
    [InlineData("slot 3 to 4 at tick 5", true, DeliverPromptKeys.CancelSelectionChangedKey)]
    [InlineData("3 to 4 to 3 inside tick 5", true, null)]
    [InlineData("the selected entry retired (stale_entry)", true, DeliverPromptKeys.CancelSelectionChangedKey)]
    [InlineData("MATCH hold, slot 3 to 4", false, null)]
    public void SelectionChange_CancelsAMisdeliverOneHoldOnly_ComparedAtTheTickBoundary(string row, bool misdeliver, string? expectedKey)
    {
        // Arrange: slot 3 selected; slot 4 holds another wrong-house piece.
        var rig = Latched(misdeliver, matches: 1, slot: 3);

        // Act
        for (int k = 1; k <= HoldTicks; k++)
        {
            if (k == 5)
            {
                if (row.Contains("stale", StringComparison.Ordinal))
                {
                    rig.Panel.Hotbar[3] = rig.Letter(rig.FarAddress);   // a new id in the same slot
                }
                else
                {
                    rig.Source.SelectedSlot = 4;
                    if (row.Contains("to 3 inside", StringComparison.Ordinal))
                    {
                        rig.Source.SelectedSlot = 3;
                    }
                }
            }

            rig.Tick();
        }

        // Assert
        if (expectedKey is null)
        {
            Assert.Equal(InteractState.Complete, rig.Interaction.State);
            Assert.Equal(1, rig.Requests.Sent);
            return;
        }

        Assert.Equal(InteractState.Idle, rig.Interaction.State);
        Assert.Equal(Key(expectedKey), rig.Interaction.LastCancelReason);
        Assert.Equal(0, rig.Requests.Attempts);
    }

    // A hold latched at tick 0 on a settled branch: MATCH with `matches` letters for #Home, or MISDELIVER_ONE on the selected slot.
    private static DeliverClientRig Latched(bool misdeliver, int matches, int slot = 2)
    {
        var rig = new DeliverClientRig();
        rig.Panel.Hotbar[slot] = rig.Letter(rig.FarAddress);
        rig.Panel.Hotbar[4] = rig.Letter(rig.FarAddress);
        rig.Source.SelectedSlot = slot;
        for (int i = 0; !misdeliver && i < matches; i++)
        {
            rig.Panel.Inventory.Add(rig.Letter(rig.HomeAddress));
        }

        rig.FocusOn(rig.Home);
        for (int i = 0; i < 6; i++)
        {
            rig.Tick();
        }

        rig.PressEdge(rig.Home);
        rig.Tick();
        Assert.Equal(InteractState.Holding, rig.Interaction.State);
        return rig;
    }

    private static void Change(DeliverClientRig rig, char row)
    {
        switch (row)
        {
            case 'a':
                rig.Panel.Inventory.Clear();
                break;
            case 'b':
                rig.Panel.Inventory.Add(rig.Letter(rig.HomeAddress));
                break;
            case 'c':
                rig.Panel.Hotbar[2] = default;
                break;
            case 'd':
                rig.SetStatus(swimming: true);
                break;
            case 'e':
                rig.Source.Pending.Add(ThrowOpKinds.ThrowRelease);
                break;
            default:
                rig.Panel.Inventory.RemoveAt(0);
                break;
        }
    }

    private static StringId Key(string spelling) => Assert.Single(DeliverPromptKeys.All, k => k.Spelling == spelling).Id;
}
