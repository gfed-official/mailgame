namespace PerformativeMail.Sim.Core;

/// <summary>
/// Read-only view of the transform samples latched at this tick's prologue (ADR-0003 section 2), for Player Character's phase 3
/// ingest. Samples appear in slot order, then sequence order; each was stamped <see cref="ReceivedTick"/>. Valid for the whole
/// tick; the next tick's prologue replaces the contents. Allocation-free. Not thread-safe (Sim thread only).
/// </summary>
/// <example>
/// <code>
/// for (int i = 0; i &lt; latch.Count; i++)
/// {
///     ref readonly TransformSample s = ref latch[i];
///     Apply(s.Slot, in s);
/// }
/// </code>
/// </example>
public interface ISampleLatch
{
    /// <summary>Number of samples latched this tick.</summary>
    int Count { get; }

    /// <summary>The tick the latched samples were stamped with (the tick whose prologue drained them).</summary>
    uint ReceivedTick { get; }

    /// <summary>The <paramref name="index"/>th sample in slot order then sequence order.</summary>
    /// <param name="index">0 to <see cref="Count"/> - 1.</param>
    ref readonly TransformSample this[int index] { get; }

    /// <summary>The last accepted sequence number of <paramref name="slot"/>; 0 when none has been accepted.</summary>
    /// <param name="slot">The sender slot.</param>
    uint LastSeq(Primitives.PlayerSlot slot);
}
