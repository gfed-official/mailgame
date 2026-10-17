namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Who performed an action: an <see cref="EntityId"/>-width value, or the reserved
/// <see cref="System"/> actor (0x0000_0001). Class 0 is never allocated to an entity, so
/// <see cref="System"/> cannot collide with a real entity. <c>default</c> equals <see cref="None"/>.
/// </summary>
/// <example>
/// <code>
/// ActorId actor = ActorId.FromEntity(playerEntityId);
/// bool isSystem = actor == ActorId.System;   // false
/// </code>
/// </example>
public readonly struct ActorId : IEquatable<ActorId>
{
    /// <summary>The raw 32-bit encoding.</summary>
    public readonly uint Value;

    /// <summary>Wraps a raw encoding (for example from an op payload); performs no validation.</summary>
    /// <param name="value">The raw actor encoding.</param>
    public ActorId(uint value)
    {
        Value = value;
    }

    /// <summary>The "no actor" id (raw 0); equal to <c>default</c>.</summary>
    public static ActorId None => default;

    /// <summary>The simulation itself as an actor (raw 0x0000_0001).</summary>
    public static ActorId System => new ActorId(0x0000_0001u);

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsNone => Value == 0u;

    /// <summary>True when this is the <see cref="System"/> actor.</summary>
    public bool IsSystem => Value == 0x0000_0001u;

    /// <summary>Uses an entity as the actor.</summary>
    /// <param name="entity">The acting entity.</param>
    /// <returns>An actor with the same raw value as <paramref name="entity"/>.</returns>
    public static ActorId FromEntity(EntityId entity) => new ActorId(entity.Value);

    /// <inheritdoc />
    public bool Equals(ActorId other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ActorId other && Value == other.Value;

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)Value);

    /// <summary>Equality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(ActorId left, ActorId right) => left.Value == right.Value;

    /// <summary>Inequality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(ActorId left, ActorId right) => left.Value != right.Value;
}
