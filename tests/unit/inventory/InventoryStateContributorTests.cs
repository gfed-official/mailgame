using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/017 AC "Dump" (ADR-0006 section 4; Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT, TD-160 item 10; advisory fix, Mail Review PR #36 A11): the block
/// <see cref="InventoryStateContributor"/> appends to section 4 after story 021's backlog ring, in order: the ledger, the entry-id
/// allocator, the live entries ascending by entryId, the per-slot cursor origin and the per-slot open external. Two identically built
/// and driven worlds give byte-identical sections, and for each appended field class a world that differs only in it gives a section
/// that differs only inside that class's bytes. <see cref="TailBytes"/> is the block's length, which the earlier blocks' tail-relative
/// layout tests subtract.
/// </summary>
public sealed class InventoryStateContributorTests
{
    private const uint Address4 = 4u;

    /// <summary>One ledger row: six live states, three consume reasons, destroyed, spawned, granted (i64 each).</summary>
    private const int LedgerRowBytes = 12 * 8;

    // A dumped ledger row's field indices after its live states (each field is one i64).
    private const int LedgerDelivered = UnitLedger.LiveStateCount;
    private const int LedgerMisdelivered = LedgerDelivered + 1;
    private const int LedgerReturnedToSender = LedgerDelivered + 2;
    private const int LedgerDestroyed = LedgerReturnedToSender + 1;
    private const int LedgerSpawned = LedgerDestroyed + 1;
    private const int LedgerGranted = LedgerSpawned + 1;

    /// <summary>One live entry before its mailIds: id, container, x, y, rotated, count, isMail, kind, addressId, flags, W, H, arrivalSeq.</summary>
    private const int EntryFixedBytes = 4 + 4 + 1 + 1 + 1 + 1 + 1 + 2 + 4 + 1 + 1 + 1 + 4;

    /// <summary>One cursor origin: entry id, container id, x, y, rotated.</summary>
    private const int OriginBytes = 4 + 4 + 1 + 1 + 1;

    /// <summary>The length of story 017's block for <paramref name="inventory"/>'s state (the section ends with it).</summary>
    internal static int TailBytes(InventorySystem inventory)
    {
        int entries = 0;
        EntryPool pool = inventory.Entries;
        for (int s = 0; s < pool.Capacity; s++)
        {
            ref readonly EntryRecord e = ref pool[(ushort)s];
            if (e.IsLive)
            {
                entries += EntryFixedBytes + (e.IsMail ? 4 * e.Count : 0);
            }
        }

        return 4 + (inventory.Ledger.Rows * LedgerRowBytes) + 4 + 4 + entries + (PlayerSlot.Count * OriginBytes) + (PlayerSlot.Count * 4);
    }

    [Fact]
    public void Section_TwoIdenticallyDrivenWorlds_AreByteIdentical()
    {
        // Arrange + Act
        byte[] a = SectionOf(Drive());
        byte[] b = SectionOf(Drive());

        // Assert
        Assert.Equal(a, b);
    }

    [Fact]
    public void Section_TheAppendedBlock_HasTheDocumentedLayout_EntriesAscendingByEntryIdNotBySlot()
    {
        // Arrange: Drive() frees an entry early and reuses its pool slot for a later (higher) id
        InvAutoPlaceRig rig = Drive();
        InventorySystem inv = rig.Inventory;
        UnitLedger ledger = inv.Ledger;
        Assert.Equal(2, ledger.Consumed(UnitLedger.MailRow, ConsumeReason.Delivered) + ledger.Consumed(UnitLedger.MailRow, ConsumeReason.Misdelivered));

        // Then every ledger counter of every row is set to its own distinct non-zero value, so a dump that swapped any two fields fails
        SetEveryLedgerCounterDistinct(rig);

        // Act
        byte[] s = SectionOf(rig);

        // Assert: the ledger, each field its exact value at its offset
        int at = s.Length - TailBytes(inv);
        Assert.Equal((uint)ledger.Rows, U32(s, ref at));
        for (int row = 0; row < ledger.Rows; row++)
        {
            for (int st = 0; st < UnitLedger.LiveStateCount; st++)
            {
                Assert.Equal(LedgerTarget(row, st), I64(s, ref at));
            }

            Assert.Equal(LedgerTarget(row, LedgerDelivered), I64(s, ref at));
            Assert.Equal(LedgerTarget(row, LedgerMisdelivered), I64(s, ref at));
            Assert.Equal(LedgerTarget(row, LedgerReturnedToSender), I64(s, ref at));
            Assert.Equal(LedgerTarget(row, LedgerDestroyed), I64(s, ref at));
            Assert.Equal(LedgerTarget(row, LedgerSpawned), I64(s, ref at));
            Assert.Equal(LedgerTarget(row, LedgerGranted), I64(s, ref at));
        }

        // The entry-id allocator, then the live entries ascending by entryId (a pool slot was reused, so slot order differs)
        Assert.Equal(inv.Entries.LastId, U32(s, ref at));
        uint live = U32(s, ref at);
        Assert.Equal((uint)inv.Entries.LiveCount, live);
        uint previous = 0;
        bool slotOrderDiffers = false;
        ushort previousSlot = 0;
        for (int i = 0; i < live; i++)
        {
            uint id = U32(s, ref at);
            Assert.True(id > previous, $"entry ids not ascending at {i}: {id} after {previous}");
            previous = id;
            Assert.True(inv.Entries.TryGetSlot(EntryId.FromValue(id), out ushort slot));
            slotOrderDiffers |= i > 0 && slot < previousSlot;
            previousSlot = slot;
            ref readonly EntryRecord e = ref inv.Entries[slot];
            Assert.Equal(e.ContainerSlot == EntryRecord.NoContainer ? 0u : inv.Containers[e.ContainerSlot].Id.Value, U32(s, ref at));
            Assert.Equal(e.X, s[at++]);
            Assert.Equal(e.Y, s[at++]);
            Assert.Equal(e.Rotated ? 1 : 0, s[at++]);
            Assert.Equal(e.Count, s[at++]);
            Assert.Equal(e.IsMail ? 1 : 0, s[at++]);
            Assert.Equal(e.IsMail ? e.MailKind : e.ItemId, BitConverter.ToUInt16(s, at));
            at += 2;
            Assert.Equal(e.AddressId, U32(s, ref at));
            Assert.Equal(e.Flags, s[at++]);
            Assert.Equal(e.W, s[at++]);
            Assert.Equal(e.H, s[at++]);
            Assert.Equal(e.ArrivalSeq, U32(s, ref at));
            if (e.IsMail)
            {
                uint[] ids = rig.IdsOf(slot);
                Assert.Equal(e.Count, ids.Length);
                foreach (uint mail in ids)
                {
                    Assert.Equal(mail, U32(s, ref at));
                }
            }
        }

        Assert.True(slotOrderDiffers, "the fixture must reuse a pool slot so that entryId order is not slot order");

        // The cursor origins (slot 0 holds the picked planks, from A's inventory (3, 0)), then the open externals (slot 0: the chest)
        Assert.True(inv.TryGetCursorOrigin(PlayerSlot.FromValue(0), out InventorySystem.CursorOrigin origin));
        Assert.Equal(origin.Entry.Value, U32(s, ref at));
        Assert.Equal(rig.IdOf(rig.InventoryA).Value, U32(s, ref at));
        Assert.Equal(3, s[at++]);
        Assert.Equal(0, s[at++]);
        Assert.Equal(0, s[at++]);
        for (int slot = 1; slot < PlayerSlot.Count; slot++)
        {
            Assert.Equal(0u, U32(s, ref at));
            Assert.Equal(0u, U32(s, ref at));
            at += 3;
        }

        Assert.Equal(inv.OpenSet.ExternalOf(PlayerSlot.FromValue(0)).Value, U32(s, ref at));
        Assert.NotEqual(0u, inv.OpenSet.ExternalOf(PlayerSlot.FromValue(0)).Value);
        for (int slot = 1; slot < PlayerSlot.Count; slot++)
        {
            Assert.Equal(0u, U32(s, ref at));
        }

        Assert.Equal(s.Length, at);
    }

    [Fact]
    public void Section_AWorldDifferingOnlyInTheLedger_DiffersOnlyInTheLedgerBytes()
    {
        // Arrange
        InvAutoPlaceRig a = Drive();
        InvAutoPlaceRig b = Drive();
        b.Inventory.Ledger.CorruptSpawnedForTests(UnitLedger.MailRow, 1);

        // Act / Assert
        int start = SectionOf(a).Length - TailBytes(a.Inventory);
        AssertDiffersOnlyIn(SectionOf(a), SectionOf(b), start, start + 4 + (a.Inventory.Ledger.Rows * LedgerRowBytes));
    }

    [Fact]
    public void Section_AWorldDifferingOnlyInTheEntryIdAllocator_DiffersOnlyInLastId()
    {
        // Arrange: b burns one entry id after the same history (allocated and freed between ticks)
        InvAutoPlaceRig a = Drive();
        InvAutoPlaceRig b = Drive();
        Assert.True(b.Entries.TryAllocate(b.Host.World.CurrentTick, out ushort burnt, out _));
        b.Entries.Free(burnt);

        // Act / Assert
        int start = SectionOf(a).Length - TailBytes(a.Inventory) + 4 + (a.Inventory.Ledger.Rows * LedgerRowBytes);
        AssertDiffersOnlyIn(SectionOf(a), SectionOf(b), start, start + 4);
    }

    [Fact]
    public void Section_AWorldDifferingOnlyInOneEntryField_DiffersOnlyInTheEntriesBytes()
    {
        // Arrange: b's chest letters carry other flags (a dumped field nothing else reads)
        InvAutoPlaceRig a = Drive();
        InvAutoPlaceRig b = Drive();
        ushort letters = LettersInChest(b);
        b.Entries[letters].Flags = 0x5;

        // Act / Assert
        (int start, int end) = EntriesRange(a);
        AssertDiffersOnlyIn(SectionOf(a), SectionOf(b), start, end);
    }

    [Fact]
    public void Section_AWorldDifferingOnlyInACursorOrigin_DiffersOnlyInTheOriginBytes()
    {
        // Arrange: both move the same 4 planks from A's inventory onto A's cursor; a in one call (an origin is recorded), b in two
        // (the placement's call saw no departure, so no origin); the containers' versions and the ledger end up the same
        InvAutoPlaceRig a = Drive(pickInOneCall: true);
        InvAutoPlaceRig b = Drive(pickInOneCall: false);
        Assert.True(a.Inventory.TryGetCursorOrigin(PlayerSlot.FromValue(0), out _));
        Assert.False(b.Inventory.TryGetCursorOrigin(PlayerSlot.FromValue(0), out _));

        // Act / Assert
        (_, int entriesEnd) = EntriesRange(a);
        AssertDiffersOnlyIn(SectionOf(a), SectionOf(b), entriesEnd, entriesEnd + (PlayerSlot.Count * OriginBytes));
    }

    [Fact]
    public void Section_AWorldDifferingOnlyInAnOpenExternal_DiffersOnlyInTheExternalBytes()
    {
        // Arrange
        InvAutoPlaceRig a = Drive();
        InvAutoPlaceRig b = Drive(openChest: false);

        // Act
        byte[] sa = SectionOf(a);

        // Assert
        AssertDiffersOnlyIn(sa, SectionOf(b), sa.Length - (PlayerSlot.Count * 4), sa.Length);
    }

    [Fact]
    public void Section_TheEarlierBlocks_AreUnmovedByTheAppendedBlock()
    {
        // Arrange: the container records keep their fixed leading offsets (IntakeSpawnOutfeedTests reads the Intake's at 8 + 23)
        InvAutoPlaceRig rig = Drive();

        // Act
        byte[] s = SectionOf(rig);

        // Assert
        Assert.Equal(rig.Inventory.Containers.NextId, BitConverter.ToUInt32(s, 0));
        Assert.Equal((uint)rig.Inventory.Containers.LiveCount, BitConverter.ToUInt32(s, 4));
        Assert.Equal(rig.Inventory.IntakeContainer.Value, BitConverter.ToUInt32(s, 8));
    }

    /// <summary>
    /// The fixture history: 3 letters spawned into the Intake, a 3-letter stack (in the reused slot) and 6 planks in A's inventory, 4 planks picked from
    /// A's inventory (3, 0) onto A's cursor, 2 letters in a chest A has open, a 3-plank detached entry freed after the spawn so its lower pool
    /// slot is reused by a later, higher id, and 2 letters consumed (one Delivered, one Misdelivered).
    /// </summary>
    private static InvAutoPlaceRig Drive(bool pickInOneCall = true, bool openChest = true)
    {
        var rig = new InvAutoPlaceRig();
        ushort early = rig.DetachedItem("plank", 3);
        _ = rig.InPhaseSpawn(rig.KindIndex("letter"), 1, 2, 3);
        rig.Entries.Free(early);
        rig.Recount();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(20, 3));
        _ = rig.PlaceItem(rig.InventoryA, "plank", 1, 0, 6);
        ushort picked = rig.PlaceItem(rig.InventoryA, "plank", 3, 0, 4);
        ushort chest = rig.CreateGrid(4, 4);
        _ = rig.PlaceMail(chest, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(30, 2));
        if (pickInOneCall)
        {
            Assert.True(rig.Step(
                (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 || p.Touch(rig.CursorA) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) =>
                {
                    rig.Inventory.Mutator.Remove(ref p, picked);
                    rig.Inventory.Mutator.Place(ref p, picked, rig.CursorA, 0, 0, false);
                }).IsOk);
        }
        else
        {
            Assert.True(rig.Step(
                (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => rig.Inventory.Mutator.Remove(ref p, picked)).IsOk);
            rig.PlaceDetached(picked, rig.CursorA, 0, 0);
        }

        if (openChest)
        {
            Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue(0), rig.IdOf(chest)));
        }

        ContainerId inventoryA = rig.IdOf(rig.InventoryA);
        EntryId stackId = rig.Entries[stack].Id;
        Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ConsumeUnit(new EntryRef(inventoryA, stackId), ConsumeReason.Delivered, rig.PlayerA)));
        Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ConsumeUnit(new EntryRef(inventoryA, stackId), ConsumeReason.Misdelivered, rig.PlayerA)));
        return rig;
    }

    /// <summary>
    /// The value <see cref="SetEveryLedgerCounterDistinct"/> gives <paramref name="row"/>'s field <paramref name="field"/> (its index in
    /// the dumped row: the live states, then the three consume reasons, destroyed, spawned, granted): distinct and non-zero across
    /// every field of every row.
    /// </summary>
    private static long LedgerTarget(int row, int field) => (1000L * (row + 1)) + field + 1;

    /// <summary>
    /// Sets every counter of every ledger row to its <see cref="LedgerTarget"/>. The crossings write all but <c>granted</c>, which only
    /// the Recount seam sets (to Σ live − (spawned − consumed − destroyed), live recounted from storage), so spawned is first set to
    /// give the target granted on a recount and then moved to its own target, after which the live states are moved to theirs.
    /// </summary>
    private static void SetEveryLedgerCounterDistinct(InvAutoPlaceRig rig)
    {
        UnitLedger ledger = rig.Inventory.Ledger;
        rig.Recount();
        for (int row = 0; row < ledger.Rows; row++)
        {
            ledger.AddConsumed(row, ConsumeReason.Delivered, (int)(LedgerTarget(row, LedgerDelivered) - ledger.Consumed(row, ConsumeReason.Delivered)));
            ledger.AddConsumed(row, ConsumeReason.Misdelivered, (int)(LedgerTarget(row, LedgerMisdelivered) - ledger.Consumed(row, ConsumeReason.Misdelivered)));
            ledger.AddConsumed(row, ConsumeReason.ReturnedToSender, (int)(LedgerTarget(row, LedgerReturnedToSender) - ledger.Consumed(row, ConsumeReason.ReturnedToSender)));
            ledger.AddDestroyed(row, (int)(LedgerTarget(row, LedgerDestroyed) - ledger.Destroyed(row)));
            long spawnedForGranted = ledger.LiveSum(row) + ledger.Consumed(row) + ledger.Destroyed(row) - LedgerTarget(row, LedgerGranted);
            ledger.AddSpawned(row, (int)(spawnedForGranted - ledger.Spawned(row)));
        }

        rig.Recount();
        for (int row = 0; row < ledger.Rows; row++)
        {
            ledger.AddSpawned(row, (int)(LedgerTarget(row, LedgerSpawned) - ledger.Spawned(row)));
            for (int st = 0; st < UnitLedger.LiveStateCount; st++)
            {
                ledger.AddLive(row, (LedgerState)st, (int)(LedgerTarget(row, st) - ledger.Live(row, (LedgerState)st)));
            }

            for (int field = 0; field < LedgerRowBytes / 8; field++)
            {
                Assert.Equal(LedgerTarget(row, field), LedgerField(ledger, row, field));
            }
        }
    }

    private static long LedgerField(UnitLedger ledger, int row, int field) => field switch
    {
        < UnitLedger.LiveStateCount => ledger.Live(row, (LedgerState)field),
        LedgerDelivered => ledger.Consumed(row, ConsumeReason.Delivered),
        LedgerMisdelivered => ledger.Consumed(row, ConsumeReason.Misdelivered),
        LedgerReturnedToSender => ledger.Consumed(row, ConsumeReason.ReturnedToSender),
        LedgerDestroyed => ledger.Destroyed(row),
        LedgerSpawned => ledger.Spawned(row),
        _ => ledger.Granted(row),
    };

    private static ushort LettersInChest(InvAutoPlaceRig rig)
    {
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            ref readonly EntryRecord e = ref rig.Entries[(ushort)s];
            if (e.IsLive && e.IsMail && e.ContainerSlot != EntryRecord.NoContainer && rig.Inventory.Containers[e.ContainerSlot].OwnerSlot == ContainerHeader.NoOwner
                && e.ContainerSlot != rig.Intake)
            {
                return (ushort)s;
            }
        }

        Assert.Fail("no letters in the chest");
        return 0;
    }

    /// <summary>The byte range of the live-entries block (its count included).</summary>
    private static (int Start, int End) EntriesRange(InvAutoPlaceRig rig)
    {
        int length = SectionOf(rig).Length;
        int start = length - TailBytes(rig.Inventory) + 4 + (rig.Inventory.Ledger.Rows * LedgerRowBytes) + 4;
        int end = length - (PlayerSlot.Count * OriginBytes) - (PlayerSlot.Count * 4);
        return (start, end);
    }

    private static void AssertDiffersOnlyIn(byte[] a, byte[] b, int start, int end)
    {
        Assert.Equal(a.Length, b.Length);
        int first = -1;
        int last = -1;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                first = first < 0 ? i : first;
                last = i;
            }
        }

        Assert.True(first >= 0, "the sections are identical");
        Assert.True(first >= start && last < end, $"differences at [{first}, {last}] outside [{start}, {end})");
    }

    private static byte[] SectionOf(InvAutoPlaceRig rig) => Section(DumpHash.Bytes(rig.Host.World), SectionIds.Inventory);

    private static uint U32(byte[] s, ref int at)
    {
        uint v = BitConverter.ToUInt32(s, at);
        at += 4;
        return v;
    }

    private static long I64(byte[] s, ref int at)
    {
        long v = BitConverter.ToInt64(s, at);
        at += 8;
        return v;
    }

    private static byte[] Section(byte[] dump, ushort id)
    {
        int pos = 6;
        while (pos < dump.Length)
        {
            ushort sectionId = BitConverter.ToUInt16(dump, pos);
            int length = (int)BitConverter.ToUInt32(dump, pos + 2);
            if (sectionId == id)
            {
                return dump.AsSpan(pos + 6, length).ToArray();
            }

            pos += 6 + length;
        }

        Assert.Fail($"Section {id} is not in the dump.");
        return Array.Empty<byte>();
    }
}
