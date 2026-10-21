using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Hosting;

/// <summary>
/// The real <see cref="IPresentationRoot"/>: calls the presenters in the fixed ADR-0019 section 2 order World, Hud, Audio,
/// each once per frame. Plain C#; presenter behaviour is story 006. Allocation-free per call. Main thread only.
/// </summary>
/// <example>
/// <code>
/// var root = new PresentationRoot(worldPresenter, hudPresenter, audioPresenter);
/// root.Present(driver.AlphaPermille);
/// </code>
/// </example>
public sealed class PresentationRoot : IPresentationRoot
{
    private readonly IPresenter[] _presenters;

    /// <summary>Creates the root over the three presenters.</summary>
    /// <param name="world">The world presenter (first).</param>
    /// <param name="hud">The HUD presenter (second).</param>
    /// <param name="audio">The audio presenter (third).</param>
    /// <exception cref="ArgumentNullException">A presenter is null.</exception>
    public PresentationRoot(IPresenter world, IPresenter hud, IPresenter audio)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(hud);
        ArgumentNullException.ThrowIfNull(audio);
        _presenters = [world, hud, audio];
    }

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        IPresenter[] presenters = _presenters;
        for (int i = 0; i < presenters.Length; i++)
        {
            presenters[i].Present(alphaPermille);
        }
    }
}
