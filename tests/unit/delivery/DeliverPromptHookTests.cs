using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 010, the hook half of AC-1 and AC-2's purity (GDD UI Requirements, MD2, MD3): the client
/// <c>IInteractable.WritePrompt</c> writes through <see cref="DeliverInteractable.WritePrompt(in DeliverPromptInput, ref Players.PromptArgs)"/>
/// from story 003's published state, read back from Players' interaction snapshot. The rig's hooks run inside
/// <see cref="PureHookGuard"/>, which throws if any hook changes the published preview state. The row table itself is
/// <see cref="DeliverPromptDataTests"/>'.
/// </summary>
public class DeliverPromptHookTests
{
    [Theory]
    [Trait("Id", "M-UI-1")]
    [InlineData("MATCH", DeliverPromptBranch.Match, DeliverPromptKeys.MatchPiecesKey, 0, DeliverPromptFlags.None)]
    [InlineData("MISDELIVER_ONE, settling", DeliverPromptBranch.MisdeliverOne, DeliverPromptKeys.MismatchPieceForKey, 2, DeliverPromptFlags.Settling)]
    [InlineData("row 1, panel open", DeliverPromptBranch.Unavailable, DeliverPromptKeys.ClosePanelKey, 0, DeliverPromptFlags.None)]
    public void SnapshotPrompt_IsTheFocusedDeliverRow_WrittenByThePureHook(
        string row, DeliverPromptBranch branch, string key, int addressLot, DeliverPromptFlags flags)
    {
        // Arrange
        var rig = new DeliverClientRig();
        if (row.StartsWith("MISDELIVER", StringComparison.Ordinal))
        {
            rig.Panel.Hotbar[2] = rig.Letter(rig.FarAddress);
            rig.Source.SelectedSlot = 2;
        }
        else
        {
            rig.Panel.Inventory.Add(rig.Letter(rig.HomeAddress));
        }

        rig.FocusOn(rig.Home);
        rig.SetStatus(panel: row.StartsWith("row 1", StringComparison.Ordinal));

        // Act
        rig.Tick();

        // Assert
        Players.PromptArgs args = rig.Publisher.Current.PromptArgs;
        Assert.True(DeliverPromptArgs.TryRead(in args, out DeliverPromptArgs back));
        Assert.Equal(branch, back.Branch);
        Assert.Equal(Assert.Single(DeliverPromptKeys.All, k => k.Spelling == key).Id, back.Key);
        Assert.Equal(rig.DisplayOf(addressLot), back.Address);
        Assert.Equal(flags, back.Flags);
    }
}
