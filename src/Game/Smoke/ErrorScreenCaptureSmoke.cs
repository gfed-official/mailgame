using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Screens;
using PerformativeMail.Sim.Core;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Screen-capture seam for story godot-host-shell/004's retained screenshots (debug builds only, like every scenario):
/// injects one boot failure, leaves its error screen up and passes after <see cref="Frames"/> rendered frames, so a
/// windowed <c>--write-movie</c> run captures it. Subclasses pick the injection.
/// </summary>
public abstract class ErrorScreenCaptureSmoke : SmokeScenario
{
    /// <summary>Rendered frames the screen stays up before the scenario passes.</summary>
    public const int Frames = 60;

    private int _frames;

    /// <summary>True when the content error screen is the one expected.</summary>
    protected abstract bool IsContent { get; }

    /// <summary>The text the message label must contain.</summary>
    protected abstract string ExpectedMessage { get; }

    /// <summary>The plan with the injected failure.</summary>
    /// <param name="loop">The loop.</param>
    /// <returns>The plan.</returns>
    protected abstract IReadOnlyList<IBootStep> Plan(GameLoop loop);

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        BootContext boot = loop.CreateBootContext();
        Expect(ctx, "boot with the injected failure returns", new BootSequence(Plan(loop)).Run(boot), false);
        Expect(ctx, "loop faulted", loop.IsFaulted, true);
    }

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        if (++_frames < Frames)
        {
            return SmokeStatus.Running;
        }

        BootErrorScreen? bootScreen = ctx.Loop.ErrorScreen;
        BootErrorScreen? contentScreen = BootSmoke.ContentScreen(ctx.Loop);
        BootErrorScreen? shown = IsContent ? contentScreen : bootScreen;
        Expect(ctx, $"Main/Screens/BootError visible after {Frames} frames", bootScreen?.Visible ?? false, !IsContent);
        Expect(ctx, $"Main/Screens/ContentError visible after {Frames} frames", contentScreen?.Visible ?? false, IsContent);
        Expect(ctx, "fault sink calls", bootScreen?.FaultCount ?? -1, 1);
        Expect(ctx, "message shown", shown?.MessageText.Contains(ExpectedMessage, StringComparison.Ordinal) ?? false, true);
        Expect(ctx, "Quit visible", shown?.QuitVisible ?? false, true);
        ctx.Pass($"screen text: {shown?.TitleText} | {shown?.DetailText} | {shown?.MessageText}");
        return SmokeStatus.Done;
    }
}

/// <summary>Captures <c>Main/Screens/BootError</c>: step 6 throws <c>SimBuildException(ReservedPhase)</c> (injection (b)).</summary>
[SmokeScenario("screen-boot-error")]
public sealed class BootErrorScreenCaptureSmoke : ErrorScreenCaptureSmoke
{
    /// <inheritdoc />
    protected override bool IsContent => false;

    /// <inheritdoc />
    protected override string ExpectedMessage => $"{nameof(SimBuildException)} (code {SimBuildErrorCode.ReservedPhase}): {BootSmoke.SimBuildMessage}";

    /// <inheritdoc />
    protected override IReadOnlyList<IBootStep> Plan(GameLoop loop) => BootSmoke.PlanWithSimBuildFailure(loop);
}

/// <summary>Captures <c>Main/Screens/ContentError</c>: the content source throws a findings list (injection (c)).</summary>
[SmokeScenario("screen-content-error")]
public sealed class ContentErrorScreenCaptureSmoke : ErrorScreenCaptureSmoke
{
    /// <inheritdoc />
    protected override bool IsContent => true;

    /// <inheritdoc />
    protected override string ExpectedMessage => $"{nameof(InvalidOperationException)}: {ThrowingContentSource.Message}";

    /// <inheritdoc />
    protected override IReadOnlyList<IBootStep> Plan(GameLoop loop) => loop.CreateBootPlan(content: new ThrowingContentSource());
}
