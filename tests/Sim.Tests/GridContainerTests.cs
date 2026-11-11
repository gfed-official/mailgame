using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>Grid placement, rotation, stacking by address, quick-move and sort (doc 03 §3.2).</summary>
    public class GridContainerTests
    {
        private static readonly AddressId A = new AddressId(1, 1, 1);
        private static readonly AddressId B = new AddressId(1, 1, 2);

        private readonly EntryIdAllocator _ids = new EntryIdAllocator();
        private uint _mailId = 1;

        private GridContainer New(string def, uint id = 1) => new GridContainer(id, TestContent.Db.Container(def));

        private ItemStack Mail(string kind, AddressId address, int count = 1)
        {
            var stack = ItemStack.OfMail(TestContent.Mail(kind, address, _mailId++));
            for (int i = 1; i < count; i++) stack.Mail.Add(TestContent.Mail(kind, address, _mailId++));
            return stack;
        }

        /// <summary>Auto-places a stack. Returns its entry (null! if it merged into an existing stack).</summary>
        private GridEntry Put(GridContainer c, ItemStack stack)
        {
            Assert.True(InventoryOps.AutoPlace(TestContent.Db, c, stack, _ids));
            return c.Entries.FirstOrDefault(e => ReferenceEquals(e.Stack, stack))!;
        }

        private static int TotalMail(params GridContainer[] containers) =>
            containers.Sum(c => c.Entries.Sum(e => e.Stack.Count));

        [Fact]
        public void AutoPlaceScansRowMajorAndSkipsOccupiedCells()
        {
            var chest = New("chest");
            var first = Put(chest, Mail("medium", A));
            var second = Put(chest, Mail("medium", B));
            var third = Put(chest, Mail("small", A));
            Assert.Equal((0, 0), (first.X, first.Y));
            Assert.Equal((2, 0), (second.X, second.Y));
            Assert.Equal((4, 0), (third.X, third.Y));
            Assert.Equal((1, 2), (third.W, third.H));
        }

        [Fact]
        public void PlacementMustBeInsideGridAndUnoccupied()
        {
            var chest = New("chest");
            var medium = Put(chest, Mail("medium", A));
            var small = Put(chest, Mail("small", A));
            var db = TestContent.Db;

            Assert.Equal(OpResult.DoesNotFit, InventoryOps.Move(db, chest, small.EntryId, chest, 1, 1, false, 0, _ids));
            Assert.Equal(OpResult.DoesNotFit, InventoryOps.Move(db, chest, small.EntryId, chest, 7, 3, false, 0, _ids));
            Assert.Equal(OpResult.DoesNotFit, InventoryOps.Move(db, chest, medium.EntryId, chest, 7, 0, false, 0, _ids));
            Assert.Equal(OpResult.Ok, InventoryOps.Move(db, chest, medium.EntryId, chest, 6, 2, false, 0, _ids));
            Assert.Equal((6, 2), (medium.X, medium.Y));
            Assert.Null(chest.EntryAt(0, 0));
            Assert.Same(medium, chest.EntryAt(7, 3));
        }

        [Fact]
        public void EntryCanMoveOntoCellsItAlreadyCovers()
        {
            var chest = New("chest");
            var medium = Put(chest, Mail("medium", A));
            Assert.Equal(OpResult.Ok, InventoryOps.Move(TestContent.Db, chest, medium.EntryId, chest, 1, 1, false, 0, _ids));
            Assert.Equal((1, 1), (medium.X, medium.Y));
            Assert.Null(chest.EntryAt(0, 0));
        }

        [Fact]
        public void RotationSwapsFootprint()
        {
            var hotbar = New("hotbar");
            var chest = New("chest", 2);
            var small = Put(chest, Mail("small", A));
            var db = TestContent.Db;

            // A 1x2 package is too tall for the one-row hotbar unless rotated.
            Assert.Equal(OpResult.DoesNotFit, InventoryOps.Move(db, chest, small.EntryId, hotbar, 1, 0, false, 0, _ids));
            Assert.Equal(OpResult.Ok, InventoryOps.Move(db, chest, small.EntryId, hotbar, 1, 0, true, 0, _ids));
            Assert.True(small.Rotated);
            Assert.Equal((2, 1), (small.W, small.H));
            Assert.Same(small, hotbar.EntryAt(2, 0));
            Assert.Equal(0, chest.EntryCount);
        }

        [Fact]
        public void HotbarSlotOneIsReservedForHands()
        {
            var hotbar = New("hotbar");
            var chest = New("chest", 2);
            var letter = Put(chest, Mail("letter", A));
            Assert.True(hotbar.IsReserved(0, 0));
            Assert.Equal(OpResult.DoesNotFit, InventoryOps.Move(TestContent.Db, chest, letter.EntryId, hotbar, 0, 0, false, 0, _ids));

            var auto = Put(hotbar, Mail("letter", B));
            Assert.Equal(1, auto.X);
        }

        [Fact]
        public void CargoCannotGoIntoHandCarriedContainers()
        {
            var inventory = New("player_inventory");
            Assert.False(InventoryOps.AutoPlace(TestContent.Db, inventory, Mail("cargo", A), _ids));
            Assert.Equal(0, inventory.EntryCount);
        }

        [Fact]
        public void LettersStackOnlyWithTheSameAddress()
        {
            var chest = New("chest");
            Put(chest, Mail("letter", A));
            Put(chest, Mail("letter", B));
            Put(chest, Mail("letter", A));
            Put(chest, Mail("postcard", A));
            Assert.Equal(3, chest.EntryCount);
            Assert.Equal(2, chest.EntryAt(0, 0)!.Stack.Count);
            Assert.Equal(4, TotalMail(chest));
        }

        [Fact]
        public void MergeStopsAtMaxStackAndExcessStaysAtSource()
        {
            var chest = New("chest");
            var big = Put(chest, Mail("letter", A, 15));
            var other = New("chest", 2);
            var more = Put(other, Mail("letter", A, 12));

            Assert.Equal(OpResult.Ok, InventoryOps.Move(TestContent.Db, other, more.EntryId, chest, big.X, big.Y, false, 0, _ids));
            Assert.Equal(20, big.Stack.Count);
            Assert.Equal(7, more.Stack.Count);
            Assert.Same(more, other.Get(more.EntryId));

            // A full stack takes nothing more.
            Assert.Equal(OpResult.DoesNotFit, InventoryOps.Move(TestContent.Db, other, more.EntryId, chest, big.X, big.Y, false, 0, _ids));
            Assert.Equal(27, TotalMail(chest, other));
        }

        [Fact]
        public void MovingOntoAnIncompatibleStackIsRejected()
        {
            var chest = New("chest");
            var a = Put(chest, Mail("letter", A));
            var b = Put(chest, Mail("letter", B));
            Assert.Equal(OpResult.DoesNotFit, InventoryOps.Move(TestContent.Db, chest, a.EntryId, chest, b.X, b.Y, false, 0, _ids));
            Assert.Equal(2, chest.EntryCount);
        }

        [Fact]
        public void SplitCreatesANewEntryAndKeepsEveryItem()
        {
            var chest = New("chest");
            var stack = Put(chest, Mail("letter", A, 10));
            var ids = stack.Stack.Mail.Select(m => m.Id).ToHashSet();

            Assert.Equal(OpResult.Ok, InventoryOps.Move(TestContent.Db, chest, stack.EntryId, chest, 3, 3, false, 4, _ids));
            Assert.Equal(2, chest.EntryCount);
            Assert.Equal(6, stack.Stack.Count);
            var split = chest.EntryAt(3, 3)!;
            Assert.Equal(4, split.Stack.Count);
            Assert.NotEqual(stack.EntryId, split.EntryId);

            var after = chest.Entries.SelectMany(e => e.Stack.Mail).Select(m => m.Id).ToHashSet();
            Assert.True(ids.SetEquals(after));
            Assert.Equal(OpResult.BadRequest, InventoryOps.Move(TestContent.Db, chest, stack.EntryId, chest, 5, 3, false, 99, _ids));
        }

        [Fact]
        public void QuickMoveMergesFirstThenUsesFreeSpaceAcrossTargets()
        {
            var intake = New("po_intake");
            var hotbar = New("hotbar", 2);
            var inventory = New("player_inventory", 3);
            var held = Put(inventory, Mail("letter", A, 5));
            var targets = new List<GridContainer> { hotbar, inventory };

            // Merges into the matching stack in the inventory even though the hotbar has free cells.
            var more = Put(intake, Mail("letter", A, 3));
            Assert.Equal(OpResult.Ok, InventoryOps.QuickMove(TestContent.Db, intake, more.EntryId, targets));
            Assert.Equal(8, held.Stack.Count);
            Assert.Equal(0, hotbar.EntryCount);

            // No stack to merge with: first free cell of the first target (hotbar slot 2).
            var other = Put(intake, Mail("letter", B, 2));
            Assert.Equal(OpResult.Ok, InventoryOps.QuickMove(TestContent.Db, intake, other.EntryId, targets));
            Assert.Same(other, hotbar.EntryAt(1, 0));

            // A medium package cannot fit the one-row hotbar and lands in the inventory.
            var medium = Put(intake, Mail("medium", A));
            Assert.Equal(OpResult.Ok, InventoryOps.QuickMove(TestContent.Db, intake, medium.EntryId, targets));
            Assert.Same(medium, inventory.Get(medium.EntryId));
            Assert.Equal(0, intake.EntryCount);
        }

        [Fact]
        public void QuickMoveRotatesWhenOnlyRotatedFits()
        {
            var intake = New("po_intake");
            var hotbar = New("hotbar", 2);
            var small = Put(intake, Mail("small", A));
            Assert.Equal(OpResult.Ok, InventoryOps.QuickMove(TestContent.Db, intake, small.EntryId, new List<GridContainer> { hotbar }));
            Assert.True(small.Rotated);
            Assert.Equal((1, 0), (small.X, small.Y));
        }

        [Fact]
        public void QuickMoveIntoFullTargetsFails()
        {
            var intake = New("po_intake");
            var hotbar = New("hotbar", 2);
            for (int i = 0; i < 7; i++) Put(hotbar, Mail("letter", new AddressId(1, 1, 100 + i)));
            var extra = Put(intake, Mail("letter", A));
            Assert.Equal(OpResult.DoesNotFit, InventoryOps.QuickMove(TestContent.Db, intake, extra.EntryId, new List<GridContainer> { hotbar }));
            Assert.Equal(1, intake.EntryCount);
        }

        [Fact]
        public void SortByAddressMergesStacksAndGroupsAddresses()
        {
            var chest = New("chest");
            // Interleave two addresses as separate entries placed by hand so nothing auto-merges.
            for (int i = 0; i < 6; i++)
            {
                var stack = Mail("letter", i % 2 == 0 ? B : A, 2);
                chest.Add(new GridEntry { EntryId = _ids.Next(), X = i, Y = 1, W = 1, H = 1, Stack = stack });
            }
            Put(chest, Mail("medium", B));
            int before = TotalMail(chest);

            Assert.Equal(OpResult.Ok, InventoryOps.Sort(TestContent.Db, chest, SortKey.Address));
            Assert.Equal(before, TotalMail(chest));
            Assert.Equal(3, chest.EntryCount);

            // Address A sorts before B; within B the bigger item comes first.
            var first = chest.EntryAt(0, 0)!;
            Assert.Equal(A, first.Stack.Address);
            Assert.Equal(6, first.Stack.Count);
            var second = chest.EntryAt(1, 0)!;
            Assert.Equal(B, second.Stack.Address);
            Assert.Equal((2, 2), (second.W, second.H));
        }

        [Fact]
        public void SortBySizePutsLargestFirst()
        {
            var chest = New("chest");
            Put(chest, Mail("letter", A));
            Put(chest, Mail("small", A));
            Put(chest, Mail("large", B));
            Put(chest, Mail("medium", A));
            Assert.Equal(OpResult.Ok, InventoryOps.Sort(TestContent.Db, chest, SortKey.Size));
            Assert.Equal(8, chest.EntryAt(0, 0)!.W * chest.EntryAt(0, 0)!.H);
            Assert.Equal(4, chest.EntryAt(2, 0)!.W * chest.EntryAt(2, 0)!.H);
            Assert.Equal(4, TotalMail(chest));
        }

        [Fact]
        public void MirrorFollowsDeltasAndHashesMatchOnEveryVersion()
        {
            var server = New("chest");
            var mirror = New("chest");
            var db = TestContent.Db;

            void Sync()
            {
                var delta = server.Commit();
                Assert.NotNull(delta);
                Assert.True(mirror.Apply(delta!));
                Assert.Equal(delta!.Hash, mirror.ComputeHash());
                Assert.Equal(server.Version, mirror.Version);
            }

            var letters = Put(server, Mail("letter", A, 6));
            Sync();
            var package = Put(server, Mail("small", B));
            Sync();
            Assert.Equal(OpResult.Ok, InventoryOps.Move(db, server, letters.EntryId, server, 5, 2, false, 2, _ids));
            Sync();
            Assert.Equal(OpResult.Ok, InventoryOps.Move(db, server, package.EntryId, server, 6, 0, true, 0, _ids));
            Sync();
            Assert.Equal(OpResult.Ok, InventoryOps.Sort(db, server, SortKey.Address));
            Sync();
            server.Remove(package.EntryId);
            Sync();

            Assert.Null(server.Commit());
            Assert.Equal(server.Snapshot().Hash, mirror.Snapshot().Hash);
        }

        [Fact]
        public void MirrorRejectsADeltaWithTheWrongVersion()
        {
            var server = New("chest");
            var mirror = New("chest");
            Put(server, Mail("letter", A));
            server.Commit();
            Put(server, Mail("letter", B));
            var second = server.Commit()!;
            Assert.False(mirror.Apply(second));

            mirror.Load(server.Snapshot());
            Assert.Equal(server.ComputeHash(), mirror.ComputeHash());
            Assert.Equal(server.Version, mirror.Version);
        }

        [Fact]
        public void HashDependsOnPositionAndContents()
        {
            var a = New("chest");
            var b = New("chest");
            a.Add(new GridEntry { EntryId = 1, X = 0, Y = 0, W = 1, H = 1, Stack = ItemStack.OfMail(TestContent.Mail("letter", A, 7)) });
            b.Add(new GridEntry { EntryId = 1, X = 1, Y = 0, W = 1, H = 1, Stack = ItemStack.OfMail(TestContent.Mail("letter", A, 7)) });
            Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
            Assert.Equal(a.ComputeHash(), a.Clone().ComputeHash());
        }
    }
}
