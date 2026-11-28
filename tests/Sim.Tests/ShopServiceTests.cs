using System;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 10 §3's services at the Post Office counter (design choice 176): the Extended Forecast (100 g, from shift 1,
    /// once a shift: the coming Delivery's mail by street for everybody, at Payday the next shift's) and the Emergency PO
    /// Repair (200 g, from shift 2: the Post Office +1000 HP at once, not while it is unhurt).
    /// </summary>
    public class ShopServiceTests
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

        private OpResult Buy(SimWorld world, Player p, string id) => Request(world, p, SimRequest.Buy(0, Db.ShopItem(id).Index));

        private (SimWorld World, Player Host) InPrep(int shift, uint seed = 7, string map = "test_street")
        {
            var world = new SimWorld(Db, new SimSettings { Seed = seed, MapId = map, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.NoQuota, shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            for (int i = 0; i < 60 * Tps && world.Run.Phase != RunPhase.Prep; i++) world.Step();
            Assert.Equal((RunPhase.Prep, shift), (world.Run.Phase, world.Run.Shift));
            world.Economy.AddLoot(2000);
            SimDriver.Teleport(host, world.Map.PostOffice.ShopX + 1.0f, world.Map.PostOffice.ShopZ);
            SimDriver.StepTicks(world, 2);
            return (world, host);
        }

        private void IntoPayday(SimWorld world, Player host)
        {
            foreach (var p in world.Players.ToList()) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            world.Run.PhaseEndTick = world.Tick + 1;
            for (int i = 0; i < 5 * Tps && world.Run.Phase != RunPhase.Payday; i++) world.Step();
            Assert.Equal(RunPhase.Payday, world.Run.Phase);
        }

        [Fact]
        public void TheServices_AreContent_AsDocTenSaysThem()
        {
            var forecast = Db.ShopItem("forecast");
            Assert.Equal((ShopKind.Service, ShopService.Forecast, 100, 1, true, false), (forecast.Kind, forecast.Service, forecast.Price, forecast.FromShift, forecast.OncePerShift, forecast.OncePerRun));
            var repair = Db.ShopItem("po_repair");
            Assert.Equal((ShopKind.Service, ShopService.PostOfficeRepair, 200, 2, 1000, false, false), (repair.Kind, repair.Service, repair.Price, repair.FromShift, repair.Count, repair.OncePerShift, repair.OncePerRun));
        }

        [Fact]
        public void TheExtendedForecast_TellsEverybodyTheComingDeliverysMailByStreet_OnceAShift()
        {
            var (world, host) = InPrep(2, map: "small_island");
            var guest = world.AddPlayer("guest")!;
            int wallet = world.Economy.Wallet;
            Assert.Equal(OpResult.Ok, Buy(world, host, "forecast"));
            Assert.Equal(wallet - 100, world.Economy.Wallet);
            var forecast = world.Events.OfType<ForecastEvent>().Single();
            Assert.Equal(2, forecast.Shift);
            Assert.True(forecast.Audience.IsNone, "the forecast is not for everybody");
            Assert.NotEmpty(forecast.Streets);
            // Districts 1-2 open in shift 2: only their streets.
            Assert.All(forecast.Streets, s => Assert.InRange(world.Map.GetStreet(s.Street)!.District, 1, 2));
            Assert.Equal(OpResult.AlreadyOwned, Buy(world, guest, "forecast"));
            Assert.Equal(wallet - 100, world.Economy.Wallet);

            // The next shift sells it again; at Payday it is the next shift's.
            IntoPayday(world, host);
            Assert.Equal(OpResult.AlreadyOwned, Buy(world, host, "forecast"));
            world.Run.PhaseEndTick = world.Tick + 1;
            for (int i = 0; i < 5 * Tps && world.Run.Phase != RunPhase.Prep; i++) world.Step();
            Assert.Equal((RunPhase.Prep, 3), (world.Run.Phase, world.Run.Shift));
            IntoPayday(world, host);
            Assert.Equal(OpResult.Ok, Buy(world, host, "forecast"));
            Assert.Equal(4, world.Events.OfType<ForecastEvent>().Single().Shift);
        }

        [Fact]
        public void AtTheLastShiftsPayday_ThereIsNoShiftLeftToForecast()
        {
            var (world, host) = InPrep(5);
            IntoPayday(world, host);
            Assert.Equal(OpResult.WrongPhase, Buy(world, host, "forecast"));
        }

        [Fact]
        public void TheEmergencyRepair_GivesThePostOfficeAThousandHp_FromShiftTwo_NotWhileUnhurt()
        {
            var (early, host1) = InPrep(1);
            Assert.Equal(OpResult.ShopLocked, Buy(early, host1, "po_repair"));

            var (world, host) = InPrep(2);
            float max = world.PostOfficeMaxHp;
            Assert.Equal(OpResult.FullHealth, Buy(world, host, "po_repair"));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            world.DamagePostOffice(2500f);
            Assert.Equal(max - 2500f, world.PostOfficeHp);
            world.Run.PhaseEndTick = world.Tick + 1;
            for (int i = 0; i < 5 * Tps && world.Run.Phase != RunPhase.Payday; i++) world.Step();
            Assert.Equal(RunPhase.Payday, world.Run.Phase);
            int wallet = world.Economy.Wallet;
            Assert.Equal(OpResult.Ok, Buy(world, host, "po_repair"));
            Assert.Equal(max - 1500f, world.PostOfficeHp);
            Assert.Equal(wallet - 200, world.Economy.Wallet);
            // Not once a shift: again, and the last only up to the full bar.
            Assert.Equal(OpResult.Ok, Buy(world, host, "po_repair"));
            Assert.Equal(OpResult.Ok, Buy(world, host, "po_repair"));
            Assert.Equal(max, world.PostOfficeHp);
            Assert.Equal(OpResult.FullHealth, Buy(world, host, "po_repair"));
        }
    }
}
