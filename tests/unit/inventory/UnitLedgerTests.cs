using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/017: the <see cref="UnitLedger"/> (TR-inventory-012; ADR-0009 section 6; GDD R19): its shape (the mail row
/// plus one row per item def, six live states, Consumed split by <see cref="ConsumeReason"/>, Destroyed, spawned, granted) and the
/// <see cref="UnitLedger.Recount"/> test seam the rigs call after arranging entries outside the mutator (Mail Lead planning defaults
/// pending Evan, TD-160 items 2-4, 8).
/// </summary>
public sealed class UnitLedgerTests
{
    private const uint Address4 = 4u;

    // ---------------------------------------------------------------- shape

    [Fact]
    public void Ledger_AtWorldBuild_HasTheMailRowPlusOneRowPerItemDef_EveryCounterZero()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();

        // Act
        UnitLedger ledger = rig.Inventory.Ledger;

        // Assert
        Assert.Equal(1 + rig.Content.Items.Count, ledger.Rows);
        for (int row = 0; row < ledger.Rows; row++)
        {
            for (int s = 0; s < UnitLedger.LiveStateCount; s++)
            {
                Assert.Equal(0, ledger.Live(row, (LedgerState)s));
            }

            Assert.Equal(0, ledger.Consumed(row));
            Assert.Equal(0, ledger.Destroyed(row));
            Assert.Equal(0, ledger.Spawned(row));
            Assert.Equal(0, ledger.Granted(row));
        }
    }

    [Fact]
    public void RowOf_Mail_IsRowZero_AnItem_IsItsItemIdPlusOne()
    {
        // Arrange
        var mail = new EntryRecord { IsMail = true, ItemId = 7 };
        var item = new EntryRecord { IsMail = false, ItemId = 7 };

        // Act / Assert
        Assert.Equal(UnitLedger.MailRow, UnitLedger.RowOf(in mail));
        Assert.Equal(8, UnitLedger.RowOf(in item));
    }

    [Fact]
    public void Consumed_IsTheSumOfItsThreeReasonCounters_AndExpectedSubtractsItAndDestroyed()
    {
        // Arrange
        var ledger = new UnitLedger(itemDefCount: 2, entryPoolCapacity: 4);

        // Act
        ledger.AddSpawned(UnitLedger.MailRow, 10);
        ledger.AddConsumed(UnitLedger.MailRow, ConsumeReason.Delivered, 3);
        ledger.AddConsumed(UnitLedger.MailRow, ConsumeReason.Misdelivered, 2);
        ledger.AddConsumed(UnitLedger.MailRow, ConsumeReason.ReturnedToSender, 1);
        ledger.AddDestroyed(UnitLedger.MailRow, 1);
        ledger.AddLive(UnitLedger.MailRow, LedgerState.Placed, 3);

        // Assert
        Assert.Equal(3, ledger.Consumed(UnitLedger.MailRow, ConsumeReason.Delivered));
        Assert.Equal(2, ledger.Consumed(UnitLedger.MailRow, ConsumeReason.Misdelivered));
        Assert.Equal(1, ledger.Consumed(UnitLedger.MailRow, ConsumeReason.ReturnedToSender));
        Assert.Equal(6, ledger.Consumed(UnitLedger.MailRow));
        Assert.Equal(10 - 6 - 1, ledger.Expected(UnitLedger.MailRow));
        Assert.Equal(ledger.Expected(UnitLedger.MailRow), ledger.LiveSum(UnitLedger.MailRow));
        Assert.Equal(0, ledger.Expected(1)); // the item rows are untouched
    }

    [Fact]
    public void Crossings_OnAnOutOfRangeRowOrReason_ChangeNothing()
    {
        // Arrange
        var ledger = new UnitLedger(itemDefCount: 1, entryPoolCapacity: 4);

        // Act
        ledger.AddLive(ledger.Rows, LedgerState.Placed, 5);
        ledger.AddSpawned(-1, 5);
        ledger.AddConsumed(UnitLedger.MailRow, (ConsumeReason)3, 5);
        ledger.AddDestroyed(ledger.Rows + 4, 5);

        // Assert
        for (int row = 0; row < ledger.Rows; row++)
        {
            Assert.Equal(0, ledger.LiveSum(row));
            Assert.Equal(0, ledger.Expected(row));
        }
    }

    // ---------------------------------------------------------------- Recount seam

    [Fact]
    public void Recount_ArrangedStorage_CountsEachLiveStateAndHolder_AndSetsGrantedSoTheSumHolds()
    {
        // Arrange: a full Intake (320 letters), one queued backlog entry of 3 letters, a 4-letter stack in player A's inventory, 7 planks
        // on A's cursor, a 2-plank WorldItem and a detached 5-letter entry (in flight), all made without the ledger's crossings
        var rig = new InvAutoPlaceRig();
        rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(10, 4));
        rig.PlaceItem(rig.CursorA, "plank", 0, 0, 7);
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        DefIndex letter = rig.KindIndex("letter");
        Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address4, 0, IntakeSpawnOutfeedTests.Mail(5, 6, 7)))));
        var w = new WorldItemRig(rig);
        ushort ground = rig.DetachedItem("plank", 2);
        _ = w.Create(w.Now, ground);
        ushort flying = rig.DetachedMail("letter", Address4, InvAutoPlaceRig.Ids(20, 5));
        UnitLedger ledger = rig.Inventory.Ledger;
        int plank = UnitLedger.RowOf(in rig.Entries[ground]);
        ledger.AddSpawned(UnitLedger.MailRow, 100); // kept by Recount: granted absorbs the difference
        ledger.AddConsumed(UnitLedger.MailRow, ConsumeReason.Delivered, 2);

        // Act
        rig.Recount();

        // Assert
        Assert.Equal(320 + 4, ledger.Live(UnitLedger.MailRow, LedgerState.Placed));
        Assert.Equal(3, ledger.Live(UnitLedger.MailRow, LedgerState.Backlog));
        Assert.Equal(5, ledger.Live(UnitLedger.MailRow, LedgerState.Flight));
        Assert.Equal(0, ledger.Live(UnitLedger.MailRow, LedgerState.Cursor));
        Assert.Equal(0, ledger.Live(UnitLedger.MailRow, LedgerState.World));
        Assert.Equal(0, ledger.Live(UnitLedger.MailRow, LedgerState.Belt));
        Assert.Equal(7, ledger.Live(plank, LedgerState.Cursor));
        Assert.Equal(2, ledger.Live(plank, LedgerState.World));
        Assert.Equal(0, ledger.Live(plank, LedgerState.Placed));
        Assert.Equal(100, ledger.Spawned(UnitLedger.MailRow));
        Assert.Equal(2, ledger.Consumed(UnitLedger.MailRow));
        Assert.Equal(332 + 2 - 100, ledger.Granted(UnitLedger.MailRow));
        Assert.Equal(9, ledger.Granted(plank));
        for (int row = 0; row < ledger.Rows; row++)
        {
            Assert.Equal(ledger.Expected(row), ledger.LiveSum(row));
        }

        Assert.Equal((byte)LedgerState.World, ledger.HolderOf(ground));
        Assert.Equal((byte)LedgerState.Flight, ledger.HolderOf(flying));
        Assert.Equal((byte)LedgerState.Backlog, ledger.HolderOf(rig.Inventory.Backlog.Head));
        Assert.Equal(UnitLedger.NoHolder, ledger.HolderOf(rig.EntryAt(rig.InventoryA, 0, 0)));
    }

    [Fact]
    public void Recount_AfterAnEntryLeavesItsHolder_ForgetsTheHolderAndItsUnits()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort flying = rig.DetachedItem("plank", 4);
        UnitLedger ledger = rig.Inventory.Ledger;
        int plank = UnitLedger.RowOf(in rig.Entries[flying]);
        Assert.Equal(4, ledger.Live(plank, LedgerState.Flight));

        // Act
        rig.FreeDetached(flying);

        // Assert
        Assert.Equal(0, ledger.Live(plank, LedgerState.Flight));
        Assert.Equal(UnitLedger.NoHolder, ledger.HolderOf(flying));
        Assert.Equal(ledger.Expected(plank), ledger.LiveSum(plank));
    }
}
