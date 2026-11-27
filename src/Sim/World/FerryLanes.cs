using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// Doc 02 §3.8: "If a path is impossible (water), a ferry lane is marked between the two nearest beaches." The map's
    /// ground is parted into what can be walked between (four ways over every tile that is neither a building nor
    /// water); while a destination lies on ground the Post Office's cannot be walked to, the nearest two shore tiles of
    /// the ground reached so far and of that ground, with nothing but water between them, are joined by a lane. Ties go
    /// to a lane from street end to street end (the main streets run down to the water), then to the one first found.
    /// </summary>
    public static class FerryLanes
    {
        /// <summary>The lanes, none if every destination can be walked to; null if some cannot be reached even by water.</summary>
        public static List<FerryLane>? Mark(int cols, int rows, TileType[] tiles, float padX, float padZ, IReadOnlyList<DestinationDef> destinations)
        {
            const float tile = SimConstants.TileSize;
            var ground = Grounds(cols, rows, tiles, out int count);
            int At(float x, float z)
            {
                int tx = (int)MathF.Floor(x / tile), tz = (int)MathF.Floor(z / tile);
                return tx < 0 || tz < 0 || tx >= cols || tz >= rows ? -1 : ground[tz * cols + tx];
            }
            int home = At(padX, padZ);
            if (home < 0) return null;
            var wanted = new bool[count];
            foreach (var d in destinations)
            {
                int g = At(d.MailboxX, d.MailboxZ);
                if (g < 0) return null;
                wanted[g] = true;
            }
            var reached = new bool[count];
            reached[home] = true;

            // The shore: ground with water beside it.
            var shore = new List<int>();
            for (int i = 0; i < tiles.Length; i++)
            {
                if (ground[i] < 0) continue;
                int x = i % cols, z = i / cols;
                if (Water(tiles, cols, rows, x + 1, z) || Water(tiles, cols, rows, x - 1, z) || Water(tiles, cols, rows, x, z + 1) || Water(tiles, cols, rows, x, z - 1))
                    shore.Add(i);
            }

            var lanes = new List<FerryLane>();
            while (true)
            {
                bool done = true;
                for (int g = 0; g < count; g++) if (wanted[g] && !reached[g]) done = false;
                if (done) return lanes;
                int bestFrom = -1, bestTo = -1, bestD = int.MaxValue;
                bool bestStreets = false;
                foreach (int s in shore)
                {
                    if (!reached[ground[s]]) continue;
                    int sx = s % cols, sz = s / cols;
                    foreach (int t in shore)
                    {
                        int g = ground[t];
                        if (reached[g] || !wanted[g]) continue;
                        int tx = t % cols, tz = t / cols;
                        int d = (tx - sx) * (tx - sx) + (tz - sz) * (tz - sz);
                        bool streets = tiles[s] == TileType.Street && tiles[t] == TileType.Street;
                        if (d > bestD || (d == bestD && (bestStreets || !streets))) continue;
                        if (!WaterBetween(tiles, cols, rows, sx, sz, tx, tz)) continue;
                        bestFrom = s; bestTo = t; bestD = d; bestStreets = streets;
                    }
                }
                if (bestTo < 0) return null;
                lanes.Add(new FerryLane((bestFrom % cols + 0.5f) * tile, (bestFrom / cols + 0.5f) * tile, (bestTo % cols + 0.5f) * tile, (bestTo / cols + 0.5f) * tile));
                reached[ground[bestTo]] = true;
            }
        }

        /// <summary>The ground each tile is on (−1 for a building or water), four ways.</summary>
        public static int[] Grounds(int cols, int rows, TileType[] tiles, out int count)
        {
            var ground = new int[cols * rows];
            for (int i = 0; i < ground.Length; i++) ground[i] = -1;
            count = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < ground.Length; i++)
            {
                if (ground[i] >= 0 || !Walkable(tiles[i])) continue;
                int id = count++;
                ground[i] = id;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    int x = cur % cols, z = cur / cols;
                    Visit(x + 1, z); Visit(x - 1, z); Visit(x, z + 1); Visit(x, z - 1);
                    void Visit(int nx, int nz)
                    {
                        if (nx < 0 || nz < 0 || nx >= cols || nz >= rows) return;
                        int n = nz * cols + nx;
                        if (ground[n] >= 0 || !Walkable(tiles[n])) return;
                        ground[n] = id;
                        stack.Push(n);
                    }
                }
            }
            return ground;
        }

        private static bool Walkable(TileType t) => t != TileType.Building && t != TileType.Water && t != TileType.DeepWater;

        private static bool Water(TileType[] tiles, int cols, int rows, int x, int z) =>
            x >= 0 && z >= 0 && x < cols && z < rows && tiles[z * cols + x] is TileType.Water or TileType.DeepWater;

        /// <summary>True if the straight line between the two tiles' centres crosses only water past the tiles themselves.</summary>
        private static bool WaterBetween(TileType[] tiles, int cols, int rows, int ax, int az, int bx, int bz)
        {
            float dx = bx - ax, dz = bz - az;
            int steps = (int)MathF.Ceiling(MathF.Sqrt(dx * dx + dz * dz) * 4f);
            for (int k = 1; k < steps; k++)
            {
                float f = k / (float)steps;
                int x = (int)MathF.Floor(ax + 0.5f + dx * f), z = (int)MathF.Floor(az + 0.5f + dz * f);
                if ((x == ax && z == az) || (x == bx && z == bz)) continue;
                if (!Water(tiles, cols, rows, x, z)) return false;
            }
            return true;
        }
    }
}
