using Godot;
using PerformativeMail.Game.Hud;
using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Camera;

/// <summary>
/// The debug-build focus bracket (player-character story 026): a <see cref="CanvasLayer"/> boot adds through
/// <c>HudPresenter.AddLayer</c> only when <c>OS.IsDebugBuild()</c>, so the <see cref="FocusFeed"/> can be checked on screen before the
/// HUD's prompt exists. Each Hud frame it copies <see cref="IHudFocusFeed.Current"/> (published by the World presenter earlier in the
/// same frame) to its <see cref="FocusBracket"/>, which outlines the bounds and marks the anchor point with <c>DrawRect</c> only. Inert
/// until <see cref="Initialize"/>; with no feed published it draws nothing. Main thread only.
/// </summary>
/// <example><code>if (OS.IsDebugBuild()) { hud.AddLayer(new FocusDebugOverlay(), FocusDebugOverlay.LayerOrder); }</code></example>
public partial class FocusDebugOverlay : CanvasLayer, IInjectable<PresenterDeps>, IPresentedView
{
    /// <summary>The overlay's place in the Hud layer stack: above every HUD layer.</summary>
    public const int LayerOrder = 1000;

    private IHudFocusFeed? _feed;
    private FocusBracket? _bracket;

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <summary>The feed the last <see cref="Present"/> drew.</summary>
    public FocusFeed LastFeed { get; private set; }

    /// <inheritdoc />
    public void Initialize(PresenterDeps deps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        deps.Published.TryGet(out _feed);
        _bracket = new FocusBracket { Name = "Bracket" };
        AddChild(_bracket);
        IsInitialized = true;
    }

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        if (_feed is null || _bracket is null)
        {
            return;
        }

        LastFeed = _feed.Current;
        _bracket.SetFeed(LastFeed);
    }
}
