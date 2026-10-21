using PerformativeMail.Game.Hosting;

namespace PerformativeMail.Game.Smoke;

/// <summary>Result of one <see cref="SmokeScenario.Poll"/> call.</summary>
public enum SmokeStatus
{
    /// <summary>Not finished; poll again next frame.</summary>
    Running,

    /// <summary>Finished; the runner computes the exit code.</summary>
    Done,
}

/// <summary>
/// Base class for headless smoke scenarios. Name comes from <see cref="SmokeScenarioAttribute"/>.
/// Exit codes: 0 pass, 1 failed assertion, 2 unknown scenario, 3 zero assertions, 4 boot failed,
/// 5 timeout.
/// </summary>
public abstract class SmokeScenario
{
    /// <summary>True when the scenario needs <c>GameLoop.RequestBoot()</c> to succeed first.</summary>
    public virtual bool NeedsBoot => false;

    /// <summary>The exit code this scenario is designed to produce (negative-path scenarios override).</summary>
    public virtual int ExpectedExit => 0;

    /// <summary>Wall-clock budget in seconds before the run is stopped with exit 5.</summary>
    public virtual double TimeoutSeconds => 60;

    /// <summary>
    /// Pre-boot hook: for a <see cref="NeedsBoot"/> scenario the runner calls it just before
    /// <c>GameLoop.RequestBoot()</c> (install a frame sequence, or a failing plan with
    /// <c>GameLoop.InstallBootStepsForSmoke</c>). Not called when the scenario does not need boot.
    /// </summary>
    /// <param name="loop">The root <c>GameLoop</c>.</param>
    public virtual void BeforeBoot(GameLoop loop)
    {
    }

    /// <summary>Runs once, after the optional boot, on the first frame.</summary>
    /// <param name="ctx">Assertion and tree access.</param>
    public abstract void Start(SmokeContext ctx);

    /// <summary>Called every frame after <see cref="Start"/> until it returns <see cref="SmokeStatus.Done"/>.</summary>
    /// <param name="ctx">Assertion and tree access.</param>
    /// <returns>Whether the scenario is still running.</returns>
    public virtual SmokeStatus Poll(SmokeContext ctx) => SmokeStatus.Done;
}
