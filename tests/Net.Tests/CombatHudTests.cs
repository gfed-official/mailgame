using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The fight as a client is told of it over the loopback transport: health, the raid's lines, the kill feed, the loot cards.</summary>
    public class CombatHudTests
    {
        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly ClientRuntime Client;
            public readonly EventFeed Feed = new EventFeed();
            public readonly LootCards Cards = new LootCards();

            public Session(uint seed = 3)
            {
                Server = new ServerRuntime(Shared.Content, new SimSettings { Seed = seed, Draft = false }, Hub);
                Client = new ClientRuntime(Shared.Content, Hub.CreateClient(), "Ada");
                Client.EventReceived += e =>
                {
                    Feed.OnEvent(Client.World!, e);
                    Cards.OnEvent(Client.World!, e);
                };
                Step(10);
            }

            public SimWorld Sim => Server.World;
            public ClientWorld World => Client.World!;

            public void Step(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    Client.Tick();
                    Server.Tick();
                }
            }

            public bool StepUntil(System.Func<bool> done, int maxSeconds)
            {
                for (int i = 0; i < maxSeconds * 30 && !done(); i++) Step(1);
                return done();
            }

            public IEnumerable<string> FeedLines => Feed.Entries.Select(e => e.Text);
        }

        [Fact]
        public void HudTextSaysMoneyCountsAndNames()
        {
            var content = Shared.Content;
            Assert.Equal("Raider", HudText.EnemyName(content, content.Enemy("barbarian").Index, false));
            Assert.Equal("Mega Horned Giant", HudText.EnemyName(content, content.Enemy("giant").Index, true));
            Assert.Equal("Something", HudText.EnemyName(content, 200, false));
            Assert.Equal("100", HudText.Health(100f));
            Assert.Equal("1", HudText.Health(0.2f));
            Assert.Equal("0", HudText.Health(-3f));
            Assert.Equal("x12", HudText.Count(12));
            Assert.Equal("[LMB] hammer   ·   [RMB] mail cannon x3", HudText.Weapons(3));
            Assert.Contains("no Small Package", HudText.Weapons(0));
            var g = new GlobalState { Backlog = 3, Complaint = 1, Deliveries = 4, Misdeliveries = 2 };
            Assert.Equal("Backlog 3   ·   Complaint 1", HudText.Status(g));
            Assert.Equal("Delivered 4   ·   Misdelivered 2", HudText.Tally(g));
        }

        [Fact]
        public void AFallenGiantIsInTheFeed_AndItsLootOnTheCards()
        {
            var s = new Session();
            var player = s.Sim.Players[0];
            player.Move.X = 50f;
            player.Move.Z = 28f;
            s.Step(6);
            Assert.Equal("100", HudText.Health(s.World.LocalHp));
            Assert.Equal("", HudText.Down(s.World));
            Assert.False(s.Cards.Visible);
            Assert.Equal(-1, s.Cards.Best);

            var giant = s.Sim.SpawnEnemy(Shared.Content.Enemy("giant"), 62f, 28f)!;
            s.Sim.DamageEnemy(giant, 9999f, player);
            s.Step(6);
            Assert.Contains("You felled a Horned Giant", s.FeedLines);
            Assert.Single(s.World.Loot);
            Assert.False(s.Cards.Visible);

            // Walking up takes it: the coins and both kinds of drop, under the giant's name.
            var bundle = s.Sim.Loot.Single();
            int coins = bundle.Coins;
            player.Move.X = bundle.X - 1f;
            player.Move.Z = bundle.Z;
            Assert.True(s.StepUntil(() => s.Cards.Visible, 3));
            Assert.Equal("Horned Giant", s.Cards.Title);
            var stone = Shared.Content.Item("stone");
            var ore = Shared.Content.Item("iron_ore");
            Assert.True(s.Cards.Cards[0].Coins);
            Assert.Equal(new[] { (LootCards.CoinsName, 0, coins), (ore.Name, 1, ore.SellPrice), (stone.Name, 3, stone.SellPrice * 3) },
                s.Cards.Cards.Select(c => (c.Name, c.Coins ? 0 : c.Count, c.Value)).OrderBy(c => c.Name));
            int best = s.Cards.Best;
            Assert.All(s.Cards.Cards, c => Assert.True(c.Value <= s.Cards.Cards[best].Value));
            // The coins are in the wallet a snapshot later.
            Assert.True(s.StepUntil(() => s.World.Global.Wallet == coins, 2));
            Assert.Equal(HudText.Wallet(s.Sim.GetGlobalState()), HudText.Wallet(s.World.Global));

            // A second kill straight after adds to the cards on show rather than replacing them.
            var raider = s.Sim.SpawnEnemy(Shared.Content.Enemy("barbarian"), bundle.X + 1f, bundle.Z)!;
            s.Sim.DamageEnemy(raider, 9999f, player);
            Assert.True(s.StepUntil(() => s.Sim.Loot.Count == 0 && s.World.Loot.Count == 0 && s.World.Global.Wallet > coins, 3));
            Assert.Equal("Raider", s.Cards.Title);
            Assert.True(s.Cards.Cards[0].Coins);
            Assert.Equal(s.World.Global.Wallet, s.Cards.Cards[0].Value);
            Assert.True(s.Cards.Cards[0].Value > coins);

            // And a few seconds after the last pickup they are gone.
            int revision = s.Cards.Revision;
            s.Cards.Update(LootCards.ShownSeconds - 0.1);
            Assert.True(s.Cards.Visible);
            s.Cards.Update(0.2);
            Assert.False(s.Cards.Visible);
            Assert.Equal(LootCards.DefaultTitle, s.Cards.Title);
            Assert.True(s.Cards.Revision > revision);
        }

        [Fact]
        public void ThreeKillsInARowAreOneFeedLine()
        {
            var s = new Session();
            var player = s.Sim.Players[0];
            for (int i = 0; i < 3; i++)
            {
                var swarmling = s.Sim.SpawnEnemy(Shared.Content.Enemy("swarmling"), 60f + i, 40f)!;
                s.Sim.DamageEnemy(swarmling, 9999f, player);
            }
            s.Step(6);
            Assert.Contains("You felled 3 Swarmlings", s.FeedLines);
            Assert.DoesNotContain("You felled a Swarmling", s.FeedLines);
        }

        [Fact]
        public void BeingKnockedDownShowsTheCountdown_AndStandingUpClearsIt()
        {
            var s = new Session();
            var player = s.Sim.Players[0];
            byte raider = (byte)Shared.Content.Enemy("barbarian").Index;
            s.Sim.DamagePlayer(player, 40f, raider);
            s.Step(6);
            Assert.Equal("60", HudText.Health(s.World.LocalHp));
            Assert.True(s.World.LocalHurtTick > 0);

            s.Sim.DamagePlayer(player, 999f, raider);
            s.Step(6);
            Assert.True(s.World.Local.Dead);
            Assert.Equal("0", HudText.Health(s.World.LocalHp));
            Assert.StartsWith("DOWN   ·   back on your feet in ", HudText.Down(s.World));
            Assert.Contains("You went down to a Raider", s.FeedLines);

            Assert.True(s.StepUntil(() => !s.World.Local.Dead, 15));
            Assert.Equal("", HudText.Down(s.World));
            Assert.Contains("You are back on your feet", s.FeedLines);
            Assert.Equal("100", HudText.Health(s.World.LocalHp));

            // Nit 17: back up means five seconds nobody can hurt them, and the HUD says so and counts them down.
            Assert.True(s.World.LocalProtected);
            Assert.Matches("^PROTECTED   ·   [45] s, or until you hit a raider$", HudText.Protected(s.World));
            s.Sim.DamagePlayer(player, 40f, raider);
            s.Step(6);
            Assert.Equal("100", HudText.Health(s.World.LocalHp));
            Assert.True(s.StepUntil(() => !s.World.LocalProtected, 6));
            Assert.Equal("", HudText.Protected(s.World));
            s.Sim.DamagePlayer(player, 40f, raider);
            s.Step(6);
            Assert.Equal("60", HudText.Health(s.World.LocalHp));
        }

        [Fact]
        public void WithRaidersAbout_TheYardKeepsWhoStoodUpInIt_AndTheBannerSaysUntilWhen()
        {
            var s = new Session();
            var player = s.Sim.Players[0];
            var po = s.Sim.Map.PostOffice;
            var raider = s.Sim.SpawnEnemy(Shared.Content.Enemy("barbarian"), po.SpawnPadX + 3f, po.SpawnPadZ)!;
            s.Sim.DamagePlayer(player, 999f, raider.Def.Index);
            Assert.True(s.StepUntil(() => s.World.Local.Dead, 1));
            Assert.True(s.StepUntil(() => !s.World.Local.Dead, 15));
            Assert.Matches("^PROTECTED   ·   [45] s, or until you hit a raider$", HudText.Protected(s.World));

            // The seconds run out with the raider still there: the banner stays, and so does their health.
            s.Step(7 * 30);
            Assert.True(s.World.LocalProtected);
            Assert.Equal("PROTECTED in the yard   ·   until you leave it or hit a raider", HudText.Protected(s.World));
            s.Sim.DamagePlayer(player, 40f, raider.Def.Index);
            s.Step(6);
            Assert.Equal("100", HudText.Health(s.World.LocalHp));

            // Their own blow on it ends that, and the next one of the raider's counts.
            s.Sim.DamageEnemy(raider, 10f, player);
            s.Step(6);
            Assert.False(s.World.LocalProtected);
            Assert.Equal("", HudText.Protected(s.World));
            s.Sim.DamagePlayer(player, 40f, raider.Def.Index);
            s.Step(6);
            Assert.Equal("60", HudText.Health(s.World.LocalHp));
        }

        [Fact]
        public void TheRaidOfTheSecondShiftIsAnnounced_CountedDown_AndCounted()
        {
            var s = new Session();
            var sim = s.Sim;
            var player = sim.Players[0];
            Assert.Equal("", HudText.RaidBanner(s.World));
            Assert.Equal("", HudText.RaidTracker(s.World));

            // Shift 1 on quota with one very valuable letter, then on to the Delivery of shift 2.
            Assert.NotEqual(0u, s.Client.PressGo());
            Assert.True(s.StepUntil(() => s.World.Global.Phase == RunPhase.Prep, 2));
            Assert.NotEqual(0u, s.Client.PressGo());
            Assert.True(s.StepUntil(() => s.World.Global.Phase == RunPhase.Delivery, 2));
            var home = s.World.Map.Destinations[0];
            var kind = Shared.Content.MailKind("letter");
            var letter = new Sim.Mail.MailItem { Id = 900100, Kind = kind.Index, Address = home.Address, Value = (ushort)sim.Run.Quota, SpawnShift = 1, DeadlineShift = 1 };
            Assert.True(sim.Give(player.Hotbar, ItemStack.OfMail(letter)));
            player.Move.X = home.MailboxX + 0.5f;
            player.Move.Z = home.MailboxZ;
            s.Step(20);
            Assert.NotEqual(0u, s.Client.Deliver(s.World.HotbarId, s.World.Hotbar.Entries.Single().EntryId, home.Id));
            Assert.True(s.StepUntil(() => s.World.Global.Phase == RunPhase.Payday, 245));
            Assert.True(s.StepUntil(() => s.World.Global.Phase == RunPhase.Prep, 25));
            Assert.NotEqual(0u, s.Client.PressGo());
            Assert.True(s.StepUntil(() => s.World.Global.Phase == RunPhase.Delivery && s.World.Global.Shift == 2, 2));

            Assert.True(s.StepUntil(() => s.World.Raid == RaidState.Warning, 275));
            Assert.StartsWith(HudText.RaidWarning + "   ·   00:", HudText.RaidBanner(s.World));
            Assert.StartsWith("Raid in 00:", HudText.RaidTracker(s.World));
            Assert.Contains(HudText.RaidWarning, s.FeedLines);
            Assert.InRange(s.World.RaidSecondsLeft, 1.0, 60.0);

            Assert.True(s.StepUntil(() => s.World.Raid == RaidState.Active && s.World.Enemies.Count > 0, 60));
            Assert.Equal("", HudText.RaidBanner(s.World));
            Assert.Equal($"Raid   ·   {HudText.CountAndKind("raider", s.World.Enemies.Count)} left", HudText.RaidTracker(s.World));

            Assert.True(s.StepUntil(() => s.World.Raid == RaidState.Fleeing, 120));
            Assert.Contains("running", HudText.RaidBanner(s.World));
            Assert.True(s.StepUntil(() => s.World.Raid == RaidState.None, 30));
            Assert.Contains("The raid is over", s.FeedLines);
            Assert.Equal("", HudText.RaidTracker(s.World));
        }
    }
}
