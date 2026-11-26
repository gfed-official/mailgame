using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Slingshot (doc 05 §1.2 "12 | 0.8 s | 25 m | Shop 200 ¢; uses Stone as ammo (1 Stone = 5
    /// shots)"; "ranged uses server-side hitscan"; §2 Balloon "Only ranged weapons and turrets can hit
    /// it"; design choice 143).
    /// </summary>
    public class SlingshotTests
    {
        private const int Tps = SimConstants.TickRate;
        private static ContentDb Db => TestContent.Db;
        private static ItemDef Sling => Db.Item("slingshot");
        private static ItemDef Stone => Db.Item("stone");
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        /// <summary>In Delivery without raids, the host on the street at (30, 28) facing west (−X), the Slingshot held and <paramref name="stones"/> Stone in the inventory.</summary>
        private (SimWorld World, Player Host) Armed(int stones)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false, StartingKits = false });
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetPlaytest(host, PlaytestFlags.None, 1);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.True(world.Give(host.Hotbar, ItemStack.OfItem(Sling, 1)));
            host.HeldSlot = (byte)host.Hotbar.Entries.Single(x => x.Stack.Kind == StackKind.Item && x.Stack.DefIndex == Sling.Index).X;
            Assert.True(host.HeldSlot > 0);
            if (stones > 0) Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Stone, stones)));
            SimDriver.Teleport(host, 30f, 28f);
            return (world, host);
        }

        /// <summary><paramref name="ticks"/> of attack held facing (<paramref name="x"/>, <paramref name="z"/>); the events of them.</summary>
        private static List<SimEvent> Hold(SimWorld world, Player p, float x, float z, int ticks, InputButtons buttons = InputButtons.Attack)
        {
            var seen = new List<SimEvent>();
            float yaw = MathF.Atan2(-(x - p.Move.X), -(z - p.Move.Z));
            for (int i = 0; i < ticks; i++)
            {
                world.Events.Clear();
                world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(yaw), Buttons = buttons, HeldSlot = p.HeldSlot });
                world.Step();
                seen.AddRange(world.Events);
            }
            return seen;
        }

        private static int Stones(Player p) => SlingRules.Ammo(Stone, p.Hotbar, p.Inventory);

        [Fact]
        public void TheContent_TwelveEveryPointEightSeconds_TwentyFiveMetres_FiveShotsAStone_InTheShopForTwoHundred()
        {
            Assert.Equal(("tool", 12f, 0.8f, 25f, "stone", 5), (Sling.Category, Sling.ShotDamage, Sling.ShotCooldown, Sling.ShotRange, Sling.Ammo!.Id, Sling.ShotsPerAmmo));
            Assert.True(Sling.Shoots && !Sling.Repairs && Sling.Harvests.Count == 0);
            Assert.All(Db.Items.Where(i => i != Sling), i => Assert.False(i.Shoots));
            var sold = Db.ShopItem("slingshot");
            Assert.Equal((200, "slingshot", 2), (sold.Price, sold.Item!.Id, sold.FromShift));
        }

        [Fact]
        public void HeldWithAStone_ARaiderTenMetresAhead_TakesTwelve_AndTheStoneLoadsFiveShots()
        {
            var (world, host) = Armed(stones: 2);
            var barbarian = world.SpawnEnemy(Db.Enemy("barbarian"), 20f, 28f)!;
            var seen = Hold(world, host, 0f, 28f, 1);
            var shot = seen.OfType<SlingshotFiredEvent>().Single();
            Assert.Equal((host.Id, SlingHit.Ground, (byte)4), (shot.Player, shot.Hit, shot.Loaded));
            Assert.InRange(shot.ToX, 20f - barbarian.Radius - 0.5f, 20f + barbarian.Radius);
            Assert.Equal(barbarian.Def.Hp - 12f, barbarian.Hp);
            Assert.Equal(1, Stones(host));
            Assert.Empty(seen.OfType<AttackEvent>());
        }

        [Fact]
        public void HeldDown_ItLoosesEveryPointEightSeconds_AndAStoneGoesEveryFifthShot()
        {
            var (world, host) = Armed(stones: 2);
            var seen = Hold(world, host, 0f, 28f, (int)(0.8f * Tps) * 6 + 1);
            Assert.Equal(7, seen.OfType<SlingshotFiredEvent>().Count());
            Assert.Equal(0, Stones(host));
            Assert.Equal(new byte[] { 4, 3, 2, 1, 0, 4, 3 }, seen.OfType<SlingshotFiredEvent>().Select(e => e.Loaded).ToArray());
            // A shot at nothing falls at the reach.
            Assert.All(seen.OfType<SlingshotFiredEvent>(), e => Assert.Equal(25f, MathF.Abs(e.ToX - e.FromX), 2));
        }

        [Fact]
        public void WithoutAStone_ItDoesNothing_AndTheHammerDoesNotSwingInstead()
        {
            var (world, host) = Armed(stones: 0);
            world.SpawnEnemy(Db.Enemy("barbarian"), 28f, 28f);
            var seen = Hold(world, host, 0f, 28f, 30);
            Assert.Empty(seen.OfType<SlingshotFiredEvent>());
            Assert.Empty(seen.OfType<AttackEvent>());
        }

        [Fact]
        public void ItStrikesTheFirstOnItsLine_NotOneBeside_NorOneBeyondItsReach()
        {
            var (world, host) = Armed(stones: 1);
            var near = world.SpawnEnemy(Db.Enemy("barbarian"), 18f, 28f)!;
            var far = world.SpawnEnemy(Db.Enemy("barbarian"), 12f, 28f)!;
            var beside = world.SpawnEnemy(Db.Enemy("barbarian"), 24f, 30.5f)!;
            Hold(world, host, 0f, 28f, 1);
            Assert.Equal(near.Def.Hp - 12f, near.Hp);
            Assert.Equal(far.Def.Hp, far.Hp);
            Assert.Equal(beside.Def.Hp, beside.Hp);

            var (world2, host2) = Armed(stones: 1);
            var beyond = world2.SpawnEnemy(Db.Enemy("barbarian"), 30f - 25f - Db.Enemy("barbarian").Radius - 1f, 28f)!;
            var seen = Hold(world2, host2, 0f, 28f, 1);
            Assert.Equal(SlingHit.None, seen.OfType<SlingshotFiredEvent>().Single().Hit);
            Assert.Equal(beyond.Def.Hp, beyond.Hp);
        }

        [Fact]
        public void ItReachesABalloon_WhichTheHammerDoesNot()
        {
            var (world, host) = Armed(stones: 1);
            var balloon = world.SpawnEnemy(Db.Enemy("balloon"), 22f, 28f)!;
            var shot = Hold(world, host, 0f, 28f, 1).OfType<SlingshotFiredEvent>().Single();
            Assert.Equal(SlingHit.Air, shot.Hit);
            Assert.Equal(balloon.Def.Hp - 12f, balloon.Hp);
        }

        [Fact]
        public void TheCannonStillFires_OnTheOtherButton_WithTheSlingshotInTheHand()
        {
            var (world, host) = Armed(stones: 1);
            var small = TestContent.Mail("small", world.Map.Destinations[0].Address, 990001, 1);
            Assert.True(world.Give(host.Inventory, ItemStack.OfMail(small)));
            var seen = Hold(world, host, 0f, 28f, 1, InputButtons.AltAttack);
            Assert.Single(seen.OfType<AttackEvent>(), e => e.Weapon == Weapon.Cannon);
            Assert.Empty(seen.OfType<SlingshotFiredEvent>());
        }
    }
}
