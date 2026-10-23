using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/017: the <see cref="UnitLedger"/> (TR-inventory-012; ADR-0009 section 6; GDD R19): its shape (the mail row
/// plus one row per item def, six live states, Consumed split by <see cref="ConsumeReason"/>, Destroyed, spawned, granted) and the
/// <see cref="UnitLedger.Recount"/> test seam the rigs call after arranging entries outside the mutator (Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT,
/// TD-160 items 2-4, 8; advisory fix, Mail Review PR #36 A11).
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
    public void LiveStateCount_AndConsumeReasonCount_AreTheMemberCountsOfTheirEnums_ValuedZeroToCountMinusOne()
    {
        // Arrange: every LedgerState member is a live state (the enum has no non-live or sentinel member; "no holder" is the separate
        // byte UnitLedger.NoHolder) and every ConsumeReason member is a consume reason (no sentinel either). The ledger indexes its row
        // by (int)state and ConsumedBase + (int)reason, so each enum must also be exactly 0..count-1.
        int[] states = Enum.GetValues<LedgerState>().Select(s => (int)s).Order().ToArray();
        int[] reasons = Enum.GetValues<ConsumeReason>().Select(r => (int)r).Order().ToArray();

        // Act / Assert
        Assert.Equal(UnitLedger.LiveStateCount, states.Length);
        Assert.Equal(Enumerable.Range(0, UnitLedger.LiveStateCount), states);
        Assert.Equal(UnitLedger.ConsumeReasonCount, reasons.Length);
        Assert.Equal(Enumerable.Range(0, UnitLedger.ConsumeReasonCount), reasons);
        Assert.False(Enum.IsDefined(typeof(LedgerState), UnitLedger.NoHolder));
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
        // on A's cursor, a 2-plank WorldItem and a detached 5-letter entry (in flight); the rig-made entries bypass the crossings
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
        Assert.Equal(320 + 3, ledger.Spawned(UnitLedger.MailRow)); // the spawns and the append: kept by Recount
        Assert.Equal(2, ledger.Consumed(UnitLedger.MailRow));
        Assert.Equal(332 + 2 - 323, ledger.Granted(UnitLedger.MailRow)); // the rig-made 4 + 5 letters, plus the 2 consumed
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

    // ---------------------------------------------------------------- crossing table

    [Fact]
    [Trait("Id", "H-R19-1")]
    public void CrossingTable_AFixedScriptOverEveryRow_EachStepMovesExactlyItsDeltas_AndTheSumHoldsAfterEveryStep()
    {
        // Arrange: players A (slot 0) and B (slot 1); A: 3 letters at inventory (0,0) (the TryInsert merge target), 10 planks at
        // inventory (4,0) (P), 18 planks at inventory (0,1) (room for 2) and 5 letters at hotbar 3 (T); a fixture chest;
        // B: an 18-letter stack at hotbar 1 and every other panel cell full of fiber, so a 5-letter catch merges 2 and fumbles 3
        var p = new PickupRig();
        InvAutoPlaceRig r = p.R;
        var t = new LedgerScript(r.Inventory.Ledger);
        int plank = 1 + r.ItemIndex("plank").Value;
        const int Mail = UnitLedger.MailRow;
        DefIndex letter = r.KindIndex("letter");
        PlayerSlot slotA = PlayerSlot.FromValue(0);
        ushort invA = p.Inventory(0);
        ushort hotA = p.Hotbar(0);
        ushort curA = p.Cursor(0);
        ushort letters = r.PlaceMail(invA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(400, 3));
        ushort planks = r.PlaceItem(invA, "plank", 4, 0, 10);
        ushort nearlyFull = r.PlaceItem(invA, "plank", 0, 1, 18);
        ushort thrown = r.PlaceMail(hotA, "letter", 3, 0, Address4, InvAutoPlaceRig.Ids(500, 5));
        ushort chest = r.CreateGrid(4, 4);
        ushort stackB = r.PlaceMail(p.Hotbar(1), "letter", 1, 0, Address4, InvAutoPlaceRig.Ids(700, 18));
        FillPanel(r, 1, "fiber");

        // Act / Assert: spawn placement, the backlog append, a spawn the backlog-blocked Intake leaves to Mail
        t.Step("spawn 3 letters into the Intake", () => Assert.Equal(new SpawnResult(3, 0), r.InPhaseSpawn(letter, 1, 2, 3)), (Mail, S.Spawned, 3), (Mail, S.Placed, 3));
        IntakeSpawnOutfeedTests.FillIntake(r, 317, 1_000);
        t.Step("AppendBacklog 2 letters", () => Assert.True(InPhase(r, () => r.Inventory.AppendBacklog(new BacklogEntry(letter, Address4, 0, IntakeSpawnOutfeedTests.Mail(50, 51))))), (Mail, S.Spawned, 2), (Mail, S.Backlog, 2));
        t.Step("spawn overflow left to Mail", () => Assert.Equal(new SpawnResult(0, 2), r.InPhaseSpawn(letter, 60, 61)));

        // TakeUnit (a whole 1-letter entry), the drain the freed cell lets run next tick, TryInsert (a merge), ConsumeUnit (a partial take)
        UnitRef unit = default;
        t.Step("TakeUnit", () => Assert.True(InPhase(r, () => r.Inventory.TakeUnit(r.Inventory.IntakeContainer, Selector.Fifo, r.PlayerA, out unit))), (Mail, S.Placed, -1), (Mail, S.Belt, 1));
        t.Step("drain", () => r.Host.World.Tick(), (Mail, S.Backlog, -2), (Mail, S.Placed, 2));
        t.Step("TryInsert", () => Assert.True(InPhase(r, () => r.Inventory.TryInsert(r.IdOf(invA), in unit, r.PlayerA))), (Mail, S.Belt, -1), (Mail, S.Placed, 1));
        Assert.Equal(4, r.Entries[letters].Count);
        var insertedRef = new EntryRef(r.IdOf(invA), r.Entries[letters].Id);
        t.Step("ConsumeUnit Delivered", () => Assert.True(InPhase(r, () => r.Inventory.ConsumeUnit(insertedRef, ConsumeReason.Delivered, r.PlayerA))), (Mail, S.Placed, -1), (Mail, S.Delivered, 1));

        // The player ops: pick, place, split, merge, an in-container move, quick_move, return, the leash put-away
        t.Step("pick onto the cursor", () => Ok(r, r.PlayerA, PickOp(r, invA, planks)), (plank, S.Placed, -10), (plank, S.Cursor, 10));
        t.Step("place from the cursor", () => Ok(r, r.PlayerA, MoveOp(r, curA, planks, invA, 5, 0)), (plank, S.Cursor, -10), (plank, S.Placed, 10));
        t.Step("split onto the cursor", () => Ok(r, r.PlayerA, MoveOp(r, invA, planks, curA, 0, 0, 4)), (plank, S.Placed, -4), (plank, S.Cursor, 4));
        ushort piece = r.EntryAt(curA, 0, 0);
        t.Step("merge part of the cursor piece", () => Ok(r, r.PlayerA, MergeOp(r, curA, piece, invA, nearlyFull)), (plank, S.Cursor, -2), (plank, S.Placed, 2));
        Assert.Equal(2, r.Entries[piece].Count);
        t.Step("merge the rest of the cursor piece", () => Ok(r, r.PlayerA, MergeOp(r, curA, piece, invA, planks)), (plank, S.Cursor, -2), (plank, S.Placed, 2));
        t.Step("move inside one container", () => Ok(r, r.PlayerA, MoveOp(r, invA, planks, invA, 6, 0)));
        t.Step("quick_move to the hotbar", () => Ok(r, r.PlayerA, QuickMoveReturnTests.QuickMoveOp(r, invA, planks)));
        Assert.Equal(hotA, r.Entries[planks].ContainerSlot);
        int n = r.Entries[planks].Count; // what of P's 12 the hotbar slot holds after quick_move's merges
        t.Step("pick from the hotbar", () => Ok(r, r.PlayerA, PickOp(r, hotA, planks)), (plank, S.Placed, -n), (plank, S.Cursor, n));
        t.Step("return", () => Ok(r, r.PlayerA, new Op { Kind = InvOpKinds.Return, ReqId = 86 }), (plank, S.Cursor, -n), (plank, S.Placed, n));
        Assert.True(r.Inventory.TryOpenExternal(slotA, r.IdOf(chest)));
        t.Step("pick for the leash", () => Ok(r, r.PlayerA, PickOp(r, hotA, planks)), (plank, S.Placed, -n), (plank, S.Cursor, n));
        t.Step("leash put-away", () => Assert.True(InPhase(r, () => Leash(r, slotA))), (plank, S.Cursor, -n), (plank, S.Placed, n));

        // Flight: DetachForThrow, then B's catch merges 2 and fumbles 3; B consumes 1 and picks the fumble up partly
        t.Step("DetachForThrow", () => Assert.Equal(new DetachResult(default, r.Entries[thrown].Id, 5), InPhase(r, () => r.Inventory.DetachForThrow(slotA, r.Entries[thrown].Id, 5))), (Mail, S.Placed, -5), (Mail, S.Flight, 5));
        t.Step("TryCatch in and fumble", () => Assert.Equal(new CatchResult(2, 3, 0), p.Catch(1, thrown)), (Mail, S.Flight, -5), (Mail, S.Placed, 2), (Mail, S.World, 3));
        r.CaptureEvents = true;
        EntityId fumble = p.World.Table[p.WorldItemHolding(thrown)].Id;
        var stackRef = new EntryRef(r.IdOf(p.Hotbar(1)), r.Entries[stackB].Id);
        t.Step("ConsumeUnit Misdelivered", () => Assert.True(InPhase(r, () => r.Inventory.ConsumeUnit(stackRef, ConsumeReason.Misdelivered, r.PlayerB))), (Mail, S.Placed, -1), (Mail, S.Misdelivered, 1));
        t.Step("partial pickup", () => Assert.True(r.ReplyOfOk(p.Pickup(r.PlayerB, fumble))), (Mail, S.World, -1), (Mail, S.Placed, 1));
        Assert.True(p.World.OwnsEntry(thrown));
        Assert.Equal(2, r.Entries[thrown].Count);

        // CreateWorldItem: Thrown from Flight; Dropped from Placed in the caller's own call (its Remove); a whole pickup
        ushort second = r.PlaceMail(hotA, "letter", FreeX(r, hotA), 0, Address4, InvAutoPlaceRig.Ids(800, 2));
        t.Step("DetachForThrow again", () => Assert.Equal(2, InPhase(r, () => r.Inventory.DetachForThrow(slotA, r.Entries[second].Id, 2)).Units), (Mail, S.Placed, -2), (Mail, S.Flight, 2));
        t.Step("CreateWorldItem Thrown", () => p.W.Create(p.W.Now, second, WorldItemOrigin.Thrown), (Mail, S.Flight, -2), (Mail, S.World, 2));
        r.CaptureEvents = true;
        ushort dropped = r.PlaceItem(invA, "plank", FreeX(r, invA), 0, 3);
        t.Step("CreateWorldItem Dropped after the caller's Remove", () => DropInOneCall(r, invA, dropped), (plank, S.Placed, -3), (plank, S.World, 3));
        EntityId droppedItem = p.World.Table[p.WorldItemHolding(dropped)].Id;
        t.Step("whole pickup", () => Assert.True(r.ReplyOfOk(p.Pickup(r.PlayerA, droppedItem))), (plank, S.World, -3), (plank, S.Placed, 3));
        ushort cursorDrop = r.PlaceItem(curA, "plank", 0, 0, 2);
        t.Step("CreateWorldItem Dropped from the cursor after the caller's Remove", () => DropInOneCall(r, curA, cursorDrop), (plank, S.Cursor, -2), (plank, S.World, 2));

        // Returns on a water float: to the Intake (a cell freed), then to the backlog (the Intake full); a floating plank destroyed
        Assert.True(InPhase(r, () => r.Inventory.TakeUnit(r.Inventory.IntakeContainer, Selector.Fifo, r.PlayerA, out _)));
        uint at = Float(p, r.DetachedMail("letter", Address4, InvAutoPlaceRig.Ids(900, 2)));
        t.Step("water return to the Intake", () => p.W.RunTick(at), (Mail, S.World, -2), (Mail, S.Placed, 2));
        at = Float(p, r.DetachedMail("letter", Address4, InvAutoPlaceRig.Ids(910, 2)));
        t.Step("water return to the backlog", () => p.W.RunTick(at), (Mail, S.World, -2), (Mail, S.Backlog, 2));
        at = Float(p, r.DetachedItem("plank", 4));
        t.Step("water float destroys a plank", () => p.W.RunTick(at), (plank, S.World, -4), (plank, S.Destroyed, 4));

        // The cap: 512 live, then a 513th evicts the fumble's 2 letters (old enough) to the backlog, the Intake being full; a 514th the
        // other old mail (the second throw's 2 letters); a 515th, no mail left on the ground, destroys the lowest non-mail head, the
        // cursor-dropped 2 planks (DestroyNonMail, TD-135); then the 509 fill planks reach their ground expiry together and are destroyed
        // (the water-float plank above is the water FIFO's timer expiry)
        Assert.Equal(3, p.World.LiveCount);
        ushort[] fill = Enumerable.Range(0, 509).Select(_ => r.DetachedItem("plank", 1)).ToArray();
        uint now = p.W.Now;
        Assert.Equal(509, p.W.At(now, () => p.W.CreateAll(fill, now)));
        ushort last = r.DetachedItem("plank", 1);
        t.Step("cap eviction of a mail to the backlog", () => p.W.Create(p.W.Now, last), (plank, S.Flight, -1), (plank, S.World, 1), (Mail, S.World, -2), (Mail, S.Backlog, 2));
        Assert.False(p.World.OwnsEntry(thrown));
        Assert.True(r.Inventory.Backlog.Contains(thrown));
        ushort next = r.DetachedItem("plank", 1);
        t.Step("cap eviction of the second mail to the backlog", () => p.W.Create(p.W.Now, next), (plank, S.Flight, -1), (plank, S.World, 1), (Mail, S.World, -2), (Mail, S.Backlog, 2));
        Assert.True(r.Inventory.Backlog.Contains(second));
        ushort third = r.DetachedItem("plank", 1);
        t.Step("cap eviction destroys a non-mail", () => p.W.Create(p.W.Now, third), (plank, S.Flight, -1), (plank, S.World, -1), (plank, S.Destroyed, 2));
        Assert.False(p.World.OwnsEntry(cursorDrop));
        Assert.Equal(512, p.World.LiveCount);
        p.W.AdvanceTo(now + p.World.DurationTicks(ExpiryFifo.NonMailWorldItem));
        t.Step("ground expiry destroys the fill planks", () => p.W.RunTick(p.W.Now), (plank, S.World, -509), (plank, S.Destroyed, 509));
        Assert.Equal(3, p.World.LiveCount);
        Assert.True(t.Steps <= 50);
    }

    // ---------------------------------------------------------------- H-X-2 (ledger clause) and rejects

    [Fact]
    [Trait("Id", "H-X-2")]
    public void HX2_DetachForThrowOfEntry88Of5_PlacedMinus5FlightPlus5_ThenConsumeUnitDelivered_PlacedMinus1ConsumedPlus1()
    {
        // Arrange: hotbar index 3 holds 5 letters as entry 88; 2 letters in the inventory to consume from
        var rig = new InvAutoPlaceRig();
        while (rig.Entries.LastId < 87)
        {
            Assert.True(rig.Entries.TryAllocate(rig.Host.World.CurrentTick, out ushort burnt, out _));
            rig.Entries.Free(burnt);
        }

        ushort slot88 = rig.PlaceMail(rig.HotbarA, "letter", 3, 0, Address4, new uint[] { 11, 12, 13, 14, 15 });
        Assert.Equal(88u, rig.Entries[slot88].Id.Value);
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 30, 31 });
        var t = new LedgerScript(rig.Inventory.Ledger);
        var stackRef = new EntryRef(rig.IdOf(rig.InventoryA), rig.Entries[stack].Id);

        // Act / Assert
        t.Step(
            "DetachForThrow(slot, 88, 5)",
            () => Assert.Equal(5, InPhase(rig, () => rig.Inventory.DetachForThrow(PlayerSlot.FromValue(0), EntryId.Real(88), 5)).Units),
            (UnitLedger.MailRow, S.Placed, -5),
            (UnitLedger.MailRow, S.Flight, 5));
        t.Step(
            "ConsumeUnit(ref, Delivered, actor)",
            () => Assert.True(InPhase(rig, () => rig.Inventory.ConsumeUnit(stackRef, ConsumeReason.Delivered, rig.PlayerA))),
            (UnitLedger.MailRow, S.Placed, -1),
            (UnitLedger.MailRow, S.Delivered, 1));
        Assert.Equal(1, rig.Inventory.Ledger.Consumed(UnitLedger.MailRow));
        Assert.Equal(1, rig.Inventory.Ledger.Consumed(UnitLedger.MailRow, ConsumeReason.Delivered));
    }

    [Fact]
    public void Rejects_ARefusedOp_FalseApiCalls_ARefusedPutAway_ADrainSkip_AndARefusedCatch_LeaveEveryCounterUnchanged()
    {
        // Arrange: a 2-slot WorldItem table, both live; A's panel full (20 letters at inventory (0,0), axes, full plank stacks); 5 planks
        // on A's cursor with a chest open; a 5-letter flight; the Intake full but one cell
        var p = new PickupRig(tune: s => s with { WorldItemCapacity = 2 });
        InvAutoPlaceRig r = p.R;
        p.Drop(r.DetachedItem("plank", 1));
        p.Drop(r.DetachedItem("plank", 1));
        ushort letters = r.PlaceMail(p.Inventory(0), "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(1, 20));
        p.FillHotbar(0);
        p.FillInventory(0, from: 1);
        ushort axe = r.EntryAt(p.Hotbar(0), 1, 0);
        _ = r.PlaceItem(p.Cursor(0), "plank", 0, 0, 5);
        Assert.True(r.Inventory.TryOpenExternal(PlayerSlot.FromValue(0), r.IdOf(r.CreateGrid(2, 2))));
        ushort flight = p.Letters(101, 5);
        IntakeSpawnOutfeedTests.FillIntake(r, 319, 1_000);
        DefIndex large = r.KindIndex("large");
        DefIndex letter = r.KindIndex("letter");
        Assert.True(InPhase(r, () => r.Inventory.AppendBacklog(new BacklogEntry(large, Address4, 0, IntakeSpawnOutfeedTests.Mail(900)))));
        var t = new LedgerScript(r.Inventory.Ledger);
        var unit = new UnitRef(new MailId(5_000), default, letter, Address4, 0);
        var staleRef = new EntryRef(r.IdOf(p.Hotbar(0)), r.Entries[letters].Id);

        // Act / Assert: every step leaves every counter unchanged
        t.Step("a drain skip (the Large does not fit the one free cell)", () => r.Host.World.Tick());
        Assert.Equal(1, r.Inventory.BacklogEntries);
        t.Step("a refused op (out_of_bounds)", () => Assert.False(r.ReplyOfOk(r.Run(r.PlayerA, MoveOp(r, p.Inventory(0), letters, p.Inventory(0), 99, 0)))));
        t.Step("TryInsert into the Intake (outfeed only)", () => Assert.False(InPhase(r, () => r.Inventory.TryInsert(r.Inventory.IntakeContainer, in unit, r.PlayerA))));
        t.Step("TryInsert with no room", () => Assert.False(InPhase(r, () => r.Inventory.TryInsert(r.IdOf(p.Hotbar(0)), in unit, r.PlayerA))));
        t.Step("AppendBacklog of descending ids", () => Assert.False(InPhase(r, () => r.Inventory.AppendBacklog(new BacklogEntry(letter, Address4, 0, IntakeSpawnOutfeedTests.Mail(9, 8))))));
        t.Step("ConsumeUnit of a stale ref", () => Assert.False(InPhase(r, () => r.Inventory.ConsumeUnit(staleRef, ConsumeReason.Delivered, r.PlayerA))));
        t.Step("DetachForThrow of the wrong count", () => Assert.Equal(InvReason.StaleEntry, InPhase(r, () => r.Inventory.DetachForThrow(PlayerSlot.FromValue(0), r.Entries[axe].Id, 2)).Reason));
        t.Step("a refused leash put-away", () => Assert.True(InPhase(r, () => Leash(r, PlayerSlot.FromValue(0)))));
        Assert.NotEqual(Occupancy.Free, r.EntryAt(p.Cursor(0), 0, 0));
        t.Step("a refused catch {0, 0}", () => Assert.Equal(new CatchResult(0, 0, InvReason.CapacityExhausted), p.Catch(0, flight)));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The counters a <see cref="LedgerScript"/> step names: the six live states, then Consumed by reason, Destroyed, spawned, granted.</summary>
    internal enum S
    {
        Placed = 0,
        Cursor = 1,
        World = 2,
        Flight = 3,
        Belt = 4,
        Backlog = 5,
        Delivered = 6,
        Misdelivered = 7,
        ReturnedToSender = 8,
        Destroyed = 9,
        Spawned = 10,
        Granted = 11,
    }

    // ---------------------------------------------------------------- tick-end check

    [Fact]
    public void Ids_CheckCode15_CounterId51_SimAssertCode84_ArePinned()
    {
        // Arrange / Act / Assert: append-only values (story 017; next free CheckCode 16)
        Assert.Equal(15, (int)CheckCode.InventoryConservationMismatch);
        Assert.Equal(51, (int)CounterId.InventoryConservationMismatches);
        Assert.Equal(84, SimAssertCode.InventoryConservationScan);
    }

    [Fact]
    [Trait("Id", "H-R19-2")]
    public void Check_ABalancedWorldDrivenThroughCrossings_WritesNoRecord_AndCountsNoMismatch()
    {
        // Arrange: spawns, a placed stack, a pick onto the cursor and a throw, each tick ending in the check
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        _ = rig.InPhaseSpawn(letter, 1, 2, 3);
        ushort planks = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 6);
        ushort letters = rig.PlaceMail(rig.HotbarA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(40, 4));
        Ok(rig, rig.PlayerA, PickOp(rig, rig.InventoryA, planks));

        // Act
        DetachResult thrown = InPhase(rig, () => rig.Inventory.DetachForThrow(PlayerSlot.FromValue(0), rig.Entries[letters].Id, 4));
        rig.Host.World.Tick();

        // Assert
        Assert.Equal(4, thrown.Units);
        Assert.Equal((byte)LedgerState.Flight, rig.Inventory.Ledger.HolderOf(letters));
        Assert.Equal(0, CheckRecords(rig));
        Assert.Equal(0, rig.Counters[CounterId.InventoryConservationMismatches]);
    }

    [Fact]
    [Trait("Id", "H-R19-2")]
    public void Check_ACorruptedSpawned_WritesOneRateLimitedRecord_ACountsEveryTick_AndTheTickCompletes_InEveryBuild()
    {
        // Arrange: a recounted world; the mail row's spawned +3 and the plank row's spawned -2 (the seam touches only spawned, which
        // only the Σ check reads, so the INVENTORY_ASSERT scan does not trip in Debug)
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 6);
        _ = rig.InPhaseSpawn(rig.KindIndex("letter"), 1, 2);
        int plank = 1 + rig.ItemIndex("plank").Value;
        UnitLedger ledger = rig.Inventory.Ledger;
        ledger.CorruptSpawnedForTests(UnitLedger.MailRow, 3);
        ledger.CorruptSpawnedForTests(plank, -2);
        long before = rig.Counters[CounterId.InventoryConservationMismatches];

        // Act: two ticks, inside the ring's 30-tick per-code window
        rig.Host.World.Tick();
        rig.Host.World.Tick();

        // Assert: one record (the first finding; the rest suppressed by the rate limit), a = row, b = Σ live − expected; the counter
        // counts all four findings
        SimCheckRecord[] records = Records(rig, CheckCode.InventoryConservationMismatch);
        SimCheckRecord only = Assert.Single(records);
        Assert.Equal(UnitLedger.MailRow, only.A);
        Assert.Equal(-3, only.B);
        Assert.Equal(4, rig.Counters[CounterId.InventoryConservationMismatches] - before);
    }

    [Fact]
    public void Check_DirectlyOnACorruptedItemRow_RecordsTheRowAndTheDelta()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 6);
        int plank = 1 + rig.ItemIndex("plank").Value;
        rig.Inventory.Ledger.CorruptSpawnedForTests(plank, -2);

        // Act
        rig.Inventory.ConservationCheck.Check(rig.Host.World.CurrentTick);

        // Assert
        SimCheckRecord only = Assert.Single(Records(rig, CheckCode.InventoryConservationMismatch));
        Assert.Equal(plank, only.A);
        Assert.Equal(2, only.B);
        Assert.Equal(1, rig.Counters[CounterId.InventoryConservationMismatches]);
    }

    [Fact]
    [Trait("Id", "H-R19-2")]
    public void Check_ANegativeBeltFromAnInsertNeverTaken_RecordsTheStateInBits24To31AndTheValue_InEveryBuild()
    {
        // Arrange: a fresh letter no entry holds and no TakeUnit took (TryInsert crosses Belt -1, Placed +1: Σ holds, Belt is -1)
        var rig = new InvAutoPlaceRig();
        var unit = new UnitRef(new MailId(7_000), default, rig.KindIndex("letter"), Address4, 0);

        // Act
        bool inserted = InPhase(rig, () => rig.Inventory.TryInsert(rig.IdOf(rig.InventoryA), in unit, rig.PlayerA));

        // Assert: a = mail row | (Belt ordinal + 1) << 24, b = the counter
        Assert.True(inserted);
        SimCheckRecord only = Assert.Single(Records(rig, CheckCode.InventoryConservationMismatch));
        Assert.Equal(((long)LedgerState.Belt + 1) << 24, only.A);
        Assert.Equal(-1, only.B);
        Assert.Equal(1, rig.Counters[CounterId.InventoryConservationMismatches]);
    }

    // ---------------------------------------------------------------- duplicate TryInsert (TD-145, narrowed)

    [Fact]
    public void DuplicateTryInsert_Release_ShowsANegativeBeltRecord_AndTheTickCompletes()
    {
        Assert.SkipWhen(TestBuildExpectation.SimAssertsExpected, "Release only: an INVENTORY_ASSERT build's scan asserts (its Debug twin).");

        // Arrange: letter 400 is in A's inventory; a belt re-inserts it without having taken it
        var rig = new InvAutoPlaceRig();
        (ushort _, UnitRef dup) = ArrangeDuplicateInsert(rig);

        // Act
        bool inserted = InPhase(rig, () => rig.Inventory.TryInsert(rig.IdOf(rig.HotbarA), in dup, rig.PlayerA));

        // Assert
        Assert.True(inserted);
        SimCheckRecord only = Assert.Single(Records(rig, CheckCode.InventoryConservationMismatch));
        Assert.Equal(((long)LedgerState.Belt + 1) << 24, only.A);
        Assert.Equal(-1, only.B);
        Assert.Equal(ScanFindingKind.None, rig.Inventory.ConservationCheck.ScanFinding.Kind); // no scan in Release
    }

    [Fact]
    public void DuplicateTryInsert_Debug_TheScanNamesTheDuplicatedMailId_AfterTheNegativeBeltRecord()
    {
        Assert.SkipUnless(TestBuildExpectation.SimAssertsExpected, "INVENTORY_ASSERT is compiled out of this build (its Release twin runs).");

        // Arrange
        var rig = new InvAutoPlaceRig();
        (ushort _, UnitRef dup) = ArrangeDuplicateInsert(rig);

        // Act: the check runs at the end of the insert's tick
        SimAssertException thrown = Assert.Throws<SimAssertException>(
            () => InPhase(rig, () => rig.Inventory.TryInsert(rig.IdOf(rig.HotbarA), in dup, rig.PlayerA)));

        // Assert
        Assert.Equal(SimAssertCode.InventoryConservationScan, thrown.Code);
        ScanFindingRecord finding = rig.Inventory.ConservationCheck.ScanFinding;
        Assert.Equal(ScanFindingKind.DuplicateMailId, finding.Kind);
        Assert.Equal(400, finding.A);
        SimCheckRecord only = Assert.Single(Records(rig, CheckCode.InventoryConservationMismatch));
        Assert.Equal(((long)LedgerState.Belt + 1) << 24, only.A);
    }

    // ---------------------------------------------------------------- INVENTORY_ASSERT scan

    [Fact]
    public void Scan_Debug_ASeededLostUnit_AssertsWithTheRowAndState()
    {
        Assert.SkipUnless(TestBuildExpectation.SimAssertsExpected, "INVENTORY_ASSERT is compiled out of this build (its Release twin runs).");

        // Arrange: 6 planks placed through the mutator, then one unit lost outside it (the ledger still says 6)
        var rig = new InvAutoPlaceRig();
        ushort planks = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 6);
        int plank = 1 + rig.ItemIndex("plank").Value;
        rig.Entries.SetCount(planks, 5);

        // Act
        SimAssertException thrown = Assert.Throws<SimAssertException>(() => rig.Inventory.ConservationCheck.Check(rig.Host.World.CurrentTick));

        // Assert: the Σ check saw nothing (the ledger is self-consistent); the scan's recount did
        Assert.Equal(SimAssertCode.InventoryConservationScan, thrown.Code);
        ScanFindingRecord finding = rig.Inventory.ConservationCheck.ScanFinding;
        Assert.Equal(ScanFindingKind.Recount, finding.Kind);
        Assert.Equal(plank, finding.A);
        Assert.Equal((long)LedgerState.Placed, finding.B);
        Assert.Empty(Records(rig, CheckCode.InventoryConservationMismatch));
    }

    [Fact]
    public void Scan_Release_ASeededLostUnit_IsNotScanned_TheTickCompletes_AndNothingIsRecorded()
    {
        Assert.SkipWhen(TestBuildExpectation.SimAssertsExpected, "Release only: an INVENTORY_ASSERT build's scan asserts (its Debug twin).");

        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort planks = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 6);
        rig.Entries.SetCount(planks, 5);

        // Act
        rig.Host.World.Tick();

        // Assert
        Assert.Equal(ScanFindingKind.None, rig.Inventory.ConservationCheck.ScanFinding.Kind);
        Assert.Empty(Records(rig, CheckCode.InventoryConservationMismatch));
    }

    [Fact]
    public void Scan_Debug_ASeededDuplicatedMailId_AssertsThroughTheTick_AndNamesTheId()
    {
        Assert.SkipUnless(TestBuildExpectation.SimAssertsExpected, "INVENTORY_ASSERT is compiled out of this build (its Release twin runs).");

        // Arrange: letter 900 held by two entries (arranged outside the mutator, then recounted, so every counter agrees)
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 899, 900 });

        // Act: the second placement's tick ends in the check
        SimAssertException thrown = Assert.Throws<SimAssertException>(
            () => rig.PlaceMail(rig.HotbarA, "letter", 0, 0, Address4, new uint[] { 900, 901 }));

        // Assert
        Assert.Equal(SimAssertCode.InventoryConservationScan, thrown.Code);
        ScanFindingRecord finding = rig.Inventory.ConservationCheck.ScanFinding;
        Assert.Equal(ScanFindingKind.DuplicateMailId, finding.Kind);
        Assert.Equal(900, finding.A);
    }

    [Fact]
    public void Scan_Release_ASeededDuplicatedMailId_IsNotScanned_AndTheTickCompletes()
    {
        Assert.SkipWhen(TestBuildExpectation.SimAssertsExpected, "Release only: an INVENTORY_ASSERT build's scan asserts (its Debug twin).");

        // Arrange
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 899, 900 });

        // Act
        _ = rig.PlaceMail(rig.HotbarA, "letter", 0, 0, Address4, new uint[] { 900, 901 });

        // Assert
        Assert.Equal(ScanFindingKind.None, rig.Inventory.ConservationCheck.ScanFinding.Kind);
        Assert.Empty(Records(rig, CheckCode.InventoryConservationMismatch));
    }

    [Fact]
    public void Scan_Debug_ADetachedEntryWhoseHolderIsNotWhereItIsHeld_Asserts_AndAnUnheldDetachedEntryDoesNot()
    {
        Assert.SkipUnless(TestBuildExpectation.SimAssertsExpected, "INVENTORY_ASSERT is compiled out of this build (its Release twin runs).");

        // Arrange: a recounted flying entry (holder Flight), then one in no holder state (its caller's), then a holder byte that names
        // World for an entry no WorldItem holds
        var rig = new InvAutoPlaceRig();
        ushort flying = rig.DetachedMail("letter", Address4, InvAutoPlaceRig.Ids(30, 2));
        rig.Inventory.ConservationCheck.Check(rig.Host.World.CurrentTick);
        rig.Inventory.Mutator.LeaveHolder(flying, 2);
        rig.Inventory.ConservationCheck.Check(rig.Host.World.CurrentTick); // in no state: not counted, no scan finding (Σ records it)
        rig.Inventory.Mutator.EnterHolder(LedgerState.Flight, flying);
        rig.Inventory.Ledger.SetHolder(flying, (byte)LedgerState.World);

        // Act
        SimAssertException thrown = Assert.Throws<SimAssertException>(() => rig.Inventory.ConservationCheck.Check(rig.Host.World.CurrentTick));

        // Assert
        Assert.Equal(SimAssertCode.InventoryConservationScan, thrown.Code);
        ScanFindingRecord finding = rig.Inventory.ConservationCheck.ScanFinding;
        Assert.Equal(ScanFindingKind.Holder, finding.Kind);
        Assert.Equal(flying, finding.A);
        Assert.Equal((long)LedgerState.Flight, finding.B);
    }

    [Fact]
    public void Scan_Release_AHolderMismatch_IsNotScanned()
    {
        Assert.SkipWhen(TestBuildExpectation.SimAssertsExpected, "Release only: an INVENTORY_ASSERT build's scan asserts (its Debug twin).");

        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort flying = rig.DetachedMail("letter", Address4, InvAutoPlaceRig.Ids(30, 2));
        rig.Inventory.Ledger.SetHolder(flying, (byte)LedgerState.World);

        // Act
        rig.Inventory.ConservationCheck.Check(rig.Host.World.CurrentTick);

        // Assert
        Assert.Equal(ScanFindingKind.None, rig.Inventory.ConservationCheck.ScanFinding.Kind);
    }

    /// <summary>Letter 400 placed in A's inventory and a <see cref="UnitRef"/> naming it again that no <c>TakeUnit</c> took.</summary>
    private static (ushort Entry, UnitRef Unit) ArrangeDuplicateInsert(InvAutoPlaceRig rig)
    {
        ushort held = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 400 });
        return (held, new UnitRef(new MailId(400), default, rig.KindIndex("letter"), Address4, 0));
    }

    /// <summary>The check-ring records of <paramref name="code"/>, oldest first.</summary>
    private static SimCheckRecord[] Records(InvAutoPlaceRig rig, CheckCode code)
    {
        SimCheckRing ring = rig.Host.World.CheckRing;
        var found = new List<SimCheckRecord>();
        for (int i = 0; i < ring.Count; i++)
        {
            if (ring[i].Check == code)
            {
                found.Add(ring[i]);
            }
        }

        return found.ToArray();
    }

    /// <summary>The conservation records written so far.</summary>
    private static int CheckRecords(InvAutoPlaceRig rig) => Records(rig, CheckCode.InventoryConservationMismatch).Length;

    private static T InPhase<T>(InvAutoPlaceRig rig, Func<T> call) => IntakeSpawnOutfeedTests.InPhase(rig, call);

    private static void Ok(InvAutoPlaceRig rig, ActorId actor, Op op) => MoveSplitMergeTests.AssertOk(rig.Run(actor, in op));

    private static bool Leash(InvAutoPlaceRig rig, PlayerSlot slot)
    {
        rig.Inventory.CloseExternal(slot, CloseCause.Leash);
        return true;
    }

    /// <summary>Creates a WorldItem on water of the detached entry now; returns the tick it floats back on (300 ticks later).</summary>
    private static uint Float(PickupRig p, ushort entry)
    {
        uint created = p.W.Now;
        _ = p.W.Create(created, entry, WorldItemOrigin.Dropped, onWater: true);
        p.R.CaptureEvents = true;
        return created + p.World.DurationTicks(ExpiryFifo.WaterFloatNonMail);
    }

    /// <summary>One fixture op that removes the placed entry through the mutator and then creates a Dropped WorldItem of it, in one call.</summary>
    private static void DropInOneCall(InvAutoPlaceRig rig, ushort container, ushort entry)
    {
        EntityId created = EntityId.None;
        Reply reply = rig.Step(
            (ref InvPlan plan) => plan.Touch(container) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan plan) =>
            {
                rig.Inventory.Mutator.Remove(ref plan, entry);
                created = rig.Inventory.CreateWorldItem(rig.Entries[entry].Id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, rig.Host.World.CurrentTick);
            });
        Assert.True(reply.IsOk);
        Assert.False(created.IsNone);
    }

    /// <summary>The first free cell of row 0 of a container (a slots hotbar skips its reserved slot 0).</summary>
    private static int FreeX(InvAutoPlaceRig rig, ushort container)
    {
        ContainerHeader h = rig.Header(container);
        for (int x = h.Layout == ContainerLayout.Slots ? 1 : 0; x < h.W; x++)
        {
            if (rig.Inventory.Occupancy.OwnerAt(container, h.W, x, 0) == Occupancy.Free)
            {
                return x;
            }
        }

        throw new InvalidOperationException("no free cell in row 0");
    }

    /// <summary>One <paramref name="item"/> (1 unit) on every free usable cell of <paramref name="player"/>'s hotbar and player_inventory.</summary>
    private static void FillPanel(InvAutoPlaceRig rig, int player, string item)
    {
        foreach (ushort c in new[] { rig.HotbarOf(player), rig.InventoryOf(player) })
        {
            ContainerHeader h = rig.Header(c);
            bool slots = h.Layout == ContainerLayout.Slots;
            for (int y = 0; y < h.H; y++)
            {
                for (int x = slots && y == 0 ? 1 : 0; x < h.W; x++)
                {
                    if (rig.Inventory.Occupancy.OwnerAt(c, h.W, x, y) == Occupancy.Free)
                    {
                        _ = rig.PlaceItem(c, item, x, y, 1);
                    }
                }
            }
        }
    }

    private static Op PickOp(InvAutoPlaceRig rig, ushort from, ushort entry)
    {
        var op = new Op { Kind = InvOpKinds.Pick, ReqId = 80 };
        op.SetPayload(new PickPayload { From = rig.IdOf(from).Value, EntryId = rig.Entries[entry].Id.Value });
        return op;
    }

    private static Op MoveOp(InvAutoPlaceRig rig, ushort from, ushort entry, ushort to, int x, int y, byte count = 0)
    {
        var op = new Op { Kind = InvOpKinds.Move, ReqId = 81 };
        op.SetPayload(new MovePayload
        {
            From = rig.IdOf(from).Value, EntryId = rig.Entries[entry].Id.Value, To = rig.IdOf(to).Value, BaseVersion = rig.Header(to).Version,
            X = (short)x, Y = (short)y, Count = count,
        });
        return op;
    }

    private static Op MergeOp(InvAutoPlaceRig rig, ushort from, ushort entry, ushort to, ushort target)
    {
        var op = new Op { Kind = InvOpKinds.Merge, ReqId = 83 };
        op.SetPayload(new MergePayload
        {
            From = rig.IdOf(from).Value, EntryId = rig.Entries[entry].Id.Value, To = rig.IdOf(to).Value, TargetEntryId = rig.Entries[target].Id.Value,
        });
        return op;
    }

    /// <summary>
    /// A crossing-table script over one ledger: each <see cref="Step"/> snapshots every counter of every row, runs its action, and asserts
    /// that exactly the named counters moved by exactly the named deltas, and that <c>Σ live = spawned + granted − consumed − destroyed</c>
    /// holds for every row afterwards.
    /// </summary>
    internal sealed class LedgerScript
    {
        private const int Counters = 12;
        private readonly UnitLedger _ledger;

        public LedgerScript(UnitLedger ledger)
        {
            _ledger = ledger;
            AssertSumHolds("before the script");
        }

        public int Steps { get; private set; }

        public long[] Snapshot()
        {
            var v = new long[_ledger.Rows * Counters];
            for (int row = 0; row < _ledger.Rows; row++)
            {
                int b = row * Counters;
                for (int s = 0; s < UnitLedger.LiveStateCount; s++)
                {
                    v[b + s] = _ledger.Live(row, (LedgerState)s);
                }

                for (int c = 0; c < UnitLedger.ConsumeReasonCount; c++)
                {
                    v[b + (int)S.Delivered + c] = _ledger.Consumed(row, (ConsumeReason)c);
                }

                v[b + (int)S.Destroyed] = _ledger.Destroyed(row);
                v[b + (int)S.Spawned] = _ledger.Spawned(row);
                v[b + (int)S.Granted] = _ledger.Granted(row);
            }

            return v;
        }

        public void Step(string name, Action act, params (int Row, S Counter, long Delta)[] expected)
        {
            Steps++;
            long[] before = Snapshot();
            act();
            long[] after = Snapshot();
            var want = new long[before.Length];
            foreach ((int row, S counter, long delta) in expected)
            {
                want[(row * Counters) + (int)counter] += delta;
            }

            var wrong = new List<string>();
            for (int i = 0; i < before.Length; i++)
            {
                if (after[i] - before[i] != want[i])
                {
                    wrong.Add($"row {i / Counters} {(S)(i % Counters)}: moved {after[i] - before[i]}, expected {want[i]}");
                }
            }

            Assert.True(wrong.Count == 0, $"step {Steps} '{name}': " + string.Join("; ", wrong));
            AssertSumHolds($"step {Steps} '{name}'");
        }

        public void AssertSumHolds(string when)
        {
            for (int row = 0; row < _ledger.Rows; row++)
            {
                Assert.True(_ledger.LiveSum(row) == _ledger.Expected(row), $"{when}: row {row} Σ live {_ledger.LiveSum(row)} != expected {_ledger.Expected(row)}");
            }
        }
    }
}
