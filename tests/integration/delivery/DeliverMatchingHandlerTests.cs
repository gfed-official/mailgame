using System.Collections.Immutable;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Sim.Tests.Integration.ManualDelivery.DeliverRig;

namespace PerformativeMail.Sim.Tests.Integration.ManualDelivery;

/// <summary>
/// Manual-delivery story 005 AC-2 to AC-6 (Manual Delivery MD5, MD6; grid-inventory H-R7-6, H-R7-7, H-R7-11, H-X-1; ADR-0009 errata
/// 2026-10-16, ADR-0014 section 5, ADR-0017 section 5): <c>deliver_matching</c> requests through the op queue into the
/// <see cref="DeliverMatchingHandler"/> of a composed world (<see cref="DeliverRig"/>), with the real Players range gate, the real host
/// panel scan and Mail's real consume routine; only the wallet is a stub. Simplified per M-1a regroup plan section 3: AC-4's
/// two-players-in-one-tick row, AC-5's dead/frozen row and AC-6's two-players row are not tested.
/// </summary>
public class DeliverMatchingHandlerTests
{
    private static InvReplyExtra Extra(in Reply r) => r.ExtraAs<InvReplyExtra>();

    private static (ushort ReqEntries, ushort GotEntries, ushort ReqUnits, ushort GotUnits) Counts(in Reply r)
    {
        InvReplyExtra x = Extra(in r);
        return (x.RequestedEntries, x.GotEntries, x.RequestedUnits, x.GotUnits);
    }

    // ---------------------------------------------------------------- AC-2

    [Fact]
    [Trait("Id", "H-R7-6")]
    public void Ac2_FourLettersInThePanel_ThenOnTheCursor_AreAllConsumed_AndTheContainerVersionRisesPerUnit()
    {
        // Arrange
        var rig = new DeliverRig();
        EntryRef inPack = rig.Give(0, rig.Pack(0), "letter", Right, 4);
        uint packBefore = rig.Version(rig.Pack(0));

        // Act
        Reply fromPack = rig.Deliver(0);
        uint packAfter = rig.Version(rig.Pack(0));
        EntryRef onCursor = rig.PickToCursor(0, rig.Give(0, rig.Pack(0), "letter", Right, 4));
        Reply fromCursor = rig.Deliver(0);

        // Assert: one ConsumeUnit call per unit, so the container version rises 4 times (ADR-0009 section 3, ADR-0017 section 5; TD-210).
        Assert.True(fromPack.IsOk);
        Assert.Equal(((ushort)1, (ushort)1, (ushort)4, (ushort)4), Counts(in fromPack));
        Assert.Equal(0, rig.Units(inPack));
        Assert.Equal(packBefore + 4, packAfter);
        Assert.True(fromCursor.IsOk);
        Assert.Equal(((ushort)1, (ushort)1, (ushort)4, (ushort)4), Counts(in fromCursor));
        Assert.Equal(0, rig.Units(onCursor));
        Assert.Equal(0, rig.Inventory.HostPanelView(PlayerSlot.FromValue(0)).EntryCount(PanelSection.Cursor));
    }

    [Fact]
    [Trait("Id", "H-R7-7")]
    public void Ac2_HotbarThenInventoryMatches_AreConsumedInScanOrder_TheOtherAddressIsUntouched_AndNothingIsMisdelivered()
    {
        // Arrange: hotbar 5 letters #17; inventory 2 Mediums #17, then 6 letters #18.
        var rig = new DeliverRig();
        EntryRef hot = rig.Give(0, rig.Hotbar(0), "letter", Right, 5);
        EntryRef m1 = rig.Give(0, rig.Pack(0), "medium", Right, 1);
        EntryRef m2 = rig.Give(0, rig.Pack(0), "medium", Right, 1);
        EntryRef other = rig.Give(0, rig.Pack(0), "letter", Wrong, 6);

        // Act
        Reply r = rig.Deliver(0);

        // Assert
        DeliveryOutcomePayload[] o = rig.Outcomes();
        Assert.True(r.IsOk);
        Assert.Equal(((ushort)3, (ushort)3, (ushort)7, (ushort)7), Counts(in r));
        Assert.Equal(
            new[] { (rig.Kind("letter").Value, (ushort)5), (rig.Kind("medium").Value, (ushort)1), (rig.Kind("medium").Value, (ushort)1) },
            o.Select(p => (p.Kind, p.Units)).ToArray());
        Assert.All(o, p => Assert.Equal((byte)EvalResult.Accept, p.Result));
        Assert.Equal((0, 0, 0, 6), (rig.Units(hot), rig.Units(m1), rig.Units(m2), rig.Units(other)));
        Assert.Equal(0, rig.Wallet.Debits);
    }

    // ---------------------------------------------------------------- AC-3

    [Fact]
    [Trait("Id", "H-R7-11")]
    [Trait("Id", "M-MD5-2")]
    public void Ac3_NoMatch_TheSelectedEntryGivesExactlyItsOldestUnit_AbsentIsNoTarget_AndAMatchElsewhereWins()
    {
        // Arrange: no #17 entry; the hotbar holds 5 letters to #18.
        var rig = new DeliverRig();
        EntryRef selected = rig.Give(0, rig.Hotbar(0), "letter", Wrong, 5);
        Assert.True(rig.Inventory.TryReadEntry(selected, out EntryView before));

        // Act
        Reply absent = rig.Deliver(0);
        DeliveryOutcomePayload[] absentOutcomes = rig.Outcomes();
        long walletAfterAbsent = rig.Wallet.Balance;
        Reply fallback = rig.Deliver(0, Right, selected.Entry.Value);
        DeliveryOutcomePayload fallbackOutcome = Assert.Single(rig.Outcomes());
        Assert.True(rig.Inventory.TryReadEntry(selected, out EntryView afterFallback));
        EntryRef match = rig.Give(0, rig.Pack(0), "letter", Right, 1);
        int debits = rig.Wallet.Debits;
        Reply matchWins = rig.Deliver(0, Right, selected.Entry.Value);

        // Assert: absent is no_target with nothing changed.
        Assert.Equal((ushort)DeliveryReason.NoTarget, absent.Reason);
        Assert.Empty(absentOutcomes);
        Assert.Equal(0L, walletAfterAbsent);

        // The fallback consumes exactly the oldest unit; the entry keeps the other four.
        Assert.True(fallback.IsOk);
        Assert.Equal(((ushort)1, (ushort)1, (ushort)1, (ushort)1), Counts(in fallback));
        Assert.Equal((byte)EvalResult.Misdeliver, fallbackOutcome.Result);
        Assert.Equal(4, afterFallback.Units);
        Assert.Equal(before.LowestMailId.Value + 1, afterFallback.LowestMailId.Value);

        // A match wins over the fallback: only it is delivered, and no penalty is charged.
        Assert.True(matchWins.IsOk);
        Assert.Equal((byte)EvalResult.Accept, Assert.Single(rig.Outcomes()).Result);
        Assert.Equal((0, 4), (rig.Units(match), rig.Units(selected)));
        Assert.Equal(debits, rig.Wallet.Debits);
    }

    // ---------------------------------------------------------------- AC-4

    [Fact]
    [Trait("Id", "H-X-1")]
    [Trait("Id", "M-EC-3b")]
    public void Ac4_AtMinus496_AWrongHouseLandsOnMinus500_AgainIsWalletFloor_TheRightHouseStillDelivers_AndARejectChangesNothing()
    {
        // Arrange
        var rig = new DeliverRig();
        rig.Wallet.Balance = -496;
        EntryRef wrong = rig.Give(0, rig.Hotbar(0), "letter", Wrong, 5);

        // Act
        Reply first = rig.Deliver(0, Right, wrong.Entry.Value);
        long afterFirst = rig.Wallet.Balance;
        Reply second = rig.Deliver(0, Right, wrong.Entry.Value);
        long afterSecond = rig.Wallet.Balance;
        int unitsAfterSecond = rig.Units(wrong);
        EntryRef right = rig.Give(0, rig.Pack(0), "letter", Right, 3);
        Reply correct = rig.Deliver(0);
        EntryRef late = rig.Give(0, rig.Pack(0), "letter", Right, 2);
        rig.Mail.Registry.Deliveries.Set(false, rig.Now - 1, DeliveriesState.DefaultUnlockedMask);   // closed before the hold began
        long beforeReject = rig.Wallet.Balance;
        Reply rejected = rig.Deliver(0);

        // Assert
        Assert.True(first.IsOk);
        Assert.Equal(-500L, afterFirst);
        Assert.Equal((ushort)DeliveryReason.WalletFloor, second.Reason);
        Assert.Equal((-500L, 4), (afterSecond, unitsAfterSecond));
        Assert.True(correct.IsOk);
        Assert.Equal((ushort)3, Extra(in correct).GotUnits);
        Assert.Equal(0, rig.Units(right));
        Assert.Equal((ushort)DeliveryReason.Closed, rejected.Reason);
        Assert.Equal((2, beforeReject), (rig.Units(late), rig.Wallet.Balance));
        Assert.Empty(rig.Outcomes());
    }

    // ---------------------------------------------------------------- AC-5

    [Fact]
    [Trait("Id", "M-MD6-1")]
    [Trait("Id", "M-MD6-3")]
    public void Ac5_HostChecks_Reach2900Accepted_3200OutOfRange_SwimDepthDryLand_AndTwoRequestsOneTickApartBothApply()
    {
        // Arrange
        var rig = new DeliverRig();
        EntryRef a = rig.Give(0, rig.Pack(0), "letter", Right, 2);
        EntryRef selected = rig.Give(0, rig.Hotbar(0), "letter", Wrong, 2);

        // Act: 3,200 mm, then swim depth (both consume nothing), then 2,900 mm.
        ClaimedPosition far = rig.Stand(0, Right, dx: 3_200);
        rig.Queue(0, Right, in far);
        rig.Run();
        Reply outOfRange = Assert.Single(rig.Replies());
        ClaimedPosition swim = rig.Stand(0, Right, flags: SampleFlags.Swimming);
        rig.Queue(0, Right, in swim);
        rig.Run();
        Reply dryLand = Assert.Single(rig.Replies());
        int unitsAfterRefusals = rig.Units(a);
        ClaimedPosition near = rig.Stand(0, Right, dx: 2_900);
        rig.Queue(0, Right, in near);
        rig.Run();
        Reply accepted = Assert.Single(rig.Replies());

        // Two requests one tick apart (no throttle): a match, then the selected fallback.
        EntryRef c = rig.Give(0, rig.Pack(0), "letter", Right, 1);
        ClaimedPosition at = rig.Stand(0, Right);
        rig.Queue(0, Right, in at, selected.Entry.Value);
        rig.Run();
        Reply firstOfTwo = Assert.Single(rig.Replies());
        rig.Queue(0, Right, in at, selected.Entry.Value);
        rig.Run();
        Reply secondOfTwo = Assert.Single(rig.Replies());

        // Assert
        Assert.Equal((ushort)DeliveryReason.OutOfRange, outOfRange.Reason);
        Assert.Equal((ushort)DeliveryReason.DryLand, dryLand.Reason);
        Assert.Equal(2, unitsAfterRefusals);
        Assert.True(accepted.IsOk);
        Assert.Equal((ushort)2, Extra(in accepted).GotUnits);
        Assert.True(firstOfTwo.IsOk);
        Assert.True(secondOfTwo.IsOk);
        Assert.Equal((0, 1), (rig.Units(c), rig.Units(selected)));
    }

    [Fact]
    [Trait("Id", "M-MD6-1")]
    public void Ac5_ADestinationWithNoDeliverAnchor_IsNoTarget_WithNothingConsumed()
    {
        // Arrange: the fixture dock, a house type with manualInsertHold 0, so no destination registers a Deliver anchor.
        ContentSet m1a = BuiltInContent.CreateM1a();
        ContentSet dock = new(
            m1a.Balance,
            m1a.ResolvedStatBounds,
            m1a.Containers,
            m1a.Items,
            m1a.MailKinds,
            new DefTable<DestinationTypeDef>(ImmutableArray.Create(m1a.DestinationTypes[DefIndex.FromValue(0)] with { ManualInsertHoldMs = 0 })),
            m1a.MailMixes);
        var rig = new DeliverRig(dock);
        EntryRef e = rig.Give(0, rig.Pack(0), "letter", Right, 2);

        // Act
        Reply r = rig.Deliver(0);

        // Assert
        Assert.Equal((ushort)DeliveryReason.NoTarget, r.Reason);
        Assert.Equal(2, rig.Units(e));
        Assert.Empty(rig.Outcomes());
    }

    // ---------------------------------------------------------------- AC-6

    [Theory]
    [Trait("Id", "M-MD6-2")]
    [InlineData(-12, -1, true)]     // begun before the close, applied before it
    [InlineData(-12, 5, true)]      // applied inside the grace
    [InlineData(-12, 90, true)]     // the grace's last tick
    [InlineData(-12, 91, false)]    // past the grace: closed
    [InlineData(0, 5, false)]       // begun at the close: closed, nothing consumed
    [InlineData(205, 5, false)]     // applyTick + 200, clamped to applyTick: begun after the close, closed
    [InlineData(-155, 5, true)]     // applyTick − 160, clamped to applyTick − 150: still before the close
    public void Ac6_HoldStartTick_IsJudgedAtItsClampedStart_UnderTheClosingGrace(int holdFromClose, int applyFromClose, bool accepted)
    {
        // Arrange: deliveries close at the start of tick T = applyTick − applyFromClose.
        var rig = new DeliverRig();
        while (rig.Now < 400)
        {
            rig.Run();
        }

        EntryRef e = rig.Give(0, rig.Pack(0), "letter", Right, 1);
        ClaimedPosition at = rig.Stand(0, Right);
        uint apply = rig.Now;
        uint close = (uint)(apply - applyFromClose);
        rig.Mail.Registry.Deliveries.Set(false, close, DeliveriesState.DefaultUnlockedMask);

        // Act
        rig.Queue(0, Right, in at, holdStartTick: (uint)(close + holdFromClose));
        rig.Run();
        Reply r = Assert.Single(rig.Replies());

        // Assert
        Assert.Equal(accepted ? (ushort)0 : (ushort)DeliveryReason.Closed, r.Reason);
        Assert.Equal(accepted ? 0 : 1, rig.Units(e));
    }

    [Theory]
    [Trait("Id", "M-MD6-2")]
    [InlineData(1_200u, 1_000u)]   // applyTick + 200 → applyTick
    [InlineData(840u, 850u)]       // applyTick − 160 → applyTick − 150
    [InlineData(850u, 850u)]       // the bound itself
    [InlineData(988u, 988u)]       // inside: kept, never replaced
    public void Ac6_TheClamp_BoundsTheHoldStartToTheHouseWindowOf150Ticks(uint holdStartTick, uint expected)
    {
        // Arrange: the house window is maxHoldTicks 60 (the 2.0 s ceiling) + closeGraceTicks 90.
        var rig = new DeliverRig();
        int window = DeliverySystem.HoldWindows(rig.Delivery.Registry, BuiltInContent.CreateM1a())[Right];

        // Act
        uint clamped = DeliverMatchingHandler.ClampHoldStart(holdStartTick, 1_000u, window);

        // Assert
        Assert.Equal(150, window);
        Assert.Equal(expected, clamped);
    }
}
