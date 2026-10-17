namespace PerformativeMail.Sim.Content.Balance;

/// <summary>
/// A handle to one registered balance key: the row's position in its <see cref="BalanceRegistry"/> (rows are in ordinal
/// <c>owner.key</c> order) plus that registry's identity stamp. Per-tick code reads <see cref="BalanceTable.Get"/> with a
/// handle (never a string, ADR-0004 section 6). The position is stored plus 1 so that <c>default</c> is
/// <see cref="None"/> and never means row 0. The stamp is a deterministic hash of the registry's row names, so a handle
/// presented to a table built from a different registry is rejected instead of silently reading another row. A handle
/// is never persisted and its position changes when another owner registers rows; resolve it once, at composition
/// (for example <c>BalanceKeys.Sim.Resolve</c>). Immutable.
/// </summary>
/// <example>
/// <code>
/// SimKeys keys = BalanceKeys.Sim.Resolve(table.Registry);   // once, at composition
/// long capacity = table.Get(keys.OpQueueCapacity);          // O(1), 0 B
/// bool none = default(BalanceKey).IsNone;                   // true
/// </code>
/// </example>
public readonly struct BalanceKey : IEquatable<BalanceKey>
{
    // Position + 1; 0 means None so that default(BalanceKey) is None.
    private readonly int _slot;

    private BalanceKey(int slot, int stamp)
    {
        _slot = slot;
        Stamp = stamp;
    }

    /// <summary>The "no key" value; equal to <c>default</c>.</summary>
    public static BalanceKey None => default;

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsNone => _slot == 0;

    /// <summary>The row position in the registry, or -1 when <see cref="IsNone"/>.</summary>
    public int Index => _slot - 1;

    /// <summary>The identity stamp of the registry that issued the handle (0 for <see cref="None"/>).</summary>
    public int Stamp { get; }

    /// <summary>Creates the handle for the row at <paramref name="index"/>. Called by <see cref="BalanceRegistry"/>.</summary>
    /// <param name="index">The row position, 0 or more.</param>
    /// <param name="stamp">The issuing registry's stamp.</param>
    internal static BalanceKey FromIndex(int index, int stamp) => new(index + 1, stamp);

    /// <inheritdoc />
    public bool Equals(BalanceKey other) => _slot == other._slot && Stamp == other.Stamp;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BalanceKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _slot ^ Stamp;

    /// <inheritdoc />
    public override string ToString() => IsNone ? "BalanceKey.None" : "BalanceKey#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator ==(BalanceKey left, BalanceKey right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator !=(BalanceKey left, BalanceKey right) => !left.Equals(right);
}
