namespace PerformativeMail.Sim.Core;

/// <summary>
/// The only source of <see cref="Reply.Reason"/> codes 1 to 63 (ADR-0007 section 5). Values are append-only:
/// never renumber or reuse one; 9 to 63 are reserved. System reason codes start at <see cref="FirstSystemReason"/>
/// and are declared in the kind's <see cref="OpDescriptor"/>.
/// </summary>
/// <example>
/// <code>
/// return Reply.Reject(in op, CoreReason.UnknownActor);
/// </code>
/// </example>
public static class CoreReason
{
    /// <summary>Success (reason 0).</summary>
    public const ushort Ok = 0;

    /// <summary>The target or queue is busy.</summary>
    public const ushort Busy = 1;

    /// <summary>The actor is not present in the world, or is not allowed to issue a System-only kind.</summary>
    public const ushort UnknownActor = 2;

    /// <summary>The kind is unregistered, or the op has a wrong payload length or non-zero reserved byte.</summary>
    public const ushort UnknownKind = 3;

    /// <summary>The player slot is invalid.</summary>
    public const ushort SlotInvalid = 4;

    /// <summary>The player slot is already occupied.</summary>
    public const ushort SlotBusy = 5;

    /// <summary>The player is already in the run.</summary>
    public const ushort DuplicatePlayer = 6;

    /// <summary>Joining is closed.</summary>
    public const ushort JoinClosed = 7;

    /// <summary>The request is malformed or its arguments are out of range.</summary>
    public const ushort BadRequest = 8;

    /// <summary>First reason code a system may declare; 9 to 63 stay reserved for Sim Core.</summary>
    public const ushort FirstSystemReason = 64;
}
