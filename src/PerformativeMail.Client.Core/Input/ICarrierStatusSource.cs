using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The ClientLayer-published carrier status the router reads its <c>body</c> and <c>holding</c> layers from (ADR-0012 section 2;
/// ADR-0014 section 7). The Players client step publishes it once per client tick (Player Character story 020); the router reads
/// it at event time and at every <see cref="InputRouter.FlushToLatch"/> and never writes it. <see cref="CarrierStatus.Aiming"/>
/// is ignored: aiming is router-owned.
/// </summary>
/// <example><code>BodyContext body = source.Current.Body;</code></example>
public interface ICarrierStatusSource
{
    /// <summary>The latest published status.</summary>
    ref readonly CarrierStatus Current { get; }
}
