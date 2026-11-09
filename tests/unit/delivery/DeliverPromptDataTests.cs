using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Manual-delivery story 010 (M-UI-1 data half; ADR-0014 section 7 l.115 and l.144; ADR-0004 section 6 erratum 2026-10-23): the Deliver
/// prompt as data. AC-1 and AC-3: every branch row written by <see cref="DeliverInteractable.WritePrompt(in DeliverPromptInput, ref PromptArgs)"/>
/// decodes through <see cref="DeliverPromptArgs.TryRead"/> to its key and typed arguments, with every unused byte zero (AC-2's size half).
/// AC-4: the <see cref="DeliverPromptKeys"/> table. The 0 B check (AC-2) is advisory until M0 and not added (M-1a regroup plan).
/// </summary>
public class DeliverPromptDataTests
{
    private static readonly StringId _house17 = new(StringNamespace.Run, 17);
    private static readonly StringId _house18 = new(StringNamespace.Run, 18);

    private static Cents C(long cents)
    {
        var sink = default(SimCounterSink);
        return Cents.FromCents(cents, ref sink);
    }

    /// <summary>Rows: name, input, expected decoded args (UI Requirements rows 1 to 6, then MATCH, MISDELIVER_ONE and REJECT variants).</summary>
    public static TheoryData<string, DeliverPromptInput, DeliverPromptArgs> Rows()
    {
        DeliverBranch nothing = new() { Kind = DeliverBranchKind.Nothing };
        DeliverBranch match = new() { Kind = DeliverBranchKind.Match, N = 4, PredictedPay = C(32) };
        DeliverBranch misdeliver = new() { Kind = DeliverBranchKind.MisdeliverOne, PredictedPenalty = C(4), FloorRefusal = true };
        DeliverBranch wontFit = new() { Kind = DeliverBranchKind.MisdeliverOne, WontFit = true };
        DeliverBranch closed = new() { Kind = DeliverBranchKind.Reject, RejectReason = MailRejectReason.Closed };
        DeliverBranch locked = new() { Kind = DeliverBranchKind.Reject, RejectReason = MailRejectReason.NotOnRoute };
        const DeliverPromptFlags None = DeliverPromptFlags.None;
        return new TheoryData<string, DeliverPromptInput, DeliverPromptArgs>
        {
            {
                "1 close_panel", new() { Branch = match, Unavailable = DeliverUnavailable.ClosePanel, DestinationAddress = _house17 },
                new(DeliverPromptBranch.Unavailable, None, DeliverPromptKeys.ClosePanel, _house17, 0, 0, 0)
            },
            {
                "2 dry_land", new() { Branch = nothing, Unavailable = DeliverUnavailable.DryLand, DestinationAddress = _house17 },
                new(DeliverPromptBranch.Unavailable, None, DeliverPromptKeys.DryLand, _house17, 0, 0, 0)
            },
            {
                "3 pending, settling", new() { Branch = match, Unavailable = DeliverUnavailable.Pending, Settling = true, DestinationAddress = _house17 },
                new(DeliverPromptBranch.Unavailable, DeliverPromptFlags.Settling, DeliverPromptKeys.Pending, _house17, 0, 0, 0)
            },
            {
                "3 sorting", new() { Branch = match, Unavailable = DeliverUnavailable.Sorting, DestinationAddress = _house17 },
                new(DeliverPromptBranch.Unavailable, None, DeliverPromptKeys.Sorting, _house17, 0, 0, 0)
            },
            {
                "4 closed (REJECT)", new() { Branch = closed, DestinationAddress = _house17 },
                new(DeliverPromptBranch.Reject, None, DeliverPromptKeys.MailRejectClosed, _house17, 0, 0, 0)
            },
            {
                "5 not_on_route (REJECT)", new() { Branch = locked, DestinationAddress = _house17 },
                new(DeliverPromptBranch.Reject, None, DeliverPromptKeys.MailRejectNotOnRoute, _house17, 0, 0, 0)
            },
            {
                "6 nothing", new() { Branch = nothing, DestinationAddress = _house17 },
                new(DeliverPromptBranch.Nothing, None, DeliverPromptKeys.Nothing, _house17, 0, 0, 0)
            },
            {
                "MATCH", new() { Branch = match, DestinationAddress = _house17 },
                new(DeliverPromptBranch.Match, None, DeliverPromptKeys.MatchPieces, _house17, 4, 32, 0)
            },
            {
                "MISDELIVER_ONE, floor refusal, settling", new() { Branch = misdeliver, Settling = true, DestinationAddress = _house17, SelectedAddress = _house18 },
                new(DeliverPromptBranch.MisdeliverOne, DeliverPromptFlags.FloorRefusal | DeliverPromptFlags.Settling, DeliverPromptKeys.MismatchPieceFor, _house18, 1, 0, 4)
            },
            {
                "MISDELIVER_ONE, wont_fit", new() { Branch = wontFit, DestinationAddress = _house17, SelectedAddress = _house18 },
                new(DeliverPromptBranch.MisdeliverOne, DeliverPromptFlags.WontFit, DeliverPromptKeys.MismatchPieceFor, _house18, 1, 0, 0)
            },
        };
    }

    [Theory]
    [Trait("Id", "M-UI-1")]
    [MemberData(nameof(Rows))]
    public void WritePrompt_EachBranchRow_DecodesToItsKeyAndArgs_WithTheUnusedBytesZero(string row, DeliverPromptInput input, DeliverPromptArgs expected)
    {
        // Arrange: start from non-zero bytes, so a byte the writer forgets is caught.
        var args = default(PromptArgs);
        ((Span<byte>)args).Fill(0xAB);
        var roundTrip = default(PromptArgs);

        // Act
        DeliverInteractable.WritePrompt(in input, ref args);
        bool read = DeliverPromptArgs.TryRead(in args, out DeliverPromptArgs decoded);
        DeliverPromptArgs.Write(in expected, ref roundTrip);
        bool readBack = DeliverPromptArgs.TryRead(in roundTrip, out DeliverPromptArgs back);

        // Assert: TryRead refuses any non-zero reserved byte, so a true read also proves bytes 20 to 31 and the id pads are zero.
        Assert.True(read, row);
        Assert.Equal(expected, decoded);
        Assert.True(readBack, row);
        Assert.Equal(expected, back);
        Assert.Equal(((ReadOnlySpan<byte>)roundTrip).ToArray(), ((ReadOnlySpan<byte>)args).ToArray());
    }

    [Fact]
    [Trait("Id", "M-UI-1")]
    public void PromptKeys_AreReasonNamespaceIdsFrom16_Unique_SpelledAsTheGdd_AndNoneHoldsACentSign()
    {
        // Arrange: the GDD's Localisation keys line (l.432, without the deferred a11y.* variants), then the two mail.reject.* keys.
        string[] gdd =
        {
            "deliver.verb.hold", "deliver.verb.toggle", "deliver.verb.armed", "deliver.match.pieces", "deliver.match.stacks",
            "deliver.match.kind", "deliver.match.pay", "deliver.match.late", "deliver.match.dead_only", "deliver.keep_raised",
            "deliver.mismatch.piece_for", "deliver.mismatch.post_one", "deliver.mismatch.cursed", "deliver.floor_refusal",
            "deliver.nothing", "deliver.close_panel", "deliver.dry_land", "deliver.pending", "deliver.sorting",
            "deliver.reply.too_far", "deliver.reply.timeout", "deliver.cancel.selection_changed", "deliver.cancel.prompt_changed",
            "deliver.cancel.settle", "mail.reject.closed", "mail.reject.not_on_route",
        };

        // Act
        DeliverPromptKey[] keys = DeliverPromptKeys.All.ToArray();

        // Assert
        Assert.Equal(gdd, keys.Select(k => k.Spelling).ToArray());
        Assert.All(keys, k => Assert.Equal(StringNamespace.Reason, k.Id.Ns));
        Assert.All(keys, k => Assert.True(k.Id.Index >= 16, k.Spelling));
        Assert.Equal(keys.Length, keys.Select(k => k.Id.Index).Distinct().Count());
        Assert.DoesNotContain(keys, k => k.Spelling.Contains('¢', StringComparison.Ordinal));
    }
}
