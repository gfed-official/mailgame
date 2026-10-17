namespace PerformativeMail.Sim.Core;

/// <summary>
/// An owner-filtered view of the RNG registry (ADR-0005 section 3). The composition root hands each system the
/// view for its own <see cref="SystemId"/>; holding the view is the ownership check. Throwing receives a view
/// that exposes nothing. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// IRngStreams view = registry.ViewFor(SystemId.MailDestinations);
/// RngStream mail = view.Create(in RngNames.Mail, 1);       // owned: succeeds
/// uint roll = mail.NextInt(10);
/// view.Create(in RngNames.Waves, 0);                       // not owned: asserts (Debug) / refused and counted (Release)
/// </code>
/// </example>
public interface IRngStreams
{
    /// <summary>
    /// Creates (re-seeds in place) the stream for (<paramref name="name"/>, <paramref name="index"/>) and returns its
    /// one handle. A second call for the same key returns the same object, reset to the start of its sequence
    /// with <see cref="RngStream.Draws"/> 0. Allocates nothing.
    /// </summary>
    /// <param name="name">A name from <see cref="RngNames"/> that this view's owner owns.</param>
    /// <param name="index">The stream index, within the name's range.</param>
    /// <returns>The stream handle. For a refused request (Release) a detached stream that is not part of the world's state.</returns>
    /// <exception cref="Primitives.SimAssertException">Under SIM_ASSERT: the name is not owned by this view, or the index is out of range.</exception>
    RngStream Create(in StreamName name, uint index);
}
