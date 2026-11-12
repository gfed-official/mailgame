using System.Collections.Generic;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    /// <summary>One line of the Intake manifest: all waiting mail of one kind for one address.</summary>
    public struct ManifestRow
    {
        public AddressId Address;
        public ushort Kind;
        /// <summary>The oldest stack of the row, the one a click takes; 0 when the row is empty.</summary>
        public uint FirstEntry;
        public int Stacks;
        public int Items;

        public bool Empty => Items == 0;
    }

    /// <summary>
    /// The rows of the Intake manifest (doc 03 §3.2) in an order that holds still while the panel is
    /// open, so a row never changes under the cursor: when the list is opened the rows are in address
    /// order; mail that arrives for a new address goes to the bottom; a row whose mail is gone stays
    /// where it is, empty, until the list is opened again (empty rows at the very end are dropped).
    /// </summary>
    public sealed class ManifestList
    {
        private readonly List<ManifestRow> _rows = new List<ManifestRow>();
        private readonly Dictionary<(AddressId, ushort), int> _index = new Dictionary<(AddressId, ushort), int>();
        private readonly List<(AddressId Address, ushort Kind)> _new = new List<(AddressId, ushort)>();

        public IReadOnlyList<ManifestRow> Rows => _rows;
        public int TotalItems { get; private set; }

        /// <summary>Forget the order: the next <see cref="Update"/> starts from address order again.</summary>
        public void Reset()
        {
            _rows.Clear();
            _index.Clear();
            TotalItems = 0;
        }

        public void Update(GridContainer container)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                row.FirstEntry = 0;
                row.Stacks = 0;
                row.Items = 0;
                _rows[i] = row;
            }

            _new.Clear();
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Mail && !_index.ContainsKey((e.Stack.Address, e.Stack.DefIndex)) && !_new.Contains((e.Stack.Address, e.Stack.DefIndex)))
                    _new.Add((e.Stack.Address, e.Stack.DefIndex));
            _new.Sort((a, b) => a.Address != b.Address ? a.Address.CompareTo(b.Address) : a.Kind.CompareTo(b.Kind));
            foreach (var key in _new)
            {
                _index[key] = _rows.Count;
                _rows.Add(new ManifestRow { Address = key.Address, Kind = key.Kind });
            }

            TotalItems = 0;
            foreach (var e in container.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                int i = _index[(e.Stack.Address, e.Stack.DefIndex)];
                var row = _rows[i];
                row.FirstEntry = row.Stacks == 0 || e.EntryId < row.FirstEntry ? e.EntryId : row.FirstEntry;
                row.Stacks++;
                row.Items += e.Stack.Count;
                _rows[i] = row;
                TotalItems += e.Stack.Count;
            }

            // Dropping empty rows off the end moves no other row.
            while (_rows.Count > 0 && _rows[_rows.Count - 1].Empty)
            {
                var last = _rows[_rows.Count - 1];
                _index.Remove((last.Address, last.Kind));
                _rows.RemoveAt(_rows.Count - 1);
            }
        }
    }
}
