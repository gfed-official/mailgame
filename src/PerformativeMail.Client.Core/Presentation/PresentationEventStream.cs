using PerformativeMail.Client.Core.Net;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Presentation;

/// <summary>
/// The outcome-beat feed (ADR-0016 section 5; godot-host-shell story 006). Boot step 7 sets it as the client adapter's
/// <see cref="ClientAdapter.DecodedEvents"/>, so every batch entry is published in the frame the batch is decoded, before the client
/// tick whose <c>MirrorDrain</c> applies it. The adapter decodes batches in tick order and records in ordinal order, so publish order
/// is (<c>sourceTick</c>, <c>ordinal</c>) order and nothing re-sorts. Each consumer opens its own <see cref="EventCursor"/>; a cursor
/// starts at the end of the stream (it never sees events published before it opened) and reads each event of its kinds once.
/// Events every cursor has passed are released; with no cursor open nothing is kept. Main thread only.
/// </summary>
/// <example>
/// <code>
/// EventCursor beats = deps.Events.OpenCursor(EventKindSet.Of(MailEventKinds.DeliveryOutcome));
/// while (beats.TryRead(out PresentedEvent e)) { /* start the beat */ }
/// </code>
/// </example>
public sealed class PresentationEventStream : IDecodedEventObserver
{
    private readonly List<PresentedEvent> _events = [];
    private readonly List<long> _positions = [];
    private readonly List<EventKindSet> _kinds = [];
    private long _first;

    /// <summary>Events published since the stream was created.</summary>
    public long PublishedCount { get; private set; }

    /// <summary>Opens a cursor over the given kinds, positioned after everything published so far.</summary>
    /// <param name="kinds">The kinds the cursor reads; others are skipped.</param>
    /// <returns>The cursor.</returns>
    public EventCursor OpenCursor(EventKindSet kinds)
    {
        _positions.Add(_first + _events.Count);
        _kinds.Add(kinds);
        return new EventCursor(this, _positions.Count - 1);
    }

    /// <inheritdoc />
    public void OnDecoded(uint sourceTick, in EventEntry entry)
    {
        PublishedCount++;
        Release();
        if (_positions.Count == 0)
        {
            _first++;
            return;
        }

        _events.Add(new PresentedEvent(sourceTick, in entry));
    }

    internal bool TryRead(int cursor, out PresentedEvent e)
    {
        long position = _positions[cursor];
        EventKindSet kinds = _kinds[cursor];
        long end = _first + _events.Count;
        while (position < end)
        {
            PresentedEvent candidate = _events[(int)(position - _first)];
            position++;
            if (kinds.Contains(candidate.Kind))
            {
                _positions[cursor] = position;
                e = candidate;
                return true;
            }
        }

        _positions[cursor] = position;
        e = default;
        return false;
    }

    // Drops the prefix every cursor has read.
    private void Release()
    {
        long lowest = _first + _events.Count;
        for (int i = 0; i < _positions.Count; i++)
        {
            lowest = Math.Min(lowest, _positions[i]);
        }

        int read = (int)(lowest - _first);
        if (read > 0)
        {
            _events.RemoveRange(0, read);
            _first = lowest;
        }
    }
}
