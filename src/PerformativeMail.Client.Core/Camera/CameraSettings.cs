namespace PerformativeMail.Client.Core.Camera;

/// <summary>
/// The player's camera settings (GDD player-character-movement P9, PCM-P9-1): vertical FOV clamped to 60–100 degrees (default
/// 80), the shoulder side (a swap request flips the sign) and the optional −10 degree aim FOV (off by default). Pitch limits
/// are −60…+70 degrees in signed BAM. Integer only. Main thread only, not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var s = new CameraSettings();
/// s.SetVerticalFov(101);                       // 100
/// s.SwapShoulder();                            // ShoulderSign == -1
/// short pitch = CameraSettings.ClampPitch(look.Pitch);
/// </code>
/// </example>
// Mail Dev default, pending Evan (TD-201 item 5): a mutable sealed class holding FOV, shoulder side and the aim-FOV toggle.
public sealed class CameraSettings
{
    /// <summary>Lowest vertical FOV setting, degrees.</summary>
    public const int MinFovDeg = 60;

    /// <summary>Highest vertical FOV setting, degrees.</summary>
    public const int MaxFovDeg = 100;

    /// <summary>Default vertical FOV, degrees.</summary>
    public const int DefaultFovDeg = 80;

    /// <summary>FOV reduction while aiming when <see cref="AimFovReduction"/> is on, degrees.</summary>
    public const int AimFovReductionDeg = 10;

    /// <summary>Lowest pitch, −60 degrees in signed BAM (65 536 = 360 degrees).</summary>
    public const short MinPitchBam = -10_923;

    /// <summary>Highest pitch, +70 degrees in signed BAM.</summary>
    public const short MaxPitchBam = 12_743;

    /// <summary>Vertical FOV in whole degrees, always within <see cref="MinFovDeg"/>…<see cref="MaxFovDeg"/>.</summary>
    public int VerticalFovDeg { get; private set; } = DefaultFovDeg;

    /// <summary>+1 for the right shoulder (default), −1 after a swap.</summary>
    public int ShoulderSign { get; private set; } = 1;

    /// <summary>Whether aiming narrows the FOV by <see cref="AimFovReductionDeg"/> (off by default).</summary>
    public bool AimFovReduction { get; set; }

    /// <summary>Clamps a FOV setting to 60–100 degrees.</summary>
    /// <param name="fovDeg">The requested FOV.</param>
    /// <returns>The clamped FOV.</returns>
    public static int ClampFov(int fovDeg) => Math.Clamp(fovDeg, MinFovDeg, MaxFovDeg);

    /// <summary>Clamps a signed BAM pitch to −60…+70 degrees.</summary>
    /// <param name="pitchBam">The pitch.</param>
    /// <returns>The clamped pitch.</returns>
    /// <example><code>short p = CameraSettings.ClampPitch(20_000);   // 12 743</code></example>
    public static short ClampPitch(short pitchBam) => Math.Clamp(pitchBam, MinPitchBam, MaxPitchBam);

    /// <summary>Stores a FOV setting, clamped.</summary>
    /// <param name="fovDeg">The requested FOV.</param>
    public void SetVerticalFov(int fovDeg) => VerticalFovDeg = ClampFov(fovDeg);

    /// <summary>A shoulder-swap request: flips <see cref="ShoulderSign"/>.</summary>
    public void SwapShoulder() => ShoulderSign = -ShoulderSign;

    /// <summary>The profile's shoulder offset on the current side.</summary>
    /// <param name="profile">The (blended) profile.</param>
    /// <returns>The signed shoulder offset, mm.</returns>
    public int ShoulderMm(in CameraProfile profile) => checked(profile.ShoulderMm * ShoulderSign);

    /// <summary>The vertical FOV to show: unchanged while aiming unless <see cref="AimFovReduction"/> is on.</summary>
    /// <param name="aiming">Whether Throwing's aim is held.</param>
    /// <returns>FOV in whole degrees.</returns>
    // Mail Dev default, pending Evan (TD-201 item 6): the optional aim -10 degrees is re-clamped to 60-100.
    public int FovDeg(bool aiming) => aiming && AimFovReduction ? ClampFov(VerticalFovDeg - AimFovReductionDeg) : VerticalFovDeg;
}
