using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.Content;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/006 (TR-inventory-004, TR-inventory-006; GDD R3, R4, R5, R1 <c>reservedSlots</c> / <c>acceptsInsert</c>):
/// <see cref="StackKey"/>, <see cref="PlacementRules"/> and <see cref="InsertionWhitelist"/> in validate form — H-R3-1, H-R3-2,
/// H-R4-1 a–f, the rule-level precedence suffix and H-R5-1. Op form (versions, cursor, events) is the handlers' (stories
/// 007/008). Fixtures come from <see cref="BuiltInContent"/> through <see cref="PlacementFixture"/>; the only fixture defs are the
/// ones the ACs name (a <c>carryByHand: false</c> item, a Bulk mail kind, and <c>AcceptsInsert = false</c> container defs).
/// <c>MailFlags.Cursed</c> is used here only: Inventory compares the flags byte raw.
/// </summary>
public class PlacementRulesTests
{
    private const byte Cursed = (byte)MailFlags.Cursed;

    /// <summary>Fixture item: 2 x 2, <c>carryByHand: false</c> (dormant in v1 — no M-1a def has it). Ordinal 200 is outside the table; only its key is used.</summary>
    private static PlacementSubject NoHandsCrate(PlacementFixture f) => PlacementSubject.ForItem(
        f.Content.Items[f.ItemIndex("plank")] with { Id = "crate", GridW = 2, GridH = 2, CarryByHand = false },
        DefIndex.FromValue(200));

    /// <summary>Fixture mail kind: a Bulk letter (dormant in v1 — no Cargo).</summary>
    private static PlacementSubject BulkLetter(PlacementFixture f)
    {
        DefIndex letter = f.KindIndex("letter");
        return PlacementSubject.ForMail(
            f.Content.MailKinds[letter] with { Id = "bulk_letter", WeightClass = WeightClass.Bulk }, letter, 5u, 0, f.Sheet);
    }

    /// <summary>Fixture def: <paramref name="defId"/> with <c>AcceptsInsert = false</c> (GDD R1; none at M-1a).</summary>
    private static ContainerDef OutputOnly(PlacementFixture f, string defId) => f.ContainerDef(defId) with { AcceptsInsert = false };

    // ------------------------------------------------------------ StackKey (R3)

    [Fact]
    [Trait("Id", "H-R3-1")]
    public void StackKey_MailKeyIsKindAddressFlags_ItemKeyIsItemId_AndTheTwoNeverCollide()
    {
        // Arrange
        StackKey letter17 = StackKey.ForMail(1, 17u, 0);

        // Act + Assert
        Assert.Equal(letter17, StackKey.ForMail(1, 17u, 0));
        Assert.NotEqual(letter17, StackKey.ForMail(1, 17u, Cursed));
        Assert.NotEqual(letter17, StackKey.ForMail(1, 18u, 0));
        Assert.NotEqual(letter17, StackKey.ForMail(2, 17u, 0));
        Assert.NotEqual(StackKey.ForMail(0, 0u, 1), StackKey.ForItem(1));
        Assert.NotEqual(StackKey.ForMail(0xFFFF, uint.MaxValue, 0xFF), StackKey.ForItem(0xFFFF));
        Assert.Equal(StackKey.ForItem(3), StackKey.ForItem(3));
        Assert.True(StackKey.ForItem(0).IsItem);
        Assert.False(StackKey.ForMail(0xFFFF, uint.MaxValue, 0xFF).IsItem);
    }

    [Fact]
    [Trait("Id", "H-R3-1")]
    public void StackKey_Of_IgnoresArrivalSeqAndMailIds_SoCrossShiftLettersShareAKey()
    {
        // Arrange: shift lives in the mail table (mailIds), not in the entry key.
        var f = new PlacementFixture();
        ushort shift1 = f.AddMail(f.Chest, "letter", 2, 0, PlacementFixture.Address17, count: 5);
        ushort shift2 = f.AddMail(f.Chest, "letter", 3, 0, PlacementFixture.Address17, count: 3);
        f.Entries[shift1].ArrivalSeq = 1;
        f.Entries[shift1].MailIds = new MailIdsHandle(0, 0, 0, 5);
        f.Entries[shift2].ArrivalSeq = 9;
        f.Entries[shift2].MailIds = new MailIdsHandle(0, 1, 0, 3);

        // Act
        StackKey a = StackKey.Of(in f.Entries[shift1]);
        StackKey b = StackKey.Of(in f.Entries[shift2]);

        // Assert
        Assert.Equal(a, b);
        Assert.Equal(StackKey.ForMail(f.KindIndex("letter").Value, PlacementFixture.Address17, 0), a);
    }

    // ------------------------------------------------------------ AC-1: H-R3-1

    [Fact]
    [Trait("Id", "H-R3-1")]
    public void HR31_ThreeLettersTo17_OntoFiveLettersTo17_IsAMergeIntoA()
    {
        // Arrange
        var f = new PlacementFixture();
        ushort a = f.AddMail(f.Chest, "letter", 2, 0, PlacementFixture.Address17, count: 5);
        PlacementSubject b = f.Mail("letter", PlacementFixture.Address17);

        // Act
        PlacementVerdict v = f.Validate(f.Chest, in b, 2, 0);

        // Assert
        Assert.True(v.IsOk);
        Assert.True(v.IsMerge);
        Assert.Equal(a, v.TargetSlot);
        Assert.Equal(20, v.EffMax);
    }

    [Fact]
    [Trait("Id", "H-R3-1")]
    public void HR31_ThreeCursedLettersTo17_OntoFiveLettersTo17_Blocked()
    {
        // Arrange
        var f = new PlacementFixture();
        f.AddMail(f.Chest, "letter", 2, 0, PlacementFixture.Address17, count: 5);
        PlacementSubject cursed = f.Mail("letter", PlacementFixture.Address17, Cursed);

        // Act
        PlacementVerdict v = f.Validate(f.Chest, in cursed, 2, 0);

        // Assert
        Assert.Equal(InvReason.Blocked, v.Reason);
        Assert.False(v.IsMerge);
    }

    // ------------------------------------------------------------ AC-2: H-R3-2

    private static bool PushDoubleStack(PlacementFixture f)
    {
        var src = new ModifierSource(SourceKind.Perk, DefIndex.FromValue(0), 0, ModifierOwner.Team, 1, ModifierLifetime.Run);
        Modifier Leg(string kind) => new(
            src.Kind, src.Id, src.StackIndex, PlayerSlot.None, Stat.MailMaxStack, f.KindIndex(kind).Value, ModifierOp.Mul,
            GridMailRuleFixtures.DoubleStackPm, src.Lifetime);
        return f.Stats.Push(in src, [Leg("letter"), Leg("postcard")]);
    }

    [Fact]
    [Trait("Id", "H-R3-2")]
    public void HR32_TwentyLettersOntoTwenty_StackFull_ThenWithDoubleStackSourceEffMax40AndTheMergeFits()
    {
        // Arrange
        var f = new PlacementFixture();
        ushort a = f.AddMail(f.Chest, "letter", 2, 0, PlacementFixture.Address17, count: 20);

        // Act
        PlacementVerdict before = f.Validate(f.Chest, f.Mail("letter", PlacementFixture.Address17), 2, 0);
        bool pushed = PushDoubleStack(f);
        PlacementVerdict after = f.Validate(f.Chest, f.Mail("letter", PlacementFixture.Address17), 2, 0);

        // Assert
        Assert.Equal(InvReason.StackFull, before.Reason);
        Assert.Equal(a, before.TargetSlot);
        Assert.Equal(20, before.EffMax);
        Assert.True(pushed);
        Assert.Equal(40, f.Sheet.Read(Stat.MailMaxStack, f.KindIndex("letter").Value));
        Assert.Equal(80, f.Sheet.Read(Stat.MailMaxStack, f.KindIndex("postcard").Value));
        Assert.True(after.IsOk);
        Assert.True(after.IsMerge);
        Assert.Equal(a, after.TargetSlot);
        Assert.Equal(40, after.EffMax);
        Assert.True(f.Entries[a].Count + 20 <= after.EffMax);
    }

    [Fact]
    [Trait("Id", "H-R3-2")]
    public void HR32_SmallOntoSmall_UnderTheDoubleStackSource_BlockedBecauseEffMaxStays1()
    {
        // Arrange
        var f = new PlacementFixture();
        Assert.True(PushDoubleStack(f));
        f.AddMail(f.Chest, "small", 3, 0, PlacementFixture.Address17);
        PlacementSubject small = f.Mail("small", PlacementFixture.Address17);

        // Act
        PlacementVerdict v = f.Validate(f.Chest, in small, 3, 0);

        // Assert
        Assert.Equal(1, small.EffMax);
        Assert.Equal(InvReason.Blocked, v.Reason);
    }

    // ------------------------------------------------------------ AC-3: H-R4-1 (validate form)

    [Fact]
    [Trait("Id", "H-R4-1a")]
    public void HR41a_TwoLettersTo3_At00_MergeIntoTheFourLetterStack()
    {
        // Arrange
        var f = new PlacementFixture();

        // Act
        PlacementVerdict v = f.Validate(f.Chest, f.Mail("letter", PlacementFixture.Address3), 0, 0);

        // Assert
        Assert.True(v.IsOk);
        Assert.True(v.IsMerge);
        Assert.Equal(f.Letters, v.TargetSlot);
    }

    [Fact]
    [Trait("Id", "H-R4-1b")]
    public void HR41b_LargeAt70_OutOfBounds()
    {
        var f = new PlacementFixture();

        PlacementVerdict v = f.Validate(f.Chest, f.Mail("large", 5u), 7, 0);

        Assert.Equal(InvReason.OutOfBounds, v.Reason);
    }

    [Fact]
    [Trait("Id", "H-R4-1c")]
    public void HR41c_LargeAt50_OverlapsTheMedium_Blocked()
    {
        var f = new PlacementFixture();

        PlacementVerdict v = f.Validate(f.Chest, f.Mail("large", 5u), 5, 0);

        Assert.Equal(InvReason.Blocked, v.Reason);
    }

    [Fact]
    [Trait("Id", "H-R4-1d")]
    public void HR41d_LargeAt51_LeavesTheGridAndOverlaps_OutOfBoundsBeforeBlocked()
    {
        var f = new PlacementFixture();

        PlacementVerdict v = f.Validate(f.Chest, f.Mail("large", 5u), 5, 1);

        Assert.Equal(InvReason.OutOfBounds, v.Reason);
    }

    [Fact]
    [Trait("Id", "H-R4-1e")]
    public void HR41e_SmallRotatedAt00_OverlapsTheLetterStack_Blocked()
    {
        var f = new PlacementFixture();

        PlacementVerdict v = f.Validate(f.Chest, f.Mail("small", PlacementFixture.Address3), 0, 0, rotated: true);

        Assert.Equal(InvReason.Blocked, v.Reason);
    }

    [Fact]
    [Trait("Id", "H-R4-1f")]
    public void HR41f_LogsAt21_PartiallyOverlapTheMergeableLogStackAt20_Blocked()
    {
        // Arrange
        var f = new PlacementFixture();
        ushort logs = f.AddItem(f.Chest, "log", 2, 0, count: 3);
        PlacementSubject log = f.Item("log");

        // Act
        PlacementVerdict partial = f.Validate(f.Chest, in log, 2, 1);
        PlacementVerdict exact = f.Validate(f.Chest, in log, 2, 0);

        // Assert: the same stack is a merge target when matched exactly, so the rejection is the partial overlap.
        Assert.Equal(InvReason.Blocked, partial.Reason);
        Assert.True(exact.IsMerge);
        Assert.Equal(logs, exact.TargetSlot);
    }

    [Fact]
    [Trait("Id", "H-R4-1")]
    public void HR41_LetterOnAFreeCell_IsAFreePlacementNeverAMerge_AndTheRulesWriteNothing()
    {
        // Arrange
        var f = new PlacementFixture();
        ulong row0 = f.Occupancy.RowBits(f.Chest, 0);
        byte count = f.Entries[f.Letters].Count;

        // Act
        PlacementVerdict v = f.Validate(f.Chest, f.Mail("letter", PlacementFixture.Address3), 4, 0);

        // Assert
        Assert.True(v.IsOk);
        Assert.False(v.IsMerge);
        Assert.Equal(Occupancy.Free, v.TargetSlot);
        Assert.Equal(row0, f.Occupancy.RowBits(f.Chest, 0));
        Assert.Equal(count, f.Entries[f.Letters].Count);
    }

    [Fact]
    [Trait("Id", "H-R4-2a")]
    public void WholeMove_OntoItsOwnOldCells_TreatsThemAsFree_SplitDoesNot_AndAStackNeverMergesIntoItself()
    {
        // Arrange
        var f = new PlacementFixture();
        ushort large = f.AddMail(f.Chest, "large", 0, 1, 5u, rotated: true);   // 4 x 2: x 0–3, y 1–2
        PlacementSubject l = PlacementSubject.Of(f.Content, f.Sheet, in f.Entries[large]);
        PlacementSubject letter = PlacementSubject.Of(f.Content, f.Sheet, in f.Entries[f.Letters]);

        // Act
        PlacementVerdict shifted = f.Validate(f.Chest, in l, 1, 1, rotated: true, sourceSlot: large, wholeMove: true);
        PlacementVerdict notWhole = f.Validate(f.Chest, in l, 1, 1, rotated: true, sourceSlot: large);
        PlacementVerdict splitOntoSelf = f.Validate(f.Chest, in letter, 0, 0, sourceSlot: f.Letters);

        // Assert
        Assert.True(shifted.IsOk);
        Assert.False(shifted.IsMerge);
        Assert.Equal(InvReason.Blocked, notWhole.Reason);
        Assert.Equal(InvReason.Blocked, splitOntoSelf.Reason);
    }

    [Fact]
    [Trait("Id", "H-R4-1")]
    public void HR41_FootprintsFlushToTheRightAndBottomEdges_AreInBounds()
    {
        // Arrange: (7,0) and (5,3) are free single cells on the last column / row; rotated Large (4 x 2) at (4,0) covers
        // x 4–7, y 0–1 and at (2,2) covers x 2–5, y 2–3, clear of the letters (0,0) and the Medium (6–7, 2–3).
        var f = new PlacementFixture();
        PlacementSubject letter = f.Mail("letter", 5u);
        PlacementSubject large = f.Mail("large", 5u);

        // Act
        PlacementVerdict right = f.Validate(f.Chest, in letter, 7, 0);
        PlacementVerdict bottom = f.Validate(f.Chest, in letter, 5, 3);
        PlacementVerdict largeRight = f.Validate(f.Chest, in large, 4, 0, rotated: true);
        PlacementVerdict largeBottom = f.Validate(f.Chest, in large, 2, 2, rotated: true);

        // Assert
        Assert.True(right.IsOk);
        Assert.True(bottom.IsOk);
        Assert.True(largeRight.IsOk);
        Assert.True(largeBottom.IsOk);
        Assert.False(largeRight.IsMerge);
    }

    [Theory]
    [Trait("Id", "H-R4-1")]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(int.MinValue, 0)]
    [InlineData(int.MaxValue, 0)]
    [InlineData(0, int.MaxValue)]
    [InlineData(int.MaxValue - 1, int.MaxValue - 1)]
    public void HR41_NegativeOrHugePosition_OutOfBounds_WithoutOverflow(int x, int y)
    {
        // Arrange
        var f = new PlacementFixture();

        // Act
        PlacementVerdict letter = f.Validate(f.Chest, f.Mail("letter", 5u), x, y);
        PlacementVerdict large = f.Validate(f.Chest, f.Mail("large", 5u), x, y, rotated: true);

        // Assert
        Assert.Equal(InvReason.OutOfBounds, letter.Reason);
        Assert.Equal(InvReason.OutOfBounds, large.Reason);
    }

    [Fact]
    [Trait("Id", "TR-inventory-004")]
    public void HR42_SameKeyDifferentFootprintOrPosition_OverTheStack_Blocked_MatchingRotationMerges()
    {
        // Arrange: a rotated Log stack (2 x 1) covers (2,0),(3,0); (2,1) is free.
        var f = new PlacementFixture();
        ushort logs = f.AddItem(f.Chest, "log", 2, 0, count: 3, rotated: true);
        PlacementSubject log = f.Item("log");
        PlacementSubject sameKey1x1 = new(log.Key, 1, 1, log.Category, log.CarryByHand, log.WeightClass, log.EffMax);

        // Act
        PlacementVerdict unrotated = f.Validate(f.Chest, in log, 2, 0);              // covers (2,0),(2,1)
        PlacementVerdict smallerAtOrigin = f.Validate(f.Chest, in sameKey1x1, 2, 0); // inside the stack, footprint differs
        PlacementVerdict smallerOffset = f.Validate(f.Chest, in sameKey1x1, 3, 0);   // inside the stack, position differs
        PlacementVerdict rotated = f.Validate(f.Chest, in log, 2, 0, rotated: true);

        // Assert
        Assert.Equal(InvReason.Blocked, unrotated.Reason);
        Assert.Equal(InvReason.Blocked, smallerAtOrigin.Reason);
        Assert.Equal(InvReason.Blocked, smallerOffset.Reason);
        Assert.True(rotated.IsOk);
        Assert.True(rotated.IsMerge);
        Assert.Equal(logs, rotated.TargetSlot);
    }

    [Fact]
    [Trait("Id", "TR-inventory-004")]
    public void HR42_LogOverTwoLetterStacksStackedVertically_Blocked()
    {
        // Arrange: two 1 x 1 letter stacks at (4,0) and (4,1); a Log (1 x 2) at (4,0) covers both.
        var f = new PlacementFixture();
        f.AddMail(f.Chest, "letter", 4, 0, PlacementFixture.Address17, count: 2);
        f.AddMail(f.Chest, "letter", 4, 1, PlacementFixture.Address17, count: 2);

        // Act
        PlacementVerdict v = f.Validate(f.Chest, f.Item("log"), 4, 0);

        // Assert
        Assert.Equal(InvReason.Blocked, v.Reason);
        Assert.Equal(Occupancy.Free, v.TargetSlot);
    }

    [Fact]
    [Trait("Id", "H-R3-2")]
    public void HR32_TargetCountAboveEffMax_StackFull()
    {
        // Arrange: 30 letters left over from a lapsed DoubleStack source; effMax is back to 20.
        var f = new PlacementFixture();
        ushort a = f.AddMail(f.Chest, "letter", 2, 0, PlacementFixture.Address17, count: 30);

        // Act
        PlacementVerdict v = f.Validate(f.Chest, f.Mail("letter", PlacementFixture.Address17), 2, 0);

        // Assert
        Assert.Equal(InvReason.StackFull, v.Reason);
        Assert.Equal(a, v.TargetSlot);
        Assert.Equal(20, v.EffMax);
    }

    [Fact]
    [Trait("Id", "TR-inventory-004")]
    public void SlotsLayout_LetterOntoSlotWithThreeLetters_Merges_OntoSlotWithTwenty_StackFull()
    {
        // Arrange
        var f = new PlacementFixture();
        ushort hotbar = f.CreateContainer("hotbar");
        ushort three = f.AddMail(hotbar, "letter", 1, 0, PlacementFixture.Address3, count: 3);
        ushort twenty = f.AddMail(hotbar, "letter", 2, 0, PlacementFixture.Address3, count: 20);
        PlacementSubject letter = f.Mail("letter", PlacementFixture.Address3);

        // Act
        PlacementVerdict slot1 = f.Validate(hotbar, in letter, 1, 0);
        PlacementVerdict slot2 = f.Validate(hotbar, in letter, 2, 0);

        // Assert
        Assert.True(slot1.IsOk);
        Assert.True(slot1.IsMerge);
        Assert.Equal(three, slot1.TargetSlot);
        Assert.Equal(InvReason.StackFull, slot2.Reason);
        Assert.Equal(twenty, slot2.TargetSlot);
    }

    [Fact]
    [Trait("Id", "H-R1-2")]
    public void HR12_LetterOrAxeIntoEmptyReservedHotbarSlot0_ReservedSlot_Slot1Ok()
    {
        // Arrange
        var f = new PlacementFixture();
        ushort hotbar = f.CreateContainer("hotbar");

        // Act
        PlacementVerdict letter = f.Validate(hotbar, f.Mail("letter", PlacementFixture.Address3), 0, 0);
        PlacementVerdict axe = f.Validate(hotbar, f.Item("axe"), 0, 0);
        PlacementVerdict axeSlot1 = f.Validate(hotbar, f.Item("axe"), 1, 0);

        // Assert
        Assert.Equal(InvReason.ReservedSlot, letter.Reason);
        Assert.Equal(0, letter.EffMax);
        Assert.Equal(InvReason.ReservedSlot, axe.Reason);
        Assert.True(axeSlot1.IsOk);
    }

    // ------------------------------------------------------------ Edge rows (PR #15 advisory A4; GDD R2–R4)
    // Rows (2) non-full merge and (3) whole move vs split are HR31_ThreeLettersTo17_OntoFiveLettersTo17_IsAMergeIntoA and
    // WholeMove_OntoItsOwnOldCells_TreatsThemAsFree_SplitDoesNot_AndAStackNeverMergesIntoItself.

    [Fact]
    [Trait("Id", "H-R3-2")]
    public void EdgeRow1_MailMaxStackMulsPast255_EffMaxClampsTo255_A255StackIsFullAndA254StackMerges()
    {
        // Arrange: letter maxStack 20 x two team Muls of 5 000 permille each (in range: StatResolve.MaxMulPermille is
        // 10 000) = 500 before the StatDefs clamp 1..255 (entry count is uint8).
        var f = new PlacementFixture();
        var srcA = new ModifierSource(SourceKind.Perk, DefIndex.FromValue(1), 0, ModifierOwner.Team, 1, ModifierLifetime.Run);
        var srcB = new ModifierSource(SourceKind.Perk, DefIndex.FromValue(2), 0, ModifierOwner.Team, 1, ModifierLifetime.Run);
        Assert.True(f.Stats.Push(in srcA, [LetterMul(f, in srcA, 5_000)]));
        Assert.True(f.Stats.Push(in srcB, [LetterMul(f, in srcB, 5_000)]));
        ushort full = f.AddMail(f.Chest, "letter", 2, 0, PlacementFixture.Address17, count: 255);
        ushort room = f.AddMail(f.Chest, "letter", 3, 0, PlacementFixture.Address17, count: 254);

        // Act
        PlacementSubject letter = f.Mail("letter", PlacementFixture.Address17);
        PlacementVerdict ontoFull = f.Validate(f.Chest, in letter, 2, 0);
        PlacementVerdict ontoRoom = f.Validate(f.Chest, in letter, 3, 0);

        // Assert
        Assert.Equal(255, letter.EffMax);
        Assert.Equal(InvReason.StackFull, ontoFull.Reason);
        Assert.Equal(full, ontoFull.TargetSlot);
        Assert.Equal(255, ontoFull.EffMax);
        Assert.True(ontoRoom.IsOk);
        Assert.True(ontoRoom.IsMerge);
        Assert.Equal(room, ontoRoom.TargetSlot);
    }

    private static Modifier LetterMul(PlacementFixture f, in ModifierSource src, int permille) => new(
        src.Kind, src.Id, src.StackIndex, PlayerSlot.None, Stat.MailMaxStack, f.KindIndex("letter").Value, ModifierOp.Mul,
        permille, src.Lifetime);

    [Fact]
    [Trait("Id", "TR-inventory-004")]
    public void EdgeRow4_ItemStackKey_PlankOntoPlankMerges_PlankOntoLogOrFiberBlocked()
    {
        // Arrange: plank and fiber share a 1 x 1 footprint, category and maxStack, so only the itemId key differs.
        var f = new PlacementFixture();
        ushort planks = f.AddItem(f.Chest, "plank", 2, 0, count: 5);
        f.AddItem(f.Chest, "log", 3, 0, count: 3);   // 1 x 2: (3,0) and (3,1)
        f.AddItem(f.Chest, "fiber", 4, 0, count: 5);
        PlacementSubject plank = f.Item("plank");

        // Act
        PlacementVerdict ontoPlank = f.Validate(f.Chest, in plank, 2, 0);
        PlacementVerdict ontoLog = f.Validate(f.Chest, in plank, 3, 0);
        PlacementVerdict ontoFiber = f.Validate(f.Chest, in plank, 4, 0);

        // Assert
        Assert.True(ontoPlank.IsOk);
        Assert.True(ontoPlank.IsMerge);
        Assert.Equal(planks, ontoPlank.TargetSlot);
        Assert.Equal(InvReason.Blocked, ontoLog.Reason);
        Assert.Equal(InvReason.Blocked, ontoFiber.Reason);
        Assert.False(ontoFiber.IsMerge);
    }

    // ------------------------------------------------------------ AC-4: rule-level precedence suffix

    /// <summary>
    /// For each rule-level step, a placement that violates that step and every later one; the earliest code must win.
    /// <c>reserved_slot</c> exists only on a slots layout, where the footprint is one slot, so a reserved (in-range) slot cannot
    /// also be <c>out_of_bounds</c>: that case violates every later step that can co-occur (<c>not_carryable</c>, <c>not_allowed</c>,
    /// <c>blocked</c>). <c>blocked</c> and <c>stack_full</c> share the last step and exclude each other (one overlapped stack).
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-006")]
    [InlineData(InvReason.ReservedSlot)]
    [InlineData(InvReason.OutOfBounds)]
    [InlineData(InvReason.NotCarryable)]
    [InlineData(InvReason.NotAllowed)]
    [InlineData(InvReason.Blocked)]
    [InlineData(InvReason.StackFull)]
    public void Precedence_PlacementViolatingThisStepAndEveryLaterOne_ReturnsThisStep(InvReason step)
    {
        // Arrange
        var f = new PlacementFixture();
        ContainerDef chestOutputOnly = OutputOnly(f, "player_inventory");   // HandCarried, AcceptsInsert = false
        PlacementVerdict v;

        // Act
        switch (step)
        {
            case InvReason.ReservedSlot:
            {
                ushort hotbar = f.CreateContainer("hotbar");
                f.AddItem(hotbar, "plank", 0, 0);   // slot 0 occupied by a non-mergeable entry -> blocked
                v = f.Validate(hotbar, OutputOnly(f, "hotbar"), NoHandsCrate(f), 0, 0);   // not_carryable, not_allowed
                break;
            }

            case InvReason.OutOfBounds:
                // Crate (2 x 2) at (7,2): x 7–8 leaves the grid and (7,2),(7,3) are the Medium's.
                v = f.Validate(f.Chest, chestOutputOnly, NoHandsCrate(f), 7, 2);
                break;
            case InvReason.NotCarryable:
                // Crate exactly on the Medium's cells (blocked: different key), into an output-only hand-carried def.
                v = f.Validate(f.Chest, chestOutputOnly, NoHandsCrate(f), 6, 2);
                break;
            case InvReason.NotAllowed:
                // A carryable plank on the letter stack (blocked), into an output-only def.
                v = f.Validate(f.Chest, chestOutputOnly, f.Item("plank"), 0, 0);
                break;
            case InvReason.Blocked:
                v = f.Validate(f.Chest, f.Item("plank"), 0, 0);
                break;
            default:
            {
                f.AddMail(f.Chest, "letter", 1, 0, 5u, count: 20);
                v = f.Validate(f.Chest, f.Mail("letter", 5u), 1, 0);
                break;
            }
        }

        // Assert
        Assert.Equal(step, v.Reason);
    }

    [Fact]
    [Trait("Id", "TR-inventory-006")]
    public void Precedence_ReservedSlotCase_WithTheReservationLifted_IsBlockedByTheEntryInSlot0()
    {
        // Arrange: the control for the theory's reserved_slot case — slot 0 really does violate blocked.
        var f = new PlacementFixture();
        ushort hotbar = f.CreateContainer("hotbar");
        f.AddItem(hotbar, "plank", 0, 0);
        ContainerDef unreserved = f.ContainerDef("hotbar") with { ReservedSlots = [] };

        // Act
        PlacementVerdict v = f.Validate(hotbar, unreserved, f.Item("axe"), 0, 0);

        // Assert
        Assert.Equal(InvReason.Blocked, v.Reason);
    }

    [Fact]
    [Trait("Id", "TR-inventory-006")]
    public void Precedence_FullMergeableStackIntoAcceptsInsertFalseDef_NotAllowedBeforeStackFull()
    {
        // Arrange
        var f = new PlacementFixture();
        f.AddMail(f.Chest, "letter", 1, 0, 5u, count: 20);
        ContainerDef outputOnly = OutputOnly(f, "player_inventory");
        PlacementSubject letter = f.Mail("letter", 5u);

        // Act
        PlacementVerdict v = f.Validate(f.Chest, outputOnly, in letter, 1, 0);
        PlacementVerdict control = f.Validate(f.Chest, in letter, 1, 0);

        // Assert
        Assert.Equal(InvReason.NotAllowed, v.Reason);
        Assert.Equal(0, v.EffMax);
        Assert.Equal(InvReason.StackFull, control.Reason);
    }

    // ------------------------------------------------------------ AC-5: H-R5-1 (validate form)

    [Fact]
    [Trait("Id", "H-R5-1")]
    public void HR51_PlankIntoPoIntake_NotAllowed_LetterIntoPoIntakePasses_PlankIntoPlayerInventoryPasses()
    {
        // Arrange
        var f = new PlacementFixture();
        ushort intake = f.CreateContainer("po_intake");
        ushort inventory = f.CreateContainer("player_inventory");

        // Act
        PlacementVerdict plankIntake = f.Validate(intake, f.Item("plank"), 0, 0);
        PlacementVerdict letterIntake = f.Validate(intake, f.Mail("letter", 3u), 0, 0);
        PlacementVerdict plankInventory = f.Validate(inventory, f.Item("plank"), 0, 0);

        // Assert
        Assert.Equal(InvReason.NotAllowed, plankIntake.Reason);
        Assert.True(letterIntake.IsOk);
        Assert.True(plankInventory.IsOk);
        Assert.Equal(InvReason.NotAllowed, InsertionWhitelist.Check(f.ContainerDef("po_intake"), f.Item("plank")));
        Assert.Equal((InvReason)0, InsertionWhitelist.Check(f.ContainerDef("po_intake"), f.Mail("letter", 3u)));
    }

    [Theory]
    [Trait("Id", "H-R5-1")]
    [InlineData("hotbar", 1)]
    [InlineData("player_inventory", 0)]
    [InlineData("cursor", 0)]
    public void HR51_HandCarriedContainer_RejectsCarryByHandFalseAndBulk_NotCarryable(string defId, int x)
    {
        // Arrange
        var f = new PlacementFixture();
        ushort container = f.CreateContainer(defId);

        // Act
        PlacementVerdict noHands = f.Validate(container, NoHandsCrate(f), x, 0);
        PlacementVerdict bulk = f.Validate(container, BulkLetter(f), x, 0);
        PlacementVerdict letter = f.Validate(container, f.Mail("letter", 3u), x, 0);

        // Assert
        Assert.True(f.ContainerDef(defId).HandCarried);
        Assert.Equal(InvReason.NotCarryable, noHands.Reason);
        Assert.Equal(InvReason.NotCarryable, bulk.Reason);
        Assert.True(letter.IsOk);
    }

    [Fact]
    [Trait("Id", "H-R5-1")]
    public void HR51_ContainerNotHandCarried_AdmitsBulkAndCarryByHandFalse()
    {
        var f = new PlacementFixture();
        ushort intake = f.CreateContainer("po_intake");

        PlacementVerdict bulk = f.Validate(intake, BulkLetter(f), 0, 0);

        Assert.True(bulk.IsOk);
        Assert.Equal((InvReason)0, InsertionWhitelist.Check(f.ContainerDef("po_intake"), ItemCategory.Mail, false, WeightClass.Bulk));
    }

    [Theory]
    [Trait("Id", "H-R5-1")]
    [InlineData("player_inventory")]
    [InlineData("po_intake")]
    public void HR51_AcceptsInsertFalse_RejectsEveryInsertion_NotAllowed(string defId)
    {
        // Arrange
        var f = new PlacementFixture();
        ushort container = f.CreateContainer(defId);
        ContainerDef outputOnly = OutputOnly(f, defId);

        // Act
        PlacementVerdict letter = f.Validate(container, outputOnly, f.Mail("letter", 3u), 0, 0);
        PlacementVerdict plank = f.Validate(container, outputOnly, f.Item("plank"), 0, 0);

        // Assert
        Assert.Equal(InvReason.NotAllowed, letter.Reason);
        Assert.Equal(InvReason.NotAllowed, plank.Reason);
        Assert.True(f.Validate(container, f.Mail("letter", 3u), 0, 0).IsOk);
    }

    [Fact]
    [Trait("Id", "H-R5-1")]
    public void HR51_MergeableNeedsTheCategoryAdmitted_CanMergeIsKeyAndEffMaxAndCategory()
    {
        // Arrange
        var f = new PlacementFixture();
        StackKey plank = StackKey.ForItem(f.ItemIndex("plank").Value);
        ContainerDef intake = f.ContainerDef("po_intake");
        ContainerDef inventory = f.ContainerDef("player_inventory");

        // Act + Assert
        Assert.True(PlacementRules.CanMerge(plank, plank, 20, inventory, ItemCategory.Material));
        Assert.False(PlacementRules.CanMerge(plank, plank, 20, intake, ItemCategory.Material));
        Assert.False(PlacementRules.CanMerge(plank, plank, 1, inventory, ItemCategory.Material));
        Assert.False(PlacementRules.CanMerge(plank, StackKey.ForItem(0), 20, inventory, ItemCategory.Material));
    }
}
