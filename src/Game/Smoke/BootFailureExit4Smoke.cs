using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/004 AC5, the runner's boot-failed path (exit 4, <c>ExitBootFailed</c>), in its own process: the
/// pre-boot hook installs, through the debug-only <see cref="GameLoop.SetBootPlanForSmoke"/>, the production plan with a
/// throwing step-4 contributor, so <c>RequestBoot()</c> returns false and the runner exits 4 without starting the
/// scenario.
/// </summary>
[SmokeScenario("boot-failure-exit4")]
public sealed class BootFailureExit4Smoke : SmokeScenario
{
    /// <inheritdoc />
    public override bool NeedsBoot => true;

    /// <inheritdoc />
    public override int ExpectedExit => 4;

    /// <inheritdoc />
    public override void BeforeBoot(GameLoop loop) =>
        loop.SetBootPlanForSmoke(l => l.CreateBootPlan(contributors: [new ThrowingBootContributor(BootPlan.InputStep)]));

    /// <inheritdoc />
    public override void Start(SmokeContext ctx) => ctx.Fail("Start ran although boot was expected to fail");
}
