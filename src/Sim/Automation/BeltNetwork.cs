using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Automation
{
    /// <summary>One piece of mail riding a belt (doc 04 §2.1).</summary>
    public struct BeltItem
    {
        public MailItem Mail;
        /// <summary>Metres of its front from the segment's start.</summary>
        public float Pos;
        /// <summary><see cref="BeltNetwork.LaneLeft"/>, <see cref="BeltNetwork.LaneRight"/> or <see cref="BeltNetwork.LaneBoth"/> (Cargo).</summary>
        public byte Lane;
        /// <summary>Metres of lane it takes behind its front (the kind's beltLength).</summary>
        public float Length;
    }

    public enum BeltEndKind : byte
    {
        /// <summary>Nothing takes what comes: it waits at the end, and the line backs up.</summary>
        None,
        /// <summary>The head of another segment (or its own, round a loop).</summary>
        Belt,
        /// <summary>A mailbox: what it accepts goes in at its insert rate.</summary>
        Mailbox,
    }

    /// <summary>
    /// A compiled run of belt tiles (doc 04 §2.1): from a head with no single feeder behind it,
    /// tile after tile while each is fed by the one before it alone, to an end. A 40-tile belt is
    /// one segment. Its id is its head tile, so the server and every client, compiling the same
    /// tiles, name it the same.
    /// </summary>
    public sealed class BeltSegment
    {
        /// <summary>Tile indices (z × cols + x), from the head.</summary>
        public readonly List<int> Tiles = new List<int>();
        /// <summary>Front first: in each lane the order never changes.</summary>
        public readonly List<BeltItem> Items = new List<BeltItem>();
        public int Head => Tiles[0];
        public float Length => Tiles.Count * SimConstants.TileSize;
        /// <summary>m/s: the slowest belt on it (a change of tier starts a segment).</summary>
        public float Speed;
        public BeltEndKind End;
        public BeltSegment? Next;
        /// <summary>A mailbox end: the destination's id.</summary>
        public int DestId = -1;
        /// <summary>The Intake's outfeed is behind its head (doc 04 §2.6).</summary>
        public bool FromIntake;
        /// <summary>A mailbox end: the first tick it takes the next item.</summary>
        public uint NextInsertTick;
        /// <summary>The lane the next item put on at the head goes to.</summary>
        public byte NextLane;

        /// <summary>The tile the segment's last tile faces, as a tile index; −1 off the map.</summary>
        public int FrontTile = -1;

        public bool HasRoom(byte lane, float length) => RearRoom(lane) >= length - 1e-4f;

        /// <summary>Metres free at the segment's start in a lane: to the rear of its last item there.</summary>
        public float RearRoom(byte lane)
        {
            float room = Length;
            foreach (var it in Items)
            {
                if (!BeltNetwork.SharesLane(it.Lane, lane)) continue;
                room = Math.Min(room, it.Pos - it.Length);
            }
            return room;
        }
    }

    /// <summary>What the server does at a segment's end; a client has none (only the server moves mail off a belt).</summary>
    public interface IBeltSink
    {
        /// <summary>The front item has reached a mailbox end; true if the mailbox took it.</summary>
        bool TryDeliver(BeltSegment segment, in BeltItem item);
        /// <summary>An item went from one segment's end onto the next one's head.</summary>
        void Moved(BeltSegment from, BeltSegment to, in BeltItem item);
    }

    /// <summary>
    /// Every belt of the map (doc 04 §2.1), compiled from the <see cref="ConstructField"/> into
    /// segments and advanced in one pass per segment, front to back (doc 07 §8: per-segment arrays,
    /// endpoints look at the front item only). Plain C#: the server runs it with a sink that moves
    /// mail from end to head and into mailboxes; a client runs the same code without one, for the
    /// look, and the server's events move its items between segments.
    /// </summary>
    public sealed class BeltNetwork
    {
        public const byte LaneLeft = 0, LaneRight = 1, LaneBoth = 2;
        private const float Tile = SimConstants.TileSize;

        /// <summary>Rotation r faces (Dx, Dz)[r]: east, south, west, north — the order of <see cref="ConstructField"/>'s arms.</summary>
        public static readonly (int Dx, int Dz)[] Dirs = { (1, 0), (0, 1), (-1, 0), (0, -1) };

        private readonly WorldMap _map;
        private readonly ConstructField _field;
        private readonly ContentDb _content;
        private readonly List<BeltSegment> _segments = new List<BeltSegment>();
        private readonly Dictionary<int, BeltSegment> _byHead = new Dictionary<int, BeltSegment>();
        private readonly BeltSegment?[] _segOf;
        private readonly int[] _indexIn;
        private readonly Dictionary<int, int> _mailboxAt = new Dictionary<int, int>();
        private readonly int _intakeTile;
        private int _fieldVersion = -1;
        private string _signature = "";

        public IReadOnlyList<BeltSegment> Segments => _segments;
        /// <summary>Goes up with every compile that changed the segments.</summary>
        public int Version { get; private set; }
        /// <summary>Items riding all belts.</summary>
        public int ItemCount { get; private set; }

        public BeltNetwork(ContentDb content, WorldMap map, ConstructField field)
        {
            _content = content;
            _map = map;
            _field = field;
            _segOf = new BeltSegment?[map.Cols * map.Rows];
            _indexIn = new int[map.Cols * map.Rows];
            foreach (var d in map.Destinations)
            {
                int t = TileOf(d.MailboxX, d.MailboxZ);
                if (t >= 0 && !_mailboxAt.ContainsKey(t)) _mailboxAt[t] = d.Id;
            }
            _intakeTile = TileOf(map.PostOffice.IntakeX, map.PostOffice.IntakeZ);
        }

        private int TileOf(float x, float z)
        {
            int tx = (int)MathF.Floor(x / Tile), tz = (int)MathF.Floor(z / Tile);
            return _map.InBounds(tx, tz) ? tz * _map.Cols + tx : -1;
        }

        /// <summary>The Intake's tile.</summary>
        public int IntakeTile => _intakeTile;

        /// <summary>
        /// The way a belt on <see cref="OutfeedTile"/> faces to be fed by the Intake: north, away
        /// from the counter's north end (design choice 95: doc 10's east side is where the carriers
        /// stand at the counter and the pad's seats are).
        /// </summary>
        public const int OutfeedRotation = 3;

        /// <summary>The tile north of the Intake's, where its outfeed belt starts; −1 off the map.</summary>
        public int OutfeedTile => _intakeTile >= _map.Cols ? _intakeTile - _map.Cols : -1;

        public int OutfeedX => OutfeedTile % _map.Cols;
        public int OutfeedZ => OutfeedTile / _map.Cols;

        /// <summary>The segment the Intake's outfeed feeds, if a belt stands there the right way.</summary>
        public BeltSegment? Outfeed
        {
            get
            {
                foreach (var s in _segments) if (s.FromIntake) return s;
                return null;
            }
        }

        /// <summary>Whether a lane holder <paramref name="a"/> takes room in lane <paramref name="b"/>.</summary>
        public static bool SharesLane(byte a, byte b) => a == LaneBoth || b == LaneBoth || a == b;

        public static bool IsBelt(Construct? c) => c != null && c.Def.Behaviour == BuildingBehaviour.Belt;

        public BeltSegment? SegmentAt(int tx, int tz) => _map.InBounds(tx, tz) ? _segOf[tz * _map.Cols + tx] : null;
        public BeltSegment? SegmentByHead(int head) => _byHead.TryGetValue(head, out var s) ? s : null;
        public int IndexInSegment(int tx, int tz) => _map.InBounds(tx, tz) ? _indexIn[tz * _map.Cols + tx] : -1;

        /// <summary>The mailbox standing on a tile, −1 for none.</summary>
        public int MailboxOn(int tile) => _mailboxAt.TryGetValue(tile, out int d) ? d : -1;

        private int FrontOf(int tile, int rotation)
        {
            int tx = tile % _map.Cols + Dirs[rotation & 3].Dx, tz = tile / _map.Cols + Dirs[rotation & 3].Dz;
            return _map.InBounds(tx, tz) ? tz * _map.Cols + tx : -1;
        }

        private Construct? BeltOn(int tile) => tile < 0 ? null : _field.At(tile % _map.Cols, tile / _map.Cols) is { } c && IsBelt(c) ? c : null;

        /// <summary>True if the belt on <paramref name="from"/> hands on to the belt on the tile it faces (not one facing back at it).</summary>
        private bool Feeds(int from, out int to)
        {
            to = -1;
            var a = BeltOn(from);
            if (a == null) return false;
            to = FrontOf(from, a.Rotation);
            var b = BeltOn(to);
            return b != null && (b.Rotation & 3) != ((a.Rotation + 2) & 3);
        }

        /// <summary>
        /// Compiles the segments again if the belts changed since the last time (built, taken down,
        /// turned; damage alone does not). Items keep their place by tile; what lay on a tile that is
        /// gone goes to <paramref name="orphans"/>. <paramref name="changed"/> gets every segment that
        /// is not exactly as it was (its tiles, end or speed). True if anything was compiled.
        /// </summary>
        public bool Compile(List<BeltSegment>? changed = null, List<BeltItem>? orphans = null)
        {
            if (_field.Version == _fieldVersion) return false;
            _fieldVersion = _field.Version;
            var sig = new System.Text.StringBuilder();
            foreach (var c in _field.All)
                if (IsBelt(c)) sig.Append(c.Tx).Append(',').Append(c.Tz).Append(',').Append(c.Rotation).Append(',').Append(c.Def.Index).Append(';');
            string signature = sig.ToString();
            if (signature == _signature) return false;
            _signature = signature;

            var old = new List<BeltSegment>(_segments);
            var oldKeys = new Dictionary<int, string>();
            foreach (var s in old) oldKeys[s.Head] = Key(s);
            _segments.Clear();
            _byHead.Clear();
            Array.Clear(_segOf, 0, _segOf.Length);

            var belts = new List<int>();
            foreach (var c in _field.All)
                if (IsBelt(c)) belts.Add(c.Tz * _map.Cols + c.Tx);
            belts.Sort();
            var feeders = new Dictionary<int, int>();
            foreach (int t in belts)
                if (Feeds(t, out int to)) feeders[to] = feeders.TryGetValue(to, out int n) ? n + 1 : 1;

            // A tile continues the segment of the belt behind it if that belt is its only feeder and of its tier.
            var from = new Dictionary<int, int>();
            foreach (int t in belts)
            {
                if (!Feeds(t, out int to) || feeders[to] != 1) continue;
                if (BeltOn(t)!.Def != BeltOn(to)!.Def) continue;
                from[to] = t;
            }
            foreach (int t in belts)
                if (!from.ContainsKey(t)) Walk(t, from);
            // What is left are loops: each one's head is its lowest tile.
            foreach (int t in belts)
                if (_segOf[t] == null) Walk(t, from);

            foreach (var s in _segments)
            {
                var last = BeltOn(s.Tiles[s.Tiles.Count - 1])!;
                s.FrontTile = FrontOf(s.Tiles[s.Tiles.Count - 1], last.Rotation);
                if (Feeds(s.Tiles[s.Tiles.Count - 1], out int to))
                {
                    s.End = BeltEndKind.Belt;
                    s.Next = _segOf[to];
                }
                else if (s.FrontTile >= 0 && _mailboxAt.TryGetValue(s.FrontTile, out int dest))
                {
                    s.End = BeltEndKind.Mailbox;
                    s.DestId = dest;
                }
                // The Intake's outfeed (doc 04 §2.6): a belt on its tile, facing away from the counter, is fed.
                var head = BeltOn(s.Head)!;
                s.FromIntake = !feeders.ContainsKey(s.Head) && s.Head == OutfeedTile && (head.Rotation & 3) == OutfeedRotation;
            }

            // Items keep their tile and their place on it.
            ItemCount = 0;
            foreach (var s in old)
            {
                foreach (var it in s.Items)
                {
                    int k = Math.Clamp((int)MathF.Floor(it.Pos / Tile), 0, s.Tiles.Count - 1);
                    float off = it.Pos - k * Tile;
                    var to = _segOf[s.Tiles[k]];
                    if (to == null) { orphans?.Add(it); continue; }
                    var moved = it;
                    moved.Pos = _indexIn[s.Tiles[k]] * Tile + off;
                    to.Items.Add(moved);
                }
            }
            foreach (var s in _segments)
            {
                SortItems(s);
                // Two runs joined where an item stood at the end of one and another at the head of
                // the next: whoever does not fit behind the one ahead is off the belt.
                Span<float> frontier = stackalloc float[2];
                frontier[0] = frontier[1] = s.Length;
                for (int i = 0; i < s.Items.Count; i++)
                {
                    var it = s.Items[i];
                    float limit = Limit(frontier, it.Lane);
                    if (it.Pos > limit) it.Pos = limit;
                    if (it.Pos - it.Length < -1e-4f) { orphans?.Add(it); s.Items.RemoveAt(i--); continue; }
                    s.Items[i] = it;
                    SetFrontier(frontier, it.Lane, it.Pos - it.Length);
                }
                ItemCount += s.Items.Count;
                if (changed != null && (!oldKeys.TryGetValue(s.Head, out var key) || key != Key(s))) changed.Add(s);
            }
            Version++;
            return true;
        }

        private static string Key(BeltSegment s) =>
            string.Join(",", s.Tiles) + "|" + s.End + "|" + (s.Next?.Head ?? -1) + "|" + s.DestId + "|" + s.Speed + "|" + s.FromIntake;

        private void Walk(int start, Dictionary<int, int> from)
        {
            var s = new BeltSegment { Speed = BeltOn(start)!.Def.Speed };
            int t = start;
            while (t >= 0 && _segOf[t] == null)
            {
                _segOf[t] = s;
                _indexIn[t] = s.Tiles.Count;
                s.Tiles.Add(t);
                if (!Feeds(t, out int to) || !from.TryGetValue(to, out int back) || back != t) break;
                t = to;
            }
            _segments.Add(s);
            _byHead[s.Head] = s;
        }

        private static float Limit(Span<float> frontier, byte lane) =>
            lane == LaneBoth ? Math.Min(frontier[0], frontier[1]) : frontier[lane];

        private static void SetFrontier(Span<float> frontier, byte lane, float value)
        {
            if (lane == LaneBoth) frontier[0] = frontier[1] = value;
            else frontier[lane] = value;
        }

        private static void SortItems(BeltSegment s)
        {
            // Nearly sorted already: an insertion sort, stable, front first.
            var items = s.Items;
            for (int i = 1; i < items.Count; i++)
            {
                var it = items[i];
                int j = i - 1;
                while (j >= 0 && items[j].Pos < it.Pos) { items[j + 1] = items[j]; j--; }
                items[j + 1] = it;
            }
        }

        /// <summary>
        /// One tick: every item moves on at its segment's speed (times <paramref name="speedMult"/>)
        /// up to the one ahead in its lane or the segment's end. With a sink, the front items at an
        /// end then go on to the next segment or into the mailbox, if there is room.
        /// </summary>
        public void Step(float dt, IBeltSink? sink, uint tick = 0, float speedMult = 1f)
        {
            Span<float> frontier = stackalloc float[2];
            foreach (var s in _segments)
            {
                if (s.Items.Count == 0) continue;
                float step = s.Speed * speedMult * dt;
                frontier[0] = frontier[1] = s.Length;
                var items = s.Items;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    it.Pos = Math.Min(it.Pos + step, Limit(frontier, it.Lane));
                    items[i] = it;
                    SetFrontier(frontier, it.Lane, it.Pos - it.Length);
                }
                SortItems(s);
            }
            if (sink == null) return;
            foreach (var s in _segments) HandOff(s, sink, tick);
        }

        private void HandOff(BeltSegment s, IBeltSink sink, uint tick)
        {
            // At most one front item per lane is at the end; look at the first few.
            for (int i = 0; i < s.Items.Count && i < 2; i++)
            {
                var it = s.Items[i];
                if (it.Pos < s.Length - 1e-4f) break;
                bool gone = false;
                switch (s.End)
                {
                    case BeltEndKind.Belt when s.Next != null:
                        byte lane = EntryLane(s, s.Next, it.Lane);
                        if (s.Next == s && s.Items.Count > 1)
                        {
                            // Round a loop: the lane behind it must have room for it at the head.
                            float rear = s.Length;
                            for (int k = 0; k < s.Items.Count; k++)
                                if (k != i && SharesLane(s.Items[k].Lane, lane)) rear = Math.Min(rear, s.Items[k].Pos - s.Items[k].Length);
                            if (rear < it.Length - 1e-4f) break;
                        }
                        else if (s.Next != s && !s.Next.HasRoom(lane, it.Length)) break;
                        s.Items.RemoveAt(i);
                        var moved = it;
                        moved.Pos = 0f;
                        moved.Lane = lane;
                        s.Next.Items.Add(moved);
                        SortItems(s.Next);
                        sink.Moved(s, s.Next, moved);
                        gone = true;
                        break;
                    case BeltEndKind.Mailbox:
                        if (tick < s.NextInsertTick) break;
                        if (!sink.TryDeliver(s, it)) break;
                        s.Items.RemoveAt(i);
                        ItemCount--;
                        gone = true;
                        break;
                }
                if (gone) i--;
            }
        }

        /// <summary>
        /// The lane mail goes on at the next segment's head: its own coming in straight (or round a
        /// corner of the same run), the lane on the side it comes from when it comes in from the
        /// side (design choice 94). Cargo takes both lanes wherever it goes.
        /// </summary>
        public byte EntryLane(BeltSegment from, BeltSegment to, byte lane)
        {
            if (lane == LaneBoth) return lane;
            var head = BeltOn(to.Head);
            var last = BeltOn(from.Tiles[from.Tiles.Count - 1]);
            if (head == null || last == null) return lane;
            int d = head.Rotation & 3, f = last.Rotation & 3;
            if (d == f) return lane;
            // The feeder stands behind the head's tile, towards −f; the head's left is (Dz, −Dx).
            bool fromLeft = -Dirs[f].Dx == Dirs[d].Dz && -Dirs[f].Dz == -Dirs[d].Dx;
            return fromLeft ? LaneLeft : LaneRight;
        }

        /// <summary>The lane an item of a kind is put on at a segment's head; false if neither has room.</summary>
        public static bool PickLane(BeltSegment s, MailKindDef kind, out byte lane)
        {
            float length = kind.BeltLength;
            if (kind.BeltLanes >= 2)
            {
                lane = LaneBoth;
                return s.HasRoom(LaneBoth, length);
            }
            for (int k = 0; k < 2; k++)
            {
                lane = (byte)((s.NextLane + k) & 1);
                if (s.HasRoom(lane, length)) return true;
            }
            lane = 0;
            return false;
        }

        /// <summary>Puts a piece of mail on a segment's head; false (nothing done) without room.</summary>
        public bool TryInsert(BeltSegment s, in MailItem mail, out BeltItem item)
        {
            var kind = _content.MailKinds[mail.Kind];
            item = default;
            if (!PickLane(s, kind, out byte lane)) return false;
            item = new BeltItem { Mail = mail, Pos = 0f, Lane = lane, Length = kind.BeltLength };
            s.Items.Add(item);
            SortItems(s);
            if (lane != LaneBoth) s.NextLane = (byte)((lane + 1) & 1);
            ItemCount++;
            return true;
        }

        // --- the client's mirror ------------------------------------------------------------------

        /// <summary>A client: an item the server put on a segment.</summary>
        public void Add(BeltSegment s, in BeltItem item)
        {
            RemoveMail(item.Mail.Id);
            s.Items.Add(item);
            SortItems(s);
            ItemCount++;
        }

        /// <summary>A client: the server's whole state of a segment.</summary>
        public void SetItems(BeltSegment s, List<BeltItem> items)
        {
            foreach (var it in items) RemoveMail(it.Mail.Id);
            ItemCount -= s.Items.Count;
            s.Items.Clear();
            s.Items.AddRange(items);
            SortItems(s);
            ItemCount += s.Items.Count;
        }

        /// <summary>The item carrying a piece of mail, wherever it rides; null if on no belt.</summary>
        public BeltItem? Find(uint mailId)
        {
            foreach (var s in _segments)
                foreach (var it in s.Items)
                    if (it.Mail.Id == mailId) return it;
            return null;
        }

        /// <summary>Takes a piece of mail off whichever belt it is on; false if it is on none.</summary>
        public bool RemoveMail(uint mailId)
        {
            foreach (var s in _segments)
                for (int i = 0; i < s.Items.Count; i++)
                    if (s.Items[i].Mail.Id == mailId)
                    {
                        s.Items.RemoveAt(i);
                        ItemCount--;
                        return true;
                    }
            return false;
        }

        /// <summary>Everything off every belt (a run's end); the items are handed back.</summary>
        public List<BeltItem> Clear()
        {
            var all = new List<BeltItem>();
            foreach (var s in _segments)
            {
                all.AddRange(s.Items);
                s.Items.Clear();
            }
            ItemCount = 0;
            return all;
        }

        /// <summary>
        /// What the server and a client compare (doc 06 §4 LaneChecksum): the count, and a hash of
        /// the items lane by lane in their order. Positions are left out: a client's lag behind the
        /// server's tick moves them, not which mail is where.
        /// </summary>
        public static uint Checksum(BeltSegment s)
        {
            var h = Fnv32.Create();
            h.Add(s.Items.Count);
            for (byte lane = 0; lane <= LaneBoth; lane++)
                foreach (var it in s.Items)
                    if (it.Lane == lane) { h.Add((int)it.Mail.Id); h.Add(lane); }
            return h.Value;
        }

        /// <summary>The world position of a point <paramref name="pos"/> metres along a segment, <paramref name="lane"/>'s side of the middle.</summary>
        public void PointAt(BeltSegment s, float pos, byte lane, out float x, out float z, out int dir)
        {
            int k = Math.Clamp((int)MathF.Floor(pos / Tile), 0, s.Tiles.Count - 1);
            float t = Math.Clamp(pos - k * Tile, 0f, Tile) / Tile;
            int tile = s.Tiles[k];
            var c = BeltOn(tile);
            dir = c != null ? c.Rotation & 3 : 0;
            // Where it comes in from: the tile behind in the segment, else straight from behind.
            int inDir = dir;
            if (k > 0 && BeltOn(s.Tiles[k - 1]) is { } prev) inDir = prev.Rotation & 3;
            float cx = (tile % _map.Cols + 0.5f) * Tile, cz = (tile / _map.Cols + 0.5f) * Tile;
            // Entry at the tile's edge it comes in by, to the middle, then out to the edge it faces.
            float ex = cx - Dirs[inDir].Dx * Tile * 0.5f, ez = cz - Dirs[inDir].Dz * Tile * 0.5f;
            float ox = cx + Dirs[dir].Dx * Tile * 0.5f, oz = cz + Dirs[dir].Dz * Tile * 0.5f;
            int along = t < 0.5f ? inDir : dir;
            if (t < 0.5f) { x = ex + (cx - ex) * t * 2f; z = ez + (cz - ez) * t * 2f; }
            else { x = cx + (ox - cx) * (t - 0.5f) * 2f; z = cz + (oz - cz) * (t - 0.5f) * 2f; }
            if (lane != LaneBoth)
            {
                // Left of the way it runs is lane 0: (dz, −dx) turned from (dx, dz) on a y-up, z-south map.
                float side = lane == LaneLeft ? 1f : -1f;
                x += Dirs[along].Dz * 0.45f * side;
                z += -Dirs[along].Dx * 0.45f * side;
            }
            dir = along;
        }
    }
}
