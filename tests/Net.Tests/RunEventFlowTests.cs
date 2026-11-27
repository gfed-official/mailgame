using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Run events on the client (doc 10 §6; design choice 162): the live ones and Street Festival's street in the global
    /// block, the feed's line and the banner as one begins, the tracker row while it is live, its numbers on the client's
    /// team sheet and the closed street in the client's prediction.
    /// </summary>
    public class RunEventFlowTests
    {
        private static (ServerRuntime Server, ClientRuntime Client, EventFeed Feed, System.Action<int> Run) Start(string? forced, int shift)
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, RunEvents = true, ForcedEvent = forced }, hub);
            var ada = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.NoQuota, shift);
            var feed = new EventFeed();
            ada.EventReceived += ev => feed.OnEvent(ada.World!, ev);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            return (server, ada, feed, Run);
        }

        [Fact]
        public void AStreetFestival_IsSaidInTheFeed_OnTheBanner_AndTheTracker_AndClosesItsStreetInThePrediction()
        {
            var (_, ada, feed, run) = Start("festival", 2);
            var a = ada.World!;
            Assert.Equal(RunPhase.Prep, a.Global.Phase);
            Assert.Contains(feed.Entries, e => e.Text == "Street Festival: Larch Lane is closed to vehicles   ·   Letters there pay ×2");
            Assert.Equal("STREET FESTIVAL", HudText.RunEventBanner(a));
            run(30);
            Assert.Equal("Street Festival   ·   Larch Lane ×2", HudText.EventTracker(a));
            var street = a.Map.GetStreet(1)!;
            Assert.Same(street, a.FestivalStreet);
            Assert.Equal((street.Rect.X, street.Rect.Y, street.Rect.W, street.Rect.H, 0.2f), (a.RoadClosure.Rect.X, a.RoadClosure.Rect.Y, a.RoadClosure.Rect.W, a.RoadClosure.Rect.H, a.RoadClosure.Mult));
            // The banner's few seconds pass.
            run((int)(HudText.EventBannerSeconds * 30) + 10);
            Assert.Equal("", HudText.RunEventBanner(a));
            Assert.Equal(0, ada.ContainerDesyncs + ada.BadPackets);
        }

        [Fact]
        public void ThePostalInspector_IsComingInTheFeed_ThenLiveOnTheClientsSheet()
        {
            var (server, ada, feed, run) = Start(null, 2);
            var a = ada.World!;
            Assert.NotEqual(0u, ada.PressGo());
            run(10);
            Assert.Equal(RunPhase.Delivery, a.Global.Phase);
            var sim = server.World;
            sim.Economy.AddComplaint(90);
            sim.Run.PhaseEndTick = sim.Tick + 1;
            run(10);
            Assert.Equal(RunPhase.Payday, a.Global.Phase);
            Assert.Contains(feed.Entries, e => e.Bad && e.Text == "Complaint 90: the Postal Inspector comes next shift   ·   deliveries pay 10% less");
            sim.Run.PhaseEndTick = sim.Tick + 1;
            run(30);
            Assert.Equal((3, RunPhase.Prep), (a.Global.Shift, a.Global.Phase));
            Assert.True(a.EventLive(Shared.Content.RunEvent("inspector")));
            Assert.Contains("Postal Inspector   ·   deliveries pay 10% less", HudText.EventTracker(a));
            Assert.Equal(0.9f, a.LocalStat(Stat.DeliveryEarnings, 1f), 3);
        }
    }
}
