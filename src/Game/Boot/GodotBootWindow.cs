using Godot;

namespace PerformativeMail.Game.Boot;

/// <summary>The <see cref="IBootWindow"/> over a Godot <see cref="Window"/> (the game passes <c>GetWindow()</c>).</summary>
/// <param name="window">The root window.</param>
/// <example><code>IBootWindow w = new GodotBootWindow(loop.GetWindow());</code></example>
public sealed class GodotBootWindow(Window window) : IBootWindow
{
    private readonly Window _window = window ?? throw new ArgumentNullException(nameof(window));

    /// <inheritdoc />
    public (int Width, int Height) MinSize
    {
        get => (_window.MinSize.X, _window.MinSize.Y);
        set => _window.MinSize = new Vector2I(value.Width, value.Height);
    }

    /// <inheritdoc />
    public (int Width, int Height) Size => (_window.Size.X, _window.Size.Y);

    /// <inheritdoc />
    public string ContentScaleMode => _window.ContentScaleMode.ToString();

    /// <inheritdoc />
    public string ContentScaleAspect => _window.ContentScaleAspect.ToString();
}
