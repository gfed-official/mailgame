namespace PerformativeMail.Sim.Core;

/// <summary>
/// A read-only, zero-copy view of one log entry returned by <see cref="EventLog.Read"/>. A <c>ref struct</c>: it cannot be
/// stored, boxed or captured, which keeps a reader from holding an entry past the tick (the log is reset after phase 13).
/// A <c>default</c> value (what <c>Read</c> returns with <c>false</c>) must not be used.
/// </summary>
/// <example>
/// <code>
/// while (log.Read(ref cursor, out EventRef e)) { var moved = e.PayloadAs&lt;MovedPayload&gt;(); }
/// </code>
/// </example>
public readonly ref struct EventRef
{
    private readonly ref readonly EventEntry _entry;

    internal EventRef(ref readonly EventEntry entry)
    {
        _entry = ref entry;
    }

    /// <summary>The whole entry (read-only).</summary>
    public ref readonly EventEntry Entry => ref _entry;

    /// <summary>Position in this tick's log.</summary>
    public uint Ordinal => _entry.Ordinal;

    /// <summary>What happened.</summary>
    public EventKind Kind => _entry.Kind;

    /// <summary>Who it is delivered to.</summary>
    public ReplicationClass Class => _entry.Class;

    /// <summary>Slot bitmask resolved at raise time.</summary>
    public byte Audience => _entry.Audience;

    /// <summary>The call scope that raised it.</summary>
    public uint CallSeq => _entry.CallSeq;

    /// <summary>Number of meaningful payload bytes.</summary>
    public byte PayloadLength => _entry.PayloadLength;

    /// <summary>Entry flags.</summary>
    public EventFlags Flags => _entry.Flags;

    /// <summary>Reads the payload as <typeparamref name="T"/> (unaligned, no allocation); see <see cref="EventEntry.PayloadAs{T}"/>.</summary>
    /// <typeparam name="T">The kind's padding-free <c>unmanaged</c> payload struct.</typeparam>
    /// <returns>The payload value.</returns>
    /// <example><code>var p = e.PayloadAs&lt;Pay12&gt;();</code></example>
    public T PayloadAs<T>()
        where T : unmanaged => _entry.PayloadAs<T>();
}
