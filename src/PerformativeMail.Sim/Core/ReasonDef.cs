namespace PerformativeMail.Sim.Core;

/// <summary>
/// A system-defined reason code declared in an <see cref="OpDescriptor"/> (ADR-0007 section 5). The code must be
/// at least <see cref="CoreReason.FirstSystemReason"/>; the descriptor test enforces it. Codes are append-only per kind.
/// </summary>
/// <example>
/// <code>
/// var noRoom = new ReasonDef(64, "no_room");
/// </code>
/// </example>
/// <param name="Code">The reason code carried in <see cref="Reply.Reason"/>.</param>
/// <param name="Name">Stable snake_case name for logs and the wire catalogue.</param>
public readonly record struct ReasonDef(ushort Code, string Name);
