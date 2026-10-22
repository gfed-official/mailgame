using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/005 (TR-inventory-002, TR-inventory-019; GDD H-R7-1; ADR-0009 sections 2-5, Validation Criteria seal test;
/// ADR-0008 sections 1 and 3): the <see cref="InventoryMutator"/> primitives, per-call version bumps, records then one
/// <c>InvVersion</c> seal per touched container, the reply last, mailId runs in <c>Continuation</c> entries, raise-time audiences, and
/// the shared arena-reservation step (reason 85) with its caller rule. No real inventory handler exists until stories 007+, so ops go
/// through the real phase-0 drain of a composed world to the TEST-ONLY <see cref="InvFixtureTransferHandler"/>, whose commit is the real
/// mutator and whose validate ends with the real <see cref="InvRequestCheck.CheckArenaReservation(in InvPlan)"/>. The chest is a
/// test-fixture external container (chest proper is M-1b); subscriptions are set directly (the open set is story 011).
/// </summary>
public class InventoryEventsTests
{
    private const int SlotA = 0;
    private const int SlotB = 1;

    public static TheoryData<string> Shapes => new() { "multi-block", "none-input", "chained" };

    public static TheoryData<string> CallShapes => new() { "single-container", "cross-container", "no-op" };

    public static TheoryData<EventKind> InventoryKinds
    {
        get
        {
            var data = new TheoryData<EventKind>();
            foreach (EventDescriptor d in InvEventDescriptors.All)
            {
                data.Add(d.Kind);
            }

            return data;
        }
    }

    private static uint ContainerOf(in EventEntry e) => e.PayloadAs<uint>(); // every inventory payload starts with the container id

    // ---------------------------------------------------------------- H-R7-1

    [Fact]
    [Trait("Id", "H-R7-1")]
    public void HR71_ChestToInventoryTransfer_BumpsBothOnce_EachRecipientGetsItsContainers_RejectedNextMoveChangesNothing()
    {
        // Arrange: chest C at v10 (A and B subscribed), A's inventory at v4 (B cannot see it)
        var rig = new InvEventRig();
        rig.Subscribe(rig.Chest, 0b11);
        rig.Header(rig.Chest).Version = 10;
        rig.Header(rig.InventoryA).Version = 4;
        EntryId e = rig.AddMail(rig.Chest, 0, 0, new uint[] { 100, 101, 102 });
        EntryId blocker = rig.AddMail(rig.Chest, 2, 0, new uint[] { 200 });
        ContainerHeader chestBefore = rig.Header(rig.Chest);
        ContainerHeader invBefore = rig.Header(rig.InventoryA);

        // Act: A's move transfers E chest -> inventory
        EventEntry[] log = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, rig.InventoryA, 0, 0));

        // Assert: versions and the touched containers' counters
        Assert.Equal(11u, rig.Header(rig.Chest).Version);
        Assert.Equal(5u, rig.Header(rig.InventoryA).Version);
        Assert.Equal(chestBefore.EntryCount - 1, rig.Header(rig.Chest).EntryCount);
        Assert.Equal(chestBefore.FreeCells + 1, rig.Header(rig.Chest).FreeCells);
        Assert.Equal(invBefore.EntryCount + 1, rig.Header(rig.InventoryA).EntryCount);
        Assert.Equal(invBefore.FreeCells - 1, rig.Header(rig.InventoryA).FreeCells);
        Reply reply = InvEventRig.ReplyOf(log);
        Assert.True(reply.IsOk);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal(11u, extra.ResultVersionA);
        Assert.Equal(5u, extra.ResultVersionB);
        Assert.Equal(0u, extra.ByActorId); // byActorId is the contested actor (stories 013/014); the actor is on the seals
        Assert.False(extra.Unchanged);

        // Assert: A receives C's InvRemoved{E} + seal v10->11 (actorId A) and the inventory's InvInserted{E} (+ its run) + seal v4->5, then the reply
        EventEntry[] toA = InvEventRig.DeliveredTo(log, SlotA);
        Assert.Equal(
            new[] { InvEventKinds.Removed, InvEventKinds.Inserted, InvEventKinds.Inserted, InvEventKinds.Version, InvEventKinds.Version, EventKind.OpReply },
            toA.Select(x => x.Kind).ToArray());
        Assert.Equal(e.Value, toA[0].PayloadAs<InvRemovedPayload>().EntryId);
        Assert.Equal(rig.Chest.Value, ContainerOf(in toA[0]));
        Assert.Equal(e.Value, toA[1].PayloadAs<InvInsertedPayload>().EntryId);
        Assert.Equal(rig.InventoryA.Value, ContainerOf(in toA[1]));
        Assert.Equal(EventFlags.Continuation, toA[2].Flags);
        AssertSeal(toA[3], rig.Chest, 10, 11, rig.PlayerA);
        AssertSeal(toA[4], rig.InventoryA, 4, 5, rig.PlayerA);

        // Assert: B receives only the chest's record and seal
        EventEntry[] toB = InvEventRig.DeliveredTo(log, SlotB);
        Assert.Equal(new[] { InvEventKinds.Removed, InvEventKinds.Version }, toB.Select(x => x.Kind).ToArray());
        Assert.Equal(e.Value, toB[0].PayloadAs<InvRemovedPayload>().EntryId);
        AssertSeal(toB[1], rig.Chest, 10, 11, rig.PlayerA);

        // Act: A's next move is rejected (blocked: E occupies the target cell)
        EventEntry[] rejectedLog = rig.Run(rig.PlayerA, rig.Move(rig.Chest, blocker, rig.InventoryA, 0, 0));

        // Assert: versions unchanged, A gets only the reject (resultVersion = current), B receives no event
        Assert.Equal(11u, rig.Header(rig.Chest).Version);
        Assert.Equal(5u, rig.Header(rig.InventoryA).Version);
        Reply rejected = InvEventRig.ReplyOf(rejectedLog);
        Assert.Equal((ushort)InvReason.Blocked, rejected.Reason);
        Assert.Equal(11u, rejected.ExtraAs<InvReplyExtra>().ResultVersionA);
        Assert.Equal(5u, rejected.ExtraAs<InvReplyExtra>().ResultVersionB);
        Assert.Equal(new[] { EventKind.OpReply }, InvEventRig.DeliveredTo(rejectedLog, SlotA).Select(x => x.Kind).ToArray());
        Assert.Empty(InvEventRig.DeliveredTo(rejectedLog, SlotB));
        Assert.Single(rejectedLog);
    }

    // ---------------------------------------------------------------- Seal test (ADR-0009 Validation Criteria)

    [Theory]
    [Trait("Id", "TR-inventory-002")]
    [Trait("Id", "TR-inventory-019")]
    [MemberData(nameof(CallShapes))]
    public void SealTest_EachTouchedContainerGetsOneSealAfterItsRecordsAndBeforeTheReply_NoOpRaisesAndBumpsNothing(string shape)
    {
        // Arrange
        var rig = new InvEventRig();
        rig.Subscribe(rig.Chest, 0b11);
        rig.Header(rig.Chest).Version = 10;
        rig.Header(rig.InventoryA).Version = 4;
        EntryId e = rig.AddMail(rig.Chest, 0, 0, new uint[] { 7, 8 });
        (ContainerId to, short x, ContainerId[] sealedExpected) = shape switch
        {
            "single-container" => (rig.Chest, (short)3, new[] { rig.Chest }),
            "cross-container" => (rig.InventoryA, (short)0, new[] { rig.Chest, rig.InventoryA }),
            _ => (rig.Chest, (short)0, Array.Empty<ContainerId>()),
        };
        ContainerHeader chestBefore = rig.Header(rig.Chest);
        ContainerHeader invBefore = rig.Header(rig.InventoryA);

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, to, x, 0));

        // Assert: the reply is last, everything before it is the handler's call
        Reply reply = InvEventRig.ReplyOf(log);
        Assert.True(reply.IsOk);
        Assert.Equal(EventKind.OpReply, log[^1].Kind);
        uint call = log[^1].CallSeq;
        Assert.All(log, entry => Assert.Equal(call, entry.CallSeq));

        // Assert: exactly one seal per touched container, in first-touch order, each +1
        EventEntry[] seals = log.Where(entry => entry.Kind == InvEventKinds.Version).ToArray();
        Assert.Equal(sealedExpected.Select(c => c.Value).ToArray(), seals.Select(s => ContainerOf(in s)).ToArray());
        Assert.Equal(shape == "no-op" ? 10u : 11u, rig.Header(rig.Chest).Version);
        Assert.Equal(shape == "cross-container" ? 5u : 4u, rig.Header(rig.InventoryA).Version);

        // Assert: every record (and Continuation chunk) of a container precedes that container's seal
        uint headContainer = 0;
        for (int i = 0; i < log.Length - 1; i++)
        {
            EventEntry entry = log[i];
            if (entry.Kind == InvEventKinds.Version)
            {
                continue;
            }

            uint c = (entry.Flags & EventFlags.Continuation) != 0 ? headContainer : ContainerOf(in entry);
            headContainer = c;
            int sealIndex = Array.FindIndex(log, s => s.Kind == InvEventKinds.Version && ContainerOf(in s) == c);
            Assert.True(sealIndex > i, $"record {i} ({entry.Kind}) of container {c} is not before its seal ({sealIndex})");
        }

        int moved = shape == "cross-container" ? 1 : 0;
        Assert.Equal(chestBefore.EntryCount - moved, rig.Header(rig.Chest).EntryCount);
        Assert.Equal(chestBefore.FreeCells + moved, rig.Header(rig.Chest).FreeCells);
        Assert.Equal(invBefore.EntryCount + moved, rig.Header(rig.InventoryA).EntryCount);
        Assert.Equal(invBefore.FreeCells - moved, rig.Header(rig.InventoryA).FreeCells);

        if (shape == "no-op")
        {
            Assert.Single(log); // nothing but the reply
            Assert.True(reply.ExtraAs<InvReplyExtra>().Unchanged);
        }
    }

    // ---------------------------------------------------------------- mailId runs, InvRemoved, sizes

    [Fact]
    [Trait("Id", "TR-inventory-019")]
    public void Inserted20ConsecutiveUnits_OneRunInOneContinuationEntryOfTheSameKindAndCallSeq()
    {
        // Arrange
        var rig = new InvEventRig();
        uint[] ids = Enumerable.Range(500, 20).Select(i => (uint)i).ToArray();
        EntryId e = rig.AddMail(rig.Chest, 0, 0, ids);

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, rig.InventoryA, 0, 0));

        // Assert
        int head = Array.FindIndex(log, x => x.Kind == InvEventKinds.Inserted);
        InvInsertedPayload p = log[head].PayloadAs<InvInsertedPayload>();
        Assert.Equal(20, p.Count);
        Assert.Equal(1, p.RunCount);
        Assert.Equal(EventFlags.None, log[head].Flags);
        EventEntry chunk = log[head + 1];
        Assert.Equal(InvEventKinds.Inserted, chunk.Kind);
        Assert.Equal(EventFlags.Continuation, chunk.Flags);
        Assert.Equal(log[head].CallSeq, chunk.CallSeq);
        Assert.Equal(InvMailIdRuns.RunBytes, chunk.PayloadLength);
        InvMailIdRuns runs = chunk.PayloadAs<InvMailIdRuns>();
        Assert.Equal(500u, runs[0].Start);
        Assert.Equal(20, runs[0].Length);
        Assert.Equal(0, runs[1].Length);
        Assert.NotEqual(InvEventKinds.Inserted, log[head + 2].Kind);
    }

    [Fact]
    [Trait("Id", "TR-inventory-019")]
    public void Inserted20UnitsInTenRuns_EightRunsPer48BChunk_AscendingAndComplete_InvRemovedCarriesNoMailIds()
    {
        // Arrange: 10 runs of 2 ids (1000-1001, 1010-1011, ...)
        var rig = new InvEventRig();
        uint[] ids = Enumerable.Range(0, 10).SelectMany(r => new[] { (uint)(1000 + (r * 10)), (uint)(1001 + (r * 10)) }).ToArray();
        EntryId e = rig.AddMail(rig.Chest, 0, 0, ids);

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, rig.InventoryA, 0, 0));

        // Assert: InvRemoved is 12 B with no Continuation after it
        int removed = Array.FindIndex(log, x => x.Kind == InvEventKinds.Removed);
        Assert.Equal(12, log[removed].PayloadLength);
        Assert.Equal(20, log[removed].PayloadAs<InvRemovedPayload>().Count);
        Assert.Equal(EventFlags.None, log[removed + 1].Flags);
        Assert.DoesNotContain(log, x => x.Kind == InvEventKinds.Removed && (x.Flags & EventFlags.Continuation) != 0);

        // Assert: the head says 10 runs; two chunks of 8 and 2 runs (48 B and 12 B), same kind and CallSeq, contiguous
        int head = Array.FindIndex(log, x => x.Kind == InvEventKinds.Inserted);
        Assert.Equal(48, log[head].PayloadLength);
        Assert.Equal(10, log[head].PayloadAs<InvInsertedPayload>().RunCount);
        EventEntry[] chunks = { log[head + 1], log[head + 2] };
        Assert.All(chunks, c =>
        {
            Assert.Equal(InvEventKinds.Inserted, c.Kind);
            Assert.Equal(EventFlags.Continuation, c.Flags);
            Assert.Equal(log[head].CallSeq, c.CallSeq);
        });
        Assert.Equal(48, chunks[0].PayloadLength);
        Assert.Equal(12, chunks[1].PayloadLength);
        Assert.NotEqual(EventFlags.Continuation, log[head + 3].Flags);
        var decoded = new List<uint>();
        foreach (EventEntry c in chunks)
        {
            InvMailIdRuns runs = c.PayloadAs<InvMailIdRuns>();
            for (int r = 0; r < c.PayloadLength / InvMailIdRuns.RunBytes; r++)
            {
                for (uint k = 0; k < runs[r].Length; k++)
                {
                    decoded.Add(runs[r].Start + k);
                }
            }
        }

        Assert.Equal(ids, decoded.ToArray()); // ascending and complete
    }

    [Theory]
    [MemberData(nameof(InventoryKinds))]
    public void EveryInventoryEventDescriptor_IsPaddingFreeWithinLimitsInTheInventoryBlock_AndBoundInTheWorld(EventKind kind)
    {
        // Arrange
        EventDescriptor d = InvEventDescriptors.All.Single(x => x.Kind == kind);
        var rig = new InvEventRig();

        // Act + Assert: plain-integer, at most 48 B, size == PayloadLength, field sizes sum to the size (padding-free), inventory block
        EventDescriptorAssert.Check(d, OpKindBlock.Inventory);
        bool world = kind >= InvEventKinds.WorldItemSpawned; // story 013's world kinds replicate to every connected slot
        Assert.Equal(world ? ReplicationClass.Replicated : ReplicationClass.Targeted, d.Class);
        Assert.Same(d, rig.Host.World.Events.DescriptorFor(kind));
    }

    [Fact]
    public void PayloadSizes_ArePinned_AndTheRunChunkIsPaddingFree()
    {
        // Act + Assert
        Assert.Equal(48, Unsafe.SizeOf<InvInsertedPayload>());
        Assert.Equal(12, Unsafe.SizeOf<InvRemovedPayload>());
        Assert.Equal(48, Unsafe.SizeOf<InvMergedPayload>());
        Assert.Equal(12, Unsafe.SizeOf<InvMovedPayload>());
        Assert.Equal(16, Unsafe.SizeOf<InvSplitFromPayload>());
        Assert.Equal(20, Unsafe.SizeOf<InvVersionPayload>());
        Assert.Equal(44, Unsafe.SizeOf<WorldItemSpawnedPayload>());
        Assert.Equal(16, Unsafe.SizeOf<WorldItemDespawnedPayload>());
        Assert.Equal(16, Unsafe.SizeOf<WorldItemEntriesChangedPayload>());
        Assert.Equal(8, Unsafe.SizeOf<ItemsLostPayload>());
        Assert.Equal(4, Unsafe.SizeOf<BacklogUnitsPayload>());
        Assert.Equal(InvMailIdRuns.RunBytes, Unsafe.SizeOf<InvMailIdRun>());
        Assert.Equal(EventPayload.Size, Unsafe.SizeOf<InvMailIdRuns>());
        Assert.Equal(InvMailIdRuns.Capacity * InvMailIdRuns.RunBytes, Unsafe.SizeOf<InvMailIdRuns>());
        Assert.Equal(Unsafe.SizeOf<InvMailIdRuns>(), DescriptorAssert.SumOfFieldSizes(typeof(InvMailIdRuns)));
        Assert.Equal(Unsafe.SizeOf<InvMailIdRun>(), DescriptorAssert.SumOfFieldSizes(typeof(InvMailIdRun)));
        Assert.Equal(EventEntry.SizeBytes, Unsafe.SizeOf<EventEntry>());
    }

    // ---------------------------------------------------------------- audiences

    [Fact]
    public void PanelAndCursorEvents_GoOnlyToTheOwningSlot()
    {
        // Arrange: an entry in A's inventory; B is in slot 1
        var rig = new InvEventRig();
        EntryId e = rig.AddMail(rig.InventoryA, 0, 0, new uint[] { 9 });

        // Act: inventory -> hotbar, then hotbar -> cursor
        EventEntry[] toHotbar = rig.Run(rig.PlayerA, rig.Move(rig.InventoryA, e, rig.HotbarA, 0, 0));
        EventEntry[] toCursor = rig.Run(rig.PlayerA, rig.Move(rig.HotbarA, e, rig.CursorA, 0, 0));

        // Assert: every record and seal is addressed to exactly A's bit; B is sent nothing
        foreach (EventEntry[] log in new[] { toHotbar, toCursor })
        {
            Assert.True(InvEventRig.ReplyOf(log).IsOk);
            EventEntry[] inventoryEntries = log.Where(x => x.Kind != EventKind.OpReply).ToArray();
            Assert.NotEmpty(inventoryEntries);
            Assert.All(inventoryEntries, x => Assert.Equal(1 << SlotA, x.Audience));
            Assert.All(inventoryEntries, x => Assert.Equal(ReplicationClass.Targeted, x.Class)); // pinned deviation, TD-114
            Assert.Empty(InvEventRig.DeliveredTo(log, SlotB));
        }

        Assert.Contains(toCursor, x => x.Kind == InvEventKinds.Version && ContainerOf(in x) == rig.CursorA.Value);
    }

    [Fact]
    public void ExternalContainerEvents_AreTargetedToTheSubscriberSetResolvedAtRaiseTime()
    {
        // Arrange
        var rig = new InvEventRig();
        EntryId e = rig.AddMail(rig.Chest, 0, 0, new uint[] { 9 });

        // Act: the subscriber set changes between calls
        rig.Subscribe(rig.Chest, 0b11);
        EventEntry[] both = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, rig.Chest, 1, 0));
        rig.Subscribe(rig.Chest, 0b10);
        EventEntry[] onlyB = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, rig.Chest, 2, 0));
        rig.Subscribe(rig.Chest, 0);
        EventEntry[] nobody = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, rig.Chest, 3, 0));

        // Assert: each call's records and seal carry the set as it was when raised
        AssertAudience(both, 0b11);
        AssertAudience(onlyB, 0b10);
        AssertAudience(nobody, 0);
        Assert.Equal(new[] { EventKind.OpReply }, InvEventRig.DeliveredTo(nobody, SlotA).Select(x => x.Kind).ToArray());
        Assert.Equal(3u, rig.Header(rig.Chest).Version); // one bump per call (from 0), whoever watched

        static void AssertAudience(EventEntry[] log, int mask)
        {
            EventEntry[] records = log.Where(x => x.Kind != EventKind.OpReply).ToArray();
            Assert.Equal(new[] { InvEventKinds.Moved, InvEventKinds.Version }, records.Select(x => x.Kind).ToArray());
            Assert.All(records, x =>
            {
                Assert.Equal(ReplicationClass.Targeted, x.Class);
                Assert.Equal(mask, x.Audience);
            });
        }
    }

    // ---------------------------------------------------------------- reason 85 and the caller rule

    [Fact]
    [Trait("Id", "TD-102")]
    public void ArenaReservationFails_RejectsWithCapacityExhausted85_NothingMutated_NoCheckRecordWritten()
    {
        // Arrange: a transfer whose commit would acquire blocks the exhausted arena cannot hold at once
        var rig = new InvEventRig();
        rig.Subscribe(rig.Chest, 0b11);
        EntryId e = rig.AddMail(rig.Chest, 0, 0, new uint[] { 1, 2, 3 });
        ArenaShape shape = ArenaShape.MultiBlock();
        _ = shape.Prepare(rig.Inventory.MailIds);
        rig.Handler.Shape = shape;
        List<long> before = rig.Snapshot();
        long writes = rig.RingWrites;

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, rig.InventoryA, 0, 0));

        // Assert: the reply says 85, nothing is mutated or raised, and CanReserve (a dry run) wrote no record 12
        Reply reply = InvEventRig.ReplyOf(log);
        Assert.Equal((ushort)InvReason.CapacityExhausted, reply.Reason);
        Assert.Equal(ReplyResult.Rejected, reply.Result);
        Assert.Single(log);
        Assert.Equal(before, rig.Snapshot());
        Assert.Equal(writes, rig.RingWrites);
        Assert.Equal(0, rig.Handler.Commits);
        Assert.Null(shape.Results);
    }

    [Theory]
    [Trait("Id", "TD-102")]
    [MemberData(nameof(Shapes))]
    public void CallerRule_EveryAcquiredBlockPassed_AtExhaustion_Is85WithNothingMutated(string shapeName)
    {
        // Arrange
        var rig = new InvEventRig();
        EntryId e = rig.AddMail(rig.Chest, 0, 0, new uint[] { 1 });
        ArenaShape shape = ArenaShape.ByName(shapeName);
        _ = shape.Prepare(rig.Inventory.MailIds);
        rig.Handler.Shape = shape;
        rig.Handler.Requested = shape.Blocks;
        List<long> before = rig.Snapshot();
        long writes = rig.RingWrites;

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, rig.InventoryA, 0, 0));

        // Assert
        Assert.Equal((ushort)InvReason.CapacityExhausted, InvEventRig.ReplyOf(log).Reason);
        Assert.Equal(before, rig.Snapshot());
        Assert.Equal(writes, rig.RingWrites);
        Assert.Null(shape.Results);
    }

    [Theory]
    [Trait("Id", "TD-102")]
    [MemberData(nameof(Shapes))]
    public void CallerRule_OneBlockOmitted_PassesTheStep_FalseAccept_AndTheRealCommitFailsToAcquire(string shapeName)
    {
        // Arrange: the same exhausted state; the validator is handed the under-sized request (one block left out)
        var rig = new InvEventRig();
        EntryId e = rig.AddMail(rig.Chest, 0, 0, new uint[] { 1 });
        ArenaShape shape = ArenaShape.ByName(shapeName);
        _ = shape.Prepare(rig.Inventory.MailIds);
        rig.Handler.Shape = shape;
        rig.Handler.Requested = shape.UnderSized;
        long writes = rig.RingWrites;

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, rig.InventoryA, 0, 0));

        // Assert: accepted (the false accept), and an acquisition of the commit failed with one InventoryArenaExhausted record
        Assert.True(InvEventRig.ReplyOf(log).IsOk);
        Assert.NotNull(shape.Results);
        Assert.Contains(false, shape.Results!);
        Assert.Equal(writes + 1, rig.RingWrites);
        Assert.Equal(CheckCode.InventoryArenaExhausted, rig.Ring[rig.Ring.Count - 1].Check);
    }

    [Fact]
    public void CheckArenaReservation_EmptyPlanPasses_OverflowedPlanAssertsUnderInventoryAssert_OrIs85InRelease_NoRecordWritten()
    {
        // Arrange
        var rig = new InvEventRig();
        var empty = new InvPlan();
        var overflowed = new InvPlan();
        for (int i = 0; i < InvPlan.MaxAcquiredBlocks; i++)
        {
            Assert.True(overflowed.AddAcquiredBlock(1));
        }

        long writes = rig.RingWrites;

        // Act + Assert: the empty plan and an in-limit span pass
        Assert.Equal(0, rig.Inventory.RequestCheck.CheckArenaReservation(in empty));
        Assert.Equal(0, rig.Inventory.RequestCheck.CheckArenaReservation(new[] { 1, 2, 256 }));

        // Act + Assert: one block over the limit is a programming error; Release keeps the conservative 85
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => overflowed.AddAcquiredBlock(1));
            Assert.Equal(SimAssertCode.InventoryPlanCapacityExceeded, thrown.Code);
            Assert.False(overflowed.AcquiredBlocksOverflowed);
        }
        else
        {
            Assert.False(overflowed.AddAcquiredBlock(1));
            Assert.True(overflowed.AcquiredBlocksOverflowed);
            Assert.Equal((ushort)InvReason.CapacityExhausted, rig.Inventory.RequestCheck.CheckArenaReservation(in overflowed));
        }

        Assert.Equal(InvPlan.MaxAcquiredBlocks, overflowed.AcquiredBlockCount);
        Assert.Equal(writes, rig.RingWrites);
    }

    [Fact]
    public void Validate_FifthTouchedContainerRefused_RejectsWithBadRequestBeforeAnyMutation()
    {
        // Arrange: the fixture plans three more containers, so the destination is the fifth touch
        var rig = new InvEventRig();
        rig.Subscribe(rig.Chest, 0b11);
        EntryId e = rig.AddMail(rig.Chest, 0, 0, new uint[] { 5 });
        rig.Handler.ExtraTouches = new[] { rig.HotbarA, rig.CursorA, rig.Inventory.IntakeContainer };
        List<long> before = rig.Snapshot();

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, rig.Move(rig.Chest, e, rig.InventoryA, 0, 0));

        // Assert: same in every config (Touch refuses with -1, no assert); nothing mutated, nothing raised
        Assert.Equal(CoreReason.BadRequest, InvEventRig.ReplyOf(log).Reason);
        Assert.Single(log);
        Assert.Equal(before, rig.Snapshot());
        Assert.Equal(0, rig.Handler.Commits);
    }

    [Fact]
    public void Primitive_MarkingAnUntouchedContainer_AssertsUnderInventoryAssert_OrIsStillSealedInRelease()
    {
        // Arrange: Validate breaks the contract and does not touch the destination
        var rig = new InvEventRig();
        rig.Subscribe(rig.Chest, 0b11);
        EntryId e = rig.AddMail(rig.Chest, 0, 0, new uint[] { 5 });
        rig.Handler.SkipDestinationTouch = true;
        Op op = rig.Move(rig.Chest, e, rig.InventoryA, 0, 0);

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.True(rig.Host.World.EnqueueOp(rig.PlayerA, in op));
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => rig.Host.World.Tick());
            Assert.Equal(SimAssertCode.InventoryPlanContainerNotTouched, thrown.Code);
        }
        else
        {
            EventEntry[] log = rig.Run(rig.PlayerA, in op);
            uint[] sealedContainers = log.Where(x => x.Kind == InvEventKinds.Version).Select(x => ContainerOf(in x)).ToArray();
            Assert.Equal(new[] { rig.Chest.Value, rig.InventoryA.Value }, sealedContainers);
            Assert.Equal(1u, rig.Header(rig.InventoryA).Version);
        }
    }

    [Fact]
    public void Place_OnAPlacedOrFreeEntry_AssertsUnderInventoryAssert_OrReturnsWithNothingChangedInRelease()
    {
        // Arrange: a placed entry and a free pool slot (no tick needed: nothing may be raised)
        var rig = new InvEventRig();
        EntryId e = rig.AddMail(rig.Chest, 0, 0, new uint[] { 5 });
        Assert.True(rig.Inventory.Entries.TryGetSlot(e, out ushort placed));
        ushort free = (ushort)(rig.Inventory.Entries.Capacity - 1);
        Assert.False(rig.Inventory.Entries[free].IsLive);
        ushort to = rig.SlotOf(rig.InventoryA);
        var plan = new InvPlan();
        _ = plan.Touch(to);
        List<long> before = rig.Snapshot();
        InventoryMutator m = rig.Inventory.Mutator;

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException onPlaced = Assert.Throws<SimAssertException>(() => m.Place(ref plan, placed, to, 2, 0, false));
            Assert.Equal(SimAssertCode.InventoryCellConflict, onPlaced.Code);
            SimAssertException onFree = Assert.Throws<SimAssertException>(() => m.Place(ref plan, free, to, 2, 0, false));
            Assert.Equal(SimAssertCode.InventoryEntrySlotFree, onFree.Code);
        }
        else
        {
            m.Place(ref plan, placed, to, 2, 0, false);
            m.Place(ref plan, free, to, 2, 0, false);
        }

        Assert.Equal(before, rig.Snapshot());
        Assert.Equal(0, plan.MutatedCount);
    }

    [Fact]
    public void Transfer_WithTheTouchedSetFullOfOtherContainers_AssertsBeforeAnyChange_OrChangesNothingInRelease()
    {
        // Arrange: Validate broke the touch contract and filled the set with four other containers
        var rig = new InvEventRig();
        EntryId e = rig.AddMail(rig.Chest, 0, 0, new uint[] { 5 });
        Assert.True(rig.Inventory.Entries.TryGetSlot(e, out ushort entry));
        ushort to = rig.SlotOf(rig.InventoryA);
        var plan = new InvPlan();
        for (ushort i = 0; i < InvPlan.MaxTouched; i++)
        {
            Assert.True(plan.Touch((ushort)(200 + i)) >= 0);
        }

        List<long> before = rig.Snapshot();
        InventoryMutator m = rig.Inventory.Mutator;

        // Act + Assert: never a change the plan cannot seal (the entry is not left detached either)
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => m.Transfer(ref plan, entry, to, 0, 0, false));
            Assert.Equal(SimAssertCode.InventoryPlanContainerNotTouched, thrown.Code);
        }
        else
        {
            m.Transfer(ref plan, entry, to, 0, 0, false);
        }

        Assert.Equal(before, rig.Snapshot());
        Assert.Equal(0, plan.MutatedCount);
        Assert.Equal(InvPlan.MaxTouched, plan.TouchedCount);
    }

    [Fact]
    public void TryCreateContainer_SizeBeyondTheOccupancyPreallocation_ReturnsFalse_LeavingNoContainer()
    {
        // Arrange
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryGetHeader(rig.InventoryA, out ContainerHeader inv));
        int live = rig.Inventory.LiveContainerCount;
        uint nextId = rig.Inventory.Containers.NextId;
        int maxW = rig.Inventory.Occupancy.MaxWidth;
        int maxH = rig.Inventory.Occupancy.MaxRows;

        // Act
        bool wide = rig.Inventory.TryCreateContainer(inv.DefIndex, maxW + 1, 1, inv.Layout, ContainerHeader.NoOwner, out ContainerId wideId);
        bool tall = rig.Inventory.TryCreateContainer(inv.DefIndex, 1, maxH + 1, inv.Layout, ContainerHeader.NoOwner, out ContainerId tallId);
        bool negative = rig.Inventory.TryCreateContainer(inv.DefIndex, -1, 1, inv.Layout, ContainerHeader.NoOwner, out _);
        bool fits = rig.Inventory.TryCreateContainer(inv.DefIndex, maxW, maxH, inv.Layout, ContainerHeader.NoOwner, out ContainerId fitId);

        // Assert
        Assert.False(wide || tall || negative);
        Assert.True(wideId.IsNone && tallId.IsNone);
        Assert.True(fits);
        Assert.Equal(live + 1, rig.Inventory.LiveContainerCount);
        Assert.Equal(nextId, fitId.Value); // the refusals consumed no id
    }

    // ---------------------------------------------------------------- InvPlan and the reply extra

    [Fact]
    public void InvPlan_TouchedSetDeduplicatesInFirstTouchOrder_AndOnlyMutatedContainersCount()
    {
        // Arrange
        var plan = new InvPlan();

        // Act
        int a = plan.Touch(7);
        int b = plan.Touch(3);
        int again = plan.Touch(7);
        int c = plan.Touch(9);
        plan.MarkMutated(3);
        plan.MarkMutated(9);

        // Assert
        Assert.Equal(0, a);
        Assert.Equal(1, b);
        Assert.Equal(0, again);
        Assert.Equal(2, c);
        Assert.Equal(3, plan.TouchedCount);
        Assert.Equal(new ushort[] { 7, 3, 9 }, Enumerable.Range(0, plan.TouchedCount).Select(plan.TouchedSlot).ToArray());
        Assert.False(plan.IsMutated(0));
        Assert.True(plan.IsMutated(1));
        Assert.True(plan.IsMutated(2));
        Assert.Equal(2, plan.MutatedCount);
    }

    [Fact]
    public void InvPlan_FifthContainer_TouchRefusesWithMinusOne_InEveryConfig_AndTheSetIsUnchanged()
    {
        // Arrange
        var plan = new InvPlan();
        for (ushort s = 0; s < InvPlan.MaxTouched; s++)
        {
            _ = plan.Touch(s);
        }

        // Act
        int fifth = plan.Touch(99);
        int existing = plan.Touch(2);

        // Assert: a full set is a Validate refusal (no assert); a container already in the set still resolves
        Assert.Equal(-1, fifth);
        Assert.Equal(2, existing);
        Assert.Equal(InvPlan.MaxTouched, plan.TouchedCount);
        Assert.DoesNotContain((ushort)99, Enumerable.Range(0, plan.TouchedCount).Select(plan.TouchedSlot));
    }

    [Fact]
    public void InvReplyExtra_SetCause_WritesThreeCauseBitsAndTheUnchangedFlag_KeepingOtherFlags()
    {
        // Arrange
        var extra = new InvReplyExtra { CauseAndFlags = InvReplyExtra.FlagAppliedModeSize };

        // Act
        extra.SetCause(InvCause.Destroyed, unchanged: true);

        // Assert
        Assert.Equal(InvCause.Destroyed, extra.Cause);
        Assert.True(extra.Unchanged);
        Assert.Equal(InvReplyExtra.FlagAppliedModeSize | InvReplyExtra.FlagUnchanged | (byte)InvCause.Destroyed, extra.CauseAndFlags);
        extra.SetCause(InvCause.None, unchanged: false);
        Assert.Equal(InvReplyExtra.FlagAppliedModeSize, extra.CauseAndFlags);
    }

    private static void AssertSeal(in EventEntry e, ContainerId c, uint from, uint to, ActorId actor)
    {
        Assert.Equal(InvEventKinds.Version, e.Kind);
        InvVersionPayload seal = e.PayloadAs<InvVersionPayload>();
        Assert.Equal(c.Value, seal.Container);
        Assert.Equal(from, seal.VersionFrom);
        Assert.Equal(to, seal.VersionTo);
        Assert.Equal(actor.Value, seal.ActorId);
        Assert.Equal((byte)InvCause.None, seal.Cause);
    }
}
