using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    public enum TileType : byte
    {
        Grass = 0,
        Street = 1,
        /// <summary>Solid: blocks movement.</summary>
        Building = 2,
        /// <summary>Residential lot ground (walkable).</summary>
        Lot = 3,
        /// <summary>Post Office yard (walkable).</summary>
        Yard = 4,
        /// <summary>Sand by the water (doc 02 §3.2 Beach): walkable and open ground, where the sand piles lie.</summary>
        Beach = 5,
        /// <summary>Doc 02 §3.2 shallow water (depth &lt; 2 m): rowed over, a Pier is built on it; nobody walks it.</summary>
        Water = 6,
        /// <summary>Doc 02 §3.2 deep water: rowed over too; a Small Port wants it.</summary>
        DeepWater = 7,
    }

    /// <summary>Tile rectangle [x, y, w, h] in tiles.</summary>
    public readonly struct TileRect
    {
        public readonly int X, Y, W, H;
        public TileRect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        public bool Contains(int tx, int ty) => tx >= X && ty >= Y && tx < X + W && ty < Y + H;
        public float CenterX => (X + W * 0.5f) * SimConstants.TileSize;
        public float CenterZ => (Y + H * 0.5f) * SimConstants.TileSize;
    }

    public sealed class DistrictDef
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public int UnlockShift { get; init; } = 1;
    }

    public sealed class StreetDef
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public int District { get; init; }
        public TileRect Rect { get; init; }
    }

    /// <summary>A building that accepts mail. In M0 every destination is a house with one mailbox.</summary>
    public sealed class DestinationDef
    {
        /// <summary>Index into <see cref="WorldMap.Destinations"/>; the wire id of the destination.</summary>
        public ushort Id { get; init; }
        public DestinationTypeDef Type { get; init; } = null!;
        public AddressId Address { get; init; }
        public StreetDef Street { get; init; } = null!;
        public TileRect Lot { get; init; }
        public TileRect Building { get; init; }
        public float MailboxX { get; init; }
        public float MailboxZ { get; init; }
        public int Variant { get; init; }

        /// <summary>Delivery label text: "{number} {street}".</summary>
        public string Label => $"{Address.Number} {Street.Name}";
    }

    public sealed class PostOfficeDef
    {
        public TileRect Rect { get; init; }
        public IReadOnlyList<TileRect> Solid { get; init; } = Array.Empty<TileRect>();
        public float IntakeX { get; init; }
        public float IntakeZ { get; init; }
        public float SpawnPadX { get; init; }
        public float SpawnPadZ { get; init; }
        /// <summary>Facing of a freshly spawned player in radians (0 looks along −Z), from the map's <c>spawnLookAt</c>.</summary>
        public float SpawnYaw { get; init; }
        /// <summary>The shop counter (doc 02 §3.6), if the map has one: buying and selling happens within reach of it.</summary>
        public bool HasShop { get; init; }
        public float ShopX { get; init; }
        public float ShopZ { get; init; }

        /// <summary>Seats there are around the pad: one for each carrier a server can hold.</summary>
        public const int Seats = 8;

        /// <summary>Where the carrier of a slot stands up: the seats fan out around the pad so nobody spawns inside another.</summary>
        public void Seat(int slot, out float x, out float z)
        {
            x = SpawnPadX + (slot % 2) * 1.0f - 0.5f;
            z = SpawnPadZ + (slot / 2) * 1.0f - 1.5f;
        }
    }

    /// <summary>A storage container that stands in the map from the start (playtest nit 9: a chest by the Post Office).</summary>
    public sealed class MapChestDef
    {
        /// <summary>Index into <see cref="WorldMap.Chests"/>.</summary>
        public int Id { get; init; }
        public ContainerDef Container { get; init; } = null!;
        public float X { get; init; }
        public float Z { get; init; }
        /// <summary>Which way its front looks, in radians (0 looks along −Z).</summary>
        public float Yaw { get; init; }

        /// <summary>The solid part: a round body a carrier walks around or jumps onto.</summary>
        public const float Radius = 0.45f;
        public const float Height = 0.8f;
    }

    /// <summary>
    /// Runtime world representation (doc 02 §4): tile grid, lots/destinations, addresses.
    /// In M0 it is loaded from a hand-authored static file instead of being generated from a seed.
    /// </summary>
    public sealed class WorldMap
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public int Cols { get; init; }
        public int Rows { get; init; }
        public TileType[] Tiles { get; init; } = Array.Empty<TileType>();
        public byte[] TileDistrict { get; init; } = Array.Empty<byte>();
        public IReadOnlyList<DistrictDef> Districts { get; init; } = Array.Empty<DistrictDef>();
        public IReadOnlyList<StreetDef> Streets { get; init; } = Array.Empty<StreetDef>();
        public IReadOnlyList<DestinationDef> Destinations { get; init; } = Array.Empty<DestinationDef>();
        public PostOfficeDef PostOffice { get; init; } = new PostOfficeDef();
        public IReadOnlyList<MapChestDef> Chests { get; init; } = Array.Empty<MapChestDef>();
        /// <summary>The island around a generated town (doc 02 §3.1–§3.2); null for a map from a file.</summary>
        public IslandTerrain? Terrain { get; init; }
        /// <summary>Street furniture and greenery, derived from the map itself (<see cref="Scenery"/>).</summary>
        public IReadOnlyList<Prop> Props { get; private set; } = Array.Empty<Prop>();
        /// <summary>The streets as a graph (doc 02 §3.8): routes, lengths, the map's ways in.</summary>
        public RoutingGraph Routes { get; private set; } = new RoutingGraph();
        /// <summary>The raiders' ways in (doc 02 §3.10); the first is the east end of the first street, open from shift 1.</summary>
        public IReadOnlyList<SpawnEdge> SpawnEdges { get; private set; } = Array.Empty<SpawnEdge>();

        /// <summary>FNV-1a 64 of the tile and address tables and the chests; the M0 stand-in for worldHash (doc 02 §2).</summary>
        public ulong MapHash { get; private set; }
        /// <summary>The hash of everything but the water: what the sand piles are grown from.</summary>
        public ulong LandHash { get; private set; }
        /// <summary>
        /// The hash of the ground alone (tiles, streets, destinations, Post Office): what the scenery
        /// is grown from, so putting a chest into a map does not move its trees.
        /// </summary>
        public ulong SceneryKey { get; private set; }

        public float WidthM => Cols * SimConstants.TileSize;

        /// <summary>The rows down to the last that holds any land: the water along the south edge (doc 02 §3.2) lies beyond them.</summary>
        public int LandRows
        {
            get
            {
                for (int z = Rows - 1; z >= 0; z--)
                    for (int x = 0; x < Cols; x++)
                        if (Tiles[z * Cols + x] is not (TileType.Water or TileType.DeepWater)) return z + 1;
                return 0;
            }
        }
        public float DepthM => Rows * SimConstants.TileSize;

        public bool InBounds(int tx, int tz) => tx >= 0 && tz >= 0 && tx < Cols && tz < Rows;

        public TileType GetTile(int tx, int tz) => InBounds(tx, tz) ? Tiles[tz * Cols + tx] : TileType.Building;

        /// <summary>Out-of-bounds counts as solid so the map edge is a wall.</summary>
        public bool IsSolid(int tx, int tz) => !InBounds(tx, tz) || Tiles[tz * Cols + tx] == TileType.Building;

        /// <summary>Doc 02 §3.2: water of either depth, where boats go and walkers do not.</summary>
        public bool IsWater(int tx, int tz) => InBounds(tx, tz) && Tiles[tz * Cols + tx] is TileType.Water or TileType.DeepWater;

        public bool IsShallowWater(int tx, int tz) => InBounds(tx, tz) && Tiles[tz * Cols + tx] == TileType.Water;

        /// <summary>True if the map has any water at all.</summary>
        public bool HasWater
        {
            get
            {
                foreach (var t in Tiles) if (t is TileType.Water or TileType.DeepWater) return true;
                return false;
            }
        }

        /// <summary>Terrain height at a world position. The M0 test map is flat.</summary>
        public float HeightAt(float x, float z) => 0f;

        /// <summary>
        /// The biome of a tile of the map: the island's where there is one; on a map from a file its
        /// beach is beach and the rest grassland.
        /// </summary>
        public Biome BiomeAt(int tx, int tz) =>
            Terrain != null ? Terrain.TownBiome(tx, tz) : GetTile(tx, tz) == TileType.Beach ? Biome.Beach : Biome.Grassland;

        public DestinationDef? GetDestination(int id) =>
            id >= 0 && id < Destinations.Count ? Destinations[id] : null;

        public DestinationDef? FindDestination(AddressId address)
        {
            for (int i = 0; i < Destinations.Count; i++)
                if (Destinations[i].Address == address) return Destinations[i];
            return null;
        }

        public StreetDef? GetStreet(int id)
        {
            for (int i = 0; i < Streets.Count; i++)
                if (Streets[i].Id == id) return Streets[i];
            return null;
        }

        /// <summary>"13 Larch Lane" for a known address, or the canonical id if the street is unknown.</summary>
        public string FormatAddress(AddressId address)
        {
            var street = GetStreet(address.Street);
            if (street == null) return address.ToString();
            return address.Unit == 0
                ? $"{address.Number} {street.Name}"
                : $"{address.Number} {street.Name}, Unit {address.Unit}";
        }

        internal void ComputeHash()
        {
            var h = Fnv64.Create();
            h.Add(Id);
            h.Add(Cols);
            // Water laid along the south edge does not move the trees either: the scenery key is the land's.
            h.Add(LandRows);
            // A beach is grass to the scenery's key, so laying one down does not move the street's trees.
            for (int i = 0; i < LandRows * Cols; i++) { h.Add((byte)(Tiles[i] == TileType.Beach ? TileType.Grass : Tiles[i])); h.Add(TileDistrict[i]); }
            foreach (var s in Streets) { h.Add(s.Id); h.Add(s.Name); h.Add(s.District); }
            foreach (var d in Destinations)
            {
                h.Add(d.Id);
                h.Add(d.Type.Id);
                h.Add(d.Address.Key);
                h.Add(d.Address.Unit);
                h.Add((int)MathF.Round(d.MailboxX * 100f));
                h.Add((int)MathF.Round(d.MailboxZ * 100f));
            }
            h.Add((int)MathF.Round(PostOffice.IntakeX * 100f));
            h.Add((int)MathF.Round(PostOffice.IntakeZ * 100f));
            h.Add((int)MathF.Round(PostOffice.SpawnPadX * 100f));
            h.Add((int)MathF.Round(PostOffice.SpawnPadZ * 100f));
            h.Add((int)MathF.Round(PostOffice.SpawnYaw * 1000f));
            SceneryKey = h.Value;
            Terrain?.AddTo(ref h);
            for (int i = 0; i < Tiles.Length; i++)
                if (Tiles[i] == TileType.Beach) h.Add(i);
            if (PostOffice.HasShop)
            {
                h.Add((int)MathF.Round(PostOffice.ShopX * 100f));
                h.Add((int)MathF.Round(PostOffice.ShopZ * 100f));
            }
            foreach (var c in Chests)
            {
                h.Add(c.Container.Id);
                h.Add((int)MathF.Round(c.X * 100f));
                h.Add((int)MathF.Round(c.Z * 100f));
                h.Add((int)MathF.Round(c.Yaw * 1000f));
            }
            // The sand piles grow from the land's hash: water laid down after does not move them.
            LandHash = h.Value;
            if (Rows != LandRows || HasWater)
            {
                h.Add(Rows);
                for (int i = 0; i < Tiles.Length; i++)
                    if (Tiles[i] is TileType.Water or TileType.DeepWater) { h.Add(i); h.Add((byte)Tiles[i]); }
            }
            MapHash = h.Value;
            Props = Scenery.Generate(this);
            Routes = RoutingGraph.Build(this);
            IndexProps();
            SpawnEdges = World.SpawnEdges.Build(this);
        }

        /// <summary>How far past a prop's own radius a walker's body can reach: the widest walker's radius.</summary>
        public const float WalkerReach = 0.5f;
        /// <summary>The radius the tile paths are planned for (a carrier; no enemy that paths is wider).</summary>
        private const float NavRadius = 0.42f;

        private int[]?[] _tileProps = Array.Empty<int[]?>();
        private bool[] _navBlocked = Array.Empty<bool>();

        /// <summary>Indices into <see cref="Props"/> of the solid props a walker standing on this tile can touch.</summary>
        public int[]? SolidPropsAt(int tx, int tz) => InBounds(tx, tz) ? _tileProps[tz * Cols + tx] : null;

        /// <summary>
        /// True for a tile a tile path may not use: solid, or with a solid prop close enough that a
        /// walker crossing the tile anywhere could bump into it. Streets are never blocked
        /// (<see cref="Scenery"/> keeps its props back from them).
        /// </summary>
        public bool IsNavBlocked(int tx, int tz) => !InBounds(tx, tz) || _navBlocked[tz * Cols + tx];

        private void IndexProps()
        {
            const float tile = SimConstants.TileSize;
            var buckets = new List<int>?[Cols * Rows];
            _navBlocked = new bool[Cols * Rows];
            for (int i = 0; i < _navBlocked.Length; i++) _navBlocked[i] = Tiles[i] is TileType.Building or TileType.Water or TileType.DeepWater;
            for (int i = 0; i < Props.Count; i++)
            {
                var p = Props[i];
                if (!p.Solid) continue;
                float reach = p.Radius + WalkerReach;
                int tx0 = (int)MathF.Floor((MathF.Min(p.X, p.X2) - reach) / tile), tx1 = (int)MathF.Floor((MathF.Max(p.X, p.X2) + reach) / tile);
                int tz0 = (int)MathF.Floor((MathF.Min(p.Z, p.Z2) - reach) / tile), tz1 = (int)MathF.Floor((MathF.Max(p.Z, p.Z2) + reach) / tile);
                for (int tz = Math.Max(tz0, 0); tz <= Math.Min(tz1, Rows - 1); tz++)
                {
                    for (int tx = Math.Max(tx0, 0); tx <= Math.Min(tx1, Cols - 1); tx++)
                    {
                        (buckets[tz * Cols + tx] ??= new List<int>()).Add(i);
                        // Nearest point of the tile's square to the prop's centre line.
                        float cx = Math.Clamp(p.X, tx * tile, (tx + 1) * tile), cz = Math.Clamp(p.Z, tz * tile, (tz + 1) * tile);
                        float d = p.Distance(cx, cz, out float nx, out float nz);
                        cx = Math.Clamp(nx, tx * tile, (tx + 1) * tile); cz = Math.Clamp(nz, tz * tile, (tz + 1) * tile);
                        d = MathF.Min(d, p.Distance(cx, cz, out _, out _));
                        if (d < p.Radius + NavRadius) _navBlocked[tz * Cols + tx] = true;
                    }
                }
            }
            _tileProps = new int[]?[Cols * Rows];
            for (int i = 0; i < buckets.Length; i++) _tileProps[i] = buckets[i]?.ToArray();
        }
    }
}
