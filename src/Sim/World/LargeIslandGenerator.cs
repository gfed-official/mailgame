using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// The Large Island from a seed (doc 02 §1, §3.1–§3.8, §3.11–§3.12; design choice 169): two or three towns along
    /// the south coast of an island 1000 m across, west of one another. The Post Office's town, a medium one of two
    /// streets, lies east, where its main street leaves the map (the raiders' first way in, as on the Small Island);
    /// an inlet of the sea parts it from the next town west — a large town of three streets, or a medium one of two
    /// with a small one of a single street beyond a long road further west. Either the inlet runs right through the
    /// island, and a ferry lane is marked across it between the two nearest beaches, or it ends short of the north
    /// and a dirt road leads round its head. The Post Office's town holds districts 1 and 2 (10–12 houses nearest the
    /// Post Office, then the rest); the other towns districts 3–5 in distance order, so the crossing is in play from
    /// shift 3, and the run ends near 60–70 houses. Each of the first two towns has a Business Dock on its beach. The
    /// same streams and integer tiles as the Small Island, so every machine builds the same island; a draw that fails
    /// validation is made again, at most eight times.
    /// </summary>
    public static class LargeIslandGenerator
    {
        public const string LargeIsland = "large_island";
        public const int Rows = WorldGenerator.Rows;
        /// <summary>The island's tiles a side: 1000 m (doc 02 §1).</summary>
        public const int TerrainSize = 500;
        /// <summary>Grass between a town's last lot and its bank, and the bank's beach, in tiles.</summary>
        public const int ShoreGap = 3, BankWidth = 2;
        /// <summary>Doc 01 §2.1 "end nearer 60–70": the houses a Large Island must have, and each of its districts.</summary>
        public const int MinHouses = 56, MaxHouses = 76, MinDistrictHouses = 8, MaxDistrictHouses = 18;
        private const int BeachRows = 6;
        private const int LotSize = 4;
        private const int MaxAttempts = 8;
        private const int Districts = 5;

        public static WorldMap Generate(ContentDb content, uint seed)
        {
            string? last = null;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var map = TryGenerate(content, seed, attempt, out last);
                if (map != null) return map;
            }
            throw new InvalidOperationException($"seed {seed} makes no valid Large Island: {last}");
        }

        private sealed class StreetPlan
        {
            public string Name = "";
            /// <summary>Row of its top tile; two rows wide, from column X0 to X1 (exclusive).</summary>
            public int Y, X0, X1;
        }

        private sealed class Lot
        {
            public TileRect Rect;
            public bool North;
            public int Number;
            public float MailboxX, MailboxZ;
        }

        private sealed class Segment
        {
            public StreetPlan Street = null!;
            public Town Town = null!;
            public int X0, X1;
            public readonly List<Lot> Lots = new List<Lot>();
            public int District;
            public int Id;
            /// <summary>A main street's or a road's: kept with no lots.</summary>
            public bool Keep;
        }

        private sealed class Town
        {
            /// <summary>The columns its lots and cross streets lie in.</summary>
            public int X0, X1;
            /// <summary>Its main street's extent: on to the bank, the map's edge or the road.</summary>
            public int MainX0, MainX1;
            public bool North, South;
            public TileRect? Po;
            public StreetPlan Main = null!;
            public StreetPlan? NorthStreet, SouthStreet;
            public readonly List<(int X, bool North, bool South)> Cross = new List<(int, bool, bool)>();
            public readonly List<string> CrossNames = new List<string>();
            public readonly List<Segment> Segments = new List<Segment>();
            public int FirstDistrict, DistrictCount;
            /// <summary>Where its districts grow from, metres: the Post Office's pad, or where the way from it arrives.</summary>
            public float EntryX, EntryZ;
            public int BackX0 => Cross[0].X;
            public int BackX1 => Cross[Cross.Count - 1].X + 2;
            public int Houses
            {
                get
                {
                    int n = 0;
                    foreach (var s in Segments) n += s.Lots.Count;
                    return n;
                }
            }
        }

        private static WorldMap? TryGenerate(ContentDb content, uint seed, int attempt, out string? problem)
        {
            problem = null;
            string suffix = attempt == 0 ? "" : $"#{attempt}";
            var towns = new RngStream(seed, "towns" + suffix);
            var roads = new RngStream(seed, "roads" + suffix);
            var addresses = new RngStream(seed, "addresses" + suffix);
            const float tile = SimConstants.TileSize;

            // --- the frame: the towns west to east, the inlet between the last two ------------------------
            bool three = towns.Chance(500);
            bool through = towns.Chance(500);
            int mainY = 26 + towns.NextInt(3);
            int northY = mainY - 14, southY = mainY + 16;
            int wA = 66 + 2 * towns.NextInt(5);
            int wB = three ? 64 + 2 * towns.NextInt(5) : 74 + 2 * towns.NextInt(5);
            int wC = three ? 46 + 2 * towns.NextInt(4) : 0;
            int road = three ? 40 + towns.NextInt(9) : 0;
            int wI = 12 + 2 * towns.NextInt(3);
            int headY = through ? 0 : 8 + towns.NextInt(4);

            Town? c = null;
            int x = 0;
            if (three)
            {
                c = new Town { X0 = 0, X1 = wC, MainX0 = 0, MainX1 = wC };
                x = wC + road;
            }
            var b = new Town { X0 = x, X1 = x + wB, MainX0 = three ? x : 0, North = true, South = !three };
            int iX0 = b.X1 + ShoreGap + BankWidth, iX1 = iX0 + wI;
            b.MainX1 = iX0;
            var a = new Town { X0 = iX1 + BankWidth + ShoreGap, North = true, MainX0 = iX1 };
            a.X1 = a.X0 + wA;
            a.MainX1 = a.X1;
            int cols = a.X1;
            int rows = Rows;
            int poX = a.X0 + wA / 2 - 6 + towns.NextInt(5);
            var poRect = new TileRect(poX, mainY - 7, 6, 6);
            a.Po = poRect;
            // From the PO town out: the Post Office's, then west.
            var all = new List<Town> { a, b };
            if (c != null) all.Add(c);

            // --- cross streets (doc 02 §3.4: every 10–14 tiles; here 12–16, as the Small Island's) ---------
            foreach (var t in all)
            {
                if (!t.North && !t.South) continue;
                for (int cx = t.X0 + 8 + roads.NextInt(4); cx < t.X1 - 8; cx += 12 + roads.NextInt(5))
                {
                    if (t.Po is { } po && cx + 2 > po.X - 2 && cx < po.X + po.W + 2) cx = po.X + po.W + 3;
                    if (cx >= t.X1 - 8) break;
                    int which = roads.NextInt(10);
                    bool n = t.North && (!t.South || (which != 0 && which != 1));
                    bool s = t.South && (!t.North || (which != 2 && which != 3));
                    t.Cross.Add((cx, n, s));
                }
                if (t.Cross.Count < 2) { problem = "a town with fewer than two cross streets"; return null; }
                if (t.North) t.Cross[0] = (t.Cross[0].X, true, t.Cross[0].South);
                if (t.South) t.Cross[t.Cross.Count - 1] = (t.Cross[t.Cross.Count - 1].X, t.Cross[t.Cross.Count - 1].North, true);
            }

            // --- names (doc 02 §3.4: unique per map) ---------------------------------------------------------
            var pool = new List<string>(content.StreetNames);
            int wanted = 2 + (three ? 1 : 0);
            foreach (var t in all) wanted += 1 + (t.North ? 1 : 0) + (t.South ? 1 : 0) + t.Cross.Count;
            if (pool.Count < wanted) { problem = "not enough street names"; return null; }
            string Name()
            {
                int i = addresses.NextInt(pool.Count);
                string n = pool[i];
                pool.RemoveAt(i);
                return n;
            }
            foreach (var t in all)
            {
                t.Main = new StreetPlan { Name = Name(), Y = mainY, X0 = t.MainX0, X1 = t.MainX1 };
                if (t.North) t.NorthStreet = new StreetPlan { Name = Name(), Y = northY, X0 = t.BackX0, X1 = t.BackX1 };
                if (t.South) t.SouthStreet = new StreetPlan { Name = Name(), Y = southY, X0 = t.BackX0, X1 = t.BackX1 };
                foreach (var _ in t.Cross) t.CrossNames.Add(Name());
            }
            string? roadName = three ? Name() : null;
            string? inletRoadName = through ? null : Name();

            // --- lots along the frontages (doc 02 §3.4–§3.5) -----------------------------------------------
            foreach (var t in all)
            {
                bool Clear(int x0, int x1)
                {
                    foreach (var cr in t.Cross)
                        if (x1 > cr.X - 1 && x0 < cr.X + 3) return false;
                    return true;
                }
                void Frontage(StreetPlan street, bool main)
                {
                    var cuts = new List<int> { street.X0 };
                    foreach (var cr in t.Cross)
                        if (cr.X > street.X0 && cr.X + 2 < street.X1) cuts.Add(cr.X + 1);
                    cuts.Add(street.X1);
                    int first = t.Segments.Count;
                    for (int i = 0; i + 1 < cuts.Count; i++)
                        t.Segments.Add(new Segment { Street = street, Town = t, X0 = cuts[i], X1 = cuts[i + 1], Keep = main });
                    int lotX0 = Math.Max(street.X0, t.X0), lotX1 = Math.Min(street.X1, t.X1);
                    foreach (bool side in new[] { true, false })
                    {
                        int y = side ? street.Y - 1 - LotSize : street.Y + 3;
                        for (int lx = lotX0 + 2 + towns.NextInt(2); lx + LotSize <= lotX1 - 2; )
                        {
                            bool clear = Clear(lx, lx + LotSize)
                                && !(t.Po is { } po && main && side && lx + LotSize > po.X - 1 && lx < po.X + po.W + 1);
                            if (!clear) { lx++; continue; }
                            var lot = new Lot { Rect = new TileRect(lx, y, LotSize, LotSize), North = side };
                            lot.MailboxX = (lx + 2) * tile;
                            lot.MailboxZ = side ? (y + LotSize) * tile + 1f : (y - 1) * tile + 1f;
                            for (int k = first; k < t.Segments.Count; k++)
                            {
                                var seg = t.Segments[k];
                                if (lx + 2 >= seg.X0 && lx + 2 < seg.X1) { seg.Lots.Add(lot); break; }
                            }
                            lx += LotSize + 2 + towns.NextInt(2);
                        }
                    }
                }
                Frontage(t.Main, main: true);
                if (t.NorthStreet != null) Frontage(t.NorthStreet, main: false);
                if (t.SouthStreet != null) Frontage(t.SouthStreet, main: false);
                t.Segments.RemoveAll(s => s.Lots.Count == 0 && !s.Keep);
            }

            // --- addresses (doc 02 §3.6): each street from its west end, odd north, even south --------------
            foreach (var t in all)
                foreach (var street in new[] { t.Main, t.NorthStreet, t.SouthStreet })
                {
                    if (street == null) continue;
                    var northLots = new List<Lot>();
                    var southLots = new List<Lot>();
                    foreach (var seg in t.Segments)
                        if (seg.Street == street)
                            foreach (var lot in seg.Lots) (lot.North ? northLots : southLots).Add(lot);
                    northLots.Sort((p, q) => p.Rect.X.CompareTo(q.Rect.X));
                    southLots.Sort((p, q) => p.Rect.X.CompareTo(q.Rect.X));
                    for (int i = 0; i < northLots.Count; i++) northLots[i].Number = 2 * i + 1;
                    for (int i = 0; i < southLots.Count; i++) southLots[i].Number = 2 * i + 2;
                }

            // --- districts (doc 02 §3.7): the PO town's two, then the other towns' 3–5 in distance order -----
            a.EntryX = poRect.X * tile + 10f;
            a.EntryZ = poRect.Y * tile + 9f;
            a.FirstDistrict = 1;
            a.DistrictCount = 2;
            // The way from the Post Office arrives at the bank end of the next town's main street, and down the road at the last's east end.
            b.EntryX = b.MainX1 * tile;
            b.EntryZ = (mainY + 1) * tile;
            b.FirstDistrict = 3;
            b.DistrictCount = three ? 2 : 3;
            if (c != null)
            {
                c.EntryX = c.MainX1 * tile;
                c.EntryZ = (mainY + 1) * tile;
                c.FirstDistrict = 5;
                c.DistrictCount = 1;
            }
            int firstWant = 10 + towns.NextInt(3);
            foreach (var t in all)
            {
                var segs = t.Segments;
                float ex = t.EntryX, ez = t.EntryZ;
                segs.Sort((p, q) =>
                {
                    int cmp = SegmentDistance(p, ex, ez).CompareTo(SegmentDistance(q, ex, ez));
                    return cmp != 0 ? cmp : p.X0.CompareTo(q.X0) != 0 ? p.X0.CompareTo(q.X0) : p.Street.Y.CompareTo(q.Street.Y);
                });
                int last = t.FirstDistrict + t.DistrictCount - 1;
                // The PO town's first district is the 10–12 houses nearest the Post Office; else the town's houses evenly.
                int want = t == a ? firstWant : (t.Houses + t.DistrictCount - 1) / t.DistrictCount;
                int district = t.FirstDistrict, inDistrict = 0;
                foreach (var seg in segs)
                {
                    if (inDistrict >= want && district < last && seg.Lots.Count > 0)
                    {
                        district++;
                        inDistrict = 0;
                        if (t == a) want = int.MaxValue;
                    }
                    seg.District = district;
                    inDistrict += seg.Lots.Count;
                }
            }
            var perDistrict = new int[Districts + 1];
            int houses = 0;
            foreach (var t in all)
                foreach (var seg in t.Segments)
                {
                    perDistrict[seg.District] += seg.Lots.Count;
                    houses += seg.Lots.Count;
                }
            if (houses < MinHouses || houses > MaxHouses) { problem = $"{houses} houses"; return null; }
            for (int d = 1; d <= Districts; d++)
                if (perDistrict[d] < MinDistrictHouses || perDistrict[d] > MaxDistrictHouses) { problem = $"district {d} has {perDistrict[d]} houses"; return null; }

            // --- the tile grid ---------------------------------------------------------------------------
            int allRows = rows + WorldGenerator.ShallowRows + WorldGenerator.DeepRows;
            var tiles = new TileType[cols * allRows];
            var tileDistrict = new byte[cols * allRows];
            void Fill(TileRect r, TileType type, int d)
            {
                for (int y = Math.Max(0, r.Y); y < Math.Min(allRows, r.Y + r.H); y++)
                    for (int fx = Math.Max(0, r.X); fx < Math.Min(cols, r.X + r.W); fx++)
                    {
                        tiles[y * cols + fx] = type;
                        if (d > 0) tileDistrict[y * cols + fx] = (byte)d;
                    }
            }
            Fill(new TileRect(0, rows - BeachRows, cols, BeachRows), TileType.Beach, 0);
            // The harbour basin under the beach: shelving under the west half of each town, deep under its east half (for its dock) and in the last rows.
            for (int y = rows; y < allRows; y++)
                for (int fx = 0; fx < cols; fx++)
                {
                    bool deep = y >= rows + WorldGenerator.ShallowRows;
                    foreach (var t in all)
                        if (fx >= (t.X0 + t.X1) / 2 && fx < t.X1) deep = true;
                    tiles[y * cols + fx] = deep ? TileType.DeepWater : TileType.Water;
                }
            // The inlet: beach banks, shelving water two tiles in from each and along its head, deep between, on into the basin.
            int bankTop = through ? 0 : headY - BankWidth;
            Fill(new TileRect(iX0 - BankWidth, bankTop, wI + 2 * BankWidth, rows - bankTop), TileType.Beach, 0);
            for (int y = headY; y < allRows; y++)
                for (int fx = iX0; fx < iX1; fx++)
                {
                    bool shelf = fx - iX0 < 2 || iX1 - 1 - fx < 2 || (!through && y == headY);
                    tiles[y * cols + fx] = shelf && y < rows + WorldGenerator.ShallowRows ? TileType.Water : TileType.DeepWater;
                }

            var districts = new List<DistrictDef>();
            for (int d = 1; d <= Districts; d++)
                districts.Add(new DistrictDef { Id = d, Name = d == 1 ? "Post Office District" : $"District {d}", UnlockShift = d });

            var streets = new List<StreetDef>();
            // The PO town's main street first: the first street is the one the raiders come in by (RaidRules.Gate).
            int nextStreet = 1;
            foreach (var t in all)
                foreach (var seg in t.Segments)
                {
                    if (seg.Street != t.Main) continue;
                    seg.Id = nextStreet++;
                    streets.Add(new StreetDef { Id = seg.Id, Name = seg.Street.Name, District = seg.District, Rect = new TileRect(seg.X0, seg.Street.Y, seg.X1 - seg.X0, 2) });
                }
            foreach (var t in all)
                foreach (var seg in t.Segments)
                {
                    if (seg.Street == t.Main) continue;
                    seg.Id = nextStreet++;
                    streets.Add(new StreetDef { Id = seg.Id, Name = seg.Street.Name, District = seg.District, Rect = new TileRect(seg.X0, seg.Street.Y, seg.X1 - seg.X0, 2) });
                }
            foreach (var t in all)
                for (int i = 0; i < t.Cross.Count; i++)
                {
                    var cr = t.Cross[i];
                    int y0 = cr.North ? northY : mainY, y1 = cr.South ? southY + 2 : mainY + 2;
                    int d = NearestDistrict(t.Segments, cr.X + 1, (y0 + y1) / 2);
                    streets.Add(new StreetDef { Id = nextStreet++, Name = t.CrossNames[i], District = d, Rect = new TileRect(cr.X, y0, 2, y1 - y0) });
                }
            // The long road to the last town, the main street's row run on (doc 02 §3.8 "dirt paths between towns"), in its district.
            if (c != null)
                streets.Add(new StreetDef { Id = nextStreet++, Name = roadName!, District = c.FirstDistrict, Rect = new TileRect(c.X1, mainY, b.X0 - c.X1, 2) });
            // Round the inlet's head: up from the PO town's north street, across north of the head, down to the next town's.
            if (!through)
            {
                int ry = headY - 6, d = b.FirstDistrict;
                int ax = a.BackX0, bx = b.BackX1 - 2;
                streets.Add(new StreetDef { Id = nextStreet++, Name = inletRoadName!, District = d, Rect = new TileRect(bx, ry, ax + 2 - bx, 2) });
                streets.Add(new StreetDef { Id = nextStreet++, Name = inletRoadName!, District = d, Rect = new TileRect(ax, ry, 2, northY - ry) });
                streets.Add(new StreetDef { Id = nextStreet++, Name = inletRoadName!, District = d, Rect = new TileRect(bx, ry, 2, northY - ry) });
            }
            foreach (var s in streets) Fill(s.Rect, TileType.Street, s.District);
            foreach (var t in all)
            {
                if (t.NorthStreet != null) Fill(new TileRect(t.BackX0, northY, t.BackX1 - t.BackX0, 2), TileType.Street, 0);
                if (t.SouthStreet != null) Fill(new TileRect(t.BackX0, southY, t.BackX1 - t.BackX0, 2), TileType.Street, 0);
            }

            // The Post Office (doc 02 §3.11), as on the Small Island: a 4 × 6 building with its yard to the east.
            var solid = new TileRect(poRect.X, poRect.Y, 4, 6);
            Fill(poRect, TileType.Yard, 0);
            Fill(solid, TileType.Building, 0);
            float ox = poRect.X * tile, oz = poRect.Y * tile;
            var postOffice = new PostOfficeDef
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
                SpawnYaw = MathF.Atan2(-26f, -((mainY + 1) * tile - (oz + 9f))),
            };

            var house = content.DestinationType("house");
            var destinations = new List<DestinationDef>();
            int variant = towns.NextInt(12);
            foreach (var t in all)
                foreach (var seg in t.Segments)
                {
                    var street = streets.Find(s => s.Id == seg.Id)!;
                    seg.Lots.Sort((p, q) => p.North != q.North ? (p.North ? -1 : 1) : p.Rect.X.CompareTo(q.Rect.X));
                    foreach (var lot in seg.Lots)
                    {
                        var building = lot.North ? new TileRect(lot.Rect.X, lot.Rect.Y, 3, 2) : new TileRect(lot.Rect.X, lot.Rect.Y + 2, 3, 2);
                        Fill(lot.Rect, TileType.Lot, seg.District);
                        Fill(building, TileType.Building, seg.District);
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

            // Doc 02 §3.5: Business Docks on the coast with deep water, "Yes on Large Island" — one on the beach of each of
            // the first two towns, at its east end, open from the Small Island's dock shift or the town's own, whichever is later.
            var dockType = content.DestinationType("business_dock");
            foreach (var t in new[] { a, b })
            {
                int opens = Math.Max(WorldGenerator.DockShift, t.FirstDistrict);
                for (int dx = t.X1 - 6; dx >= (t.X0 + t.X1) / 2; dx--)
                {
                    var lot = new TileRect(dx, rows - BeachRows, 4, BeachRows);
                    bool free = true;
                    for (int z = lot.Y; z < lot.Y + lot.H && free; z++)
                        for (int tx = lot.X; tx < lot.X + lot.W && free; tx++)
                            if (tiles[z * cols + tx] != TileType.Beach) free = false;
                    for (int tx = lot.X; tx < lot.X + lot.W && free; tx++)
                        if (tiles[rows * cols + tx] != TileType.DeepWater) free = false;
                    if (!free) continue;
                    float mx = dx * tile + 1f, mz = rows * tile - 1f;
                    StreetDef? near = null;
                    float best = float.MaxValue;
                    foreach (var seg in t.Segments)
                    {
                        if (seg.District > opens) continue;
                        var st = streets.Find(s => s.Id == seg.Id)!;
                        float sx = Math.Clamp(mx, st.Rect.X * tile, (st.Rect.X + st.Rect.W) * tile);
                        float sz = Math.Clamp(mz, st.Rect.Y * tile, (st.Rect.Y + st.Rect.H) * tile);
                        float dd = (sx - mx) * (sx - mx) + (sz - mz) * (sz - mz);
                        if (dd < best) { best = dd; near = st; }
                    }
                    if (near == null) break;
                    // Past the last number of the whole street (all its segments), so no house shares the dock's label.
                    int number = 1;
                    foreach (var d in destinations)
                        if (d.Street.Name == near.Name) number = Math.Max(number, d.Address.Number + 1);
                    var building = new TileRect(dx + 1, lot.Y, 3, 3);
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
                        UnlockShift = opens,
                        LaidOn = TileType.Beach,
                    });
                    break;
                }
            }

            // Oil seeps (doc 02 §3.2 "Marsh (Large Island) · low flat land near inlets"): two on the open grass by the
            // inlet's banks, two more out of the towns, far from the Post Office and from one another.
            var oil = new RngStream(seed, "oil" + suffix);
            var seeps = new List<(int X, int Z)>();
            int poCx = poRect.X + 3, poCz = poRect.Y + 3;
            for (int tries = 0; tries < 800 && seeps.Count < 4; tries++)
            {
                bool marsh = seeps.Count < 2 && tries < 400;
                int sx = marsh ? iX0 - 14 + oil.NextInt(wI + 28) : 2 + oil.NextInt(cols - 4);
                int sz = 2 + oil.NextInt(rows - BeachRows - 4);
                if (sx < 2 || sx >= cols - 2) continue;
                if (Math.Abs(sx - poCx) + Math.Abs(sz - poCz) < 14) continue;
                bool open = true;
                for (int dz = -2; dz <= 2 && open; dz++)
                    for (int ddx = -2; ddx <= 2 && open; ddx++)
                        if (tiles[(sz + dz) * cols + sx + ddx] != TileType.Grass) open = false;
                foreach (var (qx, qz) in seeps) if (Math.Abs(qx - sx) + Math.Abs(qz - sz) < 10) open = false;
                if (open) seeps.Add((sx, sz));
            }

            // Doc 02 §3.8: where no walk joins a town to the Post Office's, a ferry lane between the two nearest beaches.
            var lanes = FerryLanes.Mark(cols, allRows, tiles, postOffice.SpawnPadX, postOffice.SpawnPadZ, destinations);
            if (lanes == null) { problem = "a town no walk nor ferry lane reaches"; return null; }

            // --- the island round the towns (doc 02 §3.1–§3.2): 1000 m across, the inlet cut into it -------------
            var shape = new IslandTerrain.Shape
            {
                Size = TerrainSize,
                Radius = 223,
                RadiusStep = 10,
                GridTile = (gx, gz) => tiles[gz * cols + gx],
                NorthChannel = through ? (iX0, iX1) : null,
            };
            var terrain = IslandTerrain.Generate(seed, suffix, cols, rows, BeachRows, poRect.X + 5, poRect.Y + 4, shape, out problem);
            if (terrain == null) return null;

            var chests = new List<MapChestDef>
            {
                new MapChestDef { Id = 0, Container = content.Container("chest"), X = ox + 11f, Z = oz + 0.8f, Yaw = -MathF.PI * 0.5f },
            };

            var map = new WorldMap
            {
                Id = LargeIsland,
                Name = $"Large Island #{seed}",
                Cols = cols,
                Rows = allRows,
                Tiles = tiles,
                TileDistrict = tileDistrict,
                Districts = districts,
                Streets = streets,
                Destinations = destinations,
                OilSeeps = seeps,
                FerryLanes = lanes,
                PostOffice = postOffice,
                Chests = chests,
                Terrain = terrain,
            };
            map.ComputeHash();
            problem = WorldGenerator.Validate(map) ?? Check(map);
            return problem == null ? map : null;
        }

        /// <summary>
        /// Doc 12 M4's acceptance on top of doc 02 §3.12: at least one Business Dock, the districts 3–5 in the towns past
        /// the Post Office's. Null when good.
        /// </summary>
        public static string? Check(WorldMap map)
        {
            int docks = 0;
            foreach (var d in map.Destinations) if (d.IsDock) docks++;
            if (docks == 0) return "no Business Dock";
            return null;
        }

        private static float SegmentDistance(Segment s, float x, float z)
        {
            float cx = (s.X0 + s.X1) * 0.5f * SimConstants.TileSize, cz = (s.Street.Y + 1) * SimConstants.TileSize;
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
    }
}
