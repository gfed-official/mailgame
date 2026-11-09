using PerformativeMail.Game.Boot;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim.Tests.Unit.Boot;

/// <summary>
/// M-1a wave-3 assembly: boot step 5 binds the client mirrors the HUD reads into the host session, so the shift stub's Prep → Delivery
/// reaches the client's run mirror through MirrorDrain, and step 7 finds the mirrors published.
/// </summary>
public class ClientMirrorsBootTests
{
    [Fact]
    public void SessionStep_ShiftRunsIntoDelivery_ClientRunMirrorFollowsAndMirrorsArePublished()
    {
        // Arrange: Prep cut to 3 ticks.
        var h = new BootHarness();
        h.Context.Options = new BootOptions
        {
            Clock = h.Context.Options.Clock,
            FixtureProfile = SimProfile.M1aGreybox.WithShiftStub(new ShiftStubSettings { PrepTicks = 3 }),
        };
        Assert.True(new BootSequence(h.Plan()).Run(h.Context));
        BootSession booted = h.Context.Session!;
        var clock = (FakeBootClock)booted.Clock;
        booted.Driver.Start(clock.Timestamp);

        // Act: one tick per frame, past the Prep → Delivery transition.
        for (int i = 0; i < 6; i++)
        {
            clock.Timestamp += FakeBootClock.TicksPerSecond / 30;
            booted.Session.RunFrame();
        }

        // Assert
        Assert.Equal(6u, booted.CurrentTick);
        Assert.Equal(ShiftPhase.Delivery, booted.Mirrors!.Run.Phase);
        Assert.Same(booted.Mirrors.Run, h.Context.Published.Get<RunMirror>());
        Assert.Same(booted.Mirrors.Wallet, h.Context.Published.Get<WalletMirror>());
    }
}
