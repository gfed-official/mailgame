using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.Tests.Unit.Geometry;

/// <summary>A segment-versus-box golden row. <c>Enter</c>/<c>Exit</c> are the expected permille bounds
/// (<see cref="SegmentInterval.None"/> is 0 / -1, an unbounded side is <c>int.MinValue</c> / <c>int.MaxValue</c>).</summary>
internal readonly record struct SlabVector(string Name, Vec3Mm P0, Vec3Mm P1, AabbMm Box, int Enter, int Exit, bool Hit);

/// <summary>A segment-versus-circle (xz) or sphere (3D) golden row.</summary>
internal readonly record struct RoundVector(string Name, Vec3Mm P0, Vec3Mm P1, Vec3Mm Centre, int Radius, int Enter, int Exit, bool Hit);

/// <summary>A segment-versus-cylinder golden row (flat-capped; y from <c>Centre.Y</c> to <c>Centre.Y + Height</c>).</summary>
internal readonly record struct CylinderVector(string Name, Vec3Mm P0, Vec3Mm P1, Vec3Mm Centre, int Radius, int Height, int Enter, int Exit, bool Hit);

/// <summary>A height-field march golden row; <c>Plane</c> is the flat terrain height (mm) of the fixture used.</summary>
internal readonly record struct MarchVector(string Name, int Plane, Vec3Mm P0, Vec3Mm P1, bool Hit, int FractionPm);

/// <summary>
/// The shared T2 golden vectors of the <c>Sim/Geometry</c> primitives (player-character story 004; Throwing F4;
/// ADR-0013 section 6). Throwing-catching reuses this table for the flight sweep. Every number was derived by hand from
/// the F4 formulas (derivations beside each row) and must never be regenerated from the code under test.
/// <para>
/// Slab recipe per axis, <c>D_a != 0</c>: <c>lo' = (lo - P0) * 1000</c>, <c>hi' = (hi - P0) * 1000</c>, negated and swapped when
/// <c>D_a &lt; 0</c>, then <c>enter = ceil(lo' / |D|)</c>, <c>exit = floor(hi' / |D|)</c>. Circle/sphere recipe:
/// <c>A = D.D, B = D.R, C = R.R - r^2, disc = B^2 - A*C, s = isqrt(disc)</c>,
/// <c>enter = ceil(1000 (-B - s) / A)</c>, <c>exit = floor(1000 (-B + s) / A)</c>.
/// </para>
/// </summary>
internal static class GeometryGoldenVectors
{
    private const int Inf = int.MaxValue;
    private const int NegInf = int.MinValue;

    // The TC-F4-1 mailbox: x 9750..10750, catch volume y 600..1400, z 9500..10500.
    private static readonly AabbMm _mailbox = new(new Vec3Mm(9750, 600, 9500), new Vec3Mm(10750, 1400, 10500));

    // A 200 mm cube-ish box around the origin in y and z, used by the x-axis rows.
    private static AabbMm XBox(int xLo, int xHi) => new(new Vec3Mm(xLo, -100, -100), new Vec3Mm(xHi, 100, 100));

    /// <summary>Slab (AABB) rows.</summary>
    public static readonly SlabVector[] Slab =
    {
        // S0 TC-F4-1 (the F1 flight at tick 21): D = (0, -126, -456).
        //   x: D = 0, 9750 <= 10250 <= 10750 -> unbounded.
        //   y: lo' = (600-963)*1000 = -363000, hi' = (1400-963)*1000 = 437000; D < 0 -> (-437000, 363000)/126
        //      -> enter = ceil(-3468.25) = -3468, exit = floor(2880.95) = 2880.
        //   z: lo' = (9500-10570)*1000 = -1070000, hi' = (10500-10570)*1000 = -70000 -> (70000, 1070000)/456
        //      -> enter = ceil(153.51) = 154, exit = floor(2346.49) = 2346.
        //   tEnter = max(-3468, 154) = 154, tExit = min(2880, 2346) = 2346 -> hit, f = 154.
        new SlabVector("TC-F4-1 mailbox f154", new Vec3Mm(10250, 963, 10570), new Vec3Mm(10250, 837, 10114), _mailbox, 154, 2346, true),

        // S1 TC-F4-1b D = (0, -126, -70): z enter = ceil(70000/70) = 1000 (exactly touching at t = 1000), z exit = floor(1070000/70) = 15285;
        //   y as S0 (-3468, 2880) -> (1000, 2880): hit, f = 1000.
        new SlabVector("TC-F4-1b touch at 1000", new Vec3Mm(10250, 963, 10570), new Vec3Mm(10250, 837, 10500), _mailbox, 1000, 2880, true),

        // S2 TC-F4-1b D = (0, -126, -69): z enter = ceil(70000/69) = ceil(1014.49) = 1015 > 1000, z exit = floor(15507.2) = 15507;
        //   y as S0 -> (1015, 2880): tEnter > 1000 -> no hit.
        new SlabVector("TC-F4-1b just short 1015", new Vec3Mm(10250, 963, 10570), new Vec3Mm(10250, 837, 10501), _mailbox, 1015, 2880, false),

        // S3 TC-F4-1c start inside, D = (0, -100, -50), P0 = (10250, 1000, 10200).
        //   y: lo' = -400000, hi' = 400000 -> (-400000, 400000)/100 -> (-4000, 4000).
        //   z: lo' = -700000, hi' = 300000 -> (-300000, 700000)/50 -> (-6000, 14000).
        //   (-4000, 4000): tEnter < 0 -> f = max(tEnter, 0) = 0.
        new SlabVector("TC-F4-1c start inside f0", new Vec3Mm(10250, 1000, 10200), new Vec3Mm(10250, 900, 10150), _mailbox, -4000, 4000, true),

        // S4 box behind: P0 = origin, D = (1000, 0, 0), box x -3000..-2000 -> lo' = -3000000, hi' = -2000000 -> (-3000, -2000);
        //   y, z: D = 0 and 0 is inside +-100 -> tExit = -2000 < 0 -> no hit.
        new SlabVector("box behind", new Vec3Mm(0, 0, 0), new Vec3Mm(1000, 0, 0), XBox(-3000, -2000), -3000, -2000, false),

        // S5 touching exactly at t = 1000: box x 1000..2000, D = (1000, 0, 0): lo' = 1000000 -> enter 1000; hi' = 2000000 -> exit 2000.
        new SlabVector("touch at exactly 1000", new Vec3Mm(0, 0, 0), new Vec3Mm(1000, 0, 0), XBox(1000, 2000), 1000, 2000, true),

        // S6 D_y = 0 outside the slab: P0.y = 500, box y -100..100 -> certain miss -> None (0, -1).
        new SlabVector("D=0 outside slab", new Vec3Mm(0, 500, 0), new Vec3Mm(1000, 500, 0), XBox(200, 400), 0, -1, false),

        // S7 negative direction swap: P0.x = 1000, D = (-2000, 0, 0), box x -500..500:
        //   lo' = (-500-1000)*1000 = -1500000, hi' = (500-1000)*1000 = -500000 -> negate/swap (500000, 1500000)/2000 -> (250, 750). f = 250.
        new SlabVector("negative direction", new Vec3Mm(1000, 0, 0), new Vec3Mm(-1000, 0, 0), XBox(-500, 500), 250, 750, true),

        // S8 inexact division: D = (3, 0, 0), box x 1..2: lo' = 1000, hi' = 2000 -> enter = ceil(333.33) = 334, exit = floor(666.67) = 666.
        new SlabVector("ceil and floor rounding", new Vec3Mm(0, 0, 0), new Vec3Mm(3, 0, 0), XBox(1, 2), 334, 666, true),

        // S9 zero-length segment inside the box: every axis D = 0 and inside -> (-inf, +inf), hit with f = 0.
        new SlabVector("point inside", new Vec3Mm(500, 0, 0), new Vec3Mm(500, 0, 0), XBox(0, 1000), NegInf, Inf, true),

        // S10 zero-length segment outside the box: x = 5000 is outside 0..1000 -> None.
        new SlabVector("point outside", new Vec3Mm(5000, 0, 0), new Vec3Mm(5000, 0, 0), XBox(0, 1000), 0, -1, false),

        // S11 tEnter > tExit (the line clips the corner): D = (1000, 1000, 0), box x 600..700, y 0..100.
        //   x: (600000, 700000)/1000 -> (600, 700). y: lo' = 0, hi' = 100000 -> (0, 100). tEnter = 600 > tExit = 100 -> no hit.
        new SlabVector("corner miss enter > exit", new Vec3Mm(0, 0, 0), new Vec3Mm(1000, 1000, 0),
            new AabbMm(new Vec3Mm(600, 0, -10), new Vec3Mm(700, 100, 10)), 600, 100, false),
    };

    /// <summary>Circle (xz) rows; the y of every point is irrelevant (some rows use y != centre.Y on purpose).</summary>
    public static readonly RoundVector[] Circle =
    {
        // C0 TC-F4-1d goon r 300 + slack 300 = 600 at the origin; P0 = (-2000, 500, 0), D = (2000, 0, 0).
        //   R = (-2000, 0): A = 4e6, B = -4e6, C = 4e6 - 360000 = 3.64e6, disc = 16e12 - 14.56e12 = 1.44e12, s = 1.2e6.
        //   enter = ceil(1000 * (4e6 - 1.2e6) / 4e6) = 700, exit = floor(1000 * 5.2e6 / 4e6) = 1300.
        new RoundVector("TC-F4-1d goon f700", new Vec3Mm(-2000, 500, 0), new Vec3Mm(0, 500, 0), default, 600, 700, 1300, true),

        // C1 TC-F4-1f the goon behind: P0 = (2000, 500, 0), D = (2000, 0, 0): R = (2000, 0), A = 4e6, B = +4e6, C = 3.64e6, s = 1.2e6.
        //   enter = ceil(1000 * (-4e6 - 1.2e6) / 4e6) = -1300, exit = floor(1000 * (-4e6 + 1.2e6) / 4e6) = -700 -> both negative: tExit < 0, no hit.
        new RoundVector("TC-F4-1f both roots negative", new Vec3Mm(2000, 500, 0), new Vec3Mm(4000, 500, 0), default, 600, -1300, -700, false),

        // C2 tangent, exact: centre origin, r = 500, P0 = (-2000, 0, 500), D = (4000, 0, 0): R = (-2000, 500).
        //   A = 16e6, B = -8e6, C = 4.25e6 - 0.25e6 = 4e6, disc = 64e12 - 64e12 = 0, s = 0.
        //   enter = ceil(1000 * 8e6 / 16e6) = 500 = exit = floor(500) -> hit, f = 500.
        new RoundVector("tangent exact", new Vec3Mm(-2000, 0, 500), new Vec3Mm(2000, 0, 500), default, 500, 500, 500, true),

        // C3 tangent, inexact: D = (3000, 0, 0): A = 9e6, B = -6e6, C = 4e6, disc = 36e12 - 36e12 = 0.
        //   t = 6e9 / 9e6 = 666.67 -> enter = ceil = 667, exit = floor = 666 -> enter > exit, no hit (F4 rounding choice).
        new RoundVector("tangent inexact", new Vec3Mm(-2000, 0, 500), new Vec3Mm(1000, 0, 500), default, 500, 667, 666, false),

        // C4 start inside: P0 = (100, 0, 0), D = (200, 0, 0), r = 500: A = 40000, B = 20000, C = 10000 - 250000 = -240000,
        //   disc = 4e8 + 9.6e9 = 1.0e10, s = 100000. enter = ceil(1000 * (-20000 - 100000) / 40000) = -3000,
        //   exit = floor(1000 * (-20000 + 100000) / 40000) = 2000 -> hit, f = 0.
        new RoundVector("start inside", new Vec3Mm(100, 0, 0), new Vec3Mm(300, 0, 0), default, 500, -3000, 2000, true),

        // C5 disc < 0: P0 = (-2000, 0, 2000), D = (4000, 0, 0), r = 500: A = 16e6, B = -8e6, C = 8e6 - 0.25e6 = 7.75e6,
        //   disc = 64e12 - 124e12 < 0 -> miss (None).
        new RoundVector("disc negative", new Vec3Mm(-2000, 0, 2000), new Vec3Mm(2000, 0, 2000), default, 500, 0, -1, false),

        // C6 enters beyond the segment: P0 = (-2000, 0, 0), D = (1000, 0, 0), r = 500: A = 1e6, B = -2e6, C = 3.75e6,
        //   disc = 4e12 - 3.75e12 = 2.5e11, s = 5e5. enter = ceil(1000 * 1.5e6 / 1e6) = 1500, exit = floor(1000 * 2.5e6 / 1e6) = 2500.
        new RoundVector("enters after the segment ends", new Vec3Mm(-2000, 0, 0), new Vec3Mm(-1000, 0, 0), default, 500, 1500, 2500, false),

        // C7 A = 0, inside (C = 10000 - 250000 <= 0): point test -> (-inf, +inf), hit.
        new RoundVector("point inside", new Vec3Mm(100, 0, 0), new Vec3Mm(100, 0, 0), default, 500, NegInf, Inf, true),

        // C8 A = 0, outside (C = 1e6 - 250000 > 0): miss, no division.
        new RoundVector("point outside", new Vec3Mm(1000, 0, 0), new Vec3Mm(1000, 0, 0), default, 500, 0, -1, false),

        // C9 A = 0, on the boundary (C = 250000 - 250000 = 0 <= 0): inside.
        new RoundVector("point on the boundary", new Vec3Mm(500, 0, 0), new Vec3Mm(500, 0, 0), default, 500, NegInf, Inf, true),
    };

    /// <summary>Sphere (3D) rows.</summary>
    public static readonly RoundVector[] Sphere =
    {
        // R0 TC-F4-1e armed catcher: feet (0, 0, 0), sphere centre (0, 1000, 0), r = 1500, P0 = (-3000, 1000, 0), D = (3000, 0, 0).
        //   R = (-3000, 0, 0): A = 9e6, B = -9e6, C = 9e6 - 2.25e6 = 6.75e6, disc = 81e12 - 60.75e12 = 20.25e12, s = 4.5e6.
        //   enter = ceil(1000 * (9e6 - 4.5e6) / 9e6) = 500, exit = floor(1000 * 13.5e6 / 9e6) = 1500.
        new RoundVector("TC-F4-1e catcher f500", new Vec3Mm(-3000, 1000, 0), new Vec3Mm(0, 1000, 0), new Vec3Mm(0, 1000, 0), 1500, 500, 1500, true),

        // R1 straight down onto the sphere top: centre origin, r = 1000, P0 = (0, 3000, 0), D = (0, -2000, 0).
        //   A = 4e6, B = -6e6, C = 9e6 - 1e6 = 8e6, disc = 36e12 - 32e12 = 4e12, s = 2e6.
        //   enter = ceil(1000 * 4e6 / 4e6) = 1000, exit = floor(1000 * 8e6 / 4e6) = 2000 -> touches at t = 1000, f = 1000.
        new RoundVector("touch sphere top at 1000", new Vec3Mm(0, 3000, 0), new Vec3Mm(0, 1000, 0), default, 1000, 1000, 2000, true),

        // R2 3D diagonal: P0 = (1000, 2000, 2000) (|R| = 3000), D = (-1000, -2000, -2000), r = 1000.
        //   A = 9e6, B = -(1e6 + 4e6 + 4e6) = -9e6, C = 9e6 - 1e6 = 8e6, disc = 81e12 - 72e12 = 9e12, s = 3e6.
        //   enter = ceil(1000 * 6e6 / 9e6) = ceil(666.67) = 667, exit = floor(1000 * 12e6 / 9e6) = floor(1333.33) = 1333.
        new RoundVector("3D diagonal", new Vec3Mm(1000, 2000, 2000), new Vec3Mm(0, 0, 0), default, 1000, 667, 1333, true),

        // R3 point inside the sphere (A = 0, C = 250000 - 1e6 < 0): unbounded.
        new RoundVector("point inside", new Vec3Mm(500, 0, 0), new Vec3Mm(500, 0, 0), default, 1000, NegInf, Inf, true),

        // R4 point outside (A = 0, C > 0): miss.
        new RoundVector("point outside", new Vec3Mm(1500, 0, 0), new Vec3Mm(1500, 0, 0), default, 1000, 0, -1, false),

        // R5 y matters for a sphere: P0 = (-2000, 1500, 0), D = (4000, 0, 0), r = 1000, centre origin:
        //   R = (-2000, 1500, 0): A = 16e6, B = -8e6,
        //   C = 4e6 + 2.25e6 - 1e6 = 5.25e6, disc = 64e12 - 84e12 < 0 -> miss (a circle at the same xz would hit).
        new RoundVector("above the sphere", new Vec3Mm(-2000, 1500, 0), new Vec3Mm(2000, 1500, 0), default, 1000, 0, -1, false),
    };

    /// <summary>Cylinder rows (circle interval intersected with the y-slab).</summary>
    public static readonly CylinderVector[] Cylinder =
    {
        // Y0 TC-F4-1d goon (r 600 incl. slack, y 0..1800) at the origin, P0 = (-2000, 500, 0), D = (2000, 0, 0):
        //   circle (700, 1300) as C0; y: D = 0 and 0 <= 500 <= 1800 -> unbounded -> (700, 1300), f = 700.
        new CylinderVector("TC-F4-1d goon f700", new Vec3Mm(-2000, 500, 0), new Vec3Mm(0, 500, 0), default, 600, 1800, 700, 1300, true),

        // Y1 TC-F4-1f the goon behind: circle (-1300, -700) -> no hit.
        new CylinderVector("TC-F4-1f goon behind", new Vec3Mm(2000, 500, 0), new Vec3Mm(4000, 500, 0), default, 600, 1800, -1300, -700, false),

        // Y2 TC-F4-1g entered above its top: P0 = (-700, 2400, 0), D = (1000, -900, 0).
        //   circle: R = (-700, 0): A = 1e6, B = -7e5, C = 4.9e5 - 3.6e5 = 1.3e5, disc = 4.9e11 - 1.3e11 = 3.6e11, s = 6e5
        //     -> enter = ceil(1000 * 1e5 / 1e6) = 100, exit = floor(1000 * 1.3e6 / 1e6) = 1300.
        //   y-slab 0..1800: lo' = -2400000, hi' = -600000 -> (600000, 2400000)/900 -> (667, 2666).
        //   tEnter = max(100, 667) = 667, tExit = min(1300, 2666) = 1300 -> f = 667, not 100.
        new CylinderVector("TC-F4-1g above the top f667", new Vec3Mm(-700, 2400, 0), new Vec3Mm(300, 1500, 0), default, 600, 1800, 667, 1300, true),

        // Y3 D_y = 0 above the cylinder (2400 > 1800): the y-slab is a certain miss although the circle hits -> None.
        new CylinderVector("flying over the top", new Vec3Mm(-2000, 2400, 0), new Vec3Mm(0, 2400, 0), default, 600, 1800, 0, -1, false),

        // Y4 circle misses (disc < 0 as C5, r = 500), y inside -> None.
        new CylinderVector("circle misses", new Vec3Mm(-2000, 100, 2000), new Vec3Mm(2000, 100, 2000), default, 500, 1800, 0, -1, false),

        // Y5 the y-slab exits before the circle does: P0 = (-2000, 900, 0), D = (4000, 1800, 0), r = 500, y 0..1000.
        //   circle: A = 16e6, B = -8e6, C = 4e6 - 250000 = 3.75e6, disc = 64e12 - 60e12 = 4e12, s = 2e6
        //     -> enter = ceil(1000 * 6e6 / 16e6) = 375, exit = floor(1000 * 10e6 / 16e6) = 625.
        //   y: P0 = 900, D = +1800: lo' = -900000, hi' = 100000 -> enter = ceil(-500) = -500, exit = floor(55.56) = 55.
        //   tEnter = max(375, -500) = 375 > tExit = min(625, 55) = 55 -> no hit.
        new CylinderVector("y-slab exits first", new Vec3Mm(-2000, 900, 0), new Vec3Mm(2000, 2700, 0), default, 500, 1000, 375, 55, false),
    };

    /// <summary>
    /// Height-field march rows over flat planes (the F4 example flight and TC-F4-1h). The ramp row lives in the test
    /// because its fixture is built there.
    /// <para>
    /// The F4 example flight P0 (10250, 963, 10570) -> P1 (10250, 837, 10114): D = (0, -126, -456), |Dxz| = 456 ->
    /// ceil(456/250) = 2, |Dy| = 126 -> 1, n = 2. Q_1 = (10250, 963 + RH(-63), 10570 + RH(-228)) = (10250, 900, 10342), Q_2 = P1.
    /// </para>
    /// </summary>
    public static readonly MarchVector[] March =
    {
        // M0 plane 880: gaps 963-880 = 83, 900-880 = 20, 837-880 = -43. First <= 0 at i = 2: gapPrev = 20, gap = -43, drop = 63.
        //   f = ceil(1000 * (1 * 63 + 20) / (2 * 63)) = ceil(83000 / 126) = ceil(658.73) = 659.
        new MarchVector("F4 example flight over a plane at 880", 880, new Vec3Mm(10250, 963, 10570), new Vec3Mm(10250, 837, 10114), true, 659),

        // M1 plane 900: gap_1 = 900 - 900 = 0 <= 0 at i = 1: gapPrev = 63, gap = 0, drop = 63: f = ceil(1000 * (0 + 63) / (2 * 63)) = 500.
        new MarchVector("sample lands exactly on the terrain", 900, new Vec3Mm(10250, 963, 10570), new Vec3Mm(10250, 837, 10114), true, 500),

        // M2 plane 0 (the flight stays 837 mm above it): never touches -> false.
        new MarchVector("never touches", 0, new Vec3Mm(10250, 963, 10570), new Vec3Mm(10250, 837, 10114), false, 0),

        // M3 FX-Dry (plane 1000): P0.y = 963 < 1000, gap_0 = -37 <= 0 -> starts below terrain: contact f = 0.
        new MarchVector("starts below terrain", 1000, new Vec3Mm(10250, 963, 10570), new Vec3Mm(10250, 837, 10114), true, 0),

        // M4 TC-F4-1h: plane 0, P0 = (5000, 250, 5000), D = (900, -600, 0): |Dxz| = 900 -> ceil(900/250) = 4, |Dy| = 600 -> 3, n = 4.
        //   Q_i = (5000 + 225 i, 250 - 150 i, 5000): gaps 250, 100, -50 -> first <= 0 at i = 2, gapPrev = 100, gap = -50, drop = 150.
        //   f = ceil(1000 * (1 * 150 + 100) / (4 * 150)) = ceil(250000 / 600) = ceil(416.67) = 417.
        new MarchVector("TC-F4-1h n=4 f417", 0, new Vec3Mm(5000, 250, 5000), new Vec3Mm(5900, -350, 5000), true, 417),

        // M5 zero-length segment above the terrain: n = 1, gap_1 = gap_0 = 100 > 0 -> false.
        new MarchVector("zero-length above", 0, new Vec3Mm(3000, 100, 3000), new Vec3Mm(3000, 100, 3000), false, 0),
    };

    /// <summary>Row indices of <see cref="Slab"/> for xUnit theories.</summary>
    public static IEnumerable<object[]> SlabIndices() => Indices(Slab.Length);

    /// <summary>Row indices of <see cref="Circle"/> for xUnit theories.</summary>
    public static IEnumerable<object[]> CircleIndices() => Indices(Circle.Length);

    /// <summary>Row indices of <see cref="Sphere"/> for xUnit theories.</summary>
    public static IEnumerable<object[]> SphereIndices() => Indices(Sphere.Length);

    /// <summary>Row indices of <see cref="Cylinder"/> for xUnit theories.</summary>
    public static IEnumerable<object[]> CylinderIndices() => Indices(Cylinder.Length);

    /// <summary>Row indices of <see cref="March"/> for xUnit theories.</summary>
    public static IEnumerable<object[]> MarchIndices() => Indices(March.Length);

    private static IEnumerable<object[]> Indices(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new object[] { i };
        }
    }
}
