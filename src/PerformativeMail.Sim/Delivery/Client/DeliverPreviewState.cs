using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Delivery.Client;

/// <summary>
/// What <see cref="DeliverPreviewStep"/> publishes each client tick (ADR-0016 section 4 and Key Interfaces): the focused Deliver
/// destination, this tick's <see cref="DeliverBranch"/> with its payload, MD2's row, and the settle tracker's
/// <see cref="DisplayedTicks"/> and <see cref="CanStartHoldNext"/>. <c>default</c> is IDLE (no Deliver anchor focused). A plain value;
/// Presentation reads it and never derives <see cref="CanStartHoldNext"/> itself.
/// </summary>
/// <example><code>ref readonly DeliverPreviewState s = ref preview.Current; if (s.Active &amp;&amp; s.CanStartHoldNext) { /* reach echo may start */ }</code></example>
public readonly struct DeliverPreviewState
{
    /// <summary>True while a Deliver anchor is focused (PREVIEW); false is IDLE.</summary>
    public bool Active { get; init; }

    /// <summary>The client tick this state was computed on.</summary>
    public uint Tick { get; init; }

    /// <summary>The focused (or held) destination.</summary>
    public EntityId Destination { get; init; }

    /// <summary>This tick's preview branch and payload.</summary>
    public DeliverBranch Branch { get; init; }

    /// <summary>MD2's first matching row (<see cref="DeliverRow.None"/> is available).</summary>
    public DeliverRow Row { get; init; }

    /// <summary>The selected hotbar slot the preview read.</summary>
    public int SelectedSlot { get; init; }

    /// <summary>Client Sim ticks the branch key has been displayed (<c>clientTick − sinceTick</c>).</summary>
    public uint DisplayedTicks { get; init; }

    /// <summary>A hold may start on this branch now: available, and MATCH or a MISDELIVER_ONE settled for <c>branchSettleTicks</c>.</summary>
    public bool CanStartHoldNext { get; init; }

    /// <summary>The prompt's settling flag: a MISDELIVER_ONE branch shown before its settle elapsed.</summary>
    public bool Settling => Active && Branch.Kind == DeliverBranchKind.MisdeliverOne && !Branch.WontFit && !CanStartHoldNext && Row == DeliverRow.None;
}

/// <summary>The read side of <see cref="DeliverPreviewStep"/> (ADR-0016 Key Interfaces), read by Presentation.</summary>
/// <example><code>IDeliverPreviewRead read = previewStep; bool echo = read.Current.CanStartHoldNext;</code></example>
public interface IDeliverPreviewRead
{
    /// <summary>The state the last completed client tick published.</summary>
    ref readonly DeliverPreviewState Current { get; }
}
