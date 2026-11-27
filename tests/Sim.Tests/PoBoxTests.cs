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
    /// Doc 02 §3.5 PO Box Bank (design choice 171): "inside PO district; 8–16 boxes"; doc 03 §2.1: postcards and letters
    /// addressed to a box number, one interaction point.
    /// </summary>
    public class PoBoxTests
    {
        private static ContentDb Db => TestContent.Db;

        [Fact]
        public void EveryIsland_HasOneBankBeforeThePostOffice_InItsDistrict_WithEightToSixteenBoxes()
        {
            var path = new List<(float X, float Z)>();
            var maps = Enumerable.Range(1, 30).Select(s => WorldGenerator.Generate(Db, (uint)s))
                .Concat(Enumerable.Range(1, 8).Select(s => LargeIslandGenerator.Generate(Db, (uint)s)));
            foreach (var map in maps)
            {
                var bank = Assert.Single(map.Destinations, d => d.IsPoBoxBank);
                Assert.Equal(1, bank.Address.District);
                Assert.Equal(1, map.OpensAt(bank));
                Assert.InRange(bank.Units, PoBoxes.MinBoxes, PoBoxes.MaxBoxes);
                var po = map.PostOffice;
                Assert.InRange(MathF.Abs(bank.MailboxX - po.SpawnPadX) + MathF.Abs(bank.MailboxZ - po.SpawnPadZ), 2f, 16f);
                Assert.True(TilePathfinder.Find(map, po.SpawnPadX, po.SpawnPadZ, bank.MailboxX, bank.MailboxZ, path), $"{map.Name}: no walk to the boxes");
                Assert.Equal(1, map.TileDistrict[(int)(bank.MailboxZ / 2f) * map.Cols + (int)(bank.MailboxX / 2f)]);
                // Its number is past the street's houses: no label twice.
                Assert.Equal(map.Destinations.Count, map.Destinations.Select(d => map.FormatAddress(d.MailAddress)).Distinct().Count());
                Assert.Equal($"PO Box 7, {bank.Address.Number} {bank.Street.Name}", map.FormatAddress(new AddressId(bank.Address.Key, 7)));
                Assert.Null(WorldGenerator.Validate(map));
            }
        }

        [Fact]
        public void TheBank_TakesPostcardsAndLettersForItsBoxes_NothingBigger()
        {
            var map = WorldGenerator.Generate(Db, 3);
            var bank = map.Destinations.Single(d => d.IsPoBoxBank);
            DeliveryResult At(string kind, AddressId to) => DeliveryRules.Evaluate(TestContent.Mail(kind, to, 1, 2), Db.MailKind(kind), bank, 2, 0, Db);
            var box = new AddressId(bank.Address.Key, 5);
            Assert.Equal(DeliveryOutcome.Delivered, At("letter", box).Outcome);
            Assert.Equal(DeliveryOutcome.Delivered, At("postcard", box).Outcome);
            foreach (string kind in new[] { "small", "medium", "large", "cargo" })
                Assert.Equal(DeliveryOutcome.WontFitHere, At(kind, box).Outcome);
            Assert.Equal(DeliveryOutcome.Misdelivered, At("letter", new AddressId(bank.Address.Key, (byte)(bank.Units + 1))).Outcome);
            Assert.Equal(DeliveryOutcome.Misdelivered, At("letter", map.Houses.First(d => !d.IsPoBoxBank).Address).Outcome);
        }

        [Fact]
        public void ItsMail_IsLetters_ForItsBoxes_AsOneAddressAmongTheHouses()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 9, MapId = WorldGenerator.SmallIsland, Raids = false, Draft = false, QuickStart = true });
            world.AddPlayer("host");
            var bank = world.Map.Destinations.Single(d => d.IsPoBoxBank);
            int all = 0, theirs = 0;
            var boxes = new HashSet<int>();
            for (int i = 0; i < 6 && world.Run.Phase != RunPhase.Payday; i++)
            {
                SimDriver.StepTicks(world, 30 * 30);
                foreach (var e in world.Intake.Entries.ToList())
                {
                    if (e.Stack.Kind != StackKind.Mail) continue;
                    foreach (var m in e.Stack.Mail)
                    {
                        all++;
                        if (m.Address.Key != bank.Address.Key) continue;
                        theirs++;
                        Assert.Contains(Db.MailKinds[m.Kind].Id, new[] { "letter", "postcard" });
                        Assert.InRange(m.Address.Unit, 1, bank.Units);
                        boxes.Add(m.Address.Unit);
                    }
                    world.Intake.Remove(e.EntryId);
                }
            }
            Assert.True(theirs > 0 && boxes.Count >= 2, $"{theirs} pieces for {boxes.Count} boxes");
            // One address's share of the letters, give or take: not one a box (it would take half of shift 1).
            int open = world.Map.Destinations.Count(d => !d.IsDock && world.Map.OpensAt(d) <= 1);
            Assert.True(theirs / (double)all < 4.0 / open, $"the bank had {theirs} of {all}; {open} addresses open");
        }
    }
}
