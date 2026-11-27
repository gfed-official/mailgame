using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The Cursed Mail stamp over the loopback transport (doc 01 §6, doc 05 §6; design choice 140): the
    /// host's key sets it in the Lobby and everyone's plate says so, a guest's does nothing; cursed mail
    /// says so in the hand, and at the wrong mailbox the prompt warns and the feed says who came.
    /// </summary>
    public class CursedMailFlowTests
    {
        [Fact]
        public void TheHostSetsTheStamp_CursedMailSaysSo_AndTheWrongBoxBringsRaiders()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var ada = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 1);
            var feed = new EventFeed();
            ada.EventReceived += ev => feed.OnEvent(ada.World!, ev);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); server.Tick(); }
            }
            Run(10);
            var bob = new ClientRuntime(content, hub.CreateClient(), "Bob", PlaytestFlags.None, 1);
            void RunBoth(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); bob.Tick(); server.Tick(); }
            }
            RunBoth(10);
            var a = ada.World!;
            Assert.Equal("Stamps: none", HudText.StampsTracker(a));
            Assert.EndsWith("[T] stamps", HudText.WorldKeys);
            Assert.Equal(0u, bob.ToggleStamp());
            Assert.False(bob.NextStamp());
            Assert.Equal("[T] Cursed Mail ×1.15   ·   [Y] on", HudText.StampPick(a, ada.StampCursor, ada.StampTierOpen));
            Assert.NotEqual(0u, ada.ToggleStamp());
            RunBoth(10);
            Assert.Equal((byte)1, server.World.Stamps);
            Assert.Equal("[T] Cursed Mail ×1.15   ·   [Y] off", HudText.StampPick(a, ada.StampCursor, ada.StampTierOpen));
            // Doc 01 §5.2: at Postal Rank 1 a tier 2 stamp is locked, on the plate and on the server.
            while (content.Stamps[ada.StampCursor].Tier < 2) Assert.True(ada.NextStamp());
            Assert.EndsWith("tier 2 from Postal Rank 6", HudText.StampPick(a, ada.StampCursor, ada.StampTierOpen));
            Assert.Equal(0u, ada.ToggleStamp());
            while (ada.StampCursor != 0) Assert.True(ada.NextStamp());
            Assert.Equal("Stamps: Cursed Mail   ·   score ×1.15", HudText.StampsTracker(a));
            Assert.Equal("Stamps: Cursed Mail   ·   score ×1.15", HudText.StampsTracker(bob.World!));
            Assert.Equal("× 1.15 Cursed Mail", HudText.StampsMultLine(a));

            Assert.NotEqual(0u, ada.PressGo());
            RunBoth(10);
            Assert.NotEqual(0u, ada.PressGo());
            Assert.NotEqual(0u, bob.PressGo());
            RunBoth(10);
            Assert.Equal(RunPhase.Delivery, a.Global.Phase);

            var sim = server.World;
            var truth = sim.GetPlayer(a.LocalId)!;
            var letter = content.MailKind("letter");
            var houses = a.Map.Destinations.Where(d => DeliveryRules.KindAccepted(letter, d.Type)).ToList();
            var home = houses[0];
            var other = houses.First(d => d.Address != home.Address);
            var item = new MailItem { Id = 777777, Kind = letter.Index, Address = home.Address, Value = 30, SpawnShift = 1, DeadlineShift = 3, Flags = MailFlags.Cursed };
            Assert.True(sim.Give(truth.Hotbar, ItemStack.OfMail(item)));
            truth.Move.X = other.MailboxX;
            truth.Move.Z = other.MailboxZ;
            RunBoth(20);
            var held = a.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            Assert.True(held.Stack.Cursed);
            Assert.StartsWith("Cursed 1 Letter for ", HudText.Stack(a, held.Stack));
            var prompt = new InteractPrompt { Kind = InteractKind.Deliver, Destination = other, Stack = held, KindAccepted = true, AddressMatches = false };
            Assert.Contains(HudText.CursedWarning, HudText.Prompt(a, prompt, out var tone));
            Assert.Equal(PromptTone.Mismatch, tone);

            Assert.NotEqual(0u, ada.Deliver(a.HotbarId, held.EntryId, other.Id));
            RunBoth(10);
            Assert.Equal(5, a.Enemies.Count);
            Assert.Single(feed.Entries, e => e.Bad && e.Text == $"Your cursed Letter at {other.Label}: 5 raiders!");
            Assert.Equal(RaidState.None, a.Raid);
            Assert.Equal(0, ada.ContainerDesyncs + ada.BadPackets + bob.ContainerDesyncs + bob.BadPackets);
        }

        [Fact]
        public void TheLobbysStampsLine_NamesUpToTwoShortOnes_ThenCountsThem()
        {
            // The d0c338e playtest's N13: "Stamps: Rush Hour, Heavy Load, Double Raids" ran past its plate.
            var content = Shared.Content;
            int Bit(string id) => 1 << content.Stamps.Single(s => s.Id == id).Index;
            Assert.Equal("Stamps: none", HudText.StampsTracker(content, 0));
            Assert.Equal("Stamps: Rush Hour, Heavy Load   ·   score ×1.21", HudText.StampsTracker(content, Bit("rush_hour") | Bit("heavy_load")));
            Assert.Equal("Stamps: 3 on   ·   score ×1.51", HudText.StampsTracker(content, Bit("rush_hour") | Bit("heavy_load") | Bit("double_raids")));
            // Two long names count as well.
            Assert.Equal("Stamps: 2 on   ·   score ×1.56", HudText.StampsTracker(content, Bit("skeleton_crew") | Bit("postal_audit")));
            Assert.StartsWith("Stamps: 8 on   ·   score ×", HudText.StampsTracker(content, (1 << content.Stamps.Count) - 1));
        }

        [Fact]
        public void AnEnemysNameTakesItsArticle()
        {
            // The d0c338e playtest: "You went down to a Archer".
            Assert.Equal("an Archer", HudText.WithArticle("Archer"));
            Assert.Equal("a Raider", HudText.WithArticle("Raider"));
        }
    }
}
