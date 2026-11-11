using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Bot
{
    /// <summary>Breadth-first tile path for bots. 8-way, never cutting a solid corner; waypoints are tile centres.</summary>
    public static class TilePathfinder
    {
        private static readonly (int Dx, int Dz)[] Steps =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
        };

        /// <summary>Fills <paramref name="path"/> with world-space waypoints from start to goal. False if unreachable.</summary>
        public static bool Find(WorldMap map, float fromX, float fromZ, float toX, float toZ, List<(float X, float Z)> path)
        {
            path.Clear();
            const float tile = SimConstants.TileSize;
            int sx = (int)MathF.Floor(fromX / tile), sz = (int)MathF.Floor(fromZ / tile);
            int gx = (int)MathF.Floor(toX / tile), gz = (int)MathF.Floor(toZ / tile);
            if (map.IsSolid(sx, sz) || map.IsSolid(gx, gz)) return false;

            int cols = map.Cols;
            var cameFrom = new int[map.Cols * map.Rows];
            for (int i = 0; i < cameFrom.Length; i++) cameFrom[i] = -1;
            int start = sz * cols + sx, goal = gz * cols + gx;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            cameFrom[start] = start;
            while (queue.Count > 0 && cameFrom[goal] < 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % cols, cz = cur / cols;
                foreach (var (dx, dz) in Steps)
                {
                    int nx = cx + dx, nz = cz + dz;
                    if (map.IsSolid(nx, nz)) continue;
                    if (dx != 0 && dz != 0 && (map.IsSolid(cx + dx, cz) || map.IsSolid(cx, cz + dz))) continue;
                    int next = nz * cols + nx;
                    if (cameFrom[next] >= 0) continue;
                    cameFrom[next] = cur;
                    queue.Enqueue(next);
                }
            }
            if (cameFrom[goal] < 0) return false;

            for (int cur = goal; cur != start; cur = cameFrom[cur])
                path.Add(((cur % cols + 0.5f) * tile, (cur / cols + 0.5f) * tile));
            path.Reverse();
            // Finish on the exact goal point rather than the centre of its tile.
            if (path.Count > 0) path[path.Count - 1] = (toX, toZ);
            else path.Add((toX, toZ));
            return true;
        }
    }
}
