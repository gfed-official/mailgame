namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The mouse mode the router asks the Godot adapter to apply (ADR-0012 section 7; story 027 applies it). It changes only on a
/// router-state change.
/// </summary>
/// <example><code>if (router.MouseModeRequest == MouseModeRequest.Visible) { /* show the cursor */ }</code></example>
public enum MouseModeRequest : byte
{
    /// <summary>The pointer is captured: body states with look (World, Holding-closed, aiming).</summary>
    Captured = 0,

    /// <summary>The pointer is visible: Panel, Holding-open, Pause and every other state without look.</summary>
    Visible = 1,
}
