namespace PerformativeMail.Sim.Core;

/// <summary>
/// Identifies an always-on check recorded in the <see cref="SimCheckRing"/>. Explicit,
/// append-only values: never renumber or reuse. Value 0 is reserved (a zeroed record is not a
/// real check). Codes outside the defined range are still recorded; see <see cref="SimCheckRing.Record"/>.
/// </summary>
/// <example><code>ring.Record(CheckCode.TickHitch, tick, elapsedMs, 0);</code></example>
public enum CheckCode : ushort
{
    /// <summary>Reserved; never recorded by the Sim.</summary>
    None = 0,

    /// <summary>A frame ran longer than the tick budget (written by FrameRunner).</summary>
    TickHitch = 1,

    /// <summary>An id space was exhausted.</summary>
    IdExhausted = 2,

    /// <summary>A system was busy when work arrived.</summary>
    BusySystem = 3,

    /// <summary>The operation sequence counter was exhausted.</summary>
    OpSeqExhausted = 4,

    /// <summary>A tick value was out of range.</summary>
    BadTick = 5,

    /// <summary>An entity id was requested for a reserved class (0x00, 0x09 to 0xEF) or a test class without <c>allowTestClasses</c>; a = class.</summary>
    IdReservedClass = 6,

    /// <summary>The mail table grew (ADR-0017 section 2). a = kind: 1 = the empty page pool doubled (b = new total page count), 2 = the page map doubled because ids ran past it (b = new map length).</summary>
    MailTableGrow = 7,

    /// <summary>The <c>InputLatch</c> held more than 32 edges for one frame; a = edges dropped (newest presses), b = presses coalesced. Written once per overflow (ADR-0012 section 4).</summary>
    InputEdgeOverflow = 8,
}
