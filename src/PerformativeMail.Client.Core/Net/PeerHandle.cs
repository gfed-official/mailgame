namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Opaque, transport-issued handle naming one remote end of a connection. The adapter treats it
/// as an identity token only: it assumes nothing about its range or about Godot peer ids.
/// <c>default</c> equals <see cref="None"/>, which no transport issues for a live peer.
/// </summary>
/// <example>
/// <code>
/// PeerHandle peer = new PeerHandle(7);
/// bool same = peer == new PeerHandle(7);   // true
/// </code>
/// </example>
public readonly struct PeerHandle : IEquatable<PeerHandle>
{
    /// <summary>The raw handle value.</summary>
    public readonly uint Value;

    /// <summary>Wraps a transport-issued value; performs no validation.</summary>
    /// <param name="value">The raw handle value.</param>
    public PeerHandle(uint value)
    {
        Value = value;
    }

    /// <summary>The "no peer" handle (raw 0); equal to <c>default</c>.</summary>
    public static PeerHandle None => default;

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsNone => Value == 0u;

    /// <inheritdoc />
    public bool Equals(PeerHandle other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PeerHandle other && Value == other.Value;

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)Value);

    /// <summary>Equality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(PeerHandle left, PeerHandle right) => left.Value == right.Value;

    /// <summary>Inequality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(PeerHandle left, PeerHandle right) => left.Value != right.Value;
}
