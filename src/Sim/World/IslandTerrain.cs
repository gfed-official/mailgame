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
    /// grassland the rest. The town's own grid sits in the middle, levelled to
    /// <see cref="TownLevelCm"/>: the sim walks the town flat, the land around it rises from it.
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
        public static IslandTerrain? Generate(uint seed, string suffix, int townCols, int townRows, int beachRows, int padTx, int padTz, out string? problem)
        {
            var heightRng = new RngStream(seed, "heightmap" + suffix);
            var biomeRng = new RngStream(seed, "biomes" + suffix);
            uint heightSeed = heightRng.NextUInt(), coastSeed = heightRng.NextUInt(), forestSeed = biomeRng.NextUInt();
            problem = null;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var t = new IslandTerrain { Cols = Size, Rows = Size, TownCols = townCols, TownRows = townRows };
                t.TownX = (Size - townCols) / 2;
                t.TownZ = (Size - townRows) / 2;
                t.Radius = 134 + attempt * 6;
                var natural = t.Heights(heightSeed, coastSeed);
                t.Classify(natural, forestSeed, beachRows);
                problem = t.Check(padTx + t.TownX, padTz + t.TownZ);
                if (problem == null) return t;
            }
            return null;
        }

        /// <summary>Heights: the natural ground, the town levelled into it, the bay; returns the natural ground.</summary>
        private int[] Heights(uint heightSeed, uint coastSeed)
        {
            int n = Cols * Rows;
            var natural = new int[n];
            var h = new int[n];
            int cx = Cols / 2, cz = Rows / 2;
            int townX1 = TownX + TownCols, townZ1 = TownZ + TownRows, townMid = TownX + TownCols / 2;
            const int Margin = 26, Falloff = 24;
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
                    int ground = (land * inland - 600 * (1024 - inland)) / 1024;
                    natural[iz * Cols + ix] = ground;

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
            return natural;
        }

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
                    else if (town ? iz >= TownZ + TownRows - beachRows : toWater[i] <= 3) b = Biome.Beach;
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
