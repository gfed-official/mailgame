using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// A way the raiders come into the map (doc 02 §3.10): a mouth on the map's edge, open from the
    /// shift of the district it faces, with its way to the Post Office over the routing graph.
    /// </summary>
    public sealed class SpawnEdge
    {
        public int Index { get; init; }
        /// <summary>Where they come in and leave: a point just inside the map's edge.</summary>
        public float X { get; init; }
        public float Z { get; init; }
        /// <summary>The way into the map at the mouth (a unit vector).</summary>
        public float InX { get; init; }
        public float InZ { get; init; }
        /// <summary>How far to either side of the mouth a pulse spreads out.</summary>
        public float HalfWidth { get; init; }
        /// <summary>The first shift raiders may come by it: the unlock shift of the district it faces.</summary>
        public int UnlockShift { get; init; } = 1;
        /// <summary>The compass point of the mouth seen from the Post Office: "east", "north-west".</summary>
        public string Direction { get; init; } = "";
        /// <summary>The street that leaves the map at the mouth; empty where they come over open ground.</summary>
        public string Street { get; init; } = "";
        /// <summary>
        /// The way from the mouth to the spawn pad over the routing graph (doc 02 §3.10 "a
        /// precomputed path to the PO"); its last leg is the walk off the street to the pad.
        /// </summary>
        public IReadOnlyList<(float X, float Z)> Path { get; init; } = Array.Empty<(float X, float Z)>();

        public bool OpenAt(int shift) => UnlockShift <= shift;

        /// <summary>"the east, along Larch Lane" / "the north-west".</summary>
        public string Name => Street.Length > 0 ? $"the {Direction}, along {Street}" : $"the {Direction}";

        /// <summary>
        /// The raiders' line of this way in (nit 17): the first point of the path within
        /// <paramref name="standoff"/> of the pad, but at least <paramref name="minFromMouth"/> along
        /// it from the mouth; the path's last leg to the pad does not count, so the line stays on
        /// the street. <paramref name="dirX"/>, <paramref name="dirZ"/>: the path's way there, towards
        /// the Post Office.
        /// </summary>
        public void LinePoint(float padX, float padZ, float standoff, float minFromMouth, out float x, out float z, out float dirX, out float dirZ)
        {
            x = X; z = Z; dirX = InX; dirZ = InZ;
            int last = Math.Max(1, Path.Count - 2);
            float walked = 0f;
            for (int i = 0; i < last && i + 1 < Path.Count; i++)
            {
                var (ax, az) = Path[i];
                var (bx, bz) = Path[i + 1];
                float dx = bx - ax, dz = bz - az, len = MathF.Sqrt(dx * dx + dz * dz);
                if (len < 1e-4f) continue;
                float ux = dx / len, uz = dz / len;
                dirX = ux; dirZ = uz;
                // Where along this leg the circle of the standoff is first crossed: |a + u t − pad| = standoff.
                float fx = ax - padX, fz = az - padZ;
                float b = fx * ux + fz * uz, c = fx * fx + fz * fz - standoff * standoff;
                float disc = b * b - c;
                float t = float.MaxValue;
                if (c <= 0f) t = 0f;
                else if (disc >= 0f)
                {
                    float t0 = -b - MathF.Sqrt(disc);
                    if (t0 >= 0f && t0 <= len) t = t0;
                }
                if (t <= len)
                {
                    t = MathF.Max(t, minFromMouth - walked);
                    if (t <= len)
                    {
                        x = ax + ux * t; z = az + uz * t;
                        return;
                    }
                }
                // The leg's point nearest the pad, in case the circle is never reached.
                float near = Math.Clamp(-b, MathF.Max(0f, minFromMouth - walked), len);
                x = ax + ux * near; z = az + uz * near;
                walked += len;
            }
        }
    }

    /// <summary>
    /// The raiders' ways in of a map (doc 02 §3.10). Every map has the east end of its first
    /// street, open from shift 1 ("Raid incoming from the east", doc 05 §3.1). Then every district
    /// that opens later adds the way in facing it: on the side of the map it lies towards from the
    /// Post Office, where a street leaves the map there, else over open ground — unless a way in
    /// already lies near, which it then shares. Built from the map alone, on the server and the
    /// clients alike.
    /// </summary>
    public static class SpawnEdges
    {
        /// <summary>No mouth nearer the spawn pad than this (doc 02 asks 120 m, more than a town grid's half; design choice 92).</summary>
        public const float MinPadDistance = 40f;
        /// <summary>A district's way in closer than this to one there is already is that one.</summary>
        public const float ShareDistance = 30f;
        /// <summary>A street that leaves the map this near the point a district faces is its way in.</summary>
        public const float StreetSnap = 30f;
        /// <summary>The mouth of a street stands this far inside the map's edge.</summary>
        public const float MouthInset = 1.5f;
        /// <summary>How wide a pulse spreads over a mouth on open ground, either side.</summary>
        public const float OpenHalfWidth = 2f;

        private enum Side { North, East, West, South }

        public static List<SpawnEdge> Build(WorldMap map)
        {
            var edges = new List<SpawnEdge>();
            float padX = map.PostOffice.SpawnPadX, padZ = map.PostOffice.SpawnPadZ;
            float half = map.Streets.Count > 0 ? map.Streets[0].Rect.H * SimConstants.TileSize * 0.5f - 0.6f : 1.4f;
            float gz = map.Streets.Count > 0 ? map.Streets[0].Rect.CenterZ : map.DepthM * 0.5f;
            edges.Add(Make(map, 0, map.WidthM - MouthInset, gz, -1f, 0f, half, 1, map.Streets.Count > 0 ? StreetName(map, map.WidthM, gz) : ""));

            var districts = new List<DistrictDef>(map.Districts);
            districts.Sort((a, b) => a.UnlockShift != b.UnlockShift ? a.UnlockShift.CompareTo(b.UnlockShift) : a.Id.CompareTo(b.Id));
            var scratch = new TilePathfinder.Scratch();
            var path = new List<(float X, float Z)>();
            foreach (var d in districts)
            {
                if (d.UnlockShift <= 1) continue;
                float cx = 0f, cz = 0f;
                int n = 0;
                foreach (var dest in map.Destinations)
                {
                    if (dest.Street.District != d.Id) continue;
                    cx += dest.MailboxX; cz += dest.MailboxZ; n++;
                }
                if (n == 0) continue;
                cx /= n; cz /= n;
                if (!Mouth(map, cx, cz, padX, padZ, scratch, path, out float mx, out float mz, out float inX, out float inZ, out bool street)) continue;
                bool shared = false;
                foreach (var e in edges)
                    if (Dist(e.X, e.Z, mx, mz) < ShareDistance) { shared = true; break; }
                if (shared) continue;
                edges.Add(Make(map, edges.Count, mx, mz, inX, inZ, street ? half : OpenHalfWidth, d.UnlockShift,
                    street ? StreetName(map, mx - inX * MouthInset, mz - inZ * MouthInset) : ""));
            }
            return edges;
        }

        /// <summary>
        /// The mouth facing a district: on the side of the map (not the beach's) its centroid lies
        /// towards from the pad, at a street that leaves the map near there, else at the nearest
        /// open tile of that edge from which the pad can be walked to.
        /// </summary>
        private static bool Mouth(WorldMap map, float cx, float cz, float padX, float padZ, TilePathfinder.Scratch scratch,
            List<(float X, float Z)> path, out float x, out float z, out float inX, out float inZ, out bool street)
        {
            x = z = inX = inZ = 0f;
            street = false;
            float dx = cx - padX, dz = cz - padZ, len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 1e-3f) return false;
            dx /= len; dz /= len;
            // The sides there is land behind, best facing first.
            var sides = new List<(Side Side, float Facing)>();
            foreach (Side s in new[] { Side.North, Side.East, Side.West, Side.South })
            {
                if (!SideHasLand(map, s)) continue;
                Normal(s, out float nx, out float nz);
                sides.Add((s, dx * nx + dz * nz));
            }
            sides.Sort((a, b) => b.Facing.CompareTo(a.Facing));
            foreach (var (side, _) in sides)
            {
                Normal(side, out float nx, out float nz);
                inX = -nx; inZ = -nz;
                // The point of the side the district lies towards.
                float px = side == Side.East ? map.WidthM : side == Side.West ? 0f : Math.Clamp(cx, 0f, map.WidthM);
                float pz = side == Side.North ? 0f : side == Side.South ? map.DepthM : Math.Clamp(cz, 0f, map.DepthM);
                // A street leaving the map there.
                float best = StreetSnap;
                foreach (var node in map.Routes.Nodes)
                {
                    if (node.Kind != RouteNodeKind.MapEdge || !OnSide(map, side, node.X, node.Z)) continue;
                    float d = Dist(node.X, node.Z, px, pz);
                    if (d > best) continue;
                    float mx = node.X + inX * MouthInset, mz = node.Z + inZ * MouthInset;
                    if (Dist(mx, mz, padX, padZ) < MinPadDistance) continue;
                    best = d; x = mx; z = mz; street = true;
                }
                if (street) return true;
                // Else the open tile of the edge nearest that point.
                var tiles = EdgeTiles(map, side);
                tiles.Sort((a, b) => Dist(Centre(a.Tx), Centre(a.Tz), px, pz).CompareTo(Dist(Centre(b.Tx), Centre(b.Tz), px, pz)));
                foreach (var (tx, tz) in tiles)
                {
                    float mx = Centre(tx), mz = Centre(tz);
                    var tile = map.GetTile(tx, tz);
                    if (tile == TileType.Building || tile == TileType.Beach || map.IsNavBlocked(tx, tz)) continue;
                    if (Dist(mx, mz, padX, padZ) < MinPadDistance) continue;
                    if (!TilePathfinder.Find(map, mx, mz, padX, padZ, path, scratch)) continue;
                    x = mx; z = mz;
                    return true;
                }
            }
            return false;
        }

        private static SpawnEdge Make(WorldMap map, int index, float x, float z, float inX, float inZ, float half, int unlock, string street)
        {
            float padX = map.PostOffice.SpawnPadX, padZ = map.PostOffice.SpawnPadZ;
            var path = new List<(float X, float Z)>();
            map.Routes.Route(x, z, padX, padZ, path);
            return new SpawnEdge
            {
                Index = index, X = x, Z = z, InX = inX, InZ = inZ, HalfWidth = half, UnlockShift = unlock,
                Direction = Compass(x - padX, z - padZ), Street = street, Path = path,
            };
        }

        /// <summary>The name of the street under a point, if any.</summary>
        private static string StreetName(WorldMap map, float x, float z)
        {
            const float tile = SimConstants.TileSize;
            int tx = Math.Clamp((int)MathF.Floor(x / tile), 0, map.Cols - 1), tz = Math.Clamp((int)MathF.Floor(z / tile), 0, map.Rows - 1);
            foreach (var s in map.Streets)
                if (s.Rect.Contains(tx, tz)) return s.Name;
            return "";
        }

        /// <summary>Eight points of the compass; north is −z, east +x.</summary>
        public static string Compass(float dx, float dz)
        {
            float deg = MathF.Atan2(dx, -dz) * 180f / MathF.PI;
            if (deg < 0f) deg += 360f;
            int sector = (int)MathF.Round(deg / 45f) % 8;
            return sector switch
            {
                0 => "north", 1 => "north-east", 2 => "east", 3 => "south-east",
                4 => "south", 5 => "south-west", 6 => "west", _ => "north-west",
            };
        }

        private static void Normal(Side s, out float nx, out float nz)
        {
            nx = s == Side.East ? 1f : s == Side.West ? -1f : 0f;
            nz = s == Side.South ? 1f : s == Side.North ? -1f : 0f;
        }

        private static bool OnSide(WorldMap map, Side s, float x, float z) => s switch
        {
            Side.North => z < 0.5f,
            Side.South => z > map.DepthM - 0.5f,
            Side.West => x < 0.5f,
            _ => x > map.WidthM - 0.5f,
        };

        private static List<(int Tx, int Tz)> EdgeTiles(WorldMap map, Side s)
        {
            var tiles = new List<(int, int)>();
            if (s == Side.North || s == Side.South)
                for (int tx = 0; tx < map.Cols; tx++) tiles.Add((tx, s == Side.North ? 0 : map.Rows - 1));
            else
                for (int tz = 0; tz < map.Rows; tz++) tiles.Add((s == Side.West ? 0 : map.Cols - 1, tz));
            return tiles;
        }

        /// <summary>A side is land if most of its tiles are open ground other than the beach: the beach's side faces the sea.</summary>
        private static bool SideHasLand(WorldMap map, Side s)
        {
            int land = 0, all = 0;
            foreach (var (tx, tz) in EdgeTiles(map, s))
            {
                all++;
                var t = map.GetTile(tx, tz);
                if (t != TileType.Beach && t != TileType.Building) land++;
            }
            return land * 2 > all;
        }

        private static float Centre(int t) => (t + 0.5f) * SimConstants.TileSize;

        private static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
    }
}
