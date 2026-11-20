using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Build mode (doc 09 §2.7) as the clients see it over the loopback transport: B, the recipe
    /// bar, the ghost and why it is red, building with the attack button, taking down with the
    /// alt button held, the pipette, a built chest that opens like the map's, and what stands
    /// for whoever watches or joins late.
    /// </summary>
    public class BuildFlowTests
    {
        private const double Dt = 1.0 / 30.0;
        /// <summary>Open ground south of the street, between the mailboxes of numbers 4 and 6.</summary>
        private const int Tx = 22, Tz = 15;

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly ContentDb Content = Shared.Content;

            public Session()
            {
                Server = new ServerRuntime(Content, new SimSettings { Seed = 3, Raids = false, Draft = false }, Hub);
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

            public void ToPrep()
            {
                Assert.NotEqual(0u, Clients.Single(c => c.World!.IsHost).PressGo());
                Run(10);
                Assert.Equal(RunPhase.Prep, Sim.Run.Phase);
            }

            /// <summary>Stands the client's player at a point and lets the client hear of it.</summary>
            public void Put(ClientRuntime client, float x, float z)
            {
                var truth = Truth(client);
                truth.Move.X = x;
                truth.Move.Z = z;
                Run(10);
            }

            public void GiveLogs(ClientRuntime client, int logs)
            {
                Assert.True(Sim.Give(Truth(client).Inventory, ItemStack.OfItem(Content.Item("log"), logs)));
                Run(5);
            }
        }

        private static float Centre(int t) => BuildRules.TileCentre(t);

        /// <summary>Turns the view onto the middle of a tile on the ground.</summary>
        private static void LookAt(ClientRuntime client, int tx, int tz) =>
            ViewRay.Aim(client.World!.Local, Centre(tx), 0f, Centre(tz), out client.Input.YawRadians, out client.Input.PitchRadians);

        /// <summary>Turns the view onto the middle of what is built on a tile.</summary>
        private static void LookAt(ClientRuntime client, Construct built)
        {
            var box = AimBox.Built(client.World!.Map, built);
            ViewRay.Aim(client.World.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
        }

        private static int Logs(ClientWorld world) => BuildRules.Have(world.Hotbar, world.Inventory, world.Content.Item("log"));

        private static (int, int) StreetTileNear(WorldMap map, int tx, int tz)
        {
            for (int r = 0; r < 10; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (map.GetTile(tx + dx, tz + dz) == TileType.Street) return (tx + dx, tz + dz);
            throw new InvalidOperationException("no street near");
        }

        [Fact]
        public void BOpensBuildModeInPrep_AndTheAttackButtonBuildsWhereTheGhostIsGreen()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            ClientWorld a = ada.World!, b = bo.World!;
            var build = new BuildController(ada);
            EventFeed adaFeed = new EventFeed(), boFeed = new EventFeed();
            ada.EventReceived += e => adaFeed.OnEvent(a, e);
            bo.EventReceived += e => boFeed.OnEvent(b, e);
            var refusals = new List<OpResult>();
            build.OnRefused = refusals.Add;

            // The Lobby: nothing is built, and B says so.
            Assert.False(build.Toggle());
            Assert.Equal(OpResult.WrongPhase, build.Refused);
            Assert.Equal(new[] { OpResult.WrongPhase }, refusals);
            Assert.Equal(HudText.BuildClosed, HudText.PlaceReason(a, s.Content.Recipes[0], OpResult.WrongPhase));

            s.ToPrep();
            s.Put(ada, Centre(Tx - 2), Centre(Tz));
            Assert.True(build.Toggle());
            Assert.True(build.Active);
            Assert.Equal("wall_wood", build.Recipe!.Building.Id);

            // Empty pockets: the ghost is on the tile and red, and says what is missing.
            LookAt(ada, Tx, Tz);
            build.Update(Dt, false, false);
            var ghost = build.Ghost;
            Assert.Equal(BuildAim.Place, ghost.Aim);
            Assert.Equal((Tx, Tz), (ghost.Tx, ghost.Tz));
            Assert.Equal(OpResult.MissingMaterials, ghost.State);
            Assert.Equal("Wooden Wall   ·   Missing 3 Log", HudText.BuildPrompt(a, ghost, out var tone));
            Assert.Equal(PromptTone.Mismatch, tone);
            build.Update(Dt, true, false);
            s.Run(5);
            Assert.Empty(s.Sim.Constructs.All);

            // With Logs it is green; the button builds it, for everybody.
            s.GiveLogs(ada, 10);
            build.Update(Dt, false, false);
            Assert.Equal(OpResult.Ok, build.Ghost.State);
            Assert.Equal("Build Wooden Wall   ·   Log x3   [LMB]", HudText.BuildPrompt(a, build.Ghost, out tone));
            Assert.Equal(PromptTone.Match, tone);
            Assert.Equal("[1] Wooden Wall   ·   Log x3", HudText.RecipeCard(build.Recipe!, 0));
            build.Update(Dt, true, false);
            s.Run(10);
            var wall = Assert.Single(s.Sim.Constructs.All);
            Assert.Equal((Tx, Tz, 0), (wall.Tx, wall.Tz, (int)wall.Rotation));
            Assert.Equal(7, Logs(a));
            Assert.Equal(0, ada.ContainerDesyncs);
            foreach (var w in new[] { a, b })
            {
                var mirror = Assert.Single(w.Constructs.All);
                Assert.Equal((Tx, Tz, "wall_wood"), (mirror.Tx, mirror.Tz, mirror.Def.Id));
            }
            Assert.Equal("You built a Wooden Wall", adaFeed.Entries.Last().Text);
            Assert.Equal("Ada built a Wooden Wall", boFeed.Entries.Last().Text);

            // Looked at again, it is something built: what taking it down gives back.
            build.Update(Dt, false, false);
            Assert.Equal(BuildAim.Construct, build.Ghost.Aim);
            Assert.Same(a.Constructs.All[0], build.Ghost.Target);
            Assert.Equal("Take down Wooden Wall   ·   Log x3 back   [hold RMB]", HudText.BuildPrompt(a, build.Ghost, out _));

            // R turns the next one; the one beside it closes the corner on screen as in the sim.
            Assert.True(build.Rotate());
            LookAt(ada, Tx, Tz + 1);
            build.Update(Dt, false, false);
            Assert.Equal((Tx, Tz + 1, 1), (build.Ghost.Tx, build.Ghost.Tz, build.Ghost.Rotation));
            build.Update(Dt, true, false);
            s.Run(10);
            Assert.Equal(2, b.Constructs.All.Count);
            Assert.Equal(1, b.Constructs.At(Tx, Tz + 1)!.Rotation);
            Assert.Equal(s.Sim.Constructs.Arms(s.Sim.Constructs.At(Tx, Tz)!), b.Constructs.Arms(b.Constructs.At(Tx, Tz)!));
            Assert.Equal(4, Logs(a));

            // A press with nothing green does nothing: not a second request for the same tile.
            build.Update(Dt, true, false);
            s.Run(5);
            Assert.Equal(2, s.Sim.Constructs.All.Count);

            // B again, and the mode is down.
            Assert.False(build.Toggle());
            Assert.Equal(BuildAim.None, build.Ghost.Aim);
        }

        [Fact]
        public void TheGhostSaysWhyItIsRed()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            ClientWorld a = ada.World!;
            var build = new BuildController(ada);
            s.ToPrep();
            s.GiveLogs(ada, 10);
            s.Put(ada, Centre(Tx - 2), Centre(Tz));
            Assert.True(build.Toggle());

            // The street.
            var (sx, sz) = StreetTileNear(a.Map, Tx, Tz);
            s.Put(ada, Centre(sx), Centre(sz) + (sz < Tz ? 3.5f : -3.5f));
            LookAt(ada, sx, sz);
            build.Update(Dt, false, false);
            Assert.Equal((sx, sz), (build.Ghost.Tx, build.Ghost.Tz));
            Assert.Equal(OpResult.OnStreet, build.Ghost.State);
            Assert.Equal("Wooden Wall   ·   On the street: it stays open for the mail", HudText.BuildPrompt(a, build.Ghost, out _));

            // Too far: the ghost goes along the ground further than the hands reach.
            s.Put(ada, Centre(Tx - 5), Centre(Tz));
            LookAt(ada, Tx, Tz);
            build.Update(Dt, false, false);
            Assert.Equal((Tx, Tz), (build.Ghost.Tx, build.Ghost.Tz));
            Assert.Equal(OpResult.OutOfRange, build.Ghost.State);
            Assert.EndsWith("Too far away", HudText.BuildPrompt(a, build.Ghost, out _));

            // Somebody stands there.
            s.Put(ada, Centre(Tx - 2), Centre(Tz));
            s.Put(bo, Centre(Tx), Centre(Tz));
            s.Run(10);
            LookAt(ada, Tx, Tz);
            build.Update(Dt, false, false);
            Assert.Equal(OpResult.Occupied, build.Ghost.State);
            Assert.EndsWith("Somebody is in the way", HudText.BuildPrompt(a, build.Ghost, out _));

            // A mailbox's tile.
            var dest = a.Map.Destinations[0];
            int mx = (int)MathF.Floor(dest.MailboxX / SimConstants.TileSize), mz = (int)MathF.Floor(dest.MailboxZ / SimConstants.TileSize);
            Assert.Equal(OpResult.Blocked, a.PlaceState(build.Recipe!, mx, mz));

            // Looking up: no ghost at all.
            ada.Input.PitchRadians = 0.4f;
            build.Update(Dt, false, false);
            Assert.Equal(BuildAim.None, build.Ghost.Aim);
            Assert.Equal("Build Wooden Wall   ·   look at open ground", HudText.BuildPrompt(a, build.Ghost, out _));
        }

        [Fact]
        public void AChestIsBuiltOpenedAndTakenDown_ByTheHeldAltButton()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            ClientWorld a = ada.World!;
            var build = new BuildController(ada);
            var interaction = new InteractionController(ada);
            EventFeed feed = new EventFeed();
            ada.EventReceived += e => feed.OnEvent(a, e);
            s.ToPrep();
            s.GiveLogs(ada, 10);
            s.Put(ada, Centre(Tx - 1) - 0.2f, Centre(Tz));
            Assert.True(build.Toggle());

            // Key 2 is the Chest.
            Assert.True(build.PressKey(1));
            Assert.Equal("chest", build.Recipe!.Building.Id);
            Assert.False(build.PressKey(5));
            LookAt(ada, Tx, Tz);
            build.Update(Dt, true, false);
            s.Run(10);
            var chest = Assert.Single(a.Constructs.All);
            Assert.NotEqual(0u, chest.ContainerId);
            Assert.Equal(6, Logs(a));

            // E opens it like the map's chest; something in it, and it does not come down.
            LookAt(ada, chest);
            interaction.Update(Dt, false, false, interactTapped: true);
            Assert.Equal(InteractKind.OpenChest, interaction.Prompt.Kind);
            Assert.Same(chest, interaction.Prompt.Built);
            Assert.Equal("Open Chest  [E]", HudText.Prompt(a, interaction.Prompt, out _));
            s.Run(10);
            Assert.Equal(chest.ContainerId, a.OpenContainerId);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.CloseChest, interaction.Prompt.Kind);
            var log = a.Inventory.Entries.First(e => e.Stack.Kind == StackKind.Item);
            Assert.NotEqual(0u, ada.Send(SimRequest.QuickMove(0, a.InventoryId, log.EntryId)));
            s.Run(10);
            Assert.True(a.Containers[chest.ContainerId].EntryCount > 0);
            build.Update(Dt, false, false);
            Assert.Equal(BuildAim.Construct, build.Ghost.Aim);
            Assert.Equal(OpResult.NotEmpty, build.Ghost.State);
            Assert.Equal("Chest   ·   empty it first to take it down", HudText.BuildPrompt(a, build.Ghost, out var tone));
            Assert.Equal(PromptTone.Mismatch, tone);

            // Emptied: the alt button held half a second takes it down, and the Logs are back.
            var inside = a.Containers[chest.ContainerId].Entries.First();
            Assert.NotEqual(0u, ada.Send(SimRequest.QuickMove(0, chest.ContainerId, inside.EntryId)));
            s.Run(10);
            Assert.Equal(6, Logs(a));
            build.Update(Dt, false, false);
            Assert.Equal(OpResult.Ok, build.Ghost.State);
            Assert.Equal("Take down Chest   ·   Log x4 back   [hold RMB]", HudText.BuildPrompt(a, build.Ghost, out _));
            int frames = 0;
            for (; frames < 60 && a.Constructs.All.Count > 0; frames++)
            {
                build.Update(Dt, false, true);
                if (frames == 7) Assert.InRange(build.Ghost.HoldProgress, 0.4f, 0.6f);
                s.Run(1);
            }
            Assert.Empty(a.Constructs.All);
            Assert.InRange(frames, 15, 25);
            Assert.Empty(s.Sim.Constructs.All);
            Assert.Equal(10, Logs(a));
            Assert.Equal(0u, a.OpenContainerId);
            Assert.False(a.Containers.ContainsKey(chest.ContainerId));
            Assert.Equal("You took down a Chest", feed.Entries.Last().Text);
            Assert.Equal(0, ada.ContainerDesyncs);
        }

        [Fact]
        public void ThePipettePicksWhatIsLookedAt_AndALateJoinerSeesWhatStands()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            ClientWorld a = ada.World!;
            var build = new BuildController(ada);
            s.ToPrep();
            s.GiveLogs(ada, 10);
            s.Put(ada, Centre(Tx - 2), Centre(Tz));
            Assert.True(build.Toggle());
            Assert.True(build.Rotate());
            LookAt(ada, Tx, Tz);
            build.Update(Dt, true, false);
            s.Run(10);
            var wall = Assert.Single(a.Constructs.All);
            Assert.Equal(1, wall.Rotation);

            // Turned back and on the Chest, Q on the wall gives the wall as it stands.
            Assert.True(build.PressKey(1));
            Assert.True(build.Rotate());
            Assert.True(build.Rotate());
            LookAt(ada, wall);
            build.Update(Dt, false, false);
            Assert.True(build.Pipette());
            Assert.Equal("wall_wood", build.Recipe!.Building.Id);
            Assert.Equal(1, build.Rotation);

            // A late joiner is told of it in the Welcome; a hit on it reaches every screen.
            var cy = s.Join("Cy");
            var c = cy.World!;
            var mirror = Assert.Single(c.Constructs.All);
            Assert.Equal((Tx, Tz, 1, 300), (mirror.Tx, mirror.Tz, (int)mirror.Rotation, mirror.Hp));
            s.Sim.DamageConstruct(s.Sim.Constructs.At(Tx, Tz)!, 120f);
            s.Run(5);
            Assert.Equal(180, c.Constructs.At(Tx, Tz)!.Hp);
            Assert.Equal(180, a.Constructs.At(Tx, Tz)!.Hp);
            build.Update(Dt, false, false);
            Assert.Equal("Take down Wooden Wall   ·   Log x1 back   ·   180/300 HP   [hold RMB]", HudText.BuildPrompt(a, build.Ghost, out _));

            // Walking into it: the client's own prediction stops where the server does (the put there was the server's).
            s.Put(cy, Centre(Tx) - 1.5f, Centre(Tz));
            for (int i = 0; i < 45; i++)
            {
                cy.Input.MoveX = 1f;
                cy.Input.MoveZ = 0f;
                s.Run(1);
            }
            cy.Input.MoveX = 0f;
            s.Run(10);
            Assert.True(c.Local.X < Centre(Tx) - 0.2f, $"walked through the wall to {c.Local.X}");
            Assert.True(MathF.Abs(c.Local.X - s.Truth(cy).Move.X) < 0.05f);

            // Broken by the raiders, it is gone from every screen.
            s.Sim.DamageConstruct(s.Sim.Constructs.At(Tx, Tz)!, 1000f);
            s.Run(5);
            Assert.Empty(c.Constructs.All);
        }

        [Fact]
        public void TheFortifyBlueprintBoughtByOneIsBuiltWithByAnother()
        {
            var s = new Session();
            var ada = s.Join("Ada", startShift: 2);
            var bo = s.Join("Bo");
            ClientWorld a = ada.World!, b = bo.World!;
            var build = new BuildController(bo);
            var boFeed = new EventFeed();
            bo.EventReceived += e => boFeed.OnEvent(b, e);
            s.ToPrep();
            Assert.Equal(2, s.Sim.Run.Shift);
            s.Sim.Economy.AddLoot(1000);
            Assert.True(s.Sim.Give(s.Truth(bo).Inventory, ItemStack.OfItem(s.Content.Item("iron_ingot"), 4)));
            s.Put(bo, Centre(Tx - 2), Centre(Tz));

            // The bar has the Fortify recipes, red, and the ghost of one says what it takes.
            Assert.True(build.Toggle());
            var spikes = s.Content.Recipes.First(r => r.Building.Id == "spike_strip");
            int key = s.Content.Recipes.ToList().IndexOf(spikes);
            Assert.True(build.PressKey(key));
            Assert.Equal(OpResult.NeedsBlueprint, b.RecipeState(spikes));
            LookAt(bo, Tx, Tz);
            build.Update(Dt, true, false);
            s.Run(5);
            Assert.Equal(OpResult.NeedsBlueprint, build.Ghost.State);
            Assert.Equal("Spike Strip   ·   Needs the Blueprint: Fortify from the shop", HudText.BuildPrompt(b, build.Ghost, out _));
            Assert.Empty(s.Sim.Constructs.All);

            // Ada buys it at the counter, the tenth card, key 0.
            var po = a.Map.PostOffice;
            s.Put(ada, po.ShopX + 1.0f, po.ShopZ);
            Assert.True(ada.OpenShop());
            var fortify = s.Content.ShopItem("bp_defense");
            int place = a.ShopShelf.ToList().IndexOf(fortify);
            Assert.Equal("Once per run, for the whole team", HudText.ShopNote(a, fortify));
            Assert.Equal(place == 9 ? "[0]" : HudText.CardKey(place), HudText.ShopCard(a, fortify, place)[0].Split(' ')[0]);
            Assert.True(ada.PressShopKey(place));
            s.Run(10);
            Assert.Contains(fortify, b.ShopOwned);
            Assert.Equal("The team owns it", HudText.ShopNote(a, fortify));
            Assert.Contains(boFeed.Entries, e => e.Text == "Blueprint: Fortify: the team can build Stone Wall and Spike Strip now   ·   [B]");

            // Bo's ghost is green now, and the strip goes down.
            build.Update(Dt, false, false);
            Assert.Equal(OpResult.Ok, build.Ghost.State);
            build.Update(Dt, true, false);
            s.Run(10);
            var strip = Assert.Single(b.Constructs.All);
            Assert.Equal("spike_strip", strip.Def.Id);
            Assert.Equal(2, BuildRules.Have(b.Hotbar, b.Inventory, s.Content.Item("iron_ingot")));

            // Walked over by its builder, on both ends alike.
            s.Put(bo, Centre(Tx) - 1.5f, Centre(Tz));
            for (int i = 0; i < 30; i++)
            {
                bo.Input.MoveX = 1f;
                s.Run(1);
            }
            bo.Input.MoveX = 0f;
            s.Run(10);
            Assert.True(b.Local.X > Centre(Tx) + 0.5f, $"stopped at {b.Local.X} by the spikes");
            Assert.True(MathF.Abs(b.Local.X - s.Truth(bo).Move.X) < 0.05f);
        }
    }
}
