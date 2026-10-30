using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Camera;

/// <summary>
/// The aim ease (GDD player-character-movement P9; TD-198 item 5, approved by Evan 2026-10-29): every profile field moves
/// linearly from <c>from</c> to <c>to</c> over <see cref="BlendMs"/> of render time (an <c>IRenderClock.RenderSimTimeMs</c>
/// difference). 0 ms (or less) is <c>from</c>, 100 ms or more is <c>to</c>. Pure, integer-only, allocation-free.
/// </summary>
/// <example>
/// <code>
/// CameraProfile p = CameraBlend.Lerp(CameraProfile.Default, CameraProfile.Aim, 50);   // shoulder 675, boom 2700
/// </code>
/// </example>
public static class CameraBlend
{
    /// <summary>Blend duration, ms.</summary>
    public const int BlendMs = 100;

    /// <summary>The profile <paramref name="elapsedMs"/> into the blend from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">The profile at 0 ms.</param>
    /// <param name="to">The profile at <see cref="BlendMs"/>.</param>
    /// <param name="elapsedMs">Render time since the blend started, ms.</param>
    /// <returns>The blended profile.</returns>
    public static CameraProfile Lerp(in CameraProfile from, in CameraProfile to, long elapsedMs)
    {
        if (elapsedMs <= 0)
        {
            return from;
        }

        if (elapsedMs >= BlendMs)
        {
            return to;
        }

        // Mail Dev default, pending Evan (TD-201 item 8): the aim blend also blends pivot height and the look multiplier linearly.
        return new CameraProfile(
            Mix(from.PivotHeightMm, to.PivotHeightMm, elapsedMs),
            Mix(from.BoomMm, to.BoomMm, elapsedMs),
            Mix(from.ShoulderMm, to.ShoulderMm, elapsedMs),
            Mix(from.LookSensitivityPm, to.LookSensitivityPm, elapsedMs));
    }

    private static int Mix(int a, int b, long elapsedMs)
    {
        var sink = default(SimCounterSink);
        return checked((int)(a + Fix.MulDiv((long)b - a, elapsedMs, BlendMs, ref sink)));
    }
}
