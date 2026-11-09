using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;

namespace PerformativeMail.Sim.Tests.Integration.MailDestinations;

/// <summary>
/// Mail-destinations story 008 AC-5 (GDD R6 tick order, ADR-0017 section 5, ADR-0003): in a real <c>SimWorld</c> the hand path (a
/// Delivery stand-in's op, phase 0) and the throw path (a Throwing stand-in's step, phase 4) meet at one door in one tick, and the order
/// is the phase order, not the order the callers were set up in. The feeder and NPC sub-cases of M-R6-3 a/c are M-1b and later.
/// </summary>
public class ConsumeTickOrderTests
{
    [Theory]
    [Trait("Id", "M-R6-3a")]
    [Trait("Id", "M-R6-3b")]
    [InlineData(true)]
    [InlineData(false)]
    public void Ac5_AtMinus496_AHandOpInTheLandingsTick_ConsumesFirst_AndTheLandingStopsAtTheFloor(bool withHandOp)
    {
        // Arrange: one 4 ¢ wrong-house letter in flight and one in the hotbar, both for the same door.
        var rig = new ThrowRig();
        rig.Open();
        rig.Wallet.Balance = -496;
        EntryRef flying = default;
        EntryRef held = default;
        rig.Tick(() =>
        {
            flying = rig.GiveFlying(0, 1, 1);
            held = rig.Give(0, 2, 1);
        });

        // Act: the flight is armed before the op is queued; both apply in the next tick.
        rig.Arm(flying);
        if (withHandOp)
        {
            rig.EnqueueHand(0, held);
        }

        rig.Tick();

        // Assert
        Assert.Equal(-500, rig.Wallet.Balance);
        (LandingResult landing, PhaseId landingPhase) = rig.Flights.Results.Single();
        Assert.Equal(PhaseId.ThrowingStep, landingPhase);
        if (withHandOp)
        {
            (HandConsumeSummary hand, PhaseId handPhase) = rig.HandOps.Applied.Single();
            Assert.Equal((new HandConsumeSummary(1, 1, MailRejectReason.None, false, -4), PhaseId.InputsApply), (hand, handPhase));
            Assert.Equal(new LandingResult(LandingKind.Misdelivered, 0, 1, true, MailRejectReason.None), landing);
        }
        else
        {
            Assert.Equal(new LandingResult(LandingKind.Misdelivered, 1, 0, false, MailRejectReason.None), landing);
        }
    }

    [Fact]
    [Trait("Id", "M-R6-3d")]
    public void Ac5_FourOneLetterMisdeliverOpsAtOneDoorInOneTick_ChargeTheDoorAndTheWalletOncePerOp_InOpOrder()
    {
        // Arrange: four one-letter stacks for four other houses (rows 1-4), handed to door 0 at wallet 0.
        var rig = new ThrowRig();
        rig.Open();
        var held = new EntryRef[4];
        rig.Tick(() =>
        {
            for (int i = 0; i < held.Length; i++)
            {
                held[i] = rig.Give(0, i + 1, 1);
            }
        });

        // Act
        foreach (EntryRef e in held)
        {
            rig.EnqueueHand(0, e);
        }

        rig.Tick();

        // Assert: per op, in op order: the unit, a 5,000 permille charge at the door, the outcome; and 4 ¢ off the wallet each.
        EventEntry[] mail = rig.Capture.Last
            .Where(x => x.Kind == MailEventKinds.MailConsumed || x.Kind == MailEventKinds.ComplaintRaised || x.Kind == MailEventKinds.DeliveryOutcome)
            .ToArray();
        Assert.Equal(12, mail.Length);
        for (int op = 0; op < 4; op++)
        {
            Assert.Equal(
                new[] { MailEventKinds.MailConsumed, MailEventKinds.ComplaintRaised, MailEventKinds.DeliveryOutcome },
                mail.Skip(op * 3).Take(3).Select(x => x.Kind));
            ComplaintRaisedPayload c = mail[(op * 3) + 1].PayloadAs<ComplaintRaisedPayload>();
            Assert.Equal((rig.DoorId.Value, (ushort)5000), (c.Destination, c.AmountPermille));
            Assert.Equal(rig.AddressOf(op + 1), mail[(op * 3) + 2].PayloadAs<DeliveryOutcomePayload>().IntendedAddress);
        }

        Assert.Equal(new long[] { 4, 4, 4, 4 }, rig.Wallet.Debits);
        Assert.Equal(-16, rig.Wallet.Balance);
    }
}
