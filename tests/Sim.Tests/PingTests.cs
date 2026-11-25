using System.Linq;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>Pings (doc 09 §2.6; design choice 117): a mark on the map for everyone, for 10 s, at most two a second a carrier.</summary>
    public class PingTests
    {
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Players.Player p, SimRequest request, out PingEvent? ping)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            ping = world.Events.OfType<PingEvent>().LastOrDefault();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        [Fact]
        public void APingIsForEveryone_ForTenSeconds_AtMostTwiceASecond_OnTheMap()
        {
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Ping(0, 40.25f, 30.5f, PingKind.Danger), out var ping));
            Assert.NotNull(ping);
            Assert.True(ping!.Audience.IsNone);
            Assert.Equal((host.Id, 40.25f, 30.5f, PingKind.Danger), (ping.Player, ping.X, ping.Z, ping.Kind));
            Assert.InRange(ping.ExpiresTick - world.Tick, SimConstants.SecondsToTicks(SimWorld.PingSeconds) - 2, SimConstants.SecondsToTicks(SimWorld.PingSeconds));
            // Too soon after the last; off the map; no such kind.
            Assert.Equal(OpResult.TooFast, Request(world, host, SimRequest.Ping(0, 10f, 10f, PingKind.Here), out _));
            SimDriver.StepTicks(world, SimConstants.TickRate);
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Ping(0, world.Map.WidthM + 5f, 10f, PingKind.Here), out _));
            var bad = SimRequest.Ping(0, 10f, 10f, PingKind.Here);
            bad.Count = 9;
            Assert.Equal(OpResult.BadRequest, Request(world, host, bad, out _));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Ping(0, 10f, 10f, PingKind.Materials), out _));
        }
    }
}
