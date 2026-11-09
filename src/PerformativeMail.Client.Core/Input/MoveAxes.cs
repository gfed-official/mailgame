using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Movement axis state (story 008; F3 and the sign convention, story 009). Axes are state, not edges (GDD G3): the digital
/// contribution is read from raw held keys of the Move action's composite bindings, and each pad's left stick from its last
/// <see cref="RawKind.PadAxis"/> sample, so no press machine or mute touches them. A stick goes through the F3 deadzone with the
/// move curve fixed at 1.0 and its Y is negated (Godot's stick Y is positive down); the digital vector is pulled onto the unit
/// disc with <see cref="Fix.ISqrt"/> (W+D is (23170, 23170)). The frame's <c>moveIntent</c> is the device vector with the
/// largest magnitude (R9), Q15 on the unit disc, x right, y forward; camera-relative conversion is Player Character's. The
/// router zeroes it outside <see cref="Kept"/>. Preallocated; 0 B per call.
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

    private const int Q15One = TrigQ15.One;

    private readonly int[] _upDownLeftRight = new int[8];
    private readonly int _compositeCount;

    /// <summary>Reads the composite (digital) and axis-pair (stick) bindings of <paramref name="move"/>.</summary>
    /// <param name="move">The Move action.</param>
    public MoveAxes(ActionDef move)
    {
        Stick = new StickSamples(move);
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
        }
    }

    /// <summary>The left-stick samples per pad.</summary>
    public StickSamples Stick { get; }

    /// <summary>The device vector with the largest magnitude; the digital keys win a tie, then the lower pad.</summary>
    /// <param name="held">The raw held table.</param>
    /// <param name="dzInnerPm">Inner deadzone, permille.</param>
    /// <param name="dzOuterPm">Outer deadzone, permille.</param>
    /// <param name="x">Q15 x (right).</param>
    /// <param name="y">Q15 y (forward).</param>
    /// <param name="fromPad">True when a pad's stick won (the keys win a tie, so (0, 0) is never from a pad).</param>
    public void Intent(HeldTable held, int dzInnerPm, int dzOuterPm, out short x, out short y, out bool fromPad)
    {
        fromPad = false;
        Digital(held, out int bx, out int by);
        long best = ((long)bx * bx) + ((long)by * by);
        for (int p = 0; p < DeviceSlots.PadCount; p++)
        {
            Deadzone.Apply(Stick.X(p), Stick.Y(p), dzInnerPm, dzOuterPm, ResponseCurveTable.Linear, out short sx, out short sy);
            long m = ((long)sx * sx) + ((long)sy * sy);
            if (m > best)
            {
                best = m;
                bx = sx;
                by = -sy;
                fromPad = true;
            }
        }

        x = (short)bx;
        y = (short)by;
    }

    // The keys' vector: each axis -1, 0 or +1, a diagonal pulled back onto the unit disc.
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
        if (x != 0 && y != 0)
        {
            int len = (int)Fix.ISqrt((ulong)(((long)x * x) + ((long)y * y)));
            x = x * Q15One / len;
            y = y * Q15One / len;
        }
    }

    private static int Down(HeldTable held, int index) => index >= 0 && held.IsRawHeld(index) ? 1 : 0;
}
