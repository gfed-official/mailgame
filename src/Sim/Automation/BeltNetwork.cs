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
        /// <summary>A splitter (doc 04 §2.3): to the first of its outputs with room, round-robin.</summary>
        Split,
        /// <summary>An Address Sorter's input (doc 04 §2.4): into its buffer while there is room.</summary>
        Sorter,
        /// <summary>A container belts feed directly (doc 04 §2.6: a Depot, on any side): into its grid while it has room.</summary>
        Container,
        /// <summary>A pipe inlet (doc 04 §2.2), from behind: into a capsule when it can send one.</summary>
        Inlet,
    }

    /// <summary>
    /// An Address Sorter in the belt network (doc 04 §2.4): the mail in its buffer, oldest first,
    /// and the segments its outputs feed. It keeps its buffer from one compile to the next.
    /// </summary>
    public sealed class SorterNode
    {
        public const int Front = 0, Left = 1, Right = 2, Overflow = 3, Ports = 4;

        public Construct Built { get; }
        public readonly List<MailItem> Buffer = new List<MailItem>();
        /// <summary>The segment each port hands on to (front, left, right, overflow); null with no belt there.</summary>
        public readonly BeltSegment?[] Outs = new BeltSegment?[Ports];
        /// <summary>The first tick it looks at the next piece.</summary>
        public uint NextTick;
        /// <summary>This run: pieces it sent out by a filter, and to the overflow.</summary>
        public int Sorted, Overflowed;

        public SorterNode(Construct built) { Built = built; }

        /// <summary>
        /// The port a piece goes out by: the first filtered output with a belt whose filters catch
        /// it, else the overflow; −1 if that has no belt either.
        /// </summary>
        public int PortFor(in MailItem mail)
        {
            var def = Built.Def;
            for (int o = 0; o < def.FilterOutputs; o++)
            {
                if (Outs[o] == null) continue;
                for (int k = 0; k < def.FilterSlots; k++)
                    if (Built.Filter(o, k).Matches(mail)) return o;
            }
            return Outs[Overflow] != null ? Overflow : -1;
        }

        /// <summary>
        /// Where a port is: the sorter's tile it leaves from (along, across from the one it was
        /// built on) and the way it goes out. The input comes into (0, 0) from behind.
        /// </summary>
        public static (int Along, int Across, int Dir) Port(int port, int facing)
        {
            int f = facing & 3;
            switch (port)
            {
                case Front: return (1, 0, f);
                case Left: return (1, 0, (f + 3) & 3);
                case Right: return (1, 1, (f + 1) & 3);
                default: return (0, 1, (f + 2) & 3);
            }
        }
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

        /// <summary>A splitter: the segments it hands on to, front, left, right; the next to try.</summary>
        public readonly List<BeltSegment> Outs = new List<BeltSegment>();
        /// <summary>A splitter: the port of each of <see cref="Outs"/> (front 0, left 1, right 2), for its filters.</summary>
        public readonly List<int> OutPorts = new List<int>();
        /// <summary>A splitter: itself, for its filters.</summary>
        public Construct? Splitter;
        /// <summary>A sorter end: the sorter.</summary>
        public SorterNode? Sorter;
        /// <summary>A container end: the container's id.</summary>
        public uint ContainerId;
        public int NextOut;
        /// <summary>A filtered splitter: the turn of the outputs without a filter, kept apart so neither kind of mail skews the other's.</summary>
        public int NextOutRest;
        /// <summary>A merger: the segments that feed it; whose turn it is.</summary>
        public readonly List<BeltSegment> Ins = new List<BeltSegment>();
        public int NextIn;

        /// <summary>
        /// Ticks left of a carrier's push (doc 04 §2.1 "pushing a belt"): while above 0 the
        /// segment runs at <see cref="BeltNetwork.PushMultiplier"/> times its speed. Counted down
        /// by every step, on the server and on a client alike, so neither needs the other's tick.
        /// </summary>
        public int PushTicks;

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
        /// <summary>The front item has reached a container's side; true if the container took it.</summary>
        bool TryStore(BeltSegment segment, in BeltItem item);
        /// <summary>An item went off a segment's end into a sorter's buffer.</summary>
        void Took(BeltSegment from, in BeltItem item);
        /// <summary>The front item has reached an end with nothing built before it; true if a vehicle's loading face standing there took it.</summary>
        bool TryLoad(BeltSegment segment, in BeltItem item) => false;
        /// <summary>The front item has reached a pipe inlet; true if the inlet sent it on.</summary>
        bool TryInlet(BeltSegment segment, in BeltItem item) => false;
        /// <summary>A sorter put a piece out by a port onto the head of a segment.</summary>
        void Sorted(SorterNode sorter, int port, BeltSegment to, in BeltItem item);
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
        /// <summary>Top of a belt's running surface above the ground: where the mail rides and a carrier on it stands.</summary>
        public const float SurfaceHeight = 0.36f;
        /// <summary>
        /// How much higher an Elevated Belt runs (doc 04 §2.1 "one level"; design choice 99: clear
        /// of a carrier's head and a bike's rider, so the street under it stays open).
        /// </summary>
        public const float LevelHeight = 2.2f;
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
        private readonly List<SorterNode> _sorters = new List<SorterNode>();
        private readonly int _intakeTile;
        private int _fieldVersion = -1;
        private string _signature = "";

        public IReadOnlyList<BeltSegment> Segments => _segments;
        /// <summary>Goes up with every compile that changed the segments.</summary>
        public int Version { get; private set; }
        /// <summary>Items riding all belts.</summary>
        public int ItemCount { get; private set; }
        /// <summary>Every Address Sorter, in the order they were built.</summary>
        public IReadOnlyList<SorterNode> Sorters => _sorters;

        public SorterNode? SorterOf(Construct c)
        {
            foreach (var n in _sorters) if (n.Built == c) return n;
            return null;
        }

        /// <summary>Mail in the sorters' buffers.</summary>
        public int BufferedCount
        {
            get
            {
                int n = 0;
                foreach (var s in _sorters) n += s.Buffer.Count;
                return n;
            }
        }

        /// <summary>Doc 11 "operated belt multiplier": a pushed segment's speed, times its own.</summary>
        public float PushMultiplier { get; }
        /// <summary>How long one push lasts: a carrier holding the key pushes again before it runs out.</summary>
        public int PushTickCount { get; }

        public BeltNetwork(ContentDb content, WorldMap map, ConstructField field)
        {
            _content = content;
            var balance = content.Balance;
            PushMultiplier = balance.Has("beltPushMultiplier") ? balance.GetFloat("beltPushMultiplier") : 1.5f;
            PushTickCount = (int)SimConstants.SecondsToTicks(balance.Has("beltPushSeconds") ? balance.GetFloat("beltPushSeconds") : 0.7);
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

        /// <summary>Anything mail rides through: a belt, a splitter, a merger.</summary>
        public static bool IsBelt(Construct? c) => c != null && c.Def.Carries;

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

        /// <summary>
        /// The tiles a belt-like construct hands mail on to (doc 04 §2.3): the one it faces; a
        /// splitter also the ones to its left and right, front first, then left, then right.
        /// </summary>
        private void Outputs(int tile, List<int> outs)
        {
            outs.Clear();
            var a = BeltOn(tile);
            if (a == null) return;
            int r = a.Rotation & 3;
            outs.Add(FrontOf(tile, r));
            if (a.Def.Behaviour != BuildingBehaviour.Splitter) return;
            outs.Add(FrontOf(tile, (r + 3) & 3));
            outs.Add(FrontOf(tile, (r + 1) & 3));
        }

        /// <summary>
        /// Whether what stands on <paramref name="to"/> takes mail from <paramref name="from"/>: a
        /// belt and a merger from anywhere but their front, a splitter and a ramp only from behind,
        /// and only at the height it comes at (doc 04 §2.1: a ramp between the levels).
        /// </summary>
        private bool Accepts(int from, int to)
        {
            int dir = DirTo(from, to);
            return dir >= 0 && AcceptsFrom(dir, to, OutLevel(from));
        }

        /// <summary>Whether what stands on <paramref name="to"/> takes mail coming in going <paramref name="dir"/> at a height (half levels).</summary>
        private bool AcceptsFrom(int dir, int to, int level)
        {
            var b = BeltOn(to);
            if (b == null) return false;
            int r = b.Rotation & 3;
            bool way = b.Def.Behaviour == BuildingBehaviour.Splitter || b.Def.Ramp ? dir == r : dir != ((r + 2) & 3);
            return way && level == InLevel(to);
        }

        private int TileIndex(int tx, int tz) => _map.InBounds(tx, tz) ? tz * _map.Cols + tx : -1;

        /// <summary>The tile a sorter's port hands on to, −1 off the map.</summary>
        private int PortTile(Construct sorter, int port)
        {
            var (along, across, dir) = SorterNode.Port(port, sorter.Facing);
            var (x, z) = Construct.TileOf(sorter.Tx, sorter.Tz, sorter.Facing, along, across);
            return TileIndex(x + Dirs[dir].Dx, z + Dirs[dir].Dz);
        }

        /// <summary>
        /// The height mail comes onto a belt tile at, in half levels: 0 on the ground, 2 an
        /// elevated belt's; a ramp's back tile at its low or high end, its front tile at 1, half way.
        /// </summary>
        public int InLevel(int tile)
        {
            var c = BeltOn(tile);
            if (c == null) return 0;
            if (!c.Def.Ramp) return 2 * c.Def.Level;
            int i = c.IndexOf(tile % _map.Cols, tile / _map.Cols);
            return RampUp(c) ? i : 2 - i;
        }

        /// <summary>The height mail leaves a belt tile at, in half levels.</summary>
        public int OutLevel(int tile)
        {
            var c = BeltOn(tile);
            if (c == null) return 0;
            if (!c.Def.Ramp) return 2 * c.Def.Level;
            int i = c.IndexOf(tile % _map.Cols, tile / _map.Cols);
            return RampUp(c) ? i + 1 : 1 - i;
        }

        /// <summary>A ramp turned 0–3 runs up the way it faces, 4–7 down.</summary>
        public static bool RampUp(Construct c) => c.Rotation < 4;

        /// <summary>Metres above a belt's ordinary running surface at a point <paramref name="pos"/> metres along a segment.</summary>
        public float HeightAt(BeltSegment s, float pos)
        {
            int k = Math.Clamp((int)MathF.Floor(pos / Tile), 0, s.Tiles.Count - 1);
            float t = Math.Clamp(pos - k * Tile, 0f, Tile) / Tile;
            int tile = s.Tiles[k];
            return (InLevel(tile) + (OutLevel(tile) - InLevel(tile)) * t) * 0.5f * LevelHeight;
        }

        /// <summary>The direction from one tile to its neighbour; −1 if they are not neighbours.</summary>
        private int DirTo(int from, int to)
        {
            if (from < 0 || to < 0) return -1;
            int dx = to % _map.Cols - from % _map.Cols, dz = to / _map.Cols - from / _map.Cols;
            for (int d = 0; d < 4; d++) if (Dirs[d].Dx == dx && Dirs[d].Dz == dz) return d;
            return -1;
        }

        private readonly List<int> _outs = new List<int>();

        /// <summary>True if the belt on <paramref name="from"/> hands on to the tile it faces (a splitter: to the first of its outputs that takes it).</summary>
        private bool Feeds(int from, out int to)
        {
            to = -1;
            Outputs(from, _outs);
            foreach (int o in _outs)
            {
                if (!Accepts(from, o)) continue;
                to = o;
                return true;
            }
            return false;
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
                if (IsBelt(c) || c.Def.Behaviour == BuildingBehaviour.Sorter || c.Def.Behaviour == BuildingBehaviour.Inserter
                    || c.Def.PipeRole == PipeRole.Inlet || c.Def.PipeRole == PipeRole.Outlet)
                    sig.Append(c.Tx).Append(',').Append(c.Tz).Append(',').Append(c.Rotation).Append(',').Append(c.Def.Index).Append(';');
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
            {
                if (!IsBelt(c)) continue;
                // A ramp's two tiles are both belt.
                for (int i = 0; i < c.Def.Length; i++)
                {
                    var (x, z) = c.TileAt(i);
                    if (_map.InBounds(x, z) && _field.At(x, z) == c) belts.Add(z * _map.Cols + x);
                }
            }
            belts.Sort();
            var feeders = new Dictionary<int, int>();
            foreach (int t in belts)
            {
                Outputs(t, _outs);
                foreach (int o in _outs)
                    if (Accepts(t, o)) feeders[o] = feeders.TryGetValue(o, out int n) ? n + 1 : 1;
            }

            // The sorters: those taken down give their buffers back; a belt a port feeds is a head.
            var sorters = new List<SorterNode>();
            foreach (var c in _field.All)
            {
                if (c.Def.Behaviour != BuildingBehaviour.Sorter) continue;
                var node = SorterOf(c) ?? new SorterNode(c);
                Array.Clear(node.Outs, 0, node.Outs.Length);
                sorters.Add(node);
                for (int port = 0; port < SorterNode.Ports; port++)
                {
                    int o = PortTile(c, port);
                    if (o >= 0 && AcceptsFrom(SorterNode.Port(port, c.Facing).Dir, o, 0)) feeders[o] = feeders.TryGetValue(o, out int n) ? n + 1 : 1;
                }
            }
            // An inserter (or a pipe outlet) puts mail on the head of the belt in front of it: that belt starts a segment.
            foreach (var c in _field.All)
            {
                if (c.Def.Behaviour != BuildingBehaviour.Inserter && c.Def.PipeRole != PipeRole.Outlet) continue;
                int o = Ahead(TileIndex(c.Tx, c.Tz), c.Facing, c.Def.Reach);
                if (o >= 0 && AcceptsFrom(c.Facing, o, 0)) feeders[o] = feeders.TryGetValue(o, out int n) ? n + 1 : 1;
            }
            foreach (var node in _sorters)
            {
                if (sorters.Contains(node)) continue;
                foreach (var mail in node.Buffer) orphans?.Add(new BeltItem { Mail = mail });
                node.Buffer.Clear();
            }
            _sorters.Clear();
            _sorters.AddRange(sorters);

            // A belt continues the segment of the belt behind it if that belt is its only feeder and
            // of its tier; a splitter or a merger is a segment of its own.
            var from = new Dictionary<int, int>();
            foreach (int t in belts)
            {
                var a = BeltOn(t)!;
                if (a.Def.Behaviour != BuildingBehaviour.Belt || !Feeds(t, out int to) || feeders[to] != 1) continue;
                if (a.Def != BeltOn(to)!.Def) continue;
                from[to] = t;
            }
            foreach (int t in belts)
                if (!from.ContainsKey(t)) Walk(t, from);
            // What is left are loops: each one's head is its lowest tile.
            foreach (int t in belts)
                if (_segOf[t] == null) Walk(t, from);

            foreach (var s in _segments)
            {
                int lastTile = s.Tiles[s.Tiles.Count - 1];
                var last = BeltOn(lastTile)!;
                s.FrontTile = FrontOf(lastTile, last.Rotation);
                if (last.Def.Behaviour == BuildingBehaviour.Splitter)
                {
                    // Doc 04 §2.3: up to three outputs, round-robin.
                    s.Splitter = last;
                    Outputs(lastTile, _outs);
                    for (int port = 0; port < _outs.Count; port++)
                    {
                        if (!Accepts(lastTile, _outs[port]) || _segOf[_outs[port]] is not { } outSeg) continue;
                        s.Outs.Add(outSeg);
                        s.OutPorts.Add(port);
                    }
                    if (s.Outs.Count > 0) s.End = BeltEndKind.Split;
                }
                else if (Feeds(lastTile, out int to))
                {
                    s.End = BeltEndKind.Belt;
                    s.Next = _segOf[to];
                    // A merger takes its inputs in turn: it knows them, back, left, right.
                    if (s.Next != null && BeltOn(s.Next.Head)!.Def.Behaviour == BuildingBehaviour.Merger) s.Next.Ins.Add(s);
                }
                // An Address Sorter takes it at its input: into the tile it was built on, from behind, on the ground.
                else if (s.FrontTile >= 0 && OutLevel(lastTile) == 0 && _field.At(s.FrontTile % _map.Cols, s.FrontTile / _map.Cols) is { Def.Behaviour: BuildingBehaviour.Sorter } sorter
                    && TileIndex(sorter.Tx, sorter.Tz) == s.FrontTile && (last.Rotation & 3) == sorter.Facing)
                {
                    s.End = BeltEndKind.Sorter;
                    s.Sorter = SorterOf(sorter);
                }
                // A Depot (doc 04 §2.6 "belts can feed directly on any side"), from a belt on the ground.
                else if (s.FrontTile >= 0 && OutLevel(lastTile) == 0 && _field.At(s.FrontTile % _map.Cols, s.FrontTile / _map.Cols) is { ContainerId: > 0 } store
                    && store.Def.Container?.BeltAccess == "any_side")
                {
                    s.End = BeltEndKind.Container;
                    s.ContainerId = store.ContainerId;
                }
                // A pipe inlet takes it from behind, from a belt on the ground (doc 04 §2.2).
                else if (s.FrontTile >= 0 && OutLevel(lastTile) == 0 && _field.At(s.FrontTile % _map.Cols, s.FrontTile / _map.Cols) is { Def.PipeRole: PipeRole.Inlet } inlet
                    && (last.Rotation & 3) == inlet.Facing)
                {
                    s.End = BeltEndKind.Inlet;
                }
                // A mailbox takes mail from a belt on the ground, not from one running over it.
                else if (s.FrontTile >= 0 && OutLevel(lastTile) == 0 && _mailboxAt.TryGetValue(s.FrontTile, out int dest))
                {
                    s.End = BeltEndKind.Mailbox;
                    s.DestId = dest;
                }
                // The Intake's outfeed (doc 04 §2.6): a belt on its tile, facing away from the counter, is fed.
                var head = BeltOn(s.Head)!;
                s.FromIntake = !feeders.ContainsKey(s.Head) && s.Head == OutfeedTile && (head.Rotation & 3) == OutfeedRotation && InLevel(s.Head) == 0;
            }

            foreach (var node in _sorters)
                for (int port = 0; port < SorterNode.Ports; port++)
                {
                    int o = PortTile(node.Built, port);
                    if (o >= 0 && AcceptsFrom(SorterNode.Port(port, node.Built.Facing).Dir, o, 0)) node.Outs[port] = _segOf[o];
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
                    // Its rear may still hang behind the head (it has just come on); its front may not.
                    if (it.Pos < -1e-4f) { orphans?.Add(it); s.Items.RemoveAt(i--); continue; }
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
            string.Join(",", s.Tiles) + "|" + s.End + "|" + (s.Next?.Head ?? -1) + "|" + string.Join(",", s.Outs.ConvertAll(o => o.Head)) + "|" + s.DestId + "|" + s.Speed + "|" + s.FromIntake + "|" + (s.Sorter != null ? s.Sorter.Built.Tx * 1000 + s.Sorter.Built.Tz : -1) + "|" + s.ContainerId;

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
                bool pushed = s.PushTicks > 0;
                if (pushed) s.PushTicks--;
                if (s.Items.Count == 0) continue;
                float step = s.Speed * speedMult * (pushed ? PushMultiplier : 1f) * dt;
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
            foreach (var node in _sorters) StepSorter(node, sink, tick);
        }

        /// <summary>Doc 04 §2.4: one piece at a time, at the sorter's rate, out by the first filter it matches.</summary>
        private void StepSorter(SorterNode node, IBeltSink sink, uint tick)
        {
            if (node.Buffer.Count == 0 || tick < node.NextTick) return;
            var mail = node.Buffer[0];
            int port = node.PortFor(mail);
            // Its way out is full, or there is none: it waits, and the buffer fills behind it.
            if (port < 0 || !TryInsert(node.Outs[port]!, mail, out var item)) return;
            node.Buffer.RemoveAt(0);
            if (port == SorterNode.Overflow) node.Overflowed++;
            else node.Sorted++;
            node.NextTick = tick + Math.Max(1u, SimConstants.SecondsToTicks(1.0 / node.Built.Def.Throughput));
            sink.Sorted(node, port, node.Outs[port]!, item);
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
                    {
                        var next = s.Next;
                        byte lane = EntryLane(s, next, it.Lane);
                        if (next == s && s.Items.Count > 1)
                        {
                            // Round a loop: the lane behind it must have room for it at the head.
                            float rear = s.Length;
                            for (int k = 0; k < s.Items.Count; k++)
                                if (k != i && SharesLane(s.Items[k].Lane, lane)) rear = Math.Min(rear, s.Items[k].Pos - s.Items[k].Length);
                            if (rear < it.Length - 1e-4f) break;
                        }
                        else if (next != s && !next.HasRoom(lane, it.Length)) break;
                        // A merger (doc 04 §2.3) takes its inputs in turn: another's turn holds this one
                        // back if that one has mail waiting at its end.
                        int input = next.Ins.IndexOf(s);
                        if (input >= 0 && next.Ins.Count > 1)
                        {
                            var turn = next.Ins[next.NextIn % next.Ins.Count];
                            if (turn != s && Waiting(turn)) break;
                            next.NextIn = (input + 1) % next.Ins.Count;
                        }
                        Hand(s, i, it, next, lane, sink);
                        gone = true;
                        break;
                    }
                    case BeltEndKind.Sorter when s.Sorter != null:
                    {
                        var node = s.Sorter;
                        if (node.Buffer.Count >= node.Built.Def.BufferSize) break;
                        s.Items.RemoveAt(i);
                        ItemCount--;
                        node.Buffer.Add(it.Mail);
                        sink.Took(s, it);
                        gone = true;
                        break;
                    }
                    case BeltEndKind.Split:
                    {
                        // Round-robin over the outputs, skipping those with no room (doc 04 §2.3);
                        // with filters, over those that catch it, else over those that have none.
                        bool caught = Caught(s, it.Mail);
                        for (int k = 0; k < s.Outs.Count && !gone; k++)
                        {
                            int turn = caught || s.Splitter == null ? s.NextOut : s.NextOutRest;
                            int o = (turn + k) % s.Outs.Count;
                            if (!Takes(s, o, it.Mail, caught)) continue;
                            var next = s.Outs[o];
                            byte lane = EntryLane(s, next, it.Lane);
                            if (!next.HasRoom(lane, it.Length)) continue;
                            if (caught || s.Splitter == null) s.NextOut = (o + 1) % s.Outs.Count;
                            else s.NextOutRest = (o + 1) % s.Outs.Count;
                            Hand(s, i, it, next, lane, sink);
                            gone = true;
                        }
                        break;
                    }
                    case BeltEndKind.None:
                        // Doc 04 §3.1: a parked truck's rear on the tile in front loads from a belt on the ground.
                        if (s.FrontTile < 0 || OutLevel(s.Tiles[s.Tiles.Count - 1]) != 0 || !sink.TryLoad(s, it)) break;
                        s.Items.RemoveAt(i);
                        ItemCount--;
                        gone = true;
                        break;
                    case BeltEndKind.Inlet:
                        if (!sink.TryInlet(s, it)) break;
                        s.Items.RemoveAt(i);
                        ItemCount--;
                        gone = true;
                        break;
                    case BeltEndKind.Container:
                        if (!sink.TryStore(s, it)) break;
                        s.Items.RemoveAt(i);
                        ItemCount--;
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

        // --- inserters (doc 04 §2.5) ---------------------------------------------------------------

        /// <summary>The tile <paramref name="reach"/> tiles on from <paramref name="tile"/> the way <paramref name="rotation"/> faces; -1 off the map.</summary>
        private int Ahead(int tile, int rotation, int reach)
        {
            for (int i = 0; i < reach && tile >= 0; i++) tile = FrontOf(tile, rotation);
            return tile;
        }

        /// <summary>
        /// The segment an inserter on (tx, tz) facing <paramref name="facing"/> puts mail on: the
        /// one headed at the tile it reaches in front (a Long Inserter's two on), on the ground,
        /// taking mail from that way; null for none.
        /// </summary>
        public BeltSegment? FedBy(int tx, int tz, int facing, int reach = 1)
        {
            int o = Ahead(TileIndex(tx, tz), facing, reach);
            if (o < 0 || !AcceptsFrom(facing & 3, o, 0)) return null;
            var s = _segOf[o];
            return s != null && s.Head == o ? s : null;
        }

        /// <summary>
        /// The segment whose end an inserter on (tx, tz) facing <paramref name="facing"/> takes
        /// from: the belt behind it running into it, or for a Long Inserter the belt two behind
        /// running into the tile between.
        /// </summary>
        public BeltSegment? EndingAt(int tx, int tz, int facing, int reach = 1)
        {
            int self = TileIndex(tx, tz);
            int into = Ahead(self, (facing + 2) & 3, reach - 1);
            int back = Ahead(self, (facing + 2) & 3, reach);
            if (back < 0) return null;
            var s = _segOf[back];
            return s != null && s.Tiles[s.Tiles.Count - 1] == back && s.FrontTile == into && OutLevel(back) == 0 ? s : null;
        }

        /// <summary>The piece waiting at a segment's very end, if one is.</summary>
        public static bool FrontWaiting(BeltSegment s, out BeltItem item)
        {
            item = default;
            if (!Waiting(s)) return false;
            item = s.Items[0];
            return true;
        }

        /// <summary>Takes a segment's front piece off it (an inserter's grab).</summary>
        public void TakeFront(BeltSegment s)
        {
            s.Items.RemoveAt(0);
            ItemCount--;
        }

        /// <summary>A splitter: whether one of its outputs with a belt has a filter that catches the piece.</summary>
        private static bool Caught(BeltSegment s, in MailItem mail)
        {
            if (s.Splitter == null) return false;
            foreach (int port in s.OutPorts)
                if (s.Splitter.Filter(port, 0).Matches(mail)) return true;
            return false;
        }

        /// <summary>A splitter's output takes a piece: one its filter catches, or, when none does, one with no filter.</summary>
        private static bool Takes(BeltSegment s, int o, in MailItem mail, bool caught)
        {
            if (s.Splitter == null) return true;
            var filter = s.Splitter.Filter(s.OutPorts[o], 0);
            return caught ? filter.Matches(mail) : filter.IsEmpty;
        }

        private static void Hand(BeltSegment s, int i, BeltItem it, BeltSegment next, byte lane, IBeltSink sink)
        {
            s.Items.RemoveAt(i);
            it.Pos = 0f;
            it.Lane = lane;
            next.Items.Add(it);
            SortItems(next);
            sink.Moved(s, next, it);
        }

        /// <summary>True if a segment has mail at its very end.</summary>
        private static bool Waiting(BeltSegment s) => s.Items.Count > 0 && s.Items[0].Pos >= s.Length - 1e-4f;

        /// <summary>
        /// The lane mail goes on at the next segment's head: its own coming in straight (or round a
        /// corner of the same run), the lane on the side it comes from when it comes in from the
        /// side (design choice 94). Cargo takes both lanes wherever it goes.
        /// </summary>
        public byte EntryLane(BeltSegment from, BeltSegment to, byte lane)
        {
            if (lane == LaneBoth) return lane;
            var head = BeltOn(to.Head);
            int f = DirTo(from.Tiles[from.Tiles.Count - 1], to.Head);
            if (head == null || f < 0) return lane;
            int d = head.Rotation & 3;
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

        /// <summary>A carrier pushes the segment a tile is on (doc 04 §2.1): it runs faster for <see cref="PushTickCount"/> ticks; null without a belt there.</summary>
        public BeltSegment? Push(int tx, int tz)
        {
            var s = SegmentAt(tx, tz);
            if (s != null) s.PushTicks = Math.Max(s.PushTicks, PushTickCount);
            return s;
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
            foreach (var node in _sorters)
            {
                foreach (var mail in node.Buffer) all.Add(new BeltItem { Mail = mail });
                node.Buffer.Clear();
                node.Sorted = node.Overflowed = 0;
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
