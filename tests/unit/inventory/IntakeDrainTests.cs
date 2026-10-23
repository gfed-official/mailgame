using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Backlog;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Sim.Tests.Unit.Inventory.DumpSections;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/021: the Intake backlog (<see cref="IInventoryApi.AppendBacklog"/>, <see cref="IInventoryApi.BacklogUnits"/>,
/// <see cref="IInventoryApi.BacklogEntries"/>, the <c>ReturnToIntake</c> backlog branch, nothing overtakes the backlog) and the phase 2
/// <c>IntakeDrain</c> (gating, skip rule, start anchors, one bracket per pass, fresh <c>arrivalSeq</c>, H-R14-2, H-PERF-6c).
/// </summary>
public sealed class IntakeDrainTests
{
    private const uint Address4 = 4u;
    private const uint Address9 = 9u;

    // ---------------------------------------------------------------- AppendBacklog

    [Fact]
    [Trait("Id", "H-R14-4")]
    public void AppendBacklog_AWellFormedEntry_QueuesOneDetachedEntryAtTheTail_AndRaisesOnlyBacklogUnits()
    {
        // Arrange: a backlog already holding one letter (so the drain cannot take the new one: nothing overtakes it)
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        Assert.True(Append(rig, "letter", Address4, 2_000));
        uint version = rig.Header(rig.Intake).Version;
        uint lastId = rig.Entries.LastId;

        // Act
        bool ok = Append(rig, "letter", Address9, 30, 31, 32);
        EventEntry[] log = rig.Sink.Last;

        // Assert: one new detached count-3 entry with the StackKey and ids, at the tail; units 4, entries 2; one BacklogUnits record
        Assert.True(ok);
        Assert.Equal(2, rig.Inventory.BacklogEntries);
        Assert.Equal(4, rig.Inventory.BacklogUnits);
        ushort tail = Queue(rig)[1];
        Assert.Equal(lastId + 1, rig.Entries[tail].Id.Value);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[tail].ContainerSlot);
        Assert.Equal(3, rig.Entries[tail].Count);
        Assert.Equal(rig.KindIndex("letter").Value, rig.Entries[tail].MailKind);
        Assert.Equal(Address9, rig.Entries[tail].AddressId);
        Assert.Equal(1, rig.Entries[tail].W);
        Assert.Equal(1, rig.Entries[tail].H);
        Assert.Equal(new uint[] { 30, 31, 32 }, rig.IdsOf(tail));
        Assert.Equal(version, rig.Header(rig.Intake).Version);
        EventEntry record = Assert.Single(log, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);
        Assert.Equal(InvEventKinds.BacklogUnits, record.Kind);
        Assert.Equal(4, record.PayloadLength);
        BacklogUnitsPayload p = record.PayloadAs<BacklogUnitsPayload>();
        Assert.Equal(4, p.Units);
        Assert.Equal(2, p.Entries);
    }

    public static TheoryData<string> Malformed => new()
    {
        "none-kind", "unknown-kind", "no-ids", "256-ids", "not-ascending", "duplicate", "none-id", "small-2-ids-over-max-stack-1", "letter-21-ids-over-max-stack-20",
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AppendBacklog_AMalformedEntry_IsRefused_WithNothingMutatedAndNothingRaised(string shape)
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        uint lastId = rig.Entries.LastId;
        int liveBlocks = rig.Inventory.MailIds.LiveBlocks;
        int records = rig.Host.World.CheckRing.Count;
        (DefIndex kind, MailId[] ids) = shape switch
        {
            "none-kind" => (default(DefIndex), IntakeSpawnOutfeedTests.Mail(1)),
            "unknown-kind" => (DefIndex.FromValue((ushort)rig.Content.MailKinds.Count), IntakeSpawnOutfeedTests.Mail(1)),
            "no-ids" => (letter, Array.Empty<MailId>()),
            "256-ids" => (letter, IntakeSpawnOutfeedTests.Mail(InvAutoPlaceRig.Ids(1, 256))),
            "not-ascending" => (letter, IntakeSpawnOutfeedTests.Mail(5, 4)),
            "duplicate" => (letter, IntakeSpawnOutfeedTests.Mail(5, 5)),
            "none-id" => (letter, new[] { MailId.None, new MailId(2) }),
            "small-2-ids-over-max-stack-1" => (rig.KindIndex("small"), IntakeSpawnOutfeedTests.Mail(5, 6)), // could never drain whole
            _ => (letter, IntakeSpawnOutfeedTests.Mail(InvAutoPlaceRig.Ids(1, 21))),
        };

        // Act
        bool ok = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.AppendBacklog(new BacklogEntry(kind, Address4, 0, ids)));

        // Assert
        Assert.False(ok);
        Assert.Equal(0, rig.Inventory.BacklogEntries);
        Assert.Equal(lastId, rig.Entries.LastId);
        Assert.Equal(liveBlocks, rig.Inventory.MailIds.LiveBlocks);
        Assert.Equal(records, rig.Host.World.CheckRing.Count);
        Assert.DoesNotContain(rig.Sink.Last, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);
    }

    [Fact]
    public void AppendBacklog_OutsideATickPhase_IsRefusedAndCounted_WithNothingMutated()
    {
        // Arrange: between ticks
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        long refused = rig.Counters[CounterId.MutatingApiOutOfPhase];
        uint lastId = rig.Entries.LastId;

        // Act + Assert: SIM_ASSERT builds assert; Release refuses and counts
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(
                () => rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address4, 0, IntakeSpawnOutfeedTests.Mail(1))));
            Assert.Equal(SimAssertCode.MutatingApiOutOfPhase, ex.Code);
        }
        else
        {
            Assert.False(rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address4, 0, IntakeSpawnOutfeedTests.Mail(1))));
        }

        Assert.Equal(refused + 1, rig.Counters[CounterId.MutatingApiOutOfPhase]);
        Assert.Equal(0, rig.Inventory.BacklogEntries);
        Assert.Equal(lastId, rig.Entries.LastId);
    }

    [Fact]
    public void AppendBacklog_AFullEntryPool_IsRefused_WithThePoolRecord_AndNothingMutated()
    {
        // Arrange: a 2-slot pool holding two detached letters
        var rig = new InvAutoPlaceRig(entryPoolCapacity: 2);
        _ = rig.DetachedMail("letter", Address4, new[] { 1u });
        _ = rig.DetachedMail("letter", Address4, new[] { 2u });
        uint lastId = rig.Entries.LastId;
        int liveBlocks = rig.Inventory.MailIds.LiveBlocks;
        int records = rig.Host.World.CheckRing.Count;

        // Act
        bool ok = Append(rig, "letter", Address4, 3);

        // Assert: false, one InventoryEntryPoolExhausted record, no id, no block, nothing raised; the units stay Mail's
        Assert.False(ok);
        Assert.Equal(records + 1, rig.Host.World.CheckRing.Count);
        Assert.Equal(CheckCode.InventoryEntryPoolExhausted, rig.Host.World.CheckRing[rig.Host.World.CheckRing.Count - 1].Check);
        Assert.Equal(lastId, rig.Entries.LastId);
        Assert.Equal(liveBlocks, rig.Inventory.MailIds.LiveBlocks);
        Assert.Equal(0, rig.Inventory.BacklogEntries);
        Assert.DoesNotContain(rig.Sink.Last, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);
    }

    [Fact]
    public void AppendBacklog_AnArenaRefusal_IsRefused_WithTheArenaRecord_AndNothingMutated()
    {
        // Arrange: letter maxStack 255 (so only the arena refuses); the smallest arena, its blocks taken until a 255-unit block no longer fits
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: 1024, content: BacklogUnitsTests.LetterMaxStack255Content());
        for (uint first = 100_000; rig.Inventory.MailIds.CanReserve(255); first += 1_000)
        {
            Assert.True(rig.Inventory.MailIds.TryCreate(InvAutoPlaceRig.Ids(first, 255), 0, out _));
        }

        uint lastId = rig.Entries.LastId;
        int liveBlocks = rig.Inventory.MailIds.LiveBlocks;
        long writes = rig.Host.World.CheckRing.Count + rig.Host.World.CheckRing.Suppressed(CheckCode.InventoryArenaExhausted);

        // Act
        bool ok = Append(rig, "letter", Address4, InvAutoPlaceRig.Ids(1, 255));

        // Assert: false, the arena's record (written or rate-suppressed), no entry id, no block, nothing raised
        Assert.False(ok);
        Assert.Equal(writes + 1, rig.Host.World.CheckRing.Count + rig.Host.World.CheckRing.Suppressed(CheckCode.InventoryArenaExhausted));
        Assert.Equal(lastId, rig.Entries.LastId);
        Assert.Equal(liveBlocks, rig.Inventory.MailIds.LiveBlocks);
        Assert.Equal(0, rig.Inventory.BacklogEntries);
        Assert.DoesNotContain(rig.Sink.Last, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);
    }

    // ---------------------------------------------------------------- nothing overtakes the backlog

    [Fact]
    [Trait("Id", "H-R14-4")]
    public void Spawn_WithANonEmptyBacklogAndFreeCells_PlacesNothing_MakesNoProbe_AndTheLetterGoesToTheBacklogTail()
    {
        // Arrange: a full Intake, one backlog letter, then two cells freed in this tick's MailStep before the spawn
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        Assert.True(Append(rig, "letter", Address4, 2_000));
        DefIndex letter = rig.KindIndex("letter");
        long probes = 0;
        SpawnResult r = default;
        bool appended = false;

        // Act: Mail's phase 8 sequence: Spawn, then AppendBacklog of ids[r.Placed..]
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            _ = rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _);
            _ = rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _);
            long before = rig.Counters[CounterId.InventoryPlacementProbes];
            MailId[] ids = IntakeSpawnOutfeedTests.Mail(3_000);
            r = rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address9, 0, ids, MergePolicy.NoMerge);
            probes = rig.Counters[CounterId.InventoryPlacementProbes] - before;
            appended = rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address9, 0, ids.AsSpan(r.Placed)));
            return 0;
        });

        // Assert: nothing placed, no probe, the letter queued behind the older backlog letter
        Assert.Equal(new SpawnResult(0, 1), r);
        Assert.Equal(0, probes);
        Assert.True(appended);
        ushort[] queue = Queue(rig);
        Assert.Equal(new uint[] { 2_000 }, rig.IdsOf(queue[0]));
        Assert.Equal(new uint[] { 3_000 }, rig.IdsOf(queue[^1]));
    }

    [Fact]
    [Trait("Id", "H-R14-4")]
    public void ReturnToIntake_WithANonEmptyBacklogAndFreeCells_AppendsBehindIt_WithNoProbe()
    {
        // Arrange: a full Intake and one backlog letter; one cell freed; a detached letter returning
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        Assert.True(Append(rig, "letter", Address4, 2_000));
        ushort detached = rig.DetachedMail("letter", Address9, new[] { 7u });
        long probes = 0;
        bool ok = false;

        // Act
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            _ = rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _);
            long before = rig.Counters[CounterId.InventoryPlacementProbes];
            ok = rig.Inventory.ReturnToIntake(detached, ActorId.System, InvCause.Expired);
            probes = rig.Counters[CounterId.InventoryPlacementProbes] - before;
            return 0;
        });

        // Assert: true, queued at the tail, still detached, no probe
        Assert.True(ok);
        Assert.Equal(0, probes);
        Assert.Equal(detached, Queue(rig)[^1]);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[detached].ContainerSlot);
    }

    [Fact]
    public void ReturnToIntake_ANonMailEntryTheIntakeRefuses_IsNotQueued()
    {
        // Arrange: a detached plank (the Intake admits mail only)
        var rig = new InvAutoPlaceRig();
        ushort plank = rig.DetachedItem("plank", 3);

        // Act
        bool ok = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ReturnToIntake(plank, ActorId.System, InvCause.Expired));

        // Assert
        Assert.False(ok);
        Assert.Equal(0, rig.Inventory.BacklogEntries);
    }

    // ---------------------------------------------------------------- the drain

    [Fact]
    [Trait("Id", "H-R14-2")]
    public void Drain_IntakeFullExceptTheLastCell_BacklogLargeL1L2_PlacesL1_SkipsTheLarge_OneSeal_ThenAnIdleTickProbesNothing()
    {
        // Arrange: 319 letters fill the Intake except (19,15); the backlog [Large, L1, L2] is appended in one MailStep
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 319, 1_000);
        DefIndex large = rig.KindIndex("large");
        DefIndex letter = rig.KindIndex("letter");
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
            rig.Inventory.AppendBacklog(new BacklogEntry(large, Address4, 0, IntakeSpawnOutfeedTests.Mail(10)))
            & rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address4, 0, IntakeSpawnOutfeedTests.Mail(11)))
            & rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address9, 0, IntakeSpawnOutfeedTests.Mail(12))));
        ushort[] before = Queue(rig);
        uint version = rig.Header(rig.Intake).Version;

        // Act 1: the next tick's phase 2
        (long probes, long visits, EventEntry[] log) = Tick(rig);

        // Assert 1: L1 at (19,15), backlog [Large, L2], 2 units, Intake +1; the Large was skipped in O(1) (area 8 > 1 free cell)
        Assert.Equal(before[1], rig.EntryAt(rig.Intake, 19, 15));
        Assert.Equal(new[] { before[0], before[2] }, Queue(rig));
        Assert.Equal(2, rig.Inventory.BacklogUnits);
        Assert.Equal(version + 1, rig.Header(rig.Intake).Version);
        Assert.Equal(320, probes); // L1's one scan, 320 one-cell anchors; the Large made none
        Assert.Equal(2, visits); // Large, L1; then no free cell
        EventEntry[] inv = log.Where(e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits && (e.Flags & EventFlags.Continuation) == 0).ToArray();
        Assert.Equal(new[] { InvEventKinds.Inserted, InvEventKinds.Version, InvEventKinds.BacklogUnits }, inv.Select(e => e.Kind).ToArray());
        Assert.Equal((byte)InvCause.System, inv[1].PayloadAs<InvVersionPayload>().Cause);

        // Act 2 + Assert 2: the next tick with no Intake change: no probe, no visit, nothing raised
        (long probes2, long visits2, EventEntry[] log2) = Tick(rig);
        Assert.Equal(0, probes2);
        Assert.Equal(0, visits2);
        Assert.DoesNotContain(log2, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);
        Assert.Equal(version + 1, rig.Header(rig.Intake).Version);
    }

    [Fact]
    [Trait("Id", "H-R14-2")]
    public void Drain_100CellsFreedInTheMailStep_100BacklogLetters_Places32_32_32_4_OneSealPerPass_ThenIdle()
    {
        // Arrange: a full Intake and 100 backlog letters (Intake full, so the drain idles)
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        DefIndex letter = rig.KindIndex("letter");
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            for (uint i = 0; i < 100; i++)
            {
                Assert.True(rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address9, 0, new[] { new MailId(5_000 + i) })));
            }

            return 0;
        });
        Assert.Equal(100, rig.Inventory.BacklogEntries);

        // Act: tick T-1's MailStep frees 100 cells (100 TakeUnit, standing in for the M1 sort); then ticks T..T+4
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            for (int i = 0; i < 100; i++)
            {
                Assert.True(rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _));
            }

            return 0;
        });
        var placed = new List<int>();
        var bumps = new List<uint>();
        long lastProbes = 0;
        long lastVisits = 0;
        EventEntry[] firstLog = Array.Empty<EventEntry>();
        for (int t = 0; t < 5; t++)
        {
            int entries = rig.Inventory.BacklogEntries;
            uint version = rig.Header(rig.Intake).Version;
            (lastProbes, lastVisits, EventEntry[] log) = Tick(rig);
            firstLog = t == 0 ? log : firstLog;
            placed.Add(entries - rig.Inventory.BacklogEntries);
            bumps.Add(rig.Header(rig.Intake).Version - version);
        }

        // Assert
        Assert.Equal(new[] { 32, 32, 32, 4, 0 }, placed);
        Assert.Equal(new uint[] { 1, 1, 1, 1, 0 }, bumps);
        EventKind[] kinds = firstLog
            .Where(e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits && (e.Flags & EventFlags.Continuation) == 0)
            .Select(e => e.Kind)
            .ToArray();
        Assert.Equal(Enumerable.Repeat(InvEventKinds.Inserted, 32).Append(InvEventKinds.Version).Append(InvEventKinds.BacklogUnits), kinds);
        Assert.Equal(0, lastProbes);
        Assert.Equal(0, lastVisits);
        Assert.Equal(0, rig.Inventory.BacklogUnits);
    }

    [Fact]
    public void Drain_MultiIdEntries_BacklogUnitsPayloadAndProperties_CountUnitsNotEntries_AfterAPartialAndAFullPass()
    {
        // Arrange: 319 letters leave (19,15) free; the backlog [letter x3 ids, letter x4 ids, Large x1] (8 units, 3 entries), one MailStep
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 319, 1_000);
        DefIndex letter = rig.KindIndex("letter");
        DefIndex large = rig.KindIndex("large");
        Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () =>
            rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address4, 0, IntakeSpawnOutfeedTests.Mail(50, 51, 52)))
            & rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address9, 0, IntakeSpawnOutfeedTests.Mail(60, 61, 62, 63)))
            & rig.Inventory.AppendBacklog(new BacklogEntry(large, Address4, 0, IntakeSpawnOutfeedTests.Mail(70)))));
        ushort[] queue = Queue(rig);
        Assert.Equal(8, rig.Inventory.BacklogUnits);
        Assert.Equal(3, rig.Inventory.BacklogEntries);

        // Act 1: the next pass places the 3-id letter in the one free cell, then stops
        (_, _, EventEntry[] log1) = Tick(rig);

        // Assert 1 (partial pass): one BacklogUnits record after the pass, {units 5, entries 2}, and the properties agree
        Assert.Equal(new[] { queue[1], queue[2] }, Queue(rig));
        Assert.Equal(rig.Intake, rig.Entries[queue[0]].ContainerSlot);
        BacklogUnitsPayload p1 = Assert.Single(log1, e => e.Kind == InvEventKinds.BacklogUnits).PayloadAs<BacklogUnitsPayload>();
        Assert.Equal(5, p1.Units);
        Assert.Equal(2, p1.Entries);
        Assert.Equal(5, rig.Inventory.BacklogUnits);
        Assert.Equal(2, rig.Inventory.BacklogEntries);

        // Act 2: the MailStep frees rows 0 and 1 (40 TakeUnit, oldest first); the next pass places the 4-id letter and the Large
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            for (int i = 0; i < 40; i++)
            {
                Assert.True(rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _));
            }

            return 0;
        });
        (_, _, EventEntry[] log2) = Tick(rig);

        // Assert 2 (the pass that empties the backlog): {0, 0}, and the properties agree
        Assert.Equal(rig.Intake, rig.Entries[queue[1]].ContainerSlot);
        Assert.Equal(4, rig.Entries[queue[1]].Count);
        Assert.Equal(rig.Intake, rig.Entries[queue[2]].ContainerSlot);
        BacklogUnitsPayload p2 = Assert.Single(log2, e => e.Kind == InvEventKinds.BacklogUnits).PayloadAs<BacklogUnitsPayload>();
        Assert.Equal(0, p2.Units);
        Assert.Equal(0, p2.Entries);
        Assert.Equal(0, rig.Inventory.BacklogUnits);
        Assert.Equal(0, rig.Inventory.BacklogEntries);
    }

    [Fact]
    [Trait("Id", "H-R14-3")]
    public void Drain_PlaceCase_AnOlderMailIdFromTheBacklog_GetsArrivalSeq4_OneInsertedNoMerged_AndLeavesAfterAllThreeResidents()
    {
        // Arrange: residents 10, 20, 30 to three addresses (arrivalSeq 1, 2, 3); a backlog letter with the older id 5
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        foreach ((uint id, uint address) in new[] { (10u, 1u), (20u, 2u), (30u, 3u) })
        {
            Assert.Equal(1, IntakeSpawnOutfeedTests.InPhase(
                rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, address, 0, IntakeSpawnOutfeedTests.Mail(id), MergePolicy.NoMerge)).Placed);
        }

        ushort[] residents = { rig.EntryAt(rig.Intake, 0, 0), rig.EntryAt(rig.Intake, 1, 0), rig.EntryAt(rig.Intake, 2, 0) };
        Assert.Equal(4u, rig.Header(rig.Intake).NextArrivalSeq);
        Assert.True(Append(rig, "letter", 4u, 5));
        ushort drained = Queue(rig)[0];
        (EntryId Id, byte Count, uint Seq)[] residentsBefore = residents.Select(r => (rig.Entries[r].Id, rig.Entries[r].Count, rig.Entries[r].ArrivalSeq)).ToArray();

        // Act: the drain (next tick), then four TakeUnit
        (_, _, EventEntry[] log) = Tick(rig);
        uint[] seqs = residents.Select(r => rig.Entries[r].ArrivalSeq).Append(rig.Entries[drained].ArrivalSeq).ToArray();
        (EntryId Id, byte Count, uint Seq)[] residentsAfter = residents.Select(r => (rig.Entries[r].Id, rig.Entries[r].Count, rig.Entries[r].ArrivalSeq)).ToArray();
        EntryRecord placed = rig.Entries[drained];
        uint[] placedIds = rig.IdsOf(drained);
        uint next = rig.Header(rig.Intake).NextArrivalSeq;
        var order = new List<uint>();
        for (int i = 0; i < 4; i++)
        {
            UnitRef u = default;
            Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out u)));
            order.Add(u.Mail.Value);
        }

        // Assert: a new count-1 entry with arrivalSeq 4 (NextArrivalSeq 5), one InvInserted, no InvMerged; it leaves last
        Assert.Equal(rig.Intake, placed.ContainerSlot);
        Assert.Equal(1, placed.Count);
        Assert.DoesNotContain(drained, residents);
        Assert.Equal(new uint[] { 5 }, placedIds);
        Assert.Equal(residentsBefore, residentsAfter); // the three residents: same entries, count 1 each, arrivalSeq 1, 2, 3
        Assert.All(residentsAfter, r => Assert.Equal(1, r.Count));
        Assert.Single(log, e => e.Kind == InvEventKinds.Inserted && (e.Flags & EventFlags.Continuation) == 0);
        Assert.DoesNotContain(log, e => e.Kind == InvEventKinds.Merged);
        Assert.Equal(new uint[] { 10, 20, 30, 5 }, order);
        Assert.Equal(5u, next);
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, seqs);
    }

    [Fact]
    [Trait("Id", "H-R14-3")]
    public void MergeCase_ALetterMergedOntoTheSecondResident_IsRestampedSeq4_TheOtherTwoLeaveAhead_ThenTheStackLowestIdFirst()
    {
        // Arrange: residents 10 (#1, seq 1), 20 (#9, seq 2), 30 (#3, seq 3); a detached letter to #9, id 15
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        foreach ((uint id, uint address) in new[] { (10u, 1u), (20u, Address9), (30u, 3u) })
        {
            _ = IntakeSpawnOutfeedTests.InPhase(
                rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, address, 0, IntakeSpawnOutfeedTests.Mail(id), MergePolicy.NoMerge));
        }

        ushort target = rig.EntryAt(rig.Intake, 1, 0);
        uint[] seqsBefore = { rig.Entries[rig.EntryAt(rig.Intake, 0, 0)].ArrivalSeq, rig.Entries[target].ArrivalSeq, rig.Entries[rig.EntryAt(rig.Intake, 2, 0)].ArrivalSeq };
        ushort source = rig.DetachedMail("letter", Address9, new[] { 15u });

        // Act: MergeInto (the merging path; not a drain path at M-1a), then four TakeUnit
        Reply merged = rig.Step(
            (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.MergeInto(ref p, source, target, 1));
        uint seqAfter = rig.Entries[target].ArrivalSeq;
        uint[] othersAfter = { rig.Entries[rig.EntryAt(rig.Intake, 0, 0)].ArrivalSeq, rig.Entries[rig.EntryAt(rig.Intake, 2, 0)].ArrivalSeq };
        var order = new List<uint>();
        for (int i = 0; i < 4; i++)
        {
            UnitRef u = default;
            Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out u)));
            order.Add(u.Mail.Value);
        }

        // Assert: seq 4 (NextArrivalSeq 5); residents 1 and 3 leave ahead; then the merged stack, lowest mailId first; no seq decreased
        Assert.True(merged.IsOk);
        Assert.Equal(new uint[] { 1, 2, 3 }, seqsBefore);
        Assert.Equal(4u, seqAfter);
        Assert.Equal(new uint[] { 1, 3 }, othersAfter); // the other two residents: no arrivalSeq decreased (nor changed)
        Assert.Equal(5u, rig.Header(rig.Intake).NextArrivalSeq);
        Assert.Equal(new uint[] { 10, 30, 15, 20 }, order);
    }

    // ---------------------------------------------------------------- H-PERF-6c and idle passes

    [Fact]
    [Trait("Id", "H-PERF-6c")]
    public void Drain_A2800EntryPass_IntoScattered1x1Holes_StaysWithin8500ProbesMergesAndVisits_AndEntriesWalkedPlus1Visits()
    {
        // Arrange: a full Intake with 32 scattered one-cell holes (every 10th cell); a backlog of 2,768 Large (area 8) then 32 letters
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        for (int i = 0; i < 32; i++)
        {
            ushort entry = rig.EntryAt(rig.Intake, (i * 10) % 20, (i * 10) / 20);
            Reply r = rig.Step(
                (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => rig.Inventory.Mutator.Remove(ref p, entry));
            Assert.True(r.IsOk);
        }

        DefIndex large = rig.KindIndex("large");
        DefIndex letter = rig.KindIndex("letter");
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            for (uint i = 0; i < 2_800; i++)
            {
                DefIndex kind = i < 2_768 ? large : letter;
                Assert.True(rig.Inventory.AppendBacklog(new BacklogEntry(kind, Address4, 0, new[] { new MailId(10_000 + i) })));
            }

            return 0;
        });

        // Act: the next tick's pass (the appends changed the gating memo)
        long mergeBefore = rig.Counters[CounterId.InventoryMergeCandidates];
        (long probes, long visits, _) = Tick(rig);
        long merges = rig.Counters[CounterId.InventoryMergeCandidates] - mergeBefore;

        // Assert: 32 letters placed; merge candidates 0 under NoMerge. The H-PERF-6c gate (ADR-0009 l.57, l.232: every cell probe or
        // candidate visit, a backlog node included): placement probes (43, per cell) + merge candidates + backlog visits (50) <= 8,500
        // (measured 4,358 + 0 + 2,800). Second gate: visits <= entries walked + 1.
        Assert.Equal(2_768, rig.Inventory.BacklogEntries);
        Assert.Equal(0, rig.Header(rig.Intake).FreeCells);
        Assert.Equal(0, merges);
        Assert.InRange(probes + merges + visits, 1, 8_500);
        Assert.InRange(visits, 1, 2_800 + 1);
        Assert.Equal(2_800, visits); // pins the visit count (advisory fix, Mail Review PR #35 r2 A4)
    }

    [Fact]
    [Trait("Id", "H-PERF-6c")]
    public void Drain_AnEmptyBacklogOrAFullIntake_MakesNoProbeAndNoVisit_AndRaisesNothing()
    {
        // Arrange: an empty backlog with free cells; then a full Intake with a backlog
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 10, 1_000);

        // Act + Assert 1: empty backlog
        (long probes, long visits, EventEntry[] log) = Tick(rig);
        Assert.Equal(0, probes);
        Assert.Equal(0, visits);
        Assert.DoesNotContain(log, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);

        // Act + Assert 2: full Intake, non-empty backlog (the append changed the memo, yet no free cell)
        IntakeSpawnOutfeedTests.FillIntake(rig, 310, 2_000);
        Assert.True(Append(rig, "letter", Address9, 9_000));
        uint version = rig.Header(rig.Intake).Version;
        (probes, visits, log) = Tick(rig);
        Assert.Equal(0, probes);
        Assert.Equal(0, visits);
        Assert.Equal(version, rig.Header(rig.Intake).Version);
        Assert.DoesNotContain(log, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);
    }

    [Fact]
    public void Drain_APassThatRunsButPlacesNothing_RaisesNothing_AndBumpsNothing()
    {
        // Arrange: a full Intake with two non-adjacent one-cell holes, (0,0) and (10,0); one Small (1x2, area 2) appended
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        foreach (int x in new[] { 0, 10 })
        {
            ushort entry = rig.EntryAt(rig.Intake, x, 0);
            Reply r = rig.Step(
                (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => rig.Inventory.Mutator.Remove(ref p, entry));
            Assert.True(r.IsOk);
        }

        Assert.True(Append(rig, "small", Address9, 9_000));
        uint version = rig.Header(rig.Intake).Version;

        // Act: the next tick's pass runs (the append changed the memo), plans the Small, finds no fit
        (long probes, long visits, EventEntry[] log) = Tick(rig);

        // Assert: the pass ran (a visit and probes) but placed nothing: no record, no seal, no version change
        Assert.Equal(1, visits);
        Assert.True(probes > 0);
        Assert.Equal(1, rig.Inventory.BacklogEntries);
        Assert.Equal(version, rig.Header(rig.Intake).Version);
        Assert.DoesNotContain(log, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);
    }

    [Theory]
    [Trait("Id", "H-PERF-6c")]
    [InlineData("take-unit")]
    [InlineData("append")]
    public void Drain_GatingMemo_AnUnplaceableEntryWithFreeCells_IdlesOnTheNextTick_AndReRunsAfterAnIntakeChangeOrAnAppend(string change)
    {
        // Arrange: a full Intake with two non-adjacent one-cell holes, (0,0) and (10,0); one Small (1x2) appended; the first pass runs
        // (it plans the Small, finds no fit, places nothing)
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        foreach (int x in new[] { 0, 10 })
        {
            ushort entry = rig.EntryAt(rig.Intake, x, 0);
            Reply r = rig.Step(
                (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => rig.Inventory.Mutator.Remove(ref p, entry));
            Assert.True(r.IsOk);
        }

        Assert.True(Append(rig, "small", Address9, 9_000));
        (long probes1, long visits1, _) = Tick(rig);
        Assert.Equal(1, visits1);
        Assert.True(probes1 > 0);

        // Act 1: the next tick: free cells, a queued entry, but the Intake unchanged and no append since that pass
        (long probes2, long visits2, EventEntry[] log2) = Tick(rig);

        // Assert 1: the memo closes the pass: no probe, no visit, nothing raised
        Assert.Equal(0, probes2);
        Assert.Equal(0, visits2);
        Assert.DoesNotContain(log2, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);

        // Act 2: change the Intake (a TakeUnit seals it) or append (AppendSeq moves), in one MailStep; then the next tick
        if (change == "take-unit")
        {
            Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _)));
        }
        else
        {
            Assert.True(Append(rig, "small", Address9, 9_001));
        }

        (long probes3, long visits3, _) = Tick(rig);

        // Assert 2: the pass re-runs: an Intake change re-plans with probes (the no-fit facts were at the old version); an append walks
        // both queued entries (the unchanged Intake's no-fit fact for the Small shape skips their scans)
        if (change == "take-unit")
        {
            Assert.InRange(visits3, 1, 2);
            Assert.True(probes3 > 0);
        }
        else
        {
            Assert.Equal(2, visits3);
        }
    }

    // ---------------------------------------------------------------- state and determinism

    [Fact]
    public void Dump_TheBacklogRingFollowsTheWorldItemsBlock_CountThenIdsInQueueOrder_IdenticalAcrossIdenticalWorlds()
    {
        // Arrange + Act: two identically driven worlds (a full Intake, three appends, a few ticks)
        InvAutoPlaceRig a = Drive();
        InvAutoPlaceRig b = Drive();
        byte[] sa = Section(DumpHash.Bytes(a.Host.World), SectionIds.Inventory);
        byte[] sb = Section(DumpHash.Bytes(b.Host.World), SectionIds.Inventory);

        // Assert: byte-identical; the 021 block (before story 017's appended block) ends with u32 3 and the three entry ids in queue order
        Assert.Equal(sa, sb);
        ushort[] queue = Queue(a);
        Assert.Equal(3, queue.Length);
        int at = sa.Length - InventoryStateContributorTests.TailBytes(a.Inventory) - 16;
        Assert.Equal(3u, BitConverter.ToUInt32(sa, at));
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(a.Entries[queue[i]].Id.Value, BitConverter.ToUInt32(sa, at + 4 + (4 * i)));
        }

        static InvAutoPlaceRig Drive()
        {
            var rig = new InvAutoPlaceRig();
            IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
            Assert.True(Append(rig, "letter", Address4, 50));
            Assert.True(Append(rig, "letter", Address9, 60, 61));
            Assert.True(Append(rig, "small", Address4, 70));
            _ = Tick(rig);
            _ = Tick(rig);
            return rig;
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Ticks once; returns the placement probes (43), the backlog visits (50) and the tick's log.</summary>
    internal static (long Probes, long Visits, EventEntry[] Log) Tick(InvAutoPlaceRig rig)
    {
        long probes = rig.Counters[CounterId.InventoryPlacementProbes];
        long visits = rig.Counters[CounterId.InventoryBacklogVisits];
        rig.Host.World.Tick();
        return (rig.Counters[CounterId.InventoryPlacementProbes] - probes, rig.Counters[CounterId.InventoryBacklogVisits] - visits, rig.Sink.Last);
    }

    /// <summary><see cref="IInventoryApi.AppendBacklog"/> of one entry of <paramref name="kind"/> in its own tick's <c>MailStep</c>.</summary>
    internal static bool Append(InvAutoPlaceRig rig, string kind, uint address, params uint[] ids)
    {
        DefIndex k = rig.KindIndex(kind);
        MailId[] mail = IntakeSpawnOutfeedTests.Mail(ids);
        return IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.AppendBacklog(new BacklogEntry(k, address, 0, mail)));
    }

    /// <summary>The backlog's entry slots in queue order.</summary>
    internal static ushort[] Queue(InvAutoPlaceRig rig)
    {
        var slots = new List<ushort>();
        for (ushort s = rig.Inventory.Backlog.Head; s != BacklogRing.None; s = rig.Inventory.Backlog.Next(s))
        {
            slots.Add(s);
        }

        return slots.ToArray();
    }
}
