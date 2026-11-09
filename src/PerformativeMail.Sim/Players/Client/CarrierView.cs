using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// The published owned carrier (player-character story 020): written only by <see cref="CarrierStepBinder"/> at
/// <c>ClientStepId.CarrierStep</c>, read by the previews (it is the <see cref="ICarrierFocusSource"/> that <see cref="FocusStep"/> reads)
/// and by presentation. Plain fields copied once per tick; allocation-free. Not thread-safe: read on the client's thread.
/// </summary>
/// <example>
/// <code>
/// ICarrierView view = binder.View;
/// int feetX = view.Current.X;
/// </code>
/// </example>
public sealed class CarrierView : ICarrierView, ICarrierFocusSource
{
    private CarrierState _current;
    private CarrierState _previous;
    private CarrierStatus _status;
    private CarrierStepResult _lastStep;
    private CarrierInput _lastInput;

    /// <inheritdoc />
    public bool HasCarrier { get; private set; }

    /// <inheritdoc />
    public uint Tick { get; private set; }

    /// <inheritdoc />
    public ref readonly CarrierState Current => ref _current;

    /// <inheritdoc />
    public ref readonly CarrierState Previous => ref _previous;

    /// <inheritdoc />
    public ushort LookYaw { get; private set; }

    /// <inheritdoc />
    public short LookPitch { get; private set; }

    /// <inheritdoc cref="ICarrierView.Status" />
    public ref readonly CarrierStatus Status => ref _status;

    /// <inheritdoc />
    public ref readonly CarrierStepResult LastStep => ref _lastStep;

    /// <inheritdoc />
    public ref readonly CarrierInput LastInput => ref _lastInput;

    /// <summary>The feet after the last step, mm (<see cref="ICarrierFocusSource"/>).</summary>
    public Vec3Mm Feet => new(_current.X, _current.Y, _current.Z);

    /// <summary>Publishes one step.</summary>
    internal void Publish(uint tick, in CarrierState previous, in CarrierState current, in CarrierInput input, in CarrierStatus status, in CarrierStepResult step, ushort lookYaw, short lookPitch)
    {
        Tick = tick;
        _previous = previous;
        _current = current;
        _lastInput = input;
        _status = status;
        _lastStep = step;
        LookYaw = lookYaw;
        LookPitch = lookPitch;
        HasCarrier = true;
    }
}
