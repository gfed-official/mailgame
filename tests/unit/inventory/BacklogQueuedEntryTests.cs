using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Backlog;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Intake;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/021 review round 1, B1 (ADR-0009 l.169-170: the Backlog is its own ledger location): a still-queued backlog
/// entry is live and in no container and no WorldItem, yet it is not detached. <see cref="IInventoryApi.TryCatch"/> answers
/// <c>bad_request</c>, <see cref="IInventoryApi.CreateWorldItem"/> answers <see cref="EntityId.None"/> and <c>ReturnToIntake</c>
/// answers false, each with nothing mutated and nothing raised; the ring stays intact and the next drain places the entry normally.
/// </summary>
public sealed class BacklogQueuedEntryTests
{
    private const uint Address4 = 4u;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryCatch_OfAQueuedBacklogEntry_IsBadRequest_MutatesNothing_AndTheNextDrainPlacesItIntoTheIntake(bool hotbarHoldsAMergeTarget)
    {
        // Arrange: a full Intake, one queued letter (id 9000 to #4); optionally a letter to #4 in hotbar A that a catch would merge onto
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        if (hotbarHoldsAMergeTarget)
        {
            rig.CaptureEvents = true;
            _ = rig.PlaceMail(rig.HotbarA, "letter", 0, 0, Address4, new[] { 9_500u });
        }

        Assert.True(IntakeDrainTests.Append(rig, "letter", Address4, 9_000));
        ushort queued = rig.Inventory.Backlog.Head;
        EntryId id = rig.Entries[queued].Id;
        List<long> before = State(rig);
        long probes = rig.Counters[CounterId.InventoryPlacementProbes];

        // Act
        CatchResult r = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TryCatch(PlayerSlot.FromValue(0), new FlightPayload(id, default, false)));
        EventEntry[] log = rig.Sink.Last;

        // Assert 1: bad_request {0, 0}; no probe; nothing changed (containers, entries, arena, ring, WorldItems) and nothing raised
        Assert.Equal(new CatchResult(0, 0, (InvReason)InvReasons.BadRequest), r);
        Assert.Equal(probes, rig.Counters[CounterId.InventoryPlacementProbes]);
        Assert.Equal(before, State(rig));
        Assert.DoesNotContain(log, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);

        // Act 2 + Assert 2: one Intake cell freed; the next drain places the same entry into the Intake, the hotbar untouched
        uint hotbarVersion = rig.Header(rig.HotbarA).Version;
        Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _)));
        _ = IntakeDrainTests.Tick(rig);
        Assert.Equal(rig.Intake, rig.Entries[queued].ContainerSlot);
        Assert.Equal(id, rig.Entries[queued].Id);
        Assert.Equal(new uint[] { 9_000 }, rig.IdsOf(queued));
        Assert.Equal(0, rig.Inventory.BacklogEntries);
        Assert.Equal(hotbarVersion, rig.Header(rig.HotbarA).Version);
    }

    [Fact]
    public void CreateWorldItem_OfAQueuedBacklogEntry_ReturnsNone_MutatesNothing_AndTheNextDrainPlacesItIntoTheIntake()
    {
        // Arrange: a full Intake and one queued letter
        var w = new WorldItemRig();
        IntakeSpawnOutfeedTests.FillIntake(w.R, 320, 1_000);
        Assert.True(IntakeDrainTests.Append(w.R, "letter", Address4, 9_000));
        ushort queued = w.Inv.Backlog.Head;
        EntryId id = w.R.Entries[queued].Id;
        List<long> before = State(w.R);
        uint tick = w.Now;

        // Act
        EntityId created = w.At(tick, () => w.Inv.CreateWorldItem(id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, tick));
        EventEntry[] log = w.R.Sink.Last;

        // Assert 1: None; no WorldItem (count, WorldSeq), the entry unowned and still queued, nothing raised
        Assert.Equal(EntityId.None, created);
        Assert.False(w.World.OwnsEntry(queued));
        Assert.Equal(before, State(w.R));
        Assert.DoesNotContain(log, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);

        // Act 2 + Assert 2: one Intake cell freed; the next drain places it
        Assert.True(IntakeSpawnOutfeedTests.InPhase(w.R, () => w.Inv.TakeUnit(w.Inv.IntakeContainer, Selector.Fifo, w.R.PlayerA, out _)));
        _ = IntakeDrainTests.Tick(w.R);
        Assert.Equal(w.R.Intake, w.R.Entries[queued].ContainerSlot);
        Assert.Equal(0, w.Inv.BacklogEntries);
        Assert.Equal(0, w.World.LiveCount);
    }

    [Fact]
    public void ReturnToIntake_OfAQueuedBacklogEntry_ReturnsFalse_MutatesNothing_AndTheNextDrainPlacesItIntoTheIntake()
    {
        // Arrange: a full Intake and two queued letters (9000, then 9001)
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        Assert.True(IntakeDrainTests.Append(rig, "letter", Address4, 9_000));
        Assert.True(IntakeDrainTests.Append(rig, "letter", Address4, 9_001));
        ushort[] queue = IntakeDrainTests.Queue(rig);
        List<long> before = State(rig);

        // Act: return each queued entry (no full-ring assert may trip: the entry is refused before the append)
        bool head = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ReturnToIntake(queue[0], ActorId.System, InvCause.Expired));
        EventEntry[] log = rig.Sink.Last;
        bool tail = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ReturnToIntake(queue[1], ActorId.System, InvCause.Expired));

        // Assert 1: false twice; the queue order, counts and everything else unchanged; nothing raised
        Assert.False(head);
        Assert.False(tail);
        Assert.Equal(before, State(rig));
        Assert.DoesNotContain(log, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);
        Assert.DoesNotContain(rig.Sink.Last, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);

        // Act 2 + Assert 2: two cells freed; the next drain places both, in queue order
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
            rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _)
            & rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out _));
        _ = IntakeDrainTests.Tick(rig);
        Assert.Equal(0, rig.Inventory.BacklogEntries);
        Assert.Equal(rig.Intake, rig.Entries[queue[0]].ContainerSlot);
        Assert.Equal(rig.Intake, rig.Entries[queue[1]].ContainerSlot);
        Assert.True(rig.Entries[queue[0]].ArrivalSeq < rig.Entries[queue[1]].ArrivalSeq);
    }

    /// <summary>
    /// Every container header (version, counts, free cells, seq, replace version, stat epoch, outfeed memo), every entry-pool record,
    /// the arena's counts, the backlog (entries, units, append seq, queue order) and the WorldItems (count, WorldSeq), as one list.
    /// </summary>
    internal static List<long> State(InvAutoPlaceRig rig)
    {
        var values = new List<long>();
        ContainerTable containers = rig.Inventory.Containers;
        for (int s = 0; s < containers.LiveCount; s++)
        {
            ContainerHeader h = containers[(ushort)s];
            values.AddRange(new long[] { h.Version, h.EntryCount, h.FreeCells, h.NextArrivalSeq, h.LastReplaceVersion, h.StatEpoch, h.OutfeedHead, h.OutfeedTail });
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
        values.Add(rig.Inventory.BacklogEntries);
        values.Add(rig.Inventory.BacklogUnits);
        values.Add(rig.Inventory.Backlog.AppendSeq);
        values.AddRange(IntakeDrainTests.Queue(rig).Select(s => (long)s));
        values.Add(rig.Inventory.WorldItems.LiveCount);
        values.Add(rig.Inventory.WorldItems.WorldSeq);
        return values;
    }
}
