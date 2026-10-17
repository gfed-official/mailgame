using System.Reflection;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Story 022: the System-only PlayerJoin / PlayerLeave handlers (GDD SC-R21-1): accept, validate-then-mutate rejects through the
/// SC-R8-2 harness, leave and keep ticking, the join-listener seam and the sample-latch reset (TD-049 item 1).
/// </summary>
public class PlayerJoinTests
{
    private const EventKind ListenerEvent = DrainRig.TestEvent;

    private static Op JoinOp(uint req, byte slot, uint playerId = 0)
    {
        var op = new Op { Kind = OpKind.PlayerJoin, ReqId = req };
        op.SetPayload(new PlayerJoinPayload { PlayerId = playerId, Slot = slot });
        return op;
    }

    private static Op LeaveOp(uint req, uint playerId)
    {
        var op = new Op { Kind = OpKind.PlayerLeave, ReqId = req };
        op.SetPayload(new PlayerLeavePayload { PlayerId = playerId });
        return op;
    }

    /// <summary>Listener stub: records calls and raises an event inside the call so its position against the OpReply is observable.</summary>
    private sealed class ListenerStub : IPlayerJoinListener
    {
        public SimWorld? World;
        public List<(string What, byte Slot, uint Player)> Calls { get; } = new();
        public uint LastPlayer;
        public uint LastJoinTick;

        public void OnPlayerJoined(PlayerSlot slot, EntityId player, uint tick)
        {
            Calls.Add(("join", slot.Value, player.Value));
            LastJoinTick = tick;
            LastPlayer = player.Value;
            uint v = player.Value;
            World?.Events.TryRaise(ListenerEvent, 0xFF, in v);
        }

        public void OnPlayerLeft(PlayerSlot slot, EntityId player)
        {
            Calls.Add(("leave", slot.Value, player.Value));
            uint v = player.Value;
            World?.Events.TryRaise(ListenerEvent, 0xFF, in v);
        }
    }

    private sealed class Fwd : ITickReader, IWorldStateReader
    {
        public SimWorld? Target { get; set; }

        public uint CurrentTick => Target!.CurrentTick;

        public TickStage Stage => Target!.Stage;

        public PhaseId Phase => Target!.Phase;

        public SimState State => Target!.State;
    }

    /// <summary>An empty world (no players) built through the normal builder path with Core state bound.</summary>
    private sealed class Rig
    {
        public Rig(bool instrument = true)
        {
            var fwd = new Fwd();
            Counters = new SimCounters();
            var ring = new SimCheckRing();
            Ids = new EntityIds(fwd, ring, Counters);
            Slots = new SlotTable();
            var builder = new SimWorldBuilder(new SimCoreSettings { SystemOpReserve = 8 }, Counters, ring);
            builder.BindCoreState(Ids, null, null, Slots);
            builder.BindEventKind(EventDescriptor.For<uint>(ListenerEvent, ReplicationClass.Replicated, false, 1, 0, 4));
            if (instrument)
            {
                builder.BindEventSink(Sink);
            }

            builder.BindPlayerJoinListener(Listener);
            World = builder.Freeze();
            fwd.Target = World;
            Listener.World = World;
        }

        public SimCounters Counters { get; }

        public EntityIds Ids { get; }

        public SlotTable Slots { get; }

        public CaptureSink Sink { get; } = new();

        public ListenerStub Listener { get; } = new();

        public SimWorld World { get; }

        public void Send(in Op op) => Assert.True(World.EnqueueOp(ActorId.System, in op));

        public List<Reply> TickReplies()
        {
            World.Tick();
            return Sink.Last.Where(e => e.Kind == EventKind.OpReply).Select(e => e.PayloadAs<Reply>()).ToList();
        }
    }

    // ---------------------------------------------------------------- AC-1

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void Ac1_TwoJoins_SlotsZeroAndThree_GetFreshIds_AreOkAndPresent()
    {
        var rig = new Rig();
        rig.Send(JoinOp(1, 0));
        rig.Send(JoinOp(2, 3));

        List<Reply> replies = rig.TickReplies();

        Assert.Equal(2, replies.Count);
        Assert.All(replies, r => Assert.True(r.IsOk));
        Assert.Equal(16_777_217u, rig.Slots.Entry(PlayerSlot.FromValue(0)).PlayerId.Value);
        Assert.Equal(16_777_218u, rig.Slots.Entry(PlayerSlot.FromValue(3)).PlayerId.Value);
        Assert.Equal(0b0000_1001, rig.Slots.ConnectedMask);
        Assert.True(rig.Slots.IsPresent(new ActorId(16_777_217u)));
        Assert.True(rig.Slots.IsPresent(new ActorId(16_777_218u)));
        Assert.Equal(2u, rig.Ids.Peek(EntityClass.Player));
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void Ac1_ListenerIsCalledInsideTheCallScope_SoItsEventPrecedesTheOpReply()
    {
        var rig = new Rig();
        rig.Send(JoinOp(1, 0));
        rig.Send(LeaveOp(2, 16_777_217u));

        rig.World.Tick();

        Assert.Equal(new[] { ("join", (byte)0, 16_777_217u), ("leave", (byte)0, 16_777_217u) }, rig.Listener.Calls.ToArray());
        Assert.Equal(
            new[] { ListenerEvent, EventKind.OpReply, ListenerEvent, EventKind.OpReply },
            rig.Sink.Last.Select(e => e.Kind).ToArray());
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void Ac1_ListenerReceivesTheTickTheJoinWasApplied()
    {
        // TD-052 item 3: the join tick reaches the listener (player-character ClockInTick).
        var rig = new Rig();
        rig.World.Tick(); // tick 0
        rig.World.Tick(); // tick 1
        rig.Send(JoinOp(1, 2));

        rig.World.Tick(); // tick 2 applies the join

        Assert.Equal(2u, rig.Listener.LastJoinTick);
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void Ac1_NoPublicMemberMutatesTheSlotTable()
    {
        // The only mutators are Assign and Free; neither is visible outside the Sim assembly (and its tests).
        foreach (string name in new[] { "Assign", "Free" })
        {
            MethodInfo m = typeof(SlotTable).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            Assert.True(m.IsAssembly, $"SlotTable.{name} must be internal");
        }

        string[] readOnly = { "Entry", "IsPresent", "SlotOf", "CanAssign", "get_ConnectedMask" };
        string[] publicMethods = typeof(SlotTable)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();
        Assert.All(publicMethods, n => Assert.Contains(n, readOnly));

        // No public settable property or field is exposed, and the handlers and listener seam are not public.
        Assert.DoesNotContain(typeof(SlotTable).GetProperties(BindingFlags.Instance | BindingFlags.Public), p => p.SetMethod is { IsPublic: true });
        Assert.Empty(typeof(SlotTable).GetFields(BindingFlags.Instance | BindingFlags.Public));
        Assert.False(typeof(PlayerJoinHandler).IsPublic);
        Assert.False(typeof(IPlayerJoinListener).IsPublic);
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void Descriptors_AreSystemOnly_PaddingFree_AndUseNoSystemReasons()
    {
        foreach (OpDescriptor d in new[] { CorePlayerOps.JoinDescriptor, CorePlayerOps.LeaveDescriptor })
        {
            Assert.True(d.SystemOnly);
            Assert.Empty(d.Reasons);
            DescriptorAssert.Check(d, OpKindBlock.Core);
        }

        Assert.Equal(8, CorePlayerOps.JoinDescriptor.PayloadLength);
        Assert.Equal(4, CorePlayerOps.LeaveDescriptor.PayloadLength);
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void SecondListenerBind_Throws()
    {
        var builder = new SimWorldBuilder();
        builder.BindPlayerJoinListener(new ListenerStub());
        var ex = Assert.Throws<SimBuildException>(() => builder.BindPlayerJoinListener(new ListenerStub()));
        Assert.Equal(SimBuildErrorCode.PlayerJoinListenerAlreadyBound, ex.Code);
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void WorldWithoutCoreState_RegistersNoPlayerOps()
    {
        SimWorld world = new SimWorldBuilder().Freeze();
        Assert.False(world.TryGetOp(OpKind.PlayerJoin, out _, out _));
        Assert.False(world.TryGetOp(OpKind.PlayerLeave, out _, out _));
    }

    // ---------------------------------------------------------------- AC-2

    public static TheoryData<string> RejectRows => new() { "slot_invalid", "duplicate_player", "unknown_actor", "bad_request_rejoin_absent" };

    [Theory]
    [MemberData(nameof(RejectRows))]
    [Trait("Id", "SC-R21-1")]
    public void Ac2_Rejects_AllocateNothing_AndLeaveTheWorldUntouched(string row)
    {
        // DrainRig seats PlayerA in slot 0 and PlayerB in slot 3.
        (Op op, ActorId actor, ushort reason) = row switch
        {
            "slot_invalid" => (JoinOp(1, 8), ActorId.System, CoreReason.SlotInvalid),
            "duplicate_player" => (JoinOp(1, 1, DrainRig.PlayerA.Value), ActorId.System, CoreReason.DuplicatePlayer),
            "unknown_actor" => (JoinOp(1, 1), DrainRig.PlayerA, CoreReason.UnknownActor),
            _ => (JoinOp(1, 1, 0x0100_0042u), ActorId.System, CoreReason.BadRequest),
        };
        var stubs = new List<ListenerStub>();

        DrainRig rig = RejectHarness.Run(
            (b, _) =>
            {
                var stub = new ListenerStub();
                stubs.Add(stub);
                b.BindPlayerJoinListener(stub);
            },
            in op,
            actor,
            reason);

        Assert.Equal(0u, rig.Ids.Peek(EntityClass.Player));
        Assert.Equal(0b0000_1001, rig.Slots.ConnectedMask);
        Assert.Empty(stubs[0].Calls); // the handler never reached the seam
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void Ac2_SlotFreedEarlierInTheSameDrain_IsBusy_AndAllocatesNothing()
    {
        var rig = new Rig();
        rig.Send(JoinOp(1, 2));
        rig.World.Tick(); // tick 0: player 16_777_217 in slot 2
        uint peek = rig.Ids.Peek(EntityClass.Player);
        rig.Send(LeaveOp(2, 16_777_217u));
        rig.Send(JoinOp(3, 2));

        List<Reply> replies = rig.TickReplies(); // tick 1

        Assert.Equal(2, replies.Count);
        Assert.True(replies[0].IsOk);
        Assert.Equal(CoreReason.SlotBusy, replies[1].Reason);
        Assert.Equal(peek, rig.Ids.Peek(EntityClass.Player));
        Assert.Equal(0, rig.Slots.ConnectedMask);
        Assert.Single(rig.Listener.Calls, c => c.What == "leave");
        Assert.Single(rig.Listener.Calls, c => c.What == "join");
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void Ac2_BusyOccupiedSlot_Rejects()
    {
        var rig = new Rig();
        rig.Send(JoinOp(1, 5));
        rig.Send(JoinOp(2, 5));

        List<Reply> replies = rig.TickReplies();

        Assert.True(replies[0].IsOk);
        Assert.Equal(CoreReason.SlotBusy, replies[1].Reason);
        Assert.Equal(1u, rig.Ids.Peek(EntityClass.Player));
    }

    // ---------------------------------------------------------------- AC-3

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void Ac3_LeaveFreesAtT_JoinSameSlotAtTPlusOne_Succeeds_WithAFreshId()
    {
        var rig = new Rig();
        rig.Send(JoinOp(1, 4));
        rig.World.Tick(); // tick 0
        rig.Send(LeaveOp(2, 16_777_217u));
        Assert.True(rig.TickReplies().Single().IsOk); // tick 1: freed at 1
        Assert.Equal(1u, rig.Slots.Entry(PlayerSlot.FromValue(4)).FreedTick);
        Assert.False(rig.Slots.IsPresent(new ActorId(16_777_217u)));

        rig.Send(JoinOp(3, 4));
        Reply joined = rig.TickReplies().Single(); // tick 2

        Assert.True(joined.IsOk);
        Assert.Equal(16_777_218u, rig.Slots.Entry(PlayerSlot.FromValue(4)).PlayerId.Value); // ids are never reused
        Assert.Equal(16_777_218u, joined.ExtraAs<uint>()); // the reply carries the new player id in Extra[0..4) (network-adapter story 004)
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void Ac3_AfterTheLastPlayerLeaves_TickKeepsRunning_AndCurrentTickAdvances()
    {
        var rig = new Rig();
        rig.Send(JoinOp(1, 0));
        rig.Send(JoinOp(2, 1));
        rig.World.Tick();
        rig.Send(LeaveOp(3, 16_777_217u));
        rig.Send(LeaveOp(4, 16_777_218u));
        rig.World.Tick();
        Assert.Equal(0, rig.Slots.ConnectedMask);
        uint before = rig.World.CurrentTick;

        for (int i = 0; i < 5; i++)
        {
            rig.World.Tick();
        }

        Assert.Equal(before + 5, rig.World.CurrentTick);
        Assert.Equal(SimState.Running, rig.World.State);
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void LeaveOfAbsentPlayer_IsBadRequest_AndChangesNothing()
    {
        var rig = new Rig();
        rig.Send(JoinOp(1, 0));
        rig.World.Tick();
        rig.Send(LeaveOp(2, 0x0100_0099u));
        rig.Send(LeaveOp(3, 0));

        List<Reply> replies = rig.TickReplies();

        Assert.All(replies, r => Assert.Equal(CoreReason.BadRequest, r.Reason));
        Assert.Equal(1, rig.Slots.ConnectedMask);
        Assert.Single(rig.Listener.Calls);
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void LeaveFromNonSystemActor_IsUnknownActor()
    {
        var rig = new Rig();
        rig.Send(JoinOp(1, 0));
        rig.World.Tick();
        Op leave = LeaveOp(2, 16_777_217u);
        Assert.True(rig.World.EnqueueOp(new ActorId(16_777_217u), in leave));

        Reply r = rig.TickReplies().Single();

        Assert.Equal(CoreReason.UnknownActor, r.Reason);
        Assert.Equal(1, rig.Slots.ConnectedMask);
    }

    // ------------------------------------------------- TD-049 item 1

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void Leave_ResetsTheSlotsSampleLatch_SoARejoiningPeersLowSeqIsAccepted()
    {
        var rig = new Rig();
        rig.Send(JoinOp(1, 0));
        rig.World.Tick(); // tick 0
        var high = new TransformSample { Seq = 50 };
        Assert.True(rig.World.TryWriteSample(PlayerSlot.FromValue(0), in high));
        rig.World.Tick(); // tick 1: latched
        Assert.Equal(50u, rig.World.Samples.LastSeq(PlayerSlot.FromValue(0)));

        rig.Send(LeaveOp(2, 16_777_217u));
        rig.World.Tick(); // tick 2: slot freed, latch state reset
        Assert.Equal(0u, rig.World.Samples.LastSeq(PlayerSlot.FromValue(0)));

        rig.Send(JoinOp(3, 0));
        rig.World.Tick(); // tick 3
        var low = new TransformSample { Seq = 1 };
        Assert.True(rig.World.TryWriteSample(PlayerSlot.FromValue(0), in low));
        long staleBefore = rig.Counters[CounterId.SamplesStale];
        rig.World.Tick(); // tick 4

        Assert.Equal(1u, rig.World.Samples.LastSeq(PlayerSlot.FromValue(0)));
        Assert.Equal(1, rig.World.Samples.Count);
        Assert.Equal(staleBefore, rig.Counters[CounterId.SamplesStale]);
    }
}

/// <summary>Story 022 guardrail: the join and leave path allocates 0 B once warm (optimised builds only, TD-008).</summary>
[Collection(NonParallelCollection.Name)]
public class PlayerJoinAllocationTests
{
    private sealed class IdListener : IPlayerJoinListener
    {
        public uint Last;

        public void OnPlayerJoined(PlayerSlot slot, EntityId player, uint tick) => Last = player.Value;

        public void OnPlayerLeft(PlayerSlot slot, EntityId player)
        {
        }
    }

    private sealed class Fwd : ITickReader, IWorldStateReader
    {
        public SimWorld? Target { get; set; }

        public uint CurrentTick => Target!.CurrentTick;

        public TickStage Stage => Target!.Stage;

        public PhaseId Phase => Target!.Phase;

        public SimState State => Target!.State;
    }

    [Fact]
    [Trait("Id", "SC-R21-1")]
    public void JoinAndLeave_AllocateNothing_InOptimisedBuilds()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        var fwd = new Fwd();
        var counters = new SimCounters();
        var ring = new SimCheckRing();
        var ids = new EntityIds(fwd, ring, counters);
        var slots = new SlotTable();
        var listener = new IdListener();
        var builder = new SimWorldBuilder(new SimCoreSettings { SystemOpReserve = 8 }, counters, ring);
        builder.BindCoreState(ids, null, null, slots);
        builder.BindPlayerJoinListener(listener);
        SimWorld world = builder.Freeze();
        fwd.Target = world;
        var join = new Op { Kind = OpKind.PlayerJoin, ReqId = 1 };
        join.SetPayload(new PlayerJoinPayload { Slot = 2 });
        var bad = new Op { Kind = OpKind.PlayerJoin, ReqId = 2 };
        bad.SetPayload(new PlayerJoinPayload { Slot = 9 });

        int runs = AllocAssert.Zero(
            "join, a rejected join, then leave",
            () =>
            {
                world.EnqueueOp(ActorId.System, in join);
                world.EnqueueOp(ActorId.System, in bad);
                world.Tick();
                var leave = new Op { Kind = OpKind.PlayerLeave, ReqId = 3 };
                leave.SetPayload(new PlayerLeavePayload { PlayerId = listener.Last });
                world.EnqueueOp(ActorId.System, in leave);
                world.Tick();
            });

        Assert.True(runs > 0);
        Assert.Equal(0, slots.ConnectedMask);
    }
}
