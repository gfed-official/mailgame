namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The fixed top-level steps of one client tick, in run order (ADR-0003 section 6, ADR-0016 section 2). The numeric
/// values are the order. Interaction work never gets a new top-level id: it is a sub-step of <see cref="Previews"/>.
/// </summary>
/// <example><code>builder.Bind(ClientStepId.CarrierStep, ownedCarrierStep);</code></example>
public enum ClientStepId : byte
{
    /// <summary>Events and replies handed over by the adapter are applied to the client mirrors.</summary>
    MirrorDrain = 0,

    /// <summary>The built-in <c>InputLatch</c> emits this tick's <see cref="InputFrame"/>. It latches only and never dispatches a verb.</summary>
    InputLatch = 1,

    /// <summary>The owned carrier's step (client prediction of the local carrier).</summary>
    CarrierStep = 2,

    /// <summary>Previews composite (focus, deliver and throw previews, edge resolve, hold advance). Several binds run in bind order.</summary>
    Previews = 3,
}
