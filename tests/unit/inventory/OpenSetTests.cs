using System.Reflection;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Intake;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/011 slice 1 (ACs 1-4 and 6): the open set (<see cref="OpenSet"/>), <see cref="InventorySystem.TryOpenExternal"/>,
/// <see cref="InventorySystem.CloseExternal"/>, the production <c>Close</c> handler and the leave path; GDD R10 and H-R10-2; ADR-0009
/// section 4 rule 1. State is arranged only through those calls and real ops (<c>Close</c>, <c>PlayerJoin</c>, <c>PlayerLeave</c>) in a
/// composed world; "nothing changed" compares every container header (versions included), entry, arena state, subscriber mask and
/// per-slot external before and after. AC 5 (the leash put-away and <c>TargetOrder</c>) is <see cref="OpenSetPutAwayTests"/>; AC 7 (0 B) is
/// <see cref="OpenSetAllocationTests"/>.
/// </summary>
public class OpenSetTests
{
    private static readonly PlayerSlot _slotA = PlayerSlot.FromValue(0);
    private static readonly PlayerSlot _slotB = PlayerSlot.FromValue(1);

    // ------------------------------------------------------------------ AC 1: H-R10-2

    [Fact]
    [Trait("Id", "H-R10-2")]
    public void TryOpenExternal_C2_WhileAHasC1_ClosesC1ForAOnly_AndBKeepsC1()
    {
        // Arrange: A and B both have C1 (the rig's chest) open
        var rig = new InvEventRig();
        ContainerId c1 = rig.Chest;
        ContainerId c2 = CreateExternal(rig);
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, c1));
        Assert.True(rig.Inventory.TryOpenExternal(_slotB, c1));
        Assert.Equal(0b11, Mask(rig, c1));
        List<long> storage = rig.Snapshot();

        // Act
        bool opened = rig.Inventory.TryOpenExternal(_slotA, c2);

        // Assert: C1 closed for A only (A's bit cleared, B's kept); C2 open for A only; no container changed
        Assert.True(opened);
        Assert.False(rig.Inventory.OpenSet.IsInOpenSet(_slotA, c1));
        Assert.True(rig.Inventory.OpenSet.IsInOpenSet(_slotA, c2));
        Assert.True(rig.Inventory.OpenSet.IsInOpenSet(_slotB, c1));
        Assert.False(rig.Inventory.OpenSet.IsInOpenSet(_slotB, c2));
        Assert.Equal(0b10, Mask(rig, c1));
        Assert.Equal(0b01, Mask(rig, c2));
        Assert.Equal(storage, rig.Snapshot());
    }

    [Fact]
    [Trait("Id", "H-R10-2")]
    public void CloseOp_OnC1_AfterAOpenedC2_IsNotOpen_AndChangesNothing()
    {
        // Arrange
        var rig = new InvEventRig();
        ContainerId c1 = rig.Chest;
        ContainerId c2 = CreateExternal(rig);
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, c1));
        Assert.True(rig.Inventory.TryOpenExternal(_slotB, c1));
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, c2));
        List<long> before = State(rig);

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, CloseOp(c1.Value));

        // Assert
        AssertRejected(log, (ushort)InvReason.NotOpen);
        Assert.Equal(before, State(rig));
    }

    [Fact]
    [Trait("Id", "H-R10-2")]
    public void IsInOpenSet_AnotherSlotsPanelAndCursor_IsFalse_TheOwnersAreTrue_WhateverExternalIsOpen()
    {
        // Arrange: A's three panel containers; B has the chest open
        var rig = new InvEventRig();
        ContainerId[] panelA = { rig.HotbarA, rig.InventoryA, rig.CursorA };
        ContainerId[] panelB = { rig.Inventory.HotbarOf(_slotB), rig.Inventory.PlayerInventoryOf(_slotB), rig.Inventory.CursorOf(_slotB) };
        Assert.True(rig.Inventory.TryOpenExternal(_slotB, rig.Chest));

        // Act + Assert: 3 + 3 containers each way
        Assert.All(panelA, c => Assert.True(rig.Inventory.OpenSet.IsInOpenSet(_slotA, c)));
        Assert.All(panelA, c => Assert.False(rig.Inventory.OpenSet.IsInOpenSet(_slotB, c)));
        Assert.All(panelB, c => Assert.True(rig.Inventory.OpenSet.IsInOpenSet(_slotB, c)));
        Assert.All(panelB, c => Assert.False(rig.Inventory.OpenSet.IsInOpenSet(_slotA, c)));
        Assert.All(panelA, c => Assert.False(rig.Inventory.OpenSet.IsInOpenSet(PlayerSlot.None, c)));
    }

    // ------------------------------------------------------------------ AC 2: the Close op

    [Fact]
    public void CloseOp_OnTheActorsExternal_RemovesIt_WithNoRecordAndNoVersionBump()
    {
        // Arrange: A and B have the chest open
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, rig.Chest));
        Assert.True(rig.Inventory.TryOpenExternal(_slotB, rig.Chest));
        List<long> storage = rig.Snapshot();

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, CloseOp(rig.Chest.Value));

        // Assert: Ok; A's external and bit gone, B's kept; no inventory record; no container (version included) changed
        Reply reply = InvEventRig.ReplyOf(log);
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.Equal(CoreReason.Ok, reply.Reason);
        Assert.True(rig.Inventory.OpenSet.ExternalOf(_slotA).IsNone);
        Assert.False(rig.Inventory.OpenSet.IsInOpenSet(_slotA, rig.Chest));
        Assert.Equal(rig.Chest, rig.Inventory.OpenSet.ExternalOf(_slotB));
        Assert.Equal(0b10, Mask(rig, rig.Chest));
        Assert.Empty(InventoryRecords(log));
        Assert.Equal(storage, rig.Snapshot());
    }

    [Fact]
    public void CloseOp_LiveExternalNotInTheActorsSet_IsNotOpen_AndChangesNothing_ForAPlayerAndForTheSystemActor()
    {
        // Arrange: B has the chest open, A has nothing open
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryOpenExternal(_slotB, rig.Chest));
        List<long> before = State(rig);

        // Act
        EventEntry[] byA = rig.Run(rig.PlayerA, CloseOp(rig.Chest.Value));
        EventEntry[] bySystem = rig.Run(ActorId.System, CloseOp(rig.Chest.Value));

        // Assert
        AssertRejected(byA, (ushort)InvReason.NotOpen);
        AssertRejected(bySystem, (ushort)InvReason.NotOpen);
        Assert.Equal(before, State(rig));
    }

    [Fact]
    public void CloseOp_AnotherSlotsPanelOrCursor_IsNotOpen_AndChangesNothing()
    {
        // Arrange
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, rig.Chest));
        ContainerId[] panelB = { rig.Inventory.HotbarOf(_slotB), rig.Inventory.PlayerInventoryOf(_slotB), rig.Inventory.CursorOf(_slotB) };
        List<long> before = State(rig);

        // Act + Assert: each of B's 3 containers, closed by A
        foreach (ContainerId c in panelB)
        {
            AssertRejected(rig.Run(rig.PlayerA, CloseOp(c.Value)), (ushort)InvReason.NotOpen);
        }

        Assert.Equal(before, State(rig));
    }

    [Fact]
    public void CloseOp_TheActorsOwnPanelOrCursor_IsOk_AndChangesNothing_TheExternalStaysOpen()
    {
        // Arrange: A has the chest open
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, rig.Chest));
        List<long> before = State(rig);

        // Act + Assert: each of A's 3 containers is always in A's set and never closable
        foreach (ContainerId c in new[] { rig.HotbarA, rig.InventoryA, rig.CursorA })
        {
            EventEntry[] log = rig.Run(rig.PlayerA, CloseOp(c.Value));
            Assert.Equal(ReplyResult.Ok, InvEventRig.ReplyOf(log).Result);
            Assert.Empty(InventoryRecords(log));
        }

        Assert.Equal(before, State(rig));
        Assert.Equal(rig.Chest, rig.Inventory.OpenSet.ExternalOf(_slotA));
    }

    [Fact]
    public void CloseOp_ContainerIdBoundaries_ZeroAndHighWaterAndAbove_AreBadRequest_LastIssuedPasses()
    {
        // Arrange: the high-water N = NextId; N - 1 is the last issued id (a second external), which A opens
        var rig = new InvEventRig();
        ContainerId last = CreateExternal(rig);
        uint n = rig.Inventory.Containers.NextId;
        Assert.Equal(n - 1, last.Value);
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, last));
        List<long> before = State(rig);

        // Act + Assert: 0, N, N + 1 and uint.MaxValue are bad_request with nothing changed
        foreach (uint id in new[] { 0u, n, n + 1, uint.MaxValue })
        {
            AssertRejected(rig.Run(rig.PlayerA, CloseOp(id)), CoreReason.BadRequest);
        }

        Assert.Equal(before, State(rig));

        // N - 1 passes the bad_request step and closes A's external
        Assert.Equal(ReplyResult.Ok, InvEventRig.ReplyOf(rig.Run(rig.PlayerA, CloseOp(n - 1))).Result);
        Assert.True(rig.Inventory.OpenSet.ExternalOf(_slotA).IsNone);
    }

    [Fact]
    public void CloseOp_IssuedIdNoLongerLive_IsContainerGone_AndChangesNothing()
    {
        // Arrange: no container is destroyed before M-1b, so no real path tombstones a container id. The narrowest seam: advance the
        // container table's private id counter by one (reflection), so id N is below the high-water but was never live, the state a
        // destroyed container's id is in. The map and every live container are untouched.
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, rig.Chest));
        uint gone = SkipOneContainerId(rig);
        Assert.False(rig.Inventory.Containers.TryGetSlot(new ContainerId(gone), out _));
        List<long> before = State(rig);

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, CloseOp(gone));

        // Assert
        AssertRejected(log, (ushort)InvReason.ContainerGone);
        Assert.Equal(before, State(rig));
    }

    // ------------------------------------------------------------------ AC 3: TryOpenExternal edges

    [Fact]
    public void TryOpenExternal_DeadOrNeverIssuedId_OrNoneSlot_ReturnsFalse_AndChangesNothing()
    {
        // Arrange: A has the chest open; id N is skipped (no longer live, see CloseOp_IssuedIdNoLongerLive_IsContainerGone_AndChangesNothing)
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, rig.Chest));
        uint gone = SkipOneContainerId(rig);
        uint neverIssued = rig.Inventory.Containers.NextId;
        List<long> before = State(rig);

        // Act + Assert: 4 dead or never-issued ids for A, and a live external for the none slot
        foreach (uint id in new[] { 0u, gone, neverIssued, uint.MaxValue })
        {
            Assert.False(rig.Inventory.TryOpenExternal(_slotA, new ContainerId(id)));
        }

        Assert.False(rig.Inventory.TryOpenExternal(PlayerSlot.None, rig.Inventory.IntakeContainer));
        Assert.Equal(before, State(rig));
        Assert.Equal(rig.Chest, rig.Inventory.OpenSet.ExternalOf(_slotA));
    }

    [Fact]
    public void TryOpenExternal_ContainerWithAnOwnerSlot_ReturnsFalse_AndChangesNothing()
    {
        // Arrange: A has the chest open
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, rig.Chest));
        ContainerId[] owned =
        {
            rig.HotbarA, rig.InventoryA, rig.CursorA, rig.Inventory.HotbarOf(_slotB), rig.Inventory.PlayerInventoryOf(_slotB), rig.Inventory.CursorOf(_slotB),
        };
        List<long> before = State(rig);

        // Act + Assert: A's 3 and B's 3 panel containers
        Assert.All(owned, c => Assert.False(rig.Inventory.TryOpenExternal(_slotA, c)));
        Assert.Equal(before, State(rig));
        Assert.Equal(rig.Chest, rig.Inventory.OpenSet.ExternalOf(_slotA));
    }

    [Fact]
    public void TryOpenExternal_TheCurrentExternalAgain_ReturnsTrue_AndChangesNothing()
    {
        // Arrange
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, rig.Chest));
        List<long> before = State(rig);

        // Act
        bool again = rig.Inventory.TryOpenExternal(_slotA, rig.Chest);

        // Assert
        Assert.True(again);
        Assert.Equal(before, State(rig));
    }

    [Fact]
    public void CloseExternal_ClosesOnlyThatSlotsExternal_AndOnASlotWithNoExternalOrTheNoneSlotChangesNothing()
    {
        // Arrange: A and B have the chest open
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, rig.Chest));
        Assert.True(rig.Inventory.TryOpenExternal(_slotB, rig.Chest));
        List<long> storage = rig.Snapshot();

        // Act
        rig.Inventory.CloseExternal(_slotA, CloseCause.Close);
        List<long> afterFirst = State(rig);
        rig.Inventory.CloseExternal(_slotA, CloseCause.Close);
        rig.Inventory.CloseExternal(PlayerSlot.None, CloseCause.Close);

        // Assert: A closed, B kept; the repeat and the none slot change nothing; no container changed
        Assert.True(rig.Inventory.OpenSet.ExternalOf(_slotA).IsNone);
        Assert.Equal(rig.Chest, rig.Inventory.OpenSet.ExternalOf(_slotB));
        Assert.Equal(0b10, Mask(rig, rig.Chest));
        Assert.Equal(afterFirst, State(rig));
        Assert.Equal(storage, rig.Snapshot());
    }

    // ------------------------------------------------------------------ AC 4: PlayerLeave

    [Fact]
    public void PlayerLeave_ClearsTheSlotsExternalAndItsBit_WithNoRecordAndNoVersionBump_AndARejoinStartsWithNoExternal()
    {
        // Arrange: A and B have the chest open
        var rig = new InvEventRig();
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, rig.Chest));
        Assert.True(rig.Inventory.TryOpenExternal(_slotB, rig.Chest));
        List<long> storage = rig.Snapshot();
        var leave = new Op { Kind = OpKind.PlayerLeave, ReqId = 41 };
        leave.SetPayload(new PlayerLeavePayload { PlayerId = rig.Host.Services.Slots.Entry(_slotB).PlayerId.Value });

        // Act
        EventEntry[] leaveLog = rig.Run(ActorId.System, leave);

        // Assert: B's external and bit gone, A's kept; no inventory record; no container (version included) changed
        Assert.Equal(ReplyResult.Ok, InvEventRig.ReplyOf(leaveLog).Result);
        Assert.True(rig.Inventory.OpenSet.ExternalOf(_slotB).IsNone);
        Assert.Equal(0b01, Mask(rig, rig.Chest));
        Assert.Empty(InventoryRecords(leaveLog));
        Assert.Equal(storage, rig.Snapshot());

        // Act: a new player joins slot 1
        var join = new Op { Kind = OpKind.PlayerJoin, ReqId = 42 };
        join.SetPayload(new PlayerJoinPayload { Slot = 1 });
        Assert.Equal(ReplyResult.Ok, InvEventRig.ReplyOf(rig.Run(ActorId.System, join)).Result);
        ActorId rejoined = ActorId.FromEntity(rig.Host.Services.Slots.Entry(_slotB).PlayerId);
        Assert.NotEqual(rig.PlayerB, rejoined);

        // Assert: the rejoined slot starts with no external, so a Close of the chest is not_open
        Assert.True(rig.Inventory.OpenSet.ExternalOf(_slotB).IsNone);
        Assert.False(rig.Inventory.OpenSet.IsInOpenSet(_slotB, rig.Chest));
        Assert.Equal(0b01, Mask(rig, rig.Chest));
        AssertRejected(rig.Run(rejoined, CloseOp(rig.Chest.Value)), (ushort)InvReason.NotOpen);
    }

    // ------------------------------------------------------------------ AC 6: audiences

    [Fact]
    public void SpawnIntoTheIntake_ReachesAOnlyWhileAHasItOpen_AndNeverB()
    {
        // Arrange: A in slot 0 (the rig's), B joins slot 1; neither has the Intake open
        var rig = new InvAutoPlaceRig();
        var join = new Op { Kind = OpKind.PlayerJoin, ReqId = 2 };
        join.SetPayload(new PlayerJoinPayload { Slot = 1 });
        Assert.True(rig.Host.World.EnqueueOp(ActorId.System, in join));
        rig.Host.World.Tick();
        ActorId playerA = rig.PlayerA;
        ContainerId intake = rig.Inventory.IntakeContainer;
        DefIndex letter = rig.KindIndex("letter");
        uint nextMail = 1;
        EventEntry[] SpawnTick()
        {
            MailId[] ids = { new(nextMail++) };
            SpawnResult r = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.Spawn(intake, letter, 4, 0, ids, MergePolicy.NoMerge));
            Assert.Equal(1, r.Placed);
            EventEntry[] records = InventoryRecords(rig.Sink.Last);
            Assert.NotEmpty(records);
            return records;
        }

        // Act + Assert: before the open, the Intake's records reach nobody
        Assert.All(SpawnTick(), e => Assert.Equal(0, e.Audience));

        // after A opens it, every record and the seal reach A and only A
        Assert.True(rig.Inventory.TryOpenExternal(_slotA, intake));
        Assert.All(SpawnTick(), e => Assert.Equal(0b01, e.Audience));

        // after A closes it (the Close op), A receives none again
        var close = CloseOp(intake.Value);
        Assert.True(rig.Host.World.EnqueueOp(playerA, in close));
        rig.Host.World.Tick();
        Assert.Equal(ReplyResult.Ok, InvAutoPlaceRig.ReplyOf(rig.Sink.Last).Result);
        Assert.All(SpawnTick(), e => Assert.Equal(0, e.Audience));
    }

    // ------------------------------------------------------------------ helpers

    private static Op CloseOp(uint container)
    {
        var op = new Op { Kind = InvOpKinds.Close, ReqId = 31 };
        op.SetPayload(new ClosePayload { Container = container });
        return op;
    }

    /// <summary>A second fixture external (the rig chest's def and dimensions, no owner), created between ticks.</summary>
    private static ContainerId CreateExternal(InvEventRig rig)
    {
        ref ContainerHeader chest = ref rig.Header(rig.Chest);
        Assert.True(rig.Inventory.TryCreateContainer(chest.DefIndex, chest.W, chest.H, chest.Layout, ContainerHeader.NoOwner, out ContainerId id));
        return id;
    }

    /// <summary>
    /// TEST SEAM (no container destruction exists before M-1b): advances the container table's private <c>_nextId</c> by one, so the
    /// skipped id is below the high-water and not live. Returns it.
    /// </summary>
    private static uint SkipOneContainerId(InvEventRig rig)
    {
        ContainerTable table = rig.Inventory.Containers;
        uint skipped = table.NextId;
        FieldInfo field = typeof(ContainerTable).GetField("_nextId", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(table, skipped + 1);
        Assert.Equal(skipped + 1, table.NextId);
        return skipped;
    }

    private static byte Mask(InvEventRig rig, ContainerId c) => rig.Inventory.Subscribers.Mask(rig.SlotOf(c));

    /// <summary>The rig's storage snapshot (headers with versions, occupancy, entries, arena), every live container's subscriber mask and every slot's external.</summary>
    private static List<long> State(InvEventRig rig)
    {
        List<long> values = rig.Snapshot();
        ContainerTable containers = rig.Inventory.Containers;
        for (int s = 0; s < containers.LiveCount; s++)
        {
            values.Add(rig.Inventory.Subscribers.Mask((ushort)s));
        }

        for (byte slot = 0; slot < PlayerSlot.Count; slot++)
        {
            values.Add(rig.Inventory.OpenSet.ExternalOf(PlayerSlot.FromValue(slot)).Value);
        }

        return values;
    }

    private static EventEntry[] InventoryRecords(EventEntry[] log) =>
        log.Where(e => e.Kind is >= InvEventKinds.Inserted and <= InvEventKinds.Version).ToArray();

    private static void AssertRejected(EventEntry[] log, ushort reason)
    {
        Reply reply = InvEventRig.ReplyOf(log);
        Assert.Equal(ReplyResult.Rejected, reply.Result);
        Assert.Equal(reason, reply.Reason);
        Assert.Empty(InventoryRecords(log));
    }
}
