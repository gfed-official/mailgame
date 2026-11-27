using System;
using System.IO;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Client.Meta;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The local MetaProfile (doc 08 §3.8): its file, and how a run feeds it (doc 01 §5.1, doc 07 §7.1).</summary>
    public class ProfileTests
    {
        [Fact]
        public void ProfileSurvivesAJsonRoundTrip()
        {
            var profile = MetaProfile.Create("Ada");
            profile.RankXp = 1234;
            profile.Runs = 7;
            profile.Wins = 2;
            profile.Deliveries = 90;
            profile.Misdeliveries = 4;
            profile.BestScore = 4321;
            profile.AddRun(new RecentRun { SeedString = "17", ShiftsCompleted = 5, Victory = true, Score = 4321, Players = 3, Date = "2026-11-13" });
            profile.AddRun(new RecentRun { SeedString = "\"quoted\" seed", ShiftsCompleted = 1, Score = 12, Players = 1, Date = "2026-11-14" });

            string json = profile.ToJson();
            var back = MetaProfile.FromJson(json);

            Assert.StartsWith("local:", profile.ProfileId);
            Assert.Equal(profile.ProfileId, back.ProfileId);
            Assert.Equal("Ada", back.DisplayName);
            Assert.Equal(1234, back.RankXp);
            Assert.Equal(7, back.Runs);
            Assert.Equal(2, back.Wins);
            Assert.Equal(90, back.Deliveries);
            Assert.Equal(4, back.Misdeliveries);
            Assert.Equal(4321, back.BestScore);
            Assert.Equal(2, back.RecentRuns.Count);
            // Newest first.
            Assert.Equal("\"quoted\" seed", back.RecentRuns[0].SeedString);
            Assert.False(back.RecentRuns[0].Victory);
            Assert.Equal("17", back.RecentRuns[1].SeedString);
            Assert.True(back.RecentRuns[1].Victory);
            Assert.Equal(5, back.RecentRuns[1].ShiftsCompleted);
            Assert.Equal(3, back.RecentRuns[1].Players);
            Assert.Equal("2026-11-13", back.RecentRuns[1].Date);
            Assert.Equal(json, back.ToJson());
        }

        [Fact]
        public void RankComesFromTheXp()
        {
            var profile = MetaProfile.Create("Ada");
            Assert.Equal(1, profile.Rank(500));
            Assert.Equal("Postal Rank 1   ·   0 / 500 XP", HudText.Rank(profile, 500));
            profile.RankXp = 499;
            Assert.Equal(1, profile.Rank(500));
            profile.RankXp = 500;
            Assert.Equal(2, profile.Rank(500));
            Assert.Equal(0, profile.XpIntoRank(500));
            profile.RankXp = 1120;
            Assert.Equal("Postal Rank 3   ·   120 / 500 XP", HudText.Rank(profile, 500));
        }

        [Fact]
        public void OnlyTheLastTenRunsAreKept()
        {
            var profile = MetaProfile.Create("Ada");
            for (int i = 1; i <= 13; i++) profile.AddRun(new RecentRun { SeedString = i.ToString(), Score = i });
            Assert.Equal(MetaProfile.MaxRecentRuns, profile.RecentRuns.Count);
            Assert.Equal("13", profile.RecentRuns[0].SeedString);
            Assert.Equal("4", profile.RecentRuns[9].SeedString);
            Assert.Equal(10, MetaProfile.FromJson(profile.ToJson()).RecentRuns.Count);
        }

        [Fact]
        public void MissingFieldsDefault_AndNonsenseIsRefused()
        {
            var sparse = MetaProfile.FromJson("{ \"profileId\": \"local:x\", \"rankXp\": -5 }");
            Assert.Equal("local:x", sparse.ProfileId);
            Assert.Equal("", sparse.DisplayName);
            Assert.Equal(0, sparse.RankXp);
            Assert.Empty(sparse.RecentRuns);

            Assert.Throws<FormatException>(() => MetaProfile.FromJson("not json"));
            Assert.Throws<FormatException>(() => MetaProfile.FromJson("[1, 2]"));
            Assert.Throws<FormatException>(() => MetaProfile.FromJson(""));
        }

        [Fact]
        public void AnUnreadableProfileIsSetAside_NotOverwritten()
        {
            var store = new MemoryProfileStore { Json = "{ broken" };
            var profile = ProfileTracker.LoadOrCreate(store, "Postie", out string? problem);
            Assert.NotNull(problem);
            Assert.Equal("Postie", profile.DisplayName);
            Assert.Equal(0, profile.RankXp);
            Assert.Equal("{ broken", store.Bad);
            // The new profile is stored at once: its id is the account a server gives a lost seat back to.
            Assert.Equal(profile.ProfileId, MetaProfile.FromJson(store.Json!).ProfileId);

            var empty = new MemoryProfileStore();
            var fresh = ProfileTracker.LoadOrCreate(empty, "Postie", out problem);
            Assert.Null(problem);
            Assert.Equal("Postie", fresh.DisplayName);
            Assert.Null(empty.Bad);
            Assert.Equal(fresh.ProfileId, MetaProfile.FromJson(empty.Json!).ProfileId);
            Assert.NotEqual(0ul, fresh.AccountId);
            Assert.NotEqual(profile.AccountId, fresh.AccountId);
            Assert.Equal(fresh.AccountId, ProfileTracker.LoadOrCreate(empty, "Other", out problem).AccountId);

            // A store that cannot be written does not stop the game.
            var full = new FailingStore();
            Assert.Equal("Postie", ProfileTracker.LoadOrCreate(full, "Postie", out problem).DisplayName);
            Assert.Null(problem);

            var kept = new MemoryProfileStore { Json = profile.ToJson() };
            Assert.Equal(profile.ProfileId, ProfileTracker.LoadOrCreate(kept, "Other", out problem).ProfileId);
            Assert.Null(problem);
        }

        [Fact]
        public void TheFileStoreWritesReadsAndSetsAside()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pm-profile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "profile.json");
                var store = new FileProfileStore(path);
                Assert.Null(store.Read());

                var profile = MetaProfile.Create("Ada");
                profile.RankXp = 40;
                store.Write(profile.ToJson());
                profile.RankXp = 90;
                store.Write(profile.ToJson());
                Assert.Equal(90, MetaProfile.FromJson(store.Read()!).RankXp);
                // Nothing but the profile is left behind by the write.
                Assert.Equal(new[] { path }, Directory.GetFiles(dir));

                File.WriteAllText(path, "{ broken");
                var again = ProfileTracker.LoadOrCreate(store, "Postie", out string? problem);
                Assert.NotNull(problem);
                Assert.Equal(0, again.RankXp);
                Assert.Equal("{ broken", File.ReadAllText(path + ".bad"));
                Assert.Equal(again.ProfileId, MetaProfile.FromJson(store.Read()!).ProfileId);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void ARunFeedsTheProfile_AndItIsSavedAtEveryPaydayAndAtResults()
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3 }, hub);
            var client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Bot");
            var bot = new DeliveryBot(client);
            var store = new MemoryProfileStore();
            var profile = MetaProfile.Create("Bot");
            profile.RankXp = 480;
            profile.Runs = 4;
            var tracker = new ProfileTracker(client, profile, store, () => new DateTime(2026, 11, 13, 22, 0, 0));

            int paydays = 0, lobbies = 0, savesAtFirstPayday = -1;
            RunResults? results = null;
            ProfileGain? gainAtResults = null;
            client.EventReceived += e =>
            {
                switch (e)
                {
                    case ShiftSummaryEvent _:
                        paydays++;
                        if (savesAtFirstPayday < 0) savesAtFirstPayday = store.Writes;
                        break;
                    case RunResultsEvent x:
                        results = x.Results;
                        gainAtResults = tracker.LastGain;
                        break;
                    case PhaseChangedEvent p when p.Phase == RunPhase.Lobby:
                        lobbies++;
                        break;
                }
            };

            for (int i = 0; i < 30 * 60 * 30 && lobbies == 0; i++)
            {
                bot.Tick();
                client.Tick();
                server.Tick();
            }
            Assert.Equal(1, lobbies);
            Assert.NotNull(results);
            var world = client.World!;
            var own = results!.Players.Single(p => p.Player == world.LocalId);
            Assert.True(own.Xp > 20, $"the bot earned {own.Xp} XP");

            Assert.Equal(1, savesAtFirstPayday);
            Assert.Equal(paydays + 1, store.Writes);
            Assert.Null(tracker.SaveProblem);

            Assert.Equal(480 + own.Xp, profile.RankXp);
            Assert.Equal(5, profile.Runs);
            Assert.Equal(results.Outcome == RunOutcome.Victory ? 1 : 0, profile.Wins);
            Assert.Equal(own.Deliveries, profile.Deliveries);
            Assert.Equal(own.Misdeliveries, profile.Misdeliveries);
            Assert.Equal(results.Score, profile.BestScore);
            var run = Assert.Single(profile.RecentRuns);
            Assert.Equal("3", run.SeedString);
            Assert.Equal(results.ShiftsCompleted, run.ShiftsCompleted);
            Assert.Equal(results.Score, run.Score);
            Assert.Equal(1, run.Players);
            Assert.Equal("2026-11-13", run.Date);

            // The gain shows with the Results and is gone back in the Lobby.
            Assert.NotNull(gainAtResults);
            Assert.Equal(own.Xp, gainAtResults!.Value.Xp);
            int rank = 1 + (480 + own.Xp) / 500;
            Assert.Equal(1, gainAtResults.Value.RankBefore);
            Assert.Equal(rank, gainAtResults.Value.RankAfter);
            Assert.Null(tracker.LastGain);
            var lines = HudText.ResultsLines(world, results, profile, gainAtResults);
            // Rank 2 opens the Sea kit (doc 01 §5.2): revealed under the rank line, and in the profile's unlocks.
            Assert.Equal(new[] { $"RANK UP!   Postal Rank {rank}   ·   {(480 + own.Xp) % 500} / 500 XP", "Unlocked: the Sea kit" }, lines.Skip(lines.Count - 2));
            Assert.Equal(lines.Count - 2, HudText.ResultsLines(world, results).Count);
            Assert.Equal(new[] { "land", "sea" }, profile.Unlocks.Kits);

            // What is on disk is what is in memory.
            Assert.Equal(profile.ToJson(), store.Json);
            Assert.Equal(profile.RankXp, MetaProfile.FromJson(store.Json!).RankXp);
        }

        [Fact]
        public void APracticeRun_IsTwoShiftsWithNoXp_AndTheProfileDoesNotCountIt()
        {
            // Doc 09 §6 (design choice 165): the host's [P] in the Lobby; the bot plays the run through.
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3 }, hub);
            var client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Bot");
            var store = new MemoryProfileStore();
            var profile = MetaProfile.Create("Bot");
            profile.RankXp = 480;
            var tracker = new ProfileTracker(client, profile, store);
            for (int i = 0; i < 10; i++) { client.Tick(); server.Tick(); }
            var world = client.World!;
            Assert.Equal("[P] practice   ·   [O] daily   ·   [Ctrl+V] a seed", HudText.ModeKeys(world));
            Assert.NotEqual(0u, client.TogglePractice());
            for (int i = 0; i < 10; i++) { client.Tick(); server.Tick(); }
            world = client.World!;
            Assert.Equal(RunMode.Practice, world.Global.Mode);
            Assert.Equal("Practice · 2 shifts, no raid, no XP", HudText.WorldTracker(world));
            Assert.Equal("[P] leave practice   ·   [O] daily   ·   [Ctrl+V] a seed", HudText.ModeKeys(world));
            Assert.Equal("", HudText.StampPick(world, 0, 3));
            Assert.Equal(0u, client.ToggleStamp());

            var bot = new DeliveryBot(client);
            RunResults? results = null;
            bool raid = false;
            int lobbies = 0;
            client.EventReceived += e =>
            {
                if (e is RunResultsEvent x) results = x.Results;
                if (e is PhaseChangedEvent p && p.Phase == RunPhase.Lobby) lobbies++;
            };
            for (int i = 0; i < 30 * 60 * 15 && lobbies == 0; i++)
            {
                bot.Tick();
                client.Tick();
                server.Tick();
                raid |= client.World!.Raid != Sim.Combat.RaidState.None;
            }
            Assert.Equal(1, lobbies);
            Assert.NotNull(results);
            Assert.False(raid);
            Assert.True(results!.LastShift <= 2);
            Assert.Equal(0, results.Players.Single().Xp);
            Assert.Equal((480, 0, 0, 0), (profile.RankXp, profile.Runs, profile.Deliveries, profile.RecentRuns.Count));
            world = client.World!;
            Assert.Contains("Practice run   ·   no XP", HudText.ResultsLines(world, results));
            Assert.StartsWith(results.Outcome == RunOutcome.Victory ? "PRACTICE DONE — all 2 shifts" : "RUN OVER", HudText.ResultsHeadline(world, results));

            // [P] again: back to a run of the host's own on the test street.
            Assert.NotEqual(0u, client.TogglePractice());
            for (int i = 0; i < 10; i++) { client.Tick(); server.Tick(); }
            Assert.Equal(RunMode.Normal, client.World!.Global.Mode);
            Assert.Equal("test_street", client.World.Map.Id);
            Assert.NotEqual(3u, client.World.Seed);
        }

        [Fact]
        public void TheDailySeed_ReachesTheClients_WithItsDateAndStamps()
        {
            var hub = new LoopbackHub();
            var day = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3, UtcNow = () => day }, hub);
            var host = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            for (int i = 0; i < 10; i++) { host.Tick(); server.Tick(); }
            var guest = new ClientRuntime(Shared.Content, hub.CreateClient(), "Bob");
            for (int i = 0; i < 10; i++) { host.Tick(); guest.Tick(); server.Tick(); }
            Assert.Equal(0u, guest.ToggleDaily());
            Assert.NotEqual(0u, host.ToggleDaily());
            for (int i = 0; i < 20; i++) { host.Tick(); guest.Tick(); server.Tick(); }
            foreach (var w in new[] { host.World!, guest.World! })
            {
                Assert.Equal(RunMode.Daily, w.Global.Mode);
                Assert.Equal(RunModes.DailySeed(RunModes.Day(day)), w.Seed);
                Assert.StartsWith("Daily 2026-11-27 · PM1-SMALL-", HudText.WorldTracker(w));
                Assert.Equal("Stamps: Rush Hour, Heavy Load   ·   score ×1.21", HudText.StampsTracker(w));
            }
            Assert.Equal("[P] practice   ·   [O] leave the daily   ·   [Ctrl+V] a seed", HudText.ModeKeys(host.World!));
            Assert.Equal(0u, host.ToggleStamp());
            Assert.NotEqual(0u, host.ToggleDaily());
            for (int i = 0; i < 20; i++) { host.Tick(); guest.Tick(); server.Tick(); }
            Assert.Equal((RunMode.Normal, (byte)0), (guest.World!.Global.Mode, guest.World.Global.Stamps));
            Assert.Equal(0, host.ContainerDesyncs + host.BadPackets + guest.ContainerDesyncs + guest.BadPackets);
        }

        [Fact]
        public void APastedSeedString_ChoosesItsWorldSeedAndStamps_WithinTheHostsRank()
        {
            // Doc 01 §5.3: the string copied at Results, pasted in another Lobby (design choice 166).
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3 }, hub);
            var host = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            void Run(int ticks) { for (int i = 0; i < ticks; i++) { host.Tick(); server.Tick(); } }
            Run(10);
            Assert.NotEqual(0u, host.TogglePractice());
            Run(10);
            Assert.Equal("Seed PM1-SMALL-0000002A-CM.RH pasted", host.PasteSeed("pm1-small-2a-cm.rh"));
            Run(20);
            var w = host.World!;
            Assert.Equal((RunMode.Normal, "small_island", 42u), (w.Global.Mode, w.Map.Id, w.Seed));
            Assert.Equal("PM1-SMALL-0000002A-CM.RH", HudText.ShareString(w));
            Assert.Equal("PM1-SMALL-0000002A-CM.RH", host.SeedToCopy());
            // Double Raids is tier 2: rank 1 may not put it on, and nothing changes.
            Assert.Equal("Not pasted: Double Raids needs Postal Rank 6", host.PasteSeed("PM1-TEST-00000005-DR"));
            Assert.Equal("Not pasted: no world 'LARGE'", host.PasteSeed("PM1-LARGE-00000005"));
            Run(10);
            Assert.Equal(42u, host.World!.Seed);
            Assert.Equal("Seed PM1-TEST-00000005 pasted", host.PasteSeed("PM1-TEST-00000005"));
            Run(10);
            Assert.Equal(("test_street", 5u, (byte)0), (host.World!.Map.Id, host.World.Seed, host.World.Global.Stamps));
            Assert.Equal(0, host.ContainerDesyncs + host.BadPackets);
        }

        [Fact]
        public void TheUnlocks_AreWhatTheRankOpens_ReadFromTheContent()
        {
            var c = Shared.Content;
            var one = RankUnlocks.For(c, 1);
            static string Of(ProfileUnlocks u) =>
                $"{string.Join(",", u.Kits)} / {string.Join(",", u.Archetypes)} / {u.StampTiers} / {string.Join(",", u.PerkPools)} / {u.Rerolls}";
            Assert.Equal("land / small_island / 1 / carrier,postal / 1", Of(one));
            var eight = RankUnlocks.For(c, 8);
            Assert.Equal("land,sea / small_island / 2 / carrier,postal,facility,defense / 2", Of(eight));
            Assert.Equal(new[] { "land", "sea", "pipes" }, RankUnlocks.For(c, 12).Kits);
            Assert.Equal(3, RankUnlocks.For(c, 12).StampTiers);

            Assert.Equal(new[] { "the Sea kit" }, RankUnlocks.Revealed(c, 1, 2));
            Assert.Empty(RankUnlocks.Revealed(c, 2, 3));
            Assert.Equal(new[] { "a second Draft reroll" }, RankUnlocks.Revealed(c, 3, 4));
            Assert.Equal(new[] { "facility perks in the Draft" }, RankUnlocks.Revealed(c, 4, 5));
            Assert.Equal(new[] { "Postage Stamps tier 2" }, RankUnlocks.Revealed(c, 5, 6));
            Assert.Equal(new[] { "defense perks in the Draft" }, RankUnlocks.Revealed(c, 7, 8));
            Assert.Equal(new[] { "the Sea kit", "the Pipes kit", "Postage Stamps tier 2", "Postage Stamps tier 3", "facility perks in the Draft", "defense perks in the Draft", "a second Draft reroll" },
                RankUnlocks.Revealed(c, 1, 12));

            // Written to the file and read back.
            var profile = MetaProfile.Create("Ada");
            profile.Unlocks = eight;
            var back = MetaProfile.FromJson(profile.ToJson());
            Assert.Equal(Of(eight), Of(back.Unlocks));
        }

        private sealed class FailingStore : IProfileStore
        {
            public bool Fail = true;
            public string? Json;
            public string? Read() => Json;
            public void Write(string json)
            {
                if (Fail) throw new IOException("disk full");
                Json = json;
            }
            public void SetAside() { }
        }

        [Fact]
        public void AFailedSaveIsReported_AndTheNextOneTriesAgain()
        {
            var hub = new LoopbackHub();
            var client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            var store = new FailingStore();
            var tracker = new ProfileTracker(client, MetaProfile.Create("Ada"), store);
            tracker.Save();
            Assert.Equal("disk full", tracker.SaveProblem);
            Assert.Null(store.Json);
            store.Fail = false;
            tracker.Save();
            Assert.Null(tracker.SaveProblem);
            Assert.NotNull(store.Json);
        }
    }
}
