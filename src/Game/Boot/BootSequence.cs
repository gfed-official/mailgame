using System.Text;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Executes a boot plan (ADR-0019 section 3) exactly as listed, on the boot thread. After each step it writes the step's
/// status line — <c>BOOT &lt;n&gt; &lt;name&gt; ok</c> or <c>BOOT &lt;n&gt; &lt;name&gt; NOT WIRED (story …)</c>. When a step
/// throws, the run stops there: the failure (step and kind) goes into <see cref="BootContext.Failure"/>, a
/// <c>BOOT &lt;n&gt; &lt;name&gt; FAIL …</c> line is written, the error display learns which step failed and the boot-fault
/// entry is called once, so the session ends before <c>RunFrame</c> ever runs. Knows nothing of the smoke harness.
/// </summary>
/// <example>
/// <code>
/// var sequence = new BootSequence(loop.BuildBootPlan());
/// if (!sequence.Run(ctx)) { /* ctx.Failure says which step; the error screen is showing */ }
/// </code>
/// </example>
public sealed class BootSequence
{
    private readonly IBootStep[] _plan;

    /// <summary>Checks and keeps the plan.</summary>
    /// <param name="steps">The steps in run order: at least one, numbers strictly increasing, no null entry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="steps"/> or one of its entries is null.</exception>
    /// <exception cref="ArgumentException">The plan is empty or a step number does not increase.</exception>
    public BootSequence(IReadOnlyList<IBootStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            throw new ArgumentException("A boot plan needs at least one step.", nameof(steps));
        }

        _plan = new IBootStep[steps.Count];
        int previous = int.MinValue;
        for (int i = 0; i < _plan.Length; i++)
        {
            IBootStep step = steps[i] ?? throw new ArgumentNullException(nameof(steps), $"Boot plan entry {i} is null.");
            if (step.Number <= previous)
            {
                throw new ArgumentException($"Boot step {step.Number} ({step.Name}) does not come after step {previous}.", nameof(steps));
            }

            previous = step.Number;
            _plan[i] = step;
        }
    }

    /// <summary>The plan in run order.</summary>
    public IReadOnlyList<IBootStep> Steps => _plan;

    /// <summary>
    /// One-line text of a fault for the log and the error screen: the exception's type name, then for a
    /// <see cref="SimBuildException"/> its <see cref="SimBuildException.Code"/> as <c>(code Name)</c>, then the message.
    /// </summary>
    /// <param name="ex">The fault.</param>
    /// <returns>For example <c>SimBuildException (code ReservedPhase): bad bind</c> or <c>InvalidOperationException: …</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ex"/> is null.</exception>
    public static string Summarize(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var text = new StringBuilder(ex.GetType().Name);
        if (ex is SimBuildException composition)
        {
            text.Append(" (code ").Append(composition.Code).Append(')');
        }

        return text.Append(": ").Append(ex.Message).ToString();
    }

    /// <summary>
    /// Reports a failure that happened before any step could run (building or checking the plan) the same way a failing
    /// step is reported: as step 0 <c>plan</c>, line <c>BOOT plan FAIL …</c>, boot error screen, one boot-fault call.
    /// </summary>
    /// <param name="ctx">The boot context.</param>
    /// <param name="ex">What went wrong.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void FailBeforeFirstStep(BootContext ctx, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(ex);
        Abort(ctx, new BootFailure(0, "plan", BootFailureKind.Boot, ex), $"BOOT plan FAIL {Summarize(ex)}");
    }

    /// <summary>Runs the steps in order until one throws.</summary>
    /// <param name="ctx">The boot context.</param>
    /// <returns>True when every step was ok or NOT WIRED; false when one failed (the failure has been reported).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ctx"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Not on <see cref="BootContext.BootThreadId"/>, or the context already
    /// holds a failed boot.</exception>
    public bool Run(BootContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        int caller = System.Environment.CurrentManagedThreadId;
        if (caller != ctx.BootThreadId)
        {
            throw new InvalidOperationException(
                $"Boot must run on the boot thread {ctx.BootThreadId}; it was called on managed thread {caller} (ADR-0003 section 3).");
        }

        if (ctx.Failure is not null)
        {
            throw new InvalidOperationException("This boot context belongs to a boot that already failed; use a new context.");
        }

        for (int i = 0; i < _plan.Length; i++)
        {
            IBootStep step = _plan[i];
            BootStepResult outcome = default;
            Exception? thrown = null;
            try
            {
                outcome = step.Run(ctx);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            if (thrown is not null)
            {
                Abort(ctx, new BootFailure(step.Number, step.Name, step.FailureKind, thrown), $"BOOT {step.Number} {step.Name} FAIL {Summarize(thrown)}");
                return false;
            }

            string status = outcome.IsNotWired ? $"NOT WIRED ({outcome.UnwiredOwner})" : "ok";
            ctx.Log.Write($"BOOT {step.Number} {step.Name} {status}");
        }

        return true;
    }

    // Order matters: the failure is recorded and logged, the display is told which screen to show (a throwing display is
    // logged, never allowed to swallow the fault), and only then does the single boot-fault entry deliver the message.
    private static void Abort(BootContext ctx, BootFailure failure, string failLine)
    {
        ctx.Failure = failure;
        ctx.Log.Write(failLine);
        try
        {
            ctx.Display?.PrepareBootFailure(failure);
        }
        catch (Exception displayEx)
        {
            ctx.Log.Write($"BOOT {failure.Step} {failure.Name}: error display threw {Summarize(displayEx)}");
        }

        ctx.FaultBoot(failure.Exception);
    }
}
