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
    /// Lag compensation (doc 06 §4.3 "Melee: server checks the target within an expanded hit volume
    /// (+0.3 m) using the target's position at serverTick − attackerRTT/2 (rewind buffer of 250 ms)";
    /// doc 12 M3 "Player combat: melee arc, hitscan, lag compensation rewind buffer"; design choice 145):
    /// the swing, the Slingshot's Stone and a manned turret's shot are judged where the attacker's
    /// client drew the raiders, at most 250 ms back.
    /// </summary>
    public class LagCompensationTests
    {
        private static ContentDb Db => TestContent.Db;
        private static EnemyDef Barbarian => Db.Enemy("barbarian");
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        /// <summary>In Delivery without raids, the host on the street at (30, 28), <paramref name="item"/> held (none: hands).</summary>
        private (SimWorld World, Player Host) Street(string? item = null)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false, StartingKits = false });
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetPlaytest(host, PlaytestFlags.None, 1);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            if (item != null)
            {
                var def = Db.Item(item);
                Assert.True(world.Give(host.Hotbar, ItemStack.OfItem(def, 1)));
                host.HeldSlot = (byte)host.Hotbar.Entries.Single(x => x.Stack.Kind == StackKind.Item && x.Stack.DefIndex == def.Index).X;
            }
            if (item == "slingshot") Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 2)));
            SimDriver.Teleport(host, 30f, 28f);
            return (world, host);
        }

        /// <summary>A raider that stands still unless walked: it is put at each point in turn, one a tick.</summary>
        private static Enemy Still(SimWorld world, float x, float z)
        {
            var e = world.SpawnEnemy(Barbarian, x, z)!;
            e.Speed = e.BaseSpeed = 0f;
            return e;
        }

        /// <summary>Walks the raider through the points, one a tick; the tick it stood at each.</summary>
        private static uint[] Walk(SimWorld world, Enemy e, params (float X, float Z)[] points)
        {
            var ticks = new uint[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                e.X = points[i].X;
                e.Z = points[i].Z;
                world.Step();
                ticks[i] = world.Tick;
            }
            return ticks;
        }

        /// <summary>One tick of attack facing (<paramref name="x"/>, <paramref name="z"/>), the client having drawn the raiders at <paramref name="drawn"/> (0: not said).</summary>
        private static void Attack(SimWorld world, Player p, float x, float z, uint drawn)
        {
            world.Events.Clear();
            float yaw = MathF.Atan2(-(x - p.Move.X), -(z - p.Move.Z));
            world.QueueInput(p.Id, new PlayerInput
            {
                Seq = p.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(yaw), Buttons = InputButtons.Attack, HeldSlot = p.HeldSlot,
                View = drawn == 0 ? (ushort)0 : PlayerInput.ViewOf(drawn),
            });
            world.Step();
        }

        /// <summary>A raider crossing the Stone's line at x 20 southwards, half a metre a tick: on it at the first point.</summary>
        private static (float, float)[] Crossing(int ticks) =>
            Enumerable.Range(0, ticks).Select(k => (20f, 28f + 0.5f * k)).ToArray();

        [Fact]
        public void TheSlingshot_HitsTheRaiderWhereTheClientDrewIt_FiveTicksAgo()
        {
            var (world, host) = Street("slingshot");
            var e = Still(world, 20f, 28f);
            var at = Walk(world, e, Crossing(6));
            // Now 2.5 m off the line: judged where it stands, the Stone misses.
            Attack(world, host, 0f, 28f, 0);
            Assert.Equal(SlingHit.None, world.Events.OfType<SlingshotFiredEvent>().Single().Hit);
            Assert.Equal(Barbarian.Hp, e.Hp);

            var (world2, host2) = Street("slingshot");
            var e2 = Still(world2, 20f, 28f);
            at = Walk(world2, e2, Crossing(6));
            Attack(world2, host2, 0f, 28f, at[0]);
            var shot = world2.Events.OfType<SlingshotFiredEvent>().Single();
            Assert.Equal(SlingHit.Ground, shot.Hit);
            Assert.Equal(Barbarian.Hp - 12f, e2.Hp);
            Assert.Equal(5, world2.LastRewindTicks);
            // The Stone is drawn to where the client saw it, on the line.
            Assert.InRange(shot.ToX, 20f - e2.Radius - 0.5f, 20f + e2.Radius);
            Assert.Equal(28f, shot.ToZ, 2);
        }

        [Fact]
        public void TheRewind_IsAtMost250Milliseconds()
        {
            Assert.Equal(7, SimWorld.MaxRewindTicks);
            // On the line ten ticks ago: rewound only seven, where it was 1.5 m off it.
            var (world, host) = Street("slingshot");
            var e = Still(world, 20f, 28f);
            var at = Walk(world, e, Crossing(11));
            Attack(world, host, 0f, 28f, at[0]);
            Assert.Equal(SimWorld.MaxRewindTicks, world.LastRewindTicks);
            Assert.Equal(Barbarian.Hp, e.Hp);

            // Seven ticks ago is still within it.
            var (world2, host2) = Street("slingshot");
            var e2 = Still(world2, 20f, 28f);
            at = Walk(world2, e2, Crossing(8));
            Attack(world2, host2, 0f, 28f, at[0]);
            Assert.Equal(7, world2.LastRewindTicks);
            Assert.Equal(Barbarian.Hp - 12f, e2.Hp);
        }

        [Fact]
        public void AClientDrawingAheadOfTheServer_IsNotRewound()
        {
            var (world, host) = Street("slingshot");
            Assert.Equal(0, world.RewindTicks(new PlayerInput { View = PlayerInput.ViewOf(world.Tick + 3) }));
            Assert.Equal(0, world.RewindTicks(new PlayerInput { View = 0 }));
            Assert.Equal(4, world.RewindTicks(new PlayerInput { View = PlayerInput.ViewOf(world.Tick - 5) }));
            // The wire's 16 bits never say "none" for a real tick.
            Assert.Equal((ushort)1, PlayerInput.ViewOf(65536.0));
            Assert.Equal((ushort)0, PlayerInput.ViewOf(0.4));
            Assert.Equal((ushort)4465, PlayerInput.ViewOf(70001.2));
            _ = host;
        }

        [Fact]
        public void TheHammer_HitsTheRaiderWhereTheClientDrewIt_AndShovesItFromWhereItIs()
        {
            // A raider 1.5 m west of the host, backing off a metre a tick: 4.5 m off as the swing comes.
            var path = new[] { (28.5f, 28f), (27.5f, 28f), (26.5f, 28f), (25.5f, 28f) };
            var (world, host) = Street();
            var e = Still(world, 28.5f, 28f);
            Walk(world, e, path);
            Attack(world, host, 0f, 28f, 0);
            Assert.Equal(Barbarian.Hp, e.Hp);

            var (world2, host2) = Street();
            var e2 = Still(world2, 28.5f, 28f);
            var at = Walk(world2, e2, path);
            Attack(world2, host2, 0f, 28f, at[0]);
            Assert.True(e2.Hp < Barbarian.Hp);
            Assert.Equal(3, world2.LastRewindTicks);
            Assert.True(e2.PushX < 0f); // away from the host, west
        }

        [Fact]
        public void TheHammersReach_IsTheRaidersBodyAndThreeTenthsOfAMetre()
        {
            float reach = Db.Balance.GetFloat("hammerRange");
            var (world, host) = Street();
            var e = Still(world, 30f - reach - Barbarian.Radius - 0.2f, 28f);
            Walk(world, e, (e.X, e.Z));
            Attack(world, host, 0f, 28f, 0);
            Assert.True(e.Hp < Barbarian.Hp);

            var (world2, host2) = Street();
            var e2 = Still(world2, 30f - reach - Barbarian.Radius - SimWorld.MeleeHitSlack - 0.2f, 28f);
            Walk(world2, e2, (e2.X, e2.Z));
            Attack(world2, host2, 0f, 28f, 0);
            Assert.Equal(Barbarian.Hp, e2.Hp);
        }

        [Fact]
        public void AMannedTurret_HitsTheRaiderWhereTheGunnersClientDrewIt()
        {
            const int Tx = 22, Tz = 15;
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, 3);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.True(world.Grant(Db.ShopItem("bp_turret"), host.Id));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 6)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 2)));
            SimDriver.Teleport(host, BuildRules.TileCentre(Tx), BuildRules.TileCentre(Tz) + 2.6f);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Db.Building("turret").Index, Tx, Tz, 0)));
            var turret = world.Constructs.At(Tx, Tz)!;
            SimDriver.Teleport(host, turret.CentreX + 2f, turret.CentreZ);
            SimDriver.StepTicks(world, Player.HistoryLength);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.OperateTurret(0, Tx, Tz, true)));

            // A raider 10 m east of the gun crossing its line northwards, 0.6 m a tick: on it five ticks ago, 3 m off it now.
            var e = Still(world, turret.CentreX + 10f, turret.CentreZ);
            var at = Walk(world, e, Enumerable.Range(0, 6).Select(k => (turret.CentreX + 10f, turret.CentreZ - 0.6f * k)).ToArray());
            float east = -MathF.PI / 2f;
            world.Events.Clear();
            world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(east), Buttons = InputButtons.Attack, View = PlayerInput.ViewOf(at[0]) });
            world.Step();
            var shot = world.Events.OfType<TurretFiredEvent>().Single();
            Assert.True(e.Hp < Barbarian.Hp);
            Assert.InRange(shot.TargetZ, turret.CentreZ - 0.5f, turret.CentreZ + 0.5f); // drawn where the gunner saw it
        }
    }
}
