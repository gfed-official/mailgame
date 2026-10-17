using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Core.Mirror;

/// <summary>
/// A per-system client mirror's entry point for host data (ADR-0003 section 6): <see cref="MirrorDrainStep"/> hands it every
/// inbox record, in arrival order, at the client tick's <c>MirrorDrain</c> step. A consumer ignores kinds it does not own. At
/// M-1a only test consumers exist; feature epics register the real mirrors. Must not allocate and must not touch a <c>SimWorld</c>.
/// </summary>
/// <example>
/// <code>
/// sealed class ContainerMirror : IMirrorConsumer
/// {
///     public void OnEntry(uint tick, in EventEntry entry) { /* if (entry.Kind == ...) */ }
///     public void OnReply(uint tick, in Reply reply) { /* resolve pending queue */ }
/// }
/// </code>
/// </example>
public interface IMirrorConsumer
{
    /// <summary>A replicated event of the batch for host tick <paramref name="tick"/>.</summary>
    /// <param name="tick">The host tick of the batch.</param>
    /// <param name="entry">The decoded entry, valid only for this call.</param>
    void OnEntry(uint tick, in EventEntry entry);

    /// <summary>The reply to one of this client's requests (including adapter-generated <c>busy</c> and <c>bad_request</c>).</summary>
    /// <param name="tick">The host tick of the batch it arrived in (the last batch tick for a bare reply).</param>
    /// <param name="reply">The reply, valid only for this call.</param>
    void OnReply(uint tick, in Reply reply);
}
