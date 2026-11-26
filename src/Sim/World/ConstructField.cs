using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>Something a carrier built (doc 04 §1): one building on its tile (a Belt Ramp on two, an Address Sorter on four).</summary>
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

        /// <summary>The middle of its tile (the one it was built on).</summary>
        public float X => (Tx + 0.5f) * SimConstants.TileSize;
        public float Z => (Tz + 0.5f) * SimConstants.TileSize;

        /// <summary>The middle of all its tiles: where a sorter's round body stands.</summary>
        public float CentreX
        {
            get
            {
                var (fx, _) = TileAt(Def.TileCount - 1);
                return (Tx + fx + 1) * 0.5f * SimConstants.TileSize;
            }
        }

        public float CentreZ
        {
            get
            {
                var (_, fz) = TileAt(Def.TileCount - 1);
                return (Tz + fz + 1) * 0.5f * SimConstants.TileSize;
            }
        }

        /// <summary>
        /// A sorter's or a splitter's filters (doc 04 §2.3, §2.4): output by output,
        /// <see cref="BuildingDef.FilterSlots"/> each; empty for what sorts nothing.
        /// </summary>
        public MailFilter[] Filters { get; }

        public MailFilter Filter(int output, int slot) => Filters[output * Def.FilterSlots + slot];
        /// <summary>A wall runs along X (false) or along Z (true).</summary>
        public bool AlongZ => (Rotation & 1) != 0;

        /// <summary>The way it faces: east, south, west, north (a ramp's up or down aside).</summary>
        public int Facing => Rotation & 3;

        /// <summary>
        /// Its <paramref name="i"/>-th tile, from 0 for the one it was built on to
        /// <see cref="BuildingDef.TileCount"/> − 1: on in the way it faces, then (a sorter) the
        /// same again one tile to the right of that way.
        /// </summary>
        public (int Tx, int Tz) TileAt(int i) => TileOf(Tx, Tz, Facing, i % Def.Length, i / Def.Length);

        /// <summary>The tile <paramref name="along"/> on and <paramref name="across"/> to the right of a building's own, turned so.</summary>
        public static (int Tx, int Tz) TileOf(int tx, int tz, int facing, int along, int across)
        {
            int f = facing & 3, r = (f + 1) & 3;
            return (tx + FacingDx[f] * along + FacingDx[r] * across, tz + FacingDz[f] * along + FacingDz[r] * across);
        }

        private static readonly int[] FacingDx = { 1, 0, -1, 0 }, FacingDz = { 0, 1, 0, -1 };

        /// <summary>Which of its tiles (tx, tz) is, −1 for none of them.</summary>
        public int IndexOf(int tx, int tz)
        {
            for (int i = 0; i < Def.TileCount; i++)
                if (TileAt(i) == (tx, tz)) return i;
            return -1;
        }

        public Construct(BuildingDef def, int tx, int tz, int rotation, int hp, uint containerId)
        {
            Def = def; Tx = tx; Tz = tz; Rotation = (byte)rotation; Hp = hp; ContainerId = containerId;
            Filters = def.FilterOutputs > 0 ? new MailFilter[def.FilterOutputs * def.FilterSlots] : Array.Empty<MailFilter>();
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
        private readonly Construct?[] _ruinTiles;
        private readonly List<Construct> _ruins = new List<Construct>();

        /// <summary>In the order they were built.</summary>
        public IReadOnlyList<Construct> All => _all;
        /// <summary>Goes up with everything a view would draw differently: built, gone, damaged.</summary>
        public int Version { get; private set; }

        public ConstructField(WorldMap map)
        {
            _map = map;
            _tiles = new Construct?[map.Cols * map.Rows];
            _ruinTiles = new Construct?[map.Cols * map.Rows];
        }

        public Construct? At(int tx, int tz) => _map.InBounds(tx, tz) ? _tiles[tz * _map.Cols + tx] : null;

        /// <summary>
        /// Doc 05 §5: what is left of a construct broken at 0 HP — a ghost on its tiles that does
        /// nothing and stops nobody, but holds the ground (and its configuration) until it is
        /// rebuilt or cleared. In the order they fell.
        /// </summary>
        public IReadOnlyList<Construct> Ruins => _ruins;

        public Construct? RuinAt(int tx, int tz) => _map.InBounds(tx, tz) ? _ruinTiles[tz * _map.Cols + tx] : null;

        /// <summary>True for a tile something built or a ruin holds: nothing else is built there.</summary>
        public bool Taken(int tx, int tz) => At(tx, tz) != null || RuinAt(tx, tz) != null;

        /// <summary>Leaves the ruin of <paramref name="c"/> (gone from the field already, or never in it) on its tiles.</summary>
        public void AddRuin(Construct c)
        {
            for (int i = 0; i < c.Def.TileCount; i++)
            {
                var (x, z) = c.TileAt(i);
                if (!_map.InBounds(x, z)) continue;
                RemoveRuin(x, z);
                _ruinTiles[z * _map.Cols + x] = c;
            }
            _ruins.Add(c);
            Version++;
        }

        /// <summary>Clears the ruin on the tile from all of its tiles; the ruin, or null if there was none.</summary>
        public Construct? RemoveRuin(int tx, int tz)
        {
            var c = RuinAt(tx, tz);
            if (c == null) return null;
            for (int i = 0; i < c.Def.TileCount; i++)
            {
                var (x, z) = c.TileAt(i);
                if (_map.InBounds(x, z) && _ruinTiles[z * _map.Cols + x] == c) _ruinTiles[z * _map.Cols + x] = null;
            }
            _ruins.Remove(c);
            Version++;
            return c;
        }

        /// <summary>
        /// True for a tile a tile path keeps off because of what is built there: anything but
        /// spikes, for a carrier's path anything but spikes and gates, and for a raider that jumps
        /// walls (doc 05 §2 Hog Rider) anything but spikes, walls and gates.
        /// </summary>
        public bool Blocks(int tx, int tz, bool carrier = false, bool jumper = false) =>
            At(tx, tz) is { Def.Solid: true } c && !(carrier && c.Def.Behaviour == BuildingBehaviour.Gate) && !(jumper && c.Def.Joins);

        /// <summary>Builds on the tile, and on as many more as its length takes (on the map); what stood on them goes.</summary>
        public Construct Add(BuildingDef def, int tx, int tz, int rotation, int hp, uint containerId)
        {
            var c = new Construct(def, tx, tz, rotation % Math.Max(1, def.Rotations), Math.Clamp(hp, 1, def.Hp), containerId);
            for (int i = 0; i < def.TileCount; i++)
            {
                var (x, z) = c.TileAt(i);
                if (!_map.InBounds(x, z)) continue;
                Remove(x, z);
                _tiles[z * _map.Cols + x] = c;
            }
            _all.Add(c);
            Version++;
            return c;
        }

        /// <summary>Takes down what stands on the tile, from all of its tiles.</summary>
        public bool Remove(int tx, int tz)
        {
            var c = At(tx, tz);
            if (c == null) return false;
            for (int i = 0; i < c.Def.TileCount; i++)
            {
                var (x, z) = c.TileAt(i);
                if (_map.InBounds(x, z) && _tiles[z * _map.Cols + x] == c) _tiles[z * _map.Cols + x] = null;
            }
            _all.Remove(c);
            Version++;
            return true;
        }

        public void Clear()
        {
            if (_all.Count == 0 && _ruins.Count == 0) return;
            Array.Clear(_tiles, 0, _tiles.Length);
            _all.Clear();
            Array.Clear(_ruinTiles, 0, _ruinTiles.Length);
            _ruins.Clear();
            Version++;
        }

        /// <summary>A sorter's or a splitter's filters as they are now (the server's word, or a client told of it).</summary>
        public void SetFilters(Construct c, IReadOnlyList<MailFilter> filters)
        {
            bool same = true;
            for (int i = 0; i < c.Filters.Length; i++)
            {
                var f = i < filters.Count ? filters[i] : MailFilter.Empty;
                if (c.Filters[i] != f) same = false;
                c.Filters[i] = f;
            }
            if (!same) Version++;
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
            float cx = c.CentreX, cz = c.CentreZ;
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
        /// does not; what is lower than the walker's feet is passed over, and a raider that jumps
        /// walls goes over walls and gates. True if it moved.
        /// </summary>
        public bool Push(ref float x, ref float z, float feetY, float r, bool carrier, bool jumper = false)
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
                    if (jumper && c.Def.Joins) continue;
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
        /// walks) rather than beside or behind it. A raider that jumps walls is up against no wall or gate.
        /// </summary>
        public Construct? Ahead(float x, float z, float dirX, float dirZ, float reach, bool jumper = false)
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
                    if (c == null || !c.Def.Solid || (jumper && c.Def.Joins)) continue;
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
