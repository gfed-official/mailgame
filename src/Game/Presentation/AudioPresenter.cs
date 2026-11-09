using Godot;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// Presenter of the <c>Main/Audio</c> subtree and the cue entry of <see cref="PresenterDeps.Audio"/> (godot-host-shell story 006,
/// ADR-0019 sections 1-2). Inert until <see cref="Initialize"/>, which walks the subtree once like <see cref="WorldPresenter"/>.
/// <see cref="Cue"/> forwards to the sinks registered with <see cref="RegisterSink"/>, in registration order; with no sink registered
/// a cue is dropped (no placeholder audio, no log sink). Main thread only.
/// </summary>
/// <example><code>audioPresenter.RegisterSink(voicePool);   // mail-destinations 016 / throwing-catching 021</code></example>
public partial class AudioPresenter : Node, IPresenter, IInjectable<PresenterDeps>, IAudioCueSink
{
    private readonly PresenterViews _views = new();
    private readonly List<IAudioCueSink> _sinks = [];

    /// <inheritdoc />
    public bool IsInitialized => _views.Deps is not null;

    /// <summary>Registered views.</summary>
    public int ViewCount => _views.Count;

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps) => _views.Initialize(deps, this, null);

    /// <summary>Registers a view; once the presenter is initialised the view is initialised at once and presented from the next frame.</summary>
    /// <param name="view">The view.</param>
    public void Register(IPresentedView view) => _views.Register(view);

    /// <summary>Adds a cue sink; every later <see cref="Cue"/> reaches it.</summary>
    /// <param name="sink">The sink.</param>
    public void RegisterSink(IAudioCueSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sinks.Add(sink);
    }

    /// <inheritdoc />
    public void Cue(in AudioCue cue)
    {
        for (int i = 0; i < _sinks.Count; i++)
        {
            _sinks[i].Cue(in cue);
        }
    }

    /// <inheritdoc />
    public void Present(int alphaPermille) => _views.Present(alphaPermille);
}
