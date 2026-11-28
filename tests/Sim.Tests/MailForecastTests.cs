using System;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The free mail forecast (doc 01 §2.1 "mail forecast (counts by size class and district)", doc 09 §2.9 Payday's
    /// "mail forecast for the next shift"; design choice 177): as each Prep begins, and at a Payday the run goes on from,
    /// everybody hears about how many pieces of each kind a Delivery brings each district — the Extended Forecast's
    /// estimate by kind instead of by street.
    /// </summary>
    public class MailForecastTests
    {
        private uint _nextRequest = 1;
        private const int Tps = SimConstants.TickRate;
        private static ContentDb Db => TestContent.Db;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        [Fact]
        public void EveryPrep_EverybodyHearsTheShiftsMailByKindAndDistrict_AsTheStreetForecastReckonsIt()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 5, MapId = WorldGenerator.SmallIsland, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.NoQuota, 3);
            world.Submit(host.Id, SimRequest.StartRun(_nextRequest++));
            MailForecastEvent? heard = null;
            for (int i = 0; i < 60 * Tps && world.Run.Phase != RunPhase.Prep; i++)
            {
                world.Events.Clear();
                world.Step();
                heard ??= world.Events.OfType<MailForecastEvent>().FirstOrDefault();
            }
            Assert.Equal((RunPhase.Prep, 3), (world.Run.Phase, world.Run.Shift));
            Assert.NotNull(heard);
            Assert.Equal(3, heard!.Shift);
            Assert.True(heard.Audience.IsNone);
            Assert.Same(heard, world.LastMailForecast);
            Assert.All(heard.Counts, c => Assert.InRange(c.District, 1, 3));
            Assert.Contains(heard.Counts, c => c.Kind == Db.MailKind("letter").Index);

            // The same estimate as the Extended Forecast's streets, by kind.
            var byStreet = world.Spawner.Forecast(3, 1000).Values.Sum();
            var byKind = world.Spawner.ForecastByKind(3, 1000).Values.Sum();
            Assert.InRange(byKind, byStreet - 1e-6, byStreet + 1e-6);
            Assert.True(byKind > 0);
        }

        [Fact]
        public void AtPayday_TheNextShiftsComes_ButNotAfterTheLastShift()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 5, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.NoQuota, 4);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            world.Run.PhaseEndTick = world.Tick + 1;
            MailForecastEvent? heard = null;
            for (int i = 0; i < 5 * Tps && world.Run.Phase != RunPhase.Payday; i++)
            {
                world.Events.Clear();
                world.Step();
                heard ??= world.Events.OfType<MailForecastEvent>().FirstOrDefault();
            }
            Assert.Equal(RunPhase.Payday, world.Run.Phase);
            Assert.Equal(5, (int?)heard?.Shift);

            world.Run.PhaseEndTick = world.Tick + 1;
            for (int i = 0; i < 5 * Tps && world.Run.Phase != RunPhase.Prep; i++) world.Step();
            Assert.Equal((RunPhase.Prep, 5), (world.Run.Phase, world.Run.Shift));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            world.Run.PhaseEndTick = world.Tick + 1;
            heard = null;
            for (int i = 0; i < 5 * Tps && world.Run.Phase != RunPhase.Payday; i++)
            {
                world.Events.Clear();
                world.Step();
                heard ??= world.Events.OfType<MailForecastEvent>().FirstOrDefault();
            }
            Assert.Equal(RunPhase.Payday, world.Run.Phase);
            Assert.Null(heard);
        }
    }
}
