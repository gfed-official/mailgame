using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/015 (GDD R6, R17; ADR-0009 Key Interfaces and section 5; ADR-0003 section 5): the feature-epic hooks of
/// <see cref="IInventoryApi"/>. <see cref="IInventoryApi.ConsumeUnit"/> takes the oldest unit through <c>TakeOldest</c> with one
/// <c>InvRemoved</c> and one <c>InvVersion</c> seal (cause <c>Consumed</c>), and refuses a stale ref or an out-of-phase call with nothing
/// mutated; <see cref="IInventoryApi.CountItem"/> reads the <c>itemId → units</c> index (<see cref="ItemCountIndex"/>) the mutator's
/// item primitives maintain, which equals a brute-force scan after every one of 1,000 seeded random mutations, with a probe count that
/// does not grow with the entry count. <see cref="IInventoryApi.DetachForThrow"/> takes a whole hotbar entry out through <c>Remove</c>
/// (one <c>InvRemoved</c>, one seal), leaving it live and detached, and refuses a malformed id or a live non-hotbar entry
/// (<c>bad_request</c>), a once-allocated id that is no longer live, an unmapped temp id of the thrower's namespace or a wrong count
/// (<c>stale_entry</c>) with nothing changed; <see cref="IInventoryApi.TryInsert"/> (mail form) refuses an outfeed container, a
/// whitelist refusal and no room with nothing changed, and otherwise merges first (one <c>InvMerged</c>, no entry created) or places
/// a new entry. The mutating calls run inside a real tick through the rig's <c>MailStep</c> hook.
/// </summary>
public class InventoryApiTests
{
    private const uint Address4 = 4u;

    /// <summary>
    /// The stated bound on table slots one <c>CountItem</c> lookup of one container reads (hit or miss, the empty slot that ends a
    /// miss included). The table is at least twice the entry pool and holds at most one key per live item entry, so it is never more
    /// than half full; at load 1/2 linear probing bounds only the <i>expected</i> probe length (about 1.5 for a hit, 2.5 for a miss),
    /// not the worst case. So this is an empirical bound for these deterministic fixtures, with margin over the expected length. A
    /// constant; it is not scaled by entries or keys.
    /// </summary>
    private const int MaxProbesPerLookup = 8;

    [Fact]
    [Trait("Id", "H-R6-1")]
    public void ConsumeUnit_OnA_4_9_15_21_30_ConsumesTheOldest4_VersionPlusOne_OneRemovedThenOneConsumedSeal()
    {
        // Arrange: A = [4, 9, 15, 21, 30] in player A's inventory (the Misdeliver path)
        var rig = new InvAutoPlaceRig();
        ushort a = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 4, 9, 15, 21, 30 });
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        EntryId id = rig.Entries[a].Id;
        uint version = rig.Header(rig.InventoryA).Version;

        // Act
        bool consumed = IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.ConsumeUnit(new EntryRef(inventoryA, id), ConsumeReason.Misdelivered, rig.PlayerA));
        EventEntry[] log = InventoryEvents(rig.Sink.Last);

        // Assert: 4 consumed, A = [9, 15, 21, 30], same entry and cell, version + 1
        Assert.True(consumed);
        Assert.Equal(new uint[] { 9, 15, 21, 30 }, rig.IdsOf(a));
        Assert.Equal(4, rig.Entries[a].Count);
        Assert.Equal(id, rig.Entries[a].Id);
        Assert.Equal(a, rig.EntryAt(rig.InventoryA, 0, 0));
        Assert.Equal(version + 1, rig.Header(rig.InventoryA).Version);

        // Assert: exactly InvRemoved{inventoryA, A, 1} then one InvVersion seal by player A with cause Consumed
        Assert.Equal(2, log.Length);
        Assert.Equal(InvEventKinds.Removed, log[0].Kind);
        InvRemovedPayload removed = log[0].PayloadAs<InvRemovedPayload>();
        Assert.Equal(inventoryA.Value, removed.Container);
        Assert.Equal(id.Value, removed.EntryId);
        Assert.Equal(1, removed.Count);
        Assert.Equal(InvEventKinds.Version, log[1].Kind);
        InvVersionPayload seal = log[1].PayloadAs<InvVersionPayload>();
        Assert.Equal(inventoryA.Value, seal.Container);
        Assert.Equal(version, seal.VersionFrom);
        Assert.Equal(version + 1, seal.VersionTo);
        Assert.Equal(rig.PlayerA.Value, seal.ActorId);
        Assert.Equal((byte)InvCause.Consumed, seal.Cause);
    }

    [Fact]
    [Trait("Id", "H-R6-1")]
    public void ConsumeUnit_OfTheLastUnit_RemovesAndFreesTheEntryInTheSameCall()
    {
        // Arrange: a one-unit letter at (2, 1) of player A's inventory
        var rig = new InvAutoPlaceRig();
        ushort a = rig.PlaceMail(rig.InventoryA, "letter", 2, 1, Address4, new uint[] { 30 });
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        EntryId id = rig.Entries[a].Id;
        ContainerHeader before = rig.Header(rig.InventoryA);

        // Act
        bool consumed = IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.ConsumeUnit(new EntryRef(inventoryA, id), ConsumeReason.Delivered, rig.PlayerA));
        EventEntry[] log = InventoryEvents(rig.Sink.Last);

        // Assert: the entry is gone (id not live, cell free, entry count - 1), one InvRemoved{1} and one Consumed seal
        Assert.True(consumed);
        Assert.False(rig.Entries.TryGetSlot(id, out _));
        Assert.Equal(Occupancy.Free, rig.Inventory.Occupancy.OwnerAt(rig.InventoryA, before.W, 2, 1));
        Assert.Equal(before.EntryCount - 1, rig.Header(rig.InventoryA).EntryCount);
        Assert.Equal(before.FreeCells + 1, rig.Header(rig.InventoryA).FreeCells);
        Assert.Equal(before.Version + 1, rig.Header(rig.InventoryA).Version);
        Assert.Equal(new[] { InvEventKinds.Removed, InvEventKinds.Version }, log.Select(e => e.Kind));
        Assert.Equal(1, log[0].PayloadAs<InvRemovedPayload>().Count);
        Assert.Equal((byte)InvCause.Consumed, log[1].PayloadAs<InvVersionPayload>().Cause);
    }

    [Fact]
    public void ConsumeUnit_OnAnItemStack_TakesOneUnit_AndCountItemFollows()
    {
        // Arrange: 5 planks in the hotbar
        var rig = new InvAutoPlaceRig();
        ushort planks = rig.PlaceItem(rig.HotbarA, "plank", 3, 0, 5);
        ContainerId hotbar = rig.Inventory.HotbarOf(PlayerSlot.FromValue(0));
        DefIndex plank = rig.ItemIndex("plank");

        // Act
        bool consumed = IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.ConsumeUnit(new EntryRef(hotbar, rig.Entries[planks].Id), ConsumeReason.Delivered, rig.PlayerA));

        // Assert
        Assert.True(consumed);
        Assert.Equal(4, rig.Entries[planks].Count);
        Assert.Equal(4, rig.Inventory.CountItem(new[] { hotbar }, plank));
    }

    [Fact]
    public void ConsumeUnit_WithReturnedToSender_TheLastDefinedReason_Consumes_AndTheNextValueIsRefused()
    {
        // Arrange: 3 letters in player A's inventory
        var rig = new InvAutoPlaceRig();
        ushort a = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 1, 2, 3 });
        var entry = new EntryRef(rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0)), rig.Entries[a].Id);

        // Act
        bool returned = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ConsumeUnit(entry, ConsumeReason.ReturnedToSender, rig.PlayerA));
        List<long> before = State(rig);
        bool undefined = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ConsumeUnit(entry, (ConsumeReason)3, rig.PlayerA));

        // Assert: ReturnedToSender (2) consumed the oldest unit; 3 was refused with nothing changed and no record
        Assert.True(returned);
        Assert.Equal(new uint[] { 2, 3 }, rig.IdsOf(a));
        Assert.False(undefined);
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void ConsumeUnit_OnAStaleRefAnEmptyEntryOrAnUndefinedReason_ReturnsFalse_AndMutatesNothing()
    {
        // Arrange: a 3-letter entry in player A's inventory, a consumed (gone) entry, and a dead container id
        var rig = new InvAutoPlaceRig();
        ushort a = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 1, 2, 3 });
        ushort gone = rig.PlaceMail(rig.InventoryA, "letter", 1, 0, Address4, new uint[] { 9 });
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        ContainerId hotbar = rig.Inventory.HotbarOf(PlayerSlot.FromValue(0));
        EntryId id = rig.Entries[a].Id;
        EntryId goneId = rig.Entries[gone].Id;
        Assert.True(IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.ConsumeUnit(new EntryRef(inventoryA, goneId), ConsumeReason.Delivered, rig.PlayerA)));
        List<long> before = State(rig);
        var refs = new[]
        {
            (new EntryRef(hotbar, id), ConsumeReason.Delivered),             // the entry is not in that container
            (new EntryRef(inventoryA, goneId), ConsumeReason.Delivered),     // the entry is no longer live
            (new EntryRef(new ContainerId(999), id), ConsumeReason.Delivered), // a dead container
            (new EntryRef(inventoryA, default), ConsumeReason.Delivered),    // no entry
            (new EntryRef(inventoryA, id), (ConsumeReason)3),                // an undefined reason
        };
        var results = new List<bool>();

        // Act: every refusal in one tick
        rig.Hook.Action = (in TickContext ctx) =>
        {
            foreach ((EntryRef r, ConsumeReason reason) in refs)
            {
                results.Add(rig.Inventory.ConsumeUnit(r, reason, rig.PlayerA));
            }
        };
        rig.Host.World.Tick();
        rig.Hook.Action = null;

        // Assert: all false, nothing changed, no inventory event
        Assert.Equal(refs.Length, results.Count);
        Assert.All(results, Assert.False);
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void ConsumeUnit_OnAnEmptyEntry_ReturnsFalse_AndRaisesNothing()
    {
        // Arrange: an item entry whose count was forced to 0 outside the mutator (an invariant break the guard must not act on)
        var rig = new InvAutoPlaceRig();
        ushort planks = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 2);
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        EntryId id = rig.Entries[planks].Id;
        rig.Entries[planks].Count = 0;
        rig.Recount(); // story 017: arranged outside the mutator
        uint version = rig.Header(rig.InventoryA).Version;

        // Act
        bool consumed = IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.ConsumeUnit(new EntryRef(inventoryA, id), ConsumeReason.Delivered, rig.PlayerA));

        // Assert
        Assert.False(consumed);
        Assert.True(rig.Entries[planks].IsLive);
        Assert.Equal(version, rig.Header(rig.InventoryA).Version);
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    [Trait("Id", "R9")]
    public void ConsumeUnit_OutsideATickPhase_IsRefused_CountsMutatingApiOutOfPhase_AndMutatesNothing()
    {
        // Arrange: between ticks, a 2-letter entry
        var rig = new InvAutoPlaceRig();
        ushort a = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 5, 6 });
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        var entry = new EntryRef(inventoryA, rig.Entries[a].Id);
        List<long> before = State(rig);
        long refusedBefore = rig.Counters[CounterId.MutatingApiOutOfPhase];
        long outsideBefore = rig.Counters[CounterId.EventsOutsideTick];

        // Act + Assert: SIM_ASSERT builds assert; Release refuses
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => rig.Inventory.ConsumeUnit(entry, ConsumeReason.Delivered, rig.PlayerA));
            Assert.Equal(SimAssertCode.MutatingApiOutOfPhase, ex.Code);
        }
        else
        {
            Assert.False(rig.Inventory.ConsumeUnit(entry, ConsumeReason.Delivered, rig.PlayerA));
        }

        // Assert: nothing mutated, counter 45 + 1, no event raised then or flushed by the next tick
        Assert.Equal(before, State(rig));
        Assert.Equal(new uint[] { 5, 6 }, rig.IdsOf(a));
        Assert.Equal(refusedBefore + 1, rig.Counters[CounterId.MutatingApiOutOfPhase]);
        Assert.Equal(outsideBefore, rig.Counters[CounterId.EventsOutsideTick]);
        rig.Host.World.Tick();
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void CountItem_SumsTheListedContainers_IgnoresMailAndDeadContainers_AndIsLegalBetweenTicks()
    {
        // Arrange: planks in two containers, an axe (item def 0) and mail (whose ItemId field is also 0) beside them
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 7);
        _ = rig.PlaceItem(rig.InventoryA, "plank", 1, 0, 20);
        _ = rig.PlaceItem(rig.HotbarA, "plank", 2, 0, 3);
        _ = rig.PlaceItem(rig.InventoryA, "axe", 2, 0, 1);
        _ = rig.PlaceMail(rig.InventoryA, "letter", 3, 0, Address4, new uint[] { 1, 2, 3 });
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        ContainerId hotbar = rig.Inventory.HotbarOf(PlayerSlot.FromValue(0));
        DefIndex plank = rig.ItemIndex("plank");
        DefIndex axe = rig.ItemIndex("axe");
        Assert.Equal(0, axe.Value);

        // Act + Assert (between ticks: a read)
        Assert.Equal(27, rig.Inventory.CountItem(new[] { inventoryA }, plank));
        Assert.Equal(3, rig.Inventory.CountItem(new[] { hotbar }, plank));
        Assert.Equal(30, rig.Inventory.CountItem(new[] { inventoryA, hotbar, new ContainerId(999) }, plank));
        Assert.Equal(1, rig.Inventory.CountItem(new[] { inventoryA, hotbar }, axe));
        Assert.Equal(0, rig.Inventory.CountItem(new[] { hotbar }, axe));
        Assert.Equal(0, rig.Inventory.CountItem(new[] { inventoryA }, rig.ItemIndex("log")));
        Assert.Equal(0, rig.Inventory.CountItem(new[] { inventoryA }, DefIndex.None));
        Assert.Equal(0, rig.Inventory.CountItem(ReadOnlySpan<ContainerId>.Empty, plank));
    }

    [Fact]
    [Trait("Id", "H-R17-1")]
    public void OneThousandRandomItemMutations_CountItemEqualsABruteForceScan_AfterEveryMutation()
    {
        // Arrange
        var driver = new CountDriver(new InvAutoPlaceRig(), 0x2545_F491u);

        // Act
        driver.Run(1000);

        // Assert: every primitive ran often enough to matter (the per-mutation checks ran inside Run)
        Assert.Equal(1000, driver.Mutations);
        Assert.True(driver.Places >= 100, $"places {driver.Places}");
        Assert.True(driver.Removes >= 50, $"removes {driver.Removes}");
        Assert.True(driver.Transfers >= 100, $"transfers {driver.Transfers}");
        Assert.True(driver.CrossTransfers >= 50, $"cross-container transfers {driver.CrossTransfers}");
        Assert.True(driver.Merges >= 50, $"merges {driver.Merges}");
        Assert.True(driver.DetachedMerges >= 10, $"detached-source merges {driver.DetachedMerges}");
        Assert.True(driver.Takes >= 100, $"takes {driver.Takes}");
        Assert.True(driver.MaxUnitsSeen >= 40, $"max units {driver.MaxUnitsSeen}");
    }

    [Fact]
    [Trait("Id", "H-R17-1")]
    public void RebuildIndex_RestoresTheItemTotals_AfterTheyWereCleared()
    {
        // Arrange: planks and logs in player A's inventory; drop the container's totals as a load starts without them
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 12);
        _ = rig.PlaceItem(rig.InventoryA, "log", 1, 0, 4);
        _ = rig.PlaceItem(rig.HotbarA, "log", 1, 0, 2);
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        ContainerId hotbar = rig.Inventory.HotbarOf(PlayerSlot.FromValue(0));
        rig.Inventory.Mutator.ItemCounts.ClearContainer(rig.InventoryA);
        Assert.Equal(0, rig.Inventory.CountItem(new[] { inventoryA }, rig.ItemIndex("plank")));

        // Act
        rig.Inventory.Mutator.RebuildIndex(rig.InventoryA);

        // Assert: the inventory's totals are back; the hotbar's were never touched
        Assert.Equal(12, rig.Inventory.CountItem(new[] { inventoryA }, rig.ItemIndex("plank")));
        Assert.Equal(4, rig.Inventory.CountItem(new[] { inventoryA }, rig.ItemIndex("log")));
        Assert.Equal(2, rig.Inventory.CountItem(new[] { hotbar }, rig.ItemIndex("log")));
        Assert.Equal(3, rig.Inventory.Mutator.ItemCounts.KeyCount);
    }

    [Fact]
    [Trait("Id", "H-R17-1")]
    public void RebuildIndex_OnALiveIndex_ReplacesTheContainersTotals_WithoutDoublingThem()
    {
        // Arrange: planks and logs in player A's inventory, logs in the hotbar; the index is live and correct (nothing cleared)
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 12);
        _ = rig.PlaceItem(rig.InventoryA, "log", 1, 0, 4);
        _ = rig.PlaceItem(rig.HotbarA, "log", 1, 0, 2);
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        ContainerId hotbar = rig.Inventory.HotbarOf(PlayerSlot.FromValue(0));
        Assert.Equal(12, rig.Inventory.CountItem(new[] { inventoryA }, rig.ItemIndex("plank")));

        // Act: rebuild twice over the live totals
        rig.Inventory.Mutator.RebuildIndex(rig.InventoryA);
        rig.Inventory.Mutator.RebuildIndex(rig.InventoryA);

        // Assert: the same totals, not 24 / 8 or 36 / 12; the hotbar's untouched; no key added
        Assert.Equal(12, rig.Inventory.CountItem(new[] { inventoryA }, rig.ItemIndex("plank")));
        Assert.Equal(4, rig.Inventory.CountItem(new[] { inventoryA }, rig.ItemIndex("log")));
        Assert.Equal(2, rig.Inventory.CountItem(new[] { hotbar }, rig.ItemIndex("log")));
        Assert.Equal(3, rig.Inventory.Mutator.ItemCounts.KeyCount);
    }

    [Fact]
    [Trait("Id", "H-R17-1")]
    public void CountItem_ProbesPerLookup_StayUnderAConstantBound_AsKeysAndEntriesGrow_AtRealisticLoad()
    {
        // Arrange: the balance's entry pool (8,192 -> a 16,384-slot table); six 8 x 8 grids beside player A's panels
        var rig = new InvAutoPlaceRig();
        var grids = new List<ushort>();
        for (int g = 0; g < 6; g++)
        {
            grids.Add(rig.CreateGrid(8, 8));
        }

        ContainerId[] all = AllContainers(rig);
        DefIndex[] items = ItemDefs(rig);
        var stages = new List<(int Entries, int Keys, int MaxHit, int MaxMiss)>();

        // Act: fill one grid per stage with all four item defs (one item per 1 x 2 column pair; 1 x 1 defs as two entries),
        // measuring every (container, item) lookup of every live container after each stage
        foreach (ushort grid in grids)
        {
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y += 2)
                {
                    FillPair(rig, grid, items[(grid + x + (y / 2)) % items.Length], x, y);
                }
            }

            (int maxHit, int maxMiss) = MaxProbes(rig, all, items);
            stages.Add((rig.Entries.LiveCount, rig.Inventory.Mutator.ItemCounts.KeyCount, maxHit, maxMiss));
        }

        // Assert: entries and distinct keys grew sixfold; every lookup, hit or miss, read at most MaxProbesPerLookup slots. This checks
        // the bound at the balance's realistic load only: 24 keys in 16,384 slots read 1 slot under any reasonable hash, so it does not
        // guard the hash itself (ItemCountIndex_OneItemInSuccessiveContainers_AtMaxLoad_StaysUnderTheProbeBound does)
        Assert.Equal(6 * 48, stages[^1].Entries);
        Assert.Equal(6 * stages[0].Entries, stages[^1].Entries);
        Assert.Equal(4, stages[0].Keys);
        Assert.Equal(6 * 4, stages[^1].Keys);
        Assert.All(stages, st => Assert.InRange(st.MaxHit, 1, MaxProbesPerLookup));
        Assert.All(stages, st => Assert.InRange(st.MaxMiss, 1, MaxProbesPerLookup));
    }

    [Fact]
    [Trait("Id", "H-R17-1")]
    public void CountItem_ProbesPerLookup_StayUnderAConstantBound_AsKeysAndEntriesGrow_ToTheMapsMaxLoad()
    {
        // Arrange: a 64-entry pool (a 128-slot table) and sixteen 3 x 2 grids, each to hold all four item defs as one entry each,
        // so the last stage has 64 entries and 64 distinct keys: the pool is full and the table at its maximum load of 1/2
        var rig = new InvAutoPlaceRig(entryPoolCapacity: 64);
        var grids = new List<ushort>();
        for (int g = 0; g < 16; g++)
        {
            grids.Add(rig.CreateGrid(3, 2));
        }

        ContainerId[] all = AllContainers(rig);
        DefIndex[] items = ItemDefs(rig);
        var unknown = new[] { DefIndex.FromValue((ushort)items.Length), DefIndex.FromValue(200) }; // never a key: misses everywhere
        var stages = new List<(int Entries, int Keys, int MaxHit, int MaxMiss)>();
        Assert.Equal(128, rig.Inventory.Mutator.ItemCounts.TableSize);

        // Act: fill four grids per stage (axe and log in the 1 x 2 columns, plank and fiber in the third), measuring after each
        for (int g = 0; g < grids.Count; g++)
        {
            _ = rig.PlaceItem(grids[g], "axe", 0, 0, 1);
            _ = rig.PlaceItem(grids[g], "log", 1, 0, 3);
            _ = rig.PlaceItem(grids[g], "plank", 2, 0, 5);
            _ = rig.PlaceItem(grids[g], "fiber", 2, 1, 7);
            if (g % 4 == 3)
            {
                (int maxHit, int maxMiss) = MaxProbes(rig, all, items.Concat(unknown).ToArray());
                stages.Add((rig.Entries.LiveCount, rig.Inventory.Mutator.ItemCounts.KeyCount, maxHit, maxMiss));
            }
        }

        // Assert: entries and keys grew fourfold to the pool's capacity; every lookup stayed under the same constant bound
        Assert.Equal(new[] { 16, 32, 48, 64 }, stages.Select(st => st.Entries));
        Assert.Equal(new[] { 16, 32, 48, 64 }, stages.Select(st => st.Keys));
        Assert.Equal(rig.Entries.Capacity, rig.Entries.LiveCount);
        Assert.Equal(7 * 16, rig.Inventory.CountItem(all, rig.ItemIndex("fiber")));
        Assert.All(stages, st => Assert.InRange(st.MaxHit, 1, MaxProbesPerLookup));
        Assert.All(stages, st => Assert.InRange(st.MaxMiss, 1, MaxProbesPerLookup));
    }

    [Fact]
    [Trait("Id", "H-R17-1")]
    public void ItemCountIndex_OneItemInSuccessiveContainers_AtMaxLoad_StaysUnderTheProbeBound()
    {
        // Arrange: a 32-entry pool (a 64-slot table) holding item 7 in containers 0..31: 32 keys a fixed 1 << 16 apart in key space,
        // the table at its maximum load of 1/2. The old middle-bits Home ((key * golden) >> 40 & mask) put these keys into one run:
        // worst hit 31 and worst miss 33 probes; the top-bits Home reads at most 1 (hit) and 3 (miss)
        var counters = new SimCounters();
        var index = new ItemCountIndex(32, 96, counters);
        ushort item = 7;
        for (ushort c = 0; c < 32; c++)
        {
            index.Add(c, item, c + 1);
        }

        // Act: every hit, then misses in the next 64 containers and of the next item in the filled ones
        long maxHit = 0;
        long maxMiss = 0;
        for (ushort c = 0; c < 32; c++)
        {
            long before = counters[CounterId.InventoryItemCountProbes];
            Assert.Equal(c + 1, index.Units(c, item));
            maxHit = Math.Max(maxHit, counters[CounterId.InventoryItemCountProbes] - before);
        }

        for (ushort c = 0; c < 96; c++)
        {
            long before = counters[CounterId.InventoryItemCountProbes];
            Assert.Equal(0, index.Units(c, c < 32 ? (ushort)(item + 1) : item));
            maxMiss = Math.Max(maxMiss, counters[CounterId.InventoryItemCountProbes] - before);
        }

        // Assert: the table is half full, and no lookup read more than the stated bound
        Assert.Equal(64, index.TableSize);
        Assert.Equal(32, index.KeyCount);
        Assert.InRange(maxHit, 1, MaxProbesPerLookup);
        Assert.InRange(maxMiss, 1, MaxProbesPerLookup);
    }

    [Fact]
    public void ItemCountIndex_RefusesAtConstruction_AContainerCapacityWhoseSlotPlusOneWouldNotFitTheKeys16Bits()
    {
        // Arrange
        var counters = new SimCounters();

        // Act + Assert: 65,535 containers (slot + 1 at most 0xFFFF) builds, and its last slot keeps its own totals; one more, or none, throws
        var widest = new ItemCountIndex(4, ItemCountIndex.MaxContainerCapacity, counters);
        widest.Add(ushort.MaxValue - 1, 7, 3);
        widest.Add(0, 7, 2);
        Assert.Equal(3, widest.Units(ushort.MaxValue - 1, 7));
        Assert.Equal(2, widest.Units(0, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemCountIndex(4, ItemCountIndex.MaxContainerCapacity + 1, counters));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemCountIndex(4, 0, counters));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemCountIndex(0, 4, counters));
    }

    [Fact]
    public void ItemCountIndex_InsertsAndDeletesManyKeys_WithBackwardShift_KeepingEveryTotal()
    {
        // Arrange: a small table (entry capacity 16 -> 32 slots) so runs collide and wrap
        var counters = new SimCounters();
        var index = new ItemCountIndex(16, 4, counters);
        var expected = new Dictionary<(ushort, ushort), int>();
        uint state = 0xACE1u;

        // Act + Assert: 2,000 random adds and subtractions over 16 keys, checked against a dictionary after each
        for (int step = 0; step < 2000; step++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            ushort container = (ushort)(state % 4);
            ushort item = (ushort)((state >> 8) % 4);
            expected.TryGetValue((container, item), out int units);
            int delta = units > 0 && (state >> 16) % 2 == 0 ? -(int)(1 + ((state >> 20) % (uint)units)) : (int)(1 + ((state >> 20) % 5));
            index.Add(container, item, delta);
            expected[(container, item)] = units + delta;
            foreach (((ushort c, ushort i), int u) in expected)
            {
                Assert.Equal(u, index.Units(c, i));
            }

            Assert.Equal(expected.Count(kv => kv.Value > 0), index.KeyCount);
        }

        // Clearing one container drops only its keys
        index.ClearContainer(2);
        foreach (((ushort c, ushort i), int u) in expected)
        {
            Assert.Equal(c == 2 ? 0 : u, index.Units(c, i));
        }
    }

    [Fact]
    public void ItemCountIndex_ARunWrappingTheTableEnd_BackwardShiftsAcrossTheWrap_KeepingEveryTotal()
    {
        // Arrange: a 32-slot table; three keys whose home is the last slot (31) and two whose home is slot 0, so inserting the first
        // three in order fills 31, 0 and 1 (the run wraps) and the home-0 keys land after them at 2 and 3
        var counters = new SimCounters();
        var index = new ItemCountIndex(16, 64, counters);
        Assert.Equal(32, index.TableSize);
        List<(ushort C, ushort I)> last = KeysWithHome(31, 3, index.TableSize);
        List<(ushort C, ushort I)> first = KeysWithHome(0, 2, index.TableSize);
        var keys = last.Concat(first).ToList();
        var expected = new Dictionary<(ushort, ushort), int>();
        for (int k = 0; k < keys.Count; k++)
        {
            index.Add(keys[k].C, keys[k].I, 10 + k);
            expected[keys[k]] = 10 + k;
        }

        // Assert: the wrap happened. A home-31 key read in 2 probes sits in slot 0, in 3 in slot 1; the home-0 keys come after them
        Assert.Equal(new long[] { 1, 2, 3, 3, 4 }, keys.Select(k => ProbesOf(index, counters, k)));

        // Act + Assert: delete each key in insertion order, checking every total and the key count after each deletion. The first
        // deletion empties slot 31, so the backward shift moves the run back across the table end: the second home-31 key returns to
        // slot 31 (1 probe), the third to slot 0, the home-0 keys to slots 1 and 2
        for (int k = 0; k < keys.Count; k++)
        {
            index.Add(keys[k].C, keys[k].I, -expected[keys[k]]);
            expected[keys[k]] = 0;
            if (k == 0)
            {
                Assert.Equal(new long[] { 1, 2, 2, 3 }, keys.Skip(1).Select(key => ProbesOf(index, counters, key)));
            }

            foreach (((ushort c, ushort i), int u) in expected)
            {
                Assert.Equal(u, index.Units(c, i));
            }

            Assert.Equal(keys.Count - k - 1, index.KeyCount);
        }
    }

    [Fact]
    public void ItemCountIndex_ATotalThatWouldGoNegative_AssertsInventoryItemCountNegative_OrDeletesTheKeyInRelease()
    {
        // Arrange: 3 units of item 7 in container 1; container 2 has no key
        var counters = new SimCounters();
        var index = new ItemCountIndex(4, 4, counters);
        index.Add(1, 7, 3);

        // Act + Assert: SIM_ASSERT builds assert before anything changes; Release deletes the key (and adds none for a missing one)
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException over = Assert.Throws<SimAssertException>(() => index.Add(1, 7, -5));
            Assert.Equal(SimAssertCode.InventoryItemCountNegative, over.Code);
            SimAssertException missing = Assert.Throws<SimAssertException>(() => index.Add(2, 7, -1));
            Assert.Equal(SimAssertCode.InventoryItemCountNegative, missing.Code);
            Assert.Equal(3, index.Units(1, 7));
            Assert.Equal(1, index.KeyCount);
        }
        else
        {
            index.Add(1, 7, -5);
            index.Add(2, 7, -1);
            Assert.Equal(0, index.Units(1, 7));
            Assert.Equal(0, index.Units(2, 7));
            Assert.Equal(0, index.KeyCount);
        }

        Assert.Equal(78, SimAssertCode.InventoryItemCountNegative);
    }

    [Fact]
    public void MergeUnitInto_OntoAStackAtItsEffMax_AssertsInventoryMergeInvalid_OrChangesNothingInRelease()
    {
        // Arrange: a full 20-letter stack (effMax 20)
        var rig = new InvAutoPlaceRig();
        ushort target = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(1, 20));
        Assert.Equal(20, rig.SubjectOf(target).EffMax);
        StackKey key = StackKey.Of(in rig.Entries[target]);
        var plan = new InvPlan();
        _ = plan.Touch(rig.InventoryA);
        List<long> before = State(rig);

        // Act + Assert (between ticks: the assert fires before anything is changed or raised)
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => rig.Inventory.Mutator.MergeUnitInto(ref plan, target, key, new MailId(500)));
            Assert.Equal(SimAssertCode.InventoryMergeInvalid, thrown.Code);
        }
        else
        {
            Assert.False(rig.Inventory.Mutator.MergeUnitInto(ref plan, target, key, new MailId(500)));
        }

        Assert.Equal(InvAutoPlaceRig.Ids(1, 20), rig.IdsOf(target));
        Assert.Equal(before, State(rig));
        Assert.Equal(0, plan.MutatedCount);
    }

    [Theory]
    [InlineData("target-not-live")]
    [InlineData("target-not-mail")]
    [InlineData("target-detached")]
    [InlineData("none-mail-id")]
    [InlineData("key-mismatch")]
    public void MergeUnitInto_WithAnInvalidTargetIdOrKey_AssertsInventoryMergeInvalid_OrChangesNothingInRelease(string arm)
    {
        // Arrange: a 2-letter stack (address 4) in player A's inventory and the one input each arm makes invalid (the count-255 arm
        // cannot be reached alone: a mail effMax is at most 255, so a 255 stack is already at its effMax, the effMax test's arm)
        var rig = new InvAutoPlaceRig();
        ushort letters = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 1, 2 });
        StackKey key = StackKey.Of(in rig.Entries[letters]);
        ushort target = letters;
        var mail = new MailId(500);
        switch (arm)
        {
            case "target-not-live":
                Assert.True(rig.Entries.TryAllocate(rig.Host.World.CurrentTick, out target, out _));
                rig.Entries.Free(target);
                break;
            case "target-not-mail":
                target = rig.PlaceItem(rig.InventoryA, "plank", 3, 0, 2);
                key = StackKey.Of(in rig.Entries[target]);
                break;
            case "target-detached":
                target = rig.DetachedMail("letter", Address4, new uint[] { 3, 4 });
                break;
            case "none-mail-id":
                mail = default;
                break;
            default:
                key = rig.Mail("letter", Address4 + 1).Key;
                break;
        }

        var plan = new InvPlan();
        _ = plan.Touch(rig.InventoryA);
        SimCheckRing ring = rig.Host.World.CheckRing;
        int ringBefore = ring.Count;
        List<long> before = State(rig);

        // Act + Assert (between ticks: the assert fires before anything is changed or raised)
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => rig.Inventory.Mutator.MergeUnitInto(ref plan, target, key, mail));
            Assert.Equal(SimAssertCode.InventoryMergeInvalid, thrown.Code);
        }
        else
        {
            Assert.False(rig.Inventory.Mutator.MergeUnitInto(ref plan, target, key, mail));
        }

        // Assert: nothing changed (versions, entries, arena), nothing marked mutated, no check record
        Assert.Equal(new uint[] { 1, 2 }, rig.IdsOf(letters));
        Assert.Equal(before, State(rig));
        Assert.Equal(0, plan.MutatedCount);
        Assert.Equal(ringBefore, ring.Count);
    }

    [Fact]
    public void MergeUnitInto_ReturningFalseInsideABracket_BumpsNoVersion_RaisesNoRecordAndNoSeal()
    {
        // Arrange: a valid merge onto the 2-letter stack of a 1 x 1 grid whose arena is exhausted, so MergeUnitInto's union fails
        // after its validity check (the false path that asserts in no build); the caller reserved nothing, as a test may
        (InvAutoPlaceRig rig, ContainerId gridId, ushort stack) = ArenaRig(exhaustArena: true);
        ushort grid = rig.SlotOf(gridId);
        StackKey key = StackKey.Of(in rig.Entries[stack]);
        uint version = rig.Header(grid).Version;
        List<long> before = State(rig);
        EventLogFacade events = rig.Host.Services.Events;
        bool merged = true;
        int mutated = -1;

        // Act: one call scope and one bracket, as TryInsert's merge pass runs it
        IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            var plan = new InvPlan();
            _ = plan.Touch(grid);
            var extra = new InvReplyExtra();
            using (EventCallScope scope = events.BeginCall())
            {
                rig.Inventory.Mutator.Begin(ref plan, rig.PlayerA, InvCause.None);
                merged = rig.Inventory.Mutator.MergeUnitInto(ref plan, stack, key, new MailId(15));
                mutated = plan.MutatedCount;
                rig.Inventory.Mutator.End(ref plan, ref extra);
            }

            return merged;
        });

        // Assert: refused before MarkMutated, so End sealed nothing: the version is unchanged and no inventory record was raised
        Assert.False(merged);
        Assert.Equal(0, mutated);
        Assert.Equal(version, rig.Header(grid).Version);
        Assert.Equal(new uint[] { 10, 20 }, rig.IdsOf(stack));
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    [Trait("Id", "H-X-2")]
    public void DetachForThrow_Hotbar3Entry88Of5Letters_EmptiesIndex3_VersionPlusOne_OneRemovedThenOneSeal_EntryLiveAndDetached()
    {
        // Arrange: hotbar index 3 holds 5 letters as entry 88 (earlier ids spent between ticks)
        var rig = new InvAutoPlaceRig();
        BurnEntryIdsTo(rig, 87);
        ushort slot88 = rig.PlaceMail(rig.HotbarA, "letter", 3, 0, Address4, new uint[] { 11, 12, 13, 14, 15 });
        EntryId id = rig.Entries[slot88].Id;
        Assert.Equal(88u, id.Value);
        ContainerId hotbar = rig.Inventory.HotbarOf(PlayerSlot.FromValue(0));
        ContainerHeader before = rig.Header(rig.HotbarA);

        // Act
        DetachResult r = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.DetachForThrow(PlayerSlot.FromValue(0), id, 5));
        EventEntry[] log = InventoryEvents(rig.Sink.Last);

        // Assert: the result is (none, 88, 5); index 3 empty; version + 1
        Assert.Equal(new DetachResult(default, id, 5), r);
        Assert.Equal(Occupancy.Free, rig.Inventory.Occupancy.OwnerAt(rig.HotbarA, before.W, 3, 0));
        Assert.Equal(before.EntryCount - 1, rig.Header(rig.HotbarA).EntryCount);
        Assert.Equal(before.Version + 1, rig.Header(rig.HotbarA).Version);

        // Assert: entry 88 live and detached, in no WorldItem, with its 5 mailIds
        Assert.True(rig.Entries.TryGetSlot(id, out ushort still));
        Assert.Equal(slot88, still);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[slot88].ContainerSlot);
        Assert.False(rig.Inventory.WorldItems.OwnsEntry(slot88));
        Assert.Equal(5, rig.Entries[slot88].Count);
        Assert.Equal(new uint[] { 11, 12, 13, 14, 15 }, rig.IdsOf(slot88));

        // Assert: exactly InvRemoved{hotbar, 88, 5} then one InvVersion seal by player A, cause None
        Assert.Equal(new[] { InvEventKinds.Removed, InvEventKinds.Version }, log.Select(e => e.Kind));
        InvRemovedPayload removed = log[0].PayloadAs<InvRemovedPayload>();
        Assert.Equal(hotbar.Value, removed.Container);
        Assert.Equal(88u, removed.EntryId);
        Assert.Equal(5, removed.Count);
        InvVersionPayload seal = log[1].PayloadAs<InvVersionPayload>();
        Assert.Equal(hotbar.Value, seal.Container);
        Assert.Equal(before.Version, seal.VersionFrom);
        Assert.Equal(before.Version + 1, seal.VersionTo);
        Assert.Equal(rig.PlayerA.Value, seal.ActorId);
        Assert.Equal((byte)InvCause.None, seal.Cause);
    }

    [Fact]
    [Trait("Id", "H-X-2")]
    public void DetachForThrow_FromSlot1sHotbar_SealsWithPlayerBsActor()
    {
        // Arrange: player B joins slot 1; 3 letters at B's hotbar index 2
        var rig = new InvAutoPlaceRig();
        JoinSlot1(rig);
        PlayerSlot b = PlayerSlot.FromValue(1);
        ActorId playerB = ActorId.FromEntity(rig.Host.Services.Slots.Entry(b).PlayerId);
        Assert.NotEqual(rig.PlayerA, playerB);
        ContainerId hotbarB = rig.Inventory.HotbarOf(b);
        ushort hotbarBSlot = rig.SlotOf(hotbarB);
        ushort entry = rig.PlaceMail(hotbarBSlot, "letter", 2, 0, Address4, new uint[] { 1, 2, 3 });
        EntryId id = rig.Entries[entry].Id;
        uint version = rig.Header(hotbarBSlot).Version;

        // Act
        DetachResult r = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.DetachForThrow(b, id, 3));
        EventEntry[] log = InventoryEvents(rig.Sink.Last);

        // Assert: detached from B's hotbar; one InvRemoved then one seal on B's hotbar whose actor is player B
        Assert.Equal(new DetachResult(default, id, 3), r);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[entry].ContainerSlot);
        Assert.Equal(new[] { InvEventKinds.Removed, InvEventKinds.Version }, log.Select(e => e.Kind));
        InvVersionPayload seal = log[1].PayloadAs<InvVersionPayload>();
        Assert.Equal(hotbarB.Value, seal.Container);
        Assert.Equal(version + 1, seal.VersionTo);
        Assert.Equal(playerB.Value, seal.ActorId);
    }

    [Fact]
    [Trait("Id", "H-X-2")]
    public void DetachForThrow_OfTheCursorEntryOrANonHotbarEntryOrAMalformedId_IsBadRequest_NothingChanges_NoRecord()
    {
        // Arrange: player B joined; entries in A's cursor, A's inventory, B's hotbar, a detached one; one in A's hotbar
        var rig = new InvAutoPlaceRig();
        JoinSlot1(rig);
        ushort cursorA = rig.SlotOf(rig.Inventory.CursorOf(PlayerSlot.FromValue(0)));
        ushort hotbarB = rig.SlotOf(rig.Inventory.HotbarOf(PlayerSlot.FromValue(1)));
        ushort inCursor = rig.PlaceMail(cursorA, "letter", 0, 0, Address4, new uint[] { 1, 2 });
        ushort inPanel = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 3 });
        ushort inHotbarB = rig.PlaceMail(hotbarB, "letter", 2, 0, Address4, new uint[] { 4 });
        ushort inHotbarA = rig.PlaceMail(rig.HotbarA, "letter", 2, 0, Address4, new uint[] { 5 });
        ushort detached = rig.DetachedMail("letter", Address4, new uint[] { 6 });
        List<long> before = State(rig);
        PlayerSlot a = PlayerSlot.FromValue(0);
        var calls = new (PlayerSlot Slot, EntryId Entry, byte Units)[]
        {
            (a, rig.Entries[inCursor].Id, 2),                 // the cursor's entry
            (a, rig.Entries[inPanel].Id, 1),                  // a player_inventory entry
            (a, rig.Entries[inHotbarB].Id, 1),                // another player's hotbar entry
            (a, rig.Entries[detached].Id, 1),                 // a live detached entry
            (a, EntryId.Real(rig.Entries.LastId + 1), 1),     // the next id, never allocated (above the high-water)
            (a, EntryId.Real(999_999), 1),                     // an unknown id far above the high-water
            (a, EntryId.Temp(PlayerSlot.FromValue(1), 1), 1), // a temp id of slot B's namespace thrown from slot A
            (a, RawEntryId(EntryId.Temp(a, 1).Value | (1u << 23)), 1), // a temp id of A's namespace with reserved bit 23 set
            (a, default, 1),                                  // no entry
            (PlayerSlot.FromValue(1), rig.Entries[inHotbarA].Id, 1), // A's hotbar entry named by slot B
            (PlayerSlot.FromValue(2), rig.Entries[inHotbarA].Id, 1), // a slot with no hotbar
            (PlayerSlot.None, rig.Entries[inHotbarA].Id, 1),  // no slot
        };
        var results = new List<DetachResult>();

        // Act: every refusal in one tick
        rig.Hook.Action = (in TickContext ctx) =>
        {
            foreach ((PlayerSlot s, EntryId e, byte u) in calls)
            {
                results.Add(rig.Inventory.DetachForThrow(s, e, u));
            }
        };
        rig.Host.World.Tick();
        rig.Hook.Action = null;

        // Assert: every call bad_request with no entry, nothing changed, no inventory event
        Assert.Equal(calls.Length, results.Count);
        Assert.All(results, r => Assert.Equal(new DetachResult((InvReason)InvReasons.BadRequest, default, 0), r));
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    [Trait("Id", "H-R7-4")]
    public void DetachForThrow_OfAnOnceAllocatedEntryIdThatIsNoLongerLiveOrAnUnmappedTempIdOfTheThrower_IsStaleEntry_NothingChanges_NoRecord()
    {
        // Arrange: a hotbar entry consumed away (its last unit), and an id allocated and freed, both at or below the high-water; and
        // temp ids of the thrower's own namespace, which no op has mapped (ADR-0009 l.78, l.165)
        var rig = new InvAutoPlaceRig();
        ushort consumed = rig.PlaceMail(rig.HotbarA, "letter", 4, 0, Address4, new uint[] { 7 });
        EntryId consumedId = rig.Entries[consumed].Id;
        Assert.True(IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.ConsumeUnit(new EntryRef(rig.Inventory.HotbarOf(PlayerSlot.FromValue(0)), consumedId), ConsumeReason.Delivered, rig.PlayerA)));
        Assert.True(rig.Entries.TryAllocate(rig.Host.World.CurrentTick, out ushort freed, out _));
        EntryId freedId = rig.Entries[freed].Id;
        rig.Entries.Free(freed);
        _ = rig.PlaceMail(rig.HotbarA, "letter", 2, 0, Address4, new uint[] { 5 });
        Assert.False(rig.Entries.TryGetSlot(consumedId, out _));
        Assert.False(rig.Entries.TryGetSlot(freedId, out _));
        Assert.True(freedId.Value <= rig.Entries.LastId);
        List<long> before = State(rig);
        PlayerSlot a = PlayerSlot.FromValue(0);
        var calls = new (EntryId Entry, byte Units)[]
        {
            (consumedId, 1), (freedId, 1), (EntryId.Temp(a, 1), 1), (EntryId.Temp(a, EntryId.TempCounterMask), 1),
        };
        var results = new List<DetachResult>();

        // Act
        rig.Hook.Action = (in TickContext ctx) =>
        {
            foreach ((EntryId e, byte u) in calls)
            {
                results.Add(rig.Inventory.DetachForThrow(a, e, u));
            }
        };
        rig.Host.World.Tick();
        rig.Hook.Action = null;

        // Assert: stale_entry at once (entry ids have no tombstone; a temp id is never mapped yet), nothing changed, no inventory event
        Assert.Equal(calls.Length, results.Count);
        Assert.All(results, r => Assert.Equal(new DetachResult(InvReason.StaleEntry, default, 0), r));
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    [Trait("Id", "H-X-2")]
    public void DetachForThrow_TheSameEntryTwice_TheDuplicateReleaseIsBadRequest_NothingChanges_NoRecord()
    {
        // Arrange: 3 letters at hotbar index 1, detached once
        var rig = new InvAutoPlaceRig();
        ushort entry = rig.PlaceMail(rig.HotbarA, "letter", 1, 0, Address4, new uint[] { 1, 2, 3 });
        EntryId id = rig.Entries[entry].Id;
        PlayerSlot a = PlayerSlot.FromValue(0);
        Assert.Equal(new DetachResult(default, id, 3), IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.DetachForThrow(a, id, 3)));
        List<long> before = State(rig);

        // Act: the same release again (the entry is live and detached)
        DetachResult r = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.DetachForThrow(a, id, 3));

        // Assert
        Assert.Equal(new DetachResult((InvReason)InvReasons.BadRequest, default, 0), r);
        Assert.Equal(before, State(rig));
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[entry].ContainerSlot);
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    [Trait("Id", "H-X-2")]
    public void DetachForThrow_WrongOnBothChecks_AnswersTheFirstFailingCheck_IdClassThenPlacementThenUnits()
    {
        // Arrange: a 2-letter cursor entry, a 5-letter hotbar entry, a consumed (dead) hotbar entry
        var rig = new InvAutoPlaceRig();
        PlayerSlot a = PlayerSlot.FromValue(0);
        ushort inCursor = rig.PlaceMail(rig.SlotOf(rig.Inventory.CursorOf(a)), "letter", 0, 0, Address4, new uint[] { 1, 2 });
        ushort inHotbar = rig.PlaceMail(rig.HotbarA, "letter", 3, 0, Address4, new uint[] { 3, 4, 5, 6, 7 });
        ushort dead = rig.PlaceMail(rig.HotbarA, "letter", 4, 0, Address4, new uint[] { 8 });
        EntryId deadId = rig.Entries[dead].Id;
        Assert.True(IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.ConsumeUnit(new EntryRef(rig.Inventory.HotbarOf(a), deadId), ConsumeReason.Delivered, rig.PlayerA)));
        List<long> before = State(rig);
        var bad = new DetachResult((InvReason)InvReasons.BadRequest, default, 0);
        var stale = new DetachResult(InvReason.StaleEntry, default, 0);
        var calls = new (PlayerSlot Slot, EntryId Entry, byte Units, DetachResult Expected)[]
        {
            (a, rig.Entries[inCursor].Id, 5, bad),            // not in the hotbar and the wrong units: placement first
            (a, deadId, 5, stale),                           // dead and the wrong units: stale either way
            (a, deadId, 0, stale),
            (a, rig.Entries[inHotbar].Id, 4, stale),          // in the hotbar, the wrong units
            (a, EntryId.Real(rig.Entries.LastId + 1), 5, bad), // above the high-water and the wrong units
            (a, EntryId.Temp(a, 1), 5, stale),                // an unmapped temp id of A's namespace and the wrong units
            (a, EntryId.Temp(PlayerSlot.FromValue(1), 1), 5, bad), // a temp id of B's namespace and the wrong units
            (PlayerSlot.FromValue(2), deadId, 1, bad),        // a slot with no hotbar naming a dead id: the slot first
        };
        var results = new List<DetachResult>();

        // Act
        rig.Hook.Action = (in TickContext ctx) =>
        {
            foreach ((PlayerSlot s, EntryId e, byte u, DetachResult _) in calls)
            {
                results.Add(rig.Inventory.DetachForThrow(s, e, u));
            }
        };
        rig.Host.World.Tick();
        rig.Hook.Action = null;

        // Assert
        Assert.Equal(calls.Select(c => c.Expected), results);
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    [Trait("Id", "H-X-2")]
    public void DetachForThrow_AfterTheSlotRejoinsWithANewPlayer_SealsWithTheNewPlayersActor()
    {
        // Arrange: player B joins slot 1, leaves, and slot 1 is joined again by a new player entity; 2 letters in slot 1's hotbar
        var rig = new InvAutoPlaceRig();
        PlayerSlot b = PlayerSlot.FromValue(1);
        JoinSlot1(rig);
        EntityId first = rig.Host.Services.Slots.Entry(b).PlayerId;
        ContainerId hotbarB = rig.Inventory.HotbarOf(b);
        var leave = new Op { Kind = OpKind.PlayerLeave, ReqId = 3 };
        leave.SetPayload(new PlayerLeavePayload { PlayerId = first.Value });
        Assert.True(rig.Host.World.EnqueueOp(ActorId.System, in leave));
        rig.Host.World.Tick();
        JoinSlot1(rig);
        EntityId rejoined = rig.Host.Services.Slots.Entry(b).PlayerId;
        Assert.NotEqual(first, rejoined);
        Assert.Equal(hotbarB, rig.Inventory.HotbarOf(b));
        ushort hotbarBSlot = rig.SlotOf(hotbarB);
        ushort entry = rig.PlaceMail(hotbarBSlot, "letter", 0, 0, Address4, new uint[] { 1, 2 });
        EntryId id = rig.Entries[entry].Id;

        // Act
        DetachResult r = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.DetachForThrow(b, id, 2));
        EventEntry[] log = InventoryEvents(rig.Sink.Last);

        // Assert: the seal's actor is the rejoined player, not the one that left
        Assert.Equal(new DetachResult(default, id, 2), r);
        Assert.Equal(new[] { InvEventKinds.Removed, InvEventKinds.Version }, log.Select(e => e.Kind));
        InvVersionPayload seal = log[1].PayloadAs<InvVersionPayload>();
        Assert.Equal(hotbarB.Value, seal.Container);
        Assert.Equal(ActorId.FromEntity(rejoined).Value, seal.ActorId);
    }

    [Fact]
    [Trait("Id", "H-X-2")]
    public void DetachForThrow_WithUnitsOtherThanTheEntryCount_IsStaleEntry_NothingChanges_NoRecord()
    {
        // Arrange: 5 letters at hotbar index 3
        var rig = new InvAutoPlaceRig();
        ushort entry = rig.PlaceMail(rig.HotbarA, "letter", 3, 0, Address4, new uint[] { 1, 2, 3, 4, 5 });
        EntryId id = rig.Entries[entry].Id;
        List<long> before = State(rig);
        var results = new List<DetachResult>();

        // Act: 4, 6 and 0 units in one tick
        rig.Hook.Action = (in TickContext ctx) =>
        {
            foreach (byte units in new byte[] { 4, 6, 0 })
            {
                results.Add(rig.Inventory.DetachForThrow(PlayerSlot.FromValue(0), id, units));
            }
        };
        rig.Host.World.Tick();
        rig.Hook.Action = null;

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(new DetachResult(InvReason.StaleEntry, default, 0), r));
        Assert.Equal(before, State(rig));
        Assert.Equal(rig.HotbarA, rig.Entries[entry].ContainerSlot);
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    [Trait("Id", "R9")]
    public void DetachForThrow_OutsideATickPhase_IsRefused_CountsMutatingApiOutOfPhase_AndMutatesNothing()
    {
        // Arrange: between ticks, 5 letters at hotbar index 3
        var rig = new InvAutoPlaceRig();
        ushort entry = rig.PlaceMail(rig.HotbarA, "letter", 3, 0, Address4, new uint[] { 1, 2, 3, 4, 5 });
        EntryId id = rig.Entries[entry].Id;
        List<long> before = State(rig);
        long refusedBefore = rig.Counters[CounterId.MutatingApiOutOfPhase];

        // Act + Assert: SIM_ASSERT builds assert; Release refuses with bad_request
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => rig.Inventory.DetachForThrow(PlayerSlot.FromValue(0), id, 5));
            Assert.Equal(SimAssertCode.MutatingApiOutOfPhase, ex.Code);
        }
        else
        {
            Assert.Equal(new DetachResult((InvReason)InvReasons.BadRequest, default, 0), rig.Inventory.DetachForThrow(PlayerSlot.FromValue(0), id, 5));
        }

        // Assert: nothing mutated, counter 45 + 1, nothing flushed by the next tick
        Assert.Equal(before, State(rig));
        Assert.Equal(refusedBefore + 1, rig.Counters[CounterId.MutatingApiOutOfPhase]);
        rig.Host.World.Tick();
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void DetachForThrow_OfAnItemStack_CountItemDropsByTheStack_AndEqualsAScan()
    {
        // Arrange: 5 planks at hotbar index 3, 2 at index 4
        var rig = new InvAutoPlaceRig();
        ushort planks = rig.PlaceItem(rig.HotbarA, "plank", 3, 0, 5);
        _ = rig.PlaceItem(rig.HotbarA, "plank", 4, 0, 2);
        ContainerId hotbar = rig.Inventory.HotbarOf(PlayerSlot.FromValue(0));
        DefIndex plank = rig.ItemIndex("plank");
        Assert.Equal(7, rig.Inventory.CountItem(new[] { hotbar }, plank));

        // Act
        DetachResult r = IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.DetachForThrow(PlayerSlot.FromValue(0), rig.Entries[planks].Id, 5));

        // Assert: the detached stack no longer counts; the index equals a scan of the hotbar
        Assert.Equal(0, (int)r.Reason);
        Assert.Equal(2, rig.Inventory.CountItem(new[] { hotbar }, plank));
        Assert.Equal(ScanItem(rig, rig.HotbarA, plank), rig.Inventory.CountItem(new[] { hotbar }, plank));
    }

    [Fact]
    [Trait("Id", "H-R5-1")]
    public void TryInsert_IntoPoIntake_OutfeedOnly_ReturnsFalse_NoRecord_NoEntry_VersionUnchanged()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        UnitRef letter = Letter(rig, 501, Address4);
        List<long> before = State(rig);
        uint version = rig.Header(rig.Intake).Version;

        // Act
        bool inserted = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TryInsert(rig.Inventory.IntakeContainer, in letter, rig.PlayerA));

        // Assert: refused; no entry (pool and last id unchanged), no arena block, no record, version unchanged
        Assert.False(inserted);
        Assert.Equal(before, State(rig));
        Assert.Equal(version, rig.Header(rig.Intake).Version);
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    [Trait("Id", "H-R5-1")]
    public void TryInsert_ALetterIntoAContainerWhoseWhitelistRefusesIt_ReturnsFalse_NothingChanges()
    {
        // Arrange: an empty fixture tool rack (allowedCategories [tool])
        var rig = new InvAutoPlaceRig(content: RefusingContent());
        ushort rack = rig.CreateGrid(4, 2, ToolRackDef);
        ContainerId rackId = rig.Header(rack).Id;
        UnitRef letter = Letter(rig, 501, Address4);
        List<long> before = State(rig);

        // Act
        bool inserted = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TryInsert(rackId, in letter, rig.PlayerA));

        // Assert
        Assert.False(inserted);
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    [Trait("Id", "H-X-3")]
    public void TryInsert_ALetterWithAMatchingNonFullStack_MergesFirst_OneMergedRecord_VersionPlusOne_NoEntryCreated()
    {
        // Arrange: a 2-letter stack (address 4) at (5, 1) of player A's inventory, free cells before it
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 5, 1, Address4, new uint[] { 10, 20 });
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        UnitRef letter = Letter(rig, 15, Address4);
        ContainerHeader before = rig.Header(rig.InventoryA);
        uint lastId = rig.Entries.LastId;
        int liveEntries = rig.Entries.LiveCount;

        // Act
        bool inserted = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TryInsert(inventoryA, in letter, rig.PlayerA));
        EventEntry[] heads = InventoryEvents(rig.Sink.Last).Where(e => (e.Flags & EventFlags.Continuation) == 0).ToArray();

        // Assert: merged into the stack (ids sorted), no entry created, version + 1
        Assert.True(inserted);
        Assert.Equal(new uint[] { 10, 15, 20 }, rig.IdsOf(stack));
        Assert.Equal(lastId, rig.Entries.LastId);
        Assert.Equal(liveEntries, rig.Entries.LiveCount);
        Assert.Equal(before.EntryCount, rig.Header(rig.InventoryA).EntryCount);
        Assert.Equal(before.FreeCells, rig.Header(rig.InventoryA).FreeCells);
        Assert.Equal(before.Version + 1, rig.Header(rig.InventoryA).Version);

        // Assert: exactly one InvMerged{inventoryA, stack, 1} then one InvVersion seal by player A
        Assert.Equal(new[] { InvEventKinds.Merged, InvEventKinds.Version }, heads.Select(e => e.Kind));
        InvMergedPayload merged = heads[0].PayloadAs<InvMergedPayload>();
        Assert.Equal(inventoryA.Value, merged.Container);
        Assert.Equal(rig.Entries[stack].Id.Value, merged.TargetEntryId);
        Assert.Equal(1, merged.AddedCount);
        InvVersionPayload seal = heads[1].PayloadAs<InvVersionPayload>();
        Assert.Equal(before.Version + 1, seal.VersionTo);
        Assert.Equal(rig.PlayerA.Value, seal.ActorId);
    }

    [Fact]
    [Trait("Id", "H-X-3")]
    public void TryInsert_OntoA19LetterStack_MergesTo20_ThenTheNextLetterDoesNotMergeOntoTheFullStack()
    {
        // Arrange: a 19-letter stack (address 4, effMax 20) at (0, 0) of player A's inventory
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(1, 19));
        Assert.Equal(20, rig.SubjectOf(stack).EffMax);
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        UnitRef twentieth = Letter(rig, 500, Address4);
        UnitRef next = Letter(rig, 501, Address4);
        int entries = rig.Header(rig.InventoryA).EntryCount;

        // Act: effMax - 1 -> effMax
        bool merged = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TryInsert(inventoryA, in twentieth, rig.PlayerA));
        EventKind[] mergeHeads = InventoryEvents(rig.Sink.Last).Where(e => (e.Flags & EventFlags.Continuation) == 0).Select(e => e.Kind).ToArray();
        bool placed = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TryInsert(inventoryA, in next, rig.PlayerA));
        EventKind[] placeHeads = InventoryEvents(rig.Sink.Last).Where(e => (e.Flags & EventFlags.Continuation) == 0).Select(e => e.Kind).ToArray();

        // Assert: the stack became full through one InvMerged; the next letter got a new entry and the full stack is unchanged
        Assert.True(merged);
        Assert.Equal(new[] { InvEventKinds.Merged, InvEventKinds.Version }, mergeHeads);
        Assert.True(placed);
        Assert.Equal(new[] { InvEventKinds.Inserted, InvEventKinds.Version }, placeHeads);
        Assert.Equal(20, rig.Entries[stack].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(1, 19).Append(500u), rig.IdsOf(stack));
        Assert.Equal(entries + 1, rig.Header(rig.InventoryA).EntryCount);
    }

    [Fact]
    [Trait("Id", "H-R5-1")]
    public void TryInsert_ALetterWithNoMatchingStack_PlacesANewEntryInTheFirstFreeCell_OneInsertedRecord()
    {
        // Arrange: a full 20-letter stack (address 4) at (0, 0) and a 1-letter stack of another address at (1, 0)
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(100, 20));
        _ = rig.PlaceMail(rig.InventoryA, "letter", 1, 0, 5u, new uint[] { 300 });
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        UnitRef letter = Letter(rig, 501, Address4);
        ContainerHeader before = rig.Header(rig.InventoryA);

        // Act
        bool inserted = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TryInsert(inventoryA, in letter, rig.PlayerA));
        EventEntry[] heads = InventoryEvents(rig.Sink.Last).Where(e => (e.Flags & EventFlags.Continuation) == 0).ToArray();

        // Assert: a new one-unit entry at (2, 0) with the unit's StackKey, version + 1, one InvInserted then one seal
        Assert.True(inserted);
        ushort placed = rig.EntryAt(rig.InventoryA, 2, 0);
        Assert.NotEqual(Occupancy.Free, placed);
        Assert.Equal(new uint[] { 501 }, rig.IdsOf(placed));
        Assert.Equal(Address4, rig.Entries[placed].AddressId);
        Assert.Equal(before.EntryCount + 1, rig.Header(rig.InventoryA).EntryCount);
        Assert.Equal(before.Version + 1, rig.Header(rig.InventoryA).Version);
        Assert.Equal(new[] { InvEventKinds.Inserted, InvEventKinds.Version }, heads.Select(e => e.Kind));
    }

    [Fact]
    public void TryInsert_WithNoRoomOrAMalformedUnitOrADeadContainer_ReturnsFalse_NothingChanges_NoEntryOrArenaLeak()
    {
        // Arrange: a 1 x 1 grid holding a full stack of another address
        var rig = new InvAutoPlaceRig();
        ushort tiny = rig.CreateGrid(1, 1);
        _ = rig.PlaceMail(tiny, "letter", 0, 0, 5u, InvAutoPlaceRig.Ids(100, 20));
        ContainerId tinyId = rig.Header(tiny).Id;
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        UnitRef ok = Letter(rig, 501, Address4);
        var calls = new (ContainerId C, UnitRef U)[]
        {
            (tinyId, ok),                                                       // no room (and no matching stack)
            (inventoryA, ok with { Mail = MailId.None }),                       // no mail id
            (inventoryA, ok with { MailKind = DefIndex.None }),                 // no mail kind
            (inventoryA, ok with { MailKind = DefIndex.FromValue((ushort)rig.Content.MailKinds.Count) }), // unknown kind
            (new ContainerId(999), ok),                                         // a dead container
        };
        List<long> before = State(rig);
        var results = new List<bool>();

        // Act
        rig.Hook.Action = (in TickContext ctx) =>
        {
            foreach ((ContainerId c, UnitRef u) in calls)
            {
                results.Add(rig.Inventory.TryInsert(c, in u, rig.PlayerA));
            }
        };
        rig.Host.World.Tick();
        rig.Hook.Action = null;

        // Assert
        Assert.Equal(calls.Length, results.Count);
        Assert.All(results, Assert.False);
        Assert.Equal(before, State(rig));
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void TryInsert_WithTheEntryPoolExhaustedAndNoMergeCandidate_ReturnsFalse_NothingChanges_NoEntryOrArenaLeak()
    {
        // Arrange: a 64-entry pool filled by 64 planks in an 8 x 8 grid; player A's inventory is empty, so the letter would need a new entry
        var rig = new InvAutoPlaceRig(entryPoolCapacity: 64);
        ushort grid = rig.CreateGrid(8, 8);
        for (int i = 0; i < 64; i++)
        {
            _ = rig.PlaceItem(grid, "plank", i % 8, i / 8, 1);
        }

        Assert.Equal(rig.Entries.Capacity, rig.Entries.LiveCount);
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        UnitRef letter = Letter(rig, 501, Address4);
        SimCheckRing ring = rig.Host.World.CheckRing;
        int ringBefore = ring.Count;
        List<long> before = State(rig);

        // Act
        bool inserted = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TryInsert(inventoryA, in letter, rig.PlayerA));

        // Assert: refused at the pool (its one InventoryEntryPoolExhausted record); entries, LastId, arena blocks, versions unchanged
        Assert.False(inserted);
        Assert.Equal(before, State(rig));
        Assert.Equal(ringBefore + 1, ring.Count);
        Assert.Equal(CheckCode.InventoryEntryPoolExhausted, ring[ring.Count - 1].Check);
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void TryInsert_WhenTheArenaReservationFails_ReturnsFalse_NothingChanges_NoEntryOrArenaLeak_NoCheckRecord()
    {
        // Arrange: a matching 2-letter stack filling a 1 x 1 grid (no free cell, so the merge pass is the only way in) and an empty
        // player inventory (the new-entry path), on a minimum-size arena; the control rig leaves the arena free, the test rig takes
        // every free block, so neither the union block nor a new one-unit block can be reserved (reason 85)
        (InvAutoPlaceRig control, ContainerId controlGrid, ushort controlStack) = ArenaRig(exhaustArena: false);
        (InvAutoPlaceRig rig, ContainerId gridId, ushort stack) = ArenaRig(exhaustArena: true);
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        UnitRef letter = Letter(rig, 15, Address4);
        var plan = new InvPlan();
        _ = plan.AddAcquiredBlock(1);
        Assert.Equal((ushort)InvReason.CapacityExhausted, rig.Inventory.RequestCheck.CheckArenaReservation(in plan));
        SimCheckRing ring = rig.Host.World.CheckRing;
        int ringBefore = ring.Count;
        List<long> before = State(rig);
        var results = new List<bool>();

        // Act: the control first (its free arena lets the merge commit), then both calls on the exhausted rig
        bool controlInserted = IntakeSpawnOutfeedTests.InPhase(
            control, () => control.Inventory.TryInsert(controlGrid, in letter, control.PlayerA));
        EventEntry[] controlHeads = InventoryEvents(control.Sink.Last).Where(e => (e.Flags & EventFlags.Continuation) == 0).ToArray();
        rig.Hook.Action = (in TickContext ctx) =>
        {
            results.Add(rig.Inventory.TryInsert(gridId, in letter, rig.PlayerA));     // merge pass: the union block
            results.Add(rig.Inventory.TryInsert(inventoryA, in letter, rig.PlayerA)); // new entry: its one-unit block
        };
        rig.Host.World.Tick();
        rig.Hook.Action = null;

        // Assert: the control reached the grid only through the merge (InvMerged, ids unioned), so the test rig's grid call was
        // refused at the merge's union-block reservation, not at the new-entry fallback (which has no free cell to reach)
        Assert.True(controlInserted);
        Assert.Equal(new[] { InvEventKinds.Merged, InvEventKinds.Version }, controlHeads.Select(e => e.Kind));
        Assert.Equal(new uint[] { 10, 15, 20 }, control.IdsOf(controlStack));

        // Assert: both refused by the dry-run reservation (no check record, no entry allocated); arena blocks and LastId unchanged
        Assert.Equal(new[] { false, false }, results);
        Assert.Equal(new uint[] { 10, 20 }, rig.IdsOf(stack));
        Assert.Equal(before, State(rig));
        Assert.Equal(ringBefore, ring.Count);
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    [Trait("Id", "R9")]
    public void TryInsert_OutsideATickPhase_IsRefused_CountsMutatingApiOutOfPhase_AndMutatesNothing()
    {
        // Arrange: between ticks, a matching 2-letter stack
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, new uint[] { 1, 2 });
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        UnitRef letter = Letter(rig, 3, Address4);
        List<long> before = State(rig);
        long refusedBefore = rig.Counters[CounterId.MutatingApiOutOfPhase];

        // Act + Assert: SIM_ASSERT builds assert; Release refuses
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => rig.Inventory.TryInsert(inventoryA, in letter, rig.PlayerA));
            Assert.Equal(SimAssertCode.MutatingApiOutOfPhase, ex.Code);
        }
        else
        {
            Assert.False(rig.Inventory.TryInsert(inventoryA, in letter, rig.PlayerA));
        }

        // Assert
        Assert.Equal(before, State(rig));
        Assert.Equal(refusedBefore + 1, rig.Counters[CounterId.MutatingApiOutOfPhase]);
        rig.Host.World.Tick();
        Assert.Empty(InventoryEvents(rig.Sink.Last));
    }

    [Fact]
    public void TryInsert_OfMail_LeavesCountItemUnchanged_AndEqualToAScan()
    {
        // Arrange: planks and an axe (item def 0, the value a mail entry's ItemId field also holds) beside a letter stack
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 7);
        _ = rig.PlaceItem(rig.InventoryA, "axe", 1, 0, 1);
        _ = rig.PlaceMail(rig.InventoryA, "letter", 2, 0, Address4, new uint[] { 1 });
        ContainerId inventoryA = rig.Inventory.PlayerInventoryOf(PlayerSlot.FromValue(0));
        DefIndex plank = rig.ItemIndex("plank");
        DefIndex axe = rig.ItemIndex("axe");
        UnitRef merge = Letter(rig, 2, Address4);
        UnitRef place = Letter(rig, 3, 5u);

        // Act: one merge, one new entry
        Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TryInsert(inventoryA, in merge, rig.PlayerA)));
        Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TryInsert(inventoryA, in place, rig.PlayerA)));

        // Assert
        Assert.Equal(7, rig.Inventory.CountItem(new[] { inventoryA }, plank));
        Assert.Equal(1, rig.Inventory.CountItem(new[] { inventoryA }, axe));
        Assert.Equal(ScanItem(rig, rig.InventoryA, plank), rig.Inventory.CountItem(new[] { inventoryA }, plank));
        Assert.Equal(ScanItem(rig, rig.InventoryA, axe), rig.Inventory.CountItem(new[] { inventoryA }, axe));
    }

    /// <summary>The fixture def <see cref="RefusingContent"/> adds.</summary>
    internal const string ToolRackDef = "tool_rack";

    /// <summary>The M-1a content plus <see cref="ToolRackDef"/>: a grid with <c>allowedCategories [tool]</c>, which refuses mail (none at M-1a).</summary>
    internal static ContentSet RefusingContent()
    {
        RuleRegistry rules = RuleRegistry.CreateDefault();
        BalanceBuildResult balance = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries(), rules);
        Assert.NotNull(balance.Table);
        ImmutableArray<ContainerDef> containers = BuiltInContent.M1aContainers().Add(new ContainerDef
        {
            Id = ToolRackDef,
            Layout = ContainerLayout.Grid,
            HasGrid = true,
            GridW = 4,
            GridH = 2,
            View = ContainerView.Grid,
            BeltAccess = BeltAccess.None,
            AcceptsInsert = true,
            HasAllowedCategories = true,
            AllowedCategories = ImmutableArray.Create(ItemCategory.Tool),
        });
        ContentBuildResult built = BuiltInContent.Build(
            balance.Table!, containers, BuiltInContent.M1aItems(), BuiltInContent.M1aMailKinds(), BuiltInContent.M1aDestinationTypes(), rules);
        Assert.True(built.Set is not null, BuiltInContent.Describe(built.Validation, rules));
        return built.Set!;
    }

    /// <summary>A one-letter <see cref="UnitRef"/> (its entry field unused by <c>TryInsert</c>).</summary>
    internal static UnitRef Letter(InvAutoPlaceRig rig, uint mailId, uint address) =>
        new(new MailId(mailId), default, rig.KindIndex("letter"), address, 0);

    /// <summary>Joins player slot 1 (one tick).</summary>
    internal static void JoinSlot1(InvAutoPlaceRig rig)
    {
        var join = new Op { Kind = OpKind.PlayerJoin, ReqId = 2 };
        join.SetPayload(new PlayerJoinPayload { Slot = 1 });
        Assert.True(rig.Host.World.EnqueueOp(ActorId.System, in join));
        rig.Host.World.Tick();
    }

    /// <summary>
    /// The first <paramref name="count"/> (container, item) keys whose home slot in a <paramref name="tableSize"/>-slot
    /// <see cref="ItemCountIndex"/> is <paramref name="home"/>: the index's own Fibonacci hash (top log2(size) bits of
    /// <c>key * 0x9E3779B97F4A7C15</c>, key <c>(container + 1) &lt;&lt; 16 | item</c>), restated so a fixture can aim at the table's end.
    /// A drift from the index's hash shows as the probe-count assertions failing.
    /// </summary>
    private static List<(ushort C, ushort I)> KeysWithHome(int home, int count, int tableSize)
    {
        int shift = 64 - System.Numerics.BitOperations.Log2((uint)tableSize);
        var keys = new List<(ushort C, ushort I)>();
        for (ushort c = 0; c < 64 && keys.Count < count; c++)
        {
            for (ushort i = 0; i < 64 && keys.Count < count; i++)
            {
                uint key = ((uint)(c + 1) << 16) | i;
                if ((int)(unchecked((ulong)key * 0x9E37_79B9_7F4A_7C15UL) >> shift) == home)
                {
                    keys.Add((c, i));
                }
            }
        }

        Assert.Equal(count, keys.Count);
        return keys;
    }

    private static long ProbesOf(ItemCountIndex index, SimCounters counters, (ushort C, ushort I) key)
    {
        long before = counters[CounterId.InventoryItemCountProbes];
        Assert.NotEqual(0, index.Units(key.C, key.I));
        return counters[CounterId.InventoryItemCountProbes] - before;
    }

    /// <summary>Spends entry ids between ticks (allocate and free) until the last id is <paramref name="lastId"/>.</summary>
    private static void BurnEntryIdsTo(InvAutoPlaceRig rig, uint lastId)
    {
        while (rig.Entries.LastId < lastId)
        {
            Assert.True(rig.Entries.TryAllocate(rig.Host.World.CurrentTick, out ushort slot, out _));
            rig.Entries.Free(slot);
        }
    }

    /// <summary>An <see cref="EntryId"/> of any raw bits, as a wire decode yields it (<see cref="EntryId.FromValue"/> asserts on reserved bits).</summary>
    private static EntryId RawEntryId(uint raw) => Unsafe.As<uint, EntryId>(ref raw);

    /// <summary>A brute-force scan of the container's placed item entries of <paramref name="item"/>.</summary>
    private static int ScanItem(InvAutoPlaceRig rig, ushort container, DefIndex item)
    {
        int units = 0;
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            EntryRecord r = rig.Entries[(ushort)s];
            if (r.IsLive && !r.IsMail && r.ContainerSlot == container && r.ItemId == item.Value)
            {
                units += r.Count;
            }
        }

        return units;
    }

    /// <summary>Every live container's id (the panels, the Intake and the test's grids).</summary>
    private static ContainerId[] AllContainers(InvAutoPlaceRig rig)
    {
        var ids = new ContainerId[rig.Inventory.Containers.LiveCount];
        for (int s = 0; s < ids.Length; s++)
        {
            ids[s] = rig.Header((ushort)s).Id;
        }

        return ids;
    }

    private static DefIndex[] ItemDefs(InvAutoPlaceRig rig) =>
        Enumerable.Range(0, rig.Content.Items.Count).Select(i => DefIndex.FromValue((ushort)i)).ToArray();

    /// <summary>Places <paramref name="item"/> over cells (x, y) and (x, y + 1): one 1 x 2 entry, or two 1 x 1 entries.</summary>
    private static void FillPair(InvAutoPlaceRig rig, ushort grid, DefIndex item, int x, int y)
    {
        ItemDef def = rig.Content.Items[item];
        _ = rig.PlaceItem(grid, def.Id, x, y, 1);
        if (def.GridH == 1)
        {
            _ = rig.PlaceItem(grid, def.Id, x, y + 1, 1);
        }
    }

    /// <summary>The most probes one single-container <c>CountItem</c> read, over every container and item, split by hit and miss.</summary>
    private static (int MaxHit, int MaxMiss) MaxProbes(InvAutoPlaceRig rig, ContainerId[] containers, DefIndex[] items)
    {
        int maxHit = 0;
        int maxMiss = 0;
        foreach (ContainerId c in containers)
        {
            foreach (DefIndex item in items)
            {
                int probes = (int)Probes(rig, c, item, out int units);
                if (units > 0)
                {
                    maxHit = Math.Max(maxHit, probes);
                }
                else
                {
                    maxMiss = Math.Max(maxMiss, probes);
                }
            }
        }

        return (maxHit, maxMiss);
    }

    /// <summary>A minimum-size arena rig with a 2-letter stack (address 4) filling a 1 x 1 grid; with <paramref name="exhaustArena"/>, every free arena block is then taken.</summary>
    private static (InvAutoPlaceRig Rig, ContainerId Grid, ushort Stack) ArenaRig(bool exhaustArena)
    {
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits);
        ushort grid = rig.CreateGrid(1, 1);
        ushort stack = rig.PlaceMail(grid, "letter", 0, 0, Address4, new uint[] { 10, 20 });
        Assert.Equal(0, rig.Header(grid).FreeCells);
        MailIdArena arena = rig.Inventory.MailIds;
        uint filler = 1_000_000;
        while (exhaustArena && arena.CanReserve(1))
        {
            uint id = filler++;
            Assert.True(arena.TryCreate(new ReadOnlySpan<uint>(in id), rig.Host.World.CurrentTick, out _));
        }

        return (rig, rig.Header(grid).Id, stack);
    }

    private static long Probes(InvAutoPlaceRig rig, ContainerId c, DefIndex item, out int units)
    {
        long before = rig.Counters[CounterId.InventoryItemCountProbes];
        units = rig.Inventory.CountItem(new[] { c }, item);
        return rig.Counters[CounterId.InventoryItemCountProbes] - before;
    }

    private static EventEntry[] InventoryEvents(EventEntry[] log) =>
        log.Where(e => e.Kind is >= InvEventKinds.Inserted and <= InvEventKinds.Version).ToArray();

    /// <summary>Every container header and occupancy owner, the entry pool (its LastId included), the arena's live units, live blocks and free blocks per class, and the index's key count, as one comparable list.</summary>
    private static List<long> State(InvAutoPlaceRig rig)
    {
        var values = new List<long>();
        ContainerTable containers = rig.Inventory.Containers;
        for (int s = 0; s < containers.LiveCount; s++)
        {
            ContainerHeader h = containers[(ushort)s];
            values.AddRange(new long[] { h.Version, h.EntryCount, h.FreeCells, h.NextArrivalSeq });
            for (int y = 0; y < h.H; y++)
            {
                for (int x = 0; x < h.W; x++)
                {
                    values.Add(rig.Inventory.Occupancy.OwnerAt((ushort)s, h.W, x, y));
                }
            }
        }

        values.Add(rig.Entries.LastId);
        values.Add(rig.Entries.LiveCount);
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            EntryRecord r = rig.Entries[(ushort)s];
            if (r.IsLive)
            {
                values.AddRange(new long[] { s, r.Id.Value, r.Count, r.ContainerSlot, r.X, r.Y, r.MailIds.Raw });
            }
        }

        MailIdArena arena = rig.Inventory.MailIds;
        values.Add(arena.LiveUnits);
        values.Add(arena.LiveBlocks);
        for (int c = 0; c < MailIdArena.ClassCount; c++)
        {
            values.Add(arena.FreeBlocks(c));
        }

        values.Add(rig.Inventory.Mutator.ItemCounts.KeyCount);
        return values;
    }

    /// <summary>
    /// The seeded mutation generator of the CountItem property test: <c>Place</c>, <c>Remove</c>, <c>Transfer</c> (within and across
    /// containers), <c>MergeInto</c> (placed and detached sources) and <c>TakeOldest</c> through the mutator, one per tick, on player A's
    /// inventory and hotbar and a 6 x 4 grid; items mostly, some mail. After each mutation <c>CountItem</c> of each container alone and
    /// of all three is checked against a scan of the entry pool for every item def. A fixed xorshift, so a failure reproduces from its
    /// seed and mutation number.
    /// </summary>
    private sealed class CountDriver
    {
        private static readonly string[] _items = { "axe", "plank", "fiber", "log", "plank" };

        private readonly InvAutoPlaceRig _rig;
        private readonly ushort[] _containers;
        private readonly ContainerId[] _ids;
        private readonly DefIndex[] _defs;
        private readonly uint[] _taken = new uint[256];
        private uint _state;
        private uint _nextMailId = 1;

        public CountDriver(InvAutoPlaceRig rig, uint seed)
        {
            _rig = rig;
            _state = seed;
            _containers = new[] { rig.InventoryA, rig.HotbarA, rig.CreateGrid(6, 4) };
            _ids = _containers.Select(c => rig.Inventory.Containers[c].Id).ToArray();
            _defs = Enumerable.Range(0, rig.Content.Items.Count).Select(i => DefIndex.FromValue((ushort)i)).ToArray();
        }

        public int Mutations { get; private set; }

        public int Places { get; private set; }

        public int Removes { get; private set; }

        public int Transfers { get; private set; }

        public int CrossTransfers { get; private set; }

        public int Merges { get; private set; }

        public int DetachedMerges { get; private set; }

        public int Takes { get; private set; }

        public int MaxUnitsSeen { get; private set; }

        public void Run(int mutations)
        {
            while (Mutations < mutations)
            {
                int roll = Next(100);
                bool done = roll switch
                {
                    < 30 => DoPlace(),
                    < 42 => DoRemove(),
                    < 62 => DoTransfer(),
                    < 80 => DoMerge(),
                    _ => DoTake(),
                };

                if (!done)
                {
                    continue; // nothing to do this roll (no fit, no candidate): not a mutation
                }

                Mutations++;
                AssertCountsMatchScan();
            }
        }

        private uint NextRaw()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        private int Next(int bound) => (int)(NextRaw() % (uint)bound);

        private ushort NewDetached()
        {
            if (Next(6) == 0)
            {
                int count = 1 + Next(5);
                uint[] ids = InvAutoPlaceRig.Ids(_nextMailId, count);
                _nextMailId += (uint)count;
                return _rig.DetachedMail("letter", Address4, ids);
            }

            string item = _items[Next(_items.Length)];
            return _rig.DetachedItem(item, (byte)(1 + Next(_rig.Item(item).EffMax)));
        }

        private bool DoPlace()
        {
            ushort entry = NewDetached();
            ushort container = _containers[Next(_containers.Length)];
            if (!TryFindFit(entry, container, out int x, out int y))
            {
                _rig.FreeDetached(entry);
                return false;
            }

            _rig.PlaceDetached(entry, container, x, y);
            Places++;
            return true;
        }

        private bool DoRemove()
        {
            if (!TryPickPlaced(out ushort entry))
            {
                return false;
            }

            ushort from = _rig.Entries[entry].ContainerSlot;
            Run(
                (ref InvPlan p) => p.Touch(from) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => _rig.Inventory.Mutator.Remove(ref p, entry));
            _rig.FreeDetached(entry);
            Removes++;
            return true;
        }

        private bool DoTransfer()
        {
            if (!TryPickPlaced(out ushort entry))
            {
                return false;
            }

            ushort from = _rig.Entries[entry].ContainerSlot;
            ushort to = _containers[Next(_containers.Length)];
            if (!TryFindFit(entry, to, out int x, out int y))
            {
                return false;
            }

            Run(
                (ref InvPlan p) => p.Touch(from) < 0 || p.Touch(to) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => _rig.Inventory.Mutator.Transfer(ref p, entry, to, (byte)x, (byte)y, false));
            Transfers++;
            CrossTransfers += from != to ? 1 : 0;
            return true;
        }

        private bool DoMerge()
        {
            bool detachedSource = Next(4) == 0;
            ushort source;
            if (detachedSource)
            {
                source = NewDetached();
            }
            else if (!TryPickPlaced(out source))
            {
                return false;
            }

            StackKey key = StackKey.Of(in _rig.Entries[source]);
            int effMax = _rig.Inventory.Mutator.EffMaxOf(in _rig.Entries[source]);
            ushort s = source;
            List<ushort> targets = Placed().Where(t => t != s && StackKey.Of(in _rig.Entries[t]) == key && _rig.Entries[t].Count < effMax).ToList();
            if (targets.Count == 0)
            {
                if (detachedSource)
                {
                    _rig.FreeDetached(source);
                }

                return false;
            }

            ushort target = targets[Next(targets.Count)];
            int units = 1 + Next(Math.Min(effMax - _rig.Entries[target].Count, _rig.Entries[source].Count));
            ushort from = _rig.Entries[source].ContainerSlot;
            ushort to = _rig.Entries[target].ContainerSlot;
            Run(
                (ref InvPlan p) => (from != EntryRecord.NoContainer && p.Touch(from) < 0) || p.Touch(to) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => _rig.Inventory.Mutator.MergeInto(ref p, s, target, units));
            if (detachedSource && _rig.Entries[source].IsLive)
            {
                _rig.FreeDetached(source); // the unmerged rest of a detached source leaves the test
            }

            Merges++;
            DetachedMerges += detachedSource ? 1 : 0;
            return true;
        }

        private bool DoTake()
        {
            if (!TryPickPlaced(out ushort entry))
            {
                return false;
            }

            int k = 1 + Next(_rig.Entries[entry].Count);
            ushort from = _rig.Entries[entry].ContainerSlot;
            int took = 0;
            Run(
                (ref InvPlan p) => p.Touch(from) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => took = _rig.Inventory.Mutator.TakeOldest(ref p, entry, k, _taken));
            Assert.Equal(k, took);
            Takes++;
            return true;
        }

        private void Run(InvPlanStep validate, InvCommitStep commit) => Assert.True(_rig.Step(validate, commit).IsOk);

        private void AssertCountsMatchScan()
        {
            foreach (DefIndex def in _defs)
            {
                int total = 0;
                for (int c = 0; c < _containers.Length; c++)
                {
                    int scan = Scan(_containers[c], def);
                    total += scan;
                    int read = _rig.Inventory.CountItem(new[] { _ids[c] }, def);
                    Assert.True(read == scan, $"mutation {Mutations}: CountItem({c}, {def.Value}) = {read}, scan {scan}");
                }

                Assert.Equal(total, _rig.Inventory.CountItem(_ids, def));
                MaxUnitsSeen = Math.Max(MaxUnitsSeen, total);
            }
        }

        /// <summary>The brute force: every live item entry of the def whose container is <paramref name="container"/>.</summary>
        private int Scan(ushort container, DefIndex def)
        {
            int units = 0;
            for (int s = 0; s < _rig.Entries.Capacity; s++)
            {
                ref EntryRecord e = ref _rig.Entries[(ushort)s];
                if (e.IsLive && !e.IsMail && e.ContainerSlot == container && e.ItemId == def.Value)
                {
                    units += e.Count;
                }
            }

            return units;
        }

        private List<ushort> Placed()
        {
            var list = new List<ushort>();
            foreach (ushort c in _containers)
            {
                ContainerHeader h = _rig.Header(c);
                for (int y = 0; y < h.H; y++)
                {
                    for (int x = 0; x < h.W; x++)
                    {
                        ushort e = _rig.EntryAt(c, x, y);
                        if (e != Occupancy.Free)
                        {
                            list.Add(e);
                        }
                    }
                }
            }

            return list;
        }

        private bool TryPickPlaced(out ushort entry)
        {
            List<ushort> placed = Placed();
            entry = placed.Count == 0 ? (ushort)0 : placed[Next(placed.Count)];
            return placed.Count > 0;
        }

        private bool TryFindFit(ushort entry, ushort container, out int foundX, out int foundY)
        {
            ContainerHeader h = _rig.Header(container);
            EntryRecord e = _rig.Entries[entry];
            bool slots = h.Layout == ContainerLayout.Slots;
            int fw = slots ? 1 : e.W;
            int fh = slots ? 1 : e.H;
            ushort self = e.ContainerSlot == container ? entry : Occupancy.Free;
            int start = Next(h.W * h.H);
            for (int k = 0; k < h.W * h.H; k++)
            {
                int i = (start + k) % (h.W * h.H);
                if (CellsFree(container, in h, i % h.W, i / h.W, fw, fh, self))
                {
                    foundX = i % h.W;
                    foundY = i / h.W;
                    return true;
                }
            }

            foundX = 0;
            foundY = 0;
            return false;
        }

        private bool CellsFree(ushort container, in ContainerHeader h, int x, int y, int fw, int fh, ushort self)
        {
            if (x + fw > h.W || y + fh > h.H)
            {
                return false;
            }

            for (int r = 0; r < fh; r++)
            {
                for (int c = 0; c < fw; c++)
                {
                    ushort owner = _rig.Inventory.Occupancy.OwnerAt(container, h.W, x + c, y + r);
                    if (owner != Occupancy.Free && owner != self)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
