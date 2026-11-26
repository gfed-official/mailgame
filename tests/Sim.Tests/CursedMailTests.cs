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
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Postage Stamps and Cursed Mail (doc 01 §6 "Cursed Mail | 1 | 10% of mail is Cursed: misdelivery
    /// spawns a mini-raid on the spot | 1.15"; doc 05 §6 "4 Barbarians + 1 Archer (30 budget), no
    /// warning. Correct delivery pays ×1.5"; doc 11 "Stamp XP bonus +10% per active stamp"; design choice 140).
    /// </summary>
    public class CursedMailTests
    {
        private const int Tps = SimConstants.TickRate;
        private static ContentDb Db => TestContent.Db;
        private static StampDef Cursed => Db.Stamp("cursed_mail");
        private uint _nextRequest = 1, _nextMailId = 900000;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InLobby(uint seed = 7)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = seed, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetPlaytest(host, PlaytestFlags.None, 1);
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            return (world, host);
        }

        private (SimWorld World, Player Host) InDelivery(bool cursed, uint seed = 7)
        {
            var (world, host) = InLobby(seed);
            if (cursed) Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetStamps(0, 1 << Cursed.Index)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            return (world, host);
        }

        private static List<MailItem> IntakeMail(SimWorld world) =>
            world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).SelectMany(e => e.Stack.Mail).ToList();

        [Fact]
        public void TheStamp_TierOne_ScoreTimesOnePointOneFive_TenPercent_HalfAgain_FourBarbariansAndAnArcher()
        {
            Assert.Equal(("Cursed Mail", 1, 1.15f, 0.10f, 1.5f), (Cursed.Name, Cursed.Tier, Cursed.ScoreMult, Cursed.CursedChance, Cursed.CursedValueMult));
            Assert.True(Cursed.Has(StampRule.CursedMail));
            Assert.Equal(new[] { ("barbarian", 4), ("archer", 1) }, Cursed.MiniRaid.Select(m => (m.Enemy.Id, m.Count)).ToArray());
            // Doc 05 §6 says "(30 budget)" by its own costs; this content's raiders cost more (design choice 140): the count is the doc's.
        }

        [Fact]
        public void OnlyTheHost_InTheLobby_SetsTheStamps_AndTheGlobalBlockCarriesThem()
        {
            var (world, host) = InLobby();
            var guest = world.AddPlayer("guest")!;
            Assert.Equal(OpResult.NotHost, Request(world, guest, SimRequest.SetStamps(0, 1)));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.SetStamps(0, 1 << Db.Stamps.Count)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetStamps(0, 1)));
            Assert.Equal((byte)1, world.GetGlobalState().Stamps);
            Assert.Same(Cursed, world.CursedStamp());
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.WrongPhase, Request(world, host, SimRequest.SetStamps(0, 0)));
            Assert.Equal((byte)1, world.Stamps);
        }

        [Fact]
        public void WithTheStamp_ATenthOfTheMailIsCursed_WorthHalfAgain_TheSameForTheSameSeed_NoneWithout()
        {
            List<MailItem> Spawned(bool cursed)
            {
                var (world, _) = InDelivery(cursed);
                for (int i = 0; i < 90 * Tps; i++) world.Step();
                return IntakeMail(world);
            }
            var plain = Spawned(false);
            Assert.True(plain.Count >= 30, $"{plain.Count} pieces in the Intake");
            Assert.DoesNotContain(plain, m => (m.Flags & MailFlags.Cursed) != 0);
            var withStamp = Spawned(true);
            var cursed = withStamp.Where(m => (m.Flags & MailFlags.Cursed) != 0).ToList();
            Assert.InRange(cursed.Count, 1, withStamp.Count / 4);
            // The same mail as without the stamp (what fits in the Intake differs: cursed does not stack with plain), the cursed worth half again.
            var byId = plain.ToDictionary(m => m.Id);
            var both = withStamp.Where(m => byId.ContainsKey(m.Id)).ToList();
            Assert.True(both.Count >= plain.Count / 2);
            Assert.Contains(both, m => (m.Flags & MailFlags.Cursed) != 0);
            foreach (var m in both)
                Assert.Equal((byId[m.Id].Kind, byId[m.Id].Address), (m.Kind, m.Address));
            foreach (var m in both)
                Assert.Equal((m.Flags & MailFlags.Cursed) != 0 ? (ushort)Math.Round(byId[m.Id].Value * 1.5) : byId[m.Id].Value, m.Value);
            Assert.Equal(cursed.Select(m => m.Id), Spawned(true).Where(m => (m.Flags & MailFlags.Cursed) != 0).Select(m => m.Id));
            // Cursed mail never stacks with mail that is not.
            var c = ItemStack.OfMail(cursed[0]);
            var twin = cursed[0];
            twin.Flags = 0;
            twin.Id++;
            Assert.True(c.Cursed);
            Assert.False(c.CanStackWith(ItemStack.OfMail(twin)));
        }

        private (DestinationDef Home, DestinationDef Other) TwoHouses(SimWorld world)
        {
            var letter = Db.MailKind("letter");
            var houses = world.Map.Destinations.Where(d => DeliveryRules.KindAccepted(letter, d.Type)).ToList();
            return (houses[0], houses.First(d => d.Address != houses[0].Address));
        }

        private EntryRef GiveCursedLetter(SimWorld world, Player p, DestinationDef home)
        {
            var item = TestContent.Mail("letter", home.Address, _nextMailId++, world.Run.Shift);
            item.Flags = MailFlags.Cursed;
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(item)));
            var entry = p.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            return new EntryRef(entry.EntryId);
        }

        private readonly record struct EntryRef(uint Id);

        [Fact]
        public void ACursedLetterAtTheWrongMailbox_BringsFourBarbariansAndAnArcherThere_Unannounced()
        {
            var (world, host) = InDelivery(cursed: true);
            var (home, other) = TwoHouses(world);
            var entry = GiveCursedLetter(world, host, home);
            SimDriver.Teleport(host, other.MailboxX, other.MailboxZ);
            SimDriver.StepTicks(world, 15);
            Assert.Empty(world.Enemies);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Deliver(0, host.Hotbar.Id, entry.Id, other.Id)));
            Assert.Single(world.Events.OfType<MisdeliveredEvent>());
            var raid = world.Events.OfType<CursedRaidEvent>().Single();
            Assert.Equal(((ushort)other.Id, host.Id, (byte)5), (raid.DestId, raid.Player, raid.Raiders));
            Assert.Equal(4, world.Enemies.Count(e => e.Def.Id == "barbarian"));
            Assert.Equal(1, world.Enemies.Count(e => e.Def.Id == "archer"));
            Assert.All(world.Enemies, e => Assert.InRange(MathF.Sqrt((e.X - other.MailboxX) * (e.X - other.MailboxX) + (e.Z - other.MailboxZ) * (e.Z - other.MailboxZ)), 0f, 3.2f));
            // No warning, no raid: they are just there.
            Assert.Equal(RaidState.None, world.Raid);
            Assert.Empty(world.Events.OfType<RaidChangedEvent>());
            Assert.Equal(1, world.MiniRaids);
        }

        [Fact]
        public void ACursedLetterHome_PaysItsRaisedValue_AndBringsNobody_NorDoesAPlainOneAtTheWrongBox()
        {
            var (world, host) = InDelivery(cursed: true);
            var (home, other) = TwoHouses(world);
            var entry = GiveCursedLetter(world, host, home);
            var value = host.Hotbar.Get(entry.Id)!.Stack.Mail[0].Value;
            SimDriver.Teleport(host, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Deliver(0, host.Hotbar.Id, entry.Id, home.Id)));
            Assert.True(world.Events.OfType<DeliveredEvent>().Single().Paid >= value);
            Assert.Empty(world.Enemies);

            var plain = TestContent.Mail("letter", home.Address, _nextMailId++, world.Run.Shift);
            Assert.True(world.Give(host.Hotbar, ItemStack.OfMail(plain)));
            var e2 = host.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            SimDriver.Teleport(host, other.MailboxX, other.MailboxZ);
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Deliver(0, host.Hotbar.Id, e2.EntryId, other.Id)));
            Assert.Single(world.Events.OfType<MisdeliveredEvent>());
            Assert.Empty(world.Enemies);
            Assert.Equal(0, world.MiniRaids);
        }

        [Fact]
        public void TheScore_IsTimesTheStampsMultiplier_AndTheXpTenPercentMore()
        {
            int Score(bool cursed, out int xp)
            {
                var (world, host) = InDelivery(cursed, seed: 11);
                var (home, _) = TwoHouses(world);
                var item = TestContent.Mail("letter", home.Address, _nextMailId++, world.Run.Shift);
                item.Value = 1000;
                Assert.True(world.Give(host.Hotbar, ItemStack.OfMail(item)));
                var entry = host.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
                SimDriver.Teleport(host, home.MailboxX, home.MailboxZ);
                SimDriver.StepTicks(world, 15);
                Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Deliver(0, host.Hotbar.Id, entry.EntryId, home.Id)));
                world.DamagePostOffice(100000f);
                var r = world.Run.Results!;
                xp = r.Players.Single().Xp;
                Assert.True(r.TotalEarned > 0);
                return r.Score * 1000 / r.TotalEarned;
            }
            Assert.Equal(1000, Score(false, out int plainXp));
            Assert.InRange(Score(true, out int stampedXp), 1149, 1151);
            Assert.Equal((int)Math.Round(plainXp * 1.1), stampedXp);
        }
    }
}
