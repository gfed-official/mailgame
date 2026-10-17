using System.Buffers.Binary;

namespace PerformativeMail.Sim.Content.Hashing;

/// <summary>
/// A hand-written managed SHA-256 (FIPS 180-2) with no platform crypto: the Sim references no
/// <c>System.Security.Cryptography</c> type (ADR-0010 section 6; on Linux the BCL would need the system OpenSSL, and
/// availability inside a Godot export is NOT SOURCEABLE). Incremental: feed any chunking to <see cref="Update"/>, then call
/// <see cref="Finish"/> once. Pure 32-bit unsigned arithmetic, no unsafe code, no static mutable state.
/// <para>Not thread-safe: use one instance per thread. Allocates its working buffers once per instance.</para>
/// </summary>
/// <example>
/// <code>
/// Hash256 d = Sha256.Hash("abc"u8);                     // ba7816bf...
/// var sha = new Sha256();
/// sha.Update("a"u8); sha.Update("bc"u8);
/// Hash256 same = sha.Finish();                          // equal to d
/// </code>
/// </example>
public sealed class Sha256
{
    private readonly uint[] _h = new uint[8];
    private readonly uint[] _w = new uint[64];
    private readonly byte[] _block = new byte[64];
    private int _blockLength;
    private ulong _totalBytes;
    private bool _finished;

    /// <summary>Creates a hasher in the initial state.</summary>
    /// <example><code>var sha = new Sha256();</code></example>
    public Sha256()
    {
        Reset();
    }

    // The round constants (FIPS 180-2 section 4.2.2). A ReadOnlySpan over a constant array is baked into the assembly
    // data, so there is no static field and no per-call allocation.
    private static ReadOnlySpan<uint> K => new uint[]
    {
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
    };

    /// <summary>One-shot digest of <paramref name="data"/>.</summary>
    /// <param name="data">The message.</param>
    /// <returns>The 32-byte digest.</returns>
    /// <example><code>Hash256 d = Sha256.Hash(new byte[0]);   // e3b0c442...</code></example>
    public static Hash256 Hash(ReadOnlySpan<byte> data)
    {
        var sha = new Sha256();
        sha.Update(data);
        return sha.Finish();
    }

    /// <summary>Returns the hasher to its initial state so the instance can be reused.</summary>
    /// <example><code>sha.Reset();</code></example>
    public void Reset()
    {
        _h[0] = 0x6a09e667;
        _h[1] = 0xbb67ae85;
        _h[2] = 0x3c6ef372;
        _h[3] = 0xa54ff53a;
        _h[4] = 0x510e527f;
        _h[5] = 0x9b05688c;
        _h[6] = 0x1f83d9ab;
        _h[7] = 0x5be0cd19;
        _blockLength = 0;
        _totalBytes = 0;
        _finished = false;
    }

    /// <summary>Appends message bytes.</summary>
    /// <param name="data">The next chunk; any length, including empty.</param>
    /// <exception cref="InvalidOperationException">Called after <see cref="Finish"/> without a <see cref="Reset"/>.</exception>
    /// <example><code>sha.Update("abc"u8);</code></example>
    public void Update(ReadOnlySpan<byte> data)
    {
        if (_finished)
        {
            throw new InvalidOperationException("The hasher is finished; call Reset before reuse.");
        }

        _totalBytes += (ulong)data.Length;
        if (_blockLength > 0)
        {
            int take = Math.Min(64 - _blockLength, data.Length);
            data.Slice(0, take).CopyTo(_block.AsSpan(_blockLength));
            _blockLength += take;
            data = data.Slice(take);
            if (_blockLength < 64)
            {
                return;
            }

            Compress(_block);
            _blockLength = 0;
        }

        while (data.Length >= 64)
        {
            Compress(data.Slice(0, 64));
            data = data.Slice(64);
        }

        data.CopyTo(_block);
        _blockLength = data.Length;
    }

    /// <summary>Pads, finishes and returns the digest. The hasher must be <see cref="Reset"/> before further use.</summary>
    /// <returns>The 32-byte digest.</returns>
    /// <exception cref="InvalidOperationException">Already finished.</exception>
    /// <example><code>Hash256 d = sha.Finish();</code></example>
    public Hash256 Finish()
    {
        if (_finished)
        {
            throw new InvalidOperationException("The hasher is already finished.");
        }

        ulong bitLength = _totalBytes * 8UL;
        _block[_blockLength++] = 0x80;
        if (_blockLength > 56)
        {
            _block.AsSpan(_blockLength).Clear();
            Compress(_block);
            _blockLength = 0;
        }

        _block.AsSpan(_blockLength, 56 - _blockLength).Clear();
        BinaryPrimitives.WriteUInt64BigEndian(_block.AsSpan(56), bitLength);
        Compress(_block);
        _finished = true;

        Span<byte> digest = stackalloc byte[Hash256.Length];
        for (int i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(digest.Slice(4 * i), _h[i]);
        }

        return new Hash256(digest);
    }

    private static uint Rotr(uint x, int n) => (x >> n) | (x << (32 - n));

    private void Compress(ReadOnlySpan<byte> block)
    {
        uint[] w = _w;
        for (int t = 0; t < 16; t++)
        {
            w[t] = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(4 * t));
        }

        for (int t = 16; t < 64; t++)
        {
            uint s0 = Rotr(w[t - 15], 7) ^ Rotr(w[t - 15], 18) ^ (w[t - 15] >> 3);
            uint s1 = Rotr(w[t - 2], 17) ^ Rotr(w[t - 2], 19) ^ (w[t - 2] >> 10);
            w[t] = unchecked(w[t - 16] + s0 + w[t - 7] + s1);
        }

        uint a = _h[0], b = _h[1], c = _h[2], d = _h[3], e = _h[4], f = _h[5], g = _h[6], h = _h[7];
        ReadOnlySpan<uint> k = K;
        for (int t = 0; t < 64; t++)
        {
            uint bigS1 = Rotr(e, 6) ^ Rotr(e, 11) ^ Rotr(e, 25);
            uint ch = (e & f) ^ (~e & g);
            uint t1 = unchecked(h + bigS1 + ch + k[t] + w[t]);
            uint bigS0 = Rotr(a, 2) ^ Rotr(a, 13) ^ Rotr(a, 22);
            uint maj = (a & b) ^ (a & c) ^ (b & c);
            uint t2 = unchecked(bigS0 + maj);
            h = g;
            g = f;
            f = e;
            e = unchecked(d + t1);
            d = c;
            c = b;
            b = a;
            a = unchecked(t1 + t2);
        }

        unchecked
        {
            _h[0] += a;
            _h[1] += b;
            _h[2] += c;
            _h[3] += d;
            _h[4] += e;
            _h[5] += f;
            _h[6] += g;
            _h[7] += h;
        }
    }
}
