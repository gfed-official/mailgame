using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Delivery.Client;

/// <summary>
/// The client facts <see cref="DeliverPreviewStep"/> reads that no mirror part carries yet (ADR-0016 l.238: client steps read only
/// mirrors or ports). Sim-defined; tests supply a scripted double. The selected hotbar slot has no client seam yet (the
/// <c>HotbarSelect</c> op and the hotbar strip are WP-D's); the pending set is the <c>ClientAdapter</c>'s in-flight own requests until
/// the M0 <c>PendingQueue</c> (manual-delivery story 003 notes); the current shift is not in <c>RunMirror</c>. Implementations must
/// not allocate.
/// </summary>
/// <example>
/// <code>
/// sealed class Source : IDeliverClientSource
/// {
///     public int SelectedSlot { get; set; }
///     public int CurrentShift { get; set; } = 1;
///     public bool IsPending(OpKind kind) =&gt; false;
/// }
/// </code>
/// </example>
public interface IDeliverClientSource
{
    /// <summary>The selected hotbar slot (0 is hands).</summary>
    int SelectedSlot { get; }

    /// <summary>The client's current shift (F5 timeliness input for the predicted pay).</summary>
    int CurrentShift { get; }

    /// <summary>True while an own request of <paramref name="kind"/> awaits its reply (row 3 is derived from this, never stored).</summary>
    /// <param name="kind">The op kind.</param>
    /// <returns>True when pending.</returns>
    bool IsPending(OpKind kind);
}
