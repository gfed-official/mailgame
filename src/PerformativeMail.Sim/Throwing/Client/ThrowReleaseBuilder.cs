namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Builds the <c>ThrowRelease</c> payload from the last published aim preview (GDD T4 "Release"; ADR-0015 section 1; throwing-catching
/// story 007): the latched entry and units, the claimed feet, the three published direction integers and the hand, <c>flags = 0</c>. It
/// never re-quantises or recomputes the direction: what was drawn is what is sent. Pure, allocation-free.
/// </summary>
/// <example><code>if (ThrowReleaseBuilder.TryBuild(in preview.Current, out ThrowReleasePayload p)) { op.SetPayload(p); }</code></example>
public static class ThrowReleaseBuilder
{
    /// <summary>Builds the payload, or refuses when the state has no arc to throw along.</summary>
    /// <param name="s">The published preview state.</param>
    /// <param name="payload">The payload; <c>default</c> on refusal.</param>
    /// <returns>False when the state is inactive, not throwable or has no arc (nothing is sent).</returns>
    public static bool TryBuild(in ThrowPreviewState s, out ThrowReleasePayload payload)
    {
        payload = default;
        if (s.Active != 1 || s.NotThrowable != NotThrowable.None || s.PointCount == 0)
        {
            return false;
        }

        payload = new ThrowReleasePayload
        {
            EntryId = s.Entry.Value,
            Units = s.Units,
            Side = s.Side,
            FeetX = s.Feet.X,
            FeetY = s.Feet.Y,
            FeetZ = s.Feet.Z,
            DirX = s.DirX,
            DirY = s.DirY,
            DirZ = s.DirZ,
        };
        return true;
    }
}
