using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Presentation;

/// <summary>
/// One decoded event as Presentation sees it (ADR-0016 section 5): the host tick of the batch it arrived in and the entry the
/// client adapter decoded, the same entry <c>MirrorDrain</c> applies at the next client tick. Ordered by
/// (<see cref="SourceTick"/>, <see cref="Ordinal"/>); HUD's <c>opId</c> is <see cref="Ordinal"/>.
/// </summary>
/// <example><code>if (cursor.TryRead(out PresentedEvent e) &amp;&amp; e.Kind == FixtureEvent) { uint v = e.PayloadAs&lt;uint&gt;(); }</code></example>
public readonly struct PresentedEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="sourceTick">The host tick of the batch.</param>
    /// <param name="entry">The decoded entry (copied).</param>
    public PresentedEvent(uint sourceTick, in EventEntry entry)
    {
        SourceTick = sourceTick;
        Entry = entry;
    }

    /// <summary>The host tick of the batch the event arrived in.</summary>
    public uint SourceTick { get; }

    /// <summary>The decoded entry (class, audience and call sequence are not on the wire and are zero).</summary>
    public EventEntry Entry { get; }

    /// <summary>The event kind.</summary>
    public EventKind Kind => Entry.Kind;

    /// <summary>The record's index in its batch.</summary>
    public uint Ordinal => Entry.Ordinal;

    /// <summary>Reads the payload as the kind's payload struct (see <see cref="EventEntry.PayloadAs{T}"/>).</summary>
    /// <typeparam name="T">The payload struct.</typeparam>
    /// <returns>The payload.</returns>
    public T PayloadAs<T>()
        where T : unmanaged => Entry.PayloadAs<T>();
}
