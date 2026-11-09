namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// What one owned-carrier step did (player-character story 020, "the swept result of the last tick"): the feet displacement and the
/// solver's probe counters for that tick, read from the scratch before the counter flush. Values only.
/// </summary>
/// <example><code>int moved = view.LastStep.DxMm;</code></example>
public readonly struct CarrierStepResult
{
    /// <summary>Feet X displacement this tick, mm.</summary>
    public int DxMm { get; init; }

    /// <summary>Feet Y displacement this tick, mm.</summary>
    public int DyMm { get; init; }

    /// <summary>Feet Z displacement this tick, mm.</summary>
    public int DzMm { get; init; }

    /// <summary>Sub-steps the sweep took (max over the tick).</summary>
    public int SubSteps { get; init; }

    /// <summary>Slide iterations in one sub-step (max over the tick).</summary>
    public int SlideIterations { get; init; }

    /// <summary>Caps that clamped a move this tick.</summary>
    public int StepClamped { get; init; }

    /// <summary>Depenetration corrections this tick.</summary>
    public int Depenetrations { get; init; }
}

/// <summary>
/// The owned carrier as the ClientLayer publishes it after <c>ClientStepId.CarrierStep</c> (player-character story 020): read-only, and
/// everything story 022 needs to render the pose without touching the solver. Valid once <see cref="HasCarrier"/>; before the first
/// stepped tick every member is its default. The own carrier's timeline is "present, ≤ 1-tick interpolation by <c>AlphaPermille</c>"
/// between <see cref="Previous"/> and <see cref="Current"/> (ADR-0016 section 6).
/// </summary>
/// <example>
/// <code>
/// if (view.HasCarrier)
/// {
///     ref readonly CarrierState now = ref view.Current;
///     ref readonly CarrierState before = ref view.Previous;   // interpolate by AlphaPermille
/// }
/// </code>
/// </example>
public interface ICarrierView
{
    /// <summary>True once the carrier has been seeded and stepped at least once.</summary>
    bool HasCarrier { get; }

    /// <summary>The client tick of the last step.</summary>
    uint Tick { get; }

    /// <summary>The state after the last step (feet, <c>BodyYaw</c>, <c>Loco</c>, flags, velocity, <c>RemX</c>/<c>RemZ</c>).</summary>
    ref readonly CarrierState Current { get; }

    /// <summary>The state before the last step (equal to <see cref="Current"/>'s seed on the first stepped tick).</summary>
    ref readonly CarrierState Previous { get; }

    /// <summary>The aim's look yaw, BAM (the frame's absolute value).</summary>
    ushort LookYaw { get; }

    /// <summary>The aim's look pitch, signed BAM.</summary>
    short LookPitch { get; }

    /// <summary>The status published with the last step (story 013; <c>OccupiedSeat</c> is <c>AnchorRef.None</c> at M-1a).</summary>
    ref readonly CarrierStatus Status { get; }

    /// <summary>The swept result of the last step.</summary>
    ref readonly CarrierStepResult LastStep { get; }

    /// <summary>The solver input of the last step, so presentation can run a one-tick probe on a copy of the state.</summary>
    ref readonly CarrierInput LastInput { get; }
}
