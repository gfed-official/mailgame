using System.Collections.Immutable;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/016 (GDD <c>grid-inventory.md</c> R16, D1; TR-inventory-015; ADR-0006 section 2; ADR-0010): the per-slot
/// weight points <c>P</c> the mutator keeps in <c>Place</c> / <c>Remove</c> over the counted containers (an owner slot: hotbar,
/// player_inventory, cursor), <see cref="IInventoryApi.WeightPoints"/> (a <see cref="short"/> saturating at <see cref="short.MaxValue"/>,
/// assert 79) and <see cref="IInventoryApi.SpeedPermille"/> (D1, one <c>Fix.MulDiv</c>, <c>WeightPenaltyPm</c> read through the stat
/// sheet). The shipped knobs: <c>classPts {1, 3, 8}</c>, <c>ptsPermille</c> 10, floor 600.
/// </summary>
public class WeightSpeedTests
{
    private const uint Address4 = 4u;

    private static readonly PlayerSlot _slot0 = PlayerSlot.FromValue(0);

    // --- H-D1-1 -------------------------------------------------------------------------------------------------------------

    [Theory]
    [Trait("Id", "H-D1-1")]
    [InlineData(0, 1000, 1000)]
    [InlineData(2, 980, 990)]
    [InlineData(8, 920, 960)]
    [InlineData(12, 880, 940)]
    [InlineData(39, 610, 805)]
    [InlineData(40, 600, 800)]
    [InlineData(41, 600, 800)]
    [InlineData(112, 600, 800)]
    public void D1_ThePointsTable_GivesTheGddSpeeds_WithAndWithoutStrongBack(int points, int plain, int strongBack)
    {
        // Arrange
        long[] block = new long[SimCounterSink.RequiredLength];
        var sink = new SimCounterSink(block);

        // Act: WeightPenaltyPm 1000 (no modifier) and 500 (a Mul 500 Strong Back source)
        int noPerk = Weight.SpeedPermille(points, 10, 600, 1000, ref sink);
        int perk = Weight.SpeedPermille(points, 10, 600, 500, ref sink);

        // Assert
        Assert.Equal(plain, noPerk);
        Assert.Equal(strongBack, perk);
        Assert.All(block, c => Assert.Equal(0, c));
    }

    [Fact]
    [Trait("Id", "H-D1-1")]
    public void SpeedPermille_ReadsWeightPenaltyPm_ThroughTheStatSheet_ForTheSlot()
    {
        // Arrange: P = 39 in slot 0 through the mutator (4 Large + 2 Medium + 1 letter in the hotbar)
        var rig = new InvAutoPlaceRig();
        FillHotbarTo39(rig);
        Assert.Equal(39, rig.Inventory.WeightPoints(_slot0));
        int before = rig.Inventory.SpeedPermille(_slot0);

        // Act: a synthetic Strong Back source for slot 0 (WeightPenaltyPm Mul 500)
        Assert.True(PushStrongBack(rig.Host, _slot0, 500));

        // Assert: 610 without it, 805 with it; slot 1 (no containers) reads P 0 and 1000
        Assert.Equal(610, before);
        Assert.Equal(805, rig.Inventory.SpeedPermille(_slot0));
        Assert.Equal(0, rig.Inventory.WeightPoints(PlayerSlot.FromValue(1)));
        Assert.Equal(1000, rig.Inventory.SpeedPermille(PlayerSlot.FromValue(1)));
        Assert.Equal(0, rig.Inventory.WeightPoints(PlayerSlot.None));
        Assert.Equal(1000, rig.Inventory.SpeedPermille(PlayerSlot.None));
    }

    // --- H-D1-2 -------------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Id", "H-D1-2")]
    public void D1_P39WithStrongBack_WalkAndSprintTruncate_AndCarrierSpeedFinalIsOneDivision()
    {
        // Arrange
        long[] block = new long[SimCounterSink.RequiredLength];
        var sink = new SimCounterSink(block);
        int weightPm = Weight.SpeedPermille(39, 10, 600, 500, ref sink);

        // Act
        int walk = CarrierSpeed.Final(5000, weightPm, 0, 0, 1000, ref sink);
        int sprint = CarrierSpeed.Final(7500, weightPm, 0, 0, 1000, ref sink);
        int boosted = CarrierSpeed.Final(7500, weightPm, addPm: 500, buffCapPm: 500, wadePm: 1000, ref sink);
        long twoDivisions = Fix.MulDiv(Fix.MulDiv(7500, weightPm, 1000, ref sink), 1500, 1000, ref sink);

        // Assert: 805; 4025; 6037 (6037.5 truncated); 7500 x 805 x 1500 / 10^6 = 9056 (9056.25), the two-division chain gives 9055
        Assert.Equal(805, weightPm);
        Assert.Equal(4025, walk);
        Assert.Equal(6037, sprint);
        Assert.Equal(9056, boosted);
        Assert.Equal(9055, twoDivisions);
        Assert.NotEqual(twoDivisions, boosted);
    }

    // --- H-R16-1 a/b --------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Id", "H-R16-1")]
    public void R16_CountedPanelsAndCursor_P13Speed870_TheMediumIntoTheChest_P10Speed900()
    {
        // Arrange: hotbar Large (8); inventory a 20-letter and a 1-letter stack (1 + 1); cursor Medium (3); the Chest (no owner)
        // holds 2 Large. Entries are arranged directly (no mutator), so the memo is recounted once; the Chest's Large are stored as
        // 1 x 1 footprints (an 8 x 2 Chest cannot hold two 2 x 4 Large and the Medium; weight reads only the kind).
        var rig = new InvEventRig();
        _ = AddKind(rig, rig.HotbarA, "large", 1, 0, [1], fw: 1, fh: 1);
        _ = AddKind(rig, rig.InventoryA, "letter", 0, 0, InvAutoPlaceRig.Ids(10, 20));
        _ = AddKind(rig, rig.InventoryA, "letter", 1, 0, [40]);
        EntryId medium = AddKind(rig, rig.CursorA, "medium", 0, 0, [2], fw: 1, fh: 1);
        _ = AddKind(rig, rig.Chest, "large", 0, 0, [3], fw: 1, fh: 1);
        _ = AddKind(rig, rig.Chest, "large", 1, 0, [4], fw: 1, fh: 1);
        Assert.Equal(ContainerHeader.NoOwner, rig.Header(rig.Chest).OwnerSlot);
        rig.Inventory.Mutator.RebuildWeight(_slot0);

        // Act + Assert (a): P = 8 + 1 + 1 + 3 = 13 -> 870
        Assert.Equal(13, rig.Inventory.WeightPoints(_slot0));
        Assert.Equal(870, rig.Inventory.SpeedPermille(_slot0));

        // Act (b): the Medium moves from the cursor into the Chest at (2, 0) through the mutator's Transfer (Remove + Place)
        Reply reply = InvEventRig.ReplyOf(rig.Run(rig.PlayerA, rig.Move(rig.CursorA, medium, rig.Chest, 2, 0)));

        // Assert (b): P = 10 -> 900; the Chest still counts for nobody (slot 1 reads 0)
        Assert.True(reply.IsOk);
        Assert.Equal(10, rig.Inventory.WeightPoints(_slot0));
        Assert.Equal(900, rig.Inventory.SpeedPermille(_slot0));
        Assert.Equal(0, rig.Inventory.WeightPoints(PlayerSlot.FromValue(1)));
    }

    // --- H-X-2 (weight clause) ----------------------------------------------------------------------------------------------

    [Theory]
    [Trait("Id", "H-X-2")]
    [InlineData(false)]
    [InlineData(true)]
    public void X2_RemovingA5LetterHotbarEntry_DropsPBy1_ByRemoveOrDetachForThrow_TheDetachedEntryDoesNotCount(bool detachForThrow)
    {
        // Arrange: a 5-letter entry at hotbar index 3 and a 1-letter entry in the inventory: P = 2
        var rig = new InvAutoPlaceRig();
        ushort five = rig.PlaceMail(rig.HotbarA, "letter", 3, 0, Address4, InvAutoPlaceRig.Ids(11, 5));
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, [30]);
        EntryId id = rig.Entries[five].Id;
        Assert.Equal(2, rig.Inventory.WeightPoints(_slot0));

        // Act
        if (detachForThrow)
        {
            DetachResult r = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.DetachForThrow(_slot0, id, 5));
            Assert.Equal(new DetachResult(default, id, 5), r);
        }
        else
        {
            Reply r = rig.Step(
                (ref InvPlan p) => p.Touch(rig.HotbarA) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => rig.Inventory.Mutator.Remove(ref p, five));
            Assert.True(r.IsOk);
        }

        // Assert: P = 1; the entry is live and detached; a recount agrees (the detached entry is not counted)
        Assert.True(rig.Entries[five].IsLive);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[five].ContainerSlot);
        Assert.Equal(1, rig.Inventory.WeightPoints(_slot0));
        rig.Inventory.Mutator.RebuildWeight(_slot0);
        Assert.Equal(1, rig.Inventory.WeightPoints(_slot0));
    }

    // --- Bounds -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void WeightPoints_AboveShortMax_SaturatesAndAssertsInventoryWeightPointsSaturated_SpeedStillReadsTheFloor()
    {
        // Arrange: a synthetic heavy knob of 10 000; four Large in the hotbar: P = 40 000 in the int accumulator
        var rig = new InvAutoPlaceRig(tune: s => s with { WeightPointsHeavy = 10_000 });
        for (int x = 1; x <= 4; x++)
        {
            _ = rig.PlaceMail(rig.HotbarA, "large", x, 0, Address4, [(uint)x]);
        }

        // Act + Assert: INVENTORY_ASSERT builds assert 79; Release returns short.MaxValue
        Assert.Equal(40_000, rig.Inventory.Mutator.Weight.RawPoints(0));
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => rig.Inventory.WeightPoints(_slot0));
            Assert.Equal(SimAssertCode.InventoryWeightPointsSaturated, ex.Code);
        }
        else
        {
            Assert.Equal(short.MaxValue, rig.Inventory.WeightPoints(_slot0));
        }

        // Assert: SpeedPermille reads the int accumulator, never asserts, and sits on the floor
        Assert.Equal(600, rig.Inventory.SpeedPermille(_slot0));
        Assert.Equal(79, SimAssertCode.InventoryWeightPointsSaturated);
    }

    [Fact]
    public void ABulkEntryInACountedContainer_AssertsInventoryWeightBulkCounted_OrContributes0InRelease()
    {
        // Arrange: M-1a content with the log made Bulk, built without the content validator (CV-GRD refuses it: R5 makes this
        // unreachable); a letter in the inventory (P = 1) and a detached log
        var rig = new InvAutoPlaceRig(content: BulkLogContent());
        _ = rig.PlaceMail(rig.InventoryA, "letter", 0, 0, Address4, [1]);
        ushort log = rig.DetachedItem("log", 1);
        Assert.Equal(WeightClass.Bulk, rig.Content.Items[rig.ItemIndex("log")].WeightClass);

        // Act + Assert: Place into the hotbar
        if (TestBuildExpectation.SimAssertsExpected)
        {
            // asserts before anything changes: the log stays detached, P stays 1
            var plan = new InvPlan();
            _ = plan.Touch(rig.HotbarA);
            SimAssertException ex = Assert.Throws<SimAssertException>(() => rig.Inventory.Mutator.Place(ref plan, log, rig.HotbarA, 1, 0, false));
            Assert.Equal(SimAssertCode.InventoryWeightBulkCounted, ex.Code);
            Assert.Equal(EntryRecord.NoContainer, rig.Entries[log].ContainerSlot);
            Assert.Equal(0, plan.MutatedCount);
            Assert.Equal(1, rig.Inventory.WeightPoints(_slot0));
        }
        else
        {
            // placed, contributing 0; removing it subtracts 0
            rig.PlaceDetached(log, rig.HotbarA, 1, 0);
            Assert.Equal(rig.HotbarA, rig.Entries[log].ContainerSlot);
            Assert.Equal(1, rig.Inventory.WeightPoints(_slot0));
            Reply r = rig.Step(
                (ref InvPlan p) => p.Touch(rig.HotbarA) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => rig.Inventory.Mutator.Remove(ref p, log));
            Assert.True(r.IsOk);
            Assert.Equal(1, rig.Inventory.WeightPoints(_slot0));
        }

        Assert.Equal(80, SimAssertCode.InventoryWeightBulkCounted);
    }

    [Fact]
    public void ABulkEntryInAContainerWithNoOwner_IsNotCounted_AndDoesNotAssert()
    {
        // Arrange: the Bulk log content; a no-owner grid
        var rig = new InvAutoPlaceRig(content: BulkLogContent());
        ushort chest = rig.CreateGrid(8, 2);

        // Act: placed through the mutator into a container no slot owns
        ushort log = rig.PlaceItem(chest, "log", 0, 0, 1);

        // Assert
        Assert.Equal(chest, rig.Entries[log].ContainerSlot);
        Assert.Equal(0, rig.Inventory.WeightPoints(_slot0));
    }

    // --- Helpers ------------------------------------------------------------------------------------------------------------

    /// <summary>Hotbar indices 1-4 Large (32), 5-6 Medium (6), 7 a letter (1): P = 39, all through <c>Place</c>.</summary>
    private static void FillHotbarTo39(InvAutoPlaceRig rig)
    {
        for (int x = 1; x <= 4; x++)
        {
            _ = rig.PlaceMail(rig.HotbarA, "large", x, 0, Address4, [(uint)x]);
        }

        _ = rig.PlaceMail(rig.HotbarA, "medium", 5, 0, Address4, [5]);
        _ = rig.PlaceMail(rig.HotbarA, "medium", 6, 0, Address4, [6]);
        _ = rig.PlaceMail(rig.HotbarA, "letter", 7, 0, Address4, [7]);
    }

    /// <summary>A synthetic Strong Back: a Perk source owned by <paramref name="slot"/> with <c>WeightPenaltyPm Mul pm</c>.</summary>
    private static bool PushStrongBack(SimHost host, PlayerSlot slot, int pm)
    {
        var src = new ModifierSource(SourceKind.Perk, DefIndex.FromValue(0), 0, ModifierOwner.ForSlot(slot), 1, ModifierLifetime.Run);
        var leg = new Modifier(src.Kind, src.Id, src.StackIndex, slot, Stat.WeightPenaltyPm, StatSheet.NoQualifier, ModifierOp.Mul, pm, src.Lifetime);
        return host.Services.Stats.Push(in src, [leg]);
    }

    /// <summary>
    /// Arranges a mail entry of <paramref name="kind"/> (its def's <c>w x h</c>) at (x, y) directly, as <see cref="InvEventRig.AddMail"/>
    /// does (no event, no version, no weight); the occupied footprint is <paramref name="fw"/> x <paramref name="fh"/> (default the def's).
    /// </summary>
    private static EntryId AddKind(InvEventRig rig, ContainerId container, string kind, byte x, byte y, uint[] ids, int fw = 0, int fh = 0)
    {
        DefIndex k = PlacementFixture.Index(rig.Host.Content.MailKinds, kind);
        MailKindDef def = rig.Host.Content.MailKinds[k];
        ushort cslot = rig.SlotOf(container);
        EntryPool pool = rig.Inventory.Entries;
        Assert.True(pool.TryAllocate(rig.Host.World.CurrentTick, out ushort slot, out EntryId id));
        ref EntryRecord e = ref pool[slot];
        e.IsMail = true;
        e.MailKind = k.Value;
        e.AddressId = Address4;
        e.W = (byte)def.GridW;
        e.H = (byte)def.GridH;
        e.X = x;
        e.Y = y;
        pool.SetCount(slot, (byte)ids.Length);
        Assert.True(rig.Inventory.MailIds.TryCreate(ids, 0, out e.MailIds));
        int w = fw == 0 ? def.GridW : fw;
        int h = fh == 0 ? def.GridH : fh;
        ref ContainerHeader header = ref rig.Inventory.Containers[cslot];
        rig.Inventory.Occupancy.Place(cslot, header.W, header.H, x, y, w, h, slot);
        e.ContainerSlot = cslot;
        header.EntryCount++;
        header.FreeCells -= (ushort)(w * h);
        return id;
    }

    /// <summary>The M-1a content with the <c>log</c> item made <see cref="WeightClass.Bulk"/>, assembled without validation.</summary>
    private static ContentSet BulkLogContent()
    {
        ContentSet m1a = BuiltInContent.CreateM1a();
        ImmutableArray<ItemDef> items = m1a.Items.AsSpan().ToArray()
            .Select(i => i.Id == "log" ? i with { WeightClass = WeightClass.Bulk } : i)
            .ToImmutableArray();
        return new ContentSet(
            m1a.Balance, m1a.ResolvedStatBounds, m1a.Containers, new DefTable<ItemDef>(items), m1a.MailKinds, m1a.DestinationTypes, m1a.MailMixes);
    }
}
