namespace PerformativeMail.Game.Boot;

/// <summary>
/// Boot step 9, start (ADR-0019 section 3, ADR-0002): only arms the start (<c>GameLoop.ArmStart</c>); the tick driver is
/// started with the clock's timestamp on the first <c>_Process</c>, never during boot (F12: the first-frame hitch must not
/// become ticks).
/// </summary>
/// <param name="armStart">The arm callback, <c>GameLoop.ArmStart</c>.</param>
/// <example><code>IBootStep start = new ArmStartStep(loop.ArmStart);</code></example>
public sealed class ArmStartStep(Action armStart) : IBootStep
{
    private readonly Action _armStart = armStart ?? throw new ArgumentNullException(nameof(armStart));

    /// <inheritdoc />
    public int Number => BootPlan.StartStep;

    /// <inheritdoc />
    public string Name => "start";

    /// <inheritdoc />
    public BootStepResult Run(BootContext ctx)
    {
        _armStart();
        return BootStepResult.Ok;
    }
}
