using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 12 M4's replay acceptance test: the same seed, stamps and player count reproduce the mail schedule and the perk
    /// offers exactly, on the Small Island, the Large Island and the daily seed. Two servers set up alike play the same
    /// shifts with the same (idle) carriers; what reaches the Intake, tick by tick, and every Draft's cards (a reroll's
    /// too) are written down and compared.
    /// </summary>
    public class ReplayTests
    {
        private static ContentDb Db => TestContent.Db;
        private static readonly DateTime Today = new DateTime(2026, 11, 27, 9, 30, 0, DateTimeKind.Utc);
        private const int Tps = 30;
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static int Choice(string mapId) => SimWorld.WorldChoices.ToList().IndexOf(mapId);

        private static byte Mask(params string[] ids) => (byte)ids.Aggregate(0, (m, id) => m | 1 << Db.Stamp(id).Index);

        /// <summary>What a replay is compared by.</summary>
        private sealed class Record
        {
            /// <summary>Each mail item as it is first seen in the Intake: "tick:id kind address value spawn deadline flags due".</summary>
            public readonly List<string> Mail = new List<string>();
            /// <summary>Each Draft's offer and each reroll's, "shift N [reroll of P]: perk, perk, perk".</summary>
            public readonly List<string> Offers = new List<string>();
            public ulong MapHash;
            public uint Seed;
            public int Shifts;
        }

        /// <summary>
        /// One run: the host sets the world (and the stamps of a run of its own), the carriers ready up, idle through the
        /// shifts (no quota, so the run goes on) and each takes its first open card; the host rerolls the first Draft once.
        /// </summary>
        private Record Replay(string mapId, uint seed, RunMode mode, byte stamps, int players, int shifts)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 1, Raids = true, Draft = true, RunEvents = true, StartingKits = true, UtcNow = () => Today });
            var carriers = new List<Player>();
            for (int i = 0; i < players; i++)
            {
                var p = world.AddPlayer("p" + i)!;
                p.Rank = 20;
                p.Playtest |= PlaytestFlags.NoQuota;
                carriers.Add(p);
            }
            var host = carriers[0];
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetWorld(0, Choice(mapId), seed, mode)));
            if (mode == RunMode.Normal) Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetStamps(0, stamps)));
            var record = new Record { MapHash = world.Map.MapHash, Seed = world.Settings.Seed };
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));

            var seen = new HashSet<uint>();
            int lastDraftShift = 0;
            bool rerolled = false;
            for (int tick = 0; tick < 40 * 60 * Tps; tick++)
            {
                var run = world.Run;
                if (run.Phase == RunPhase.Results || run.Shift > shifts) break;
                if (run.Phase == RunPhase.Prep)
                    foreach (var p in carriers)
                        if (!p.Ready) Request(world, p, SimRequest.SetReady(0, true));
                if (run.Phase == RunPhase.Draft && world.DraftOffer.Count > 0)
                {
                    if (lastDraftShift != run.Shift)
                    {
                        lastDraftShift = run.Shift;
                        record.Offers.Add($"shift {run.Shift}: {string.Join(", ", world.DraftOffer.Select(d => d.Id))}");
                    }
                    if (!rerolled && world.RerollsLeft(host) > 0 && host.DraftPick < 0)
                    {
                        rerolled = true;
                        world.Events.Clear();
                        Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Reroll(0)));
                        var offer = world.Events.OfType<DraftOfferEvent>().Single(e => e.Reroll);
                        record.Offers.Add($"shift {run.Shift} reroll of p0: {string.Join(", ", offer.Perks.Select(i => Db.Perks[i].Id))}");
                    }
                    foreach (var p in carriers)
                    {
                        if (p.DraftPick >= 0) continue;
                        for (int card = 0; card < 3; card++)
                            if (Request(world, p, SimRequest.PickPerk(0, card)) == OpResult.Ok) break;
                    }
                }
                world.Step();
                foreach (var e in world.Intake.Entries)
                {
                    if (e.Stack.Kind != Inventory.StackKind.Mail) continue;
                    foreach (var m in e.Stack.Mail)
                        if (seen.Add(m.Id))
                            record.Mail.Add($"{world.Tick}:{m.Id} {Db.MailKinds[m.Kind].Id} {m.Address.Key} {m.Value} {m.SpawnShift} {m.DeadlineShift} {m.Flags} {m.DueTick}");
                }
            }
            record.Shifts = world.Run.Shift;
            return record;
        }

        private void AssertReplays(string mapId, uint seed, RunMode mode, byte stamps, int players, int shifts)
        {
            var a = Replay(mapId, seed, mode, stamps, players, shifts);
            var b = Replay(mapId, seed, mode, stamps, players, shifts);
            Assert.True(a.Mail.Count > 50, $"only {a.Mail.Count} mail items reached the Intake");
            Assert.True(a.Offers.Count >= 2, $"the run saw {a.Offers.Count} offers: {string.Join(" | ", a.Offers)}");
            Assert.Equal((a.MapHash, a.Seed, a.Shifts), (b.MapHash, b.Seed, b.Shifts));
            Assert.Equal(a.Offers, b.Offers);
            // First the first difference, readable, then the whole.
            int at = Enumerable.Range(0, Math.Min(a.Mail.Count, b.Mail.Count)).FirstOrDefault(i => a.Mail[i] != b.Mail[i], -1);
            Assert.True(at < 0, at < 0 ? "" : $"the mail parts at item {at}: {a.Mail[at]} vs {b.Mail[at]}");
            Assert.Equal(a.Mail, b.Mail);

            // And the record is the seed's: another seed is another schedule.
            if (mode == RunMode.Daily) return;
            var other = Replay(mapId, seed + 1, mode, stamps, players, 1);
            Assert.NotEqual(a.Mail.Take(other.Mail.Count), other.Mail);
        }

        [Fact]
        public void TheSmallIsland_WithTwoStamps_ForTwoCarriers_ReplaysExactly() =>
            AssertReplays(WorldGenerator.SmallIsland, 11, RunMode.Normal, Mask("rush_hour", "double_raids"), 2, 3);

        [Fact]
        public void TheLargeIsland_WithAStamp_Solo_ReplaysExactly() =>
            AssertReplays(LargeIslandGenerator.LargeIsland, 7, RunMode.Normal, Mask("megamail"), 1, 3);

        [Fact]
        public void TheDailySeed_ForThreeCarriers_ReplaysExactly() =>
            AssertReplays(WorldGenerator.SmallIsland, 0, RunMode.Daily, 0, 3, 3);
    }
}
