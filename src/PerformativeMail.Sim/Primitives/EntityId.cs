namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Identity of a world entity: <c>((uint)class &lt;&lt; 24) | counter</c> with the counter in
/// 1 ... 2^24-1. <c>default</c> equals <see cref="None"/> (0). Never reused within a run.
/// Orders by raw <see cref="Value"/> ascending. Allocation lives in a later story.
/// </summary>
/// <example>
/// <code>
/// EntityId id = EntityId.Compose(EntityClass.Construct, 1);  // Value == 50_331_649
/// EntityClass cls = id.Class;                                // Construct
/// uint counter = id.Counter;                                 // 1
/// bool none = default(EntityId) == EntityId.None;            // true
/// </code>
/// </example>
public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
{
    /// <summary>Mask of the 24-bit counter field.</summary>
    public const uint CounterMask = 0xFF_FFFFu;

    /// <summary>The raw 32-bit encoding.</summary>
    public readonly uint Value;

    /// <summary>Wraps a raw encoding (for example from a snapshot or wire payload); performs no validation.</summary>
    /// <param name="value">The raw <c>class &lt;&lt; 24 | counter</c> encoding.</param>
    public EntityId(uint value)
    {
        Value = value;
    }

    /// <summary>The "no entity" id (raw 0); equal to <c>default</c>.</summary>
    public static EntityId None => default;

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsNone => Value == 0u;

    /// <summary>The class byte (bits 24-31).</summary>
    public EntityClass Class => (EntityClass)(Value >> 24);

    /// <summary>The 24-bit counter (bits 0-23).</summary>
    public uint Counter => Value & CounterMask;

    /// <summary>
    /// Builds an id from a class and a counter. A counter above <see cref="CounterMask"/>
    /// asserts under <c>SIM_ASSERT</c> and is masked to 24 bits otherwise, so it can never
    /// corrupt the class byte.
    /// </summary>
    /// <param name="cls">The entity class.</param>
    /// <param name="counter">The per-class counter, 1 ... 0xFF_FFFF.</param>
    /// <returns>The composed id.</returns>
    /// <example>
    /// <code>EntityId.Compose(EntityClass.Player, 1).Value // 16_777_217</code>
    /// </example>
    public static EntityId Compose(EntityClass cls, uint counter)
    {
        SimAssert.That(counter <= CounterMask, SimAssertCode.IdCounterRange);
        return new EntityId(((uint)cls << 24) | (counter & CounterMask));
    }

    /// <inheritdoc />
    public bool Equals(EntityId other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EntityId other && Value == other.Value;

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)Value);

    /// <summary>Orders by raw <see cref="Value"/> ascending.</summary>
    /// <param name="other">The id to compare against.</param>
    /// <returns>Negative, zero or positive as this id is lower than, equal to or higher than <paramref name="other"/>.</returns>
    public int CompareTo(EntityId other) => Value < other.Value ? -1 : (Value > other.Value ? 1 : 0);

    /// <summary>Equality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(EntityId left, EntityId right) => left.Value == right.Value;

    /// <summary>Inequality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(EntityId left, EntityId right) => left.Value != right.Value;
}
