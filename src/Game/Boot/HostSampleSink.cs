using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The solo M-1a binding of the owned carrier's <see cref="ITransformSampleSink"/> (godot-host-shell story 005, player-character 020):
/// every sample goes to <see cref="SimWorld.TryWriteSample"/> for the local slot, between the client step and the world tick that
/// latches it. The one place the binding lives, so M0 swaps it for the client adapter's transform stream (ADR-0020); the Sim sees
/// only its sample ring. A write before the local player is known is refused. Allocation-free; main thread only.
/// </summary>
/// <example><code>var sink = new HostSampleSink(world, localPlayer);   // attached through ClientPorts.TransformSamples</code></example>
/// <param name="world">The host world.</param>
/// <param name="local">The local player (its slot is the sender).</param>
public sealed class HostSampleSink(SimWorld world, ILocalPlayer local) : ITransformSampleSink
{
    private readonly SimWorld _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly ILocalPlayer _local = local ?? throw new ArgumentNullException(nameof(local));

    /// <summary>Samples the world accepted.</summary>
    public long Written { get; private set; }

    /// <summary>Samples refused (ring full, world not accepting, or the local player not yet known).</summary>
    public long Refused { get; private set; }

    /// <inheritdoc />
    public bool TryWrite(in TransformSample s)
    {
        bool ok = _local.IsKnown && _world.TryWriteSample(_local.Slot, in s);
        if (ok)
        {
            Written++;
        }
        else
        {
            Refused++;
        }

        return ok;
    }
}
