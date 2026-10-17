using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-016 and TR-sim-017 for story 014 (GDD SC-R6-1, enqueue half; AC-1 to AC-3): the op ring
/// (<see cref="OpQueue"/>) and <see cref="SimWorld.EnqueueOp"/> stamping, the system reserve, every refusal and its counter /
/// check record, boundary ticks and allocation. The drain-side half (handler sees the stamped actor) is story 020.
/// Behaviour that differs between SIM_ASSERT and other builds branches on <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// </summary>
[Trait("Id", "SC-R6-1")]
public class OpQueueTests
{
    private const uint PlayerA = 0x0100_0005u;
    private const uint PlayerB = 0x0200_0009u;

    private static readonly ActorId _actorA = new(PlayerA);
    private static readonly ActorId _actorB = new(PlayerB);

    private static SimWorld Frozen(int capacity, int reserve, SimInitialCounters? initial = null)
    {
        // MaxOpsDrainedPerTick = 0: these tests inspect the ring across ticks, so phase 0 must not drain it (Story 020).
        var settings = new SimCoreSettings { OpQueueCapacity = capacity, SystemOpReserve = reserve, MaxOpsDrainedPerTick = 0 };
        return new SimWorldBuilder(settings, initialCounters: initial).Freeze();
    }

    /// <summary>Builds a world directly in <paramref name="state"/>, bypassing the builder (the only way to hold a Constructed world).</summary>
    private static SimWorld InState(SimState state)
    {
        var phases = new ISimPhase[14][];
        for (int i = 0; i < phases.Length; i++)
        {
            phases[i] = Array.Empty<ISimPhase>();
        }

        var settings = new SimCoreSettings { OpQueueCapacity = 8, SystemOpReserve = 2 };
        var parts = new SimWorldParts(
            phases,
            Array.Empty<ITickEndCheck>(),
            settings,
            new SimCounters(),
            new SimCheckRing(settings.CheckRingCapacity),
            new NullPhaseProbe())
        {
            InitialState = state,
        };
        return new SimWorld(parts);
    }

    private static Op OpWith(uint reqId, uint claimedActor = 0u, uint payloadId = 0u)
    {
        var op = new Op { Kind = OpKind.TeleportAck, ReqId = reqId, Actor = new ActorId(claimedActor), OpSeq = 0xDEAD_BEEFu };
        op.SetPayload(payloadId);
        return op;
    }

    private static Op Plain(uint reqId = 1) => new() { Kind = OpKind.TeleportAck, ReqId = reqId };

    // ---------------------------------------------------------------- AC-1

    [Fact]
    public void Ac1_ActorIdSystem_IsOne()
    {
        Assert.Equal(0x0000_0001u, ActorId.System.Value);
    }

    [Fact]
    public void Ac1_ThousandEnqueues_EveryTenthRefusedOnFullRing_SequenceStrictlyIncreasingFromOne()
    {
        // Arrange
        SimWorld world = Frozen(capacity: 8, reserve: 2);
        uint expectedSeq = 1;
        int refused = 0;

        // Act / Assert
        for (int call = 1; call <= 1000; call++)
        {
            if (call % 10 == 0)
            {
                // Hold the ring full with System ops (each accepted and sequenced), then a player call must be refused.
                while (world.Ops.Count < world.Ops.Capacity)
                {
                    Op filler = Plain();
                    Assert.True(world.EnqueueOp(ActorId.System, in filler));
                    Assert.Equal(expectedSeq++, world.LastOpSeq);
                }

                uint before = world.LastOpSeq;
                Op rejected = OpWith(reqId: (uint)call);
                Assert.False(world.EnqueueOp(_actorA, in rejected));
                Assert.Equal(before, world.LastOpSeq);
                refused++;

                DrainAndVerify(world, expectedSeq);
                continue;
            }

            // The op's own Actor field and its payload both name a different player; the parameter must win.
            ActorId actor = call % 2 == 0 ? _actorA : _actorB;
            uint otherPlayer = call % 2 == 0 ? PlayerB : PlayerA;
            Op op = OpWith(reqId: (uint)call, claimedActor: otherPlayer, payloadId: otherPlayer);
            Assert.True(world.EnqueueOp(actor, in op));
            Assert.Equal(expectedSeq, world.LastOpSeq);

            ref readonly Op queued = ref world.Ops.PeekAt(world.Ops.Count - 1);
            Assert.Equal(expectedSeq, queued.OpSeq);
            Assert.Equal(actor.Value, queued.Actor.Value);
            Assert.Equal((uint)call, queued.ReqId);
            Assert.Equal(otherPlayer, queued.PayloadAs<uint>());
            expectedSeq++;

            Assert.True(world.OpsForTests.TryDequeue(out _, out _));
        }

        Assert.Equal(100, refused);
        Assert.Equal(100L, world.Counters[CounterId.BusyPlayer]);
        Assert.Equal(expectedSeq - 1, world.LastOpSeq);
    }

    /// <summary>Drains the ring, checking the queued ops are the System fillers with consecutive sequence numbers ending at <paramref name="expectedSeq"/> - 1.</summary>
    private static void DrainAndVerify(SimWorld world, uint expectedSeq)
    {
        uint seq = expectedSeq - (uint)world.Ops.Count;
        while (world.OpsForTests.TryDequeue(out Op taken, out _))
        {
            Assert.Equal(seq++, taken.OpSeq);
            Assert.Equal(ActorId.System.Value, taken.Actor.Value);
        }

        Assert.Equal(expectedSeq, seq);
    }

    [Fact]
    public void Ac1_RefusedCall_ConsumesNoOpSeq_NextAcceptedGetsNextValue()
    {
        SimWorld world = Frozen(capacity: 4, reserve: 1);
        Op op = Plain();
        for (int i = 0; i < 3; i++)
        {
            Assert.True(world.EnqueueOp(_actorA, in op));
        }

        Assert.False(world.EnqueueOp(_actorA, in op));
        Assert.Equal(3u, world.LastOpSeq);

        Assert.True(world.OpsForTests.TryDequeue(out _, out _));
        Assert.True(world.EnqueueOp(_actorA, in op));
        Assert.Equal(4u, world.Ops.PeekAt(world.Ops.Count - 1).OpSeq);
    }

    [Fact]
    public void Ac1_Enqueue_DoesNotModifyTheCallersOp()
    {
        SimWorld world = Frozen(capacity: 4, reserve: 1);
        Op op = OpWith(reqId: 7, claimedActor: PlayerB, payloadId: PlayerB);

        Assert.True(world.EnqueueOp(_actorA, in op));

        Assert.Equal(PlayerB, op.Actor.Value);
        Assert.Equal(0xDEAD_BEEFu, op.OpSeq);
    }

    [Fact]
    public void Ac1_FirstAcceptedOp_GetsOpSeqOne_AndLastOpSeqStartsAtZero()
    {
        SimWorld world = Frozen(capacity: 4, reserve: 1);
        Assert.Equal(0u, world.LastOpSeq);
        Op op = Plain();

        Assert.True(world.EnqueueOp(_actorA, in op));

        Assert.Equal(1u, world.Ops.PeekAt(0).OpSeq);
    }

    // ---------------------------------------------------------------- AC-2

    [Fact]
    public void Ac2_NonSystem_AtCapacityMinusReserve_IsRefusedAndCountsBusyPlayer_SystemIsAccepted()
    {
        SimWorld world = Frozen(capacity: 8, reserve: 3);
        Op op = Plain();
        for (int i = 0; i < 5; i++)
        {
            Assert.True(world.EnqueueOp(_actorA, in op));
        }

        Assert.Equal(5, world.Ops.Count);
        Assert.False(world.EnqueueOp(_actorA, in op));
        Assert.Equal(1L, world.Counters[CounterId.BusyPlayer]);
        Assert.Equal(0L, world.Counters[CounterId.BusySystem]);
        Assert.Equal(0, world.CheckRing.Count);
        Assert.Equal(5, world.Ops.Count);

        Assert.True(world.EnqueueOp(ActorId.System, in op));
        Assert.True(world.EnqueueOp(ActorId.System, in op));
        Assert.True(world.EnqueueOp(ActorId.System, in op));
        Assert.Equal(8, world.Ops.Count);
        Assert.Equal(1L, world.Counters[CounterId.BusyPlayer]);
    }

    [Fact]
    public void Ac2_System_AtFullRing_IsRefusedCountsBusySystemAndWritesOneRecord()
    {
        SimWorld world = Frozen(capacity: 4, reserve: 2);
        Op op = Plain();
        for (int i = 0; i < 4; i++)
        {
            Assert.True(world.EnqueueOp(i < 2 ? _actorA : ActorId.System, in op));
        }

        Assert.False(world.EnqueueOp(ActorId.System, in op));

        Assert.Equal(1L, world.Counters[CounterId.BusySystem]);
        Assert.Equal(0L, world.Counters[CounterId.BusyPlayer]);
        Assert.Equal(1, world.CheckRing.Count);
        SimCheckRecord record = world.CheckRing[0];
        Assert.Equal((ushort)CheckCode.BusySystem, (ushort)record.Code);
        Assert.Equal(4, world.Ops.Count);
        Assert.Equal(4u, world.LastOpSeq);
    }

    [Fact]
    public void Ac2_NonSystem_AtFullRing_CountsBusyPlayerOnly()
    {
        SimWorld world = Frozen(capacity: 4, reserve: 0);
        Op op = Plain();
        for (int i = 0; i < 4; i++)
        {
            Assert.True(world.EnqueueOp(_actorA, in op));
        }

        Assert.False(world.EnqueueOp(_actorA, in op));

        Assert.Equal(1L, world.Counters[CounterId.BusyPlayer]);
        Assert.Equal(0L, world.Counters[CounterId.BusySystem]);
        Assert.Equal(0, world.CheckRing.Count);
    }

    [Fact]
    public void Ac2_ReserveZero_PlayerCanFillTheWholeRing_ThenSystemIsRefusedToo()
    {
        SimWorld world = Frozen(capacity: 4, reserve: 0);
        Op op = Plain();

        for (int i = 0; i < 4; i++)
        {
            Assert.True(world.EnqueueOp(_actorA, in op));
        }

        Assert.False(world.EnqueueOp(ActorId.System, in op));
        Assert.Equal(1L, world.Counters[CounterId.BusySystem]);
    }

    [Fact]
    public void Ac2_BusySystemRecord_IsRateLimitedButCounterIsNot()
    {
        SimWorld world = Frozen(capacity: 2, reserve: 0);
        Op op = Plain();
        world.EnqueueOp(ActorId.System, in op);
        world.EnqueueOp(ActorId.System, in op);

        for (int i = 0; i < 5; i++)
        {
            Assert.False(world.EnqueueOp(ActorId.System, in op));
        }

        Assert.Equal(5L, world.Counters[CounterId.BusySystem]);
        Assert.Equal(1, world.CheckRing.Count);
        Assert.Equal(4u, world.CheckRing.Suppressed(CheckCode.BusySystem));
    }

    [Fact]
    public void Ac2_OpSeqCounterAtMax_RefusesCountsAndWritesOneRecordWithoutWrapping()
    {
        SimWorld world = Frozen(8, 2, new SimInitialCounters { LastOpSeq = uint.MaxValue });
        Op op = Plain();

        Assert.False(world.EnqueueOp(_actorA, in op));
        Assert.False(world.EnqueueOp(ActorId.System, in op));

        Assert.Equal(2L, world.Counters[CounterId.OpSeqExhausted]);
        Assert.Equal(1, world.CheckRing.Count);
        Assert.Equal((ushort)CheckCode.OpSeqExhausted, (ushort)world.CheckRing[0].Code);
        Assert.Equal(uint.MaxValue, world.LastOpSeq);
        Assert.Equal(0, world.Ops.Count);
        Assert.Equal(0L, world.Counters[CounterId.BusyPlayer]);
    }

    [Fact]
    public void Ac2_OpSeqCounterOneBelowMax_IssuesMaxThenRefuses()
    {
        SimWorld world = Frozen(8, 2, new SimInitialCounters { LastOpSeq = uint.MaxValue - 1 });
        Op op = Plain();

        Assert.True(world.EnqueueOp(_actorA, in op));
        Assert.Equal(uint.MaxValue, world.Ops.PeekAt(0).OpSeq);
        Assert.False(world.EnqueueOp(_actorA, in op));
        Assert.Equal(1L, world.Counters[CounterId.OpSeqExhausted]);
        Assert.Equal(1, world.Ops.Count);
    }

    [Fact]
    public void Ac2_FullRing_TakesPrecedenceOverExhaustion()
    {
        SimWorld world = Frozen(2, 0, new SimInitialCounters { LastOpSeq = uint.MaxValue - 2 });
        Op op = Plain();
        Assert.True(world.EnqueueOp(_actorA, in op));
        Assert.True(world.EnqueueOp(_actorA, in op));

        Assert.False(world.EnqueueOp(_actorA, in op));

        Assert.Equal(1L, world.Counters[CounterId.BusyPlayer]);
        Assert.Equal(0L, world.Counters[CounterId.OpSeqExhausted]);
    }

    [Theory]
    [InlineData(SimState.Constructed)]
    [InlineData(SimState.Stopped)]
    [InlineData(SimState.Faulted)]
    public void Ac2_EnqueueInNonAcceptingState_IsRefusedAndCountsEnqueueBadState(SimState state)
    {
        SimWorld world = InState(state);
        Op op = Plain();

        Assert.False(world.EnqueueOp(_actorA, in op));
        Assert.False(world.EnqueueOp(ActorId.System, in op));

        Assert.Equal(2L, world.Counters[CounterId.EnqueueBadState]);
        Assert.Equal(0, world.Ops.Count);
        Assert.Equal(0u, world.LastOpSeq);
        Assert.Equal(0, world.CheckRing.Count);
    }

    [Fact]
    public void Ac2_AfterRealStop_EnqueueIsRefusedAsBadState()
    {
        SimWorld world = Frozen(8, 2);
        world.Tick();
        Assert.True(world.Stop());
        Op op = Plain();

        Assert.False(world.EnqueueOp(_actorA, in op));

        Assert.Equal(1L, world.Counters[CounterId.EnqueueBadState]);
    }

    [Fact]
    public void Ac2_ReadyWorld_AcceptsWithBoundaryTickZero_AndRunningWorldAcceptsToo()
    {
        SimWorld world = Frozen(8, 2);
        Assert.Equal(SimState.Ready, world.State);
        Op op = Plain();

        Assert.True(world.EnqueueOp(_actorA, in op));
        Assert.Equal(0u, world.Ops.BoundaryTick(0));

        world.Tick();
        Assert.Equal(SimState.Running, world.State);
        Assert.True(world.EnqueueOp(_actorA, in op));
        Assert.Equal(1u, world.Ops.BoundaryTick(1));
        Assert.Equal(0L, world.Counters[CounterId.EnqueueBadState]);
    }

    [Fact]
    public void Ac2_ThreadSafety_OffOwnerThread_AssertsUnderSimAssertAndIsNotGuardedOtherwise()
    {
        SimWorld world = Frozen(8, 2);
        Op op = Plain();
        Exception? caught = null;
        bool accepted = false;

        var thread = new Thread(() =>
        {
            try
            {
                accepted = world.EnqueueOp(_actorA, in op);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.Start();
        thread.Join();

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.IsType<SimAssertException>(caught);
            Assert.Equal(SimAssertCode.WrongThread, ex.Code);
            Assert.Equal(0, world.Ops.Count);
        }
        else
        {
            Assert.Null(caught);
            Assert.True(accepted);
        }
    }

    // ---------------------------------------------------------------- AC-3

    [Fact]
    public void Ac3_BoundaryTick_EqualsCurrentTickAtTheCall()
    {
        SimWorld world = Frozen(16, 2);
        Op op = Plain();

        for (uint tick = 0; tick < 5; tick++)
        {
            Assert.Equal(tick, world.CurrentTick);
            Assert.True(world.EnqueueOp(_actorA, in op));
            Assert.True(world.EnqueueOp(ActorId.System, in op));
            world.Tick();
        }

        Assert.Equal(10, world.Ops.Count);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal((uint)(i / 2), world.Ops.BoundaryTick(i));
            Assert.Equal((uint)(i + 1), world.Ops.PeekAt(i).OpSeq);
        }
    }

    [Fact]
    public void Ac3_DefaultSettings_AreRingOf4096WithReserve64()
    {
        SimWorld world = new SimWorldBuilder().Freeze();

        Assert.Equal(4096, world.Ops.Capacity);
        Assert.Equal(64, world.Ops.SystemOpReserve);
        Op op = Plain();
        for (int i = 0; i < 4096 - 64; i++)
        {
            Assert.True(world.EnqueueOp(_actorA, in op));
        }

        Assert.False(world.EnqueueOp(_actorA, in op));
        for (int i = 0; i < 64; i++)
        {
            Assert.True(world.EnqueueOp(ActorId.System, in op));
        }

        Assert.False(world.EnqueueOp(ActorId.System, in op));
    }

    // ---------------------------------------------------------------- OpQueue

    [Fact]
    public void Queue_NewQueue_IsEmptyWithRequestedShape()
    {
        var queue = new OpQueue(8, 2);

        Assert.Equal(8, queue.Capacity);
        Assert.Equal(2, queue.SystemOpReserve);
        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryDequeue(out Op op, out uint tick));
        Assert.Equal(default(uint), tick);
        Assert.Equal(0u, op.OpSeq);
    }

    [Fact]
    public void Queue_Admit_FollowsTheReserveTable()
    {
        var queue = new OpQueue(4, 1);
        Op op = Plain();

        Assert.Equal(OpAdmission.Accepted, queue.Admit(isSystem: false));
        Assert.Equal(OpAdmission.Accepted, queue.Admit(isSystem: true));
        for (int i = 0; i < 3; i++)
        {
            Assert.True(queue.Push(in op, _actorA, (uint)i + 1, 0));
        }

        Assert.Equal(OpAdmission.BusyPlayer, queue.Admit(isSystem: false));
        Assert.Equal(OpAdmission.Accepted, queue.Admit(isSystem: true));
        Assert.True(queue.Push(in op, ActorId.System, 4, 0));
        Assert.Equal(OpAdmission.BusyPlayer, queue.Admit(isSystem: false));
        Assert.Equal(OpAdmission.BusySystem, queue.Admit(isSystem: true));
    }

    [Fact]
    public void Queue_Push_WhenCompletelyFull_StoresNothingAndReturnsFalse()
    {
        var queue = new OpQueue(2, 0);
        Op op = Plain();
        Assert.True(queue.Push(in op, _actorA, 1, 10));
        Assert.True(queue.Push(in op, _actorA, 2, 11));

        Assert.False(queue.Push(in op, _actorB, 3, 12));

        Assert.Equal(2, queue.Count);
        Assert.Equal(1u, queue.PeekAt(0).OpSeq);
        Assert.Equal(2u, queue.PeekAt(1).OpSeq);
        Assert.Equal(11u, queue.BoundaryTick(1));
    }

    [Fact]
    public void Queue_HeadWrapsAroundTheMask_PreservingFifoOrderAndBoundaryTicks()
    {
        var queue = new OpQueue(4, 0);
        Op op = Plain();
        uint seq = 0;

        // Walk the head across the array end several times with a varying fill level.
        for (int round = 0; round < 10; round++)
        {
            int push = 1 + (round % 4);
            for (int i = 0; i < push; i++)
            {
                seq++;
                Assert.True(queue.Push(in op, _actorA, seq, seq * 10));
            }

            uint expectFirst = seq - (uint)push + 1;
            for (int i = 0; i < push; i++)
            {
                Assert.Equal(expectFirst + (uint)i, queue.PeekAt(i).OpSeq);
                Assert.Equal((expectFirst + (uint)i) * 10, queue.BoundaryTick(i));
            }

            for (int i = 0; i < push; i++)
            {
                Assert.True(queue.TryDequeue(out Op taken, out uint tick));
                Assert.Equal(expectFirst + (uint)i, taken.OpSeq);
                Assert.Equal(taken.OpSeq * 10, tick);
            }

            Assert.Equal(0, queue.Count);
        }
    }

    [Fact]
    public void Queue_DropFront_RemovesOldestAcrossTheWrapPoint()
    {
        var queue = new OpQueue(4, 0);
        Op op = Plain();
        for (uint s = 1; s <= 3; s++)
        {
            queue.Push(in op, _actorA, s, s);
        }

        queue.DropFront(2);
        queue.Push(in op, _actorA, 4, 4);
        queue.Push(in op, _actorA, 5, 5);
        queue.Push(in op, _actorA, 6, 6);

        Assert.Equal(4, queue.Count);
        Assert.Equal(3u, queue.PeekAt(0).OpSeq);

        queue.DropFront(3);
        Assert.Equal(1, queue.Count);
        Assert.Equal(6u, queue.PeekAt(0).OpSeq);
        Assert.Equal(6u, queue.BoundaryTick(0));

        queue.DropFront(0);
        Assert.Equal(1, queue.Count);
        queue.DropFront(1);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Queue_DropFront_OutOfRange_AssertsUnderSimAssertAndClampsOtherwise()
    {
        var queue = new OpQueue(4, 0);
        Op op = Plain();
        queue.Push(in op, _actorA, 1, 0);
        queue.Push(in op, _actorA, 2, 0);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.OpQueueIndexRange, Assert.Throws<SimAssertException>(() => queue.DropFront(3)).Code);
            Assert.Equal(SimAssertCode.OpQueueIndexRange, Assert.Throws<SimAssertException>(() => queue.DropFront(-1)).Code);
            Assert.Equal(2, queue.Count);
        }
        else
        {
            queue.DropFront(-1);
            Assert.Equal(2, queue.Count);
            queue.DropFront(99);
            Assert.Equal(0, queue.Count);
        }
    }

    [Fact]
    public void Queue_PeekAtAndBoundaryTick_OutOfRange_AssertUnderSimAssertAndStayInBoundsOtherwise()
    {
        var queue = new OpQueue(4, 0);
        Op op = Plain();
        queue.Push(in op, _actorA, 1, 7);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.OpQueueIndexRange, Assert.Throws<SimAssertException>(() => queue.PeekAt(1).OpSeq).Code);
            Assert.Equal(SimAssertCode.OpQueueIndexRange, Assert.Throws<SimAssertException>(() => queue.PeekAt(-1).OpSeq).Code);
            Assert.Equal(SimAssertCode.OpQueueIndexRange, Assert.Throws<SimAssertException>(() => queue.BoundaryTick(1)).Code);
            Assert.Equal(SimAssertCode.OpQueueIndexRange, Assert.Throws<SimAssertException>(() => queue.BoundaryTick(-1)).Code);
        }
        else
        {
            _ = queue.PeekAt(1).OpSeq;
            _ = queue.PeekAt(-1).OpSeq;
            _ = queue.PeekAt(int.MaxValue).OpSeq;
            _ = queue.BoundaryTick(1);
            _ = queue.BoundaryTick(int.MinValue);
        }
    }

    [Fact]
    public void Queue_Push_OverwritesActorAndOpSeqButKeepsEverythingElse()
    {
        var queue = new OpQueue(4, 0);
        Op op = OpWith(reqId: 42, claimedActor: PlayerB, payloadId: 0x1234u);

        queue.Push(in op, _actorA, 9, 5);

        ref readonly Op stored = ref queue.PeekAt(0);
        Assert.Equal(PlayerA, stored.Actor.Value);
        Assert.Equal(9u, stored.OpSeq);
        Assert.Equal(42u, stored.ReqId);
        Assert.Equal(OpKind.TeleportAck, stored.Kind);
        Assert.Equal(0x1234u, stored.PayloadAs<uint>());
        Assert.Equal(PlayerB, op.Actor.Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4096)]
    public void Queue_PowerOfTwoCapacities_AreAccepted(int capacity)
    {
        var queue = new OpQueue(capacity, 0);

        Assert.Equal(capacity, queue.Capacity);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 4)]
    [InlineData(1000, 1024)]
    [InlineData(4097, 8192)]
    public void Queue_NonPowerOfTwoCapacity_AssertsUnderSimAssertAndRoundsUpOtherwise(int capacity, int rounded)
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => new OpQueue(capacity, 0));
            Assert.Equal(SimAssertCode.OpQueueCapacityNotPow2, ex.Code);
        }
        else
        {
            var queue = new OpQueue(capacity, 0);
            Assert.Equal(rounded, queue.Capacity);
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(9, 8)]
    public void Queue_ReserveOutsideZeroToCapacity_AssertsUnderSimAssertAndClampsOtherwise(int reserve, int clamped)
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => new OpQueue(8, reserve));
            Assert.Equal(SimAssertCode.OpQueueReserveRange, ex.Code);
        }
        else
        {
            var queue = new OpQueue(8, reserve);
            Assert.Equal(clamped, queue.SystemOpReserve);
        }
    }

    [Fact]
    public void Queue_ReserveEqualToCapacity_IsLegal_AndOnlySystemCanEnqueue()
    {
        var queue = new OpQueue(4, 4);

        Assert.Equal(OpAdmission.BusyPlayer, queue.Admit(isSystem: false));
        Assert.Equal(OpAdmission.Accepted, queue.Admit(isSystem: true));
    }

    [Fact]
    public void Builder_DefaultInitialCounters_StartOpSeqAtZero()
    {
        SimWorld world = new SimWorldBuilder().Freeze();

        Assert.Equal(0u, world.LastOpSeq);
        Assert.Equal(0u, new SimInitialCounters().LastOpSeq);
    }
}

/// <summary>
/// Zero-allocation checks for story 014 AC-3. A separate non-parallel class: runtime start-up work (tiered JIT call counting,
/// type loading) on another test's thread must not land in a measured window. <see cref="AllocAssert"/> warms each window up
/// and passes if any measured window is clean; a real per-call allocation shows up in every window.
/// </summary>
[Trait("Id", "SC-R6-1")]
[Collection(NonParallelCollection.Name)]
public class OpQueueAllocationTests
{
    private static readonly ActorId _actorA = new(0x0100_0005u);

    private static SimWorld Frozen(int capacity, int reserve) =>
        new SimWorldBuilder(new SimCoreSettings { OpQueueCapacity = capacity, SystemOpReserve = reserve }).Freeze();

    [Fact]
    public void Ac3_TenThousandEnqueueDequeueCycles_AllocateNothing()
    {
        // Arrange
        SimWorld world = Frozen(8, 2);
        Op op = new() { Kind = OpKind.TeleportAck, ReqId = 3 };
        op.SetPayload(0x0200_0009u);

        // Act + Assert (0 B)
        int runs = AllocAssert.Zero("10,000 enqueue/dequeue cycles", () =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                world.EnqueueOp(_actorA, in op);
                world.OpsForTests.TryDequeue(out _, out _);
            }
        });

        // Assert
        Assert.Equal(0, world.Ops.Count);
        Assert.Equal((uint)(10_000 * runs), world.LastOpSeq);
    }

    [Fact]
    public void Ac3_RefusedEnqueues_AllocateNothing()
    {
        // Arrange: ring of 2, reserve 0, held full so both actor classes are refused.
        SimWorld world = Frozen(2, 0);
        Op op = new() { Kind = OpKind.TeleportAck, ReqId = 1 };
        world.EnqueueOp(_actorA, in op);
        world.EnqueueOp(_actorA, in op);

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("1,000 refused player and system enqueues", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                world.EnqueueOp(_actorA, in op);
                world.EnqueueOp(ActorId.System, in op);
            }
        });

        // Assert
        Assert.Equal(2, world.Ops.Count);
        Assert.Equal(2u, world.LastOpSeq);
    }
}
