using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Mirror;

/// <summary>
/// The write side of the <c>ClientLayer</c> inbox (ADR-0011 section 2, ADR-0003 section 6): what the client adapter hands
/// decoded batches and replies to. Nothing added here is applied to anything until the <c>MirrorDrain</c> step runs at the next
/// client tick (<see cref="MirrorDrainStep"/>); the adapter never touches the Sim. Storage is preallocated: an add that does
/// not fit returns false and the adapter counts it. Not thread-safe; main thread only.
/// </summary>
/// <example>
/// <code>
/// if (inbox.FreeCount >= 1) { inbox.TryAddReply(tick, in reply); }
/// </code>
/// </example>
public interface IClientInbox
{
    /// <summary>Records that can still be added before the inbox is full.</summary>
    int FreeCount { get; }

    /// <summary>Appends a decoded event record of batch <paramref name="tick"/>, after everything already queued.</summary>
    /// <param name="tick">The host tick of the batch the entry arrived in.</param>
    /// <param name="entry">The decoded entry (copied).</param>
    /// <returns>False (nothing added) when the inbox is full.</returns>
    bool TryAddEntry(uint tick, in EventEntry entry);

    /// <summary>Appends a reply, after everything already queued.</summary>
    /// <param name="tick">The host tick of the batch the reply arrived in; for a bare reply, the last batch tick the adapter saw (0 before any).</param>
    /// <param name="reply">The reply (copied).</param>
    /// <returns>False (nothing added) when the inbox is full.</returns>
    bool TryAddReply(uint tick, in Reply reply);
}

/// <summary>
/// One inbox record: the host tick it arrived under and its entry. A reply is stored as an <see cref="EventKind.OpReply"/> entry
/// whose payload is the <see cref="Reply"/>, so events and replies keep one arrival order.
/// </summary>
/// <example><code>if (inbox.TryDequeue(out ClientInboxRecord r) &amp;&amp; r.IsReply) { Reply reply = r.Entry.PayloadAs&lt;Reply&gt;(); }</code></example>
public struct ClientInboxRecord
{
    /// <summary>The host tick of the batch (or last seen batch, for a bare reply).</summary>
    public uint Tick;

    /// <summary>The entry. <c>Ordinal</c> is the record's index in its batch (0 for a reply record); class, audience and call sequence are not on the wire and are zero.</summary>
    public EventEntry Entry;

    /// <summary>True when <see cref="Entry"/> is an <see cref="EventKind.OpReply"/> carrying a <see cref="Reply"/>.</summary>
    public readonly bool IsReply => Entry.Kind == EventKind.OpReply;
}
