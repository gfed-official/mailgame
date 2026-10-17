using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Hashing;

/// <summary>
/// The forward-only little-endian writer for the content canonical encoding (content-data F6, ADR-0010 section 6). Fixed-width
/// values go through <see cref="BinaryPrimitives"/>; strings are UTF-8 with no normalisation; counts are u16 and a count or
/// string above 65 535 throws <see cref="ContentEncodingException"/> (CV-GEN-12). It is deliberately separate from the
/// state-dump <see cref="CanonicalWriter"/>, which frames sections and asserts rather than throws.
/// <para>Not thread-safe; allocation-free except for <see cref="WriteSortedSet(ReadOnlySpan{ushort})"/> (a copy to sort).</para>
/// </summary>
/// <example>
/// <code>
/// var buffer = new ArrayBufferWriter&lt;byte&gt;();
/// var w = new CanonicalContentWriter(buffer);
/// w.WriteId("letter");                // 06 6C 65 74 74 65 72
/// w.WriteU16(1);                      // 01 00
/// w.WriteSortedSet(stackalloc ushort[] { 3, 1 });   // 02 00 01 00 03 00
/// </code>
/// </example>
public ref struct CanonicalContentWriter
{
    /// <summary>The catalogue rule id for an over-wide count or string (CV-GEN-12).</summary>
    public const string RangeRuleId = "CV-GEN-12";

    /// <summary>The catalogue rule id for an over-long def id (CV-GEN-02).</summary>
    public const string IdRuleId = "CV-GEN-02";

    /// <summary>The largest list count or string byte length the format can hold.</summary>
    public const int MaxCount = ushort.MaxValue;

    private readonly IBufferWriter<byte> _output;

    /// <summary>Creates a writer appending to <paramref name="output"/>.</summary>
    /// <param name="output">The destination.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    /// <example><code>var w = new CanonicalContentWriter(new ArrayBufferWriter&lt;byte&gt;());</code></example>
    public CanonicalContentWriter(IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>Writes one byte.</summary>
    /// <example><code>w.WriteU8(1);</code></example>
    public void WriteU8(byte v)
    {
        Span<byte> s = _output.GetSpan(1);
        s[0] = v;
        _output.Advance(1);
    }

    /// <summary>Writes a bool as one byte (0 or 1).</summary>
    /// <example><code>w.WriteBool(true);   // 01</code></example>
    public void WriteBool(bool v) => WriteU8(v ? (byte)1 : (byte)0);

    /// <summary>Writes a little-endian u16.</summary>
    /// <example><code>w.WriteU16(20);   // 14 00</code></example>
    public void WriteU16(ushort v)
    {
        Span<byte> s = _output.GetSpan(2);
        BinaryPrimitives.WriteUInt16LittleEndian(s, v);
        _output.Advance(2);
    }

    /// <summary>Writes an enum member as its explicit numeric value in a little-endian u16 (CD8).</summary>
    /// <example><code>w.WriteEnum((ushort)WeightClass.Light);</code></example>
    public void WriteEnum(ushort numericValue) => WriteU16(numericValue);

    /// <summary>Writes a little-endian i32.</summary>
    /// <example><code>w.WriteI32(-1);   // FF FF FF FF</code></example>
    public void WriteI32(int v)
    {
        Span<byte> s = _output.GetSpan(4);
        BinaryPrimitives.WriteInt32LittleEndian(s, v);
        _output.Advance(4);
    }

    /// <summary>Writes a little-endian i64.</summary>
    /// <example><code>w.WriteI64(400);   // 90 01 00 00 00 00 00 00</code></example>
    public void WriteI64(long v)
    {
        Span<byte> s = _output.GetSpan(8);
        BinaryPrimitives.WriteInt64LittleEndian(s, v);
        _output.Advance(8);
    }

    /// <summary>Writes a def reference as its u16 ordinal, <c>0xFFFF</c> for none.</summary>
    /// <example><code>w.WriteDefIndex(DefIndex.None);   // FF FF</code></example>
    public void WriteDefIndex(DefIndex v) => WriteU16(v.Value);

    /// <summary>Writes an optional-field presence byte (0 absent, 1 present); the caller writes the value when present.</summary>
    /// <example><code>w.WritePresence(def.HasBuyPrice); if (def.HasBuyPrice) { w.WriteI64(def.BuyPriceCents); }</code></example>
    public void WritePresence(bool present) => WriteBool(present);

    /// <summary>Writes a list or table count as a u16.</summary>
    /// <param name="count">The entry count, 0 to 65 535.</param>
    /// <exception cref="ContentEncodingException">The count is negative or above 65 535 (CV-GEN-12).</exception>
    /// <example><code>w.WriteCount(6);   // 06 00</code></example>
    public void WriteCount(int count)
    {
        if ((uint)count > MaxCount)
        {
            throw new ContentEncodingException(RangeRuleId, "a list of " + count + " entries exceeds the 65535-entry limit");
        }

        WriteU16((ushort)count);
    }

    /// <summary>Writes a def id as <c>u8 length, UTF-8 bytes</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is null.</exception>
    /// <exception cref="ContentEncodingException">The UTF-8 form is over 255 bytes (CV-GEN-02).</exception>
    /// <example><code>w.WriteId("letter");   // 06 6C 65 74 74 65 72</code></example>
    public void WriteId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int byteCount = Encoding.UTF8.GetByteCount(id);
        if (byteCount > byte.MaxValue)
        {
            throw new ContentEncodingException(IdRuleId, "a def id of " + byteCount + " UTF-8 bytes exceeds the 255-byte length prefix");
        }

        WriteU8((byte)byteCount);
        WriteUtf8(id, byteCount);
    }

    /// <summary>Writes a string as <c>u16 byte length, UTF-8 bytes</c> with no normalisation (localisation keys, balance keys).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ContentEncodingException">The UTF-8 form is over 65 535 bytes (CV-GEN-12).</exception>
    /// <example><code>w.WriteString("mail.letter");</code></example>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > MaxCount)
        {
            throw new ContentEncodingException(RangeRuleId, "a string of " + byteCount + " UTF-8 bytes exceeds the 65535-byte limit");
        }

        WriteU16((ushort)byteCount);
        WriteUtf8(value, byteCount);
    }

    /// <summary>
    /// Writes a set-valued list: <c>u16 count</c> then the values sorted ascending (so the supplied order never changes the bytes).
    /// </summary>
    /// <param name="values">The numeric values (enum numeric values or <c>DefIndex.Value</c>).</param>
    /// <exception cref="ContentEncodingException">More than 65 535 values (CV-GEN-12).</exception>
    /// <example><code>w.WriteSortedSet(stackalloc ushort[] { 3, 1 });   // 02 00 01 00 03 00</code></example>
    public void WriteSortedSet(ReadOnlySpan<ushort> values)
    {
        WriteCount(values.Length);
        if (values.Length == 0)
        {
            return;
        }

        ushort[] sorted = values.ToArray();
        StableSort.Sort<ushort, UShortAscending>(sorted.AsSpan(), new ushort[sorted.Length].AsSpan(), new UShortAscending());
        foreach (ushort v in sorted)
        {
            WriteU16(v);
        }
    }

    private void WriteUtf8(string value, int byteCount)
    {
        if (byteCount == 0)
        {
            return;
        }

        Span<byte> s = _output.GetSpan(byteCount);
        int written = Encoding.UTF8.GetBytes(value, s);
        _output.Advance(written);
    }

    private readonly struct UShortAscending : IComparer<ushort>
    {
        public int Compare(ushort x, ushort y) => x.CompareTo(y);
    }
}
