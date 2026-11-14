using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// M0 acceptance (doc 12): two players moving items between the same chest concurrently never
    /// produce a duplicate or lost item across 10 000 randomized ops. Both play through the real
    /// client and the wire, behind latency, so each decides on a picture the other has already
    /// changed; after every server tick everything that exists is counted.
    /// </summary>
    public class ChestFuzzTests
    {
        private const int Ops = 10_000;

        private readonly Xunit.Abstractions.ITestOutputHelper _output;

        public ChestFuzzTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

        private sealed class Hand
        {
            public readonly ClientRuntime Client;
            public readonly Dictionary<OpResult, int> Results = new Dictionary<OpResult, int>();
            public int Sent;

            public Hand(ClientRuntime client)
            {
                Client = client;
                client.RequestCompleted += (request, result) =>
                {
                    if (!InventoryPrediction.Predicts(request.Kind)) return;
                    Results[result] = Results.GetValueOrDefault(result) + 1;
                };
            }

            public ClientWorld World => Client.World!;
        }

        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 0)]
        [InlineData(3, 5)]
        public void TwoPlayersOnOneChestNeverDuplicateOrLoseAnItem(int seed, int delayPolls)
        {
            var hub = new LoopbackHub { DelayPolls = delayPolls };
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3 }, hub);
            var hands = new[]
            {
                new Hand(new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada")),
                new Hand(new ClientRuntime(Shared.Content, hub.CreateClient(), "Bo")),
            };
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var h in hands) h.Client.Tick();
                    server.Tick();
                }
            }
            Run(10 + 2 * delayPolls);

            var world = server.World;
            Player ada = world.Players[0], bo = world.Players[1];
            var chest = world.AddWorldContainer("chest", (ada.Move.X + bo.Move.X) / 2f, (ada.Move.Z + bo.Move.Z) / 2f);
            var all = new[] { chest, ada.Hotbar, ada.Inventory, bo.Hotbar, bo.Inventory };

            // Enough to crowd the chest (32 cells) but leave room to move: materials, and mail of several shapes.
            uint mailId = 1000;
            ItemStack Mail(string kind, int number) => ItemStack.OfMail(new MailItem
            {
                Id = mailId++, Kind = Shared.Content.MailKind(kind).Index, Address = new AddressId(0, 0, number),
                Value = (ushort)Shared.Content.MailKind(kind).BaseValue, SpawnShift = 1, DeadlineShift = 2,
            });
            ItemStack Item(string id, int count) => ItemStack.OfItem(Shared.Content.Item(id), count);
            var gifts = new List<(GridContainer To, ItemStack Stack)>
            {
                (chest, Item("fiber", 20)), (chest, Item("fiber", 9)), (chest, Item("stone", 14)), (chest, Item("log", 10)),
                (chest, Item("log", 3)), (chest, Item("oil_can", 5)), (chest, Item("rope", 7)), (chest, Mail("medium", 4)),
                (chest, Mail("small", 2)), (chest, Mail("small", 6)),
                (ada.Inventory, Item("stone", 20)), (ada.Inventory, Item("plank", 11)), (ada.Inventory, Item("log", 4)), (ada.Hotbar, Item("fiber", 5)),
                (bo.Inventory, Item("rope", 10)), (bo.Inventory, Item("oil_can", 2)), (bo.Inventory, Mail("small", 2)), (bo.Hotbar, Item("plank", 20)),
            };
            for (int i = 0; i < 12; i++) gifts.Add((i % 3 == 0 ? ada.Inventory : chest, Mail("letter", 2 + 2 * (i % 2))));
            for (int i = 0; i < 6; i++) gifts.Add((bo.Inventory, Mail("postcard", 4)));
            foreach (var (to, stack) in gifts) Assert.True(world.Give(to, stack), "the starting items do not fit");

            var expected = Census(all, out var expectedMail);
            Assert.Equal(gifts.Sum(g => g.Stack.Count), expected.Values.Sum());

            foreach (var h in hands) Assert.NotEqual(0u, h.Client.OpenContainer(chest.Id));
            Run(4 + 2 * delayPolls);
            foreach (var h in hands) Assert.Equal(chest.Id, h.World.OpenContainerId);

            var rng = new Random(seed);
            int sent = 0, ticks = 0;
            while (sent < Ops)
            {
                foreach (var h in hands)
                {
                    // Up to three a tick: on average under the server's two per tick, in bursts above it.
                    for (int n = rng.Next(4); n > 0 && sent < Ops; n--)
                    {
                        if (!Act(h, chest.Id, rng)) break;
                        h.Sent++;
                        sent++;
                    }
                }
                Run(1);
                ticks++;
                AssertNothingMadeOrLost(all, expected, expectedMail, $"after tick {ticks} ({sent} ops, seed {seed})");
                Assert.True(ticks < Ops * 4, $"only {sent} ops could be sent in {ticks} ticks");
            }

            // Everything answered: both screens show what the server has, and nothing is still faint.
            foreach (var h in hands)
                if (h.World.OpenContainerId == 0) h.Client.OpenContainer(chest.Id);
            Run(10 + 4 * delayPolls);
            AssertNothingMadeOrLost(all, expected, expectedMail, "at the end");
            foreach (var h in hands)
            {
                Assert.Equal(0, h.Client.PendingRequestCount);
                Assert.Equal(0, h.World.Prediction.PendingCount);
                Assert.Equal(0, h.Client.ContainerDesyncs);
                Assert.Equal(0, h.Client.BadPackets);
                Assert.Equal(chest.Id, h.World.OpenContainerId);
                foreach (var mirror in h.World.Containers.Values)
                    Assert.Equal(world.GetContainer(mirror.Id)!.ComputeHash(), mirror.ComputeHash());
                Assert.Same(h.World.Containers[chest.Id], h.World.OpenContainer);
                Assert.Equal(h.Sent, h.Results.Values.Sum());
            }

            // The fuzz must bite: most ops go through, and the two do get in each other's way.
            int ok = hands.Sum(h => h.Results.GetValueOrDefault(OpResult.Ok));
            int refused = sent - ok;
            _output.WriteLine($"seed {seed}, {delayPolls} polls of delay: {sent} ops in {ticks} ticks, {ok} accepted — {Tally(hands)}");
            Assert.True(ok > Ops / 3, $"only {ok} of {sent} ops were accepted: {Tally(hands)}");
            Assert.True(refused > Ops / 100, $"only {refused} of {sent} ops were refused: {Tally(hands)}");
            if (delayPolls > 0)
            {
                int raced = hands.Sum(h => h.Results.GetValueOrDefault(OpResult.UnknownEntry));
                Assert.True(raced > 0, $"the players never reached for the same stack: {Tally(hands)}");
            }
        }

        private static string Tally(Hand[] hands) =>
            string.Join("; ", hands.Select(h => string.Join(", ", h.Results.OrderBy(r => r.Key).Select(r => $"{r.Key} {r.Value}"))));

        /// <summary>One thing a player might do with what they see. False if no item op was sent.</summary>
        private static bool Act(Hand h, uint chestId, Random rng)
        {
            var w = h.World;
            int roll = rng.Next(1000);

            // Now and then one walks off and comes back: the chest closes under whatever is on its way.
            // (Neither is one of the 10 000: the rest of this tick is the walk.)
            if (w.OpenContainerId == 0)
            {
                if (h.Client.PendingRequestCount == 0) h.Client.OpenContainer(chestId);
                return false;
            }
            if (roll < 2)
            {
                h.Client.CloseContainer();
                return false;
            }

            var views = new[] { w.Hotbar, w.Inventory, w.OpenContainer! };
            var from = views[rng.Next(views.Length)];
            if (roll < 60) return h.Client.Sort(from.Id, rng.Next(2) == 0 ? SortKey.Address : SortKey.Size) != 0;

            var entries = from.Entries.Where(e => !InventoryPrediction.IsPrivate(e)).OrderBy(e => e.EntryId).ToList();
            if (entries.Count == 0)
            {
                from = views.OrderByDescending(v => v.EntryCount).First();
                entries = from.Entries.Where(e => !InventoryPrediction.IsPrivate(e)).OrderBy(e => e.EntryId).ToList();
                if (entries.Count == 0) return false;
            }
            var entry = entries[rng.Next(entries.Count)];
            if (roll < 260) return h.Client.QuickMove(from.Id, entry.EntryId) != 0;

            var to = views[rng.Next(views.Length)];
            // Nonsense too: a stack that is not there, a cell off the grid, more than the stack holds.
            if (roll < 290)
                return h.Client.Move(from.Id, entry.EntryId + (uint)rng.Next(1, 50), to.Id, rng.Next(-1, to.Cols + 2), rng.Next(-1, to.Rows + 2), rng.Next(2) == 0, rng.Next(0, 60)) != 0;

            int x = rng.Next(to.Cols), y = rng.Next(to.Rows);
            if (roll < 450)
            {
                // Aim at a stack it can merge with, if one is in sight.
                var like = to.Entries.Where(e => e.EntryId != entry.EntryId && e.Stack.CanStackWith(entry.Stack)).OrderBy(e => e.EntryId).ToList();
                if (like.Count > 0)
                {
                    var target = like[rng.Next(like.Count)];
                    x = target.X;
                    y = target.Y;
                }
            }
            int have = entry.Stack.Count;
            int count = rng.Next(3) == 0 && have > 1 ? rng.Next(1, have) : 0;
            return h.Client.Move(from.Id, entry.EntryId, to.Id, x, y, rng.Next(3) == 0, count) != 0;
        }

        /// <summary>How many of each thing exist, and every piece of mail by id.</summary>
        private static Dictionary<(StackKind, ushort, AddressId), int> Census(GridContainer[] all, out HashSet<uint> mail)
        {
            var counts = new Dictionary<(StackKind, ushort, AddressId), int>();
            mail = new HashSet<uint>();
            foreach (var c in all)
            {
                foreach (var e in c.Entries)
                {
                    var key = (e.Stack.Kind, e.Stack.DefIndex, e.Stack.Kind == StackKind.Mail ? e.Stack.Address : default);
                    counts[key] = counts.GetValueOrDefault(key) + e.Stack.Count;
                    if (e.Stack.Kind != StackKind.Mail) continue;
                    foreach (var m in e.Stack.Mail) Assert.True(mail.Add(m.Id), $"mail {m.Id} exists twice");
                }
            }
            return counts;
        }

        private static void AssertNothingMadeOrLost(GridContainer[] all, Dictionary<(StackKind, ushort, AddressId), int> expected, HashSet<uint> expectedMail, string when)
        {
            var now = Census(all, out var mail);
            foreach (var (key, count) in expected)
                Assert.True(now.GetValueOrDefault(key) == count, $"{when}: {now.GetValueOrDefault(key)} of {key} exist, there were {count}");
            Assert.True(now.Count == expected.Count, $"{when}: a new kind of thing appeared");
            Assert.True(mail.SetEquals(expectedMail), $"{when}: the pieces of mail are not the ones that were given");

            var ids = new HashSet<uint>();
            foreach (var c in all)
            {
                var cells = new HashSet<(int, int)>();
                foreach (var e in c.Entries)
                {
                    Assert.True(ids.Add(e.EntryId), $"{when}: entry {e.EntryId} is in two places");
                    var shape = StackShape.Of(Shared.Content, e.Stack);
                    Assert.True(e.Stack.Count >= 1 && e.Stack.Count <= shape.MaxStack, $"{when}: a stack of {e.Stack.Count} (max {shape.MaxStack})");
                    Assert.True((e.W, e.H) == (e.Rotated ? (shape.H, shape.W) : (shape.W, shape.H)), $"{when}: entry {e.EntryId} has the wrong footprint");
                    for (int y = e.Y; y < e.Y + e.H; y++)
                    {
                        for (int x = e.X; x < e.X + e.W; x++)
                        {
                            Assert.True(c.InBounds(x, y) && !c.IsReserved(x, y), $"{when}: entry {e.EntryId} lies off the grid of container {c.Id}");
                            Assert.True(cells.Add((x, y)), $"{when}: two stacks share cell {x},{y} of container {c.Id}");
                        }
                    }
                }
            }
        }
    }
}
