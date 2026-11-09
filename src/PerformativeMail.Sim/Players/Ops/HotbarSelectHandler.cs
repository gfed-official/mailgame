using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Ops;

/// <summary>
/// The <c>HotbarSelect</c> handler: stores the player's selected hotbar index on the host, which Throwing's <c>ThrowRelease</c> check 2
/// reads through <see cref="IPlayersApi.SelectedHotbarIndex"/> (ADR-0015 section 1, "not the selected slot"). <see cref="Apply"/> is
/// <see cref="Validate"/>, then on 0 <see cref="Commit"/>, then the reply. <c>bad_request</c> for an index outside 0 to 7 or an actor in no
/// slot (the System actor included); no other check: the selection is not range-gated, and it is allowed while swimming
/// (<see cref="SwimVerb.HotbarSelect"/>), dead or frozen (the throw's own checks 3 to 5 refuse those). No event, no reply extra. A reject
/// changes nothing. Allocation-free; never throws. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>builder.BindOp(HotbarSelectOp.Descriptor, new HotbarSelectHandler(players, slots));</code></example>
internal sealed class HotbarSelectHandler : IOpHandler
{
    private readonly PlayersSystem _players;
    private readonly ISlotView _slots;

    /// <summary>Creates the handler.</summary>
    /// <param name="players">The module holding the per-slot selection.</param>
    /// <param name="slots">The slot table (<see cref="Op.Actor"/> to slot).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public HotbarSelectHandler(PlayersSystem players, ISlotView slots)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(slots);
        _players = players;
        _slots = slots;
    }

    /// <inheritdoc />
    public OpKind Kind => PlayerOpKinds.HotbarSelect;

    /// <inheritdoc />
    public Reply Apply(in Op op, in TickContext ctx)
    {
        ushort reason = Validate(in op, out HotbarSelectPlan plan);
        if (reason != 0)
        {
            return Reply.Reject(in op, reason);
        }

        Commit(in plan);
        return Reply.Ok(in op);
    }

    /// <summary>The pure half: the actor's slot and the index range. Reads state only.</summary>
    /// <param name="op">The drained op.</param>
    /// <param name="plan">The slot and index, on a pass.</param>
    /// <returns>0, or <see cref="CoreReason.BadRequest"/>.</returns>
    internal ushort Validate(in Op op, out HotbarSelectPlan plan)
    {
        plan = default;
        HotbarSelectPayload p = op.PayloadAs<HotbarSelectPayload>();
        PlayerSlot slot = op.Actor.IsSystem ? PlayerSlot.None : _slots.SlotOf(new EntityId(op.Actor.Value));
        if (slot.IsNone || _players.RecordRef(slot).IsEmpty || p.Index >= HotbarSelectOp.SlotCount)
        {
            return CoreReason.BadRequest;
        }

        plan = new HotbarSelectPlan(slot, (byte)p.Index);
        return 0;
    }

    /// <summary>The mutating half: stores the plan's index for the plan's slot. Never fails.</summary>
    /// <param name="plan">The plan <see cref="Validate"/> passed.</param>
    internal void Commit(in HotbarSelectPlan plan) => _players.RecordRef(plan.Slot).HotbarIndex = plan.Index;

    /// <summary>What <see cref="Validate"/> decided: the selecting slot and the index.</summary>
    /// <param name="Slot">The actor's slot.</param>
    /// <param name="Index">The hotbar index, 0 to 7.</param>
    internal readonly record struct HotbarSelectPlan(PlayerSlot Slot, byte Index);
}
