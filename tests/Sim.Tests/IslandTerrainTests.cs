using System.Collections.Generic;
using System;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The island round a generated town (doc 02 §3.1–§3.2, §3.12): heights in whole centimetres
    /// from fixed-point noise, the same on every machine; a coast without spits; water, beach,
    /// rocky, forest and grassland by the doc's rules; the town levelled into it with its beach on
    /// the bay; and the scenery grown by the biome it stands on.
    /// </summary>
    public class IslandTerrainTests
    {
        private static ContentDb Db => TestContent.Db;

        [Fact]
        public void TheNoiseIsIntegerOnly_AndPinned()
        {
            // Pinned values: a change here changes every island of every seed.
            Assert.Equal(FixedNoise.Value(1, 0, 0, 16), FixedNoise.Value(1, 0, 0, 16));
            for (int x = -40; x < 40; x += 7)
                for (int y = -40; y < 40; y += 5)
                    Assert.InRange(FixedNoise.Fbm(9, x, y, 32, 4), 0, 65535);
            // On a lattice point the octave is its corner's hash; between, it lies between the corners.
            int a = FixedNoise.Value(3, 16, 16, 16), b = FixedNoise.Value(3, 32, 16, 16), mid = FixedNoise.Value(3, 24, 16, 16);
            Assert.InRange(mid, Math.Min(a, b), Math.Max(a, b));
            Assert.Equal(PinnedNoise, FixedNoise.Fbm(12345, 77, -31, 48, 4));
        }

        private const int PinnedNoise = 23978;

        [Fact]
        public void TheSameSeed_MakesTheSameIsland()
        {
            var a = WorldGenerator.Generate(Db, 21).Terrain!;
            var b = WorldGenerator.Generate(Db, 21).Terrain!;
            Assert.Equal(a.HeightCm, b.HeightCm);
            Assert.Equal(a.Biomes, b.Biomes);
            Assert.NotEqual(a.HeightCm, WorldGenerator.Generate(Db, 22).Terrain!.HeightCm);
            Assert.Null(Db.Map("test_street").Terrain);
        }

        [Fact]
        public void EveryIslandOfThirtySeeds_FollowsTheDocsRules()
        {
            for (uint seed = 1; seed <= 30; seed++)
            {
                var map = WorldGenerator.Generate(Db, seed);
                var t = map.Terrain!;
                Assert.Equal((IslandTerrain.Size, IslandTerrain.Size), (t.Cols, t.Rows));
                int land = 0, forest = 0, buildable = 0;
                for (int iz = 0; iz < t.Rows; iz++)
                {
                    for (int ix = 0; ix < t.Cols; ix++)
                    {
                        int h = t.Height(ix, iz);
                        var biome = t.BiomeAt(ix, iz);
                        bool town = ix >= t.TownX && iz >= t.TownZ && ix < t.TownX + map.Cols && iz < t.TownZ + map.Rows;
                        // Water by depth (doc 02 §3.2: shallow under 2 m).
                        if (h <= 0)
                        {
                            Assert.Equal(-h < IslandTerrain.DeepCm ? Biome.ShallowWater : Biome.DeepWater, biome);
                            continue;
                        }
                        land++;
                        Assert.False(IslandTerrain.IsWater(biome));
                        if (biome == Biome.Forest) forest++;
                        if (t.SlopeCm(ix, iz) <= IslandTerrain.CliffSlopeCm) buildable++;
                        if (town)
                        {
                            // The town is levelled, its own beach the island's beach.
                            Assert.Equal(IslandTerrain.TownLevelCm, h);
                            Assert.Equal(map.GetTile(ix - t.TownX, iz - t.TownZ) == TileType.Beach, biome == Biome.Beach);
                            continue;
                        }
                        // No spits: every land tile has two land neighbours at least.
                        int sides = (t.Height(ix - 1, iz) > 0 ? 1 : 0) + (t.Height(ix + 1, iz) > 0 ? 1 : 0) + (t.Height(ix, iz - 1) > 0 ? 1 : 0) + (t.Height(ix, iz + 1) > 0 ? 1 : 0);
                        Assert.True(sides >= 2, $"seed {seed}: a spit at ({ix}, {iz})");
                        // Beach exactly where the water is within 3 tiles.
                        bool nearWater = false;
                        for (int oz = -3; oz <= 3; oz++)
                            for (int ox = -3; ox <= 3; ox++)
                                nearWater |= t.InBounds(ix + ox, iz + oz) && t.Height(ix + ox, iz + oz) <= 0;
                        Assert.Equal(nearWater, biome == Biome.Beach);
                        if (!nearWater && t.SlopeCm(ix, iz) > IslandTerrain.RockySlopeCm) Assert.Equal(Biome.Rocky, biome);
                    }
                }
                Assert.True(forest * 4 >= land, $"seed {seed}: forest {forest} of {land}");
                Assert.True(buildable * 10 >= land * 3, $"seed {seed}: buildable {buildable} of {land}");
                // The map is the doc's 600 × 600 m; the land on it some 120 000 m² and more.
                Assert.True(land * 4 > 120_000, $"seed {seed}: {land * 4} m² of land");
                // Deep water within 200 m of the pad, and the bay right below the town's beach.
                var po = map.PostOffice;
                int px = (int)(po.SpawnPadX / SimConstants.TileSize) + t.TownX, pz = (int)(po.SpawnPadZ / SimConstants.TileSize) + t.TownZ;
                bool deep = false;
                for (int iz = pz - 100; iz <= pz + 100; iz++)
                    for (int ix = px - 100; ix <= px + 100; ix++)
                        deep |= t.BiomeAt(ix, iz) == Biome.DeepWater && (ix - px) * (ix - px) + (iz - pz) * (iz - pz) <= 100 * 100;
                Assert.True(deep, $"seed {seed}: no deep water within 200 m");
                int below = t.TownZ + map.Rows + 3, mid = t.TownX + map.Cols / 2;
                Assert.True(IslandTerrain.IsWater(t.BiomeAt(mid, below)));
                // In the sim the town is flat; the view is the island's, the sea under the town's level.
                Assert.Equal(0f, t.MetresAboveTown(map.WidthM * 0.5f, map.DepthM * 0.5f), 3);
                Assert.True(t.MetresAboveTown(map.WidthM * 0.5f, map.DepthM + 20f) < 0f);
            }
        }

        [Fact]
        public void TheTownsSite_IsSearchedFor_OnLandAndFlat_NearTheMiddle_AndDiffersBySeed()
        {
            // Doc 02 §3.3: the town goes where the ground is flat, by the coast, near the middle.
            var sites = new HashSet<(int, int)>();
            int central = 0;
            for (uint seed = 1; seed <= 30; seed++)
            {
                var map = WorldGenerator.Generate(Db, seed);
                var t = map.Terrain!;
                var site = t.Site;
                sites.Add((t.TownX, t.TownZ));
                int midX = (t.Cols - map.Cols) / 2, midZ = (t.Rows - map.Rows) / 2;
                Assert.InRange(t.TownX - midX, -IslandTerrain.SiteReach, IslandTerrain.SiteReach);
                Assert.InRange(t.TownZ - midZ, -IslandTerrain.SiteReach, IslandTerrain.SiteReach);
                // Within 80 m all round, the square's corners not included.
                Assert.True(site.FromCentre <= IslandTerrain.SiteReach, $"seed {seed}: {site.FromCentre * 2} m off the middle");
                if (t.TownX == midX && t.TownZ == midZ) central++;
                // All but a few tiles under the grid were land, and most of them flat.
                Assert.True(site.Land >= 970, $"seed {seed}: {site.Land}‰ land");
                Assert.True(site.Flat >= 850, $"seed {seed}: {site.Flat}‰ flat");
                Assert.Equal(site.Flat * 2 - site.Levelling - site.Coast * 5 - site.FromCentre * 5, site.Total);
                // Land beyond the west, north and east edges, where the streets leave and the raiders come in.
                foreach (var (ix, iz) in new[] { (t.TownX - 4, t.TownZ + map.Rows / 2), (t.TownX + map.Cols + 3, t.TownZ + map.Rows / 2), (t.TownX + map.Cols / 2, t.TownZ - 4) })
                    Assert.False(IslandTerrain.IsWater(t.BiomeAt(ix, iz)), $"seed {seed}: water at ({ix}, {iz}) beside the town");
            }
            // Not one place for every seed: the island decides.
            Assert.True(sites.Count >= 15, $"only {sites.Count} sites for 30 seeds");
            Assert.True(central < 10, $"{central} of 30 towns in the very middle");
        }

        [Fact]
        public void TheScenery_GrowsByTheBiome()
        {
            int forestTrees = 0, forestTiles = 0, grassTrees = 0, grassTiles = 0;
            for (uint seed = 1; seed <= 10; seed++)
            {
                var map = WorldGenerator.Generate(Db, seed);
                for (int tz = 0; tz < map.Rows; tz++)
                    for (int tx = 0; tx < map.Cols; tx++)
                    {
                        if (map.GetTile(tx, tz) != TileType.Grass) continue;
                        if (map.BiomeAt(tx, tz) == Biome.Forest) forestTiles++;
                        else if (map.BiomeAt(tx, tz) == Biome.Grassland) grassTiles++;
                    }
                foreach (var p in map.Props.Where(p => p.Kind == PropKind.Tree))
                {
                    var b = map.BiomeAt((int)(p.X / SimConstants.TileSize), (int)(p.Z / SimConstants.TileSize));
                    if (b == Biome.Forest) forestTrees++;
                    else if (b == Biome.Grassland) grassTrees++;
                }
                Assert.Null(WorldGenerator.Validate(map));
                foreach (var (kind, least) in WorldGenerator.ResourceMinimums)
                    Assert.True(map.Props.Count(p => p.Kind == kind) >= least, $"seed {seed}: too few {kind}");
            }
            Assert.True(forestTiles > 0 && grassTiles > 0);
            // The forest grows more than twice the trees a tile of the grassland does.
            Assert.True((double)forestTrees / forestTiles > 2.0 * grassTrees / grassTiles,
                $"forest {forestTrees}/{forestTiles}, grassland {grassTrees}/{grassTiles}");
            // A map from a file has no island: its beach is beach, the rest grassland.
            var street = Db.Map("test_street");
            Assert.Equal(Biome.Grassland, street.BiomeAt(1, 1));
        }
    }
}
