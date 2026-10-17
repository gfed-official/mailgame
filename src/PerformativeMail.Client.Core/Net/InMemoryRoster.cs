using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// The M-1a <see cref="IRoster"/>: a fixed-capacity in-memory table with a linear scan (capacity is at most
/// <see cref="PlayerSlot.Count"/> in practice). Allocates once, at construction.
/// </summary>
/// <example>
/// <code>
/// var roster = new InMemoryRoster();
/// roster.Record(1UL, playerId);
/// </code>
/// </example>
public sealed class InMemoryRoster : IRoster
{
    private readonly ulong[] _keys;
    private readonly EntityId[] _ids;
    private int _count;

    /// <summary>Creates an empty roster.</summary>
    /// <param name="capacity">Maximum number of accounts; at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is below 1.</exception>
    public InMemoryRoster(int capacity = PlayerSlot.Count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _keys = new ulong[capacity];
        _ids = new EntityId[capacity];
    }

    /// <summary>Number of recorded accounts.</summary>
    public int Count => _count;

    /// <inheritdoc />
    public EntityId Lookup(ulong accountKey)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_keys[i] == accountKey)
            {
                return _ids[i];
            }
        }

        return EntityId.None;
    }

    /// <inheritdoc />
    public bool Record(ulong accountKey, EntityId playerId)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_keys[i] == accountKey)
            {
                _ids[i] = playerId;
                return true;
            }
        }

        if (_count == _keys.Length)
        {
            return false;
        }

        _keys[_count] = accountKey;
        _ids[_count] = playerId;
        _count++;
        return true;
    }
}
