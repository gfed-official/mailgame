using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// The committed golden vectors of Throwing story 001 (GDD <c>design/gdd/throwing-catching.md</c> TC-F1-1, TC-F2-1, TC-F3-1;
/// ADR-0015 sections 2 and 3; T2 cross-platform surface). Every expected integer is copied literally from the GDD and the
/// story's "How it is tested" bullets and was checked once by an independent exact-integer oracle; never regenerate them
/// from the code under test. Kept as data so the shared-function story (014) and the host apply story (010) reuse them.
/// </summary>
internal static class ThrowGoldenVectors
{
    /// <summary>FX-T feet: (10 000, 0, 20 000), facing -Z.</summary>
    public static readonly Vec3Mm Feet = new(10_000, 0, 20_000);

    /// <summary>FX-T direction (loft 6 degrees, quantised): (0, 3406, -16 026), <c>n</c> = 16 383.</summary>
    public static readonly (short Dx, short Dy, short Dz) FxTDir = (0, 3406, -16_026);

    /// <summary>FX-T expected <c>n</c>.</summary>
    public const long FxTN = 16_383;

    /// <summary>FX-T light <c>v</c>, mm/s.</summary>
    public const long FxTV = 14_000;

    /// <summary>FX-T light velocity, mm/s.</summary>
    public static readonly Vec3Mm FxTVel = new(0, 2911, -13_695);

    /// <summary>FX-T right-handed origin, mm.</summary>
    public static readonly Vec3Mm FxTOrigin = new(10_250, 1200, 19_700);

    /// <summary>TC-F1-1 c: left-handed origin x.</summary>
    public const int FxTLeftOriginX = 9750;

    /// <summary>TC-F1-1 b (Cannon Arm, <c>ThrowRangePermille</c> 1500): speedPm, v, vel.</summary>
    public static readonly (int Permille, long SpeedPm, long V, Vec3Mm Vel) CannonArm = (1500, 1224, 17_136, new Vec3Mm(0, 3563, -16_763));

    /// <summary>Medium class at the FX-T direction: v 10 000, vel (0, 2079, -9782).</summary>
    public static readonly (long V, Vec3Mm Vel) Medium = (10_000, new Vec3Mm(0, 2079, -9782));

    /// <summary>TC-F2-1: (k, p(k)) for the FX-T flight with <c>g</c> 9800.</summary>
    public static readonly (int K, Vec3Mm P)[] FxTPositions =
    {
        (1, new Vec3Mm(10_250, 1292, 19_244)),
        (20, new Vec3Mm(10_250, 963, 10_570)),
        (21, new Vec3Mm(10_250, 837, 10_114)),
    };

    /// <summary>TC-F2-1: the k = 90 gravity drop term <c>RH(9800·90², 1800)</c>.</summary>
    public const long DropTermK90 = 44_100;

    /// <summary>Knob extreme: <c>g</c> 14 000, k 150 → gravity term 175 000, p(150).</summary>
    public static readonly (int G, int K, long DropTerm, Vec3Mm P) KnobExtreme = (14_000, 150, 175_000, new Vec3Mm(10_250, -159_245, -48_775));

    /// <summary>TC-F1-1 d: (n, d, Throw.RH(n, d)).</summary>
    public static readonly (long N, long D, long Expected)[] RoundHalfUp =
    {
        (-1, 2, 0),
        (-3, 2, -1),
        (-13_695, 30, -456),
    };

    /// <summary>TC-F1-1 e: (dx, dy, dz, accepted). Length bounds, then elevation bounds (all elevation rows have <c>n</c> = 16 384).</summary>
    public static readonly (short Dx, short Dy, short Dz, bool Accepted)[] HostBounds =
    {
        (0, 0, 15_999, false),
        (0, 0, 16_769, false),
        (0, 0, 16_000, true),
        (0, 0, 16_768, true),
        (0, 16_073, -3178, false),   // elevation 981
        (0, 16_057, -3258, true),    // elevation 980
        (0, -14_352, -7903, true),   // elevation -876
        (0, -14_368, -7874, false),  // elevation -877
        (-32_768, 32_767, 32_767, false),   // n 56 754; int32 squaring would wrap
    };

    /// <summary>TC-F1-1 e: <c>DirLengthSquared(-32768, 32767, 32767)</c>.</summary>
    public const long ExtremeDirLengthSquared = 3_221_094_402;

    /// <summary>
    /// Defence branch (<c>OriginFor</c>, right-handed, FX-T feet, default offsets): (dx, dz, yaw BAM, expected origin).
    /// </summary>
    public static readonly (short Dx, short Dz, ushort Yaw, Vec3Mm Origin)[] DefenceOrigins =
    {
        (0, 0, 0, new Vec3Mm(10_250, 1200, 19_700)),        // yaw 0 faces -Z
        (0, 0, 16_384, new Vec3Mm(9700, 1200, 19_750)),     // yaw 16 384 faces -X
        (0, 2047, 0, new Vec3Mm(10_250, 1200, 19_700)),     // h 2047: yaw basis
        (0, 2048, 0, new Vec3Mm(9750, 1200, 20_300)),       // h 2048: dir basis, faces +Z
    };

    /// <summary>
    /// TC-F3-1: (name, class, elevation e in degrees, dir, golden range m, golden landing tick, expected landing segment k,
    /// expected integer range m to 1 mm). Dir integers are <c>round(16384·(0, sin e, -cos e))</c>: light e = 36 degrees,
    /// heavy e = 51 degrees. The float oracle starts from <c>e</c>, not from the integer velocity.
    /// </summary>
    public static readonly (string Name, WeightClass Class, double ElevationDeg, short Dx, short Dy, short Dz, double RangeM, int LandingTick, int Segment, double IntegerRangeM)[] RangeTable =
    {
        ("light 30", WeightClass.Light, 36.0, 0, 9630, -13_255, 20.55, 54, 55, 20.550),
        ("heavy 45", WeightClass.Heavy, 51.0, 0, 12_733, -10_311, 4.4, 35, 35, 4.388),
    };

    private static readonly ThrowBalance _defaults = BuiltInContent.CreateM1a().Throw;

    /// <summary>The M-1a built-in throw record (GDD defaults 14000/10000/6000, g 9800, maxFlightTicks 90, hand 1200, fwd 300, side 250).</summary>
    public static ThrowBalance DefaultBalance => _defaults;

    /// <summary>A settings view over the M-1a defaults.</summary>
    public static ThrowSettings DefaultSettings() => new(_defaults);

    /// <summary>A settings view over the M-1a defaults with one record edit (e.g. <c>b =&gt; b with { G = 14000 }</c>).</summary>
    public static ThrowSettings Settings(Func<ThrowBalance, ThrowBalance> edit) => new(edit(_defaults));

    /// <summary>An input at the FX-T feet with the given direction and knobs.</summary>
    public static LaunchInput Input(
        short dx,
        short dy,
        short dz,
        ThrowSettings settings,
        sbyte side = 1,
        WeightClass weightClass = WeightClass.Light,
        int permille = 1000,
        ushort yaw = 0,
        IChestOriginProbe? probe = null) =>
        new(Feet, dx, dy, dz, side, weightClass, permille, yaw, settings, probe);

    /// <summary>The FX-T input (right-handed, light, permille 1000).</summary>
    public static LaunchInput FxT(ThrowSettings settings) => Input(FxTDir.Dx, FxTDir.Dy, FxTDir.Dz, settings);
}
