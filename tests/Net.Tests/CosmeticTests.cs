using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Client.Meta;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The cosmetics of doc 10 §8 (design choice 174): uniform and cap tints, opened by rank and achievements, worn for all to see.</summary>
    public class CosmeticTests
    {
        private static ContentDb Content => Shared.Content;

        [Fact]
        public void TheContentHasEightUniformsAndSixCaps_AfterTheDefaults()
        {
            Assert.Equal(9, Content.Uniforms.Count);
            Assert.Equal(7, Content.Caps.Count);
            Assert.Equal(("uniform_team", ""), (Content.Uniforms[0].Id, Content.Uniforms[0].Colour));
            Assert.Equal(("cap_match", ""), (Content.Caps[0].Id, Content.Caps[0].Colour));
            // Every achievement opens something.
            foreach (string a in CosmeticDef.Achievements)
                Assert.Contains(RankUnlocks.Cosmetics(Content), c => c.Achievement == a);
        }

        [Fact]
        public void RanksFromFifteenAndAchievementsOpenThem_AndTheProfileKeepsThem()
        {
            Assert.Equal(new[] { "uniform_team", "cap_match" }, Looks.Open(Content, 14, new List<string>()));
            Assert.Equal(new[] { "uniform_team", "uniform_post_red", "cap_match" }, Looks.Open(Content, 15, new List<string>()));
            Assert.Contains("cap_red", Looks.Open(Content, 16, new List<string>()));
            var gold = Looks.Open(Content, 1, new List<string> { CosmeticDef.FirstVictory });
            Assert.Equal(new[] { "uniform_team", "cap_match", "cap_gold" }, gold);
            Assert.Equal(new[] { "the Post Red uniform" }, RankUnlocks.Revealed(Content, 14, 15));

            var profile = MetaProfile.Create("Ada");
            profile.Uniform = "uniform_post_red";
            profile.Cap = "cap_gold";
            profile.Achievements.Add(CosmeticDef.FirstVictory);
            var back = MetaProfile.FromJson(profile.ToJson());
            Assert.Equal(("uniform_post_red", "cap_gold"), (back.Uniform, back.Cap));
            Assert.Equal(new[] { CosmeticDef.FirstVictory }, back.Achievements);
            Assert.Equal(profile.ToJson(), back.ToJson());
        }

        [Fact]
        public void ARunEarnsItsAchievements_OnceEach_AndAPracticeRunNone()
        {
            var profile = MetaProfile.Create("Ada");
            var win = new RunResults { Outcome = RunOutcome.Victory, ShiftsCompleted = 5, Deliveries = 80, Misdeliveries = 2 };
            byte threeStamps = 0b1011;
            Assert.Equal(new[] { CosmeticDef.FirstVictory, CosmeticDef.StampedVictory }, Looks.Earned(win, RunMode.Normal, threeStamps, profile));
            Assert.Equal(new[] { CosmeticDef.FirstVictory }, Looks.Earned(win, RunMode.Normal, 0b11, profile));
            Assert.Empty(Looks.Earned(win, RunMode.Practice, threeStamps, profile));
            profile.Achievements.Add(CosmeticDef.FirstVictory);
            Assert.Empty(Looks.Earned(win, RunMode.Normal, 0, profile));

            // A clean run: three shifts or more, deliveries, nobody's misdelivery; and the thousandth delivery.
            var clean = new RunResults { Outcome = RunOutcome.QuotaMissed, ShiftsCompleted = 3, Deliveries = 40 };
            Assert.Equal(new[] { CosmeticDef.CleanRun }, Looks.Earned(clean, RunMode.Daily, 0, profile));
            clean.ShiftsCompleted = 2;
            Assert.Empty(Looks.Earned(clean, RunMode.Normal, 0, profile));
            profile.Deliveries = 1000;
            Assert.Equal(new[] { CosmeticDef.Deliveries1000 }, Looks.Earned(clean, RunMode.Normal, 0, profile));
        }

        [Fact]
        public void TheLookIsWornForAll_FromTheProfileOnJoining_AndPickedInTheLobbyOnly()
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Content, new SimSettings { Seed = 3, Raids = false, Draft = false }, hub);
            var clients = new List<ClientRuntime>();
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var c in clients) c.Tick();
                    server.Tick();
                }
            }
            // Ada is rank 20 and has won once; what she wore last goes with her as she joins.
            var adaProfile = MetaProfile.Create("Ada");
            adaProfile.RankXp = 19 * 500;
            adaProfile.Uniform = "uniform_royal_blue";
            adaProfile.Achievements.Add(CosmeticDef.FirstVictory);
            var ada = new ClientRuntime(Content, hub.CreateClient(), "Ada", rank: 20);
            var adaStore = new MemoryProfileStore();
            var adaLooks = new Looks(ada, new ProfileTracker(ada, adaProfile, adaStore));
            clients.Add(ada);
            var bo = new ClientRuntime(Content, hub.CreateClient(), "Bo");
            var boLooks = new Looks(bo, new ProfileTracker(bo, MetaProfile.Create("Bo"), new MemoryProfileStore()));
            clients.Add(bo);
            Run(20);
            var adaId = ada.World!.LocalId;
            var royal = Content.Uniforms.Single(u => u.Id == "uniform_royal_blue");
            Assert.Equal(royal.Index, bo.World!.Players[adaId].Uniform);
            Assert.Equal(0, bo.World.Players[adaId].Cap);
            Assert.Equal("Look: Royal Blue, Matching cap   ·   [U] [J]", HudText.LookTracker(Content, adaLooks));
            Assert.Equal("Look: Team colour, Matching cap   ·   more from Postal Rank 15", HudText.LookTracker(Content, boLooks));
            Assert.False(boLooks.Next(CosmeticKind.Uniform));

            // J: the next cap open to her, rank 16's red; again and again, round to the Gold cap of her Victory.
            Assert.True(adaLooks.Next(CosmeticKind.Cap));
            Run(10);
            Assert.Equal("cap_red", adaProfile.Cap);
            Assert.Equal(Content.Caps.Single(c => c.Id == "cap_red").Index, bo.World.Players[adaId].Cap);
            Assert.Equal(adaProfile.ToJson(), adaStore.Json);
            var caps = new List<string>();
            for (int i = 0; i < 6; i++) { adaLooks.Next(CosmeticKind.Cap); caps.Add(adaLooks.Cap.Id); }
            // The Spotless White waits for a clean run, the Black cap for rank 22.
            Assert.Equal(new[] { "cap_navy", "cap_green", "cap_gold", "cap_match", "cap_red", "cap_navy" }, caps);

            // Out of the Lobby the look stands.
            ada.PressGo();
            Run(10);
            Assert.Equal(RunPhase.Prep, ada.World.Global.Phase);
            Assert.False(adaLooks.Next(CosmeticKind.Uniform));
            Assert.Equal(royal.Index, bo.World.Players[adaId].Uniform);
        }
    }
}
