using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 12 M3 acceptance: "No friendly fire; no enemy damage to mailboxes or houses (assertion tests)".
    /// </summary>
    public class M3AssertionTests
    {
        private const int Tps = SimConstants.TickRate;
        private const float East = -MathF.PI / 2f;
        private uint _nextRequest = 1;
        private uint _nextMailId = 900000;

        private static ContentDb Db => TestContent.Db;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static void Press(SimWorld world, Player p, InputButtons buttons, byte held = 0)
        {
            world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(East), Buttons = buttons, HeldSlot = held });
            world.Step();
        }

        [Fact]
        public void NoFriendlyFire_TheHammerTheCannonAndTheSlingshotHurtNoCarrier()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false, StartingKits = false });
            var shooter = world.AddPlayer("shooter")!;
            var friend = world.AddPlayer("friend")!;
            var beside = world.AddPlayer("beside")!;
            SimDriver.Teleport(shooter, 50f, 28f);
            // In the hammer's arc, a step ahead; and right beside the raider the package goes off at.
            SimDriver.Teleport(friend, 51.2f, 28f);
            SimDriver.Teleport(beside, 57.6f, 28.6f);
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), 58f, 28f)!;
            raider.Speed = raider.BaseSpeed = 0f;
            SimDriver.StepTicks(world, 2);
            float full = friend.MaxHp;

            // The hammer: a swing with a carrier in its arc and no raider in reach.
            Press(world, shooter, InputButtons.Attack);
            var swing = world.Events.OfType<AttackEvent>().Single();
            Assert.Equal((Weapon.Hammer, (byte)0), (swing.Weapon, swing.Hits));

            // The cannon: over the friend's head, off beside the raider and the carrier there.
            var home = world.Map.Destinations[0];
            Assert.True(world.Give(shooter.Hotbar, ItemStack.OfMail(TestContent.Mail("small", home.Address, _nextMailId++))));
            for (int i = 0; i < 2 * Tps && !world.Events.OfType<BlastEvent>().Any(); i++) Press(world, shooter, i == 0 ? InputButtons.AltAttack : InputButtons.None);
            Assert.True(raider.Hp < raider.MaxHp, "the package did not go off at the raider");

            // The Slingshot: the friend stands on the Stone's line before the raider.
            var sling = Db.Item("slingshot");
            Assert.True(world.Give(shooter.Hotbar, ItemStack.OfItem(sling, 1)));
            Assert.True(world.Give(shooter.Inventory, ItemStack.OfItem(Db.Item("stone"), 1)));
            byte slot = (byte)shooter.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == sling.Index).X;
            float before = raider.Hp;
            Press(world, shooter, InputButtons.Attack, slot);
            Assert.Equal(before - sling.ShotDamage, raider.Hp);

            foreach (var p in new[] { friend, beside, shooter }) Assert.Equal(full, p.Hp);
        }

        [Fact]
        public void NoEnemyDamagesAMailboxOrAHouse_ThroughAWholeRaidFoughtAmongThem()
        {
            // Shift 3's raid (shift 4 and 5 lead with a Tank, which fells the Post Office of a team that builds
            // nothing, design choice 141), fought by two carriers standing at mailboxes on the street.
            var world = new SimWorld(Db, new SimSettings { Seed = 21, Draft = false, StartingKits = false });
            var team = new[] { world.AddPlayer("a")!, world.AddPlayer("b")! };
            foreach (var p in team) world.SetPlaytest(p, PlaytestFlags.NoTimer | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow, 3);
            Assert.Equal(OpResult.Ok, Request(world, team[0], SimRequest.StartRun(0)));
            foreach (var p in team) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            var map = world.Map;
            var tiles = map.Tiles.ToArray();
            var dests = map.Destinations.Select(d => (d.Id, d.MailboxX, d.MailboxZ, d.Address)).ToArray();
            for (int i = 0; i < team.Length; i++)
            {
                var at = map.Destinations[i * 3 % map.Destinations.Count];
                SimDriver.Teleport(team[i], at.MailboxX + 0.8f, at.MailboxZ);
                team[i].MaxHp = team[i].Hp = 1e9f;
            }
            var kinds = new HashSet<string>();
            int hits = 0;
            for (int t = 0; t < 120 * Tps; t++)
            {
                world.Step();
                foreach (var e in world.Enemies) kinds.Add(e.Def.Id);
                hits += world.Events.OfType<ConstructDamagedEvent>().Count();
            }
            Assert.True(world.EnemiesSpawnedTotal >= 20, $"only {world.EnemiesSpawnedTotal} raiders came");
            Assert.True(kinds.Count >= 4, $"only {string.Join(", ", kinds)} came");
            // Nothing was built: no blow fell on anything of the carriers', and the houses are as they were.
            Assert.Equal(0, hits);
            Assert.Equal(tiles, map.Tiles);
            Assert.Equal(dests, map.Destinations.Select(d => (d.Id, d.MailboxX, d.MailboxZ, d.Address)).ToArray());
            Assert.True(world.PostOfficeHp > 0f);

            // And every mailbox still takes its mail.
            var carrier = team[0];
            // The raid's loot has flown into its pockets: emptied, to carry the mail.
            foreach (var e in carrier.Hotbar.Entries.ToList()) carrier.Hotbar.Remove(e.EntryId);
            // What a hand carries (a dock's cargo goes by vehicle).
            var carried = Db.MailKinds.Where(k => k.Id != "cargo").ToList();
            foreach (var dest in map.Destinations.Where(d => carried.Any(k => DeliveryRules.KindAccepted(k, d.Type))).Take(12))
            {
                var kind = carried.First(k => DeliveryRules.KindAccepted(k, dest.Type));
                Assert.True(world.Give(carrier.Hotbar, ItemStack.OfMail(TestContent.Mail(kind.Id, dest.Address, _nextMailId++))));
                SimDriver.Teleport(carrier, dest.MailboxX, dest.MailboxZ);
                SimDriver.StepTicks(world, 12);
                var entry = carrier.Hotbar.Entries.First(e => e.Stack.Kind == StackKind.Mail);
                Assert.Equal(OpResult.Ok, Request(world, carrier, SimRequest.Deliver(0, carrier.Hotbar.Id, entry.EntryId, dest.Id)));
            }
        }
    }
}
