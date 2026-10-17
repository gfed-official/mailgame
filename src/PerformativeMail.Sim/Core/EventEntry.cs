using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PerformativeMail.Sim.Core;

/// <summary>The fixed 48-byte payload area of an <see cref="EventEntry"/>. Read it with <see cref="EventEntry.PayloadAs{T}"/>.</summary>
[InlineArray(Size)]
public struct EventPayload
{
    /// <summary>Payload size in bytes.</summary>
    public const int Size = 48;

#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private byte _e0;
#pragma warning restore CS0169
}

/// <summary>
/// One event: a 64-byte blittable value (ADR-0008 section 1, TR-sim-021). Offsets: <see cref="Ordinal"/> 0,
/// <see cref="Kind"/> 4, <see cref="Class"/> 6, <see cref="Audience"/> 7, <see cref="CallSeq"/> 8,
/// <see cref="PayloadLength"/> 12, <see cref="Flags"/> 13, <see cref="Reserved"/> 14, <see cref="Payload"/> 16.
/// Each kind owns an <c>unmanaged</c>, padding-free, string-free payload struct of at most 48 bytes, written and
/// read unaligned (the payload sits at a 4-byte-aligned offset). Payload bytes at and after <see cref="PayloadLength"/>
/// and <see cref="Reserved"/> are zero. Not thread-safe (plain value). <see cref="Kind"/> is typed
/// <see cref="EventKind"/> (a <c>u16</c> enum), which is layout-identical to the ADR's <c>ushort</c>.
/// </summary>
/// <example>
/// <code>
/// foreach (ref readonly EventEntry e in entries) { if (e.Kind == EventKind.OpReply) { var r = e.PayloadAs&lt;Reply&gt;(); } }
/// </code>
/// </example>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct EventEntry
{
    /// <summary>Total size in bytes (asserted by a test).</summary>
    public const int SizeBytes = 64;

    /// <summary>Position in this tick's log (0 to count-1); equals the array index.</summary>
    public uint Ordinal;

    /// <summary>What happened.</summary>
    public EventKind Kind;

    /// <summary>Who it is delivered to, copied from the kind's descriptor at raise (<see cref="ReplicationClass.Internal"/> when undeclared).</summary>
    public ReplicationClass Class;

    /// <summary>Slot bitmask resolved by the raiser at raise time: 0 for <c>Internal</c>, exactly one bit (or none) for <c>OwnerOnly</c>.</summary>
    public byte Audience;

    /// <summary>The call scope that raised the entry (per-tick counter from 1); 0 when raised outside any scope.</summary>
    public uint CallSeq;

    /// <summary>Number of meaningful payload bytes (0 to 48).</summary>
    public byte PayloadLength;

    /// <summary>Entry flags.</summary>
    public EventFlags Flags;

    /// <summary>Reserved; always zero.</summary>
    public ushort Reserved;

    /// <summary>The payload bytes; bytes at and after <see cref="PayloadLength"/> are zero.</summary>
    public EventPayload Payload;

    /// <summary>
    /// Reads the payload as <typeparamref name="T"/> (unaligned, no allocation). A type larger than the 48-byte area
    /// returns <c>default</c>; a type larger than <see cref="PayloadLength"/> reads zero past the stored length.
    /// </summary>
    /// <typeparam name="T">The kind's padding-free <c>unmanaged</c> payload struct.</typeparam>
    /// <returns>The payload value.</returns>
    /// <example><code>var reply = entry.PayloadAs&lt;Reply&gt;();</code></example>
    public readonly T PayloadAs<T>()
        where T : unmanaged
    {
        if (Unsafe.SizeOf<T>() > EventPayload.Size)
        {
            return default;
        }

        ReadOnlySpan<byte> span = Payload;
        return MemoryMarshal.Read<T>(span);
    }
}
