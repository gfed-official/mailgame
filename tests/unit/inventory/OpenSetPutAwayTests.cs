using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Sim.Tests.Unit.Inventory.InvOpTestHelpers;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/011 AC 5 (the leash put-away hook; GDD R10, the put-away clause of H-R10-1, R9's cursor row, R11):
/// <see cref="TargetOrder"/> and <c>CloseExternal(slot, CloseCause.Leash)</c>. Player A (slot 0) of the story 009 rig has the Intake (the
/// M-1a external) open. State is arranged through real paths: letters by <see cref="IInventoryApi.Spawn"/> (and onto the cursor by a
/// "pick": one fixture-op bracket whose commit is <see cref="InventoryMutator.Transfer"/>, as story 007's <c>pick</c> will commit), the
/// arena exhausted by Intake spawns, items and the plank fillers by <see cref="InventoryMutator.Place"/> in a fixture op (no item spawn
/// API exists at M-1a), and the external by <see cref="InventorySystem.TryOpenExternal"/>. The leash
/// runs through three caller paths: inside a tick phase (<c>MailStep</c>, as story 019's leash or an <see cref="IInventoryApi"/> caller),
/// inside an op handler (the fixture op's commit), and between ticks (the tick guard). The panel is the 7 usable hotbar slots (slot 0 is
/// reserved) and the 8 x 2 player_inventory.
/// </summary>
public class OpenSetPutAwayTests
{
    private const uint Address4 = 4;

    private static readonly PlayerSlot _slotA = PlayerSlot.FromValue(0);

    /// <summary>Where <c>CloseExternal(A, Leash)</c> is called from.</summary>
    public enum Caller
    {
        /// <summary>Inside the <c>MailStep</c> phase (no op).</summary>
        Phase,

        /// <summary>Inside an op handler (the fixture op's commit step).</summary>
        Op,
    }

    // ------------------------------------------------------------------ TargetOrder: the cursor row

    [Theory]
    [InlineData(ItemCategory.Tool, true)]
    [InlineData(ItemCategory.Weapon, true)]
    [InlineData(ItemCategory.Consumable, true)]
    [InlineData(ItemCategory.Material, false)]
    [InlineData(ItemCategory.Ammo, false)]
    [InlineData(ItemCategory.Blueprint, false)]
    [InlineData(ItemCategory.Mail, false)]
    [Trait("Id", "R9")]
    public void CursorRow_ToolWeaponConsumable_AreHotbarFirst_EveryOtherCategoryIncludingMail_IsPlayerInventoryFirst(ItemCategory category, bool hotbarFirst)
    {
        // Arrange
        const ushort Hotbar = 11;
        const ushort PlayerInventory = 22;
        Span<ushort> targets = stackalloc ushort[TargetOrder.CursorRowLength + 1];
        targets[2] = 99;

        // Act
        TargetOrder.CursorRow(category, Hotbar, PlayerInventory, targets);

        // Assert: two targets, no backpack, the third element untouched
        Assert.Equal(2, TargetOrder.CursorRowLength);
        Assert.Equal(hotbarFirst, TargetOrder.IsHotbarFirst(category));
        Assert.Equal(hotbarFirst ? Hotbar : PlayerInventory, targets[0]);
        Assert.Equal(hotbarFirst ? PlayerInventory : Hotbar, targets[1]);
        Assert.Equal(99, targets[2]);
    }

    // ------------------------------------------------------------------ AC 5: first target, fallback, full panel

    [Fact]
    [Trait("Id", "H-R10-1")]
    public void Leash_ToolOnTheCursor_GoesToTheHotbarFirst_InOneBracketSealedWithAsPlayer_AndTheExternalCloses()
    {
        // Arrange: an empty panel; the axe on the cursor; A has the Intake open
        var rig = new InvAutoPlaceRig();
        ushort axe = rig.PlaceItem(Cursor(rig), "axe", 0, 0, 1);
        Open(rig);
        uint[] before = Versions(rig);

        // Act
        EventEntry[] records = Leash(rig, Caller.Phase);

        // Assert: the axe in the lowest free hotbar slot >= 1; the cursor empty; the external closed
        Assert.Equal(rig.HotbarA, rig.Entries[axe].ContainerSlot);
        Assert.Equal((1, 0), ((int)rig.Entries[axe].X, (int)rig.Entries[axe].Y));
        Assert.Equal(0, rig.Header(Cursor(rig)).EntryCount);
        AssertClosed(rig);

        // one bracket: Removed{cursor}, Inserted{hotbar}, then one seal each (cursor first), actor A, cause None; player_inventory untouched
        Assert.Equal(new[] { InvEventKinds.Removed, InvEventKinds.Inserted, InvEventKinds.Version, InvEventKinds.Version }, records.Select(e => e.Kind));
        AssertOneBracket(rig, records, before, Cursor(rig), rig.HotbarA);
    }

    [Fact]
    [Trait("Id", "H-R10-1")]
    public void Leash_LetterOnTheCursor_GoesToThePlayerInventoryFirst_InOneBracketSealedWithAsPlayer_AndTheExternalCloses()
    {
        // Arrange: a letter spawned into player_inventory and picked onto the cursor (the panel is empty again); A has the Intake open
        var rig = new InvAutoPlaceRig();
        ushort letter = LetterOnCursor(rig, 1);
        Open(rig);
        uint[] before = Versions(rig);

        // Act
        EventEntry[] records = Leash(rig, Caller.Phase);

        // Assert: the letter at player_inventory (0, 0); the hotbar untouched
        Assert.Equal(rig.InventoryA, rig.Entries[letter].ContainerSlot);
        Assert.Equal((0, 0), ((int)rig.Entries[letter].X, (int)rig.Entries[letter].Y));
        Assert.Equal(new uint[] { 1 }, rig.IdsOf(letter));
        Assert.Equal(0, rig.Header(Cursor(rig)).EntryCount);
        AssertClosed(rig);
        AssertOneBracket(rig, records, before, Cursor(rig), rig.InventoryA);
    }

    [Fact]
    [Trait("Id", "H-R10-1")]
    public void Leash_Tool_HotbarWithExactlyOneFreeSlot_TakesThatSlot()
    {
        // Arrange: hotbar slots 1-6 hold planks, slot 7 is the one free slot
        var rig = new InvAutoPlaceRig();
        FillHotbar(rig, freeSlots: 1);
        ushort axe = rig.PlaceItem(Cursor(rig), "axe", 0, 0, 1);
        Open(rig);
        uint[] before = Versions(rig);

        // Act
        EventEntry[] records = Leash(rig, Caller.Phase);

        // Assert
        Assert.Equal(rig.HotbarA, rig.Entries[axe].ContainerSlot);
        Assert.Equal(7, rig.Entries[axe].X);
        Assert.Equal(1, rig.Header(rig.HotbarA).FreeCells); // only reserved slot 0 is left
        AssertClosed(rig);
        AssertOneBracket(rig, records, before, Cursor(rig), rig.HotbarA);
    }

    [Fact]
    [Trait("Id", "H-R10-1")]
    public void Leash_Tool_HotbarWithZeroFreeSlots_FallsBackToThePlayerInventory()
    {
        // Arrange: all 7 usable hotbar slots hold planks
        var rig = new InvAutoPlaceRig();
        FillHotbar(rig, freeSlots: 0);
        ushort axe = rig.PlaceItem(Cursor(rig), "axe", 0, 0, 1);
        Open(rig);
        uint[] before = Versions(rig);

        // Act
        EventEntry[] records = Leash(rig, Caller.Phase);

        // Assert: the 1 x 2 axe at player_inventory (0, 0); the hotbar unchanged
        Assert.Equal(rig.InventoryA, rig.Entries[axe].ContainerSlot);
        Assert.Equal((0, 0), ((int)rig.Entries[axe].X, (int)rig.Entries[axe].Y));
        AssertClosed(rig);
        AssertOneBracket(rig, records, before, Cursor(rig), rig.InventoryA);
    }

    [Fact]
    [Trait("Id", "H-R10-1")]
    public void Leash_Letter_PlayerInventoryWithExactlyOneFreeCell_TakesThatCell()
    {
        // Arrange: a letter on the cursor; then player_inventory cells 0-14 hold planks, (7, 1) is the one free cell
        var rig = new InvAutoPlaceRig();
        ushort letter = LetterOnCursor(rig, 1);
        FillPlayerInventory(rig, freeCells: 1);
        Open(rig);
        uint[] before = Versions(rig);

        // Act
        EventEntry[] records = Leash(rig, Caller.Phase);

        // Assert
        Assert.Equal(rig.InventoryA, rig.Entries[letter].ContainerSlot);
        Assert.Equal((7, 1), ((int)rig.Entries[letter].X, (int)rig.Entries[letter].Y));
        Assert.Equal(0, rig.Header(rig.InventoryA).FreeCells);
        AssertClosed(rig);
        AssertOneBracket(rig, records, before, Cursor(rig), rig.InventoryA);
    }

    [Fact]
    [Trait("Id", "H-R10-1")]
    public void Leash_Letter_PlayerInventoryWithZeroFreeCells_FallsBackToTheHotbar()
    {
        // Arrange: a letter on the cursor; then all 16 player_inventory cells hold planks
        var rig = new InvAutoPlaceRig();
        ushort letter = LetterOnCursor(rig, 1);
        FillPlayerInventory(rig, freeCells: 0);
        Open(rig);
        uint[] before = Versions(rig);

        // Act
        EventEntry[] records = Leash(rig, Caller.Phase);

        // Assert: hotbar slot 1 (never slot 0); player_inventory unchanged
        Assert.Equal(rig.HotbarA, rig.Entries[letter].ContainerSlot);
        Assert.Equal(1, rig.Entries[letter].X);
        AssertClosed(rig);
        AssertOneBracket(rig, records, before, Cursor(rig), rig.HotbarA);
    }

    [Fact]
    [Trait("Id", "H-R10-1")]
    public void Leash_Letter_MergePassRunsOverBothTargetsFirst_AMatchingHotbarStackTakesItBeforeAFreeInventoryCell()
    {
        // Arrange: a 3-letter #4 stack at hotbar slot 1 (spawned, merge policy); an empty player_inventory; 2 letters to #4 on the cursor
        var rig = new InvAutoPlaceRig();
        ContainerId hotbar = rig.Inventory.HotbarOf(_slotA);
        Assert.Equal(3, Spawn(rig, hotbar, MergePolicy.Merge, 10, 11, 12));
        ushort stack = rig.EntryAt(rig.HotbarA, 1, 0);
        Assert.Equal(3, rig.Entries[stack].Count);
        ushort letters = LetterOnCursor(rig, 1, 2);
        Open(rig);
        uint[] before = Versions(rig);

        // Act
        EventEntry[] records = Leash(rig, Caller.Phase);

        // Assert: PASS M (pass-first) merged both units into the hotbar stack; the cursor entry is gone; player_inventory untouched
        Assert.Equal(5, rig.Entries[stack].Count);
        Assert.Equal(new uint[] { 1, 2, 10, 11, 12 }, rig.IdsOf(stack).Order());
        Assert.False(rig.Entries[letters].IsLive);
        Assert.Equal(0, rig.Header(Cursor(rig)).EntryCount);
        Assert.Equal(0, rig.Header(rig.InventoryA).EntryCount);
        AssertClosed(rig);
        AssertOneBracket(rig, records, before, Cursor(rig), rig.HotbarA);
    }

    [Fact]
    [Trait("Id", "H-R10-1")]
    public void Leash_Letters_FullPanelButOneStackWithRoomForTwo_MergesTheTwoOldest_AndTheNewestStayOnTheCursor()
    {
        // Arrange: 5 letters to #4 on the cursor; then the hotbar full of planks, and player_inventory holding planks in 15 cells and an
        // 18-letter #4 stack in (7, 1)
        var rig = new InvAutoPlaceRig();
        ushort letters = LetterOnCursor(rig, 1, 2, 3, 4, 5);
        FillHotbar(rig, freeSlots: 0);
        FillPlayerInventory(rig, freeCells: 1);
        Assert.Equal(18, Spawn(rig, rig.Inventory.PlayerInventoryOf(_slotA), MergePolicy.Merge, InvAutoPlaceRig.Ids(100, 18)));
        ushort stack = rig.EntryAt(rig.InventoryA, 7, 1);
        Assert.Equal(18, rig.Entries[stack].Count);
        Open(rig);
        uint[] before = Versions(rig);

        // Act
        EventEntry[] records = Leash(rig, Caller.Phase);

        // Assert: the stack reaches 20 with the two oldest ids; the 3 newest stay on the cursor (same entry); one bracket over both
        Assert.Equal(20, rig.Entries[stack].Count);
        Assert.Equal(new uint[] { 1, 2 }.Concat(InvAutoPlaceRig.Ids(100, 18)), rig.IdsOf(stack).Order());
        Assert.Equal(Cursor(rig), rig.Entries[letters].ContainerSlot);
        Assert.Equal(new uint[] { 3, 4, 5 }, rig.IdsOf(letters));
        AssertClosed(rig);
        AssertOneBracket(rig, records, before, Cursor(rig), rig.InventoryA);
    }

    // ------------------------------------------------------------------ AC 5: refusals, through both in-tick caller paths

    [Theory]
    [InlineData(Caller.Phase, "axe")]
    [InlineData(Caller.Op, "axe")]
    [InlineData(Caller.Phase, "letter")]
    [InlineData(Caller.Op, "letter")]
    [Trait("Id", "H-R10-1")]
    public void Leash_FullPanel_KeepsTheEntryOnTheCursor_WithNoRecordAndNoVersionBump_AndTheExternalStillCloses(Caller caller, string subject)
    {
        // Arrange: 7 hotbar slots and 16 player_inventory cells hold planks (zero free cells)
        var rig = new InvAutoPlaceRig();
        ushort entry = subject == "axe" ? rig.PlaceItem(Cursor(rig), "axe", 0, 0, 1) : LetterOnCursor(rig, 1);
        FillHotbar(rig, freeSlots: 0);
        FillPlayerInventory(rig, freeCells: 0);
        Open(rig);
        List<long> before = Storage(rig);
        long outOfPhase = rig.Counters[CounterId.MutatingApiOutOfPhase];

        // Act
        EventEntry[] records = Leash(rig, caller);

        // Assert: no_room — nothing moved, nothing raised, no version bumped; the external closed regardless
        Assert.Empty(records);
        Assert.Equal(Cursor(rig), rig.Entries[entry].ContainerSlot);
        Assert.Equal(before, Storage(rig));
        Assert.Equal(outOfPhase, rig.Counters[CounterId.MutatingApiOutOfPhase]);
        AssertClosed(rig);
    }

    [Theory]
    [InlineData(Caller.Phase)]
    [InlineData(Caller.Op)]
    [Trait("Id", "H-R10-1")]
    public void Leash_LetterWhoseMergeUnionBlockTheArenaCannotReserve_KeepsTheEntryOnTheCursor_WithNoRecord_AndTheExternalStillCloses(Caller caller)
    {
        // Arrange: a minimum-size arena; a #4 letter on the cursor and a 2-letter #4 stack in player_inventory (spawned, merge policy),
        // then letters spawned into the Intake until Spawn is refused at its arena reservation: PASS M plans the merge (its 3-unit union
        // block), which cannot be reserved (capacity_exhausted), although player_inventory has 15 free cells
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits);
        ushort letter = LetterOnCursor(rig, 1);
        Assert.Equal(2, Spawn(rig, rig.Inventory.PlayerInventoryOf(_slotA), MergePolicy.Merge, 10, 11));
        ushort stack = rig.EntryAt(rig.InventoryA, 0, 0);
        ExhaustArenaThroughIntakeSpawns(rig);
        Assert.False(rig.Inventory.MailIds.CanReserve(3));
        Open(rig);
        List<long> before = Storage(rig);
        SimCheckRing ring = rig.Host.World.CheckRing;
        long ringBefore = RingWrites(ring);

        // Act
        EventEntry[] records = Leash(rig, caller);

        // Assert: refused at the reservation step (a dry run), so no acquire was attempted and the arena wrote no check record
        Assert.Equal(ringBefore, RingWrites(ring));
        Assert.Empty(records);
        Assert.Equal(Cursor(rig), rig.Entries[letter].ContainerSlot);
        Assert.Equal(2, rig.Entries[stack].Count);
        Assert.Equal(before, Storage(rig));
        AssertClosed(rig);
    }

    [Fact]
    [Trait("Id", "H-R10-1")]
    public void Leash_OutsideATickPhase_ClosesTheExternal_RefusesThePutAway_CountsMutatingApiOutOfPhase_AndMutatesNothing()
    {
        // Arrange: between ticks, the axe on the cursor of an empty panel; A has the Intake open
        var rig = new InvAutoPlaceRig();
        ushort axe = rig.PlaceItem(Cursor(rig), "axe", 0, 0, 1);
        Open(rig);
        List<long> before = Storage(rig);
        long refusedBefore = rig.Counters[CounterId.MutatingApiOutOfPhase];

        // Act + Assert: SIM_ASSERT builds assert at the put-away's tick guard; Release returns. The external is closed first in both.
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => rig.Inventory.CloseExternal(_slotA, CloseCause.Leash));
            Assert.Equal(SimAssertCode.MutatingApiOutOfPhase, ex.Code);
        }
        else
        {
            rig.Inventory.CloseExternal(_slotA, CloseCause.Leash);
        }

        // Assert: closed; the axe still on the cursor; nothing mutated; counter + 1; nothing flushed by the next tick
        AssertClosed(rig);
        Assert.Equal(Cursor(rig), rig.Entries[axe].ContainerSlot);
        Assert.Equal(before, Storage(rig));
        Assert.Equal(refusedBefore + 1, rig.Counters[CounterId.MutatingApiOutOfPhase]);
        rig.Host.World.Tick();
        Assert.Empty(InventoryRecords(rig.Sink.Last));
    }

    // ------------------------------------------------------------------ AC 5: nothing to put away; no put-away on Close

    [Theory]
    [InlineData(Caller.Phase)]
    [InlineData(Caller.Op)]
    [InlineData(null)]
    [Trait("Id", "H-R10-1")]
    public void Leash_WithAnEmptyCursor_JustCloses_NoRecordNoVersionBump_AndTakesNoTickGuard(Caller? caller)
    {
        // Arrange: an empty cursor; A has the Intake open (null = between ticks)
        var rig = new InvAutoPlaceRig();
        Open(rig);
        List<long> before = Storage(rig);
        long refusedBefore = rig.Counters[CounterId.MutatingApiOutOfPhase];

        // Act
        EventEntry[] records;
        if (caller is { } c)
        {
            records = Leash(rig, c);
        }
        else
        {
            rig.Inventory.CloseExternal(_slotA, CloseCause.Leash);
            rig.Host.World.Tick();
            records = InventoryRecords(rig.Sink.Last);
        }

        // Assert
        Assert.Empty(records);
        Assert.Equal(before, Storage(rig));
        Assert.Equal(refusedBefore, rig.Counters[CounterId.MutatingApiOutOfPhase]);
        AssertClosed(rig);
    }

    [Fact]
    [Trait("Id", "H-R10-1")]
    public void Leash_WithNoExternalOpen_ChangesNothing_TheCursorEntryStays()
    {
        // Arrange: the axe on the cursor of an empty panel; nothing open
        var rig = new InvAutoPlaceRig();
        ushort axe = rig.PlaceItem(Cursor(rig), "axe", 0, 0, 1);
        List<long> before = Storage(rig);

        // Act
        EventEntry[] records = Leash(rig, Caller.Phase);

        // Assert
        Assert.Empty(records);
        Assert.Equal(Cursor(rig), rig.Entries[axe].ContainerSlot);
        Assert.Equal(before, Storage(rig));
        AssertClosed(rig);
    }

    [Fact]
    [Trait("Id", "R11")]
    public void CloseCauseClose_DirectAndThroughTheCloseOp_LeavesTheCursorEntryOnTheCursor_WithNoRecord()
    {
        // Arrange: the axe on the cursor of an empty panel (the leash would put it on the hotbar); A has the Intake open
        var rig = new InvAutoPlaceRig();
        ushort axe = rig.PlaceItem(Cursor(rig), "axe", 0, 0, 1);
        Open(rig);
        List<long> before = Storage(rig);

        // Act 1: CloseExternal(Close) in a tick phase
        _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            rig.Inventory.CloseExternal(_slotA, CloseCause.Close);
            return true;
        });
        EventEntry[] direct = InventoryRecords(rig.Sink.Last);

        // Assert 1
        Assert.Empty(direct);
        AssertClosed(rig);
        Assert.Equal(before, Storage(rig));

        // Act 2: re-open, then the Close op
        Open(rig);
        var close = new Op { Kind = InvOpKinds.Close, ReqId = 7 };
        close.SetPayload(new ClosePayload { Container = rig.Inventory.IntakeContainer.Value });
        Assert.True(rig.Host.World.EnqueueOp(rig.PlayerA, in close));
        rig.Host.World.Tick();

        // Assert 2: Ok, closed, the axe still on the cursor, no record
        Assert.Equal(ReplyResult.Ok, InvAutoPlaceRig.ReplyOf(rig.Sink.Last).Result);
        Assert.Empty(InventoryRecords(rig.Sink.Last));
        AssertClosed(rig);
        Assert.Equal(Cursor(rig), rig.Entries[axe].ContainerSlot);
        Assert.Equal(before, Storage(rig));
    }

    // ------------------------------------------------------------------ helpers

    private static ushort Cursor(InvAutoPlaceRig rig) => rig.SlotOf(rig.Inventory.CursorOf(_slotA));

    private static void Open(InvAutoPlaceRig rig) => Assert.True(rig.Inventory.TryOpenExternal(_slotA, rig.Inventory.IntakeContainer));

    /// <summary>A's external is none and A's bit is clear in the Intake's subscriber mask.</summary>
    private static void AssertClosed(InvAutoPlaceRig rig)
    {
        Assert.True(rig.Inventory.OpenSet.ExternalOf(_slotA).IsNone);
        Assert.False(rig.Inventory.OpenSet.IsInOpenSet(_slotA, rig.Inventory.IntakeContainer));
        Assert.Equal(0, rig.Inventory.Subscribers.Mask(rig.Intake));
    }

    /// <summary>Runs <c>CloseExternal(A, Leash)</c> from <paramref name="caller"/> in one tick; returns that tick's inventory records.</summary>
    private static EventEntry[] Leash(InvAutoPlaceRig rig, Caller caller)
    {
        if (caller == Caller.Phase)
        {
            _ = IntakeSpawnOutfeedTests.InPhase(rig, () =>
            {
                rig.Inventory.CloseExternal(_slotA, CloseCause.Leash);
                return true;
            });
        }
        else
        {
            // The fixture op's own bracket touches nothing, so it seals nothing: every record is the put-away's
            Reply reply = rig.Step((ref InvPlan p) => 0, (ref InvPlan p) => rig.Inventory.CloseExternal(_slotA, CloseCause.Leash));
            Assert.True(reply.IsOk);
        }

        return InventoryRecords(rig.Sink.Last);
    }

    /// <summary>
    /// The one-bracket contract: one call scope (one <c>CallSeq</c>), every record to A's bit only, exactly one seal per touched container
    /// in <paramref name="touched"/> order (each <c>from + 1</c>, actor A, cause None) after that container's records, each touched
    /// container's version + 1 and every other panel and Intake version unchanged.
    /// </summary>
    private static void AssertOneBracket(InvAutoPlaceRig rig, EventEntry[] records, uint[] versionsBefore, params ushort[] touched)
    {
        Assert.NotEmpty(records);
        Assert.Single(records.Select(e => e.CallSeq).Distinct());
        Assert.All(records, e => Assert.Equal(0b01, e.Audience));
        InvVersionPayload[] seals = records.Where(e => e.Kind == InvEventKinds.Version).Select(e => e.PayloadAs<InvVersionPayload>()).ToArray();
        Assert.Equal(touched.Select(s => rig.Header(s).Id.Value), seals.Select(s => s.Container));
        Assert.All(seals, s =>
        {
            Assert.Equal(s.VersionFrom + 1, s.VersionTo);
            Assert.Equal(rig.PlayerA.Value, s.ActorId);
            Assert.Equal((byte)InvCause.None, s.Cause);
        });
        int firstSeal = Array.FindIndex(records, e => e.Kind == InvEventKinds.Version);
        Assert.All(records[firstSeal..], e => Assert.Equal(InvEventKinds.Version, e.Kind));

        ushort[] panel = PanelAndIntake(rig);
        uint[] after = Versions(rig);
        for (int i = 0; i < panel.Length; i++)
        {
            Assert.Equal(versionsBefore[i] + (touched.Contains(panel[i]) ? 1u : 0u), after[i]);
        }
    }

    private static ushort[] PanelAndIntake(InvAutoPlaceRig rig) => new[] { Cursor(rig), rig.HotbarA, rig.InventoryA, rig.Intake };

    private static uint[] Versions(InvAutoPlaceRig rig) => PanelAndIntake(rig).Select(s => rig.Header(s).Version).ToArray();

    /// <summary>Spawns <paramref name="ids"/> (letters to #4) into <paramref name="c"/> inside one tick; returns the units placed.</summary>
    private static int Spawn(InvAutoPlaceRig rig, ContainerId c, MergePolicy policy, params uint[] ids)
    {
        MailId[] mail = ids.Select(i => new MailId(i)).ToArray();
        DefIndex letter = rig.KindIndex("letter");
        return IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.Spawn(c, letter, Address4, 0, mail, policy)).Placed;
    }

    /// <summary>
    /// Spawns letters to #4 with <paramref name="ids"/> into A's empty player_inventory as one stack at (0, 0) (merge policy), then
    /// "picks" it onto A's cursor: one fixture-op bracket whose commit is <see cref="InventoryMutator.Transfer"/> (story 007's
    /// <c>pick</c> commit). Call it before anything else is placed in player_inventory. Returns the cursor entry.
    /// </summary>
    private static ushort LetterOnCursor(InvAutoPlaceRig rig, params uint[] ids)
    {
        Assert.Equal(0, rig.Header(rig.InventoryA).EntryCount);
        Assert.Equal(ids.Length, Spawn(rig, rig.Inventory.PlayerInventoryOf(_slotA), MergePolicy.Merge, ids));
        ushort entry = rig.EntryAt(rig.InventoryA, 0, 0);
        ushort from = rig.InventoryA;
        ushort cursor = Cursor(rig);
        Reply picked = rig.Step(
            (ref InvPlan p) => p.Touch(from) < 0 || p.Touch(cursor) < 0 ? CoreReason.BadRequest : (ushort)0,
            (ref InvPlan p) => rig.Inventory.Mutator.Transfer(ref p, entry, cursor, 0, 0, false));
        Assert.True(picked.IsOk);
        Assert.Equal(cursor, rig.Entries[entry].ContainerSlot);
        Assert.Equal(ids, rig.IdsOf(entry));
        Assert.Equal(0, rig.Header(rig.InventoryA).EntryCount);
        return entry;
    }

    /// <summary>Puts one plank in each usable hotbar slot (1 to 7) but the last <paramref name="freeSlots"/>.</summary>
    private static void FillHotbar(InvAutoPlaceRig rig, int freeSlots)
    {
        for (int x = 1; x <= 7 - freeSlots; x++)
        {
            _ = rig.PlaceItem(rig.HotbarA, "plank", x, 0, 1);
        }

        Assert.Equal(1 + freeSlots, rig.Header(rig.HotbarA).FreeCells); // reserved slot 0 counts as free
    }

    /// <summary>Puts one plank in each player_inventory cell, row-major, but the last <paramref name="freeCells"/>.</summary>
    private static void FillPlayerInventory(InvAutoPlaceRig rig, int freeCells)
    {
        for (int i = 0; i < 16 - freeCells; i++)
        {
            _ = rig.PlaceItem(rig.InventoryA, "plank", i % 8, i / 8, 1);
        }

        Assert.Equal(freeCells, rig.Header(rig.InventoryA).FreeCells);
    }

    /// <summary>Every container header and occupancy owner, the live entries, the arena's units and blocks per class.</summary>
    private static List<long> Storage(InvAutoPlaceRig rig)
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

        return values;
    }

    /// <summary>Every check record the ring was offered: kept, overwritten, or suppressed as a repeat of the arena's exhaustion code.</summary>
    private static long RingWrites(SimCheckRing ring) => ring.Count + (long)ring.Overwritten + ring.Suppressed(CheckCode.InventoryArenaExhausted);

    private static EventEntry[] InventoryRecords(EventEntry[] log) =>
        log.Where(e => e.Kind is >= InvEventKinds.Inserted and <= InvEventKinds.Version).ToArray();
}
