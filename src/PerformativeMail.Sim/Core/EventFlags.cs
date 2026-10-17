namespace PerformativeMail.Sim.Core;

/// <summary>Bit flags in <see cref="EventEntry.Flags"/> (ADR-0008 section 1). Bits 1 to 7 are reserved and zero.</summary>
/// <example>
/// <code>
/// bool chunk = (entry.Flags &amp; EventFlags.Continuation) != 0;
/// </code>
/// </example>
[Flags]
public enum EventFlags : byte
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>This entry continues the previous entry of the same kind and <c>CallSeq</c> (owner chunking, ADR-0008 section 1.3).</summary>
    Continuation = 1,
}
