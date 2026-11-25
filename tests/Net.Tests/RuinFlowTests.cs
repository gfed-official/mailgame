using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Ruins over the loopback transport (doc 05 §5; design choice 126): a wall the raiders break
    /// leaves its ruin on every client and for one who joins later; beside it the prompt offers
    /// to rebuild it in Prep for half, E does, and the feed says so once; in Delivery the prompt
    /// clears it away instead.
    /// </summary>
    public class RuinFlowTests
    {
        private const double Dt = 1.0 / 30.0;
        private const int Tx = 22, Tz = 15;

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly ContentDb Content = Shared.Content;

            public Session()
            {
                Server = new ServerRuntime(Content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, Hub);
            }

            public SimWorld Sim => Server.World;

            public ClientRuntime Join(string name, int startShift = 0)
            {
                var client = new ClientRuntime(Content, Hub.CreateClient(), name, PlaytestFlags.None, startShift);
                Clients.Add(client);
                Run(10);
                return client;
            }

            public void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var c in Clients) c.Tick();
                    Server.Tick();
                }
            }

            public Player Truth(ClientRuntime client) => Sim.GetPlayer(client.World!.LocalId)!;
        }

        [Fact]
        public void ABrokenWallsRuin_IsSeen_RebuiltInPrepForHalf_AndClearedInDelivery()
        {
            var s = new Session();
            var ada = s.Join("Ada", startShift: 2);
            var a = ada.World!;
            var interaction = new InteractionController(ada);
            var feed = new EventFeed();
            ada.EventReceived += e => feed.OnEvent(a, e);
            Assert.NotEqual(0u, ada.PressGo());
            s.Run(10);
            Assert.Equal((RunPhase.Prep, 2), (s.Sim.Run.Phase, s.Sim.Run.Shift));
            var truth = s.Truth(ada);
            var wallDef = s.Content.Building("wall_stone");
            Assert.True(s.Sim.Grant(s.Content.ShopItem("bp_defense"), truth.Id));
            Assert.True(s.Sim.Give(truth.Inventory, ItemStack.OfItem(s.Content.Item("stone"), 6)));
            truth.Move.X = BuildRules.TileCentre(Tx) + 2f;
            truth.Move.Z = BuildRules.TileCentre(Tz);
            s.Run(20);
            Assert.NotEqual(0u, ada.PlaceConstruct(wallDef.Recipe, Tx, Tz, 1));
            s.Run(10);
            Assert.NotNull(a.Constructs.At(Tx, Tz));

            s.Sim.DamageConstruct(s.Sim.Constructs.At(Tx, Tz)!, wallDef.Hp);
            s.Run(5);
            Assert.Null(a.Constructs.At(Tx, Tz));
            var ruin = a.Constructs.RuinAt(Tx, Tz)!;
            Assert.Same(wallDef, ruin.Def);
            Assert.True(a.IsRuin(ruin));
            Assert.Contains(feed.Entries, e => e.Bad && e.Text == "The raiders broke a Stone Wall: its ruin is left, rebuilt in Prep for half its cost");

            // One who joins now sees it too.
            var bo = s.Join("Bo");
            Assert.NotNull(bo.World!.Constructs.RuinAt(Tx, Tz));

            // Beside it, looking at it: rebuild for half.
            var box = AimBox.Built(a.Map, ruin);
            ViewRay.Aim(a.Local, box.X, box.Y, box.Z, out ada.Input.YawRadians, out ada.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.RebuildRuin, interaction.Prompt.Kind);
            Assert.Equal("Rebuild the Stone Wall  [E]   ·   Stone x2 (half its cost)", HudText.Prompt(a, interaction.Prompt, out _));
            interaction.Update(Dt, false, false, interactTapped: true);
            s.Run(10);
            Assert.Null(a.Constructs.RuinAt(Tx, Tz));
            Assert.Null(bo.World.Constructs.RuinAt(Tx, Tz));
            Assert.Same(wallDef, a.Constructs.At(Tx, Tz)!.Def);
            Assert.Equal(1, a.Constructs.At(Tx, Tz)!.Rotation);
            Assert.Equal(0, BuildRules.Have(truth.Hotbar, truth.Inventory, s.Content.Item("stone")));
            Assert.Single(feed.Entries, e => e.Text == "You rebuilt the Stone Wall");
            Assert.DoesNotContain(feed.Entries.Skip(feed.Entries.Count - 1), e => e.Text.StartsWith("You built", System.StringComparison.Ordinal));

            // In Delivery the same ruin is cleared away instead.
            Assert.NotEqual(0u, ada.PressGo());
            Assert.NotEqual(0u, bo.PressGo());
            s.Run(10);
            Assert.Equal(RunPhase.Delivery, a.Global.Phase);
            s.Sim.DamageConstruct(s.Sim.Constructs.At(Tx, Tz)!, wallDef.Hp);
            s.Run(5);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.ClearRuin, interaction.Prompt.Kind);
            Assert.Equal("Clear the ruin of the Stone Wall  [E]   ·   rebuilt in Prep for half its cost, or anew at full cost", HudText.Prompt(a, interaction.Prompt, out _));
            interaction.Update(Dt, false, false, interactTapped: true);
            s.Run(10);
            Assert.Null(a.Constructs.RuinAt(Tx, Tz));
            Assert.Null(a.Constructs.At(Tx, Tz));
            Assert.Contains(feed.Entries, e => e.Text == "You cleared away the ruin of the Stone Wall");
            Assert.Equal(0, BuildRules.Have(truth.Hotbar, truth.Inventory, s.Content.Item("stone")));
        }
    }
}
