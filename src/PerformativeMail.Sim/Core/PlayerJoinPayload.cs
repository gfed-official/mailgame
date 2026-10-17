using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// Payload of <see cref="OpKind.PlayerJoin"/> (8 B, padding-free; wire form is <c>playerId</c> u32 + <c>slot</c> u8). The actor is
/// never read from here: joins are System-only and the Sim never sees accounts or peers.
/// </summary>
/// <example><code>var p = new PlayerJoinPayload { Slot = 3 };   // PlayerId 0 = new player, allocate</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerJoinPayload
{
    /// <summary>0 = new player (allocate a fresh id). Non-zero is a rejoin: <c>duplicate_player</c> when present, <c>bad_request</c> when absent until M0 (SC-R21-2).</summary>
    public uint PlayerId;

    /// <summary>Requested slot, 0 to 7; anything else is <c>slot_invalid</c>.</summary>
    public byte Slot;

    /// <summary>Explicit padding, must be zero on the wire.</summary>
    public byte Pad0;

    /// <summary>Explicit padding.</summary>
    public byte Pad1;

    /// <summary>Explicit padding.</summary>
    public byte Pad2;
}

/// <summary>Payload of <see cref="OpKind.PlayerLeave"/> (4 B): the player that leaves.</summary>
/// <example><code>var p = new PlayerLeavePayload { PlayerId = id.Value };</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerLeavePayload
{
    /// <summary>The leaving player's id; an absent player is <c>bad_request</c>.</summary>
    public uint PlayerId;
}

/// <summary>Sim Core's descriptors for the join and leave ops (both System-only, Core reasons only).</summary>
public static class CorePlayerOps
{
    /// <summary>Descriptor of <see cref="OpKind.PlayerJoin"/>: System-only, codec id 1, wire size 5 (u32 + u8).</summary>
    public static OpDescriptor JoinDescriptor { get; } = OpDescriptor.For<PlayerJoinPayload>(OpKind.PlayerJoin, systemOnly: true, wireCodecId: 1, wireSize: 5);

    /// <summary>Descriptor of <see cref="OpKind.PlayerLeave"/>: System-only, codec id 2, wire size 4.</summary>
    public static OpDescriptor LeaveDescriptor { get; } = OpDescriptor.For<PlayerLeavePayload>(OpKind.PlayerLeave, systemOnly: true, wireCodecId: 2, wireSize: 4);
}
