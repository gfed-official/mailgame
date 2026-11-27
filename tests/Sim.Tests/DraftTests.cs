using System;
using System.IO;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Draft (doc 01 §2.5): between a Payday the run survives and the next shift everybody
    /// takes one card of a shared offer of three.
    /// </summary>
    public class DraftTests
    {
        private const int Tps = 30;
        private const string SpeedMod = "{ \"stat\": \"PlayerSpeed\", \"op\": \"mul\", \"value\": 1.1 }";

        private uint _nextRequest = 1;
        private uint _nextMailId = 950000;

        private static ContentDb Db => TestContent.Db;

        private static SimWorld NewWorld(uint seed = 7, ContentDb? content = null, bool draft = true, bool quickStart = false, bool raids = false) =>
            new SimWorld(content ?? Db, new SimSettings { Seed = seed, Raids = raids, Draft = draft, QuickStart = quickStart });

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static void RunUntil(SimWorld world, Func<bool> done, int maxSeconds)
        {
            for (int i = 0; i < maxSeconds * Tps && !done(); i++) world.Step();
            Assert.True(done(), $"still in {world.Run.Phase} of shift {world.Run.Shift} after {maxSeconds} s");
        }

        /// <summary>Plays the shift at hand on quota to the end of its Payday: into the Draft, if there is one.</summary>
        private void PastThePayday(SimWorld world, Player carrier)
        {
            if (world.Run.Phase == RunPhase.Lobby) Assert.Equal(OpResult.Ok, Request(world, world.GetPlayer(world.Run.Host)!, SimRequest.StartRun(0)));
            if (world.Run.Phase == RunPhase.Prep)
                foreach (var p in world.Players)
                    if (!p.Away) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);

            var home = world.Map.Destinations[0];
            var item = TestContent.Mail("letter", home.Address, _nextMailId++, world.Run.Shift);
            item.Value = (ushort)world.Run.Quota;
            Assert.True(world.Give(carrier.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(carrier, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 15);
            var entry = carrier.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            Assert.Equal(OpResult.Ok, Request(world, carrier, SimRequest.Deliver(0, carrier.Hotbar.Id, entry.EntryId, home.Id)));

            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 310);
            RunUntil(world, () => world.Run.Phase != RunPhase.Payday, 21);
        }

        /// <summary>The real content with these files in place of its own.</summary>
        private static ContentDb Content(string? perks = null, Func<string, string>? balance = null)
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            var source = new MemoryContentSource();
            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                source.Files[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllText(file);
            if (perks != null)
            {
                foreach (string key in source.Files.Keys.Where(k => k.StartsWith("perks/", StringComparison.Ordinal)).ToList())
                    source.Files.Remove(key);
                source.Files["perks/perks.json"] = perks;
            }
            if (balance != null)
            {
                string changed = balance(source.Files["balance.json"]);
                Assert.NotEqual(source.Files["balance.json"], changed);
                source.Files["balance.json"] = changed;
            }
            return ContentDb.Load(source);
        }

        private static string Perk(string id, string scope, string rest = "", string rarity = "common") =>
            $"{{ \"id\": \"{id}\", \"name\": \"{id}\", \"description\": \"-\", \"category\": \"carrier\", \"scope\": \"{scope}\", "
            + $"\"rarity\": \"{rarity}\", \"modifiers\": [{SpeedMod}]{(rest.Length > 0 ? ", " + rest : "")} }}";

        private static string Perks(params string[] perks) => "[" + string.Join(",", perks) + "]";

        private static string[] Offer(SimWorld world) => world.DraftOffer.Select(p => p.Id).ToArray();

        // --- the offer --------------------------------------------------------------------------

        [Fact]
        public void AfterAPaydayTheRunSurvives_ThreeCardsLieOnTheTable_ForThirtySeconds()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            Assert.Empty(world.DraftOffer);
            Assert.Equal(OpResult.WrongPhase, Request(world, host, SimRequest.PickPerk(0, 0)));

            PastThePayday(world, host);
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            // The shift that was paid is still the shift: the next one begins with its Prep.
            Assert.Equal(1, world.Run.Shift);
            Assert.False(world.AcceptsPlayers);
            var changed = world.Events.OfType<PhaseChangedEvent>().Last();
            Assert.Equal((RunPhase.Draft, 30u * Tps), (changed.Phase, changed.PhaseEndTick - changed.Tick));

            Assert.Equal(3, world.DraftOffer.Count);
            Assert.Equal(3, world.DraftOffer.Distinct().Count());
            var offered = world.Events.OfType<DraftOfferEvent>().Single();
            Assert.Equal(world.DraftOffer.Select(p => p.Index), offered.Perks);
            Assert.Empty(world.Events.OfType<PerkPickedEvent>());
            Assert.Equal(-1, host.DraftPick);
        }

        [Fact]
        public void TheOfferIsTheSameForTheSameSeed_AndForTheNextRunOfTheServer()
        {
            string[] First(uint seed)
            {
                var world = NewWorld(seed);
                PastThePayday(world, world.AddPlayer("host")!);
                return Offer(world);
            }

            Assert.Equal(First(7), First(7));
            Assert.Contains(Enumerable.Range(8, 6), seed => !First((uint)seed).SequenceEqual(First(7)));

            // A run draws from the seed anew, as it does its mail and its raids.
            var again = NewWorld(7);
            var host = again.AddPlayer("host")!;
            PastThePayday(again, host);
            Assert.Equal(OpResult.Ok, Request(again, host, SimRequest.PickPerk(0, 1)));
            RunUntil(again, () => again.Run.Phase == RunPhase.Prep, 1);
            Assert.Equal(OpResult.Ok, Request(again, host, SimRequest.SetReady(0, true)));
            // Nothing is delivered: the quota is missed and the run is over.
            RunUntil(again, () => again.Run.Phase == RunPhase.Results, 310);
            Assert.Equal(OpResult.Ok, Request(again, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Lobby, again.Run.Phase);
            PastThePayday(again, host);
            Assert.Equal(First(7), Offer(again));
        }

        [Fact]
        public void TheRarityOfACardIsRolledByTheWeights_AndRareGainsWeightWithEveryShift()
        {
            var rareOnly = Content(balance: b => b.Replace("\"draftRarityWeights\": [60, 30, 10]", "\"draftRarityWeights\": [0, 0, 1]"));
            var rare = rareOnly.Perks.Where(p => p.Rarity == PerkRarity.Rare).Select(p => p.Id).ToArray();
            Assert.Equal(new[] { "postal_sprint" }, rare);
            for (uint seed = 1; seed <= 4; seed++)
            {
                var world = NewWorld(seed, rareOnly);
                PastThePayday(world, world.AddPlayer("host")!);
                // The one rare perk first; the other cards from what is left, whatever it weighs.
                Assert.Equal(3, world.DraftOffer.Count);
                Assert.Equal("postal_sprint", world.DraftOffer[0].Id);
            }

            var late = Content(balance: b => b
                .Replace("\"draftRarityWeights\": [60, 30, 10]", "\"draftRarityWeights\": [1, 0, 0]")
                .Replace("\"draftRareWeightPerShift\": 5", "\"draftRareWeightPerShift\": 100000"));
            var w = NewWorld(7, late);
            var host = w.AddPlayer("host")!;
            PastThePayday(w, host);
            // Before the second shift Rare weighs what the table says: nothing here.
            Assert.All(w.DraftOffer, p => Assert.Equal(PerkRarity.Common, p.Rarity));
            Assert.Equal(OpResult.Ok, Request(w, host, SimRequest.PickPerk(0, 0)));
            RunUntil(w, () => w.Run.Phase == RunPhase.Prep, 1);
            PastThePayday(w, host);
            Assert.Equal(RunPhase.Draft, w.Run.Phase);
            Assert.Equal("postal_sprint", w.DraftOffer[0].Id);
        }

        [Fact]
        public void ACardMustBeOneSomebodyHereCanTake_AndWithNoneLeftThereIsNoDraft()
        {
            var db = Content(Perks(Perk("mine", "personal"), Perk("ours", "team")));
            var world = NewWorld(content: db);
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;

            PastThePayday(world, a);
            Assert.Equal(new[] { "mine", "ours" }, Offer(world).OrderBy(id => id));
            int mine = Array.IndexOf(Offer(world), "mine"), ours = 1 - mine;
            Assert.Equal(OpResult.Ok, Request(world, a, SimRequest.PickPerk(0, mine)));
            Assert.Equal(OpResult.Ok, Request(world, b, SimRequest.PickPerk(0, ours)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 1);
            Assert.Equal(2, world.Run.Shift);

            // The team perk is taken; the personal one is still open to b alone.
            PastThePayday(world, a);
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            Assert.Equal(new[] { "mine" }, Offer(world));
            Assert.Equal(OpResult.PerkMaxed, Request(world, a, SimRequest.PickPerk(0, 0)));
            Assert.Equal(-1, a.DraftPick);
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            // a has no card to take and is not waited for.
            Assert.Equal(OpResult.Ok, Request(world, b, SimRequest.PickPerk(0, 0)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 1);
            Assert.Equal(3, world.Run.Shift);

            // Nothing is left for anybody: the Payday goes straight on to the Prep.
            world.Events.Clear();
            PastThePayday(world, a);
            Assert.Equal((RunPhase.Prep, 4), (world.Run.Phase, world.Run.Shift));
            Assert.Empty(world.Events.OfType<DraftOfferEvent>());
            Assert.DoesNotContain(world.Events.OfType<PhaseChangedEvent>(), e => e.Phase == RunPhase.Draft);
        }

        [Fact]
        public void APerkIsNotOfferedBeforeItsShift_NorNextToACardItExcludes()
        {
            var db = Content(Perks(
                Perk("early", "personal", "\"excludes\": [\"rival\"], \"maxStacks\": 9"),
                Perk("rival", "personal", "\"maxStacks\": 9"),
                Perk("late", "personal", "\"prerequisites\": { \"shiftMin\": 3 }, \"maxStacks\": 9"),
                Perk("ranked", "personal", "\"prerequisites\": { \"rankMin\": 2 }, \"maxStacks\": 9")));
            var world = NewWorld(content: db);
            var host = world.AddPlayer("host")!;

            // Before shift 2: "late" is not due, nobody has a rank, and of the two rivals one fits on the table.
            PastThePayday(world, host);
            string first = Assert.Single(Offer(world));
            Assert.Contains(first, new[] { "early", "rival" });
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PickPerk(0, 0)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 1);

            // Before shift 3: "late" is due; the rival of what the carrier holds is no card for them.
            PastThePayday(world, host);
            Assert.Equal(new[] { first, "late" }.OrderBy(id => id), Offer(world).OrderBy(id => id));
        }

        // --- picking ----------------------------------------------------------------------------

        [Fact]
        public void ACardIsTakenOnce_AndThePerkIsTheirsAtOnce()
        {
            var db = Content(Perks(Perk("a", "personal"), Perk("b", "personal"), Perk("c", "personal")));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada")!;
            var bo = world.AddPlayer("Bo")!;
            PastThePayday(world, ada);
            var offer = world.DraftOffer.ToArray();
            Assert.Equal(3, offer.Length);
            world.Events.Clear();

            Assert.Equal(OpResult.BadRequest, Request(world, ada, SimRequest.PickPerk(0, 3)));
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PickPerk(0, 2)));
            Assert.Equal(new[] { offer[2] }, ada.Perks);
            Assert.Equal(2, ada.DraftPick);
            Assert.Equal(1.1f, ada.Stats.Resolve(Stat.PlayerSpeed, 1f), 4);
            var picked = world.Events.OfType<PerkPickedEvent>().Single();
            Assert.Equal((ada.Id, offer[2].Index, (byte)2, false), (picked.Player, picked.Perk, picked.Card, picked.Auto));

            Assert.Equal(OpResult.AlreadyPicked, Request(world, ada, SimRequest.PickPerk(0, 1)));
            Assert.Single(ada.Perks);
            // The others still choose: the Draft waits for them, and a personal card is open to everybody.
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            Assert.Empty(bo.Perks);
            Assert.Equal(OpResult.Ok, Request(world, bo, SimRequest.PickPerk(0, 2)));
            Assert.Equal(new[] { offer[2] }, bo.Perks);

            // Everybody has chosen: the next shift does not wait for the clock.
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 1);
            Assert.Equal(2, world.Run.Shift);
            Assert.Empty(world.DraftOffer);
            Assert.Equal((-1, -1), (ada.DraftPick, bo.DraftPick));
            Assert.Equal(OpResult.WrongPhase, Request(world, bo, SimRequest.PickPerk(0, 0)));
            // What was picked lasts.
            Assert.Single(ada.Perks);
            Assert.Single(bo.Perks);
        }

        // --- rerolls (doc 01 §2.5: "one free reroll per player per run"; doc 01 §5.2: a second at rank 4) ---

        [Fact]
        public void AReroll_GivesThatPlayerThreeNewCardsOfTheirOwn_TheOthersKeepTheirs()
        {
            var db = Content(Perks(Enumerable.Range(0, 8).Select(i => Perk($"p{i}", "personal")).ToArray()));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada")!;
            var bo = world.AddPlayer("Bo")!;
            PastThePayday(world, ada);
            var shared = world.DraftOffer.ToArray();
            Assert.Equal((1, 1), (world.RerollsLeft(ada), world.RerollsLeft(bo)));
            // Everybody hears of the shared cards, each with their own rerolls left.
            var dealt = world.Events.OfType<DraftOfferEvent>().ToList();
            Assert.Equal(new[] { ada.Id, bo.Id }, dealt.Select(e => e.Audience));
            Assert.All(dealt, e => Assert.Equal((false, (byte)1), (e.Reroll, e.RerollsLeft)));
            world.Events.Clear();

            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.Reroll(0)));
            var own = world.OfferFor(ada).ToArray();
            Assert.Equal(3, own.Length);
            Assert.Empty(own.Intersect(shared));
            Assert.Equal(shared, world.OfferFor(bo));
            Assert.Equal(shared, world.DraftOffer);
            var rerolled = world.Events.OfType<DraftOfferEvent>().Single();
            Assert.Equal((ada.Id, true, (byte)0), (rerolled.Audience, rerolled.Reroll, rerolled.RerollsLeft));
            Assert.Equal(own.Select(p => p.Index), rerolled.Perks);

            // One a run: no second, and the card taken is one of their own.
            Assert.Equal(OpResult.NoRerolls, Request(world, ada, SimRequest.Reroll(0)));
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PickPerk(0, 1)));
            Assert.Equal(new[] { own[1] }, ada.Perks);
            Assert.Equal(OpResult.Ok, Request(world, bo, SimRequest.PickPerk(0, 1)));
            Assert.Equal(new[] { shared[1] }, bo.Perks);

            // The next Draft is shared again, and the reroll is still spent.
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 1);
            Assert.Null(ada.Offer);
            PastThePayday(world, ada);
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            Assert.Equal(world.DraftOffer, world.OfferFor(ada));
            Assert.Equal((0, 1), (world.RerollsLeft(ada), world.RerollsLeft(bo)));
            Assert.Equal(OpResult.NoRerolls, Request(world, ada, SimRequest.Reroll(0)));
        }

        [Fact]
        public void NoRerollOnceTheCardIsTaken_NorOutsideTheDraft_AndRankFourHasTwo_EachWithNoneOfTheCardsBefore()
        {
            var db = Content(Perks(Enumerable.Range(0, 12).Select(i => Perk($"p{i}", "personal")).ToArray()));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada")!;
            var bo = world.AddPlayer("Bo")!;
            world.SetRank(bo, 4);
            Assert.Equal(OpResult.WrongPhase, Request(world, ada, SimRequest.Reroll(0)));
            PastThePayday(world, ada);
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PickPerk(0, 0)));
            Assert.Equal(OpResult.AlreadyPicked, Request(world, ada, SimRequest.Reroll(0)));
            Assert.Equal(1, world.RerollsLeft(ada));

            Assert.Equal(2, world.RerollsLeft(bo));
            var first = world.OfferFor(bo).ToArray();
            Assert.Equal(OpResult.Ok, Request(world, bo, SimRequest.Reroll(0)));
            var second = world.OfferFor(bo).ToArray();
            Assert.Equal(OpResult.Ok, Request(world, bo, SimRequest.Reroll(0)));
            var third = world.OfferFor(bo).ToArray();
            // Each time none of the cards just before them.
            Assert.Empty(second.Intersect(first));
            Assert.Empty(third.Intersect(second));
            Assert.Equal(OpResult.NoRerolls, Request(world, bo, SimRequest.Reroll(0)));
        }

        [Fact]
        public void ARerollWithNothingElseToDraw_IsRefused_AndSpendsNothing()
        {
            var db = Content(Perks(Perk("a", "personal"), Perk("b", "personal"), Perk("c", "personal")));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada")!;
            PastThePayday(world, ada);
            Assert.Equal(OpResult.NoNewCards, Request(world, ada, SimRequest.Reroll(0)));
            Assert.Equal(1, world.RerollsLeft(ada));
            Assert.Null(ada.Offer);
        }

        [Fact]
        public void WhenTheClockRunsOut_ARerolledPlayerIsGivenTheFirstOfTheirOwnCards()
        {
            var db = Content(Perks(Enumerable.Range(0, 8).Select(i => Perk($"p{i}", "personal")).ToArray()));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada")!;
            PastThePayday(world, ada);
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.Reroll(0)));
            var own = world.OfferFor(ada)[0];
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 31);
            Assert.Equal(new[] { own }, ada.Perks);
        }

        [Fact]
        public void ATeamCardIsGoneForTheOthersOnceItIsTaken()
        {
            var db = Content(Perks(Perk("x", "team"), Perk("y", "team"), Perk("z", "team")));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada")!;
            var bo = world.AddPlayer("Bo")!;
            PastThePayday(world, ada);
            var offer = world.DraftOffer.ToArray();

            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PickPerk(0, 0)));
            Assert.Equal(new[] { offer[0] }, world.TeamPerks);
            Assert.Empty(ada.Perks);
            Assert.Equal(OpResult.PerkTaken, Request(world, bo, SimRequest.PickPerk(0, 0)));
            Assert.Equal(-1, bo.DraftPick);
            Assert.Equal(OpResult.Ok, Request(world, bo, SimRequest.PickPerk(0, 1)));
            Assert.Equal(new[] { offer[0], offer[1] }, world.TeamPerks);
        }

        [Fact]
        public void WhenTheClockRunsOut_WhoHasNotChosenIsGivenTheFirstCardOpenToThem()
        {
            var db = Content(Perks(Perk("x", "team"), Perk("y", "team"), Perk("z", "team")));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada")!;
            var bo = world.AddPlayer("Bo")!;
            PastThePayday(world, ada);
            var offer = world.DraftOffer.ToArray();
            uint end = world.Run.PhaseEndTick;
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PickPerk(0, 0)));
            world.Events.Clear();

            RunUntil(world, () => world.Tick == end - 1, 31);
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            Assert.Empty(world.Events.OfType<PerkPickedEvent>());
            world.Step();
            Assert.Equal((RunPhase.Prep, 2), (world.Run.Phase, world.Run.Shift));
            // The first card is Ada's and the team's already: Bo gets the second.
            var given = world.Events.OfType<PerkPickedEvent>().Single();
            Assert.Equal((bo.Id, offer[1].Index, (byte)1, true), (given.Player, given.Perk, given.Card, given.Auto));
            Assert.Equal(new[] { offer[0], offer[1] }, world.TeamPerks);
        }

        [Fact]
        public void WhoIsAwayIsNotWaitedFor_AndGoesWithout()
        {
            var db = Content(Perks(Perk("a", "personal"), Perk("b", "personal"), Perk("c", "personal")));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada", account: 11)!;
            var bo = world.AddPlayer("Bo", account: 22)!;
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.StartRun(0)));
            foreach (var p in world.Players) Request(world, p, SimRequest.SetReady(0, true));
            // The grace is two minutes: Bo drops out late enough to be still away at the Draft.
            RunUntil(world, () => world.Run.PhaseEndTick - world.Tick < 60 * Tps, 240);
            Assert.True(world.Disconnect(bo.Id));

            PastThePayday(world, ada);
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            Assert.True(bo.Away);
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PickPerk(0, 0)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 1);
            Assert.Single(ada.Perks);
            Assert.Empty(bo.Perks);
        }

        [Fact]
        public void WhoComesBackDuringTheDraftStillChooses()
        {
            var db = Content(Perks(Perk("a", "personal"), Perk("b", "personal"), Perk("c", "personal")));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada", account: 11)!;
            var bo = world.AddPlayer("Bo", account: 22)!;
            var cy = world.AddPlayer("Cy", account: 33)!;
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.StartRun(0)));
            foreach (var p in world.Players) Request(world, p, SimRequest.SetReady(0, true));
            RunUntil(world, () => world.Run.PhaseEndTick - world.Tick < 60 * Tps, 240);
            Assert.True(world.Disconnect(bo.Id));
            PastThePayday(world, ada);
            var third = world.DraftOffer[2];
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PickPerk(0, 0)));
            // Cy has not chosen: the Draft is still on when Bo is back.
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            world.Reconnect(bo);
            Assert.Equal(OpResult.Ok, Request(world, cy, SimRequest.PickPerk(0, 1)));
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            Assert.Equal(OpResult.Ok, Request(world, bo, SimRequest.PickPerk(0, 2)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 1);
            Assert.Equal(third, Assert.Single(bo.Perks));
        }

        [Fact]
        public void NothingOfTheRaidIsLeftWhenTheCardsAreDealt()
        {
            var world = NewWorld(raids: true);
            var host = world.AddPlayer("host")!;
            PastThePayday(world, host);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PickPerk(0, 0)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 1);

            // The second shift ends in a raid; its Payday still sees the raiders run.
            PastThePayday(world, host);
            Assert.True(world.EnemiesSpawnedTotal > 0);
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            Assert.Equal(Combat.RaidState.None, world.Raid);
            Assert.Empty(world.Enemies);
            Assert.Empty(world.Loot);
            Assert.False(host.Dead);
        }

        // --- where there is none ------------------------------------------------------------------

        [Fact]
        public void ThereIsNoDraftWhereTheServerHasNone()
        {
            var off = NewWorld(draft: false);
            var host = off.AddPlayer("host")!;
            PastThePayday(off, host);
            Assert.Equal((RunPhase.Prep, 2), (off.Run.Phase, off.Run.Shift));
            Assert.Empty(off.Events.OfType<DraftOfferEvent>());

            // A quick-start server goes from shift to shift by itself.
            var quick = NewWorld(quickStart: true);
            var carrier = quick.AddPlayer("carrier")!;
            RunUntil(quick, () => quick.Run.Phase == RunPhase.Delivery, 1);
            PastThePayday(quick, carrier);
            Assert.Equal((RunPhase.Delivery, 2), (quick.Run.Phase, quick.Run.Shift));
            Assert.Empty(quick.Events.OfType<DraftOfferEvent>());
        }

        [Fact]
        public void EveryPaydayButTheLastIsFollowedByADraft_AndAMissedQuotaByNone()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            for (int shift = 1; shift < world.ShiftCount; shift++)
            {
                PastThePayday(world, host);
                Assert.Equal((RunPhase.Draft, shift), (world.Run.Phase, world.Run.Shift));
                Assert.Equal(3, world.DraftOffer.Count);
                Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PickPerk(0, 0)));
                RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 1);
            }
            Assert.Equal(world.ShiftCount - 1, world.TeamPerks.Count + host.Perks.Count);
            PastThePayday(world, host);
            Assert.Equal(RunPhase.Results, world.Run.Phase);
            Assert.Equal(RunOutcome.Victory, world.Run.Outcome);
            Assert.Equal(world.ShiftCount - 1, world.Events.OfType<DraftOfferEvent>().Count());

            var missed = NewWorld();
            var p = missed.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(missed, p, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(missed, p, SimRequest.SetReady(0, true)));
            RunUntil(missed, () => missed.Run.Phase == RunPhase.Results, 270);
            Assert.Empty(missed.Events.OfType<DraftOfferEvent>());
        }

        [Fact]
        public void TheDraftNeedsItsNumbersInTheBalance()
        {
            foreach (string key in new[] { "draftSeconds", "draftCards", "draftRarityWeights", "draftRareWeightPerShift", "rerollsPerRun", "rerollSecondRank" })
            {
                string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
                var source = new MemoryContentSource();
                foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                    source.Files[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllText(file);
                string balance = source.Files["balance.json"];
                Assert.Contains($"\"{key}\"", balance);
                source.Files["balance.json"] = balance.Replace($"\"{key}\"", $"\"{key}_\"");
                ContentDb.TryLoad(source, out var errors);
                Assert.Contains($"balance.json: missing required key '{key}'", errors);
            }
        }

        // --- the personal cap (doc 01 §4.2: "up to 5 personal perks; the 6th pick must be a team perk if any is available") ---

        private static string[] CapPool(int personal, int team) =>
            Enumerable.Range(0, personal).Select(i => Perk($"p{i}", "personal"))
                .Concat(Enumerable.Range(0, team).Select(i => Perk($"t{i}", "team"))).ToArray();

        /// <summary>Into the run (a run starts without perks), and p0 to p4 to the carrier.</summary>
        private void GrantFive(SimWorld world, ContentDb db, Player p)
        {
            Assert.Equal(OpResult.Ok, Request(world, world.GetPlayer(world.Run.Host)!, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            for (int i = 0; i < 5; i++) Assert.Equal(OpResult.Ok, world.GrantPerk(p, db.Perks.Single(k => k.Id == $"p{i}")));
        }

        [Fact]
        public void FivePersonalPerksCloseTheSixth_WhileATeamPerkIsLeft_ThenTheCapGivesWay()
        {
            var db = Content(Perks(CapPool(7, 1)));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada")!;
            Assert.Equal(5, world.PersonalPerkCap);
            for (int i = 0; i < 5; i++) Assert.Equal(OpResult.Ok, world.GrantPerk(ada, db.Perks.Single(p => p.Id == $"p{i}")));
            Assert.True(world.AtPersonalCap(ada));
            var sixth = db.Perks.Single(p => p.Id == "p5");
            var team = db.Perks.Single(p => p.Id == "t0");
            Assert.Equal(OpResult.PerkCapped, world.CanTake(ada, sixth));
            Assert.Equal(OpResult.PerkCapped, world.GrantPerk(ada, sixth));
            Assert.Equal(5, ada.Perks.Count);
            // The team perk is open, and once it is the team's no team perk is left: a sixth personal one may come.
            Assert.Equal(OpResult.Ok, world.GrantPerk(ada, team));
            Assert.Equal(OpResult.Ok, world.CanTake(ada, sixth));
            // The cap is the carrier's own: somebody with fewer is not held back.
            var bo = world.AddPlayer("Bo")!;
            Assert.Equal(OpResult.Ok, world.CanTake(bo, sixth));
        }

        [Fact]
        public void ACappedCarrierWithOnlyPersonalCards_IsDealtTeamCardsOfTheirOwn_ForFree()
        {
            var db = Content(Perks(CapPool(8, 2)));
            bool dealt = false, shared = false;
            for (uint seed = 1; seed <= 40 && !(dealt && shared); seed++)
            {
                var world = NewWorld(seed, content: db);
                var ada = world.AddPlayer("Ada")!;
                var bo = world.AddPlayer("Bo")!;
                GrantFive(world, db, ada);
                PastThePayday(world, bo);
                Assert.Equal(RunPhase.Draft, world.Run.Phase);
                var own = world.OfferFor(ada);
                Assert.Equal(world.DraftOffer, world.OfferFor(bo));
                Assert.Equal(1, world.RerollsLeft(ada));
                if (world.DraftOffer.Any(p => p.Team))
                {
                    // A team card on the table: Ada keeps the shared cards, the personal ones closed to her.
                    shared = true;
                    Assert.Same(world.DraftOffer, own);
                    for (int c = 0; c < own.Count; c++)
                        Assert.Equal(own[c].Team ? OpResult.Ok : ada.Perks.Contains(own[c]) ? OpResult.PerkMaxed : OpResult.PerkCapped, world.CanTake(ada, own[c]));
                }
                else
                {
                    // Only personal cards: hers are team cards, and she was told of them as of a Draft's, not a reroll.
                    dealt = true;
                    Assert.NotEmpty(own);
                    Assert.All(own, p => Assert.True(p.Team));
                    var told = world.Events.OfType<DraftOfferEvent>().Single(e => e.Audience == ada.Id);
                    Assert.False(told.Reroll);
                    Assert.Equal(own.Select(p => p.Index), told.Perks);
                    Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PickPerk(0, 0)));
                    Assert.Equal(own[0], world.TeamPerks.Single());
                    Assert.Equal(5, ada.Perks.Count);
                }
            }
            Assert.True(dealt && shared, $"in 40 seeds: dealt {dealt}, shared {shared}");
        }

        [Fact]
        public void ACappedCarrierWithNoTeamPerkLeft_TakesAPersonalCard()
        {
            var db = Content(Perks(CapPool(9, 0)));
            var world = NewWorld(content: db);
            var ada = world.AddPlayer("Ada")!;
            GrantFive(world, db, ada);
            PastThePayday(world, ada);
            Assert.Equal(RunPhase.Draft, world.Run.Phase);
            Assert.Same(world.DraftOffer, world.OfferFor(ada));
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.PickPerk(0, 0)));
            Assert.Equal(6, ada.Perks.Count);
        }

        [Fact]
        public void TheCapNeedsItsNumberInTheBalance()
        {
            var e = Assert.Throws<ContentException>(() => Content(balance: b => b.Replace("\"personalPerkCap\": 5,", "")));
            Assert.Contains("personalPerkCap", e.Message);
        }
    }
}
