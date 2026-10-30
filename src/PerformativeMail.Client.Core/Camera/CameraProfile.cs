namespace PerformativeMail.Client.Core.Camera;

/// <summary>
/// One camera rig profile (GDD player-character-movement P9, PCM-P9-1; Presentation only, never replicated): pivot height above
/// the feet, desired boom, shoulder offset (right side, before <see cref="CameraSettings.ShoulderSign"/>) and look sensitivity.
/// The three presets are the P9 values; Throwing owns the aim values and reuses <see cref="Aim"/>. Integer mm and permille only.
/// </summary>
/// <example>
/// <code>
/// CameraProfile p = swimming ? CameraProfile.Swim : CameraBlend.Lerp(CameraProfile.Default, CameraProfile.Aim, aimHeldMs);
/// </code>
/// </example>
public readonly struct CameraProfile
{
    /// <summary>Creates a profile.</summary>
    /// <param name="pivotHeightMm">Pivot height above the feet, mm.</param>
    /// <param name="boomMm">Desired boom length, mm.</param>
    /// <param name="shoulderMm">Shoulder offset along the camera's right vector, mm.</param>
    /// <param name="lookSensitivityPm">Look sensitivity multiplier, permille.</param>
    public CameraProfile(int pivotHeightMm, int boomMm, int shoulderMm, int lookSensitivityPm)
    {
        PivotHeightMm = pivotHeightMm;
        BoomMm = boomMm;
        ShoulderMm = shoulderMm;
        LookSensitivityPm = lookSensitivityPm;
    }

    /// <summary>On foot: pivot 1600, boom 3000, shoulder +600, sensitivity 1000 permille.</summary>
    public static CameraProfile Default => new(1600, 3000, 600, 1000);

    /// <summary>Throwing's aim held: shoulder +750, boom 2400, sensitivity 600 permille (<c>aimCamMult</c>).</summary>
    public static CameraProfile Aim => new(1600, 2400, 750, 600);

    /// <summary>Swimming: the pivot lowered 0.5 m to 1100; the water-plane clamp is <see cref="CameraPose.Compose"/>'s.</summary>
    public static CameraProfile Swim => new(1100, 3000, 600, 1000);

    /// <summary>Pivot height above the feet, mm.</summary>
    public int PivotHeightMm { get; }

    /// <summary>Desired boom length, mm.</summary>
    public int BoomMm { get; }

    /// <summary>Shoulder offset along the right vector, mm (right shoulder positive).</summary>
    public int ShoulderMm { get; }

    /// <summary>Look sensitivity multiplier, permille.</summary>
    public int LookSensitivityPm { get; }
}
