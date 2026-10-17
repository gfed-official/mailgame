using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Story 020: the phase-0 drain (SC-R8-1), validate-then-mutate rejects (SC-R8-2), Core pre-checks and the OpReply append
/// (SC-R10-1), and the handler half of the actor stamp (SC-R6-1), plus the TD items folded into this story.
/// </summary>
public class OpDrainTests
{
    private static readonly OpDescriptor _plain = OpDescriptor.Empty(OpKind.TeleportAck, false, 0, 0);
    private static readonly OpDescriptor _withPayload = OpDescriptor.For<uint>(OpKind.PlayerLeave, false, 0, 4);
    private static readonly OpDescriptor _sysOnly = OpDescriptor.Empty(OpKind.FreezePlayer, true, 0, 0);

    private static Op Make(OpKind kind, uint reqId) => new() { Kind = kind, ReqId = reqId };

    private static Op Plain(uint reqId) => Make(OpKind.TeleportAck, reqId);

    private static LambdaHandler OkHandler(OpKind kind) => new(kind, (in Op op, in TickContext _) => Reply.Ok(in op));

    // ------------------------------------------------------------ SC-R8-1

    [Fact]
    [Trait("Id", "SC-R8-1")]
    public void HundredQueued_CapSixtyFour_AppliesFirst64InOrder_ThenRemaining36()
    {
        var applied = new List<uint>();
        var handler = new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) =>
        {
            applied.Add(op.ReqId);
            return Reply.Ok(in op);
        });
        var rig = new DrainRig(b => b.BindOp(_plain, handler));
        for (uint i = 1; i <= 100; i++)
        {
            Assert.True(rig.World.EnqueueOp(DrainRig.PlayerA, Plain(i)));
        }

        rig.World.Tick();

        Assert.Equal(Enumerable.Range(1, 64).Select(i => (uint)i), applied);
        Assert.Equal(36L, rig.Counters[CounterId.DrainBacklog]);
        Assert.Equal(36, rig.World.Ops.Count);
        Assert.Equal(65u, rig.World.Ops.PeekAt(0).OpSeq);

        rig.World.Tick();

        Assert.Equal(Enumerable.Range(1, 100).Select(i => (uint)i), applied);
        Assert.Equal(0L, rig.Counters[CounterId.DrainBacklog]);
        Assert.Equal(100, rig.Recorder.Drained.Count);
    }

    [Fact]
    [Trait("Id", "SC-R8-1")]
    public void HookedOp_IsAccepted_ButAppliesAfterEarlierOps_OnALaterTick()
    {
        var applied = new List<uint>();
        DrainRig? rig = null;
        bool hookAccepted = false;
        var handler = new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) =>
        {
            applied.Add(op.ReqId);
            if (op.ReqId == 10)
            {
                hookAccepted = rig!.World.EnqueueOp(DrainRig.PlayerA, Plain(1000));
            }

            return Reply.Ok(in op);
        });
        rig = new DrainRig(b => b.BindOp(_plain, handler));
        for (uint i = 1; i <= 100; i++)
        {
            rig.World.EnqueueOp(DrainRig.PlayerA, Plain(i));
        }

        rig.World.Tick();

        Assert.True(hookAccepted);
        Assert.DoesNotContain(1000u, applied);
        Assert.Equal(64, applied.Count);
        Assert.Equal(37L, rig.Counters[CounterId.DrainBacklog]);

        rig.World.Tick();

        Assert.Equal(1000u, applied[^1]);
        Assert.Equal(65u, applied[64]);
        Assert.Equal(101, applied.Count);
        Assert.Equal(1u, rig.Recorder.Drained.Single(d => d.Op.ReqId == 1000).Tick);
    }

    [Fact]
    [Trait("Id", "SC-R8-1")]
    public void OpEnqueuedDuringDrain_IsNotAppliedThatTick_EvenWhenUnderTheCap()
    {
        var applied = new List<uint>();
        DrainRig? rig = null;
        var handler = new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) =>
        {
            applied.Add(op.ReqId);
            if (op.ReqId == 1)
            {
                rig!.World.EnqueueOp(DrainRig.PlayerA, Plain(99));
            }

            return Reply.Ok(in op);
        });
        rig = new DrainRig(b => b.BindOp(_plain, handler));
        for (uint i = 1; i <= 3; i++)
        {
            rig.World.EnqueueOp(DrainRig.PlayerA, Plain(i));
        }

        rig.World.Tick();

        Assert.Equal(new uint[] { 1, 2, 3 }, applied);
        Assert.Equal(1, rig.World.Ops.Count);
        rig.World.Tick();
        Assert.Equal(new uint[] { 1, 2, 3, 99 }, applied);
    }

    [Fact]
    [Trait("Id", "SC-R8-1")]
    public void SystemOps_GetNoBypassOfTheCap()
    {
        var applied = new List<uint>();
        var handler = new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) =>
        {
            applied.Add(op.ReqId);
            return Reply.Ok(in op);
        });
        var rig = new DrainRig(b => b.BindOp(_plain, handler), maxDrain: 4);
        for (uint i = 1; i <= 4; i++)
        {
            rig.World.EnqueueOp(DrainRig.PlayerA, Plain(i));
        }

        rig.World.EnqueueOp(ActorId.System, Plain(50));
        rig.World.Tick();

        Assert.Equal(new uint[] { 1, 2, 3, 4 }, applied);
        Assert.Equal(1L, rig.Counters[CounterId.DrainBacklog]);
        rig.World.Tick();
        Assert.Equal(50u, applied[^1]);
        Assert.Equal(0L, rig.Counters[CounterId.DrainBacklog]);
    }

    [Fact]
    [Trait("Id", "SC-R6-1")]
    public void Handler_SeesTheEnqueueActor_NotTheClaimedOneAndTheDrainTickAndPhase()
    {
        ActorId seen = default;
        TickContext seenCtx = default;
        var handler = new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext ctx) =>
        {
            seen = op.Actor;
            seenCtx = ctx;
            return Reply.Ok(in op);
        });
        var rig = new DrainRig(b => b.BindOp(_plain, handler));
        rig.World.Tick();
        Op claimed = Plain(1);
        claimed.Actor = DrainRig.PlayerB;

        rig.World.EnqueueOp(DrainRig.PlayerA, in claimed);
        rig.World.Tick();

        Assert.Equal(DrainRig.PlayerA, seen);
        Assert.Equal(1u, seenCtx.Tick);
        Assert.Equal(PhaseId.InputsApply, seenCtx.Phase);
        Assert.Equal(DrainRig.PlayerA, rig.Recorder.Drained.Single().Op.Actor);
    }

    // ------------------------------------------------------------ SC-R8-2

    [Fact]
    [Trait("Id", "SC-R8-2")]
    public void RejectingHandler_ChangesNothing_OnlyAnOpReplyIsLogged()
    {
        var handler = new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) => Reply.Reject(in op, CoreReason.BadRequest));
        DrainRig rig = RejectHarness.Run((b, _) => b.BindOp(_plain, handler), Plain(7), DrainRig.PlayerA, CoreReason.BadRequest);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1u, rig.World.LastOpSeq);
    }

    [Fact]
    [Trait("Id", "SC-R8-2")]
    public void CoreRejects_RunThroughTheSameHarness_WithoutCallingTheHandler()
    {
        var handler = OkHandler(OpKind.TeleportAck);
        RejectHarness.Run((b, _) => b.BindOp(_plain, handler), Make(DrainRig.UnregisteredKind, 3), DrainRig.PlayerA, CoreReason.UnknownKind);
        RejectHarness.Run((b, _) => b.BindOp(_plain, handler), Plain(4), DrainRig.Stranger, CoreReason.UnknownActor);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    [Trait("Id", "SC-R8-2")]
    public void Harness_CatchesAHandlerThatDrawsRngBeforeRejecting()
    {
        Assert.ThrowsAny<Exception>(() => RejectHarness.Run(
            (b, rig) => b.BindOp(_plain, new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) =>
            {
                rig().Stream.Next32();
                return Reply.Reject(in op, CoreReason.BadRequest);
            })),
            Plain(1),
            DrainRig.PlayerA,
            CoreReason.BadRequest));
    }

    // ------------------------------------------------------------ SC-R10-1

    [Fact]
    [Trait("Id", "SC-R10-1")]
    public void CommittingHandler_LogsEventEventThenOwnerOnlyReply_WithReqIdEchoed()
    {
        DrainRig? rig = null;
        var handler = new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) =>
        {
            rig!.Raise(11);
            rig.Raise(22);
            return Reply.Ok(in op);
        });
        rig = new DrainRig(b => b.BindOp(_plain, handler));
        rig.World.EnqueueOp(DrainRig.PlayerB, Plain(0xA5A5_0001u));

        rig.World.Tick();

        EventEntry[] log = rig.Sink.Last;
        Assert.Equal(3, log.Length);
        Assert.Equal(DrainRig.TestEvent, log[0].Kind);
        Assert.Equal(DrainRig.TestEvent, log[1].Kind);
        Assert.Equal(EventKind.OpReply, log[2].Kind);
        Assert.Equal(log[0].CallSeq, log[1].CallSeq);
        Assert.NotEqual(0u, log[0].CallSeq);
        Assert.Equal(log[0].CallSeq, log[2].CallSeq); // TD-048 item 1: the reply carries the CallSeq of the call it answers
        Assert.Equal(ReplicationClass.OwnerOnly, log[2].Class);
        Assert.Equal((byte)(1 << 3), log[2].Audience);
        Reply reply = log[2].PayloadAs<Reply>();
        Assert.Equal(0, reply.Reason);
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.Equal(0xA5A5_0001u, reply.ReqId);
        Assert.Equal(OpKind.TeleportAck, reply.Kind);
        Assert.Equal(Reply.SizeBytes, log[2].PayloadLength);
    }

    [Fact]
    [Trait("Id", "SC-R10-1")]
    public void OpReply_CarriesItsHandlerScopesCallSeq_PreCheckRejectCarriesZero()
    {
        // Arrange: three ops in one tick: a handler that raises one event, a handler that raises nothing, an unregistered kind.
        DrainRig? rig = null;
        var raising = new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) =>
        {
            rig!.Raise(7);
            return Reply.Ok(in op);
        });
        rig = new DrainRig(b =>
        {
            b.BindOp(_plain, raising);
            b.BindOp(_withPayload, OkHandler(OpKind.PlayerLeave));
        });
        var leave = new Op { Kind = OpKind.PlayerLeave, ReqId = 2 };
        leave.SetPayload(5u);
        rig.World.EnqueueOp(DrainRig.PlayerA, Plain(1));
        rig.World.EnqueueOp(DrainRig.PlayerA, in leave);
        rig.World.EnqueueOp(DrainRig.PlayerA, Make(DrainRig.UnregisteredKind, 3));

        // Act
        rig.World.Tick();

        // Assert: event(1), reply(1); reply(2) of its own empty scope; reply(3) with CallSeq 0 (no scope opened).
        EventEntry[] log = rig.Sink.Last;
        Assert.Equal(4, log.Length);
        Assert.Equal(DrainRig.TestEvent, log[0].Kind);
        Assert.Equal(EventKind.OpReply, log[1].Kind);
        Assert.Equal(log[0].CallSeq, log[1].CallSeq);
        Assert.NotEqual(0u, log[2].CallSeq);
        Assert.NotEqual(log[1].CallSeq, log[2].CallSeq);
        Assert.Equal(CoreReason.UnknownKind, log[3].PayloadAs<Reply>().Reason);
        Assert.Equal(0u, log[3].CallSeq);
        Assert.Equal(0u, rig.World.Events.CurrentCallSeq);
    }

    [Fact]
    [Trait("Id", "SC-R10-1")]
    public void UnregisteredKind_RepliesUnknownKind_AndNoHandlerRuns()
    {
        var handler = OkHandler(OpKind.TeleportAck);
        var rig = new DrainRig(b => b.BindOp(_plain, handler));
        rig.World.EnqueueOp(DrainRig.PlayerA, Make(DrainRig.UnregisteredKind, 9));

        rig.World.Tick();

        Reply reply = Assert.Single(rig.Sink.Last).PayloadAs<Reply>();
        Assert.Equal(CoreReason.UnknownKind, reply.Reason);
        Assert.InRange(reply.Reason, 1, 63);
        Assert.Equal(9u, reply.ReqId);
        Assert.Equal(0, handler.Calls);
        Assert.Single(rig.Recorder.Drained);
    }

    [Fact]
    [Trait("Id", "SC-R10-1")]
    public void WrongPayloadLength_AndNonZeroReserved_AreUnknownKind()
    {
        var handler = OkHandler(OpKind.PlayerLeave);
        var rig = new DrainRig(b => b.BindOp(_withPayload, handler));
        Op noPayload = Make(OpKind.PlayerLeave, 1); // PayloadLength 0, descriptor says 4
        Op reserved = Make(OpKind.PlayerLeave, 2);
        reserved.SetPayload(5u);
        reserved.Reserved = 1;
        Op good = Make(OpKind.PlayerLeave, 3);
        good.SetPayload(5u);
        rig.World.EnqueueOp(DrainRig.PlayerA, in noPayload);
        rig.World.EnqueueOp(DrainRig.PlayerA, in reserved);
        rig.World.EnqueueOp(DrainRig.PlayerA, in good);

        rig.World.Tick();

        EventEntry[] log = rig.Sink.Last;
        Assert.Equal(CoreReason.UnknownKind, log[0].PayloadAs<Reply>().Reason);
        Assert.Equal(CoreReason.UnknownKind, log[1].PayloadAs<Reply>().Reason);
        Assert.Equal(0, log[2].PayloadAs<Reply>().Reason);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    [Trait("Id", "SC-R10-1")]
    public void NonPresentActor_AndPlayerOnSystemOnlyKind_AreUnknownActor()
    {
        var plain = OkHandler(OpKind.TeleportAck);
        var sys = OkHandler(OpKind.FreezePlayer);
        var rig = new DrainRig(b =>
        {
            b.BindOp(_plain, plain);
            b.BindOp(_sysOnly, sys);
        });
        rig.World.EnqueueOp(DrainRig.Stranger, Plain(1));
        rig.World.EnqueueOp(DrainRig.PlayerA, Make(OpKind.FreezePlayer, 2));

        rig.World.Tick();

        EventEntry[] log = rig.Sink.Last;
        Assert.Equal(CoreReason.UnknownActor, log[0].PayloadAs<Reply>().Reason);
        Assert.Equal(0, log[0].Audience);
        Assert.Equal(CoreReason.UnknownActor, log[1].PayloadAs<Reply>().Reason);
        Assert.Equal(1, log[1].Audience);
        Assert.Equal(0, plain.Calls + sys.Calls);
    }

    [Fact]
    [Trait("Id", "SC-R10-1")]
    public void PreCheckOrder_KindAndLengthBeforeActor()
    {
        var rig = new DrainRig(b => b.BindOp(_withPayload, OkHandler(OpKind.PlayerLeave)));
        rig.World.EnqueueOp(DrainRig.Stranger, Make(DrainRig.UnregisteredKind, 1));
        rig.World.EnqueueOp(DrainRig.Stranger, Make(OpKind.PlayerLeave, 2)); // wrong length, stranger

        rig.World.Tick();

        Assert.All(rig.Sink.Last, e => Assert.Equal(CoreReason.UnknownKind, e.PayloadAs<Reply>().Reason));
    }

    [Fact]
    [Trait("Id", "SC-R10-1")]
    public void SystemOp_AppliesOnSystemOnlyKind_WithAudienceZero()
    {
        ActorId seen = default;
        var sys = new LambdaHandler(OpKind.FreezePlayer, (in Op op, in TickContext _) =>
        {
            seen = op.Actor;
            return Reply.Ok(in op);
        });
        var rig = new DrainRig(b => b.BindOp(_sysOnly, sys));
        rig.World.EnqueueOp(ActorId.System, Make(OpKind.FreezePlayer, 5));

        rig.World.Tick();

        EventEntry entry = Assert.Single(rig.Sink.Last);
        Assert.Equal(0, entry.Audience);
        Assert.Equal(ReplicationClass.OwnerOnly, entry.Class);
        Assert.Equal(0, entry.PayloadAs<Reply>().Reason);
        Assert.True(seen.IsSystem);
    }

    [Fact]
    [Trait("Id", "SC-R10-1")]
    public void SameReqIdTwice_BothApply_NoDedupe()
    {
        var handler = OkHandler(OpKind.TeleportAck);
        var rig = new DrainRig(b => b.BindOp(_plain, handler));
        rig.World.EnqueueOp(DrainRig.PlayerA, Plain(77));
        rig.World.EnqueueOp(DrainRig.PlayerA, Plain(77));

        rig.World.Tick();

        Assert.Equal(2, handler.Calls);
        Assert.Equal(2, rig.Sink.Last.Length);
    }

    [Fact]
    [Trait("Id", "SC-R10-1")]
    public void InconsistentReply_AssertsUnderSimAssert_AndIsRepairedInRelease()
    {
        var bad = new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) =>
        {
            Reply r = Reply.Ok(in op);
            r.Reason = 64;
            return r; // Result Ok, Reason 64
        });
        var rig = new DrainRig(b => b.BindOp(_plain, bad));
        rig.World.EnqueueOp(DrainRig.PlayerA, Plain(1));

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.Throws<SimAssertException>(rig.World.Tick);
            Assert.Equal(SimAssertCode.ReplyResultReasonMismatch, ex.Code);
        }
        else
        {
            rig.World.Tick();
            Reply reply = Assert.Single(rig.Sink.Last).PayloadAs<Reply>();
            Assert.Equal(ReplyResult.Rejected, reply.Result);
            Assert.Equal(64, reply.Reason);
            Assert.Equal(1L, rig.Counters[CounterId.ReplyInconsistent]);
            Assert.Equal(ReplyResult.Rejected, rig.Recorder.Drained.Single().Reply.Result);
        }
    }

    // ------------------------------------------------------------ folded tech debt

    [Fact]
    public void Td042_BindOp_WithAMismatchedHandlerKind_ThrowsHandlerKindMismatch_AndDeclaresNothing()
    {
        var builder = new SimWorldBuilder();
        var ex = Assert.Throws<SimBuildException>(() => builder.BindOp(_plain, OkHandler(OpKind.PlayerLeave)));
        Assert.Equal(SimBuildErrorCode.HandlerKindMismatch, ex.Code);
        Assert.Equal(23, (int)SimBuildErrorCode.HandlerKindMismatch);

        builder.Freeze(); // nothing was left declared without a handler
    }

    [Fact]
    public void Td031_WrongThreadCode_IsNeutral_AndNotTheTickCode()
    {
        Assert.Equal(47, SimAssertCode.WrongThread);
        Assert.NotEqual(SimAssertCode.TickWrongThread, SimAssertCode.WrongThread);
    }

    [Fact]
    public void Td031_StoppedWorld_WithAFullRing_RefusesAsBadState_NotBusy()
    {
        var settings = new SimCoreSettings { OpQueueCapacity = 2, SystemOpReserve = 0, MaxOpsDrainedPerTick = 0 };
        SimWorld world = new SimWorldBuilder(settings).Freeze();
        Op op = Plain(1);
        world.EnqueueOp(DrainRig.PlayerA, in op);
        world.EnqueueOp(DrainRig.PlayerA, in op);
        world.Tick();
        Assert.True(world.Stop());
        Assert.Equal(2, world.Ops.Count);

        Assert.False(world.EnqueueOp(DrainRig.PlayerA, in op));
        Assert.False(world.EnqueueOp(ActorId.System, in op));

        Assert.Equal(2L, world.Counters[CounterId.EnqueueBadState]);
        Assert.Equal(0L, world.Counters[CounterId.BusyPlayer]);
        Assert.Equal(0L, world.Counters[CounterId.BusySystem]);
    }

    [Fact]
    public void Td031_OpsView_ExposesNoWriters()
    {
        string[] names = typeof(OpQueueView).GetMembers().Select(m => m.Name).ToArray();
        Assert.DoesNotContain("Push", names);
        Assert.DoesNotContain("TryDequeue", names);
        Assert.DoesNotContain("DropFront", names);
    }
}

/// <summary>Story 020 guardrail: the drain loop allocates 0 B once warm (optimised builds only, TD-008).</summary>
[Collection(NonParallelCollection.Name)]
public class OpDrainAllocationTests
{
    [Fact]
    [Trait("Id", "SC-R8-1")]
    public void Drain_OfRejectsAndCommits_AllocatesNothing_InOptimisedBuilds()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        OpDescriptor plain = OpDescriptor.Empty(OpKind.TeleportAck, false, 0, 0);
        var handler = new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) => Reply.Ok(in op));
        var rig = new DrainRig(b => b.BindOp(plain, handler), opCapacity: 128, instrument: false);
        Op ok = new() { Kind = OpKind.TeleportAck, ReqId = 1 };
        Op bad = new() { Kind = DrainRig.UnregisteredKind, ReqId = 2 };

        int runs = AllocAssert.Zero(
            "drain of 32 commits and 32 core rejects",
            () =>
            {
                for (int i = 0; i < 32; i++)
                {
                    rig.World.EnqueueOp(DrainRig.PlayerA, in ok);
                    rig.World.EnqueueOp(DrainRig.PlayerA, in bad);
                }

                rig.World.Tick();
            });

        Assert.True(runs > 0);
        Assert.Equal(0, rig.World.Ops.Count);
        Assert.True(handler.Calls >= 32);
    }
}
