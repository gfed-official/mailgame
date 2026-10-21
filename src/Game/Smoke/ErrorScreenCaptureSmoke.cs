using Godot;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Screens;
using PerformativeMail.Sim.Core;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Screen-capture seam for story godot-host-shell/004's retained screenshots (debug builds only, like every scenario):
/// injects one boot failure, leaves its error screen up and passes after <see cref="Frames"/> rendered frames, so a
/// windowed <c>--write-movie</c> run captures it. The root window is resized to the 1280×720 minimum first (the project's
/// <c>window_*_override</c> 1600×900 wins over <c>--resolution</c>), so the capture shows the smallest supported layout.
/// Subclasses pick the injection.
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
        loop.GetWindow().Size = new Vector2I(1280, 720);
        BootContext boot = loop.BeginBootForSmoke();
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

        Vector2I size = ctx.Loop.GetWindow().Size;
        ctx.Pass($"window size at capture {size.X}x{size.Y} (display {DisplayServer.GetName()})");
        if (DisplayServer.GetName() != "headless")
        {
            Expect(ctx, "window size at capture (1280x720 minimum)", size, new Vector2I(1280, 720));
        }

        BootErrorScreen? bootScreen = ctx.Loop.BootErrorPanel;
        BootErrorScreen? contentScreen = ctx.Loop.ContentErrorPanel;
        BootErrorScreen? shown = IsContent ? contentScreen : bootScreen;
        Expect(ctx, $"Main/Screens/BootError visible after {Frames} frames", bootScreen?.Visible ?? false, !IsContent);
        Expect(ctx, $"Main/Screens/ContentError visible after {Frames} frames", contentScreen?.Visible ?? false, IsContent);
        Expect(ctx, "fault sink calls", bootScreen?.FaultsReceived ?? -1, 1);
        Expect(ctx, "message shown", shown?.MessageText.Contains(ExpectedMessage, StringComparison.Ordinal) ?? false, true);
        Expect(ctx, "sink state", bootScreen?.Showing ?? BootErrorScreen.Shown.Nothing, IsContent ? BootErrorScreen.Shown.ContentStep : BootErrorScreen.Shown.BootStep);
        Expect(ctx, "Quit visible", shown?.QuitShown ?? false, true);
        Expect(ctx, "Quit has keyboard/pad focus (stock ui_* focus)", shown?.QuitFocused ?? false, true);
        ctx.Pass($"screen text: {shown?.HeadingText} | {shown?.WhereText} | {shown?.MessageText}");
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
    protected override string ExpectedMessage => $"{nameof(SimBuildException)} (code {SimBuildErrorCode.ReservedPhase}): {BootSmokeFaults.ReservedPhaseMessage}";

    /// <inheritdoc />
    protected override IReadOnlyList<IBootStep> Plan(GameLoop loop) => BootSmokeFaults.WithFailingSimStep(loop);
}

/// <summary>Captures <c>Main/Screens/ContentError</c>: the content source throws a findings list (injection (c)).</summary>
[SmokeScenario("screen-content-error")]
public sealed class ContentErrorScreenCaptureSmoke : ErrorScreenCaptureSmoke
{
    /// <inheritdoc />
    protected override bool IsContent => true;

    /// <inheritdoc />
    protected override string ExpectedMessage => $"{nameof(InvalidOperationException)}: {FaultingContentSource.Message}";

    /// <inheritdoc />
    protected override IReadOnlyList<IBootStep> Plan(GameLoop loop) => loop.BuildBootPlan(content: new FaultingContentSource());
}
