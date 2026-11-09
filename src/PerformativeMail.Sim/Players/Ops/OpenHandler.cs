using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Ops;

/// <summary>
/// The range-gated <c>Open</c> handler (grid-inventory story 019; GDD R10; ADR-0009 section 2, ADR-0014 section 5), owned by Player
/// Character under the proposed ADR-0009 erratum (as the approved Drop erratum moved Drop): <see cref="Apply"/> is
/// <see cref="Validate"/>, then on 0 <see cref="Commit"/>, then the reply. The chain is
/// <c>bad_request → dead → frozen → no_target → unavailable → out_of_range / position_stale → container_gone</c>:
/// <list type="number">
/// <item><c>bad_request</c>: <see cref="IInventoryApi.ValidateOpenExternal"/>'s structural verdict (a none or never-issued container id,
/// a panel or cursor, an actor in no slot);</item>
/// <item>the P13 chain: <see cref="IPlayersApi.CheckInteraction"/> on the container's anchor (<see cref="OpenOp.AnchorOf"/>) with the
/// payload's claim, mapped by <see cref="PlayerOpReasons.Of"/>;</item>
/// <item><c>container_gone</c>: the primitive's verdict for an issued container that is no longer live (unreachable at M-1a: no container
/// is freed, and a freed container's anchor would fail <c>no_target</c> first).</item>
/// </list>
/// The commit is <see cref="IInventoryApi.CommitOpenExternal"/>: story 011's <c>TryOpenExternal</c>, which closes the slot's previous
/// external and subscribes it to this one. No record, no version, no reply extra. A reject changes nothing. Allocation-free; never
/// throws. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>builder.BindOp(OpenOp.Descriptor, new OpenHandler(players, inventory, slots));</code></example>
internal sealed class OpenHandler : IOpHandler
{
    private readonly IPlayersApi _players;
    private readonly IInventoryApi _inventory;
    private readonly ISlotView _slots;

    /// <summary>Creates the handler.</summary>
    /// <param name="players">The range gate.</param>
    /// <param name="inventory">The open-set primitives.</param>
    /// <param name="slots">The slot table (<see cref="Op.Actor"/> to slot).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public OpenHandler(IPlayersApi players, IInventoryApi inventory, ISlotView slots)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(slots);
        _players = players;
        _inventory = inventory;
        _slots = slots;
    }

    /// <inheritdoc />
    public OpKind Kind => PlayerOpKinds.Open;

    /// <inheritdoc />
    public Reply Apply(in Op op, in TickContext ctx)
    {
        ushort reason = Validate(in op, out OpenPlan plan);
        if (reason != 0)
        {
            return Reply.Reject(in op, reason);
        }

        Commit(in plan);
        return Reply.Ok(in op);
    }

    /// <summary>The pure half: walks the chain (see the class remarks) and on a pass writes the plan. Reads state only.</summary>
    /// <param name="op">The drained op.</param>
    /// <param name="plan">The slot and container to open, on a pass.</param>
    /// <returns>0, or the earliest failing reason.</returns>
    internal ushort Validate(in Op op, out OpenPlan plan)
    {
        plan = default;
        OpenPayload p = op.PayloadAs<OpenPayload>();
        var c = new ContainerId(p.Container);
        PlayerSlot slot = op.Actor.IsSystem ? PlayerSlot.None : _slots.SlotOf(new EntityId(op.Actor.Value));
        InvReason own = _inventory.ValidateOpenExternal(slot, c);
        if ((ushort)own == InvReasons.BadRequest)
        {
            return InvReasons.BadRequest;
        }

        var claim = new ClaimedPosition(p.ClaimedX, p.ClaimedY, p.ClaimedZ);
        ushort range = PlayerOpReasons.Of(_players.CheckInteraction(op.Actor, OpenOp.AnchorOf(c), in claim).Kind);
        if (range != 0)
        {
            return range;
        }

        if (own != 0)
        {
            return (ushort)own;
        }

        plan = new OpenPlan(slot, c);
        return 0;
    }

    /// <summary>The mutating half: opens the plan's container for the plan's slot. Never fails, never recomputes.</summary>
    /// <param name="plan">The plan <see cref="Validate"/> passed.</param>
    internal void Commit(in OpenPlan plan) => _inventory.CommitOpenExternal(plan.Slot, plan.Container);

    /// <summary>What <see cref="Validate"/> decided: the opening slot and the container.</summary>
    /// <param name="Slot">The actor's slot.</param>
    /// <param name="Container">The container.</param>
    internal readonly record struct OpenPlan(PlayerSlot Slot, ContainerId Container);
}
