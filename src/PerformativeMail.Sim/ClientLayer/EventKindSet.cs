using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The event kinds an <see cref="IMirrorPart"/> is bound to (ADR-0016 Key Interfaces): a small immutable set, built at setup.
/// <see cref="Contains"/> is a linear scan over a handful of kinds and allocates nothing. A default value is empty.
/// </summary>
/// <example><code>EventKindSet kinds = EventKindSet.Of(RunEventKinds.DeliveriesState); bool hit = kinds.Contains(entry.Kind);</code></example>
public readonly struct EventKindSet
{
    private readonly EventKind[]? _kinds;

    private EventKindSet(EventKind[] kinds)
    {
        _kinds = kinds;
    }

    /// <summary>The number of kinds in the set.</summary>
    public int Count => _kinds?.Length ?? 0;

    /// <summary>Builds a set (setup time; copies the kinds).</summary>
    /// <param name="kinds">The kinds.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kinds"/> is null.</exception>
    public static EventKindSet Of(params EventKind[] kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        return new EventKindSet((EventKind[])kinds.Clone());
    }

    /// <summary>True when <paramref name="kind"/> is in the set. 0 B.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>True when present.</returns>
    public bool Contains(EventKind kind)
    {
        EventKind[]? kinds = _kinds;
        if (kinds is null)
        {
            return false;
        }

        for (int i = 0; i < kinds.Length; i++)
        {
            if (kinds[i] == kind)
            {
                return true;
            }
        }

        return false;
    }
}
