using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>Doc 03 §2.2 acceptance rules, one test per rule (M0 acceptance criterion).</summary>
    public class DeliveryRulesTests
    {
        private static WorldMap Map => TestContent.Db.Map("test_street");
        private static DestinationDef House(int index) => Map.Destinations[index];

        private static DeliveryResult Judge(MailItem item, DestinationDef dest, int shift = 1, int wallet = 0) =>
            DeliveryRules.Evaluate(item, TestContent.Db.MailKinds[item.Kind], dest, shift, wallet, TestContent.Db);

        [Theory]
        [InlineData("letter", 8)]
        [InlineData("small", 30)]
        [InlineData("medium", 70)]
        [InlineData("large", 160)]
        public void Rule3_CorrectAddressOnTimePaysFullValue(string kind, int value)
        {
            var result = Judge(TestContent.Mail(kind, House(0).Address), House(0));
            Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
            Assert.Equal(value, result.Amount);
            Assert.Equal(0, result.Complaint);
            Assert.False(result.Late);
        }

        [Theory]
        [InlineData(1, 70)] // spawned shift 1, delivered shift 1: on time
        [InlineData(2, 35)] // one shift late: half
        [InlineData(3, 0)]  // later: nothing, but it still clears
        public void Rule3_TimelinessScalesPayment(int currentShift, int expectedPaid)
        {
            var result = Judge(TestContent.Mail("medium", House(0).Address), House(0), currentShift);
            Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
            Assert.Equal(expectedPaid, result.Amount);
            Assert.Equal(currentShift > 1, result.Late);
            Assert.Equal(currentShift > 1 ? 2 : 0, result.Complaint);
        }

        [Fact]
        public void Rule3_CargoDeadlineIsOneShiftLater()
        {
            var cargo = TestContent.Mail("cargo", House(0).Address, shift: 3);
            Assert.Equal(4, cargo.DeadlineShift);
            Assert.Equal(1000, DeliveryRules.TimelinessPermille(4, cargo.DeadlineShift, 0.5));
            Assert.Equal(500, DeliveryRules.TimelinessPermille(5, cargo.DeadlineShift, 0.5));
        }

        [Fact]
        public void Rule4_KindNotAcceptedIsRejectedAndNothingHappens()
        {
            // Cargo only goes to business docks; a house mailbox will not take it even at the right address.
            var result = Judge(TestContent.Mail("cargo", House(0).Address), House(0));
            Assert.Equal(DeliveryOutcome.WontFitHere, result.Outcome);
            Assert.False(result.Consumed);
            Assert.Equal(0, result.Amount);
            Assert.Equal(0, result.Complaint);
        }

        [Theory]
        [InlineData("letter", 4, 5)]
        [InlineData("small", 15, 8)]
        [InlineData("medium", 35, 12)]
        [InlineData("large", 80, 16)]
        public void Rule5_WrongAddressConsumesAndDeductsHalfValue(string kind, int penalty, int complaint)
        {
            var result = Judge(TestContent.Mail(kind, House(0).Address), House(1));
            Assert.Equal(DeliveryOutcome.Misdelivered, result.Outcome);
            Assert.True(result.Consumed);
            Assert.Equal(penalty, result.Amount);
            Assert.Equal(complaint, result.Complaint);
        }

        [Theory]
        [InlineData(-490, true)]  // -494 after the 4 ¢ letter penalty
        [InlineData(-496, true)]  // lands exactly on the floor
        [InlineData(-497, false)] // would be -501
        [InlineData(-500, false)]
        public void Rule5_WalletFloorRejectsMisdelivery(int wallet, bool accepted)
        {
            var result = Judge(TestContent.Mail("letter", House(0).Address), House(1), wallet: wallet);
            Assert.Equal(accepted ? DeliveryOutcome.Misdelivered : DeliveryOutcome.WalletFloor, result.Outcome);
            Assert.Equal(accepted, result.Consumed);
        }

        [Fact]
        public void WalletFloorNeverBlocksACorrectDelivery()
        {
            var result = Judge(TestContent.Mail("letter", House(0).Address), House(0), wallet: -500);
            Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        }

        [Fact]
        public void EconomyAppliesPayAndPenaltyToWalletAndShiftEarnings()
        {
            var economy = new Economy(TestContent.Db.Balance);
            economy.Apply(Judge(TestContent.Mail("medium", House(0).Address), House(0)));
            Assert.Equal(70, economy.Wallet);
            Assert.Equal(70, economy.ShiftEarnings);
            Assert.Equal(1, economy.ShiftDeliveries);

            economy.Apply(Judge(TestContent.Mail("small", House(0).Address), House(1), wallet: economy.Wallet));
            Assert.Equal(55, economy.Wallet);
            Assert.Equal(55, economy.ShiftEarnings);
            Assert.Equal(1, economy.ShiftMisdeliveries);
            Assert.Equal(8, economy.Complaint);

            // A rejected insert changes nothing.
            economy.Apply(Judge(TestContent.Mail("cargo", House(0).Address), House(0)));
            Assert.Equal(55, economy.Wallet);
            Assert.Equal(8, economy.Complaint);
        }

        [Fact]
        public void ComplaintDecaysOnePointPerTenSecondsAndClampsAtMax()
        {
            var economy = new Economy(TestContent.Db.Balance);
            economy.AddComplaint(250);
            Assert.Equal(100, economy.Complaint);
            for (int i = 0; i < 300; i++) economy.StepDelivery();
            Assert.Equal(99, economy.Complaint);
            for (int i = 0; i < 299; i++) economy.StepDelivery();
            Assert.Equal(99, economy.Complaint);
            economy.StepDelivery();
            Assert.Equal(98, economy.Complaint);
        }
    }
}
