using System.Collections.Immutable;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Balance;

/// <summary>
/// The code-side balance key registry (GDD CD9, ADR-0010 section 4): the closed set of keys with owner, unit and hard
/// range, held in ordinal <c>owner.key</c> order. A key's <see cref="BalanceKey"/> handle is its row position plus this
/// registry's <see cref="Stamp"/>, so a <see cref="BalanceTable"/> reads values by array index and refuses a handle from
/// another registry. Immutable and thread-safe. The production instance is <see cref="BalanceKeys.CreateRegistry"/>;
/// tests build their own from rows (for example to add a <c>player</c> row before story 008 registers them).
/// </summary>
/// <example>
/// <code>
/// var registry = new BalanceRegistry(ImmutableArray.Create(
///     new BalanceRow("sim", "warmupTicks", BalanceUnit.Ticks, 0, 100_000),
///     new BalanceRow("player", "rangeMm", BalanceUnit.Mm, 100, 20_000)));
/// BalanceKey key = registry.Find("player.rangeMm");   // Index 0: "player.*" sorts before "sim.*"
/// </code>
/// </example>
public sealed class BalanceRegistry
{
    private readonly ImmutableArray<BalanceRow> _rows;
    private readonly ImmutableArray<string> _names;
    private readonly Dictionary<string, int> _byName;

    /// <summary>Sorts <paramref name="rows"/> into ordinal <c>owner.key</c> order and validates them.</summary>
    /// <param name="rows">The rows in any order.</param>
    /// <exception cref="ArgumentException">A row has an empty owner or key, an owner containing a dot, a hard range with min above max, or the same <c>owner.key</c> appears twice.</exception>
    public BalanceRegistry(ImmutableArray<BalanceRow> rows)
    {
        int n = rows.IsDefault ? 0 : rows.Length;
        var sorted = new Keyed[n];
        for (int i = 0; i < n; i++)
        {
            ValidateRow(rows[i]);
            sorted[i] = new Keyed(rows[i].Name, rows[i]);
        }

        StableSort.Sort(sorted.AsSpan(), new Keyed[n].AsSpan(), new ByName());
        _byName = new Dictionary<string, int>(n, StringComparer.Ordinal);
        var names = new string[n];
        var ordered = new BalanceRow[n];
        for (int i = 0; i < n; i++)
        {
            if (!_byName.TryAdd(sorted[i].Name, i))
            {
                throw new ArgumentException("Balance key registered twice: " + sorted[i].Name, nameof(rows));
            }

            names[i] = sorted[i].Name;
            ordered[i] = sorted[i].Row;
        }

        _rows = ImmutableArray.Create(ordered);
        _names = ImmutableArray.Create(names);
        Stamp = ComputeStamp(names);
    }

    private readonly struct Keyed
    {
        public Keyed(string name, BalanceRow row)
        {
            Name = name;
            Row = row;
        }

        public string Name { get; }

        public BalanceRow Row { get; }
    }

    private readonly struct ByName : IComparer<Keyed>
    {
        public int Compare(Keyed x, Keyed y) => string.CompareOrdinal(x.Name, y.Name);
    }

    // FNV-1a over the UTF-16 code units of every name (ordinal order, '\n' between names), never zero. Deterministic on
    // every platform; deliberately not string.GetHashCode (randomised per process, banned by ADR-0006 rule 6).
    private static int ComputeStamp(string[] names)
    {
        uint h = 2166136261u;
        foreach (string name in names)
        {
            foreach (char c in name)
            {
                h = unchecked((h ^ c) * 16777619u);
            }

            h = unchecked((h ^ '\n') * 16777619u);
        }

        int stamp = unchecked((int)h);
        return stamp == 0 ? 1 : stamp;
    }

    private static void ValidateRow(BalanceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (string.IsNullOrEmpty(row.Owner) || row.Owner.Contains('.') || string.IsNullOrEmpty(row.Key))
        {
            throw new ArgumentException("A balance row needs a dot-free owner and a non-empty key.", nameof(row));
        }

        if (row.HardMin > row.HardMax)
        {
            throw new ArgumentException("Balance row " + row.Name + " has a hard minimum above its maximum.", nameof(row));
        }
    }

    /// <summary>The rows in ordinal <c>owner.key</c> order; the row at position <c>i</c> has handle index <c>i</c>. Story 013 hashes in this order.</summary>
    public ImmutableArray<BalanceRow> Rows => _rows;

    /// <summary>The full <c>owner.key</c> names, parallel to <see cref="Rows"/> (cached; no allocation per read).</summary>
    public ImmutableArray<string> Names => _names;

    /// <summary>The number of registered keys.</summary>
    public int Count => _rows.Length;

    /// <summary>
    /// A deterministic, never-zero identity of this registry's key layout (FNV-1a over the ordinal names). Handles carry
    /// it; a table accepts only handles with its registry's stamp.
    /// </summary>
    public int Stamp { get; }

    /// <summary>True when <paramref name="key"/> was issued by a registry with this layout (never true for <see cref="BalanceKey.None"/>).</summary>
    /// <param name="key">A handle.</param>
    public bool Owns(BalanceKey key) => key.Stamp == Stamp;

    /// <summary>The row for <paramref name="key"/>.</summary>
    /// <param name="key">A handle from this registry.</param>
    /// <exception cref="ArgumentException">The handle is <see cref="BalanceKey.None"/> or from another registry.</exception>
    public BalanceRow this[BalanceKey key] =>
        Owns(key) ? _rows[key.Index] : throw new ArgumentException("The handle was not issued by this registry.", nameof(key));

    /// <summary>Looks a key up by its full <c>owner.key</c> name. Not for per-tick code.</summary>
    /// <param name="name">For example <c>sim.warmupTicks</c>.</param>
    /// <param name="key">The handle when found; <see cref="BalanceKey.None"/> otherwise.</param>
    /// <returns>True when registered.</returns>
    public bool TryFind(string name, out BalanceKey key)
    {
        if (name is not null && _byName.TryGetValue(name, out int index))
        {
            key = BalanceKey.FromIndex(index, Stamp);
            return true;
        }

        key = BalanceKey.None;
        return false;
    }

    /// <summary>Looks a key up by its full name; throws when unregistered (composition-time use).</summary>
    /// <param name="name">For example <c>sim.warmupTicks</c>.</param>
    /// <exception cref="KeyNotFoundException">No such key.</exception>
    public BalanceKey Find(string name) =>
        TryFind(name, out BalanceKey key) ? key : throw new KeyNotFoundException("Unregistered balance key: " + name);
}
