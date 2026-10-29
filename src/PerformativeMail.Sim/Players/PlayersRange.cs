namespace PerformativeMail.Sim.Players;

/// <summary>The overflow-safe 3D distance comparison the host range check and the leash use (GDD F5: squared, integer, inclusive).</summary>
internal static class PlayersRange
{
    /// <summary>True when <c>|(dx, dy, dz)| &lt;= limitMm</c> (a limit above <c>int.MaxValue</c> is read as <c>int.MaxValue</c>); never overflows.</summary>
    /// <param name="dx">X difference, mm.</param>
    /// <param name="dy">Y difference, mm.</param>
    /// <param name="dz">Z difference, mm.</param>
    /// <param name="limitMm">The limit, mm; negative is never within.</param>
    /// <returns>True when within.</returns>
    public static bool Within(long dx, long dy, long dz, long limitMm)
    {
        limitMm = Math.Min(limitMm, int.MaxValue);
        if (limitMm < 0 || Math.Abs(dx) > limitMm || Math.Abs(dy) > limitMm || Math.Abs(dz) > limitMm)
        {
            return false;
        }

        // Each |d| <= limit < 2^31, so each square is below 2^62 and the sum of three fits a ulong.
        ulong sum = (ulong)(dx * dx) + (ulong)(dy * dy) + (ulong)(dz * dz);
        return sum <= (ulong)limitMm * (ulong)limitMm;
    }
}
