using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// Where the owned carrier's step reports its authoritative transform (ADR-0013 section 1, ADR-0016 section 1; player-character story
/// 020, PROVISIONAL, additive). The host shell binds it to <c>SimWorld.TryWriteSample</c> for the local slot in solo M-1a
/// (godot-host-shell story 005, one adapter class); a remote client binds it to the client adapter's transform stream at M0
/// (ADR-0020). Declared in the ClientLayer ports beside <see cref="IRequestSink"/> so <see cref="ClientPorts"/> can carry it.
/// Implementations must not allocate.
/// </summary>
/// <example><code>if (!sink.TryWrite(in sample)) { refused++; }   // never retried within the tick</code></example>
public interface ITransformSampleSink
{
    /// <summary>Writes one sample.</summary>
    /// <param name="s">The sample; the sink copies it.</param>
    /// <returns>False when the sample was refused (for example the ring is full); the caller counts it and does not retry.</returns>
    bool TryWrite(in TransformSample s);
}
