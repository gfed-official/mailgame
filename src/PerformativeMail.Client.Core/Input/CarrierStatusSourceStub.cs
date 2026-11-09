using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// A settable <see cref="ICarrierStatusSource"/> for tests and smoke scenes that drive the router without the Players client
/// step. Starts at <c>default</c> (alive, OnFoot, cursor empty). Not thread-safe; 0 B per read.
/// </summary>
/// <example>
/// <code>
/// var carrier = new CarrierStatusSourceStub();
/// carrier.Set(new CarrierStatus { CursorHolding = true });   // a pick: Panel becomes Holding-open
/// </code>
/// </example>
public sealed class CarrierStatusSourceStub : ICarrierStatusSource
{
    private CarrierStatus _current;

    /// <inheritdoc />
    public ref readonly CarrierStatus Current => ref _current;

    /// <summary>Replaces the published status.</summary>
    /// <param name="status">The new status.</param>
    public void Set(in CarrierStatus status) => _current = status;
}
