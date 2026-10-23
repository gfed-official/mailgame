using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The ClientLayer's request port (ADR-0016 section 1 and Key Interfaces): edge resolution (<see cref="PreviewSubStep.EdgeResolve"/>)
/// and hold completion send owner-built requests through it. The adapter assigns the <c>reqId</c>. Declared by player-character
/// story 016; the production adapter over <c>ClientAdapter</c> and <c>ClientLayer.Attach</c> are godot-host-shell story 005's.
/// Implementations must not allocate.
/// </summary>
/// <example><code>if (!requests.TrySend(in op)) { /* busy: nothing was sent */ }</code></example>
public interface IRequestSink
{
    /// <summary>Sends one request.</summary>
    /// <param name="op">The request; the sink copies it.</param>
    /// <returns>True when sent; false is the local "busy" (no op sent).</returns>
    bool TrySend(in Op op);
}
