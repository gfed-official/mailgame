using Godot;

namespace SpikeOqIc9Input;

/// <summary>_Input receiver that reads exactly the fields the adapter needs; allocation-measurement target.</summary>
public partial class AllocRx : Node
{
    public long Delivered;
    public long Sink;
    public long LastEchoSeen = -1;
    /// <summary>Experiment S-25c: dispose the wrapper at the end of _Input (mitigation candidate).</summary>
    public bool DisposeAfter;

    public override void _Input(InputEvent @event)
    {
        Delivered++;
        switch (@event)
        {
            case InputEventKey k:
                Sink += k.Device + (long)k.Keycode + (long)k.PhysicalKeycode + (k.Pressed ? 1 : 0) + (k.Echo ? 1 : 0);
                break;
            case InputEventMouseMotion m:
                Sink += m.Device + (long)m.ScreenRelative.X + (long)m.ScreenRelative.Y;
                break;
            case InputEventJoypadMotion j:
                Sink += j.Device + (long)j.Axis + (long)(j.AxisValue * 1000f);
                break;
            case InputEventMouseButton b:
                Sink += b.Device + (long)b.ButtonIndex + (b.Pressed ? 1 : 0);
                break;
        }
        if (DisposeAfter) { @event.Dispose(); }
    }
}
