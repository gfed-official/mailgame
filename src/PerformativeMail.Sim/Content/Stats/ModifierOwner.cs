using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// Who a stat source belongs to, with team ownership spelled out (TD-082 item 1). <c>default(ModifierOwner)</c> is
/// <b>unowned</b> and is refused by <see cref="IStatWriter"/>; it is never silently the team. This is the writer-side
/// counterpart of <see cref="Modifier.OwnerSlot"/>, where <see cref="PlayerSlot.None"/> keeps meaning "team" for the stored
/// modifier. Immutable.
/// </summary>
/// <example>
/// <code>
/// ModifierOwner team = ModifierOwner.Team;
/// ModifierOwner p2 = ModifierOwner.ForSlot(PlayerSlot.FromValue(2));
/// bool bad = ModifierOwner.ForSlot(PlayerSlot.None).IsValid;   // false: a slot owner needs a real slot
/// </code>
/// </example>
public readonly struct ModifierOwner : IEquatable<ModifierOwner>
{
    // 0 = unowned (default), 1 = team, 2 + slot = a player slot.
    private readonly byte _v;

    private ModifierOwner(byte stored)
    {
        _v = stored;
    }

    /// <summary>The team: its modifiers feed the team sheet and, for a <c>both</c> stat, every personal read.</summary>
    public static ModifierOwner Team => new(1);

    /// <summary>True for <see cref="Team"/> or a slot owner; false for <c>default</c>.</summary>
    public bool IsValid => _v != 0;

    /// <summary>True for <see cref="Team"/>.</summary>
    public bool IsTeam => _v == 1;

    /// <summary>The owning slot of a slot owner; <see cref="PlayerSlot.None"/> for the team and for an unowned value.</summary>
    public PlayerSlot Slot => _v >= 2 ? PlayerSlot.FromValue((byte)(_v - 2)) : PlayerSlot.None;

    /// <summary>The index of this owner's list: 0 for the team, 1 + slot for a slot. Only meaningful when <see cref="IsValid"/>.</summary>
    internal int ListIndex => _v - 1;

    /// <summary>A slot owner; <c>default</c> (unowned, refused by the writer) when <paramref name="slot"/> is <see cref="PlayerSlot.None"/>.</summary>
    /// <param name="slot">The player slot.</param>
    /// <returns>The owner.</returns>
    /// <example><code>ModifierOwner owner = ModifierOwner.ForSlot(PlayerSlot.FromValue(0));</code></example>
    public static ModifierOwner ForSlot(PlayerSlot slot) => slot.IsNone ? default : new ModifierOwner((byte)(2 + slot.Value));

    /// <inheritdoc />
    public bool Equals(ModifierOwner other) => _v == other._v;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ModifierOwner other && _v == other._v;

    /// <inheritdoc />
    public override int GetHashCode() => _v;

    /// <summary>Equality.</summary>
    /// <param name="left">Left.</param>
    /// <param name="right">Right.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(ModifierOwner left, ModifierOwner right) => left._v == right._v;

    /// <summary>Inequality.</summary>
    /// <param name="left">Left.</param>
    /// <param name="right">Right.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(ModifierOwner left, ModifierOwner right) => left._v != right._v;
}
