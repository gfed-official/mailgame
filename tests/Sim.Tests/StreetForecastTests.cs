using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 10 §4.3 Forecast Desk / the shop's Extended Forecast (design choice 176, mail by street), playtest N24: on Large
    /// Island a street that cross streets cut into pieces is several street ids under one name, and the forecast listed
    /// "Larch Lane" twice, every street at the same ~14 (spread evenly per house, while the draw weighs an Apartment
    /// Complex by its units). The forecast is by name, and what a Delivery really brings each name.
    /// </summary>
    public class StreetForecastTests
    {
        private static ContentDb Db => TestContent.Db;

        private static WorldMap LargeIsland(uint seed) =>
            new SimWorld(Db, new SimSettings { Seed = seed, MapId = LargeIslandGenerator.LargeIsland, Raids = false, Draft = false }).Map;

        [Theory]
        [InlineData(1u)]
        [InlineData(2u)]
        [InlineData(3u)]
        public void EachStreetNameOnce_UnderItsFirstId(uint seed)
        {
            var map = LargeIsland(seed);
            Assert.Contains(map.Streets.GroupBy(s => s.Name), g => g.Count() > 1);
            var spawner = new MailSpawner(Db, map, new GridContainer(900, Db.Container("po_intake")), new EntryIdAllocator(), seed);
            var forecast = spawner.Forecast(3, 20000);
            var names = forecast.Keys.Select(id => map.GetStreet(id)!.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            foreach (var id in forecast.Keys) Assert.Equal(id, map.Streets.Where(s => s.Name == map.GetStreet(id)!.Name).Min(s => s.Id));
        }

        [Theory]
        [InlineData(1u)]
        [InlineData(4u)]
        public void TheForecastIsWhatDeliveriesBringEachStreet(uint seed)
        {
            var map = LargeIsland(seed);
            const int shift = 3, value = 40000, deliveries = 12;
            var expected = new MailSpawner(Db, map, new GridContainer(900, Db.Container("po_intake")), new EntryIdAllocator(), seed).Forecast(shift, value);
            Assert.True(expected.Values.Distinct().Count() > 2, "a forecast with every street alike");

            var seen = new Dictionary<string, int>();
            for (int run = 0; run < deliveries; run++)
            {
                var intake = new GridContainer(900, Db.Container("po_intake"));
                var spawner = new MailSpawner(Db, map, intake, new EntryIdAllocator(), seed * 100 + (uint)run);
                uint duration = SimConstants.SecondsToTicks(240);
                spawner.BeginShift(shift, 0, duration, value);
                int complaint = 0;
                for (uint t = 0; t < duration + 60 * SimConstants.TickRate && (t < duration || spawner.BacklogCount > 0); t++)
                {
                    spawner.Step(t, ref complaint);
                    foreach (var e in intake.Entries.ToList())
                    {
                        intake.Remove(e.EntryId);
                        string name = map.GetStreet(e.Stack.Address.Street)!.Name;
                        seen[name] = (seen.TryGetValue(name, out int n) ? n : 0) + e.Stack.Count;
                    }
                }
                Assert.Equal(0, spawner.BacklogCount);
            }

            double total = expected.Values.Sum() * deliveries;
            Assert.InRange(seen.Values.Sum(), total * 0.9, total * 1.1);
            foreach (var (id, each) in expected)
            {
                double want = each * deliveries;
                if (want < 150) continue;
                int got = seen.TryGetValue(map.GetStreet(id)!.Name, out int n) ? n : 0;
                Assert.True(Math.Abs(got - want) <= 0.25 * want, $"{map.GetStreet(id)!.Name}: forecast {want:0}, delivered {got}");
            }
        }
    }
}
