using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;
using G = PerformativeMail.Sim.Tests.Unit.ThrowingCatching.ThrowGoldenVectors;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing story 001 (ADR-0015 sections 2 and 3; TR-throwing-001, TR-throwing-004; GDD <c>design/gdd/throwing-catching.md</c>
/// TC-F1-1 a to e, TC-F2-1 a and b, TC-F3-1, Edge Cases): <see cref="Throw.RH"/>, <see cref="LaunchMath.Compute"/> (F1) and
/// <see cref="Ballistic.PositionAt"/> (F2) against the committed <see cref="ThrowGoldenVectors"/>, plus the QA extras
/// (k contract, knob extreme, medium class, chest probe, 0 B per call). The float oracle of TC-F3-1 lives here only; the
/// Sim never sees a float. Non-parallel because of the 0 B guardrail.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class LaunchBallisticTests
{
    public static TheoryData<short, short, short, bool> HostBoundCases()
    {
        var data = new TheoryData<short, short, short, bool>();
        foreach ((short dx, short dy, short dz, bool ok) in G.HostBounds)
        {
            data.Add(dx, dy, dz, ok);
        }

        return data;
    }

    public static TheoryData<long, long, long> RoundHalfUpCases()
    {
        var data = new TheoryData<long, long, long>();
        foreach ((long n, long d, long expected) in G.RoundHalfUp)
        {
            data.Add(n, d, expected);
        }

        return data;
    }

    public static TheoryData<int> RangeTableCases() => new(Enumerable.Range(0, G.RangeTable.Length));

    public static TheoryData<int> DefenceCases() => new(Enumerable.Range(0, G.DefenceOrigins.Length));

    // ------------------------------------------------------------------ TC-F1-1

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void Compute_FxTLightRightHanded_GivesTheGoldenLengthSpeedVelocityAndOrigin()
    {
        // Arrange
        ThrowSettings s = G.DefaultSettings();
        LaunchInput input = G.FxT(s);

        // Act
        bool ok = LaunchMath.Compute(in input, out Launch l, out bool fromChest);
        long n = Fix.ISqrt((ulong)LaunchMath.DirLengthSquared(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz));
        long v = Throw.RH((long)s.BaseSpeed(WeightClass.Light) * Fix.ISqrt(1000UL * 1000UL), 1000);

        // Assert
        Assert.True(ok);
        Assert.False(fromChest);
        Assert.Equal(G.FxTN, n);
        Assert.Equal(G.FxTV, v);
        Assert.Equal(G.FxTVel, l.Vel);
        Assert.Equal(G.FxTOrigin, l.Origin);
        Assert.Equal(9800, l.G);
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void Compute_FxTLeftHanded_MovesTheOriginToX9750()
    {
        // Arrange
        LaunchInput input = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, G.DefaultSettings(), side: -1);

        // Act
        bool ok = LaunchMath.Compute(in input, out Launch l, out _);

        // Assert
        Assert.True(ok);
        Assert.Equal(new Vec3Mm(G.FxTLeftOriginX, 1200, 19_700), l.Origin);
        Assert.Equal(G.FxTVel, l.Vel);
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void Compute_CannonArmPermille1500_GivesSpeedPm1224V17136AndTheGoldenVelocity()
    {
        // Arrange: gating, not skipped (only the perk that sets 1500 is Later)
        ThrowSettings s = G.DefaultSettings();
        LaunchInput input = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, s, permille: G.CannonArm.Permille);

        // Act
        bool ok = LaunchMath.Compute(in input, out Launch l, out _);
        long speedPm = Fix.ISqrt((ulong)G.CannonArm.Permille * 1000UL);
        long v = Throw.RH(s.BaseSpeed(WeightClass.Light) * speedPm, 1000);

        // Assert
        Assert.True(ok);
        Assert.Equal(G.CannonArm.SpeedPm, speedPm);
        Assert.Equal(G.CannonArm.V, v);
        Assert.Equal(G.CannonArm.Vel, l.Vel);
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void Compute_MediumClassAtTheFxTDir_GivesV10000AndTheGoldenVelocity()
    {
        // Arrange
        ThrowSettings s = G.DefaultSettings();
        LaunchInput input = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, s, weightClass: WeightClass.Medium);

        // Act
        bool ok = LaunchMath.Compute(in input, out Launch l, out _);

        // Assert
        Assert.True(ok);
        Assert.Equal(G.Medium.V, Throw.RH(s.BaseSpeed(WeightClass.Medium) * 1000L, 1000));
        Assert.Equal(G.Medium.Vel, l.Vel);
        Assert.Equal(G.FxTOrigin, l.Origin);
    }

    [Theory]
    [MemberData(nameof(RoundHalfUpCases))]
    [Trait("Id", "TC-F1-1")]
    public void ThrowRH_NegativeAndHalfNumerators_RoundHalfTowardPositiveInfinity(long n, long d, long expected)
    {
        // Act
        long actual = Throw.RH(n, d);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void FixCeilAndFloorDiv_NegativeNumerator_RoundAsCeilingAndFloor_NeverTruncate()
    {
        // Arrange
        var sink = default(SimCounterSink);

        // Act
        long ceil = Fix.CeilDiv(-7, 2, ref sink);
        long floor = Fix.FloorDiv(-7, 2, ref sink);

        // Assert
        Assert.Equal(-3, ceil);
        Assert.Equal(-4, floor);
        Assert.NotEqual(-7 / 2, floor); // C# truncation would give -3
    }

    [Theory]
    [MemberData(nameof(HostBoundCases))]
    [Trait("Id", "TC-F1-1")]
    public void Compute_HostBounds_AcceptOnlyLength16000To16768AndElevationMinus876To980(short dx, short dy, short dz, bool accepted)
    {
        // Arrange
        LaunchInput input = G.Input(dx, dy, dz, G.DefaultSettings());

        // Act
        bool ok = LaunchMath.Compute(in input, out Launch l, out bool fromChest);

        // Assert
        Assert.Equal(accepted, ok);
        if (!accepted)
        {
            Assert.Equal(default(Vec3Mm), l.Origin);
            Assert.Equal(default(Vec3Mm), l.Vel);
            Assert.False(fromChest);
        }
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void ElevationVectors_HaveLength16384_AndTheStatedFloorElevation()
    {
        // Arrange
        (short Dy, short Dz, long Elevation)[] rows = { (16_073, -3178, 981), (16_057, -3258, 980), (-14_352, -7903, -876), (-14_368, -7874, -877) };
        var sink = default(SimCounterSink);

        foreach ((short dy, short dz, long elevation) in rows)
        {
            // Act
            long n = Fix.ISqrt((ulong)LaunchMath.DirLengthSquared(0, dy, dz));

            // Assert
            Assert.Equal(16_384, n);
            Assert.Equal(elevation, Fix.FloorDiv(1000L * dy, n, ref sink));
        }
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void DirLengthSquared_Int16Extremes_WidensBeforeSquaring_NoOverflow()
    {
        // Act
        long squared = LaunchMath.DirLengthSquared(-32_768, 32_767, 32_767);

        // Assert
        Assert.Equal(G.ExtremeDirLengthSquared, squared);
        Assert.Equal(56_754u, Fix.ISqrt((ulong)squared));
        Assert.True(unchecked((-32_768 * -32_768) + (32_767 * 32_767) + (32_767 * 32_767)) < 0); // what int32 squaring would give
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void Compute_BulkOrInvalidClassNonPositivePermilleSideNotPlusMinusOneOrDefaultInput_Rejects()
    {
        // Arrange
        ThrowSettings s = G.DefaultSettings();
        LaunchInput bulk = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, s, weightClass: WeightClass.Bulk);
        LaunchInput invalid = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, s, weightClass: (WeightClass)0);
        LaunchInput zeroPermille = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, s, permille: 0);
        LaunchInput none = default;
        LaunchInput sideZero = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, s, side: 0);
        LaunchInput sideHostile = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, s, side: -128);
        LaunchInput sideTwo = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, s, side: 2);

        // Act + Assert
        Assert.Equal(0, s.BaseSpeed(WeightClass.Bulk));
        Assert.Equal(0, s.BaseSpeed((WeightClass)0));
        Assert.False(LaunchMath.Compute(in bulk, out _, out _));
        Assert.False(LaunchMath.Compute(in invalid, out _, out _));
        Assert.False(LaunchMath.Compute(in zeroPermille, out _, out _));
        Assert.False(LaunchMath.Compute(in none, out _, out _));
        Assert.False(LaunchMath.Compute(in sideZero, out _, out _));
        Assert.False(LaunchMath.Compute(in sideHostile, out _, out _));
        Assert.False(LaunchMath.Compute(in sideTwo, out _, out _));
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void ThrowSettings_FromTheBuiltInRecord_CopiesTheGddDefaults()
    {
        // Act
        ThrowSettings s = G.DefaultSettings();

        // Assert
        Assert.Equal((14_000, 10_000, 6000), (s.BaseSpeed(WeightClass.Light), s.BaseSpeed(WeightClass.Medium), s.BaseSpeed(WeightClass.Heavy)));
        Assert.Equal((9800, 90, 1200, 300, 250), (s.G, s.MaxFlightTicks, s.HandHeightMm, s.FwdOffMm, s.SideOffMm));
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void Compute_ChestProbeBlocked_StartsAtTheChestPoint_AndReportsOriginFromChest()
    {
        // Arrange
        var probe = new RecordingProbe(blocked: true);
        LaunchInput input = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, G.DefaultSettings(), probe: probe);

        // Act
        bool ok = LaunchMath.Compute(in input, out Launch l, out bool fromChest);

        // Assert
        Assert.True(ok);
        Assert.True(fromChest);
        Assert.Equal(new Vec3Mm(10_000, 1000, 20_000), l.Origin);
        Assert.Equal(G.FxTVel, l.Vel);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(new Vec3Mm(10_000, 1000, 20_000), probe.Chest);
        Assert.Equal(G.FxTOrigin, probe.Origin);
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void Compute_ChestProbeClearOrDefault_KeepsTheHandOrigin()
    {
        // Arrange
        var clear = new RecordingProbe(blocked: false);
        LaunchInput withClear = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, G.DefaultSettings(), probe: clear);
        LaunchInput withDefault = G.FxT(G.DefaultSettings());

        // Act
        LaunchMath.Compute(in withClear, out Launch a, out bool fromChestA);
        LaunchMath.Compute(in withDefault, out Launch b, out bool fromChestB);

        // Assert
        Assert.Same(NoChestProbe.Instance, withDefault.ChestProbe);
        Assert.False(fromChestA);
        Assert.False(fromChestB);
        Assert.Equal(G.FxTOrigin, a.Origin);
        Assert.Equal(G.FxTOrigin, b.Origin);
    }

    // ------------------------------------------------------------------ Defence branch

    [Theory]
    [MemberData(nameof(DefenceCases))]
    [Trait("Id", "TC-F1-1")]
    public void OriginFor_HorizontalLengthBelow2048_UsesTheBodyYawBasis_Deterministically(int row)
    {
        // Arrange
        (short dx, short dz, ushort yaw, Vec3Mm expected) = G.DefenceOrigins[row];
        ThrowSettings s = G.DefaultSettings();
        Vec3Mm feet = G.Feet;

        // Act
        Vec3Mm first = LaunchMath.OriginFor(in feet, dx, dz, 1, yaw, s);
        Vec3Mm second = LaunchMath.OriginFor(in feet, dx, dz, 1, yaw, s);

        // Assert
        Assert.Equal(expected, first);
        Assert.Equal(first, second);
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void OriginFor_AtTheFxTDir_IsTheOriginComputeReturns()
    {
        // Arrange
        ThrowSettings s = G.DefaultSettings();
        Vec3Mm feet = G.Feet;

        // Act
        Vec3Mm origin = LaunchMath.OriginFor(in feet, G.FxTDir.Dx, G.FxTDir.Dz, 1, 0, s);

        // Assert
        Assert.Equal(G.FxTOrigin, origin);
    }

    // ------------------------------------------------------------------ TC-F2-1

    [Fact]
    [Trait("Id", "TC-F2-1")]
    public void PositionAt_FxTFlight_MatchesTheGoldenP1P20P21()
    {
        // Arrange
        Launch l = FxTLaunch();

        foreach ((int k, Vec3Mm expected) in G.FxTPositions)
        {
            // Act
            Vec3Mm p = Ballistic.PositionAt(in l, k);

            // Assert
            Assert.Equal(expected, p);
        }
    }

    [Fact]
    [Trait("Id", "TC-F2-1")]
    public void PositionAt_K90_GravityDropTermIs44100()
    {
        // Arrange: zero velocity isolates the gravity term
        var still = new Launch(new Vec3Mm(0, 0, 0), new Vec3Mm(0, 0, 0), 9800);

        // Act
        Vec3Mm p = Ballistic.PositionAt(in still, 90);

        // Assert
        Assert.Equal(G.DropTermK90, Throw.RH(9800L * 90 * 90, 1800));
        Assert.Equal(new Vec3Mm(0, -(int)G.DropTermK90, 0), p);
    }

    [Fact]
    [Trait("Id", "TC-F2-1")]
    public void PositionAt_KnobExtremeG14000AtK150_FitsIntAndMatchesTheGolden()
    {
        // Arrange
        LaunchInput input = G.FxT(G.Settings(b => b with { G = G.KnobExtreme.G }));
        Assert.True(LaunchMath.Compute(in input, out Launch l, out _));

        // Act
        Vec3Mm p = Ballistic.PositionAt(in l, G.KnobExtreme.K);

        // Assert
        Assert.Equal(G.KnobExtreme.DropTerm, Throw.RH((long)G.KnobExtreme.G * G.KnobExtreme.K * G.KnobExtreme.K, 1800));
        Assert.True((long)G.KnobExtreme.G * G.KnobExtreme.K * G.KnobExtreme.K <= int.MaxValue);
        Assert.Equal(G.KnobExtreme.P, p);
    }

    [Fact]
    [Trait("Id", "TC-F2-1")]
    public void PositionAt_AgeZero_IsTheOrigin()
    {
        // Arrange
        Launch l = FxTLaunch();

        // Act
        Vec3Mm p = Ballistic.PositionAt(in l, 0);

        // Assert
        Assert.Equal(l.Origin, p);
    }

    [Fact]
    [Trait("Id", "TC-F2-1")]
    public void PositionAt_EveryAge1To20_EqualsTheTestSideExactNumeratorSegmentIntegrator()
    {
        // Arrange
        Launch l = FxTLaunch();
        long sx = 0;
        long sy = 0;
        long sz = 0;
        long sg = 0;

        for (int k = 1; k <= 20; k++)
        {
            // Act: accumulate exact numerators, one RH per axis per tick (never calls PositionAt)
            sx += l.Vel.X;
            sy += l.Vel.Y;
            sz += l.Vel.Z;
            sg += (long)l.G * ((2 * k) - 1); // sg = g·k²
            var integrated = new Vec3Mm(
                (int)(l.Origin.X + OracleRH(sx, 30)),
                (int)(l.Origin.Y + OracleRH(sy, 30) - OracleRH(sg, 1800)),
                (int)(l.Origin.Z + OracleRH(sz, 30)));

            // Assert
            Assert.Equal(integrated, Ballistic.PositionAt(in l, k));
        }

        Assert.Equal(G.FxTPositions[1].P, Ballistic.PositionAt(in l, 20));
    }

    [Fact]
    [Trait("Id", "TC-F2-1")]
    public void PositionAt_SecondEvaluationAfterOtherAges_IsBitIdentical()
    {
        // Arrange
        Launch l = FxTLaunch();
        var first = new Vec3Mm[Ballistic.MaxAgeTicks + 1];
        for (int k = 0; k <= Ballistic.MaxAgeTicks; k++)
        {
            first[k] = Ballistic.PositionAt(in l, k);
        }

        // Act + Assert: revisit in a scrambled order
        for (int i = 0; i <= Ballistic.MaxAgeTicks; i++)
        {
            int k = (i * 37) % (Ballistic.MaxAgeTicks + 1);
            Assert.Equal(first[k], Ballistic.PositionAt(in l, k));
        }
    }

    [Fact]
    [Trait("Id", "TC-F2-1")]
    public void PositionAt_AgeOutside0To150_AssertsUnderSimAssert_OrEvaluatesTheSameFormulaInRelease()
    {
        // Arrange
        Launch l = FxTLaunch();
        Vec3Mm atMax = Ballistic.PositionAt(in l, 150); // the ceiling itself is in contract

        // Act + Assert
        Assert.Equal(150, Ballistic.MaxAgeTicks);
        Assert.Equal(Formula(in l, 150), atMax);
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.BallisticAgeOutOfRange, Assert.Throws<SimAssertException>(() => Ballistic.PositionAt(in l, -1)).Code);
            Assert.Equal(SimAssertCode.BallisticAgeOutOfRange, Assert.Throws<SimAssertException>(() => Ballistic.PositionAt(in l, 151)).Code);
        }
        else
        {
            Assert.Equal(Formula(in l, -1), Ballistic.PositionAt(in l, -1));
            Assert.Equal(Formula(in l, 151), Ballistic.PositionAt(in l, 151));
        }
    }

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void ThrowRH_NonPositiveDivisor_AssertsUnderSimAssert_OrReturnsTheFloorDivResultInRelease()
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.ThrowRhDivisorNotPositive, Assert.Throws<SimAssertException>(() => Throw.RH(1, 0)).Code);
            Assert.Equal(SimAssertCode.ThrowRhDivisorNotPositive, Assert.Throws<SimAssertException>(() => Throw.RH(1, -2)).Code);
        }
        else
        {
            Assert.Equal(0, Throw.RH(1, 0));     // Fix.FloorDiv(2, 0) = 0 (the dropped fixDivZero count)
            Assert.Equal(0, Throw.RH(1, -2));    // Fix.FloorDiv(2·1 + (-2), -4) = FloorDiv(0, -4) = 0
        }
    }

    // ------------------------------------------------------------------ TC-F3-1

    [Theory]
    [MemberData(nameof(RangeTableCases))]
    [Trait("Id", "TC-F3-1")]
    public void IntegerFlight_RangeAndLandingTick_MatchTheGoldenAndTheFloatOracle(int row)
    {
        // Arrange
        (string name, WeightClass weightClass, double elevationDeg, short dx, short dy, short dz, double goldenRangeM, int goldenTick, int segment, double integerRangeM) = G.RangeTable[row];
        ThrowSettings s = G.DefaultSettings();
        LaunchInput input = G.Input(dx, dy, dz, s, weightClass: weightClass);
        Assert.True(LaunchMath.Compute(in input, out Launch l, out _), name);

        // Act: first k >= 1 with py(k) <= feet.y, interpolated linearly to y = feet.y (test-side doubles)
        int k = 1;
        Vec3Mm prev = Ballistic.PositionAt(in l, 0);
        Vec3Mm cur = Ballistic.PositionAt(in l, k);
        while (cur.Y > G.Feet.Y)
        {
            prev = cur;
            k++;
            cur = Ballistic.PositionAt(in l, k);
        }

        double f = (prev.Y - (double)G.Feet.Y) / (prev.Y - (double)cur.Y);
        double x = prev.X + (f * (cur.X - prev.X));
        double z = prev.Z + (f * (cur.Z - prev.Z));
        double rangeM = Math.Sqrt(Math.Pow(x - l.Origin.X, 2) + Math.Pow(z - l.Origin.Z, 2)) / 1000.0;
        double flightTicks = (k - 1) + f;
        int landingTick = (int)Math.Round(flightTicks, MidpointRounding.AwayFromZero);

        // F3 float oracle, independent of the integer F1: speed = baseSpeed·sqrt(permille / 1000) at elevation e,
        // R = vh·t_land, t_land = (vy + sqrt(vy² + 2·g·handHeight)) / g
        double speed = s.BaseSpeed(weightClass) * Math.Sqrt(input.ThrowRangePermille / 1000.0);
        double e = elevationDeg * Math.PI / 180.0;
        double vh = speed * Math.Cos(e);
        double vy = speed * Math.Sin(e);
        double tLand = (vy + Math.Sqrt((vy * vy) + (2.0 * s.G * s.HandHeightMm))) / s.G;
        double oracleRangeM = vh * tLand / 1000.0;
        double oracleTicks = tLand * 30.0;

        // Assert
        Assert.Equal(segment, k);
        Assert.InRange(rangeM, integerRangeM - 0.001, integerRangeM + 0.001);
        Assert.InRange(rangeM, goldenRangeM - 0.1, goldenRangeM + 0.1);
        Assert.InRange(landingTick, goldenTick - 1, goldenTick + 1);
        Assert.InRange(rangeM, oracleRangeM - 0.1, oracleRangeM + 0.1);
        Assert.InRange(flightTicks, oracleTicks - 1.0, oracleTicks + 1.0);
        Assert.Equal(goldenTick, landingTick);
    }

    // ------------------------------------------------------------------ 0 B per call

    [Fact]
    [Trait("Id", "TC-F1-1")]
    public void ComputePositionAtAndRH_TenThousandCalls_AllocateZeroBytes()
    {
        // Arrange: valid-direction path (never touches TrigQ15), so no Debug-only Tier-0 table allocation is expected
        LaunchInput input = G.FxT(G.DefaultSettings());
        long checksum = 0;

        // Act + Assert
        AllocAssert.Zero("10 000 x (LaunchMath.Compute + Ballistic.PositionAt + Throw.RH)", () =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                LaunchMath.Compute(in input, out Launch l, out bool fromChest);
                Vec3Mm p = Ballistic.PositionAt(in l, i % 91);
                checksum += p.X + p.Y + p.Z + Throw.RH(-i, 30) + (fromChest ? 1 : 0);
            }
        });
        Assert.NotEqual(long.MinValue, checksum);
    }

    // ------------------------------------------------------------------ helpers

    private static Launch FxTLaunch()
    {
        LaunchInput input = G.FxT(G.DefaultSettings());
        Assert.True(LaunchMath.Compute(in input, out Launch l, out _));
        return l;
    }

    // Independent test-side RH: floor((2n + d) / (2d)) for d > 0, written without Throw or Fix.
    private static long OracleRH(long n, long d)
    {
        long num = (2 * n) + d;
        long den = 2 * d;
        long q = num / den;
        return (num % den != 0 && (num < 0) != (den < 0)) ? q - 1 : q;
    }

    private static Vec3Mm Formula(in Launch l, int k) => new(
        (int)(l.Origin.X + OracleRH((long)l.Vel.X * k, 30)),
        (int)(l.Origin.Y + OracleRH((long)l.Vel.Y * k, 30) - OracleRH((long)l.G * k * k, 1800)),
        (int)(l.Origin.Z + OracleRH((long)l.Vel.Z * k, 30)));

    /// <summary>A test-only chest probe that records its arguments and returns a fixed answer.</summary>
    private sealed class RecordingProbe : IChestOriginProbe
    {
        private readonly bool _blocked;

        public RecordingProbe(bool blocked)
        {
            _blocked = blocked;
        }

        public int Calls { get; private set; }

        public Vec3Mm Chest { get; private set; }

        public Vec3Mm Origin { get; private set; }

        public bool IsBlocked(in Vec3Mm chest, in Vec3Mm origin)
        {
            Calls++;
            Chest = chest;
            Origin = origin;
            return _blocked;
        }
    }
}
