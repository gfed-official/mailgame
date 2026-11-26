using System;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// A Mega's drops and its announcement (doc 05 §2.1 "Guaranteed drop on death: a Lost Parcel
    /// (Medium Package with a random unlocked address, value ×3) plus 2 Iron Ingots. Announced on the
    /// HUD when spawned (\"Mega Giant sighted, north\")"; design choice 142).
    /// </summary>
    public class LostParcelTests
    {
        private const int Tps = SimConstants.TickRate;
        private static ContentDb Db => TestContent.Db;
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InDelivery(uint seed = 7)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = seed, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetPlaytest(host, PlaytestFlags.None, 1);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            return (world, host);
        }

        /// <summary>Felled where it stands, by the host; the events of that tick.</summary>
        private static Enemy Fell(SimWorld world, Player host, string kind, bool mega, float x = 40f, float z = 28f)
        {
            var e = world.SpawnEnemy(Db.Enemy(kind), x, z, mega)!;
            world.Events.Clear();
            world.DamageEnemy(e, 1e6f, host);
            return e;
        }

        [Fact]
        public void TheContent_AMediumPackageAtThreeTimesItsValue_AndTwoIronIngots()
        {
            var waves = Db.Waves;
            Assert.Equal("medium", waves.LostParcelKind!.Id);
            Assert.Equal(3.0, waves.LostParcelValueMult);
            Assert.Equal(new[] { ("iron_ingot", 2) }, waves.MegaItems.Select(d => (d.Item.Id, d.Count)).ToArray());
        }

        [Fact]
        public void AFelledMega_LeavesALostParcelInABag_ForAnUnlockedAddressThatTakesIt_AtThreeTimesItsValue()
        {
            var (world, host) = InDelivery();
            var e = Fell(world, host, "giant", mega: true);
            var ev = world.Events.OfType<LostParcelEvent>().Single();
            var bag = world.Bags.Single(b => b.Id == ev.Bag);
            Assert.Equal(EntityId.None, bag.Owner);
            Assert.InRange(MathF.Abs(bag.X - e.X) + MathF.Abs(bag.Z - e.Z), 0f, 0.01f);
            var stack = bag.Container.Entries.Single().Stack;
            Assert.True(stack.Lost);
            Assert.False(stack.Cursed);
            var parcel = stack.Mail.Single();
            var medium = Db.MailKind("medium");
            Assert.Equal(medium.Index, parcel.Kind);
            Assert.Equal((e.Def.Index, parcel.Kind, parcel.Address, parcel.Value), (ev.Enemy, ev.Kind, ev.Address, ev.Value));
            var dest = world.Map.Destinations.Single(d => d.Address == parcel.Address);
            Assert.True(DeliveryRules.KindAccepted(medium, dest.Type));
            Assert.True(world.Map.OpensAt(dest) <= world.Run.Shift);
            var mix = Db.MailMix;
            double plain = medium.BaseValue * (1 + mix.DistanceMultiplierPerDistrict * (dest.Address.District - 1)) * (1 + mix.ShiftMultiplierPerShift * (world.Run.Shift - 1));
            Assert.Equal((ushort)Math.Round(plain * 3), parcel.Value);
        }

        [Fact]
        public void AMegasLoot_HasTwoIronIngotsBesidesItsKindsDropsTimesThree_AndANormalOneNeither()
        {
            var (world, host) = InDelivery();
            Fell(world, host, "tank", mega: true);
            var ingot = Db.Item("iron_ingot");
            var loot = world.Loot.Single();
            Assert.Equal(4 * 3 + 2, loot.Items.Single(i => i.Item == ingot.Index).Count);

            var (plainWorld, plainHost) = InDelivery();
            Fell(plainWorld, plainHost, "giant", mega: false);
            Assert.Empty(plainWorld.Events.OfType<LostParcelEvent>());
            Assert.Empty(plainWorld.Bags);
            Assert.DoesNotContain(plainWorld.Loot.Single().Items, i => i.Item == ingot.Index);
        }

        [Fact]
        public void TheLostParcel_TakenFromTheBag_IsDeliveredHome_ForItsValue()
        {
            var (world, host) = InDelivery();
            var e = Fell(world, host, "giant", mega: true, x: 40f, z: 28f);
            var bag = world.Bags.Single();
            SimDriver.Teleport(host, bag.X + 1f, bag.Z);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Open(0, bag.Id)));
            uint entry = bag.Container.Entries.Single().EntryId;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.QuickMove(0, bag.Id, entry)));
            var held = host.Hotbar.Entries.Concat(host.Inventory.Entries).Single(x => x.Stack.Kind == StackKind.Mail);
            var parcel = held.Stack.Mail.Single();
            var dest = world.Map.Destinations.Single(d => d.Address == parcel.Address);
            SimDriver.Teleport(host, dest.MailboxX, dest.MailboxZ);
            SimDriver.StepTicks(world, 15);
            uint from = host.Hotbar.Get(held.EntryId) != null ? host.Hotbar.Id : host.Inventory.Id;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Deliver(0, from, held.EntryId, dest.Id)));
            Assert.True(world.Events.OfType<DeliveredEvent>().Single().Paid >= parcel.Value);
        }

        [Fact]
        public void LeftLying_ItIsBackInTheIntake_WhenTheBagsTimeIsUp()
        {
            var (world, host) = InDelivery();
            Fell(world, host, "giant", mega: true);
            uint id = world.Bags.Single().Container.Entries.Single().Stack.Mail.Single().Id;
            SimDriver.StepTicks(world, (int)(Db.Balance.GetDouble("deathBagDespawnSeconds") * Tps) + 2);
            Assert.Empty(world.Bags);
            Assert.Contains(world.Intake.Entries, x => x.Stack.Kind == StackKind.Mail && x.Stack.Mail.Any(m => m.Id == id));
        }

        [Fact]
        public void ARaidsMega_IsAnnouncedWithItsWayIn()
        {
            // Doc 05 §3.2: the last pulse of the last shift always brings a Mega.
            var world = new SimWorld(Db, new SimSettings { Seed = 3, Draft = false });
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetPlaytest(host, PlaytestFlags.NoTimer | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow, world.ShiftCount);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Request(world, host, SimRequest.SetReady(0, true));
            int sighted = 0;
            for (int t = 0; t < 120 * Tps && world.RaidPulsesSpawned < Db.Waves.Pulses && world.Run.Phase == RunPhase.Delivery; t++)
            {
                world.Events.Clear();
                world.Step();
                foreach (var ev in world.Events.OfType<MegaSightedEvent>())
                {
                    sighted++;
                    var mega = world.GetEnemy(ev.Enemy)!;
                    Assert.True(mega.Mega);
                    Assert.Equal((mega.Def.Index, (sbyte)mega.Edge), (ev.Kind, ev.Edge));
                }
                foreach (var e in world.Enemies.Where(e => e.Mega)) Assert.True(sighted > 0);
            }
            Assert.True(sighted >= 1);
        }

        [Fact]
        public void AMegasAura_SpeedsItsKindWithinEightMetresByAFifth_NotOthersNorItself()
        {
            var (world, host) = InDelivery();
            SimDriver.Teleport(host, 10f, 28f);
            var mega = world.SpawnEnemy(Db.Enemy("barbarian"), 60f, 28f, mega: true)!;
            var near = world.SpawnEnemy(Db.Enemy("barbarian"), 60f, 34f)!;
            var far = world.SpawnEnemy(Db.Enemy("barbarian"), 75f, 28f)!;
            var other = world.SpawnEnemy(Db.Enemy("archer"), 62f, 28f)!;
            world.Step();
            Assert.Equal(8f, Db.Waves.MegaAuraRadius);
            Assert.True(near.InAura);
            Assert.Equal(near.BaseSpeed * 1.2f, near.Speed, 3);
            Assert.False(far.InAura);
            Assert.Equal(far.BaseSpeed, far.Speed);
            Assert.False(other.InAura);
            Assert.False(mega.InAura);
            Assert.Equal(Db.Enemy("barbarian").Speed * Db.Waves.MegaSpeedMult, mega.Speed, 3);
            // Felled, the aura is gone with it.
            world.DamageEnemy(mega, 1e6f, host);
            world.Step();
            Assert.False(near.InAura);
            Assert.Equal(near.BaseSpeed, near.Speed);
        }
    }
}
