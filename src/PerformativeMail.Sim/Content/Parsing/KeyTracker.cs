namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The keys seen so far in the open objects of one file walk: a small list of (start, length) entries into one growing byte
/// pool, compared by ordinal bytes (no <c>Dictionary</c>). Each object remembers where its keys began, so closing it drops
/// them. Not thread-safe; one instance per walk.
/// </summary>
/// <example>
/// <code>
/// var keys = new KeyTracker();
/// bool first = keys.TryAdd("id"u8, keys.KeyCount);    // true
/// bool again = keys.TryAdd("id"u8, 0);                // false: a duplicate
/// </code>
/// </example>
internal sealed class KeyTracker
{
    private readonly List<(int Start, int Length)> _keys = new();
    private byte[] _pool = new byte[256];

    /// <summary>The number of keys held (the key base of an object opened now).</summary>
    public int KeyCount => _keys.Count;

    /// <summary>The number of pool bytes used (the pool base of an object opened now).</summary>
    public int PoolUsed { get; private set; }

    /// <summary>Adds <paramref name="name"/> to the object whose keys begin at <paramref name="keyBase"/>.</summary>
    /// <param name="name">The unescaped key bytes.</param>
    /// <param name="keyBase">The <see cref="KeyCount"/> when the object was opened.</param>
    /// <returns>False when the object already has this key (nothing is added).</returns>
    public bool TryAdd(ReadOnlySpan<byte> name, int keyBase)
    {
        for (int i = keyBase; i < _keys.Count; i++)
        {
            (int start, int length) = _keys[i];
            if (_pool.AsSpan(start, length).SequenceEqual(name))
            {
                return false;
            }
        }

        if (PoolUsed + name.Length > _pool.Length)
        {
            Array.Resize(ref _pool, Math.Max(_pool.Length * 2, PoolUsed + name.Length));
        }

        name.CopyTo(_pool.AsSpan(PoolUsed));
        _keys.Add((PoolUsed, name.Length));
        PoolUsed += name.Length;
        return true;
    }

    /// <summary>Drops the keys of a closed object.</summary>
    /// <param name="keyBase">The key base recorded when it opened.</param>
    /// <param name="poolBase">The pool base recorded when it opened.</param>
    public void Truncate(int keyBase, int poolBase)
    {
        _keys.RemoveRange(keyBase, _keys.Count - keyBase);
        PoolUsed = poolBase;
    }
}
