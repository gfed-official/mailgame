using System.Buffers.Binary;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>One record read from an <see cref="EventBatch"/> by an <see cref="EventBatchReader"/>.</summary>
/// <example><code>while (reader.TryReadNext(out EventBatchRecord r)) { r.Codec.TryDecode(r.Payload, ref entry, out _); }</code></example>
public readonly ref struct EventBatchRecord
{
    /// <summary>Creates a record view.</summary>
    /// <param name="kind">The event kind (continuation bit stripped).</param>
    /// <param name="isContinuation">True for a <c>Continuation</c> chunk.</param>
    /// <param name="payload">The record's payload bytes, exactly as long as the codec measured.</param>
    /// <param name="codec">The kind's codec.</param>
    public EventBatchRecord(EventKind kind, bool isContinuation, ReadOnlySpan<byte> payload, IEventCodec codec)
    {
        Kind = kind;
        IsContinuation = isContinuation;
        Payload = payload;
        Codec = codec;
    }

    /// <summary>The event kind (bits 0 to 14 of the record's kind field).</summary>
    public EventKind Kind { get; }

    /// <summary>True when the record's kind field carried <see cref="EventBatch.ContinuationBit"/>.</summary>
    public bool IsContinuation { get; }

    /// <summary>The payload bytes (valid as long as the batch message is).</summary>
    public ReadOnlySpan<byte> Payload { get; }

    /// <summary>The codec of <see cref="Kind"/>; decode <see cref="Payload"/> with it.</summary>
    public IEventCodec Codec { get; }
}

/// <summary>
/// Reads the records of one event batch message (network-adapter-loopback story 006; TD-065 item 3), the inverse of the host
/// adapter's batch writer. Each record's length comes from its kind's <see cref="IEventCodec.TryMeasure"/>, so a kind with no
/// codec, a truncated record or a record count that disagrees with the message length ends the read with <see cref="Failed"/>
/// set. Allocation-free and never throws on malformed bytes.
/// </summary>
/// <example>
/// <code>
/// if (EventBatchReader.TryCreate(message, events, out EventBatchReader reader))
/// {
///     while (reader.TryReadNext(out EventBatchRecord record)) { /* record.Kind, record.Payload */ }
///     bool wholeBatchGood = !reader.Failed &amp;&amp; reader.IsComplete;
/// }
/// </code>
/// </example>
public ref struct EventBatchReader
{
    private readonly ReadOnlySpan<byte> _message;
    private readonly EventCodecRegistry _codecs;
    private int _at;
    private int _read;

    private EventBatchReader(ReadOnlySpan<byte> message, EventCodecRegistry codecs, uint tick, ushort count)
    {
        _message = message;
        _codecs = codecs;
        _at = EventBatch.HeaderSize;
        _read = 0;
        Tick = tick;
        Count = count;
        Failed = false;
    }

    /// <summary>The tick the batch reports.</summary>
    public uint Tick { get; }

    /// <summary>The record count in the header.</summary>
    public ushort Count { get; }

    /// <summary>Records read so far.</summary>
    public readonly int ReadCount => _read;

    /// <summary>True once a record was malformed; no further record is returned.</summary>
    public bool Failed { get; private set; }

    /// <summary>True when every header-counted record was read and the message has no bytes left over.</summary>
    public readonly bool IsComplete => !Failed && _read == Count && _at == _message.Length;

    /// <summary>Starts reading a batch message.</summary>
    /// <param name="message">The whole message, valid only while the reader is used.</param>
    /// <param name="codecs">The session's event codec registry.</param>
    /// <param name="reader">The reader positioned at the first record; default when this returns false.</param>
    /// <returns>False when <paramref name="message"/> is not a batch (short or wrongly tagged) or <paramref name="codecs"/> is null.</returns>
    public static bool TryCreate(ReadOnlySpan<byte> message, EventCodecRegistry codecs, out EventBatchReader reader)
    {
        reader = default;
        if (codecs is null || !EventBatch.TryReadHeader(message, out uint tick, out ushort count))
        {
            return false;
        }

        reader = new EventBatchReader(message, codecs, tick, count);
        return true;
    }

    /// <summary>Reads the next record.</summary>
    /// <param name="record">The record; default when this returns false.</param>
    /// <returns>False after the last record, or when the next one is malformed (then <see cref="Failed"/> is set).</returns>
    public bool TryReadNext(out EventBatchRecord record)
    {
        record = default;
        if (Failed || _read >= Count)
        {
            return false;
        }

        ReadOnlySpan<byte> rest = _message.Slice(_at);
        if (rest.Length < EventBatch.RecordHeaderSize)
        {
            Failed = true;
            return false;
        }

        ushort field = BinaryPrimitives.ReadUInt16LittleEndian(rest);
        var kind = (EventKind)(field & EventBatch.MaxKind);
        IEventCodec? codec = _codecs.CodecFor(kind);
        ReadOnlySpan<byte> body = rest.Slice(EventBatch.RecordHeaderSize);
        if (codec is null || !codec.TryMeasure(body, out int length) || length < 0 || length > body.Length)
        {
            Failed = true;
            return false;
        }

        record = new EventBatchRecord(kind, (field & EventBatch.ContinuationBit) != 0, body.Slice(0, length), codec);
        _at += EventBatch.RecordHeaderSize + length;
        _read++;
        return true;
    }
}
