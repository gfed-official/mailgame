using System;
using System.Collections.Generic;
using System.Text.Json;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>Parses and validates a hand-authored map file (content/maps/*.json).</summary>
    internal static class MapLoader
    {
        public static WorldMap? Load(JsonCtx ctx, string text,
            IReadOnlyDictionary<string, DestinationTypeDef> destinationTypes, ICollection<string> streetNames)
        {
            using var doc = J.Parse(ctx, text);
            if (doc == null) return null;
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { ctx.Error("map root must be an object"); return null; }

            int errorsBefore = ctx.Errors.Count;
            string id = J.Str(ctx, root, "id");
            string name = J.Str(ctx, root, "name", id);
            int[] size = J.IntArray(ctx, root, "sizeTiles", 2);
            int cols = size[0], rows = size[1];
            if (cols <= 0 || rows <= 0 || cols > 2048 || rows > 2048)
            {
                ctx.Error("sizeTiles must be positive and at most 2048");
                return null;
            }

            var tiles = new TileType[cols * rows];
            var tileDistrict = new byte[cols * rows];

            var districts = new List<DistrictDef>();
            if (root.TryGetProperty("districts", out var districtsEl) && districtsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in districtsEl.EnumerateArray())
                {
                    var d = new DistrictDef
                    {
                        Id = J.Int(ctx, e, "id"),
                        Name = J.Str(ctx, e, "name", ""),
                        UnlockShift = J.Int(ctx, e, "unlockShift", 1),
                    };
                    if (d.Id < 1 || d.Id > 255) ctx.Error($"district id {d.Id} out of range 1..255");
                    if (districts.Exists(x => x.Id == d.Id)) ctx.Error($"duplicate district id {d.Id}");
                    districts.Add(d);
                }
            }
            else ctx.Error("missing 'districts' array");

            bool RectOk(TileRect r, string what)
            {
                if (r.W <= 0 || r.H <= 0 || r.X < 0 || r.Y < 0 || r.X + r.W > cols || r.Y + r.H > rows)
                {
                    ctx.Error($"{what}: rect [{r.X},{r.Y},{r.W},{r.H}] is outside the map");
                    return false;
                }
                return true;
            }

            void Fill(TileRect r, TileType type, int district)
            {
                for (int y = r.Y; y < r.Y + r.H; y++)
                    for (int x = r.X; x < r.X + r.W; x++)
                    {
                        tiles[y * cols + x] = type;
                        if (district > 0) tileDistrict[y * cols + x] = (byte)district;
                    }
            }

            TileRect ReadRect(JsonElement obj, string field)
            {
                int[] r = J.IntArray(ctx, obj, field, 4);
                return new TileRect(r[0], r[1], r[2], r[3]);
            }

            var streets = new List<StreetDef>();
            if (root.TryGetProperty("streets", out var streetsEl) && streetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in streetsEl.EnumerateArray())
                {
                    var s = new StreetDef
                    {
                        Id = J.Int(ctx, e, "id"),
                        Name = J.Str(ctx, e, "name"),
                        District = J.Int(ctx, e, "district"),
                        Rect = ReadRect(e, "rect"),
                    };
                    if (s.Id < 1 || s.Id > 255) ctx.Error($"street id {s.Id} out of range 1..255");
                    if (streets.Exists(x => x.Id == s.Id)) ctx.Error($"duplicate street id {s.Id}");
                    if (streets.Exists(x => x.Name == s.Name)) ctx.Error($"duplicate street name '{s.Name}'");
                    if (!districts.Exists(d => d.Id == s.District)) ctx.Error($"street '{s.Name}' references unknown district {s.District}");
                    if (streetNames.Count > 0 && !streetNames.Contains(s.Name)) ctx.Error($"street name '{s.Name}' is not in streets.json");
                    if (RectOk(s.Rect, $"street '{s.Name}'")) Fill(s.Rect, TileType.Street, s.District);
                    streets.Add(s);
                }
            }
            else ctx.Error("missing 'streets' array");

            var po = new PostOfficeDef();
            if (root.TryGetProperty("postOffice", out var poEl) && poEl.ValueKind == JsonValueKind.Object)
            {
                var rect = ReadRect(poEl, "rect");
                var solid = new List<TileRect>();
                if (RectOk(rect, "postOffice")) Fill(rect, TileType.Yard, 0);
                if (poEl.TryGetProperty("solid", out var solidEl) && solidEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in solidEl.EnumerateArray())
                    {
                        double[] r = J.NumArrayOf(ctx, e, "postOffice.solid[]", 4);
                        var sr = new TileRect((int)r[0], (int)r[1], (int)r[2], (int)r[3]);
                        if (RectOk(sr, "postOffice.solid")) Fill(sr, TileType.Building, 0);
                        solid.Add(sr);
                    }
                }
                double[] intake = J.NumArray(ctx, poEl, "intake", 2);
                double[] pad = J.NumArray(ctx, poEl, "spawnPad", 2);
                po = new PostOfficeDef
                {
                    Rect = rect,
                    Solid = solid,
                    IntakeX = (float)intake[0],
                    IntakeZ = (float)intake[1],
                    SpawnPadX = (float)pad[0],
                    SpawnPadZ = (float)pad[1],
                };
            }
            else ctx.Error("missing 'postOffice' object");

            var destinations = new List<DestinationDef>();
            var seenAddresses = new HashSet<AddressId>();
            if (root.TryGetProperty("destinations", out var destEl) && destEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in destEl.EnumerateArray())
                {
                    string typeId = J.Str(ctx, e, "type");
                    int streetId = J.Int(ctx, e, "street");
                    int number = J.Int(ctx, e, "number");
                    int unit = J.Int(ctx, e, "unit", 0);
                    var lot = ReadRect(e, "lot");
                    var building = ReadRect(e, "building");
                    double[] mailbox = J.NumArray(ctx, e, "mailbox", 2);

                    if (!destinationTypes.TryGetValue(typeId, out var type))
                    {
                        ctx.Error($"destination {number}: unknown destination type '{typeId}'");
                        continue;
                    }
                    var street = streets.Find(s => s.Id == streetId);
                    if (street == null)
                    {
                        ctx.Error($"destination {number}: unknown street {streetId}");
                        continue;
                    }
                    if (number < 1 || number > 65535) ctx.Error($"destination number {number} out of range");
                    var address = new AddressId(street.District, street.Id, number, unit);
                    if (!seenAddresses.Add(address)) ctx.Error($"duplicate address {address}");

                    if (RectOk(lot, $"destination {address} lot")) Fill(lot, TileType.Lot, street.District);
                    if (RectOk(building, $"destination {address} building")) Fill(building, TileType.Building, street.District);

                    destinations.Add(new DestinationDef
                    {
                        Id = (ushort)destinations.Count,
                        Type = type,
                        Address = address,
                        Street = street,
                        Lot = lot,
                        Building = building,
                        MailboxX = (float)mailbox[0],
                        MailboxZ = (float)mailbox[1],
                        Variant = J.Int(ctx, e, "variant", 0),
                    });
                }
            }
            else ctx.Error("missing 'destinations' array");

            var map = new WorldMap
            {
                Id = id,
                Name = name,
                Cols = cols,
                Rows = rows,
                Tiles = tiles,
                TileDistrict = tileDistrict,
                Districts = districts,
                Streets = streets,
                Destinations = destinations,
                PostOffice = po,
            };

            // Interaction points must be standable: inside the map and not inside a solid tile.
            void CheckPoint(float x, float z, string what)
            {
                int tx = (int)MathF.Floor(x / SimConstants.TileSize), tz = (int)MathF.Floor(z / SimConstants.TileSize);
                if (!map.InBounds(tx, tz)) ctx.Error($"{what} ({x}, {z}) is outside the map");
            }
            CheckPoint(po.IntakeX, po.IntakeZ, "postOffice.intake");
            CheckPoint(po.SpawnPadX, po.SpawnPadZ, "postOffice.spawnPad");
            {
                int tx = (int)MathF.Floor(po.SpawnPadX / SimConstants.TileSize), tz = (int)MathF.Floor(po.SpawnPadZ / SimConstants.TileSize);
                if (map.IsSolid(tx, tz)) ctx.Error("postOffice.spawnPad is inside a solid tile");
            }
            foreach (var d in destinations)
            {
                CheckPoint(d.MailboxX, d.MailboxZ, $"mailbox of {d.Address}");
                int tx = (int)MathF.Floor(d.MailboxX / SimConstants.TileSize), tz = (int)MathF.Floor(d.MailboxZ / SimConstants.TileSize);
                if (map.IsSolid(tx, tz)) ctx.Error($"mailbox of {d.Address} is inside a solid tile");
            }
            if (destinations.Count == 0) ctx.Error("map has no destinations");

            if (ctx.Errors.Count != errorsBefore) return null;
            map.ComputeHash();
            return map;
        }
    }
}
