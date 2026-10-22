using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Backlog;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/021 AC "Ring capacity": the backlog ring is preallocated at world build with capacity =
/// <c>inventory.entryPoolCapacity</c>, the entry pool refuses before the ring can fill, and the unreachable full-ring case is
/// <see cref="SimAssertCode.InventoryBacklogRingFull"/> (82) under <c>INVENTORY_ASSERT</c> while Release drops nothing and keeps the
/// WorldItem. The full ring is reached through the test-only <see cref="BacklogRing.LimitForTests"/>. The 2,800-entry H-PERF fixture
/// fitting is <see cref="IntakeDrainTests.Drain_A2800EntryPass_IntoScattered1x1Holes_StaysWithin8500PlacementProbes_AndEntriesWalkedPlus1Visits"/>
/// (every one of its 2,800 appends must succeed).
/// </summary>
public sealed class BacklogRingTests
{
    private const uint Address4 = 4u;
    private const uint Address9 = 9u;

    [Fact]
    public void Ring_IsPreallocatedAtWorldBuild_WithTheConfiguredEntryPoolCapacity_AndHoldsThe2800EntryFixture()
    {
        // Arrange + Act: a world built from the balance and one with an overridden entry pool
        var balanced = new InvAutoPlaceRig();
        var small = new InvAutoPlaceRig(entryPoolCapacity: 400);

        // Assert: capacity = inventory.entryPoolCapacity (no key of its own), empty at build; the balance default holds 2,800 entries
        Assert.Equal(balanced.Content.Inventory.EntryPoolCapacity, balanced.Inventory.Backlog.Capacity);
        Assert.Equal(balanced.Entries.Capacity, balanced.Inventory.Backlog.Capacity);
        Assert.InRange(balanced.Inventory.Backlog.Capacity, 2_800, int.MaxValue);
        Assert.Equal(0, balanced.Inventory.BacklogEntries);
        Assert.Equal(400, small.Inventory.Backlog.Capacity);
        Assert.Equal(small.Entries.Capacity, small.Inventory.Backlog.Capacity);
    }

    [Fact]
    public void Ring_TakesEveryEntryThePoolCanHold_AndThePoolRefusesTheNextAppend_NotTheRing()
    {
        // Arrange: a 64-slot pool; every free slot is appended in one MailStep (no drain pass between appends)
        var rig = new InvAutoPlaceRig(entryPoolCapacity: 64);
        DefIndex letter = rig.KindIndex("letter");
        int free = rig.Entries.Capacity - rig.Entries.LiveCount;
        int records = rig.Host.World.CheckRing.Count;
        int appended = 0;
        bool extra = true;

        // Act
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            for (int i = 0; i < free; i++)
            {
                if (rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address4, 0, IntakeSpawnOutfeedTests.Mail(1_000u + (uint)i))))
                {
                    appended++;
                }
            }

            extra = rig.Inventory.AppendBacklog(new BacklogEntry(letter, Address4, 0, IntakeSpawnOutfeedTests.Mail(9_999)));
            return 0;
        });

        // Assert: every free slot queued (the ring is now as full as the pool); the next append is the pool's refusal and record,
        // with no full-ring assert
        Assert.Equal(free, appended);
        Assert.Equal(free, rig.Inventory.BacklogEntries);
        Assert.Equal(rig.Entries.Capacity, rig.Entries.LiveCount);
        Assert.False(extra);
        Assert.Equal(records + 1, rig.Host.World.CheckRing.Count);
        Assert.Equal(CheckCode.InventoryEntryPoolExhausted, rig.Host.World.CheckRing[rig.Host.World.CheckRing.Count - 1].Check);
    }

    [Fact]
    public void Expiry_OfAMailWorldItem_IntakeFullAndTheRingForcedFull_AssertsRingFullUnderInventoryAssert_OrKeepsTheWorldItemInRelease()
    {
        // Arrange: letter 500 dropped at 1000; the Intake full; one backlog letter; the ring capped at that one entry
        var w = new WorldItemRig();
        ushort entry = w.R.DetachedMail("letter", Address4, new[] { 500u });
        EntityId letter = w.Create(1000, entry);
        IntakeSpawnOutfeedTests.FillIntake(w.R, 320, 1);
        Assert.True(IntakeDrainTests.Append(w.R, "letter", Address9, 2_000));
        w.Inv.Backlog.LimitForTests(1);
        Assert.True(w.World.Table.TryGetSlot(letter, out ushort slot));
        w.AdvanceTo(w.World.Table[slot].ExpiresTick);
        ushort queued = IntakeDrainTests.Queue(w.R)[0];
        int live = w.World.LiveCount;
        uint worldSeq = w.World.WorldSeq;

        // Act + Assert: the expiry tick
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => w.R.Host.World.Tick());
            Assert.Equal(SimAssertCode.InventoryBacklogRingFull, thrown.Code);
            return;
        }

        EventEntry[] log = w.RunTick(w.Now);

        // Release: nothing dropped; the WorldItem is still live with the same id, slot and entry; the backlog is unchanged
        Assert.True(w.World.Table.TryGetSlot(letter, out ushort after));
        Assert.Equal(slot, after);
        Assert.Equal(entry, w.World.Table[after].EntrySlot);
        Assert.True(w.World.OwnsEntry(entry));
        Assert.True(w.R.Entries[entry].IsLive);
        Assert.Equal(EntryRecord.NoContainer, w.R.Entries[entry].ContainerSlot);
        Assert.Equal(new uint[] { 500 }, w.R.IdsOf(entry));
        Assert.False(w.Inv.Backlog.Contains(entry));
        Assert.Equal(new[] { queued }, IntakeDrainTests.Queue(w.R));
        Assert.Equal(1, w.Inv.BacklogEntries);
        Assert.Equal(1, w.Inv.BacklogUnits);
        Assert.Equal(live, w.World.LiveCount);
        Assert.Equal(worldSeq, w.World.WorldSeq);
        Assert.Empty(WorldItemTests.Heads(log, InvEventKinds.BacklogUnits));
        Assert.Empty(WorldItemTests.Heads(log, InvEventKinds.WorldItemDespawned));
        Assert.Empty(WorldItemTests.Heads(log, InvEventKinds.ItemsLost));
    }

    [Fact]
    public void CapEviction_OfAQualifyingLetter_IntakeFullAndTheRingForcedFull_AssertsRingFullUnderInventoryAssert_OrKeepsTheLetterInRelease()
    {
        // Arrange: one old letter (10) + 511 planks (11..521); the Intake full; one backlog letter; the ring capped at that one entry
        var w = new WorldItemRig();
        ushort letterEntry = w.R.DetachedMail("letter", Address4, new[] { 600u });
        EntityId letter = w.Create(10, letterEntry);
        EntityId[] planks = w.CreateOnePerTick(11, 511, _ => w.R.DetachedItem("plank", 1));
        IntakeSpawnOutfeedTests.FillIntake(w.R, 320, 1);
        Assert.True(IntakeDrainTests.Append(w.R, "letter", Address9, 2_000));
        w.Inv.Backlog.LimitForTests(1);
        Assert.True(w.World.Table.TryGetSlot(letter, out ushort slot));
        ushort plank = w.R.DetachedItem("plank", 1);

        // Act: the 513th creation's cap eviction picks the letter (age >= 300) first
        (EntityId created, ushort code) = w.At(900, () =>
        {
            try
            {
                return (w.Inv.CreateWorldItem(w.R.Entries[plank].Id, WorldItemRig.Pos, WorldItemOrigin.Dropped, false, 900), (ushort)0);
            }
            catch (SimAssertException e)
            {
                return (EntityId.None, e.Code);
            }
        });
        EventEntry[] log = w.R.Sink.Last;

        // Assert: INVENTORY_ASSERT trips 82; Release keeps the letter's WorldItem (same slot, same entry), queues nothing, and the
        // eviction falls through to story 013's non-mail rule (the lowest plank), so the count is back at the cap
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.InventoryBacklogRingFull, code);
            return;
        }

        Assert.Equal((ushort)0, code);
        Assert.NotEqual(EntityId.None, created);
        Assert.True(w.World.Table.TryGetSlot(letter, out ushort after));
        Assert.Equal(slot, after);
        Assert.Equal(letterEntry, w.World.Table[after].EntrySlot);
        Assert.False(w.Inv.Backlog.Contains(letterEntry));
        Assert.Equal(1, w.Inv.BacklogEntries);
        Assert.Equal(1, w.Inv.BacklogUnits);
        Assert.Empty(WorldItemTests.Heads(log, InvEventKinds.BacklogUnits));
        Assert.False(w.World.Table.TryGetSlot(planks[0], out _));
        Assert.Single(WorldItemTests.Heads(log, InvEventKinds.ItemsLost));
        Assert.Equal(512, w.World.LiveCount);
    }

    [Fact]
    public void ReturnToIntakeAndTryAppendDetached_WithTheRingForcedFull_AssertRingFullUnderInventoryAssert_OrReturnFalseChangingNothingInRelease()
    {
        // Arrange: the Intake full; one backlog letter; the ring capped at that one entry; a detached letter returning
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1_000);
        Assert.True(IntakeDrainTests.Append(rig, "letter", Address4, 2_000));
        rig.Inventory.Backlog.LimitForTests(1);
        ushort detached = rig.DetachedMail("letter", Address9, new[] { 7u });
        uint version = rig.Header(rig.Intake).Version;

        // Act
        (bool returned, ushort returnCode) = IntakeSpawnOutfeedTests.InPhase(rig, () => Catching(() => rig.Inventory.ReturnToIntake(detached, ActorId.System, InvCause.Expired)));
        EventEntry[] returnLog = rig.Sink.Last;
        (bool appended, ushort appendCode) = IntakeSpawnOutfeedTests.InPhase(rig, () => Catching(() => rig.Inventory.TryAppendDetached(detached)));
        EventEntry[] appendLog = rig.Sink.Last;

        // Assert: 82 under INVENTORY_ASSERT, else false; either way the entry stays the caller's and nothing is queued or raised
        ushort expected = TestBuildExpectation.SimAssertsExpected ? SimAssertCode.InventoryBacklogRingFull : (ushort)0;
        Assert.Equal(expected, returnCode);
        Assert.Equal(expected, appendCode);
        Assert.False(returned);
        Assert.False(appended);
        Assert.False(rig.Inventory.Backlog.Contains(detached));
        Assert.True(rig.Entries[detached].IsLive);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[detached].ContainerSlot);
        Assert.Equal(1, rig.Inventory.BacklogEntries);
        Assert.Equal(1, rig.Inventory.BacklogUnits);
        Assert.Equal(version, rig.Header(rig.Intake).Version);
        Assert.DoesNotContain(returnLog, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);
        Assert.DoesNotContain(appendLog, e => e.Kind >= InvEventKinds.Inserted && e.Kind <= InvEventKinds.BacklogUnits);

        // Act + Assert 2: with the seam lifted the same return queues the entry (only the full ring refused it)
        rig.Inventory.Backlog.LimitForTests(rig.Inventory.Backlog.Capacity);
        Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ReturnToIntake(detached, ActorId.System, InvCause.Expired)));
        Assert.Equal(detached, IntakeDrainTests.Queue(rig)[^1]);
    }

    /// <summary>In-phase body: runs <paramref name="call"/>; under INVENTORY_ASSERT an assert trip is caught and reported as (false, its code).</summary>
    private static (bool Ok, ushort Code) Catching(Func<bool> call)
    {
        try
        {
            return (call(), 0);
        }
        catch (SimAssertException e)
        {
            return (false, e.Code);
        }
    }
}
