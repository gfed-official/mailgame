using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>Doc 02 §3.2: what a tile of the island is.</summary>
    public enum Biome : byte
    {
        DeepWater = 0,
        ShallowWater = 1,
        Beach = 2,
        Grassland = 3,
        Forest = 4,
        Rocky = 5,
    }

    /// <summary>
    /// The island around a generated town (doc 02 §3.1–§3.2): a heightmap of 2 m tiles in whole
    /// centimetres above sea level from fixed-point noise with a radial falloff, a bay cut into the
    /// south shore up to the town's beach, the coastline smoothed (no single-tile spits), and a
    /// biome for every tile — deep and shallow water by depth, beach within 3 tiles of the water,
    /// rocky ground high up or steep, forest from a noise mask on at least a quarter of the land,
    /// grassland the rest. The town's site is searched for on the natural ground (doc 02 §3.3,
    /// <see cref="PlaceTown"/>), and its grid levelled there to <see cref="TownLevelCm"/>: the sim
    /// walks the town flat, the land around it rises from it.
    /// </summary>
    public sealed class IslandTerrain
    {
        /// <summary>Small Island: 300 × 300 tiles, ~600 × 600 m.</summary>
        public const int Size = 300;
        /// <summary>How high the levelled town stands over the sea.</summary>
        public const int TownLevelCm = 150;
        /// <summary>Deeper than this the water is deep (doc 02 §3.2: "depth ≥ 2 m").</summary>
        public const int DeepCm = 200;
        /// <summary>Steeper than this, rocky: 20° over a 2 m tile.</summary>
        public const int RockySlopeCm = 73;
        /// <summary>Steeper than this, a cliff that cannot be built on: 35° over a 2 m tile.</summary>
        public const int CliffSlopeCm = 140;
        private const int MaxAttempts = 8;

        public int Cols { get; private set; }
        public int Rows { get; private set; }
        /// <summary>The island tile of the town's tile (0, 0).</summary>
        public int TownX { get; private set; }
        public int TownZ { get; private set; }
        public int TownCols { get; private set; }
        public int TownRows { get; private set; }
        public short[] HeightCm { get; private set; } = Array.Empty<short>();
        public Biome[] Biomes { get; private set; } = Array.Empty<Biome>();
        /// <summary>The highest ground, in centimetres.</summary>
        public int MaxHeightCm { get; private set; }
        /// <summary>The island's radius in tiles the falloff settled on (it grows when there is too little flat land).</summary>
        public int Radius { get; private set; }
        /// <summary>The site search's figures for the site taken (doc 02 §3.3), for the SeedViewer and the tests.</summary>
        public SiteScore Site { get; private set; }

        /// <summary>What a site for the town's grid is judged by: all in ‰ of its tiles, centimetres and tiles.</summary>
        public readonly struct SiteScore
        {
            /// <summary>Land under the grid, ‰.</summary>
            public readonly int Land;
            /// <summary>Ground under 10° (doc 02 §3.3 "flat"), ‰.</summary>
            public readonly int Flat;
            /// <summary>The mean height the ground is levelled by, in centimetres.</summary>
            public readonly int Levelling;
            /// <summary>Tiles from the grid's south edge to the sea (the bay's length).</summary>
            public readonly int Coast;
            /// <summary>Tiles from the grid's middle to the island's.</summary>
            public readonly int FromCentre;
            public readonly int Total;
            public SiteScore(int land, int flat, int levelling, int coast, int fromCentre)
            {
                Land = land; Flat = flat; Levelling = levelling; Coast = coast; FromCentre = fromCentre;
                Total = flat * 2 - levelling - coast * 5 - fromCentre * 5;
            }
        }

        /// <summary>Doc 02 §3.3: flat is under 10°, 35 cm over a 2 m tile.</summary>
        public const int FlatSlopeCm = 35;
        /// <summary>How far from the island's middle the town's grid may be moved (doc 02 §1: "PO near centre").</summary>
        public const int SiteReach = 40;
        /// <summary>The site search tries every so many tiles.</summary>
        private const int SiteStep = 4;
        /// <summary>Of a site's grid at least this much must be land, ‰.</summary>
        private const int SiteLandMin = 970;
        /// <summary>
        /// And all of this many tiles beyond its west, north and east edges, where its streets
        /// leave it and the raiders come in from the island.
        /// </summary>
        private const int SiteShore = 8;

        public bool InBounds(int ix, int iz) => ix >= 0 && iz >= 0 && ix < Cols && iz < Rows;

        public int Height(int ix, int iz) => InBounds(ix, iz) ? HeightCm[iz * Cols + ix] : -600;

        public Biome BiomeAt(int ix, int iz) => InBounds(ix, iz) ? Biomes[iz * Cols + ix] : Biome.DeepWater;

        public static bool IsWater(Biome b) => b == Biome.DeepWater || b == Biome.ShallowWater;

        /// <summary>The biggest step to a side neighbour, in centimetres: the slope over 2 m.</summary>
        public int SlopeCm(int ix, int iz)
        {
            int h = Height(ix, iz), s = 0;
            if (ix > 0) s = Math.Max(s, Math.Abs(h - Height(ix - 1, iz)));
            if (ix < Cols - 1) s = Math.Max(s, Math.Abs(h - Height(ix + 1, iz)));
            if (iz > 0) s = Math.Max(s, Math.Abs(h - Height(ix, iz - 1)));
            if (iz < Rows - 1) s = Math.Max(s, Math.Abs(h - Height(ix, iz + 1)));
            return s;
        }

        /// <summary>The biome of a tile of the town's grid.</summary>
        public Biome TownBiome(int tx, int tz) => BiomeAt(tx + TownX, tz + TownZ);

        /// <summary>
        /// Metres above the town's level at a point of the town's frame (metres, the map's x and z),
        /// between the tile centres: what the island looks like around the map. The sea is at
        /// −<see cref="TownLevelCm"/> / 100.
        /// </summary>
        public float MetresAboveTown(float x, float z)
        {
            float fx = x / SimConstants.TileSize - 0.5f + TownX, fz = z / SimConstants.TileSize - 0.5f + TownZ;
            int ix = (int)MathF.Floor(fx), iz = (int)MathF.Floor(fz);
            float u = fx - ix, v = fz - iz;
            float h = (Height(ix, iz) * (1f - u) + Height(ix + 1, iz) * u) * (1f - v)
                    + (Height(ix, iz + 1) * (1f - u) + Height(ix + 1, iz + 1) * u) * v;
            return (h - TownLevelCm) / 100f;
        }

        /// <summary>Counts of land, of each biome, and of buildable (not cliff) land.</summary>
        public int Count(Biome biome)
        {
            int n = 0;
            foreach (var b in Biomes) if (b == biome) n++;
            return n;
        }

        public int LandTiles => Biomes.Length - Count(Biome.DeepWater) - Count(Biome.ShallowWater);

        internal void AddTo(ref Fnv64 h)
        {
            h.Add(Cols);
            h.Add(Rows);
            h.Add(TownX);
            h.Add(TownZ);
            for (int i = 0; i < HeightCm.Length; i++) { h.Add((byte)HeightCm[i]); h.Add((byte)(HeightCm[i] >> 8)); h.Add((byte)Biomes[i]); }
        }

        /// <summary>
        /// The island for a town of <paramref name="townCols"/> × <paramref name="townRows"/> tiles
        /// whose last <paramref name="beachRows"/> rows are its beach, drawn from the seed's
        /// "heightmap" and "biomes" streams (with the attempt's suffix). Null, with the reason, if
        /// eight radii in turn leave less than 30 % of the land buildable or no deep water within
        /// 200 m of the pad.
        /// </summary>
        public static IslandTerrain? Generate(uint seed, string suffix, int townCols, int townRows, int beachRows, int padTx, int padTz, out string? problem) =>
            Generate(seed, suffix, townCols, townRows, beachRows, padTx, padTz, Shape.Small, out problem);

        /// <summary>
        /// What the island is like beyond its town grid: how big, how wide its land, and the water the grid
        /// holds itself (a Large Island's inlet, doc 02 §1 "towns separated by water inlets").
        /// </summary>
        public sealed class Shape
        {
            /// <summary>The island's tiles a side.</summary>
            public int Size { get; init; } = IslandTerrain.Size;
            /// <summary>The land's radius in tiles at the first attempt, and how much each further attempt adds.</summary>
            public int Radius { get; init; } = 134;
            public int RadiusStep { get; init; } = 6;
            /// <summary>
            /// The grid's own tiles, if it has water or beaches of its own beyond its last rows (null: the Small Island's
            /// grid, all land with its beach along the foot): water is cut below the sea there, a beach is beach.
            /// </summary>
            public Func<int, int, TileType>? GridTile { get; init; }
            /// <summary>Grid columns [X0, X1) whose water runs on north of the grid to the sea (an inlet cutting the island in two); null for none.</summary>
            public (int X0, int X1)? NorthChannel { get; init; }

            /// <summary>The Small Island: 300 × 300 tiles, its town grid all land.</summary>
            public static readonly Shape Small = new Shape();
        }

        /// <inheritdoc cref="Generate(uint, string, int, int, int, int, int, out string?)"/>
        public static IslandTerrain? Generate(uint seed, string suffix, int townCols, int townRows, int beachRows, int padTx, int padTz, Shape shape, out string? problem)
        {
            var heightRng = new RngStream(seed, "heightmap" + suffix);
            var biomeRng = new RngStream(seed, "biomes" + suffix);
            uint heightSeed = heightRng.NextUInt(), coastSeed = heightRng.NextUInt(), forestSeed = biomeRng.NextUInt();
            problem = null;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var t = new IslandTerrain { Cols = shape.Size, Rows = shape.Size, TownCols = townCols, TownRows = townRows, _shape = shape };
                t.Radius = shape.Radius + attempt * shape.RadiusStep;
                var natural = t.Natural(heightSeed, coastSeed);
                t.PlaceTown(natural);
                t.Heights(natural);
                t.Classify(natural, forestSeed, beachRows);
                problem = t.Check(padTx + t.TownX, padTz + t.TownZ);
                if (problem == null) return t;
            }
            return null;
        }

        private Shape _shape = Shape.Small;

        /// <summary>How deep the grid's own water is cut, centimetres under the sea: shallow and deep (doc 02 §3.2, 2 m).</summary>
        private const int GridShallowCm = 100, GridDeepCm = 300;

        /// <summary>The natural ground: noise hills under a radial falloff with a noisy coast.</summary>
        private int[] Natural(uint heightSeed, uint coastSeed)
        {
            var natural = new int[Cols * Rows];
            int cx = Cols / 2, cz = Rows / 2;
            const int Falloff = 24;
            for (int iz = 0; iz < Rows; iz++)
            {
                for (int ix = 0; ix < Cols; ix++)
                {
                    int dx = ix - cx, dz = iz - cz;
                    int dist = ISqrt((long)(dx * dx + dz * dz) << 20); // tiles × 1024
                    int jitter = (FixedNoise.Fbm(coastSeed, ix, iz, 32, 3) - 32768) * 22 * 1024 / 32768;
                    int inland = Math.Clamp((Radius * 1024 + jitter - dist) / Falloff, 0, 1024);
                    int q = FixedNoise.Fbm(heightSeed, ix, iz, 48, 4);
                    // Hills: the noise squared, so most of the land is low and a few tops stand out.
                    int land = 160 + (int)((long)q * q / 65536 * 1700 / 65536);
                    natural[iz * Cols + ix] = (land * inland - 600 * (1024 - inland)) / 1024;
                }
            }
            return natural;
        }

        /// <summary>
        /// Doc 02 §3.3, settlement placement: the town's grid goes where the natural ground under it
        /// is all land and scores best — flat (under 10°), little to level, near the south coast
        /// ("towns like the coast": its beach faces the bay), not far from the middle ("PO near
        /// centre"); every <see cref="SiteStep"/> tiles within <see cref="SiteReach"/> of the middle
        /// is tried. With no such site the grid stays in the middle.
        /// </summary>
        private void PlaceTown(int[] natural)
        {
            int midX = (Cols - TownCols) / 2, midZ = (Rows - TownRows) / 2;
            TownX = midX;
            TownZ = midZ;
            Site = Score(natural, midX, midZ);
            bool found = Site.Land >= SiteLandMin && LandAround(natural, midX, midZ);
            for (int z0 = midZ - SiteReach; z0 <= midZ + SiteReach; z0 += SiteStep)
            {
                for (int x0 = midX - SiteReach; x0 <= midX + SiteReach; x0 += SiteStep)
                {
                    if (x0 < SiteShore || z0 < SiteShore || x0 + TownCols + SiteShore >= Cols || z0 + TownRows >= Rows - 1) continue;
                    var score = Score(natural, x0, z0);
                    // Within the reach all round, not to the corners of the square searched.
                    if (score.FromCentre > SiteReach) continue;
                    if (score.Land < SiteLandMin || !LandAround(natural, x0, z0)) continue;
                    if (found && score.Total <= Site.Total) continue;
                    found = true;
                    Site = score;
                    TownX = x0;
                    TownZ = z0;
                }
            }
        }

        /// <summary>True if the ground just beyond the grid's west, north and east edges is all land.</summary>
        private bool LandAround(int[] natural, int x0, int z0)
        {
            for (int iz = z0 - SiteShore; iz < z0 + TownRows; iz++)
                for (int ix = x0 - SiteShore; ix < x0 + TownCols + SiteShore; ix++)
                {
                    bool beyond = iz < z0 || ix < x0 || ix >= x0 + TownCols;
                    if (beyond && natural[iz * Cols + ix] <= 0) return false;
                }
            return true;
        }

        private SiteScore Score(int[] natural, int x0, int z0)
        {
            int all = 0, land = 0, flat = 0;
            long level = 0;
            for (int iz = z0; iz < z0 + TownRows; iz += 2)
            {
                for (int ix = x0; ix < x0 + TownCols; ix += 2)
                {
                    int h = natural[iz * Cols + ix];
                    all++;
                    if (h <= 0) continue;
                    land++;
                    level += Math.Abs(h - TownLevelCm);
                    int slope = 0;
                    if (ix > 0) slope = Math.Max(slope, Math.Abs(h - natural[iz * Cols + ix - 1]));
                    if (ix < Cols - 1) slope = Math.Max(slope, Math.Abs(h - natural[iz * Cols + ix + 1]));
                    if (iz > 0) slope = Math.Max(slope, Math.Abs(h - natural[(iz - 1) * Cols + ix]));
                    if (iz < Rows - 1) slope = Math.Max(slope, Math.Abs(h - natural[(iz + 1) * Cols + ix]));
                    if (slope <= FlatSlopeCm) flat++;
                }
            }
            // The bay runs south from the grid's middle to the sea.
            int coast = 0, mid = x0 + TownCols / 2;
            for (int iz = z0 + TownRows; iz < Rows && natural[iz * Cols + mid] > 0; iz++) coast++;
            int dx = x0 + TownCols / 2 - Cols / 2, dz = z0 + TownRows / 2 - Rows / 2;
            return new SiteScore(land * 1000 / all, flat * 1000 / all, land > 0 ? (int)(level / land) : 0, coast, ISqrt((long)dx * dx + (long)dz * dz));
        }

        /// <summary>Heights: the natural ground with the town levelled into it, and the bay.</summary>
        private void Heights(int[] natural)
        {
            int n = Cols * Rows;
            var h = new int[n];
            int townX1 = TownX + TownCols, townZ1 = TownZ + TownRows, townMid = TownX + TownCols / 2;
            const int Margin = 26;
            for (int iz = 0; iz < Rows; iz++)
            {
                for (int ix = 0; ix < Cols; ix++)
                {
                    int ground = natural[iz * Cols + ix];

                    // The town is levelled; the ground blends into it over 26 tiles round it.
                    int ox = Math.Max(0, Math.Max(TownX - ix, ix - (townX1 - 1)));
                    int oz = Math.Max(0, Math.Max(TownZ - iz, iz - (townZ1 - 1)));
                    int outside = Math.Max(ox, oz);
                    int w = outside >= Margin ? 0 : (Margin - outside) * 1024 / Margin;
                    int level = Math.Max(ground, TownLevelCm);
                    int y = (TownLevelCm * w + level * (1024 - w)) / 1024;
                    if (outside >= Margin) y = ground;

                    // The bay: south of the town the shore falls into the sea, wider the farther out.
                    if (iz >= townZ1)
                    {
                        int row = iz - townZ1;
                        int half = TownCols / 2 + 6 + row / 2;
                        int side = Math.Clamp((half - Math.Abs(ix - townMid)) * 1024 / 8, 0, 1024);
                        int bay = TownLevelCm - 60 * (row + 1);
                        if (bay < y) y = (bay * side + y * (1024 - side)) / 1024;
                    }
                    // The grid's own water (a Large Island's inlet) under the sea, and its channel on north to the coast.
                    if (_shape.GridTile != null && InTown(ix, iz))
                    {
                        var tile = _shape.GridTile(ix - TownX, iz - TownZ);
                        if (tile == TileType.Water) y = -GridShallowCm;
                        else if (tile == TileType.DeepWater) y = -GridDeepCm;
                    }
                    else if (_shape.NorthChannel is { } ch && iz < TownZ)
                    {
                        int c0 = TownX + ch.X0, c1 = TownX + ch.X1;
                        // Shelving in over 3 tiles each side, deep in the middle; the banks fall to it over 6.
                        int inside = Math.Min(ix - c0, c1 - 1 - ix);
                        if (inside >= 0) y = inside < 2 ? -GridShallowCm : -GridDeepCm;
                        else if (inside > -6) y = Math.Min(y, TownLevelCm * (-inside) / 6);
                    }
                    h[iz * Cols + ix] = Math.Clamp(y, -600, 3000);
                }
            }
            // The coast smoothed: a land tile with fewer than two land neighbours goes under (no spits).
            for (int pass = 0; pass < 12; pass++)
            {
                bool changed = false;
                for (int iz = 0; iz < Rows; iz++)
                {
                    for (int ix = 0; ix < Cols; ix++)
                    {
                        int i = iz * Cols + ix;
                        if (h[i] <= 0 || InTown(ix, iz)) continue;
                        int landSides = 0;
                        if (ix > 0 && h[i - 1] > 0) landSides++;
                        if (ix < Cols - 1 && h[i + 1] > 0) landSides++;
                        if (iz > 0 && h[i - Cols] > 0) landSides++;
                        if (iz < Rows - 1 && h[i + Cols] > 0) landSides++;
                        if (landSides < 2) { h[i] = -50; changed = true; }
                    }
                }
                if (!changed) break;
            }
            HeightCm = new short[n];
            int max = 0;
            for (int i = 0; i < n; i++) { HeightCm[i] = (short)h[i]; max = Math.Max(max, h[i]); }
            MaxHeightCm = max;
        }

        /// <summary>A beach tile of the town's grid: its last rows, or the grid's own beaches where it says.</summary>
        private bool GridBeach(int ix, int iz, int beachRows) =>
            _shape.GridTile != null ? _shape.GridTile(ix - TownX, iz - TownZ) == TileType.Beach : iz >= TownZ + TownRows - beachRows;

        private bool InTown(int ix, int iz) => ix >= TownX && iz >= TownZ && ix < TownX + TownCols && iz < TownZ + TownRows;

        private void Classify(int[] natural, uint forestSeed, int beachRows)
        {
            int n = Cols * Rows;
            Biomes = new Biome[n];
            // Distance to the water, 8-way, up to 3 tiles.
            var toWater = new int[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (HeightCm[i] <= 0) { toWater[i] = 0; queue.Enqueue(i); }
                else toWater[i] = int.MaxValue;
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                if (toWater[i] >= 3) continue;
                int ix = i % Cols, iz = i / Cols;
                for (int oz = -1; oz <= 1; oz++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int x = ix + ox, z = iz + oz;
                        if (!InBounds(x, z)) continue;
                        int j = z * Cols + x;
                        if (toWater[j] > toWater[i] + 1) { toWater[j] = toWater[i] + 1; queue.Enqueue(j); }
                    }
            }
            int maxNatural = 1;
            foreach (int v in natural) maxNatural = Math.Max(maxNatural, v);
            int rockyAbove = maxNatural * 60 / 100;
            var grass = new List<long>();
            for (int iz = 0; iz < Rows; iz++)
            {
                for (int ix = 0; ix < Cols; ix++)
                {
                    int i = iz * Cols + ix;
                    int h = HeightCm[i];
                    Biome b;
                    bool town = InTown(ix, iz);
                    if (h <= 0) b = -h < DeepCm ? Biome.ShallowWater : Biome.DeepWater;
                    else if (town ? GridBeach(ix, iz, beachRows) : toWater[i] <= 3) b = Biome.Beach;
                    // A hill the town was levelled out of keeps its stony ground.
                    else if (natural[i] > rockyAbove || (!town && SlopeCm(ix, iz) > RockySlopeCm)) b = Biome.Rocky;
                    else
                    {
                        b = Biome.Grassland;
                        grass.Add(((long)FixedNoise.Fbm(forestSeed, ix, iz, 20, 3) << 20) | (uint)i);
                    }
                    Biomes[i] = b;
                }
            }
            // Forest on the grassland where the mask is highest: 30 % of the land, or all the grass there is.
            int land = 0;
            foreach (var b in Biomes) if (!IsWater(b)) land++;
            int forest = Math.Min(grass.Count, land * 30 / 100);
            grass.Sort();
            for (int k = grass.Count - forest; k < grass.Count; k++) Biomes[(int)(grass[k] & 0xFFFFF)] = Biome.Forest;
        }

        /// <summary>Doc 02 §3.1 and §3.12: enough buildable land, and deep water within 200 m of the pad.</summary>
        private string? Check(int padX, int padZ)
        {
            int land = 0, buildable = 0;
            for (int iz = 0; iz < Rows; iz++)
                for (int ix = 0; ix < Cols; ix++)
                {
                    if (IsWater(BiomeAt(ix, iz))) continue;
                    land++;
                    if (SlopeCm(ix, iz) <= CliffSlopeCm) buildable++;
                }
            if (land == 0 || buildable * 10 < land * 3) return $"only {buildable} of {land} land tiles can be built on";
            const int reach = 100;
            for (int iz = Math.Max(0, padZ - reach); iz <= Math.Min(Rows - 1, padZ + reach); iz++)
                for (int ix = Math.Max(0, padX - reach); ix <= Math.Min(Cols - 1, padX + reach); ix++)
                    if (BiomeAt(ix, iz) == Biome.DeepWater && (ix - padX) * (ix - padX) + (iz - padZ) * (iz - padZ) <= reach * reach) return null;
            return "no deep water within 200 m of the Post Office";
        }

        private static int ISqrt(long v)
        {
            if (v <= 0) return 0;
            long r = (long)Math.Sqrt(v);
            while (r * r > v) r--;
            while ((r + 1) * (r + 1) <= v) r++;
            return (int)r;
        }
    }
}
