namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// The reusable section buffer behind <see cref="CanonicalWriter"/> (ADR-0006 section 4: the writer buffers each section
/// because an <c>IBufferWriter&lt;byte&gt;</c> cannot be patched after <c>Advance</c>). One instance is owned by whatever
/// produces dumps (the world's dump service); it grows by doubling and never shrinks, so a warm dump allocates nothing.
/// Growth allocates, so it must only happen outside the tick: dumps are never on the tick path. Not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var scratch = new CanonicalBuffer();                 // once, outside the tick
/// var writer = new CanonicalWriter(output, scratch);   // per dump; reuses scratch
/// </code>
/// </example>
public sealed class CanonicalBuffer
{
    /// <summary>The initial capacity in bytes.</summary>
    public const int InitialCapacity = 1024;

    private byte[] _data;

    /// <summary>Creates a buffer of <see cref="InitialCapacity"/> bytes.</summary>
    public CanonicalBuffer()
    {
        _data = new byte[InitialCapacity];
    }

    /// <summary>The current capacity in bytes.</summary>
    public int Capacity => _data.Length;

    internal byte[] Data => _data;

    /// <summary>Grows the buffer, preserving its first <paramref name="used"/> bytes, so it holds at least <paramref name="required"/> bytes.</summary>
    /// <param name="required">The minimum capacity needed.</param>
    /// <param name="used">How many leading bytes to keep.</param>
    /// <returns>True when the buffer now holds <paramref name="required"/> bytes; false when that exceeds the maximum array length.</returns>
    internal bool Reserve(int required, int used)
    {
        if (required <= _data.Length)
        {
            return true;
        }

        if (required > Array.MaxLength)
        {
            return false;
        }

        long doubled = (long)_data.Length * 2;
        int target = (int)Math.Min(Math.Max(doubled, required), Array.MaxLength);
        byte[] grown = new byte[target];
        Buffer.BlockCopy(_data, 0, grown, 0, used);
        _data = grown;
        return true;
    }
}
