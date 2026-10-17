using System.Buffers;
using System.Buffers.Binary;

namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// A forward-only little-endian writer for the canonical state dump (ADR-0006 section 4). Fields are written one at a time
/// (<see cref="WriteU8"/> to <see cref="WriteBytes"/>); a struct is never blitted, so padding and endianness cannot leak into
/// the bytes. Output is grouped in sections: <see cref="BeginSection"/> starts buffering a payload in the shared
/// <see cref="CanonicalBuffer"/>, and <see cref="EndSection"/> flushes <c>sectionId u16, byteLength u32, payload</c> to the
/// <see cref="IBufferWriter{T}"/> in one go. System-agnostic: the persistence format (ADR-0024) and JoinState reuse it.
/// <para>
/// Misuse (a write outside a section, a nested or unmatched begin/end, a section over the maximum array length) asserts under
/// <c>SIM_ASSERT</c> with <see cref="SimAssertCode.DumpWriterMisuse"/>; other builds drop the offending call. If a
/// contributor throws mid-section, bytes already flushed to the output stay there: the caller should discard the output.
/// </para>
/// <para>
/// Not thread-safe. Allocation-free once the <see cref="CanonicalBuffer"/> has grown to the largest section.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var output = new ArrayBufferWriter&lt;byte&gt;();
/// var writer = new CanonicalWriter(output, new CanonicalBuffer());
/// writer.WriteHeader();                  // 50 4D 53 44 01 00
/// writer.BeginSection(7);
/// writer.WriteU16(0x1234);               // payload 34 12
/// writer.EndSection();                   // appends 07 00 | 02 00 00 00 | 34 12
/// </code>
/// </example>
public ref struct CanonicalWriter
{
    /// <summary>The dump magic <c>"PMSD"</c> read as a little-endian u32 (bytes 50 4D 53 44).</summary>
    public const uint Magic = 0x44534D50u;

    /// <summary>The dump format version written by <see cref="WriteHeader"/>.</summary>
    public const ushort FormatVersion = 1;

    private readonly IBufferWriter<byte> _output;
    private readonly CanonicalBuffer _scratch;
    private int _length;
    private ushort _sectionId;
    private bool _inSection;

    /// <summary>Creates a writer that flushes finished sections to <paramref name="output"/>.</summary>
    /// <param name="output">The destination; receives the header and every ended section.</param>
    /// <param name="scratch">The reusable section buffer; owned by the caller and reusable across writers.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <example><code>var w = new CanonicalWriter(new ArrayBufferWriter&lt;byte&gt;(), new CanonicalBuffer());</code></example>
    public CanonicalWriter(IBufferWriter<byte> output, CanonicalBuffer scratch)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scratch);
        _output = output;
        _scratch = scratch;
        _length = 0;
        _sectionId = 0;
        _inSection = false;
    }

    /// <summary>The payload bytes buffered so far in the open section (0 outside a section).</summary>
    public readonly int SectionLength => _length;

    /// <summary>True between <see cref="BeginSection"/> and <see cref="EndSection"/>.</summary>
    public readonly bool InSection => _inSection;

    /// <summary>
    /// Writes the dump header straight to the output: <c>"PMSD"</c> (u32 little-endian 0x44534D50) then
    /// <see cref="FormatVersion"/> as a u16. Legal only outside a section.
    /// </summary>
    /// <example><code>w.WriteHeader();   // 50 4D 53 44 01 00</code></example>
    public void WriteHeader()
    {
        if (_inSection)
        {
            SimAssert.Fail(SimAssertCode.DumpWriterMisuse);
            return;
        }

        Span<byte> span = _output.GetSpan(6);
        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4), FormatVersion);
        _output.Advance(6);
    }

    /// <summary>Starts a section; the payload is buffered until <see cref="EndSection"/>.</summary>
    /// <param name="id">The section id (see <c>SectionIds</c>).</param>
    /// <example><code>w.BeginSection(SectionIds.SimCore);</code></example>
    public void BeginSection(ushort id)
    {
        if (_inSection)
        {
            SimAssert.Fail(SimAssertCode.DumpWriterMisuse);
            return;
        }

        _inSection = true;
        _sectionId = id;
        _length = 0;
    }

    /// <summary>Ends the open section and appends <c>sectionId u16, byteLength u32, payload</c> to the output.</summary>
    /// <example><code>w.EndSection();</code></example>
    public void EndSection()
    {
        if (!_inSection)
        {
            SimAssert.Fail(SimAssertCode.DumpWriterMisuse);
            return;
        }

        int length = _length;
        Span<byte> span = _output.GetSpan(6 + length);
        BinaryPrimitives.WriteUInt16LittleEndian(span, _sectionId);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(2), (uint)length);
        new ReadOnlySpan<byte>(_scratch.Data, 0, length).CopyTo(span.Slice(6));
        _output.Advance(6 + length);
        _inSection = false;
        _length = 0;
    }

    /// <summary>Writes one byte.</summary>
    /// <param name="v">The value.</param>
    /// <example><code>w.WriteU8(0x7F);</code></example>
    public void WriteU8(byte v)
    {
        Span<byte> span = Claim(1);
        if (span.Length == 0)
        {
            return;
        }

        span[0] = v;
    }

    /// <summary>Writes a little-endian u16.</summary>
    /// <param name="v">The value.</param>
    /// <example><code>w.WriteU16(0x1234);   // 34 12</code></example>
    public void WriteU16(ushort v)
    {
        Span<byte> span = Claim(2);
        if (span.Length == 0)
        {
            return;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(span, v);
    }

    /// <summary>Writes a little-endian u32.</summary>
    /// <param name="v">The value.</param>
    /// <example><code>w.WriteU32(1);   // 01 00 00 00</code></example>
    public void WriteU32(uint v)
    {
        Span<byte> span = Claim(4);
        if (span.Length == 0)
        {
            return;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(span, v);
    }

    /// <summary>Writes a little-endian u64.</summary>
    /// <param name="v">The value.</param>
    /// <example><code>w.WriteU64(rng.State);</code></example>
    public void WriteU64(ulong v)
    {
        Span<byte> span = Claim(8);
        if (span.Length == 0)
        {
            return;
        }

        BinaryPrimitives.WriteUInt64LittleEndian(span, v);
    }

    /// <summary>Writes a little-endian two's-complement i32.</summary>
    /// <param name="v">The value.</param>
    /// <example><code>w.WriteI32(-1);   // FF FF FF FF</code></example>
    public void WriteI32(int v)
    {
        Span<byte> span = Claim(4);
        if (span.Length == 0)
        {
            return;
        }

        BinaryPrimitives.WriteInt32LittleEndian(span, v);
    }

    /// <summary>Writes a little-endian two's-complement i64.</summary>
    /// <param name="v">The value.</param>
    /// <example><code>w.WriteI64(cents.Value);</code></example>
    public void WriteI64(long v)
    {
        Span<byte> span = Claim(8);
        if (span.Length == 0)
        {
            return;
        }

        BinaryPrimitives.WriteInt64LittleEndian(span, v);
    }

    /// <summary>Writes a bool as one byte: 1 for true, 0 for false.</summary>
    /// <param name="v">The value.</param>
    /// <example><code>w.WriteBool(slot.Occupied);</code></example>
    public void WriteBool(bool v) => WriteU8(v ? (byte)1 : (byte)0);

    /// <summary>Writes raw bytes with no length prefix (the caller writes a count first when the length varies).</summary>
    /// <param name="v">The bytes.</param>
    /// <example><code>w.WriteBytes(op.Payload);</code></example>
    public void WriteBytes(ReadOnlySpan<byte> v)
    {
        if (v.Length == 0)
        {
            if (!_inSection)
            {
                SimAssert.Fail(SimAssertCode.DumpWriterMisuse);
            }

            return;
        }

        Span<byte> span = Claim(v.Length);
        if (span.Length == 0)
        {
            return;
        }

        v.CopyTo(span);
    }

    // Reserves n bytes at the end of the open section and returns them; empty (after asserting) when not in a section or too large.
    private Span<byte> Claim(int n)
    {
        if (!_inSection || n > Array.MaxLength - _length || !_scratch.Reserve(_length + n, _length))
        {
            SimAssert.Fail(SimAssertCode.DumpWriterMisuse);
            return default;
        }

        Span<byte> span = new Span<byte>(_scratch.Data, _length, n);
        _length += n;
        return span;
    }
}
