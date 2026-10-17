using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Composition;

/// <summary>
/// The tick reader and world-state reader handed to Sim Core services and systems at construction, before the
/// <see cref="SimWorld"/> exists (ADR-0004 section 3). Until <see cref="Attach"/> it reports a <see cref="SimState.Constructed"/>
/// world between ticks at tick 0 (so <c>Destination</c>/<c>Prop</c> ids may be allocated at world build); afterwards it forwards
/// to the world. It is a Sim Core service, not a system. Not thread-safe.
/// </summary>
internal sealed class WorldTickForwarder : ITickReader, IWorldStateReader
{
    private SimWorld? _world;

    public uint CurrentTick => _world?.CurrentTick ?? 0;

    public TickStage Stage => _world?.Stage ?? TickStage.BetweenTicks;

    public PhaseId Phase => _world?.Phase ?? PhaseId.InputsApply;

    public SimState State => _world?.State ?? SimState.Constructed;

    public void Attach(SimWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }
}

/// <summary>
/// The <see cref="IRunControl"/> handed to Run Structure at construction; forwards to the world's stop port once attached.
/// A stop requested before <see cref="SimWorldBuilder.Freeze"/> (from a constructor or <c>Bind</c>) is a composition bug and throws.
/// </summary>
internal sealed class RunControlForwarder : IRunControl
{
    private IRunControl? _target;

    public void RequestStop(StopReason reason)
    {
        if (_target is null)
        {
            throw new InvalidOperationException("RequestStop before the world is frozen; Run Structure may stop the run only from a phase.");
        }

        _target.RequestStop(reason);
    }

    public void Attach(IRunControl target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
    }
}

/// <summary>The client layer's clock when the host supplied none: never valid, estimate 0.</summary>
internal sealed class UnsyncedHostClock : IHostClock
{
    public uint EstimatedHostTick => 0;

    public bool IsValid => false;
}
