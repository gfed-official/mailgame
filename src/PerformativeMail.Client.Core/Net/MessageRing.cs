namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Fixed-capacity FIFO of length-prefixed messages stored in one preallocated byte array. Each
/// message is a <see cref="HeaderBytes"/>-byte header (channel byte, little-endian u32 payload
/// length) followed by the payload. Storage never grows: a write that does not fit is refused.
/// A payload that wraps the end of the array is exposed through a preallocated scratch buffer so
/// the reader always sees one contiguous span. Single-threaded.
/// </summary>
internal sealed class MessageRing
{
    /// <summary>Bytes of per-message header: channel (1) plus payload length (4).</summary>
    internal const int HeaderBytes = 5;

    private readonly byte[] _buffer;
    private readonly byte[] _scratch;
    private int _head;
    private int _used;

    /// <summary>Allocates the ring and its scratch buffer once.</summary>
    /// <param name="capacityBytes">Total ring storage; must hold at least one maximum-size message with its header.</param>
    /// <param name="maxMessageBytes">Largest payload accepted.</param>
    internal MessageRing(int capacityBytes, int maxMessageBytes)
    {
        _buffer = new byte[capacityBytes];
        _scratch = new byte[maxMessageBytes];
    }

    /// <summary>Total ring storage in bytes.</summary>
    internal int CapacityBytes => _buffer.Length;

    /// <summary>Bytes currently occupied, headers included.</summary>
    internal int UsedBytes => _used;

    /// <summary>Appends a message if it fits.</summary>
    /// <param name="channel">Channel recorded in the header.</param>
    /// <param name="payload">Bytes to copy; the caller has already checked the size limit.</param>
    /// <returns>True when stored; false when the ring has too little free space (nothing is written).</returns>
    internal bool TryWrite(NetChannel channel, ReadOnlySpan<byte> payload)
    {
        int total = HeaderBytes + payload.Length;
        if (total > _buffer.Length - _used)
        {
            return false;
        }

        int pos = (_head + _used) % _buffer.Length;
        Span<byte> header = stackalloc byte[HeaderBytes];
        header[0] = (byte)channel;
        int length = payload.Length;
        // Masked before narrowing: Client.Core builds checked in SIM_ASSERT configurations, where
        // a bare (byte) cast of a value > 255 throws OverflowException.
        header[1] = (byte)(length & 0xFF);
        header[2] = (byte)((length >> 8) & 0xFF);
        header[3] = (byte)((length >> 16) & 0xFF);
        header[4] = (byte)((length >> 24) & 0xFF);
        pos = CopyIn(header, pos);
        CopyIn(payload, pos);
        _used += total;
        return true;
    }

    /// <summary>Reads the oldest message's header without consuming it.</summary>
    /// <param name="channel">The channel byte, as stored.</param>
    /// <param name="length">The payload length.</param>
    /// <returns>True when a message is waiting.</returns>
    internal bool TryPeekHeader(out NetChannel channel, out int length)
    {
        if (_used < HeaderBytes)
        {
            channel = default;
            length = 0;
            return false;
        }

        channel = (NetChannel)_buffer[_head];
        length = _buffer[Wrap(_head + 1)]
            | (_buffer[Wrap(_head + 2)] << 8)
            | (_buffer[Wrap(_head + 3)] << 16)
            | (_buffer[Wrap(_head + 4)] << 24);
        return true;
    }

    /// <summary>
    /// The oldest message's payload as one contiguous span over ring (or scratch) storage. Valid
    /// until <see cref="Skip"/> is called or the next peek.
    /// </summary>
    /// <param name="length">The length returned by <see cref="TryPeekHeader"/>.</param>
    /// <returns>The payload bytes.</returns>
    internal ReadOnlySpan<byte> PeekPayload(int length)
    {
        int start = Wrap(_head + HeaderBytes);
        if (start + length <= _buffer.Length)
        {
            return new ReadOnlySpan<byte>(_buffer, start, length);
        }

        int first = _buffer.Length - start;
        Array.Copy(_buffer, start, _scratch, 0, first);
        Array.Copy(_buffer, 0, _scratch, first, length - first);
        return new ReadOnlySpan<byte>(_scratch, 0, length);
    }

    /// <summary>Consumes the oldest message.</summary>
    /// <param name="length">Its payload length.</param>
    internal void Skip(int length)
    {
        int total = HeaderBytes + length;
        _head = Wrap(_head + total);
        _used -= total;
    }

    private int Wrap(int index) => index >= _buffer.Length ? index - _buffer.Length : index;

    private int CopyIn(ReadOnlySpan<byte> source, int pos)
    {
        int first = Math.Min(source.Length, _buffer.Length - pos);
        source.Slice(0, first).CopyTo(new Span<byte>(_buffer, pos, first));
        int rest = source.Length - first;
        if (rest > 0)
        {
            source.Slice(first).CopyTo(new Span<byte>(_buffer, 0, rest));
        }

        return Wrap(pos + source.Length);
    }
}
