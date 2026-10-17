namespace PerformativeMail.Sim.Core;

/// <summary>
/// What an <see cref="EventEntry"/> reports (ADR-0008 section 1). A <c>u16</c> enum with explicit values,
/// append-only: never renumber, reuse or reorder one. 0 is invalid. Values mirror the <see cref="OpKindBlock"/>
/// ranges: a system appends its kinds inside its own block (Sim Core 0x0001 to 0x003F, and so on). Every kind must
/// declare an <see cref="EventDescriptor"/>; an undeclared kind fails the descriptor enumeration test and is
/// stored as <see cref="ReplicationClass.Internal"/> at runtime.
/// </summary>
/// <example>
/// <code>
/// bool inCore = OpKindBlock.Core.Contains((OpKind)(ushort)EventKind.OpReply);   // true
/// </code>
/// </example>
public enum EventKind : ushort
{
    /// <summary>Invalid: never a declared kind.</summary>
    None = 0,

    /// <summary>Sim Core block: the reply to a drained op, appended after every event of that op's call (Story 020).</summary>
    OpReply = 0x0001,
}
