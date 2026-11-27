using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    public enum PropKind : byte
    {
        Tree = 0,
        Bush = 1,
        Boulder = 2,
        Lamp = 3,
        Bench = 4,
        /// <summary>A run of picket fence from (X, Z) to (X2, Z2).</summary>
        Fence = 5,
        Flowers = 6,
        /// <summary>A chest the map places (<see cref="MapChestDef"/>): not grown, only made solid here.</summary>
        Chest = 7,
        /// <summary>A heap of sand on a beach (doc 02 §3.9): dug with a Shovel.</summary>
        SandPile = 8,
    }

    /// <summary>
    /// One piece of street furniture or greenery. A prop with a <see cref="Radius"/> is solid: a
    /// capsule lying on the ground from (X, Z) to (X2, Z2), <see cref="Height"/> tall — a carrier
    /// whose feet are above it passes over (a fence can be jumped, a tree cannot).
    /// </summary>
    public readonly struct Prop
    {
        public readonly PropKind Kind;
        public readonly float X, Z, X2, Z2;
        public readonly float Yaw;
        public readonly float Scale;
        public readonly byte Variant;
        public readonly float Radius;
        public readonly float Height;

        public Prop(PropKind kind, float x, float z, float x2, float z2, float yaw, float scale, int variant, float radius, float height)
        {
            Kind = kind; X = x; Z = z; X2 = x2; Z2 = z2; Yaw = yaw; Scale = scale; Variant = (byte)variant; Radius = radius; Height = height;
        }

        public bool Solid => Radius > 0f;

        /// <summary>Distance from a point to the prop's centre line.</summary>
        public float Distance(float px, float pz, out float nearX, out float nearZ)
        {
            float dx = X2 - X, dz = Z2 - Z;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq > 1e-9f ? Math.Clamp(((px - X) * dx + (pz - Z) * dz) / lenSq, 0f, 1f) : 0f;
            nearX = X + dx * t;
            nearZ = Z + dz * t;
            float ox = px - nearX, oz = pz - nearZ;
            return MathF.Sqrt(ox * ox + oz * oz);
        }
    }

    /// <summary>
    /// Dresses a map the way the concept video's town looks (design/reference/higgsfield-01): street
    /// lamps and benches along the streets, a picket fence with a gate gap in front of every house,
    /// bushes at the house corners, trees and boulders on the open grass, flowers. The result
    /// depends only on the map (its hash seeds the stream), so server and clients agree on what
    /// stands where without sending it.
    /// </summary>
    public static class Scenery
    {
        private const float Tile = SimConstants.TileSize;
        private const float Tall = 100f;
        /// <summary>How many looks a tree, a bush or a boulder comes in; content/nodes tells them apart by it.</summary>
        public const int Variants = 3;

        /// <summary>Unit vector from a destination's building towards its mailbox along the dominant axis: the way the house faces.</summary>
        public static void Front(DestinationDef dest, out int fx, out int fz)
        {
            float dx = dest.MailboxX - dest.Building.CenterX, dz = dest.MailboxZ - dest.Building.CenterZ;
            float w = dest.Building.W * Tile, d = dest.Building.H * Tile;
            if (MathF.Abs(dx) / w > MathF.Abs(dz) / d) { fx = dx < 0 ? -1 : 1; fz = 0; }
            else { fx = 0; fz = dz < 0 ? -1 : 1; }
        }

        /// <summary>Where the front door's walk starts (the middle of the front wall) and where it meets the mailbox line.</summary>
        public static void Walkway(DestinationDef dest, out float x0, out float z0, out float x1, out float z1)
        {
            Front(dest, out int fx, out int fz);
            var b = dest.Building;
            x0 = b.CenterX + fx * b.W * Tile * 0.5f;
            z0 = b.CenterZ + fz * b.H * Tile * 0.5f;
            // It runs straight out to one tile past the mailbox line, which is where the street is.
            float reach = fx != 0 ? MathF.Abs(dest.MailboxX - x0) + 1.2f : MathF.Abs(dest.MailboxZ - z0) + 1.2f;
            x1 = x0 + fx * reach;
            z1 = z0 + fz * reach;
        }

        /// <summary>
        /// What the open ground grows, as the upper ends of a roll of 100 (trees below the first,
        /// then boulders, bushes, flowers): by the island's biome (doc 02 §3.2 — wood in the
        /// forest, stone and ore on rocky ground, fiber and berries on the grassland), and on a map
        /// without an island the test street's mix.
        /// </summary>
        public static void Odds(WorldMap map, float x, float z, out int trees, out int boulders, out int bushes, out int flowers)
        {
            if (map.Terrain == null) { trees = 34; boulders = 44; bushes = 58; flowers = 80; return; }
            switch (map.BiomeAt((int)MathF.Floor(x / Tile), (int)MathF.Floor(z / Tile)))
            {
                case Biome.Forest: trees = 56; boulders = 60; bushes = 72; flowers = 78; break;
                case Biome.Rocky: trees = 6; boulders = 44; bushes = 50; flowers = 54; break;
                default: trees = 16; boulders = 24; bushes = 46; flowers = 74; break;
            }
        }

        public static List<Prop> Generate(WorldMap map)
        {
            var props = new List<Prop>();
            if (map.Bare) return props;
            var rng = new RngStream((uint)(map.SceneryKey ^ (map.SceneryKey >> 32)), "scenery");
            var keepClear = new List<(float X0, float Z0, float X1, float Z1, float R)>();
            var po = map.PostOffice;
            keepClear.Add((po.IntakeX, po.IntakeZ, po.IntakeX, po.IntakeZ, 3f));
            keepClear.Add((po.SpawnPadX, po.SpawnPadZ, po.SpawnPadX, po.SpawnPadZ, 3f));
            foreach (var c in map.Chests) keepClear.Add((c.X, c.Z, c.X, c.Z, 2f));
            foreach (var d in map.Destinations)
            {
                // A Business Dock has no garden: the town grows as if it were not there, and its lot is cleared after.
                if (d.IsDock) continue;
                keepClear.Add((d.MailboxX, d.MailboxZ, d.MailboxX, d.MailboxZ, 1.8f));
                Walkway(d, out float wx0, out float wz0, out float wx1, out float wz1);
                keepClear.Add((wx0, wz0, wx1, wz1, 1.3f));
            }

            var cells = new Dictionary<long, List<int>>();
            int indexed = 0;
            float maxSolid = 0f;
            bool Free(float x, float z, float r)
            {
                foreach (var k in keepClear)
                {
                    var seg = new Prop(PropKind.Fence, k.X0, k.Z0, k.X1, k.Z1, 0f, 1f, 0, 0f, 0f);
                    if (seg.Distance(x, z, out _, out _) < k.R + r) return false;
                }
                // The solid props near the point only, from a grid of 4 m cells (a Large Island grows a thousand and more).
                for (; indexed < props.Count; indexed++)
                {
                    var p = props[indexed];
                    if (!p.Solid) continue;
                    maxSolid = MathF.Max(maxSolid, p.Radius);
                    int cx0 = Cell(MathF.Min(p.X, p.X2) - p.Radius), cx1 = Cell(MathF.Max(p.X, p.X2) + p.Radius);
                    int cz0 = Cell(MathF.Min(p.Z, p.Z2) - p.Radius), cz1 = Cell(MathF.Max(p.Z, p.Z2) + p.Radius);
                    for (int cz = cz0; cz <= cz1; cz++)
                        for (int cx = cx0; cx <= cx1; cx++)
                        {
                            long key = ((long)cz << 32) ^ (uint)cx;
                            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                            list.Add(indexed);
                        }
                }
                float reach = r + 0.6f + maxSolid;
                for (int cz = Cell(z - reach); cz <= Cell(z + reach); cz++)
                    for (int cx = Cell(x - reach); cx <= Cell(x + reach); cx++)
                    {
                        if (!cells.TryGetValue(((long)cz << 32) ^ (uint)cx, out var list)) continue;
                        foreach (int i in list)
                        {
                            var p = props[i];
                            if (p.Distance(x, z, out _, out _) < p.Radius + r + 0.6f) return false;
                        }
                    }
                return true;
            }
            static int Cell(float v) => (int)MathF.Floor(v / 4f);

            // A dock's lot reads as the grass it was laid on, so it moves nothing of the rest (the draws of the stream depend on what fits).
            DestinationDef? OnDock(int tx, int tz)
            {
                foreach (var d in map.Destinations)
                    if (d.IsDock && tx >= d.Lot.X && tx < d.Lot.X + d.Lot.W && tz >= d.Lot.Y && tz < d.Lot.Y + d.Lot.H) return d;
                return null;
            }
            TileType At(float x, float z)
            {
                int tx = (int)MathF.Floor(x / Tile), tz = (int)MathF.Floor(z / Tile);
                return OnDock(tx, tz)?.LaidOn ?? map.GetTile(tx, tz);
            }
            bool Walkable(float x, float z)
            {
                int tx = (int)MathF.Floor(x / Tile), tz = (int)MathF.Floor(z / Tile);
                return map.InBounds(tx, tz) && (!map.IsSolid(tx, tz) || OnDock(tx, tz) != null);
            }
            // Solid things keep a walker's width back from the street, so its tiles stay clear for tile paths.
            bool OffStreet(float x, float z, float r)
            {
                float m = r + 0.45f;
                for (int oz = -1; oz <= 1; oz++)
                    for (int ox = -1; ox <= 1; ox++)
                        if (At(x + ox * m, z + oz * m) == TileType.Street) return false;
                return true;
            }
            float Unit() => rng.NextInt(1000) / 1000f;

            // Fences first: everything else keeps away from them.
            foreach (var d in map.Destinations)
            {
                if (d.IsDock) continue;
                Front(d, out int fx, out int fz);
                float lx0 = d.Lot.X * Tile, lz0 = d.Lot.Y * Tile, lx1 = (d.Lot.X + d.Lot.W) * Tile, lz1 = (d.Lot.Y + d.Lot.H) * Tile;
                Walkway(d, out float wx0, out float wz0, out _, out _);
                // Set back from the street by more than a walker's radius, so the street's tiles stay
                // clear for tile paths; the gate gap takes in the front walk and the mailbox.
                const float inset = 0.6f, gap = 1.3f, r = 0.12f, h = 0.95f;
                if (fz != 0)
                {
                    float z = fz > 0 ? lz1 - inset : lz0 + inset;
                    float g0 = MathF.Min(wx0 - gap, d.MailboxX - 0.9f), g1 = MathF.Max(wx0 + gap, d.MailboxX + 0.9f);
                    if (g0 - (lx0 + inset) > 1f) props.Add(new Prop(PropKind.Fence, lx0 + inset, z, g0, z, 0f, 1f, 0, r, h));
                    if (lx1 - inset - g1 > 1f) props.Add(new Prop(PropKind.Fence, g1, z, lx1 - inset, z, 0f, 1f, 0, r, h));
                }
                else
                {
                    float x = fx > 0 ? lx1 - inset : lx0 + inset;
                    float g0 = MathF.Min(wz0 - gap, d.MailboxZ - 0.9f), g1 = MathF.Max(wz0 + gap, d.MailboxZ + 0.9f);
                    if (g0 - (lz0 + inset) > 1f) props.Add(new Prop(PropKind.Fence, x, lz0 + inset, x, g0, 0f, 1f, 0, r, h));
                    if (lz1 - inset - g1 > 1f) props.Add(new Prop(PropKind.Fence, x, g1, x, lz1 - inset, 0f, 1f, 0, r, h));
                }
            }

            // Lamps along both sides of every street, a bench beside every other one.
            foreach (var street in map.Streets)
            {
                bool alongX = street.Rect.W >= street.Rect.H;
                float a0 = (alongX ? street.Rect.X : street.Rect.Y) * Tile;
                float a1 = a0 + (alongX ? street.Rect.W : street.Rect.H) * Tile;
                float b0 = (alongX ? street.Rect.Y : street.Rect.X) * Tile;
                float b1 = b0 + (alongX ? street.Rect.H : street.Rect.W) * Tile;
                int index = 0;
                for (float a = a0 + 5f; a < a1 - 2f; a += 10f, index++)
                {
                    bool low = index % 2 == 0;
                    float b = low ? b0 - 0.7f : b1 + 0.7f;
                    float lampYaw = alongX ? (low ? 0f : MathF.PI) : (low ? MathF.PI * 0.5f : -MathF.PI * 0.5f);
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        float along = a + attempt * 2.5f;
                        float x = alongX ? along : b, z = alongX ? b : along;
                        if (!Walkable(x, z) || !Free(x, z, 0.3f)) continue;
                        props.Add(new Prop(PropKind.Lamp, x, z, x, z, lampYaw, 1f, 0, 0.18f, Tall));
                        if (index % 2 == 1) break;
                        float benchAlong = along + 2.6f, benchB = low ? b0 - 1.5f : b1 + 1.5f;
                        float bx = alongX ? benchAlong : benchB, bz = alongX ? benchB : benchAlong;
                        if (!Walkable(bx, bz) || !Free(bx, bz, 1.1f)) break;
                        float hx = alongX ? 0.75f : 0f, hz = alongX ? 0f : 0.75f;
                        props.Add(new Prop(PropKind.Bench, bx - hx, bz - hz, bx + hx, bz + hz, lampYaw, 1f, 0, 0.32f, 0.85f));
                        break;
                    }
                }
            }

            // Bushes at the front corners of every house, flowers in the front yard.
            foreach (var d in map.Destinations)
            {
                if (d.IsDock) continue;
                Front(d, out int fx, out int fz);
                var b = d.Building;
                float halfW = b.W * Tile * 0.5f, halfD = b.H * Tile * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    // "Across" is along the front wall; "out" is away from it.
                    float across = (fz != 0 ? halfW : halfD) - 0.7f;
                    float outward = (fz != 0 ? halfD : halfW) + 0.75f;
                    float x = b.CenterX + (fz != 0 ? side * across : fx * outward);
                    float z = b.CenterZ + (fz != 0 ? fz * outward : side * across);
                    if (Walkable(x, z) && Free(x, z, 0.5f) && OffStreet(x, z, 0.5f))
                        props.Add(new Prop(PropKind.Bush, x, z, x, z, Unit() * 6.283f, 0.8f + Unit() * 0.35f, rng.NextInt(Variants), 0.5f, 0.9f));
                    float fxp = b.CenterX + (fz != 0 ? side * (across - 0.6f - Unit()) : fx * (outward + 1.2f + Unit()));
                    float fzp = b.CenterZ + (fz != 0 ? fz * (outward + 1.2f + Unit()) : side * (across - 0.6f - Unit()));
                    if (Walkable(fxp, fzp) && Free(fxp, fzp, 0.2f))
                        props.Add(new Prop(PropKind.Flowers, fxp, fzp, fxp, fzp, Unit() * 6.283f, 0.8f + Unit() * 0.5f, rng.NextInt(4), 0f, 0f));
                }
            }

            // Trees, boulders, bushes and flowers on the open grass, on a jittered grid.
            const float cell = 4.5f;
            for (float gz = cell * 0.5f; gz < map.LandRows * Tile; gz += cell)
            {
                for (float gx = cell * 0.5f; gx < map.WidthM; gx += cell)
                {
                    float x = gx + (Unit() - 0.5f) * cell * 0.7f, z = gz + (Unit() - 0.5f) * cell * 0.7f;
                    int roll = rng.NextInt(100);
                    float scale = 0.85f + Unit() * 0.5f, yaw = Unit() * 6.283f;
                    int variant = rng.NextInt(Variants);
                    if (At(x, z) != TileType.Grass) continue;
                    Odds(map, x, z, out int trees, out int boulders, out int bushes, out int flowers);
                    // Open grass only: nothing but grass within a tile and a half, so trunks stay off lots, yards and the road.
                    bool open = true;
                    for (int oz = -1; oz <= 1 && open; oz++)
                        for (int ox = -1; ox <= 1 && open; ox++)
                            if (At(x + ox * 3f, z + oz * 3f) != TileType.Grass && map.InBounds((int)MathF.Floor((x + ox * 3f) / Tile), (int)MathF.Floor((z + oz * 3f) / Tile))
                                && !map.IsWater((int)MathF.Floor((x + ox * 3f) / Tile), (int)MathF.Floor((z + oz * 3f) / Tile))) open = false;
                    if (roll < trees)
                    {
                        if (open && Free(x, z, 0.4f)) props.Add(new Prop(PropKind.Tree, x, z, x, z, yaw, scale, variant, 0.35f, Tall));
                    }
                    else if (roll < boulders)
                    {
                        float r = 0.55f * scale;
                        if (Free(x, z, r) && OffStreet(x, z, r)) props.Add(new Prop(PropKind.Boulder, x, z, x, z, yaw, scale, variant, r, 0.7f * scale));
                    }
                    else if (roll < bushes)
                    {
                        if (Free(x, z, 0.5f) && OffStreet(x, z, 0.5f * scale)) props.Add(new Prop(PropKind.Bush, x, z, x, z, yaw, scale, variant, 0.5f * scale, 0.9f));
                    }
                    else if (roll < flowers)
                    {
                        if (Free(x, z, 0.2f)) props.Add(new Prop(PropKind.Flowers, x, z, x, z, yaw, scale, rng.NextInt(4), 0f, 0f));
                    }
                }
            }
            // Sand piles on the beaches, out of a stream of their own: a beach moves nothing else.
            var sandRng = new RngStream((uint)(map.LandHash ^ (map.LandHash >> 32)), "sand");
            const float sandCell = 4f;
            for (float gz = sandCell * 0.5f; gz < map.LandRows * Tile; gz += sandCell)
            {
                for (float gx = sandCell * 0.5f; gx < map.WidthM; gx += sandCell)
                {
                    float x = gx + (sandRng.NextInt(1000) / 1000f - 0.5f) * sandCell * 0.6f, z = gz + (sandRng.NextInt(1000) / 1000f - 0.5f) * sandCell * 0.6f;
                    int roll = sandRng.NextInt(100);
                    float scale = 0.85f + sandRng.NextInt(1000) / 1000f * 0.4f, yaw = sandRng.NextInt(1000) / 1000f * 6.283f;
                    if (roll >= 45 || At(x, z) != TileType.Beach) continue;
                    // A pile keeps a walker's width off the map's edge, which is a wall.
                    float r = 0.6f * scale;
                    if (x < r + 1f || z < r + 1f || x > map.WidthM - r - 1f || z > map.LandRows * Tile - r - 1f) continue;
                    if (Free(x, z, r) && OffStreet(x, z, r)) props.Add(new Prop(PropKind.SandPile, x, z, x, z, yaw, scale, 0, r, 0.45f * scale));
                }
            }
            // A dock's paved yard and warehouse take their lot, and a body's width round it.
            foreach (var d in map.Destinations)
            {
                if (!d.IsDock) continue;
                float x0 = d.Lot.X * Tile - 1f, z0 = d.Lot.Y * Tile - 1f, x1 = (d.Lot.X + d.Lot.W) * Tile + 1f, z1 = (d.Lot.Y + d.Lot.H) * Tile + 1f;
                props.RemoveAll(p => MathF.Max(p.X, p.X2) > x0 && MathF.Min(p.X, p.X2) < x1 && MathF.Max(p.Z, p.Z2) > z0 && MathF.Min(p.Z, p.Z2) < z1);
            }
            // Nothing stands on an oil seep, nor within a body's width of it (design choice 116).
            foreach (var (sx, sz) in map.OilSeeps)
            {
                // Clear of a walker's reach and the widest boulder, so the seep's tile is never nav-blocked.
                float x0 = sx * Tile - 1.5f, z0 = sz * Tile - 1.5f, x1 = (sx + 1) * Tile + 1.5f, z1 = (sz + 1) * Tile + 1.5f;
                props.RemoveAll(p => MathF.Max(p.X, p.X2) > x0 && MathF.Min(p.X, p.X2) < x1 && MathF.Max(p.Z, p.Z2) > z0 && MathF.Min(p.Z, p.Z2) < z1);
            }
            // Last, and without a roll of the dice: the chests stand where the map says.
            foreach (var c in map.Chests)
                props.Add(new Prop(PropKind.Chest, c.X, c.Z, c.X, c.Z, c.Yaw, 1f, 0, MapChestDef.Radius, MapChestDef.Height));
            return props;
        }
    }
}
