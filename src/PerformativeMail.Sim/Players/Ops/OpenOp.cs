using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Ops;

/// <summary>
/// Player Character's <see cref="OpKind"/> values (ADR-0007 section 4 Players block <c>0x0040-0x007F</c>). <c>0x0040</c> is reserved for
/// <c>Drop</c> (ADR-0009 erratum 2026-10-29; grid-inventory story 020, not built yet), so it is not declared here. <see cref="Open"/> left
/// the Inventory block (<c>0x008C</c>, retired as an <c>OpKind</c>, never reused) for this one under the proposed ADR-0009 erratum of
/// grid-inventory story 019 (pending Evan; Mail Lead planning default Q1, regroup 2026-11-08): Inventory cannot inject
/// <see cref="IPlayersApi"/> (<c>SystemGraph</c> declares Player Character → Grid Inventory), so the range-gated handler is Players'.
/// </summary>
/// <example><code>var op = new Op { Kind = PlayerOpKinds.Open, ReqId = 7 };</code></example>
public static class PlayerOpKinds
{
    /// <summary>Open an external container (range-gated, P13): <see cref="OpenPayload"/>.</summary>
    public const OpKind Open = (OpKind)0x0041;
}

/// <summary>
/// Payload of <see cref="PlayerOpKinds.Open"/> (16 B; ADR-0009 section 1 <c>container u32 + claimed position</c>, layout unchanged by the
/// move from the Inventory block): the container and the feet position the client claimed at press (ADR-0014 section 5).
/// </summary>
/// <example><code>var p = new OpenPayload { Container = c, ClaimedX = 0, ClaimedY = 0, ClaimedZ = 0 };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct OpenPayload
{
    /// <summary>The container id.</summary>
    public uint Container;

    /// <summary>Claimed feet x, mm (P13 layout).</summary>
    public int ClaimedX;

    /// <summary>Claimed feet y, mm.</summary>
    public int ClaimedY;

    /// <summary>Claimed feet z, mm.</summary>
    public int ClaimedZ;
}

/// <summary>
/// The reply codes of <see cref="PlayerOpKinds.Open"/> that are not Inventory's (ADR-0009 section 2: "Player Character and Throwing codes
/// are declared on their own kinds' descriptors"). The P13 codes Inventory already numbers (<c>out_of_range</c> 65, <c>position_stale</c>
/// 66, <c>no_target</c> 83) and <c>container_gone</c> 82 keep their <see cref="InvReason"/> values; these three take the next free values
/// after Inventory's 64 to 85, so no code on this kind means two things.
/// </summary>
public static class PlayerOpReasons
{
    /// <summary><c>dead</c>: the actor is not Alive (P13 step 1).</summary>
    public const ushort Dead = 86;

    /// <summary><c>frozen</c>: the actor is on break (P13 step 2).</summary>
    public const ushort Frozen = 87;

    /// <summary><c>unavailable</c>: the framework gate or the owner's <c>IsAvailable</c> refused (P13 step 4).</summary>
    public const ushort Unavailable = 88;

    /// <summary>
    /// The reply code of a failed <see cref="IPlayersApi.CheckInteraction"/> (ADR-0014 section 5: handlers "map the result to their own
    /// <c>(OpKind, Reason)</c> codes"); 0 for <see cref="InteractionCheckKind.Ok"/>.
    /// </summary>
    /// <param name="kind">The check's outcome.</param>
    /// <returns>The reply code.</returns>
    public static ushort Of(InteractionCheckKind kind) => kind switch
    {
        InteractionCheckKind.Ok => 0,
        InteractionCheckKind.Dead => Dead,
        InteractionCheckKind.Frozen => Frozen,
        InteractionCheckKind.NoTarget => (ushort)InvReason.NoTarget,
        InteractionCheckKind.Unavailable => Unavailable,
        InteractionCheckKind.PositionStale => (ushort)InvReason.PositionStale,
        _ => (ushort)InvReason.OutOfRange,
    };
}

/// <summary>The descriptor of <see cref="PlayerOpKinds.Open"/> and the container-to-anchor mapping its handler and anchor share.</summary>
public static class OpenOp
{
    /// <summary>
    /// <see cref="PlayerOpKinds.Open"/>: 16 B; the P13 chain (dead, frozen, no_target, unavailable, out_of_range, position_stale) and
    /// container_gone. <c>bad_request</c> is Core's 8 and is not declared. Wire codec id and size are 0 until the network epic assigns them.
    /// </summary>
    public static OpDescriptor Descriptor { get; } = OpDescriptor.For<OpenPayload>(
        PlayerOpKinds.Open, false, 0, 0,
        new ReasonDef(PlayerOpReasons.Dead, "dead"), new ReasonDef(PlayerOpReasons.Frozen, "frozen"), InvReasons.Def(InvReason.NoTarget),
        new ReasonDef(PlayerOpReasons.Unavailable, "unavailable"), InvReasons.Def(InvReason.OutOfRange), InvReasons.Def(InvReason.PositionStale),
        InvReasons.Def(InvReason.ContainerGone));

    /// <summary>
    /// The interaction anchor of container <paramref name="c"/>: the container id's raw value as the anchor entity, slot 0 (the client's
    /// <c>TryBuildRequest</c> writes <c>latch.Anchor.Entity.Value</c> as <see cref="OpenPayload.Container"/>). A container id is a counter
    /// far below <c>2^24</c>, so its value has entity class 0 and never equals a real entity's id.
    /// </summary>
    /// <param name="c">The container.</param>
    /// <returns>Its anchor.</returns>
    /// <example><code>AnchorRef a = OpenOp.AnchorOf(inventory.IntakeContainer);</code></example>
    public static AnchorRef AnchorOf(ContainerId c) => new(new EntityId(c.Value), 0);
}
