using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// The local carrier facts <see cref="FocusStep"/> reads each client tick (D1, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead
/// chat), TD-164; ADR-0016 l.238: client steps read only mirrors or ports, never <c>SimWorld</c>). Sim-defined; tests supply a
/// scripted double and story 020's <c>CarrierView</c> implements it from the owned carrier after <c>ClientStepId.CarrierStep</c>.
/// </summary>
/// <example>
/// <code>
/// sealed class Source : ICarrierFocusSource
/// {
///     private CarrierStatus _status;
///     public Vec3Mm Feet { get; set; }
///     public ref readonly CarrierStatus Status => ref _status;
/// }
/// </code>
/// </example>
public interface ICarrierFocusSource
{
    /// <summary>The carrier's feet this tick, mm (the chest is <c>Feet + (0, chestMm, 0)</c>).</summary>
    Vec3Mm Feet { get; }

    /// <summary>The carrier's status this tick (body context, panel, swimming, occupied seat).</summary>
    ref readonly CarrierStatus Status { get; }
}
