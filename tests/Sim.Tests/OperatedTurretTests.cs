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
    /// The turret operated by a carrier (doc 05 §4 "Player-operated: 2× fire rate and manual aim";
    /// design choice 125): a carrier beside it takes its controls; the gun then fires where they
    /// face while they hold attack, twice as often, and can miss; walking off, going down or
    /// letting go hands it back to itself.
    /// </summary>
    public class OperatedTurretTests
    {
        private const int Tps = SimConstants.TickRate;
        private const int Tx = 22, Tz = 15;
        private const float East = -MathF.PI / 2f, North = 0f;

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

        /// <summary>One tick of input: <paramref name="buttons"/> held, facing <paramref name="yaw"/>; the shots it fired.</summary>
        private static int Hold(SimWorld world, Player p, InputButtons buttons, float yaw, int ticks = 1)
        {
            int shots = 0;
            for (int i = 0; i < ticks; i++)
            {
                world.Events.Clear();
                world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(yaw), Buttons = buttons });
                world.Step();
                shots += world.Events.OfType<TurretFiredEvent>().Count();
            }
            return shots;
        }

        /// <summary>A turret built on open ground, its builder standing 2 m east of it.</summary>
        private (SimWorld World, Player Host, Construct Turret) Built()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, 3);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.True(world.Grant(Db.ShopItem("bp_turret"), host.Id));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 6)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 2)));
            SimDriver.Teleport(host, Centre(Tx), Centre(Tz) + 2.6f);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Turret.Index, Tx, Tz, 0)));
            var built = world.Constructs.At(Tx, Tz)!;
            SimDriver.Teleport(host, built.CentreX + 2f, built.CentreZ);
            SimDriver.StepTicks(world, Player.HistoryLength);
            return (world, host, built);
        }

        [Fact]
        public void TheContentHasTheDocsTwiceTheRate()
        {
            Assert.Equal(2f, Turret.OperatedRate);
            Assert.All(Db.Buildings.Where(b => b != Turret), b => Assert.Equal(0f, b.OperatedRate));
        }

        [Fact]
        public void TakingTheControls_TheGunFiresWhereTheyFace_TwiceAsOften_AndTheKillIsTheirs()
        {
            var (world, host, turret) = Built();
            var raider = Db.Enemy("barbarian");
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.OperateTurret(0, Tx, Tz, true)));
            var taken = world.Events.OfType<TurretOperatedEvent>().Single();
            Assert.Equal(((byte)Tx, (byte)Tz, host.Id), (taken.X, taken.Y, taken.Player));
            Assert.Same(turret, host.Turret);
            Assert.Same(host, world.TurretOperator(turret));

            // The nearer raider is west, behind the carrier's back: by itself the gun would take it first.
            var ahead = world.SpawnEnemy(raider, turret.CentreX + 12f, turret.CentreZ)!;
            var behind = world.SpawnEnemy(raider, turret.CentreX - 9f, turret.CentreZ)!;
            // Without attack held the gun is still.
            Assert.Equal(0, Hold(world, host, InputButtons.None, East, Tps / 2));
            Assert.Equal(raider.Hp, ahead.Hp, 3);

            // Held, it fires every 0.25 s along the facing: 60 HP is five shots in 1.25 s (40 ticks hold six).
            // The carrier keeps the gun on it as it comes.
            int shots = 0;
            for (int i = 0; i < 40; i++)
                shots += Hold(world, host, InputButtons.Attack, MathF.Atan2(-(ahead.X - turret.CentreX), -(ahead.Z - turret.CentreZ)));
            Assert.Null(world.GetEnemy(ahead.Id));
            Assert.Equal(raider.Hp, behind.Hp, 3);
            Assert.InRange(shots, 5, 6);
            Assert.Equal(1, host.Kills);
            // No hammer was swung with the hands on the controls.
            Assert.True(host.HammerReadyTick == 0);
        }

        [Fact]
        public void HeldForTwoSeconds_ItFiresEightShots_TheAutomaticFour()
        {
            var (world, host, turret) = Built();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.OperateTurret(0, Tx, Tz, true)));
            // Facing north at nothing: every shot misses, and is drawn to where the carrier's line of
            // sight (from 2 m east of the turret) leaves its reach.
            int shots = Hold(world, host, InputButtons.Attack, North, 1);
            var miss = world.Events.OfType<TurretFiredEvent>().Single();
            Assert.Equal(host.Move.X, miss.TargetX, 3);
            Assert.Equal(turret.CentreZ - MathF.Sqrt(Turret.ShotRange * Turret.ShotRange - 4f), miss.TargetZ, 2);
            shots += Hold(world, host, InputButtons.Attack, North, 2 * Tps - 1);
            Assert.Equal(8, shots);
        }

        [Fact]
        public void AimedAside_ItMisses()
        {
            var (world, host, turret) = Built();
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), turret.CentreX + 12f, turret.CentreZ)!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.OperateTurret(0, Tx, Tz, true)));
            Assert.True(Hold(world, host, InputButtons.Attack, North, Tps) >= 4);
            Assert.Equal(Db.Enemy("barbarian").Hp, raider.Hp, 3);
        }

        [Fact]
        public void ACloseRaiderUnderTheCrosshair_BesideTheBarrel_IsHit()
        {
            // The 0abedd1 playtest: the shot left the turret's middle along the carrier's facing, so a
            // raider close by under the crosshair but off to the side of the barrel was missed.
            var (world, host, turret) = Built();
            // The carrier stands 2 m east of the turret and looks north at a raider 3 m ahead of them:
            // 2 m to the side of the line from the turret's middle, on their own line of sight.
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), host.Move.X, host.Move.Z - 3f)!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.OperateTurret(0, Tx, Tz, true)));
            Hold(world, host, InputButtons.Attack, North, 1);
            var shot = world.Events.OfType<TurretFiredEvent>().Single();
            Assert.InRange(MathF.Abs(shot.TargetX - raider.X) + MathF.Abs(shot.TargetZ - raider.Z), 0f, 0.5f);
            Assert.Equal(Db.Enemy("barbarian").Hp - Turret.ShotDamage, raider.Hp, 3);

            // The nearer of two on the line of sight takes the shot; one beyond the gun's reach none.
            var far = world.SpawnEnemy(Db.Enemy("barbarian"), turret.CentreX + 2f, turret.CentreZ + Turret.ShotRange + 3f)!;
            Hold(world, host, InputButtons.Attack, MathF.PI, Tps / 2);
            Assert.Equal(Db.Enemy("barbarian").Hp, far.Hp, 3);
        }

        [Fact]
        public void LetGo_WalkedOff_OrDown_TheTurretFiresByItselfAgain()
        {
            var (world, host, turret) = Built();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.OperateTurret(0, Tx, Tz, true)));
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), turret.CentreX - 12f, turret.CentreZ)!;
            Assert.Equal(0, Hold(world, host, InputButtons.None, East, Tps));

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.OperateTurret(0, Tx, Tz, false)));
            Assert.Equal(EntityId.None, world.Events.OfType<TurretOperatedEvent>().Single().Player);
            Assert.Null(host.Turret);
            Assert.True(Hold(world, host, InputButtons.None, East, Tps) >= 1);
            Assert.True(raider.Hp < Db.Enemy("barbarian").Hp);

            // Walked off: the controls go back of themselves.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.OperateTurret(0, Tx, Tz, true)));
            SimDriver.Teleport(host, turret.CentreX + 6f, turret.CentreZ);
            world.Events.Clear();
            world.Step();
            Assert.Null(host.Turret);
            Assert.Null(world.TurretOperator(turret));
            Assert.Contains(world.Events.OfType<TurretOperatedEvent>(), e => e.Player == EntityId.None);
        }

        [Fact]
        public void OneCarrierAtATime_AndOnlyFromBesideIt()
        {
            var (world, host, turret) = Built();
            var other = world.AddPlayer("other")!;
            SimDriver.Teleport(other, turret.CentreX - 2f, turret.CentreZ);
            SimDriver.StepTicks(world, Player.HistoryLength);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.OperateTurret(0, Tx, Tz, true)));
            Assert.Equal(OpResult.Manned, Request(world, other, SimRequest.OperateTurret(0, Tx, Tz, true)));
            Assert.Equal(OpResult.BadRequest, Request(world, other, SimRequest.OperateTurret(0, Tx, Tz, false)));

            SimDriver.Teleport(other, turret.CentreX - 12f, turret.CentreZ);
            SimDriver.StepTicks(world, Player.HistoryLength);
            Assert.Equal(OpResult.OutOfRange, Request(world, other, SimRequest.OperateTurret(0, Tx, Tz, true)));
            Assert.Equal(OpResult.BadRequest, Request(world, other, SimRequest.OperateTurret(0, Tx + 3, Tz, true)));
            Assert.Same(host, world.TurretOperator(turret));

            // Taken down, the turret's controls are nobody's.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RemoveConstruct(0, Tx, Tz)));
            world.Step();
            Assert.Null(host.Turret);
        }
    }
}
