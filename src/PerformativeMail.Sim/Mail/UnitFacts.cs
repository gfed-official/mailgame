using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The facts <c>MailRules.Evaluate</c> reads about the piece being delivered (ADR-0017 key interfaces). The host builds it
/// from a mail row; the client builds it from its mirrors (ADR-0016 section 3). A small immutable value.
/// </summary>
/// <example>
/// <code>
/// var u = new UnitFacts(letterKind, ItemCategory.Mail, address, 5, 0, MailFlags.None);
/// DeliverEval e = MailRules.Evaluate(in dest, in u);
/// </code>
/// </example>
public readonly struct UnitFacts
{
    /// <summary>The mail kind index.</summary>
    public readonly DefIndex Kind;

    /// <summary>The item category (only <see cref="ItemCategory.Mail"/> can be delivered).</summary>
    public readonly ItemCategory Category;

    /// <summary>The address written on the piece.</summary>
    public readonly AddressId Address;

    /// <summary>The spawn value of the piece.</summary>
    public readonly ushort SpawnValue;

    /// <summary>The deadline shift of the piece.</summary>
    public readonly byte DeadlineShift;

    /// <summary>The piece's flags.</summary>
    public readonly MailFlags Flags;

    /// <summary>Creates the facts.</summary>
    /// <param name="kind">The mail kind index.</param>
    /// <param name="category">The item category.</param>
    /// <param name="address">The address on the piece.</param>
    /// <param name="spawnValue">The spawn value.</param>
    /// <param name="deadlineShift">The deadline shift.</param>
    /// <param name="flags">The flags.</param>
    public UnitFacts(DefIndex kind, ItemCategory category, AddressId address, ushort spawnValue, byte deadlineShift, MailFlags flags)
    {
        Kind = kind;
        Category = category;
        Address = address;
        SpawnValue = spawnValue;
        DeadlineShift = deadlineShift;
        Flags = flags;
    }
}
