using PerformativeMail.Sim.Content;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Boot step 8, <c>views</c> (ADR-0019 sections 3 and 5, CD28; godot-host-shell stories 008-009): hands step 2's content set to
/// <see cref="BootContext.Views"/>, which resolves the view catalogs with <c>release = !OS.IsDebugBuild()</c>. A missing view is a
/// warning in debug (the placeholder shows); any error (a release-build missing view, a length mismatch, a duplicate id, a missing
/// catalog or placeholder) fails the step with the content error screen. Pools are added here when the first one exists.
/// </summary>
/// <param name="isDebugBuild"><c>OS.IsDebugBuild()</c>.</param>
/// <example><code>IBootStep eight = new ViewsBootStep(OS.IsDebugBuild());   // BOOT 8 views: … / BOOT 8 views ok</code></example>
public sealed class ViewsBootStep(bool isDebugBuild) : IBootStep
{
    /// <inheritdoc />
    public int Number => BootPlan.ViewsStep;

    /// <inheritdoc />
    public string Name => "views";

    /// <inheritdoc />
    public BootFailureKind FailureKind => BootFailureKind.Content;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No content set or views boot on the context, or the resolution reported errors.</exception>
    public BootStepResult Run(BootContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ContentSet content = ctx.Content ?? throw new InvalidOperationException("Boot step 8 needs step 2's content set.");
        IViewsBoot views = ctx.Views
            ?? throw new InvalidOperationException("Boot step 8 has no views boot (GameLoop sets BootContext.Views with its presenters).");
        int errors = views.ResolveViews(content, release: !isDebugBuild, ctx.Log);
        if (errors > 0)
        {
            throw new InvalidOperationException($"Boot step 8: {errors} view-catalog error(s); see the VIEW error lines.");
        }

        return BootStepResult.Ok;
    }
}
