using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Automation
{
    /// <summary>
    /// One piece of mail in a capsule (doc 04 §2.2): sent by an inlet to one outlet, along the
    /// tiles of its path at the pipes' speed.
    /// </summary>
    public sealed class PipeCapsule
    {
        public MailItem Mail;
        /// <summary>The tile of the outlet it was sent to.</summary>
        public int Outlet;
        /// <summary>Tile indices from where it set out to its outlet's tile.</summary>
        public readonly List<int> Path = new List<int>();
        /// <summary>Metres along <see cref="Path"/>, from the middle of its first tile.</summary>
        public float Travelled;
        public float Length => (Path.Count - 1) * SimConstants.TileSize;
        public bool Arrived => Travelled >= Length;
        /// <summary>The tile it is nearest the middle of.</summary>
        public int Tile => Path[Math.Clamp((int)(Travelled / SimConstants.TileSize + 0.5f), 0, Path.Count - 1)];
    }

    /// <summary>A Pipe Outlet (doc 04 §2.2): the capsules that reached it, oldest first, and those on their way.</summary>
    public sealed class OutletNode
    {
        public Construct Built { get; }
        public int Tile { get; }
        /// <summary>Mail out of the capsules that reached it, waiting to go out in front.</summary>
        public readonly List<MailItem> Queue = new List<MailItem>();
        /// <summary>Capsules on their way to it.</summary>
        public int InFlight;
        /// <summary>The first tick it puts the next piece out.</summary>
        public uint NextTick;
        /// <summary>Every piece it holds or is sent: an inlet sends no more than its capacity.</summary>
        public int Reserved => Queue.Count + InFlight;

        public OutletNode(Construct built, int tile) { Built = built; Tile = tile; }
    }

    /// <summary>A Pipe Inlet (doc 04 §2.2): it sends a capsule at a time, a capsule's spacing apart.</summary>
    public sealed class InletNode
    {
        public Construct Built { get; }
        public int Tile { get; }
        /// <summary>The first tick it may send the next capsule.</summary>
        public uint NextTick;
        public int Sent;

        public InletNode(Construct built, int tile) { Built = built; Tile = tile; }
    }

    /// <summary>
    /// Every pneumatic pipe of the map (doc 04 §2.2): a graph of pipes, junctions, underground
    /// pipes, inlets and outlets, two tiles joined where both are open towards each other. An inlet
    /// sends each piece of mail in a capsule to one outlet it reaches; the capsule runs the
    /// shortest way there at <see cref="Speed"/> and waits in the outlet until it is put out.
    /// Nothing runs into anything in a pipe: an inlet sends no more to an outlet than it can hold
    /// (<see cref="Capacity"/>), and no faster than a capsule's spacing.
    /// Plain C#: the server routes and puts out; a client runs the same code for the look, the
    /// server's events placing and taking off its capsules.
    /// </summary>
    public sealed class PipeNetwork
    {
        private readonly WorldMap _map;
        private readonly ConstructField _field;
        private int _fieldVersion = -1;
        private string _signature = "";
        private readonly List<PipeCapsule> _capsules = new List<PipeCapsule>();
        private readonly Dictionary<Construct, OutletNode> _outlets = new Dictionary<Construct, OutletNode>();
        private readonly Dictionary<Construct, InletNode> _inlets = new Dictionary<Construct, InletNode>();
        private readonly Dictionary<int, OutletNode> _outletAt = new Dictionary<int, OutletNode>();
        private readonly Dictionary<int, InletNode> _inletAt = new Dictionary<int, InletNode>();
        /// <summary>Per outlet tile: tiles to go from each pipe tile that reaches it (a breadth-first count), made when first asked.</summary>
        private readonly Dictionary<int, Dictionary<int, int>> _distance = new Dictionary<int, Dictionary<int, int>>();

        /// <summary>Metres a second (doc 11: 5).</summary>
        public float Speed { get; }
        /// <summary>Metres between capsules an inlet sends (doc 11: 1).</summary>
        public float Spacing { get; }
        /// <summary>Pieces an outlet holds and is sent at most.</summary>
        public int Capacity { get; }

        public IReadOnlyList<PipeCapsule> Capsules => _capsules;
        public IEnumerable<OutletNode> Outlets => _outlets.Values;
        public IEnumerable<InletNode> Inlets => _inlets.Values;

        public PipeNetwork(ContentDb content, WorldMap map, ConstructField field)
        {
            _map = map;
            _field = field;
            Speed = (float)content.Balance.GetDouble("pipeSpeed");
            Spacing = (float)content.Balance.GetDouble("pipeSpacing");
            Capacity = content.Balance.GetInt("pipeOutletCapacity");
        }

        public static bool IsPipe(Construct? c) => c != null && c.Def.Behaviour == BuildingBehaviour.Pipe;

        /// <summary>Whether a piece of pipe is open towards <paramref name="dir"/> (0 east, 1 south, 2 west, 3 north).</summary>
        public static bool Opens(Construct c, int dir)
        {
            int f = c.Facing & 3;
            switch (c.Def.PipeRole)
            {
                case PipeRole.Pipe:
                case PipeRole.Underground: return (dir & 1) == (f & 1);
                case PipeRole.Junction: return true;
                case PipeRole.Inlet: return dir == f;
                case PipeRole.Outlet: return dir == ((f + 2) & 3);
                default: return false;
            }
        }

        private int TileIndex(int tx, int tz) => tz * _map.Cols + tx;

        private int Neighbour(int tile, int dir)
        {
            int tx = tile % _map.Cols + BeltNetwork.Dirs[dir].Dx, tz = tile / _map.Cols + BeltNetwork.Dirs[dir].Dz;
            return _map.InBounds(tx, tz) ? TileIndex(tx, tz) : -1;
        }

        private Construct? PipeOn(int tile) => tile < 0 ? null : _field.At(tile % _map.Cols, tile / _map.Cols) is { } c && IsPipe(c) ? c : null;

        /// <summary>Whether the pipes on <paramref name="tile"/> and the tile <paramref name="dir"/> of it are joined.</summary>
        public bool Joined(int tile, int dir)
        {
            var a = PipeOn(tile);
            int n = Neighbour(tile, dir);
            var b = PipeOn(n);
            return a != null && b != null && Opens(a, dir) && Opens(b, (dir + 2) & 3);
        }

        public OutletNode? OutletAt(int tile) => _outletAt.TryGetValue(tile, out var o) ? o : null;
        public InletNode? InletAt(int tile) => _inletAt.TryGetValue(tile, out var i) ? i : null;
        public OutletNode? OutletOf(Construct c) => _outlets.TryGetValue(c, out var o) ? o : null;
        public InletNode? InletOf(Construct c) => _inlets.TryGetValue(c, out var i) ? i : null;

        /// <summary>
        /// After building: the nodes as they stand now. A capsule whose way is broken is sent on
        /// from the tile it is at by the shortest way that is left (into <paramref name="rerouted"/>),
        /// or, with its outlet gone or out of reach, comes out with the mail of the outlets taken
        /// down (into <paramref name="orphans"/>). True if anything was compiled.
        /// </summary>
        public bool Compile(List<PipeCapsule>? rerouted = null, List<MailItem>? orphans = null)
        {
            if (_field.Version == _fieldVersion) return false;
            _fieldVersion = _field.Version;
            var sig = new System.Text.StringBuilder();
            foreach (var c in _field.All)
                if (IsPipe(c)) sig.Append(c.Tx).Append(',').Append(c.Tz).Append(',').Append(c.Rotation).Append(',').Append(c.Def.Index).Append(';');
            string signature = sig.ToString();
            if (signature == _signature) return false;
            _signature = signature;
            _distance.Clear();

            var outlets = new Dictionary<Construct, OutletNode>();
            var inlets = new Dictionary<Construct, InletNode>();
            foreach (var c in _field.All)
            {
                if (c.Def.PipeRole == PipeRole.Outlet) outlets[c] = _outlets.TryGetValue(c, out var o) ? o : new OutletNode(c, TileIndex(c.Tx, c.Tz));
                else if (c.Def.PipeRole == PipeRole.Inlet) inlets[c] = _inlets.TryGetValue(c, out var i) ? i : new InletNode(c, TileIndex(c.Tx, c.Tz));
            }
            foreach (var (c, o) in _outlets)
            {
                if (outlets.ContainsKey(c)) continue;
                orphans?.AddRange(o.Queue);
                o.Queue.Clear();
            }
            _outlets.Clear();
            _outletAt.Clear();
            foreach (var (c, o) in outlets)
            {
                _outlets[c] = o;
                _outletAt[o.Tile] = o;
                o.InFlight = 0;
            }
            _inlets.Clear();
            _inletAt.Clear();
            foreach (var (c, i) in inlets)
            {
                _inlets[c] = i;
                _inletAt[i.Tile] = i;
            }

            for (int k = 0; k < _capsules.Count; k++)
            {
                var cap = _capsules[k];
                if (StillGoes(cap))
                {
                    _outletAt[cap.Outlet].InFlight++;
                    continue;
                }
                int at = cap.Tile;
                if (_outletAt.TryGetValue(cap.Outlet, out var outlet) && Distance(cap.Outlet, at) >= 0)
                {
                    SetPath(cap, at, cap.Outlet);
                    outlet.InFlight++;
                    rerouted?.Add(cap);
                    continue;
                }
                orphans?.Add(cap.Mail);
                _capsules.RemoveAt(k--);
            }
            return true;
        }

        /// <summary>Its outlet stands, and every step of its way from where it is on is still joined.</summary>
        private bool StillGoes(PipeCapsule cap)
        {
            if (!_outletAt.ContainsKey(cap.Outlet)) return false;
            int from = Math.Max(0, (int)(cap.Travelled / SimConstants.TileSize));
            for (int i = from; i < cap.Path.Count; i++)
            {
                if (PipeOn(cap.Path[i]) == null) return false;
                if (i + 1 < cap.Path.Count && !JoinedTo(cap.Path[i], cap.Path[i + 1])) return false;
            }
            return true;
        }

        private bool JoinedTo(int a, int b)
        {
            for (int d = 0; d < 4; d++)
                if (Neighbour(a, d) == b) return Joined(a, d);
            return false;
        }

        /// <summary>Tiles to go from <paramref name="tile"/> to the outlet on <paramref name="outletTile"/> through the pipes; −1 if it does not reach it.</summary>
        public int Distance(int outletTile, int tile)
        {
            if (!_distance.TryGetValue(outletTile, out var field))
            {
                field = new Dictionary<int, int>();
                if (PipeOn(outletTile) != null)
                {
                    var queue = new Queue<int>();
                    field[outletTile] = 0;
                    queue.Enqueue(outletTile);
                    while (queue.Count > 0)
                    {
                        int t = queue.Dequeue();
                        for (int d = 0; d < 4; d++)
                        {
                            if (!Joined(t, d)) continue;
                            int n = Neighbour(t, d);
                            if (field.ContainsKey(n)) continue;
                            field[n] = field[t] + 1;
                            // A capsule goes through pipes, junctions and underground pipes; an inlet or another outlet is an end.
                            var role = PipeOn(n)!.Def.PipeRole;
                            if (role != PipeRole.Inlet && role != PipeRole.Outlet) queue.Enqueue(n);
                        }
                    }
                }
                _distance[outletTile] = field;
            }
            return field.TryGetValue(tile, out int dist) ? dist : -1;
        }

        /// <summary>The shortest way from <paramref name="from"/> to the outlet, the lowest direction first where two are as short.</summary>
        private void SetPath(PipeCapsule cap, int from, int outletTile)
        {
            cap.Path.Clear();
            cap.Travelled = 0f;
            cap.Outlet = outletTile;
            int t = from;
            cap.Path.Add(t);
            int left = Distance(outletTile, t);
            while (left > 0)
            {
                for (int d = 0; d < 4; d++)
                {
                    if (!Joined(t, d)) continue;
                    int n = Neighbour(t, d);
                    if (Distance(outletTile, n) != left - 1) continue;
                    t = n;
                    break;
                }
                cap.Path.Add(t);
                left--;
            }
        }

        /// <summary>
        /// Sends a piece of mail in a capsule from <paramref name="fromTile"/> (an inlet's, or where
        /// the server says one is) to the outlet on <paramref name="outletTile"/>; null if no pipe
        /// leads there. A capsule of the same mail already under way is replaced.
        /// </summary>
        public PipeCapsule? Send(in MailItem mail, int fromTile, int outletTile)
        {
            if (Distance(outletTile, fromTile) < 0) return null;
            RemoveMail(mail.Id);
            var cap = new PipeCapsule { Mail = mail };
            SetPath(cap, fromTile, outletTile);
            _capsules.Add(cap);
            if (_outletAt.TryGetValue(outletTile, out var o)) o.InFlight++;
            return cap;
        }

        /// <summary>Takes the capsule of a piece of mail out (a client: the server says it is gone). True if it was there.</summary>
        public bool RemoveMail(uint mailId)
        {
            for (int i = 0; i < _capsules.Count; i++)
            {
                if (_capsules[i].Mail.Id != mailId) continue;
                if (_outletAt.TryGetValue(_capsules[i].Outlet, out var o)) o.InFlight = Math.Max(0, o.InFlight - 1);
                _capsules.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Moves every capsule on. With <paramref name="arrive"/> (the server) a capsule at its
        /// outlet leaves the pipe for the outlet's queue; without (a client) it waits there to be
        /// taken off.
        /// </summary>
        public void Step(float dt, bool arrive)
        {
            for (int i = 0; i < _capsules.Count; i++)
            {
                var cap = _capsules[i];
                cap.Travelled = Math.Min(cap.Travelled + Speed * dt, cap.Length);
                if (!arrive || !cap.Arrived) continue;
                if (_outletAt.TryGetValue(cap.Outlet, out var o))
                {
                    o.InFlight = Math.Max(0, o.InFlight - 1);
                    o.Queue.Add(cap.Mail);
                }
                _capsules.RemoveAt(i--);
            }
        }

        /// <summary>Where a capsule is drawn: on the way between the middles of the tiles of its path, and the way it goes.</summary>
        public void PointAt(PipeCapsule cap, float ahead, out float x, out float z, out int dir, out int tile)
        {
            float along = Math.Min(cap.Travelled + ahead, cap.Length);
            int i = Math.Min((int)(along / SimConstants.TileSize), cap.Path.Count - 1);
            float f = along / SimConstants.TileSize - i;
            int a = cap.Path[i], b = i + 1 < cap.Path.Count ? cap.Path[i + 1] : a;
            float ax = (a % _map.Cols + 0.5f) * SimConstants.TileSize, az = (a / _map.Cols + 0.5f) * SimConstants.TileSize;
            float bx = (b % _map.Cols + 0.5f) * SimConstants.TileSize, bz = (b / _map.Cols + 0.5f) * SimConstants.TileSize;
            x = ax + (bx - ax) * f;
            z = az + (bz - az) * f;
            dir = 0;
            int p = b != a ? a : i > 0 ? cap.Path[i - 1] : a, q = b != a ? b : a;
            for (int d = 0; d < 4; d++) if (Neighbour(p, d) == q) dir = d;
            tile = f < 0.5f ? a : b;
        }

        /// <summary>The run is over: every capsule and every outlet's queue out, for the Intake.</summary>
        public List<MailItem> Clear()
        {
            var mail = new List<MailItem>();
            foreach (var cap in _capsules) mail.Add(cap.Mail);
            _capsules.Clear();
            foreach (var o in _outlets.Values)
            {
                mail.AddRange(o.Queue);
                o.Queue.Clear();
                o.InFlight = 0;
            }
            return mail;
        }

        /// <summary>Capsules in the pipes and pieces waiting in outlets.</summary>
        public int ItemCount
        {
            get
            {
                int n = _capsules.Count;
                foreach (var o in _outlets.Values) n += o.Queue.Count;
                return n;
            }
        }
    }
}
