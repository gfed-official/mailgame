using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

public sealed partial class SimWorld
{
    /// <summary>
    /// Phase 0 built-in op drain (Story 020, ADR-0007 sections 3 and 5), run before phase 0's binders (which are forbidden, so it
    /// is the whole of phase 0). Captures <c>n = min(Count, MaxOpsDrainedPerTick)</c> up front and applies those ops in ascending
    /// <c>opSeq</c>; ops enqueued meanwhile wait for a later tick and ops beyond the cap stay queued. Per op: Sim Core
    /// pre-checks (unknown kind, wrong length or reserved byte, unknown actor, System-only kind), then a call scope around
    /// <see cref="IOpHandler.Apply"/>, then the reply is appended as an <see cref="EventKind.OpReply"/> after every event the
    /// call raised (outside the handler's scope) and <see cref="IOpRecorder.OnDrained"/> is called. Finally
    /// <see cref="CounterId.DrainBacklog"/> is set to the queue length (a level). Allocation-free.
    /// </summary>
    private void InputsDrain()
    {
        OpQueue queue = _opQueue;
        _drainBegun = true; // an enqueue from here to the end of the tick waits for the next tick's drain (recorder boundary tick)
        int n = Math.Min(queue.Count, Math.Max(0, Settings.MaxOpsDrainedPerTick));
        uint tick = CurrentTick;
        var ctx = new TickContext(tick, PhaseId.InputsApply);
        for (int i = 0; i < n; i++)
        {
            // The head is peeked by reference and popped only after the op is fully answered, so a handler that enqueues cannot
            // reuse this slot while the reference is live.
            ref readonly Op op = ref queue.PeekAt(0);
            Reply reply = DrainOne(in op, in ctx, out byte audience, out uint callSeq);
            NormaliseReply(ref reply);

            // After every event the call raised, outside its scope, but stamped with that scope's CallSeq (TD-048 item 1).
            _log.TryRaiseWithCallSeq(EventKind.OpReply, audience, in reply, callSeq);
            _recorder.OnDrained(tick, in op, in reply);
            queue.DropFront(1);
        }

        Counters.Set(CounterId.DrainBacklog, queue.Count);
        CloseLeakedScopes();
    }

    // Runs the pre-checks and, when they pass, the handler inside its own call scope. The audience is resolved BEFORE the
    // handler runs, so a PlayerLeave op still replies to the slot the actor held when it was issued. callSeq is the handler's
    // call scope (0 when a pre-check rejects the op and no scope opens).
    private Reply DrainOne(in Op op, in TickContext ctx, out byte audience, out uint callSeq)
    {
        audience = 0;
        callSeq = 0;
        bool isSystem = op.Actor.IsSystem;
        PlayerSlot slot = isSystem || _slots is null ? PlayerSlot.None : _slots.SlotOf(new EntityId(op.Actor.Value));
        if (!slot.IsNone)
        {
            audience = (byte)(1 << slot.Value);
        }

        if (!TryGetOp(op.Kind, out OpDescriptor descriptor, out IOpHandler handler)
            || op.PayloadLength != descriptor.PayloadLength
            || op.Reserved != 0)
        {
            return Reply.Reject(in op, CoreReason.UnknownKind);
        }

        if (!isSystem && slot.IsNone)
        {
            return Reply.Reject(in op, CoreReason.UnknownActor);
        }

        if (descriptor.SystemOnly && !isSystem)
        {
            return Reply.Reject(in op, CoreReason.UnknownActor);
        }

        using (_log.BeginCall())
        {
            callSeq = _log.CurrentCallSeq;
            return handler.Apply(in op, in ctx);
        }
    }

    // Checks Ok iff Reason == 0 (TD-020 item 2). SIM_ASSERT builds assert. Release counts ReplyInconsistent, lets Reason win
    // (IsOk reads Reason) and still appends, so a client never waits on a missing reply; the recorder sees the repaired reply.
    private void NormaliseReply(ref Reply reply)
    {
        bool consistent = (reply.Result == ReplyResult.Ok) == (reply.Reason == 0);
        SimAssert.That(consistent, SimAssertCode.ReplyResultReasonMismatch);
        if (!consistent)
        {
            Counters.Add(CounterId.ReplyInconsistent);
            reply.Result = reply.Reason == 0 ? ReplyResult.Ok : ReplyResult.Rejected;
        }
    }
}
