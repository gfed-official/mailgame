using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Screens;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Game.Smoke;

// Failure injections and helpers for the story godot-host-shell/004 boot smoke scenarios.

/// <summary>An <see cref="IBootContributor"/> that throws when run (failure injection (a)).</summary>
/// <param name="step">The slot it registers for.</param>
public sealed class ThrowingBootContributor(int step) : IBootContributor
{
    /// <summary>The injected message.</summary>
    public const string Message = "injected boot contributor failure (smoke)";

    /// <inheritdoc />
    public int Step { get; } = step;

    /// <inheritdoc />
    public void Run(BootContext ctx) => throw new InjectedFaultException(Message);
}

/// <summary>An <see cref="IBootContributor"/> that records each run into a shared list (slot order checks).</summary>
/// <param name="step">The slot it registers for.</param>
/// <param name="name">Its name in the record.</param>
/// <param name="record">The shared record.</param>
public sealed class RecordingBootContributor(int step, string name, List<string> record) : IBootContributor
{
    /// <inheritdoc />
    public int Step { get; } = step;

    /// <inheritdoc />
    public void Run(BootContext ctx) => record.Add(name);
}

/// <summary>A boot step that throws a given exception (failure injection (b) at step 6).</summary>
/// <param name="number">The step number.</param>
/// <param name="name">The step name.</param>
/// <param name="exception">What it throws.</param>
public sealed class ThrowingBootStep(int number, string name, Exception exception) : IBootStep
{
    /// <inheritdoc />
    public int Number { get; } = number;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public BootStepResult Run(BootContext ctx) => throw exception;
}

/// <summary>An <see cref="IContentBootSource"/> that throws like a failed built-in validation (failure injection (c)).</summary>
public sealed class ThrowingContentSource : IContentBootSource
{
    /// <summary>The injected message: a findings list in the shape <c>BuiltInContent.CreateM1a</c> throws.</summary>
    public const string Message =
        "The M-1a built-in content does not validate (injected by the smoke harness):\n" +
        "CV-GEN-01 items: duplicate id 'letter_small'\n" +
        "CV-MAIL-04 mail_mixes 'm1a_street': kind shares sum to 950 permille, expected 1000";

    /// <inheritdoc />
    public string Name => "ThrowingContentSource";

    /// <inheritdoc />
    public ContentSet Load() => throw new InvalidOperationException(Message);
}

/// <summary>Boot smoke helpers.</summary>
public static class BootSmoke
{
    /// <summary>The injected composition failure message (failure injection (b)).</summary>
    public const string SimBuildMessage =
        "system 'SmokeInjected' tried to bind reserved phase 0 (InputsApply) (injected by the smoke harness)";

    /// <summary>A <c>SimBuildException(ReservedPhase)</c> as composition (step 6) would throw it.</summary>
    /// <returns>The exception.</returns>
    public static SimBuildException ReservedPhase() => new(SimBuildErrorCode.ReservedPhase, SimBuildMessage);

    /// <summary>The production plan with step 6 (sim) replaced by a step throwing <see cref="ReservedPhase"/>.</summary>
    /// <param name="loop">The loop.</param>
    /// <returns>The plan.</returns>
    public static IReadOnlyList<IBootStep> PlanWithSimBuildFailure(GameLoop loop)
    {
        var plan = new List<IBootStep>(loop.CreateBootPlan());
        int i = plan.FindIndex(s => s.Number == BootPlan.SimStep);
        plan[i] = new ThrowingBootStep(BootPlan.SimStep, plan[i].Name, ReservedPhase());
        return plan;
    }

    /// <summary>The <c>Main/Screens/ContentError</c> screen of <paramref name="loop"/>, or null.</summary>
    /// <param name="loop">The loop.</param>
    /// <returns>The screen.</returns>
    public static BootErrorScreen? ContentScreen(GameLoop loop) => loop.GetNodeOrNull<BootErrorScreen>("Screens/ContentError");

    /// <summary>The <c>BOOT</c> status line numbers in order (<c>BOOT n name ok|FAIL|NOT WIRED</c>), from <paramref name="lines"/>.</summary>
    /// <param name="lines">The boot log.</param>
    /// <returns>(number, name, status) for each status line.</returns>
    public static List<(int Number, string Name, string Status)> StatusLines(IReadOnlyList<string> lines)
    {
        var found = new List<(int, string, string)>();
        foreach (string line in lines)
        {
            string[] parts = line.Split(' ', 4);
            if (parts.Length < 4 || parts[0] != "BOOT" || !int.TryParse(parts[1], out int n) || parts[2].EndsWith(':'))
            {
                continue;
            }

            string rest = parts[3];
            string? status = rest == "ok" ? "ok"
                : rest.StartsWith("FAIL ", StringComparison.Ordinal) ? "FAIL"
                : rest.StartsWith("NOT WIRED (", StringComparison.Ordinal) ? "NOT WIRED"
                : null;
            if (status is not null)
            {
                found.Add((n, parts[2], status));
            }
        }

        return found;
    }

    /// <summary>True when some line starts with <paramref name="prefix"/>.</summary>
    /// <param name="lines">The boot log.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>Whether one matched.</returns>
    public static bool Has(IReadOnlyList<string> lines, string prefix) =>
        lines.Any(l => l.StartsWith(prefix, StringComparison.Ordinal));
}
