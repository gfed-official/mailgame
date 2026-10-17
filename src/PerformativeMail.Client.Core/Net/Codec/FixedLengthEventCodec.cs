using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// The generic passthrough event codec for kinds whose wire form equals their payload: writes exactly
/// <see cref="MaxWireSize"/> payload bytes. A <c>Continuation</c> chunk shorter than the descriptor length still writes the
/// fixed length (the entry's tail is zero-filled, ADR-0008 section 1), so every record of the kind has one size. Feature epics
/// register real codecs (and their packing) instead where the wire form differs.
/// </summary>
/// <example><code>var codec = new FixedLengthEventCodec(codecId: 910, length: 4);</code></example>
public sealed class FixedLengthEventCodec : IEventCodec
{
    /// <summary>Creates the codec.</summary>
    /// <param name="codecId">The <c>WireCodecId</c> it serves (non-zero).</param>
    /// <param name="length">The fixed wire length, 1 to 48.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of range.</exception>
    public FixedLengthEventCodec(ushort codecId, int length)
    {
        ArgumentOutOfRangeException.ThrowIfZero(codecId);
        if (length < 1 || length > EventPayload.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"The length must be 1 to {EventPayload.Size}.");
        }

        CodecId = codecId;
        MaxWireSize = length;
    }

    /// <inheritdoc />
    public ushort CodecId { get; }

    /// <inheritdoc />
    public int MaxWireSize { get; }

    /// <inheritdoc />
    public int Encode(in EventEntry entry, Span<byte> destination)
    {
        int length = MaxWireSize;
        if (entry.PayloadLength > length || destination.Length < length)
        {
            return -1;
        }

        ReadOnlySpan<byte> payload = entry.Payload;
        payload.Slice(0, length).CopyTo(destination);
        return length;
    }

    /// <inheritdoc />
    public bool TryMeasure(ReadOnlySpan<byte> payload, out int length)
    {
        bool fits = payload.Length >= MaxWireSize;
        length = fits ? MaxWireSize : 0;
        return fits;
    }

    /// <inheritdoc />
    public bool TryDecode(ReadOnlySpan<byte> payload, ref EventEntry entry, out int consumed)
    {
        if (!TryMeasure(payload, out consumed))
        {
            return false;
        }

        Span<byte> target = entry.Payload;
        target.Clear();
        payload.Slice(0, consumed).CopyTo(target);
        entry.PayloadLength = (byte)consumed;
        return true;
    }
}
