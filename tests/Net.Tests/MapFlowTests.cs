using System;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The map's guide (doc 09 §2.6) on a client: the house of the mail in the hand, the route
    /// there along the routing graph from where the carrier stands, and the line under the title.
    /// </summary>
    public class MapFlowTests
    {
        private static MailItem Letter(AddressId address, uint id)
        {
            var kind = Shared.Content.MailKind("letter");
            return new MailItem { Id = id, Kind = kind.Index, Address = address, Value = (ushort)kind.BaseValue, SpawnShift = 1, DeadlineShift = 3 };
        }

        [Theory]
        [InlineData("test_street", 3u)]
        [InlineData(WorldGenerator.SmallIsland, 5u)]
        public void TheMapShowsTheWayToTheHouseOfTheMailInTheHand(string mapId, uint seed)
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = seed, MapId = mapId, Raids = false, Draft = false }, hub);
            var client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); server.Tick(); }
            }
            Run(10);
            var world = client.World!;
            var guide = new MapGuide();
            var move = MoveConstants.From(Shared.Content);

            guide.Update(world, 1);
            Assert.Null(guide.Target);
            Assert.Equal("Take a piece of mail into the hotbar to see the way to its house", guide.Line(world, move));
            Assert.StartsWith($"MAP · {world.Map.Name}", MapGuide.Title(world));

            // Two letters: the far one in slot 3, the near one in slot 2; the held slot's wins.
            var sim = server.World;
            var me = sim.GetPlayer(world.LocalId)!;
            var houses = sim.Map.Destinations.OrderBy(d => Math.Abs(d.MailboxX - me.Move.X) + Math.Abs(d.MailboxZ - me.Move.Z)).ToList();
            var near = houses[0];
            var far = houses[^1];
            Assert.True(sim.Give(me.Hotbar, ItemStack.OfMail(Letter(near.Address, 900001))));
            Assert.True(sim.Give(me.Hotbar, ItemStack.OfMail(Letter(far.Address, 900002))));
            Run(10);
            int farSlot = Enumerable.Range(0, world.Hotbar.Cols).First(x => world.Hotbar.EntryAt(x, 0)?.Stack.Address == far.Address);
            guide.Update(world, farSlot);
            Assert.Equal(far.Address, guide.Target!.Address);
            Assert.Contains(near.Address, guide.Carried);
            Assert.Contains(far.Address, guide.Carried);
            Assert.Equal((world.Local.X, world.Local.Z), guide.Points[0]);
            Assert.Equal((far.MailboxX, far.MailboxZ), guide.Points[^1]);
            float straight = MathF.Sqrt((far.MailboxX - world.Local.X) * (far.MailboxX - world.Local.X) + (far.MailboxZ - world.Local.Z) * (far.MailboxZ - world.Local.Z));
            Assert.True(guide.Metres >= straight - 0.01f);
            Assert.Equal($"To {world.Map.FormatAddress(far.Address)} · {guide.Metres:F0} m by the streets · {HudText.Clock(guide.Metres / move.WalkSpeed)} on foot, {HudText.Clock(guide.Metres / move.SprintSpeed)} running",
                guide.Line(world, move));

            // The hands held (slot 0): the first mail in the hotbar shows the way.
            guide.Update(world, 0);
            Assert.NotNull(guide.Target);
        }
    }
}
