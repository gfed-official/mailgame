using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>The outcome kind of <see cref="IPlayersApi.CheckInteraction"/> (ADR-0014 section 5), the first failure in this order.</summary>
public enum InteractionCheckKind : byte
{
    /// <summary>Every check passed.</summary>
    Ok = 0,

    /// <summary>The actor is not Alive (and not Frozen): Dead, DeadAway, Joining or not present.</summary>
    Dead = 1,

    /// <summary>The actor is Frozen.</summary>
    Frozen = 2,

    /// <summary>The anchor is not registered (or not eligible in the actor's body context).</summary>
    NoTarget = 3,

    /// <summary>The framework gate or the owner's <c>IsAvailable</c> refused; <see cref="InteractionCheck.Reason"/> says why.</summary>
    Unavailable = 4,

    /// <summary>No transform received for more than <c>player.positionStaleTicks</c>.</summary>
    PositionStale = 5,

    /// <summary>The claim's chest is beyond <c>RangeMm + rangeSlackMm</c> of the anchor, or the claim is beyond <c>claimToleranceMm</c> of every corroborating sample.</summary>
    OutOfRange = 6,
}

/// <summary>The result of <see cref="IPlayersApi.CheckInteraction"/>: a kind and, for <see cref="InteractionCheckKind.Unavailable"/>, the reason key.</summary>
/// <example><code>InteractionCheck r = players.CheckInteraction(actor, anchor, in claim); if (r.Kind != InteractionCheckKind.Ok) { /* reply r */ }</code></example>
public readonly struct InteractionCheck : IEquatable<InteractionCheck>
{
    private InteractionCheck(InteractionCheckKind kind, StringId reason)
    {
        Kind = kind;
        Reason = reason;
    }

    /// <summary>The outcome.</summary>
    public InteractionCheckKind Kind { get; }

    /// <summary>The unavailable-reason key; <see cref="StringId.None"/> for every other kind.</summary>
    public StringId Reason { get; }

    /// <summary>A result of <paramref name="kind"/> with no reason.</summary>
    /// <param name="kind">Any kind but <see cref="InteractionCheckKind.Unavailable"/>.</param>
    /// <returns>The result.</returns>
    public static InteractionCheck Of(InteractionCheckKind kind) => new(kind, StringId.None);

    /// <summary>An <see cref="InteractionCheckKind.Unavailable"/> result.</summary>
    /// <param name="reason">The reason key.</param>
    /// <returns>The result.</returns>
    public static InteractionCheck Unavailable(StringId reason) => new(InteractionCheckKind.Unavailable, reason);

    /// <summary>Equality.</summary>
    public static bool operator ==(InteractionCheck left, InteractionCheck right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(InteractionCheck left, InteractionCheck right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(InteractionCheck other) => Kind == other.Kind && Reason == other.Reason;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is InteractionCheck other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => ((int)Kind << 24) | (((int)Reason.Ns) << 16) | Reason.Index;

    /// <inheritdoc />
    public override string ToString() => Kind == InteractionCheckKind.Unavailable ? "Unavailable(" + Reason.Ns + ":" + Reason.Index + ")" : Kind.ToString();
}
