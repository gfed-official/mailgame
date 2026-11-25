using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// The coarse water navmesh of doc 04 §3.3 ("a coarse water navmesh generated per seed"; design
    /// choice 115): every tile a boat may be on (<see cref="PlayerMovement.BoatGoes"/>: water with
    /// nothing built over it but a port's bay) is a cell, joined to its eight neighbours (a corner
    /// only past two open sides). A way is found from cell to cell (A*) and then pulled tight: from
    /// each point on to the farthest one a boat's width can sail straight to. It is read off the map
    /// and what stands on it when asked, so a pier built across a lane is sailed round at once.
    /// The same on the server and every client, so a console's estimate is the captain's way.
    /// </summary>
    public static class WaterNav
    {
        private const float Tile = SimConstants.TileSize;

        /// <summary>True for a cell of the navmesh: a tile a boat may lie on.</summary>
        public static bool Open(WorldMap map, ConstructField? constructs, int tx, int tz) =>
            map.InBounds(tx, tz) && PlayerMovement.BoatGoes(map, constructs, tx, tz);

        /// <summary>
        /// Where a boat lies to serve a destination from the water: the middle of the open cell
        /// nearest its mailbox (a Business Dock's zone at the quay's edge) within <paramref name="reach"/>;
        /// false for one inland.
        /// </summary>
        public static bool Berth(WorldMap map, ConstructField? constructs, DestinationDef dest, float reach, out float x, out float z)
        {
            int ox = (int)MathF.Floor(dest.MailboxX / Tile), oz = (int)MathF.Floor(dest.MailboxZ / Tile);
            int span = (int)MathF.Ceiling(reach / Tile) + 1;
            float best = reach * reach;
            bool found = false;
            x = z = 0f;
            for (int tz = oz - span; tz <= oz + span; tz++)
                for (int tx = ox - span; tx <= ox + span; tx++)
                {
                    if (!Open(map, constructs, tx, tz)) continue;
                    float cx = (tx + 0.5f) * Tile, cz = (tz + 0.5f) * Tile;
                    float d = (cx - dest.MailboxX) * (cx - dest.MailboxX) + (cz - dest.MailboxZ) * (cz - dest.MailboxZ);
                    if (d >= best) continue;
                    best = d; x = cx; z = cz; found = true;
                }
            return found;
        }

        /// <summary>
        /// The way over the water from one point to another: the points to pass into
        /// <paramref name="points"/>, the start first and the goal last; returns its length in metres,
        /// or a negative number when no way joins them (either end not on open water, or cut off).
        /// </summary>
        public static float Route(WorldMap map, ConstructField? constructs, float fromX, float fromZ, float toX, float toZ, float radius,
            List<(float X, float Z)> points)
        {
            points.Clear();
            int sx = (int)MathF.Floor(fromX / Tile), sz = (int)MathF.Floor(fromZ / Tile);
            int gx = (int)MathF.Floor(toX / Tile), gz = (int)MathF.Floor(toZ / Tile);
            if (!Open(map, constructs, sx, sz) || !Open(map, constructs, gx, gz)) return -1f;
            int cols = map.Cols, n = cols * map.Rows;
            int start = sz * cols + sx, goal = gz * cols + gx;

            var cost = new float[n];
            var via = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { cost[i] = float.MaxValue; via[i] = -1; }
            cost[start] = 0f;
            var open = new Heap();
            open.Push(start, Heuristic(sx, sz, gx, gz));
            while (open.Count > 0)
            {
                int at = open.Pop();
                if (closed[at]) continue;
                closed[at] = true;
                if (at == goal) break;
                int ax = at % cols, az = at / cols;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = ax + dx, nz = az + dz;
                        if (!Open(map, constructs, nx, nz)) continue;
                        // A corner is cut only between two open sides: a hull does not scrape a pier's end.
                        if (dx != 0 && dz != 0 && (!Open(map, constructs, ax + dx, az) || !Open(map, constructs, ax, az + dz))) continue;
                        int next = nz * cols + nx;
                        if (closed[next]) continue;
                        float c = cost[at] + (dx != 0 && dz != 0 ? 1.4142135f : 1f) * Tile;
                        if (c >= cost[next]) continue;
                        cost[next] = c;
                        via[next] = at;
                        open.Push(next, c + Heuristic(nx, nz, gx, gz));
                    }
            }
            if (!closed[goal]) return -1f;

            // The cells from the start to the goal, their middles, with the true ends.
            var cells = new List<(float X, float Z)>();
            for (int at = goal; at != -1; at = via[at]) cells.Add(((at % cols + 0.5f) * Tile, (at / cols + 0.5f) * Tile));
            cells.Reverse();
            cells[0] = (fromX, fromZ);
            if (cells.Count == 1) cells.Add((toX, toZ));
            else cells[cells.Count - 1] = (toX, toZ);

            // Pulled tight: from each kept point straight on to the farthest one in clear water.
            points.Add(cells[0]);
            int from = 0;
            while (from < cells.Count - 1)
            {
                int far = from + 1;
                for (int j = cells.Count - 1; j > from + 1; j--)
                    if (Clear(map, constructs, cells[from].X, cells[from].Z, cells[j].X, cells[j].Z, radius)) { far = j; break; }
                points.Add(cells[far]);
                from = far;
            }
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
                length += MathF.Sqrt((points[i].X - points[i - 1].X) * (points[i].X - points[i - 1].X) + (points[i].Z - points[i - 1].Z) * (points[i].Z - points[i - 1].Z));
            return length;
        }

        /// <summary>A binary min-heap of cells by priority (netstandard2.1 has no PriorityQueue).</summary>
        private sealed class Heap
        {
            private readonly List<(int Item, float Key)> _items = new List<(int, float)>();
            public int Count => _items.Count;

            public void Push(int item, float key)
            {
                _items.Add((item, key));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int up = (i - 1) / 2;
                    if (_items[up].Key <= key) break;
                    _items[i] = _items[up];
                    i = up;
                }
                _items[i] = (item, key);
            }

            public int Pop()
            {
                int top = _items[0].Item;
                var last = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                if (_items.Count == 0) return top;
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, small = i;
                    float key = last.Key;
                    if (l < _items.Count && _items[l].Key < key) { small = l; key = _items[l].Key; }
                    if (r < _items.Count && _items[r].Key < key) small = r;
                    if (small == i) break;
                    _items[i] = _items[small];
                    i = small;
                }
                _items[i] = last;
                return top;
            }
        }

        private static float Heuristic(int ax, int az, int bx, int bz)
        {
            int dx = Math.Abs(ax - bx), dz = Math.Abs(az - bz);
            return (Math.Max(dx, dz) + 0.41421356f * Math.Min(dx, dz)) * Tile;
        }

        /// <summary>True if a hull of <paramref name="radius"/> sails straight from one point to the other over open cells only.</summary>
        public static bool Clear(WorldMap map, ConstructField? constructs, float ax, float az, float bx, float bz, float radius)
        {
            float dx = bx - ax, dz = bz - az, length = MathF.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / 0.25f));
            for (int i = 0; i <= steps; i++)
            {
                float x = ax + dx * i / steps, z = az + dz * i / steps;
                int x0 = (int)MathF.Floor((x - radius) / Tile), x1 = (int)MathF.Floor((x + radius) / Tile);
                int z0 = (int)MathF.Floor((z - radius) / Tile), z1 = (int)MathF.Floor((z + radius) / Tile);
                for (int tz = z0; tz <= z1; tz++)
                    for (int tx = x0; tx <= x1; tx++)
                        if (!Open(map, constructs, tx, tz)) return false;
            }
            return true;
        }
    }
}
