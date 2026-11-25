using System;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Pings over the loopback transport (doc 09 §2.6; design choice 117): a tap of the ping key
    /// marks where the crosshair meets the ground, a hold brings the wheel up and the mouse picks
    /// its word; the other carrier has the ping in its world and its feed, and after 10 s it is gone.
    /// </summary>
    public class PingFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        [Fact]
        public void ATapPingsLookHere_AHoldPicksFromTheWheel_AndTheOtherSeesBothForTenSeconds()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var ada = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 0);
            var bo = new ClientRuntime(content, hub.CreateClient(), "Bo", PlaytestFlags.None, 0);
            var boFeed = new EventFeed();
            bo.EventReceived += e => boFeed.OnEvent(bo.World!, e);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); bo.Tick(); server.Tick(); }
            }
            Run(15);
            var a = ada.World!;
            var b = bo.World!;
            var ping = new PingController(ada);

            // Looking down the street, a little below the horizon: a tap.
            ada.Input.YawRadians = -MathF.PI / 2f;
            ada.Input.PitchRadians = -0.2f;
            Assert.True(PingController.Aim(a, ada.Input.YawRadians, ada.Input.PitchRadians, out float gx, out float gz));
            ping.Update(Dt, true);
            ping.Update(Dt, true);
            Assert.False(ping.WheelOpen);
            ping.Update(Dt, false);
            Assert.NotEqual(0u, ping.LastSent);
            Run(5);
            var first = Assert.Single(b.Pings);
            Assert.Equal((a.LocalId, PingKind.Here), (first.Player, first.Kind));
            Assert.Equal(gx, first.X, 1);
            Assert.Equal(gz, first.Z, 1);
            Assert.Contains(boFeed.Entries, e => e.Text.StartsWith("Ada pinged: Look here   ·   ", StringComparison.Ordinal) && e.Text.EndsWith(" m away", StringComparison.Ordinal));
            Assert.Equal("Ada   ·   Look here", HudText.PingLabel(b, first));
            Assert.Equal("You   ·   Look here", HudText.PingLabel(a, a.Pings.Single()));

            // Held: the wheel comes up and the view stays; the mouse to the left is "Danger".
            Run(20);
            for (int i = 0; i < 10; i++) ping.Update(Dt, true);
            Assert.True(ping.WheelOpen);
            ping.Steer(-60f, 8f);
            Assert.Equal(PingKind.Danger, ping.Choice);
            ping.Steer(500f, 0f);
            Assert.Equal(PingKind.Build, ping.Choice);
            Assert.True(ping.PointerX <= PingController.WheelRadius + 0.01f);
            ping.Steer(-ping.PointerX - 40f, 0f);
            Assert.Equal(PingKind.Danger, ping.Choice);
            ping.Update(Dt, false);
            Assert.False(ping.WheelOpen);
            Run(5);
            Assert.Equal(2, b.Pings.Count);
            Assert.Equal(PingKind.Danger, b.Pings[1].Kind);
            Assert.Contains(boFeed.Entries, e => e.Text.StartsWith("Ada pinged: Danger", StringComparison.Ordinal) && e.Bad);
            Assert.Equal(PingKind.Here, PingController.Pick(5f, -10f));
            Assert.Equal(PingKind.Deliver, PingController.Pick(5f, -80f));
            Assert.Equal(PingKind.Materials, PingController.Pick(-5f, 80f));

            // Looking up at the sky: no ping. Ten seconds on, both are gone.
            ada.Input.PitchRadians = 0.3f;
            uint before = ping.LastSent;
            ping.Update(Dt, true);
            ping.Update(Dt, false);
            Assert.Equal(before, ping.LastSent);
            Run(10 * SimConstants.TickRate + 5);
            Assert.Empty(b.Pings);
            Assert.Empty(a.Pings);
        }
    }
}
