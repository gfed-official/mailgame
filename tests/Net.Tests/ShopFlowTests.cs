using System;
using System.Collections.Generic;
using System.IO;
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
    /// The shop (doc 03 §4.4, doc 09 §2.8) as the clients see it over the loopback transport: the
    /// cards go up at a counter that serves, a purchase is everybody's money and everybody's news,
    /// a stack is sold back, and a Bandage is used from the hand.
    /// </summary>
    public class ShopFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        /// <summary>
        /// The real content with a counter the tests can count on: Bandages and the rotating kit
        /// are the only two that can be on special, so they always are; Oil Cans come with shift 2;
        /// the blueprint is the team's once bought.
        /// </summary>
        private static readonly Lazy<ContentDb> Counter = new Lazy<ContentDb>(() =>
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            var source = new MemoryContentSource();
            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                source.Files[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllText(file);
            foreach (string key in source.Files.Keys.Where(k => k.StartsWith("shop/", StringComparison.Ordinal)).ToList())
                source.Files.Remove(key);
            source.Files["shop/shop.json"] = @"[
                { ""id"": ""bandage_x3"", ""name"": ""Bandages ×3"", ""description"": ""Each gives 50 HP back at once."", ""kind"": ""item"", ""price"": 80,
                  ""grants"": { ""item"": ""bandage"", ""count"": 3 }, ""tags"": [""special""] },
                { ""id"": ""plank_x20"", ""name"": ""Planks ×20"", ""description"": ""Sawn timber."", ""kind"": ""item"", ""price"": 140,
                  ""grants"": { ""item"": ""plank"", ""count"": 20 }, ""tags"": [] },
                { ""id"": ""oil_can_x3"", ""name"": ""Oil Cans ×3"", ""description"": ""Three full cans."", ""kind"": ""item"", ""price"": 160,
                  ""grants"": { ""item"": ""oil_can"", ""count"": 3 }, ""availability"": { ""fromShift"": 2, ""slot"": ""fixed"" }, ""tags"": [] },
                { ""id"": ""bp_defense"", ""name"": ""Defense Blueprint"", ""description"": ""Walls that hold."", ""kind"": ""blueprint"", ""price"": 300,
                  ""grants"": { ""blueprint"": ""bp_defense"" }, ""tags"": [""blueprint""] },
                { ""id"": ""bp_fast_belts"", ""name"": ""Fast Belts"", ""description"": ""Twice as fast."", ""kind"": ""blueprint"", ""price"": 500,
                  ""grants"": { ""blueprint"": ""bp_fast_belts"" }, ""availability"": { ""fromShift"": 3, ""slot"": ""fixed"" }, ""tags"": [""blueprint""] },
                { ""id"": ""bp_sorting"", ""name"": ""Sorting"", ""description"": ""Sorters."", ""kind"": ""blueprint"", ""price"": 400,
                  ""grants"": { ""blueprint"": ""bp_sorting"" }, ""availability"": { ""fromShift"": 5, ""slot"": ""fixed"" }, ""tags"": [""blueprint""] },
                { ""id"": ""bp_pipes"", ""name"": ""Pneumatics"", ""description"": ""Pipes."", ""kind"": ""blueprint"", ""price"": 700,
                  ""grants"": { ""blueprint"": ""bp_pipes"" }, ""availability"": { ""fromShift"": 5, ""slot"": ""fixed"" }, ""tags"": [""blueprint""] },
                { ""id"": ""bp_rowboat"", ""name"": ""Rowboat"", ""description"": ""Boats."", ""kind"": ""blueprint"", ""price"": 1,
                  ""grants"": { ""blueprint"": ""bp_rowboat"" }, ""availability"": { ""fromShift"": 1, ""slot"": ""kit"" }, ""tags"": [""blueprint""] },
                { ""id"": ""bp_motorboat"", ""name"": ""Harbour"", ""description"": ""Boats."", ""kind"": ""blueprint"", ""price"": 500,
                  ""grants"": { ""blueprint"": ""bp_motorboat"" }, ""availability"": { ""fromShift"": 5, ""slot"": ""kit"" }, ""tags"": [""blueprint""] },
                { ""id"": ""bp_oil"", ""name"": ""Oil"", ""description"": ""Pumps."", ""kind"": ""blueprint"", ""price"": 600,
                  ""grants"": { ""blueprint"": ""bp_oil"" }, ""availability"": { ""fromShift"": 5, ""slot"": ""kit"" }, ""tags"": [""blueprint""] },
                { ""id"": ""bp_turret"", ""name"": ""Turret"", ""description"": ""Guns."", ""kind"": ""blueprint"", ""price"": 600,
                  ""grants"": { ""blueprint"": ""bp_turret"" }, ""availability"": { ""fromShift"": 5, ""slot"": ""kit"" }, ""tags"": [""blueprint""] },
                { ""id"": ""bp_truck"", ""name"": ""Motor Pool"", ""description"": ""Trucks."", ""kind"": ""blueprint"", ""price"": 500,
                  ""grants"": { ""blueprint"": ""bp_truck"" }, ""availability"": { ""fromShift"": 5, ""slot"": ""fixed"" }, ""tags"": [""blueprint""] },
                { ""id"": ""first_aid_kit"", ""name"": ""First-Aid Kit"", ""description"": ""Five Bandages for the price of four."", ""kind"": ""item"", ""price"": 105,
                  ""grants"": { ""item"": ""bandage"", ""count"": 5 }, ""availability"": { ""fromShift"": 1, ""slot"": ""rotating"" }, ""tags"": [""special""] }
            ]";
            return ContentDb.Load(source);
        });

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly ContentDb Content;
            private uint _nextMailId = 740000;

            public Session(ContentDb? content = null)
            {
                Content = content ?? Counter.Value;
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

            public ShopItemDef Item(string id) => Content.ShopItems.Single(d => d.Id == id);

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

            /// <summary>Puts the client's player <paramref name="off"/> metres out from the shop's counter.</summary>
            public void ToTheCounter(ClientRuntime client, float off = 0.8f)
            {
                var truth = Truth(client);
                truth.Move.X = Sim.Map.PostOffice.ShopX + off;
                truth.Move.Z = Sim.Map.PostOffice.ShopZ;
                Run(8);
            }

            /// <summary>The client puts one letter worth the whole quota into its mailbox.</summary>
            public void MeetTheQuota(ClientRuntime client)
            {
                var home = Sim.Map.Destinations[0];
                var kind = Content.MailKind("letter");
                var item = new MailItem
                {
                    Id = _nextMailId++, Kind = kind.Index, Address = home.Address, Value = (ushort)Sim.Run.Quota,
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

        private static int Units(GridContainer container, ContentDb content, string item)
        {
            int index = content.Item(item).Index, n = 0;
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index) n += e.Stack.Count;
            return n;
        }

        [Fact]
        public void TheCardsGoUpOnlyAtACounterThatServes()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            var interaction = new InteractionController(ada);
            ClientWorld a = ada.World!, b = bo.World!;
            var po = a.Map.PostOffice;
            Assert.True(po.HasShop);

            // The Lobby: a counter, and nobody behind it.
            s.ToTheCounter(ada);
            Assert.False(a.ShopServes);
            Assert.False(ada.OpenShop());
            Assert.Empty(a.ShopSpecials);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.OpenShop, interaction.Prompt.Kind);
            Assert.True(interaction.Prompt.ShopClosed);
            Assert.Equal("Shop   ·   closed — the counter serves in Prep and at Payday", HudText.Prompt(a, interaction.Prompt, out _));
            interaction.Update(Dt, true, false);
            interaction.Update(Dt, false, false);
            Assert.False(a.ShopOpen);
            Assert.Equal("", HudText.ShopText(a));

            // Prep: both are told the specials; the shelf is the fixed stock and the kit that is on special.
            s.ToPrep();
            foreach (var w in new[] { a, b })
            {
                Assert.True(w.ShopServes);
                Assert.Equal(new[] { "bandage_x3", "first_aid_kit" }, w.ShopSpecials.Select(d => d.Id));
                Assert.Equal(s.Sim.ShopSpecials.Select(d => d.Id), w.ShopSpecials.Select(d => d.Id));
                Assert.Equal(new[] { "bandage_x3", "plank_x20", "oil_can_x3", "bp_defense", "bp_fast_belts", "bp_sorting", "bp_pipes", "bp_truck", "first_aid_kit" }, w.ShopShelf.Select(d => d.Id));
            }

            // Out on the street there is no counter to open.
            s.Truth(ada).Move.X = po.ShopX + 8f;
            s.Run(8);
            Assert.False(ada.OpenShop());
            interaction.Update(Dt, false, false);
            Assert.NotEqual(InteractKind.OpenShop, interaction.Prompt.Kind);

            // At it the key puts the cards up, for the one who stands there.
            s.ToTheCounter(ada);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.OpenShop, interaction.Prompt.Kind);
            Assert.False(interaction.Prompt.ShopClosed);
            Assert.Equal("Open Shop  [E]", HudText.Prompt(a, interaction.Prompt, out _));
            interaction.Update(Dt, true, false);
            Assert.True(a.ShopOpen);
            Assert.False(b.ShopOpen);
            interaction.Update(Dt, true, false);
            Assert.True(a.ShopOpen);
            Assert.Equal(InteractKind.CloseShop, interaction.Prompt.Kind);
            Assert.Equal("Close Shop  [E]", HudText.Prompt(a, interaction.Prompt, out _));
            // Released and pressed again: down.
            interaction.Update(Dt, false, false);
            interaction.Update(Dt, true, false);
            Assert.False(a.ShopOpen);
            interaction.Update(Dt, false, false);

            // Between the two counters: the cards shut an open Intake, and an opened Intake takes them down.
            s.Truth(ada).Move.Z = (po.ShopZ + po.IntakeZ) * 0.5f;
            s.Run(8);
            Assert.NotEqual(0u, ada.OpenContainer(a.IntakeContainerId));
            s.Run(5);
            Assert.Equal(a.IntakeContainerId, a.OpenContainerId);
            Assert.True(ada.OpenShop());
            s.Run(5);
            Assert.Equal(0u, a.OpenContainerId);
            Assert.True(a.ShopOpen);
            Assert.NotEqual(0u, ada.OpenContainer(a.IntakeContainerId));
            s.Run(5);
            Assert.Equal(a.IntakeContainerId, a.OpenContainerId);
            Assert.False(a.ShopOpen);
            ada.CloseContainer();
            s.Run(5);

            // The cards stay up a step past the reach they were opened in, and come down beyond it.
            float reach = s.Content.Balance.GetFloat("shopRange");
            s.ToTheCounter(ada);
            Assert.True(ada.OpenShop());
            s.Truth(ada).Move.X = po.ShopX + reach + 0.2f;
            s.Run(8);
            Assert.True(a.ShopOpen);
            s.Truth(ada).Move.X = po.ShopX + reach + 0.7f;
            s.Run(8);
            Assert.False(a.ShopOpen);
            // And do not go up from out there: the slack is for leaving.
            s.Truth(ada).Move.X = po.ShopX + reach + 0.2f;
            s.Run(8);
            Assert.False(ada.OpenShop());

            // The shutters come down with the shift.
            s.ToTheCounter(ada);
            Assert.True(ada.OpenShop());
            s.StartDelivery();
            Assert.False(a.ShopOpen);
            Assert.False(ada.OpenShop());
            Assert.Equal(OpResult.WrongPhase, a.ShopState(s.Item("bandage_x3")));
            Assert.Equal("The counter is closed", HudText.ShopNote(a, s.Item("bandage_x3")));
        }

        private static float ViewYaw(float dx, float dz) => MathF.Atan2(-dx, -dz);

        /// <summary>
        /// Playtest 25fbca5: "Shop prompt only appears from fairly close (~1 m) and aiming at the
        /// counter body". The counter is found from every seat of the spawn pad by turning to it.
        /// </summary>
        [Fact]
        public void TheCounterIsFoundFromThePadByFacingIt()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var interaction = new InteractionController(ada);
            s.ToPrep();
            var a = ada.World!;
            var po = a.Map.PostOffice;
            var truth = s.Truth(ada);
            float shopRange = s.Content.Balance.GetFloat("shopRange");
            Assert.True(shopRange > s.Content.Balance.GetFloat("interactRange"));

            InteractKind At(float x, float z, float yaw, float pitch)
            {
                truth.Move.X = x;
                truth.Move.Z = z;
                s.Run(8);
                ada.Input.YawRadians = yaw;
                ada.Input.PitchRadians = pitch;
                interaction.Update(Dt, false, false);
                return interaction.Prompt.Kind;
            }

            // Every seat of the pad is within the counter's reach: a carrier who has just come to
            // turns to the counter and has its prompt, at eye level, on the sign or on the ground before it.
            for (int seat = 0; seat < 4; seat++)
            {
                float x = po.SpawnPadX + (seat % 2) * 1.0f - 0.5f, z = po.SpawnPadZ + (seat / 2) * 1.0f - 1.5f;
                float dx = po.ShopX - x, dz = po.ShopZ - z;
                Assert.True(dx * dx + dz * dz <= shopRange * shopRange, $"seat {seat} is {MathF.Sqrt(dx * dx + dz * dz):0.00} m from the counter");
                float facing = ViewYaw(dx, dz);
                foreach (float pitch in new[] { 0f, 0.5f, -0.6f, -1.2f })
                    Assert.Equal(InteractKind.OpenShop, At(x, z, facing, pitch));
                // Half a quarter turn off it is still the counter; with the back to it there is none.
                Assert.Equal(InteractKind.OpenShop, At(x, z, facing + 0.6f, 0f));
                Assert.Equal(InteractKind.OpenShop, At(x, z, facing - 0.6f, 0f));
                Assert.NotEqual(InteractKind.OpenShop, At(x, z, facing + MathF.PI, 0f));
                Assert.NotEqual(InteractKind.OpenShop, At(x, z, facing + 1.6f, 0f));
            }

            // The report's seat: facing the Intake it is the Intake, turned to the counter the shop.
            float sx = po.SpawnPadX - 0.5f, sz = po.SpawnPadZ - 1.5f;
            Assert.Equal(InteractKind.OpenIntake, At(sx, sz, ViewYaw(po.IntakeX - sx, po.IntakeZ - sz), 0f));
            Assert.Equal(InteractKind.OpenShop, At(sx, sz, ViewYaw(po.ShopX - sx, po.ShopZ - sz), 0f));
            interaction.Update(Dt, true, false);
            Assert.True(a.ShopOpen);
            interaction.Update(Dt, false, false);
            ada.CloseShop();

            // From the far end of its reach, looking a good way beside the counter's body.
            Assert.Equal(InteractKind.OpenShop, At(po.ShopX + shopRange - 0.1f, po.ShopZ, ViewYaw(-1f, 0.3f), -0.1f));
            // Past it there is no counter, however well it is aimed at.
            Assert.Equal(InteractKind.None, At(po.ShopX + shopRange + 0.3f, po.ShopZ, ViewYaw(-1f, 0f), -0.2f));

            // What the crosshair is on comes first: the Intake looked at from between the two is the Intake.
            float mz = (po.IntakeZ + po.ShopZ) * 0.5f, mx = po.IntakeX + 1.5f;
            truth.Move.X = mx;
            truth.Move.Z = mz;
            s.Run(8);
            ViewRay.Aim(a.Local, po.IntakeX, a.Local.Y + 1.0f, po.IntakeZ, out float yaw, out float pitchToIntake);
            Assert.Equal(InteractKind.OpenIntake, At(mx, mz, yaw, pitchToIntake));
            ViewRay.Aim(a.Local, po.ShopX, a.Local.Y + 1.0f, po.ShopZ, out yaw, out float pitchToShop);
            Assert.Equal(InteractKind.OpenShop, At(mx, mz, yaw, pitchToShop));

            // A counter that is shut says so to whoever looks at it, and to nobody who only faces its way.
            s.StartDelivery();
            float fx = po.ShopX + 3.0f, fz = po.ShopZ;
            Assert.Equal(InteractKind.None, At(fx, fz, ViewYaw(-1f, 0f), 1.2f));
            truth.Move.X = fx;
            s.Run(8);
            ViewRay.Aim(a.Local, po.ShopX, a.Local.Y + 1.0f, po.ShopZ, out yaw, out pitchToShop);
            Assert.Equal(InteractKind.OpenShop, At(fx, fz, yaw, pitchToShop));
            Assert.True(interaction.Prompt.ShopClosed);
        }

        /// <summary>
        /// Playtest 25fbca5: "buying at Payday (only 16-20 s window, host spawned facing the Intake
        /// and E opened the Intake instead)". At Payday the shop key puts the cards up wherever
        /// the carrier stands, and what they buy there is theirs.
        /// </summary>
        [Fact]
        public void AtPaydayTheShopKeyServesWhereTheCarrierStands()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            var interaction = new InteractionController(ada);
            s.ToPrep();
            ClientWorld a = ada.World!, b = bo.World!;
            var po = a.Map.PostOffice;
            s.Sim.Economy.AddLoot(1000);

            // Prep: the key wants the carrier at the counter, and asks for no aim there.
            Assert.DoesNotContain(HudText.ShopKey, HudText.PhaseHint(a));
            s.Truth(ada).Move.X = po.ShopX + 9f;
            s.Run(8);
            Assert.False(ada.ToggleShop());
            Assert.False(a.ShopOpen);
            s.ToTheCounter(ada, 3.0f);
            ada.Input.YawRadians = ViewYaw(1f, 0f);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.None, interaction.Prompt.Kind);
            Assert.True(ada.ToggleShop());
            Assert.True(a.ShopOpen);
            Assert.True(ada.ToggleShop());
            Assert.False(a.ShopOpen);

            // Delivery: no counter, no key.
            s.StartDelivery();
            Assert.False(ada.ToggleShop());
            s.MeetTheQuota(ada);
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Payday, 330));
            s.Run(5);

            // Payday finds Ada at the mailbox across the street and Bo wherever the shift left him.
            var truth = s.Truth(ada);
            float dx = truth.Move.X - po.ShopX, dz = truth.Move.Z - po.ShopZ;
            Assert.True(dx * dx + dz * dz > 10f * 10f);
            Assert.StartsWith("Quota met — shift 2 in 00:", HudText.PhaseHint(a));
            Assert.EndsWith($"   ·   {HudText.ShopKey} shop", HudText.PhaseHint(a));
            interaction.Update(Dt, false, false);
            Assert.NotEqual(InteractKind.OpenShop, interaction.Prompt.Kind);
            int wallet = a.Global.Wallet;
            Assert.True(ada.ToggleShop());
            Assert.True(a.ShopOpen);
            Assert.False(b.ShopOpen);
            // The cards are up: interact takes them down, wherever that is.
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.CloseShop, interaction.Prompt.Kind);
            Assert.True(ada.PressShopKey(0));
            s.Run(10);
            Assert.Equal(wallet - a.ShopPrice(s.Item("bandage_x3")), a.Global.Wallet);
            Assert.Equal(3, Units(a.Hotbar, s.Content, "bandage"));
            // Walking on does not take them down, as it would at the counter.
            truth.Move.X += 6f;
            s.Run(8);
            Assert.True(a.ShopOpen);
            // A stack is sold from out there too.
            var stack = a.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Item);
            Assert.NotEqual(0u, ada.Sell(a.HotbarId, stack.EntryId, 1));
            s.Run(10);
            Assert.Equal(2, Units(a.Hotbar, s.Content, "bandage"));
            interaction.Update(Dt, true, false);
            Assert.False(a.ShopOpen);
            interaction.Update(Dt, false, false);
            Assert.True(bo.ToggleShop());
            Assert.NotEqual(0u, bo.Buy(s.Item("plank_x20")));
            s.Run(10);
            Assert.Equal(20, Units(b.Inventory, s.Content, "plank"));

            // The next Prep the cards come down with the Payday for whoever is not at the counter.
            Assert.True(ada.ToggleShop());
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Prep, 120));
            s.Run(10);
            Assert.False(a.ShopOpen);
            Assert.False(ada.ToggleShop());
            // Bo never left the pad before the counter: his stay up.
            Assert.True(b.ShopOpen);
        }

        [Fact]
        public void ACardIsBoughtWithTheTeamsMoney_AndEverybodySeesIt()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            EventFeed adaFeed = new EventFeed(), boFeed = new EventFeed();
            ada.EventReceived += e => adaFeed.OnEvent(ada.World!, e);
            bo.EventReceived += e => boFeed.OnEvent(bo.World!, e);
            s.ToPrep();
            ClientWorld a = ada.World!, b = bo.World!;
            ShopItemDef bandages = s.Item("bandage_x3"), planks = s.Item("plank_x20"), oil = s.Item("oil_can_x3"),
                blueprint = s.Item("bp_defense"), kit = s.Item("first_aid_kit");
            Assert.Contains("On special at the shop: Bandages ×3, First-Aid Kit", adaFeed.Entries.Select(e => e.Text));

            // Nothing is bought through cards that are not up.
            Assert.False(ada.PressShopKey(0));
            Assert.Equal(0u, ada.Buy(bandages));
            s.ToTheCounter(ada);
            Assert.True(ada.OpenShop());

            // A run starts with an empty wallet: every card says so, but the one that is not sold yet.
            Assert.Equal(OpResult.CantAfford, a.ShopState(bandages));
            Assert.Equal(OpResult.ShopLocked, a.ShopState(oil));
            Assert.Equal("Not enough gold", HudText.ShopNote(a, bandages));
            Assert.Equal("From shift 2", HudText.ShopNote(a, oil));
            Assert.True(ada.PressShopKey(0));
            Assert.False(a.IsBuying(bandages));
            Assert.False(ada.PressShopKey(9));
            Assert.False(ada.PressShopKey(-1));

            s.Sim.Economy.AddLoot(1000);
            s.Run(5);
            Assert.Equal(1000, a.Global.Wallet);
            Assert.Equal("SHOP   ·   team wallet 1000 g", HudText.ShopTitle(a));
            Assert.Equal(new[] { "[1]  Bandages ×3", "SPECIAL   ·   64 g  (was 80 g)", "Each gives 50 HP back at once.", "" }, HudText.ShopCard(a, bandages, 0));
            Assert.Equal(new[] { "[2]  Planks ×20", "140 g", "Sawn timber.", "" }, HudText.ShopCard(a, planks, 1));
            Assert.Equal(new[] { "[4]  Defense Blueprint", "300 g", "Walls that hold.", "Once per run, for the whole team" }, HudText.ShopCard(a, blueprint, 3));
            // A rotating entry is on the counter because it is a special; its price is its price.
            Assert.Equal(new[] { "[5]  First-Aid Kit", "SPECIAL   ·   105 g", "Five Bandages for the price of four.", "" }, HudText.ShopCard(a, kit, 4));
            string text = HudText.ShopText(a);
            Assert.StartsWith("SHOP   ·   team wallet 1000 g\n[1]  Bandages ×3\n", text);
            Assert.EndsWith("\n" + HudText.ShopHint, text);
            Assert.Equal(1 + 3 + 3 + 4 + 4 + 4 + 4 + 4 + 4 + 3 + 1, text.Split('\n').Length);
            Assert.Equal("", HudText.ShopText(b));

            // The key of a card buys it: the card waits for the server, a second press asks nothing more.
            Assert.True(ada.PressShopKey(0));
            Assert.True(a.IsBuying(bandages));
            Assert.Equal("On its way", HudText.ShopNote(a, bandages));
            Assert.Equal(0u, ada.Buy(bandages));
            s.Run(10);
            Assert.False(a.IsBuying(bandages));
            Assert.Equal(936, s.Sim.Economy.Wallet);
            Assert.Equal(936, a.Global.Wallet);
            Assert.Equal(936, b.Global.Wallet);
            Assert.Equal(3, Units(a.Hotbar, s.Content, "bandage"));
            Assert.Equal(0, Units(a.Inventory, s.Content, "bandage"));
            Assert.Contains("You bought Bandages ×3  -64 g", adaFeed.Entries.Select(e => e.Text));
            Assert.Contains("Ada bought Bandages ×3  -64 g", boFeed.Entries.Select(e => e.Text));

            // Materials go to the bag.
            Assert.NotEqual(0u, ada.Buy(planks));
            s.Run(10);
            Assert.Equal(20, Units(a.Inventory, s.Content, "plank"));
            Assert.Equal(796, a.Global.Wallet);

            // The blueprint is the team's from the moment one of them buys it.
            Assert.Equal(OpResult.Ok, b.ShopState(blueprint));
            Assert.True(ada.PressShopKey(3));
            s.Run(10);
            Assert.Equal(496, a.Global.Wallet);
            foreach (var w in new[] { a, b })
            {
                Assert.Equal(new[] { "bp_defense" }, w.ShopOwned.Select(d => d.Id));
                Assert.Equal(OpResult.AlreadyOwned, w.ShopState(blueprint));
                Assert.Equal("The team owns it", HudText.ShopNote(w, blueprint));
            }
            Assert.Equal(0u, ada.Buy(blueprint));
            s.ToTheCounter(bo, 1.2f);
            Assert.True(bo.OpenShop());
            Assert.Equal(0u, bo.Buy(blueprint));
            Assert.True(s.Sim.OwnsBlueprint("bp_defense"));

            // Pockets that are full take no more.
            var truth = s.Truth(bo);
            var plank = s.Content.Item("plank");
            while (s.Sim.Give(truth.Inventory, ItemStack.OfItem(plank, plank.MaxStack))) { }
            while (s.Sim.Give(truth.Hotbar, ItemStack.OfItem(plank, plank.MaxStack))) { }
            s.Run(5);
            Assert.Equal(OpResult.DoesNotFit, b.ShopState(kit));
            Assert.Equal("No room in your pockets", HudText.ShopNote(b, kit));
            Assert.Equal(0u, bo.Buy(kit));
            Assert.Equal(OpResult.Ok, a.ShopState(kit));

            // Who joins now is told what is on special and what the team owns.
            var cy = s.Join("Cy");
            Assert.Equal(new[] { "bandage_x3", "first_aid_kit" }, cy.World!.ShopSpecials.Select(d => d.Id));
            Assert.Equal(new[] { "bp_defense" }, cy.World.ShopOwned.Select(d => d.Id));
        }

        [Fact]
        public void AStackIsSoldOverTheCounter_AndABandageIsUsedFromTheHand()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var feed = new EventFeed();
            ada.EventReceived += e => feed.OnEvent(ada.World!, e);
            ada.RequestCompleted += feed.OnRequest;
            s.ToPrep();
            var a = ada.World!;
            var truth = s.Truth(ada);
            s.Sim.Economy.AddLoot(100);
            s.ToTheCounter(ada);
            Assert.True(ada.OpenShop());
            Assert.NotEqual(0u, ada.Buy(s.Item("bandage_x3")));
            s.Run(10);
            Assert.Equal(36, a.Global.Wallet);
            var stack = a.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Item);
            int slot = stack.X;
            Assert.True(slot > 0);

            // One of them back over the counter, at what the shop pays for it.
            Assert.NotEqual(0u, ada.Sell(a.HotbarId, stack.EntryId, 1));
            s.Run(10);
            Assert.Equal(36 + 7, a.Global.Wallet);
            Assert.Equal(2, Units(a.Hotbar, s.Content, "bandage"));
            Assert.Contains("You sold 1 Bandage  +7 g", feed.Entries.Select(e => e.Text));
            // A sale is the team's money, not the shift's earnings.
            Assert.Equal(0, s.Sim.Economy.ShiftEarnings);

            // The hand: nothing to use where nothing is held, and nothing to heal at full health.
            ada.Input.HeldSlot = 0;
            Assert.Null(a.UsableAt(0));
            Assert.Equal(0u, ada.UseHeld());
            ada.Input.HeldSlot = (byte)slot;
            s.Run(3);
            Assert.NotNull(a.UsableAt(slot));
            Assert.Equal("2 Bandages   ·   [Q] use", HudText.Held(a, a.Hotbar.EntryAt(slot, 0)!.Stack));
            Assert.NotEqual(0u, ada.UseHeld());
            s.Run(10);
            Assert.Equal(2, Units(a.Hotbar, s.Content, "bandage"));
            Assert.Contains("You are at full health", feed.Entries.Select(e => e.Text));

            // Hurt, it gives 50 HP back at once — at the counter or anywhere, in any phase.
            ada.CloseShop();
            Assert.Equal(0u, ada.Sell(a.HotbarId, stack.EntryId, 1));
            s.StartDelivery();
            truth.Hp = truth.MaxHp - 60f;
            s.Run(3);
            Assert.NotEqual(0u, ada.UseHeld());
            s.Run(10);
            Assert.Equal(1, Units(a.Hotbar, s.Content, "bandage"));
            Assert.InRange(truth.Hp, truth.MaxHp - 10.5f, truth.MaxHp - 9.5f);
            Assert.InRange(a.LocalHp, truth.MaxHp - 11.5f, truth.MaxHp - 8.5f);
            Assert.Equal("1 Bandage   ·   [Q] use", HudText.Held(a, a.Hotbar.EntryAt(slot, 0)!.Stack));

            // Mail in the hand is not something to use.
            var home = s.Sim.Map.Destinations[0];
            var letter = new MailItem { Id = 741000, Kind = s.Content.MailKind("letter").Index, Address = home.Address, Value = 10, SpawnShift = 1, DeadlineShift = 1 };
            Assert.True(s.Sim.Give(truth.Hotbar, ItemStack.OfMail(letter)));
            s.Run(5);
            var mail = a.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            Assert.Null(a.UsableAt(mail.X));
            Assert.DoesNotContain("[Q]", HudText.Held(a, mail.Stack));
        }

        [Fact]
        public void TheNextShiftHasItsOwnSpecials_AndTheLobbyClearsTheCounter()
        {
            var s = new Session();
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            var offers = new List<ShopOfferEvent>();
            bo.EventReceived += e => { if (e is ShopOfferEvent o) offers.Add(o); };
            s.ToPrep();
            ClientWorld a = ada.World!, b = bo.World!;
            Assert.Single(offers);
            s.Sim.Economy.AddLoot(2000);
            s.ToTheCounter(ada);
            Assert.True(ada.OpenShop());
            Assert.NotEqual(0u, ada.Buy(s.Item("bp_defense")));
            s.Run(10);
            Assert.Single(b.ShopOwned);

            // Payday: the counter serves again, with the specials of the shift that was.
            s.StartDelivery();
            s.MeetTheQuota(ada);
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Payday, 330));
            s.ToTheCounter(ada);
            Assert.True(a.ShopServes);
            Assert.True(ada.OpenShop());
            Assert.Equal(OpResult.ShopLocked, a.ShopState(s.Item("oil_can_x3")));
            Assert.NotEqual(0u, ada.Buy(s.Item("first_aid_kit")));
            s.Run(10);
            Assert.Equal(5, Units(a.Hotbar, s.Content, "bandage"));

            // The second Prep: a new offer, the Oil Cans are sold now, the blueprint is still the team's.
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Prep, 120));
            s.Run(10);
            Assert.Equal(2, offers.Count);
            Assert.Equal(2, offers[1].Shift);
            Assert.Equal(s.Sim.ShopSpecials.Select(d => d.Id), b.ShopSpecials.Select(d => d.Id));
            s.ToTheCounter(ada);
            Assert.True(ada.OpenShop());
            Assert.Equal(OpResult.Ok, a.ShopState(s.Item("oil_can_x3")));
            Assert.Equal(OpResult.AlreadyOwned, a.ShopState(s.Item("bp_defense")));

            // Nobody delivers in the second shift: the run is over, and the Lobby's counter is bare.
            s.StartDelivery();
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Results, 330));
            s.Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            s.Run(10);
            Assert.Equal(RunPhase.Lobby, s.Sim.Run.Phase);
            foreach (var w in new[] { a, b })
            {
                Assert.Empty(w.ShopSpecials);
                Assert.Empty(w.ShopOwned);
                Assert.False(w.ShopOpen);
                Assert.False(w.ShopServes);
            }
        }
    }
}
