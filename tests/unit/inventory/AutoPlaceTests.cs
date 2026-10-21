using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.Content;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/009 (GDD R8, R3, R15; ADR-0009 sections 2, 3 and 5; TR-inventory-007): AutoPlace's passes M / U / R in
/// pass-first and target-first order, the StackKey non-full index, the negative-fit cache, the count-change primitive
/// <see cref="InventoryMutator.MergeInto"/>, and the TD-116 merge-count bound. Everything is arranged through the real mutator in a
/// composed world (<see cref="InvAutoPlaceRig"/>), so the index is maintained as in production; AutoPlace runs as a TEST-ONLY op
/// (<see cref="InvFixtureActionHandler"/>: plan, the real reservation step, commit). Pure planning checks call
/// <see cref="AutoPlace.Plan"/> directly between ticks (it raises nothing). The bike/chest/backpack of the GDD tests are fixture
/// instances of the unrestricted <c>player_inventory</c> def (no such defs at M-1a); "#2" is the opaque address 2.
/// </summary>
public class AutoPlaceTests
{
    private const uint Address2 = 2u;
    private const uint Address9 = 9u;

    /// <summary>sizeof(InvPlan), measured: 4 x 2 B touched slots, 263 x 2 B block sizes, counts, primaries, placement, actor, cause.</summary>
    private const int InvPlanSize = 568;

    /// <summary>sizeof(AutoPlacePlan), measured: 255 x 2 B merge targets + 255 x 1 B merge units, 16 x 12 B no-fit facts, counts, placement, ints.</summary>
    private const int AutoPlacePlanSize = 988;

    /// <summary>GDD D3's hinted-pack bound on the 20 x 16 Intake in cell probes: C x sum(a) + n x a &lt;= 320 x 25 + 320.</summary>
    private const long D3IntakeBound = 8_320;

    /// <summary>The H-PERF-6 b/c gate in cell probes (one Intake sort or drain pass); not lowered (Evan, 2026-10-21).</summary>
    private const long PerfGateCellProbes = 8_500;

    /// <summary>The v1 mail kinds (GDD D3): letter 1 x 1, small 1 x 2, medium 2 x 2, large 2 x 4 - six oriented shapes, areas summing to 25.</summary>
    private static readonly string[] _v1MailKinds = { "letter", "small", "medium", "large" };

    // ---------------------------------------------------------------- H-R8-1

    [Fact]
    [Trait("Id", "H-R8-1")]
    public void HR81_ThreeLettersToTwo_LowestMergesIntoInventoryTo20_TwoIntoSecondGridTo12_NoNewEntry_ViaMergeInto()
    {
        // Arrange: inventory 19 letters to #2 at (5,1), (0,0) free; second grid 10 letters to #2 at (0,0); a detached 3-letter entry
        var rig = new InvAutoPlaceRig();
        ushort second = rig.CreateGrid(8, 4);
        ushort invStack = rig.PlaceMail(rig.InventoryA, "letter", 5, 1, Address2, InvAutoPlaceRig.Ids(1, 19));
        ushort secondStack = rig.PlaceMail(second, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(100, 10));
        ushort source = rig.DetachedMail("letter", Address2, new uint[] { 200, 201, 202 });
        int liveBefore = rig.Entries.LiveCount;
        uint invVersion = rig.Header(rig.InventoryA).Version;
        uint secondVersion = rig.Header(second).Version;

        // Act
        ushort reason = rig.AutoPlace(source, rig.InventoryA, second);
        EventEntry[] log = rig.LastLog;

        // Assert: the plan (merges in PASS M order, no free-cell placement)
        Assert.Equal(0, reason);
        AutoPlacePlan plan = rig.LastPlan;
        Assert.Equal(2, plan.MergeCount);
        Assert.Equal(invStack, plan.Merge(0).TargetEntrySlot);
        Assert.Equal(1, plan.Merge(0).Units);
        Assert.Equal(secondStack, plan.Merge(1).TargetEntrySlot);
        Assert.Equal(2, plan.Merge(1).Units);
        Assert.False(plan.Placement.IsSet);
        Assert.Equal(0, plan.Unplaced);

        // Assert: the state — the lowest id into the inventory stack, the next two into the second grid, no new entry at (0,0)
        Assert.Equal(20, rig.Entries[invStack].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(1, 19).Append(200u).ToArray(), rig.IdsOf(invStack));
        Assert.Equal(12, rig.Entries[secondStack].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(100, 10).Concat(new uint[] { 201, 202 }).ToArray(), rig.IdsOf(secondStack));
        Assert.Equal(Occupancy.Free, rig.Inventory.Occupancy.OwnerAt(rig.InventoryA, 8, 0, 0));
        Assert.False(rig.Entries[source].IsLive);
        Assert.Equal(liveBefore - 1, rig.Entries.LiveCount);
        Assert.Equal(invVersion + 1, rig.Header(rig.InventoryA).Version);
        Assert.Equal(secondVersion + 1, rig.Header(second).Version);

        // Assert: committed through MergeInto — two InvMerged records with their runs, no InvInserted
        Assert.DoesNotContain(log, e => e.Kind == InvEventKinds.Inserted);
        int[] merged = Enumerable.Range(0, log.Length)
            .Where(i => log[i].Kind == InvEventKinds.Merged && (log[i].Flags & EventFlags.Continuation) == 0).ToArray();
        Assert.Equal(2, merged.Length);
        InvMergedPayload m0 = log[merged[0]].PayloadAs<InvMergedPayload>();
        InvMergedPayload m1 = log[merged[1]].PayloadAs<InvMergedPayload>();
        Assert.Equal(rig.Header(rig.InventoryA).Id.Value, m0.Container);
        Assert.Equal(rig.Entries[invStack].Id.Value, m0.TargetEntryId);
        Assert.Equal(1, m0.AddedCount);
        Assert.Equal(rig.Header(second).Id.Value, m1.Container);
        Assert.Equal(2, m1.AddedCount);
        Assert.Equal(1, m1.RunCount);
        InvMailIdRuns runs1 = log[merged[1] + 1].PayloadAs<InvMailIdRuns>();
        Assert.Equal(201u, runs1[0].Start);
        Assert.Equal(2, runs1[0].Length);
    }

    // ---------------------------------------------------------------- H-R8-2

    [Fact]
    [Trait("Id", "H-R8-2")]
    public void HR82_LargeLandsInThe8x4FreeColumnUnrotated_PassUOverAllTargetsPrecedesPassR()
    {
        // Arrange: bike 8x2 empty; chest 8x4 with only the 2x4 column at (0,0) free
        var rig = new InvAutoPlaceRig();
        ushort bike = rig.CreateGrid(8, 2);
        ushort chest = rig.CreateGrid(8, 4);
        FillWithMediums(rig, chest, (2, 0), (4, 0), (6, 0), (2, 2), (4, 2), (6, 2));
        ushort large = rig.DetachedMail("large", Address2, new uint[] { 1 });

        // Act
        ushort reason = rig.AutoPlace(large, bike, chest);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(chest, rig.Entries[large].ContainerSlot);
        Assert.Equal((0, 0), (rig.Entries[large].X, rig.Entries[large].Y));
        Assert.False(rig.Entries[large].Rotated);
        Assert.Equal(0u, rig.Header(bike).Version);
    }

    [Fact]
    [Trait("Id", "H-R8-2")]
    public void HR82_WithTheChestRemoved_LargeLandsInTheBikeAt00Rotated()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort bike = rig.CreateGrid(8, 2);
        ushort large = rig.DetachedMail("large", Address2, new uint[] { 1 });

        // Act
        ushort reason = rig.AutoPlace(large, bike);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(bike, rig.Entries[large].ContainerSlot);
        Assert.Equal((0, 0), (rig.Entries[large].X, rig.Entries[large].Y));
        Assert.True(rig.Entries[large].Rotated);
    }

    [Fact]
    [Trait("Id", "H-R8-2")]
    public void HR82_BothFull_NoRoom77_NoEntryCreated_EveryTargetVersionUnchanged()
    {
        // Arrange: both grids full of Mediums
        var rig = new InvAutoPlaceRig();
        ushort bike = rig.CreateGrid(8, 2);
        ushort chest = rig.CreateGrid(8, 4);
        FillWithMediums(rig, bike, (0, 0), (2, 0), (4, 0), (6, 0));
        FillWithMediums(rig, chest, (0, 0), (2, 0), (4, 0), (6, 0), (0, 2), (2, 2), (4, 2), (6, 2));
        ushort large = rig.DetachedMail("large", Address2, new uint[] { 1 });
        uint bikeVersion = rig.Header(bike).Version;
        uint chestVersion = rig.Header(chest).Version;
        int live = rig.Entries.LiveCount;
        ushort bikeEntries = rig.Header(bike).EntryCount;

        // Act
        ushort reason = rig.AutoPlace(large, bike, chest);

        // Assert
        Assert.Equal((ushort)InvReason.NoRoom, reason);
        Assert.Equal(77, reason);
        Assert.Equal(bikeVersion, rig.Header(bike).Version);
        Assert.Equal(chestVersion, rig.Header(chest).Version);
        Assert.Equal(live, rig.Entries.LiveCount);
        Assert.Equal(bikeEntries, rig.Header(bike).EntryCount);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[large].ContainerSlot);
        Assert.DoesNotContain(rig.LastLog, e => e.Kind == InvEventKinds.Inserted || e.Kind == InvEventKinds.Version);
    }

    [Fact]
    [Trait("Id", "H-R8-2")]
    public void HR82_MailOnlyIntakeAndAPlank_NotAllowed73_VersionUnchanged()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort plank = rig.DetachedItem("plank", 1);
        uint version = rig.Header(rig.Intake).Version;

        // Act
        ushort reason = rig.AutoPlace(plank, rig.Intake);

        // Assert
        Assert.Equal((ushort)InvReason.NotAllowed, reason);
        Assert.Equal(73, reason);
        Assert.Equal(version, rig.Header(rig.Intake).Version);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[plank].ContainerSlot);
    }

    [Fact]
    [Trait("Id", "H-R8-2")]
    public void HR82_MailOnlyIntakeWithNoSpaceAndALetter_NoRoom()
    {
        // Arrange: the 20 x 16 po_intake filled with 40 Larges to another address (2 x 4 each)
        var rig = new InvAutoPlaceRig();
        uint id = 1000;
        for (int y = 0; y < 16; y += 4)
        {
            for (int x = 0; x < 20; x += 2)
            {
                rig.PlaceMail(rig.Intake, "large", x, y, Address9, new[] { id++ });
            }
        }

        ushort letter = rig.DetachedMail("letter", Address2, new uint[] { 1 });
        uint version = rig.Header(rig.Intake).Version;

        // Act
        ushort reason = rig.AutoPlace(letter, rig.Intake);

        // Assert
        Assert.Equal(0, rig.Header(rig.Intake).FreeCells);
        Assert.Equal((ushort)InvReason.NoRoom, reason);
        Assert.Equal(version, rig.Header(rig.Intake).Version);
    }

    [Fact]
    public void NothingAdmitted_NotCarryableBeforeNotAllowed_WhenAnyTargetSkippedForCarryability()
    {
        // Arrange: a bulk subject (not hand-carryable) against [hand-carried inventory, mail-only intake]
        var rig = new InvAutoPlaceRig();
        PlacementSubject plank = rig.Item("plank");
        var bulk = new PlacementSubject(plank.Key, 1, 1, plank.Category, carryByHand: false, WeightClass.Bulk, plank.EffMax);
        var plan = new InvPlan();

        // Act
        ushort both = rig.Inventory.AutoPlace.Plan(ref plan, in bulk, 1, AutoPlace.NoSource, new[] { rig.InventoryA, rig.Intake }, MergePolicy.Merge, PlaceOrder.PassFirst, out _);
        plan = new InvPlan();
        ushort intakeOnly = rig.Inventory.AutoPlace.Plan(ref plan, in bulk, 1, AutoPlace.NoSource, new[] { rig.Intake }, MergePolicy.Merge, PlaceOrder.PassFirst, out _);

        // Assert
        Assert.Equal((ushort)InvReason.NotCarryable, both);
        Assert.Equal((ushort)InvReason.NotAllowed, intakeOnly);
    }

    // ---------------------------------------------------------------- policy, effMax 1, TargetFirst

    [Fact]
    public void NoMerge_SkipsPassM_ThePartialStackIsUntouched_AndANewEntryTakesTheFirstFreeAnchor()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 5));
        ushort source = rig.DetachedMail("letter", Address2, new uint[] { 50, 51, 52 });
        long visits = rig.Counters[CounterId.InventoryMergeCandidates];

        // Act
        ushort reason = rig.AutoPlace(rig.SubjectOf(source), 3, source, MergePolicy.NoMerge, PlaceOrder.PassFirst, rig.InventoryA);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(0, rig.LastPlan.MergeCount);
        Assert.Equal(visits, rig.Counters[CounterId.InventoryMergeCandidates]);
        Assert.Equal(5, rig.Entries[stack].Count);
        Assert.Equal(rig.InventoryA, rig.Entries[source].ContainerSlot);
        Assert.Equal((1, 0), (rig.Entries[source].X, rig.Entries[source].Y));
        Assert.Equal(3, rig.Entries[source].Count);
    }

    [Fact]
    public void EffMaxOne_SkipsPassM_EvenWhenTheKeyHasALinkedNonFullStack()
    {
        // Arrange: a linked 5-letter stack, and a subject with the same key but effMax 1 (a non-stackable reading)
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 5));
        PlacementSubject letter = rig.Mail("letter", Address2);
        var single = new PlacementSubject(letter.Key, letter.W, letter.H, letter.Category, letter.CarryByHand, letter.WeightClass, effMax: 1);
        Assert.NotEqual(StackIndex.None, rig.Index.Head(rig.InventoryA, letter.Key));
        long visits = rig.Counters[CounterId.InventoryMergeCandidates];
        var plan = new InvPlan();

        // Act
        ushort reason = rig.Inventory.AutoPlace.Plan(ref plan, in single, 1, AutoPlace.NoSource, new[] { rig.InventoryA }, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(0, ap.MergeCount);
        Assert.Equal(visits, rig.Counters[CounterId.InventoryMergeCandidates]);
        Assert.True(ap.Placement.IsSet);
        Assert.Equal((1, 0), (ap.Placement.X, ap.Placement.Y));
    }

    [Fact]
    public void PassM_AStackInTwoTargetsLists_AssertsPlannedTwiceInAssertBuilds()
    {
        Assert.SkipUnless(TestBuildExpectation.SimAssertsExpected, "INVENTORY_ASSERT is compiled out of this build.");

        // Arrange: one 5-letter stack heading both targets' lists (a corrupted index, unreachable through the mutator)
        var rig = new InvAutoPlaceRig();
        ushort second = rig.CreateGrid(8, 4);
        ushort stack = LinkIntoTwoLists(rig, second);
        PlacementSubject letter = rig.Mail("letter", Address2);
        var plan = new InvPlan();

        // Act + Assert
        SimAssertException ex = Assert.Throws<SimAssertException>(() => rig.Inventory.AutoPlace.Plan(
            ref plan, in letter, byte.MaxValue, AutoPlace.NoSource, new[] { rig.InventoryA, second }, MergePolicy.Merge, PlaceOrder.PassFirst, out _));
        Assert.Equal(SimAssertCode.InventoryAutoPlaceStackPlannedTwice, ex.Code);
        Assert.Equal(5, rig.Entries[stack].Count);
    }

    [Fact]
    public void PassM_AStackInTwoTargetsLists_IsSkippedTheSecondTimeInRelease_NoSecondMergePlanned()
    {
        Assert.SkipWhen(TestBuildExpectation.SimAssertsExpected, "INVENTORY_ASSERT fires first in this build (see the assert-build twin).");

        // Arrange: one 5-letter stack heading both targets' lists (a corrupted index, unreachable through the mutator)
        var rig = new InvAutoPlaceRig();
        ushort second = rig.CreateGrid(8, 4);
        ushort stack = LinkIntoTwoLists(rig, second);
        PlacementSubject letter = rig.Mail("letter", Address2);
        var plan = new InvPlan();

        // Act
        ushort reason = rig.Inventory.AutoPlace.Plan(
            ref plan, in letter, byte.MaxValue, AutoPlace.NoSource, new[] { rig.InventoryA, second }, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);

        // Assert: one merge fills the stack to effMax; the second visit plans nothing (no overfill past effMax)
        Assert.Equal(0, reason);
        Assert.Equal(1, ap.MergeCount);
        Assert.Equal(stack, ap.Merge(0).TargetEntrySlot);
        Assert.Equal(letter.EffMax - 5, ap.Merge(0).Units);
    }

    /// <summary>
    /// Places a 5-letter stack (#2) at (0,0) of the inventory and another in <paramref name="second"/>, then relinks the inventory
    /// stack as <paramref name="second"/>'s list head while the inventory's list still starts at it. Returns the inventory stack.
    /// </summary>
    private static ushort LinkIntoTwoLists(InvAutoPlaceRig rig, ushort second)
    {
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 5));
        _ = rig.PlaceMail(second, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(100, 5));
        StackKey key = rig.Mail("letter", Address2).Key;
        rig.Entries[stack].ContainerSlot = second;
        rig.Index.Unlink(stack); // drops second's list (its head is looked up by the overwritten container)
        rig.Index.Link(stack);   // the stack becomes second's head
        rig.Entries[stack].ContainerSlot = rig.InventoryA;
        Assert.Equal(stack, rig.Index.Head(rig.InventoryA, key));
        Assert.Equal(stack, rig.Index.Head(second, key));
        Assert.Equal(StackIndex.None, rig.Index.Next(stack));
        return stack;
    }

    [Fact]
    public void Mediums_EffMaxOne_NeverMerge_ASecondMediumOfTheSameKeyGoesToFreeCells()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort first = rig.PlaceMail(rig.InventoryA, "medium", 0, 0, Address2, new uint[] { 1 });
        ushort second = rig.DetachedMail("medium", Address2, new uint[] { 2 });
        Assert.False(rig.Index.Contains(first)); // effMax 1: a full stack is never indexed

        // Act
        ushort reason = rig.AutoPlace(second, rig.InventoryA);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(0, rig.LastPlan.MergeCount);
        Assert.Equal((2, 0), (rig.Entries[second].X, rig.Entries[second].Y));
    }

    [Fact]
    public void TargetFirst_FillsTheFirstTarget_MThenUThenR_BeforeTheNext_PassFirstMergesAcrossTargetsFirst()
    {
        // Arrange: [hotbar (empty), inventory (a 5-letter stack to #2)]
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 3, 1, Address2, InvAutoPlaceRig.Ids(1, 5));
        ushort source = rig.DetachedMail("letter", Address2, new uint[] { 60, 61 });
        PlacementSubject subject = rig.SubjectOf(source);
        ushort[] targets = { rig.HotbarA, rig.InventoryA };
        var passPlan = new InvPlan();
        ushort passFirst = rig.Inventory.AutoPlace.Plan(ref passPlan, in subject, 2, source, targets, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan pf);

        // Act
        ushort reason = rig.AutoPlace(subject, 2, source, MergePolicy.Merge, PlaceOrder.TargetFirst, targets);

        // Assert: pass-first would merge into the inventory stack; target-first takes hotbar slot 1 (lowest usable slot >= 1)
        Assert.Equal(0, passFirst);
        Assert.Equal(1, pf.MergeCount);
        Assert.Equal(stack, pf.Merge(0).TargetEntrySlot);
        Assert.False(pf.Placement.IsSet);
        Assert.Equal(0, reason);
        Assert.Equal(0, rig.LastPlan.MergeCount);
        Assert.Equal(rig.HotbarA, rig.Entries[source].ContainerSlot);
        Assert.Equal((1, 0), (rig.Entries[source].X, rig.Entries[source].Y));
        Assert.Equal(5, rig.Entries[stack].Count);
    }

    [Fact]
    public void TargetFirst_FirstTargetHasNoRoom_TriesTheNextTargetMergeBeforeItsFreeCells()
    {
        // Arrange: hotbar slots 1..7 full of other items; inventory has a 5-letter stack at (3,1) and free cells before it
        var rig = new InvAutoPlaceRig();
        for (int x = 1; x < 8; x++)
        {
            _ = rig.PlaceItem(rig.HotbarA, "axe", x, 0, 1);
        }

        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 3, 1, Address2, InvAutoPlaceRig.Ids(1, 5));
        ushort source = rig.DetachedMail("letter", Address2, new uint[] { 60, 61 });

        // Act
        ushort reason = rig.AutoPlace(rig.SubjectOf(source), 2, source, MergePolicy.Merge, PlaceOrder.TargetFirst, rig.HotbarA, rig.InventoryA);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(7, rig.Entries[stack].Count);
        Assert.False(rig.Entries[source].IsLive);
    }

    // ---------------------------------------------------------------- slots layout

    [Fact]
    public void Slots_PassUTakesTheLowestFreeUsableSlotFrom1_NeverSlot0_PassMMergesIntoTheFirstMergeableSlotInSlotOrder()
    {
        // Arrange: hotbar slot 1 a full 20-letter stack, slot 3 a 5-stack, slot 5 a 7-stack (all to #2); slot 0 (hands) free
        var rig = new InvAutoPlaceRig();
        ushort full = rig.PlaceMail(rig.HotbarA, "letter", 1, 0, Address2, InvAutoPlaceRig.Ids(1, 20));
        ushort five = rig.PlaceMail(rig.HotbarA, "letter", 3, 0, Address2, InvAutoPlaceRig.Ids(100, 5));
        ushort seven = rig.PlaceMail(rig.HotbarA, "letter", 5, 0, Address2, InvAutoPlaceRig.Ids(200, 7));
        ushort letters = rig.DetachedMail("letter", Address2, new uint[] { 300, 301, 302 });
        ushort plank = rig.DetachedItem("plank", 1);

        // Act
        ushort merged = rig.AutoPlace(letters, rig.HotbarA);
        ushort placed = rig.AutoPlace(plank, rig.HotbarA);

        // Assert: the merge goes to slot 3 (slot 1 is full); the plank to slot 2, the lowest free slot >= 1
        Assert.Equal(0, merged);
        Assert.Equal(20, rig.Entries[full].Count);
        Assert.Equal(8, rig.Entries[five].Count);
        Assert.Equal(7, rig.Entries[seven].Count);
        Assert.Equal(0, placed);
        Assert.Equal((2, 0), (rig.Entries[plank].X, rig.Entries[plank].Y));
        Assert.Equal(Occupancy.Free, rig.Inventory.Occupancy.OwnerAt(rig.HotbarA, 8, 0, 0));
    }

    [Fact]
    public void Slots_EverySlotFrom1Full_NoRoom_SlotZeroIsNeverSelected_NorReportedAsReservedSlot()
    {
        // Arrange: hotbar slots 1..7 full, slot 0 free
        var rig = new InvAutoPlaceRig();
        for (int x = 1; x < 8; x++)
        {
            _ = rig.PlaceItem(rig.HotbarA, "axe", x, 0, 1);
        }

        ushort plank = rig.DetachedItem("plank", 1);
        long probes = rig.Counters[CounterId.InventoryPlacementProbes];

        // Act
        ushort reason = rig.AutoPlace(plank, rig.HotbarA);

        // Assert: 7 candidate slots probed (1..7), one cell each = 7 cell probes; slot 0 is not a candidate
        Assert.Equal((ushort)InvReason.NoRoom, reason);
        Assert.Equal(7, rig.Counters[CounterId.InventoryPlacementProbes] - probes);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[plank].ContainerSlot);
    }

    [Fact]
    public void Slots_ASlotsContainerWithoutReservedSlots_StillStartsAtSlot1_AndStoresEveryEntryAs1x1Unrotated()
    {
        // Arrange: a 4-slot instance of the cursor def (slots layout, no reserved slots)
        var rig = new InvAutoPlaceRig();
        ushort slots = rig.CreateGrid(4, 1, "cursor");
        ushort axe = rig.DetachedItem("axe", 1); // 1 x 2 footprint

        // Act
        ushort reason = rig.AutoPlace(axe, slots);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal((1, 0), (rig.Entries[axe].X, rig.Entries[axe].Y));
        Assert.False(rig.Entries[axe].Rotated);
        Assert.Equal(3, rig.Header(slots).FreeCells);
        Assert.Equal(0x2UL, rig.Inventory.Occupancy.RowBits(slots, 0));
    }

    // ---------------------------------------------------------------- partial and all-or-nothing

    [Fact]
    public void Partial_FiveStackableUnitsIntoRoomForThree_TheThreeOldestPlaced_TheTwoNewestStayOnTheSource_Ok()
    {
        // Arrange: inventory full; its only room is a 17-letter stack to #2 at (0,0)
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 17));
        _ = rig.PlaceMail(rig.InventoryA, "letter", 1, 0, Address9, new uint[] { 90 });
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 1, Address9 + 1, new uint[] { 91 });
        _ = rig.PlaceMail(rig.InventoryA, "letter", 1, 1, Address9 + 2, new uint[] { 92 });
        FillWithMediums(rig, rig.InventoryA, (2, 0), (4, 0), (6, 0));
        ushort source = rig.DetachedMail("letter", Address2, new uint[] { 300, 301, 302, 303, 304 });
        uint sourceId = rig.Entries[source].Id.Value;

        // Act
        ushort reason = rig.AutoPlace(source, rig.InventoryA);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(2, rig.LastPlan.Unplaced);
        Assert.Equal(3, rig.LastPlan.MergedUnits);
        Assert.Equal(20, rig.Entries[stack].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(1, 17).Concat(new uint[] { 300, 301, 302 }).ToArray(), rig.IdsOf(stack));
        Assert.True(rig.Entries[source].IsLive);
        Assert.Equal(sourceId, rig.Entries[source].Id.Value);
        Assert.Equal(2, rig.Entries[source].Count);
        Assert.Equal(new uint[] { 303, 304 }, rig.IdsOf(source));
        Assert.True(InvAutoPlaceRig.ReplyOf(rig.LastLog).IsOk);
    }

    [Fact]
    public void AllOrNothing_ANonStackableThatDoesNotFitWhole_PlacesNothing_NoRoom()
    {
        // Arrange: an 8 x 2 inventory whose free cells are two separate 2 x 2 holes (Large needs 2 x 4 or 4 x 2)
        var rig = new InvAutoPlaceRig();
        FillWithMediums(rig, rig.InventoryA, (2, 0), (6, 0));
        ushort large = rig.DetachedMail("large", Address2, new uint[] { 7 });
        uint version = rig.Header(rig.InventoryA).Version;
        ushort freeCells = rig.Header(rig.InventoryA).FreeCells;

        // Act
        ushort reason = rig.AutoPlace(large, rig.InventoryA);

        // Assert
        Assert.Equal((ushort)InvReason.NoRoom, reason);
        Assert.Equal(1, rig.LastPlan.Unplaced);
        Assert.Equal(version, rig.Header(rig.InventoryA).Version);
        Assert.Equal(freeCells, rig.Header(rig.InventoryA).FreeCells);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[large].ContainerSlot);
    }

    [Fact]
    public void PartOfAPlacedStack_GoesOnlyIntoMerges_TheSourceEntryKeepsTheRestInPlace()
    {
        // Arrange: chest 6 letters to #2 at (0,0); inventory 18 letters to #2 at (0,0); AutoPlace 4 of the chest's 6
        var rig = new InvAutoPlaceRig();
        ushort chest = rig.CreateGrid(8, 4);
        ushort source = rig.PlaceMail(chest, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(500, 6));
        ushort target = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 18));

        // Act
        ushort reason = rig.AutoPlace(rig.SubjectOf(source), 4, source, MergePolicy.Merge, PlaceOrder.PassFirst, rig.InventoryA);

        // Assert: 2 merged (the room), 2 unplaced; the source stays at chest (0,0) with its 4 newest ids
        Assert.Equal(0, reason);
        Assert.Equal(2, rig.LastPlan.MergedUnits);
        Assert.False(rig.LastPlan.Placement.IsSet);
        Assert.Equal(2, rig.LastPlan.Unplaced);
        Assert.Equal(20, rig.Entries[target].Count);
        Assert.Equal(chest, rig.Entries[source].ContainerSlot);
        Assert.Equal(new uint[] { 502, 503, 504, 505 }, rig.IdsOf(source));
    }

    [Fact]
    public void APlacedSourceWithNoMergeTarget_IsTransferred_CrossContainer_KeepingItsEntryId()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort chest = rig.CreateGrid(8, 4);
        ushort source = rig.PlaceMail(chest, "letter", 4, 2, Address2, InvAutoPlaceRig.Ids(500, 6));
        uint id = rig.Entries[source].Id.Value;
        uint chestVersion = rig.Header(chest).Version;

        // Act
        ushort reason = rig.AutoPlace(source, rig.InventoryA);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(rig.InventoryA, rig.Entries[source].ContainerSlot);
        Assert.Equal((0, 0), (rig.Entries[source].X, rig.Entries[source].Y));
        Assert.Equal(id, rig.Entries[source].Id.Value);
        Assert.Equal(chestVersion + 1, rig.Header(chest).Version);
        Assert.Equal(Occupancy.Free, rig.Inventory.Occupancy.OwnerAt(chest, 8, 4, 2));
    }

    // ---------------------------------------------------------------- negative-fit cache

    [Fact]
    public void NegativeFit_ARepeatOfAFailedShapeAtTheSameVersion_VisitsZeroAnchors()
    {
        // Arrange: Large cannot fit the holed 8 x 2 inventory; the first try scans U and R and fails
        var rig = new InvAutoPlaceRig();
        FillWithMediums(rig, rig.InventoryA, (2, 0), (6, 0));
        ushort large = rig.DetachedMail("large", Address2, new uint[] { 7 });
        long p0 = rig.Counters[CounterId.InventoryPlacementProbes];
        Assert.Equal((ushort)InvReason.NoRoom, rig.AutoPlace(large, rig.InventoryA));
        long first = rig.Counters[CounterId.InventoryPlacementProbes] - p0;
        Assert.Equal(2, rig.LastPlan.NoFitCount);

        // Act
        long p1 = rig.Counters[CounterId.InventoryPlacementProbes];
        ushort reason = rig.AutoPlace(large, rig.InventoryA);

        // Assert: U (2 x 4 in 8 x 2: no anchor in bounds) + R (4 x 2: 5 anchors x 8 footprint cells = 40 cell probes) the first
        // time; nothing the second time
        Assert.Equal(40, first);
        Assert.Equal((ushort)InvReason.NoRoom, reason);
        Assert.Equal(0, rig.Counters[CounterId.InventoryPlacementProbes] - p1);
        Assert.Equal(0, rig.LastPlan.NoFitCount);
    }

    [Fact]
    public void NegativeFit_AVersionBump_ClearsIt()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        FillWithMediums(rig, rig.InventoryA, (2, 0), (6, 0));
        ushort large = rig.DetachedMail("large", Address2, new uint[] { 7 });
        _ = rig.AutoPlace(large, rig.InventoryA);
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address9, new uint[] { 70 }); // version + 1

        // Act
        long p = rig.Counters[CounterId.InventoryPlacementProbes];
        ushort reason = rig.AutoPlace(large, rig.InventoryA);

        // Assert: the full R scan again, 5 anchors x 8 cells = 40
        Assert.Equal((ushort)InvReason.NoRoom, reason);
        Assert.Equal(40, rig.Counters[CounterId.InventoryPlacementProbes] - p);
    }

    [Fact]
    public void NegativeFit_AStatEpochBump_ClearsIt_AndSoDoesRebuildIndex()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        FillWithMediums(rig, rig.InventoryA, (2, 0), (6, 0));
        ushort large = rig.DetachedMail("large", Address2, new uint[] { 7 });
        _ = rig.AutoPlace(large, rig.InventoryA);
        _ = rig.AutoPlace(large, rig.InventoryA); // records again (cache hit, nothing new) - still a no-fit at this version

        // Act: statEpoch bump (no version change)
        uint version = rig.Header(rig.InventoryA).Version;
        rig.Header(rig.InventoryA).StatEpoch++;
        long p1 = rig.Counters[CounterId.InventoryPlacementProbes];
        _ = rig.AutoPlace(large, rig.InventoryA);
        long afterEpoch = rig.Counters[CounterId.InventoryPlacementProbes] - p1;
        long p2 = rig.Counters[CounterId.InventoryPlacementProbes];
        _ = rig.AutoPlace(large, rig.InventoryA);
        long cachedAgain = rig.Counters[CounterId.InventoryPlacementProbes] - p2;
        rig.Inventory.Mutator.RebuildIndex(rig.InventoryA);
        long p3 = rig.Counters[CounterId.InventoryPlacementProbes];
        _ = rig.AutoPlace(large, rig.InventoryA);
        long afterRebuild = rig.Counters[CounterId.InventoryPlacementProbes] - p3;

        // Assert: each uncached run is the full R scan, 5 anchors x 8 cells = 40
        Assert.Equal(version, rig.Header(rig.InventoryA).Version);
        Assert.Equal(40, afterEpoch);
        Assert.Equal(0, cachedAgain);
        Assert.Equal(40, afterRebuild);
    }

    [Fact]
    public void NegativeFit_ARowChange_ClearsIt()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort grid = rig.CreateGrid(8, 2);
        FillWithMediums(rig, grid, (2, 0), (6, 0));
        ushort large = rig.DetachedMail("large", Address2, new uint[] { 7 });
        _ = rig.AutoPlace(large, grid);

        // Act: rows 2 -> 4 (R15 growth appends free rows at the bottom; no version change)
        rig.Header(grid).H = 4;
        rig.Header(grid).FreeCells += 16;
        long p = rig.Counters[CounterId.InventoryPlacementProbes];
        ushort reason = rig.AutoPlace(large, grid);

        // Assert: the cache missed and the scan found the unrotated 2 x 4 column at (0,0) on its first anchor: 1 x 8 cells = 8
        Assert.Equal(8, rig.Counters[CounterId.InventoryPlacementProbes] - p);
        Assert.Equal(0, reason);
        Assert.Equal((0, 0), (rig.Entries[large].X, rig.Entries[large].Y));
        Assert.False(rig.Entries[large].Rotated);
    }

    [Fact]
    public void NegativeFit_CacheDisabled_GivesTheSamePlanReplyAndState_ButProbesAgain()
    {
        // Arrange: two identical worlds, one with the cache disabled
        InvAutoPlaceRig[] rigs = { new(), new() };
        rigs[1].Inventory.AutoPlace.NoFitCache.Enabled = false;
        var replies = new List<ushort>[2];
        var probes = new long[2];
        for (int r = 0; r < 2; r++)
        {
            InvAutoPlaceRig rig = rigs[r];
            replies[r] = new List<ushort>();
            FillWithMediums(rig, rig.InventoryA, (2, 0), (6, 0));
            ushort large = rig.DetachedMail("large", Address2, new uint[] { 7 });
            ushort letter = rig.DetachedMail("letter", Address2, new uint[] { 8 });
            long p = rig.Counters[CounterId.InventoryPlacementProbes];

            // Act
            replies[r].Add(rig.AutoPlace(large, rig.InventoryA));
            replies[r].Add(rig.AutoPlace(large, rig.InventoryA));
            replies[r].Add(rig.AutoPlace(letter, rig.InventoryA));
            replies[r].Add(rig.AutoPlace(large, rig.InventoryA));
            probes[r] = rig.Counters[CounterId.InventoryPlacementProbes] - p;
        }

        // Assert
        Assert.Equal(replies[0], replies[1]);
        Assert.Equal(StateOf(rigs[0]), StateOf(rigs[1]));
        Assert.True(probes[1] > probes[0]);
    }

    [Fact]
    public void NegativeFit_ValidateOnlyReads_APlanThatFailsLeavesTheCacheEmptyUntilItIsRecorded()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        FillWithMediums(rig, rig.InventoryA, (2, 0), (6, 0));
        PlacementSubject large = rig.Mail("large", Address2);
        ref ContainerHeader h = ref rig.Header(rig.InventoryA);
        var plan = new InvPlan();

        // Act: Plan (the Validate half) alone
        ushort reason = rig.Inventory.AutoPlace.Plan(ref plan, in large, 1, AutoPlace.NoSource, new[] { rig.InventoryA }, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);
        bool beforeRecord = rig.Inventory.AutoPlace.NoFitCache.IsNoFit(rig.InventoryA, in h, large.W, large.H, rotated: true);
        rig.Inventory.AutoPlace.RecordNoFits(in ap);
        bool afterRecord = rig.Inventory.AutoPlace.NoFitCache.IsNoFit(rig.InventoryA, in h, large.W, large.H, rotated: true);

        // Assert
        Assert.Equal((ushort)InvReason.NoRoom, reason);
        Assert.False(beforeRecord);
        Assert.True(afterRecord);
    }

    // ---------------------------------------------------------------- effMax change (R3, R15)

    [Fact]
    [Trait("Id", "H-R3-2")]
    public void EffMaxChange_DoubleStackPushStatEpochBumpAndRebuildIndex_AOneLetterAutoPlaceMergesIntoTheFormerlyFull20Stack()
    {
        // Arrange: a full 20-stack (not indexed at effMax 20)
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 2, 1, Address2, InvAutoPlaceRig.Ids(1, 20));
        Assert.False(rig.Index.Contains(stack));
        Assert.True(PushDoubleStack(rig));
        Assert.Equal(40, rig.Mail("letter", Address2).EffMax);
        rig.Header(rig.InventoryA).StatEpoch++;
        rig.Inventory.Mutator.RebuildIndex(rig.InventoryA);
        ushort one = rig.DetachedMail("letter", Address2, new uint[] { 99 });

        // Act
        ushort reason = rig.AutoPlace(one, rig.InventoryA);

        // Assert
        Assert.True(rig.Index.Contains(stack));
        Assert.Equal(0, reason);
        Assert.Equal(1, rig.LastPlan.MergeCount);
        Assert.Equal(21, rig.Entries[stack].Count);
        Assert.False(rig.Entries[one].IsLive);
    }

    [Fact]
    [Trait("Id", "H-R3-2")]
    public void PassM_ALinkedStackFullAtTheCurrentEffMax_IsSkippedAtOpTime_NotMergedInto_TheNextNodeTakesTheUnits()
    {
        // Arrange: under Double Stack (effMax 40) a 30-letter stack at (0,0) and a 15-letter stack at (1,0) are both linked; the perk
        // is removed (effMax 20) with no RebuildIndex, so the 30-stack stays linked although it is full (over-full) at op time
        var rig = new InvAutoPlaceRig();
        Assert.True(PushDoubleStack(rig));
        ushort over = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 30));
        ushort room = rig.PlaceMail(rig.InventoryA, "letter", 1, 0, Address2, InvAutoPlaceRig.Ids(100, 15));
        Assert.True(rig.Host.Services.Stats.Remove(in _doubleStackSource));
        Assert.Equal(20, rig.Mail("letter", Address2).EffMax);
        Assert.Equal(new[] { over, room }, ListOf(rig, rig.InventoryA, rig.Mail("letter", Address2).Key));
        ushort source = rig.DetachedMail("letter", Address2, new uint[] { 500, 501, 502 });
        long m0 = rig.Counters[CounterId.InventoryMergeCandidates];

        // Act
        ushort reason = rig.AutoPlace(source, rig.InventoryA);
        long visits = rig.Counters[CounterId.InventoryMergeCandidates] - m0;

        // Assert: the stale node is visited and skipped (count 30 >= effMax 20), the 15-stack takes all 3
        Assert.Equal(0, reason);
        Assert.Equal(1, rig.LastPlan.MergeCount);
        Assert.Equal(room, rig.LastPlan.Merge(0).TargetEntrySlot);
        Assert.Equal(3, rig.LastPlan.Merge(0).Units);
        Assert.Equal(30, rig.Entries[over].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(1, 30), rig.IdsOf(over));
        Assert.Equal(18, rig.Entries[room].Count);
        Assert.False(rig.Entries[source].IsLive);
        Assert.Equal(2, visits);
    }

    [Fact]
    [Trait("Id", "H-R3-2")]
    public void PassM_ALinkedItemStackFullAtTheOpTimeEffMax_IsSkippedAtOpTime_NotMergedInto_TheNextNodeTakesTheUnits()
    {
        // Arrange: a 15-plank stack at (0,0) and a 12-plank stack at (1,0), both linked at the def's effMax 20; the op reads a lower
        // effMax (15) for the same key, so the 15-stack stays linked although it is full at op time (items read effMax per op too)
        var rig = new InvAutoPlaceRig();
        PlacementSubject plank = rig.Item("plank");
        Assert.Equal(20, plank.EffMax);
        ushort full = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 15);
        ushort room = rig.PlaceItem(rig.InventoryA, "plank", 1, 0, 12);
        Assert.Equal(new[] { full, room }, ListOf(rig, rig.InventoryA, plank.Key));
        var opTime = new PlacementSubject(plank.Key, plank.W, plank.H, plank.Category, plank.CarryByHand, plank.WeightClass, effMax: 15);
        ushort source = rig.DetachedItem("plank", 3);
        long m0 = rig.Counters[CounterId.InventoryMergeCandidates];

        // Act
        ushort reason = rig.AutoPlace(in opTime, 3, source, MergePolicy.Merge, PlaceOrder.PassFirst, rig.InventoryA);
        long visits = rig.Counters[CounterId.InventoryMergeCandidates] - m0;

        // Assert: the full node is visited and skipped (count 15 >= effMax 15), the 12-stack takes all 3 (to 15, not past it)
        Assert.Equal(0, reason);
        Assert.Equal(1, rig.LastPlan.MergeCount);
        Assert.Equal(room, rig.LastPlan.Merge(0).TargetEntrySlot);
        Assert.Equal(3, rig.LastPlan.Merge(0).Units);
        Assert.False(rig.LastPlan.Placement.IsSet);
        Assert.Equal(15, rig.Entries[full].Count);
        Assert.Equal(15, rig.Entries[room].Count);
        Assert.False(rig.Entries[source].IsLive);
        Assert.Equal(2, visits);
    }

    // ---------------------------------------------------------------- TD-116 merge-count bound

    [Fact]
    [Trait("Id", "TD-116")]
    public void TenMailMergesInOnePlan_Succeed_NoAssert66_NoReason85()
    {
        // Arrange: 6 stacks of 19 letters to #2 (room 1 each) in the inventory and 6 in a second grid
        var rig = new InvAutoPlaceRig();
        ushort second = rig.CreateGrid(8, 4);
        var stacks = new List<ushort>();
        for (int i = 0; i < 12; i++)
        {
            ushort grid = i < 6 ? rig.InventoryA : second;
            stacks.Add(rig.PlaceMail(grid, "letter", i % 6, 0, Address2, InvAutoPlaceRig.Ids(10_000u + (uint)(i * 100), 19)));
        }

        ushort source = rig.DetachedMail("letter", Address2, InvAutoPlaceRig.Ids(100, 10));

        // Act
        ushort reason = rig.AutoPlace(source, rig.InventoryA, second);

        // Assert: ten merges, one unit each, oldest first, in row-major order across the targets
        Assert.Equal(0, reason);
        Assert.Equal(10, rig.LastPlan.MergeCount);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(stacks[i], rig.LastPlan.Merge(i).TargetEntrySlot);
            Assert.Equal(20, rig.Entries[stacks[i]].Count);
            Assert.Equal(100u + (uint)i, rig.IdsOf(stacks[i])[0]);
        }

        Assert.Equal(19, rig.Entries[stacks[10]].Count);
        Assert.False(rig.Entries[source].IsLive);
    }

    [Fact]
    [Trait("Id", "TD-116")]
    public void OneHundredSingleUnitMailMergesAcrossFourTargets_PlanAndCommit_NoAssert66_NoReason85_ArenaAndIndexStayConsistent()
    {
        // Arrange: four 5 x 5 grids of 25 stacks of 19 letters to #2 (room 1 each, ids from 10,000); a detached source of 100 letters
        // (ids 1 to 100: the spawn/grant shape, its entry holding the planned units). 100 is past the old 32 cap and within the default
        // arena's class-32-and-up blocks (the stacks and unions both need one each).
        var rig = new InvAutoPlaceRig();
        ushort[] grids = { rig.CreateGrid(5, 5), rig.CreateGrid(5, 5), rig.CreateGrid(5, 5), rig.CreateGrid(5, 5) };
        var stacks = new List<ushort>();
        foreach (ushort grid in grids)
        {
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    stacks.Add(rig.PlaceMail(grid, "letter", x, y, Address2, InvAutoPlaceRig.Ids(10_000u + (uint)(stacks.Count * 100), 19)));
                }
            }
        }

        ushort source = rig.DetachedMail("letter", Address2, InvAutoPlaceRig.Ids(1, 100));
        PlacementSubject letter = rig.SubjectOf(source);
        int liveUnitsBefore = rig.Inventory.MailIds.LiveUnits;
        var plan = new InvPlan();

        // Act: the pure plan (to read its acquired blocks), then the same AutoPlace planned and committed through the fixture op
        ushort planned = rig.Inventory.AutoPlace.Plan(ref plan, in letter, 100, source, grids, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);
        ushort reason = rig.AutoPlace(source, grids);

        // Assert: one union block per merge, no overflow, the reservation passes, and the commit applies all 100
        Assert.Equal(0, planned);
        Assert.Equal(100, ap.MergeCount);
        Assert.Equal(0, ap.Unplaced);
        Assert.Equal(100, plan.AcquiredBlockCount);
        Assert.All(plan.AcquiredBlocks.ToArray(), b => Assert.Equal(20, b));
        Assert.False(plan.AcquiredBlocksOverflowed);
        Assert.Equal(0, rig.Inventory.RequestCheck.CheckArenaReservation(in plan));
        Assert.Equal(0, reason);
        Assert.Equal(100, rig.LastPlan.MergeCount);
        for (int i = 0; i < stacks.Count; i++)
        {
            Assert.Equal(stacks[i], rig.LastPlan.Merge(i).TargetEntrySlot);
            Assert.Equal(1, rig.LastPlan.Merge(i).Units);
            Assert.Equal(20, rig.Entries[stacks[i]].Count);
            uint[] ids = rig.IdsOf(stacks[i]);
            Assert.Equal(20, ids.Length);
            Assert.Equal(1u + (uint)i, ids[0]); // the source's oldest ids went first, in PASS M order
            Assert.Equal(InvAutoPlaceRig.Ids(10_000u + (uint)(i * 100), 19), ids[1..]);
            Assert.False(rig.Index.Contains(stacks[i]));
        }

        Assert.False(rig.Entries[source].IsLive);
        Assert.Equal(liveUnitsBefore, rig.Inventory.MailIds.LiveUnits);
        Assert.All(grids, g => Assert.Equal(StackIndex.None, rig.Index.Head(g, letter.Key)));
    }

    [Fact]
    [Trait("Id", "TD-116")]
    public void TwoHundredFiftyFiveSingleUnitMerges_TheExactWorstCase_PlansMaxMergesAndCommits_WithNoEarlyStop()
    {
        // Arrange: 255 stacks of 19 planks (room 1 each) over four 8 x 8 grids and a detached source of 255 planks. Items acquire no
        // arena blocks, so the true 255-merge bound fits the default world (255 mail stacks would need more class-32 blocks than it has).
        var rig = new InvAutoPlaceRig();
        ushort[] grids = { rig.CreateGrid(8, 8), rig.CreateGrid(8, 8), rig.CreateGrid(8, 8), rig.CreateGrid(8, 8) };
        var stacks = new List<ushort>();
        for (int i = 0; i < AutoPlacePlan.MaxMerges; i++)
        {
            stacks.Add(rig.PlaceItem(grids[i / 64], "plank", i % 8, (i % 64) / 8, 19));
        }

        ushort source = rig.DetachedItem("plank", byte.MaxValue);

        // Act
        ushort reason = rig.AutoPlace(source, grids);

        // Assert: exactly MaxMerges merges of one unit, nothing left unplaced, every stack full and out of the index
        Assert.Equal(0, reason);
        Assert.Equal(AutoPlacePlan.MaxMerges, rig.LastPlan.MergeCount);
        Assert.Equal(byte.MaxValue, rig.LastPlan.MergedUnits);
        Assert.Equal(0, rig.LastPlan.Unplaced);
        Assert.False(rig.LastPlan.Placement.IsSet);
        for (int i = 0; i < stacks.Count; i++)
        {
            Assert.Equal(stacks[i], rig.LastPlan.Merge(i).TargetEntrySlot);
            Assert.Equal(20, rig.Entries[stacks[i]].Count);
            Assert.False(rig.Index.Contains(stacks[i]));
        }

        Assert.False(rig.Entries[source].IsLive);
        Assert.Equal(StackIndex.None, rig.Index.Head(grids[3], rig.Item("plank").Key));
    }

    [Fact]
    [Trait("Id", "TD-116")]
    public void TwoHundredFiftyFiveSingleUnitMailMerges_TheExactWorstCaseWithMail_PlanAndCommit_NoAssert66_NoReason85_OldestFirst()
    {
        // Arrange: a test world whose arena has 600 class-32 blocks (budget 9 x 32 x 600; the default has 113): 255 stacks of 19
        // letters to #2 (room 1 each) over four 8 x 8 grids hold 255 of them and the 255 unions reserve 255 more. A detached source of
        // 255 letters, ids 1 to 255.
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.ClassCount * 32 * 600);
        Assert.Equal(600, rig.Inventory.MailIds.BlocksPerClass(MailIdArena.ClassFor(20)));
        ushort[] grids = { rig.CreateGrid(8, 8), rig.CreateGrid(8, 8), rig.CreateGrid(8, 8), rig.CreateGrid(8, 8) };
        var stacks = new List<ushort>();
        for (int i = 0; i < AutoPlacePlan.MaxMerges; i++)
        {
            stacks.Add(rig.PlaceMail(grids[i / 64], "letter", i % 8, (i % 64) / 8, Address2, InvAutoPlaceRig.Ids(10_000u + (uint)(i * 100), 19)));
        }

        ushort source = rig.DetachedMail("letter", Address2, InvAutoPlaceRig.Ids(1, byte.MaxValue));
        PlacementSubject letter = rig.SubjectOf(source);
        int liveUnitsBefore = rig.Inventory.MailIds.LiveUnits;
        int liveBlocksBefore = rig.Inventory.MailIds.LiveBlocks;
        var plan = new InvPlan();

        // Act: the pure plan (its acquired blocks), then the same AutoPlace planned, reserved and committed through the fixture op
        ushort planned = rig.Inventory.AutoPlace.Plan(ref plan, in letter, byte.MaxValue, source, grids, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);
        ushort reason = rig.AutoPlace(source, grids);

        // Assert: the plan — 255 one-unit merges, one 20-unit union block each, no overflow, the reservation passes
        Assert.Equal(0, planned);
        Assert.Equal(AutoPlacePlan.MaxMerges, ap.MergeCount);
        Assert.Equal(AutoPlacePlan.MaxMerges, plan.AcquiredBlockCount);
        Assert.All(plan.AcquiredBlocks.ToArray(), b => Assert.Equal(20, b));
        Assert.False(plan.AcquiredBlocksOverflowed);
        Assert.Equal(0, rig.Inventory.RequestCheck.CheckArenaReservation(in plan));

        // Assert: the commit (no assert 66, no reason 85) — every stack took the next-oldest id, in PASS M order
        Assert.Equal(0, reason);
        Assert.Equal(AutoPlacePlan.MaxMerges, rig.LastPlan.MergeCount);
        Assert.Equal(byte.MaxValue, rig.LastPlan.MergedUnits);
        Assert.Equal(0, rig.LastPlan.Unplaced);
        for (int i = 0; i < stacks.Count; i++)
        {
            Assert.Equal(stacks[i], rig.LastPlan.Merge(i).TargetEntrySlot);
            uint[] ids = rig.IdsOf(stacks[i]);
            Assert.Equal(20, rig.Entries[stacks[i]].Count);
            Assert.Equal(1u + (uint)i, ids[0]);
            Assert.Equal(InvAutoPlaceRig.Ids(10_000u + (uint)(i * 100), 19), ids[1..]);
            Assert.False(rig.Index.Contains(stacks[i]));
        }

        // Assert: the arena — units conserved, the source's block freed and each stack's block replaced by its union
        Assert.False(rig.Entries[source].IsLive);
        Assert.Equal(liveUnitsBefore, rig.Inventory.MailIds.LiveUnits);
        Assert.Equal(liveBlocksBefore - 1, rig.Inventory.MailIds.LiveBlocks);
        Assert.All(grids, g => Assert.Equal(StackIndex.None, rig.Index.Head(g, letter.Key)));
    }

    [Fact]
    [Trait("Id", "TD-116")]
    public void TwoHundredFiftyFiveSingleUnitMailMerges_TheArenaRunsOutPartway_IsCapacityExhaustedWhole_ThenExactlyTheReservableMergesCommit()
    {
        // Arrange: an arena of 256 class-32 blocks (budget 9 x 32 x 256; classes 64/128/256 get 128/64/32 blocks). 255 stacks of 19
        // letters to #2 (room 1 each) over four 8 x 8 grids take 255 class-32 blocks and the 255-letter source one class-256 block, so
        // the 20-unit union blocks can come from 1 + 128 + 64 + 31 = 224 free blocks: the 255-merge reservation runs out at merge 225.
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.ClassCount * 32 * 256);
        ushort[] grids = { rig.CreateGrid(8, 8), rig.CreateGrid(8, 8), rig.CreateGrid(8, 8), rig.CreateGrid(8, 8) };
        var stacks = new List<ushort>();
        for (int i = 0; i < AutoPlacePlan.MaxMerges; i++)
        {
            stacks.Add(rig.PlaceMail(grids[i / 64], "letter", i % 8, (i % 64) / 8, Address2, InvAutoPlaceRig.Ids(10_000u + (uint)(i * 100), 19)));
        }

        ushort source = rig.DetachedMail("letter", Address2, InvAutoPlaceRig.Ids(1, byte.MaxValue));
        PlacementSubject letter = rig.SubjectOf(source);
        MailIdArena arena = rig.Inventory.MailIds;
        const int Reservable = 224;
        Assert.Equal(Reservable, FreeBlocksFrom(arena, MailIdArena.ClassFor(20)));
        int liveUnitsBefore = arena.LiveUnits;
        int liveBlocksBefore = arena.LiveBlocks;
        uint[] versionsBefore = grids.Select(g => rig.Header(g).Version).ToArray();

        // Act: the whole 255, then one more than the arena can reserve, then exactly what it can
        ushort whole = rig.AutoPlace(letter, byte.MaxValue, source, MergePolicy.Merge, PlaceOrder.PassFirst, grids);
        AutoPlacePlan wholePlan = rig.LastPlan;
        EventEntry[] wholeLog = rig.LastLog;
        ushort onePast = rig.AutoPlace(letter, Reservable + 1, source, MergePolicy.Merge, PlaceOrder.PassFirst, grids);

        // Assert: both are refused whole with capacity_exhausted (85) although PASS M planned every merge; nothing is half-applied
        Assert.Equal((ushort)InvReason.CapacityExhausted, whole);
        Assert.Equal((ushort)InvReason.CapacityExhausted, onePast);
        Assert.Equal(AutoPlacePlan.MaxMerges, wholePlan.MergeCount);
        Assert.Equal(0, wholePlan.Unplaced);
        Assert.DoesNotContain(wholeLog, e => e.Kind == InvEventKinds.Merged);
        Assert.Equal(byte.MaxValue, rig.Entries[source].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(1, byte.MaxValue), rig.IdsOf(source));
        for (int i = 0; i < stacks.Count; i++)
        {
            Assert.Equal(19, rig.Entries[stacks[i]].Count);
            Assert.True(rig.Index.Contains(stacks[i]));
        }

        Assert.Equal(versionsBefore, grids.Select(g => rig.Header(g).Version).ToArray());
        Assert.Equal(liveUnitsBefore, arena.LiveUnits);
        Assert.Equal(liveBlocksBefore, arena.LiveBlocks);
        Assert.Equal(Reservable, FreeBlocksFrom(arena, MailIdArena.ClassFor(20)));

        // Act: exactly the reservable count
        ushort fits = rig.AutoPlace(letter, Reservable, source, MergePolicy.Merge, PlaceOrder.PassFirst, grids);

        // Assert: exactly 224 one-unit merges in PASS M order (oldest ids first), the other 31 stacks untouched, and the source keeps
        // the 31 newest ids in place (a part of a stack goes only into merges)
        Assert.Equal(0, fits);
        Assert.Equal(Reservable, rig.LastPlan.MergeCount);
        Assert.Equal(Reservable, rig.LastPlan.MergedUnits);
        Assert.Equal(0, rig.LastPlan.Unplaced);
        Assert.False(rig.LastPlan.Placement.IsSet);
        for (int i = 0; i < stacks.Count; i++)
        {
            bool merged = i < Reservable;
            Assert.Equal(merged ? 20 : 19, rig.Entries[stacks[i]].Count);
            uint[] ids = rig.IdsOf(stacks[i]);
            Assert.Equal(InvAutoPlaceRig.Ids(10_000u + (uint)(i * 100), 19), merged ? ids[1..] : ids);
            if (merged)
            {
                Assert.Equal(1u + (uint)i, ids[0]);
            }

            Assert.Equal(!merged, rig.Index.Contains(stacks[i]));
        }

        Assert.True(rig.Entries[source].IsLive);
        Assert.Equal(byte.MaxValue - Reservable, rig.Entries[source].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(1u + Reservable, byte.MaxValue - Reservable), rig.IdsOf(source));

        // Assert: the arena — units conserved, no block gained or lost (each union replaced its stack's block), the same free total
        Assert.Equal(liveUnitsBefore, arena.LiveUnits);
        Assert.Equal(liveBlocksBefore, arena.LiveBlocks);
        Assert.Equal(Reservable, FreeBlocksFrom(arena, MailIdArena.ClassFor(20)));
    }

    [Fact]
    [Trait("Id", "TD-116")]
    public void MergeBound_CoversEveryLegalAutoPlace_AndThePlanStructsStayStackSized()
    {
        // Arrange / Act
        int invPlan = System.Runtime.CompilerServices.Unsafe.SizeOf<InvPlan>();
        int autoPlacePlan = System.Runtime.CompilerServices.Unsafe.SizeOf<AutoPlacePlan>();

        // Assert: one merge per unit at most (units <= 255), one block per mail merge plus the handler's headroom; sizes measured on
        // net10.0 and net8.0 (a field change must be a deliberate edit of these numbers)
        Assert.Equal(byte.MaxValue, AutoPlacePlan.MaxMerges);
        Assert.Equal(AutoPlacePlan.MaxMerges + InvPlan.HandlerBlockHeadroom, InvPlan.MaxAcquiredBlocks);
        Assert.Equal(InvPlanSize, invPlan);
        Assert.Equal(AutoPlacePlanSize, autoPlacePlan);
    }

    [Fact]
    public void Plan_TouchesEveryChangedContainer_SourceFirst_AndAddsOneBlockPerMailMergeSizedAsTheUnion()
    {
        // Arrange: a placed source in a chest, merge targets in the inventory (18 -> 20) and a second grid (3 -> 5)
        var rig = new InvAutoPlaceRig();
        ushort chest = rig.CreateGrid(8, 4);
        ushort second = rig.CreateGrid(8, 2);
        ushort source = rig.PlaceMail(chest, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(500, 4));
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 18));
        _ = rig.PlaceMail(second, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(50, 3));
        PlacementSubject subject = rig.SubjectOf(source);
        var plan = new InvPlan();

        // Act
        ushort reason = rig.Inventory.AutoPlace.Plan(ref plan, in subject, 4, source, new[] { rig.InventoryA, second }, MergePolicy.Merge, PlaceOrder.PassFirst, out _);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(3, plan.TouchedCount);
        Assert.Equal(chest, plan.TouchedSlot(0));
        Assert.Equal(rig.InventoryA, plan.TouchedSlot(1));
        Assert.Equal(second, plan.TouchedSlot(2));
        Assert.Equal(new ushort[] { 20, 5 }, plan.AcquiredBlocks.ToArray());
    }

    [Fact]
    public void Plan_AFullTouchedSet_IsABadRequestRefusal_BeforeAnyMutation()
    {
        // Arrange: 4 already-touched containers (the plan's limit), then a merge target that is not among them
        var rig = new InvAutoPlaceRig();
        ushort target = rig.CreateGrid(8, 2);
        _ = rig.PlaceMail(target, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 3));
        var plan = new InvPlan();
        for (int i = 0; i < InvPlan.MaxTouched; i++)
        {
            Assert.True(plan.Touch(rig.CreateGrid(1, 1)) >= 0);
        }

        PlacementSubject letter = rig.Mail("letter", Address2);

        // Act
        ushort reason = rig.Inventory.AutoPlace.Plan(ref plan, in letter, 1, AutoPlace.NoSource, new[] { target }, MergePolicy.Merge, PlaceOrder.PassFirst, out _);

        // Assert
        Assert.Equal(CoreReason.BadRequest, reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Plan_UnitsOutside1To255_OrNoTargets_IsBadRequest(int units)
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        PlacementSubject letter = rig.Mail("letter", Address2);
        var plan = new InvPlan();

        // Act
        ushort bad = rig.Inventory.AutoPlace.Plan(ref plan, in letter, units, AutoPlace.NoSource, new[] { rig.InventoryA }, MergePolicy.Merge, PlaceOrder.PassFirst, out _);
        ushort none = rig.Inventory.AutoPlace.Plan(ref plan, in letter, 1, AutoPlace.NoSource, ReadOnlySpan<ushort>.Empty, MergePolicy.Merge, PlaceOrder.PassFirst, out _);

        // Assert
        Assert.Equal(CoreReason.BadRequest, bad);
        Assert.Equal(CoreReason.BadRequest, none);
    }

    [Theory]
    [InlineData(PlaceOrder.PassFirst)]
    [InlineData(PlaceOrder.TargetFirst)]
    public void DuplicateTarget_IsBadRequest_BeforeAnyMutation_SoOneStackIsNeverMergedTwice(PlaceOrder order)
    {
        // Arrange: a 15-letter stack (room 5) and a detached 10-letter source; 8 of its units are placed (a part of a stack goes only
        // into merges, so no free cell takes the rest in either order). Planned over [inventory, inventory], PASS M would merge 5 then
        // 3 into the same stack (it reads the pre-plan count 15 both times): the plan would claim 8 merged while the commit applies
        // only 5 (Release skips the overfilling MergeInto; Debug asserts 69 in it).
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 15));
        ushort source = rig.DetachedMail("letter", Address2, InvAutoPlaceRig.Ids(100, 10));
        uint version = rig.Header(rig.InventoryA).Version;

        // Act
        ushort reason = rig.AutoPlace(rig.SubjectOf(source), 8, source, MergePolicy.Merge, order, rig.InventoryA, rig.InventoryA);

        // Assert: what the plan (and so the reply) says was merged is what the state shows was merged
        Assert.Equal(rig.LastPlan.MergedUnits, rig.Entries[stack].Count - 15);
        Assert.Equal(10 - rig.LastPlan.MergedUnits, rig.Entries[source].Count);

        // Assert: refused before any mutation
        Assert.Equal(CoreReason.BadRequest, reason);
        Assert.Equal(0, rig.LastPlan.MergeCount);
        Assert.Equal(InvAutoPlaceRig.Ids(1, 15), rig.IdsOf(stack));
        Assert.Equal(InvAutoPlaceRig.Ids(100, 10), rig.IdsOf(source));
        Assert.Equal(version, rig.Header(rig.InventoryA).Version);
    }

    [Fact]
    public void DuplicateTarget_NotAdjacent_IsBadRequest_AndThePlanIsUntouched()
    {
        // Arrange: [inventory, second, inventory], each with mergeable room
        var rig = new InvAutoPlaceRig();
        ushort second = rig.CreateGrid(8, 2);
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 15));
        _ = rig.PlaceMail(second, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(50, 15));
        PlacementSubject letter = rig.Mail("letter", Address2);
        var plan = new InvPlan();

        // Act
        ushort reason = rig.Inventory.AutoPlace.Plan(
            ref plan, in letter, 12, AutoPlace.NoSource, new[] { rig.InventoryA, second, rig.InventoryA }, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);

        // Assert
        Assert.Equal(CoreReason.BadRequest, reason);
        Assert.Equal(0, plan.TouchedCount);
        Assert.Equal(0, plan.AcquiredBlockCount);
        Assert.Equal(0, ap.MergeCount);
    }

    // ---------------------------------------------------------------- probe counters

    [Fact]
    [Trait("Id", "TR-inventory-007")]
    public void ProbeCounters_PassMVisitsTheKeysListOnly_PassUCountsEveryFootprintCellOfEachAnchorUpToTheFirstFit()
    {
        // Arrange: inventory 8 x 2 with 2 letter stacks to #2 (full and non-full) among other stacks; a 1 x 2 Small to place
        var rig = new InvAutoPlaceRig();
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 20));   // full: not in the list
        _ = rig.PlaceMail(rig.InventoryA, "letter", 1, 0, Address2, InvAutoPlaceRig.Ids(100, 4));  // list length 2
        _ = rig.PlaceMail(rig.InventoryA, "letter", 2, 0, Address2, InvAutoPlaceRig.Ids(200, 6));
        _ = rig.PlaceMail(rig.InventoryA, "letter", 3, 0, Address9, InvAutoPlaceRig.Ids(300, 6));  // another key
        _ = rig.PlaceMail(rig.InventoryA, "postcard", 4, 0, Address2, InvAutoPlaceRig.Ids(400, 6)); // another kind
        ushort letters = rig.DetachedMail("letter", Address2, new uint[] { 900 });
        ushort small = rig.DetachedMail("small", Address2, new uint[] { 901 });
        long m0 = rig.Counters[CounterId.InventoryMergeCandidates];
        long p0 = rig.Counters[CounterId.InventoryPlacementProbes];

        // Act
        _ = rig.AutoPlace(letters, rig.InventoryA);
        long mergeVisits = rig.Counters[CounterId.InventoryMergeCandidates] - m0;
        long p1 = rig.Counters[CounterId.InventoryPlacementProbes];
        _ = rig.AutoPlace(small, rig.InventoryA);
        long cellProbes = rig.Counters[CounterId.InventoryPlacementProbes] - p1;

        // Assert: one merge visit (the first list node takes the unit) - never a container scan; the Small (1 x 2) needs rows 0-1:
        // anchors (0,0) .. (4,0) are blocked, (5,0) is free -> 6 anchors x 2 footprint cells = 12 cell probes
        Assert.Equal(1, mergeVisits);
        Assert.Equal(0, p1 - p0);
        Assert.Equal(12, cellProbes);
        Assert.Equal((5, 0), (rig.Entries[small].X, rig.Entries[small].Y));
    }

    [Fact]
    [Trait("Id", "TR-inventory-007")]
    public void ProbeCounters_AMediumScan_CountsFourCellsPerAnchorVisited_NotOnePerAnchor()
    {
        // Arrange: a fixture 8 x 2 grid with letters at (1,1), (3,0) and (5,1); a 2 x 2 Medium to place
        var rig = new InvAutoPlaceRig();
        ushort grid = rig.CreateGrid(8, 2);
        _ = rig.PlaceMail(grid, "letter", 1, 1, Address9, new uint[] { 1 });
        _ = rig.PlaceMail(grid, "letter", 3, 0, Address9, new uint[] { 2 });
        _ = rig.PlaceMail(grid, "letter", 5, 1, Address9, new uint[] { 3 });
        PlacementSubject medium = rig.Mail("medium", Address2);
        long p0 = rig.Counters[CounterId.InventoryPlacementProbes];

        // Act: a pure plan (Medium: effMax 1 skips PASS M; w == h skips PASS R)
        var plan = new InvPlan();
        ushort reason = rig.Inventory.AutoPlace.Plan(ref plan, in medium, 1, AutoPlace.NoSource, new[] { grid }, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);
        long cellProbes = rig.Counters[CounterId.InventoryPlacementProbes] - p0;

        // Assert: the only anchor row is y = 0; anchors x = 0..5 each hold a letter in their 2 x 2 footprint and x = 6 is free, so
        // 7 anchors are visited, each examining 4 cells: 7 x 4 = 28 cell probes (per-anchor counting would give 7)
        const long AnchorsVisited = 7;
        Assert.Equal(0, reason);
        Assert.Equal((6, 0), (ap.Placement.X, ap.Placement.Y));
        Assert.Equal(AnchorsVisited * 4, cellProbes);
        Assert.NotEqual(AnchorsVisited, cellProbes);
    }

    [Fact]
    [Trait("Id", "H-PERF-6")]
    public void ProbeCounters_FullIntake_EveryV1OrientedShapeScannedToNoFit_IsExactly6684CellProbes_WithinTheD3BoundAndTheGate()
    {
        // Arrange: the 20 x 16 po_intake filled with 40 Larges (no free cell)
        var rig = new InvAutoPlaceRig();
        FillIntakeWithLarges(rig);
        var perKind = new long[_v1MailKinds.Length];

        // Act: one pure NoMerge plan per v1 kind into the Intake: letter (U 1 x 1), small (U 1 x 2, R 2 x 1), medium (U 2 x 2),
        // large (U 2 x 4, R 4 x 2) - the six v1 oriented shapes, each scanned once
        for (int k = 0; k < _v1MailKinds.Length; k++)
        {
            PlacementSubject subject = rig.Mail(_v1MailKinds[k], Address2);
            var plan = new InvPlan();
            long p = rig.Counters[CounterId.InventoryPlacementProbes];
            Assert.Equal((ushort)InvReason.NoRoom, rig.Inventory.AutoPlace.Plan(ref plan, in subject, 1, AutoPlace.NoSource, new[] { rig.Intake }, MergePolicy.NoMerge, PlaceOrder.PassFirst, out _));
            perKind[k] = rig.Counters[CounterId.InventoryPlacementProbes] - p;
        }

        // Assert: an anchor's footprint test examines all fw x fh cells, so a scan costs (anchors visited) x area and visits at most
        // (20 - fw + 1)(16 - fh + 1) anchors; a no-fit scan visits all of them, so the full Intake is the exact worst case:
        //   letter 1x1: 320 anchors x 1 = 320
        //   small  1x2: 300 x 2 = 600, + R 2x1: 304 x 2 = 608 -> 1,208
        //   medium 2x2: 285 x 4 = 1,140
        //   large  2x4: 247 x 8 = 1,976, + R 4x2: 255 x 8 = 2,040 -> 4,016
        // total 6,684 = sum of anchors x area over the six shapes (1,711 anchors). No occupancy exceeds it: a scan that fits stops
        // at the fitting anchor, which is one of the counted anchors (AutoPlace keeps no hint cursor, so nothing is re-probed and
        // there is no separate "+ placements" term). GDD D3's hinted-pack bound C x sum(a) + n x a <= 320 x 25 + 320 = 8,320.
        long total = perKind.Sum();
        Assert.Equal(new long[] { 320, 1_208, 1_140, 4_016 }, perKind);
        Assert.Equal(6_684, total);
        Assert.True(total <= D3IntakeBound, $"{total} > D3 bound {D3IntakeBound}");
        Assert.True(total < PerfGateCellProbes, $"{total} >= H-PERF-6 gate {PerfGateCellProbes}");
    }

    [Fact]
    [Trait("Id", "H-PERF-6")]
    public void ProbeCounters_RandomIntakeOccupancies_EveryV1Sweep_StaysWithinSumOfAnchorsTimesArea_AndUnderThe8500Gate()
    {
        // Arrange: the bound from content - the v1 kinds' oriented shapes on the Intake (C = 320, sum of areas 25)
        var probe = new InvAutoPlaceRig();
        ContainerHeader intake = probe.Header(probe.Intake);
        long anchorsTimesArea = 0;
        int sumArea = 0;
        foreach (string kind in _v1MailKinds)
        {
            MailKindDef def = probe.Content.MailKinds[probe.KindIndex(kind)];
            foreach ((int fw, int fh) in def.GridW == def.GridH
                ? new[] { (def.GridW, def.GridH) }
                : new[] { (def.GridW, def.GridH), (def.GridH, def.GridW) })
            {
                anchorsTimesArea += (long)(intake.W - fw + 1) * (intake.H - fh + 1) * fw * fh;
                sumArea += fw * fh;
            }
        }

        int cells = intake.W * intake.H;
        Assert.Equal(25, sumArea);
        Assert.Equal(6_684, anchorsTimesArea);
        Assert.Equal(D3IntakeBound, ((long)cells * sumArea) + cells); // GDD D3: C x sum(a) + n x a, with n x a <= C
        Assert.True(D3IntakeBound < PerfGateCellProbes);

        uint state = 0x2545_F491u;
        var sweeps = new List<long>();
        for (int occupancy = 0; occupancy < 6; occupancy++)
        {
            var rig = new InvAutoPlaceRig();
            FillRandomly(rig, rig.Intake, attempts: 8 + (occupancy * 16), ref state);

            // Act: one sweep - a pure NoMerge plan of every v1 kind into the Intake
            long p = rig.Counters[CounterId.InventoryPlacementProbes];
            foreach (string kind in _v1MailKinds)
            {
                PlacementSubject subject = rig.Mail(kind, Address2);
                var plan = new InvPlan();
                _ = rig.Inventory.AutoPlace.Plan(ref plan, in subject, 1, AutoPlace.NoSource, new[] { rig.Intake }, MergePolicy.NoMerge, PlaceOrder.PassFirst, out _);
            }

            sweeps.Add(rig.Counters[CounterId.InventoryPlacementProbes] - p);
        }

        // Assert: every sweep is positive, within the per-cell maximum (6,684) and under the 8,500 gate
        Assert.All(sweeps, total =>
        {
            Assert.InRange(total, 1, anchorsTimesArea);
            Assert.True(total < PerfGateCellProbes, $"{total} >= H-PERF-6 gate {PerfGateCellProbes}");
        });
    }

    [Fact]
    [Trait("Id", "TR-inventory-007")]
    public void ProbeCounters42And43_AreNotInTheCanonicalDump_RaisingThemLeavesTheDumpBytesUnchanged()
    {
        // Arrange: a 5-letter stack to #2 (a merge candidate) and a world dump before any probe
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 3, 1, Address2, InvAutoPlaceRig.Ids(1, 5));
        PlacementSubject letter = rig.Mail("letter", Address2);
        PlacementSubject small = rig.Mail("small", Address2);
        long m0 = rig.Counters[CounterId.InventoryMergeCandidates];
        long p0 = rig.Counters[CounterId.InventoryPlacementProbes];
        byte[] before = DumpHash.Bytes(rig.Host.World);

        // Act: pure plans (they raise only the two counters), then direct writes of both and of counters 44 and 45
        var a = new InvPlan();
        var b = new InvPlan();
        Assert.Equal(0, rig.Inventory.AutoPlace.Plan(ref a, in letter, 1, AutoPlace.NoSource, new[] { rig.InventoryA }, MergePolicy.Merge, PlaceOrder.PassFirst, out _));
        Assert.Equal(0, rig.Inventory.AutoPlace.Plan(ref b, in small, 1, AutoPlace.NoSource, new[] { rig.InventoryA }, MergePolicy.Merge, PlaceOrder.PassFirst, out _));
        long m1 = rig.Counters[CounterId.InventoryMergeCandidates];
        long p1 = rig.Counters[CounterId.InventoryPlacementProbes];
        rig.Counters.Add(CounterId.InventoryMergeCandidates, 1_000_000);
        rig.Counters.Set(CounterId.InventoryPlacementProbes, 123_456_789);
        rig.Counters.Add(CounterId.InventoryOutfeedCandidates, 1_000_000);
        rig.Counters.Add(CounterId.MutatingApiOutOfPhase, 7);
        byte[] after = DumpHash.Bytes(rig.Host.World);

        // Assert: both counters moved, the dump did not
        Assert.Equal(m0 + 1, m1);
        Assert.True(p1 > p0, $"placement probes {p0} -> {p1}");
        Assert.Equal(before, after);

        // Assert (control): a committed change to the state does change the dump
        ushort one = rig.DetachedMail("letter", Address2, new uint[] { 99 });
        Assert.Equal(0, rig.AutoPlace(one, rig.InventoryA));
        Assert.Equal(6, rig.Entries[stack].Count);
        Assert.NotEqual(after, DumpHash.Bytes(rig.Host.World));
    }

    // ---------------------------------------------------------------- MergeInto

    [Fact]
    public void MergeInto_PartialFromAPlacedSource_OldestIdsMove_InvRemovedThenInvMergedWithRuns_BothVersionsPlusOne()
    {
        // Arrange: chest 5 letters (ids 10..14) at (0,0); inventory 3 letters (20..22) at (0,0)
        var rig = new InvAutoPlaceRig();
        ushort chest = rig.CreateGrid(8, 4);
        rig.Inventory.Subscribers.Set(chest, 0b1);
        ushort source = rig.PlaceMail(chest, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(10, 5));
        ushort target = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, new uint[] { 20, 21, 22 });
        uint chestV = rig.Header(chest).Version;
        uint invV = rig.Header(rig.InventoryA).Version;

        // Act
        Reply reply = rig.Step(
            (ref InvPlan p) => p.Touch(chest) < 0 || p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.MergeInto(ref p, source, target, 2));
        EventEntry[] log = rig.LastLog;

        // Assert: state
        Assert.True(reply.IsOk);
        Assert.Equal(new uint[] { 12, 13, 14 }, rig.IdsOf(source));
        Assert.Equal(new uint[] { 10, 11, 20, 21, 22 }, rig.IdsOf(target));
        Assert.Equal(3, rig.Entries[source].Count);
        Assert.Equal(5, rig.Entries[target].Count);
        Assert.Equal(chestV + 1, rig.Header(chest).Version);
        Assert.Equal(invV + 1, rig.Header(rig.InventoryA).Version);

        // Assert: records — InvRemoved{chest, source, 2}, InvMerged{inventory, target, 2, 1 run} + run (10, 2), then the seals
        EventEntry[] inv = log.Where(e => e.Kind != EventKind.OpReply).ToArray();
        Assert.Equal(InvEventKinds.Removed, inv[0].Kind);
        InvRemovedPayload removed = inv[0].PayloadAs<InvRemovedPayload>();
        Assert.Equal(rig.Header(chest).Id.Value, removed.Container);
        Assert.Equal(rig.Entries[source].Id.Value, removed.EntryId);
        Assert.Equal(2, removed.Count);
        Assert.Equal(InvEventKinds.Merged, inv[1].Kind);
        InvMergedPayload merged = inv[1].PayloadAs<InvMergedPayload>();
        Assert.Equal(rig.Entries[target].Id.Value, merged.TargetEntryId);
        Assert.Equal(2, merged.AddedCount);
        Assert.Equal(1, merged.RunCount);
        Assert.Equal(EventFlags.Continuation, inv[2].Flags);
        Assert.Equal(inv[1].CallSeq, inv[2].CallSeq);
        InvMailIdRuns runs = inv[2].PayloadAs<InvMailIdRuns>();
        Assert.Equal(10u, runs[0].Start);
        Assert.Equal(2, runs[0].Length);
        Assert.Equal(InvEventKinds.Version, inv[3].Kind);
        Assert.Equal(InvEventKinds.Version, inv[4].Kind);
        Assert.Equal(5, inv.Length);
    }

    [Fact]
    public void MergeInto_TheWholeSource_RemovesAndFreesIt_InvRemovedCarriesTheWholeCount_CellsFreed()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort source = rig.PlaceMail(rig.InventoryA, "letter", 4, 1, Address2, new uint[] { 10, 12, 14 });
        ushort target = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, new uint[] { 11, 13 });
        int live = rig.Entries.LiveCount;
        ushort free = rig.Header(rig.InventoryA).FreeCells;
        uint v = rig.Header(rig.InventoryA).Version;

        // Act
        Reply reply = rig.Step(
            (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.MergeInto(ref p, source, target, 3));
        EventEntry[] log = rig.LastLog;

        // Assert: an in-container merge seals once; the 3 non-consecutive moved ids are 3 runs
        Assert.True(reply.IsOk);
        Assert.False(rig.Entries[source].IsLive);
        Assert.Equal(live - 1, rig.Entries.LiveCount);
        Assert.Equal(free + 1, rig.Header(rig.InventoryA).FreeCells);
        Assert.Equal(Occupancy.Free, rig.Inventory.Occupancy.OwnerAt(rig.InventoryA, 8, 4, 1));
        Assert.Equal(new uint[] { 10, 11, 12, 13, 14 }, rig.IdsOf(target));
        Assert.Equal(v + 1, rig.Header(rig.InventoryA).Version);
        InvRemovedPayload removed = Assert.Single(log, e => e.Kind == InvEventKinds.Removed).PayloadAs<InvRemovedPayload>();
        Assert.Equal(3, removed.Count);
        InvMergedPayload merged = Assert.Single(log, e => e.Kind == InvEventKinds.Merged && e.Flags == EventFlags.None).PayloadAs<InvMergedPayload>();
        Assert.Equal(3, merged.AddedCount);
        Assert.Equal(3, merged.RunCount);
        Assert.Single(log, e => e.Kind == InvEventKinds.Version);
    }

    [Fact]
    public void MergeInto_Items_MoveCountsOnly_NoContinuation_AndTheIndexFollowsTheCounts()
    {
        // Arrange: planks 15 and 4 (effMax 20)
        var rig = new InvAutoPlaceRig();
        ushort source = rig.PlaceItem(rig.InventoryA, "plank", 1, 0, 15);
        ushort target = rig.PlaceItem(rig.InventoryA, "plank", 0, 0, 4);
        Assert.True(rig.Index.Contains(source));
        Assert.True(rig.Index.Contains(target));

        // Act
        Reply reply = rig.Step(
            (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.MergeInto(ref p, source, target, 15));

        // Assert: 15 moved -> target 19 (still non-full, linked), source freed (unlinked)
        Assert.True(reply.IsOk);
        Assert.Equal(19, rig.Entries[target].Count);
        Assert.False(rig.Entries[source].IsLive);
        Assert.True(rig.Index.Contains(target));
        Assert.Equal(1, rig.Index.LinkedCount);
        InvMergedPayload merged = Assert.Single(rig.LastLog, e => e.Kind == InvEventKinds.Merged).PayloadAs<InvMergedPayload>();
        Assert.Equal(15, merged.AddedCount);
        Assert.Equal(0, merged.RunCount);
    }

    [Fact]
    public void MergeInto_DetachedSource_RaisesOnlyTheTargetRecords_TargetFullLeavesTheIndex()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        ushort target = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 18));
        ushort source = rig.DetachedMail("letter", Address2, new uint[] { 50, 51, 52 });
        Assert.True(rig.Index.Contains(target));

        // Act
        Reply reply = rig.Step(
            (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.MergeInto(ref p, source, target, 2));

        // Assert
        Assert.True(reply.IsOk);
        Assert.Equal(20, rig.Entries[target].Count);
        Assert.False(rig.Index.Contains(target));
        Assert.Equal(new uint[] { 52 }, rig.IdsOf(source));
        Assert.DoesNotContain(rig.LastLog, e => e.Kind == InvEventKinds.Removed);
        Assert.Single(rig.LastLog, e => e.Kind == InvEventKinds.Merged && e.Flags == EventFlags.None);
    }

    [Fact]
    public void MergeInto_AnInvalidMerge_AssertsUnderInventoryAssert_OrChangesNothingInRelease()
    {
        // Arrange: different keys (address 2 vs 9)
        var rig = new InvAutoPlaceRig();
        ushort a = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, new uint[] { 1 });
        ushort b = rig.PlaceMail(rig.InventoryA, "letter", 1, 0, Address9, new uint[] { 2 });
        var plan = new InvPlan();
        _ = plan.Touch(rig.InventoryA);

        // Act + Assert (between ticks: the assert fires before anything is changed or raised)
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => rig.Inventory.Mutator.MergeInto(ref plan, a, b, 1));
            Assert.Equal(SimAssertCode.InventoryMergeInvalid, thrown.Code);
        }
        else
        {
            rig.Inventory.Mutator.MergeInto(ref plan, a, b, 1);
        }

        Assert.Equal(1, rig.Entries[a].Count);
        Assert.Equal(1, rig.Entries[b].Count);
        Assert.Equal(0, plan.MutatedCount);
        Assert.Equal(69, SimAssertCode.InventoryMergeInvalid);
    }

    [Fact]
    public void MergeInto_PastTheTargetsEffMax_IsInvalid_AssertsUnderInventoryAssert_OrChangesNothingInRelease()
    {
        // Arrange: a 19-letter target (effMax 20) and a 3-letter source of the same key; 2 units would make 21
        var rig = new InvAutoPlaceRig();
        ushort target = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 19));
        ushort source = rig.PlaceMail(rig.InventoryA, "letter", 1, 0, Address2, new uint[] { 50, 51, 52 });
        Assert.Equal(20, rig.SubjectOf(target).EffMax);
        var plan = new InvPlan();
        _ = plan.Touch(rig.InventoryA);

        // Act + Assert (between ticks: the assert fires before anything is changed or raised)
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => rig.Inventory.Mutator.MergeInto(ref plan, source, target, 2));
            Assert.Equal(SimAssertCode.InventoryMergeInvalid, thrown.Code);
        }
        else
        {
            rig.Inventory.Mutator.MergeInto(ref plan, source, target, 2);
        }

        Assert.Equal(19, rig.Entries[target].Count);
        Assert.Equal(3, rig.Entries[source].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(1, 19), rig.IdsOf(target));
        Assert.Equal(new uint[] { 50, 51, 52 }, rig.IdsOf(source));
        Assert.Equal(0, plan.MutatedCount);
    }

    [Fact]
    public void MergeInto_UpToTheTargetsEffMax_IsValid()
    {
        // Arrange: the same 19-letter target; exactly 1 unit fills it to effMax 20
        var rig = new InvAutoPlaceRig();
        ushort target = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 19));
        ushort source = rig.PlaceMail(rig.InventoryA, "letter", 1, 0, Address2, new uint[] { 50, 51, 52 });

        // Act
        Reply reply = rig.Step(
            (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.MergeInto(ref p, source, target, 1));

        // Assert
        Assert.True(reply.IsOk);
        Assert.Equal(20, rig.Entries[target].Count);
        Assert.Equal(new uint[] { 51, 52 }, rig.IdsOf(source));
        Assert.False(rig.Index.Contains(target));
    }

    // ---------------------------------------------------------------- count above effMax (after a MailMaxStack drop)

    [Fact]
    public void CountAboveEffMax_AWholeCountSourceWithMergeRoom_PlansOnlyTheMerges_TheRestStaysOnTheSource_Ok()
    {
        // Arrange: a 25-letter chest stack (above effMax 20, as after a MailMaxStack drop; never indexed); a 17-stack has room 3
        var rig = new InvAutoPlaceRig();
        ushort chest = rig.CreateGrid(8, 4);
        ushort source = rig.PlaceMail(chest, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 25));
        ushort target = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(100, 17));
        Assert.Equal(20, rig.SubjectOf(source).EffMax);
        Assert.False(rig.Index.Contains(source));

        // Act
        ushort reason = rig.AutoPlace(source, rig.InventoryA);

        // Assert: 3 merged, no free-cell placement of a 22 > effMax remainder; the source keeps it in place
        Assert.Equal(0, reason);
        Assert.Equal(1, rig.LastPlan.MergeCount);
        Assert.Equal(3, rig.LastPlan.MergedUnits);
        Assert.False(rig.LastPlan.Placement.IsSet);
        Assert.Equal(22, rig.LastPlan.Unplaced);
        Assert.Equal(20, rig.Entries[target].Count);
        Assert.Equal(22, rig.Entries[source].Count);
        Assert.Equal(chest, rig.Entries[source].ContainerSlot);
        Assert.Equal(InvAutoPlaceRig.Ids(4, 22), rig.IdsOf(source));
    }

    [Fact]
    public void CountAboveEffMax_AWholeCountSourceWithNoMergeRoom_IsNoRoom_EvenWithFreeCells_NothingChanges()
    {
        // Arrange: the 25-letter source and an empty inventory (free cells, nothing to merge into)
        var rig = new InvAutoPlaceRig();
        ushort chest = rig.CreateGrid(8, 4);
        ushort source = rig.PlaceMail(chest, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 25));
        uint chestVersion = rig.Header(chest).Version;
        uint invVersion = rig.Header(rig.InventoryA).Version;

        // Act
        ushort reason = rig.AutoPlace(source, rig.InventoryA);

        // Assert
        Assert.Equal((ushort)InvReason.NoRoom, reason);
        Assert.Equal(0, rig.LastPlan.MergeCount);
        Assert.False(rig.LastPlan.Placement.IsSet);
        Assert.Equal(25, rig.LastPlan.Unplaced);
        Assert.Equal(25, rig.Entries[source].Count);
        Assert.Equal(chest, rig.Entries[source].ContainerSlot);
        Assert.Equal(chestVersion, rig.Header(chest).Version);
        Assert.Equal(invVersion, rig.Header(rig.InventoryA).Version);
    }

    [Fact]
    public void CountAboveEffMax_MergesThatBringTheRemainderToEffMax_ThenMoveTheSourceEntryItself()
    {
        // Arrange: the 25-letter source; a 15-stack has room 5, leaving 20 (= effMax) for the free-cell placement
        var rig = new InvAutoPlaceRig();
        ushort chest = rig.CreateGrid(8, 4);
        ushort source = rig.PlaceMail(chest, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 25));
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(100, 15));

        // Act
        ushort reason = rig.AutoPlace(source, rig.InventoryA);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(5, rig.LastPlan.MergedUnits);
        Assert.True(rig.LastPlan.Placement.IsSet);
        Assert.Equal(20, rig.LastPlan.PlacementUnits);
        Assert.Equal(0, rig.LastPlan.Unplaced);
        Assert.Equal(rig.InventoryA, rig.Entries[source].ContainerSlot);
        Assert.Equal(20, rig.Entries[source].Count);
    }

    [Fact]
    public void CountAboveEffMax_ANoSourceSpawnOfMoreThanEffMax_PlansOneEffMaxEntry_AndReportsTheRestUnplaced()
    {
        // Arrange: 30 letters to #2 spawned (no source entry) into an empty inventory; effMax 20
        var rig = new InvAutoPlaceRig();
        PlacementSubject letter = rig.Mail("letter", Address2);
        Assert.Equal(20, letter.EffMax);
        var plan = new InvPlan();

        // Act
        ushort reason = rig.Inventory.AutoPlace.Plan(
            ref plan, in letter, 30, AutoPlace.NoSource, new[] { rig.InventoryA }, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);

        // Assert
        Assert.Equal(0, reason);
        Assert.Equal(0, ap.MergeCount);
        Assert.True(ap.Placement.IsSet);
        Assert.Equal(20, ap.PlacementUnits);
        Assert.Equal(10, ap.Unplaced);
    }

    [Fact]
    public void TargetFirst_ANoSourceSpawnOfMoreThanEffMax_PlacesOneEffMaxEntryInTheFirstTarget_AndReportsTheRestUnplaced()
    {
        // Pins current behaviour, not a confirmed rule: TD-130 item 8 (whether TargetFirst callers, story 014, intend this is open)
        // Arrange: 30 letters to #2 spawned (no source entry) into [hotbar (empty), inventory (a 15-stack with room 5)]; effMax 20
        var rig = new InvAutoPlaceRig();
        ushort stack = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, InvAutoPlaceRig.Ids(1, 15));
        PlacementSubject letter = rig.Mail("letter", Address2);
        Assert.Equal(20, letter.EffMax);
        ushort[] targets = { rig.HotbarA, rig.InventoryA };
        var targetPlan = new InvPlan();
        var passPlan = new InvPlan();

        // Act
        ushort reason = rig.Inventory.AutoPlace.Plan(ref targetPlan, in letter, 30, AutoPlace.NoSource, targets, MergePolicy.Merge, PlaceOrder.TargetFirst, out AutoPlacePlan tf);
        ushort passReason = rig.Inventory.AutoPlace.Plan(ref passPlan, in letter, 30, AutoPlace.NoSource, targets, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan pf);

        // Assert: target-first fills the hotbar first (M finds nothing, U takes slot 1 with one effMax entry); one plan holds at most
        // one free-cell placement, so it stops there and the inventory's merge room is not visited: 10 unplaced
        Assert.Equal(0, reason);
        Assert.Equal(0, tf.MergeCount);
        Assert.True(tf.Placement.IsSet);
        Assert.Equal(rig.HotbarA, tf.Placement.ContainerSlot);
        Assert.Equal((1, 0), (tf.Placement.X, tf.Placement.Y));
        Assert.Equal(AutoPlace.NoSource, tf.Placement.EntrySlot);
        Assert.Equal(20, tf.PlacementUnits);
        Assert.Equal(10, tf.Unplaced);
        Assert.Equal(1, targetPlan.TouchedCount);
        Assert.Equal(rig.HotbarA, targetPlan.TouchedSlot(0));

        // Assert (contrast): pass-first merges 5 into the inventory stack first, then places one effMax entry in the hotbar: 5 unplaced
        Assert.Equal(0, passReason);
        Assert.Equal(1, pf.MergeCount);
        Assert.Equal(stack, pf.Merge(0).TargetEntrySlot);
        Assert.Equal(5, pf.Merge(0).Units);
        Assert.Equal(rig.HotbarA, pf.Placement.ContainerSlot);
        Assert.Equal(20, pf.PlacementUnits);
        Assert.Equal(5, pf.Unplaced);
    }

    // ---------------------------------------------------------------- index hooks

    [Fact]
    public void Index_FollowsPlaceRemoveAndTransfer_RowMajor_AndRebuildIndexRestoresFixtureArrangedEntries()
    {
        // Arrange
        var rig = new InvAutoPlaceRig();
        StackKey key = rig.Mail("letter", Address2).Key;
        ushort c = rig.PlaceMail(rig.InventoryA, "letter", 6, 1, Address2, new uint[] { 3 });
        ushort a = rig.PlaceMail(rig.InventoryA, "letter", 7, 0, Address2, new uint[] { 1 });
        ushort b = rig.PlaceMail(rig.InventoryA, "letter", 0, 1, Address2, new uint[] { 2 });

        // Act + Assert: row-major (7,0) < (0,1) < (6,1)
        Assert.Equal(new[] { a, b, c }, ListOf(rig, rig.InventoryA, key));
        Reply moved = rig.Step(
            (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.Transfer(ref p, a, rig.InventoryA, 7, 1, false));
        Assert.True(moved.IsOk);
        Assert.Equal(new[] { b, c, a }, ListOf(rig, rig.InventoryA, key));
        Reply removed = rig.Step(
            (ref InvPlan p) => p.Touch(rig.InventoryA) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.Remove(ref p, c));
        Assert.True(removed.IsOk);
        Assert.Equal(new[] { b, a }, ListOf(rig, rig.InventoryA, key));

        // Act + Assert: a rebuild after unlinking everything restores the same lists
        rig.Index.Unlink(a);
        rig.Index.Unlink(b);
        Assert.Equal(0, rig.Index.HeadCount);
        rig.Inventory.Mutator.RebuildIndex(rig.InventoryA);
        Assert.Equal(new[] { b, a }, ListOf(rig, rig.InventoryA, key));
        Assert.Equal(1, rig.Index.HeadCount);
    }

    [Fact]
    public void RebuildIndex_UnlinksAStaleLinkWhoseCellsWereClearedOutsideTheMutator()
    {
        // Arrange: two linked letter stacks; b's cell is cleared directly (no mutator), so its link survives with no cell behind it
        var rig = new InvAutoPlaceRig();
        StackKey key = rig.Mail("letter", Address2).Key;
        ushort a = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address2, new uint[] { 1 });
        ushort b = rig.PlaceMail(rig.InventoryA, "letter", 3, 1, Address2, new uint[] { 2 });
        rig.Inventory.Occupancy.Clear(rig.InventoryA, 8, 2, 3, 1, 1, 1);
        Assert.True(rig.Index.Contains(b));

        // Act
        rig.Inventory.Mutator.RebuildIndex(rig.InventoryA);

        // Assert
        Assert.False(rig.Index.Contains(b));
        Assert.Equal(new[] { a }, ListOf(rig, rig.InventoryA, key));
        Assert.Equal(1, rig.Index.LinkedCount);
    }

    // ---------------------------------------------------------------- helpers

    internal static void FillIntakeWithLarges(InvAutoPlaceRig rig)
    {
        uint id = 1000;
        for (int y = 0; y < 16; y += 4)
        {
            for (int x = 0; x < 20; x += 2)
            {
                _ = rig.PlaceMail(rig.Intake, "large", x, y, Address9, new[] { id++ });
            }
        }

        Assert.Equal(0, rig.Header(rig.Intake).FreeCells);
    }

    /// <summary>Tries <paramref name="attempts"/> xorshift-chosen (kind, rotation, anchor) placements, placing each whose footprint is free.</summary>
    private static void FillRandomly(InvAutoPlaceRig rig, ushort container, int attempts, ref uint state)
    {
        ContainerHeader h = rig.Header(container);
        uint id = 3000;
        for (int i = 0; i < attempts; i++)
        {
            string kind = _v1MailKinds[(int)(Xorshift(ref state) % (uint)_v1MailKinds.Length)];
            MailKindDef def = rig.Content.MailKinds[rig.KindIndex(kind)];
            bool rotated = def.GridW != def.GridH && (Xorshift(ref state) & 1u) == 1u;
            int fw = rotated ? def.GridH : def.GridW;
            int fh = rotated ? def.GridW : def.GridH;
            int x = (int)(Xorshift(ref state) % (uint)(h.W - fw + 1));
            int y = (int)(Xorshift(ref state) % (uint)(h.H - fh + 1));
            ulong mask = ((1UL << fw) - 1UL) << x;
            bool free = true;
            for (int r = 0; r < fh; r++)
            {
                free &= (rig.Inventory.Occupancy.RowBits(container, y + r) & mask) == 0UL;
            }

            if (free)
            {
                _ = rig.PlaceMail(container, kind, x, y, Address9, new[] { id++ }, rotated);
            }
        }
    }

    private static uint Xorshift(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    internal static List<ushort> ListOf(InvAutoPlaceRig rig, ushort container, StackKey key)
    {
        var list = new List<ushort>();
        for (ushort s = rig.Index.Head(container, key); s != StackIndex.None; s = rig.Index.Next(s))
        {
            list.Add(s);
        }

        return list;
    }

    internal static void FillWithMediums(InvAutoPlaceRig rig, ushort container, params (int X, int Y)[] at)
    {
        uint id = 5000u + (uint)(rig.Entries.LiveCount * 10);
        foreach ((int x, int y) in at)
        {
            _ = rig.PlaceMail(container, "medium", x, y, Address9, new[] { id++ });
        }
    }

    internal static List<long> StateOf(InvAutoPlaceRig rig)
    {
        var values = new List<long>();
        for (int s = 0; s < rig.Inventory.Containers.LiveCount; s++)
        {
            ContainerHeader h = rig.Inventory.Containers[(ushort)s];
            values.AddRange(new long[] { h.Id.Value, h.W, h.H, h.FreeCells, h.EntryCount, h.Version, h.StatEpoch });
            for (int y = 0; y < h.H; y++)
            {
                values.Add((long)rig.Inventory.Occupancy.RowBits((ushort)s, y));
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
            if (!r.IsLive)
            {
                continue;
            }

            values.AddRange(new long[] { s, r.Id.Value, r.Count, r.ContainerSlot, r.X, r.Y, r.Rotated ? 1 : 0 });
            foreach (uint id in rig.IdsOf((ushort)s))
            {
                values.Add(id);
            }
        }

        return values;
    }

    /// <summary>Free arena blocks in <paramref name="fromClass"/> and every larger class (the blocks a run of that class can borrow).</summary>
    private static int FreeBlocksFrom(MailIdArena arena, int fromClass)
    {
        int free = 0;
        for (int c = fromClass; c < MailIdArena.ClassCount; c++)
        {
            free += arena.FreeBlocks(c);
        }

        return free;
    }

    private static readonly ModifierSource _doubleStackSource = new(SourceKind.Perk, DefIndex.FromValue(0), 0, ModifierOwner.Team, 1, ModifierLifetime.Run);

    private static bool PushDoubleStack(InvAutoPlaceRig rig)
    {
        ModifierSource src = _doubleStackSource;
        Modifier Leg(string kind) => new(
            src.Kind, src.Id, src.StackIndex, PlayerSlot.None, Stat.MailMaxStack, rig.KindIndex(kind).Value, ModifierOp.Mul,
            GridMailRuleFixtures.DoubleStackPm, src.Lifetime);
        return rig.Host.Services.Stats.Push(in src, [Leg("letter"), Leg("postcard")]);
    }
}
