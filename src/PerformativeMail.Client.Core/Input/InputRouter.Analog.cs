using PerformativeMail.Client.Core.Camera;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Analog input (story 009; ADR-0012 section 5, GDD F3, F4, R9, R11). Triggers become logical button presses through F4.
/// Mouse motion turns the absolute look at event time; the right stick goes through F3 with the look curve and integrates
/// <c>rate × dt</c> once per rendered frame at <see cref="FlushToLatch"/>. Look is live only where the Look action is (not in
/// Panel or Pause): elsewhere input is ignored and the value held. <see cref="SetLook"/> re-seeds it and raises
/// <see cref="InputFrameFlags.LookReseeded"/> for one flush. The camera owner pushes the focus cone and the pitch limits.
/// </summary>
public sealed partial class InputRouter : IFocusConeSink
{
    private readonly StickSamples _lookStick;
    private readonly LookAccumulator _lookAcc = new(CameraSettings.MinPitchBam, CameraSettings.MaxPitchBam);
    private ResponseCurveTable _lookCurve;
    private long _lastFlushMs;
    private bool _lookReseeded;
    private short _focusCosMinPm;

    /// <summary>The current absolute look; mouse motion moves it at event time, the right stick at each flush.</summary>
    public LookState Look => new(_lookAcc.Yaw, _lookAcc.Pitch);

    /// <summary>
    /// Re-seeds the absolute look (Sim-side look writes: Teleport, mount, dismount). Remainders are dropped, the next flush
    /// carries <see cref="InputFrameFlags.LookReseeded"/>, and later motion continues from the new value.
    /// </summary>
    /// <param name="yaw">The new yaw, BAM.</param>
    /// <param name="pitch">The new pitch, signed BAM (clamped to the pitch limits).</param>
    public void SetLook(ushort yaw, short pitch)
    {
        _lookAcc.Reseed(yaw, pitch);
        _lookReseeded = true;
    }

    /// <summary>Sets the focus cone threshold every later <c>InputState.FocusCosMinPm</c> carries (ADR-0012 Key Interfaces).</summary>
    /// <param name="focusCosMinPm">The cosine of the cone half-angle, permille.</param>
    public void SetViewParams(short focusCosMinPm) => _focusCosMinPm = focusCosMinPm;

    /// <inheritdoc />
    void IFocusConeSink.SetFocusCosMinPm(int pm) => SetViewParams((short)Math.Clamp(pm, -1000, 1000));

    /// <summary>Sets the pitch limits the look is clamped to (pushed by the camera owner; default −60 to +70 degrees).</summary>
    /// <param name="minBam">Lowest pitch, signed BAM.</param>
    /// <param name="maxBam">Highest pitch, signed BAM.</param>
    /// <exception cref="ArgumentException"><paramref name="minBam"/> is above <paramref name="maxBam"/>.</exception>
    public void SetPitchLimits(short minBam, short maxBam)
    {
        if (minBam > maxBam)
        {
            throw new ArgumentException("The lowest pitch is above the highest.", nameof(minBam));
        }

        _lookAcc.SetPitchLimits(minBam, maxBam);
    }

    /// <summary>
    /// Replaces the settings. Call it outside the frame (on a settings change): a new <c>curvePm</c> rebuilds the look curve
    /// table, which allocates.
    /// </summary>
    /// <param name="settings">The new settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    public void ApplySettings(InputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.CurvePm != _lookCurve.CurvePm)
        {
            _lookCurve = ResponseCurveTable.Build(settings.CurvePm);
        }

        Settings = settings;
    }

    // A pad axis: a trigger crossing its F4 threshold is a button press or release; a stick sample is stored.
    private void FeedAxis(in RawInputRecord r)
    {
        if (r.Input is PhysicalInput.PadLeftTrigger or PhysicalInput.PadRightTrigger)
        {
            int index = DeviceSlots.IndexOf(r.Input, r.PadIndex);
            if (index < 0)
            {
                return;
            }

            bool was = _held.IsRawHeld(index);
            bool now = TriggerHysteresis.IsPressed(was, r.Value, Settings.TriggerOnPm);
            if (now != was)
            {
                var button = new RawInputRecord(r.ArrivalMs, RawKind.PadButton, r.Device, r.PadIndex, r.Input, now, echo: false);
                FeedButton(index, in button);
            }

            return;
        }

        if (!_moveAxes.Stick.Sample(in r))
        {
            _ = _lookStick.Sample(in r);
        }
    }

    private void FeedMouseMotion(in RawInputRecord r)
    {
        if (_look.IsLive(_state))
        {
            _lookAcc.AddMouse(r.Value, r.Value2, Settings);
        }
    }

    // Once per rendered frame: the right stick with the largest F3 output turns the look by rate × dt.
    private void IntegrateStickLook(long nowMs)
    {
        int dtMs = (int)Math.Clamp(nowMs - _lastFlushMs, 0, InputDefaults.StickLookMaxDtMs);
        _lastFlushMs = nowMs;
        if (dtMs == 0 || !_look.IsLive(_state))
        {
            return;
        }

        long best = 0;
        int bx = 0;
        int by = 0;
        for (int p = 0; p < DeviceSlots.PadCount; p++)
        {
            Deadzone.Apply(_lookStick.X(p), _lookStick.Y(p), Settings.DzInnerPm, Settings.DzOuterPm, _lookCurve, out short sx, out short sy);
            long m = ((long)sx * sx) + ((long)sy * sy);
            if (m > best)
            {
                best = m;
                bx = sx;
                by = sy;
            }
        }

        if (best != 0)
        {
            _lookAcc.AddStick(bx, by, dtMs, Settings);
        }
    }
}
