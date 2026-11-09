using Godot;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Synthetic player input for the booted smokes: events go through <c>Input.ParseInputEvent</c> and are flushed at once, so they reach
/// the production <c>InputAdapter</c> and router exactly like a key or mouse event from the window. A smoke that wrote the client latch
/// directly would be overwritten by the router's own flush on the next pre-frame hook.
/// </summary>
/// <example><code>SmokeInput.Key(Key.W, true); Tick(loop); SmokeInput.Key(Key.W, false);</code></example>
public static class SmokeInput
{
    /// <summary>Presses or releases a key (physical and logical keycode both set).</summary>
    /// <param name="key">The key.</param>
    /// <param name="pressed">True to press.</param>
    public static void Key(Key key, bool pressed) =>
        Inject(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed });

    /// <summary>Presses or releases a mouse button at the viewport centre.</summary>
    /// <param name="button">The button.</param>
    /// <param name="pressed">True to press.</param>
    public static void Mouse(MouseButton button, bool pressed) =>
        Inject(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, Position = new Vector2(640, 360) });

    /// <summary>Moves the mouse by <paramref name="dx"/>, <paramref name="dy"/> screen pixels (the adapter sums it into one look record per frame).</summary>
    /// <param name="dx">Pixels right.</param>
    /// <param name="dy">Pixels down.</param>
    public static void MouseMotion(float dx, float dy) =>
        Inject(new InputEventMouseMotion { Relative = new Vector2(dx, dy), ScreenRelative = new Vector2(dx, dy) });

    private static void Inject(InputEvent e)
    {
        Godot.Input.ParseInputEvent(e);
        Godot.Input.FlushBufferedEvents();
    }
}
