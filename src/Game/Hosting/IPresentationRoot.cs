namespace PerformativeMail.Game.Hosting;

/// <summary>
/// The one presentation entry point per frame (ADR-0002 section 3, ADR-0019 section 2). <see cref="FrameSequence"/> calls
/// <see cref="Present"/> once per frame with the tick driver's <c>AlphaPermille</c>. Lives in the Godot project, not the
/// Sim. Main thread only.
/// </summary>
/// <example><code>root.Present(driver.AlphaPermille);</code></example>
public interface IPresentationRoot
{
    /// <summary>Presents the frame.</summary>
    /// <param name="alphaPermille">The tick driver's interpolation fraction, 0..999 (0 while paused).</param>
    void Present(int alphaPermille);
}
