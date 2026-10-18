namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// A container's id (ADR-0009 section 1, ADR-0004): a <c>u32</c> from 1 to 2^32 - 1, 0 meaning none, assigned by Grid Inventory from a
/// counter that is Sim state and never reused within a run. A plain <c>readonly struct</c> with explicit equality members, not a
/// <c>record struct</c> (control-manifest cross-ADR note I-6), so equality and hashing are fixed by hand and the value hashes as
/// <c>(int)Value</c>. Immutable; thread-safe.
/// </summary>
/// <example>
/// <code>
/// ContainerId id = inventory.HotbarOf(slot);
/// if (!id.IsNone) { /* the slot has a hotbar */ }
/// </code>
/// </example>
public readonly struct ContainerId : IEquatable<ContainerId>
{
    /// <summary>Creates an id from its raw value (0 is none).</summary>
    /// <param name="value">The raw value.</param>
    public ContainerId(uint value)
    {
        Value = value;
    }

    /// <summary>The no-container id (raw value 0).</summary>
    public static ContainerId None => default;

    /// <summary>The raw <c>u32</c> value.</summary>
    public uint Value { get; }

    /// <summary>True for the none id.</summary>
    public bool IsNone => Value == 0;

    /// <inheritdoc />
    public bool Equals(ContainerId other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ContainerId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)Value);   // wraps by design (I-6); Debug compiles checked

    /// <summary>True when both ids hold the same value.</summary>
    /// <param name="left">The left id.</param>
    /// <param name="right">The right id.</param>
    public static bool operator ==(ContainerId left, ContainerId right) => left.Value == right.Value;

    /// <summary>True when the ids differ.</summary>
    /// <param name="left">The left id.</param>
    /// <param name="right">The right id.</param>
    public static bool operator !=(ContainerId left, ContainerId right) => left.Value != right.Value;

    /// <inheritdoc />
    public override string ToString() => IsNone ? "container#none" : "container#" + Value;
}
