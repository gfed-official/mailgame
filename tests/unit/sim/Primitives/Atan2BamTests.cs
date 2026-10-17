using System.Security.Cryptography;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Primitives;

/// <summary>
/// Covers <see cref="TrigQ15.Atan2Bam"/> and the committed AtanQ15 table (story player-character/003; ADR-0013
/// section 3). The oracle is <see cref="Math.Atan2(double, double)"/> in test code only; the Sim is integer-only.
/// All pseudo-random inputs come from a fixed-seed SplitMix64 implemented here, so runs are deterministic.
/// </summary>
public class Atan2BamTests
{
    /// <summary>SHA-256 of the 2050 little-endian AtanQ15 bytes, recorded when tools/GenTrigQ15 was run.</summary>
    private const string ExpectedAtanSha256 = "59418BF648C36A7998072AEA0D9EF34D3BABB4A734582D70F6A229278CCACB70";

    private const int ToleranceBam = 10;

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A signed value of random bit width 0 ... 31 bits plus sign, so |v| &lt;= 2^31.</summary>
    private static long NextComponent(ref ulong state)
    {
        ulong r = SplitMix64(ref state);
        int bits = (int)(r % 33UL);                              // 0 ... 32
        ulong mask = bits == 0 ? 0UL : (1UL << bits) - 1UL;
        long magnitude = (long)(SplitMix64(ref state) & mask);
        if (magnitude > (1L << 31))
        {
            magnitude = 1L << 31;
        }

        return (r & 0x80) != 0 ? -magnitude : magnitude;
    }

    private static int OracleBam(long y, long x)
    {
        double bam = Math.Atan2((double)y, (double)x) * 65536.0 / (2.0 * Math.PI);
        long rounded = (long)Math.Round(bam, MidpointRounding.AwayFromZero);
        return (int)(((rounded % 65536) + 65536) % 65536);
    }

    private static int CircularDiff(int a, int b)
    {
        int d = Math.Abs(a - b) % 65536;
        return Math.Min(d, 65536 - d);
    }

    private static List<(long Y, long X)> BoundaryVectors()
    {
        var list = new List<(long, long)>();
        long[] magnitudes = { 1, 2, 7, 1000, 65_535, 1L << 20, (1L << 31) - 1, 1L << 31 };
        foreach (long m in magnitudes)
        {
            foreach (long sy in new long[] { -1, 1 })
            {
                list.Add((0, sy * m));                 // x axis, both signs
                list.Add((sy * m, 0));                 // y axis, both signs
                foreach (long sx in new long[] { -1, 1 })
                {
                    list.Add((sy * m, sx * m));        // |y| == |x| diagonals
                    list.Add((sy * m, sx * (m + 1)));  // just either side of the diagonal
                    list.Add((sy * (m + 1), sx * m));
                }
            }
        }

        return list;
    }

    // ---- AC-1: axis vectors ----

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 16_384)]
    [InlineData(0, -1, 32_768)]
    [InlineData(-1, 0, 49_152)]
    [InlineData(0, 0, 0)]
    public void Atan2Bam_AxisVectors_AreExact(long y, long x, int expected)
    {
        // Act
        ushort result = TrigQ15.Atan2Bam(y, x);

        // Assert
        Assert.Equal((ushort)expected, result);
    }

    [Theory]
    [InlineData(1_000_000, 0, 16_384)]
    [InlineData(0, -2_147_483_648L, 32_768)]
    [InlineData(-2_147_483_648L, 0, 49_152)]
    [InlineData(0, 2_147_483_648L, 0)]
    public void Atan2Bam_LargeAxisVectors_AreExact(long y, long x, int expected)
    {
        // Act
        ushort result = TrigQ15.Atan2Bam(y, x);

        // Assert
        Assert.Equal((ushort)expected, result);
    }

    [Theory]
    [InlineData(1, 1, 8_192)]
    [InlineData(1, -1, 24_576)]
    [InlineData(-1, -1, 40_960)]
    [InlineData(-1, 1, 57_344)]
    [InlineData(2_147_483_648L, -2_147_483_648L, 24_576)]
    public void Atan2Bam_DiagonalVectors_AreExactOctantBoundaries(long y, long x, int expected)
    {
        // Act
        ushort result = TrigQ15.Atan2Bam(y, x);

        // Assert
        Assert.Equal((ushort)expected, result);
    }

    // ---- AC-2: accuracy ----

    [Fact]
    public void Atan2Bam_PseudoRandomPairs_StayWithinToleranceOfOracle()
    {
        // Arrange: 20 000 pairs from a fixed-seed generator plus every octant boundary vector.
        ulong state = 0x5EEDC0DEUL;
        var cases = new List<(long Y, long X)>(BoundaryVectors());
        for (int i = 0; i < 20_000; i++)
        {
            long y = NextComponent(ref state);
            long x = NextComponent(ref state);
            cases.Add((y, x));
        }

        // Act
        int maxError = 0;
        (long Y, long X) worst = default;
        foreach ((long y, long x) in cases)
        {
            if (y == 0 && x == 0)
            {
                continue;
            }

            int error = CircularDiff(TrigQ15.Atan2Bam(y, x), OracleBam(y, x));
            if (error > maxError)
            {
                maxError = error;
                worst = (y, x);
            }
        }

        // Assert
        Assert.True(cases.Count >= 10_000);
        Assert.True(maxError <= ToleranceBam, $"max error {maxError} BAM at (y={worst.Y}, x={worst.X})");
    }

    [Fact]
    public void Atan2Bam_ExtremeLongValues_StayWithinToleranceAndDoNotThrow()
    {
        // Arrange: long.MinValue saturates to long.MaxValue magnitude, so the oracle sees the same ratio.
        long[] extremes = { long.MinValue, long.MinValue + 1, -1, 0, 1, long.MaxValue - 1, long.MaxValue };

        foreach (long y in extremes)
        {
            foreach (long x in extremes)
            {
                if (y == 0 && x == 0)
                {
                    continue;
                }

                // Act
                ushort result = TrigQ15.Atan2Bam(y, x);

                // Assert
                Assert.True(CircularDiff(result, OracleBam(y, x)) <= ToleranceBam, $"(y={y}, x={x})");
            }
        }
    }

    // ---- AC-3: symmetry ----

    [Fact]
    public void Atan2Bam_NonZeroVectors_SatisfyExactMirrorIdentities()
    {
        // Arrange
        ulong state = 0xA7A2BA11UL;
        var cases = new List<(long Y, long X)>(BoundaryVectors());
        for (int i = 0; i < 20_000; i++)
        {
            cases.Add((NextComponent(ref state), NextComponent(ref state)));
        }

        foreach ((long y, long x) in cases)
        {
            if (y == 0 && x == 0)
            {
                continue;
            }

            // Act
            ushort angle = TrigQ15.Atan2Bam(y, x);
            ushort mirrorY = TrigQ15.Atan2Bam(-y, x);
            ushort mirrorX = TrigQ15.Atan2Bam(y, -x);

            // Assert
            Assert.Equal(unchecked((ushort)(65_536 - angle)), mirrorY);
            Assert.Equal(unchecked((ushort)(32_768 - angle)), mirrorX);
        }
    }

    // ---- AC-4: round trip with the ADR convention ----

    [Fact]
    public void Atan2Bam_ForwardVectorRoundTrip_RecoversAngleWithinTolerance()
    {
        // Arrange / Act / Assert: w = (-Sin t, -Cos t) is forward(t); body yaw = Atan2Bam(-wx, -wz).
        // Sin/Cos ignore the low 4 bits of the angle (ADR-0006), so the vector encodes the angle floored to a
        // multiple of 16; the atan stage itself must be within 10 BAM of that, and 10 + 15 BAM of the raw angle.
        for (int theta = 0; theta < 65_536; theta += 257)
        {
            long wx = -TrigQ15.Sin((ushort)theta);
            long wz = -TrigQ15.Cos((ushort)theta);

            ushort yaw = TrigQ15.Atan2Bam(-wx, -wz);

            int quantised = theta & ~15;
            Assert.True(CircularDiff(yaw, quantised) <= ToleranceBam, $"theta {theta}: yaw {yaw} vs table angle {quantised}");
            Assert.True(CircularDiff(yaw, theta) <= ToleranceBam + 15, $"theta {theta}: yaw {yaw}");
        }
    }

    // ---- AC-5: table ----

    [Fact]
    public void AtanTable_Shape_HasEndpointsAndIsNonDecreasing()
    {
        // Act
        ReadOnlySpan<ushort> table = TrigQ15.AtanTable;

        // Assert
        Assert.Equal(1_025, table.Length);
        Assert.Equal(TrigQ15.AtanTableLength, table.Length);
        Assert.Equal(0, table[0]);
        Assert.Equal(8_192, table[1_024]);
        for (int i = 1; i < table.Length; i++)
        {
            Assert.True(table[i] >= table[i - 1], $"entry {i} dropped below entry {i - 1}");
        }
    }

    [Fact]
    public void AtanTable_LittleEndianBytes_MatchRecordedSha256()
    {
        // Arrange
        ReadOnlySpan<ushort> table = TrigQ15.AtanTable;
        byte[] bytes = new byte[table.Length * 2];
        for (int i = 0; i < table.Length; i++)
        {
            bytes[2 * i] = (byte)(table[i] & 0xFF);
            bytes[2 * i + 1] = (byte)(table[i] >> 8);
        }

        // Act
        string digest = Convert.ToHexString(SHA256.HashData(bytes));

        // Assert
        Assert.Equal(2_050, bytes.Length);
        Assert.Equal(ExpectedAtanSha256, digest);
    }

    internal static long Exercise()
    {
        long acc = 0;
        ReadOnlySpan<ushort> table = TrigQ15.AtanTable;
        for (int i = 0; i < table.Length; i++)
        {
            acc += table[i];
        }

        for (int i = 0; i < 100_000; i++)
        {
            long y = (i * 7919L) - 350_000;
            long x = (i * 104_729L % 1_000_003L) - 500_000;
            acc += TrigQ15.Atan2Bam(y, x);
        }

        return acc;
    }
}

/// <summary>
/// Zero-allocation guardrail for <see cref="TrigQ15.Atan2Bam"/> and the AtanQ15 table, in the non-parallel
/// collection so runtime warm-up on other test threads cannot land in a measured window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class Atan2BamAllocationTests
{
    [Fact]
    public void Atan2Bam_WarmedUp_AllocatesNothing()
    {
        // Arrange: same TD-008 precedent as TrigQ15AllocationTests (unoptimised Sim allocates in CreateSpan).
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(TrigQ15).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: RuntimeHelpers.CreateSpan (ReadOnlySpan literal table) allocates ~64 B per call at Tier-0/unoptimised JIT; the 0 B guardrail applies to optimised builds only.");
        long sink = 0;

        // Act + Assert (table read + 100 000 Atan2Bam calls per window)
        _ = AllocAssert.Zero("AtanTable read and 100 000 Atan2Bam calls", () => sink += Atan2BamTests.Exercise());

        // Assert
        Assert.NotEqual(long.MinValue, sink);
    }
}
