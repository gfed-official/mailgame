namespace PerformativeMail.Game.Boot;

/// <summary>
/// The root window as boot step 1 sees it (ADR-0019 section 7). Plain C# so the window guard is unit-tested; the game
/// passes <see cref="GodotBootWindow"/> over <c>GetWindow()</c>. Sizes are (width, height) in window pixels.
/// </summary>
/// <example><code>window.MinSize = (1280, 720); var back = window.MinSize;</code></example>
public interface IBootWindow
{
    /// <summary>The window's minimum size (<c>Window.MinSize</c>; no project setting exists, ADR-0019 section 7).</summary>
    (int Width, int Height) MinSize { get; set; }

    /// <summary>The window's current size.</summary>
    (int Width, int Height) Size { get; }

    /// <summary>The content-scale (stretch) mode, as the engine names it.</summary>
    string ContentScaleMode { get; }

    /// <summary>The content-scale (stretch) aspect, as the engine names it.</summary>
    string ContentScaleAspect { get; }
}
