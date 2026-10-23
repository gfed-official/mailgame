using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

public partial class QuickMoveReturnTests
{
    // ------------------------------------------------------------------ Return: H-R7-9

    /// <summary>
    /// H-R7-9, the three legs (GDD l.95, l.706), for an item (planks 5), mail (letters [1..4] to #3) and a 1 x 2 log picked rotated, each
    /// picked from chest (2,1): <c>origin_free</c> → E back at (2,1) with its id and rotation, chest +1 and cursor +1, nothing else;
    /// <c>origin_filled</c> (the other player moved fiber onto (2,1) meanwhile) → E AutoPlaced whole into the panel (the cursor row's
    /// first target, inventory (0,0)), inventory +1 and cursor +1, the chest unchanged; <c>panel_full</c> (the same, with every panel cell
    /// holding fiber) → <c>no_room</c>, E stays on the cursor with its origin, nothing changes. <c>ResultVersionA</c> is the cursor and
    /// <c>ResultVersionB</c> the receiver (ok) or the row's first target (reject), with the two made different first. No path creates a
    /// WorldItem.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "plank", "origin_free")]
    [InlineData(1, "plank", "origin_free")]
    [InlineData(0, "plank", "origin_filled")]
    [InlineData(1, "plank", "origin_filled")]
    [InlineData(0, "plank", "panel_full")]
    [InlineData(1, "plank", "panel_full")]
    [InlineData(0, "letter", "origin_free")]
    [InlineData(1, "letter", "origin_free")]
    [InlineData(0, "letter", "origin_filled")]
    [InlineData(1, "letter", "origin_filled")]
    [InlineData(0, "letter", "panel_full")]
    [InlineData(1, "letter", "panel_full")]
    [InlineData(0, "log", "origin_free")]
    [InlineData(1, "log", "origin_free")]
    [InlineData(0, "log", "origin_filled")]
    [InlineData(1, "log", "origin_filled")]
    [InlineData(0, "log", "panel_full")]
    [InlineData(1, "log", "panel_full")]
    public void HR79_Return_OriginFree_OriginFilled_PanelFull(int slot, string kind, string leg)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        bool rotated = kind == "log";
        ushort e = kind switch
        {
            "plank" => rig.PlaceItem(chest, "plank", 2, 1, 5),
            "letter" => rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 4)),
            _ => rig.PlaceItem(chest, "log", 2, 1, 1, rotated: true),
        };
        EntryId id = rig.Entries[e].Id;
        byte count = rig.Entries[e].Count;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, e)));
        if (leg != "origin_free")
        {
            FillOriginByTheOtherPlayer(rig, slot, chest, 2, 1);
        }

        if (leg == "panel_full")
        {
            FillPanelWithFiber(rig, slot);
        }

        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[cursor], versions[chest]);
        Assert.NotEqual(versions[cursor], versions[inventory]);
        int worldItems = rig.Inventory.WorldItems.LiveCount;
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        if (leg == "panel_full")
        {
            AssertRejected(rig, log, InvReason.NoRoom, before, slot, (versions[cursor], versions[inventory]));
            Assert.Equal((id, cursor, count), (rig.Entries[e].Id, rig.Entries[e].ContainerSlot, rig.Entries[e].Count));
            AssertOrigin(rig, ps, id, rig.IdOf(chest), 2, 1, rotated);
            return;
        }

        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        ushort to = leg == "origin_free" ? chest : inventory;
        (byte x, byte y) = leg == "origin_free" ? ((byte)2, (byte)1) : ((byte)0, (byte)0);
        Assert.Equal((id, to, x, y, count), (rig.Entries[e].Id, rig.Entries[e].ContainerSlot, rig.Entries[e].X, rig.Entries[e].Y, rig.Entries[e].Count));
        if (leg == "origin_free")
        {
            Assert.Equal(rotated, rig.Entries[e].Rotated);
        }

        Assert.Equal(0, (int)rig.Header(cursor).EntryCount);
        Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[to]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[cursor] + 1, versions[to] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(((ushort)count, (ushort)count), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal(worldItems, rig.Inventory.WorldItems.LiveCount);
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
    }

    // ------------------------------------------------------------------ Return fallbacks

    /// <summary>
    /// The origin leg runs only for a recorded origin whose container is in the actor's open set (Mail Lead planning default pending Evan
    /// (TD-153), item 2); otherwise the panel leg runs, never <c>not_open</c>. The origin cells are free in every row: <c>closed</c> (a
    /// real <c>close</c> of the chest), <c>other_external</c> (a second chest opened, which closes the first), <c>no_origin</c> (the
    /// planks were placed straight on the cursor, never picked). Planks 5 land whole at inventory (0,0) with their id; the chest is
    /// unchanged; cursor +1 and inventory +1.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "closed")]
    [InlineData(1, "closed")]
    [InlineData(0, "other_external")]
    [InlineData(1, "other_external")]
    [InlineData(0, "no_origin")]
    [InlineData(1, "no_origin")]
    public void Return_OriginNotInTheOpenSetOrNotRecorded_GoesToThePanel_NeverNotOpen(int slot, string row)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort planks;
        if (row == "no_origin")
        {
            planks = rig.PlaceItem(cursor, "plank", 0, 0, 5);
            Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
        }
        else
        {
            planks = rig.PlaceItem(chest, "plank", 2, 1, 5);
            MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, planks)));
            if (row == "closed")
            {
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), CloseOp(rig, chest)));
            }
            else
            {
                _ = MoveSplitMergeTests.OpenChest(rig, slot);
            }

            Assert.False(rig.Inventory.OpenSet.IsInOpenSet(ps, rig.IdOf(chest)));
            AssertOrigin(rig, ps, rig.Entries[planks].Id, rig.IdOf(chest), 2, 1, false);
        }

        EntryId id = rig.Entries[planks].Id;
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        int worldItems = rig.Inventory.WorldItems.LiveCount;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal((id, inventory, (byte)0, (byte)0, (byte)5), (rig.Entries[planks].Id, rig.Entries[planks].ContainerSlot, rig.Entries[planks].X, rig.Entries[planks].Y, rig.Entries[planks].Count));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[cursor] + 1, versions[inventory] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(worldItems, rig.Inventory.WorldItems.LiveCount);
    }

    /// <summary>
    /// An origin in the Intake is never merged into (R14 l.141, "every insertion into the Intake is NoMerge"): a letter picked from the
    /// open Intake whose cell now holds a same-StackKey letter goes to the panel (inventory (0,0)); the Intake is unchanged.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_OriginInTheIntakeHoldingASameKeyLetter_NoMerge_GoesToThePanel(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        DefIndex letter = rig.KindIndex("letter");
        Assert.Equal(1, IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address3, 0, new[] { new MailId(500) }, MergePolicy.NoMerge).Placed));
        ushort picked = rig.EntryAt(rig.Intake, 0, 0);
        OpenIntake(rig, slot);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, rig.Intake, picked)));
        ushort resident = rig.PlaceMail(rig.Intake, "letter", 0, 0, Address3, new uint[] { 600 });
        Assert.Equal(StackKey.Of(in rig.Entries[resident]), StackKey.Of(in rig.Entries[picked]));
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        MoveSplitMergeTests.AssertOk(log);
        Assert.Equal(inventory, rig.Entries[picked].ContainerSlot);
        Assert.Equal(new uint[] { 500 }, rig.IdsOf(picked));
        Assert.Equal(new uint[] { 600 }, rig.IdsOf(resident));
        Assert.DoesNotContain(MoveSplitMergeTests.RecordHeads(log), r => r.Kind == InvEventKinds.Merged);
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
    }

    /// <summary>
    /// A free Intake origin (GDD l.95, "into its origin cells if they are still free"): a letter picked from the open Intake whose cell is
    /// still free goes back there whole with its id and mail ids, inserted NoMerge (R14 l.141, "every insertion into the Intake is
    /// NoMerge") beside a same-StackKey resident that is left unchanged, and stamped with a new <c>arrivalSeq</c> above every other Intake
    /// entry's, so it leaves last (GDD l.400, "the inserted entry gets a new arrivalSeq, so it leaves last"). Only the cursor and the
    /// Intake bump; <c>ResultVersionA</c> is the cursor, <c>ResultVersionB</c> the Intake.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_ToAFreeIntakeOrigin_BackThereNoMerge_NewArrivalSeq_LeavesLast(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort cursor = rig.CursorOf(slot);
        DefIndex letter = rig.KindIndex("letter");
        Assert.Equal(2, IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address3, 0, new[] { new MailId(500), new MailId(501) }, MergePolicy.NoMerge).Placed));
        ushort picked = rig.EntryAt(rig.Intake, 0, 0);
        ushort resident = rig.EntryAt(rig.Intake, 1, 0);
        Assert.NotEqual(picked, resident);
        Assert.Equal(StackKey.Of(in rig.Entries[resident]), StackKey.Of(in rig.Entries[picked]));
        uint pickedSeq = rig.Entries[picked].ArrivalSeq;
        Assert.True(pickedSeq < rig.Entries[resident].ArrivalSeq);
        EntryId id = rig.Entries[picked].Id;
        uint[] mailIds = rig.IdsOf(picked);
        uint residentSeq = rig.Entries[resident].ArrivalSeq;
        OpenIntake(rig, slot);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, rig.Intake, picked)));
        AssertOrigin(rig, PlayerSlot.FromValue((byte)slot), id, rig.Inventory.IntakeContainer, 0, 0, false);
        Assert.Equal(Occupancy.Free, rig.EntryAt(rig.Intake, 0, 0));
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[cursor], versions[rig.Intake]);
        int worldItems = rig.Inventory.WorldItems.LiveCount;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal(picked, rig.EntryAt(rig.Intake, 0, 0));
        Assert.Equal((id, rig.Intake, (byte)1), (rig.Entries[picked].Id, rig.Entries[picked].ContainerSlot, rig.Entries[picked].Count));
        Assert.Equal(mailIds, rig.IdsOf(picked));
        Assert.Equal(((byte)1, residentSeq, 2), (rig.Entries[resident].Count, rig.Entries[resident].ArrivalSeq, (int)rig.Header(rig.Intake).EntryCount));
        Assert.DoesNotContain(MoveSplitMergeTests.RecordHeads(log), r => r.Kind == InvEventKinds.Merged);
        uint newSeq = rig.Entries[picked].ArrivalSeq;
        Assert.True(newSeq > pickedSeq);
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            EntryRecord r = rig.Entries[(ushort)s];
            if (r.IsLive && r.ContainerSlot == rig.Intake && s != picked)
            {
                Assert.True(newSeq > r.ArrivalSeq);
            }
        }

        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[rig.Intake]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[cursor] + 1, versions[rig.Intake] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(worldItems, rig.Inventory.WorldItems.LiveCount);
    }

    /// <summary>
    /// A mergeable origin takes <c>min(count, room)</c> of the oldest units and the rest goes on to the panel leg in the same op (Mail
    /// Lead planning default pending Evan (TD-153), item 3). A = 10 letters [1..10] to #3 picked from chest (2,1), which then holds a
    /// same-key stack; the inventory holds a same-key stack of 15 [200..214] at (0,0). Rows: <c>whole_at_origin</c> (origin stack 5,
    /// room 15: all 10 merge there, A is retired, the inventory unchanged); <c>remainder_on_cursor</c> (origin stack 18 [100..117], room 2:
    /// [1, 2] merge there, [3..7] into the inventory stack, every other panel cell holds fiber, so A keeps [8, 9, 10] on the cursor with
    /// its id and its origin, requested 10 / got 7; a second <c>return</c> is <c>no_room</c>); <c>rest_to_free_cell</c> (the same with one
    /// free inventory cell: A moves there with [8, 9, 10] and its id). The receivers and the cursor bump once each.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "whole_at_origin")]
    [InlineData(1, "whole_at_origin")]
    [InlineData(0, "remainder_on_cursor")]
    [InlineData(1, "remainder_on_cursor")]
    [InlineData(0, "rest_to_free_cell")]
    [InlineData(1, "rest_to_free_cell")]
    public void Return_PartialOriginMerge_ThenThePanelLeg_ThenTheRemainderStaysOnTheCursor(int slot, string row)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        EntryId aId = rig.Entries[a].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        int originCount = row == "whole_at_origin" ? 5 : 18;
        ushort origin = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(100, originCount));
        ushort stack = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(200, 15));
        if (row != "whole_at_origin")
        {
            FillPanelWithFiber(rig, slot, keepFree: row == "rest_to_free_cell" ? 1 : 0);
        }

        uint[] versions = MoveSplitMergeTests.Versions(rig);
        int worldItems = rig.Inventory.WorldItems.LiveCount;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[chest]++;
        Assert.Equal((versions[cursor] + 1, versions[chest] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        if (row == "whole_at_origin")
        {
            Assert.Equal(InvAutoPlaceRig.Ids(1, 10).Concat(InvAutoPlaceRig.Ids(100, 5)).ToArray(), rig.IdsOf(origin));
            Assert.False(rig.Entries.TryGetSlot(aId, out _));
            Assert.Equal(InvAutoPlaceRig.Ids(200, 15), rig.IdsOf(stack));
            Assert.Equal(((ushort)10, (ushort)10), (extra.RequestedUnits, extra.GotUnits));
            Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
            return;
        }

        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal(versions[hotbar], rig.Header(hotbar).Version);
        Assert.Equal(new uint[] { 1, 2 }.Concat(InvAutoPlaceRig.Ids(100, 18)).ToArray(), rig.IdsOf(origin));
        Assert.Equal(InvAutoPlaceRig.Ids(3, 5).Concat(InvAutoPlaceRig.Ids(200, 15)).ToArray(), rig.IdsOf(stack));
        Assert.Equal(new uint[] { 8, 9, 10 }, rig.IdsOf(a));
        Assert.Equal(aId, rig.Entries[a].Id);
        Assert.Equal(worldItems, rig.Inventory.WorldItems.LiveCount);
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
        if (row == "rest_to_free_cell")
        {
            Assert.Equal(((ushort)10, (ushort)10), (extra.RequestedUnits, extra.GotUnits));
            Assert.Equal((inventory, 0), (rig.Entries[a].ContainerSlot, (int)rig.Header(cursor).EntryCount));
            Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
            return;
        }

        Assert.Equal(((ushort)10, (ushort)7), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal(cursor, rig.Entries[a].ContainerSlot);
        AssertOrigin(rig, ps, aId, rig.IdOf(chest), 2, 1, false);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act: again, with the origin full and no room in the panel
        EventEntry[] again = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        AssertRejected(rig, again, InvReason.NoRoom, before, slot, (rig.Header(cursor).Version, rig.Header(inventory).Version));
    }

    /// <summary>
    /// A partial origin merge with no room in the panel is still <c>ok</c> (Mail Lead planning default pending Evan (TD-153), item 3: ok
    /// if any unit moved): A = 10 letters [1..10] picked from chest (2,1), which then holds a same-key stack of 18; every panel cell holds
    /// fiber. [1, 2] merge at the origin, A keeps [3..10], its id and its origin on the cursor; requested 10 / got 2; chest +1 and cursor
    /// +1 (A = cursor, B = chest), the panel unchanged.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_PartialOriginMerge_NoRoomInThePanel_OkWithTheOriginUnitsOnly(int slot)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        EntryId aId = rig.Entries[a].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        ushort origin = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(100, 18));
        FillPanelWithFiber(rig, slot);
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[cursor], versions[chest]);

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp())).ExtraAs<InvReplyExtra>();

        // Assert
        Assert.Equal(new uint[] { 1, 2 }.Concat(InvAutoPlaceRig.Ids(100, 18)).ToArray(), rig.IdsOf(origin));
        Assert.Equal((aId, cursor), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot));
        Assert.Equal(InvAutoPlaceRig.Ids(3, 8), rig.IdsOf(a));
        AssertOrigin(rig, ps, aId, rig.IdOf(chest), 2, 1, false);
        Assert.Equal(((ushort)10, (ushort)2), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal((versions[cursor] + 1, versions[chest] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[chest]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
    }

    /// <summary>
    /// The origin merge's 255 cap (content with <c>letter.maxStack</c> 255; room <c>min(effMax, 255) − count</c>, as Move): A = n letters
    /// [1..n] picked from chest (2,1), which then holds a same-key stack of 250 [1001..1250] (or 255). n = 4 → 254 and n = 5 → 255, A
    /// retired, the inventory unchanged; n = 6 → [1..5] merge (255) and A keeps [6] and its id, moved whole to inventory (0,0); a target
    /// already at 255 takes nothing and A (4) goes whole to inventory (0,0), the chest unchanged.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0, 4, 250)]
    [InlineData(1, 4, 250)]
    [InlineData(0, 5, 250)]
    [InlineData(1, 5, 250)]
    [InlineData(0, 6, 250)]
    [InlineData(1, 6, 250)]
    [InlineData(0, 4, 255)]
    [InlineData(1, 4, 255)]
    public void Return_OriginMergeAtThe255Cap_TakesUpToTheCap_TheRestGoesToThePanel(int slot, int n, int originCount)
    {
        // Arrange
        var rig = NewRig(MoveSplitMergeTests.MailStack255Content());
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, n));
        EntryId aId = rig.Entries[a].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        ushort origin = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1001, originCount));
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp())).ExtraAs<InvReplyExtra>();

        // Assert
        int merged = Math.Min(n, 255 - originCount);
        Assert.Equal(((ushort)n, (ushort)n), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal(InvAutoPlaceRig.Ids(1, merged).Concat(InvAutoPlaceRig.Ids(1001, originCount)).ToArray(), rig.IdsOf(origin));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[chest] += merged > 0 ? 1u : 0u;
        if (merged == n)
        {
            Assert.False(rig.Entries.TryGetSlot(aId, out _));
        }
        else
        {
            Assert.Equal((aId, inventory, (byte)0, (byte)0), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y));
            Assert.Equal(InvAutoPlaceRig.Ids((uint)(merged + 1), n - merged), rig.IdsOf(a));
            expected[inventory]++;
        }

        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
    }

    /// <summary>
    /// The origin room is capped at the u8 count, not the item's <c>maxStack</c> (as Move): a <c>bigstack</c> item (<c>maxStack</c> 300,
    /// <see cref="MoveSplitMergeTests.MergeContent"/>) of 10 picked from chest (2,1), which then holds a bigstack of 250: the origin takes
    /// 5 (255) and the other 5 go, with the entry's id, to inventory (0,0).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_ItemOriginMergeWithMaxStackAbove255_TheRoomIsCappedAt255(int slot)
    {
        // Arrange
        var rig = NewRig(MoveSplitMergeTests.MergeContent());
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort a = rig.PlaceItem(chest, "bigstack", 2, 1, 10);
        EntryId aId = rig.Entries[a].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        ushort origin = rig.PlaceItem(chest, "bigstack", 2, 1, 250);
        Assert.True(rig.SubjectOf(origin).EffMax > byte.MaxValue);

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp())).ExtraAs<InvReplyExtra>();

        // Assert
        Assert.Equal((byte)255, rig.Entries[origin].Count);
        Assert.Equal((aId, inventory, (byte)0, (byte)0, (byte)5), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y, rig.Entries[a].Count));
        Assert.Equal(((ushort)10, (ushort)10), (extra.RequestedUnits, extra.GotUnits));
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
    }

    /// <summary>
    /// A partial merge at an origin inside the panel (the inventory, which the cursor row also targets) is not planned twice: A = 10
    /// letters [1..10] picked from inventory (0,0), which then holds a same-key stack of 18 [100..117] (room 2). [1, 2] merge at the
    /// origin (20); the panel leg skips that stack and moves A, with [3..10] and its id, to the first free cell, inventory (1,0).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_PartialMergeAtAnOriginInThePanel_TheOriginStackIsNotPlannedTwice(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 10));
        EntryId aId = rig.Entries[a].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, inventory, a)));
        ushort origin = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 18));
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp())).ExtraAs<InvReplyExtra>();

        // Assert
        Assert.Equal(new uint[] { 1, 2 }.Concat(InvAutoPlaceRig.Ids(100, 18)).ToArray(), rig.IdsOf(origin));
        Assert.Equal((aId, inventory, (byte)1, (byte)0), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y));
        Assert.Equal(InvAutoPlaceRig.Ids(3, 8), rig.IdsOf(a));
        Assert.Equal(((ushort)10, (ushort)10), (extra.RequestedUnits, extra.GotUnits));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
    }

    /// <summary>
    /// An origin recorded by a hotbar swap (story 008's swap commit): letters on the cursor are placed onto hotbar slot 2's Axe, which
    /// comes onto the cursor with origin hotbar slot 2. <c>slot_free</c> (the letters moved on to the inventory) → the Axe goes back to
    /// slot 2; <c>slot_taken</c> → the panel leg puts it in the first free usable hotbar slot, 1 (a tool: hotbar first, R9). Hotbar +1 and
    /// cursor +1.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "slot_free")]
    [InlineData(1, "slot_free")]
    [InlineData(0, "slot_taken")]
    [InlineData(1, "slot_taken")]
    public void Return_OriginFromAHotbarSwap_BackToTheSlotOrThePanel(int slot, string row)
    {
        // Arrange
        var rig = NewRig();
        ushort hotbar = rig.HotbarOf(slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort axe = rig.PlaceItem(hotbar, "axe", 2, 0, 1);
        ushort letters = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, hotbar, 2, 0)));
        Assert.Equal(cursor, rig.Entries[axe].ContainerSlot);
        if (row == "slot_free")
        {
            MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, hotbar, letters, inventory, 0, 0)));
        }

        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp())).ExtraAs<InvReplyExtra>();

        // Assert
        byte x = row == "slot_free" ? (byte)2 : (byte)1;
        Assert.Equal((hotbar, x, (byte)0), (rig.Entries[axe].ContainerSlot, rig.Entries[axe].X, rig.Entries[axe].Y));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[hotbar]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[cursor] + 1, versions[hotbar] + 1), (extra.ResultVersionA, extra.ResultVersionB));
    }

    // ------------------------------------------------------------------ Return: capacity_exhausted, cursor_empty, precedence

    /// <summary>
    /// <c>capacity_exhausted</c> (code 85, ADR-0009 section 2), checked last, leaves the entry on the cursor with its origin (Mail Lead
    /// planning default pending Evan (TD-153); it follows the pattern of Evan's 2026-10-22 5:11 AM PT ruling, which covers only a leashed
    /// letter staying on the cursor when the mail-id arena is full, even with a free cell). Rows, each with free inventory cells under an
    /// exhausted minimum arena: <c>origin_merge</c> (letters picked from chest (2,1), which then holds a same-key stack, so the origin
    /// merge needs a union block); <c>panel_merge</c> (letters placed straight on the cursor, no origin, with a same-key stack in the
    /// inventory).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "origin_merge")]
    [InlineData(1, "origin_merge")]
    [InlineData(0, "panel_merge")]
    [InlineData(1, "panel_merge")]
    public void Return_ArenaRefusal_CapacityExhausted_TheEntryStaysOnTheCursor(int slot, string row)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a;
        if (row == "origin_merge")
        {
            a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 3));
            MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
            _ = rig.PlaceMail(chest, "letter", 2, 1, Address3, new uint[] { 10, 11 });
        }
        else
        {
            a = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
            _ = rig.PlaceMail(inventory, "letter", 0, 0, Address3, new uint[] { 10, 11 });
        }

        MoveSplitMergeTests.ExhaustArenaThroughIntakeSpawns(rig);
        Assert.True(rig.Header(inventory).FreeCells > 0);
        EntryId id = rig.Entries[a].Id;
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        AssertRejected(rig, log, InvReason.CapacityExhausted, before, slot, (rig.Header(cursor).Version, rig.Header(inventory).Version));
        Assert.Equal((id, cursor, (byte)3), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
        Assert.Equal(row == "origin_merge", rig.Inventory.TryGetCursorOrigin(ps, out _));
    }

    /// <summary>
    /// The InvMalformedOpTests stub rig's valid row, moved here now that the real handler is bound: the rig's cursors are empty, so a
    /// well-formed <c>return</c> is <c>cursor_empty</c>, from either slot (the stub answered ok).
    /// </summary>
    [Theory]
    [Trait("Id", "H-NET-5-sim")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_OnTheStubRigsEmptyCursor_IsCursorEmpty(int slot)
    {
        // Arrange
        var rig = new InvOpRig();
        Assert.True(rig.Inventory.TryGetHeader(rig.Inventory.CursorOf(PlayerSlot.FromValue((byte)slot)), out ContainerHeader cursor));
        Assert.Equal(0, (int)cursor.EntryCount);

        // Act + Assert
        rig.AssertSingleReply(ReturnOp(), slot == 0 ? rig.PlayerA : rig.PlayerB, (ushort)InvReason.CursorEmpty);
    }

    /// <summary>
    /// <c>cursor_empty</c> for an actor with no panel (so no cursor): a 6-container table holds the Intake and player A's panel, so player
    /// B's join finds no room for one. Slot 1 only: the rig always joins A (slot 0) first. Nothing changes; versions (0, 0).
    /// </summary>
    [Fact]
    [Trait("Id", "H-R7-9")]
    public void Return_AnActorWithNoPanel_CursorEmpty_NothingChanges()
    {
        // Arrange
        var rig = new InvAutoPlaceRig(tune: s => s with { ContainerCapacity = 6 }, joinPlayerB: true);
        Assert.True(rig.Inventory.CursorOf(PlayerSlot.FromValue(1)).IsNone);
        _ = rig.PlaceItem(rig.InventoryOf(0), "plank", 0, 0, 5);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(1), ReturnOp());

        // Assert
        AssertRejected(rig, log, InvReason.CursorEmpty, before, 1, (0u, 0u));
    }

    /// <summary>
    /// The Return chain, <c>bad_request → cursor_empty → [origin leg] → [panel leg] → no_room → capacity_exhausted</c> (codes from
    /// ADR-0009 section 2; the head from GDD l.103 and ADR-0009 l.113, the legs and <c>no_room</c> from GDD l.95, the rest Mail Lead
    /// defaults, TD-153). Each row violates its step and every later one that can co-occur, under an exhausted minimum arena, and the
    /// earliest code must win: <c>wrong_length</c> (a 4-byte payload on an empty cursor: Sim Core's descriptor-length check answers
    /// <c>unknown_kind</c>, 3, before any handler runs, so the handler's <c>bad_request</c> head — <see cref="InvRequestCheck.CheckRequest"/>,
    /// pinned by <c>InvMalformedOpTests.CheckRequest_ReturnWithANonZeroPayloadLength_IsBadRequest</c> — is never reached through the
    /// drain); <c>cursor_empty</c> (an empty cursor);
    /// <c>no_room</c> (letters whose origin holds fiber, every panel cell fiber); <c>capacity_exhausted</c> (letters whose origin holds a
    /// same-key stack). <c>no_room</c> and <c>capacity_exhausted</c> cannot co-occur (nothing moved means no block is planned); the origin
    /// leg's own verdicts never reject. Rejects before the cursor's entry is resolved carry (0, 0); later ones the cursor's and the cursor
    /// row's first target's versions.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData("wrong_length", 0)]
    [InlineData("wrong_length", 1)]
    [InlineData("cursor_empty", 0)]
    [InlineData("cursor_empty", 1)]
    [InlineData("no_room", 0)]
    [InlineData("no_room", 1)]
    [InlineData("capacity_exhausted", 0)]
    [InlineData("capacity_exhausted", 1)]
    public void Return_Precedence_RequestViolatingThisStepAndEveryLaterOne_ReturnsThisStep(string row, int slot)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        Op op = ReturnOp();
        InvReason reason;
        (uint, uint) versions = (0u, 0u);
        switch (row)
        {
            case "wrong_length":
                op.SetPayload(new DropPayload { EntryId = 1 });
                Assert.Equal(4, op.PayloadLength);
                reason = (InvReason)CoreReason.UnknownKind;
                break;
            case "cursor_empty":
                reason = InvReason.CursorEmpty;
                break;
            default:
            {
                ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 3));
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
                if (row == "no_room")
                {
                    _ = rig.PlaceItem(chest, "fiber", 2, 1, 3);
                    FillPanelWithFiber(rig, slot);
                    reason = InvReason.NoRoom;
                }
                else
                {
                    _ = rig.PlaceMail(chest, "letter", 2, 1, Address3, new uint[] { 10, 11 });
                    reason = InvReason.CapacityExhausted;
                }

                break;
            }
        }

        MoveSplitMergeTests.ExhaustArenaThroughIntakeSpawns(rig);
        if (row is "no_room" or "capacity_exhausted")
        {
            versions = (rig.Header(cursor).Version, rig.Header(inventory).Version);
        }

        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert
        AssertRejected(rig, log, reason, before, slot, versions);
    }

    // ------------------------------------------------------------------ Return: weight, indexes

    /// <summary>
    /// Weight and indexes after a <c>return</c>: planks 10 or letters [1..10] picked from chest (2,1); to the origin, the owner's P is
    /// back to its value before the pick; to the panel (the chest closed), P is that value plus the entry's classPts; the other player's
    /// P never changes; <c>CountItem</c> and the StackKey index equal a recount.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R16-1")]
    [InlineData(0, "plank", "origin")]
    [InlineData(1, "plank", "origin")]
    [InlineData(0, "plank", "panel")]
    [InlineData(1, "plank", "panel")]
    [InlineData(0, "letter", "origin")]
    [InlineData(1, "letter", "origin")]
    [InlineData(0, "letter", "panel")]
    [InlineData(1, "letter", "panel")]
    public void Return_Weight_POfTheOriginOrThePanel_CountItemAndIndexMatchARecount(int slot, string kind, string leg)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        PlayerSlot other = PlayerSlot.FromValue((byte)(1 - slot));
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort a = kind == "plank" ? rig.PlaceItem(chest, "plank", 2, 1, 10) : rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        short beforePick = rig.Inventory.WeightPoints(ps);
        short theirs = rig.Inventory.WeightPoints(other);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        if (leg == "panel")
        {
            MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), CloseOp(rig, chest)));
        }

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp()));

        // Assert
        Assert.Equal(leg == "origin" ? chest : rig.InventoryOf(slot), rig.Entries[a].ContainerSlot);
        Assert.Equal(beforePick + (leg == "origin" ? 0 : MoveSplitMergeTests.ClassPts(rig, a)), rig.Inventory.WeightPoints(ps));
        Assert.Equal(theirs, rig.Inventory.WeightPoints(other));
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
    }
}
