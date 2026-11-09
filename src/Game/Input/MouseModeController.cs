using Godot;
using PerformativeMail.Client.Core.Input;

namespace PerformativeMail.Game.Input;

/// <summary>
/// Applies the router's <see cref="MouseModeRequest"/> to <c>Godot.Input.MouseMode</c> (ADR-0012 section 7 "Mouse mode" as
/// amended by spike delta D-2, story input-controls 027). <see cref="Apply"/> writes only when the request differs from the
/// last one applied, so the mode switches on a router-state change and never per frame. Capture is a request, not a fact: its
/// readback is checked (native Wayland drops it without pointer focus, S-34w; headless ignores it, S-31), and
/// <see cref="Reapply"/> asks again while the state still wants it (the adapter calls it on every mouse press, the "first click
/// into the window"; story 018's focus-in handler calls it too). Leaving capture warps the cursor to the viewport centre, the
/// X11 behaviour the spike recorded (S-34x), so every platform shows it there and no panel assumes an older position.
/// Owned by <see cref="InputAdapter"/>. Main thread only.
/// </summary>
/// <example><code>mouse.Apply(router.MouseModeRequest, GetViewport());   // on router.StateChanged</code></example>
public sealed class MouseModeController
{
    /// <summary>The last request applied; null before the first <see cref="Apply"/>.</summary>
    public MouseModeRequest? Applied { get; private set; }

    /// <summary>Writes to <c>Godot.Input.MouseMode</c> since construction (applies and re-applies).</summary>
    public long Writes { get; private set; }

    /// <summary>True when the last capture write read back as not captured; <see cref="Reapply"/> retries it.</summary>
    public bool CaptureRejected { get; private set; }

    /// <summary>Applies <paramref name="request"/> if it differs from <see cref="Applied"/>; otherwise does nothing.</summary>
    /// <param name="request">The router's request.</param>
    /// <param name="viewport">The adapter's viewport (centre warp when capture ends).</param>
    public void Apply(MouseModeRequest request, Viewport viewport)
    {
        if (Applied == request)
        {
            return;
        }

        bool wasCaptured = Applied == MouseModeRequest.Captured;
        Applied = request;
        Write(request);
        if (wasCaptured && request == MouseModeRequest.Visible)
        {
            viewport.WarpMouse(viewport.GetVisibleRect().Size / 2);
        }
    }

    /// <summary>Asks for capture again when the applied request is capture and the readback shows it is not held.</summary>
    public void Reapply()
    {
        if (Applied == MouseModeRequest.Captured && Godot.Input.MouseMode != Godot.Input.MouseModeEnum.Captured)
        {
            Write(MouseModeRequest.Captured);
        }
    }

    private void Write(MouseModeRequest request)
    {
        Godot.Input.MouseModeEnum mode = request == MouseModeRequest.Captured ? Godot.Input.MouseModeEnum.Captured : Godot.Input.MouseModeEnum.Visible;
        Godot.Input.MouseMode = mode;
        Writes++;
        CaptureRejected = request == MouseModeRequest.Captured && Godot.Input.MouseMode != mode;
    }
}
