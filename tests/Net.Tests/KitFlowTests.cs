using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The starting kits over the loopback transport (doc 03 §4.4, doc 06 §5): the rank goes in the
    /// Hello, the kit key picks among those it opens, everybody sees who has which, and the run
    /// starts with the kits handed out and said in the feed.
    /// </summary>
    public class KitFlowTests
    {
        [Fact]
        public void KitsArePickedByRank_SeenByAll_AndHandedOutAsTheRunStarts()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = true }, hub);
            var clients = new List<ClientRuntime>();
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var c in clients) c.Tick();
                    server.Tick();
                }
            }
            var ada = new ClientRuntime(content, hub.CreateClient(), "Ada", rank: 1);
            clients.Add(ada);
            Run(10);
            var bo = new ClientRuntime(content, hub.CreateClient(), "Bo", rank: 3);
            clients.Add(bo);
            Run(10);
            var adaFeed = new EventFeed { Content = content };
            var boFeed = new EventFeed { Content = content };
            ada.EventReceived += e => adaFeed.OnEvent(ada.World!, e);
            bo.EventReceived += e => boFeed.OnEvent(bo.World!, e);
            bo.RequestCompleted += boFeed.OnRequest;
            ClientWorld a = ada.World!, b = bo.World!;

            // Rank 1: the Land kit and no other. Rank 3: Land and Sea; Pipes waits for rank 10.
            Assert.Equal(0u, ada.CycleKit());
            Assert.Equal(OpResult.RankTooLow, ada.KitState(content.Kit("sea")));
            Assert.Equal("Land · Sea (rank 2) · Pipes (rank 10)", HudText.KitsOpen(content, 1));
            Assert.Equal(OpResult.RankTooLow, bo.KitState(content.Kit("pipes")));
            Assert.NotEqual(0u, bo.CycleKit());
            Run(10);
            Assert.Same(content.Kit("sea"), bo.LocalKit);
            Assert.Equal(content.Kit("sea").Index, a.Players[b.LocalId].Kit);
            Assert.EndsWith("[K] kit: Sea   ·   [H] how to play", HudText.PhaseHint(b));
            Assert.Contains(boFeed.Entries, e => e.Text == "Your kit: Sea — Axe, Rope ×2 for you   ·   Log ×10 for the team");
            // Round again: back to Land.
            bo.CycleKit();
            Run(10);
            Assert.Same(content.DefaultKit, bo.LocalKit);
            bo.CycleKit();
            Run(10);

            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            Assert.Equal(RunPhase.Prep, server.World.Run.Phase);
            // Once the run is on the kit stays.
            Assert.Equal(OpResult.WrongPhase, bo.KitState(content.DefaultKit));
            Assert.Equal(0u, bo.CycleKit());

            int Count(ClientWorld w, string item) => w.Inventory.Entries.Where(e => e.Stack.Kind == StackKind.Item && content.Items[e.Stack.DefIndex].Id == item).Sum(e => e.Stack.Count);
            Assert.Equal((1, 4), (Count(a, "axe"), Count(a, "fiber")));
            Assert.Equal((1, 2), (Count(b, "axe"), Count(b, "rope")));
            Assert.Contains(adaFeed.Entries, e => e.Text == "Your Land kit is in your pockets: Axe, Fiber ×4");
            Assert.Contains(boFeed.Entries, e => e.Text == "Your Sea kit is in your pockets: Axe, Rope ×2");
            Assert.DoesNotContain(boFeed.Entries, e => e.Text.StartsWith("Your Land kit", System.StringComparison.Ordinal));
            // Ada hosts: hers is the team's.
            Assert.Contains(adaFeed.Entries, e => e.Text == "The team's kit (your Land kit): Log ×10 in the chest by the Post Office   ·   a Bike by the counter");
            Assert.Contains(boFeed.Entries, e => e.Text == "The team's kit (Ada's Land kit): Log ×10 in the chest by the Post Office   ·   a Bike by the counter");
            Assert.Single(b.Vehicles.Values);
        }
    }
}
