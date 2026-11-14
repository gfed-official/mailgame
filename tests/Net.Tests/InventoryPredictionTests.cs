using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>Optimistic item moves (doc 06 §4, doc 09 §2.5): shown at once, confirmed or rolled back by the server.</summary>
    public class InventoryPredictionTests
    {
        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly ClientRuntime Client;
            public OpResult? LastResult;

            public Session(int delayPolls = 0)
            {
                Hub.DelayPolls = delayPolls;
                Server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3, QuickStart = true }, Hub);
                Client = new ClientRuntime(Shared.Content, Hub.CreateClient(), "Ada");
                Client.RequestCompleted += (_, result) => LastResult = result;
                Run(10 + 2 * delayPolls);
            }

            public Player Player => Server.World.Players[0];
            public ClientWorld World => Client.World!;
            public GridContainer Confirmed(uint id) => World.Containers[id];

            public void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    Client.Tick();
                    Server.Tick();
                }
            }

            /// <summary>Puts items into a server container; with <paramref name="settle"/> the client hears of it.</summary>
            public void Give(GridContainer container, string itemId, int count, bool settle = true)
            {
                Assert.True(Server.World.Give(container, ItemStack.OfItem(Shared.Content.Item(itemId), count)));
                if (settle) Run(3 + 2 * Hub.DelayPolls);
            }

            public void Settle()
            {
                Run(4 + 2 * Hub.DelayPolls);
                Assert.Equal(0, Client.PendingRequestCount);
                Assert.Equal(0, World.Prediction.PendingCount);
                Assert.Equal(0, Client.ContainerDesyncs);
                foreach (var mirror in World.Containers.Values)
                    Assert.Equal(Server.World.GetContainer(mirror.Id)!.ComputeHash(), mirror.ComputeHash());
            }
        }

        [Fact]
        public void AMoveShowsAtOnceAndIsThenConfirmed()
        {
            var s = new Session();
            s.Give(s.Player.Inventory, "fiber", 5);
            uint inv = s.World.InventoryId;
            var entry = s.World.Inventory.Entries.Single();
            Assert.Same(s.Confirmed(inv), s.World.Inventory); // nothing pending: the view is the mirror
            int revision = s.World.Prediction.Revision;

            s.Client.Move(inv, entry.EntryId, inv, 3, 1);

            var shown = s.World.Inventory.Get(entry.EntryId)!;
            Assert.Equal((3, 1), (shown.X, shown.Y));
            Assert.True(s.World.Prediction.IsPending(inv, shown));
            Assert.True(s.World.Prediction.Revision > revision);
            var real = s.Confirmed(inv).Get(entry.EntryId)!;
            Assert.Equal((0, 0), (real.X, real.Y)); // the mirror waits for the server

            s.Settle();
            Assert.Equal(OpResult.Ok, s.LastResult);
            shown = s.World.Inventory.Get(entry.EntryId)!;
            Assert.Equal((3, 1), (shown.X, shown.Y));
            Assert.False(s.World.Prediction.IsPending(inv, shown));
            Assert.Same(s.Confirmed(inv), s.World.Inventory);
        }

        [Fact]
        public void ARefusedMoveSnapsBack()
        {
            var s = new Session();
            s.Give(s.Player.Inventory, "fiber", 5);
            uint inv = s.World.InventoryId;
            var fiber = s.World.Inventory.Entries.Single();
            // The server puts a stone on cell (1, 0); the client has not heard of it when it drops the fiber there.
            s.Give(s.Player.Inventory, "stone", 1, settle: false);

            s.Client.Move(inv, fiber.EntryId, inv, 1, 0);
            Assert.Equal(1, s.World.Inventory.Get(fiber.EntryId)!.X);

            s.Settle();
            Assert.Equal(OpResult.DoesNotFit, s.LastResult);
            var shown = s.World.Inventory.Get(fiber.EntryId)!;
            Assert.Equal((0, 0), (shown.X, shown.Y));
            Assert.False(s.World.Prediction.IsPending(inv, shown));
            Assert.Equal(2, s.World.Inventory.EntryCount);
        }

        [Fact]
        public void ASplitShowsAPrivateStackUntilTheServerNamesIt()
        {
            var s = new Session();
            s.Give(s.Player.Inventory, "fiber", 6);
            uint inv = s.World.InventoryId;
            var entry = s.World.Inventory.Entries.Single();

            s.Client.Move(inv, entry.EntryId, inv, 4, 1, count: 2);

            Assert.Equal(4, s.World.Inventory.Get(entry.EntryId)!.Stack.Count);
            var half = s.World.Inventory.EntryAt(4, 1)!;
            Assert.Equal(2, half.Stack.Count);
            Assert.True(InventoryPrediction.IsPrivate(half));
            Assert.True(s.World.Prediction.IsPending(inv, half));
            Assert.Equal(6, s.Confirmed(inv).Get(entry.EntryId)!.Stack.Count);

            s.Settle();
            half = s.World.Inventory.EntryAt(4, 1)!;
            Assert.Equal(2, half.Stack.Count);
            Assert.False(InventoryPrediction.IsPrivate(half));
            Assert.Equal(4, s.World.Inventory.Get(entry.EntryId)!.Stack.Count);
        }

        [Fact]
        public void MovesChainOnTopOfEachOtherBehindLatency()
        {
            var s = new Session(delayPolls: 3);
            s.Give(s.Player.Inventory, "fiber", 5);
            s.Give(s.Player.Inventory, "log", 1);
            uint inv = s.World.InventoryId, hotbar = s.World.HotbarId;
            var fiber = s.World.Inventory.Entries.Single(e => e.W == 1 && e.H == 1);
            var log = s.World.Inventory.Entries.Single(e => e.H == 2);

            // Three drops before the first answer: each one starts from what the one before left.
            s.Client.Move(inv, fiber.EntryId, inv, 7, 1);
            s.Client.Move(inv, fiber.EntryId, hotbar, 2, 0);
            s.Client.Move(inv, log.EntryId, hotbar, 4, 0, rotated: true);
            Assert.Equal(3, s.World.Prediction.PendingCount);
            Assert.Equal(0, s.World.Inventory.EntryCount);
            Assert.Equal(fiber.EntryId, s.World.Hotbar.EntryAt(2, 0)!.EntryId);
            var lying = s.World.Hotbar.EntryAt(5, 0)!;
            Assert.Equal(log.EntryId, lying.EntryId);
            Assert.True(lying.Rotated);
            Assert.Equal(2, s.Confirmed(inv).EntryCount);
            uint predicted = s.World.Hotbar.ComputeHash();

            // Halfway: some answers are in, the picture does not change.
            s.Run(4);
            Assert.Equal(0, s.World.Inventory.EntryCount);
            Assert.Equal(predicted, s.World.Hotbar.ComputeHash());

            s.Settle();
            Assert.Equal(predicted, s.World.Hotbar.ComputeHash());
            Assert.Equal(0, s.World.Inventory.EntryCount);
        }

        [Fact]
        public void QuickMoveAndSortArePredictedTheWayTheServerDoesThem()
        {
            var s = new Session(delayPolls: 2);
            var chest = s.Server.World.AddWorldContainer("chest", s.Player.Move.X + 1f, s.Player.Move.Z);
            s.Give(chest, "stone", 3);
            s.Give(chest, "log", 2);
            s.Give(chest, "fiber", 7);
            s.Give(s.Player.Inventory, "fiber", 18);
            s.Client.OpenContainer(chest.Id);
            s.Settle();
            Assert.Equal(chest.Id, s.World.OpenContainerId);
            Assert.Equal(3, s.World.OpenContainer!.EntryCount);

            // Chest → body: stones to the hotbar, logs lying down in the hotbar, fibers merge and overflow.
            foreach (var e in s.World.OpenContainer.Entries.OrderBy(e => e.EntryId).ToList())
                s.Client.QuickMove(chest.Id, e.EntryId);
            Assert.Equal(0, s.World.OpenContainer.EntryCount);
            Assert.Equal(3, s.Confirmed(chest.Id).EntryCount);
            uint hotbar = s.World.Hotbar.ComputeHash(), inventory = s.World.Inventory.ComputeHash();
            s.Settle();
            Assert.Equal(hotbar, s.World.Hotbar.ComputeHash());
            Assert.Equal(inventory, s.World.Inventory.ComputeHash());
            Assert.Equal(0, chest.EntryCount);

            // Body → chest, then sort the chest by size before any of it is confirmed.
            foreach (var e in s.World.Hotbar.Entries.Concat(s.World.Inventory.Entries).OrderBy(e => e.EntryId).ToList())
                s.Client.QuickMove(s.World.Hotbar.Get(e.EntryId) != null ? s.World.HotbarId : s.World.InventoryId, e.EntryId);
            s.Client.Sort(chest.Id, SortKey.Size);
            Assert.Equal(0, s.World.Hotbar.EntryCount + s.World.Inventory.EntryCount);
            var first = s.World.OpenContainer!.EntryAt(0, 0)!;
            Assert.Equal(2, first.H); // the logs lead a size sort
            uint sorted = s.World.OpenContainer.ComputeHash();
            s.Settle();
            Assert.Equal(sorted, s.World.OpenContainer!.ComputeHash());
            Assert.Equal(25, chest.Entries.Where(e => e.W == 1 && e.H == 1 && e.Stack.DefIndex == Shared.Content.Item("fiber").Index).Sum(e => e.Stack.Count));
        }

        [Fact]
        public void ADragTurnsTheStackAndDropsItWhereTheCursorHoldsIt()
        {
            var s = new Session();
            s.Give(s.Player.Inventory, "log", 1); // 1×2, upright at (0, 0)
            uint inv = s.World.InventoryId, hotbar = s.World.HotbarId;
            var log = s.World.Inventory.Entries.Single();
            var drag = new InventoryDrag(s.Client);

            // Picked up by its lower cell.
            Assert.True(drag.Begin(inv, log.EntryId, 0, 1, half: false));
            Assert.Equal((1, 2, 0, 1), (drag.W, drag.H, drag.GrabX, drag.GrabY));
            // Upright it is too tall for the one-row hotbar; the cell marked "hands" takes nothing.
            Assert.Equal(OpResult.DoesNotFit, drag.Aim(hotbar, 3, 0, out _, out _));
            drag.Rotate();
            Assert.Equal((2, 1, 1, 0), (drag.W, drag.H, drag.GrabX, drag.GrabY));
            Assert.Equal(OpResult.DoesNotFit, drag.Aim(hotbar, 1, 0, out _, out _));
            Assert.Equal(OpResult.Ok, drag.Aim(hotbar, 3, 0, out int x, out int y));
            Assert.Equal((2, 0), (x, y)); // the cursor holds the right-hand cell

            Assert.NotEqual(0u, drag.Drop(hotbar, 3, 0));
            Assert.False(drag.Active);
            var shown = s.World.Hotbar.Get(log.EntryId)!;
            Assert.Equal((2, 0, true), (shown.X, shown.Y, shown.Rotated));
            s.Settle();
            Assert.Equal(OpResult.Ok, s.LastResult);
            Assert.Equal(2, s.Player.Hotbar.Get(log.EntryId)!.X);
        }

        [Fact]
        public void ADragOfHalfAStackSplitsItAndADropOnTheSameKindMerges()
        {
            var s = new Session();
            s.Give(s.Player.Inventory, "fiber", 7);
            uint inv = s.World.InventoryId;
            var fiber = s.World.Inventory.Entries.Single();
            var drag = new InventoryDrag(s.Client);

            Assert.True(drag.Begin(inv, fiber.EntryId, 0, 0, half: true));
            Assert.Equal(4, drag.Count); // the bigger half
            // Half a stack cannot be dropped back onto its own cell.
            Assert.Equal(0u, drag.Drop(inv, 0, 0));
            Assert.Equal(0, s.World.Prediction.PendingCount);

            Assert.True(drag.Begin(inv, fiber.EntryId, 0, 0, half: true));
            Assert.NotEqual(0u, drag.Drop(inv, 5, 1));
            var half = s.World.Inventory.EntryAt(5, 1)!;
            // The new stack has no server name yet, so it cannot be picked up.
            Assert.False(drag.Begin(inv, half.EntryId, 5, 1, half: false));
            s.Settle();
            half = s.World.Inventory.EntryAt(5, 1)!;
            Assert.Equal((3, 4), (s.World.Inventory.Get(fiber.EntryId)!.Stack.Count, half.Stack.Count));

            // Dropped whole onto the other stack: one stack of seven again.
            Assert.True(drag.Begin(inv, half.EntryId, 5, 1, half: false));
            Assert.Equal(OpResult.Ok, drag.Aim(inv, 0, 0, out int x, out int y));
            Assert.Equal((0, 0), (x, y));
            Assert.NotEqual(0u, drag.Drop(inv, 0, 0));
            Assert.Equal(7, s.World.Inventory.Entries.Single().Stack.Count);
            s.Settle();
            Assert.Equal(7, s.Player.Inventory.Entries.Single().Stack.Count);

            // Put back exactly where it lies: nothing is sent.
            Assert.True(drag.Begin(inv, fiber.EntryId, 0, 0, half: false));
            Assert.Equal(0u, drag.Drop(inv, 0, 0));
            Assert.Equal(0, s.Client.PendingRequestCount);
        }

        [Fact]
        public void ADraggedStackThatIsGoneEndsTheDrag()
        {
            var s = new Session();
            s.Give(s.Player.Inventory, "stone", 2);
            uint inv = s.World.InventoryId;
            var stone = s.World.Inventory.Entries.Single();
            var drag = new InventoryDrag(s.Client);
            Assert.True(drag.Begin(inv, stone.EntryId, 0, 0, half: false));
            Assert.NotNull(drag.Entry);

            s.Player.Inventory.Remove(stone.EntryId);
            Assert.True(s.Server.World.Give(s.Player.Inventory, ItemStack.OfItem(Shared.Content.Item("sand"), 1)));
            s.Run(4);

            Assert.Null(drag.Entry);
            Assert.Equal(0, drag.Count);
            Assert.Equal(0u, drag.Drop(inv, 3, 0));
            Assert.Equal(0, s.Client.PendingRequestCount);
        }

        [Fact]
        public void AMoveIntoAContainerThatClosesMeanwhileLeavesNothingBehind()
        {
            var s = new Session(delayPolls: 2);
            var chest = s.Server.World.AddWorldContainer("chest", s.Player.Move.X + 1f, s.Player.Move.Z);
            s.Give(s.Player.Inventory, "stone", 4);
            s.Client.OpenContainer(chest.Id);
            s.Settle();
            var stone = s.World.Inventory.Entries.Single();

            // The player walks off (the server shuts the chest) while the drop is on its way.
            s.Player.Move.X += 30f;
            s.Client.Move(s.World.InventoryId, stone.EntryId, chest.Id, 0, 0);
            Assert.Equal(0, s.World.Inventory.EntryCount);

            s.Settle();
            Assert.NotEqual(OpResult.Ok, s.LastResult);
            Assert.Equal(0u, s.World.OpenContainerId);
            Assert.Equal(4, s.World.Inventory.Get(stone.EntryId)!.Stack.Count);
            Assert.Equal(0, chest.EntryCount);
        }
    }
}
