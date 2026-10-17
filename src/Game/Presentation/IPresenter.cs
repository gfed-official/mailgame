namespace PerformativeMail.Game.Presentation;

/// <summary>
/// A scene-side presenter that renders sim state. Presenters are inert until
/// <c>GameLoop</c> injects dependencies (story 006's <c>Initialize(deps)</c>); they never
/// look anything up in <c>_Ready</c> (ADR-0019 section 1).
/// </summary>
public interface IPresenter
{
    /// <summary>True once <c>Initialize(deps)</c> has run. Always false until story 006.</summary>
    bool IsInitialized { get; }

    /// <summary>Renders the frame at the given interpolation alpha, in permille (0..1000).</summary>
    /// <param name="alphaPermille">Fraction of the way to the next tick, in thousandths.</param>
    void Present(int alphaPermille);
}
