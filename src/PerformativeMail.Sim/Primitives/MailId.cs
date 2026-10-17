namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Identity of a mail item: its own never-reused <c>uint</c> counter, separate from
/// <see cref="EntityId"/> (mail is not an entity class). A lower id is older. <c>default</c>
/// equals <see cref="None"/> (0). Orders by raw <see cref="Value"/> ascending.
/// </summary>
/// <example>
/// <code>
/// var older = new MailId(5);
/// var newer = new MailId(9);
/// bool ordered = older.CompareTo(newer) &lt; 0;   // true
/// </code>
/// </example>
public readonly struct MailId : IEquatable<MailId>, IComparable<MailId>
{
    /// <summary>The raw counter value.</summary>
    public readonly uint Value;

    /// <summary>Wraps a raw counter value; performs no validation.</summary>
    /// <param name="value">The raw mail counter.</param>
    public MailId(uint value)
    {
        Value = value;
    }

    /// <summary>The "no mail" id (raw 0); equal to <c>default</c>.</summary>
    public static MailId None => default;

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsNone => Value == 0u;

    /// <inheritdoc />
    public bool Equals(MailId other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MailId other && Value == other.Value;

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)Value);

    /// <summary>Orders by raw <see cref="Value"/> ascending (lower = older).</summary>
    /// <param name="other">The id to compare against.</param>
    /// <returns>Negative, zero or positive as this id is lower than, equal to or higher than <paramref name="other"/>.</returns>
    public int CompareTo(MailId other) => Value < other.Value ? -1 : (Value > other.Value ? 1 : 0);

    /// <summary>Equality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(MailId left, MailId right) => left.Value == right.Value;

    /// <summary>Inequality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(MailId left, MailId right) => left.Value != right.Value;
}
