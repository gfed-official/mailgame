namespace PerformativeMail.Client.Core.Presentation;

/// <summary>
/// A consumer's read position on a <see cref="PresentationEventStream"/> (ADR-0016 section 5), from
/// <see cref="PresentationEventStream.OpenCursor"/>. The position lives in the stream, so copies of a cursor share it: an event is
/// never read twice through one cursor. A <c>default</c> cursor reads nothing. Main thread only.
/// </summary>
/// <example><code>while (cursor.TryRead(out PresentedEvent e)) { StartBeat(in e); }</code></example>
public readonly struct EventCursor
{
    private readonly PresentationEventStream? _stream;
    private readonly int _id;

    internal EventCursor(PresentationEventStream stream, int id)
    {
        _stream = stream;
        _id = id;
    }

    /// <summary>Reads the next unread event of the cursor's kinds, in (<c>sourceTick</c>, <c>ordinal</c>) order.</summary>
    /// <param name="e">The event; default when none is waiting.</param>
    /// <returns>False when every published event of the cursor's kinds has been read.</returns>
    public bool TryRead(out PresentedEvent e)
    {
        if (_stream is null)
        {
            e = default;
            return false;
        }

        return _stream.TryRead(_id, out e);
    }
}
