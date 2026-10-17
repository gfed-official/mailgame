namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// One entry of the shared <see cref="UiEventLog"/> ring (ADR-0012 section 9): a blittable value type of 24 bytes (budget 64),
/// enum ids only, no strings, no references, so the ring is one flat array. The meaning of <see cref="Reason"/>,
/// <see cref="IdA"/>, <see cref="IdB"/> and <see cref="IdC"/> depends on <see cref="Kind"/> and is owned by the writing system
/// (the router stores, for example, an <c>EdgeReason</c> or <c>CancelReason</c> numeric id in <see cref="Reason"/>).
/// </summary>
/// <example>
/// <code>
/// UiEventLog.TryGet(0, out UiEventEntry e);
/// if (e.Kind == LogKind.RawInput) { var input = (PhysicalInput)e.IdA; }
/// </code>
/// </example>
public readonly struct UiEventEntry
{
    /// <summary>Set in <see cref="Flags"/> on a coalesced mouse-motion entry (<see cref="UiEventLog.LogMotion"/>).</summary>
    public const byte FlagCoalescedMotion = 1;

    /// <summary>Monotonic time in milliseconds (the <see cref="IInputClock"/> domain).</summary>
    public readonly long TimeMs;

    /// <summary>Write sequence number, counting from 0 since construction or <see cref="UiEventLog.Clear"/> (wraps at 2^32).</summary>
    public readonly uint Seq;

    /// <summary>First id (kind specific; for a raw input the <see cref="PhysicalInput"/> value; for coalesced motion the summed X counts as a signed 16-bit value).</summary>
    public readonly ushort IdA;

    /// <summary>Second id (kind specific; for coalesced motion the summed Y counts as a signed 16-bit value).</summary>
    public readonly ushort IdB;

    /// <summary>Third id (kind specific; for coalesced motion the number of merged motion events, saturating).</summary>
    public readonly ushort IdC;

    /// <summary>What was recorded.</summary>
    public readonly LogKind Kind;

    /// <summary>Kind-specific reason id, owned by the writing system (for a raw input the <see cref="RawKind"/>).</summary>
    public readonly byte Reason;

    /// <summary>Kind-specific flag bits (<see cref="FlagCoalescedMotion"/> and others owned by writers).</summary>
    public readonly byte Flags;

    /// <summary>Creates an entry. <see cref="UiEventLog"/> assigns <see cref="Seq"/>; callers outside the log have no reason to build one.</summary>
    /// <param name="seq">Write sequence number.</param>
    /// <param name="timeMs">Time in milliseconds.</param>
    /// <param name="kind">What was recorded.</param>
    /// <param name="reason">Kind-specific reason id.</param>
    /// <param name="idA">First id.</param>
    /// <param name="idB">Second id.</param>
    /// <param name="idC">Third id.</param>
    /// <param name="flags">Kind-specific flag bits.</param>
    /// <example><code>var e = new UiEventEntry(0, 100, LogKind.Cue, 0, 3, 0, 0, 0);</code></example>
    public UiEventEntry(uint seq, long timeMs, LogKind kind, byte reason, ushort idA, ushort idB, ushort idC, byte flags)
    {
        Seq = seq;
        TimeMs = timeMs;
        Kind = kind;
        Reason = reason;
        IdA = idA;
        IdB = idB;
        IdC = idC;
        Flags = flags;
    }

    /// <summary>The summed X counts of a coalesced motion entry (<see cref="IdA"/> reinterpreted as signed).</summary>
    public short MotionDx => unchecked((short)IdA);

    /// <summary>The summed Y counts of a coalesced motion entry (<see cref="IdB"/> reinterpreted as signed).</summary>
    public short MotionDy => unchecked((short)IdB);
}
