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
    /// The Balloon (doc 05 §2 "90 HP, 30 AoE r=2 m, 3.0 s, 2.5 m/s; Turret > depot > Vehicle Depot >
    /// sorter; Airborne (6 m). Only ranged weapons and turrets can hit it. Drops bombs on turrets
    /// and logistics hubs"; §2.2 "1 Oil Can (30%)"; §3.2 cost 30 from shift 4; design choice 135).
    /// </summary>
    public class BalloonTests
    {
        private const int Tps = SimConstants.TickRate;
        private const float East = -MathF.PI / 2f;
        private static ContentDb Db => TestContent.Db;
        private static EnemyDef Balloon => Db.Enemy("balloon");
        private uint _nextMailId = 1;

        private static (SimWorld World, Player Carrier) OnTheStreet(float x = 10f, float z = 29f)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var p = world.AddPlayer("carrier")!;
            p.MaxHp = p.Hp = 10000f;
            SimDriver.Teleport(p, x, z);
            return (world, p);
        }

        private static Construct Build(SimWorld world, string id, float x, float z = 29f)
        {
            var def = Db.Building(id);
            uint container = def.Behaviour == BuildingBehaviour.Container ? world.AddWorldContainer(def.Container!.Id, x, z).Id : 0;
            return world.Constructs.Add(def, (int)MathF.Floor(x / SimConstants.TileSize), (int)MathF.Floor(z / SimConstants.TileSize), 0, def.Hp, container);
        }

        private static void Press(SimWorld world, Player p, InputButtons buttons, float yaw = East)
        {
            world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(yaw), Buttons = buttons });
            world.Step();
        }

        /// <summary>A balloon that a turret does not shoot down before it is over it (24 a second beat its 90 HP in 3.75 s).</summary>
        private static Enemy Tough(SimWorld world, float x, float z = 29f)
        {
            var e = world.SpawnEnemy(Balloon, x, z)!;
            e.MaxHp = e.Hp = 100000f;
            return e;
        }

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextMailId++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static void StepUntil(SimWorld world, Func<bool> done, int ticks)
        {
            for (int i = 0; i < ticks && !done(); i++) world.Step();
        }

        [Fact]
        public void TheContentHasTheBalloonOfTheDocs()
        {
            Assert.Equal((90f, 30f, 3f, 2.5f, 30, 4), (Balloon.Hp, Balloon.Damage, Balloon.AttackRate, Balloon.Speed, Balloon.Cost, Balloon.UnlockShift));
            Assert.Equal(EnemyTraits.Airborne, Balloon.Traits);
            Assert.Equal((6f, 2f), (Balloon.Altitude, Balloon.BombRadius));
            Assert.Equal(new[] { EnemyTarget.Turret, EnemyTarget.Depot, EnemyTarget.VehicleDepot, EnemyTarget.Sorter }, Balloon.TargetPriority);
            var drop = Assert.Single(Balloon.Drops);
            Assert.Equal(("oil_can", 300, 1), (drop.Item.Id, drop.ChancePermille, drop.Count));
            Assert.All(new[] { 4, 5 }, shift => Assert.Contains(Db.Waves.WeightsFor(shift), w => w.Key == Balloon));
            Assert.All(new[] { 2, 3 }, shift => Assert.DoesNotContain(Db.Waves.WeightsFor(shift), w => w.Key == Balloon));
            // Only it flies.
            Assert.All(Db.Enemies.Where(e => e != Balloon), e => Assert.Equal((false, 0f, 0f), (e.Airborne, e.Altitude, e.BombRadius)));
            Assert.True(EnemyDef.IsOf(EnemyTarget.Depot, Db.Building("depot")));
            Assert.False(EnemyDef.IsOf(EnemyTarget.Depot, Db.Building("chest")));
        }

        [Fact]
        public void ItFliesStraightOverAWall_ToTheTurretBehind_AndBombsIt_EveryThreeSeconds()
        {
            var (world, p) = OnTheStreet(30f, 29f);
            var turret = Build(world, "turret", 50f);
            // A line of Stone Walls across the street between them; a ground raider would go round or through.
            var walls = Enumerable.Range(0, 5).Select(i => Build(world, "wall_stone", 58f, 25f + i * 2f)).ToArray();
            var e = Tough(world, 66f);
            float startZ = e.Z;
            StepUntil(world, () => turret.Hp < turret.Def.Hp, 12 * Tps);
            Assert.Equal(turret.Def.Hp - 30, turret.Hp);
            Assert.All(walls, w => Assert.Equal(w.Def.Hp, w.Hp));
            // Straight there: it never left the line it came along.
            Assert.InRange(e.Z, startZ - 0.6f, startZ + 0.6f);
            var bomb = world.Events.OfType<EnemyShotEvent>().Single();
            Assert.Equal(EnemyShotKind.Bomb, bomb.Kind);
            Assert.True(bomb.AtConstruct);
            Assert.InRange(MathF.Abs(bomb.ToX - turret.CentreX), 0f, 1f);
            // The next one three seconds later; the carrier 20 m off is untouched.
            StepUntil(world, () => turret.Hp < turret.Def.Hp - 30, 4 * Tps);
            Assert.Equal(turret.Def.Hp - 60, turret.Hp);
            Assert.Equal(p.MaxHp, p.Hp);
        }

        [Fact]
        public void ABomb_HurtsEverythingWithinTwoMetres_CarriersToo()
        {
            var (world, p) = OnTheStreet(51.5f, 29f);
            var turret = Build(world, "turret", 50f);
            var belt = Build(world, "chest", 52f);
            var far = Build(world, "wall_wood", 58f);
            var other = world.AddPlayer("far")!;
            other.MaxHp = other.Hp = 10000f;
            SimDriver.Teleport(other, 56f, 29f);
            Tough(world, 64f);
            StepUntil(world, () => turret.Hp < turret.Def.Hp, 12 * Tps);
            Assert.Equal(turret.Def.Hp - 30, turret.Hp);
            Assert.Equal(belt.Def.Hp - 30, belt.Hp);
            Assert.Equal(far.Def.Hp, far.Hp);
            Assert.Equal(p.MaxHp - 30f, p.Hp);
            Assert.Equal(other.MaxHp, other.Hp);
        }

        [Fact]
        public void TurretFirst_ThenADepot_NeverAChest_AndWithNothingOfItsOwn_ItHoldsTheLine()
        {
            var (world, p) = OnTheStreet(20f, 29f);
            var chest = Build(world, "chest", 56f);
            var depot = Build(world, "depot", 46f, 26f);
            var turret = Build(world, "turret", 40f);
            var e = Tough(world, 66f);
            StepUntil(world, () => turret.Hp < turret.Def.Hp || depot.Hp < depot.Def.Hp, 20 * Tps);
            Assert.Equal(turret.Def.Hp - 30, turret.Hp);
            Assert.Equal(chest.Def.Hp, chest.Hp);

            // The turret gone, the depot is next.
            world.Constructs.Remove(turret.Tx, turret.Tz);
            StepUntil(world, () => depot.Hp < depot.Def.Hp, 20 * Tps);
            Assert.Equal(depot.Def.Hp - 30, depot.Hp);
            Assert.Equal(chest.Def.Hp, chest.Hp);

            // Nothing of its targetPriority left: it hovers on the raid's line, over nothing it bombs.
            world.Constructs.Remove(depot.Tx, depot.Tz);
            world.Events.Clear();
            for (int i = 0; i < 20 * Tps; i++) world.Step();
            Assert.Equal(chest.Def.Hp, chest.Hp);
            Assert.Equal(p.MaxHp, p.Hp);
            world.HoldSpot(e, out float hx, out float hz);
            Assert.InRange(MathF.Sqrt((e.X - hx) * (e.X - hx) + (e.Z - hz) * (e.Z - hz)), 0f, 1f);
        }

        [Fact]
        public void TheHammerAndSpikesDoNotReachIt_TheCannonAndATurretDo()
        {
            var (world, p) = OnTheStreet(50f, 29f);
            var spikes = Build(world, "spike_strip", 51.5f);
            var e = world.SpawnEnemy(Balloon, 51.2f, 29f)!;
            for (int i = 0; i < 2 * Tps; i++)
            {
                Press(world, p, InputButtons.Attack);
                e.X = 51.2f; e.Z = 29f;
            }
            Assert.Equal(Balloon.Hp, e.Hp);
            Assert.True(p.Hp == p.MaxHp && world.Constructs.At(spikes.Tx, spikes.Tz) == spikes);

            // The mail cannon's package goes off on it.
            var home = world.Map.Destinations[0];
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(TestContent.Mail("small", home.Address, _nextMailId++))));
            world.Step();
            SimDriver.Teleport(p, 44f, 29f);
            e.X = 52f; e.Z = 29f;
            Press(world, p, InputButtons.AltAttack);
            StepUntil(world, () => e.Hp < Balloon.Hp, Tps);
            Assert.True(e.Hp < Balloon.Hp, "the cannon's package did not reach the balloon");

            // A turret fells it.
            Build(world, "turret", 40f);
            StepUntil(world, () => world.GetEnemy(e.Id) == null, 10 * Tps);
            Assert.Null(world.GetEnemy(e.Id));
            Assert.Equal(spikes.Def.Hp, spikes.Hp);
        }

        [Fact]
        public void NothingIsKeptFromBeingBuiltUnderIt()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, 3);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 6)));
            const int tx = 22, tz = 15;
            SimDriver.Teleport(host, BuildRules.TileCentre(tx) - 2.6f, BuildRules.TileCentre(tz));
            SimDriver.StepTicks(world, Player.HistoryLength);
            var wall = Db.Building("wall_wood");
            // A raider on the tile is in the way; a balloon over it is not.
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), BuildRules.TileCentre(tx), BuildRules.TileCentre(tz))!;
            Assert.Equal(OpResult.Occupied, Request(world, host, SimRequest.PlaceConstruct(0, wall.Index, tx, tz, 0)));
            world.DamageEnemy(raider, 9999f, null);
            var e = world.SpawnEnemy(Balloon, BuildRules.TileCentre(tx), BuildRules.TileCentre(tz))!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, wall.Index, tx, tz, 0)));
            Assert.NotNull(world.Constructs.At(tx, tz));
            Assert.NotNull(world.GetEnemy(e.Id));
        }
    }
}
