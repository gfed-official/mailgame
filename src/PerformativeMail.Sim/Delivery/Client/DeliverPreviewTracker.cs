using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery.Client;

/// <summary>
/// The settle tracker (Manual Delivery MD3, MD4; TR-delivery-004; ADR-0016 section 4). State <c>(BranchKey key, uint sinceTick)</c>
/// with <c>BranchKey = (branch kind, destinationId, selectedEntryId if MISDELIVER_ONE)</c>; <c>displayedTicks = clientTick − sinceTick</c>
/// in client Sim ticks. A key change restarts it; a tick on which MD2 rows 1 to 5 apply, or IDLE, clears the key, so the next available
/// tick is a key change; a payload change (cents, <c>k</c>, <c>n</c>, <c>floorRefusal</c>, <c>cursed</c>) is not. MATCH needs no
/// settle. A value type owned by <see cref="DeliverPreviewStep"/>; 0 B.
/// </summary>
/// <example>
/// <code>
/// var tracker = new DeliverPreviewTracker(branchSettleTicks: 6);
/// tracker.Update(clientTick, dest, in branch, DeliverRow.None, out uint displayed, out bool canStart);
/// </code>
/// </example>
public struct DeliverPreviewTracker
{
    private readonly uint _settleTicks;
    private bool _hasKey;
    private DeliverBranchKind _kind;
    private EntityId _destination;
    private EntryId _selected;
    private uint _sinceTick;

    /// <summary>Creates the tracker.</summary>
    /// <param name="branchSettleTicks">GDD F5 <c>branchSettleTicks</c> (6, PROPOSED); negative reads as 0.</param>
    public DeliverPreviewTracker(int branchSettleTicks)
    {
        _settleTicks = (uint)Math.Max(branchSettleTicks, 0);
        _hasKey = false;
        _kind = default;
        _destination = default;
        _selected = default;
        _sinceTick = 0;
    }

    /// <summary>Clears the key (IDLE): the next <see cref="Update"/> starts a new display.</summary>
    public void Reset() => _hasKey = false;

    /// <summary>Advances the tracker for one client tick.</summary>
    /// <param name="clientTick">The client tick.</param>
    /// <param name="destination">The previewed destination.</param>
    /// <param name="branch">This tick's branch.</param>
    /// <param name="row">This tick's MD2 row.</param>
    /// <param name="displayedTicks">Ticks the key has been displayed (0 on a reset tick).</param>
    /// <param name="canStartHoldNext">A hold may start on this branch now.</param>
    public void Update(uint clientTick, EntityId destination, in DeliverBranch branch, DeliverRow row, out uint displayedTicks, out bool canStartHoldNext)
    {
        if (DeliverAvailability.BlocksVerb(row))
        {
            _hasKey = false;
            displayedTicks = 0;
            canStartHoldNext = false;
            return;
        }

        EntryId selected = branch.Kind == DeliverBranchKind.MisdeliverOne ? branch.SelectedEntry : EntryId.None;
        if (!_hasKey || _kind != branch.Kind || _destination != destination || _selected != selected)
        {
            _hasKey = true;
            _kind = branch.Kind;
            _destination = destination;
            _selected = selected;
            _sinceTick = clientTick;
        }

        displayedTicks = unchecked(clientTick - _sinceTick);
        canStartHoldNext = branch.Kind switch
        {
            DeliverBranchKind.Match => row == DeliverRow.None,
            DeliverBranchKind.MisdeliverOne => row == DeliverRow.None && !branch.WontFit && displayedTicks >= _settleTicks,
            _ => false,
        };
    }
}
