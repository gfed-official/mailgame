using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Inventory
{
    /// <summary>One placed stack in a grid. Rotation is a property of the placement, not the item.</summary>
    public sealed class GridEntry
    {
        public uint EntryId;
        public int X, Y;
        public bool Rotated;
        /// <summary>Footprint in cells after rotation.</summary>
        public int W, H;
        public ItemStack Stack = null!;

        public GridEntry Clone() => new GridEntry
        {
            EntryId = EntryId, X = X, Y = Y, Rotated = Rotated, W = W, H = H, Stack = Stack.Clone(),
        };
    }

    /// <summary>Full container state, sent on open / join (doc 08 §3.4).</summary>
    public sealed class ContainerState
    {
        public uint ContainerId;
        public ushort DefIndex;
        public uint Version;
        public List<GridEntry> Entries = new List<GridEntry>();
        public uint Hash;
    }

    /// <summary>One versioned mutation of a container: the entries that changed and the ids that left.</summary>
    public sealed class ContainerDelta
    {
        public uint ContainerId;
        public uint Version;
        public List<GridEntry> Upserts = new List<GridEntry>();
        public List<uint> Removed = new List<uint>();
        /// <summary>Hash of the whole container after applying this delta; clients verify it on every version.</summary>
        public uint Hash;
    }

    /// <summary>
    /// A rectangular grid inventory (doc 03 §3). Shared by server and client: the client mirrors
    /// containers by applying <see cref="ContainerDelta"/>s and compares hashes to detect desync.
    /// </summary>
    public sealed class GridContainer
    {
        private const uint Blocked = uint.MaxValue;

        public uint Id { get; }
        public ContainerDef Def { get; }
        public int Cols => Def.Cols;
        public int Rows => Def.Rows;
        public uint Version { get; private set; }

        private readonly uint[] _cells;
        private readonly Dictionary<uint, GridEntry> _entries = new Dictionary<uint, GridEntry>();
        private readonly HashSet<uint> _dirty = new HashSet<uint>();
        private readonly HashSet<uint> _removed = new HashSet<uint>();

        public GridContainer(uint id, ContainerDef def)
        {
            Id = id;
            Def = def;
            _cells = new uint[def.Cols * def.Rows];
            foreach (var (x, y) in def.ReservedCells) _cells[y * def.Cols + x] = Blocked;
        }

        public int EntryCount => _entries.Count;
        public IEnumerable<GridEntry> Entries => _entries.Values;

        public GridEntry? Get(uint entryId) => _entries.TryGetValue(entryId, out var e) ? e : null;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Cols && y < Rows;

        /// <summary>Entry occupying a cell, or null if the cell is free, reserved, or out of bounds.</summary>
        public GridEntry? EntryAt(int x, int y)
        {
            if (!InBounds(x, y)) return null;
            uint id = _cells[y * Cols + x];
            return id == 0 || id == Blocked ? null : _entries[id];
        }

        public bool IsReserved(int x, int y) => InBounds(x, y) && _cells[y * Cols + x] == Blocked;

        /// <summary>True if a w×h rectangle at (x, y) is inside the grid and every cell is free (or owned by <paramref name="ignoreEntryId"/>).</summary>
        public bool CanPlace(int x, int y, int w, int h, uint ignoreEntryId = 0)
        {
            if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > Cols || y + h > Rows) return false;
            for (int cy = y; cy < y + h; cy++)
            {
                int row = cy * Cols;
                for (int cx = x; cx < x + w; cx++)
                {
                    uint id = _cells[row + cx];
                    if (id != 0 && (id == Blocked || id != ignoreEntryId)) return false;
                }
            }
            return true;
        }

        /// <summary>First free position for a w×h rectangle scanning row-major.</summary>
        public bool FindFreeSpot(int w, int h, out int x, out int y)
        {
            for (y = 0; y + h <= Rows; y++)
                for (x = 0; x + w <= Cols; x++)
                    if (CanPlace(x, y, w, h)) return true;
            x = y = -1;
            return false;
        }

        public void Add(GridEntry entry)
        {
            if (entry.EntryId == 0 || entry.EntryId == Blocked) throw new ArgumentException("invalid entry id");
            if (_entries.ContainsKey(entry.EntryId)) throw new InvalidOperationException($"entry {entry.EntryId} already in container {Id}");
            if (!CanPlace(entry.X, entry.Y, entry.W, entry.H))
                throw new InvalidOperationException($"entry {entry.EntryId} does not fit at ({entry.X},{entry.Y}) in container {Id}");
            Stamp(entry, entry.EntryId);
            _entries[entry.EntryId] = entry;
            _removed.Remove(entry.EntryId);
            _dirty.Add(entry.EntryId);
        }

        public GridEntry? Remove(uint entryId)
        {
            if (!_entries.TryGetValue(entryId, out var entry)) return null;
            Stamp(entry, 0);
            _entries.Remove(entryId);
            _dirty.Remove(entryId);
            _removed.Add(entryId);
            return entry;
        }

        /// <summary>Repositions an entry inside this container. Caller has validated with CanPlace(ignore: id).</summary>
        public void Reposition(GridEntry entry, int x, int y, bool rotated, int w, int h)
        {
            Stamp(entry, 0);
            entry.X = x; entry.Y = y; entry.Rotated = rotated; entry.W = w; entry.H = h;
            Stamp(entry, entry.EntryId);
            _dirty.Add(entry.EntryId);
        }

        /// <summary>Marks an entry whose stack contents changed in place.</summary>
        public void MarkDirty(uint entryId) => _dirty.Add(entryId);

        public bool HasUncommittedChanges => _dirty.Count > 0 || _removed.Count > 0;

        /// <summary>
        /// Closes one mutation: bumps the version and returns the delta, or null if nothing changed.
        /// </summary>
        public ContainerDelta? Commit()
        {
            if (!HasUncommittedChanges) return null;
            Version++;
            var delta = new ContainerDelta { ContainerId = Id, Version = Version };
            foreach (uint id in _dirty) delta.Upserts.Add(_entries[id].Clone());
            delta.Upserts.Sort((a, b) => a.EntryId.CompareTo(b.EntryId));
            delta.Removed.AddRange(_removed);
            delta.Removed.Sort();
            _dirty.Clear();
            _removed.Clear();
            delta.Hash = ComputeHash();
            return delta;
        }

        /// <summary>Discards change tracking without bumping the version (used by client mirrors and scratch copies).</summary>
        public void ClearChanges()
        {
            _dirty.Clear();
            _removed.Clear();
        }

        public ContainerState Snapshot()
        {
            var state = new ContainerState { ContainerId = Id, DefIndex = Def.Index, Version = Version, Hash = ComputeHash() };
            foreach (var e in _entries.Values) state.Entries.Add(e.Clone());
            state.Entries.Sort((a, b) => a.EntryId.CompareTo(b.EntryId));
            return state;
        }

        /// <summary>Replaces all contents with a full state (client side).</summary>
        public void Load(ContainerState state)
        {
            foreach (var e in _entries.Values) Stamp(e, 0);
            _entries.Clear();
            foreach (var e in state.Entries) Add(e.Clone());
            Version = state.Version;
            ClearChanges();
        }

        /// <summary>
        /// Applies a server delta to a mirror (client side). Returns false if the delta cannot be applied
        /// (wrong version or an entry does not fit), which means the mirror is out of sync.
        /// </summary>
        public bool Apply(ContainerDelta delta)
        {
            if (delta.Version != Version + 1) return false;
            foreach (uint id in delta.Removed) Remove(id);
            foreach (var e in delta.Upserts) Remove(e.EntryId);
            foreach (var e in delta.Upserts)
            {
                if (!CanPlace(e.X, e.Y, e.W, e.H)) { ClearChanges(); return false; }
                Add(e.Clone());
            }
            Version = delta.Version;
            ClearChanges();
            return true;
        }

        public GridContainer Clone()
        {
            var copy = new GridContainer(Id, Def);
            foreach (var e in _entries.Values) copy.Add(e.Clone());
            copy.Version = Version;
            copy.ClearChanges();
            return copy;
        }

        /// <summary>
        /// Order-independent-of-insertion content hash: entries sorted by id, with position, rotation,
        /// stack identity, counts and mail ids/values.
        /// </summary>
        public uint ComputeHash()
        {
            var ids = new List<uint>(_entries.Keys);
            ids.Sort();
            var h = Fnv32.Create();
            h.Add(Cols);
            h.Add(Rows);
            foreach (uint id in ids)
            {
                var e = _entries[id];
                h.Add(e.EntryId);
                h.Add((byte)e.X);
                h.Add((byte)e.Y);
                h.Add((byte)(e.Rotated ? 1 : 0));
                h.Add((byte)e.Stack.Kind);
                h.Add((uint)e.Stack.DefIndex);
                if (e.Stack.Kind == StackKind.Mail)
                {
                    h.Add(e.Stack.Address.Key);
                    h.Add(e.Stack.Address.Unit);
                    h.Add(e.Stack.Mail.Count);
                    foreach (var m in e.Stack.Mail)
                    {
                        h.Add(m.Id);
                        h.Add((uint)m.Value);
                        h.Add(m.SpawnShift);
                        h.Add(m.DeadlineShift);
                        h.Add(m.Flags);
                    }
                }
                else h.Add(e.Stack.ItemCount);
            }
            return h.Value;
        }

        private void Stamp(GridEntry entry, uint value)
        {
            for (int cy = entry.Y; cy < entry.Y + entry.H; cy++)
                for (int cx = entry.X; cx < entry.X + entry.W; cx++)
                    _cells[cy * Cols + cx] = value;
        }
    }
}
