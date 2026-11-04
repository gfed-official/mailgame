namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The preallocated candidate buffers one <see cref="ReceiverViewBuilder.Build"/> call fills (ADR-0015 Implementation
/// Guidelines: fixed capacity, overflow counted, never grown). Heap-backed, so the returned <see cref="ReceiverView"/>'s spans
/// outlive the call; they stay valid until the next build with the same scratch. Each span holds
/// <see cref="ThrowSettings.MaxReceiverCandidates"/> entries. One per caller (host step, preview); not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var scratch = new ReceiverScratch();   // once, at construction
/// ReceiverView view = builder.Build(in p0, in p1, ReceiverMask.All, ranks, catchers, scratch);
/// </code>
/// </example>
public sealed class ReceiverScratch
{
    /// <summary>Creates the buffers.</summary>
    public ReceiverScratch()
    {
        Indices = new ushort[ThrowSettings.MaxReceiverCandidates];
        Mailboxes = new VolumeReceiver[ThrowSettings.MaxReceiverCandidates];
        Solids = new SolidReceiver[ThrowSettings.MaxReceiverCandidates];
    }

    /// <summary>Query indices (grid ordinals, then world-solid indices).</summary>
    internal ushort[] Indices { get; }

    /// <summary>The gathered mailbox volumes.</summary>
    internal VolumeReceiver[] Mailboxes { get; }

    /// <summary>The gathered world solids.</summary>
    internal SolidReceiver[] Solids { get; }
}
