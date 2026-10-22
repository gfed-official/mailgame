using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Backlog;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

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
        bool ok = Append(rig, "small", Address9, 30, 31, 32);
        EventEntry[] log = rig.Sink.Last;

        // Assert: one new detached count-3 entry with the StackKey and ids, at the tail; units 4, entries 2; one BacklogUnits record
        Assert.True(ok);
        Assert.Equal(2, rig.Inventory.BacklogEntries);
        Assert.Equal(4, rig.Inventory.BacklogUnits);
        ushort tail = Queue(rig)[1];
        Assert.Equal(lastId + 1, rig.Entries[tail].Id.Value);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[tail].ContainerSlot);
        Assert.Equal(3, rig.Entries[tail].Count);
        Assert.Equal(rig.KindIndex("small").Value, rig.Entries[tail].MailKind);
        Assert.Equal(Address9, rig.Entries[tail].AddressId);
        Assert.Equal(1, rig.Entries[tail].W);
        Assert.Equal(2, rig.Entries[tail].H);
        Assert.Equal(new uint[] { 30, 31, 32 }, rig.IdsOf(tail));
        Assert.Equal(version, rig.Header(rig.Intake).Version);
        EventEntry record = Assert.Single(log, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);
        Assert.Equal(InvEventKinds.BacklogUnits, record.Kind);
        Assert.Equal(4, record.PayloadLength);
        BacklogUnitsPayload p = record.PayloadAs<BacklogUnitsPayload>();
        Assert.Equal(4, p.Units);
        Assert.Equal(2, p.Entries);
    }

    public static TheoryData<string> Malformed => new() { "none-kind", "unknown-kind", "no-ids", "256-ids", "not-ascending", "duplicate", "none-id" };

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
            _ => (letter, new[] { MailId.None, new MailId(2) }),
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
        // Arrange: the smallest arena, its blocks taken until a 255-unit block no longer fits
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: 1024);
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

    // ---------------------------------------------------------------- helpers

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
