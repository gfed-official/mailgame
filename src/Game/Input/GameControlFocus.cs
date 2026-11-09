using Godot;

namespace PerformativeMail.Game.Input;

/// <summary>
/// The game-Control convention (ADR-0012 section 7 "`ui_*` and GUI focus", TR-input-007, story input-controls 027): every HUD
/// and in-world <c>Control</c> takes no GUI focus, so Space, Enter and the stock <c>ui_*</c> keys can never activate one while
/// the player plays (spike S-07). HUD layers and in-world views call <see cref="Apply"/> on each Control they build. The
/// boundary: only the <c>Main/Hud</c> and <c>Main/World</c> subtrees are game contexts; menus outside play under
/// <c>Main/Screens</c> (boot error, content error, the pause menu's items) keep the default focus modes and stock <c>ui_*</c>
/// navigation. <see cref="FocusPolicyCheck"/> reports a Control that missed the call. Main thread only.
/// </summary>
/// <example><code>var label = GameControlFocus.Apply(new Label { Text = "Wallet" });</code></example>
public static class GameControlFocus
{
    /// <summary>Sets <paramref name="control"/>'s <c>FocusMode</c> to <c>None</c>.</summary>
    /// <typeparam name="T">The Control type.</typeparam>
    /// <param name="control">The Control.</param>
    /// <returns><paramref name="control"/>, for chaining at construction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is null.</exception>
    public static T Apply<T>(T control)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(control);
        control.FocusMode = Control.FocusModeEnum.None;
        return control;
    }
}
