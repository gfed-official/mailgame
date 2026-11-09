using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Client.Core.Timing;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Boot step 7, <c>world</c> (ADR-0019 section 3, ADR-0016 section 5; godot-host-shell story 006): creates the
/// <see cref="PresentationEventStream"/> and sets it as the client adapter's <c>DecodedEvents</c> before any frame runs, so every batch
/// is published in the frame it is decoded; then hands the client's world tables, the render clock, the stream and
/// <see cref="BootContext.Published"/> to <see cref="BootContext.Presenters"/>, which initialises World, Hud and Audio once, in that
/// order, and runs the world-build steps. Any exception fails the step (the boot error screen shows).
/// </summary>
/// <example><code>IBootStep seven = new PresentersBootStep();   // BOOT 7 world: presenters … / BOOT 7 world ok</code></example>
public sealed class PresentersBootStep : IBootStep
{
    /// <inheritdoc />
    public int Number => BootPlan.WorldStep;

    /// <inheritdoc />
    public string Name => "world";

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No session or render clock (steps 5-6), or no presenters on the context.</exception>
    public BootStepResult Run(BootContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        BootSession booted = ctx.Session ?? throw new InvalidOperationException("Boot step 7 needs step 5's session.");
        IRenderClock clock = booted.RenderClock ?? throw new InvalidOperationException("Boot step 7 needs step 6's render clock.");
        IPresentersBoot presenters = ctx.Presenters
            ?? throw new InvalidOperationException("Boot step 7 has no presenters to initialise (GameLoop sets BootContext.Presenters from Main/World, Main/Hud and Main/Audio).");

        var events = new PresentationEventStream();
        booted.Session.ClientAdapter.DecodedEvents = events;
        booted.Events = events;
        string published = ctx.Published.Types.Count == 0 ? "none" : string.Join(",", ctx.Published.Types.Select(t => t.Name));
        ctx.Log.Write($"BOOT 7 world: event stream on the client adapter; published states: {published}");
        presenters.Initialize(new PresentationInputs(booted.World, clock, events, ctx.Published), ctx.Log);
        return BootStepResult.Ok;
    }
}
