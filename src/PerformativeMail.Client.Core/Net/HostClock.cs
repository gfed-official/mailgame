using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// The host's own <see cref="IHostClock"/> (ADR-0011 section 1): reads <see cref="SimWorld.CurrentTick"/>, so it is exact,
/// always valid, and frozen while no ticks run (a solo pause). Read by host Presentation. The host's <c>ClientLayer</c> does
/// not use it: it uses the <see cref="ClientAdapter"/> estimator like any client, which on loopback equals this clock between frames.
/// </summary>
/// <example><code>IHostClock clock = session.HostClock; uint now = clock.EstimatedHostTick;</code></example>
public sealed class HostClock : IHostClock
{
    private readonly SimWorld _world;

    /// <summary>Creates the clock over <paramref name="world"/>.</summary>
    /// <param name="world">The host's world.</param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public HostClock(SimWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <inheritdoc />
    public uint EstimatedHostTick => _world.CurrentTick;

    /// <inheritdoc />
    public bool IsValid => true;
}
