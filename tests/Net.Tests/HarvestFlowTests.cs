using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Harvesting (doc 02 §3.9) as the clients see it over the loopback transport: the prompt at
    /// a tree, a bush and a boulder, the held key that swings, what lands in the pockets, and the
    /// state of the nodes for whoever watches, joins late, or is there when the bushes grow back.
    /// </summary>
    public class HarvestFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly ContentDb Content = Shared.Content;
            private uint _nextMailId = 760000;

            public Session()
            {
                Server = new ServerRuntime(Content, new SimSettings { Seed = 3, Raids = false, Draft = false }, Hub);
            }

            public SimWorld Sim => Server.World;

            public ClientRuntime Join(string name)
            {
                var client = new ClientRuntime(Content, Hub.CreateClient(), name);
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

            public bool RunUntil(Func<bool> done, int maxSeconds)
            {
                for (int i = 0; i < maxSeconds * 30 && !done(); i++) Run(1);
                return done();
            }

            public Player Truth(ClientRuntime client) => Sim.GetPlayer(client.World!.LocalId)!;

            /// <summary>The host's go key in the Lobby: the first Prep.</summary>
            public void ToPrep()
            {
                Assert.NotEqual(0u, Clients.Single(c => c.World!.IsHost).PressGo());
                Run(10);
                Assert.Equal(RunPhase.Prep, Sim.Run.Phase);
            }

            public void StartDelivery()
            {
                foreach (var c in Clients) Assert.NotEqual(0u, c.PressGo());
                Run(10);
                Assert.Equal(RunPhase.Delivery, Sim.Run.Phase);
            }

            /// <summary>The <paramref name="nth"/> prop of the map that is this node.</summary>
            public int Node(string id, int nth = 0)
            {
                for (int i = 0; i < Sim.Nodes.Count; i++)
                    if (Sim.Nodes.Def(i)?.Id == id && nth-- == 0) return i;
                throw new InvalidOperationException($"the map has no '{id}'");
            }

            /// <summary>Stands the client's player beside a node, looking at it.</summary>
            public void Face(ClientRuntime client, int node)
            {
                var prop = Sim.Map.Props[node];
                var truth = Truth(client);
                truth.Move.X = prop.X + prop.Radius + 0.9f;
                truth.Move.Z = prop.Z;
                Run(8);
                var box = AimBox.Node(Sim.Map, prop);
                ViewRay.Aim(client.World!.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
            }

            /// <summary>Puts one of an item on the client's hotbar and says which slot it is in.</summary>
            public int Hand(ClientRuntime client, string item)
            {
                var truth = Truth(client);
                Assert.True(Sim.Give(truth.Hotbar, ItemStack.OfItem(Content.Item(item), 1)));
                Run(5);
                ushort index = Content.Item(item).Index;
                return client.World!.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index).X;
            }

            /// <summary>The client puts one letter worth the whole quota into its mailbox.</summary>
            public void MeetTheQuota(ClientRuntime client)
            {
                var home = Sim.Map.Destinations[0];
                var item = new MailItem
                {
                    Id = _nextMailId++, Kind = Content.MailKind("letter").Index, Address = home.Address, Value = (ushort)Sim.Run.Quota,
                    SpawnShift = (byte)Sim.Run.Shift, DeadlineShift = (byte)Sim.Run.Shift,
                };
                var truth = Truth(client);
                Assert.True(Sim.Give(truth.Hotbar, ItemStack.OfMail(item)));
                truth.Move.X = home.MailboxX;
                truth.Move.Z = home.MailboxZ;
                Run(20);
                var entry = truth.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
                Assert.NotEqual(0u, client.Deliver(truth.Hotbar.Id, entry.EntryId, home.Id));
                Run(10);
                Assert.True(Sim.Economy.ShiftEarnings >= Sim.Run.Quota);
            }
        }

        private static int Units(ClientWorld world, string item)
        {
            int index = world.Content.Item(item).Index, n = 0;
            foreach (var container in new[] { world.Hotbar, world.Inventory })
                foreach (var e in container.Entries)
                    if (e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index) n += e.Stack.Count;
            return n;
        }

        [Fact]
        public void AShovelDigsASandPile_AndTheOthersSeeItInTheHand()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            ClientWorld a = ada.World!, b = bo.World!;
            var interaction = new InteractionController(ada);
            int pile = s.Node("sand_pile");
            s.ToPrep();
            s.Face(ada, pile);

            // By hand, or with the wrong tool, the pile asks for its own.
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Harvest, interaction.Prompt.Kind);
            Assert.Equal(pile, interaction.Prompt.NodeIndex);
            Assert.Equal("Sand Pile   ·   needs a Shovel in the hand", HudText.Prompt(a, interaction.Prompt, out var tone));
            Assert.Equal(PromptTone.Mismatch, tone);
            ada.Input.HeldSlot = (byte)s.Hand(ada, "pickaxe");
            s.Run(3);
            interaction.Update(Dt, false, false);
            Assert.Equal(OpResult.NeedsTool, interaction.Prompt.NodeState);
            // The others see the Pickaxe in the hand: the snapshot says which tool it is.
            Assert.Equal(s.Content.Item("pickaxe").Index + 1, b.Players[a.LocalId].HeldTool);

            int shovel = s.Hand(ada, "shovel");
            ada.Input.HeldSlot = (byte)shovel;
            s.Run(3);
            Assert.Equal(s.Content.Item("shovel").Index + 1, b.Players[a.LocalId].HeldTool);
            interaction.Update(Dt, false, false);
            Assert.Equal("Dig Sand Pile   ·   Sand x4 a hit   ·   4 hits left   [hold E]", HudText.Prompt(a, interaction.Prompt, out tone));
            Assert.Equal(PromptTone.Neutral, tone);
            for (int frames = 0; frames < 150 && !a.Nodes.Depleted(pile); frames++)
            {
                interaction.Update(Dt, true, false);
                s.Run(1);
            }
            s.Run(5);
            Assert.True(a.Nodes.Depleted(pile) && b.Nodes.Depleted(pile));
            Assert.Equal(16, Units(a, "sand"));

            // Mail or bare hands: no tool for anybody to see.
            ada.Input.HeldSlot = 0;
            s.Run(3);
            Assert.Equal(0, b.Players[a.LocalId].HeldTool);
        }

        [Fact]
        public void AHeldKeyPicksABushClean_AndEverybodySeesIt()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            ClientWorld a = ada.World!, b = bo.World!;
            var interaction = new InteractionController(ada);
            EventFeed adaFeed = new EventFeed(), boFeed = new EventFeed();
            ada.EventReceived += e => adaFeed.OnEvent(a, e);
            bo.EventReceived += e => boFeed.OnEvent(b, e);
            var refused = new List<OpResult>();
            ada.RequestCompleted += (request, result) => { if (request.Kind == RequestKind.Harvest && result != OpResult.Ok) refused.Add(result); };
            int bush = s.Node("fiber_bush");
            Assert.Equal(s.Sim.Nodes.NodeCount, a.Nodes.NodeCount);

            // The Lobby: a bush is a bush.
            s.Face(ada, bush);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.None, interaction.Prompt.Kind);
            Assert.Equal(OpResult.WrongPhase, a.NodeState(bush, 0));
            Assert.Equal(0u, ada.Harvest(bush));

            // Prep: looked at from beside it, it is there for the picking.
            s.ToPrep();
            s.Face(ada, bush);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Harvest, interaction.Prompt.Kind);
            Assert.Equal(bush, interaction.Prompt.NodeIndex);
            Assert.Equal("Pick Bush   ·   Fiber x3 a hit   ·   3 hits left   [hold E]", HudText.Prompt(a, interaction.Prompt, out var tone));
            Assert.Equal(PromptTone.Neutral, tone);
            Assert.Equal(0f, interaction.Prompt.HoldProgress);

            // Looked away from, it is not.
            float yaw = ada.Input.YawRadians;
            ada.Input.YawRadians = yaw + MathF.PI;
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.None, interaction.Prompt.Kind);
            ada.Input.YawRadians = yaw;

            // The key held: the first hit after one swing's time, not before.
            for (int i = 0; i < 10; i++)
            {
                interaction.Update(Dt, true, false);
                s.Run(1);
            }
            Assert.InRange(interaction.Prompt.HoldProgress, 0.4f, 0.7f);
            Assert.Equal(0, ada.LocalHarvestSwings);
            Assert.Equal(3, a.Nodes.HitsLeft(bush));

            // Held on: three swings and the bush is bare, with nothing swung too soon.
            int frames = 10;
            for (; frames < 150 && !a.Nodes.Depleted(bush); frames++)
            {
                interaction.Update(Dt, true, false);
                s.Run(1);
            }
            Assert.True(a.Nodes.Depleted(bush), "the bush is not bare after 5 s of swinging");
            Assert.InRange(frames, 54, 75);
            s.Run(5);
            Assert.Empty(refused);
            Assert.Equal(3, ada.LocalHarvestSwings);
            Assert.True(s.Sim.Nodes.Depleted(bush));
            Assert.Equal(9, Units(a, "fiber"));
            Assert.Equal(9, s.Truth(ada).Inventory.Entries.Sum(e => e.Stack.Count));
            Assert.Equal(0, ada.ContainerDesyncs);

            // Nothing left to look at, for either of them; Bo saw every swing.
            interaction.Update(Dt, true, false);
            Assert.Equal(InteractKind.None, interaction.Prompt.Kind);
            Assert.Equal(OpResult.Depleted, a.NodeState(bush, 0));
            Assert.Equal(0u, ada.Harvest(bush));
            Assert.True(b.Nodes.Depleted(bush));
            Assert.Equal(1, b.Nodes.Version);
            Assert.NotEqual(0u, b.Players[a.LocalId].AttackTick);
            Assert.Equal(0u, a.Players[a.LocalId].AttackTick);
            Assert.Equal("You gathered Fiber x9", adaFeed.Entries[adaFeed.Entries.Count - 1].Text);
            Assert.Equal("Ada gathered Fiber x9", boFeed.Entries[boFeed.Entries.Count - 1].Text);
        }

        [Fact]
        public void ABoulderAsksForAPickaxe_AndATreeGivesMoreToAnAxe()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            ClientWorld a = ada.World!;
            var interaction = new InteractionController(ada);
            s.ToPrep();

            // Bare hands on a boulder: the prompt says what it takes, and the key swings at nothing.
            int boulder = s.Node("boulder");
            s.Face(ada, boulder);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Harvest, interaction.Prompt.Kind);
            Assert.Equal(OpResult.NeedsTool, interaction.Prompt.NodeState);
            Assert.Equal("Boulder   ·   needs a Pickaxe in the hand", HudText.Prompt(a, interaction.Prompt, out var tone));
            Assert.Equal(PromptTone.Mismatch, tone);
            for (int i = 0; i < 45; i++)
            {
                interaction.Update(Dt, true, false);
                s.Run(1);
            }
            Assert.Equal(0f, interaction.Prompt.HoldProgress);
            Assert.Equal(0, ada.LocalHarvestSwings);
            Assert.Equal(0u, ada.Harvest(boulder));
            interaction.Update(Dt, false, false);

            // The Pickaxe in the pockets is not the Pickaxe in the hand.
            int pick = s.Hand(ada, "pickaxe");
            interaction.Update(Dt, false, false);
            Assert.Equal(OpResult.NeedsTool, interaction.Prompt.NodeState);
            ada.Input.HeldSlot = (byte)pick;
            interaction.Update(Dt, false, false);
            Assert.Equal(OpResult.Ok, interaction.Prompt.NodeState);
            Assert.Equal("Mine Boulder   ·   Stone x3 a hit   ·   6 hits left   [hold E]", HudText.Prompt(a, interaction.Prompt, out tone));
            Assert.Equal(PromptTone.Neutral, tone);
            for (int i = 0; i < 30; i++)
            {
                interaction.Update(Dt, true, false);
                s.Run(1);
            }
            interaction.Update(Dt, false, false);
            Assert.Equal(1, ada.LocalHarvestSwings);
            Assert.Equal(3, Units(a, "stone"));
            Assert.Equal("Mine Boulder   ·   Stone x3 a hit   ·   5 hits left   [hold E]", HudText.Prompt(a, interaction.Prompt, out _));

            // A tree gives to bare hands and to the wrong tool, and says what gives more.
            int tree = s.Node("tree");
            s.Face(ada, tree);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Harvest, interaction.Prompt.Kind);
            Assert.Equal(tree, interaction.Prompt.NodeIndex);
            Assert.Equal("Chop Tree   ·   Log x1 a hit (more with an Axe)   ·   5 hits left   [hold E]", HudText.Prompt(a, interaction.Prompt, out _));
            ada.Input.HeldSlot = 0;
            interaction.Update(Dt, false, false);
            Assert.Equal("Chop Tree   ·   Log x1 a hit (more with an Axe)   ·   5 hits left   [hold E]", HudText.Prompt(a, interaction.Prompt, out _));
            ada.Input.HeldSlot = (byte)s.Hand(ada, "axe");
            interaction.Update(Dt, false, false);
            Assert.Equal("Chop Tree   ·   Log x2 a hit   ·   5 hits left   [hold E]", HudText.Prompt(a, interaction.Prompt, out _));
            Assert.NotEqual(0u, ada.Harvest(tree));
            s.Run(10);
            Assert.Equal(2, Units(a, "log"));
            interaction.Update(Dt, false, false);
            Assert.Equal("Chop Tree   ·   Log x2 a hit   ·   4 hits left   [hold E]", HudText.Prompt(a, interaction.Prompt, out _));
        }

        [Fact]
        public void FullPocketsAreSaidBeforeTheSwing()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            ClientWorld a = ada.World!;
            var interaction = new InteractionController(ada);
            s.ToPrep();
            var truth = s.Truth(ada);
            // Logs lie across two cells: sixteen of them fill the inventory, seven more the hotbar.
            var log = s.Content.Item("log");
            for (int i = 0; i < 8; i++) Assert.True(s.Sim.Give(truth.Inventory, ItemStack.OfItem(log, log.MaxStack)));
            int bush = s.Node("berry_bush");
            var bandage = s.Content.Item("bandage");
            while (s.Sim.Give(truth.Hotbar, ItemStack.OfItem(bandage, bandage.MaxStack))) { }
            s.Face(ada, bush);

            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Harvest, interaction.Prompt.Kind);
            Assert.Equal(OpResult.DoesNotFit, interaction.Prompt.NodeState);
            Assert.Equal("Berry Bush   ·   no room in your pockets for Berries x2", HudText.Prompt(a, interaction.Prompt, out var tone));
            Assert.Equal(PromptTone.Mismatch, tone);
            for (int i = 0; i < 30; i++)
            {
                interaction.Update(Dt, true, false);
                s.Run(1);
            }
            Assert.Equal(0, ada.LocalHarvestSwings);
            Assert.Equal(2, s.Sim.Nodes.HitsLeft(bush));
        }

        [Fact]
        public void ALateJoinerSeesWhatIsLeft_AndTheBushesGrowBack()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            ClientWorld a = ada.World!;
            var feed = new EventFeed();
            ada.EventReceived += e => feed.OnEvent(a, e);
            s.ToPrep();
            int bush = s.Node("fiber_bush", 1), berries = s.Node("berry_bush"), tree = s.Node("tree");

            s.Face(ada, bush);
            for (int i = 0; i < 3; i++)
            {
                Assert.NotEqual(0u, ada.Harvest(bush));
                s.Run(20);
            }
            s.Face(ada, berries);
            Assert.NotEqual(0u, ada.Harvest(berries));
            s.Run(20);
            s.Face(ada, tree);
            Assert.NotEqual(0u, ada.Harvest(tree));
            s.Run(20);
            Assert.Equal((9, 2, 1), (Units(a, "fiber"), Units(a, "berries"), Units(a, "log")));
            // Berries are for the hand.
            Assert.Contains(a.Hotbar.Entries, e => e.Stack.DefIndex == s.Content.Item("berries").Index);

            // Whoever joins now is told of the three that are not as they grew, and of no other.
            var cy = s.Join("Cy");
            ClientWorld c = cy.World!;
            Assert.True(c.Nodes.Depleted(bush));
            Assert.Equal(1, c.Nodes.HitsLeft(berries));
            Assert.Equal(4, c.Nodes.HitsLeft(tree));
            for (int i = 0; i < c.Nodes.Count; i++) Assert.Equal(s.Sim.Nodes.HitsLeft(i), c.Nodes.HitsLeft(i));

            // The next shift: the bushes are full again, the tree is as it was left.
            s.StartDelivery();
            s.MeetTheQuota(ada);
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Prep, 460));
            s.Run(10);
            foreach (var w in new[] { a, c })
            {
                Assert.False(w.Nodes.Depleted(bush));
                Assert.Equal(3, w.Nodes.HitsLeft(bush));
                Assert.Equal(2, w.Nodes.HitsLeft(berries));
                Assert.Equal(4, w.Nodes.HitsLeft(tree));
            }
            Assert.Contains(feed.Entries, e => e.Text == "The bushes have grown back");

            // The run ends in the Lobby with every tree standing.
            s.StartDelivery();
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Results, 330));
            s.Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            s.Run(10);
            Assert.Equal(RunPhase.Lobby, s.Sim.Run.Phase);
            foreach (var w in new[] { a, c })
                for (int i = 0; i < w.Nodes.Count; i++) Assert.False(w.Nodes.Touched(i));
        }
    }
}
