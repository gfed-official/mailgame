using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// The town of a Small Island from a seed (doc 02 §2–§3, §3.3–§3.7, §3.11–§3.12): a main
    /// street through the town past the Post Office, a back street to either side tied to it by
    /// cross streets every 12–16 tiles (some only to one side), houses on 4 × 4 lots along the
    /// frontages with their mailboxes at the verge, a beach along the south shore, the street
    /// names from streets.json, addresses numbered from the west end (odd on the north side, even
    /// on the south), and districts of 8–12 houses grown from the Post Office out, street segment
    /// by street segment. Every stage draws from a stream of its own, all of it in integer tiles,
    /// so the server and every client build the same town from the same seed and agree on its
    /// hash; a town that fails validation (a mailbox the Post Office cannot walk to, an address
    /// twice) is drawn again, at most eight times.
    /// </summary>
    public static class WorldGenerator
    {
        /// <summary>The map id of a generated Small Island town.</summary>
        public const string SmallIsland = "small_island";
        public const int Rows = 60;
        public const int MaxDistricts = 5;
        private const int MaxAttempts = 8;
        private const int LotSize = 4;
        private const int BeachRows = 6;
        /// <summary>Doc 02 §3.2: the harbour basin below the beach, inside the map — shallow rows, then deep ones.</summary>
        public const int ShallowRows = 3, DeepRows = 3;
        /// <summary>Doc 02 §3.5: the shift a Small Island's Business Dock opens.</summary>
        public const int DockShift = 4;
        /// <summary>How many oil seeps a Small Island has (design choice 116).</summary>
        public const int SeepCount = 3;

        /// <summary>True for a map id the generator makes rather than a file: the Small Island or the Large Island.</summary>
        public static bool Generates(string mapId) => mapId == SmallIsland || mapId == LargeIslandGenerator.LargeIsland;

        /// <summary>The map a server or a client plays on: a file's, or the seed's island of that archetype.</summary>
        public static WorldMap Resolve(ContentDb content, string mapId, uint seed) =>
            mapId == SmallIsland ? Generate(content, seed)
            : mapId == LargeIslandGenerator.LargeIsland ? LargeIslandGenerator.Generate(content, seed)
            : content.Map(mapId);

        public static WorldMap Generate(ContentDb content, uint seed)
        {
            string? last = null;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var map = TryGenerate(content, seed, attempt, out last);
                if (map != null) return map;
            }
            throw new InvalidOperationException($"seed {seed} makes no valid town: {last}");
        }

        private sealed class Segment
        {
            public StreetPlan Street = null!;
            public int X0, X1;
            public readonly List<Lot> Lots = new List<Lot>();
            public int District;
            public int Id;
        }

        private sealed class StreetPlan
        {
            public string Name = "";
            /// <summary>Row of its top tile; it is two rows wide and runs from column X0 to X1 (exclusive).</summary>
            public int Y, X0, X1;
        }

        private sealed class Lot
        {
            public TileRect Rect;
            public bool North;
            public int Number;
            public float MailboxX, MailboxZ;
        }

        private static WorldMap? TryGenerate(ContentDb content, uint seed, int attempt, out string? problem)
        {
            problem = null;
            string suffix = attempt == 0 ? "" : $"#{attempt}";
            var towns = new RngStream(seed, "towns" + suffix);
            var roads = new RngStream(seed, "roads" + suffix);
            var addresses = new RngStream(seed, "addresses" + suffix);

            // --- the town's frame: the main street, the Post Office on it, the back streets ---------
            int cols = 76 + 2 * towns.NextInt(7);
            int rows = Rows;
            int mainY = 26 + towns.NextInt(3);
            int northY = mainY - 14, southY = mainY + 16;
            int poX = cols / 2 - 6 + towns.NextInt(5);
            var poRect = new TileRect(poX, mainY - 7, 6, 6);

            // Cross streets every 12–16 tiles, clear of the Post Office; each reaches north, south or both.
            var cross = new List<(int X, bool North, bool South)>();
            for (int x = 8 + roads.NextInt(4); x < cols - 8; x += 12 + roads.NextInt(5))
            {
                if (x + 2 > poRect.X - 2 && x < poRect.X + poRect.W + 2) x = poRect.X + poRect.W + 3;
                if (x >= cols - 8) break;
                int which = roads.NextInt(10);
                cross.Add((x, which != 0 && which != 1, which != 2 && which != 3));
            }
            if (cross.Count < 2) { problem = "fewer than two cross streets"; return null; }
            // Both back streets must be reached: the first cross street goes north, the last south.
            cross[0] = (cross[0].X, true, cross[0].South);
            cross[cross.Count - 1] = (cross[cross.Count - 1].X, cross[cross.Count - 1].North, true);
            int backX0 = cross[0].X, backX1 = cross[cross.Count - 1].X + 2;

            // --- names (doc 02 §3.4: unique per map) -------------------------------------------------
            var pool = new List<string>(content.StreetNames);
            string Name()
            {
                int i = addresses.NextInt(pool.Count);
                string n = pool[i];
                pool.RemoveAt(i);
                return n;
            }
            if (pool.Count < 3 + cross.Count) { problem = "not enough street names"; return null; }
            var main = new StreetPlan { Name = Name(), Y = mainY, X0 = 0, X1 = cols };
            var north = new StreetPlan { Name = Name(), Y = northY, X0 = backX0, X1 = backX1 };
            var south = new StreetPlan { Name = Name(), Y = southY, X0 = backX0, X1 = backX1 };
            var crossNames = new List<string>();
            foreach (var _ in cross) crossNames.Add(Name());

            // --- lots along the frontages (doc 02 §3.4–§3.5) -----------------------------------------
            bool Clear(int x0, int x1)
            {
                foreach (var c in cross)
                    if (x1 > c.X - 1 && x0 < c.X + 3) return false;
                return true;
            }
            var segments = new List<Segment>();
            void Frontage(StreetPlan street, bool hasNorth, bool hasSouth)
            {
                // Split at the cross streets: a segment runs from one crossing to the next.
                var cuts = new List<int> { street.X0 };
                foreach (var c in cross)
                    if (c.X > street.X0 && c.X + 2 < street.X1) cuts.Add(c.X + 1);
                cuts.Add(street.X1);
                for (int i = 0; i + 1 < cuts.Count; i++)
                    segments.Add(new Segment { Street = street, X0 = cuts[i], X1 = cuts[i + 1] });
                foreach (bool side in new[] { true, false })
                {
                    if (side ? !hasNorth : !hasSouth) continue;
                    int y = side ? street.Y - 1 - LotSize : street.Y + 3;
                    for (int x = street.X0 + 2 + towns.NextInt(2); x + LotSize <= street.X1 - 2; )
                    {
                        var rect = new TileRect(x, y, LotSize, LotSize);
                        bool clear = Clear(x, x + LotSize)
                            && !(street == main && side && x + LotSize > poRect.X - 1 && x < poRect.X + poRect.W + 1);
                        if (!clear) { x++; continue; }
                        // A gap of two or three tiles between neighbours: room for a tree or a walk round.
                        var lot = new Lot { Rect = rect, North = side };
                        lot.MailboxX = (x + 2) * SimConstants.TileSize;
                        lot.MailboxZ = side ? (y + LotSize) * SimConstants.TileSize + 1f : (y - 1) * SimConstants.TileSize + 1f;
                        foreach (var seg in segments)
                            if (seg.Street == street && x + 2 >= seg.X0 && x + 2 < seg.X1) { seg.Lots.Add(lot); break; }
                        x += LotSize + 2 + towns.NextInt(2);
                    }
                }
            }
            Frontage(main, hasNorth: true, hasSouth: true);
            Frontage(north, hasNorth: true, hasSouth: true);
            Frontage(south, hasNorth: true, hasSouth: true);
            segments.RemoveAll(s => s.Lots.Count == 0 && s.Street != main);

            // --- addresses (doc 02 §3.6): from the west end out, odd north, even south --------------
            foreach (var street in new[] { main, north, south })
            {
                var northLots = new List<Lot>();
                var southLots = new List<Lot>();
                foreach (var seg in segments)
                    if (seg.Street == street)
                        foreach (var lot in seg.Lots) (lot.North ? northLots : southLots).Add(lot);
                northLots.Sort((a, b) => a.Rect.X.CompareTo(b.Rect.X));
                southLots.Sort((a, b) => a.Rect.X.CompareTo(b.Rect.X));
                for (int i = 0; i < northLots.Count; i++) northLots[i].Number = 2 * i + 1;
                for (int i = 0; i < southLots.Count; i++) southLots[i].Number = 2 * i + 2;
            }

            // --- districts (doc 02 §3.7): grown from the Post Office, whole segments at a time -------
            float padX = (poRect.X * 2 + 10), padZ = (poRect.Y * 2 + 9);
            segments.Sort((a, b) =>
            {
                int c = SegmentDistance(a, padX, padZ).CompareTo(SegmentDistance(b, padX, padZ));
                return c != 0 ? c : a.X0.CompareTo(b.X0) != 0 ? a.X0.CompareTo(b.X0) : a.Street.Y.CompareTo(b.Street.Y);
            });
            int district = 1, inDistrict = 0, want = 8 + towns.NextInt(5);
            foreach (var seg in segments)
            {
                if (inDistrict >= want && district < MaxDistricts && seg.Lots.Count > 0)
                {
                    district++;
                    inDistrict = 0;
                    want = 8 + towns.NextInt(5);
                }
                seg.District = district;
                inDistrict += seg.Lots.Count;
            }
            int districtCount = district;

            // --- the island round the town (doc 02 §3.1–§3.2) ----------------------------------------
            var terrain = IslandTerrain.Generate(seed, suffix, cols, rows, BeachRows, poRect.X + 5, poRect.Y + 4, out problem);
            if (terrain == null) return null;

            // --- the tile grid --------------------------------------------------------------------
            // The land's rows, and under them the basin's.
            int allRows = rows + ShallowRows + DeepRows;
            var tiles = new TileType[cols * allRows];
            var tileDistrict = new byte[cols * allRows];
            void Fill(TileRect r, TileType type, int d)
            {
                for (int y = Math.Max(0, r.Y); y < Math.Min(rows, r.Y + r.H); y++)
                    for (int x = Math.Max(0, r.X); x < Math.Min(cols, r.X + r.W); x++)
                    {
                        tiles[y * cols + x] = type;
                        if (d > 0) tileDistrict[y * cols + x] = (byte)d;
                    }
            }
            Fill(new TileRect(0, rows - BeachRows, cols, BeachRows), TileType.Beach, 0);
            // Shelving water under the west half of the beach (for piers), deep along the rest of the quay (for a port).
            for (int i = cols * rows; i < tiles.Length; i++)
                tiles[i] = i < cols * (rows + ShallowRows) && i % cols < cols / 2 ? TileType.Water : TileType.DeepWater;

            var districts = new List<DistrictDef>();
            for (int d = 1; d <= districtCount; d++)
                districts.Add(new DistrictDef { Id = d, Name = d == 1 ? "Post Office District" : $"District {d}", UnlockShift = d });

            var streets = new List<StreetDef>();
            // The main street's segments first: the first street is the one the raiders come in by (RaidRules.Gate).
            int nextStreet = 1;
            foreach (var seg in segments)
            {
                if (seg.Street != main) continue;
                seg.Id = nextStreet++;
                var rect = new TileRect(seg.X0, seg.Street.Y, seg.X1 - seg.X0, 2);
                streets.Add(new StreetDef { Id = seg.Id, Name = seg.Street.Name, District = seg.District, Rect = rect });
            }
            foreach (var seg in segments)
            {
                if (seg.Street == main) continue;
                seg.Id = nextStreet++;
                var rect = new TileRect(seg.X0, seg.Street.Y, seg.X1 - seg.X0, 2);
                streets.Add(new StreetDef { Id = seg.Id, Name = seg.Street.Name, District = seg.District, Rect = rect });
            }
            // The back streets run on to the cross streets at either end.
            for (int i = 0; i < cross.Count; i++)
            {
                var c = cross[i];
                int y0 = c.North ? northY : mainY, y1 = c.South ? southY + 2 : mainY + 2;
                int d = NearestDistrict(segments, c.X + 1, (y0 + y1) / 2);
                streets.Add(new StreetDef { Id = nextStreet++, Name = crossNames[i], District = d, Rect = new TileRect(c.X, y0, 2, y1 - y0) });
            }
            foreach (var s in streets) Fill(s.Rect, TileType.Street, s.District);
            // Back streets that end short of a cross street are tied to it.
            Fill(new TileRect(backX0, northY, backX1 - backX0, 2), TileType.Street, 0);
            Fill(new TileRect(backX0, southY, backX1 - backX0, 2), TileType.Street, 0);

            // The Post Office (doc 02 §3.11): a 4 × 6 building with its yard to the east, on the main street.
            var solid = new TileRect(poRect.X, poRect.Y, 4, 6);
            Fill(poRect, TileType.Yard, 0);
            Fill(solid, TileType.Building, 0);
            float ox = poRect.X * SimConstants.TileSize, oz = poRect.Y * SimConstants.TileSize;
            var po = new PostOfficeDef
            {
                Rect = poRect,
                Solid = new[] { solid },
                IntakeX = ox + 8f,
                IntakeZ = oz + 6f,
                HasShop = true,
                ShopX = ox + 8f,
                ShopZ = oz + 10f,
                SpawnPadX = ox + 10f,
                SpawnPadZ = oz + 9f,
                // Down the main street, east, the way the round goes and the raiders come.
                SpawnYaw = MathF.Atan2(-26f, -((mainY + 1) * SimConstants.TileSize - (oz + 9f))),
            };

            var house = content.DestinationType("house");
            var destinations = new List<DestinationDef>();
            int variant = towns.NextInt(12);
            foreach (var seg in segments)
            {
                var street = streets.Find(s => s.Id == seg.Id)!;
                seg.Lots.Sort((a, b) => a.North != b.North ? (a.North ? -1 : 1) : a.Rect.X.CompareTo(b.Rect.X));
                foreach (var lot in seg.Lots)
                {
                    // The house at the back of its lot, three tiles wide and two deep; the lawn before it.
                    var building = lot.North
                        ? new TileRect(lot.Rect.X, lot.Rect.Y, 3, 2)
                        : new TileRect(lot.Rect.X, lot.Rect.Y + 2, 3, 2);
                    Fill(lot.Rect, TileType.Lot, seg.District);
                    Fill(building, TileType.Building, seg.District);
                    // Neighbours never share a colour.
                    variant = (variant + 1 + towns.NextInt(4)) % 12;
                    destinations.Add(new DestinationDef
                    {
                        Id = (ushort)destinations.Count,
                        Type = house,
                        Address = new AddressId(seg.District, seg.Id, lot.Number),
                        Street = street,
                        Lot = lot.Rect,
                        Building = building,
                        MailboxX = lot.MailboxX,
                        MailboxZ = lot.MailboxZ,
                        Variant = variant,
                    });
                }
            }

            // Doc 02 §3.5: the medium town's Apartment Complex, in a district of shift 4 or 5.
            Apartments.Place(content, seed, suffix, cols, tiles, tileDistrict, destinations, 4, 5);

            // Doc 02 §3.5: "Small Island gets one [Business Dock] from shift 4", on a coastal lot with
            // deep water — the beach's east end, the farthest from the Post Office of the deep quay.
            var dockType = content.DestinationType("business_dock");
            for (int x = cols - 6; x >= cols / 2; x--)
            {
                var lot = new TileRect(x, rows - BeachRows, 4, BeachRows);
                bool free = true;
                for (int z = lot.Y; z < lot.Y + lot.H && free; z++)
                    for (int tx = lot.X; tx < lot.X + lot.W && free; tx++)
                        if (tiles[z * cols + tx] != TileType.Beach) free = false;
                for (int tx = lot.X; tx < lot.X + lot.W && free; tx++)
                    if (tiles[rows * cols + tx] != TileType.DeepWater) free = false;
                if (!free) continue;
                float mx = x * SimConstants.TileSize + 1f, mz = rows * SimConstants.TileSize - 1f;
                StreetDef? near = null;
                float best = float.MaxValue;
                // On a street open by the dock's shift, so that it opens with it (districts open one a shift).
                foreach (var st in streets)
                {
                    if (st.District > DockShift) continue;
                    float sx = Math.Clamp(mx, st.Rect.X * SimConstants.TileSize, (st.Rect.X + st.Rect.W) * SimConstants.TileSize);
                    float sz = Math.Clamp(mz, st.Rect.Y * SimConstants.TileSize, (st.Rect.Y + st.Rect.H) * SimConstants.TileSize);
                    float d = (sx - mx) * (sx - mx) + (sz - mz) * (sz - mz);
                    if (d < best) { best = d; near = st; }
                }
                if (near == null) break;
                // Past the last number of the whole street (all its segments): a number of its segment's alone could be a
                // house's further along ("14 Heron Bend" twice on seed 15).
                int number = 1;
                foreach (var d in destinations)
                    if (d.Street.Name == near.Name) number = Math.Max(number, d.Address.Number + 1);
                var building = new TileRect(x + 1, lot.Y, 3, 3);
                Fill(lot, TileType.Lot, near.District);
                Fill(building, TileType.Building, near.District);
                destinations.Add(new DestinationDef
                {
                    Id = (ushort)destinations.Count,
                    Type = dockType,
                    Address = new AddressId(near.District, near.Id, number),
                    Street = near,
                    Lot = lot,
                    Building = building,
                    MailboxX = mx,
                    MailboxZ = mz,
                    UnlockShift = DockShift,
                    LaidOn = TileType.Beach,
                });
                break;
            }

            // Oil seeps (doc 02 §3.9; design choice 116): a Small Island has no marsh, so a few seeps
            // on open grass out of the town, from a stream of their own, far from the Post Office and
            // from one another, clear of streets and lots.
            var oil = new RngStream(seed, "oil" + suffix);
            var seeps = new List<(int X, int Z)>();
            int poCx = poRect.X + 3, poCz = poRect.Y + 3;
            for (int tries = 0; tries < 400 && seeps.Count < SeepCount; tries++)
            {
                int sx = 2 + oil.NextInt(cols - 4), sz = 2 + oil.NextInt(rows - BeachRows - 4);
                if (Math.Abs(sx - poCx) + Math.Abs(sz - poCz) < 14) continue;
                bool open = true;
                for (int dz = -2; dz <= 2 && open; dz++)
                    for (int dx = -2; dx <= 2 && open; dx++)
                        if (tiles[(sz + dz) * cols + sx + dx] != TileType.Grass) open = false;
                foreach (var (qx, qz) in seeps) if (Math.Abs(qx - sx) + Math.Abs(qz - sz) < 10) open = false;
                if (open) seeps.Add((sx, sz));
            }

            var chests = new List<MapChestDef>
            {
                // In the yard's north-east corner: the tile north of the Intake is the outfeed's (doc 04 §2.6).
                new MapChestDef { Id = 0, Container = content.Container("chest"), X = ox + 11f, Z = oz + 0.8f, Yaw = -MathF.PI * 0.5f },
            };

            var map = new WorldMap
            {
                Id = SmallIsland,
                Name = $"Small Island #{seed}",
                Cols = cols,
                Rows = allRows,
                Tiles = tiles,
                TileDistrict = tileDistrict,
                Districts = districts,
                Streets = streets,
                Destinations = destinations,
                OilSeeps = seeps,
                PostOffice = po,
                Chests = chests,
                Terrain = terrain,
            };
            map.ComputeHash();
            problem = Validate(map);
            return problem == null ? map : null;
        }

        private static float SegmentDistance(Segment s, float x, float z)
        {
            float cx = (s.X0 + s.X1) * 0.5f * SimConstants.TileSize, cz = (s.Street.Y + 1) * SimConstants.TileSize;
            // Along the streets, not as the crow flies: across the blocks is the long way round.
            return MathF.Abs(cx - x) + MathF.Abs(cz - z) * 1.5f;
        }

        private static int NearestDistrict(List<Segment> segments, int tx, int ty)
        {
            int best = 1;
            float bestD = float.MaxValue;
            foreach (var s in segments)
            {
                float d = MathF.Abs((s.X0 + s.X1) * 0.5f - tx) + MathF.Abs(s.Street.Y - ty);
                if (d < bestD) { bestD = d; best = s.District; }
            }
            return best;
        }

        /// <summary>How many of each node a town must have (doc 02 §3.12 "resource minimums").</summary>
        public static readonly (PropKind Kind, int Least)[] ResourceMinimums =
        {
            (PropKind.Tree, 30), (PropKind.Boulder, 15), (PropKind.Bush, 60), (PropKind.SandPile, 20),
        };

        /// <summary>
        /// Doc 02 §3.12: every mailbox can be walked to from the Post Office, every address is
        /// unique, there is room to build by the Post Office, and every district has houses.
        /// Null when the town is good, else what is wrong with it.
        /// </summary>
        public static string? Validate(WorldMap map)
        {
            var seen = new HashSet<AddressId>();
            foreach (var d in map.Destinations)
                if (!seen.Add(d.Address)) return $"address {d.Address} twice";
            var po = map.PostOffice;
            var path = new List<(float X, float Z)>();
            var scratch = new TilePathfinder.Scratch();
            // Where a walk can start: the pad, and the far end of every ferry lane whose near end can be walked to from one
            // of them (doc 02 §3.12 "by walking, or by walking plus a marked ferry lane").
            var starts = new List<(float X, float Z)> { (po.SpawnPadX, po.SpawnPadZ) };
            var crossed = new bool[map.FerryLanes.Count];
            for (bool grew = true; grew; )
            {
                grew = false;
                for (int i = 0; i < map.FerryLanes.Count; i++)
                {
                    if (crossed[i]) continue;
                    var lane = map.FerryLanes[i];
                    foreach (var (sx, sz) in starts)
                    {
                        if (TilePathfinder.Find(map, sx, sz, lane.AX, lane.AZ, path, scratch)) { starts.Add((lane.BX, lane.BZ)); crossed[i] = grew = true; break; }
                        if (TilePathfinder.Find(map, sx, sz, lane.BX, lane.BZ, path, scratch)) { starts.Add((lane.AX, lane.AZ)); crossed[i] = grew = true; break; }
                    }
                }
            }
            foreach (var d in map.Destinations)
            {
                int tx = (int)MathF.Floor(d.MailboxX / SimConstants.TileSize), tz = (int)MathF.Floor(d.MailboxZ / SimConstants.TileSize);
                if (map.IsSolid(tx, tz)) return $"the mailbox of {map.FormatAddress(d.Address)} is in a wall";
                bool reached = false;
                foreach (var (sx, sz) in starts)
                    if (TilePathfinder.Find(map, sx, sz, d.MailboxX, d.MailboxZ, path, scratch)) { reached = true; break; }
                if (!reached)
                    return map.FerryLanes.Count == 0 ? $"no walk from the Post Office to {map.FormatAddress(d.Address)}" : $"no walk nor ferry lane from the Post Office to {map.FormatAddress(d.Address)}";
            }
            foreach (var district in map.Districts)
            {
                bool any = false;
                foreach (var d in map.Destinations) any |= d.Address.District == district.Id;
                if (!any) return $"district {district.Id} has no houses";
            }
            // Room for a factory: open ground within 20 tiles of the Post Office.
            int open = 0;
            int ptx = (int)(po.SpawnPadX / SimConstants.TileSize), ptz = (int)(po.SpawnPadZ / SimConstants.TileSize);
            for (int z = ptz - 20; z <= ptz + 20; z++)
                for (int x = ptx - 20; x <= ptx + 20; x++)
                    if (map.InBounds(x, z) && (map.GetTile(x, z) == TileType.Grass || map.GetTile(x, z) == TileType.Beach) && !map.IsNavBlocked(x, z)) open++;
            if (open < 400) return $"only {open} open tiles by the Post Office";
            // The streets one network (doc 02 §3.8) — one a shore, where ferry lanes join them —, every mailbox at the side of one.
            if (map.Routes.Components() > 1 + map.FerryLanes.Count) return "the streets are not one network";
            foreach (var d in map.Destinations)
            {
                // A dock's zone is on the quay, reached over the beach and from the water.
                if (d.IsDock) continue;
                map.Routes.NearestEdge(d.MailboxX, d.MailboxZ, out _, out _, out float off);
                if (off > 6f) return $"the mailbox of {map.FormatAddress(d.Address)} is {off:F1} m from a street";
            }
            // Resource minimums: wood, stone and ore, fiber and berries, sand enough to build with.
            foreach (var (kind, least) in ResourceMinimums)
            {
                int count = 0;
                foreach (var p in map.Props) if (p.Kind == kind) count++;
                if (count < least) return $"only {count} {kind} nodes (at least {least})";
            }
            return null;
        }
    }
}
