using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// A Mega's announcement and its Lost Parcel over the loopback transport (doc 05 §2.1; design
    /// choice 142): the feed says where it came in and what it dropped, and the parcel in the hand says
    /// it is lost.
    /// </summary>
    public class LostParcelFlowTests
    {
        [Fact]
        public void TheFeedAnnouncesTheMega_AndItsLostParcel_WhichSaysSoInTheHand()
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
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            var a = ada.World!;
            Assert.Equal(RunPhase.Delivery, a.Global.Phase);

            var giant = content.Enemy("giant");
            var edge = a.Map.SpawnEdges[0];
            Assert.Equal($"Mega {giant.Name} sighted, {edge.Direction}", HudText.MegaSighted(a, new MegaSightedEvent { Kind = giant.Index, Edge = (sbyte)edge.Index }));

            var sim = server.World;
            var truth = sim.GetPlayer(a.LocalId)!;
            var mega = sim.SpawnEnemy(giant, truth.Move.X + 6f, truth.Move.Z, mega: true)!;
            Run(5);
            sim.DamageEnemy(mega, 1e6f, truth);
            Run(10);
            var bag = sim.Bags.Single();
            var parcel = bag.Container.Entries.Single().Stack.Mail.Single();
            string line = $"The Mega {giant.Name} dropped a Lost Medium Package for {a.Map.FormatAddress(parcel.Address)}";
            Assert.Single(feed.Entries, e => e.Text == line);
            Assert.True(a.Bags.ContainsKey(bag.Id));

            Assert.True(sim.Give(truth.Inventory, ItemStack.OfMail(parcel)));
            Run(10);
            var held = a.Inventory.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            Assert.True(held.Stack.Lost);
            Assert.StartsWith("Lost 1 Medium Package for ", HudText.Stack(a, held.Stack));
            Assert.Equal(0, ada.ContainerDesyncs + ada.BadPackets);
        }
    }
}
