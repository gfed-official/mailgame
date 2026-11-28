using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>Doc 09 §2.9: the Payday card's "upcoming district preview and mail forecast for the next shift".</summary>
    public class PaydayPreviewTests
    {
        private static (ClientRuntime Client, System.Action<int> Run) Start(string mapId, uint seed, int shift)
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = seed, MapId = mapId, Raids = false, Draft = false }, hub);
            var ada = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada", PlaytestFlags.NoQuota | PlaytestFlags.NoTimer, shift);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            Assert.Equal(RunPhase.Delivery, ada.World!.Global.Phase);
            // The clock stood (--playtest-no-timer): the host's G ends the shift.
            Assert.NotEqual(0u, ada.PressGo());
            for (int i = 0; i < 300 && ada.World.Global.Phase != RunPhase.Payday; i++) Run(1);
            Assert.Equal(RunPhase.Payday, ada.World.Global.Phase);
            Run(5);
            return (ada, Run);
        }

        [Fact]
        public void AtPayday_TheCardSaysWhatOpensNextShift_ItsHousesStreetsAndNewMail()
        {
            var (ada, _) = Start(WorldGenerator.SmallIsland, 5, 1);
            var w = ada.World!;
            var lines = HudText.SummaryLines(w, w.LastSummary!);
            int houses = w.Map.Destinations.Count(d => d.Type.Id == "house" && w.Map.OpensAt(d) == 2);
            string next = Assert.Single(lines, l => l.StartsWith("Next: ", System.StringComparison.Ordinal));
            Assert.StartsWith($"Next: District 2 opens in shift 2  ·  {houses} houses on ", next);
            // Postcards and Large Packages first come in shift 2 (content's unlockShift).
            Assert.Equal("New mail: Postcards, Large Packages", lines[^3]);
            Assert.Same(next, lines[^4]);
            // The mail forecast for the next shift (design choice 177): by kind, most first, then by district on its own line (N25).
            Assert.Equal(2, w.MailForecast!.Shift);
            Assert.StartsWith("Mail next shift: ~", lines[^2]);
            Assert.Contains(" Postcards", lines[^2]);
            Assert.DoesNotContain("District", lines[^2]);
            Assert.StartsWith("By district: District 1 ~", lines[^1]);
            Assert.Contains(" · District 2 ~", lines[^1]);
        }

        /// <summary>
        /// The 5b33eb0 playtest's N25: at Payday of shift 3 on the Large Island the one forecast line (five kinds, four
        /// districts) ran off both edges of the screen at 1280×720. The kinds and the districts are two lines, each short.
        /// </summary>
        [Fact]
        public void OnTheLargeIsland_TheForecastIsTwoLines_KindsThenDistricts_EachShort()
        {
            var (ada, _) = Start(LargeIslandGenerator.LargeIsland, 1, 3);
            var w = ada.World!;
            var lines = HudText.SummaryLines(w, w.LastSummary!);
            Assert.Equal(4, w.MailForecast!.Shift);
            string kinds = Assert.Single(lines, l => l.StartsWith("Mail next shift: ~", System.StringComparison.Ordinal));
            string districts = Assert.Single(lines, l => l.StartsWith("By district: ", System.StringComparison.Ordinal));
            Assert.Equal(lines.IndexOf(kinds) + 1, lines.IndexOf(districts));
            Assert.DoesNotContain("District", kinds);
            for (int d = 1; d <= 4; d++) Assert.Contains($"District {d} ~", districts);
            // The whole forecast is still the map's line (the Prep map shrinks its own to fit).
            Assert.Equal(HudText.MailForecastCounts(w, w.MailForecast), kinds.Substring("Mail next shift: ".Length) + HudText.Dot + districts.Substring("By district: ".Length));
            // At 18 px a character is about 9 px: 110 of them stay well inside 1280.
            Assert.All(lines, l => Assert.True(l.Length <= 110, $"\"{l}\" is {l.Length} characters"));
        }

        /// <summary>Doc 01 §2.1 "Preview: … a mail forecast (counts by size class and district)": the map's line in Prep.</summary>
        [Fact]
        public void InPrep_TheMapsLineIsTheShiftsMailForecast_UntilMailIsInHand()
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 5, MapId = WorldGenerator.SmallIsland, Raids = false, Draft = false }, hub);
            var ada = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada", PlaytestFlags.NoQuota | PlaytestFlags.NoTimer, 3);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            for (int i = 0; i < 300 && ada.World!.Global.Phase != RunPhase.Prep; i++) Run(1);
            Run(5);
            var w = ada.World!;
            Assert.Equal((RunPhase.Prep, 3), (w.Global.Phase, (int)w.Global.Shift));
            var truth = server.World.LastMailForecast!;
            Assert.Equal(3, truth.Shift);
            Assert.Equal(truth.Counts, w.MailForecast!.Counts);
            // Districts 1-3 open in shift 3; every count for one of them.
            Assert.All(truth.Counts, c => Assert.InRange(c.District, 1, 3));
            string line = HudText.MapForecast(w);
            Assert.StartsWith("Mail forecast, shift 3: ~", line);
            Assert.Contains(" Letters", line);
            Assert.Contains("District 3 ~", line);
            int total = truth.Counts.Sum(c => c.Count);
            Assert.InRange(total, 20, 2000);
            // In Delivery the line is the way to the mail's house again.
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            Assert.Equal("", HudText.MapForecast(w));
        }

        [Fact]
        public void OnALargeIsland_TheFarTownSaysItIsReachedByTheFerry()
        {
            uint seed = Enumerable.Range(1, 20).Select(s => (uint)s).First(s => LargeIslandGenerator.Generate(Shared.Content, s).FerryLanes.Count > 0);
            var (ada, _) = Start(LargeIslandGenerator.LargeIsland, seed, 2);
            var w = ada.World!;
            var upcoming = HudText.Upcoming(w, w.LastSummary!);
            Assert.StartsWith("Next: District 3 opens in shift 3", upcoming[0]);
            Assert.StartsWith("By the ferry", upcoming[1]);
            // Cargo's first shift is 3, but nothing takes it before the docks open in shift 4.
            Assert.DoesNotContain("Cargo", upcoming[1]);
        }
    }
}
