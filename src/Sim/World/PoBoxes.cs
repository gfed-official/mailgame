using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// Doc 02 §3.5 PO Box Bank: "inside PO district; 8–16 boxes; accepts letters only" (doc 03 §2.1: postcards and
    /// letters, the box number matching; design choice 171). A bank of boxes at the street in front of the Post Office,
    /// one destination with a box a unit, numbered past the street's last house like a dock. From the "poboxes" stream.
    /// </summary>
    public static class PoBoxes
    {
        public const int MinBoxes = 8, MaxBoxes = 16;

        /// <summary>Adds the bank to <paramref name="destinations"/> on the street the Post Office (<paramref name="po"/>) faces at <paramref name="streetY"/>.</summary>
        public static DestinationDef? Place(ContentDb content, uint seed, string suffix, List<DestinationDef> destinations, IReadOnlyList<StreetDef> streets, TileRect po, int streetY,
            int cols, byte[] tileDistrict)
        {
            var rng = new RngStream(seed, "poboxes" + suffix);
            int boxes = MinBoxes + rng.NextInt(MaxBoxes - MinBoxes + 1);
            // Before the hall's front, a tile in from its west end, on the verge row between it and the street.
            int tx = po.X + 1, tz = streetY - 1;
            StreetDef? street = null;
            foreach (var s in streets)
                if (s.Rect.Contains(tx, streetY)) { street = s; break; }
            if (street == null) return null;
            int number = 1;
            foreach (var d in destinations)
                if (d.Street.Name == street.Name) number = Math.Max(number, d.Address.Number + 1);
            var bank = new DestinationDef
            {
                Id = (ushort)destinations.Count,
                Type = content.DestinationType("pobox"),
                Address = new AddressId(street.District, street.Id, number),
                Street = street,
                Lot = new TileRect(po.X, tz, 4, 1),
                // The Post Office's hall stands behind it: its front faces the street.
                Building = new TileRect(po.X, po.Y, 4, po.H),
                MailboxX = (tx + 1) * SimConstants.TileSize,
                MailboxZ = tz * SimConstants.TileSize + 1f,
                Variant = 0,
                Units = boxes,
            };
            // Its verge is the Post Office's district (doc 02 §3.5 "inside PO district").
            for (int x = bank.Lot.X; x < bank.Lot.X + bank.Lot.W; x++) tileDistrict[tz * cols + x] = (byte)street.District;
            destinations.Add(bank);
            return bank;
        }
    }
}
