namespace PerformativeMail.Sim.Core;

/// <summary>
/// What an <see cref="Op"/> asks the Sim to do (ADR-0007 section 4). A <c>u16</c> enum with explicit values,
/// append-only: never renumber, reuse or reorder one. 0 is invalid. Values live in per-system blocks (see
/// <see cref="OpKindBlock"/>); a system appends its kinds inside its own block. Every kind must register an
/// <see cref="OpDescriptor"/>.
/// </summary>
/// <example>
/// <code>
/// var op = new Op { Kind = OpKind.PlayerLeave, ReqId = 7 };
/// bool inCore = OpKindBlock.Core.Contains(op.Kind);   // true
/// </code>
/// </example>
public enum OpKind : ushort
{
    /// <summary>Invalid: never a registered kind.</summary>
    None = 0,

    /// <summary>Sim Core block: a player asks to join the run.</summary>
    PlayerJoin = 0x0001,

    /// <summary>Sim Core block: a player leaves the run.</summary>
    PlayerLeave = 0x0002,

    /// <summary>Sim Core block: freeze or unfreeze a player (System-issued).</summary>
    FreezePlayer = 0x0003,

    /// <summary>Sim Core block: a client acknowledges a teleport.</summary>
    TeleportAck = 0x0004,
}
