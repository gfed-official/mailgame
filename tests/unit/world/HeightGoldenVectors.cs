namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// The committed golden-vector table of the height-field kernel (world-stub story 001, AC-2; ADR-0026
/// section 2, T2 surface). Each row is <c>{ h00, h10, h01, h11, fx, fz, expectedMm }</c>. The expected
/// values were produced once by an independent exact-rational oracle (round half away from zero) and
/// are committed literally; they must never be regenerated from the code under test.
/// Rows cover tile corners (0 and 1999), the far-edge offset 2000, edges, the tile centre, and exact
/// halves of both signs (rows 36 to 45) where truncation would give 0.
/// </summary>
internal static class HeightGoldenVectors
{
    /// <summary>Raw rows.</summary>
    public static readonly int[][] Rows =
    {
        new[] { -1000, -1000, -200, -200, 0, 0, -1000 },
        new[] { -1000, -1000, -200, -200, 1999, 0, -1000 },
        new[] { -1000, -1000, -200, -200, 0, 1999, -200 },
        new[] { -1000, -1000, -200, -200, 1999, 1999, -200 },
        new[] { -1000, -1000, -200, -200, 1000, 1000, -600 },
        new[] { -1000, -1000, -200, -200, 1000, 0, -1000 },
        new[] { -1000, -1000, -200, -200, 0, 1000, -600 },
        new[] { -1000, -1000, -200, -200, 1999, 1000, -600 },
        new[] { -1000, -1000, -200, -200, 1000, 1999, -200 },
        new[] { -1000, -1000, -200, -200, 2000, 2000, -200 },
        new[] { -1000, -1000, -200, -200, 500, 1000, -600 },
        new[] { -1000, -1000, -200, -200, 500, 1375, -450 },
        new[] { 100, 300, 500, 900, 0, 0, 100 },
        new[] { 100, 300, 500, 900, 1999, 0, 300 },
        new[] { 100, 300, 500, 900, 0, 1999, 500 },
        new[] { 100, 300, 500, 900, 1999, 1999, 900 },
        new[] { 100, 300, 500, 900, 1000, 1000, 450 },
        new[] { 100, 300, 500, 900, 1000, 0, 200 },
        new[] { 100, 300, 500, 900, 0, 1000, 300 },
        new[] { 100, 300, 500, 900, 1999, 1000, 600 },
        new[] { 100, 300, 500, 900, 1000, 1999, 700 },
        new[] { 100, 300, 500, 900, 2000, 2000, 900 },
        new[] { 100, 300, 500, 900, 500, 1000, 375 },
        new[] { 100, 300, 500, 900, 500, 1375, 459 },
        new[] { -1234, 567, -89, 4321, 0, 0, -1234 },
        new[] { -1234, 567, -89, 4321, 1999, 0, 566 },
        new[] { -1234, 567, -89, 4321, 0, 1999, -90 },
        new[] { -1234, 567, -89, 4321, 1999, 1999, 4317 },
        new[] { -1234, 567, -89, 4321, 1000, 1000, 891 },
        new[] { -1234, 567, -89, 4321, 1000, 0, -334 },
        new[] { -1234, 567, -89, 4321, 0, 1000, -662 },
        new[] { -1234, 567, -89, 4321, 1999, 1000, 2442 },
        new[] { -1234, 567, -89, 4321, 1000, 1999, 2115 },
        new[] { -1234, 567, -89, 4321, 2000, 2000, 4321 },
        new[] { -1234, 567, -89, 4321, 500, 1000, 115 },
        new[] { -1234, 567, -89, 4321, 500, 1375, 452 },
        new[] { 2, 0, 0, 0, 1000, 1000, 1 },
        new[] { -2, 0, 0, 0, 1000, 1000, -1 },
        new[] { 1, 0, 0, 0, 1000, 1000, 0 },
        new[] { -1, 0, 0, 0, 1000, 1000, 0 },
        new[] { 0, 0, 0, 2, 1000, 1000, 1 },
        new[] { 0, 0, -2, 0, 1000, 1000, -1 },
        new[] { 3, 0, 0, 0, 1000, 1000, 1 },
        new[] { -3, 0, 0, 0, 1000, 1000, -1 },
        new[] { 1, 1, 0, 0, 1000, 0, 1 },
        new[] { 0, 0, 1, 1, 0, 1000, 1 },
    };

    /// <summary>The rows as xUnit theory data.</summary>
    public static IEnumerable<object[]> TheoryRows()
    {
        foreach (int[] row in Rows)
        {
            yield return new object[] { row[0], row[1], row[2], row[3], row[4], row[5], row[6] };
        }
    }
}
