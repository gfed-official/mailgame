using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Random belt networks (belts, splitters, mergers, built, turned and taken down under the
    /// mail) against what must always hold: no mail made or lost, none twice, every lane in order
    /// with its items apart, and a client's mirror, moved by the server's hand-offs alone, holding
    /// the same mail lane by lane on every segment.
    /// </summary>
    public class BeltFuzzTests
    {
        private static ContentDb Db => TestContent.Db;

        private sealed class Sink : IBeltSink
        {
            public readonly List<(uint Mail, int Head, byte Lane)> Moves = new List<(uint, int, byte)>();
            public readonly List<uint> Delivered = new List<uint>();
            public bool TryDeliver(BeltSegment segment, in BeltItem item)
            {
                // Every other piece goes in, so some wait at the end.
                if (item.Mail.Id % 2 == 0) return false;
                Delivered.Add(item.Mail.Id);
                return true;
            }
            public void Moved(BeltSegment from, BeltSegment to, in BeltItem item) => Moves.Add((item.Mail.Id, to.Head, item.Lane));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void RandomNetworks_KeepEveryPieceOfMail_InOrder_AndTheClientsMirrorAgrees(int seed)
        {
            var map = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false }).Map;
            var serverField = new ConstructField(map);
            var clientField = new ConstructField(map);
            var server = new BeltNetwork(Db, map, serverField);
            var client = new BeltNetwork(Db, map, clientField);
            var kinds = new[] { Db.Building("belt_mk1"), Db.Building("belt_mk1"), Db.Building("belt_mk1"), Db.Building("splitter"), Db.Building("merger") };
            var mailKinds = new[] { Db.MailKind("letter"), Db.MailKind("letter"), Db.MailKind("cargo") };
            var rng = new Random(seed);
            var sink = new Sink();
            var dest = map.Destinations[0];
            // Round a mailbox, so some lines end at one.
            int size = 8;
            int x0 = Math.Clamp((int)(dest.MailboxX / SimConstants.TileSize) - size / 2, 0, map.Cols - size);
            int z0 = Math.Clamp((int)(dest.MailboxZ / SimConstants.TileSize) - size / 2, 0, map.Rows - size);
            uint nextMail = 1;
            var orphaned = new HashSet<uint>();
            var made = new HashSet<uint>();
            var orphans = new List<BeltItem>();
            int handOffs = 0;

            void Build(int tx, int tz, BuildingDef? def, int rot)
            {
                foreach (var f in new[] { serverField, clientField })
                {
                    if (def == null) f.Remove(tx, tz);
                    else f.Add(def, tx, tz, rot, def.Hp, 0);
                }
            }

            for (int i = 0; i < 160; i++)
                Build(x0 + rng.Next(size), z0 + rng.Next(size), kinds[rng.Next(kinds.Length)], rng.Next(4));

            for (int tick = 1; tick <= 3000; tick++)
            {
                if (rng.Next(40) == 0)
                {
                    int edits = 1 + rng.Next(3);
                    for (int e = 0; e < edits; e++)
                        Build(x0 + rng.Next(size), z0 + rng.Next(size), rng.Next(3) == 0 ? null : kinds[rng.Next(kinds.Length)], rng.Next(4));
                }
                orphans.Clear();
                server.Compile(null, orphans);
                foreach (var o in orphans) orphaned.Add(o.Mail.Id);
                client.Compile();
                // The server's orphans are off the client's belts by its BeltItemRemoved.
                foreach (var o in orphans) client.RemoveMail(o.Mail.Id);
                Assert.Equal(server.Segments.Select(s => s.Head), client.Segments.Select(s => s.Head));

                if (server.Segments.Count > 0 && rng.Next(3) == 0)
                {
                    var s = server.Segments[rng.Next(server.Segments.Count)];
                    var kind = mailKinds[rng.Next(mailKinds.Length)];
                    var mail = TestContent.Mail(kind.Id, dest.Address, nextMail++);
                    if (server.TryInsert(s, mail, out var item))
                    {
                        made.Add(mail.Id);
                        client.Add(client.SegmentByHead(s.Head)!, item);
                    }
                }

                sink.Moves.Clear();
                int deliveredBefore = sink.Delivered.Count;
                server.Step(SimConstants.TickDt, sink, (uint)tick);
                client.Step(SimConstants.TickDt, null, (uint)tick);
                for (int d = deliveredBefore; d < sink.Delivered.Count; d++) Assert.True(client.RemoveMail(sink.Delivered[d]));
                handOffs += sink.Moves.Count;
                foreach (var (mailId, head, lane) in sink.Moves)
                {
                    var it = client.Find(mailId);
                    Assert.True(it.HasValue, $"tick {tick}: the client has no mail {mailId} to move");
                    var moved = it!.Value;
                    moved.Pos = 0f;
                    moved.Lane = lane;
                    client.Add(client.SegmentByHead(head)!, moved);
                }

                // Nothing made or lost, nothing twice.
                var riding = server.Segments.SelectMany(s => s.Items).Select(it => it.Mail.Id).ToList();
                Assert.Equal(riding.Count, riding.Distinct().Count());
                Assert.Equal(riding.Count, server.ItemCount);
                Assert.Equal(made.Count, riding.Count + sink.Delivered.Count + orphaned.Count);
                foreach (var s in server.Segments)
                {
                    for (int k = 0; k < s.Items.Count; k++)
                    {
                        var it = s.Items[k];
                        Assert.InRange(it.Pos, -1e-3f, s.Length + 1e-3f);
                        if (k > 0) Assert.True(s.Items[k - 1].Pos >= it.Pos, $"tick {tick}: segment {s.Head} out of order");
                        for (int j = 0; j < k; j++)
                            if (BeltNetwork.SharesLane(s.Items[j].Lane, it.Lane))
                                Assert.True(s.Items[j].Pos - s.Items[j].Length >= it.Pos - 1e-3f,
                                    $"tick {tick}: on segment {s.Head} mail {it.Mail.Id} at {it.Pos} runs into {s.Items[j].Mail.Id} at {s.Items[j].Pos}");
                    }
                    var mirror = client.SegmentByHead(s.Head)!;
                    Assert.True(BeltNetwork.Checksum(s) == BeltNetwork.Checksum(mirror),
                        $"tick {tick}: segment {s.Head}: server [{string.Join(",", s.Items.Select(x => $"{x.Mail.Id}/{x.Lane}@{x.Pos:0.00}"))}] "
                        + $"client [{string.Join(",", mirror.Items.Select(x => $"{x.Mail.Id}/{x.Lane}@{x.Pos:0.00}"))}]");
                }
            }
            Assert.True(made.Count > 100, $"only {made.Count} pieces were put on");
            Assert.True(handOffs > 50, $"only {handOffs} hand-offs");
        }
    }
}
