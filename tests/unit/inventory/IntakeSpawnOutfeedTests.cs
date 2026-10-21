using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Intake;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/012 (GDD R14; ADR-0009 section 5 and the 2026-10-21 as-implemented note; ADR-0003 section 5; Planner
/// resolutions R1-R10): <see cref="IInventoryApi.Spawn"/> into the Intake (NoMerge, one entry per id, one seal per call),
/// <c>arrivalSeq</c> stamping by every mutator insertion path, the outfeed FIFO behind <see cref="IInventoryApi.PeekOutfeed"/> and
/// <see cref="IInventoryApi.TakeUnit"/>, <see cref="InventorySystem.ReturnToIntake"/>, <see cref="InventoryMutator.TakeOldest"/>, the
/// whitelist pin (R8), the tick guard (R9) and the dump/rebuild of the outfeed state. The mutating calls run inside a real tick through
/// the rig's <c>MailStep</c> hook (where Mail calls <c>Spawn</c>).
/// </summary>
public class IntakeSpawnOutfeedTests
{
    private const uint Address4 = 4u;
    private const uint Address9 = 9u;

    [Fact]
    [Trait("Id", "H-R14-1")]
    public void SpawnThreeLetters_ThenTakeOne_ThenReturnALowerId_PeekFollowsArrivalOrderNotMailId()
    {
        // Arrange: an empty Intake
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        ContainerId intake = rig.Inventory.IntakeContainer;

        // Act 1: 3 letters to #4, ids 100-102, NoMerge
        SpawnResult spawned = InPhase(rig, () => rig.Inventory.Spawn(intake, letter, Address4, 0, Mail(100, 101, 102), MergePolicy.NoMerge));
        EventEntry[] spawnLog = rig.Sink.Last;

        // Assert 1: three count-1 entries at (0,0), (1,0), (2,0) in id order, arrivalSeq 1..3, one seal by System
        Assert.Equal(new SpawnResult(3, 0), spawned);
        for (int i = 0; i < 3; i++)
        {
            ushort e = rig.EntryAt(rig.Intake, i, 0);
            Assert.NotEqual(Occupancy.Free, e);
            Assert.Equal(1, rig.Entries[e].Count);
            Assert.Equal(new[] { 100u + (uint)i }, rig.IdsOf(e));
            Assert.Equal((uint)(i + 1), rig.Entries[e].ArrivalSeq);
        }

        Assert.Equal(3, Heads(spawnLog, InvEventKinds.Inserted).Length);
        InvVersionPayload seal = Assert.Single(Heads(spawnLog, InvEventKinds.Version)).PayloadAs<InvVersionPayload>();
        Assert.Equal(ActorId.System.Value, seal.ActorId);
        Assert.Equal((byte)InvCause.System, seal.Cause);
        Assert.Equal(seal.VersionFrom + 1, seal.VersionTo);
        Assert.True(rig.Inventory.PeekOutfeed(intake, Selector.Fifo, out UnitRef head));
        Assert.Equal(new UnitRef(new MailId(100), rig.Entries[rig.EntryAt(rig.Intake, 0, 0)].Id, letter, Address4, 0), head);

        // Act 2: TakeUnit(FIFO)
        UnitRef taken = default;
        bool took = InPhase(rig, () => rig.Inventory.TakeUnit(intake, Selector.Fifo, rig.PlayerA, out taken));

        // Assert 2: 100 left, (0,0) is empty, PeekOutfeed is 101
        Assert.True(took);
        Assert.Equal(100u, taken.Mail.Value);
        Assert.Equal(Occupancy.Free, rig.EntryAt(rig.Intake, 0, 0));
        Assert.True(rig.Inventory.PeekOutfeed(intake, Selector.Fifo, out head));
        Assert.Equal(101u, head.Mail.Value);

        // Act 3: a returned letter id 50 is inserted
        ushort returned = rig.DetachedMail("letter", Address4, new[] { 50u });
        bool back = InPhase(rig, () => rig.Inventory.ReturnToIntake(returned, ActorId.System, InvCause.Expired));

        // Assert 3: a separate count-1 entry (first free cell (0,0), no merge into the #4 stacks), and the outfeed is still 101
        Assert.True(back);
        Assert.Equal(returned, rig.EntryAt(rig.Intake, 0, 0));
        Assert.Equal(1, rig.Entries[returned].Count);
        Assert.Equal(4u, rig.Entries[returned].ArrivalSeq);
        Assert.Equal(3, rig.Header(rig.Intake).EntryCount);
        Assert.True(rig.Inventory.PeekOutfeed(intake, Selector.Fifo, out head));
        Assert.Equal(101u, head.Mail.Value);
    }

    [Fact]
    [Trait("Id", "H-R14-4")]
    public void ReturnToIntake_ADetached20LetterEntry_EntersWholeAsOneNewEntry_KeepsItsId_ResidentUnchanged_VersionPlusOne()
    {
        // Arrange: the Intake holds a 5-letter entry to #9; a detached 20-letter entry to #9, ids 200-219
        var rig = new InvAutoPlaceRig();
        ushort resident = rig.PlaceMail(rig.Intake, "letter", 0, 0, Address9, InvAutoPlaceRig.Ids(1, 5));
        ushort detached = rig.DetachedMail("letter", Address9, InvAutoPlaceRig.Ids(200, 20));
        EntryId detachedId = rig.Entries[detached].Id;
        uint residentSeq = rig.Entries[resident].ArrivalSeq;
        uint version = rig.Header(rig.Intake).Version;
        int entries = rig.Header(rig.Intake).EntryCount;

        // Act
        bool ok = InPhase(rig, () => rig.Inventory.ReturnToIntake(detached, ActorId.System, InvCause.Expired));
        EventEntry[] log = rig.Sink.Last;

        // Assert: exactly one new entry of count 20, same entryId, one new arrivalSeq; the resident is untouched (NoMerge); version +1
        Assert.True(ok);
        Assert.Equal(entries + 1, rig.Header(rig.Intake).EntryCount);
        Assert.Equal(detachedId, rig.Entries[detached].Id);
        Assert.Equal(rig.Intake, rig.Entries[detached].ContainerSlot);
        Assert.Equal(20, rig.Entries[detached].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(200, 20), rig.IdsOf(detached));
        Assert.Equal(residentSeq + 1, rig.Entries[detached].ArrivalSeq);
        Assert.Equal(5, rig.Entries[resident].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(1, 5), rig.IdsOf(resident));
        Assert.Equal(residentSeq, rig.Entries[resident].ArrivalSeq);
        Assert.Equal(version + 1, rig.Header(rig.Intake).Version);
        InvInsertedPayload inserted = Assert.Single(Heads(log, InvEventKinds.Inserted)).PayloadAs<InvInsertedPayload>();
        Assert.Equal(detachedId.Value, inserted.EntryId);
        Assert.Equal(20, inserted.Count);
        Assert.Equal(residentSeq + 1, inserted.ArrivalSeq);
        Assert.Empty(Heads(log, InvEventKinds.Merged));
        InvVersionPayload seal = Assert.Single(Heads(log, InvEventKinds.Version)).PayloadAs<InvVersionPayload>();
        Assert.Equal((byte)InvCause.Expired, seal.Cause);
        Assert.Equal(ActorId.System.Value, seal.ActorId);
    }

    [Fact]
    [Trait("Id", "H-R14-4")]
    public void ReturnToIntake_NoRoom_ReturnsFalse_AndChangesNothing()
    {
        // Arrange: a full Intake (320 count-1 entries) and a detached letter
        var rig = new InvAutoPlaceRig();
        FillIntake(rig, 320, 1_000);
        ushort detached = rig.DetachedMail("letter", Address9, new[] { 7u });
        List<long> before = State(rig);

        // Act
        bool ok = InPhase(rig, () => rig.Inventory.ReturnToIntake(detached, ActorId.System, InvCause.Expired));

        // Assert: refused, nothing changed, the entry still detached, no inventory event
        Assert.False(ok);
        Assert.Equal(before, State(rig));
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[detached].ContainerSlot);
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void Spawn_IntoAFullIntake_PlacesNothing_AllocatesNoEntry_KeepsTheVersion_AndRecordsTheNoFit()
    {
        // Arrange: a full Intake
        var rig = new InvAutoPlaceRig();
        FillIntake(rig, 320, 1_000);
        DefIndex letter = rig.KindIndex("letter");
        List<long> before = State(rig);

        // Act: a first spawn scans and fails; a second one at the same version
        SpawnResult first = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(1, 2, 3), MergePolicy.NoMerge));
        EventEntry[] firstLog = rig.Sink.Last;
        long probes = rig.Counters[CounterId.InventoryPlacementProbes];
        SpawnResult second = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(1, 2, 3), MergePolicy.NoMerge));

        // Assert: Placed 0, nothing mutated, no event; the reject's no-fit fact was recorded (RecordNoFits), so the repeat probes nothing
        Assert.Equal(new SpawnResult(0, 3), first);
        Assert.Equal(new SpawnResult(0, 3), second);
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(firstLog));
        Assert.Equal(probes, rig.Counters[CounterId.InventoryPlacementProbes]);
    }

    [Fact]
    public void Spawn_IntoANearlyFullIntake_PlacesTheIdsThatFit_InSpanOrder_AndStopsAtTheFirstThatDoesNot()
    {
        // Arrange: 318 of 320 cells taken
        var rig = new InvAutoPlaceRig();
        FillIntake(rig, 318, 1_000);
        DefIndex letter = rig.KindIndex("letter");
        uint lastId = rig.Entries.LastId;
        uint version = rig.Header(rig.Intake).Version;

        // Act
        SpawnResult r = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(5, 6, 7, 8, 9), MergePolicy.NoMerge));

        // Assert: ids 5 and 6 placed (the remainder is ids[2..] = 7, 8, 9), two entries allocated, one seal
        Assert.Equal(new SpawnResult(2, 3), r);
        Assert.Equal(lastId + 2, rig.Entries.LastId);
        Assert.Equal(320, rig.Header(rig.Intake).EntryCount);
        Assert.Equal(version + 1, rig.Header(rig.Intake).Version);
        Assert.Equal(new[] { 5u }, rig.IdsOf(rig.EntryAt(rig.Intake, 18, 15)));
        Assert.Equal(new[] { 6u }, rig.IdsOf(rig.EntryAt(rig.Intake, 19, 15)));
        Assert.Single(Heads(rig.Sink.Last, InvEventKinds.Version));
    }

    [Fact]
    public void Spawn_AfterAPartialSpawn_ASecondSpawnOfTheSameKeyIntoTheUnchangedIntake_ProbesNothing_AndPlacesNothing()
    {
        // Arrange: 318 of 320 cells taken; a partial spawn places 2 of 5 and seals the Intake (version + 1)
        var rig = new InvAutoPlaceRig();
        FillIntake(rig, 318, 1_000);
        DefIndex letter = rig.KindIndex("letter");
        SpawnResult partial = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(5, 6, 7, 8, 9), MergePolicy.NoMerge));
        List<long> before = State(rig);
        long probes = rig.Counters[CounterId.InventoryPlacementProbes];

        // Act: the same StackKey again, into the Intake the partial spawn left
        SpawnResult second = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(7, 8, 9), MergePolicy.NoMerge));

        // Assert: the failing id's no-fit fact was recorded at the sealed version, so the repeat hits the cache (0 anchors probed)
        Assert.Equal(new SpawnResult(2, 3), partial);
        Assert.Equal(new SpawnResult(0, 3), second);
        Assert.Equal(probes, rig.Counters[CounterId.InventoryPlacementProbes]);
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void Spawn_WhenTheArenaRefusesTheBlock_StopsWithNothingMutatedForThatId()
    {
        // Arrange: a small arena drained to one free 1-unit block
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits);
        MailIdArena arena = rig.Inventory.MailIds;
        var held = new List<MailIdsHandle>();
        uint next = 10_000_000u;
        for (int c = 0; c < MailIdArena.ClassCount; c++)
        {
            int size = MailIdArena.ClassSize(c);
            while (arena.FreeBlocks(c) > 0)
            {
                Assert.True(arena.TryCreate(InvAutoPlaceRig.Ids(next, size), 0, out MailIdsHandle h));
                next += (uint)size;
                held.Add(h);
            }
        }

        arena.Free(held[0]); // one class-0 block
        DefIndex letter = rig.KindIndex("letter");
        uint lastId = rig.Entries.LastId;

        // Act
        SpawnResult r = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(1, 2, 3), MergePolicy.NoMerge));

        // Assert: the first id took the last block; the second was refused by the reservation with no entry allocated
        Assert.Equal(new SpawnResult(1, 2), r);
        Assert.Equal(lastId + 1, rig.Entries.LastId);
        Assert.Equal(1, rig.Header(rig.Intake).EntryCount);
    }

    public static TheoryData<string> MalformedSpawns() => new() { "empty", "256 ids", "descending", "duplicate", "id 0", "unknown kind", "none kind", "dead container" };

    [Theory]
    [MemberData(nameof(MalformedSpawns))]
    public void Spawn_Malformed_IsRefusedWithNothingMutated_AndNoEvent(string shape)
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        DefIndex kind = rig.KindIndex("letter");
        ContainerId target = rig.Inventory.IntakeContainer;
        MailId[] ids = Mail(1, 2, 3);
        switch (shape)
        {
            case "empty": ids = Array.Empty<MailId>(); break;
            case "256 ids": ids = InvAutoPlaceRig.Ids(1, 256).Select(i => new MailId(i)).ToArray(); break;
            case "descending": ids = Mail(3, 2, 1); break;
            case "duplicate": ids = Mail(1, 1, 2); break;
            case "id 0": ids = Mail(0, 1, 2); break;
            case "unknown kind": kind = DefIndex.FromValue((ushort)rig.Content.MailKinds.Count); break;
            case "none kind": kind = default; break;
            case "dead container": target = new ContainerId(999); break;
        }

        List<long> before = State(rig);

        // Act
        SpawnResult r = InPhase(rig, () => rig.Inventory.Spawn(target, kind, Address4, 0, ids, MergePolicy.NoMerge));

        // Assert
        Assert.Equal(new SpawnResult(0, (ushort)ids.Length), r);
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void Spawn_MergePolicyOnTheIntake_IsNoMerge_ButIsHonouredOnAContainerWithoutAnOutfeed()
    {
        // Arrange: a non-full letter stack to #4 in the Intake and one in player A's inventory
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        ushort intakeStack = rig.PlaceMail(rig.Intake, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(1, 2));
        ushort invStack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(20, 2));
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));

        // Act
        SpawnResult intoIntake = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(10, 11), MergePolicy.Merge));
        SpawnResult intoInventory = InPhase(rig, () => rig.Inventory.Spawn(inventoryA, letter, Address4, 0, Mail(30, 31), MergePolicy.Merge));

        // Assert: the Intake got two new count-1 entries; the inventory stack took both units by merge
        Assert.Equal(new SpawnResult(2, 0), intoIntake);
        Assert.Equal(2, rig.Entries[intakeStack].Count);
        Assert.Equal(3, rig.Header(rig.Intake).EntryCount);
        Assert.Equal(new SpawnResult(2, 0), intoInventory);
        Assert.Equal(new uint[] { 20, 21, 30, 31 }, rig.IdsOf(invStack));
        Assert.Equal(1, rig.Header(rig.InventoryA).EntryCount);
        Assert.Equal(0u, rig.Entries[invStack].ArrivalSeq);
    }

    [Fact]
    [Trait("Id", "R8")]
    public void Whitelist_PoIntakeOfTheM1aContent_AcceptsMail_AndRefusesAPlankWithNothingMutated()
    {
        // Arrange: the M-1a built-in po_intake (AcceptsInsert defaults true until content-data 018 spells it out)
        var rig = new InvAutoPlaceRig();
        ContainerDef intakeDef = rig.Content.Containers[rig.Header(rig.Intake).DefIndex];
        ushort plank = rig.DetachedItem("plank", 3);
        List<long> before = State(rig);

        // Act
        InvReason mail = InsertionWhitelist.Check(intakeDef, rig.Mail("letter", Address4));
        InvReason item = InsertionWhitelist.Check(intakeDef, rig.Item("plank"));
        bool returned = InPhase(rig, () => rig.Inventory.ReturnToIntake(plank, ActorId.System, InvCause.Expired));

        // Assert
        Assert.Equal("po_intake", intakeDef.Id);
        Assert.True(intakeDef.AcceptsInsert);
        Assert.Equal(BeltAccess.OutfeedOnly, intakeDef.BeltAccess);
        Assert.Equal((InvReason)0, mail);
        Assert.Equal(InvReason.NotAllowed, item);
        Assert.False(returned);
        Assert.Equal(before, State(rig));
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[plank].ContainerSlot);
    }

    [Fact]
    public void EveryIntakeInsertion_Stamps_AndLeaving_ResetsTheStamp_AndLeavesASkippedTombstone()
    {
        // Arrange: a spawned entry (seq 1), a returned one (seq 2), and a letter in player A's inventory (not an outfeed: seq 0)
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        _ = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(1), MergePolicy.NoMerge));
        ushort spawned = rig.EntryAt(rig.Intake, 0, 0);
        ushort returned = rig.DetachedMail("letter", Address4, new[] { 2u });
        Assert.True(InPhase(rig, () => rig.Inventory.ReturnToIntake(returned, ActorId.System, InvCause.Expired)));
        ushort moved = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address9, new[] { 3u });
        OutfeedFifo fifo = rig.Inventory.Mutator.Outfeed;
        Assert.Equal(0u, rig.Entries[moved].ArrivalSeq);

        // Act 1: a player op path (the fixture op's commit through Mutator.Transfer) moves the letter into the Intake
        Reply into = rig.Step(
            (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 || p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.Transfer(ref p, moved, rig.Intake, 5, 5, false));
        InvInsertedPayload inserted = Assert.Single(Heads(rig.Sink.Last, InvEventKinds.Inserted)).PayloadAs<InvInsertedPayload>();

        // Assert 1: stamps 1, 2, 3 in insertion order; the Inserted record carries the stamp; one node each
        Assert.True(into.IsOk);
        Assert.Equal(1u, rig.Entries[spawned].ArrivalSeq);
        Assert.Equal(2u, rig.Entries[returned].ArrivalSeq);
        Assert.Equal(3u, rig.Entries[moved].ArrivalSeq);
        Assert.Equal(3u, inserted.ArrivalSeq);
        Assert.Equal(4u, rig.Header(rig.Intake).NextArrivalSeq);
        Assert.Equal(3, fifo.NodeCount(rig.Intake));

        // Act 2: the head entry (seq 1) and the middle one (seq 2) leave the Intake through the op path
        Reply out1 = rig.Step(
            (ref InvPlan p) => p.Touch(rig.Intake) < 0 || p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) =>
            {
                rig.Inventory.Mutator.Transfer(ref p, spawned, rig.InventoryA, 0, 0, false);
                rig.Inventory.Mutator.Transfer(ref p, returned, rig.InventoryA, 1, 0, false);
            });

        // Assert 2: stamps reset to 0, nodes left as tombstones (2 tombstones > 1 live, so the list was compacted to the live node)
        Assert.True(out1.IsOk);
        Assert.Equal(0u, rig.Entries[spawned].ArrivalSeq);
        Assert.Equal(0u, rig.Entries[returned].ArrivalSeq);
        Assert.Equal(1, fifo.NodeCount(rig.Intake));
        Assert.True(rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out UnitRef head));
        Assert.Equal(3u, head.Mail.Value);
        Assert.Equal(4u, rig.Header(rig.Intake).NextArrivalSeq);

        // Act 3: back into the Intake: a fresh stamp (not the old one)
        Reply back = rig.Step(
            (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 || p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.Transfer(ref p, spawned, rig.Intake, 0, 0, false));

        // Assert 3
        Assert.True(back.IsOk);
        Assert.Equal(4u, rig.Entries[spawned].ArrivalSeq);
        Assert.True(rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out head));
        Assert.Equal(3u, head.Mail.Value);
    }

    [Fact]
    public void AMoveInsideTheIntake_KeepsTheEntrysArrivalSeq_AndItsFifoPosition()
    {
        // Arrange: three spawned letters, ids 1-3 (seq 1-3) at (0,0), (1,0), (2,0)
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        _ = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(1, 2, 3), MergePolicy.NoMerge));
        ushort head = rig.EntryAt(rig.Intake, 0, 0);
        OutfeedFifo fifo = rig.Inventory.Mutator.Outfeed;

        // Act: the head entry moves to (5,5) of the Intake (a same-container Transfer)
        Reply moved = rig.Step(
            (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.Transfer(ref p, head, rig.Intake, 5, 5, false));
        EventEntry[] log = rig.Sink.Last;
        ushort at55 = rig.EntryAt(rig.Intake, 5, 5);
        uint seq = rig.Entries[head].ArrivalSeq;
        int nodes = fifo.NodeCount(rig.Intake);
        var order = new List<uint>();
        for (int i = 0; i < 3; i++)
        {
            UnitRef u = default;
            Assert.True(InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out u)));
            order.Add(u.Mail.Value);
        }

        // Assert: an InvMoved, no stamp, no node, no tombstone; the moved entry is still served first
        Assert.True(moved.IsOk);
        Assert.Single(Heads(log, InvEventKinds.Moved));
        Assert.Empty(Heads(log, InvEventKinds.Inserted));
        Assert.Equal(head, at55);
        Assert.Equal(1u, seq);
        Assert.Equal(3, nodes);
        Assert.Equal(4u, rig.Header(rig.Intake).NextArrivalSeq);
        Assert.Equal(new uint[] { 1, 2, 3 }, order);
    }

    [Fact]
    public void MergeIntoAnIntakeEntry_DoesNotRestampTheTarget_AndKeepsItsFifoPosition()
    {
        // Arrange: two spawned letters, ids 1 and 2 (seq 1 and 2); a detached letter of the same StackKey, id 50
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        _ = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(1, 2), MergePolicy.NoMerge));
        ushort second = rig.EntryAt(rig.Intake, 1, 0);
        ushort source = rig.DetachedMail("letter", Address4, new[] { 50u });
        OutfeedFifo fifo = rig.Inventory.Mutator.Outfeed;

        // Act: merge the detached letter into the second Intake entry
        Reply merged = rig.Step(
            (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.MergeInto(ref p, source, second, 1));
        EventEntry[] log = rig.Sink.Last;
        uint seq = rig.Entries[second].ArrivalSeq;
        int nodes = fifo.NodeCount(rig.Intake);
        var order = new List<uint>();
        for (int i = 0; i < 3; i++)
        {
            UnitRef u = default;
            Assert.True(InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out u)));
            order.Add(u.Mail.Value);
        }

        // Assert: the target kept seq 2 and its one node (NextArrivalSeq unchanged); its units leave after entry 1's, lowest id first
        Assert.True(merged.IsOk);
        Assert.Single(Heads(log, InvEventKinds.Merged));
        Assert.Empty(Heads(log, InvEventKinds.Inserted));
        Assert.Equal(2u, seq);
        Assert.Equal(2, nodes);
        Assert.Equal(3u, rig.Header(rig.Intake).NextArrivalSeq);
        Assert.Equal(new uint[] { 1, 2, 50 }, order);
    }

    [Fact]
    public void ANonMailOutfeedHead_AssertsInAssertBuilds_AndReadsAsEmptyInRelease_WithNothingPopped()
    {
        // Arrange: a plank placed into the empty Intake straight through the mutator (bypassing the whitelist, which makes this unreachable)
        var rig = new InvAutoPlaceRig();
        ushort plank = rig.PlaceItem(rig.Intake, "plank", 0, 0, 1);
        OutfeedFifo fifo = rig.Inventory.Mutator.Outfeed;
        Assert.Equal(1u, rig.Entries[plank].ArrivalSeq);
        List<long> before = State(rig);

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out _));
            Assert.Equal(SimAssertCode.InventoryOutfeedHeadNotMail, ex.Code);
        }
        else
        {
            Assert.False(rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out _));
            Assert.False(InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _)));
        }

        Assert.Equal(before, State(rig));
        Assert.Equal(1, fifo.NodeCount(rig.Intake));
        Assert.Equal(rig.Intake, rig.Entries[plank].ContainerSlot);
    }

    [Fact]
    public void PeekOutfeed_SkipsAMidQueueTombstone_AndALazyHeadTombstoneIsPoppedByThePeekOnly()
    {
        // Arrange: four spawned letters, ids 1-4 (seq 1-4); the entry with seq 2 leaves (a mid-queue tombstone, 1 <= 3 live)
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        _ = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(1, 2, 3, 4), MergePolicy.NoMerge));
        ushort second = rig.EntryAt(rig.Intake, 1, 0);
        Assert.True(rig.Step(
            (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.Remove(ref p, second)).IsOk);
        rig.FreeDetached(second);
        OutfeedFifo fifo = rig.Inventory.Mutator.Outfeed;
        Assert.Equal(4, fifo.NodeCount(rig.Intake));
        var order = new List<uint>();

        // Act: take everything
        for (int i = 0; i < 3; i++)
        {
            UnitRef u = default;
            Assert.True(InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out u)));
            order.Add(u.Mail.Value);
        }

        // Assert: arrival order with the tombstone skipped; empty afterwards (the last head tombstone is popped by the peek)
        Assert.Equal(new uint[] { 1, 3, 4 }, order);
        Assert.False(rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out _));
        Assert.Equal(0, fifo.NodeCount(rig.Intake));
        Assert.Equal(0u, rig.Header(rig.Intake).OutfeedHead);
        Assert.Equal(0u, rig.Header(rig.Intake).OutfeedTail);
    }

    [Fact]
    public void TakeUnit_OnAMultiUnitEntry_TakesItsLowestId_KeepsTheEntryAtTheHead_AndSealsWithTheCallersActor()
    {
        // Arrange: a returned 3-letter entry, ids 40-42
        var rig = new InvAutoPlaceRig();
        ushort entry = rig.DetachedMail("letter", Address9, InvAutoPlaceRig.Ids(40, 3));
        Assert.True(InPhase(rig, () => rig.Inventory.ReturnToIntake(entry, ActorId.System, InvCause.Expired)));
        uint version = rig.Header(rig.Intake).Version;

        // Act
        UnitRef u = default;
        bool took = InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out u));
        EventEntry[] log = rig.Sink.Last;

        // Assert: 40 taken, count 2, still the head; InvRemoved{count 1} then one seal (actor = caller, cause None); version +1
        Assert.True(took);
        Assert.Equal(new UnitRef(new MailId(40), rig.Entries[entry].Id, rig.KindIndex("letter"), Address9, 0), u);
        Assert.Equal(2, rig.Entries[entry].Count);
        Assert.Equal(new uint[] { 41, 42 }, rig.IdsOf(entry));
        Assert.Equal(version + 1, rig.Header(rig.Intake).Version);
        Assert.Equal(1, Assert.Single(Heads(log, InvEventKinds.Removed)).PayloadAs<InvRemovedPayload>().Count);
        InvVersionPayload seal = Assert.Single(Heads(log, InvEventKinds.Version)).PayloadAs<InvVersionPayload>();
        Assert.Equal(rig.PlayerA.Value, seal.ActorId);
        Assert.Equal((byte)InvCause.None, seal.Cause);
        Assert.True(rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out UnitRef next));
        Assert.Equal(41u, next.Mail.Value);
    }

    [Fact]
    public void TakeOldest_OnAPlacedStack_TakesTheKLowestIds_SetsTheCount_AndRaisesRemovedK()
    {
        // Arrange: a 5-letter stack in player A's inventory (no outfeed)
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(10, 5));
        uint[] taken = new uint[2];
        int got = 0;

        // Act
        Reply r = rig.Step(
            (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => got = rig.Inventory.Mutator.TakeOldest(ref p, stack, 2, taken));

        // Assert
        Assert.True(r.IsOk);
        Assert.Equal(2, got);
        Assert.Equal(new uint[] { 10, 11 }, taken);
        Assert.Equal(3, rig.Entries[stack].Count);
        Assert.Equal(new uint[] { 12, 13, 14 }, rig.IdsOf(stack));
        Assert.Equal(2, Assert.Single(Heads(rig.Sink.Last, InvEventKinds.Removed)).PayloadAs<InvRemovedPayload>().Count);
        Assert.Equal(stack, rig.Index.Head(rig.InventoryA, StackKey.Of(in rig.Entries[stack])));
    }

    [Fact]
    public void TakeOldest_Invalid_AssertsInAssertBuilds_AndChangesNothingInRelease()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(10, 2));
        var plan = new InvPlan();
        _ = plan.Touch(rig.InventoryA);
        uint[] taken = new uint[4];

        // Act + Assert: k above the count
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => rig.Inventory.Mutator.TakeOldest(ref plan, stack, 3, taken));
            Assert.Equal(SimAssertCode.InventoryTakeInvalid, ex.Code);
        }
        else
        {
            Assert.Equal(0, rig.Inventory.Mutator.TakeOldest(ref plan, stack, 3, taken));
            Assert.Equal(0, plan.MutatedCount);
        }

        Assert.Equal(2, rig.Entries[stack].Count);
    }

    [Fact]
    [Trait("Id", "R9")]
    public void MutatingCalls_OutsideATickPhase_AreRefused_WithNothingMutatedAndNoEvent()
    {
        // Arrange: between ticks, an Intake with one letter and a detached letter
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        ContainerId intake = rig.Inventory.IntakeContainer;
        _ = InPhase(rig, () => rig.Inventory.Spawn(intake, letter, Address4, 0, Mail(1), MergePolicy.NoMerge));
        ushort detached = rig.DetachedMail("letter", Address4, new[] { 9u });
        List<long> before = State(rig);
        long refusedBefore = rig.Counters[CounterId.MutatingApiOutOfPhase];
        long outsideBefore = rig.Counters[CounterId.EventsOutsideTick];
        var calls = new Func<object>[]
        {
            () => rig.Inventory.Spawn(intake, letter, Address4, 0, Mail(2), MergePolicy.NoMerge),
            () => rig.Inventory.TakeUnit(intake, Selector.Fifo, rig.PlayerA, out _),
            () => rig.Inventory.ReturnToIntake(detached, ActorId.System, InvCause.Expired),
        };

        // Act + Assert: SIM_ASSERT builds assert; Release refuses and counts
        foreach (Func<object> call in calls)
        {
            if (TestBuildExpectation.SimAssertsExpected)
            {
                SimAssertException ex = Assert.Throws<SimAssertException>(() => call());
                Assert.Equal(SimAssertCode.MutatingApiOutOfPhase, ex.Code);
            }
            else
            {
                object result = call();
                Assert.True(result is false || result.Equals(new SpawnResult(0, 1)));
            }
        }

        Assert.Equal(before, State(rig));
        Assert.Equal(refusedBefore + 3, rig.Counters[CounterId.MutatingApiOutOfPhase]);
        Assert.Equal(outsideBefore, rig.Counters[CounterId.EventsOutsideTick]);
        rig.Host.World.Tick();
        Assert.Empty(InventoryEvents(rig.Sink.Last));

        // PeekOutfeed is read-only and legal between ticks
        Assert.True(rig.Inventory.PeekOutfeed(intake, Selector.Fifo, out UnitRef u));
        Assert.Equal(1u, u.Mail.Value);
    }

    [Fact]
    public void PeekOutfeed_IsReadOnly_NoScopeNoSealNoVersion_AndRepeatable()
    {
        // Arrange: three letters, the head one moved away through the op path (a head tombstone)
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        _ = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(1, 2, 3), MergePolicy.NoMerge));
        ushort head = rig.EntryAt(rig.Intake, 0, 0);
        Assert.True(rig.Step(
            (ref InvPlan p) => p.Touch(rig.Intake) < 0 || p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.Transfer(ref p, head, rig.InventoryA, 0, 0, false)).IsOk);
        List<long> before = State(rig, includeMemo: false);
        OutfeedFifo fifo = rig.Inventory.Mutator.Outfeed;
        Assert.Equal(3, fifo.NodeCount(rig.Intake));
        var peeks = new UnitRef[2];

        // Act: two peeks inside one tick
        rig.Hook.Action = (in TickContext ctx) =>
        {
            _ = rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out peeks[0]);
            _ = rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out peeks[1]);
        };
        rig.Host.World.Tick();
        rig.Hook.Action = null;

        // Assert: both 2; no inventory event; versions and entries unchanged; only the memo popped the head tombstone
        Assert.Equal(2u, peeks[0].Mail.Value);
        Assert.Equal(peeks[0], peeks[1]);
        Assert.Empty(InventoryEvents(rig.Sink.Last));
        Assert.Equal(before, State(rig, includeMemo: false));
        Assert.Equal(2, fifo.NodeCount(rig.Intake));
    }

    [Fact]
    public void PeekAndTake_OnAContainerWithoutAnOutfeed_AnUnknownSelector_AnEmptyOrDeadContainer_ReturnFalse_AndMutateNothing()
    {
        // Arrange: mail in player A's inventory, an empty Intake
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(1, 3));
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        ContainerId intake = rig.Inventory.IntakeContainer;
        List<long> before = State(rig);
        var results = new List<bool>();

        // Act
        rig.Hook.Action = (in TickContext ctx) =>
        {
            results.Add(rig.Inventory.PeekOutfeed(inventoryA, Selector.Fifo, out _));
            results.Add(rig.Inventory.TakeUnit(inventoryA, Selector.Fifo, rig.PlayerA, out _));
            results.Add(rig.Inventory.PeekOutfeed(intake, (Selector)7, out _));
            results.Add(rig.Inventory.TakeUnit(intake, (Selector)7, rig.PlayerA, out _));
            results.Add(rig.Inventory.PeekOutfeed(intake, Selector.Fifo, out _));
            results.Add(rig.Inventory.TakeUnit(intake, Selector.Fifo, rig.PlayerA, out _));
            results.Add(rig.Inventory.PeekOutfeed(new ContainerId(999), Selector.Fifo, out _));
            results.Add(rig.Inventory.TakeUnit(new ContainerId(999), Selector.Fifo, rig.PlayerA, out _));
        };
        rig.Host.World.Tick();
        rig.Hook.Action = null;

        // Assert
        Assert.Equal(8, results.Count);
        Assert.All(results, Assert.False);
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void StateDump_CarriesNextArrivalSeq_AndARebuiltOutfeed_ServesTheSameArrivalOrder()
    {
        // Arrange: spawns, a return and a mid-queue departure, so live seqs are not contiguous and not in mailId order
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        _ = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(10, 11, 12, 13), MergePolicy.NoMerge));
        ushort returned = rig.DetachedMail("letter", Address9, new[] { 5u });
        Assert.True(InPhase(rig, () => rig.Inventory.ReturnToIntake(returned, ActorId.System, InvCause.Expired)));
        ushort third = rig.EntryAt(rig.Intake, 2, 0);
        Assert.True(rig.Step(
            (ref InvPlan p) => p.Touch(rig.Intake) < 0 || p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.Transfer(ref p, third, rig.InventoryA, 0, 0, false)).IsOk);
        byte[] dump = DumpHash.Bytes(rig.Host.World);
        OutfeedFifo fifo = rig.Inventory.Mutator.Outfeed;

        // Act: drop the memo (as a load starts without it), then rebuild it from the entries' arrivalSeq
        fifo.Clear(rig.Intake, ref rig.Header(rig.Intake));
        bool peekWithoutMemo = rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out _);
        rig.Inventory.Mutator.RebuildOutfeed(rig.Intake);
        byte[] dumpAfter = DumpHash.Bytes(rig.Host.World);
        var order = new List<uint>();
        for (int i = 0; i < 4; i++)
        {
            UnitRef u = default;
            Assert.True(InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out u)));
            order.Add(u.Mail.Value);
        }

        // Assert: the dump holds nextArrivalSeq (6) for the Intake after statEpoch and is unchanged by the memo rebuild; the rebuilt
        // FIFO has no tombstones and serves seq order 1, 2, 4, 5 (ids 10, 11, 13, 5)
        byte[] section = Section(dump, SectionIds.Inventory);
        const int IntakeRecord = 8;
        Assert.Equal(rig.Inventory.IntakeContainer.Value, BitConverter.ToUInt32(section, IntakeRecord));
        Assert.Equal(6u, BitConverter.ToUInt32(section, IntakeRecord + 23));
        Assert.Equal(dump, dumpAfter);
        Assert.False(peekWithoutMemo);
        Assert.Equal(new uint[] { 10, 11, 13, 5 }, order);
    }

    [Fact]
    public void StateDump_IsIdenticalAcrossTwoWorldsWithTheSameIntakeHistory()
    {
        // Arrange + Act
        byte[] a = Section(DumpHash.Bytes(IntakeHistory().Host.World), SectionIds.Inventory);
        byte[] b = Section(DumpHash.Bytes(IntakeHistory().Host.World), SectionIds.Inventory);

        // Assert
        Assert.Equal(a, b);
    }

    private static InvAutoPlaceRig IntakeHistory()
    {
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        _ = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, Mail(1, 2, 3), MergePolicy.NoMerge));
        _ = InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _));
        return rig;
    }

    /// <summary>Runs <paramref name="call"/> inside the next tick's <c>MailStep</c> phase and returns its result.</summary>
    internal static T InPhase<T>(InvAutoPlaceRig rig, Func<T> call)
    {
        T result = default!;
        rig.Hook.Action = (in TickContext ctx) => result = call();
        try
        {
            rig.Host.World.Tick();
        }
        finally
        {
            rig.Hook.Action = null;
        }

        return result;
    }

    internal static MailId[] Mail(params uint[] ids) => ids.Select(i => new MailId(i)).ToArray();

    /// <summary>Spawns <paramref name="count"/> letters to #4 into the Intake from id <paramref name="firstId"/>, at most 255 per tick.</summary>
    internal static void FillIntake(InvAutoPlaceRig rig, int count, uint firstId)
    {
        DefIndex letter = rig.KindIndex("letter");
        int done = 0;
        while (done < count)
        {
            int n = Math.Min(byte.MaxValue, count - done);
            MailId[] ids = InvAutoPlaceRig.Ids(firstId + (uint)done, n).Select(i => new MailId(i)).ToArray();
            SpawnResult r = InPhase(rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address4, 0, ids, MergePolicy.NoMerge));
            Assert.Equal(n, r.Placed);
            done += n;
        }
    }

    private static EventEntry[] Heads(EventEntry[] log, EventKind kind) =>
        log.Where(e => e.Kind == kind && (e.Flags & EventFlags.Continuation) == 0).ToArray();

    private static EventEntry[] InventoryEvents(EventEntry[] log) =>
        log.Where(e => e.Kind is >= InvEventKinds.Inserted and <= InvEventKinds.Version).ToArray();

    /// <summary>Headers (with the outfeed head/tail memo unless <paramref name="includeMemo"/> is false), the entry pool and the arena's counts, as one comparable list.</summary>
    private static List<long> State(InvAutoPlaceRig rig, bool includeMemo = true)
    {
        var values = new List<long>();
        ContainerTable containers = rig.Inventory.Containers;
        for (int s = 0; s < containers.LiveCount; s++)
        {
            ContainerHeader h = containers[(ushort)s];
            values.AddRange(new long[] { h.Version, h.EntryCount, h.FreeCells, h.NextArrivalSeq, h.LastReplaceVersion, h.StatEpoch });
            if (includeMemo)
            {
                values.Add(h.OutfeedHead);
                values.Add(h.OutfeedTail);
            }
        }

        EntryPool pool = rig.Entries;
        values.Add(pool.LastId);
        values.Add(pool.LiveCount);
        for (int s = 0; s < pool.Capacity; s++)
        {
            EntryRecord r = pool[(ushort)s];
            values.AddRange(new long[] { r.Id.Value, r.Count, r.ContainerSlot, r.X, r.Y, r.ArrivalSeq, r.MailIds.Raw });
        }

        values.Add(rig.Inventory.MailIds.LiveBlocks);
        values.Add(rig.Inventory.MailIds.LiveUnits);
        return values;
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
