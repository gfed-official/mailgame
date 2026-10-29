using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing's reply reason codes for <see cref="ThrowOpKinds.ThrowRelease"/> (ADR-0015 section 1 validation chain), keyed by
/// <c>(ThrowRelease, Reason)</c>. Append-only, never renumbered. <c>bad_request</c> is Sim Core's 8
/// (<see cref="ThrowReasons.BadRequest"/>), not redeclared. <see cref="ThrowCooldown"/> and <see cref="FlightCap"/> stay declared
/// though unreachable at M-1a (E5).
/// </summary>
/// <example><code>return Reply.Reject(in op, (ushort)ThrowReason.BadDirection);   // 70</code></example>
public enum ThrowReason : ushort
{
    /// <summary><c>stale_entry</c>: entry unknown, not the selected slot, or count differs from units.</summary>
    StaleEntry = 64,

    /// <summary><c>dead</c>: the actor is not Alive.</summary>
    Dead = 65,

    /// <summary><c>frozen</c>: the actor is Frozen.</summary>
    Frozen = 66,

    /// <summary><c>swimming</c>: the actor is swimming.</summary>
    Swimming = 67,

    /// <summary><c>unavailable</c>: the actor is not on foot.</summary>
    Unavailable = 68,

    /// <summary><c>throw_cooldown</c>: the leaky bucket is empty.</summary>
    ThrowCooldown = 69,

    /// <summary><c>bad_direction</c>: the direction is not unit length or outside the pitch band.</summary>
    BadDirection = 70,

    /// <summary><c>flight_cap</c>: every flight slot is live.</summary>
    FlightCap = 71,
}

/// <summary>The snake_case names of <see cref="ThrowReason"/> (ADR-0015 section 1) and the <see cref="ReasonDef"/>s built from them.</summary>
/// <example><code>ReasonDef d = ThrowReasons.Def(ThrowReason.Dead);   // (65, "dead")</code></example>
public static class ThrowReasons
{
    /// <summary><c>bad_request</c>: Sim Core's global code 8, used for a malformed request.</summary>
    public const ushort BadRequest = CoreReason.BadRequest;

    /// <summary>The stable snake_case name of <paramref name="reason"/>, or <c>"unknown"</c> for an undefined value.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The catalogue name (a literal; no allocation).</returns>
    public static string Name(ThrowReason reason) => reason switch
    {
        ThrowReason.StaleEntry => "stale_entry",
        ThrowReason.Dead => "dead",
        ThrowReason.Frozen => "frozen",
        ThrowReason.Swimming => "swimming",
        ThrowReason.Unavailable => "unavailable",
        ThrowReason.ThrowCooldown => "throw_cooldown",
        ThrowReason.BadDirection => "bad_direction",
        ThrowReason.FlightCap => "flight_cap",
        _ => "unknown",
    };

    /// <summary>The descriptor entry of <paramref name="reason"/>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns><c>(code, name)</c>.</returns>
    public static ReasonDef Def(ThrowReason reason) => new((ushort)reason, Name(reason));
}
