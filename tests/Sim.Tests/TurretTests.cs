using System;
using System.Linq;
using PerformativeMail.Sim.Combat;
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
    /// The Turret (doc 05 §4 "1 tile, 400 HP, Blueprint; 6 Iron Ingot, 2 Stone; 12 dmg / 0.5 s,
    /// 18 m range; always fires at full rate; targets nearest enemy"; design choice 123): behind
    /// Blueprint: Turret, it shoots the nearest raider within 18 m twice a second, never one
    /// further off, and never a carrier.
    /// </summary>
    public class TurretTests
    {
        private const int Tps = SimConstants.TickRate;
        /// <summary>Open ground south of the street, between the mailboxes of numbers 4 and 6.</summary>
        private const int Tx = 22, Tz = 15;

        private uint _nextRequest = 1;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Turret => Db.Building("turret");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static float Centre(int t) => BuildRules.TileCentre(t);

        private (SimWorld World, Player Host) InPrep(bool blueprint = true)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, 3);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            if (blueprint) Assert.True(world.Grant(Db.ShopItem("bp_turret"), host.Id));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 12)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 4)));
            SimDriver.Teleport(host, Centre(Tx), Centre(Tz) + 2.6f);
            SimDriver.StepTicks(world, 12);
            return (world, host);
        }

        private (SimWorld World, Player Host, Construct Turret) Built()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Turret.Index, Tx, Tz, 0)));
            var built = world.Constructs.At(Tx, Tz)!;
            Assert.Same(Turret, built.Def);
            return (world, host, built);
        }

        [Fact]
        public void TheContentHasTheTurretOfTheDocs()
        {
            Assert.Equal((1, 1, 400, BuildingBehaviour.Turret, true), (Turret.Width, Turret.Length, Turret.Hp, Turret.Behaviour, Turret.Solid));
            Assert.Equal((12f, 0.5f, 18f), (Turret.ShotDamage, Turret.ShotSeconds, Turret.ShotRange));
            Assert.Equal(new[] { ("iron_ingot", 6), ("stone", 2) }, Turret.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Same(Db.ShopItem("bp_turret"), Turret.Recipe.Blueprint);
            Assert.Equal((ShopKind.Blueprint, 600, 3), (Db.ShopItem("bp_turret").Kind, Db.ShopItem("bp_turret").Price, Db.ShopItem("bp_turret").FromShift));
            // On the Alarm Post's key: the defences' towers.
            Assert.Equal(Db.Building("alarm_post").Recipe.Key, Turret.Recipe.Key);
            Assert.All(Db.Buildings.Where(b => b != Turret), b => Assert.Equal(0f, b.ShotDamage));
        }

        [Fact]
        public void WithoutTheBlueprint_NoTurretIsBuilt()
        {
            var (world, host) = InPrep(blueprint: false);
            Assert.Equal(OpResult.NeedsBlueprint, Request(world, host, SimRequest.PlaceConstruct(0, Turret.Index, Tx, Tz, 0)));
            Assert.Null(world.Constructs.At(Tx, Tz));
        }

        [Fact]
        public void ItShootsTheNearestRaiderInRange_TwiceASecond_UntilItFalls()
        {
            var (world, host, turret) = Built();
            SimDriver.Teleport(host, Centre(Tx) + 40f, Centre(Tz) + 20f);
            var raider = Db.Enemy("barbarian");
            var near = world.SpawnEnemy(raider, turret.CentreX + 10f, turret.CentreZ)!;
            var further = world.SpawnEnemy(raider, turret.CentreX - 14f, turret.CentreZ)!;

            world.Events.Clear();
            world.Step();
            var first = world.Events.OfType<TurretFiredEvent>().Single();
            Assert.Equal(((byte)Tx, (byte)Tz), (first.X, first.Y));
            Assert.Equal(near.X, first.TargetX, 3);
            Assert.Equal(raider.Hp - 12f, near.Hp, 3);
            Assert.Equal(raider.Hp, further.Hp, 3);

            // 0.5 s between shots: the second comes 15 ticks after the first.
            int shots = 0;
            for (int i = 0; i < Tps / 2 - 1; i++) { world.Events.Clear(); world.Step(); shots += world.Events.OfType<TurretFiredEvent>().Count(); }
            Assert.Equal(0, shots);
            world.Events.Clear();
            world.Step();
            Assert.Single(world.Events.OfType<TurretFiredEvent>());

            // 60 HP is five shots; then the next nearest has the gun.
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.Null(world.GetEnemy(near.Id));
            Assert.True(further.Hp < raider.Hp || world.GetEnemy(further.Id) == null);
            Assert.True(world.TurretShots >= 6);
        }

        [Fact]
        public void NoShotBeyond18m_NorAtAFleeingRaider_NorAtACarrier()
        {
            var (world, host, turret) = Built();
            float hp = host.Hp;
            var far = world.SpawnEnemy(Db.Enemy("barbarian"), turret.CentreX + 19f, turret.CentreZ)!;
            world.Step();
            Assert.Equal(0, world.TurretShots);
            Assert.Equal(Db.Enemy("barbarian").Hp, far.Hp, 3);

            var fleeing = world.SpawnEnemy(Db.Enemy("barbarian"), turret.CentreX + 4f, turret.CentreZ)!;
            fleeing.State = EnemyState.Flee;
            far.State = EnemyState.Flee;
            SimDriver.StepTicks(world, Tps);
            Assert.Equal(0, world.TurretShots);
            Assert.Equal(Db.Enemy("barbarian").Hp, fleeing.Hp, 3);
            // The carrier stood beside it all along.
            Assert.Equal(hp, host.Hp);
        }
    }
}
