using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// Applies <see cref="OpKind.PlayerJoin"/> (System-only; Story 022, ADR-0004 section 4). Validates fully, then mutates:
/// slot below 8 (<c>slot_invalid</c>), <see cref="SlotTable.CanAssign"/> (<c>slot_busy</c>, including a slot freed this tick),
/// player not present (<c>duplicate_player</c>; a non-zero absent id is <c>bad_request</c> until M0), then allocates the
/// <c>player</c> id, assigns the slot and calls each bound <see cref="IPlayerJoinListener"/> in bind order (none bound: nobody). Allocation-free.
/// </summary>
/// <example><code>builder.BindCoreState(ids, mail, rng, slots);   // Freeze() registers this handler</code></example>
internal sealed class PlayerJoinHandler : IOpHandler
{
    private readonly EntityIds _ids;
    private readonly SlotTable _slots;
    private readonly IPlayerJoinListener[] _listeners;

    public PlayerJoinHandler(EntityIds ids, SlotTable slots, IPlayerJoinListener[] listeners)
    {
        _ids = ids;
        _slots = slots;
        _listeners = listeners;
    }

    public OpKind Kind => OpKind.PlayerJoin;

    public Reply Apply(in Op op, in TickContext ctx)
    {
        PlayerJoinPayload p = op.PayloadAs<PlayerJoinPayload>();
        if (p.Slot >= PlayerSlot.Count)
        {
            return Reply.Reject(in op, CoreReason.SlotInvalid);
        }

        PlayerSlot slot = PlayerSlot.FromValue(p.Slot);
        if (!_slots.CanAssign(slot, ctx.Tick))
        {
            return Reply.Reject(in op, CoreReason.SlotBusy);
        }

        if (p.PlayerId != 0)
        {
            // Rejoin is M0 (SC-R21-2). Until then: present -> duplicate_player, absent -> bad_request (logged assumption).
            return Reply.Reject(in op, !_slots.SlotOf(new EntityId(p.PlayerId)).IsNone ? CoreReason.DuplicatePlayer : CoreReason.BadRequest);
        }

        EntityId player = _ids.Allocate(EntityClass.Player);
        if (player.IsNone)
        {
            return Reply.Reject(in op, CoreReason.BadRequest); // exhaustion: unreachable in v1, already asserted by Allocate
        }

        bool assigned = _slots.Assign(slot, player, ctx.Tick);
        SimAssert.That(assigned, SimAssertCode.PlayerSlotMutationFailed);
        for (int i = 0; i < _listeners.Length; i++)
        {
            _listeners[i].OnPlayerJoined(slot, player, ctx.Tick);
        }

        // Extra[0..4) = the allocated player id (u32 LE): the System op's issuer (the network adapter) learns the id from the
        // reply it receives through IEventSink.Deliver (network-adapter-loopback story 004, TD-062 item 1).
        return Reply.Ok(in op, player.Value);
    }
}

/// <summary>
/// Applies <see cref="OpKind.PlayerLeave"/> (System-only): frees the player's slot at the current tick (reassignable from the next),
/// resets the slot's sample-latch sender state (TD-049 item 1) and calls each bound listener in bind order. A player that is not present is
/// <c>bad_request</c> (nothing mutated). Allocation-free.
/// </summary>
/// <example><code>world.EnqueueOp(ActorId.System, leaveOp);   // payload PlayerLeavePayload { PlayerId }</code></example>
internal sealed class PlayerLeaveHandler : IOpHandler
{
    private readonly SlotTable _slots;
    private readonly IPlayerJoinListener[] _listeners;
    private SampleLatch? _latch;

    public PlayerLeaveHandler(SlotTable slots, IPlayerJoinListener[] listeners)
    {
        _slots = slots;
        _listeners = listeners;
    }

    public OpKind Kind => OpKind.PlayerLeave;

    /// <summary>Hands the handler the world's sample latch (created by <c>SimWorld</c> after the handlers are built).</summary>
    public void AttachLatch(SampleLatch latch) => _latch = latch;

    /// <summary>The attached sample latch (null until the world attaches it; tests check it is the world's, TD-052 item 1).</summary>
    internal SampleLatch? Latch => _latch;

    public Reply Apply(in Op op, in TickContext ctx)
    {
        PlayerLeavePayload p = op.PayloadAs<PlayerLeavePayload>();
        var player = new EntityId(p.PlayerId);
        PlayerSlot slot = _slots.SlotOf(player);
        if (slot.IsNone)
        {
            return Reply.Reject(in op, CoreReason.BadRequest);
        }

        bool freed = _slots.Free(slot, ctx.Tick);
        SimAssert.That(freed, SimAssertCode.PlayerSlotMutationFailed);
        _latch?.ResetSender(slot);
        for (int i = 0; i < _listeners.Length; i++)
        {
            _listeners[i].OnPlayerLeft(slot, player);
        }

        return Reply.Ok(in op);
    }
}
