using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The Draft (doc 01 §2.5) as the clients see it over the loopback transport: one table for
    /// everybody, a pick seen by all, and the table as it stands for whoever comes back to it.
    /// </summary>
    public class DraftFlowTests
    {
        private const ulong AdaAccount = 0xADA, BoAccount = 0xB0, CyAccount = 0xC1;

        /// <summary>The real content with three perks: two for the team and one that halves the hold at a mailbox.</summary>
        private static readonly Lazy<ContentDb> ThreePerks = new Lazy<ContentDb>(() =>
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            var source = new MemoryContentSource();
            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                source.Files[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllText(file);
            foreach (string key in source.Files.Keys.Where(k => k.StartsWith("perks/", StringComparison.Ordinal)).ToList())
                source.Files.Remove(key);
            source.Files["perks/perks.json"] = "[" + string.Join(",",
                Perk("rep", "team", "{ \"stat\": \"Quota\", \"op\": \"mul\", \"value\": 0.9 }"),
                Perk("stamps", "team", "{ \"stat\": \"PaperMailValue\", \"op\": \"mul\", \"value\": 1.25 }"),
                Perk("hands", "personal", "{ \"stat\": \"InsertHold\", \"op\": \"mul\", \"value\": 0.5 }")) + "]";
            return ContentDb.Load(source);
        });

        private static string Perk(string id, string scope, string modifier) =>
            $"{{ \"id\": \"{id}\", \"name\": \"{id}\", \"description\": \"-\", \"category\": \"carrier\", \"scope\": \"{scope}\", "
            + $"\"rarity\": \"common\", \"modifiers\": [{modifier}] }}";

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly List<DeliveryBot> Bots = new List<DeliveryBot>();
            public readonly ContentDb Content;
            private uint _nextMailId = 730000;

            public Session(ContentDb? content = null)
            {
                Content = content ?? Shared.Content;
                Server = new ServerRuntime(Content, new SimSettings { Seed = 3, Raids = false }, Hub);
            }

            public SimWorld Sim => Server.World;

            public ClientRuntime Join(string name, ulong account = 0, bool bot = false)
            {
                var client = new ClientRuntime(Content, Hub.CreateClient(), name, PlaytestFlags.None, 0, account);
                Clients.Add(client);
                if (bot) Bots.Add(new DeliveryBot(client));
                Run(10);
                return client;
            }

            /// <summary>The client's connection is gone: nothing says goodbye.</summary>
            public void Drop(ClientRuntime client)
            {
                client.Disconnect();
                Clients.Remove(client);
                Run(10);
            }

            public void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var b in Bots) b.Tick();
                    foreach (var c in Clients) c.Tick();
                    Server.Tick();
                }
            }

            public bool RunUntil(Func<bool> done, int maxSeconds)
            {
                for (int i = 0; i < maxSeconds * 30 && !done(); i++) Run(1);
                return done();
            }

            /// <summary>Lobby or Prep → Delivery with everybody's go key.</summary>
            public void StartDelivery()
            {
                if (Sim.Run.Phase == RunPhase.Lobby)
                {
                    Assert.NotEqual(0u, Clients.Single(c => c.World!.IsHost).PressGo());
                    Run(10);
                }
                foreach (var c in Clients) Assert.NotEqual(0u, c.PressGo());
                Run(10);
                Assert.Equal(RunPhase.Delivery, Sim.Run.Phase);
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
                var truth = Sim.GetPlayer(client.World!.LocalId)!;
                Assert.True(Sim.Give(truth.Hotbar, ItemStack.OfMail(item)));
                truth.Move.X = home.MailboxX;
                truth.Move.Z = home.MailboxZ;
                Run(20);
                var entry = truth.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
                Assert.NotEqual(0u, client.Deliver(truth.Hotbar.Id, entry.EntryId, home.Id));
                Run(10);
                Assert.True(Sim.Economy.ShiftEarnings >= Sim.Run.Quota);
            }

            /// <summary>The shift at hand, paid, to the Draft after its Payday.</summary>
            public void ToTheDraft(ClientRuntime payer)
            {
                StartDelivery();
                MeetTheQuota(payer);
                Assert.True(RunUntil(() => Sim.Run.Phase == RunPhase.Draft, 330));
                Run(10);
            }

            public int Card(string perk) => Sim.DraftOffer.ToList().FindIndex(p => p.Id == perk);
        }

        [Fact]
        public void TheTableIsTheSameForEverybody_AndAPickIsSeenByAll()
        {
            var s = new Session(ThreePerks.Value);
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            Assert.Equal(0u, ada.PickPerk(0));
            s.ToTheDraft(ada);

            var offer = s.Sim.DraftOffer.Select(p => p.Id).ToArray();
            Assert.Equal(3, offer.Length);
            foreach (var c in new[] { ada, bo })
            {
                var world = c.World!;
                Assert.Equal(RunPhase.Draft, world.Global.Phase);
                Assert.Equal(1, world.Global.Shift);
                Assert.True(world.PhaseHasClock);
                Assert.InRange(world.PhaseSecondsLeft, 28.0, 30.0);
                Assert.Equal(offer, world.DraftOffer.Select(p => p.Id));
                Assert.Equal(-1, world.LocalDraftPick);
                Assert.Equal(0, world.FirstOpenCard);
                Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(OpResult.Ok, world.CardState(i)));
                Assert.Equal(OpResult.BadRequest, world.CardState(3));
            }

            // Ada takes a team card: it is the team's at once, and no card for Bo any more.
            int rep = s.Card("rep"), stamps = s.Card("stamps"), hands = s.Card("hands");
            Assert.Equal(0u, ada.PickPerk(3));
            Assert.NotEqual(0u, ada.PickPerk(rep));
            s.Run(10);
            foreach (var c in new[] { ada, bo })
            {
                var world = c.World!;
                Assert.Equal(new[] { "rep" }, world.TeamPerks.Select(p => p.Id));
                Assert.Equal(rep, world.Players[ada.World!.LocalId].DraftPick);
                Assert.Equal(-1, world.Players[bo.World!.LocalId].DraftPick);
                Assert.Empty(world.LocalPerks);
            }
            Assert.Equal(rep, ada.World!.LocalDraftPick);
            Assert.Equal(OpResult.AlreadyPicked, ada.World.CardState(stamps));
            Assert.Equal(0u, ada.PickPerk(stamps));
            Assert.Equal(-1, ada.World.FirstOpenCard);
            Assert.Equal(OpResult.PerkTaken, bo.World!.CardState(rep));
            Assert.Equal(0u, bo.PickPerk(rep));
            Assert.Equal(RunPhase.Draft, s.Sim.Run.Phase);

            // Bo takes the personal card: theirs alone, and the hold at a mailbox is half as long for them.
            Assert.Equal(0.4f, bo.World.LocalStat(Stat.InsertHold, 0.4f), 4);
            Assert.NotEqual(0u, bo.PickPerk(hands));
            s.Run(10);
            Assert.Equal(new[] { "hands" }, bo.World.LocalPerks.Select(p => p.Id));
            Assert.Equal(0.2f, bo.World.LocalStat(Stat.InsertHold, 0.4f), 4);
            Assert.Equal(0.4f, ada.World.LocalStat(Stat.InsertHold, 0.4f), 4);
            Assert.Equal(new[] { "hands" }, ada.World.Players[bo.World.LocalId].Perks.Select(p => p.Id));
            Assert.Empty(ada.World.LocalPerks);

            // Everybody has chosen: the next shift's Prep, the table is cleared, the perks stay.
            foreach (var c in new[] { ada, bo })
            {
                var world = c.World!;
                Assert.Equal((RunPhase.Prep, 2), (world.Global.Phase, (int)world.Global.Shift));
                Assert.Empty(world.DraftOffer);
                Assert.Equal(-1, world.FirstOpenCard);
                Assert.All(world.Players.Values, p => Assert.Equal(-1, p.DraftPick));
                Assert.Equal(new[] { "rep" }, world.TeamPerks.Select(p => p.Id));
            }
            Assert.Equal(0u, bo.PickPerk(0));
            // Union Rep's part: the quota the clients are shown is the lowered one.
            s.StartDelivery();
            s.Run(10);
            Assert.Equal((int)Math.Round(QuotaFormula.Quota(s.Content.Balance, 2, 2) * 0.9), ada.World.Global.Quota);
        }

        [Fact]
        public void WhoComesBackFindsTheTableAsItStands_AndTheirPerksWithThem()
        {
            var s = new Session(ThreePerks.Value);
            var ada = s.Join("Ada", AdaAccount);
            var bo = s.Join("Bo", BoAccount);
            var cy = s.Join("Cy", CyAccount);
            var boId = bo.World!.LocalId;
            s.StartDelivery();
            s.MeetTheQuota(ada);
            // The grace is two minutes: Bo drops out late enough to be still away at the Draft.
            Assert.True(s.RunUntil(() => s.Sim.Run.PhaseEndTick - s.Sim.Tick < 60 * 30, 240));
            s.Drop(bo);
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Draft, 90));
            s.Run(10);
            int rep = s.Card("rep"), stamps = s.Card("stamps"), hands = s.Card("hands");
            Assert.NotEqual(0u, ada.PickPerk(rep));
            s.Run(10);

            bo = s.Join("Bo", BoAccount);
            var world = bo.World!;
            Assert.Equal(boId, world.LocalId);
            Assert.Equal(RunPhase.Draft, world.Global.Phase);
            Assert.Equal(s.Sim.DraftOffer.Select(p => p.Id), world.DraftOffer.Select(p => p.Id));
            Assert.Equal(new[] { "rep" }, world.TeamPerks.Select(p => p.Id));
            Assert.Equal(rep, world.Players[ada.World!.LocalId].DraftPick);
            Assert.Equal(OpResult.PerkTaken, world.CardState(rep));
            Assert.Equal(-1, world.LocalDraftPick);

            Assert.NotEqual(0u, bo.PickPerk(hands));
            s.Run(10);
            Assert.Equal(RunPhase.Draft, s.Sim.Run.Phase);
            Assert.NotEqual(0u, cy.PickPerk(stamps));
            s.Run(10);
            Assert.Equal(RunPhase.Prep, s.Sim.Run.Phase);

            // Out again and back in the next Delivery: the Welcome says what the run has and what is theirs.
            s.StartDelivery();
            s.Drop(bo);
            bo = s.Join("Bo", BoAccount);
            world = bo.World!;
            Assert.Equal(boId, world.LocalId);
            Assert.Equal(new[] { "rep", "stamps" }, world.TeamPerks.Select(p => p.Id));
            Assert.Equal(new[] { "hands" }, world.LocalPerks.Select(p => p.Id));
            Assert.Equal(0.2f, world.LocalStat(Stat.InsertHold, 0.4f), 4);
            Assert.Empty(world.DraftOffer);
            Assert.Empty(world.Players[ada.World.LocalId].Perks);
        }

        [Fact]
        public void TheRunEndsWithoutPerks_OnEveryClient()
        {
            var s = new Session(ThreePerks.Value);
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            s.ToTheDraft(ada);
            Assert.NotEqual(0u, ada.PickPerk(s.Card("rep")));
            Assert.NotEqual(0u, bo.PickPerk(s.Card("hands")));
            s.Run(10);
            Assert.Equal(RunPhase.Prep, s.Sim.Run.Phase);

            // Nobody delivers in the second shift: the run is over, and back in the Lobby nothing is left of it.
            s.StartDelivery();
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Results, 330));
            s.Run(10);
            Assert.Single(ada.World!.TeamPerks);
            Assert.Single(bo.World!.LocalPerks);
            Assert.NotEqual(0u, ada.PressGo());
            s.Run(10);
            Assert.Equal(RunPhase.Lobby, s.Sim.Run.Phase);
            foreach (var c in new[] { ada, bo })
            {
                Assert.Empty(c.World!.TeamPerks);
                Assert.All(c.World.Players.Values, p => Assert.Empty(p.Perks));
                Assert.Equal(0.4f, c.World.LocalStat(Stat.InsertHold, 0.4f), 4);
            }
        }

        [Fact]
        public void ABotTakesACard_AndOneThatDoesNotIsGivenOneByTheClock()
        {
            var s = new Session();
            var picker = s.Join("Picker", bot: true);
            var idler = s.Join("Idler", bot: true);
            s.Bots[1].PicksPerks = false;
            var auto = new List<PerkPickedEvent>();
            idler.EventReceived += e => { if (e is PerkPickedEvent p) auto.Add(p); };

            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Draft, 400), $"the bots got to {s.Sim.Run.Phase} of shift {s.Sim.Run.Shift}");
            uint end = s.Sim.Run.PhaseEndTick;
            s.Run(60);
            var truth = s.Sim.GetPlayer(picker.World!.LocalId)!;
            Assert.True(truth.DraftPick >= 0);
            Assert.Equal(truth.DraftPick, idler.World!.Players[truth.Id].DraftPick);
            Assert.Equal(-1, idler.World.LocalDraftPick);
            // The Draft waits for the idler until its clock is out.
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase != RunPhase.Draft, 31));
            Assert.InRange((long)s.Sim.Tick, end, end + 1);
            s.Run(10);
            Assert.Equal(2, auto.Count);
            Assert.Equal((picker.World.LocalId, false), (auto[0].Player, auto[0].Auto));
            Assert.Equal((idler.World.LocalId, true), (auto[1].Player, auto[1].Auto));
            Assert.Equal(2, s.Sim.TeamPerks.Count + s.Sim.Players.Sum(p => p.Perks.Count));
            Assert.Equal(s.Sim.TeamPerks.Select(p => p.Id), idler.World.TeamPerks.Select(p => p.Id));
            Assert.Equal(s.Sim.GetPlayer(idler.World.LocalId)!.Perks.Select(p => p.Id), idler.World.LocalPerks.Select(p => p.Id));

            // The feed tells a card taken from a card given.
            PerkDef taken = s.Content.Perks[auto[0].Perk], given = s.Content.Perks[auto[1].Perk];
            Assert.Equal($"Picker took {taken.Name}{(taken.Team ? " for the team" : "")}", HudText.PerkPicked(idler.World, auto[0].Player, taken, auto[0].Auto));
            Assert.Equal($"You were given {given.Name}{(given.Team ? " for the team" : "")}", HudText.PerkPicked(idler.World, auto[1].Player, given, auto[1].Auto));
            Assert.Equal($"Idler was given {given.Name}{(given.Team ? " for the team" : "")}", HudText.PerkPicked(picker.World, auto[1].Player, given, auto[1].Auto));
        }

        /// <summary>
        /// Cards with a face: a team perk, a personal one that stacks, one that is held once, and
        /// Heavy Boots, which never go with Long Legs and are never dealt (the test hands them out).
        /// </summary>
        private static readonly Lazy<ContentDb> CardFaces = new Lazy<ContentDb>(() =>
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            var source = new MemoryContentSource();
            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                source.Files[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllText(file);
            foreach (string key in source.Files.Keys.Where(k => k.StartsWith("perks/", StringComparison.Ordinal)).ToList())
                source.Files.Remove(key);
            source.Files["perks/perks.json"] = @"[
                { ""id"": ""rep"", ""name"": ""Union Rep"", ""description"": ""Quota -10%."", ""category"": ""postal"", ""scope"": ""team"", ""rarity"": ""uncommon"",
                  ""modifiers"": [ { ""stat"": ""Quota"", ""op"": ""mul"", ""value"": 0.9 } ] },
                { ""id"": ""legs"", ""name"": ""Long Legs"", ""description"": ""+12% move speed."", ""category"": ""carrier"", ""scope"": ""personal"", ""rarity"": ""common"",
                  ""modifiers"": [ { ""stat"": ""PlayerSpeed"", ""op"": ""mul"", ""value"": 1.12 } ], ""maxStacks"": 3 },
                { ""id"": ""hands"", ""name"": ""Quick Hands"", ""description"": ""Mailbox insert hold -50%."", ""category"": ""carrier"", ""scope"": ""personal"", ""rarity"": ""rare"",
                  ""modifiers"": [ { ""stat"": ""InsertHold"", ""op"": ""mul"", ""value"": 0.5 } ] },
                { ""id"": ""boots"", ""name"": ""Heavy Boots"", ""description"": ""Weight speed penalty -50%."", ""category"": ""defense"", ""scope"": ""personal"", ""rarity"": ""common"",
                  ""modifiers"": [ { ""stat"": ""WeightPenalty"", ""op"": ""mul"", ""value"": 0.5 } ], ""prerequisites"": { ""shiftMin"": 9 }, ""excludes"": [""legs""] }
            ]";
            return ContentDb.Load(source);
        });

        [Fact]
        public void ACardSaysWhatItIs_WhoTookIt_AndWhatKeepsItFromYou()
        {
            var s = new Session(CardFaces.Value);
            var ada = s.Join("Ada");
            var bo = s.Join("Bo");
            var adaFeed = new EventFeed();
            ada.EventReceived += e => adaFeed.OnEvent(ada.World!, e);
            Assert.False(ada.PressCardKey(0));
            Assert.Equal("", HudText.DraftText(ada.World!));
            Assert.Equal("", HudText.Perks(ada.World!));
            Assert.Equal("", HudText.PerkTracker(ada.World!));

            // Before the Draft Ada has Long Legs once; Bo has the boots that exclude them, and Quick Hands.
            s.StartDelivery();
            SimWorld sim = s.Sim;
            Player adaTruth = sim.GetPlayer(ada.World!.LocalId)!, boTruth = sim.GetPlayer(bo.World!.LocalId)!;
            Assert.Equal(OpResult.Ok, sim.GrantPerk(adaTruth, s.Content.Perk("legs")));
            Assert.Equal(OpResult.Ok, sim.GrantPerk(boTruth, s.Content.Perk("boots")));
            Assert.Equal(OpResult.Ok, sim.GrantPerk(boTruth, s.Content.Perk("hands")));
            s.MeetTheQuota(ada);
            Assert.True(s.RunUntil(() => sim.Run.Phase == RunPhase.Draft, 330));
            s.Run(10);
            int rep = s.Card("rep"), legs = s.Card("legs"), hands = s.Card("hands");
            Assert.True(rep >= 0 && legs >= 0 && hands >= 0);
            ClientWorld a = ada.World, b = bo.World;

            Assert.Equal("DRAFT   ·   one perk each before shift 2", HudText.DraftTitle(a));
            Assert.Equal(new[] { $"[{rep + 1}]  Union Rep", "POSTAL   ·   UNCOMMON", "Quota -10%.", "Team perk", "" }, HudText.DraftCard(a, rep));
            Assert.Equal(new[] { $"[{legs + 1}]  Long Legs", "CARRIER   ·   COMMON", "+12% move speed.", "Personal perk   ·   stacks to 3   ·   never with Heavy Boots", "You hold 1 of 3" },
                HudText.DraftCard(a, legs));
            Assert.Equal(new[] { $"[{hands + 1}]  Quick Hands", "CARRIER   ·   RARE", "Mailbox insert hold -50%.", "Personal perk", "" }, HudText.DraftCard(a, hands));
            Assert.Equal("Take a perk: [1] [2] [3]   ·   0 / 2 have chosen", HudText.PhaseHint(a));
            // Both have a card to take: until they have, the mouse is the pointer's and a card takes a click.
            Assert.True(a.ChoosingCard);
            Assert.True(b.ChoosingCard);
            Assert.Equal("Shift 1 / 5   DRAFT 00:30", HudText.ShiftAndPhase(a));
            Assert.Equal("You: Long Legs", HudText.Perks(a));
            Assert.Equal("Perks   ·   team 0   ·   yours 1", HudText.PerkTracker(a));
            string text = HudText.DraftText(a);
            Assert.StartsWith("DRAFT   ·   one perk each before shift 2\n[1]  ", text);
            Assert.EndsWith("\nYou: Long Legs", text);
            Assert.Equal(1 + 3 * 4 + 1 + 1, text.Split('\n').Length);

            // Bo may take only the team's card; the keys of the other two take nothing.
            Assert.Equal("Not with Heavy Boots", HudText.CardNote(b, legs));
            Assert.Equal("You hold all you can of it", HudText.CardNote(b, hands));
            Assert.Equal($"Take a perk: [{rep + 1}]   ·   0 / 2 have chosen", HudText.PhaseHint(b));
            Assert.Equal("You: Heavy Boots, Quick Hands", HudText.Perks(b));
            Assert.True(bo.PressCardKey(legs));
            Assert.False(bo.PressCardKey(3));
            s.Run(10);
            Assert.Equal(-1, b.LocalDraftPick);

            Assert.True(bo.PressCardKey(rep));
            s.Run(10);
            Assert.Equal(rep, b.LocalDraftPick);
            // Bo has chosen and has the view back, while the cards still lie before Ada.
            Assert.False(b.ChoosingCard);
            Assert.True(a.ChoosingCard);
            Assert.Equal("Taken for the team by you", HudText.CardNote(b, rep));
            Assert.Equal("Taken for the team by Bo", HudText.CardNote(a, rep));
            Assert.Equal("You took Union Rep   ·   waiting for 1 more", HudText.PhaseHint(b));
            string open = string.Join(" ", new[] { legs, hands }.OrderBy(c => c).Select(c => $"[{c + 1}]"));
            Assert.Equal($"Take a perk: {open}   ·   1 / 2 have chosen", HudText.PhaseHint(a));
            Assert.Equal("Team: Union Rep   ·   You: Heavy Boots, Quick Hands", HudText.Perks(b));
            // Who has chosen has their number keys back for the hotbar.
            Assert.False(bo.PressCardKey(rep));
            // The team's card is no key of Ada's any more: it is taken as a key and takes nothing.
            Assert.True(ada.PressCardKey(rep));
            s.Run(10);
            Assert.Equal(-1, a.LocalDraftPick);

            Assert.True(ada.PressCardKey(legs));
            s.Run(10);
            Assert.Equal((RunPhase.Prep, 2), (sim.Run.Phase, sim.Run.Shift));
            Assert.False(a.ChoosingCard);
            Assert.Equal(new[] { "legs", "legs" }, adaTruth.Perks.Select(p => p.Id));
            Assert.Equal("", HudText.DraftText(a));
            Assert.Equal("Team: Union Rep   ·   You: Long Legs x2", HudText.Perks(a));
            Assert.Equal("Perks   ·   team 1   ·   yours 2", HudText.PerkTracker(a));
            Assert.False(ada.PressCardKey(0));
            var lines = adaFeed.Entries.Select(e => e.Text).ToList();
            Assert.Contains("The Draft: one perk each before shift 2", lines);
            Assert.Contains("Bo took Union Rep for the team", lines);
            Assert.Contains("You took Long Legs", lines);
        }

        [Fact]
        public void WhoIsAwayIsNotCountedAmongThoseTheDraftWaitsFor()
        {
            var s = new Session(CardFaces.Value);
            var ada = s.Join("Ada", AdaAccount);
            var bo = s.Join("Bo", BoAccount);
            var cy = s.Join("Cy", CyAccount);
            s.StartDelivery();
            s.MeetTheQuota(ada);
            Assert.True(s.RunUntil(() => s.Sim.Run.PhaseEndTick - s.Sim.Tick < 60 * 30, 240));
            s.Drop(bo);
            Assert.True(s.RunUntil(() => s.Sim.Run.Phase == RunPhase.Draft, 90));
            s.Run(10);

            Assert.Equal("Take a perk: [1] [2] [3]   ·   0 / 2 have chosen", HudText.PhaseHint(ada.World!));
            int legs = s.Card("legs");
            Assert.True(cy.PressCardKey(legs));
            s.Run(10);
            Assert.Equal("You took Long Legs   ·   waiting for 1 more", HudText.PhaseHint(cy.World!));
            Assert.Equal("Take a perk: [1] [2] [3]   ·   1 / 2 have chosen", HudText.PhaseHint(ada.World!));
            // A personal card stays open to the others, and says who has it.
            Assert.Equal("Taken by Cy", HudText.CardNote(ada.World!, legs));
            Assert.Equal(OpResult.Ok, ada.World!.CardState(legs));
            Assert.True(ada.PressCardKey(legs));
            s.Run(5);
            Assert.Equal(RunPhase.Prep, s.Sim.Run.Phase);
        }
    }
}
