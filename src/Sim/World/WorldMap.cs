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
        /// <summary>Street furniture and greenery, derived from the map itself (<see cref="Scenery"/>).</summary>
        public IReadOnlyList<Prop> Props { get; private set; } = Array.Empty<Prop>();

        /// <summary>FNV-1a 64 of the tile and address tables; the M0 stand-in for worldHash (doc 02 §2).</summary>
        public ulong MapHash { get; private set; }

        public float WidthM => Cols * SimConstants.TileSize;
        public float DepthM => Rows * SimConstants.TileSize;

        public bool InBounds(int tx, int tz) => tx >= 0 && tz >= 0 && tx < Cols && tz < Rows;

        public TileType GetTile(int tx, int tz) => InBounds(tx, tz) ? Tiles[tz * Cols + tx] : TileType.Building;

        /// <summary>Out-of-bounds counts as solid so the map edge is a wall.</summary>
        public bool IsSolid(int tx, int tz) => !InBounds(tx, tz) || Tiles[tz * Cols + tx] == TileType.Building;

        /// <summary>Terrain height at a world position. The M0 test map is flat.</summary>
        public float HeightAt(float x, float z) => 0f;

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
            h.Add(Rows);
            for (int i = 0; i < Tiles.Length; i++) { h.Add((byte)Tiles[i]); h.Add(TileDistrict[i]); }
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
            MapHash = h.Value;
            Props = Scenery.Generate(this);
            IndexProps();
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
            for (int i = 0; i < _navBlocked.Length; i++) _navBlocked[i] = Tiles[i] == TileType.Building;
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
