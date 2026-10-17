using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// The wire codec of one event payload shape (ADR-0008 section 7, ADR-0011 section 6; network-adapter-loopback story 004).
/// Event payloads cross the wire only through a codec, never by blitting the 64-byte <see cref="EventEntry"/>. A codec is
/// registered in an <see cref="EventCodecRegistry"/> under <see cref="CodecId"/>, the <c>WireCodecId</c> of the kinds that
/// use it. Pure: no state, no allocation, no exception.
/// <para>
/// Self-delimiting contract (used by <see cref="EventBatchReader"/>, network-adapter-loopback story 006): a record's length
/// must be recoverable from its bytes and its kind alone, either because the codec is fixed-length for the kind
/// (<see cref="FixedLengthEventCodec"/>) or because the encoding carries its own length (the <c>OpReply</c> body's
/// <c>extraLen</c>). <see cref="TryMeasure"/> reports that length and <see cref="TryDecode"/> is the inverse of
/// <see cref="Encode"/>; neither allocates or throws on malformed bytes (TD-065 item 3).
/// </para>
/// </summary>
/// <example><code>IEventCodec codec = new FixedLengthEventCodec(910, 4);</code></example>
public interface IEventCodec
{
    /// <summary>The codec id an <see cref="EventDescriptor.WireCodecId"/> names (0 is reserved: no wire codec, except <c>OpReply</c>).</summary>
    ushort CodecId { get; }

    /// <summary>The largest number of bytes <see cref="Encode"/> writes; at most 255.</summary>
    int MaxWireSize { get; }

    /// <summary>Encodes the payload of <paramref name="entry"/> (the record's bytes after its <c>kind u16</c>).</summary>
    /// <param name="entry">The adapter's own copy of the entry.</param>
    /// <param name="destination">The destination; <see cref="MaxWireSize"/> bytes always suffice.</param>
    /// <returns>Bytes written, or -1 when the entry cannot be encoded (counted by the caller, never thrown).</returns>
    int Encode(in EventEntry entry, Span<byte> destination);

    /// <summary>
    /// Measures one record payload at the start of <paramref name="payload"/> without decoding it. Trailing bytes (the next
    /// record) are allowed and not read.
    /// </summary>
    /// <param name="payload">The record's bytes after its <c>kind u16</c>, and anything after them; valid only for this call.</param>
    /// <param name="length">The bytes the payload occupies (1 to <see cref="MaxWireSize"/>); 0 on failure.</param>
    /// <returns>False when <paramref name="payload"/> is too short or malformed for the kind (never thrown).</returns>
    bool TryMeasure(ReadOnlySpan<byte> payload, out int length);

    /// <summary>
    /// The inverse of <see cref="Encode"/>: fills <see cref="EventEntry.Payload"/> (whole area zeroed first) and
    /// <see cref="EventEntry.PayloadLength"/> of <paramref name="entry"/> from the wire form, leaving every other field alone
    /// (kind, flags and ordinal come from the batch record; audience, class and call sequence are not on the wire). The
    /// payload length is the wire length, so an entry shorter than the descriptor length (a <c>Continuation</c> chunk) comes
    /// back zero-extended to it.
    /// </summary>
    /// <param name="payload">The record's bytes after its <c>kind u16</c>, and anything after them; valid only for this call.</param>
    /// <param name="entry">The entry to fill; unchanged when this returns false.</param>
    /// <param name="consumed">Bytes read (equal to <see cref="TryMeasure"/>'s length); 0 on failure.</param>
    /// <returns>False when <paramref name="payload"/> is too short or malformed for the kind (never thrown).</returns>
    bool TryDecode(ReadOnlySpan<byte> payload, ref EventEntry entry, out int consumed);
}
