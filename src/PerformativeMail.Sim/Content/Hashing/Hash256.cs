using System.Buffers.Binary;

namespace PerformativeMail.Sim.Content.Hashing;

/// <summary>
/// A 32-byte SHA-256 digest as a value type with explicit, ordinal byte equality (the join check compares all 32 bytes,
/// content-data F6). The display form is the first 4 bytes as 8 lowercase hex characters. Immutable; safe from any thread.
/// </summary>
/// <example>
/// <code>
/// Hash256 h = ContentHash.Compute(BuiltInContent.CreateM1a());
/// string shown = h.ToDisplayString();   // "1a2b3c4d"
/// bool same = h == ContentHash.Compute(BuiltInContent.CreateM1a());   // true
/// </code>
/// </example>
public readonly struct Hash256 : IEquatable<Hash256>
{
    /// <summary>The digest length in bytes.</summary>
    public const int Length = 32;

    // The digest bytes as four big-endian 64-bit words: word 0 holds bytes 0 to 7.
    private readonly ulong _w0;
    private readonly ulong _w1;
    private readonly ulong _w2;
    private readonly ulong _w3;

    /// <summary>Wraps exactly 32 digest bytes.</summary>
    /// <param name="bytes">The digest bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not 32 bytes long.</exception>
    /// <example><code>var h = new Hash256(new byte[32]);</code></example>
    public Hash256(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException("A Hash256 is exactly 32 bytes.", nameof(bytes));
        }

        _w0 = BinaryPrimitives.ReadUInt64BigEndian(bytes);
        _w1 = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(8));
        _w2 = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(16));
        _w3 = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(24));
    }

    /// <summary>Copies the 32 digest bytes to <paramref name="destination"/>.</summary>
    /// <param name="destination">At least 32 bytes of space.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 32 bytes.</exception>
    /// <example><code>Span&lt;byte&gt; buf = stackalloc byte[32]; hash.CopyTo(buf);</code></example>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException("The destination must hold 32 bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination, _w0);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(8), _w1);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(16), _w2);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(24), _w3);
    }

    /// <summary>The digest as a new 32-byte array.</summary>
    /// <example><code>byte[] raw = hash.ToArray();</code></example>
    public byte[] ToArray()
    {
        byte[] bytes = new byte[Length];
        CopyTo(bytes);
        return bytes;
    }

    /// <summary>The first 4 bytes as 8 lowercase hex characters (the UI / log form).</summary>
    /// <example><code>string s = hash.ToDisplayString();   // for example "e3b0c442"</code></example>
    public string ToDisplayString() => Hex(4);

    /// <summary>All 32 bytes as 64 lowercase hex characters.</summary>
    /// <example><code>string full = hash.ToHexString();</code></example>
    public string ToHexString() => Hex(Length);

    /// <inheritdoc />
    public override string ToString() => ToDisplayString();

    /// <inheritdoc />
    public bool Equals(Hash256 other) => _w0 == other._w0 && _w1 == other._w1 && _w2 == other._w2 && _w3 == other._w3;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Hash256 other && Equals(other);

    /// <summary>A fold of the digest words. Not for use in simulation state (ADR-0006 rule 6); present to honour the Equals contract.</summary>
    public override int GetHashCode() => unchecked((int)(_w0 ^ (_w0 >> 32)));

    /// <summary>Byte-wise equality of all 32 bytes.</summary>
    public static bool operator ==(Hash256 left, Hash256 right) => left.Equals(right);

    /// <summary>Byte-wise inequality.</summary>
    public static bool operator !=(Hash256 left, Hash256 right) => !left.Equals(right);

    private string Hex(int byteCount)
    {
        Span<byte> bytes = stackalloc byte[Length];
        CopyTo(bytes);
        Span<char> chars = stackalloc char[Length * 2];
        for (int i = 0; i < byteCount; i++)
        {
            chars[2 * i] = Digit(bytes[i] >> 4);
            chars[(2 * i) + 1] = Digit(bytes[i] & 0xF);
        }

        return new string(chars.Slice(0, byteCount * 2));
    }

    private static char Digit(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'a' + (nibble - 10));
}
