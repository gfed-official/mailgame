using Godot;

namespace SpikeOqIc9Input;

/// <summary>Logs focus / window notifications with frame and ms stamps.</summary>
public partial class FocusRx : Node
{
    public readonly List<string> Seen = new();

    public override void _Notification(int what)
    {
        string? n = what switch
        {
            (int)NotificationApplicationFocusIn => "ApplicationFocusIn",
            (int)NotificationApplicationFocusOut => "ApplicationFocusOut",
            (int)NotificationWMWindowFocusIn => "WMWindowFocusIn",
            (int)NotificationWMWindowFocusOut => "WMWindowFocusOut",
            (int)NotificationWMMouseEnter => "WMMouseEnter",
            (int)NotificationWMMouseExit => "WMMouseExit",
            (int)NotificationApplicationPaused => "ApplicationPaused",
            (int)NotificationApplicationResumed => "ApplicationResumed",
            _ => null,
        };
        if (n == null) { return; }
        Seen.Add(n);
        Util.Log($"  NOTIFICATION {n} ({what}) f={Engine.GetProcessFrames()} t={Time.GetTicksMsec()}ms");
    }
}
