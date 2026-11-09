using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Presentation.Delivery;

/// <summary>The hand-side beat a <c>deliver_matching</c> reply plays (Manual Delivery MD7). Explicit, append-only values.</summary>
public enum HandBeat : byte
{
    /// <summary>No hand beat: the knockdown or on-break presentation wins (<c>dead</c>, <c>frozen</c>).</summary>
    None = 0,

    /// <summary>The signature beat ends with the pieces slotting in (one entry).</summary>
    SlotIn = 1,

    /// <summary>The slot-in plus the sweep riffle (k ≥ 2 entries, <see cref="RiffleTiming"/>).</summary>
    SlotInRiffle = 2,

    /// <summary>A MISDELIVER_ONE hold's single piece leaves (the careful handover).</summary>
    CarefulHandover = 3,

    /// <summary>The pieces come back out into the hand.</summary>
    PiecesOut = 4,
}

/// <summary>What the hand does for one reply: the beat, the reason strip's key (<see cref="StringId.None"/> for none) and whether the pieces go in.</summary>
/// <param name="Beat">The beat.</param>
/// <param name="ReasonKey">The reason strip's key.</param>
/// <param name="SlotIn">True when the pieces go into the box.</param>
public readonly record struct HandResponse(HandBeat Beat, StringId ReasonKey, bool SlotIn);

/// <summary>
/// MD7's reply → hand-response table (manual-delivery story 006 AC-1, the data half). A pure lookup over the reply and the hold's
/// latched branch; it starts nothing. <see cref="HandBeatController"/> plays it from its <c>PresentationEventStream</c> cursor.
/// The 5 s timeout row is not a reply and is not in this table.
/// </summary>
/// <example><code>HandResponse r = HandResponseTable.For(in reply, DeliverBranchKind.Match);   // (SlotInRiffle, None, true) for k = 3</code></example>
public static class HandResponseTable
{
    /// <summary>The hand response to <paramref name="reply"/> for a hold latched on <paramref name="latched"/>.</summary>
    /// <param name="reply">The <c>deliver_matching</c> reply (an <c>ok</c> carries <see cref="InvReplyExtra"/>).</param>
    /// <param name="latched">The branch the hold latched.</param>
    /// <returns>The response.</returns>
    public static HandResponse For(in Reply reply, DeliverBranchKind latched)
    {
        if (reply.IsOk)
        {
            if (latched == DeliverBranchKind.MisdeliverOne)
            {
                return new HandResponse(HandBeat.CarefulHandover, StringId.None, true);
            }

            int k = reply.ExtraAs<InvReplyExtra>().GotEntries;
            return new HandResponse(k >= 2 ? HandBeat.SlotInRiffle : HandBeat.SlotIn, StringId.None, true);
        }

        return (DeliveryReason)reply.Reason switch
        {
            DeliveryReason.WalletFloor => Out(DeliverPromptKeys.FloorRefusal),
            DeliveryReason.NoTarget => Out(DeliverPromptKeys.Nothing),
            DeliveryReason.Closed => Out(DeliverPromptKeys.MailRejectClosed),
            DeliveryReason.NotOnRoute => Out(DeliverPromptKeys.MailRejectNotOnRoute),
            DeliveryReason.OutOfRange or DeliveryReason.PositionStale => Out(DeliverPromptKeys.ReplyTooFar),
            DeliveryReason.DryLand => Out(DeliverPromptKeys.DryLand),
            DeliveryReason.Dead or DeliveryReason.Frozen => new HandResponse(HandBeat.None, StringId.None, false),
            _ => Out(StringId.None),
        };
    }

    private static HandResponse Out(StringId key) => new(HandBeat.PiecesOut, key, false);
}
