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
        }
    }
}
