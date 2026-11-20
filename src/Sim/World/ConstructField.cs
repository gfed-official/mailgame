using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>Something a carrier built (doc 04 §1): one building on one tile.</summary>
    public sealed class Construct
    {
        public BuildingDef Def { get; }
        public int Tx { get; }
        public int Tz { get; }
        /// <summary>Quarter turns, below the building's <see cref="BuildingDef.Rotations"/>.</summary>
        public byte Rotation { get; }
        public int Hp { get; internal set; }
        /// <summary>The container of a chest; 0 for what holds nothing.</summary>
        public uint ContainerId { get; }

        /// <summary>The middle of its tile.</summary>
        public float X => (Tx + 0.5f) * SimConstants.TileSize;
        public float Z => (Tz + 0.5f) * SimConstants.TileSize;
        /// <summary>A wall runs along X (false) or along Z (true).</summary>
        public bool AlongZ => (Rotation & 1) != 0;

        public Construct(BuildingDef def, int tx, int tz, int rotation, int hp, uint containerId)
        {
            Def = def; Tx = tx; Tz = tz; Rotation = (byte)rotation; Hp = hp; ContainerId = containerId;
        }
    }

    /// <summary>
    /// What the carriers have built on a map, by tile. The server holds one, and every client
    /// mirrors it; the <see cref="WorldMap"/> itself (which a host's server and client share)
    /// never changes.
    ///
    /// A wall is two arms from the middle of its tile to the tile's edges, along the way it was
    /// turned; it grows a third or fourth arm towards a neighbouring wall that runs at it, so a
    /// corner and a T close by themselves. A chest is a round body in the middle of its tile.
    /// Whatever stands on a tile, the raiders' tile paths keep off it — but for spikes, which
    /// lie flat for everybody to walk over.
    /// </summary>
    public sealed class ConstructField
    {
        /// <summary>Arm bits of <see cref="Arms"/>: towards +X, +Z, −X, −Z.</summary>
        public const int ArmEast = 1, ArmSouth = 2, ArmWest = 4, ArmNorth = 8;

        private static readonly (int Dx, int Dz)[] ArmDirs = { (1, 0), (0, 1), (-1, 0), (0, -1) };

        private readonly WorldMap _map;
        private readonly Construct?[] _tiles;
        private readonly List<Construct> _all = new List<Construct>();

        /// <summary>In the order they were built.</summary>
        public IReadOnlyList<Construct> All => _all;
        /// <summary>Goes up with everything a view would draw differently: built, gone, damaged.</summary>
        public int Version { get; private set; }

        public ConstructField(WorldMap map)
        {
            _map = map;
            _tiles = new Construct?[map.Cols * map.Rows];
        }

        public Construct? At(int tx, int tz) => _map.InBounds(tx, tz) ? _tiles[tz * _map.Cols + tx] : null;

        /// <summary>
        /// True for a tile a tile path keeps off because of what is built there: anything but
        /// spikes, and for a carrier's path anything but spikes and gates.
        /// </summary>
        public bool Blocks(int tx, int tz, bool carrier = false) =>
            At(tx, tz) is { Def.Solid: true } c && !(carrier && c.Def.Behaviour == BuildingBehaviour.Gate);

        public Construct Add(BuildingDef def, int tx, int tz, int rotation, int hp, uint containerId)
        {
            var c = new Construct(def, tx, tz, rotation % Math.Max(1, def.Rotations), Math.Clamp(hp, 1, def.Hp), containerId);
            Remove(tx, tz);
            _tiles[tz * _map.Cols + tx] = c;
            _all.Add(c);
            Version++;
            return c;
        }

        public bool Remove(int tx, int tz)
        {
            var c = At(tx, tz);
            if (c == null) return false;
            _tiles[tz * _map.Cols + tx] = null;
            _all.Remove(c);
            Version++;
            return true;
        }

        public void Clear()
        {
            if (_all.Count == 0) return;
            Array.Clear(_tiles, 0, _tiles.Length);
            _all.Clear();
            Version++;
        }

        public void SetHp(Construct c, int hp)
        {
            hp = Math.Clamp(hp, 0, c.Def.Hp);
            if (c.Hp == hp) return;
            c.Hp = hp;
            Version++;
        }

        /// <summary>
        /// Which ways the construct reaches from the middle of its tile: a wall's own two, and one
        /// more towards every neighbouring wall or gate that runs at it. 0 for a chest.
        /// </summary>
        public int Arms(Construct c)
        {
            if (!c.Def.Joins) return 0;
            int arms = c.AlongZ ? ArmSouth | ArmNorth : ArmEast | ArmWest;
            for (int d = 0; d < 4; d++)
            {
                if ((arms & (1 << d)) != 0) continue;
                var n = At(c.Tx + ArmDirs[d].Dx, c.Tz + ArmDirs[d].Dz);
                if (n != null && n.Def.Joins && n.AlongZ == (ArmDirs[d].Dz != 0)) arms |= 1 << d;
            }
            return arms;
        }

        /// <summary>
        /// How far a point is from the construct's centre line (a chest's middle, a wall's arms),
        /// and the nearest point of that. Its body is <see cref="BuildingDef.Radius"/> around it.
        /// </summary>
        public float Distance(Construct c, float x, float z, out float nearX, out float nearZ)
        {
            float cx = c.X, cz = c.Z;
            nearX = cx; nearZ = cz;
            float dx = x - cx, dz = z - cz;
            float best = dx * dx + dz * dz;
            int arms = Arms(c);
            if (arms != 0)
            {
                // An arm ends where its round end touches the tile's edge.
                float length = SimConstants.TileSize * 0.5f - c.Def.Radius;
                for (int d = 0; d < 4; d++)
                {
                    if ((arms & (1 << d)) == 0) continue;
                    float t = Math.Clamp(dx * ArmDirs[d].Dx + dz * ArmDirs[d].Dz, 0f, length);
                    float px = cx + ArmDirs[d].Dx * t, pz = cz + ArmDirs[d].Dz * t;
                    float sq = (x - px) * (x - px) + (z - pz) * (z - pz);
                    if (sq >= best) continue;
                    best = sq; nearX = px; nearZ = pz;
                }
            }
            return MathF.Sqrt(best);
        }

        /// <summary>
        /// Pushes a walker's circle out of what is built. A carrier walks through a gate, a raider
        /// does not; what is lower than the walker's feet is passed over. True if it moved.
        /// </summary>
        public bool Push(ref float x, ref float z, float feetY, float r, bool carrier)
        {
            if (_all.Count == 0) return false;
            const float tile = SimConstants.TileSize;
            bool pushed = false;
            // Nothing built reaches past its own tile.
            int minTx = (int)MathF.Floor((x - r) / tile), maxTx = (int)MathF.Floor((x + r) / tile);
            int minTz = (int)MathF.Floor((z - r) / tile), maxTz = (int)MathF.Floor((z + r) / tile);
            for (int tz = minTz; tz <= maxTz; tz++)
            {
                for (int tx = minTx; tx <= maxTx; tx++)
                {
                    var c = At(tx, tz);
                    if (c == null || !c.Def.Solid || feetY >= c.Def.Height) continue;
                    if (carrier && c.Def.Behaviour == BuildingBehaviour.Gate) continue;
                    float reach = c.Def.Radius + r;
                    float dist = Distance(c, x, z, out float nearX, out float nearZ);
                    if (dist >= reach) continue;
                    if (dist > 1e-5f)
                    {
                        float push = (reach - dist) / dist;
                        x += (x - nearX) * push;
                        z += (z - nearZ) * push;
                    }
                    else if (c.Def.Joins && !c.AlongZ) z = nearZ + reach;
                    else x = nearX + reach;
                    pushed = true;
                }
            }
            return pushed;
        }

        /// <summary>
        /// What a raider walking in a direction is up against: the nearest construct whose body is
        /// within <paramref name="reach"/> of the point and lies ahead (within 60° of the way it
        /// walks) rather than beside or behind it.
        /// </summary>
        public Construct? Ahead(float x, float z, float dirX, float dirZ, float reach)
        {
            if (_all.Count == 0) return null;
            const float tile = SimConstants.TileSize;
            float span = reach + 0.5f;
            int minTx = (int)MathF.Floor((x - span) / tile), maxTx = (int)MathF.Floor((x + span) / tile);
            int minTz = (int)MathF.Floor((z - span) / tile), maxTz = (int)MathF.Floor((z + span) / tile);
            Construct? best = null;
            float bestDist = float.MaxValue;
            for (int tz = minTz; tz <= maxTz; tz++)
            {
                for (int tx = minTx; tx <= maxTx; tx++)
                {
                    var c = At(tx, tz);
                    if (c == null || !c.Def.Solid) continue;
                    float line = Distance(c, x, z, out float nearX, out float nearZ);
                    float dist = line - c.Def.Radius;
                    if (dist > reach || dist >= bestDist) continue;
                    if ((nearX - x) * dirX + (nearZ - z) * dirZ < 0.5f * line) continue;
                    best = c;
                    bestDist = dist;
                }
            }
            return best;
        }

        /// <summary>
        /// The first wall or gate a straight line between two points goes through; null if none
        /// stands between them. A chest is no cover.
        /// </summary>
        public Construct? Between(float x0, float z0, float x1, float z1)
        {
            if (_all.Count == 0) return null;
            const float tile = SimConstants.TileSize;
            float dx = x1 - x0, dz = z1 - z0;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(1, (int)MathF.Ceiling(dist / 0.15f));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = x0 + dx * t, z = z0 + dz * t;
                var c = At((int)MathF.Floor(x / tile), (int)MathF.Floor(z / tile));
                if (c == null || !c.Def.Joins) continue;
                if (Distance(c, x, z, out _, out _) <= c.Def.Radius) return c;
            }
            return null;
        }
    }
}
