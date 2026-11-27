using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 02 §3.5 Apartment Complex (design choice 170): "4 × 6 lot; one Mail Room entrance with units slots (6–12) … at
    /// most 1 per medium town, appears in shift 4–5 districts"; doc 03 §2.1: all non-Cargo kinds for any unit, the unit
    /// matching the item's.
    /// </summary>
    public class ApartmentTests
    {
        private static ContentDb Db => TestContent.Db;

        private static IEnumerable<WorldMap> Maps()
        {
            for (uint s = 1; s <= 30; s++) yield return WorldGenerator.Generate(Db, s);
            for (uint s = 1; s <= 12; s++) yield return LargeIslandGenerator.Generate(Db, s);
        }

        [Fact]
        public void AComplex_StandsOnAWideLotOfADistrictOfShiftFourOrFive_WithSixToTwelveUnits()
        {
            int small = 0, large = 0;
            var path = new List<(float X, float Z)>();
            foreach (var map in Maps())
            {
                var flats = map.Destinations.Where(d => d.IsApartment).ToList();
                Assert.True(flats.Count <= 1, $"{map.Name}: {flats.Count} complexes");
                if (map.Id == LargeIslandGenerator.LargeIsland) Assert.Single(flats);
                foreach (var a in flats)
                {
                    if (map.Id == WorldGenerator.SmallIsland) small++; else large++;
                    Assert.InRange(a.Address.District, 4, 5);
                    Assert.InRange(a.Units, Apartments.MinUnits, Apartments.MaxUnits);
                    Assert.Equal((6, 4), (a.Lot.W, a.Lot.H));
                    Assert.Equal((5, 3), (a.Building.W, a.Building.H));
                    for (int z = a.Building.Y; z < a.Building.Y + a.Building.H; z++)
                        for (int x = a.Building.X; x < a.Building.X + a.Building.W; x++) Assert.Equal(TileType.Building, map.GetTile(x, z));
                    // No other lot on it; its entrance walked to from the Post Office (or a ferry's far end).
                    Assert.DoesNotContain(map.Destinations, d => d != a && d.Lot.X < a.Lot.X + a.Lot.W && a.Lot.X < d.Lot.X + d.Lot.W && d.Lot.Y < a.Lot.Y + a.Lot.H && a.Lot.Y < d.Lot.Y + d.Lot.H);
                    Assert.Null(WorldGenerator.Validate(map));
                    Assert.Equal(a.Units, Enumerable.Range(1, 20).Count(u => a.Takes(new AddressId(a.Address.Key, (byte)u))));
                    Assert.False(a.Takes(a.Address));
                    Assert.Same(a, map.FindDestination(new AddressId(a.Address.Key, 2)));
                    Assert.Equal($"{a.Address.Number} {a.Street.Name}, Unit 2", map.FormatAddress(new AddressId(a.Address.Key, 2)));
                }
            }
            // Most Small Islands find room for one; every Large Island has one in its second town.
            Assert.InRange(small, 20, 30);
            Assert.Equal(12, large);
        }

        [Fact]
        public void TheMailRoom_TakesAnyKindButCargo_ForItsOwnUnits_AndAWrongUnitIsAMisdelivery()
        {
            var map = Maps().First(m => m.Destinations.Any(d => d.IsApartment));
            var a = map.Destinations.First(d => d.IsApartment);
            var house = map.Houses.First(d => !d.IsApartment);
            DeliveryResult At(string kind, AddressId to) => DeliveryRules.Evaluate(TestContent.Mail(kind, to, 1, 5), Db.MailKind(kind), a, 5, 0, Db);
            var unit3 = new AddressId(a.Address.Key, 3);
            foreach (string kind in new[] { "postcard", "letter", "small", "medium", "large" })
                Assert.Equal(DeliveryOutcome.Delivered, At(kind, unit3).Outcome);
            Assert.Equal(DeliveryOutcome.WontFitHere, At("cargo", unit3).Outcome);
            // A unit it does not have, the house next door: the resident keeps it (doc 03 §2.2 rule 5).
            Assert.Equal(DeliveryOutcome.Misdelivered, At("letter", new AddressId(a.Address.Key, (byte)(a.Units + 1))).Outcome);
            Assert.Equal(DeliveryOutcome.Misdelivered, At("letter", house.Address).Outcome);
        }

        [Fact]
        public void ItsMail_IsForItsUnits_EveryAddressAlike_FromItsDistrictsShift()
        {
            uint seed = Enumerable.Range(1, 30).Select(s => (uint)s).First(s => WorldGenerator.Generate(Db, s).Destinations.Any(d => d.IsApartment && d.Address.District == 4));
            var world = new SimWorld(Db, new SimSettings { Seed = seed, MapId = WorldGenerator.SmallIsland, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, 4);
            world.Submit(host.Id, SimRequest.StartRun(1));
            world.Step();
            world.Submit(host.Id, SimRequest.SetReady(2, true));
            world.Step();
            Assert.Equal((RunPhase.Delivery, 4), (world.Run.Phase, world.Run.Shift));
            var a = world.Map.Destinations.First(d => d.IsApartment);
            int all = 0, theirs = 0;
            var units = new HashSet<int>();
            for (int i = 0; i < 6 && world.Run.Phase == RunPhase.Delivery; i++)
            {
                SimDriver.StepTicks(world, 30 * 30);
                foreach (var e in world.Intake.Entries.ToList())
                {
                    if (e.Stack.Kind != StackKind.Mail) continue;
                    foreach (var m in e.Stack.Mail)
                    {
                        all++;
                        if (m.Address.Key != a.Address.Key) continue;
                        theirs++;
                        Assert.InRange(m.Address.Unit, 1, a.Units);
                        units.Add(m.Address.Unit);
                    }
                    // Taken out, so the Intake keeps room for more.
                    world.Intake.Remove(e.EntryId);
                }
            }
            Assert.True(theirs > 0, "no mail for the complex");
            Assert.True(units.Count >= 3, $"mail for only {units.Count} of its units");
            // Weighed by its units (doc 03 §1.3 "every unlocked address equally"): well over one house's share.
            int open = world.Map.Destinations.Count(d => !d.IsDock && world.Map.OpensAt(d) <= 4);
            double share = theirs / (double)all, oneHouse = 1.0 / open;
            Assert.True(share > oneHouse * 2.5, $"the complex had {share:P1} of the mail, one house's share is {oneHouse:P1}");
        }
    }
}
