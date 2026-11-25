using System;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Vehicles;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Oil Pump (doc 10 §2.3, doc 11 "Pump output 1 Oil Can per 45 s", doc 12 M2 "Oil Pump for
    /// vehicle fuel (no power draw)"; design choice 116): oil seeps on the test street and on
    /// every Small Island, the pump behind Blueprint: Oil on a seep only, a can into its tank every
    /// 45 s of Prep and Delivery until the tank is full, taken out as from a chest, and a truck
    /// parked beside it filled from it.
    /// </summary>
    public class PumpTests
    {
        private uint _nextRequest = 1;
        private const int Tps = SimConstants.TickRate;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Pump => Db.Building("oil_pump");
        private static ItemDef Can => Db.Item("oil_can");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InPrep(bool blueprint = true)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, 3);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(2000);
            if (blueprint) Assert.True(world.Grant(Db.ShopItem("bp_oil"), host.Id));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 12)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("glass"), 4)));
            var (sx, sz) = world.Map.OilSeeps[0];
            SimDriver.Teleport(host, BuildRules.TileCentre(sx - 1), BuildRules.TileCentre(sz));
            SimDriver.StepTicks(world, 12);
            return (world, host);
        }

        private static int Cans(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == Can.Index).Sum(e => e.Stack.ItemCount);

        [Fact]
        public void TheContentHasThePumpOfTheDocs_AndTheMapsHaveSeeps()
        {
            Assert.Equal((1, 1, 400, BuildingBehaviour.Pump, 45f, true), (Pump.Width, Pump.Length, Pump.Hp, Pump.Behaviour, Pump.CycleSeconds, Pump.Solid));
            Assert.Equal(new[] { ("iron_ingot", 6), ("glass", 2) }, Pump.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Same(Db.ShopItem("bp_oil"), Pump.Recipe.Blueprint);
            // On the chest's key, after the Small Port: the bar has room for ten keys.
            Assert.Equal(Db.Building("chest").Recipe.Key, Pump.Recipe.Key);
            Assert.Equal((ShopKind.Blueprint, 600, 3), (Db.ShopItem("bp_oil").Kind, Db.ShopItem("bp_oil").Price, Db.ShopItem("bp_oil").FromShift));
            Assert.Equal((2, 2), (Pump.Container!.Cols, Pump.Container.Rows));

            var street = Db.Map("test_street");
            Assert.Equal(new[] { (28, 22), (34, 23) }, street.OilSeeps);
            for (uint seed = 1; seed <= 30; seed++)
            {
                var map = WorldGenerator.Generate(Db, seed);
                Assert.InRange(map.OilSeeps.Count, 2, WorldGenerator.SeepCount);
                foreach (var (x, z) in map.OilSeeps)
                {
                    Assert.Equal(TileType.Grass, map.GetTile(x, z));
                    Assert.False(map.IsNavBlocked(x, z), $"seed {seed}: something stands on the seep at ({x}, {z})");
                    Assert.DoesNotContain(map.Props, p => p.Solid && p.Distance(BuildRules.TileCentre(x), BuildRules.TileCentre(z), out _, out _) < 1.5f);
                }
            }
        }

        [Fact]
        public void APumpIsBuiltOnASeepOnly_WithTheBlueprint()
        {
            var (bare, nobody) = InPrep(blueprint: false);
            var (sx, sz) = bare.Map.OilSeeps[0];
            Assert.Equal(OpResult.NeedsBlueprint, Request(bare, nobody, SimRequest.PlaceConstruct(0, Pump.Index, sx, sz, 0)));

            var (world, host) = InPrep();
            Assert.Equal(OpResult.NotOnSeep, Request(world, host, SimRequest.PlaceConstruct(0, Pump.Index, sx, sz + 1, 0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Pump.Index, sx, sz, 0)));
            var pump = world.Constructs.At(sx, sz)!;
            Assert.True(pump.Def.IsPump && pump.ContainerId != 0);
        }

        [Fact]
        public void ItFillsACanEveryFortyFiveSecondsOfAWorkingPhase_UntilItsTankIsFull_AndTheCansAreTakenOut()
        {
            var (world, host) = InPrep();
            var (sx, sz) = world.Map.OilSeeps[0];
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Pump.Index, sx, sz, 0)));
            var pump = world.Constructs.At(sx, sz)!;
            var tank = world.GetContainer(pump.ContainerId)!;
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            SimDriver.StepTicks(world, 44 * Tps);
            Assert.Equal(0, Cans(tank));
            SimDriver.StepTicks(world, Tps + 2);
            Assert.Equal(1, Cans(tank));

            // Ten fill the tank (2 x 2: two stacks of five cans of 1 x 2); then it stops and waits.
            Assert.True(world.Give(tank, ItemStack.OfItem(Can, 8)));
            SimDriver.StepTicks(world, 45 * Tps + 2);
            Assert.Equal(10, Cans(tank));
            SimDriver.StepTicks(world, 46 * Tps);
            Assert.Equal(10, Cans(tank));
            Assert.Equal(0u, world.PumpWork(pump));

            // Opened from beside it, a stack of five cans comes out into the pockets.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Open(0, tank.Id)));
            var entry = tank.Entries.First();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.QuickMove(0, tank.Id, entry.EntryId)));
            Assert.Equal(5, Cans(tank));
            Assert.Equal(5, Cans(host.Hotbar) + Cans(host.Inventory));
        }

        [Fact]
        public void NothingIsPumpedAtPayday()
        {
            var (world, host) = InPrep();
            var (sx, sz) = world.Map.OilSeeps[0];
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Pump.Index, sx, sz, 0)));
            var pump = world.Constructs.At(sx, sz)!;
            SimDriver.StepTicks(world, 10 * Tps);
            uint work = world.PumpWork(pump);
            Assert.True(work > 0);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            for (int i = 0; i < 5 * Tps && world.Run.Phase != RunPhase.Delivery; i++) world.Step();
            world.Run.PhaseEndTick = world.Tick + 1;
            for (int i = 0; i < 3 * Tps && world.Run.Phase != RunPhase.Payday; i++) world.Step();
            Assert.Equal(RunPhase.Payday, world.Run.Phase);
            work = world.PumpWork(pump);
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Equal(work, world.PumpWork(pump));
        }

        [Fact]
        public void ATruckParkedBesideIt_IsFilledFromItsTank()
        {
            var (world, host) = InPrep();
            var (sx, sz) = world.Map.OilSeeps[0];
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Pump.Index, sx, sz, 0)));
            var pump = world.Constructs.At(sx, sz)!;
            var tank = world.GetContainer(pump.ContainerId)!;
            Assert.True(world.Give(tank, ItemStack.OfItem(Can, 2)));
            var truck = world.AddVehicleAt(Db.Vehicle("mail_truck"), pump.CentreX + 2.5f, pump.CentreZ, 0f, host.Id);
            truck.Fuel = 100f;
            SimDriver.StepTicks(world, Tps + 1);
            Assert.Equal(400f, truck.Fuel);
            Assert.Equal(1, Cans(tank));
            // Its tank takes no whole can more (400 + 300 > 600): the second stays.
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.Equal(1, Cans(tank));
            Assert.Equal(400f, truck.Fuel);

            // Farther off, nothing.
            var far = world.AddVehicleAt(Db.Vehicle("mail_truck"), pump.CentreX + 8f, pump.CentreZ, 0f, host.Id);
            far.Fuel = 0f;
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.Equal(0f, far.Fuel);
        }
    }
}
