namespace PerformativeMail.Game.Presentation;

/// <summary>
/// A view its presenter calls once per frame, in registration order, after <c>RunFrame</c> and the render-clock sample (ADR-0019
/// section 2). It animates in its own <c>_Process</c> from the data <see cref="Present"/> gave it, never from <c>delta</c>.
/// </summary>
/// <example><code>public void Present(int alphaPermille) { _pose = CarrierPose.Lerp(_prev, _curr, alphaPermille); }</code></example>
public interface IPresentedView
{
    /// <summary>Presents the frame.</summary>
    /// <param name="alphaPermille">The tick driver's interpolation fraction, passed through unchanged.</param>
    void Present(int alphaPermille);
}
