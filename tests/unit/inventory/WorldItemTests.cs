using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/013 (GDD R12, R7, D6; ADR-0009 section 5; ADR-0004 <c>EntityClass.WorldItem</c>; ADR-0003 phase 1
/// <c>ItemsExpire</c>): <see cref="IInventoryApi.CreateWorldItem"/>, absolute-tick expiry through the per-class FIFOs, the water float,
/// the cap eviction, the tombstones and the interim full-Intake rule (until story 021). Creations run inside a real tick through the
/// rig's <c>MailStep</c> hook (after <c>ItemsExpire</c>, as Drop and Throwing's landing will); expiry runs in the real phase 1. Every
/// AC case is a test, named after its GDD id.
/// </summary>
public class WorldItemTests
{
    private const uint Address = 4u;

    // ---------------------------------------------------------------- H-R12-2

    [Fact]
    [Trait("Id", "H-R12-2")]
    public void HR122_Letter500DroppedAt1000_At10000IsGone_AndIsInTheIntakeAtTheBackOfTheOutfeedQueue()
    {
        // Arrange: the Intake holds letters 1-3; letter 500 is dropped at tick 1000
        var w = new WorldItemRig();
        w.SpawnIntake(1, 2, 3);
        ushort letter = w.R.DetachedMail("letter", Address, new[] { 500u });
        EntityId item = w.Create(1000, letter);
        uint lastSeq = w.MaxIntakeArrivalSeq();

        // Act
        w.AdvanceTo(10_000);
        bool aliveAt9999 = w.World.Table.TryGetSlot(item, out _);
        EventEntry[] log = w.RunTick(10_000);

        // Assert: alive through 9999; at 10000 the WorldItem is gone and entry 500 is in po_intake with the highest arrivalSeq
        Assert.True(aliveAt9999);
        Assert.False(w.World.Table.TryGetSlot(item, out _));
        Assert.Equal(w.R.Intake, w.R.Entries[letter].ContainerSlot);
        Assert.Equal(new[] { 500u }, w.R.IdsOf(letter));
        Assert.True(w.R.Entries[letter].ArrivalSeq > lastSeq);
        Assert.True(w.Inv.PeekOutfeed(w.Inv.IntakeContainer, Selector.Fifo, out UnitRef head));
        Assert.Equal(1u, head.Mail.Value);
        WorldItemDespawnedPayload gone = Assert.Single(Heads(log, InvEventKinds.WorldItemDespawned)).PayloadAs<WorldItemDespawnedPayload>();
        Assert.Equal(item.Value, gone.WorldItemId);
        Assert.Equal((byte)InvCause.Expired, gone.Cause);
        Assert.Equal(ActorId.System.Value, gone.ByActorId);
        InvVersionPayload seal = Assert.Single(Heads(log, InvEventKinds.Version)).PayloadAs<InvVersionPayload>();
        Assert.Equal((byte)InvCause.Expired, seal.Cause);
        Assert.Empty(Heads(log, InvEventKinds.ItemsLost));
    }

    [Fact]
    [Trait("Id", "H-R12-2")]
    public void HR122_Plank5DroppedAt1000_At10000IsGone_ItemsLostPlank5_AndInNoContainer()
    {
        // Arrange
        var w = new WorldItemRig();
        ushort plank = w.R.DetachedItem("plank", 5);
        EntryId plankId = w.R.Entries[plank].Id;
        EntityId item = w.Create(1000, plank);

        // Act
        w.AdvanceTo(10_000);
        bool aliveAt9999 = w.World.Table.TryGetSlot(item, out _);
        EventEntry[] log = w.RunTick(10_000);

        // Assert: gone, ItemsLost {plank, 5}, the entry no longer exists anywhere (destroyed)
        Assert.True(aliveAt9999);
        Assert.False(w.World.Table.TryGetSlot(item, out _));
        ItemsLostPayload lost = Assert.Single(Heads(log, InvEventKinds.ItemsLost)).PayloadAs<ItemsLostPayload>();
        Assert.Equal(w.R.ItemIndex("plank").Value, lost.ItemId);
        Assert.Equal(5, lost.Count);
        Assert.Equal(item.Value, lost.WorldItemId);
        Assert.False(w.R.Entries.TryGetSlot(plankId, out _));
        Assert.Equal((byte)InvCause.Destroyed, Assert.Single(Heads(log, InvEventKinds.WorldItemDespawned)).PayloadAs<WorldItemDespawnedPayload>().Cause);
        Assert.Empty(Heads(log, InvEventKinds.Version)); // no container changed
    }

    [Fact]
    [Trait("Id", "H-R12-2")]
    public void HR122_Letter600LandingOnWaterAt2000_WithIntakeHolding601To610_FloatsUntil2300_ThenEntersAfter610_PeekStill601()
    {
        // Arrange: the Intake holds 601-610, one per entry; letter 600 lands on water at tick 2000
        var w = new WorldItemRig();
        w.SpawnIntake(Enumerable.Range(601, 10).Select(i => (uint)i).ToArray());
        uint seq610 = w.R.Entries[w.IntakeEntryHolding(610)].ArrivalSeq;
        ushort letter = w.R.DetachedMail("letter", Address, new[] { 600u });
        EntityId item = w.Create(2000, letter, WorldItemOrigin.Thrown, onWater: true);

        // Act
        w.AdvanceTo(2300);
        bool floatingAt2299 = w.World.Table.TryGetSlot(item, out ushort slot) && w.World.Table[slot].OnWater;
        EventEntry[] log = w.RunTick(2300);

        // Assert
        Assert.True(floatingAt2299);
        Assert.False(w.World.Table.TryGetSlot(item, out _));
        Assert.Equal(w.R.Intake, w.R.Entries[letter].ContainerSlot);
        Assert.True(w.R.Entries[letter].ArrivalSeq > seq610);
        Assert.True(w.Inv.PeekOutfeed(w.Inv.IntakeContainer, Selector.Fifo, out UnitRef head));
        Assert.Equal(601u, head.Mail.Value);
        Assert.Equal((byte)InvCause.Expired, Assert.Single(Heads(log, InvEventKinds.WorldItemDespawned)).PayloadAs<WorldItemDespawnedPayload>().Cause);
    }

    [Fact]
    [Trait("Id", "H-R12-2")]
    public void HR122_PlankLandingOnWater_IsDestroyedAt2300WithItemsLost()
    {
        // Arrange
        var w = new WorldItemRig();
        ushort plank = w.R.DetachedItem("plank", 3);
        EntityId item = w.Create(2000, plank, WorldItemOrigin.Thrown, onWater: true);

        // Act
        w.AdvanceTo(2300);
        bool aliveAt2299 = w.World.Table.TryGetSlot(item, out _);
        EventEntry[] log = w.RunTick(2300);

        // Assert
        Assert.True(aliveAt2299);
        Assert.False(w.World.Table.TryGetSlot(item, out _));
        ItemsLostPayload lost = Assert.Single(Heads(log, InvEventKinds.ItemsLost)).PayloadAs<ItemsLostPayload>();
        Assert.Equal(3, lost.Count);
        Assert.Equal(w.R.ItemIndex("plank").Value, lost.ItemId);
    }

    // ---------------------------------------------------------------- H-D6-1

    [Fact]
    [Trait("Id", "H-D6-1")]
    public void HD61_DroppedAt1000_Expires10000_ThrownAt1000LandingAt1045_Expires10045()
    {
        // Arrange
        var w = new WorldItemRig();
        ushort dropped = w.R.DetachedMail("letter", Address, new[] { 70u });
        ushort thrown = w.R.DetachedMail("letter", Address, new[] { 71u });

        // Act: the drop at 1000; the throw left the hand at 1000 and Throwing creates the WorldItem when it lands, at 1045
        EntityId a = w.Create(1000, dropped);
        EntityId b = w.Create(1045, thrown, WorldItemOrigin.Thrown);

        // Assert
        Assert.True(w.World.Table.TryGetSlot(a, out ushort sa));
        Assert.True(w.World.Table.TryGetSlot(b, out ushort sb));
        Assert.Equal(10_000u, w.World.Table[sa].ExpiresTick);
        Assert.Equal(10_045u, w.World.Table[sb].ExpiresTick);
        Assert.Equal(1045u, w.World.Table[sb].CreatedTick);
        Assert.Equal(9000u, w.World.DurationTicks(ExpiryFifo.MailWorldItem));
        Assert.Equal(300u, w.World.DurationTicks(ExpiryFifo.WaterFloatMail));
        Assert.Equal(900u, w.World.DurationTicks(ExpiryFifo.Bag));
        w.AdvanceTo(10_001);
        Assert.False(w.World.Table.TryGetSlot(a, out _));
        Assert.True(w.World.Table.TryGetSlot(b, out _));
        w.AdvanceTo(10_046);
        Assert.False(w.World.Table.TryGetSlot(b, out _));
    }

    [Fact]
    [Trait("Id", "H-D6-1")]
    public void HD61_SoloPauseOf300TicksOfWallTime_DoesNotMoveExpiry_TheItemExpiresOnSimTick10000()
    {
        // Arrange: a host-style loop: a TickDriver at 3000 counts/s (100 counts = one 30 Hz tick), one tick released per frame
        var w = new WorldItemRig();
        ushort letter = w.R.DetachedMail("letter", Address, new[] { 80u });
        EntityId item = w.Create(1000, letter);
        var driver = new TickDriver(3_000, 8);
        long clock = 0;
        driver.Start(clock);
        uint expiredOnTick = 0;
        int frames = 0;
        int pausedFrames = 0;
        int ticksReleasedWhilePaused = 0;
        bool paused = false;

        // Act: one frame = 100 counts of wall time; at sim tick 5000 the solo game pauses for 300 frames (10 s), then resumes
        while (expiredOnTick == 0 && frames < 20_000)
        {
            frames++;
            clock += 100;
            if (!paused && pausedFrames == 0 && w.Now == 5_000)
            {
                driver.Pause();
                paused = true;
            }

            if (paused)
            {
                ticksReleasedWhilePaused += driver.Advance(clock);
                if (++pausedFrames == 300)
                {
                    driver.Resume(clock);
                    paused = false;
                }

                continue;
            }

            int n = driver.Advance(clock);
            for (int i = 0; i < n && expiredOnTick == 0; i++)
            {
                uint running = w.Now;
                w.R.Host.World.Tick();
                if (!w.World.Table.TryGetSlot(item, out _))
                {
                    expiredOnTick = running;
                }
            }
        }

        // Assert: no tick ran during the 300 paused frames, and the item still expired on sim tick 10000
        Assert.Equal(300, pausedFrames);
        Assert.Equal(0, ticksReleasedWhilePaused);
        Assert.Equal(10_000u, expiredOnTick);
        Assert.Equal(w.R.Intake, w.R.Entries[letter].ContainerSlot);
    }


    // ---------------------------------------------------------------- CreateWorldItem contract

    [Fact]
    public void CreateWorldItem_RaisesOneReplicatedSpawnedRecordToConnectedSlots_WithTheNextWorldSeq_AndAWorldItemClassId()
    {
        // Arrange
        var w = new WorldItemRig();
        ushort a = w.R.DetachedMail("letter", Address, new[] { 90u });
        ushort b = w.R.DetachedItem("plank", 2);

        // Act
        EntityId ia = w.Create(100, a);
        EventEntry[] logA = w.R.Sink.Last;
        EntityId ib = w.Create(101, b, WorldItemOrigin.Thrown, onWater: true);
        EventEntry[] logB = w.R.Sink.Last;

        // Assert
        Assert.Equal(EntityClass.WorldItem, ia.Class);
        Assert.True(ib.Value > ia.Value);
        EventEntry ea = Assert.Single(Heads(logA, InvEventKinds.WorldItemSpawned));
        Assert.Equal(ReplicationClass.Replicated, ea.Class);
        Assert.Equal(w.R.Host.Services.Slots.ConnectedMask, ea.Audience);
        Assert.NotEqual(0, ea.Audience);
        WorldItemSpawnedPayload pa = ea.PayloadAs<WorldItemSpawnedPayload>();
        WorldItemSpawnedPayload pb = Assert.Single(Heads(logB, InvEventKinds.WorldItemSpawned)).PayloadAs<WorldItemSpawnedPayload>();
        Assert.Equal(ia.Value, pa.WorldItemId);
        Assert.Equal(1u, pa.WorldSeq);
        Assert.Equal(2u, pb.WorldSeq);
        Assert.Equal(2u, w.World.WorldSeq);
        Assert.Equal(w.R.Entries[a].Id.Value, pa.EntryId);
        Assert.Equal((WorldItemRig.Pos.X, WorldItemRig.Pos.Y, WorldItemRig.Pos.Z), (pa.X, pa.Y, pa.Z));
        Assert.Equal(100u, pa.CreatedTick);
        Assert.Equal(9_100u, pa.ExpiresTick);
        Assert.Equal(WorldItemSpawnedPayload.IsMailBit, pa.Bits);
        Assert.Equal(WorldItemSpawnedPayload.OnWaterBit, pb.Bits);
        Assert.Equal(401u, pb.ExpiresTick);
        Assert.Equal(2, pb.Count);
        Assert.Equal((byte)WorldItemOrigin.Thrown, pb.Origin);
        Assert.Empty(Heads(logA, InvEventKinds.Version));
        Assert.Equal(2, w.World.LiveCount);
    }

    [Fact]
    public void CreateWorldItem_InvalidCalls_ReturnNone_AndChangeNothing()
    {
        // Arrange
        var w = new WorldItemRig();
        ushort letter = w.R.DetachedMail("letter", Address, new[] { 95u });
        ushort placed = w.R.PlaceMail(w.R.InventoryA, "letter", 0, 0, Address, new[] { 96u });
        EntryId id = w.R.Entries[letter].Id;
        w.AdvanceTo(200);
        long outOfPhaseBefore = w.R.Counters[CounterId.MutatingApiOutOfPhase];

        // Act
        EntityId spill = w.At(200, () => w.Inv.CreateWorldItem(id, WorldItemRig.Pos, WorldItemOrigin.Spill, false, 200));
        EntityId leak = w.At(201, () => w.Inv.CreateWorldItem(id, WorldItemRig.Pos, WorldItemOrigin.BeltLeak, false, 201));
        EntityId future = w.At(202, () => w.Inv.CreateWorldItem(id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, 203));
        EntityId inContainer = w.At(203, () => w.Inv.CreateWorldItem(w.R.Entries[placed].Id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, 203));
        EntityId unknown = w.At(204, () => w.Inv.CreateWorldItem(EntryId.Real(99_999), WorldItemRig.Pos, WorldItemOrigin.Dropped, false, 204));
        EntityId ok = w.At(205, () => w.Inv.CreateWorldItem(id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, 205));
        EntityId twice = w.At(206, () => w.Inv.CreateWorldItem(id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, 206));
        ushort other = w.R.DetachedMail("letter", Address, new[] { 97u });
        EntityId backwards = w.At(207, () => w.Inv.CreateWorldItem(w.R.Entries[other].Id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, 204));
        uint seqBefore = w.World.WorldSeq;
        EntityId between = SimAssertOrRelease(() => w.Inv.CreateWorldItem(w.R.Entries[other].Id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, 207));

        // Assert
        Assert.Equal(EntityId.None, spill);
        Assert.Equal(EntityId.None, leak);
        Assert.Equal(EntityId.None, future);
        Assert.Equal(EntityId.None, inContainer);
        Assert.Equal(EntityId.None, unknown);
        Assert.NotEqual(EntityId.None, ok);
        Assert.Equal(EntityId.None, twice);
        Assert.Equal(EntityId.None, backwards);
        Assert.Equal(EntityId.None, between);
        Assert.Equal(1, w.World.LiveCount);
        Assert.Equal(seqBefore, w.World.WorldSeq);
        Assert.Equal(outOfPhaseBefore + 1, w.R.Counters[CounterId.MutatingApiOutOfPhase]);
    }

    [Fact]
    [Trait("Id", "H-R12-4")]
    public void LootLetter_IsOnNoFifo_AndStillExistsAt10000()
    {
        // Arrange
        var w = new WorldItemRig();
        ushort letter = w.R.DetachedMail("letter", Address, new[] { 99u });
        EntityId item = w.Create(1000, letter, WorldItemOrigin.Loot);

        // Act
        w.AdvanceTo(10_001);

        // Assert
        Assert.True(w.World.Table.TryGetSlot(item, out ushort slot));
        Assert.Equal(ExpiryFifo.None, w.World.Table[slot].Fifo);
        Assert.Equal(0u, w.World.Table[slot].ExpiresTick);
        for (int f = 0; f < ExpiryFifos.Count; f++)
        {
            Assert.Equal(0, w.World.Fifos.NodeCount((ExpiryFifo)f));
        }
    }

    [Fact]
    public void Expiry_DueItemsOfSeveralFifos_LeaveInAscendingExpiresTickThenIdOrder_OneCallScopeEach()
    {
        // Arrange: a ground plank created at 1000 (expires 10000) and, at 9700, a water letter and a water plank (both expire 10000)
        var w = new WorldItemRig();
        EntityId ground = w.Create(1000, w.R.DetachedItem("plank", 1));
        ushort l = w.R.DetachedMail("letter", Address, new[] { 300u });
        ushort p = w.R.DetachedItem("plank", 2);
        EntityId[] water = w.At(9_700, () => new[]
        {
            w.Inv.CreateWorldItem(w.R.Entries[l].Id, WorldItemRig.Pos, WorldItemOrigin.Thrown, true, 9_700),
            w.Inv.CreateWorldItem(w.R.Entries[p].Id, WorldItemRig.Pos, WorldItemOrigin.Thrown, true, 9_700),
        });

        // Act
        w.AdvanceTo(10_000);
        EventEntry[] log = w.RunTick(10_000);

        // Assert: all three despawn at 10000, ordered by id (equal expiresTick), each in its own CallSeq
        EventEntry[] gone = Heads(log, InvEventKinds.WorldItemDespawned);
        Assert.Equal(new[] { ground.Value, water[0].Value, water[1].Value }, gone.Select(e => e.PayloadAs<WorldItemDespawnedPayload>().WorldItemId).ToArray());
        Assert.Equal(3, gone.Select(e => e.CallSeq).Distinct().Count());
        Assert.Equal(0, w.World.LiveCount);
    }

    // ---------------------------------------------------------------- H-R12-3

    [Fact]
    [Trait("Id", "H-R12-3")]
    public void HR123_512HalfMailHalfPlankAllOld_A513thDropped_TheLowestExpiresTickMailReturnsToTheIntake_Count512_NoPlankDestroyed()
    {
        // Arrange: ticks 10..521 alternate plank, letter (distinct expiresTick; the oldest item is a plank); all older than 10 s by 900
        var w = new WorldItemRig();
        var items = new EntityId[512];
        var entries = new ushort[512];
        for (int i = 0; i < 512; i++)
        {
            entries[i] = i % 2 == 0 ? w.R.DetachedItem("plank", 1) : w.R.DetachedMail("letter", Address, new[] { 1_000u + (uint)i });
            items[i] = w.Create(10 + (uint)i, entries[i]);
        }

        ushort extra = w.R.DetachedItem("plank", 1);

        // Act
        EntityId newest = w.Create(900, extra);
        EventEntry[] log = w.R.Sink.Last;

        // Assert: the first letter (i = 1) went to the Intake, the plank at i = 0 (older) survived, count 512, no ItemsLost
        Assert.Equal(512, w.World.LiveCount);
        Assert.False(w.World.Table.TryGetSlot(items[1], out _));
        Assert.True(w.World.Table.TryGetSlot(items[0], out _));
        Assert.True(w.World.Table.TryGetSlot(newest, out _));
        Assert.Equal(w.R.Intake, w.R.Entries[entries[1]].ContainerSlot);
        Assert.Empty(Heads(log, InvEventKinds.ItemsLost));
        WorldItemDespawnedPayload gone = Assert.Single(Heads(log, InvEventKinds.WorldItemDespawned)).PayloadAs<WorldItemDespawnedPayload>();
        Assert.Equal(items[1].Value, gone.WorldItemId);
        Assert.Equal((byte)InvCause.Expired, gone.Cause);
        Assert.Single(Heads(log, InvEventKinds.WorldItemSpawned).Select(e => e.CallSeq).Distinct()); // one call scope
        Assert.Equal(Heads(log, InvEventKinds.WorldItemSpawned)[0].CallSeq, Heads(log, InvEventKinds.Version)[0].CallSeq);
    }

    [Fact]
    [Trait("Id", "H-R12-3")]
    public void HR123_512Planks_A513thPlank_TheLowestExpiresTickPlankIsDestroyedWithItemsLost()
    {
        // Arrange
        var w = new WorldItemRig();
        EntityId[] planks = w.CreateOnePerTick(10, 512, _ => w.R.DetachedItem("plank", 4));

        // Act
        EntityId newest = w.Create(600, w.R.DetachedItem("plank", 1));
        EventEntry[] log = w.R.Sink.Last;

        // Assert
        Assert.Equal(512, w.World.LiveCount);
        Assert.False(w.World.Table.TryGetSlot(planks[0], out _));
        Assert.True(w.World.Table.TryGetSlot(planks[1], out _));
        Assert.True(w.World.Table.TryGetSlot(newest, out _));
        ItemsLostPayload lost = Assert.Single(Heads(log, InvEventKinds.ItemsLost)).PayloadAs<ItemsLostPayload>();
        Assert.Equal(planks[0].Value, lost.WorldItemId);
        Assert.Equal(4, lost.Count);
        Assert.Equal((byte)InvCause.Destroyed, Assert.Single(Heads(log, InvEventKinds.WorldItemDespawned)).PayloadAs<WorldItemDespawnedPayload>().Cause);
    }

    [Fact]
    [Trait("Id", "H-R12-3")]
    public void HR123_511PlanksAndALetterLanded2sAgo_ADroppedPlank_TheLetterIsNotEvicted_TheLowestPlankIs_Count512()
    {
        // Arrange: 511 planks at ticks 10..520; a letter lands at 1000; the 513th (a plank) is dropped at 1060 (2 s later)
        var w = new WorldItemRig();
        EntityId[] planks = w.CreateOnePerTick(10, 511, _ => w.R.DetachedItem("plank", 1));
        EntityId letter = w.Create(1000, w.R.DetachedMail("letter", Address, new[] { 77u }), WorldItemOrigin.Thrown);

        // Act
        w.Create(1060, w.R.DetachedItem("plank", 1));
        EventEntry[] log = w.R.Sink.Last;

        // Assert
        Assert.Equal(512, w.World.LiveCount);
        Assert.True(w.World.Table.TryGetSlot(letter, out _));
        Assert.False(w.World.Table.TryGetSlot(planks[0], out _));
        Assert.Equal(planks[0].Value, Assert.Single(Heads(log, InvEventKinds.ItemsLost)).PayloadAs<ItemsLostPayload>().WorldItemId);
        Assert.Empty(Heads(log, InvEventKinds.Version)); // nothing entered the Intake
    }

    [Fact]
    [Trait("Id", "H-R12-3")]
    public void HR123_QualifyingMail900And901WithEqualLowestExpiresTick_TheLowerWorldItemId900IsEvicted()
    {
        // Arrange: WorldItems "900" and "901" (letters 900 and 901) created in one tick, so equal expiresTick; 510 planks after them
        var w = new WorldItemRig();
        ushort e900 = w.R.DetachedMail("letter", Address, new[] { 900u });
        ushort e901 = w.R.DetachedMail("letter", Address, new[] { 901u });
        EntityId[] pair = w.At(10, () => new[]
        {
            w.Inv.CreateWorldItem(w.R.Entries[e900].Id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, 10),
            w.Inv.CreateWorldItem(w.R.Entries[e901].Id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, 10),
        });
        _ = w.CreateOnePerTick(11, 510, _ => w.R.DetachedItem("plank", 1));
        Assert.True(w.World.Table.TryGetSlot(pair[0], out ushort s900));
        Assert.True(w.World.Table.TryGetSlot(pair[1], out ushort s901));
        Assert.Equal(w.World.Table[s900].ExpiresTick, w.World.Table[s901].ExpiresTick);
        Assert.True(pair[0].Value < pair[1].Value);

        // Act
        w.Create(900, w.R.DetachedItem("plank", 1));

        // Assert
        Assert.False(w.World.Table.TryGetSlot(pair[0], out _));
        Assert.True(w.World.Table.TryGetSlot(pair[1], out _));
        Assert.Equal(w.R.Intake, w.R.Entries[e900].ContainerSlot);
        Assert.Equal(512, w.World.LiveCount);
    }

    [Fact]
    [Trait("Id", "H-R12-3")]
    public void HR123_512YoungMail_A513thLands_NothingEvicted_Count513_CapExceededLogged_TheFirstToAgePast10sIsEvictedThatTick()
    {
        // Arrange: 512 letters, 2 per tick at ticks 1000..1255 (all younger than 10 s at 1256)
        var w = new WorldItemRig();
        EntityId[] young = w.CreateTwoPerTick(1000, 256, i => w.R.DetachedMail("letter", Address, new[] { 10_000u + (uint)i }));
        int ringBefore = w.R.Host.World.CheckRing.Count;
        long exceededBefore = w.R.Counters[CounterId.InventoryWorldItemCapExceeded];

        // Act 1: the 513th lands at 1256
        EntityId newest = w.Create(1256, w.R.DetachedMail("letter", Address, new[] { 20_000u }), WorldItemOrigin.Thrown);
        EventEntry[] landLog = w.R.Sink.Last;

        // Assert 1: nothing evicted, count 513, one counter increment and one InventoryWorldItemCapExceeded record (a = 513, b = 512)
        Assert.Equal(513, w.World.LiveCount);
        Assert.Empty(Heads(landLog, InvEventKinds.WorldItemDespawned));
        Assert.Equal(exceededBefore + 1, w.R.Counters[CounterId.InventoryWorldItemCapExceeded]);
        Assert.Equal(ringBefore + 1, w.R.Host.World.CheckRing.Count);
        SimCheckRecord record = w.R.Host.World.CheckRing[w.R.Host.World.CheckRing.Count - 1];
        Assert.Equal(CheckCode.InventoryWorldItemCapExceeded, record.Check);
        Assert.Equal(513, record.A);
        Assert.Equal(512, record.B);

        // Act 2: tick 1299 (the first letters are 299 ticks old), then tick 1300 (they reach 10 s)
        w.AdvanceTo(1300);
        int at1299 = w.World.LiveCount;
        EventEntry[] log = w.RunTick(1300);

        // Assert 2: the re-check on 1300 evicts exactly the first one (lowest id) to the Intake; the 513th stays
        Assert.Equal(513, at1299);
        Assert.Equal(512, w.World.LiveCount);
        Assert.Equal(young[0].Value, Assert.Single(Heads(log, InvEventKinds.WorldItemDespawned)).PayloadAs<WorldItemDespawnedPayload>().WorldItemId);
        Assert.True(w.World.Table.TryGetSlot(young[1], out _));
        Assert.True(w.World.Table.TryGetSlot(newest, out _));
        Assert.Equal(ringBefore + 1, w.R.Host.World.CheckRing.Count); // logged once per episode
    }

    // ---------------------------------------------------------------- H-PERF-6d

    [Fact]
    [Trait("Id", "H-PERF-6d")]
    public void HPERF6d_A400PlankBurstAt512OldPlanks_VisitsAtMostCreatedPlusOneCandidates_AndEvicts400()
    {
        // Arrange
        var w = new WorldItemRig();
        _ = w.CreateOnePerTick(10, 512, _ => w.R.DetachedItem("plank", 1));
        ushort[] burst = Enumerable.Range(0, 400).Select(_ => w.R.DetachedItem("plank", 1)).ToArray();
        w.AdvanceTo(600);
        long before = w.R.Counters[CounterId.InventoryWorldItemCapCandidates];

        // Act
        int created = w.At(600, () => w.CreateAll(burst, 600));
        long visits = w.R.Counters[CounterId.InventoryWorldItemCapCandidates] - before;

        // Assert
        Assert.Equal(400, created);
        Assert.True(visits <= created + 1, $"visits {visits} > created + 1 ({created + 1})");
        Assert.Equal(399, visits); // exact: the first head was verified by phase 1; each later eviction reads the new head once
        Assert.Equal(400, Heads(w.R.Sink.Last, InvEventKinds.ItemsLost).Length);
        Assert.Equal(512, w.World.LiveCount);
    }

    [Fact]
    [Trait("Id", "H-PERF-6d")]
    public void HPERF6d_A400BurstOverYoungMailOldPlanksAndWaterPlanks_VisitsAtMostCreatedPlusOne_WaterPlanksGoFirst()
    {
        // Arrange: 200 old planks (ticks 10..209, expiring from 9010), 12 water planks at 8700 (expiring 9000, so the lowest
        // expiresTick), then 300 young letters, 2 per tick at 8701..8850; the burst comes at 8860, when every letter is under 10 s old
        var w = new WorldItemRig();
        _ = w.CreateOnePerTick(10, 200, _ => w.R.DetachedItem("plank", 1));
        ushort[] waterPlanks = Enumerable.Range(0, 12).Select(_ => w.R.DetachedItem("plank", 1)).ToArray();
        EntityId[] water = w.At(8_700, () => waterPlanks.Select(e => w.Inv.CreateWorldItem(w.R.Entries[e].Id, WorldItemRig.Pos, WorldItemOrigin.Thrown, true, 8_700)).ToArray());
        _ = w.CreateTwoPerTick(8_701, 150, i => w.R.DetachedMail("letter", Address, new[] { 50_000u + (uint)i }));
        Assert.Equal(512, w.World.LiveCount);
        ushort[] burst = Enumerable.Range(0, 400).Select(_ => w.R.DetachedItem("plank", 1)).ToArray();
        w.AdvanceTo(8_860);
        long before = w.R.Counters[CounterId.InventoryWorldItemCapCandidates];

        // Act
        int created = w.At(8_860, () => w.CreateAll(burst, 8_860));
        long visits = w.R.Counters[CounterId.InventoryWorldItemCapCandidates] - before;
        EventEntry[] lost = Heads(w.R.Sink.Last, InvEventKinds.ItemsLost);

        // Assert: 400 evictions, the 12 water planks first (lowest expiresTick, 9000), no letter touched
        Assert.Equal(400, created);
        Assert.True(visits <= created + 1, $"visits {visits} > created + 1 ({created + 1})");
        Assert.Equal(11 + 387, visits); // exact: 11 new water heads, then 387 new plank heads (the mail head stays verified and young)
        Assert.Equal(400, lost.Length);
        Assert.Equal(water.Select(x => x.Value).ToArray(), lost.Take(12).Select(e => e.PayloadAs<ItemsLostPayload>().WorldItemId).ToArray());
        Assert.Empty(Heads(w.R.Sink.Last, InvEventKinds.Version));
        Assert.Equal(512, w.World.LiveCount);
    }

    [Fact]
    [Trait("Id", "H-PERF-6d")]
    public void HPERF6d_A400YoungMailBurstAt512YoungMail_VisitsNoCandidate_AndTheCountExceedsTheCap()
    {
        // Arrange
        var w = new WorldItemRig();
        _ = w.CreateTwoPerTick(1_000, 256, i => w.R.DetachedMail("letter", Address, new[] { 30_000u + (uint)i }));
        ushort[] burst = Enumerable.Range(0, 400).Select(i => w.R.DetachedMail("letter", Address, new[] { 40_000u + (uint)i })).ToArray();
        w.AdvanceTo(1_256);
        long before = w.R.Counters[CounterId.InventoryWorldItemCapCandidates];
        long exceededBefore = w.R.Counters[CounterId.InventoryWorldItemCapExceeded];

        // Act
        int created = w.At(1_256, () => w.CreateAll(burst, 1_256));

        // Assert
        Assert.Equal(400, created);
        Assert.Equal(0, w.R.Counters[CounterId.InventoryWorldItemCapCandidates] - before); // the young mail head is known: nothing visited
        Assert.Equal(912, w.World.LiveCount);
        Assert.Equal(exceededBefore + 400, w.R.Counters[CounterId.InventoryWorldItemCapExceeded]);
    }

    // ---------------------------------------------------------------- helpers

    internal static EventEntry[] Heads(EventEntry[] log, EventKind kind) =>
        log.Where(e => e.Kind == kind && (e.Flags & EventFlags.Continuation) == 0).ToArray();

    /// <summary>Calls <paramref name="call"/> between ticks: under SIM_ASSERT the tick guard throws; otherwise it refuses.</summary>
    private static EntityId SimAssertOrRelease(Func<EntityId> call)
    {
        try
        {
            return call();
        }
        catch (SimAssertException e) when (e.Code == SimAssertCode.MutatingApiOutOfPhase)
        {
            return EntityId.None;
        }
    }
}

/// <summary>
/// TEST-ONLY rig of story 013: an <see cref="InvAutoPlaceRig"/> world (player A in slot 0, the real Intake) with tick-addressed
/// helpers. <see cref="Now"/> is the tick that runs next; <see cref="At{T}"/> runs a call inside that tick's <c>MailStep</c> hook
/// (after phase 1 <c>ItemsExpire</c>), capturing only that tick's log.
/// </summary>
internal sealed class WorldItemRig
{
    public WorldItemRig(InvAutoPlaceRig? rig = null)
    {
        R = rig ?? new InvAutoPlaceRig();
        R.CaptureEvents = false;
    }

    /// <summary>The position every rig creation uses, mm.</summary>
    public static Vec3Mm Pos => new(12_000, 0, -3_500);

    public InvAutoPlaceRig R { get; }

    public InventorySystem Inv => R.Inventory;

    public WorldItems World => Inv.WorldItems;

    /// <summary>The tick that runs next.</summary>
    public uint Now => R.Host.World.CurrentTick;

    /// <summary>Ticks (capture off) until <see cref="Now"/> is <paramref name="tick"/>.</summary>
    public void AdvanceTo(uint tick)
    {
        R.CaptureEvents = false;
        while (Now < tick)
        {
            R.Host.World.Tick();
        }
    }

    /// <summary>Runs tick <paramref name="tick"/> and returns its log.</summary>
    public EventEntry[] RunTick(uint tick)
    {
        AdvanceTo(tick);
        R.CaptureEvents = true;
        R.Host.World.Tick();
        R.CaptureEvents = false;
        return R.Sink.Last;
    }

    /// <summary>Runs <paramref name="call"/> inside tick <paramref name="tick"/> (its <c>MailStep</c>); that tick's log is <c>R.Sink.Last</c>.</summary>
    public T At<T>(uint tick, Func<T> call)
    {
        AdvanceTo(tick);
        R.CaptureEvents = true;
        T result = IntakeSpawnOutfeedTests.InPhase(R, call);
        R.CaptureEvents = false;
        return result;
    }

    /// <summary>Creates a WorldItem of the detached entry in tick <paramref name="tick"/> (createdTick = that tick); asserts success.</summary>
    public EntityId Create(uint tick, ushort entrySlot, WorldItemOrigin origin = WorldItemOrigin.Dropped, bool onWater = false)
    {
        EntryId entry = R.Entries[entrySlot].Id;
        EntityId id = At(tick, () => Inv.CreateWorldItem(entry, Pos, origin, onWater, tick));
        Assert.False(id.IsNone);
        return id;
    }

    /// <summary>Creates <paramref name="n"/> WorldItems, one per tick from <paramref name="firstTick"/>; <paramref name="entry"/> makes each detached entry (between ticks).</summary>
    public EntityId[] CreateOnePerTick(uint firstTick, int n, Func<int, ushort> entry)
    {
        var ids = new EntityId[n];
        for (int i = 0; i < n; i++)
        {
            ids[i] = Create(firstTick + (uint)i, entry(i));
        }

        return ids;
    }

    /// <summary>Creates <c>2 x ticks</c> WorldItems, two per tick from <paramref name="firstTick"/> (equal expiresTick in pairs).</summary>
    public EntityId[] CreateTwoPerTick(uint firstTick, int ticks, Func<int, ushort> entry)
    {
        var ids = new EntityId[2 * ticks];
        for (int t = 0; t < ticks; t++)
        {
            ushort a = entry(2 * t);
            ushort b = entry((2 * t) + 1);
            uint tick = firstTick + (uint)t;
            EntityId[] pair = At(tick, () => new[]
            {
                Inv.CreateWorldItem(R.Entries[a].Id, Pos, WorldItemOrigin.Dropped, false, tick),
                Inv.CreateWorldItem(R.Entries[b].Id, Pos, WorldItemOrigin.Dropped, false, tick),
            });
            Assert.DoesNotContain(EntityId.None, pair);
            ids[2 * t] = pair[0];
            ids[(2 * t) + 1] = pair[1];
        }

        return ids;
    }

    /// <summary>In-phase body: creates a WorldItem of each detached entry at <paramref name="tick"/> (Dropped, ground); returns how many were created.</summary>
    public int CreateAll(ushort[] entries, uint tick)
    {
        int created = 0;
        foreach (ushort e in entries)
        {
            if (!Inv.CreateWorldItem(R.Entries[e].Id, Pos, WorldItemOrigin.Dropped, false, tick).IsNone)
            {
                created++;
            }
        }

        return created;
    }

    /// <summary>Spawns one letter per id into the Intake (one entry each) in the next tick.</summary>
    public void SpawnIntake(params uint[] ids)
    {
        DefIndex letter = R.KindIndex("letter");
        MailId[] mail = ids.Select(i => new MailId(i)).ToArray();
        SpawnResult r = At(Now, () => Inv.Spawn(Inv.IntakeContainer, letter, 4u, 0, mail, MergePolicy.NoMerge));
        Assert.Equal(ids.Length, r.Placed);
    }

    /// <summary>The Intake entry holding mail id <paramref name="mailId"/>.</summary>
    public ushort IntakeEntryHolding(uint mailId)
    {
        for (ushort s = 0; s < R.Entries.Capacity; s++)
        {
            if (R.Entries[s].IsLive && R.Entries[s].ContainerSlot == R.Intake && R.IdsOf(s).Contains(mailId))
            {
                return s;
            }
        }

        throw new InvalidOperationException($"mail {mailId} is not in the Intake");
    }

    /// <summary>The highest arrivalSeq of the Intake's live entries.</summary>
    public uint MaxIntakeArrivalSeq()
    {
        uint max = 0;
        for (ushort s = 0; s < R.Entries.Capacity; s++)
        {
            if (R.Entries[s].IsLive && R.Entries[s].ContainerSlot == R.Intake)
            {
                max = Math.Max(max, R.Entries[s].ArrivalSeq);
            }
        }

        return max;
    }
}
