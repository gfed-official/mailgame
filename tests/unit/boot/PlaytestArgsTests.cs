using PerformativeMail.Game.Boot;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Sim.Tests.Unit.Boot;

/// <summary>WP-G's playtest launch flags (and WP-I's park-next): read in debug builds only, clamped, applied to the composition profile's shift stub.</summary>
public class PlaytestArgsTests
{
    [Theory]
    [InlineData(false, "--playtest-no-timer --playtest-at-mailbox --playtest-park-next", 240, 60 * TickRate.Hz, false, false)]
    [InlineData(true, "--playtest-no-timer", PlaytestArgs.MaxDeliverySeconds, 60 * TickRate.Hz, false, false)]
    [InlineData(true, "--playtest-delivery-seconds=5", PlaytestArgs.MinDeliverySeconds, 60 * TickRate.Hz, false, false)]
    [InlineData(true, "--playtest-delivery-seconds=600 --playtest-prep-seconds=0", 600, 1, false, false)]
    [InlineData(true, "--playtest-at-mailbox --seed=7", 240, PlaytestArgs.AtMailboxPrepSeconds * TickRate.Hz, true, false)]
    [InlineData(true, "--playtest-park-next", 240, PlaytestArgs.AtMailboxPrepSeconds * TickRate.Hz, true, true)]
    [InlineData(true, "--playtest-delivery-seconds=abc", 240, 60 * TickRate.Hz, false, false)]
    public void Parse_ThenApply_SetsTheShiftLengthsInDebugBuildsOnly(bool debugBuild, string args, int deliverySeconds, int prepTicks, bool atMailbox, bool parkNext)
    {
        // Arrange
        string[] userArgs = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Act
        PlaytestArgs flags = PlaytestArgs.Parse(userArgs, debugBuild);
        SimProfile profile = flags.Apply(SimProfile.M1aGreybox);

        // Assert: lengths, and a spawn budget scaled with the Delivery length (960 c over the default 240 s).
        Assert.Equal((deliverySeconds, prepTicks, atMailbox, parkNext), (profile.ShiftStub.DeliverySeconds, profile.ShiftStub.PrepTicks, flags.AtMailbox, flags.ParkNext));
        Assert.Equal(960L * deliverySeconds / 240, profile.ShiftStub.SpawnBudgetCents);
    }
}
