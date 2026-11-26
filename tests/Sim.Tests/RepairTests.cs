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
    /// The Repair Hammer (doc 05 §4 "Repairs 50 HP/s to a construct; consumes materials at 25% of build
    /// cost per full HP bar"; §5 "the PO … can be repaired with the hammer"; design choice 139).
    /// </summary>
    public class RepairTests
    {
        private const int Tps = SimConstants.TickRate;
        private const int Tx = 22, Tz = 15;
        private static ContentDb Db => TestContent.Db;
        private static ItemDef Hammer => Db.Item("repair_hammer");
        private static ItemDef Log => Db.Item("log");
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InPrep()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetPlaytest(host, PlaytestFlags.None, 1);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            return (world, host);
        }

        /// <summary>The hammer in the hotbar, held.</summary>
        private static void HoldHammer(SimWorld world, Player p)
        {
            Assert.True(world.Give(p.Hotbar, ItemStack.OfItem(Hammer, 1)));
            p.HeldSlot = (byte)p.Hotbar.Entries.Single(x => x.Stack.Kind == StackKind.Item && x.Stack.DefIndex == Hammer.Index).X;
            Assert.True(p.HeldSlot > 0);
        }

        private static int Logs(Player p) => BuildRules.Have(p.Hotbar, p.Inventory, Log);

        /// <summary><paramref name="ticks"/> of attack held (or not), facing (<paramref name="x"/>, <paramref name="z"/>); the events of them.</summary>
        private static System.Collections.Generic.List<SimEvent> Hold(SimWorld world, Player p, float x, float z, int ticks, bool attack = true)
        {
            var seen = new System.Collections.Generic.List<SimEvent>();
            float yaw = MathF.Atan2(-(x - p.Move.X), -(z - p.Move.Z));
            for (int i = 0; i < ticks; i++)
            {
                world.Events.Clear();
                world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(yaw), Buttons = attack ? InputButtons.Attack : InputButtons.None, HeldSlot = p.HeldSlot });
                world.Step();
                seen.AddRange(world.Events);
            }
            return seen;
        }

        /// <summary>A Wooden Wall (300 HP, 3 Logs) down to <paramref name="hp"/>, the host 1.8 m east of it.</summary>
        private static Construct HurtWall(SimWorld world, Player host, int hp)
        {
            var wall = world.PutUp(Db.Building("wall_wood"), Tx, Tz);
            world.DamageConstruct(wall, wall.Def.Hp - hp);
            Assert.Equal(hp, wall.Hp);
            SimDriver.Teleport(host, wall.CentreX + 1.8f, wall.CentreZ);
            return wall;
        }

        [Fact]
        public void TheContent_FiftyASecond_AQuarterOfTheCostABar_InTheShopFromShiftTwo()
        {
            Assert.Equal(("tool", 50f, 0.25f, 1), (Hammer.Category, Hammer.RepairPerSecond, Hammer.RepairCostRatio, Hammer.MaxStack));
            Assert.True(Hammer.Repairs && Hammer.Harvests.Count == 0);
            Assert.All(Db.Items.Where(i => i != Hammer), i => Assert.False(i.Repairs));
            var sold = Db.ShopItem("repair_hammer");
            Assert.Equal((100, "repair_hammer", 2), (sold.Price, sold.Item!.Id, sold.FromShift));
            Assert.Equal(30f, Db.Balance.GetFloat("postOfficeRepairLogs"));
        }

        [Fact]
        public void HeldAtAHurtWall_ItMendsFiftyASecond_ForAQuarterOfItsLogsAFullBar()
        {
            var (world, host) = InPrep();
            HoldHammer(world, host);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Log, 2)));
            var wall = HurtWall(world, host, 100);

            // Two seconds: 100 HP; a full bar of 300 is 0.75 Logs, so 100 is a quarter of a Log — one Log is taken for it.
            var seen = Hold(world, host, wall.CentreX, wall.CentreZ, 2 * Tps);
            Assert.InRange(wall.Hp, 190, 200);
            Assert.Equal(1, Logs(host));
            Assert.True(seen.OfType<ConstructDamagedEvent>().Count() >= 9);
            Assert.All(seen.OfType<ConstructDamagedEvent>(), e => Assert.Equal(((byte)Tx, (byte)Tz), (e.X, e.Y)));
            // Others see the hammer go at it; no raider was there to hit.
            Assert.Contains(seen.OfType<AttackEvent>(), a => a.Player == host.Id && a.Hits == 0);
            Assert.Empty(seen.OfType<RepairedEvent>());

            // Two more and it is whole, the Log already paid for still covering it; it says so once.
            seen = Hold(world, host, wall.CentreX, wall.CentreZ, 3 * Tps);
            Assert.Equal(wall.Def.Hp, wall.Hp);
            Assert.Equal(1, Logs(host));
            var whole = seen.OfType<RepairedEvent>().Single();
            Assert.Equal(((byte)Tx, (byte)Tz, host.Id, false), (whole.X, whole.Y, whole.Player, whole.PostOffice));
            Assert.Equal(200, world.HpRepaired);
        }

        [Fact]
        public void WithNoLogInThePockets_NothingIsMended_AndNoBlowIsStruck()
        {
            var (world, host) = InPrep();
            HoldHammer(world, host);
            var wall = HurtWall(world, host, 100);
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), wall.CentreX + 1.8f, wall.CentreZ - 1.2f)!;
            float raiderHp = raider.Hp;
            Hold(world, host, wall.CentreX, wall.CentreZ, Tps);
            Assert.Equal(100, wall.Hp);
            Assert.Equal(raiderHp, raider.Hp, 3);
        }

        [Fact]
        public void WithoutTheHammer_OrWithNothingHurtBeforeIt_ItSwings()
        {
            var (world, host) = InPrep();
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Log, 3)));
            var wall = HurtWall(world, host, 100);
            // Bare hands: the hammer of the fight swings, nothing is mended.
            var seen = Hold(world, host, wall.CentreX, wall.CentreZ, Tps);
            Assert.Equal(100, wall.Hp);
            Assert.Contains(seen.OfType<AttackEvent>(), a => a.Player == host.Id);

            // The Repair Hammer turned away from the wall, at a raider: it strikes the raider.
            HoldHammer(world, host);
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), host.Move.X + 1.2f, host.Move.Z)!;
            float before = raider.Hp;
            Hold(world, host, raider.X, raider.Z, Tps);
            Assert.True(raider.Hp < before);
            Assert.Equal(100, wall.Hp);
        }

        [Fact]
        public void NotInPayday_NorBeyondItsReach()
        {
            var (world, host) = InPrep();
            HoldHammer(world, host);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Log, 3)));
            var wall = HurtWall(world, host, 100);
            SimDriver.Teleport(host, wall.CentreX + 4.5f, wall.CentreZ);
            Hold(world, host, wall.CentreX, wall.CentreZ, Tps);
            Assert.Equal(100, wall.Hp);
            Assert.False(RepairRules.IsOpen(RunPhase.Payday));
            Assert.False(RepairRules.IsOpen(RunPhase.Lobby));
        }

        [Fact]
        public void ThePostOffice_IsMendedFromBesideItsWall_ForLogs()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            HoldHammer(world, host);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Log, 1)));
            world.DamagePostOffice(500f);
            var po = world.Map.PostOffice;
            // Open ground a metre off its wall.
            float x = 0f, z = 0f;
            bool found = false;
            const float tile = SimConstants.TileSize;
            for (float gz = po.Rect.Y * tile - 3f; gz < (po.Rect.Y + po.Rect.H) * tile + 3f && !found; gz += 0.25f)
                for (float gx = po.Rect.X * tile - 3f; gx < (po.Rect.X + po.Rect.W) * tile + 3f && !found; gx += 0.25f)
                {
                    float off = world.PostOfficeDistance(gx, gz, out _, out _);
                    if (off < 0.9f || off > 1.1f || world.Map.IsSolid((int)(gx / tile), (int)(gz / tile))) continue;
                    (x, z, found) = (gx, gz, true);
                }
            Assert.True(found);
            float d = world.PostOfficeDistance(x, z, out float wx, out float wz);
            Assert.InRange(d, 0.5f, 1.3f);
            SimDriver.Teleport(host, x, z);

            // Two seconds: 100 HP; 7.5 Logs a full bar of 3000 is a quarter of a Log — the one Log goes.
            Hold(world, host, wx, wz, 2 * Tps);
            Assert.InRange(world.PostOfficeHp, 2590f, 2600f);
            Assert.Equal(0, Logs(host));
            Assert.Equal((ushort)MathF.Ceiling(world.PostOfficeHp), world.GetGlobalState().PostOfficeHp);
            // Facing away from it, nothing.
            float was = world.PostOfficeHp;
            Hold(world, host, x + (x - wx) * 5f, z + (z - wz) * 5f, Tps);
            Assert.Equal(was, world.PostOfficeHp);
        }
    }
}
