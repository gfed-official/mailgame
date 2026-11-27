using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The Post Office's HP on the client (doc 05 §5, doc 01 §7; design choice 137): its health in the
    /// snapshot, each blow heard with what it took (the numbers and the under-attack marker), the HUD's
    /// tracker and the feed, and the run over when it falls.
    /// </summary>
    public class PostOfficeFlowTests
    {
        [Fact]
        public void BlowsOnThePostOffice_AreSeen_AndItsFallEndsTheRun()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var ada = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 1);
            var feed = new EventFeed();
            ada.EventReceived += ev => feed.OnEvent(ada.World!, ev);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            // Ready: Delivery, where it does not mend.
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            var sim = server.World;
            var a = ada.World!;
            Assert.Equal(RunPhase.Delivery, a.Global.Phase);
            Assert.Equal((3000, 3000), (a.PostOfficeHp, a.PostOfficeMaxHp));
            Assert.Equal("", HudText.PostOfficeTracker(a));

            sim.DamagePostOffice(40f, sim.SpawnEnemy(content.Enemy("giant"), 60f, 28f));
            Run(3);
            Assert.Equal(2960, a.PostOfficeHp);
            var hit = Assert.Single(a.ConstructHits);
            Assert.Equal((40, false, "Post Office"), (hit.Amount, hit.Broke, hit.Name));
            Assert.Null(hit.Def);
            Assert.Equal("Post Office under attack", HudText.UnderAttack(hit));
            // UI polish plan item 10: a tag read at a glance, and how far.
            Assert.Equal("PO  42 m", HudText.MarkerTag(hit, 41.6f));
            Assert.Same(hit, a.UnderAttack(a.Local.X, a.Local.Z));
            Assert.Equal("Post Office   2960 / 3000 HP   ·   under attack", HudText.PostOfficeTracker(a));
            Assert.Contains(feed.Entries, e => e.Bad && e.Text == HudText.PostOfficeUnderAttack);
            // The snapshot's word catches up; a second blow soon after is no second line in the feed.
            Run(30);
            Assert.Equal((ushort)2960, a.Global.PostOfficeHp);
            sim.DamagePostOffice(40f);
            Run(3);
            Assert.Equal(2920, a.PostOfficeHp);
            Assert.Single(feed.Entries, e => e.Text == HudText.PostOfficeUnderAttack);
            Run(4 * 30);
            Assert.Equal("Post Office   2920 / 3000 HP", HudText.PostOfficeTracker(a));

            // It falls: the run is over at once.
            sim.DamagePostOffice(5000f);
            Run(5);
            Assert.Equal(0, a.PostOfficeHp);
            Assert.Contains(feed.Entries, e => e.Bad && e.Text == HudText.PostOfficeFallen);
            Assert.Equal(RunPhase.Results, a.Global.Phase);
            Assert.Equal(RunOutcome.PostOfficeLost, a.Results!.Outcome);
            Assert.Equal("RUN OVER — the Post Office fell in shift 1", HudText.ResultsLines(a, a.Results).First());
            Assert.Equal("Post Office fallen", HudText.UnderAttack(a.ConstructHits.Last()));
            Assert.Equal(0, ada.ContainerDesyncs + ada.BadPackets);
        }
    }
}
