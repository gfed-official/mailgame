using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// Breadth-first tile path for bots and enemies. 8-way, never cutting a corner; waypoints are
    /// tile centres. It keeps to tiles a walker can cross without touching a house or a solid prop
    /// (<see cref="WorldMap.IsNavBlocked"/>); only the tiles it starts and ends on may be such.
    /// </summary>
    public static class TilePathfinder
    {
        /// <summary>Working memory a frequent caller keeps, so that a search allocates nothing.</summary>
        public sealed class Scratch
        {
            internal int[] CameFrom = Array.Empty<int>();
            internal readonly Queue<int> Queue = new Queue<int>();
        }

        private static readonly (int Dx, int Dz)[] Steps =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
        };

        /// <summary>Fills <paramref name="path"/> with world-space waypoints from start to goal. False if unreachable.</summary>
        public static bool Find(WorldMap map, float fromX, float fromZ, float toX, float toZ, List<(float X, float Z)> path, Scratch? scratch = null)
        {
            path.Clear();
            const float tile = SimConstants.TileSize;
            int sx = (int)MathF.Floor(fromX / tile), sz = (int)MathF.Floor(fromZ / tile);
            int gx = (int)MathF.Floor(toX / tile), gz = (int)MathF.Floor(toZ / tile);
            if (map.IsSolid(sx, sz) || map.IsSolid(gx, gz)) return false;

            scratch ??= new Scratch();
            int cols = map.Cols;
            if (scratch.CameFrom.Length < map.Cols * map.Rows) scratch.CameFrom = new int[map.Cols * map.Rows];
            var cameFrom = scratch.CameFrom;
            for (int i = 0; i < cameFrom.Length; i++) cameFrom[i] = -1;
            int start = sz * cols + sx, goal = gz * cols + gx;
            var queue = scratch.Queue;
            queue.Clear();
            queue.Enqueue(start);
            cameFrom[start] = start;
            while (queue.Count > 0 && cameFrom[goal] < 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % cols, cz = cur / cols;
                foreach (var (dx, dz) in Steps)
                {
                    int nx = cx + dx, nz = cz + dz;
                    int next = nz * cols + nx;
                    if (next == goal ? map.IsSolid(nx, nz) : map.IsNavBlocked(nx, nz)) continue;
                    if (dx != 0 && dz != 0 && (map.IsNavBlocked(cx + dx, cz) || map.IsNavBlocked(cx, cz + dz))) continue;
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
