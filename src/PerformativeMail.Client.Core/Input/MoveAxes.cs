using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Movement axis state (story 008's minimum; story 009 adds the F3 deadzone and response curve). Axes are state, not edges
/// (GDD G3): the digital contribution is read from raw held keys of the Move action's composite bindings, and each pad's left
/// stick from its last <see cref="RawKind.PadAxis"/> sample, so no press machine or mute touches them. The frame's
/// <c>moveIntent</c> is the device vector with the largest magnitude (R9), Q15 on the unit disc. The router zeroes it outside
/// <see cref="Kept"/>. Preallocated; 0 B per call.
/// </summary>
internal sealed class MoveAxes
{
    /// <summary>
    /// Where move intent is kept (GDD G3 axes clause): everywhere except capture, Map, Draft, a turret seat (Mounted; vehicle
    /// seats steer with their own actions, M1) and Dead. Holding-open keeps it (H-G3-3) although the R6 Move row does not list
    /// it as a live state; that row gates presses of the Move binding, not the axis state.
    /// </summary>
    public static readonly LivePredicate Kept = LivePredicate.Any with
    {
        CaptureMask = CaptureMask.None,
        OverlayMask = (ushort)(OverlayMask.All & ~(OverlayMask.Map | OverlayMask.Draft)),
        BodyMask = BodyMask.OnFoot | BodyMask.Build,
    };

    private const int Q15One = 32767;

    private readonly int[] _upDownLeftRight = new int[8];
    private readonly short[] _stickX = new short[DeviceSlots.PadCount];
    private readonly short[] _stickY = new short[DeviceSlots.PadCount];
    private readonly PhysicalInput _stickXInput;
    private readonly PhysicalInput _stickYInput;
    private readonly int _compositeCount;

    /// <summary>Reads the composite (digital) and axis-pair (stick) bindings of <paramref name="move"/>.</summary>
    /// <param name="move">The Move action.</param>
    public MoveAxes(ActionDef move)
    {
        ReadOnlySpan<InputBinding> bindings = [move.KbmPrimary, move.KbmSecondary, move.PadPrimary, move.PadSecondary];
        foreach (InputBinding b in bindings)
        {
            if (b.Kind == BindingKind.Composite4 && _compositeCount < 2)
            {
                int o = _compositeCount * 4;
                _upDownLeftRight[o] = DeviceSlots.IndexOf(b.Up, 0);
                _upDownLeftRight[o + 1] = DeviceSlots.IndexOf(b.Down, 0);
                _upDownLeftRight[o + 2] = DeviceSlots.IndexOf(b.Left, 0);
                _upDownLeftRight[o + 3] = DeviceSlots.IndexOf(b.Right, 0);
                _compositeCount++;
            }
            else if (b.Kind == BindingKind.AxisPair && _stickXInput == PhysicalInput.None)
            {
                _stickXInput = b.AxisX;
                _stickYInput = b.AxisY;
            }
        }
    }

    /// <summary>Stores a pad axis sample when it is one of Move's stick axes.</summary>
    /// <param name="r">A <see cref="RawKind.PadAxis"/> record.</param>
    public void Sample(in RawInputRecord r)
    {
        if (r.PadIndex >= DeviceSlots.PadCount || r.Input == PhysicalInput.None)
        {
            return;
        }

        if (r.Input == _stickXInput)
        {
            _stickX[r.PadIndex] = r.Value;
        }
        else if (r.Input == _stickYInput)
        {
            _stickY[r.PadIndex] = r.Value;
        }
    }

    /// <summary>The device vector with the largest magnitude; the digital keys win a tie, then the lower pad.</summary>
    /// <param name="held">The raw held table.</param>
    /// <param name="x">Q15 x (right).</param>
    /// <param name="y">Q15 y (forward, as the record carries it).</param>
    public void Intent(HeldTable held, out short x, out short y)
    {
        Digital(held, out int bx, out int by);
        long best = ((long)bx * bx) + ((long)by * by);
        for (int p = 0; p < DeviceSlots.PadCount; p++)
        {
            long m = ((long)_stickX[p] * _stickX[p]) + ((long)_stickY[p] * _stickY[p]);
            if (m > best)
            {
                best = m;
                bx = _stickX[p];
                by = _stickY[p];
            }
        }

        // A stick corner past the unit disc is pulled back onto it.
        if (best > (long)Q15One * Q15One)
        {
            int len = (int)Fix.ISqrt((ulong)best);
            bx = bx * Q15One / len;
            by = by * Q15One / len;
        }

        x = (short)bx;
        y = (short)by;
    }

    private void Digital(HeldTable held, out int x, out int y)
    {
        x = 0;
        y = 0;
        for (int c = 0; c < _compositeCount; c++)
        {
            int o = c * 4;
            y += Down(held, _upDownLeftRight[o]) - Down(held, _upDownLeftRight[o + 1]);
            x += Down(held, _upDownLeftRight[o + 3]) - Down(held, _upDownLeftRight[o + 2]);
        }

        x = Math.Sign(x) * Q15One;
        y = Math.Sign(y) * Q15One;
    }

    private static int Down(HeldTable held, int index) => index >= 0 && held.IsRawHeld(index) ? 1 : 0;
}
