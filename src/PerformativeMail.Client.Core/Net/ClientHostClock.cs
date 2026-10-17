using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// The client's <see cref="IHostClock"/> estimator (ADR-0011 section 1; network-adapter-loopback story 006). Loopback form:
/// exact, <c>EstimatedHostTick = last batch tick + 1</c> (the host's <c>CurrentTick</c> between frames), valid after the first
/// batch, never decreasing. The remote RTT-based estimator is Networking's algorithm (M0) and replaces <see cref="Observe"/>'s
/// body, not this contract. Every tick a client puts in a request is in the host tick domain (TR-delivery-009). Main thread only.
/// </summary>
/// <example>
/// <code>
/// IHostClock clock = adapter.Clock;
/// uint holdStartTick = clock.IsValid ? clock.EstimatedHostTick : 0;
/// </code>
/// </example>
public sealed class ClientHostClock : IHostClock
{
    private uint _estimate;

    /// <inheritdoc />
    public uint EstimatedHostTick => _estimate;

    /// <inheritdoc />
    public bool IsValid { get; private set; }

    /// <summary>Folds in a received batch for host tick <paramref name="batchTick"/>; an older tick never lowers the estimate.</summary>
    /// <param name="batchTick">The tick the batch reports.</param>
    public void Observe(uint batchTick)
    {
        uint candidate = batchTick == uint.MaxValue ? batchTick : batchTick + 1;
        if (!IsValid || candidate > _estimate)
        {
            _estimate = candidate;
        }

        IsValid = true;
    }
}
