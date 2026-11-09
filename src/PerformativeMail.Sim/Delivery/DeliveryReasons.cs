using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Delivery;

/// <summary>
/// Delivery's reply reason codes for <see cref="DeliveryOpKinds.DeliverMatching"/> (ADR-0009 erratum 2026-10-16: Delivery declares its
/// own reasons), keyed by <c>(DeliverMatching, Reason)</c>. Append-only, never renumbered. <c>bad_request</c> is Sim Core's 8.
/// </summary>
/// <example><code>return Reply.Reject(in op, (ushort)DeliveryReason.NoTarget);   // 64</code></example>
public enum DeliveryReason : ushort
{
    /// <summary><c>no_target</c>: not a Deliver anchor, or nothing to deliver (no match and no selected mail).</summary>
    NoTarget = 64,

    /// <summary><c>wallet_floor</c>: the wallet floor refused the misdelivery penalty; nothing was consumed.</summary>
    WalletFloor = 65,

    /// <summary><c>closed</c>: deliveries are closed (the hold began at or after the close, or the grace ran out).</summary>
    Closed = 66,

    /// <summary><c>not_on_route</c>: the destination's district is locked.</summary>
    NotOnRoute = 67,

    /// <summary><c>wont_fit</c>: the destination type does not accept the selected mail's kind.</summary>
    WontFit = 68,

    /// <summary><c>dry_land</c>: the actor is at swim depth.</summary>
    DryLand = 69,

    /// <summary><c>out_of_range</c>: the claim is beyond the anchor's reach plus slack, or not corroborated.</summary>
    OutOfRange = 70,

    /// <summary><c>position_stale</c>: no transform received for too long.</summary>
    PositionStale = 71,

    /// <summary><c>dead</c>: the actor is not Alive.</summary>
    Dead = 72,

    /// <summary><c>frozen</c>: the actor is Frozen.</summary>
    Frozen = 73,

    /// <summary><c>unavailable</c>: the anchor refused with a reason key not listed above.</summary>
    Unavailable = 74,
}

/// <summary>The snake_case names of <see cref="DeliveryReason"/> and the <see cref="ReasonDef"/>s built from them.</summary>
/// <example><code>ReasonDef d = DeliveryReasons.Def(DeliveryReason.Closed);   // (66, "closed")</code></example>
public static class DeliveryReasons
{
    /// <summary>The stable snake_case name of <paramref name="reason"/>, or <c>"unknown"</c> for an undefined value.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The catalogue name (a literal; no allocation).</returns>
    public static string Name(DeliveryReason reason) => reason switch
    {
        DeliveryReason.NoTarget => "no_target",
        DeliveryReason.WalletFloor => "wallet_floor",
        DeliveryReason.Closed => "closed",
        DeliveryReason.NotOnRoute => "not_on_route",
        DeliveryReason.WontFit => "wont_fit",
        DeliveryReason.DryLand => "dry_land",
        DeliveryReason.OutOfRange => "out_of_range",
        DeliveryReason.PositionStale => "position_stale",
        DeliveryReason.Dead => "dead",
        DeliveryReason.Frozen => "frozen",
        DeliveryReason.Unavailable => "unavailable",
        _ => "unknown",
    };

    /// <summary>The descriptor entry of <paramref name="reason"/>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns><c>(code, name)</c>.</returns>
    public static ReasonDef Def(DeliveryReason reason) => new((ushort)reason, Name(reason));
}
