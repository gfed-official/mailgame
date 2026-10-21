using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/004 AC5: the runner's "boot failed" exit (4), in a process of its own. Before boot, the
/// pre-boot hook installs (debug-only <see cref="GameLoop.InstallBootStepsForSmoke"/>) the production plan with a step-4
/// contributor that fails, so <c>GameLoop.RequestBoot()</c> returns false and the runner ends with exit 4, naming the
/// <c>BOOT 4 input FAIL</c> line, before this scenario is ever started.
/// </summary>
[SmokeScenario("boot-failure-exit4")]
public sealed class BootFailedExitSmoke : SmokeScenario
{
    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override int ExpectedExit => 4;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop) =>
        loop.InstallBootStepsForSmoke(loop.BuildBootPlan(contributors: [new FaultingContributor(BootPlan.InputStep)]));

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        // Reaching Start means RequestBoot reported success with a failing contributor installed: a defect.
        ctx.Fail($"the scenario started; boot result was {ctx.Loop.BootResult}, expected false and exit 4 from the runner");
        ctx.Quit();
    }
}
