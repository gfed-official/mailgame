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
            Assert.Null(store.Json);

            var empty = new MemoryProfileStore();
            var fresh = ProfileTracker.LoadOrCreate(empty, "Postie", out problem);
            Assert.Null(problem);
            Assert.Equal("Postie", fresh.DisplayName);
            Assert.Null(empty.Bad);

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
                Assert.False(File.Exists(path));
                Assert.Equal("{ broken", File.ReadAllText(path + ".bad"));
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
            Assert.Equal($"RANK UP!   Postal Rank {rank}   ·   {(480 + own.Xp) % 500} / 500 XP", lines[lines.Count - 1]);
            Assert.Equal(lines.Count - 1, HudText.ResultsLines(world, results).Count);

            // What is on disk is what is in memory.
            Assert.Equal(profile.ToJson(), store.Json);
            Assert.Equal(profile.RankXp, MetaProfile.FromJson(store.Json!).RankXp);
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
