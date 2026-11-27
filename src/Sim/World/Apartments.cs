using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// Doc 02 §3.5 Apartment Complex: "4 × 6 lot; one Mail Room entrance with unit slots (6–12) … at most 1 per medium
    /// town, appears in shift 4–5 districts" (design choice 170). After a town's houses are laid out, one house of its
    /// districts 4–5 whose lot has two tiles of open grass beside it becomes a complex: the lot widened to six tiles, a
    /// block of five by three on it, its Mail Room entrance at the street where the house's mailbox was, and 6–12 units
    /// that mail is addressed to. The draw is the "apartments" stream's, so the town itself is drawn as before.
    /// </summary>
    public static class Apartments
    {
        public const int MinUnits = 6, MaxUnits = 12;
        private const int LotWidth = 6;

        /// <summary>
        /// Makes one house of districts <paramref name="fromDistrict"/>–<paramref name="toDistrict"/> (that
        /// <paramref name="inTown"/> lets through) a complex, in place in <paramref name="destinations"/> and the tiles;
        /// false when no house there has room.
        /// </summary>
        public static bool Place(ContentDb content, uint seed, string suffix, int cols, TileType[] tiles, byte[] tileDistrict,
            List<DestinationDef> destinations, int fromDistrict, int toDistrict, Func<DestinationDef, bool>? inTown = null)
        {
            var rng = new RngStream(seed, "apartments" + suffix);
            var candidates = new List<int>();
            for (int i = 0; i < destinations.Count; i++)
            {
                var d = destinations[i];
                if (d.Type.Id != "house" || d.Address.District < fromDistrict || d.Address.District > toDistrict) continue;
                if (inTown != null && !inTown(d)) continue;
                candidates.Add(i);
            }
            if (candidates.Count == 0) return false;
            int start = rng.NextInt(candidates.Count), units = MinUnits + rng.NextInt(MaxUnits - MinUnits + 1);
            var type = content.DestinationType("apartment");
            for (int k = 0; k < candidates.Count; k++)
            {
                int index = candidates[(start + k) % candidates.Count];
                var house = destinations[index];
                var lot = house.Lot;
                // East first, else west: the two tiles of the gap to the neighbour must be open grass the whole lot deep.
                int x0 = Open(cols, tiles, lot.X + lot.W, lot.Y, lot.H) ? lot.X : Open(cols, tiles, lot.X - 2, lot.Y, lot.H) ? lot.X - 2 : -1;
                if (x0 < 0) continue;
                bool north = house.Building.Y == lot.Y;
                var wide = new TileRect(x0, lot.Y, LotWidth, lot.H);
                // The block at the back of the lot, a tile in from each side; a row of lawn before it to the entrance.
                var block = new TileRect(x0, north ? lot.Y : lot.Y + 1, LotWidth - 1, 3);
                int d = house.Address.District;
                for (int z = wide.Y; z < wide.Y + wide.H; z++)
                    for (int x = wide.X; x < wide.X + wide.W; x++)
                    {
                        tiles[z * cols + x] = block.Contains(x, z) ? TileType.Building : TileType.Lot;
                        tileDistrict[z * cols + x] = (byte)d;
                    }
                destinations[index] = new DestinationDef
                {
                    Id = house.Id,
                    Type = type,
                    Address = house.Address,
                    Street = house.Street,
                    Lot = wide,
                    Building = block,
                    MailboxX = (x0 + LotWidth * 0.5f) * SimConstants.TileSize,
                    MailboxZ = house.MailboxZ,
                    Variant = house.Variant,
                    Units = units,
                };
                return true;
            }
            return false;
        }

        private static bool Open(int cols, TileType[] tiles, int x, int y, int h)
        {
            if (x < 0 || x + 2 > cols) return false;
            for (int z = y; z < y + h; z++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int i = z * cols + x + dx;
                    if (i < 0 || i >= tiles.Length || tiles[i] != TileType.Grass) return false;
                }
            return true;
        }
    }
}
