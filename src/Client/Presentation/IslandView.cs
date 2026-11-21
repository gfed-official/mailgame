using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// What lies around the playable map in the concept video: the town sits on an island of rolling
    /// grass and cliffs above the sea, with a harbour dock of crates, mail trucks and boats, a
    /// conveyor climbing to a wooden tower, and a spiked palisade with a gate and watch towers
    /// where the street leaves town. None of it can be walked on. A generated town's island is the
    /// sim's (<see cref="IslandTerrain"/>): its heights, its coast, its biomes and the bay under the
    /// town's beach, where the harbour lies; the test street keeps the island drawn for it.
    /// </summary>
    public static class IslandView
    {
        public const float SeaLevel = -3.5f;
        private const float DockTop = -2.6f;
        private const float Step = 4f;
        private const float Tile = SimConstants.TileSize;

        private static readonly Vector2 Harbour = new Vector2(-40f, 36f);

        private static float Fbm(float x, float z)
        {
            float sum = 0f, amplitude = 0.5f;
            var p = new Vector3(x, 0.37f, z);
            for (int i = 0; i < 4; i++)
            {
                sum += amplitude * MeshKit.Noise3(p);
                p = p * 2.03f + new Vector3(17f, 0f, 9f);
                amplitude *= 0.5f;
            }
            return sum;
        }

        /// <summary>How far a point is outside the map's rectangle (0 inside).</summary>
        private static float Outside(WorldMap map, float x, float z)
        {
            float dx = Mathf.Max(0f, Mathf.Max(-x, x - map.WidthM)), dz = Mathf.Max(0f, Mathf.Max(-z, z - map.DepthM));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The street an edge point continues: the road runs on past the map to the harbour and out of the gate.</summary>
        private static float RoadWeight(WorldMap map, float x, float z)
        {
            float best = 0f;
            foreach (var street in map.Streets)
            {
                var r = street.Rect;
                // Only a street that leaves the map runs on past it (a back street of a generated town ends in town).
                if (r.W < r.H || (r.X > 0 && r.X + r.W < map.Cols)) continue;
                float z0 = r.Y * Tile, z1 = (r.Y + r.H) * Tile;
                float off = Mathf.Max(0f, Mathf.Max(z0 - z, z - z1));
                best = Mathf.Max(best, 1f - Mathf.SmoothStep(1.5f, 6f, off));
            }
            return best;
        }

        /// <summary>Where the sea stands: under a generated town by the island's level, else the test street's.</summary>
        public static float SeaLevelOf(WorldMap map) => map.Terrain != null ? -IslandTerrain.TownLevelCm / 100f : SeaLevel;

        /// <summary>Height of the island's ground. Inside the map it is the sim's flat 0.</summary>
        public static float Height(WorldMap map, float x, float z)
        {
            float d = Outside(map, x, z);
            if (d <= 0f) return 0f;
            if (map.Terrain is IslandTerrain island)
            {
                // The streets that leave the town run on level for a while, then climb with the land.
                float ground = island.MetresAboveTown(x, z);
                float off = x < 0f ? -x : x - map.WidthM;
                if (off > 0f) ground = Mathf.Lerp(ground, 0f, RoadWeight(map, x, z) * (1f - Mathf.SmoothStep(30f, 60f, off)));
                return ground;
            }
            float cx = map.WidthM * 0.5f, cz = map.DepthM * 0.5f;
            float ex = (x - cx) / (cx + 72f), ez = (z - cz) / (cz + 62f);
            float r = Mathf.Sqrt(ex * ex + ez * ez) + (Fbm(x * 0.021f, z * 0.021f) - 0.5f) * 0.28f;

            // Rolling ground that rises away from town, most of all to the north.
            float h = Mathf.SmoothStep(5f, 32f, d) * (Fbm(x * 0.034f + 5f, z * 0.034f) * 10f - 2.4f);
            h += Mathf.SmoothStep(10f, 44f, -z) * 4.5f;
            h = Mathf.Max(h, -0.4f);

            // The road west drops to the dock; the road east leaves level through the gate.
            float road = RoadWeight(map, x, z);
            if (x < 0f) h = Mathf.Lerp(h, Mathf.Lerp(0f, DockTop, Mathf.SmoothStep(3f, 24f, -x)), road * (1f - Mathf.SmoothStep(26f, 34f, -x)));
            else if (x > map.WidthM) h = Mathf.Lerp(h, 0f, road * (1f - Mathf.SmoothStep(20f, 40f, x - map.WidthM)));

            // The harbour bay, then the cliffs all round.
            float bay = 1f - Mathf.SmoothStep(15f, 30f, new Vector2(x, z).DistanceTo(Harbour));
            h = Mathf.Lerp(h, -6f, bay);
            return Mathf.Lerp(h, -8f, Mathf.SmoothStep(0.84f, 0.97f, r));
        }

        /// <returns>Lights for the night: the torches at the gate.</returns>
        public static List<Vector3> Build(Node3D parent, MeshBatch batch, WorldMap map, ShaderMaterial ground)
        {
            parent.AddChild(new MeshInstance3D { Name = "Terrain", Mesh = Terrain(map), MaterialOverride = ground });
            parent.AddChild(new MeshInstance3D
            {
                Name = "Sea",
                Mesh = new PlaneMesh { Size = new Vector2(2400f, 2400f) },
                MaterialOverride = Shaders.Water(),
                Position = new Vector3(map.WidthM * 0.5f, SeaLevelOf(map), map.DepthM * 0.5f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            Islets(batch, map);
            if (map.Terrain is IslandTerrain island) IslandGreenery(batch, map, island);
            else Greenery(batch, map);
            Hedges(batch, map);
            if (map.Terrain != null)
            {
                // The harbour in the bay under the town's beach, its deck level with the shore.
                float sea = SeaLevelOf(map);
                Dock(batch, map, new Vector2(map.WidthM * 0.3f, map.DepthM + 14f), sea + 0.9f, sea, tower: false);
            }
            else Dock(batch, map, Harbour, DockTop, SeaLevel, tower: true);
            return Palisade(batch, map);
        }

        private static Mesh Terrain(WorldMap map)
        {
            var b = new MeshBuilder();
            float cx = map.WidthM * 0.5f, cz = map.DepthM * 0.5f;
            // The whole of a generated island; round the test street what the camera can see.
            float rx = 176f, rz = 136f;
            if (map.Terrain is IslandTerrain island) { rx = island.Cols * Tile * 0.5f; rz = island.Rows * Tile * 0.5f; }
            int x0 = Mathf.FloorToInt((cx - rx) / Step), x1 = Mathf.CeilToInt((cx + rx) / Step);
            int z0 = Mathf.FloorToInt((cz - rz) / Step), z1 = Mathf.CeilToInt((cz + rz) / Step);
            var index = new int[x1 - x0 + 1, z1 - z0 + 1];
            for (int ix = x0; ix <= x1; ix++)
                for (int iz = z0; iz <= z1; iz++)
                    index[ix - x0, iz - z0] = b.Add(new Vector3(ix * Step, Height(map, ix * Step, iz * Step), iz * Step), Vector3.Up);
            for (int ix = x0; ix < x1; ix++)
            {
                for (int iz = z0; iz < z1; iz++)
                {
                    // The map's own ground is drawn by the town; the island starts at its edge.
                    float mx = (ix + 0.5f) * Step, mz = (iz + 0.5f) * Step;
                    if (mx > 0f && mx < map.WidthM && mz > 0f && mz < map.DepthM) continue;
                    int i = ix - x0, j = iz - z0;
                    b.Quad(index[i, j], index[i + 1, j], index[i + 1, j + 1], index[i, j + 1]);
                }
            }
            b.SmoothNormals();
            return b.Build();
        }

        private static void Islets(MeshBatch batch, WorldMap map)
        {
            var rock = MeshKit.Mat(new Color("4f4a63"), 0.95f);
            var mesh = MeshKit.Blob(1f, 0.22f, 1.4f, 41, new Vector3(1f, 0.5f, 1f), 10);
            var centre = new Vector3(map.WidthM * 0.5f, SeaLevel, map.DepthM * 0.5f);
            (float Angle, float Distance, float Size)[] islets = { (0.5f, 330f, 60f), (1.9f, 420f, 90f), (2.8f, 300f, 45f), (4.1f, 380f, 70f), (5.3f, 460f, 110f) };
            foreach (var (angle, distance, size) in islets)
                batch.Add(mesh, rock, new Transform3D(Basis.Identity.Scaled(new Vector3(size, size * 0.5f, size * 0.8f)), centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance));
        }

        private static void Greenery(MeshBatch batch, WorldMap map)
        {
            const float cell = 7f;
            float cx = map.WidthM * 0.5f, cz = map.DepthM * 0.5f;
            for (float gz = cz - 130f; gz < cz + 130f; gz += cell)
            {
                for (float gx = cx - 170f; gx < cx + 170f; gx += cell)
                {
                    var seed = new Vector3(gx * 0.713f, 3.1f, gz * 0.531f);
                    float x = gx + (MeshKit.Noise3(seed) - 0.5f) * cell * 1.6f, z = gz + (MeshKit.Noise3(seed + new Vector3(9f, 0f, 0f)) - 0.5f) * cell * 1.6f;
                    if (Outside(map, x, z) < 3.5f || RoadWeight(map, x, z) > 0.2f) continue;
                    float h = Height(map, x, z);
                    if (h < -1.2f) continue;
                    float slope = Mathf.Abs(Height(map, x + 2f, z) - Height(map, x - 2f, z)) + Mathf.Abs(Height(map, x, z + 2f) - Height(map, x, z - 2f));
                    float roll = MeshKit.Noise3(seed * 3.7f + new Vector3(0f, 5f, 0f)) * 100f;
                    float scale = 0.9f + MeshKit.Noise3(seed * 5.3f) * 0.8f, yaw = roll * 0.7f;
                    int variant = (int)(MeshKit.Noise3(seed * 7.9f) * 2.99f);
                    var at = new Vector3(x, h - 0.05f, z);
                    if (slope > 2.2f)
                    {
                        if (roll < 30f) SceneryView.Boulder(batch, at, yaw, scale * 2.2f, variant);
                    }
                    else if (roll < 46f) SceneryView.Tree(batch, at, yaw, scale * 1.15f, variant);
                    else if (roll < 60f) SceneryView.Bush(batch, at, yaw, scale * 1.2f, variant);
                    else if (roll < 68f) SceneryView.Boulder(batch, at, yaw, scale * 1.4f, variant);
                }
            }
        }

        /// <summary>
        /// The island's own growth, by the sim's biome under it (doc 02 §3.2): woods in the
        /// forest, boulders on rocky ground, a tree or a bush here and there on the grassland,
        /// nothing on the beach and in the water. Scenery only: none of it is a node.
        /// </summary>
        private static void IslandGreenery(MeshBatch batch, WorldMap map, IslandTerrain island)
        {
            const float cell = 8f;
            float sea = SeaLevelOf(map);
            float left = -island.TownX * Tile, top = -island.TownZ * Tile;
            float right = left + island.Cols * Tile, bottom = top + island.Rows * Tile;
            for (float gz = top + cell * 0.5f; gz < bottom; gz += cell)
            {
                for (float gx = left + cell * 0.5f; gx < right; gx += cell)
                {
                    var seed = new Vector3(gx * 0.713f, 3.1f, gz * 0.531f);
                    float x = gx + (MeshKit.Noise3(seed) - 0.5f) * cell * 1.4f, z = gz + (MeshKit.Noise3(seed + new Vector3(9f, 0f, 0f)) - 0.5f) * cell * 1.4f;
                    if (Outside(map, x, z) < 3.5f || RoadWeight(map, x, z) > 0.2f) continue;
                    float h = Height(map, x, z);
                    if (h < sea + 0.5f) continue;
                    var biome = island.BiomeAt(Mathf.FloorToInt(x / Tile) + island.TownX, Mathf.FloorToInt(z / Tile) + island.TownZ);
                    float roll = MeshKit.Noise3(seed * 3.7f + new Vector3(0f, 5f, 0f)) * 100f;
                    float scale = 0.9f + MeshKit.Noise3(seed * 5.3f) * 0.8f, yaw = roll * 0.7f;
                    int variant = (int)(MeshKit.Noise3(seed * 7.9f) * 2.99f);
                    var at = new Vector3(x, h - 0.05f, z);
                    switch (biome)
                    {
                        case Biome.Forest:
                            if (roll < 72f) SceneryView.Tree(batch, at, yaw, scale * 1.2f, variant);
                            else if (roll < 84f) SceneryView.Bush(batch, at, yaw, scale * 1.2f, variant);
                            break;
                        case Biome.Rocky:
                            if (roll < 40f) SceneryView.Boulder(batch, at, yaw, scale * 2.0f, variant);
                            else if (roll < 46f) SceneryView.Tree(batch, at, yaw, scale, variant);
                            break;
                        case Biome.Grassland:
                            if (roll < 12f) SceneryView.Tree(batch, at, yaw, scale * 1.1f, variant);
                            else if (roll < 26f) SceneryView.Bush(batch, at, yaw, scale * 1.2f, variant);
                            else if (roll < 29f) SceneryView.Boulder(batch, at, yaw, scale * 1.3f, variant);
                            break;
                    }
                }
            }
        }

        /// <summary>A hedge marks the edge of town on the three sides without a wall.</summary>
        private static void Hedges(MeshBatch batch, WorldMap map)
        {
            var leaf = Shaders.Foliage(new Color("2c5f22"), new Color("86ad3a"), -0.6f, 1.2f);
            var mesh = MeshKit.RoundedBox(new Vector3(4.3f, 1.25f, 1.1f), 0.42f, 4);
            void Run(Vector3 from, Vector3 to)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(from.DistanceTo(to) / 4f));
                var along = (to - from) / count;
                float yaw = Mathf.Atan2(-along.Z, along.X);
                for (int i = 0; i < count; i++)
                {
                    var mid = from + along * (i + 0.5f);
                    float grow = 0.85f + 0.35f * MeshKit.Noise3(mid * 0.9f);
                    batch.Add(mesh, leaf, new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(1f, grow, 1f)), mid + new Vector3(0f, 0.6f * grow, 0f)));
                }
            }
            const float off = 0.75f;
            Run(new Vector3(-off, 0f, -off), new Vector3(map.WidthM, 0f, -off));
            Run(new Vector3(-off, 0f, map.DepthM + off), new Vector3(map.WidthM, 0f, map.DepthM + off));
            // The west side, with gaps where streets leave: a striped barrier closes each (the district beyond is not open yet).
            float z = -off;
            var red = MeshKit.Mat(new Color("c0392b"), 0.6f);
            var white = MeshKit.Mat(new Color("f6f0e4"), 0.6f);
            var wood = MeshKit.Mat(new Color("7a4a2c"), 0.7f);
            foreach (var street in map.Streets)
            {
                var r = street.Rect;
                if (r.W < r.H || r.X > 0) continue;
                float z0 = r.Y * Tile, z1 = (r.Y + r.H) * Tile;
                Run(new Vector3(-off, 0f, z), new Vector3(-off, 0f, z0 - 0.4f));
                for (int i = 0; i < 8; i++)
                {
                    float width = (z1 - z0) / 8f;
                    batch.Box(new Vector3(-0.7f, 0.95f, z0 + (i + 0.5f) * width), new Vector3(0.08f, 0.26f, width), i % 2 == 0 ? red : white, 0.02f);
                }
                for (int side = 0; side < 2; side++)
                {
                    float pz = side == 0 ? z0 + 0.3f : z1 - 0.3f;
                    batch.Box(new Vector3(-0.7f, 0.55f, pz), new Vector3(0.7f, 1.1f, 0.1f), wood, 0.03f);
                }
                z = z1 + 0.4f;
            }
            Run(new Vector3(-off, 0f, z), new Vector3(-off, 0f, map.DepthM + off));
        }

        private static void Dock(MeshBatch batch, WorldMap map, Vector2 harbourAt, float deck, float sea, bool tower)
        {
            var plank = MeshKit.Mat(new Color("6e4a35"), 0.8f);
            var plankDark = MeshKit.Mat(new Color("5c3d2c"), 0.8f);
            var pile = MeshKit.Mat(new Color("3f2a20"), 0.9f);
            float x0 = harbourAt.X - 16f, x1 = harbourAt.X + 16f, z0 = harbourAt.Y - 13f, z1 = harbourAt.Y + 11f;
            // The deck: long planks side by side, on piles standing in the water.
            int index = 0;
            for (float x = x0; x < x1; x += 1.0f, index++)
                batch.Box(new Vector3(x + 0.5f, deck - 0.1f, (z0 + z1) * 0.5f), new Vector3(0.96f, 0.2f, z1 - z0), index % 3 == 0 ? plankDark : plank, 0.03f);
            for (float x = x0 + 1f; x <= x1; x += 6f)
                for (float z = z0 + 1f; z <= z1; z += 5.5f)
                    batch.Add(MeshKit.Cylinder(0.22f, 0.22f, 5f, 10), pile, new Vector3(x, deck - 2.4f, z));
            // Two jetties out to the south, boats tied alongside.
            for (int j = 0; j < 2; j++)
            {
                float jx = x0 + 8f + j * 14f;
                batch.Box(new Vector3(jx, deck - 0.1f, z1 + 7f), new Vector3(2.4f, 0.2f, 14f), plank, 0.03f);
                for (float z = z1 + 2f; z <= z1 + 14f; z += 4f)
                    for (int side = -1; side <= 1; side += 2)
                        batch.Add(MeshKit.Cylinder(0.16f, 0.16f, 5.6f, 8), pile, new Vector3(jx + side * 1.1f, deck - 2.2f, z));
                Boat(batch, new Vector3(jx + 3.6f, sea, z1 + 6f + j * 3f), 0.1f + j * 0.2f);
                if (j == 0) Boat(batch, new Vector3(jx - 3.8f, sea, z1 + 10f), 3.3f);
            }

            // Crates of mail, stacked where the trucks unload.
            var crateColours = new[] { MeshKit.Mat(new Color("c89a62"), 0.8f), MeshKit.Mat(new Color("a8774a"), 0.8f), MeshKit.Mat(new Color("d8b27a"), 0.8f) };
            var band = MeshKit.Mat(new Color("7a5234"), 0.8f);
            for (int i = 0; i < 46; i++)
            {
                var seed = new Vector3(i * 1.37f, 0.5f, i * 2.11f);
                float x = Mathf.Lerp(x0 + 2f, x1 - 2f, MeshKit.Noise3(seed)), z = Mathf.Lerp(z0 + 2f, z1 - 2f, MeshKit.Noise3(seed + new Vector3(40f, 0f, 0f)));
                float size = 0.8f + 0.6f * MeshKit.Noise3(seed * 2.3f);
                int stack = 1 + (int)(MeshKit.Noise3(seed * 4.1f) * 2.4f);
                for (int level = 0; level < stack; level++)
                {
                    var at = new Vector3(x, deck + size * (level + 0.5f), z);
                    var turn = new Vector3(0f, MeshKit.Noise3(seed * 6.7f + new Vector3(level, 0f, 0f)) * 90f, 0f);
                    batch.Box(at, new Vector3(size, size, size), crateColours[(i + level) % crateColours.Length], 0.05f, turn);
                    batch.Box(at, new Vector3(size + 0.03f, size * 0.16f, size + 0.03f), band, 0.02f, turn);
                }
            }
            // Sheds, trucks, and the conveyor that carries parcels up to the sorting tower.
            Shed(batch, new Vector3(x0 + 5f, deck, z0 + 4f), 0.2f);
            Shed(batch, new Vector3(x1 - 6f, deck, z0 + 3.5f), -0.15f);
            Truck(batch, new Vector3(x1 - 5f, deck, z1 - 5f), 2.2f);
            Truck(batch, new Vector3(harbourAt.X + 1f, deck, z1 - 2.5f), 1.2f);
            if (!tower) return;
            Truck(batch, new Vector3(-14f, Height(map, -14f, 28f), 28.5f), -Mathf.Pi * 0.5f);

            var towerAt = new Vector3(-58f, 0f, -22f);
            towerAt.Y = Height(map, towerAt.X, towerAt.Z);
            Tower(batch, towerAt, 6.5f, true);
            Conveyor(batch, map, new Vector3(x0 + 9f, deck + 1.0f, z0 + 6f), new Vector3(-50f, towerAt.Y + 0.5f, -4f));
            Conveyor(batch, map, new Vector3(-50f, towerAt.Y + 0.5f, -4f), towerAt + new Vector3(0f, 6.5f, 1.5f));
        }

        private static void Boat(MeshBatch batch, Vector3 at, float yaw)
        {
            var saved = batch.Frame;
            batch.Frame = saved * new Transform3D(new Basis(Vector3.Up, yaw), at);
            batch.Add(MeshKit.Blob(1f, 0.02f, 1f, 3, new Vector3(1.25f, 0.8f, 3.4f), 8), MeshKit.Mat(new Color("b8312c"), 0.5f), new Vector3(0f, 0.15f, 0f));
            batch.Box(new Vector3(0f, 0.7f, 0f), new Vector3(2.0f, 0.2f, 5.4f), MeshKit.Mat(new Color("efe6d6"), 0.6f), 0.08f);
            batch.Box(new Vector3(0f, 1.35f, -0.6f), new Vector3(1.5f, 1.2f, 1.8f), MeshKit.Mat(new Color("f6f0e4"), 0.6f), 0.1f);
            batch.Box(new Vector3(0f, 1.5f, -0.6f), new Vector3(1.54f, 0.45f, 1.5f), MeshKit.Mat(new Color("31506e"), 0.12f, 0.35f), 0.05f);
            batch.Box(new Vector3(0f, 2.02f, -0.6f), new Vector3(1.8f, 0.14f, 2.1f), MeshKit.Mat(new Color("b8312c"), 0.5f), 0.05f);
            batch.Add(MeshKit.Cylinder(0.05f, 0.04f, 1.6f, 8), MeshKit.Mat(new Color("f6f0e4"), 0.6f), new Vector3(0f, 2.8f, -0.9f));
            batch.Frame = saved;
        }

        /// <summary>A mail truck: red cab, white box, as on the dock in the video.</summary>
        public static void Truck(MeshBatch batch, Vector3 at, float yaw)
        {
            var saved = batch.Frame;
            batch.Frame = saved * new Transform3D(new Basis(Vector3.Up, yaw), at);
            var red = MeshKit.Mat(new Color("c5342c"), 0.4f, 0.1f);
            var white = MeshKit.Mat(new Color("f4eee2"), 0.5f);
            var dark = MeshKit.Mat(new Color("1f1d24"), 0.7f);
            batch.Box(new Vector3(0f, 1.55f, -0.75f), new Vector3(2.1f, 2.0f, 3.3f), white, 0.12f);
            batch.Box(new Vector3(0f, 1.0f, -0.75f), new Vector3(2.13f, 0.3f, 3.33f), red, 0.06f);
            batch.Box(new Vector3(0f, 1.25f, 1.65f), new Vector3(2.0f, 1.5f, 1.5f), red, 0.22f);
            batch.Box(new Vector3(0f, 1.55f, 2.2f), new Vector3(1.7f, 0.6f, 0.5f), MeshKit.Mat(new Color("31506e"), 0.12f, 0.35f), 0.08f);
            batch.Box(new Vector3(0f, 0.55f, 0.3f), new Vector3(1.9f, 0.3f, 5.2f), dark, 0.08f);
            for (int side = -1; side <= 1; side += 2)
            {
                foreach (float z in new[] { -1.6f, 1.6f })
                    batch.Add(MeshKit.Cylinder(0.42f, 0.42f, 0.34f, 16), dark, new Vector3(side * 0.95f, 0.42f, z), new Vector3(0f, 0f, 90f));
                batch.Add(MeshKit.Sphere(0.11f, 10), MeshKit.Glow(new Color("ffe9b0"), 1.5f), new Vector3(side * 0.7f, 0.95f, 2.4f));
            }
            batch.Frame = saved;
        }

        private static void Shed(MeshBatch batch, Vector3 at, float yaw)
        {
            var saved = batch.Frame;
            var frame = saved * new Transform3D(new Basis(Vector3.Up, yaw), at);
            batch.Frame = frame;
            var red = Shaders.Clapboard(new Color("b23a34"));
            var trim = MeshKit.Mat(new Color("f6f0e4"), 0.6f);
            batch.Add(MeshKit.Box(new Vector3(5f, 2.6f, 3.4f)), red, new Vector3(0f, 1.3f, 0f));
            batch.Box(new Vector3(0f, 2.75f, 0f), new Vector3(5.5f, 0.3f, 3.9f), MeshKit.Mat(new Color("2d2a3c"), 0.7f), 0.1f);
            batch.Box(new Vector3(0f, 1.1f, 1.72f), new Vector3(1.8f, 2.1f, 0.1f), trim, 0.03f);
            batch.Box(new Vector3(0f, 1.05f, 1.75f), new Vector3(1.5f, 1.9f, 0.1f), MeshKit.Mat(new Color("5a2e22"), 0.6f), 0.03f);
            HouseBuilder.Window(batch, frame, new Vector3(1.8f, 1.5f, 1.7f), 0f, 0.8f, 0.8f, true);
            batch.Frame = saved;
        }

        /// <summary>A wooden tower on legs under a pointed roof: the sorting tower on the hill, the watch towers at the gate.</summary>
        private static void Tower(MeshBatch batch, Vector3 at, float platform, bool cabin)
        {
            var saved = batch.Frame;
            batch.Frame = saved * new Transform3D(Basis.Identity, at);
            var wood = MeshKit.Mat(new Color("4f3526"), 0.85f);
            var dark = MeshKit.Mat(new Color("3a271d"), 0.85f);
            const float half = 1.5f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    batch.Add(MeshKit.Cylinder(0.2f, 0.16f, platform + 2.6f, 10), wood, new Vector3(sx * half, (platform + 2.6f) * 0.5f, sz * half));
                    // Cross braces between the legs.
                    batch.Box(new Vector3(sx * half, platform * 0.5f, 0f), new Vector3(0.1f, 0.12f, half * 2.6f), dark, 0.03f, new Vector3(sz * 48f, 0f, 0f));
                }
            }
            batch.Box(new Vector3(0f, platform, 0f), new Vector3(half * 2f + 0.9f, 0.22f, half * 2f + 0.9f), wood, 0.05f);
            for (int side = 0; side < 4; side++)
            {
                var turn = new Basis(Vector3.Up, side * Mathf.Pi * 0.5f);
                batch.Add(MeshKit.RoundedBox(new Vector3(half * 2f + 0.8f, cabin ? 2.2f : 0.14f, 0.12f), 0.03f), cabin ? MeshKit.Mat(new Color("7c3a30"), 0.8f) : dark,
                    new Transform3D(turn, turn * new Vector3(0f, platform + (cabin ? 1.2f : 1.0f), half + 0.35f)));
            }
            batch.Add(MeshKit.Cone(half * 2f + 0.5f, 2.1f, 4), MeshKit.Mat(new Color("2f2330"), 0.8f), new Vector3(0f, platform + 3.6f, 0f), new Vector3(0f, 45f, 0f));
            batch.Frame = saved;
        }

        /// <summary>A belt on trestles from one point up to another, parcels riding it.</summary>
        private static void Conveyor(MeshBatch batch, WorldMap map, Vector3 from, Vector3 to)
        {
            var along = to - from;
            float length = along.Length();
            var dir = along / length;
            float yaw = Mathf.Atan2(-dir.Z, dir.X), pitch = Mathf.Asin(dir.Y);
            var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Back, pitch);
            var mid = (from + to) * 0.5f;
            batch.Add(MeshKit.RoundedBox(new Vector3(length, 0.14f, 1.1f), 0.04f), MeshKit.Mat(new Color("2b2a33"), 0.6f), new Transform3D(basis, mid));
            for (int side = -1; side <= 1; side += 2)
                batch.Add(MeshKit.RoundedBox(new Vector3(length, 0.3f, 0.1f), 0.03f), MeshKit.Mat(new Color("b23a34"), 0.5f), new Transform3D(basis, mid + basis * new Vector3(0f, 0.08f, side * 0.6f)));
            var wood = MeshKit.Mat(new Color("4f3526"), 0.85f);
            var parcel = MeshKit.Mat(new Color("c89a62"), 0.8f);
            for (float t = 3f; t < length - 1f; t += 6f)
            {
                var p = from + dir * t;
                float ground = Mathf.Max(Height(map, p.X, p.Z), SeaLevelOf(map) - 1.5f);
                float leg = p.Y - ground;
                if (leg > 0.3f)
                    for (int side = -1; side <= 1; side += 2)
                        batch.Add(MeshKit.Cylinder(0.12f, 0.12f, 1f, 8), wood, new Transform3D(Basis.Identity.Scaled(new Vector3(1f, leg, 1f)), p + new Basis(Vector3.Up, yaw) * new Vector3(0f, -leg * 0.5f, side * 0.5f)));
                batch.Add(MeshKit.RoundedBox(new Vector3(0.6f, 0.45f, 0.6f), 0.05f), parcel, new Transform3D(basis, p + dir * 2.2f + basis * new Vector3(0f, 0.3f, 0f)));
            }
        }

        /// <summary>The spiked log wall across the east end of town, open at each street, a watch tower either side of the gate.</summary>
        private static List<Vector3> Palisade(MeshBatch batch, WorldMap map)
        {
            var torches = new List<Vector3>();
            var log = MeshKit.Mat(new Color("553726"), 0.9f);
            var logDark = MeshKit.Mat(new Color("452c1f"), 0.9f);
            var gates = new List<(float Z0, float Z1)>();
            foreach (var street in map.Streets)
            {
                var r = street.Rect;
                if (r.W >= r.H && r.X + r.W >= map.Cols) gates.Add((r.Y * Tile - 0.5f, (r.Y + r.H) * Tile + 0.5f));
            }
            float wallX = map.WidthM + 0.75f;
            int index = 0;
            for (float z = -1.5f; z <= map.DepthM + 1.5f; z += 0.47f, index++)
            {
                bool open = false;
                foreach (var gate in gates) open |= z > gate.Z0 && z < gate.Z1;
                if (open) continue;
                float height = 3.3f + 0.5f * MeshKit.Noise3(new Vector3(z * 1.9f, 0f, 4f));
                batch.Add(MeshKit.Cylinder(0.25f, 0.23f, 1f, 9), index % 2 == 0 ? log : logDark, new Transform3D(Basis.Identity.Scaled(new Vector3(1f, height, 1f)), new Vector3(wallX, height * 0.5f, z)));
                batch.Add(MeshKit.Cone(0.23f, 0.6f, 9), index % 2 == 0 ? log : logDark, new Vector3(wallX, height + 0.3f, z));
            }
            foreach (float y in new[] { 1.1f, 2.5f })
            {
                float from = -1.5f;
                foreach (var gate in gates)
                {
                    batch.Box(new Vector3(wallX - 0.28f, y, (from + gate.Z0) * 0.5f), new Vector3(0.16f, 0.22f, gate.Z0 - from), logDark, 0.04f);
                    from = gate.Z1;
                }
                batch.Box(new Vector3(wallX - 0.28f, y, (from + map.DepthM + 1.5f) * 0.5f), new Vector3(0.16f, 0.22f, map.DepthM + 1.5f - from), logDark, 0.04f);
            }
            var flame = MeshKit.Glow(new Color(1.0f, 0.55f, 0.15f), 5f);
            foreach (var gate in gates)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float edge = side < 0 ? gate.Z0 : gate.Z1;
                    // Gate posts, the leaves swung open outwards, a tower behind the wall and a torch on it.
                    batch.Add(MeshKit.Cylinder(0.34f, 0.3f, 4.6f, 10), logDark, new Vector3(wallX, 2.3f, edge));
                    batch.Box(new Vector3(wallX + 1.2f, 1.7f, edge + side * 0.5f), new Vector3(2.5f, 3.2f, 0.18f), log, 0.04f, new Vector3(0f, side * 20f, 0f));
                    Tower(batch, new Vector3(wallX + 2.4f, 0f, edge + side * 3.2f), 4.3f, false);
                    var torch = new Vector3(wallX - 0.45f, 3.0f, edge + side * 1.2f);
                    batch.Add(MeshKit.Cylinder(0.05f, 0.04f, 0.7f, 6), logDark, torch + new Vector3(0f, -0.45f, 0f), new Vector3(0f, 0f, 20f));
                    batch.Add(MeshKit.Blob(0.16f, 0.3f, 2f, 9, new Vector3(1f, 1.5f, 1f), 5), flame, torch);
                    torches.Add(torch);
                }
                batch.Box(new Vector3(wallX, 4.45f, (gate.Z0 + gate.Z1) * 0.5f), new Vector3(0.4f, 0.4f, gate.Z1 - gate.Z0 + 0.8f), logDark, 0.08f);
            }
            return torches;
        }
    }
}
