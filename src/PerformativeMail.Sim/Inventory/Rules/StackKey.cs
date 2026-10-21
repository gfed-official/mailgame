using PerformativeMail.Sim.Inventory.Storage;

namespace PerformativeMail.Sim.Inventory.Rules;

/// <summary>
/// The stacking identity of an entry (GDD R3; TR-inventory-004; story grid-inventory/006), packed into one <c>ulong</c>:
/// mail is <c>mailKind u16 &lt;&lt; 40 | addressId u32 &lt;&lt; 8 | flags u8</c> (Cursed is in the key; the spawn shift is not,
/// so cross-shift leftovers still compress), an item is the item bit (63) <c>| itemId</c>. The item bit keeps a mail key and an
/// item key from ever comparing equal. <c>addressId</c> and <c>flags</c> are the opaque <see cref="EntryRecord"/> fields compared
/// raw: Inventory never interprets them (and never calls Mail, ADR-0017). Equal keys are necessary, not sufficient, for a merge
/// (<see cref="PlacementRules.CanMerge"/>). Pure and allocation-free.
/// </summary>
/// <example>
/// <code>
/// StackKey a = StackKey.ForMail(letter, 17u, 0);
/// StackKey b = StackKey.Of(in entries[slot]);
/// bool same = a == b;
/// </code>
/// </example>
public readonly struct StackKey : IEquatable<StackKey>
{
    private const ulong ItemBit = 1UL << 63;
    private const int KindShift = 40;
    private const int AddressShift = 8;

    /// <summary>The packed key.</summary>
    public readonly ulong Raw;

    private StackKey(ulong raw)
    {
        Raw = raw;
    }

    /// <summary>True for an item key, false for a mail key.</summary>
    public bool IsItem => (Raw & ItemBit) != 0UL;

    /// <summary>The key of a mail entry.</summary>
    /// <param name="mailKind">The mail kind's def ordinal.</param>
    /// <param name="addressId">The opaque packed address (<see cref="EntryRecord.AddressId"/>).</param>
    /// <param name="flags">The raw flags byte (<see cref="EntryRecord.Flags"/>).</param>
    /// <returns>The key.</returns>
    public static StackKey ForMail(ushort mailKind, uint addressId, byte flags) =>
        new(((ulong)mailKind << KindShift) | ((ulong)addressId << AddressShift) | flags);

    /// <summary>The key of an item entry.</summary>
    /// <param name="itemId">The item's def ordinal.</param>
    /// <returns>The key.</returns>
    public static StackKey ForItem(ushort itemId) => new(ItemBit | itemId);

    /// <summary>The key of a stored entry: <see cref="ForMail"/> when <see cref="EntryRecord.IsMail"/>, else <see cref="ForItem"/>.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The key.</returns>
    public static StackKey Of(in EntryRecord entry) =>
        entry.IsMail ? ForMail(entry.MailKind, entry.AddressId, entry.Flags) : ForItem(entry.ItemId);

    /// <inheritdoc />
    public bool Equals(StackKey other) => Raw == other.Raw;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StackKey other && Raw == other.Raw;

    /// <summary>A hash of <see cref="Raw"/>; never used for ordering (ADR-0006).</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() => unchecked((int)(Raw ^ (Raw >> 32)));

    /// <summary>True when the keys are equal.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(StackKey left, StackKey right) => left.Raw == right.Raw;

    /// <summary>True when the keys differ.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(StackKey left, StackKey right) => left.Raw != right.Raw;
}
