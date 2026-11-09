namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Where the camera owner pushes the focus cone threshold it derives from the FOV and aspect (Player Character 023, ADR-0014
/// section 2). <see cref="InputRouter"/> implements it and carries the value in every <c>InputState.FocusCosMinPm</c>.
/// </summary>
/// <example><code>IFocusConeSink sink = router; sink.SetFocusCosMinPm(FocusCone.CosMinPm(fovDeg, aspectPm));</code></example>
public interface IFocusConeSink
{
    /// <summary>Sets the focus cone threshold; it holds until the next call.</summary>
    /// <param name="pm">The cosine of the cone half-angle in permille (clamped to -1000..1000).</param>
    void SetFocusCosMinPm(int pm);
}
