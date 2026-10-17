namespace PerformativeMail.Sim.Core;

/// <summary>
/// A pull reader's handle on the log (ADR-0008 section 4): the reader's <see cref="ReaderId"/> plus a mirror of its
/// position. The authoritative position lives in the <see cref="EventLog"/>'s cursor registry (indexed by reader), which Sim
/// Core rewinds to 0 every tick; <see cref="EventLog.Read"/> updates both, so a copied or stale handle cannot replay entries
/// or survive into the next tick. Create it once at composition and keep it in the reading system.
/// </summary>
/// <example>
/// <code>
/// EventCursor cursor = new(myReaderId);
/// while (log.Read(ref cursor, out EventRef e)) { /* handle e */ }
/// </code>
/// </example>
public struct EventCursor
{
    /// <summary>Creates a cursor for <paramref name="reader"/> at position 0.</summary>
    /// <param name="reader">An id bound with <see cref="SimWorldBuilder.BindEventReader"/>.</param>
    public EventCursor(ReaderId reader)
    {
        ReaderId = reader;
        Position = 0;
    }

    /// <summary>The reader this cursor belongs to.</summary>
    public ReaderId ReaderId { get; }

    /// <summary>Index of the next entry this reader will look at: a read-only mirror of the registry, updated by <see cref="EventLog.Read"/> only.</summary>
    public int Position { get; internal set; }
}
