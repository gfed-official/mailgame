using PerformativeMail.Client.Core.Presentation.Delivery;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 006, the reply-table scope (GDD MD7, F4): <see cref="HandResponseTable"/> and <see cref="RiffleTiming"/>.
/// The <c>dead</c>/<c>frozen</c> and timeout rows and the beat controller wait (Mail Lead regroup 2026-11-08; godot-host-shell 006).
/// </summary>
public class HandResponseTableTests
{
    [Theory]
    [Trait("Id", "M-MD7-1")]
    [InlineData("ok, 1 entry", null, DeliverBranchKind.Match, 1, HandBeat.SlotIn, null, true)]
    [InlineData("ok, k = 2 (riffle)", null, DeliverBranchKind.Match, 2, HandBeat.SlotInRiffle, null, true)]
    [InlineData("ok, MISDELIVER_ONE", null, DeliverBranchKind.MisdeliverOne, 1, HandBeat.CarefulHandover, null, true)]
    [InlineData("wallet_floor", DeliveryReason.WalletFloor, DeliverBranchKind.MisdeliverOne, 0, HandBeat.PiecesOut, DeliverPromptKeys.FloorRefusalKey, false)]
    [InlineData("no_target", DeliveryReason.NoTarget, DeliverBranchKind.Match, 0, HandBeat.PiecesOut, DeliverPromptKeys.NothingKey, false)]
    [InlineData("closed", DeliveryReason.Closed, DeliverBranchKind.Match, 0, HandBeat.PiecesOut, DeliverPromptKeys.MailRejectClosedKey, false)]
    [InlineData("not_on_route", DeliveryReason.NotOnRoute, DeliverBranchKind.Match, 0, HandBeat.PiecesOut, DeliverPromptKeys.MailRejectNotOnRouteKey, false)]
    [InlineData("out_of_range", DeliveryReason.OutOfRange, DeliverBranchKind.Match, 0, HandBeat.PiecesOut, DeliverPromptKeys.ReplyTooFarKey, false)]
    [InlineData("position_stale", DeliveryReason.PositionStale, DeliverBranchKind.Match, 0, HandBeat.PiecesOut, DeliverPromptKeys.ReplyTooFarKey, false)]
    [InlineData("dry_land", DeliveryReason.DryLand, DeliverBranchKind.Match, 0, HandBeat.PiecesOut, DeliverPromptKeys.DryLandKey, false)]
    public void Reply_MapsToItsHandResponse(
        string row, DeliveryReason? reason, DeliverBranchKind latched, int got, HandBeat beat, string? key, bool slotIn)
    {
        // Arrange
        var op = new Op { Kind = DeliveryOpKinds.DeliverMatching, ReqId = 7 };
        var extra = new InvReplyExtra { RequestedEntries = (ushort)got, GotEntries = (ushort)got };
        Reply reply = reason is { } r ? Reply.Reject(in op, (ushort)r) : Reply.Ok(in op, in extra);

        // Act
        HandResponse response = HandResponseTable.For(in reply, latched);

        // Assert
        StringId expectedKey = key is null ? StringId.None : Assert.Single(DeliverPromptKeys.All, k => k.Spelling == key).Id;
        Assert.True(new HandResponse(beat, expectedKey, slotIn) == response, row + ": " + response);
    }

    [Theory]
    [Trait("Id", "M-EC-1")]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 120)]
    [InlineData(5, 5, 300)]
    [InlineData(9, 5, 300)]
    [InlineData(26, 5, 300)]
    public void Riffle_ShowsMinKFiveStacks_WithinThreeHundredMs(int k, int stacks, int totalMs)
    {
        // Act
        RiffleTiming r = RiffleTiming.For(k);

        // Assert
        Assert.Equal(new RiffleTiming(stacks, totalMs), r);
    }
}
