namespace PerformativeMail.Game.Boot;

/// <summary>
/// Runs a boot plan (ADR-0019 section 3) once, in order, on the boot thread. Each step writes one status line,
/// <c>BOOT &lt;n&gt; &lt;name&gt; ok</c> or <c>BOOT &lt;n&gt; &lt;name&gt; NOT WIRED (story …)</c>. The first step that throws
/// writes <c>BOOT &lt;n&gt; &lt;name&gt; FAIL &lt;type&gt; …</c>, is recorded in <see cref="BootContext.Failure"/>, prepares
/// the error display with its kind and goes to the boot-fault entry; no later step runs (control manifest: any exception
/// goes to the error screen and <c>RunFrame</c> stops). Never references the smoke harness.
/// </summary>
/// <example>
/// <code>
/// bool ok = new BootSequence(BootPlan.Create(window, isDebugBuild, new BuiltInContentSource(), BootPlan.Contributors, loop.ArmStart)).Run(ctx);
/// </code>
/// </example>
public sealed class BootSequence
{
    private readonly IBootStep[] _steps;

    /// <summary>Creates a sequence over <paramref name="steps"/>.</summary>
    /// <param name="steps">The plan: at least one step, numbers strictly ascending.</param>
    /// <exception cref="ArgumentNullException"><paramref name="steps"/> or one of its entries is null.</exception>
    /// <exception cref="ArgumentException">The plan is empty or its step numbers are not strictly ascending.</exception>
    public BootSequence(IReadOnlyList<IBootStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            throw new ArgumentException("A boot plan needs at least one step.", nameof(steps));
        }

        _steps = new IBootStep[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            IBootStep step = steps[i] ?? throw new ArgumentNullException(nameof(steps), $"Boot step at index {i} is null.");
            if (i > 0 && step.Number <= _steps[i - 1].Number)
            {
                throw new ArgumentException(
                    $"Boot step numbers must be strictly ascending: {_steps[i - 1].Number} is followed by {step.Number}.",
                    nameof(steps));
            }

            _steps[i] = step;
        }
    }

    /// <summary>The steps in run order.</summary>
    public IReadOnlyList<IBootStep> Steps => _steps;

    /// <summary>Runs every step in order until one throws.</summary>
    /// <param name="ctx">The boot context.</param>
    /// <returns>True when every step was ok or NOT WIRED; false when a step failed (the fault has been reported).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Called from a thread other than <see cref="BootContext.BootThreadId"/>, or
    /// on a context that already ran a failed boot.</exception>
    public bool Run(BootContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        int thread = System.Environment.CurrentManagedThreadId;
        if (thread != ctx.BootThreadId)
        {
            throw new InvalidOperationException(
                $"Boot ran on managed thread {thread}, not the boot thread {ctx.BootThreadId} (ADR-0003 section 3).");
        }

        if (ctx.Failure is not null)
        {
            throw new InvalidOperationException("This boot context already holds a failed boot; create a new one.");
        }

        foreach (IBootStep step in _steps)
        {
            BootStepResult result;
            try
            {
                result = step.Run(ctx);
            }
            catch (Exception ex)
            {
                Fail(ctx, step, ex);
                return false;
            }

            ctx.Log.Write(result.IsNotWired
                ? $"BOOT {step.Number} {step.Name} NOT WIRED ({result.UnwiredOwner})"
                : $"BOOT {step.Number} {step.Name} ok");
        }

        return true;
    }

    // Record the kind first, then tell the display, then the one boot-fault entry: OnFault shows the matching screen.
    private static void Fail(BootContext ctx, IBootStep step, Exception ex)
    {
        var failure = new BootFailure(step.Number, step.Name, step.FailureKind, ex);
        ctx.Failure = failure;
        ctx.Log.Write($"BOOT {step.Number} {step.Name} FAIL {BootFaultText.Describe(ex)}");
        try
        {
            ctx.Display?.PrepareBootFailure(failure);
        }
        catch (Exception displayEx)
        {
            ctx.Log.Write($"BOOT {step.Number} {step.Name}: error display threw {BootFaultText.Describe(displayEx)}");
        }

        ctx.FaultBoot(ex);
    }
}
