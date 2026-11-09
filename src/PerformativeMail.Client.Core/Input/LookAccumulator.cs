using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The absolute look (ADR-0012 section 5, GDD R11): mouse counts × sensitivity and right-stick rate × dt accumulate into BAM
/// with carried sub-BAM remainders, so no fraction of a turn is ever lost. Yaw wraps; pitch is clamped to the camera owner's
/// limits (a clamped axis drops its remainder). Signs: mouse or stick right turns right (yaw falls), mouse or stick down looks
/// down (pitch falls); each invert setting flips its axis. Integer-only; 0 B per call.
/// </summary>
internal sealed class LookAccumulator
{
    private const long MouseDen = (long)InputDefaults.MouseCountsPerTurn * 1000;
    private const long StickDen = (long)TrigQ15.One * 1000 * 1000;

    private int _pitch;
    private long _mouseYawRem;
    private long _mousePitchRem;
    private long _stickYawRem;
    private long _stickPitchRem;

    /// <summary>Starts at yaw 0, pitch 0, with the given pitch limits.</summary>
    /// <param name="minPitch">Lowest pitch, signed BAM.</param>
    /// <param name="maxPitch">Highest pitch, signed BAM.</param>
    public LookAccumulator(short minPitch, short maxPitch)
    {
        MinPitch = minPitch;
        MaxPitch = maxPitch;
    }

    /// <summary>Absolute yaw, BAM.</summary>
    public ushort Yaw { get; private set; }

    /// <summary>Absolute pitch, signed BAM, inside the limits.</summary>
    public short Pitch => (short)_pitch;

    /// <summary>Lowest pitch, signed BAM.</summary>
    public short MinPitch { get; private set; }

    /// <summary>Highest pitch, signed BAM.</summary>
    public short MaxPitch { get; private set; }

    /// <summary>Accumulates relative mouse counts.</summary>
    /// <param name="dx">Counts right.</param>
    /// <param name="dy">Counts down.</param>
    /// <param name="s">The settings (mouse sensitivity and invert).</param>
    public void AddMouse(int dx, int dy, InputSettings s)
    {
        long sens = (long)s.MouseSensitivityPm * Bam.Turn;
        Turn(Step(-Sign(s.InvertMouseX) * dx * sens, MouseDen, ref _mouseYawRem));
        Tilt(Step(-Sign(s.InvertMouseY) * dy * sens, MouseDen, ref _mousePitchRem), ref _mousePitchRem);
    }

    /// <summary>Integrates a curved right-stick vector over one rendered frame.</summary>
    /// <param name="x">Stick X after F3, Q15 (right).</param>
    /// <param name="y">Stick Y after F3, Q15 (down).</param>
    /// <param name="dtMs">The frame's length, ms.</param>
    /// <param name="s">The settings (stick sensitivity and invert).</param>
    public void AddStick(int x, int y, int dtMs, InputSettings s)
    {
        long rate = (long)InputDefaults.StickLookBamPerSec * dtMs;
        Turn(Step(-Sign(s.InvertStickX) * x * rate * s.StickXSensitivityPm, StickDen, ref _stickYawRem));
        Tilt(Step(-Sign(s.InvertStickY) * y * rate * s.StickYSensitivityPm, StickDen, ref _stickPitchRem), ref _stickPitchRem);
    }

    /// <summary>Re-seeds the absolute look and drops every remainder (Teleport, mount, dismount).</summary>
    /// <param name="yaw">The new yaw, BAM.</param>
    /// <param name="pitch">The new pitch, signed BAM (clamped to the limits).</param>
    public void Reseed(ushort yaw, short pitch)
    {
        Yaw = yaw;
        _pitch = Math.Clamp(pitch, MinPitch, MaxPitch);
        _mouseYawRem = 0;
        _mousePitchRem = 0;
        _stickYawRem = 0;
        _stickPitchRem = 0;
    }

    /// <summary>Sets the pitch limits and clamps the current pitch into them.</summary>
    /// <param name="minPitch">Lowest pitch, signed BAM.</param>
    /// <param name="maxPitch">Highest pitch, signed BAM (at least <paramref name="minPitch"/>).</param>
    public void SetPitchLimits(short minPitch, short maxPitch)
    {
        MinPitch = minPitch;
        MaxPitch = maxPitch;
        _pitch = Math.Clamp(_pitch, minPitch, maxPitch);
    }

    private static int Sign(bool invert) => invert ? -1 : 1;

    // Adds numerator / den to the carried remainder and returns the whole BAM part (floor), keeping 0 <= rem < den.
    private static long Step(long numerator, long den, ref long rem)
    {
        long total = rem + numerator;
        long q = total / den;
        long r = total - (q * den);
        if (r < 0)
        {
            q--;
            r += den;
        }

        rem = r;
        return q;
    }

    private void Turn(long delta) => Yaw = unchecked((ushort)(Yaw + delta));

    private void Tilt(long delta, ref long rem)
    {
        long p = _pitch + delta;
        if (p < MinPitch || p > MaxPitch)
        {
            rem = 0;
        }

        _pitch = (int)Math.Clamp(p, MinPitch, MaxPitch);
    }
}
