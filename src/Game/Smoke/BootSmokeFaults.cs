using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Game.Smoke;

// Injected boot failures and log helpers for the story godot-host-shell/004 smoke scenarios. Boot code never sees these:
// scenarios hand them to GameLoop.BuildBootPlan / InstallBootStepsForSmoke.

/// <summary>A slot contributor whose run fails (injection (a): a broken step-4 contributor).</summary>
/// <param name="slot">The slot it registers for.</param>
public sealed class FaultingContributor(int slot) : IBootContributor
{
    /// <summary>The message of the injected fault.</summary>
    public const string Message = "input contributor failed on purpose (boot smoke)";

    /// <inheritdoc />
    public int Step => slot;

    /// <inheritdoc />
    public void Run(BootContext ctx) => throw new InjectedFaultException(Message);
}

/// <summary>A plan step that fails with a given exception (injection (b): composition at step 6).</summary>
/// <param name="number">The step number it replaces.</param>
/// <param name="name">The step name it replaces.</param>
/// <param name="fault">The exception it throws.</param>
public sealed class FaultingStep(int number, string name, Exception fault) : IBootStep
{
    /// <inheritdoc />
    public int Number => number;

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public BootStepResult Run(BootContext ctx) => throw fault;
}

/// <summary>A content source that fails the way a non-validating built-in set does (injection (c)).</summary>
public sealed class FaultingContentSource : IContentBootSource
{
    /// <summary>The injected findings list, one finding per line like <c>BuiltInContent.CreateM1a</c>'s message.</summary>
    public const string Message =
        "The M-1a built-in content does not validate (injected by the smoke harness):\n" +
        "CV-GEN-01 items: duplicate id 'letter_small'\n" +
        "CV-MAIL-04 mail_mixes 'm1a_street': kind shares sum to 950 permille, expected 1000";

    /// <inheritdoc />
    public string Name => nameof(FaultingContentSource);

    /// <inheritdoc />
    public ContentSet Load() => throw new InvalidOperationException(Message);
}

/// <summary>Plans with injected failures and readers for the <c>BOOT</c> log.</summary>
public static class BootSmokeFaults
{
    /// <summary>The message of the injected composition failure.</summary>
    public const string ReservedPhaseMessage =
        "system 'SmokeInjected' tried to bind reserved phase 0 (InputsApply) (injected by the smoke harness)";

    /// <summary>A composition failure as <c>SimComposition.Build</c> would raise it.</summary>
    /// <returns>A <see cref="SimBuildException"/> with <see cref="SimBuildErrorCode.ReservedPhase"/>.</returns>
    public static SimBuildException ReservedPhase() => new(SimBuildErrorCode.ReservedPhase, ReservedPhaseMessage);

    /// <summary>The loop's production plan with step 6 (sim) swapped for a step that throws <see cref="ReservedPhase"/>.</summary>
    /// <param name="loop">The loop whose plan is used.</param>
    /// <returns>Nine steps, step 6 failing.</returns>
    public static IReadOnlyList<IBootStep> WithFailingSimStep(GameLoop loop)
    {
        IBootStep[] steps = [.. loop.BuildBootPlan()];
        for (int i = 0; i < steps.Length; i++)
        {
            if (steps[i].Number == BootPlan.SimStep)
            {
                steps[i] = new FaultingStep(BootPlan.SimStep, steps[i].Name, ReservedPhase());
            }
        }

        return steps;
    }

    /// <summary>The status lines (<c>BOOT n name ok|FAIL …|NOT WIRED (…)</c>) of a boot log, as (number, name, status).</summary>
    /// <param name="lines">The boot log.</param>
    /// <returns>One entry per status line, in order; detail and env lines are skipped.</returns>
    public static List<(int Number, string Name, string Status)> StatusLines(IReadOnlyList<string> lines)
    {
        var statuses = new List<(int, string, string)>();
        foreach (string line in lines)
        {
            string[] field = line.Split(' ', 4);
            if (field.Length != 4 || field[0] != "BOOT" || !int.TryParse(field[1], out int number) || field[2].EndsWith(':'))
            {
                continue;
            }

            string? status = field[3] switch
            {
                "ok" => "ok",
                string t when t.StartsWith("FAIL ", StringComparison.Ordinal) => "FAIL",
                string t when t.StartsWith("NOT WIRED (", StringComparison.Ordinal) => "NOT WIRED",
                _ => null,
            };
            if (status is not null)
            {
                statuses.Add((number, field[2], status));
            }
        }

        return statuses;
    }

    /// <summary>True when a line of <paramref name="lines"/> starts with <paramref name="prefix"/>.</summary>
    /// <param name="lines">The boot log.</param>
    /// <param name="prefix">The expected start of a line.</param>
    /// <returns>Whether such a line exists.</returns>
    public static bool Logged(IReadOnlyList<string> lines, string prefix) =>
        lines.Any(line => line.StartsWith(prefix, StringComparison.Ordinal));
}
