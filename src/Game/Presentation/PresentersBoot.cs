using PerformativeMail.Game.Boot;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// Boot step 7's Godot half (godot-host-shell story 006): builds the one <see cref="PresenterDeps"/> from the step's inputs, calls
/// <c>Initialize(deps)</c> on the World, Hud and Audio presenters in that order (ADR-0019 section 2), then runs the World presenter's
/// world-build steps. <c>GameLoop</c> creates it over its own children <c>World</c>, <c>Hud</c> and <c>Audio</c>. Main thread only.
/// </summary>
/// <param name="world">The <c>Main/World</c> presenter.</param>
/// <param name="hud">The <c>Main/Hud</c> presenter.</param>
/// <param name="audio">The <c>Main/Audio</c> presenter.</param>
/// <example><code>ctx.Presenters = new PresentersBoot(world, hud, audio);</code></example>
public sealed class PresentersBoot(WorldPresenter world, HudPresenter hud, AudioPresenter audio) : IPresentersBoot
{
    /// <inheritdoc />
    public void Initialize(PresentationInputs inputs, BootLog log)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(log);
        var deps = new PresenterDeps
        {
            World = inputs.World,
            Clock = inputs.Clock,
            Events = inputs.Events,
            Published = inputs.Published,
            Audio = audio,
        };
        world.Initialize(deps);
        hud.Initialize(deps);
        audio.Initialize(deps);
        log.Write($"BOOT 7 world: presenters World,Hud,Audio initialized views={world.ViewCount},{hud.ViewCount},{audio.ViewCount}");
        int built = world.BuildWorld(inputs.World);
        log.Write(built == 0 ? "BOOT 7 world: no world-build steps" : $"BOOT 7 world: world-build steps ran={built}");
    }
}
