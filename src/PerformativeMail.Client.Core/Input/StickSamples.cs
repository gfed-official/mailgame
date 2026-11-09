namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The last raw sample of one stick per pad (axes are state, GDD G3), stored as the adapter feeds it: Q15, Godot's sign
/// convention (x right, y down). The adapter caches per-device axes and sends each change (ADR-0012 Step 5.5 note 5); this keeps
/// the router's copy. Preallocated; 0 B per call.
/// </summary>
internal sealed class StickSamples
{
    private readonly short[] _x = new short[DeviceSlots.PadCount];
    private readonly short[] _y = new short[DeviceSlots.PadCount];
    private readonly PhysicalInput _xInput;
    private readonly PhysicalInput _yInput;

    /// <summary>Reads the first axis-pair binding of <paramref name="action"/> (none: every sample is ignored).</summary>
    /// <param name="action">The axis action (Move or Look).</param>
    public StickSamples(ActionDef action)
    {
        ReadOnlySpan<InputBinding> bindings = [action.PadPrimary, action.PadSecondary, action.KbmPrimary, action.KbmSecondary];
        foreach (InputBinding b in bindings)
        {
            if (b.Kind == BindingKind.AxisPair)
            {
                _xInput = b.AxisX;
                _yInput = b.AxisY;
                return;
            }
        }
    }

    /// <summary>Raw X of pad <paramref name="pad"/>, Q15.</summary>
    /// <param name="pad">The pad index.</param>
    /// <returns>The last sample.</returns>
    public short X(int pad) => _x[pad];

    /// <summary>Raw Y of pad <paramref name="pad"/>, Q15 (positive down).</summary>
    /// <param name="pad">The pad index.</param>
    /// <returns>The last sample.</returns>
    public short Y(int pad) => _y[pad];

    /// <summary>Stores a <see cref="RawKind.PadAxis"/> sample when it is one of this stick's axes.</summary>
    /// <param name="r">The record.</param>
    /// <returns>True when the sample was stored.</returns>
    public bool Sample(in RawInputRecord r)
    {
        if (r.PadIndex >= DeviceSlots.PadCount || r.Input == PhysicalInput.None)
        {
            return false;
        }

        if (r.Input == _xInput)
        {
            _x[r.PadIndex] = r.Value;
            return true;
        }

        if (r.Input == _yInput)
        {
            _y[r.PadIndex] = r.Value;
            return true;
        }

        return false;
    }
}
