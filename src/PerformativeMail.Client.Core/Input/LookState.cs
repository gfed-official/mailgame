namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The router's absolute look (ADR-0012 sections 4 and 5, GDD R11): yaw in BAM (65 536 = 360 degrees, wraps; positive turns
/// left, as <c>CarrierHeading</c> and <c>CameraPose</c> read it) and pitch in signed BAM (positive raises the view), already
/// clamped to the camera owner's limits. Never a delta.
/// </summary>
/// <param name="Yaw">Absolute yaw, BAM.</param>
/// <param name="Pitch">Absolute pitch, signed BAM.</param>
/// <example><code>LookState look = router.Look; camera.Apply(look.Yaw, look.Pitch);</code></example>
public readonly record struct LookState(ushort Yaw, short Pitch);
