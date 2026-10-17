using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Consumer fixtures for the Player Character Movement fixture table (world-stub story 006; PC GDD: FX-Dry, FX-Beach,
/// FX-Ramp, FX-Deep, FX-Pad) built over the same <see cref="WorldTablesBuilder"/> as <see cref="M1aStreet"/>, without
/// the street. Also the single test source of the <c>players</c> walkability limits and a Constructed-state
/// <see cref="EntityIds"/> (both limits are pinned here to the PC GDD defaults; drift guards in <c>WorldTablesFixtureTests</c> tie them to the
/// content-data <c>player.swimEnterMm</c> and <c>player.slopeMaxPm</c> defaults). Hand-authored, deterministic, no IO.
/// </summary>
internal static class WorldTablesFixture
{
    /// <summary>PC GDD default <c>slopeMaxPm</c>: 700 permille.</summary>
    public const int SlopeMaxPm = 700;

    /// <summary>PC GDD default <c>swimEnterMm</c>: 900 mm.</summary>
    public const int SwimEnterMm = 900;

    /// <summary>FX-Dry and FX-Deep grid: 64 x 64 tiles = 0 to 128,000 mm on x and z.</summary>
    public const int FlatTiles = 64;

    /// <summary>FX-Dry height in mm.</summary>
    public const int DryHeightMm = 1000;

    /// <summary>FX-Deep height in mm (depth 2000).</summary>
    public const int DeepHeightMm = -2000;

    /// <summary>FX-Ramp strip length in tiles (low ground, the ramp tile, high ground), one tile deep in Z.</summary>
    public const int RampTilesX = 8;

    /// <summary>FX-Ramp: index of the single rising tile; the low ground (height 0) is west of it, the high ground (height rise) east.</summary>
    public const int RampTileX = 3;

    /// <summary>FX-Pad grid: 8 x 8 tiles.</summary>
    public const int PadTiles = 8;

    /// <summary>
    /// FX-Pad centre X and Z in mm. The PC GDD says "pad at (0,0)"; read as pad-relative, because a slot at (-1200, ...) is
    /// off the corner grid (which starts at 0) and fails the Build-time walkable check. Slot offsets from this centre are the GDD's.
    /// </summary>
    public const int PadCentreMm = 8000;

    /// <summary>A world-state stub: between ticks, never in a phase, in the given state.</summary>
    private sealed class StateReader : ITickReader, IWorldStateReader
    {
        public SimState State => SimState.Constructed;

        public uint CurrentTick => 0;

        public TickStage Stage => TickStage.BetweenTicks;

        public PhaseId Phase => default;
    }

    /// <summary>A fresh Constructed-state id allocator (every call is independent, so every build starts at counter 1).</summary>
    /// <returns>The allocator.</returns>
    public static EntityIds NewIds()
    {
        var reader = new StateReader();
        return new EntityIds(reader, new SimCheckRing(), new SimCounters(), default, state: reader);
    }

    /// <summary>FX-Dry: 64 x 64 tiles of flat dry ground at +1000 mm, map 0 to 128,000 mm on x and z.</summary>
    /// <returns>The tables.</returns>
    public static WorldTables Dry() => Flat(DryHeightMm);

    /// <summary>FX-Deep: 64 x 64 tiles of flat ground at -2000 mm (depth 2000), map 0 to 128,000 mm.</summary>
    /// <returns>The tables.</returns>
    public static WorldTables Deep() => Flat(DeepHeightMm);

    /// <summary>FX-Beach: one 2000 mm tile, h00 = h10 = -1000 and h01 = h11 = -200 (height -600 at x 500, z 1000).</summary>
    /// <returns>The tables.</returns>
    public static WorldTables Beach()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(1, 1, new[] { -1000, -1000, -200, -200 });
        return b.Build(SlopeMaxPm, SwimEnterMm);
    }

    /// <summary>
    /// FX-Ramp: one tile (index <see cref="RampTileX"/>, 0) rising <paramref name="riseMm"/> over 2000 mm along +X, between
    /// flat ground at 0 (west) and flat ground at <paramref name="riseMm"/> (east). One tile deep in Z, so the ramp is the only sloped tile.
    /// </summary>
    /// <param name="riseMm">The rise: 1400 is walkable (700 permille), 1404 is not.</param>
    /// <returns>The tables.</returns>
    public static WorldTables Ramp(int riseMm)
    {
        const int Stride = RampTilesX + 1;
        var corners = new int[Stride * 2];
        for (int cz = 0; cz < 2; cz++)
        {
            for (int cx = 0; cx < Stride; cx++)
            {
                corners[(cz * Stride) + cx] = cx > RampTileX ? riseMm : 0;
            }
        }

        var b = new WorldTablesBuilder();
        b.SetCorners(RampTilesX, 1, corners);
        return b.Build(SlopeMaxPm, SwimEnterMm);
    }

    /// <summary>
    /// FX-Pad: 8 x 8 tiles of flat dry ground at +1000 mm with the Post Office pad centred at (<see cref="PadCentreMm"/>, 1000,
    /// <see cref="PadCentreMm"/>); all 8 slots at 1200 mm are valid. Built on the composition path with a test <see cref="EntityIds"/> and no lots.
    /// </summary>
    /// <returns>The tables.</returns>
    public static WorldTables Pad()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(PadTiles, PadTiles, FlatCorners(PadTiles, DryHeightMm));
        b.SetPostOffice(
            new Vec3Mm(PadCentreMm, DryHeightMm, PadCentreMm - 2500),
            new Vec3Mm(PadCentreMm + 500, DryHeightMm + 1000, PadCentreMm - 2500),
            new Vec3Mm(PadCentreMm, DryHeightMm, PadCentreMm));
        return b.Build(NewIds(), SlopeMaxPm, SwimEnterMm);
    }

    private static WorldTables Flat(int heightMm)
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(FlatTiles, FlatTiles, FlatCorners(FlatTiles, heightMm));
        return b.Build(SlopeMaxPm, SwimEnterMm);
    }

    private static int[] FlatCorners(int tiles, int heightMm)
    {
        var corners = new int[(tiles + 1) * (tiles + 1)];
        Array.Fill(corners, heightMm);
        return corners;
    }
}
