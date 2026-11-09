using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.MailDestinations;

namespace PerformativeMail.Sim.Tests.Integration.MailDestinations;

/// <summary>
/// Mail-destinations story 008 (ADR-0017 section 5 throw row, ADR-0015 landing contract, GDD R13): <see cref="IMailApi.ResolveThrownLanding"/>
/// through the one consume routine, called by a Throwing stand-in in phase 4 that holds the entry in Flight (<see cref="ThrowRig"/>).
/// </summary>
public class ThrownLandingTests
{
    private const int WrongHouse = 1;   // the address of a wrong-house stack (registry row 1)

    // ---- AC-1 ----

    [Fact]
    [Trait("Id", "M-X-2a")]
    [Trait("Id", "M-F8-1b")]
    [Trait("Id", "M-R6-1a")]
    public void Ac1_TwentyElevenCentLettersAtAWrongHouseFromMinus440_ConsumeFive_AndFifteenStayInFlight()
    {
        // Arrange
        var rig = new ThrowRig();
        rig.Open();
        rig.Wallet.Balance = -440;
        EntryRef flying = default;
        rig.Tick(() => flying = rig.GiveFlying(0, WrongHouse, 20, spawnValue: 22));   // penalty 11 ¢ each

        // Act
        rig.Arm(flying);
        rig.Tick();

        // Assert: −440 − 5 × 11 = −495; a sixth unit would reach −506.
        Assert.Equal(new LandingResult(LandingKind.Misdelivered, 5, 15, true, MailRejectReason.None), rig.Flights.Results.Single().Result);
        Assert.Equal(-495, rig.Wallet.Balance);
        DeliveryOutcomePayload o = Assert.Single(rig.Events<DeliveryOutcomePayload>(MailEventKinds.DeliveryOutcome));
        Assert.Equal(((byte)DeliveryVia.Throw, (ushort)5, DeliveryOutcomePayload.FlagStoppedAtFloor), (o.Via, o.Units, o.Flags & DeliveryOutcomePayload.FlagStoppedAtFloor));

        // The remainder is still the flight's, in Flight, for Throwing to bounce.
        Assert.True(rig.Inventory.TryReadEntry(flying, out EntryView left));
        Assert.Equal(15, left.Units);
        Assert.Equal(15, rig.Inventory.Ledger.Live(UnitLedger.MailRow, LedgerState.Flight));
        Assert.Equal(5, rig.Inventory.Ledger.Consumed(UnitLedger.MailRow, ConsumeReason.Misdelivered));
    }

    // ---- AC-2 ----

    [Fact]
    [Trait("Id", "M-F8-2")]
    public void Ac2_OneUnitThenTwoUnitsFromMinus492_ConsumeOne_TheSecondFlightStopsAtTheFloor_AndOnlyTheConsumedUnitComplains()
    {
        // Arrange: flight A is one letter (spawn 8, penalty 4), flight B two letters (spawn 9, penalty 5), landing in that order.
        var rig = new ThrowRig();
        rig.Open();
        rig.Wallet.Balance = -492;
        EntryRef a = default;
        EntryRef b = default;
        rig.Tick(() =>
        {
            a = rig.GiveFlying(0, WrongHouse, 1, spawnValue: 8);
            b = rig.GiveFlying(0, WrongHouse, 2, spawnValue: 9);
        });
        ushort misdeliveries = rig.Mail.MisdeliveriesThisShift;

        // Act
        rig.Arm(a);
        rig.Arm(b);
        rig.Tick();

        // Assert: A reaches −496; B's first unit would reach −501, so B stays whole.
        Assert.Equal(
            new[]
            {
                new LandingResult(LandingKind.Misdelivered, 1, 0, false, MailRejectReason.None),
                new LandingResult(LandingKind.Misdelivered, 0, 2, true, MailRejectReason.None),
            },
            rig.Flights.Results.Select(x => x.Result));
        Assert.Equal(-496, rig.Wallet.Balance);
        Assert.Equal(5000, Assert.Single(rig.Events<ComplaintRaisedPayload>(MailEventKinds.ComplaintRaised)).AmountPermille);
        Assert.Equal(misdeliveries + 1, rig.Mail.MisdeliveriesThisShift);
    }

    // ---- AC-3 ----

    [Fact]
    [Trait("Id", "M-X-2b")]
    [Trait("Id", "M-R7-1a")]
    public void Ac3_AnAcceptAfterTheThrowerLeft_PaysEveryUnitWithTheSnapshottedThrownPm()
    {
        // Arrange: 20 right-house letters (spawn 8) in flight; the thrower disconnects before the landing.
        var rig = new ThrowRig();
        rig.Open();
        EntryRef flying = default;
        rig.Tick(() => flying = rig.GiveFlying(0, ThrowRig.Door, 20));
        rig.Leave(0);

        // Act
        rig.Arm(flying, thrower: 0, thrownPm: 1100);
        rig.Tick();

        // Assert: 8 × 1.1 = 8.8 → 9 ¢ a unit with Cannon Arm (8 ¢ without).
        Assert.Equal(new LandingResult(LandingKind.Accepted, 20, 0, false, MailRejectReason.None), rig.Flights.Results.Single().Result);
        Assert.Equal(180, rig.Wallet.Balance);
        Assert.All(rig.Events<MailConsumedPayload>(MailEventKinds.MailConsumed), c => Assert.Equal(9, c.Cents));
    }

    [Fact]
    [Trait("Id", "M-R16-1a")]
    [Trait("Id", "M-R16-1b")]
    [Trait("Id", "M-R8-1b")]
    public void Ac3_ATwentyLetterThrownMisdeliveryAtWalletZero_ConsumesAllTwenty_WithTwentyMailConsumedAndOneOutcome()
    {
        // Arrange
        var rig = new ThrowRig();
        rig.Open();
        EntryRef flying = default;
        rig.Tick(() => flying = rig.GiveFlying(0, WrongHouse, 20));   // 4 ¢ letters
        uint previous = rig.Mail.Consumer.OutcomeSeq;

        // Act
        rig.Arm(flying);
        rig.Tick();

        // Assert: the whole stack, no one-unit cap; exactly 80 ¢ (M-R8-1b).
        Assert.Equal(new LandingResult(LandingKind.Misdelivered, 20, 0, false, MailRejectReason.None), rig.Flights.Results.Single().Result);
        Assert.Equal(-80, rig.Wallet.Balance);
        Assert.Equal(20, rig.Events<MailConsumedPayload>(MailEventKinds.MailConsumed).Length);
        DeliveryOutcomePayload o = Assert.Single(rig.Events<DeliveryOutcomePayload>(MailEventKinds.DeliveryOutcome));
        Assert.Equal(((ushort)20, (byte)EvalResult.Misdeliver, rig.AddressOf(WrongHouse), previous + 1, (byte)0), (o.Units, o.Result, o.IntendedAddress, o.OutcomeSeq, o.Actor));
        Assert.False(rig.Inventory.TryReadEntry(flying, out _));   // the last unit freed the flight's entry
    }

    // ---- AC-4 ----

    [Theory]
    [Trait("Id", "M-X-2c")]
    [Trait("Id", "M-R6-2")]
    [Trait("Id", "M-R4-2d")]
    [InlineData(-1, LandingKind.Misdelivered)]   // released before close T, lands at T + 90: OPEN
    [InlineData(0, LandingKind.Rejected)]        // released at T: closed, the whole entry comes back
    public void Ac4_ALandingAtCloseTPlus90_IsJudgedByItsReleaseTick(int releaseOffset, LandingKind expected)
    {
        // Arrange: 20 wrong-house letters in flight; deliveries closed at T = landing tick − 90.
        var rig = new ThrowRig();
        for (int i = 0; i < 100; i++)
        {
            rig.Tick();
        }

        EntryRef flying = default;
        rig.Tick(() => flying = rig.GiveFlying(0, WrongHouse, 20));
        uint close = rig.Now + 1 - 90;
        rig.Mail.Registry.Deliveries.Set(false, close, DeliveriesState.DefaultUnlockedMask);
        ushort misdeliveries = rig.Mail.MisdeliveriesThisShift;

        // Act
        rig.Arm(flying, releaseTick: (uint)(close + releaseOffset));
        rig.Tick();

        // Assert
        LandingResult r = rig.Flights.Results.Single().Result;
        if (expected == LandingKind.Misdelivered)
        {
            Assert.Equal(new LandingResult(LandingKind.Misdelivered, 20, 0, false, MailRejectReason.None), r);
            return;
        }

        // A Reject: the whole entry is the caller's; no wallet, complaint or stats change, no outcome.
        Assert.Equal(new LandingResult(LandingKind.Rejected, 0, 20, false, MailRejectReason.Closed), r);
        Assert.True(rig.Inventory.TryReadEntry(flying, out EntryView left));
        Assert.Equal(20, left.Units);
        Assert.Equal((0, 0L), (rig.Wallet.Calls, rig.Wallet.Balance));
        Assert.Equal(misdeliveries, rig.Mail.MisdeliveriesThisShift);
        Assert.DoesNotContain(rig.Capture.Last, x =>
            x.Kind == MailEventKinds.MailConsumed || x.Kind == MailEventKinds.ComplaintRaised || x.Kind == MailEventKinds.DeliveryOutcome);
    }
}
